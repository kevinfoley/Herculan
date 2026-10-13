namespace HercWorks.Core.Data.File.Bnd;

/// <summary>
/// FILE - ES1's <c>BND\MAIN.BND</c> in SHELL0.VOL: five dwords, two of them the heap sizes the shell's start-up tests
/// (docs/es1/formats/bnd-notes.md#bndmainbnd). Not the simulator's <c>bnd\MAIN.BND</c>, which has another length. Read by
/// <see cref="Io.Transform.Bnd.Es1ShellMainBndTransformer"/>.
/// </summary>
public class Es1ShellMainBnd {
	/// <summary>Offset 0: read as seconds since 1970, a date near the file's own (docs/es1/formats/bnd-notes.md#the-first-dword).</summary>
	public uint Stamp { get; set; }

	public int Unread04 { get; set; }

	/// <summary>Offset 8: a heap smaller than this sets the shell's byte <c>0007fd09</c>. 3,700,000.</summary>
	public int LowHeapFlagLimit { get; set; }

	/// <summary>Offset 12: a heap smaller than this ends the shell with an assert. 2,400,000.</summary>
	public int MinimumHeap { get; set; }

	public int Unread10 { get; set; }
}

/// <summary>
/// FILE - one of the other seven ES1 shell tuning files in SHELL0.VOL's <c>BND\</c>: <c>ALERT</c>, <c>CTL_ALRT</c>,
/// <c>CAL_ALRT</c>, <c>MAP</c>, <c>SHELLMAP</c>, <c>CTL_MGR</c> and <c>IMDEVICE</c>. Each is read whole into a global at
/// start-up, and what reads its fields is an Open item (docs/es1/formats/bnd-notes.md#the-shells-files). Read by
/// <see cref="Io.Transform.Bnd.Es1ShellTuningFileTransformer"/>.
/// </summary>
public class Es1ShellTuningFile {
	/// <summary>The file's content, as its loader reads it into the global.</summary>
	public byte[] Bytes { get; set; } = [];
}
