// Pre-analysis setup for an ES1 X-32 program image imported raw at base 0 by
// tools/scripts/es1_x32_extract.py. Makes the file block read/write/execute (code and data share
// one segment), adds an uninitialised block from the end of the file bytes up to the memory size
// the kernel allocates, and marks the entry point the kernel jumps to, so auto-analysis starts there.
// Layout: Herculan/docs/es1/formats/x32-executable.md.
//
// Args: memSize entry, both hex.
//@category ES1
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;

public class ES1SetupX32Image extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 2) {
            throw new IllegalArgumentException("usage: ES1SetupX32Image memSize entry (hex)");
        }
        long memSize = Long.parseLong(args[0], 16);
        long entry = Long.parseLong(args[1], 16);

        Memory memory = currentProgram.getMemory();
        MemoryBlock[] blocks = memory.getBlocks();
        if (blocks.length != 1 || blocks[0].getStart().getOffset() != 0) {
            throw new IllegalStateException("expected one block at address 0, the raw image");
        }
        MemoryBlock image = blocks[0];
        image.setRead(true);
        image.setWrite(true);
        image.setExecute(true);

        long fileEnd = image.getEnd().getOffset() + 1;
        if (memSize > fileEnd) {
            MemoryBlock tail = memory.createUninitializedBlock("uninit", toAddr(fileEnd), memSize - fileEnd, false);
            tail.setRead(true);
            tail.setWrite(true);
            println(String.format("uninit %08x..%08x", fileEnd, memSize));
        }

        // At base 0 every constant from 0x400 up to the end of the code reads as a code address, and
        // the reference analyzers' subroutine option disassembles at such targets: on ES1DBSIM it
        // started instructions at 0xcaf, a length computed as 0x72273 - 0x715c4, inside the
        // function citing vcr.cpp, and that function's own flow then stopped on the conflict.
        // Code is found from the entry point and ES1SeedFunctionStarts instead.
        setAnalysisOption(currentProgram, "Reference.Subroutine References", "false");
        setAnalysisOption(currentProgram, "Data Reference.Subroutine References", "false");

        Address ep = toAddr(entry);
        currentProgram.getSymbolTable().addExternalEntryPoint(ep);
        println(String.format("image %08x..%08x, entry %08x", 0L, fileEnd, entry));
    }
}
