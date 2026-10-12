namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's <c>dat\MFORMS.DAT</c>: an <c>int16</c> count, then that many 28-byte formations of seven <c>int16</c>
/// (X, Y) slots. The bytes are ES2's. Layout and use: docs/es1/formats/formations-dat.md. Read by
/// <see cref="Io.Transform.Dbsim.Es1MechFormationsTransformer"/>.
/// </summary>
public class Es1MechFormations {
	public Es1MechFormation[] Formations { get; set; } = [];
}

/// <summary>One HERC formation: slot <i>k</i> - 1 is where member <i>k</i> stands from the group's first member.</summary>
public class Es1MechFormation {
	public const int SlotCount = 7;

	/// <summary>The record's length in the file.</summary>
	public const int Length = 0x1c;

	public Es1FormationSlot16[] Slots { get; set; } = [];
}

/// <summary>
/// FILE - ES1's <c>dat\FFORMS.DAT</c>: an <c>int16</c> count, then that many 18-byte formations of three <c>int16</c>
/// (X, Y, Z) slots. The bytes are ES2's. Layout and use: docs/es1/formats/formations-dat.md. Read by
/// <see cref="Io.Transform.Dbsim.Es1FlyerFormationsTransformer"/>.
/// </summary>
public class Es1FlyerFormations {
	public Es1FlyerFormation[] Formations { get; set; } = [];
}

/// <summary>One flyer formation: slot <i>k</i> - 1 is where member <i>k</i> stands from the group's first member.</summary>
public class Es1FlyerFormation {
	public const int SlotCount = 3;

	/// <summary>The record's length in the file.</summary>
	public const int Length = 0x12;

	public Es1FormationSlot16[] Slots { get; set; } = [];
}

/// <summary>
/// FILE - ES1's <c>dat\BFORMS.DAT</c>: an <c>int16</c> count, then that many 56-byte formations of seven <c>int32</c>
/// (X, Y) slots, with none of ES2's per-slot headings or ground patch. Layout and use:
/// docs/es1/formats/formations-dat.md. Read by <see cref="Io.Transform.Dbsim.Es1BaseFormationsTransformer"/>.
/// </summary>
public class Es1BaseFormations {
	public Es1BaseFormation[] Formations { get; set; } = [];
}

/// <summary>One structure formation: slot <i>k</i> - 1 is where member <i>k</i> stands from the group's first member.</summary>
public class Es1BaseFormation {
	public const int SlotCount = 7;

	/// <summary>The record's length in the file.</summary>
	public const int Length = 0x38;

	public Es1FormationSlot32[] Slots { get; set; } = [];
}

/// <summary>An <c>int16</c> slot offset; <see cref="Z"/> is in the file only in <c>FFORMS.DAT</c>, and 0 in a HERC slot.</summary>
public class Es1FormationSlot16 {
	public short X { get; set; }

	public short Y { get; set; }

	public short Z { get; set; }
}

/// <summary>A <c>BFORMS.DAT</c> slot offset, two <c>int32</c>.</summary>
public class Es1FormationSlot32 {
	public int X { get; set; }

	public int Y { get; set; }
}
