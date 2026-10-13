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
/// FILE - ES1's <c>car\NAMES.LOG</c> in <c>SHELL0.VOL</c>: the careers log, a count, the current entry and that many
/// 100-byte entries each naming a career and its two files (docs/es1/formats/career-log-and-player-mec.md#the-careers-log).
/// Read by <see cref="Io.Transform.Shell.Es1CareerLogTransformer"/>.
/// </summary>
public class Es1CareerLog {
	/// <summary>
	/// The entry the log selects, written only when there are entries; <c>CareerLog_Load</c> (<c>ES1GO 00017e7f</c>) sets −1
	/// when there are none, and the parse does the same.
	/// </summary>
	public short Current { get; set; } = -1;

	/// <summary>The entries, in file order; the count word is their number.</summary>
	public Es1CareerLogEntry[] Entries { get; set; } = [];
}

/// <summary>
/// One 100-byte entry of an <see cref="Es1CareerLog"/>, written from memory whole: each name is NUL-terminated in its
/// buffer, and the buffer's bytes after the NUL are kept as read.
/// </summary>
public class Es1CareerLogEntry {
	/// <summary>An entry's length.</summary>
	public const int Length = 100;

	/// <summary>The career name's buffer length.</summary>
	public const int NameLength = 0x40;

	/// <summary>Each file path's buffer length.</summary>
	public const int PathLength = 17;

	/// <summary>The career's name, <c>+0x00</c>.</summary>
	public byte[] Name { get; set; } = new byte[NameLength];

	/// <summary>The word <c>+0x40</c>, which <c>CareerLog_FindOrAdd</c> (<c>ES1GO 00018099</c>) matches with the name.</summary>
	public short Word { get; set; }

	/// <summary>The career file's path, <c>CAR\CAR_</c><i>n</i><c>.CAR</c>, <c>+0x42</c>.</summary>
	public byte[] CareerFile { get; set; } = new byte[PathLength];

	/// <summary>The hangar file's path, <c>CAR\CAR_</c><i>n</i><c>.MEC</c>, <c>+0x53</c>.</summary>
	public byte[] HangarFile { get; set; } = new byte[PathLength];
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
