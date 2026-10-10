import ghidra.app.cmd.function.CreateFunctionCmd;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressRange;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.data.ArrayDataType;
import ghidra.program.model.data.ByteDataType;
import ghidra.program.model.data.PointerDataType;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.pcode.JumpTable;
import ghidra.program.model.symbol.RefType;
import ghidra.program.model.symbol.SourceType;
import java.io.FileWriter;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.LinkedHashSet;
import java.util.Set;

// Extends a function over code its body leaves out, for the two shapes auto-analysis gets wrong:
//
//   switch:JMP:T1+T2+...  a jump table Ghidra did not recover. Adds a COMPUTED_JUMP reference from the
//                         indirect JMP to each target, disassembles the targets, writes a decompiler
//                         jump-table override (the same as Ghidra's SwitchOverride script) and re-runs
//                         CreateFunctionCmd.fixupFunctionBody on the function holding the JMP.
//   tail:ENTRY:LO:HI      code in [LO, HI) reached only through a computed jump (JMP reg) into an
//                         unrolled run. Disassembles from LO by flow and adds every instruction in
//                         [LO, HI) to ENTRY's body; fixupFunctionBody would drop it again, since the
//                         jump has no target list, so it is not run for these.
//   bytes:ADDR:N          types N bytes at ADDR as a byte array (a switch's index table), clearing
//                         whatever data units are there first.
//   ptrs:ADDR:N           types N dwords at ADDR as an array of pointers (a switch's target table).
//
// Items run in the order given. Then each function touched is reported (body ranges) and decompiled
// into the output file. Writes to the program: run with -readOnly first to see the result unsaved.
// args[0] = output path, args[1..] = items.
public class ES2RepairFunctionBody extends GhidraScript {
    private PrintWriter pw;
    private final Set<Function> touched = new LinkedHashSet<>();

    @Override
    public void run() throws Exception {
        String[] a = getScriptArgs();
        if (a.length < 2) {
            println("SCRIPT-ERROR usage: out item [item...]");
            return;
        }

        try (PrintWriter w = new PrintWriter(new FileWriter(a[0]))) {
            pw = w;
            for (int i = 1; i < a.length; i++) {
                String[] p = a[i].split(":");
                switch (p[0]) {
                    case "switch" -> repairSwitch(toAddr(p[1]), p[2].split("[+,]"));
                    case "tail" -> addTail(toAddr(p[1]), toAddr(p[2]), toAddr(p[3]));
                    case "bytes" -> typeArray(toAddr(p[1]), Integer.parseInt(p[2]), false);
                    case "ptrs" -> typeArray(toAddr(p[1]), Integer.parseInt(p[2]), true);
                    default -> report("ERROR unknown item " + a[i]);
                }
            }

            DecompInterface ifc = new DecompInterface();
            ifc.openProgram(currentProgram);
            for (Function f : touched) {
                StringBuilder ranges = new StringBuilder();
                for (AddressRange r : f.getBody()) {
                    ranges.append(' ').append(r.getMinAddress()).append('-').append(r.getMaxAddress());
                }
                report("BODY " + f.getName() + " @ " + f.getEntryPoint() + " "
                    + f.getBody().getNumAddresses() + " bytes:" + ranges);
                DecompileResults res = ifc.decompileFunction(f, 120, monitor);
                pw.println("// ---- decompile " + f.getName());
                pw.println(res.decompileCompleted() ? res.getDecompiledFunction().getC()
                    : "// FAILED: " + res.getErrorMessage());
            }
            ifc.dispose();
        }
    }

    private void report(String line) {
        println(line);
        pw.println(line);
    }

    private void repairSwitch(Address jmp, String[] targets) throws Exception {
        Instruction ins = getInstructionAt(jmp);
        Function f = getFunctionContaining(jmp);
        if (ins == null || f == null) {
            report("ERROR switch " + jmp + ": no instruction or no containing function");
            return;
        }

        ArrayList<Address> dests = new ArrayList<>();
        for (String t : targets) {
            Address d = toAddr(t);
            dests.add(d);
            ins.addOperandReference(0, d, RefType.COMPUTED_JUMP, SourceType.USER_DEFINED);
            if (getInstructionAt(d) == null) {
                boolean ok = disassemble(d);
                report((ok ? "disassembled " : "FAILED to disassemble ") + d);
            }
        }

        new JumpTable(jmp, dests, true, 0).writeOverride(f);
        long before = f.getBody().getNumAddresses();
        boolean ok = CreateFunctionCmd.fixupFunctionBody(currentProgram, f, monitor);
        report("switch " + jmp + " in " + f.getName() + ": " + dests.size() + " targets, override written, "
            + "body " + before + " -> " + f.getBody().getNumAddresses() + (ok ? "" : " (fixup FAILED)"));
        touched.add(f);
    }

    private void addTail(Address entry, Address lo, Address hi) throws Exception {
        Function f = getFunctionAt(entry);
        if (f == null) {
            report("ERROR tail: no function at " + entry);
            return;
        }

        if (getInstructionAt(lo) == null) {
            boolean ok = disassemble(lo);
            report((ok ? "disassembled from " : "FAILED to disassemble from ") + lo);
        }

        AddressSet add = new AddressSet();
        long gaps = 0;
        Address expect = lo;
        InstructionIterator it = currentProgram.getListing().getInstructions(lo, true);
        while (it.hasNext()) {
            Instruction i = it.next();
            if (i.getAddress().compareTo(hi) >= 0) {
                break;
            }

            if (!i.getAddress().equals(expect)) {
                gaps += i.getAddress().subtract(expect);
            }

            add.add(i.getAddress(), i.getMaxAddress());
            expect = i.getMaxAddress().next();
        }

        if (expect.compareTo(hi) < 0) {
            gaps += hi.subtract(expect);
        }

        Function other = null;
        for (AddressRange r : add) {
            Iterator<Function> it2 = currentProgram.getFunctionManager().getFunctionsOverlapping(new AddressSet(r));
            while (it2.hasNext()) {
                Function g = it2.next();
                if (!g.equals(f)) {
                    other = g;
                }
            }
        }

        if (other != null) {
            report("ERROR tail " + lo + "-" + hi + " overlaps " + other.getName() + " @ " + other.getEntryPoint());
            return;
        }

        long before = f.getBody().getNumAddresses();
        f.setBody(f.getBody().union(add));
        report("tail " + lo + "-" + hi + " added to " + f.getName() + ": body " + before + " -> "
            + f.getBody().getNumAddresses() + ", " + gaps + " byte(s) in the range not code");
        touched.add(f);
    }

    private void typeArray(Address at, int n, boolean pointers) throws Exception {
        int len = pointers ? n * 4 : n;
        Address end = at.add(len - 1);
        if (getInstructionContaining(at) != null || getInstructionContaining(end) != null) {
            report("ERROR " + at + ": the table range holds code");
            return;
        }

        clearListing(at, end);
        createData(at, pointers ? new ArrayDataType(new PointerDataType(), n, 4)
            : new ArrayDataType(ByteDataType.dataType, n, 1));
        report("typed " + at + " as " + (pointers ? "pointer[" : "byte[") + n + "]");
    }
}
