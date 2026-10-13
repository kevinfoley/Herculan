namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - ES1's <c>.TTM</c> animation scripts, 113 in <c>SHELL0.VOL</c> and 10 in <c>SIMVOL0.VOL</c>: tagged chunks holding a
/// version, a page count, the packed script and a table of named tags (docs/es1/formats/ttm-scripts.md). Every chunk keeps
/// its bytes, so a file writes back as it was read; <see cref="Script"/> is the packed script unpacked, which writing does not
/// use. Read by <see cref="Io.Transform.Common.Es1TtmScriptTransformer"/>.
/// </summary>
public class Es1TtmScript {
	/// <summary>The top-level chunks in file order.</summary>
	public List<Es1TtmChunk> Chunks { get; set; } = [];

	/// <summary>
	/// The word of the first <c>PAG:</c> chunk, the page count the loaders take; null when there is none.
	/// </summary>
	public ushort? PageCount { get; set; }

	/// <summary>The first <c>TT3:</c> chunk's method byte, which the shell's loader reads and does not use; null when there is none.</summary>
	public byte? PackMethod { get; set; }

	/// <summary>The first <c>TT3:</c> chunk's unpacked length, the bytes the loaders unpack; null when there is none.</summary>
	public uint? UnpackedLength { get; set; }

	/// <summary>The script unpacked, <see cref="UnpackedLength"/> bytes of 16-bit opcodes and their arguments; null with no <c>TT3:</c>.</summary>
	public byte[]? Script { get; set; }

	/// <summary>The packed bytes the unpacking read, of the <c>TT3:</c> chunk's length less its 5-byte header.</summary>
	public int PackedBytesRead { get; set; }
}

/// <summary>
/// One command of an unpacked <see cref="Es1TtmScript.Script"/>: an opcode word and its arguments, as the page walk divides them
/// (docs/es1/formats/ttm-scripts.md#pages). What each opcode does is in docs/es1/formats/ttm-scripts.md#the-commands.
/// </summary>
public class Es1TtmCommand {
	/// <summary>The opcode that ends a page.</summary>
	public const ushort EndFrame = 0x0ff0;

	/// <summary>Where the opcode word starts in the unpacked script.</summary>
	public int Offset { get; set; }

	/// <summary>The opcode word; its low four bits give the arguments.</summary>
	public ushort Opcode { get; set; }

	/// <summary>
	/// The argument words: the opcode's low-bits count of them, or for a point list (<c>0xaf1f</c>, <c>0xaf2f</c>) the count
	/// <i>n</i> followed by its 2<i>n</i> words; empty for a string argument.
	/// </summary>
	public ushort[] Arguments { get; set; } = [];

	/// <summary>The string argument of an opcode whose low bits are 15, other than a point list; null otherwise.</summary>
	public string? Text { get; set; }
}

/// <summary>One chunk of an <see cref="Es1TtmScript"/>: a 4-byte tag, a size word, and either bytes or chunks.</summary>
public class Es1TtmChunk {
	/// <summary>Bit 31 of the size word, which marks a chunk whose content is chunks.</summary>
	public const uint ContainerFlag = 0x80000000;

	/// <summary>The tag, three letters and a colon (<c>VER:</c>, <c>PAG:</c>, <c>TT3:</c>, <c>TTI:</c>, <c>TAG:</c>).</summary>
	public string Tag { get; set; } = "";

	/// <summary>Whether the size word has <see cref="ContainerFlag"/> set, its content being <see cref="Children"/>.</summary>
	public bool IsContainer { get; set; }

	/// <summary>The content of a chunk that is not a container.</summary>
	public byte[] Content { get; set; } = [];

	/// <summary>The chunks a container holds, back to back.</summary>
	public List<Es1TtmChunk> Children { get; set; } = [];
}
