namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - ES1's <c>bst\TECH_0.DAT</c> (a career) and <c>bst\TECH_SNG.DAT</c> (single missions) in <c>ZONES.VOL</c>: an
/// <c>int16</c> count and that many 8-byte records, each saying after how many career missions a chassis or a weapon
/// becomes available (docs/es1/formats/bst-battle-sets.md#equipment-availability). Read by
/// <see cref="Io.Transform.Shell.Es1TechFileTransformer"/>.
/// </summary>
public class Es1TechFile {
	public Es1TechRecord[] Records { get; set; } = [];
}

/// <summary>One chassis or weapon's availability.</summary>
public class Es1TechRecord {
	/// <summary>The record's length in the file.</summary>
	public const int Length = 8;

	/// <summary>0 a chassis, one of 8; 1 a weapon, one of 17. The load ignores any other kind.</summary>
	public short Kind { get; set; }

	/// <summary>The chassis or weapon, an index into the kind's table with no bounds test.</summary>
	public short Index { get; set; }

	/// <summary>
	/// The number of career missions played at which it becomes available when the last mission's grade is below 2. A −1
	/// here or in <see cref="AnyGradeMissions"/> keeps it unavailable.
	/// </summary>
	public short GoodGradeMissions { get; set; }

	/// <summary>The number of career missions played at which it becomes available whatever the grade.</summary>
	public short AnyGradeMissions { get; set; }
}
