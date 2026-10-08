import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.data.CategoryPath;
import ghidra.program.model.data.DataType;
import ghidra.program.model.data.DataTypeConflictHandler;
import ghidra.program.model.data.DataTypeManager;
import ghidra.program.model.data.FunctionDefinitionDataType;
import ghidra.program.model.data.PointerDataType;
import ghidra.program.model.data.StructureDataType;
import ghidra.program.model.listing.CodeUnit;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.symbol.SourceType;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolTable;
import java.io.FileReader;
import java.util.HashSet;
import java.util.Set;

// args[0] = path to known_vtables.json (see that file's _readme for the schema).
//
// Gives DBSIM's simulation-object vtables a shape. For each definition in the JSON, reading only
// those whose "binary" names the current program (isBinary):
//   - creates one FunctionDefinitionDataType per slot, under category /ES2/<VtableName>, with the
//     default (undefined) return and no declared parameters -- argument lists are a separate
//     per-function finding and belong in a known_symbols file's "signature" field, not here;
//   - assembles them into a StructureDataType of that many 4-byte function pointers, each field
//     carrying the slot's name and description;
//   - applies that structure at every listed instance address, labels it, and writes a plate
//     comment naming the class and its evidence.
//
// The point is not the label: it is that once an object pointer is typed with a struct whose first
// field is a pointer to one of these, the decompiler renders obj[+0x20](...) as a named call. This
// script is the half of that which does not depend on the object structs.
//
// Idempotent: data types are resolved with REPLACE_HANDLER, the target bytes are cleared before the
// structure is laid down, and a label that is already correct is left alone. Safe to re-run after
// known_vtables.json gains slots or instances.
//
// A structure is only ever as long as the JSON says. Nothing here infers a table's length from
// memory -- see the known_vtables.json _readme for why the definitions stop short.
public class ES2ApplyVtables extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] scriptArgs = getScriptArgs();
        if (scriptArgs.length < 1) {
            println("Usage: ES2ApplyVtables <known_vtables.json path>");
            return;
        }

        JsonObject root;
        try (FileReader fr = new FileReader(scriptArgs[0])) {
            root = JsonParser.parseReader(fr).getAsJsonObject();
        }
        JsonArray vtables = root.getAsJsonArray("vtables");

        String progName = currentProgram.getName().toUpperCase();
        DataTypeManager dtm = currentProgram.getDataTypeManager();
        Listing listing = currentProgram.getListing();
        SymbolTable st = currentProgram.getSymbolTable();

        int typesBuilt = 0, applied = 0, labeled = 0, skippedOtherBinary = 0, errors = 0;

        // One address, one instance. Two structures laid at the same address would overwrite each
        // other silently and the run could never reach a fixed point.
        Set<String> seenAddresses = new HashSet<>();

        for (JsonElement el : vtables) {
            JsonObject def = el.getAsJsonObject();
            String vtName = def.get("name").getAsString();

            if (!isBinary(progName, def.get("binary").getAsString())) {
                skippedOtherBinary++;
                continue;
            }

            JsonArray slots = def.getAsJsonArray("slots");
            CategoryPath slotCategory = new CategoryPath("/ES2/" + vtName);
            StructureDataType struct =
                new StructureDataType(new CategoryPath("/ES2"), vtName, 0, dtm);
            if (def.has("description")) {
                struct.setDescription(def.get("description").getAsString());
            }

            int expectedOffset = 0;
            boolean slotsOk = true;
            for (JsonElement slotEl : slots) {
                JsonObject slot = slotEl.getAsJsonObject();
                int offset = slot.get("offset").getAsInt();
                if (offset != expectedOffset) {
                    println("ERROR: " + vtName + " slot at offset " + offset
                        + " -- slots must run from 0 in steps of 4 with no gaps (expected "
                        + expectedOffset + ").");
                    slotsOk = false;
                    errors++;
                    break;
                }
                expectedOffset += 4;

                String slotName = slot.get("name").getAsString();
                String slotDesc = slot.has("description") ? slot.get("description").getAsString() : "";

                FunctionDefinitionDataType fn =
                    new FunctionDefinitionDataType(slotCategory, slotName, dtm);
                fn.setComment(slotDesc);
                DataType fnResolved = dtm.addDataType(fn, DataTypeConflictHandler.REPLACE_HANDLER);
                struct.add(new PointerDataType(fnResolved, 4, dtm), 4, slotName, slotDesc);
            }
            if (!slotsOk) {
                continue;
            }

            DataType structResolved =
                dtm.addDataType(struct, DataTypeConflictHandler.REPLACE_HANDLER);
            typesBuilt++;
            println("built " + vtName + " (" + slots.size() + " slots, "
                + structResolved.getLength() + " bytes)");

            for (JsonElement instEl : def.getAsJsonArray("instances")) {
                JsonObject inst = instEl.getAsJsonObject();
                String addrText = inst.get("address").getAsString();
                if (!seenAddresses.add(addrText.toLowerCase())) {
                    println("ERROR: duplicate instance address " + addrText + " -- merge them.");
                    errors++;
                    continue;
                }

                Address addr = currentProgram.getAddressFactory().getAddress(addrText);
                if (addr == null || !currentProgram.getMemory().contains(addr)) {
                    println("ERROR: " + addrText + " is not in this program's memory.");
                    errors++;
                    continue;
                }

                try {
                    int len = structResolved.getLength();
                    listing.clearCodeUnits(addr, addr.add(len - 1), false);
                    listing.createData(addr, structResolved);
                    applied++;

                    String label = inst.get("label").getAsString();
                    Symbol primary = st.getPrimarySymbol(addr);
                    if (primary == null || !label.equals(primary.getName())) {
                        Symbol created = st.createLabel(addr, label, SourceType.USER_DEFINED);
                        created.setPrimary();
                        labeled++;
                    }

                    StringBuilder comment = new StringBuilder(label).append(" -- ").append(vtName);
                    if (inst.has("description")) {
                        comment.append("\n").append(inst.get("description").getAsString());
                    }
                    listing.setComment(addr, CodeUnit.PLATE_COMMENT, comment.toString());
                } catch (Exception e) {
                    println("ERROR applying " + vtName + " at " + addrText + ": " + e.getMessage());
                    errors++;
                }
            }
        }

        println("ES2ApplyVtables: types=" + typesBuilt + " applied=" + applied
            + " labeled=" + labeled + " skippedOtherBinary=" + skippedOtherBinary
            + " errors=" + errors);
    }

    // An entry's "binary" is the program's name, less ".EXE" for ES2's (DBSIM -> DBSIM.EXE; ES1's
    // programs are named ES1DBSIM and ES1GO). An exact match, so DBSIM never matches ES1DBSIM.
    private static boolean isBinary(String progName, String binary) {
        String b = binary.toUpperCase();
        return progName.equals(b) || progName.equals(b + ".EXE");
    }
}
