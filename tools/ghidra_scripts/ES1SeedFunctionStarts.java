// Creates a function at every prologue that directly follows the end of other code, for an ES1
// X-32 image, whose functions reached only through pointers auto-analysis never finds. Run after
// auto-analysis; it re-runs analysis over what it adds.
//
// A seed is a `55 8B EC` (push ebp / mov ebp,esp) or `C8 nn nn 00` (enter n,0, n below 0x8000) whose preceding
// bytes, after 0-3 bytes of 00/90 padding, end in `C3` (ret), `C2 nn nn` (ret n) or `E9 rel32`
// (jmp). Any alignment: ES1GO's functions are not 4-aligned. Of the prologue-shaped function starts
// auto-analysis finds on its own, the rule holds for 841 of 854 in ES1DBSIM and 662 of 674 in
// ES1GO. A seed inside an existing function's body is reported, not created: that is a function
// Ghidra ran into past a call it took for returning, or started early on padding, and it needs the
// entry repair, not a second overlapping function.
//
// Args: none.
//@category ES1
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.mem.MemoryBlock;

public class ES1SeedFunctionStarts extends GhidraScript {
    private byte[] bytes;

    private int u8(long i) {
        return i < 0 || i >= bytes.length ? -1 : bytes[(int) i] & 0xff;
    }

    private boolean followsCodeEnd(long a) {
        for (int pad = 0; pad < 4; pad++) {
            long end = a - pad;
            for (long j = end; j < a; j++) {
                if (u8(j) != 0x00 && u8(j) != 0x90) {
                    return false;
                }
            }
            if (u8(end - 1) == 0xc3 || u8(end - 3) == 0xc2 || u8(end - 5) == 0xe9) {
                return true;
            }
        }
        return false;
    }

    private boolean isPrologue(long a) {
        if (u8(a) == 0x55 && u8(a + 1) == 0x8b && u8(a + 2) == 0xec) {
            return true;
        }
        // The largest real frame is 0x530; a word table reading `C3 00 C8 00 CD 00` (ES1DBSIM
        // 000a8bd0, ES1GO 00095288) otherwise passes as `ret`, `enter 0xcd00,0`.
        return u8(a) == 0xc8 && u8(a + 2) < 0x80 && u8(a + 3) == 0x00;
    }

    @Override
    public void run() throws Exception {
        MemoryBlock image = currentProgram.getMemory().getBlock("image");
        if (image == null || image.getStart().getOffset() != 0) {
            throw new IllegalStateException("no 'image' block at 0; import with es1_x32_extract.py");
        }
        bytes = new byte[(int) image.getSize()];
        image.getBytes(image.getStart(), bytes);

        FunctionManager fm = currentProgram.getFunctionManager();
        int seeds = 0, existing = 0, inside = 0, created = 0, failed = 0, cleared = 0;
        StringBuilder insideList = new StringBuilder();
        for (long a = 0; a + 4 <= bytes.length; a++) {
            if (!isPrologue(a) || !followsCodeEnd(a)) {
                continue;
            }
            seeds++;
            Address addr = toAddr(a);
            if (fm.getFunctionAt(addr) != null) {
                existing++;
                continue;
            }
            Function container = fm.getFunctionContaining(addr);
            if (container != null) {
                inside++;
                insideList.append(String.format(" %08x(in %s)", a, container.getEntryPoint()));
                continue;
            }
            // At base 0 a prologue's own bytes read as an address (`C8 10 00 00` is 0x10c8), so a
            // data reference to the function can leave a pointer typed over its first bytes.
            Data data = getDataContaining(addr);
            if (data != null && data.isPointer()) {
                clearListing(data.getAddress(), data.getMaxAddress());
                cleared++;
            }
            disassemble(addr);
            if (createFunction(addr, null) != null) {
                created++;
            } else {
                failed++;
                Data blocking = getDataContaining(addr);
                println(String.format("could not create a function at %08x%s", a,
                    blocking == null ? "" : " (" + blocking.getDataType().getName() + " at " + blocking.getAddress() + ")"));
            }
        }
        println(String.format("seeds %d: already functions %d, inside a function %d, created %d (%d over a pointer), failed %d",
            seeds, existing, inside, created, cleared, failed));
        if (inside > 0) {
            println("inside:" + insideList);
        }
        analyzeChanges(currentProgram);
        println("functions now " + fm.getFunctionCount());
    }
}
