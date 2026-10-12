namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - one of ES1's eight <c>cfg\&lt;chassis&gt;.DAT</c> in <c>SHELL0.VOL</c> (<c>ROAD</c>, <c>GUNS</c>, <c>RAPT</c>,
/// <c>TOMA</c>, <c>PATR</c>, <c>PANT</c>, <c>HERC</c>, <c>COLO</c>): per mount of the chassis, a record per weapon placing the
/// weapon's picture on the HERC diagram (docs/es1/formats/herc-diagrams.md#the-mount-pictures). Read by
/// <see cref="Io.Transform.Shell.Es1HercLayoutTransformer"/>.
/// </summary>
public class Es1HercLayout {
	public Es1HercLayoutMount[] Mounts { get; set; } = [];
}

/// <summary>One mount's records.</summary>
public class Es1HercLayoutMount {
	public Es1HercLayoutItem[] Items { get; set; } = [];
}

/// <summary>One weapon's record for a mount; the file holds its fields in this order, each an <c>int16</c>.</summary>
public class Es1HercLayoutItem {
	/// <summary>The record's length in the file.</summary>
	public const int Length = 12;

	/// <summary>The frame, the word the lookup by weapon returns.</summary>
	public short Frame { get; set; }

	/// <summary>The weapon the record is looked up by; 17 (<c>0x11</c>) for an empty mount.</summary>
	public short Weapon { get; set; }

	/// <summary>The first point, widened to <c>int32</c> at load.</summary>
	public short FirstX { get; set; }

	public short FirstY { get; set; }

	/// <summary>The second point, widened to <c>int32</c> at load.</summary>
	public short SecondX { get; set; }

	public short SecondY { get; set; }
}

/// <summary>
/// FILE - one of ES1's eight <c>cfg\&lt;chassis&gt;DMG1.DAT</c> in <c>SHELL0.VOL</c>: for each of six component groups and three
/// damage levels, the marks drawn over the HERC diagram from the chassis's <c>RES32\&lt;chassis&gt;DMG1.DBA</c>
/// (docs/es1/formats/herc-diagrams.md#the-damage-marks). Read by <see cref="Io.Transform.Shell.Es1HercDamageMarksTransformer"/>.
/// </summary>
public class Es1HercDamageMarks {
	/// <summary>The component groups in the file.</summary>
	public const int GroupCount = 6;

	/// <summary>The damage levels per group.</summary>
	public const int LevelCount = 3;

	/// <summary><see cref="GroupCount"/> groups, each <see cref="LevelCount"/> lists of marks.</summary>
	public Es1DamageMark[][][] Groups { get; set; } = [];
}

/// <summary>One mark, 10 bytes read whole.</summary>
public class Es1DamageMark {
	/// <summary>The record's length in the file.</summary>
	public const int Length = 10;

	/// <summary>The frame of the chassis's <c>DMG1</c> bank.</summary>
	public short Frame { get; set; }

	public int X { get; set; }

	public int Y { get; set; }
}

/// <summary>
/// FILE - ES1's <c>cfg\HOTSPOT.DAT</c> in <c>SHELL0.VOL</c>: per chassis, the rectangles laid over its HERC diagram
/// (docs/es1/formats/herc-diagrams.md#the-clickable-areas). Read by <see cref="Io.Transform.Shell.Es1HercHotspotsTransformer"/>.
/// </summary>
public class Es1HercHotspots {
	public Es1HotspotChassis[] Chassis { get; set; } = [];
}

/// <summary>One chassis's areas.</summary>
public class Es1HotspotChassis {
	public Es1HotspotArea[] Areas { get; set; } = [];
}

/// <summary>
/// One area as the file holds it: two corners, each a <c>uint16</c> x and y. The load orders them into a rect whose first
/// corner is the smaller x and the smaller y.
/// </summary>
public class Es1HotspotArea {
	/// <summary>The record's length in the file.</summary>
	public const int Length = 8;

	public ushort X0 { get; set; }

	public ushort Y0 { get; set; }

	public ushort X1 { get; set; }

	public ushort Y1 { get; set; }
}
