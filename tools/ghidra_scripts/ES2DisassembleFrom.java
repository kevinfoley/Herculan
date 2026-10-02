import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import java.io.FileWriter;
import java.io.PrintWriter;

// Disassembles undefined bytes by following flow from each start address, then lists every
// instruction in [lo, hi). For hand-written assembly that auto-analysis left as data because it is
// reached only through jump tables or computed jumps, which ES2DisasmRange cannot show since it
// lists only what is already disassembled. Writes to the program: run without -readOnly to keep
// the new code units, with it to look only.
// args[0] = output path, args[1] = lo, args[2] = hi, args[3..] = start addresses (hex).
public class ES2DisassembleFrom extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] a = getScriptArgs();
        if (a.length < 4) {
            println("SCRIPT-ERROR usage: out lo hi start [start...]");
            return;
        }

        for (int i = 3; i < a.length; i++) {
            disassemble(toAddr(a[i]));
        }

        Address lo = toAddr(a[1]);
        Address hi = toAddr(a[2]);
        try (PrintWriter pw = new PrintWriter(new FileWriter(a[0]))) {
            InstructionIterator it = currentProgram.getListing().getInstructions(lo, true);
            while (it.hasNext()) {
                Instruction ins = it.next();
                if (ins.getAddress().compareTo(hi) >= 0) {
                    break;
                }

                StringBuilder hex = new StringBuilder();
                for (byte b : ins.getBytes()) {
                    hex.append(String.format("%02x", b));
                }

                pw.println(ins.getAddress() + "   " + String.format("%-16s", hex) + " " + ins);
            }
        }
    }
}
