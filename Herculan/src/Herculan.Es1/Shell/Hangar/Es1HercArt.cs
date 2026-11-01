using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Hangar;

/// <summary>
/// What the HERC diagrams are built from (docs/es1/formats/herc-diagrams.md): the three <c>cfg\</c> table families
/// <c>Shell_LoadData</c> (<c>00020630</c>) loads, the per-chassis art banks and the lookups and tables the HERC's file
/// reads them through. Retail keeps them as statics of the HERC's file; here they are one object of the shared data
/// (<see cref="Es1HercDiagrams.Art"/> of <see cref="Es1ShellData.HercDiagrams"/>), every member retail's.
/// </summary>
public sealed class Es1HercArt {
	/// <summary>The chassis the path tables list.</summary>
	public const int ChassisCount = 8;

	/// <summary>The weapon an empty mount carries, 17.</summary>
	public const short EmptyMount = Es1HercRecord.EmptyMount;

	/// <inheritdoc cref="Es1HercRecord.MountCounts"/>
	public static readonly short[] MountCounts = Es1HercRecord.MountCounts;

	/// <summary><c>Herc_MountDrawOrder</c> (<c>000803d8</c>), nine words per chassis, the mounts in the order the diagram draws them.</summary>
	public static readonly short[][] MountDrawOrder = [
		[1, 2, 0], [1, 2, 0], [3, 2, 1, 0], [4, 3, 2, 1, 0], [5, 2, 4, 1, 3, 0], [1, 6, 4, 3, 5, 2, 0],
		[1, 6, 4, 3, 7, 5, 2, 0], [1, 8, 3, 6, 4, 2, 5, 7, 0],
	];

	/// <summary>The layout files, the table at <c>000804d0</c>, in <c>cfg\</c>.</summary>
	private static readonly string[] LayoutFiles = ["ROAD.DAT", "GUNS.DAT", "RAPT.DAT", "TOMA.DAT", "PATR.DAT", "PANT.DAT", "HERC.DAT", "COLO.DAT"];

	/// <summary>The damage-mark files, the table at <c>0008057c</c>, in <c>cfg\</c>.</summary>
	private static readonly string[] DamageFiles = [
		"ROADDMG1.DAT", "GUNNDMG1.DAT", "RAPTDMG1.DAT", "TOMADMG1.DAT", "PATRDMG1.DAT", "PNTHDMG1.DAT", "HERCDMG1.DAT", "COLODMG1.DAT",
	];

	/// <summary>The chassis names the <c>WEP1</c>, <c>OUTL</c> and <c>WEPD</c> path tables (<c>000800f4</c>, <c>00080264</c>, <c>000801ac</c>) put before the bank's kind.</summary>
	private static readonly string[] BankNames = ["ROAD", "GUNS", "RAPT", "TOMA", "PATR", "PNTH", "HERC", "COLL"];

	/// <summary>The chassis names of the <c>DMG1</c> path table <c>0008031c</c>.</summary>
	private static readonly string[] DamageBankNames = ["ROAD", "GUNN", "RAPT", "TOMA", "PATR", "PNTH", "HERC", "COLO"];

	/// <summary>The chassis names of the <c>BILD</c> path table <c>000803b0</c>, for chassis 2 to 7 (none for 0 and 1).</summary>
	private static readonly string?[] BuildingBankNames = [null, null, "RAPT", "TOMA", "PATR", "PNTH", "HERC", "COLO"];

	/// <summary>The kinds of art bank, each a table of a bank per chassis.</summary>
	public enum Bank {
		/// <summary><c>&lt;chassis&gt;WEP1.DBA</c>, the table <c>0007ff9c</c>: the body and the weapons' pictures.</summary>
		Weapons,

		/// <summary><c>&lt;chassis&gt;OUTL.DBA</c>, <c>0007ffbc</c>: the mounts' outlines.</summary>
		Outlines,

		/// <summary><c>&lt;chassis&gt;DMG1.DBA</c>, <c>0007ffdc</c>: the damage marks.</summary>
		DamageMarks,

		/// <summary><c>&lt;chassis&gt;WEPD.DBA</c>, <c>0007fffc</c>: the damaged weapons' pictures.</summary>
		DamagedWeapons,

		/// <summary><c>&lt;chassis&gt;BILD.DBA</c>, <c>0008001c</c>: the body being built, chassis 2 to 7.</summary>
		Building,
	}

	private readonly GameContent? _content;

	/// <summary>Per kind of <see cref="Bank"/>, the eight chassis' banks; null a bank not loaded.</summary>
	private readonly IReadOnlyList<DynamixBitmap>?[][] _banks;

	/// <summary>The tables as the image holds them before <c>Shell_LoadData</c>: none, and no archive to load a bank from.</summary>
	public static Es1HercArt Empty() => new(null);

	private Es1HercArt(GameContent? content) {
		_content = content;
		_banks = Enumerable.Range(0, Enum.GetValues<Bank>().Length).Select(_ => new IReadOnlyList<DynamixBitmap>?[ChassisCount]).ToArray();
	}

	/// <summary>The words <c>000aae0e</c> + 6 × chassis and the mount arrays at <c>000aae0a</c>: each chassis' <c>cfg\&lt;chassis&gt;.DAT</c>; null one that did not read.</summary>
	public Es1HercLayout?[] Layouts { get; } = new Es1HercLayout?[ChassisCount];

	/// <summary>The lists at <c>000aae3a</c>: each chassis' <c>cfg\&lt;chassis&gt;DMG1.DAT</c>; null one that did not read.</summary>
	public Es1HercDamageMarks?[] DamageMarks { get; } = new Es1HercDamageMarks?[ChassisCount];

	/// <summary>
	/// The area arrays at <c>0008003c</c>, one per chassis <c>cfg\hotspot.dat</c> holds, each area's rect at its <c>+0</c>
	/// with the smaller corner first; the dword <c>0007fce4</c> points at them while a hangar screen tests the mouse
	/// against them (<see cref="Menus.Es1MenuGlobals.HangarAreas"/>). Empty when the file does not read.
	/// </summary>
	public Es1Rect[][] Hotspots { get; private set; } = [];

	/// <summary>
	/// <c>Shell_LoadData</c>'s loads of the HERC diagrams (docs/es1/formats/herc-diagrams.md): <c>HercLayout_Load</c>
	/// (<c>00021d0c</c>), <c>HercDamageMarks_Load</c> (<c>00022252</c>), <c>HercHotspots_Load</c> (<c>00022404</c>) and
	/// <c>HercArt_LoadAllBanks</c> (<c>0002280f</c>), in that order; <c>LoadDamageDat_Shell</c> runs between the second and
	/// the third, in <see cref="Es1RepairCosts.Load"/>. Retail reads whatever a missing file's stream gives; such a file is
	/// reported and left null here, an engine guard.
	/// </summary>
	public static Es1HercArt Load(GameContent content) {
		var art = new Es1HercArt(content);
		for (int chassis = 0; chassis < ChassisCount; chassis++) {
			art.Layouts[chassis] = ReadTable(content, LayoutFiles[chassis], new Es1HercLayoutTransformer());
			art.DamageMarks[chassis] = ReadTable(content, DamageFiles[chassis], new Es1HercDamageMarksTransformer());
		}

		if (ReadTable(content, "HOTSPOT.DAT", new Es1HercHotspotsTransformer()) is { } hotspots) {
			art.Hotspots = hotspots.Chassis.Select(chassis => chassis.Areas.Select(area => new Es1Rect(
				Math.Min(area.X0, area.X1), Math.Min(area.Y0, area.Y1), Math.Max(area.X0, area.X1), Math.Max(area.Y0, area.Y1))).ToArray()).ToArray();
		}

		art.LoadAllBanks();
		return art;
	}

	private static T? ReadTable<T>(GameContent content, string name, Es1WalkedFileTransformer<T> transformer) where T : class {
		try {
			if (content.Read("cfg", name) is { } bytes && transformer.Parse(bytes) is { } table) {
				return table;
			}
		} catch (InvalidDataException ex) {
			Console.Error.WriteLine($"cfg\\{name} does not read ({ex.Message}).");
			return null;
		}

		Console.Error.WriteLine($"cfg\\{name} is missing or unreadable: the HERC diagrams lack it.");
		return null;
	}

	/// <summary>
	/// <c>HercArt_LoadAllBanks</c> (<c>0002280f</c>): for each chassis its <c>WEP1</c>, <c>OUTL</c>, <c>DMG1</c> and
	/// <c>WEPD</c> banks, and for chassis 2 to 7 its <c>BILD</c> bank, each loaded over what the table held.
	/// </summary>
	private void LoadAllBanks() {
		for (short chassis = 0; chassis < ChassisCount; chassis++) {
			foreach (var bank in new[] { Bank.Weapons, Bank.Outlines, Bank.DamageMarks, Bank.DamagedWeapons }) {
				_banks[(int)bank][chassis] = LoadBank(bank, chassis);
			}

			if (chassis > 1) {
				_banks[(int)Bank.Building][chassis] = LoadBank(Bank.Building, chassis);
			}
		}
	}

	/// <summary>The <paramref name="bank"/> of <paramref name="chassis"/> as its table holds it; null when it is not loaded or the chassis is outside the eight.</summary>
	public IReadOnlyList<DynamixBitmap>? Loaded(Bank bank, short chassis) =>
		chassis is >= 0 and < ChassisCount ? _banks[(int)bank][chassis] : null;

	/// <summary>
	/// The <paramref name="bank"/> of <paramref name="chassis"/>, loaded with <c>Resource_LoadBitmapArray</c>
	/// (<c>0004b7df</c>) into its table when the table holds none, as <c>Herc_BuildDiagram</c>, <c>Herc_SetBodyPicture</c>,
	/// <c>MountPicture_Set</c> and <c>Herc_DrawDamageMarks</c> each do the first time a chassis needs one. Retail indexes
	/// the path tables with no bounds test; a chassis outside the eight, or one with no <c>BILD</c> bank, has none here, an
	/// engine guard.
	/// </summary>
	public IReadOnlyList<DynamixBitmap>? Need(Bank bank, short chassis) {
		if (chassis is < 0 or >= ChassisCount) {
			return null;
		}

		return _banks[(int)bank][chassis] ??= LoadBank(bank, chassis);
	}

	/// <summary>
	/// <c>HercArt_FreeChassisBanks</c> (<c>000228aa</c>)(<paramref name="chassis"/>): each of the chassis' five banks that
	/// is loaded destroyed and its table's entry nulled, so the next <see cref="Need"/> loads it again.
	/// <c>HercArt_FreeAllBanks</c> (<c>00022981</c>) runs it for the eight.
	/// </summary>
	public void FreeChassisBanks(short chassis) {
		if (chassis is < 0 or >= ChassisCount) {
			return;
		}

		foreach (var table in _banks) {
			table[chassis] = null;
		}
	}

	private IReadOnlyList<DynamixBitmap>? LoadBank(Bank bank, short chassis) {
		string? name = bank switch {
			Bank.Weapons => BankNames[chassis] + "WEP1.DBA",
			Bank.Outlines => BankNames[chassis] + "OUTL.DBA",
			Bank.DamagedWeapons => BankNames[chassis] + "WEPD.DBA",
			Bank.DamageMarks => DamageBankNames[chassis] + "DMG1.DBA",
			_ => BuildingBankNames[chassis] is { } building ? building + "BILD.DBA" : null,
		};

		if (name == null || _content == null) {
			return null;
		}

		var frames = _content.Read("res32", name) is { } bytes ? new DynamixBitmapArrayTransformer().Parse(bytes)?.Images : null;
		if (frames == null) {
			Console.Error.WriteLine($"res32\\{name} is missing or unreadable: the HERC diagrams draw without it.");
		}

		return frames;
	}

	/// <summary>
	/// Frame <paramref name="frame"/> of <paramref name="bank"/>, the bank's frame pointers at its <c>+8</c> read with no
	/// bounds test in retail; null here for no bank or a frame outside it, an engine guard.
	/// </summary>
	public static DynamixBitmap? Frame(IReadOnlyList<DynamixBitmap>? bank, int frame) =>
		bank != null && frame >= 0 && frame < bank.Count ? bank[frame] : null;

	/// <summary>
	/// <c>HercLayout_Find</c> (<c>00022024</c>)(<paramref name="weapon"/>, <paramref name="mount"/>,
	/// <paramref name="chassis"/>): the first of the chassis' mount's records whose weapon is the weapon, or null. Retail
	/// tests neither the mount nor the chassis; a mount or chassis the tables do not hold has no records here, an engine
	/// guard.
	/// </summary>
	public Es1HercLayoutItem? Find(short weapon, short mount, short chassis) {
		if (chassis is < 0 or >= ChassisCount || Layouts[chassis] is not { } layout || mount < 0 || mount >= layout.Mounts.Length) {
			return null;
		}

		return Array.Find(layout.Mounts[mount].Items, item => item.Weapon == weapon);
	}

	/// <summary><c>HercLayout_FindFrame</c> (<c>00022086</c>): the frame of <see cref="Find"/>'s record, or −1.</summary>
	public short FindFrame(short weapon, short mount, short chassis) => Find(weapon, mount, chassis)?.Frame ?? -1;

	/// <summary>
	/// <c>HercLayout_FindSecondPoint</c> (<c>0002219f</c>): the second point of <see cref="Find"/>'s record, which places
	/// the mount's outline, or (0, 0). <c>HercLayout_FindFirstPoint</c> (<c>000220ec</c>), its sibling for the first point,
	/// has no caller <c>es2_xref.py</c> finds (herc-diagrams.md's Open) and is not ported.
	/// </summary>
	public (int X, int Y) FindSecondPoint(short weapon, short mount, short chassis) =>
		Find(weapon, mount, chassis) is { } item ? (item.SecondX, item.SecondY) : (0, 0);
}
