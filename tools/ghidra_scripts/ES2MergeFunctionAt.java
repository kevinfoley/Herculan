import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.CodeUnit;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.pcode.HighFunctionDBUtil;
import ghidra.program.model.pcode.HighFunctionDBUtil.ReturnCommitOption;
import ghidra.program.model.symbol.SourceType;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolTable;
import java.io.PrintWriter;
import java.io.FileWriter;
import java.util.ArrayList;
import java.util.List;

// Repairs a function whose entry Ghidra placed past the real prologue, leaving the prologue as a
// tiny separate function. Removes every function whose entry lies in [trueEntry, trueEntry+length),
// clears the code units there, then disassembles and creates one function at trueEntry.
// Removing a function turns its non-default name into a plain label, and plate comments are not
// code units, so a removed late entry would keep its old name and comment. Both are deleted at
// every removed entry other than trueEntry. The new function starts with no parameters, so the
// script commits the decompiler's prototype for it at ANALYSIS, the tier ES2CommitAllParams gives
// every other function; ES2ApplyStructures and ES2ApplySymbolNames then type it as before.
// args[0] = trueEntry (hex), args[1] = byte length to sweep (decimal), or "auto": through the end
// of the last body of any function entered in [trueEntry, trueEntry+16), which covers a prologue
// of up to 16 bytes and never reaches a function entered after it. "auto" also repairs an early
// start: a function entered up to 16 bytes before trueEntry whose body contains it (Ghidra began
// it on the fill bytes before the prologue) is removed and the sweep starts at its entry.
// args[2] = output path.
public class ES2MergeFunctionAt extends GhidraScript {
    private static final int AUTO_WINDOW = 16;

    @Override
    public void run() throws Exception {
        Address entry = currentProgram.getAddressFactory().getAddress(getScriptArgs()[0]);
        String lengthArg = getScriptArgs()[1];
        String outPath = getScriptArgs()[2];

        try (PrintWriter pw = new PrintWriter(new FileWriter(outPath))) {
            FunctionManager fm = currentProgram.getFunctionManager();
            Address end;
            Address start = entry;
            if (lengthArg.equals("auto")) {
                end = null;
                Function early = fm.getFunctionContaining(entry);
                if (early != null && early.getEntryPoint().compareTo(entry) < 0
                        && entry.subtract(early.getEntryPoint()) < AUTO_WINDOW) {
                    start = early.getEntryPoint();
                    end = early.getBody().getMaxAddress();
                    pw.println("early start " + early.getName() + " @ " + start);
                }
                Address windowEnd = entry.add(AUTO_WINDOW - 1);
                FunctionIterator w = fm.getFunctions(entry, true);
                while (w.hasNext()) {
                    Function f = w.next();
                    if (f.getEntryPoint().compareTo(windowEnd) > 0) {
                        break;
                    }
                    Address last = f.getBody().getMaxAddress();
                    if (end == null || last.compareTo(end) > 0) {
                        end = last;
                    }
                }
                if (end == null) {
                    pw.println("FAILED: no function entered within " + AUTO_WINDOW + " bytes of " + entry);
                    println("wrote merge result to " + outPath);
                    return;
                }
                // A non-contiguous body can reach past a neighbour; never sweep one away.
                Function beyond = getFunctionAfter(windowEnd);
                if (beyond != null && beyond.getEntryPoint().compareTo(end) <= 0) {
                    pw.println("FAILED: auto range " + entry + ".." + end + " would sweep "
                        + beyond.getName() + " @ " + beyond.getEntryPoint() + "; pass an explicit length");
                    println("wrote merge result to " + outPath);
                    return;
                }
                pw.println("auto length " + (end.subtract(start) + 1) + " (" + start + ".." + end + ")");
            } else {
                end = entry.add(Integer.parseInt(lengthArg) - 1);
            }
            SymbolTable st = currentProgram.getSymbolTable();
            Listing listing = currentProgram.getListing();
            List<Address> doomed = new ArrayList<>();
            FunctionIterator it = fm.getFunctions(true);
            while (it.hasNext()) {
                Function f = it.next();
                Address e = f.getEntryPoint();
                if (e.compareTo(start) >= 0 && e.compareTo(end) <= 0) {
                    pw.println("removing " + f.getName() + " @ " + e + " body=" + f.getBody());
                    doomed.add(e);
                }
            }
            for (Address a : doomed) {
                fm.removeFunction(a);
            }

            for (Address a : doomed) {
                if (a.equals(entry)) {
                    continue;
                }
                for (Symbol s : st.getSymbols(a)) {
                    if (s.getSource() != SourceType.DEFAULT) {
                        pw.println("deleting leftover label " + s.getName() + " @ " + a);
                        s.delete();
                    }
                }
                if (listing.getComment(CodeUnit.PLATE_COMMENT, a) != null) {
                    pw.println("deleting leftover plate comment @ " + a);
                    listing.setComment(a, CodeUnit.PLATE_COMMENT, null);
                }
            }

            listing.clearCodeUnits(start, end, false);
            disassemble(entry);
            createFunction(entry, null);

            Function made = fm.getFunctionAt(entry);
            pw.println(made == null
                ? "FAILED: no function at " + entry
                : "created " + made.getName() + " @ " + entry + " body=" + made.getBody());
            if (made != null && !made.getBody().contains(entry, end)) {
                pw.println("SHORT: the new body does not cover " + entry + ".." + end);
            }
            if (made != null) {
                DecompInterface decomp = new DecompInterface();
                decomp.openProgram(currentProgram);
                DecompileResults res = decomp.decompileFunction(made, 60, monitor);
                if (res != null && res.getHighFunction() != null) {
                    HighFunctionDBUtil.commitParamsToDatabase(res.getHighFunction(), true,
                        ReturnCommitOption.COMMIT, SourceType.ANALYSIS);
                    pw.println("committed prototype " + made.getPrototypeString(false, false));
                } else {
                    pw.println("FAILED: decompile of " + made.getName() + " for its prototype");
                }
                decomp.dispose();
            }
        }
        println("wrote merge result to " + outPath);
    }
}
