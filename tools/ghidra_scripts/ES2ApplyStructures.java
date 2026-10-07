import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.data.ArrayDataType;
import ghidra.program.model.data.ByteDataType;
import ghidra.program.model.data.CategoryPath;
import ghidra.program.model.data.CharDataType;
import ghidra.program.model.data.DataType;
import ghidra.program.model.data.DataTypeComponent;
import ghidra.program.model.data.DataTypeConflictHandler;
import ghidra.program.model.data.DataTypeManager;
import ghidra.program.model.data.IntegerDataType;
import ghidra.program.model.data.PointerDataType;
import ghidra.program.model.data.ShortDataType;
import ghidra.program.model.data.Structure;
import ghidra.program.model.data.StructureDataType;
import ghidra.program.model.data.Undefined1DataType;
import ghidra.program.model.data.Undefined2DataType;
import ghidra.program.model.data.Undefined4DataType;
import ghidra.program.model.data.UnsignedIntegerDataType;
import ghidra.program.model.data.UnsignedShortDataType;
import ghidra.program.model.data.VoidDataType;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.listing.Parameter;
import ghidra.program.model.pcode.HighFunctionDBUtil;
import ghidra.program.model.pcode.HighFunctionDBUtil.ReturnCommitOption;
import ghidra.program.model.symbol.SourceType;
import java.io.FileReader;
import java.util.HashMap;
import java.util.Map;

// args[0] = path to known_structs.json (see that file's _readme for the schema).
//
// Gives DBSIM's simulation objects a shape, and then puts that shape where the decompiler will use
// it. For each definition in the JSON, reading only those whose "binary" is a case-insensitive
// substring of the current program's name:
//   - creates a StructureDataType under category /ES2 of exactly the declared size, so it starts
//     out undefined-filled;
//   - places each field with replaceAtOffset, which overwrites undefined bytes in place. NOT add()
//     -- that appends sequentially, which is right for a vtable and wrong for a sparse struct --
//     and NOT insertAtOffset(), which shifts everything after it and would silently move every
//     field already placed;
//   - then types the listed function parameters with a pointer to it.
//
// A field's declared width is CHECKED against the resolved data type's own length rather than
// trusted. The two can only disagree if the JSON is wrong, and a wrong width is the failure mode
// worth spending a check on: it makes the decompiler produce plausible, wrong output in every
// function that touches the object, where a wrong name is merely a nuisance. Overlaps and
// past-the-end fields are refused for the same reason.
//
// Defining the struct renders nothing on its own. The payoff is in "applications": a parameter
// typed SimObject* is what turns *(char *)(param_2 + 0x99) into param_2->destroyed, and -- given
// the vtable types ES2ApplyVtables built -- an indirect call through field 0 into a named method.
//
// An "inline" field copies a base struct's components into this one instead of embedding it, so
// that a field of the base -- in practice its vtable pointer -- can be retyped: the explicit fields
// are placed first, and an inlined component is placed where its bytes are free, dropped where an
// explicit field covers all of them, and opened up into its own components where one covers part.
//
// A listed function whose stored prototype is too short for the parameter -- most often none, the
// analyser having never committed one -- gets the decompiler's parameters committed at ANALYSIS,
// the tier every other prototype in the database has, and is then typed.
//
// Idempotent: data types are resolved with REPLACE_HANDLER and a parameter already carrying the
// right type is left alone. Safe to re-run after known_structs.json grows.
public class ES2ApplyStructures extends GhidraScript {
    private static final byte FREE = 0, EXPLICIT = 1, INLINED = 2;
    private DataTypeManager dtm;
    private final Map<String, DataType> defined = new HashMap<>();
    private int errors = 0;
    private DecompInterface decomp;

    @Override
    public void run() throws Exception {
        String[] scriptArgs = getScriptArgs();
        if (scriptArgs.length < 1) {
            println("Usage: ES2ApplyStructures <known_structs.json path>");
            return;
        }

        JsonObject root;
        try (FileReader fr = new FileReader(scriptArgs[0])) {
            root = JsonParser.parseReader(fr).getAsJsonObject();
        }

        String progName = currentProgram.getName().toUpperCase();
        dtm = currentProgram.getDataTypeManager();

        int typesBuilt = 0, fieldsPlaced = 0, skippedOtherBinary = 0;

        for (JsonElement el : root.getAsJsonArray("structs")) {
            JsonObject def = el.getAsJsonObject();
            String name = def.get("name").getAsString();
            if (!progName.contains(def.get("binary").getAsString().toUpperCase())) {
                skippedOtherBinary++;
                continue;
            }

            int size = def.get("size").getAsInt();
            StructureDataType struct =
                new StructureDataType(new CategoryPath("/ES2"), name, size, dtm);

            StringBuilder desc = new StringBuilder();
            if (def.has("description")) {
                desc.append(def.get("description").getAsString());
            }
            if (def.has("notes")) {
                for (JsonElement noteEl : def.getAsJsonArray("notes")) {
                    desc.append("\n\n").append(noteEl.getAsString());
                }
            }
            struct.setDescription(desc.toString());

            // One byte, one owner. Two fields sharing a byte would make the second silently
            // truncate the first, and the run could never reach a fixed point.
            byte[] owner = new byte[size];
            int placed = 0;
            for (JsonElement fieldEl : def.getAsJsonArray("fields")) {
                JsonObject field = fieldEl.getAsJsonObject();
                if (field.has("inline") && field.get("inline").getAsBoolean()) {
                    continue;
                }
                int offset = field.get("offset").getAsInt();
                int width = field.get("width").getAsInt();
                String typeText = field.get("type").getAsString();
                // A low-confidence field is placed for its WIDTH and never named, so a guess can
                // never masquerade as a confirmed field -- the rule the known_symbols files apply to
                // functions. A null name leaves Ghidra's own field_0xNN in place.
                String fieldName = field.has("name") ? field.get("name").getAsString() : null;
                String fieldDesc =
                    field.has("description") ? field.get("description").getAsString() : "";

                if (offset < 0 || offset + width > size) {
                    fail(name + " field at " + hex(offset) + " (" + width + " bytes) runs past the "
                        + "declared size " + hex(size) + ".");
                    continue;
                }
                boolean clash = false;
                for (int i = offset; i < offset + width; i++) {
                    if (owner[i] != FREE) {
                        fail(name + " field at " + hex(offset) + " overlaps one already placed at "
                            + hex(i) + ".");
                        clash = true;
                        break;
                    }
                }
                if (clash) {
                    continue;
                }

                DataType dt = resolveType(typeText);
                if (dt == null) {
                    fail(name + " field at " + hex(offset) + ": unknown type '" + typeText + "'.");
                    continue;
                }
                if (dt.getLength() != width) {
                    fail(name + " field at " + hex(offset) + ": declared width " + width
                        + " but type '" + typeText + "' is " + dt.getLength() + " bytes. Fix the "
                        + "JSON -- do not let the two disagree.");
                    continue;
                }

                try {
                    struct.replaceAtOffset(offset, dt, width, fieldName, fieldDesc);
                    for (int i = offset; i < offset + width; i++) {
                        owner[i] = EXPLICIT;
                    }
                    placed++;
                } catch (Exception e) {
                    fail(name + " field at " + hex(offset) + ": " + e.getMessage());
                }
            }
            for (JsonElement fieldEl : def.getAsJsonArray("fields")) {
                JsonObject field = fieldEl.getAsJsonObject();
                if (!field.has("inline") || !field.get("inline").getAsBoolean()) {
                    continue;
                }
                int offset = field.get("offset").getAsInt();
                int width = field.get("width").getAsInt();
                String typeText = field.get("type").getAsString();
                DataType dt = resolveType(typeText);
                if (!(dt instanceof Structure)) {
                    fail(name + " inline field at " + hex(offset) + ": '" + typeText
                        + "' is not a struct defined earlier.");
                    continue;
                }
                if (dt.getLength() != width || offset < 0 || offset + width > size) {
                    fail(name + " inline field at " + hex(offset) + ": declared width " + width
                        + ", '" + typeText + "' is " + dt.getLength() + " bytes, struct is "
                        + hex(size) + ".");
                    continue;
                }
                placed += inline(struct, owner, (Structure) dt, offset, name);
            }

            DataType resolved = dtm.addDataType(struct, DataTypeConflictHandler.REPLACE_HANDLER);
            defined.put(name, resolved);
            typesBuilt++;
            fieldsPlaced += placed;
            println("built " + name + " (" + resolved.getLength() + " bytes, " + placed
                + " fields placed)");
        }

        int typed = 0, alreadyTyped = 0, skippedNoTarget = 0, committed = 0;
        FunctionManager fm = currentProgram.getFunctionManager();
        for (JsonElement el : root.getAsJsonArray("applications")) {
            JsonObject app = el.getAsJsonObject();
            String structName = app.get("struct").getAsString();
            DataType target = defined.get(structName);
            if (target == null) {
                // Either the struct is for another binary or its definition failed above. Neither
                // is a reason to abort the rest of the run.
                skippedOtherBinary++;
                continue;
            }

            if (app.has("binary")
                && !progName.contains(app.get("binary").getAsString().toUpperCase())) {
                skippedOtherBinary++;
                continue;
            }
            String addrText = app.get("function").getAsString();
            int index = app.get("parameter").getAsInt();
            Address addr = currentProgram.getAddressFactory().getAddress(addrText);
            Function f = addr == null ? null : fm.getFunctionAt(addr);
            if (f == null) {
                println("WARN: no function at " + addrText + " -- skipping.");
                skippedNoTarget++;
                continue;
            }
            if (index >= f.getParameterCount() && commitPrototype(f)) {
                committed++;
            }
            if (index < 0 || index >= f.getParameterCount()) {
                println("WARN: " + f.getName() + " has " + f.getParameterCount()
                    + " parameters, so index " + index + " does not exist -- skipping. Run"
                    + " ES2CommitAllParams first if the signature is still the analyser's guess.");
                skippedNoTarget++;
                continue;
            }

            DataType ptr = new PointerDataType(target, 4, dtm);
            Parameter p = f.getParameter(index);
            if (ptr.isEquivalent(p.getDataType())) {
                alreadyTyped++;
                continue;
            }
            if (p.isAutoParameter()) {
                println("WARN: " + f.getName() + " parameter " + index + " is an auto-parameter of "
                    + f.getCallingConventionName() + " -- skipping.");
                skippedNoTarget++;
                continue;
            }
            try {
                p.setDataType(ptr, SourceType.USER_DEFINED);
                typed++;
            } catch (Exception e) {
                fail("typing " + f.getName() + " parameter " + index + " as " + structName
                    + "*: " + e.getMessage());
            }
        }

        if (decomp != null) {
            decomp.dispose();
        }
        println("ES2ApplyStructures: types=" + typesBuilt + " fields=" + fieldsPlaced
            + " params typed=" + typed + " params already correct=" + alreadyTyped
            + " prototypes committed=" + committed
            + " skipped(other binary)=" + skippedOtherBinary
            + " skipped(no target)=" + skippedNoTarget + " errors=" + errors);
    }

    private boolean commitPrototype(Function f) {
        if (decomp == null) {
            decomp = new DecompInterface();
            decomp.openProgram(currentProgram);
        }
        DecompileResults res = decomp.decompileFunction(f, 60, monitor);
        if (res == null || res.getHighFunction() == null) {
            return false;
        }
        try {
            HighFunctionDBUtil.commitParamsToDatabase(res.getHighFunction(), true,
                ReturnCommitOption.NO_COMMIT, SourceType.ANALYSIS);
            return true;
        } catch (Exception e) {
            fail("committing " + f.getName() + "'s prototype: " + e.getMessage());
            return false;
        }
    }

    // Places base's components at offset + their own offsets; returns how many were placed.
    private int inline(StructureDataType struct, byte[] owner, Structure base, int offset,
            String structName) {
        int placed = 0;
        for (DataTypeComponent c : base.getDefinedComponents()) {
            int at = offset + c.getOffset();
            int len = c.getLength();
            int free = 0, explicit = 0;
            for (int i = at; i < at + len; i++) {
                free += owner[i] == FREE ? 1 : 0;
                explicit += owner[i] == EXPLICIT ? 1 : 0;
            }
            if (free == len) {
                try {
                    struct.replaceAtOffset(at, c.getDataType(), len, c.getFieldName(), c.getComment());
                    for (int i = at; i < at + len; i++) {
                        owner[i] = INLINED;
                    }
                    placed++;
                } catch (Exception e) {
                    fail(structName + " inlined component at " + hex(at) + ": " + e.getMessage());
                }
            } else if (explicit == len) {
                continue;
            } else if (c.getDataType() instanceof Structure && free + explicit == len) {
                placed += inline(struct, owner, (Structure) c.getDataType(), at, structName);
            } else {
                fail(structName + " inlined component " + c.getFieldName() + " at " + hex(at)
                    + " is partly covered by another field.");
            }
        }
        return placed;
    }

    private void fail(String message) {
        println("ERROR: " + message);
        errors++;
    }

    private static String hex(int value) {
        return "0x" + Integer.toHexString(value);
    }

    // 'X *' -> pointer to X, 'X[n]' -> array of n X, otherwise a builtin or a type defined earlier
    // in this run. Deliberately narrow: a type spelling this does not recognise is an error rather
    // than a fallback to undefined, so a typo cannot quietly produce a struct with a hole in it.
    private DataType resolveType(String text) {
        String t = text.trim();
        if (t.endsWith("*")) {
            DataType base = resolveType(t.substring(0, t.length() - 1));
            return base == null ? null : new PointerDataType(base, 4, dtm);
        }
        int bracket = t.indexOf('[');
        if (bracket > 0 && t.endsWith("]")) {
            DataType base = resolveType(t.substring(0, bracket));
            if (base == null) {
                return null;
            }
            int count;
            try {
                count = Integer.parseInt(t.substring(bracket + 1, t.length() - 1).trim());
            } catch (NumberFormatException e) {
                return null;
            }
            return new ArrayDataType(base, count, base.getLength(), dtm);
        }
        switch (t) {
            case "void": return VoidDataType.dataType;
            case "byte": return ByteDataType.dataType;
            case "sbyte": return CharDataType.dataType;
            case "short": return ShortDataType.dataType;
            case "ushort": return UnsignedShortDataType.dataType;
            case "int": return IntegerDataType.dataType;
            case "uint": return UnsignedIntegerDataType.dataType;
            case "undefined1": return Undefined1DataType.dataType;
            case "undefined2": return Undefined2DataType.dataType;
            case "undefined4": return Undefined4DataType.dataType;
            default: break;
        }
        DataType local = defined.get(t);
        if (local != null) {
            return local;
        }
        // Anything else has to already exist under /ES2 -- in practice a vtable type
        // ES2ApplyVtables built, which is what makes field 0 dispatch to named methods.
        return dtm.getDataType(new CategoryPath("/ES2"), t);
    }
}
