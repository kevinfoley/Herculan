import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.AddressRange;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import java.io.PrintWriter;
import java.io.FileWriter;

/**
 * Every function as address, name, body byte count and the body's address ranges, tab-separated.
 * The ranges are half-open (start-end, end one past the last byte), comma-separated, ascending. A body
 * Ghidra built from several ranges -- an inline jump table or other data between its instructions, a
 * chunk placed elsewhere -- has more than one, and its byte count is smaller than its span.
 */
public class ES2ListFunctions extends GhidraScript {
    @Override
    public void run() throws Exception {
        String outPath = getScriptArgs()[0];
        int count = 0;
        try (PrintWriter pw = new PrintWriter(new FileWriter(outPath))) {
            FunctionIterator it = currentProgram.getFunctionManager().getFunctions(true);
            while (it.hasNext()) {
                Function f = it.next();
                long size = f.getBody().getNumAddresses();
                StringBuilder ranges = new StringBuilder();
                for (AddressRange r : f.getBody().getAddressRanges(true)) {
                    if (ranges.length() > 0) {
                        ranges.append(',');
                    }
                    ranges.append(String.format("%08x-%08x", r.getMinAddress().getOffset(),
                            r.getMaxAddress().getOffset() + 1));
                }
                pw.printf("%s\t%s\t%d\t%s%n", f.getEntryPoint(), f.getName(), size, ranges);
                count++;
            }
        }
        println("SCRIPT-OK wrote " + count + " functions to " + outPath);
    }
}
