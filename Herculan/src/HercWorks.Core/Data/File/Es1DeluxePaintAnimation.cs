namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - ES1's <c>anm\ESIEGE2.ANM</c> and <c>anm\ESIEGE3.ANM</c> in <c>SHELL0.VOL</c>: Deluxe Paint Animation large-page
/// files, the shell's two full-screen animations (docs/es1/formats/anm-animation.md). The fields the player reads are named;
/// the others are named by offset. Read by <see cref="Io.Transform.Shell.Es1DeluxePaintAnimationTransformer"/>.
/// </summary>
public class Es1DeluxePaintAnimation {
	/// <summary>The header's length.</summary>
	public const int HeaderLength = 0x80;

	/// <summary>Where the page table starts.</summary>
	public const int PageTableOffset = 0x500;

	/// <summary>A page table entry's length.</summary>
	public const int PageTableEntryLength = 6;

	/// <summary>Where the first page starts.</summary>
	public const int FirstPageOffset = 0xb00;

	/// <summary>The distance from one page to the next.</summary>
	public const int PageLength = 0x10000;

	/// <summary><c>"LPF "</c>, the dword at <c>+0x00</c> that <c>Anm_Load</c> (<c>ES1GO 00013aa8</c>) tests.</summary>
	public const uint LpfId = 0x2046504c;

	/// <summary><c>"ANIM"</c>, the dword at <c>+0x10</c> that <c>Anm_Load</c> tests.</summary>
	public const uint AnimId = 0x4d494e41;

	/// <summary>The dword at <c>+0x00</c>; the player plays only a file holding <see cref="LpfId"/>.</summary>
	public uint Id { get; set; } = LpfId;

	public ushort Unread04 { get; set; }

	public uint Unread08 { get; set; }

	public ushort Unread0C { get; set; }

	public ushort Unread0E { get; set; }

	/// <summary>The dword at <c>+0x10</c>; the player plays only a file holding <see cref="AnimId"/>.</summary>
	public uint ContentType { get; set; } = AnimId;

	public ushort Unread14 { get; set; }

	public ushort Unread16 { get; set; }

	/// <summary><c>+0x18</c>–<c>+0x19</c>.</summary>
	public byte[] Unread18 { get; set; } = new byte[2];

	/// <summary>The byte at <c>+0x1a</c>, which <c>Anm_Load</c> subtracts from <see cref="FrameCount"/> − 1 into a field of the player.</summary>
	public byte FrameCountReduction { get; set; }

	/// <summary><c>+0x1b</c>–<c>+0x3f</c>.</summary>
	public byte[] Unread1B { get; set; } = new byte[0x25];

	/// <summary>The <c>int32</c> at <c>+0x40</c>: the player draws frames 0 to <see cref="FrameCount"/> − 2.</summary>
	public int FrameCount { get; set; }

	/// <summary>The word at <c>+0x44</c>, which sets a frame's time in the player's mode 0 only.</summary>
	public ushort FramesPerSecond { get; set; }

	/// <summary><c>+0x46</c>–<c>+0x7f</c>.</summary>
	public byte[] Unread46 { get; set; } = new byte[0x3a];

	/// <summary><c>+0x80</c>–<c>+0xff</c>.</summary>
	public byte[] Unread80 { get; set; } = new byte[0x80];

	/// <summary><c>+0x100</c>–<c>+0x4ff</c>.</summary>
	public byte[] Unread100 { get; set; } = new byte[0x400];

	/// <summary>The pages, each with its page table entry; the word at <c>+0x06</c> is their number.</summary>
	public Es1AnimationPage[] Pages { get; set; } = [];

	/// <summary>The page table's bytes past the <see cref="Pages"/>' entries, to <see cref="FirstPageOffset"/>.</summary>
	public byte[] UnreadPageTableTail { get; set; } = [];
}

/// <summary>
/// One page of an <see cref="Es1DeluxePaintAnimation"/>: its page table entry, its own header and its frame records, at
/// <see cref="Es1DeluxePaintAnimation.FirstPageOffset"/> + <see cref="Es1DeluxePaintAnimation.PageLength"/> × its index.
/// </summary>
public class Es1AnimationPage {
	/// <summary>The table entry's first record, which with <see cref="TableRecordCount"/> chooses the page for a frame.</summary>
	public ushort TableFirstRecord { get; set; }

	public ushort TableRecordCount { get; set; }

	/// <summary>The table entry's third word.</summary>
	public ushort TableUnread04 { get; set; }

	/// <summary>The page header's first record, which the frame's number is counted from within the page.</summary>
	public ushort FirstRecord { get; set; }

	/// <summary>The page header's third word; the second is the number of <see cref="Records"/>.</summary>
	public ushort Unread04 { get; set; }

	public ushort Unread06 { get; set; }

	/// <summary>The frame records, in page order; the page header's sizes are their lengths.</summary>
	public Es1AnimationRecord[] Records { get; set; } = [];

	/// <summary>The bytes after the last record, to the next page or the end of the file.</summary>
	public byte[] Padding { get; set; } = [];
}

/// <summary>One frame record of an <see cref="Es1AnimationPage"/>: a 4-byte header, optional extra bytes, and the draw operations.</summary>
public class Es1AnimationRecord {
	public byte Unread00 { get; set; }

	/// <summary>The byte at <c>+1</c>: when it is not 0, <see cref="Extra"/> lies between the header and <see cref="Ops"/>.</summary>
	public byte HasExtra { get; set; }

	/// <summary>The word at <c>+2</c>, read only when <see cref="HasExtra"/> is not 0: the extra bytes' length, which they take rounded up to even.</summary>
	public ushort ExtraLength { get; set; }

	/// <summary>The extra bytes, <see cref="ExtraLength"/> rounded up to even when <see cref="HasExtra"/> is not 0, else none.</summary>
	public byte[] Extra { get; set; } = [];

	/// <summary>The draw operations to the record's end.</summary>
	public byte[] Ops { get; set; } = [];
}
