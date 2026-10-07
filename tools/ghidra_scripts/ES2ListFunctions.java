import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import java.io.PrintWriter;
import java.io.FileWriter;

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
                pw.printf("%s\t%s\t%d%n", f.getEntryPoint(), f.getName(), size);
                count++;
            }
        }
        println("SCRIPT-OK wrote " + count + " functions to " + outPath);
    }
}
