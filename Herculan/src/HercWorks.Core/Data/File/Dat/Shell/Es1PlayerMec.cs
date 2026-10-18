namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - ES1's <c>data\player.mec</c>: the player's entry, a counted list of pilots each with a HERC, and the weapons'
/// available bytes, which the shell writes before a mission and the simulator reads
/// (docs/es1/formats/career-log-and-player-mec.md#the-player-file). Written by
/// <see cref="Io.Transform.Common.Es1MissionLoad"/> for a single mission, read and written by
/// <see cref="Io.Transform.Shell.Es1PlayerMecTransformer"/>.
/// </summary>
public class Es1PlayerMec {
	/// <summary>How many available bytes end the file: one per weapon of the equipment state.</summary>
	public const int WeaponCount = 17;

	/// <summary>The index of the player's entry, the career record's <c>+0xe</c>; the simulator's <c>000af9fa</c>.</summary>
	public short PlayerEntry { get; set; }

	/// <summary>The entries, from the player's on in the order the writer writes them; the count word is their number.</summary>
	public Es1PlayerMecEntry[] Entries { get; set; } = [];

	/// <summary>Per weapon, 1 when it is available (docs/es1/formats/bst-battle-sets.md#equipment-availability).</summary>
	public byte[] WeaponsAvailable { get; set; } = new byte[WeaponCount];
}

/// <summary>One pilot of an <see cref="Es1PlayerMec"/> and the HERC written after its two words.</summary>
public class Es1PlayerMecEntry {
	/// <summary>Pilot word 1, the simulator's record <c>+0x00</c>: a name index (docs/es1/formats/career-log-and-player-mec.md#the-player-file).</summary>
	public short Word1 { get; set; }

	/// <summary>Pilot word 2, the record's <c>+0x02</c>: the rank in a career, the entry's number in a single mission.</summary>
	public short Word2 { get; set; }

	/// <summary>
	/// The HERC, or null when the writer's slot held none and it wrote nothing in its place. The simulator reads a HERC for
	/// every entry, so a file with a null here is not read back as written
	/// (docs/es1/formats/career-log-and-player-mec.md#open).
	/// </summary>
	public Es1HercRecord? Herc { get; set; }
}
