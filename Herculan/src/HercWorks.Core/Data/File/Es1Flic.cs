namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - ES1's <c>FLIC\INTRO3.DFL</c> and <c>FLIC\END.DFL</c>, the intro and end movies <c>PLAYFLIC.EXE</c> plays: an
/// Animator Pro flic with chunk types of Dynamix's own (docs/es1/formats/dfl-flic.md). The header fields the player reads
/// are named; the others are named by offset. Read by <see cref="Io.Transform.Shell.Es1FlicTransformer"/>.
/// </summary>
public class Es1Flic {
	/// <summary>The header's length.</summary>
	public const int HeaderLength = 0x80;

	/// <summary>A frame's header: its size, type, subchunk count and 8 more bytes.</summary>
	public const int FrameHeaderLength = 16;

	/// <summary>A subchunk's header: its size and type.</summary>
	public const int ChunkHeaderLength = 6;

	/// <summary>The magic of an Animator Pro <c>.FLC</c>, both files'.</summary>
	public const ushort FlcMagic = 0xaf12;

	/// <summary>The magic of an Animator <c>.FLI</c>, whose speed is in 70ths of a second.</summary>
	public const ushort FliMagic = 0xaf11;

	/// <summary>A frame's type.</summary>
	public const ushort FrameType = 0xf1fa;

	/// <summary>The <c>uint32</c> at <c>+0x00</c>, the file's size.</summary>
	public uint FileSize { get; set; }

	/// <summary>The word at <c>+0x04</c>: <see cref="FlcMagic"/> or <see cref="FliMagic"/>; <c>Flic_Open</c> refuses another.</summary>
	public ushort Magic { get; set; } = FlcMagic;

	/// <summary>The word at <c>+0x06</c>, the frames the player plays.</summary>
	public ushort FrameCount { get; set; }

	/// <summary>The word at <c>+0x08</c>.</summary>
	public ushort Width { get; set; }

	/// <summary>The word at <c>+0x0a</c>.</summary>
	public ushort Height { get; set; }

	/// <summary>The word at <c>+0x0c</c>, bits a pixel.</summary>
	public ushort Depth { get; set; }

	public ushort Flags { get; set; }

	/// <summary>The <c>uint32</c> at <c>+0x10</c>: milliseconds a frame in a <c>.FLC</c>, 70ths of a second in a <c>.FLI</c>.</summary>
	public uint Speed { get; set; }

	/// <summary><c>+0x14</c>–<c>+0x25</c>.</summary>
	public byte[] Unread14 { get; set; } = new byte[0x12];

	public ushort AspectX { get; set; }

	public ushort AspectY { get; set; }

	/// <summary><c>+0x2a</c>–<c>+0x4f</c>.</summary>
	public byte[] Unread2A { get; set; } = new byte[0x26];

	/// <summary>The <c>uint32</c> at <c>+0x50</c>, the file offset of the first frame.</summary>
	public uint Frame1Offset { get; set; } = HeaderLength;

	/// <summary>The <c>uint32</c> at <c>+0x54</c>, the file offset of the second frame.</summary>
	public uint Frame2Offset { get; set; }

	/// <summary><c>+0x58</c>–<c>+0x7f</c>.</summary>
	public byte[] Unread58 { get; set; } = new byte[0x28];

	/// <summary>The bytes between the header and <see cref="Frame1Offset"/>.</summary>
	public byte[] Gap { get; set; } = [];

	/// <summary>The top-level chunks from <see cref="Frame1Offset"/>, one after another, as many as <see cref="FrameCount"/>.</summary>
	public Es1FlicFrame[] Frames { get; set; } = [];

	/// <summary>The bytes after the last frame.</summary>
	public byte[] Tail { get; set; } = [];
}

/// <summary>One top-level chunk of an <see cref="Es1Flic"/>, a frame when its type is <see cref="Es1Flic.FrameType"/>.</summary>
public class Es1FlicFrame {
	public ushort Type { get; set; } = Es1Flic.FrameType;

	/// <summary><c>+0x08</c>–<c>+0x0f</c> of the frame's header.</summary>
	public byte[] Unread08 { get; set; } = new byte[8];

	/// <summary>The subchunks, as many as the header's count.</summary>
	public Es1FlicChunk[] Chunks { get; set; } = [];

	/// <summary>The frame's bytes after its last subchunk.</summary>
	public byte[] Tail { get; set; } = [];
}

/// <summary>One subchunk of an <see cref="Es1FlicFrame"/>: its type and the data after its 6-byte header.</summary>
public class Es1FlicChunk {
	public ushort Type { get; set; }

	public byte[] Data { get; set; } = [];
}
