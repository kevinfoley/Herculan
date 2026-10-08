import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import ghidra.app.cmd.function.ApplyFunctionSignatureCmd;
import ghidra.app.script.GhidraScript;
import ghidra.app.util.parser.FunctionSignatureParser;
import ghidra.program.model.address.Address;
import ghidra.program.model.data.CategoryPath;
import ghidra.program.model.data.DataType;
import ghidra.program.model.data.FunctionDefinitionDataType;
import ghidra.program.model.data.Pointer;
import ghidra.program.model.listing.CodeUnit;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.listing.Parameter;
import ghidra.program.model.symbol.SourceType;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolTable;
import java.io.FileReader;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Set;

// args[0] = path to the processed binary's known_symbols_<binary>.json (schema: README.md,
// "Known symbols").
//
// Applies confirmed address->meaning findings to the currently-processed program. Reads every
// entry whose "binary" field names the current program (isBinary: running this with -process
// "VSHELL.EXE" only touches VSHELL entries, and likewise for DBSIM, ES1DBSIM and ES1GO), and for
// each:
//   - type "function": renames the function at that address (Function.setName, USER_DEFINED) if
//     the entry has a "name", then always writes/refreshes a plate comment with the description.
//   - type "data": creates/refreshes a label at that address if the entry has a "name", then
//     always writes/refreshes a plate comment.
// An entry may also carry a "signature": a full C prototype, which is applied to the function as
// SourceType.USER_DEFINED so it survives as a human-verified prototype rather than blending into
// the decompiler's ANALYSIS-tier guesses. Signatures are functions-only and optional -- see the
// README.md "Known symbols" for the rule on when one may be recorded at all. A signature that
// fails to parse or apply is reported and skipped; it never aborts the rest of the run.
//
// Entries with no "name" (low-confidence findings) only get the plate comment -- never a
// rename/label, so a guess can never masquerade as a confirmed symbol in the database.
//
// Applying a signature REPLACES the whole parameter list, including data types this file says
// nothing about. known_structs.json owns parameter types -- it is what points a param_1 at
// SimObject* so the decompiler names field accesses -- and those types survive here: a
// parameter already typed with a pointer into category /ES2 is captured before the signature
// apply and restored after it, and a signature that disagrees is reported rather than silently
// winning. Without that, this script would quietly undo ES2ApplyStructures on every run, and
// the damage would show up only as a decompilation that had stopped naming fields.
//
// Idempotent: safe to re-run after the symbols file gains new entries or existing descriptions
// change -- renames/labels are skipped if already correct, and plate comments are always
// overwritten with the current JSON content rather than appended to.
public class ES2ApplySymbolNames extends GhidraScript {
    // Calling conventions that may appear in a known_symbols "signature" string.
    private static final String[] CONVENTIONS = {
        "__cdecl", "__stdcall", "__thiscall", "__fastcall", "__watcall" };

    @Override
    public void run() throws Exception {
        String[] scriptArgs = getScriptArgs();
        if (scriptArgs.length < 1) {
            println("Usage: ES2ApplySymbolNames <known_symbols_<binary>.json path>");
            return;
        }
        String jsonPath = scriptArgs[0];

        JsonObject root;
        try (FileReader fr = new FileReader(jsonPath)) {
            root = JsonParser.parseReader(fr).getAsJsonObject();
        }
        JsonArray entries = root.getAsJsonArray("entries");

        String progName = currentProgram.getName().toUpperCase();
        FunctionManager fm = currentProgram.getFunctionManager();
        SymbolTable st = currentProgram.getSymbolTable();
        Listing listing = currentProgram.getListing();

        int renamed = 0, signatured = 0, labeled = 0, commented = 0, skippedOtherBinary = 0, skippedNoTarget = 0, errors = 0, restored = 0;

        // One address, one entry. Two entries sharing an address make the plate comment depend on
        // file order (the later one wins outright), and if their names differ the run can never
        // reach a fixed point -- every pass renames the target twice, so renamed never falls to 0.
        Set<String> seenAddresses = new HashSet<>();
        int duplicates = 0;
        for (JsonElement el : entries) {
            JsonObject entry = el.getAsJsonObject();
            String key = entry.get("binary").getAsString().toUpperCase() + ":"
                + entry.get("address").getAsString().toLowerCase();
            if (!seenAddresses.add(key)) {
                println("ERROR: duplicate entry for " + key + " -- merge them into one.");
                duplicates++;
            }
        }
        if (duplicates > 0) {
            println("ES2ApplySymbolNames: aborting, " + duplicates + " duplicate address(es) in " + jsonPath);
            return;
        }

        for (JsonElement el : entries) {
            JsonObject entry = el.getAsJsonObject();
            String binary = entry.get("binary").getAsString();
            if (!isBinary(progName, binary)) {
                skippedOtherBinary++;
                continue;
            }

            String addrHex = entry.get("address").getAsString();
            String type = entry.get("type").getAsString();
            String confidence = entry.has("confidence") ? entry.get("confidence").getAsString() : "unknown";
            String description = entry.has("description") ? entry.get("description").getAsString() : "";
            String source = entry.has("source") ? entry.get("source").getAsString() : "";
            String name = entry.has("name") ? entry.get("name").getAsString() : null;
            String signature = entry.has("signature") ? entry.get("signature").getAsString() : null;

            Address addr;
            try {
                addr = currentProgram.getAddressFactory().getAddress(addrHex);
            } catch (Exception e) {
                println("ERROR: bad address '" + addrHex + "' (binary=" + binary + "): " + e.getMessage());
                errors++;
                continue;
            }

            StringBuilder comment = new StringBuilder();
            comment.append("[known_symbols] confidence=").append(confidence);
            if (name != null) {
                comment.append(" name=").append(name);
            }
            if (!description.isEmpty()) {
                comment.append("\n").append(description);
            }
            if (!source.isEmpty()) {
                comment.append("\nsource: ").append(source);
            }

            try {
                if ("function".equals(type)) {
                    Function f = fm.getFunctionAt(addr);
                    if (f == null) {
                        println("WARN: no function at " + addr + " (" + (name != null ? name : "comment-only") + ") -- skipping");
                        skippedNoTarget++;
                        continue;
                    }
                    if (name != null && !name.equals(f.getName())) {
                        f.setName(name, SourceType.USER_DEFINED);
                        renamed++;
                    }
                    if (signature != null) {
                        try {
                            // FunctionSignatureParser does not accept an inline calling convention:
                            // it reads "int __cdecl" as the return type and fails. Lift the
                            // convention out of the text and set it on the definition instead.
                            String conv = null;
                            String parseText = signature;
                            for (String c : CONVENTIONS) {
                                int idx = signature.indexOf(" " + c + " ");
                                if (idx >= 0) {
                                    conv = c;
                                    parseText = signature.substring(0, idx) + " "
                                        + signature.substring(idx + c.length() + 2);
                                    break;
                                }
                            }
                            FunctionSignatureParser parser =
                                new FunctionSignatureParser(currentProgram.getDataTypeManager(), null);
                            FunctionDefinitionDataType def = parser.parse(f.getSignature(), parseText);
                            if (conv != null) {
                                def.setCallingConvention(conv);
                            }
                            // preserveCallingConvention=false, or the convention just set is ignored
                            // and the function keeps the decompiler's (wrong) __stdcall guess.
                            // forceSetName=false: the rename above already handled the name.
                            Map<Integer, DataType> owned = capturePointersIntoES2(f);
                            ApplyFunctionSignatureCmd cmd = new ApplyFunctionSignatureCmd(
                                addr, def, SourceType.USER_DEFINED, false, false);
                            if (cmd.applyTo(currentProgram, monitor)) {
                                signatured++;
                                restored += restorePointersIntoES2(f, owned);
                            } else {
                                println("WARN: signature rejected at " + addr + ": " + cmd.getStatusMsg());
                            }
                        } catch (Exception e) {
                            println("ERROR: bad signature at " + addr + " (" + signature + "): "
                                + e.getMessage()
                                + " -- if it names a struct type, that type has to exist first:"
                                + " run ES2ApplyStructures before this script on a fresh database.");
                            errors++;
                        }
                    }
                    listing.setComment(addr, CodeUnit.PLATE_COMMENT, comment.toString());
                    commented++;
                } else if ("data".equals(type)) {
                    if (signature != null) {
                        println("WARN: entry at " + addr + " is type 'data' but carries a signature -- ignored");
                    }
                    if (name != null) {
                        Symbol existing = st.getPrimarySymbol(addr);
                        if (existing == null || !name.equals(existing.getName())) {
                            Symbol created = st.createLabel(addr, name, SourceType.USER_DEFINED);
                            created.setPrimary();
                            labeled++;
                        }
                    }
                    listing.setComment(addr, CodeUnit.PLATE_COMMENT, comment.toString());
                    commented++;
                } else {
                    println("WARN: unknown entry type '" + type + "' at " + addr);
                    skippedNoTarget++;
                }
            } catch (Exception e) {
                println("ERROR applying entry at " + addr + " (binary=" + binary + "): " + e.getMessage());
                errors++;
            }
        }

        println(String.format(
            "ES2ApplySymbolNames [%s]: renamed=%d signatured=%d labeled=%d commented=%d skipped(other binary)=%d skipped(no target)=%d structParamsKept=%d errors=%d",
            progName, renamed, signatured, labeled, commented, skippedOtherBinary, skippedNoTarget, restored, errors));
    }

    // A parameter typed with a pointer into Ghidra category /ES2 belongs to known_structs.json, not
    // to this file. Capture those before a signature apply so they can be put back after it. The
    // test is the category rather than SourceType.USER_DEFINED, so it says WHY the type is
    // preserved and cannot be tripped by some later script that commits parameters that way.
    private Map<Integer, DataType> capturePointersIntoES2(Function f) {
        Map<Integer, DataType> owned = new LinkedHashMap<>();
        Parameter[] params = f.getParameters();
        for (int i = 0; i < params.length; i++) {
            if (pointsIntoES2(params[i].getDataType())) {
                owned.put(i, params[i].getDataType());
            }
        }
        return owned;
    }

    private int restorePointersIntoES2(Function f, Map<Integer, DataType> owned) {
        int restored = 0;
        for (Map.Entry<Integer, DataType> e : owned.entrySet()) {
            int index = e.getKey();
            if (index >= f.getParameterCount()) {
                println("WARN: " + f.getName() + " -- the signature dropped parameter " + index
                    + ", which known_structs.json had typed " + e.getValue().getDisplayName()
                    + ". The two files disagree about this function's arity; fix one.");
                continue;
            }
            Parameter p = f.getParameter(index);
            // Compared by what the pointer ultimately points AT, not with isEquivalent: a type the
            // signature parser resolved and the same type held on a parameter are separate
            // DataType instances that isEquivalent reports as different, which would warn about a
            // signature that in fact agrees.
            if (pointeePath(e.getValue()).equals(pointeePath(p.getDataType()))) {
                continue;
            }
            println("WARN: " + f.getName() + " -- the signature would retype parameter " + index
                + " from " + e.getValue().getDisplayName() + " to "
                + p.getDataType().getDisplayName() + ". Keeping known_structs.json's; drop one of"
                + " the two so they stop disagreeing.");
            try {
                p.setDataType(e.getValue(), SourceType.USER_DEFINED);
                restored++;
            } catch (Exception ex) {
                println("ERROR restoring " + f.getName() + " parameter " + index + ": "
                    + ex.getMessage());
            }
        }
        return restored;
    }

    // "<levels of indirection>:<pointee category path and name>", or "" for a non-pointer.
    // An entry's "binary" is the program's name, less ".EXE" for ES2's (DBSIM -> DBSIM.EXE; ES1's
    // programs are named ES1DBSIM and ES1GO). An exact match, so DBSIM never matches ES1DBSIM.
    private static boolean isBinary(String progName, String binary) {
        String b = binary.toUpperCase();
        return progName.equals(b) || progName.equals(b + ".EXE");
    }

    private static String pointeePath(DataType dt) {
        int depth = 0;
        DataType target = dt;
        while (target instanceof Pointer) {
            target = ((Pointer) target).getDataType();
            depth++;
        }
        if (depth == 0 || target == null) {
            return "";
        }
        return depth + ":" + target.getPathName();
    }

    private static boolean pointsIntoES2(DataType dt) {
        if (!(dt instanceof Pointer)) {
            return false;
        }
        DataType target = ((Pointer) dt).getDataType();
        while (target instanceof Pointer) {
            target = ((Pointer) target).getDataType();
        }
        return target != null && target.getCategoryPath() != null
            && target.getCategoryPath().isAncestorOrSelf(new CategoryPath("/ES2"));
    }
}
