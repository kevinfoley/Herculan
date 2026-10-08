// Brings every function Ghidra starts on the 0x90 padding before a `55 8B EC` prologue, in an ES1
// X-32 image, to the form Herculan/docs/herculan/plan-es1-support.md (1.2) sets: the function starts
// at its prologue, and the padding entry, which calls and vtable slots also enter, is a thunk of it
// whose body is the padding alone. The padding rule is retail_binaries.Image.entries_of's: a run of
// 0x90 bytes, at most 16, ending at the prologue. Two shapes, as es2_late_entries.py sorts them:
//   PADDING  one function from the padding through the prologue: it is removed, a function is
//            created at the prologue, and a thunk of it over the padding.
//   SPLIT    a function of the padding alone and another at the prologue: the first becomes a thunk.
// A padding function with a non-default name or a plate comment, or a body other than the expected
// one, is reported and left alone. A function created at a prologue gets the decompiler's prototype
// at ANALYSIS when the removed one had a committed prototype, as ES2MergeFunctionAt does.
// Run -readOnly first: the changes are made and checked in memory and then discarded.
// args[0] = output path; args[1] (optional) = `+`-separated function addresses to decompile after
// the repair, as a check that calls through the padding read as calls to the function.
//@category ES1
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.address.AddressSetView;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.pcode.HighFunctionDBUtil;
import ghidra.program.model.pcode.HighFunctionDBUtil.ReturnCommitOption;
import ghidra.program.model.symbol.SourceType;
import java.io.FileWriter;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.List;

public class ES1PaddingThunks extends GhidraScript {
    private static final int MAX_PADDING = 16;

    private Memory mem;

    private int u8(Address a) {
        try {
            return mem.getByte(a) & 0xff;
        } catch (Exception e) {
            return -1;
        }
    }

    /** The prologue a padding run starting at `a` falls through to, or null. */
    private Address prologueAfter(Address a) {
        Address p = a;
        while (u8(p) == 0x90 && p.subtract(a) < MAX_PADDING) {
            p = p.add(1);
        }
        if (p.equals(a) || u8(p) != 0x55 || u8(p.add(1)) != 0x8b || u8(p.add(2)) != 0xec) {
            return null;
        }
        return p;
    }

    private DecompInterface decomp;

    private void commitPrototype(Function f, PrintWriter pw) {
        DecompileResults res = decomp.decompileFunction(f, 60, monitor);
        if (res != null && res.getHighFunction() != null) {
            try {
                HighFunctionDBUtil.commitParamsToDatabase(res.getHighFunction(), true,
                    ReturnCommitOption.COMMIT, SourceType.ANALYSIS);
                return;
            } catch (Exception e) {
                pw.println("  prototype commit failed: " + e.getMessage());
            }
        }
        pw.println("  FAILED: decompile of " + f.getEntryPoint() + " for its prototype");
    }

    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        mem = currentProgram.getMemory();
        FunctionManager fm = currentProgram.getFunctionManager();
        decomp = new DecompInterface();
        decomp.openProgram(currentProgram);

        List<Function> candidates = new ArrayList<>();
        FunctionIterator it = fm.getFunctions(true);
        while (it.hasNext()) {
            Function f = it.next();
            if (!f.isThunk() && prologueAfter(f.getEntryPoint()) != null) {
                candidates.add(f);
            }
        }

        int before = fm.getFunctionCount();
        int padding = 0, split = 0, skipped = 0, bodyDiffers = 0;
        try (PrintWriter pw = new PrintWriter(new FileWriter(args[0]))) {
            pw.println(currentProgram.getName() + ": " + candidates.size() + " functions entered on padding");
            for (Function f : candidates) {
                Address pad = f.getEntryPoint();
                Address pro = prologueAfter(pad);
                AddressSet padBody = new AddressSet(pad, pro.subtract(1));
                AddressSetView body = f.getBody();
                String tag = String.format("%s pad %s pro %s", f.getName(), pad, pro);

                if (f.getSymbol().getSource() != SourceType.DEFAULT
                        || getPlateComment(pad) != null) {
                    pw.println("SKIP " + tag + ": named or commented");
                    skipped++;
                    continue;
                }
                for (Address a = pad.add(1); a.compareTo(pro) < 0; a = a.add(1)) {
                    if (fm.getFunctionAt(a) != null) {
                        pw.println("SKIP " + tag + ": another function at " + a);
                        skipped++;
                        f = null;
                        break;
                    }
                }
                if (f == null) {
                    continue;
                }

                Function target = fm.getFunctionAt(pro);
                if (target != null) {
                    // SPLIT: the padding function must be exactly the padding.
                    if (!body.equals(padBody)) {
                        pw.println("SKIP " + tag + ": SPLIT but body " + body + " is not the padding");
                        skipped++;
                        continue;
                    }
                    f.setThunkedFunction(target);
                    split++;
                    pw.println("SPLIT   " + tag + " -> thunk of " + target.getName());
                } else {
                    // PADDING: the function runs from the padding through the prologue.
                    if (!body.contains(pro)) {
                        pw.println("SKIP " + tag + ": body " + body + " does not hold the prologue");
                        skipped++;
                        continue;
                    }
                    boolean committed = f.getSignatureSource() != SourceType.DEFAULT;
                    AddressSet expected = new AddressSet(body);
                    expected.delete(padBody);
                    fm.removeFunction(pad);
                    target = createFunction(pro, null);
                    if (target == null) {
                        pw.println("FAILED  " + tag + ": no function created at the prologue");
                        skipped++;
                        continue;
                    }
                    if (!target.getBody().equals(expected)) {
                        bodyDiffers++;
                        pw.println("  BODY " + pro + ": was " + expected + ", now " + target.getBody());
                    }
                    if (committed) {
                        commitPrototype(target, pw);
                    }
                    fm.createThunkFunction(null, null, pad, padBody, target, SourceType.DEFAULT);
                    padding++;
                    pw.println("PADDING " + tag + " -> " + target.getName() + " at the prologue, thunk over "
                        + padBody + (committed ? ", prototype committed" : ""));
                }

                Function thunk = fm.getFunctionAt(pad);
                if (thunk == null || !thunk.isThunk() || !target.equals(thunk.getThunkedFunction(false))
                        || !thunk.getBody().equals(padBody)) {
                    pw.println("  CHECK FAILED at " + pad + ": " + (thunk == null ? "no function"
                        : "thunk=" + thunk.isThunk() + " body=" + thunk.getBody()));
                }
            }
            pw.println(String.format("PADDING %d, SPLIT %d, skipped %d, bodies changed %d; functions %d -> %d",
                padding, split, skipped, bodyDiffers, before, fm.getFunctionCount()));

            if (args.length > 1) {
                for (String s : args[1].split("[+,]")) {
                    Function f = fm.getFunctionAt(toAddr(s));
                    if (f == null) {
                        pw.println("\n### " + s + ": no function");
                        continue;
                    }
                    DecompileResults res = decomp.decompileFunction(f, 60, monitor);
                    pw.println("\n### " + s);
                    pw.println(res != null && res.getDecompiledFunction() != null
                        ? res.getDecompiledFunction().getC() : "decompile failed");
                }
            }
        }
        decomp.dispose();
        println(String.format("ES1PaddingThunks: PADDING %d, SPLIT %d, skipped %d -> %s",
            padding, split, skipped, args[0]));
    }
}
