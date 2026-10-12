namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// One of the shell's name lists: a count byte and that many NUL-terminated mission or list names
/// (docs/es1/formats/bst-battle-sets.md#name-lists). The whole of ES1's <c>bst\ANY_*.DAT</c>, and each position of a battle
/// set and each list of <c>msn\SING_MSN.DAT</c>.
/// </summary>
public class Es1NameList {
	/// <summary>The longest name a list holds, its NUL included: <c>StrList_Read</c> (<c>ES1GO 000201b7</c>) reads each into 13 bytes.</summary>
	public const int NameBufferLength = 13;

	/// <summary>The names, in file order; the count byte is their number.</summary>
	public string[] Names { get; set; } = [];

	/// <summary>
	/// The byte after a count of 0, which <c>StrList_Read</c> skips; null where the file ends at the count, as the retail
	/// <c>ANY_CNV.DAT</c> and <c>ANY_ESC.DAT</c> do.
	/// </summary>
	public byte? SkippedByte { get; set; }
}

/// <summary>
/// FILE - ES1's <c>bst\BTLSET_0.DAT</c>, <c>BTLSET_1.DAT</c>, <c>BTLSET_2.DAT</c> and <c>TEST_BTL.DAT</c> in
/// <c>ZONES.VOL</c>: a count byte of campaigns, each a count byte of positions and a name list per position
/// (docs/es1/formats/bst-battle-sets.md#battle-sets). Read by <see cref="Io.Transform.Shell.Es1BattleSetTransformer"/>.
/// </summary>
public class Es1BattleSet {
	public Es1Campaign[] Campaigns { get; set; } = [];
}

/// <summary>One campaign of a battle set: its mission positions in play order.</summary>
public class Es1Campaign {
	/// <summary>
	/// Per position, the list a mission is taken from: name 0 after outcome 0 and name 1 after outcome 1, or the only name
	/// when the list holds one (docs/es1/formats/bst-battle-sets.md#a-careers-missions).
	/// </summary>
	public Es1NameList[] Positions { get; set; } = [];
}

/// <summary>
/// FILE - ES1's <c>msn\SING_MSN.DAT</c> in <c>ZONES.VOL</c>: a count byte and that many name lists, the single missions'
/// lists (docs/es1/formats/bst-battle-sets.md#single-missions). Read by
/// <see cref="Io.Transform.Shell.Es1SingleMissionsTransformer"/>.
/// </summary>
public class Es1SingleMissions {
	public Es1NameList[] Lists { get; set; } = [];
}
