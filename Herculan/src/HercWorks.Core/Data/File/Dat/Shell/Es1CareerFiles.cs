namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - ES1's <c>car\CAR_INIT.DAT</c> (a new career) and <c>car\SM_INIT.DAT</c> (a single mission) in <c>SHELL0.VOL</c>: the
/// HERCs the player starts with, five header fields and a counted list of HERCs each with the hangar slot it goes in
/// (docs/es1/formats/car-hercs-and-stock.md#the-herc-lists). Read by <see cref="Io.Transform.Shell.Es1HercListTransformer"/>.
/// </summary>
public class Es1HercList {
	/// <summary>The number of hangar slots, the word <c>ES1GO 00080890</c>.</summary>
	public short SlotCount { get; set; }

	/// <summary>The word read into <c>ES1GO 00080894</c> (docs/es1/formats/car-hercs-and-stock.md#open).</summary>
	public short Word1 { get; set; }

	/// <summary>The slot whose HERC is read with <see cref="MarkedSlotWord"/>, the word <c>ES1GO 00080896</c>; −1 in both retail files.</summary>
	public short MarkedSlot { get; set; }

	/// <summary>The word passed to the picture build of <see cref="MarkedSlot"/>'s HERC when it is not 0, <c>ES1GO 00080898</c>.</summary>
	public short MarkedSlotWord { get; set; }

	/// <summary>The dword read into <c>ES1GO 0008089c</c>.</summary>
	public int Dword { get; set; }

	/// <summary>The HERCs, in file order; the count word is their number.</summary>
	public Es1HercListEntry[] Hercs { get; set; } = [];
}

/// <summary>One HERC of an <see cref="Es1HercList"/> and its hangar slot.</summary>
public class Es1HercListEntry {
	/// <summary>The hangar slot, an index into <see cref="Es1HercList.SlotCount"/> slots with no bounds test.</summary>
	public short Slot { get; set; }

	public Es1HercRecord Herc { get; set; } = new();
}

/// <summary>
/// A HERC as <c>Herc_ReadFromStream</c> (<c>ES1GO 0002352f</c>) reads it and <c>Herc_Write</c> (<c>000233ce</c>) writes it
/// (docs/es1/formats/car-hercs-and-stock.md#the-herc-record).
/// </summary>
public class Es1HercRecord {
	/// <summary>The status block's length in <c>int16</c>, 66 bytes.</summary>
	public const int StatusLength = 33;

	/// <summary>The chassis, 0–7.</summary>
	public short Chassis { get; set; }

	/// <summary>Per mount, the weapon it carries; 17 (<c>0x11</c>) is an empty mount. The count word is their number.</summary>
	public short[] Weapons { get; set; } = [];

	/// <summary>Per mount, the word read after the weapons (docs/es1/formats/car-hercs-and-stock.md#open).</summary>
	public short[] MountWords { get; set; } = [];

	/// <summary>The word at the HERC's <c>+0x2c</c>; the constructor sets 2.</summary>
	public short Word { get; set; }

	/// <summary>The 66-byte block at the HERC's <c>+0x2e</c>, <see cref="StatusLength"/> <c>int16</c>.</summary>
	public short[] Status { get; set; } = new short[StatusLength];
}

/// <summary>
/// FILE - ES1's <c>car\C_ARMORY.DAT</c> (a new career) and <c>car\S_ARMORY.DAT</c> (a single mission) in <c>SHELL0.VOL</c>: the
/// weapon stock, a counted list of records each pushing words onto one slot's stack
/// (docs/es1/formats/car-hercs-and-stock.md#the-weapon-stock). Read by <see cref="Io.Transform.Shell.Es1InventoryTransformer"/>.
/// </summary>
public class Es1Inventory {
	public Es1InventoryRecord[] Records { get; set; } = [];
}

/// <summary>One record of an <see cref="Es1Inventory"/>.</summary>
public class Es1InventoryRecord {
	/// <summary>The slot, with no bounds test.</summary>
	public short Slot { get; set; }

	/// <summary>The words pushed onto the slot's stack, in file order, so the last is its head; 100 in every retail record.</summary>
	public short[] Values { get; set; } = [];
}

/// <summary>
/// FILE - ES1's <c>car\NAMES.DAT</c> in <c>SHELL0.VOL</c>: a count byte of groups, each a count byte of pilot names
/// (docs/es1/formats/bst-battle-sets.md#battle-sets). Read by <see cref="Io.Transform.Shell.Es1PilotNamesTransformer"/>.
/// </summary>
public class Es1PilotNames {
	/// <summary>The longest name, its NUL included: <c>PilotNames_Load</c> (<c>ES1GO 00015070</c>) reads each into 16 bytes.</summary>
	public const int NameBufferLength = 16;

	/// <summary>The groups, each its names in file order.</summary>
	public string[][] Groups { get; set; } = [];
}
