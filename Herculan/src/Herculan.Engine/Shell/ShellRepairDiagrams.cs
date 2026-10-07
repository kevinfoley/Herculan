using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The repair screen's damage diagram: the machine drawn as parts on a blue grid down the left of the
/// canvas, each part recoloured by its component's damage band, with clickable areas over it. Two
/// pictures share the rect — the exploded external one while the selection is in the external list
/// and the internals one while it is in the internals list. See docs/retail/shell/screen-layout.md, "The
/// damage diagram".
///
/// <para><b>Both pictures are <see cref="ShellGrid"/> widgets</b>, whose remap pairs are what colour
/// each part by its damage band at paint time rather than in its frame.</para>
/// </summary>
public sealed class ShellRepairDiagrams {
	/// <summary>
	/// Where <c>Repair_BuildDiagrams</c> (<c>004140a9</c>) moves the squad panel's eight pictures for
	/// this tab, the exploded external ones: <c>{0x10, 0x2f}</c>, sized <c>0xd0</c> by <c>0x100</c>,
	/// with the far corner inclusive.
	/// </summary>
	public static readonly ShellRect ExternalPictureRect = new(0x10, 0x2f, 0x10 + 0xd0 - 1, 0x2f + 0x100 - 1);

	/// <summary>
	/// The literal rect <c>Repair_BuildScreen</c> (<c>00432037</c>) builds the eight internals pictures
	/// at, one pixel wider and taller than the external picture.
	/// </summary>
	public static readonly ShellRect InternalsPictureRect = new(0x10, 0x2f, 0xe0, 0x12f);

	/// <summary>
	/// The squad panel's empty picture (<c>DAT_0048d4dc</c>), shown when no bay is selected. It keeps the
	/// builder's rect, since <c>Repair_BuildDiagrams</c> moves only the eight per-bay pictures, and has
	/// its grid lines switched off.
	/// </summary>
	public static readonly ShellRect EmptyPictureRect = new(5, 0x2b, 0xeb, 0x130);

	/// <summary>The part slot a mount's weapon lands in: <c>slot + 6</c>, past the six body groups.</summary>
	public const int FirstWeaponPart = 6;

	/// <summary>The ink a chassis part is drawn in and remapped from, <c>Repair_BuildDiagrams</c>'s <c>0xe</c>.</summary>
	private const byte BodyInk = 0x0e;

	/// <summary>And a weapon part's, <c>0xf</c>.</summary>
	private const byte WeaponInk = 0x0f;

	/// <summary>
	/// The nine indices the internals picture paints its components in, one per internal in list order —
	/// the table at <c>0046fe80</c>, which <c>Repair_ColorDiagram</c> (<c>0041469a</c>) writes as slot 0's remap sources.
	/// </summary>
	private static readonly byte[] InternalInks = { 0x1d, 0x1c, 0x18, 0x19, 0x17, 0x1e, 0x16, 0x1f, 0x1b };

	/// <summary>The files, in chassis-type order — the tables at <c>0046fe94</c>, <c>0046fedc</c> and <c>0046ff04</c>.</summary>
	private static readonly string[] ChassisStems = { "OUTL", "RAPT", "TOMA", "SAMS", "COLO", "APOC", "OGRE", "MAVR", "RAZR" };

	private const string WeaponBankName = "RPR_WPNS";
	private const string HotspotResourceName = "RPR_HOTS.DAT";

	/// <summary>The Razor's second internals part: frame 1 of its own <c>_int</c> bank at <c>(0x1d, 0xe)</c>, in slot 1.</summary>
	private const int FlyerChassisType = 8;
	private const int FlyerSecondPartX = 0x1d;
	private const int FlyerSecondPartY = 0x0e;
	private const int FlyerSecondPartFrame = 1;

	private readonly RprHerc?[] _layouts;
	private readonly DynamixBitmap[]?[] _bodyBanks;
	private readonly DynamixBitmap[]?[] _internalBanks;
	private readonly DynamixBitmap[]? _weaponBank;
	private readonly HardpointOverlayConfig? _hotspots;

	private ShellRepairDiagrams(RprHerc?[] layouts, DynamixBitmap[]?[] bodyBanks,
			DynamixBitmap[]?[] internalBanks, DynamixBitmap[]? weaponBank, HardpointOverlayConfig? hotspots) {
		_layouts = layouts;
		_bodyBanks = bodyBanks;
		_internalBanks = internalBanks;
		_weaponBank = weaponBank;
		_hotspots = hotspots;
	}

	/// <summary>
	/// Reads the nine <c>gam\rpr_*.dat</c> layouts, <c>gam\rpr_hots.dat</c>, and the nineteen
	/// <c>dba\</c> banks they draw from. Anything missing leaves that chassis's picture or hotspots
	/// empty rather than failing the screen.
	/// </summary>
	public static ShellRepairDiagrams Load(GameContent content) {
		var layouts = new RprHerc?[ChassisStems.Length];
		var bodyBanks = new DynamixBitmap[]?[ChassisStems.Length];
		var internalBanks = new DynamixBitmap[]?[ChassisStems.Length];

		for (int type = 0; type < ChassisStems.Length; type++) {
			layouts[type] = content.Read(ShellRepairCosts.CatalogFolder, $"RPR_{ChassisStems[type]}.DAT") is { } bytes
				? new RprHercTransform().Parse(bytes) : null;
			bodyBanks[type] = ShellArt.ReadBankFrames(content, $"RPR_{ChassisStems[type]}");
			internalBanks[type] = ShellArt.ReadBankFrames(content, $"{ChassisStems[type]}_INT");
		}

		var hotspots = content.Read(ShellRepairCosts.CatalogFolder, HotspotResourceName) is { } hotBytes
			? new HardpointOverlayTransformer().Parse(hotBytes) : null;

		return new ShellRepairDiagrams(layouts, bodyBanks, internalBanks,
			ShellArt.ReadBankFrames(content, WeaponBankName), hotspots);
	}

	/// <summary>How many chassis have a layout — nine when the archive is complete.</summary>
	public int LayoutCount => _layouts.Count(layout => layout != null);

	/// <summary>
	/// Paints the picture a selection in <paramref name="column"/> shows for
	/// <paramref name="machine"/>, or the empty picture when there is none.
	/// </summary>
	public void Paint(ShellSurface surface, ShellBayMachine? machine, int column) {
		if (machine == null) {
			ShellGrid.Paint(surface, EmptyPictureRect, gridLines: false, Array.Empty<ShellGridPart>());
			return;
		}

		if (column == 0) {
			ShellGrid.Paint(surface, ExternalPictureRect, gridLines: true, ExternalParts(machine));
		} else {
			ShellGrid.Paint(surface, InternalsPictureRect, gridLines: true, InternalParts(machine));
		}
	}

	/// <summary>
	/// The build screen's blueprint of one chassis, <c>Build_FillBlueprints</c> (<c>0041579d</c>): the
	/// same <c>rpr_*.dat</c> body records and <c>rpr_</c> banks as the exploded external picture, each in
	/// the slot its id names with the record's flags, and no weapons. Every part's remap pair is
	/// <c>0xe</c> to <c>0xe</c>, which <c>ESGrid_Paint</c> applies and which changes nothing, so the parts
	/// show in their own ink.
	/// </summary>
	public void PaintBlueprint(ShellSurface surface, ShellRect rect, int chassisType, byte borderColor,
			byte lineColor) {
		var parts = new ShellGridPart?[ShellGrid.PartSlots];
		if (chassisType >= 0 && chassisType < _layouts.Length && _layouts[chassisType] is { } layout) {
			foreach (var (id, record) in layout.BodyImages ?? new()) {
				SetPart(parts, id, Frame(_bodyBanks[chassisType], record.FrameId), record.OriginX, record.OriginY,
					record.BlitFlags?.Val ?? 0, BodyInk, BodyInk);
			}
		}

		ShellGrid.Paint(surface, rect, gridLines: true, parts, borderColor, lineColor);
	}

	/// <summary>
	/// The picture-column row a canvas point selects, or null — the panels
	/// <c>Hotspots_BuildOverlay</c> (<c>0043c1a0</c>) lays over the external picture. Rows 0-5 are the
	/// six <c>rpr_hots.dat</c> areas for the chassis, row <c>6 + slot</c> the rect of the weapon part
	/// fitted in that mount. The panels are built with no chrome, so nothing of them is drawn.
	///
	/// <para>Where panels overlap, the one built last answers, and a point off the picture reaches none of
	/// them: the builder makes the six areas and then the weapons, so the test runs weapons from the
	/// highest mount down and then areas from 5 down. See docs/retail/shell/screen-layout.md#which-widget-a-click-reaches.</para>
	/// </summary>
	public int? HotspotAt(ShellBayMachine? machine, float canvasX, float canvasY) {
		if (machine == null || !ExternalPictureRect.Contains(canvasX, canvasY)) {
			return null;
		}

		float x = canvasX - ExternalPictureRect.X0;
		float y = canvasY - ExternalPictureRect.Y0;

		for (int mount = machine.MountCapacity - 1; mount >= 0; mount--) {
			// Repair_WeaponPartRect (00414418) — the part's own rect, one pixel past its frame on both axes as it is written.
			if (WeaponRecord(machine, mount) is { } record && Frame(_weaponBank, record.FrameId) is { } frame
				&& x >= record.OriginX && y >= record.OriginY
				&& x <= record.OriginX + frame.Cols && y <= record.OriginY + frame.Rows) {
				return FirstWeaponPart + mount;
			}
		}

		var areas = _hotspots?.Entries?.FirstOrDefault(entry => entry.HercId == machine.ChassisType)?.Areas;
		for (int row = (areas?.Length ?? 0) - 1; row >= 0; row--) {
			if (areas![row] is { } area && x >= area.X0 && y >= area.Y0 && x <= area.X1 && y <= area.Y1) {
				return row;
			}
		}

		return null;
	}

	/// <summary>
	/// The exploded external picture, as <c>Repair_BuildDiagrams</c> fills it and <c>Repair_ColorDiagram</c> (<c>0041469a</c>)
	/// colours it: one part per <c>rpr_*.dat</c> body record in the slot its id names, remapping index
	/// <c>0xe</c> to its group's band colour, and one per fitted mount in slot <c>6 + mount</c> from the
	/// shared weapons bank, remapping <c>0xf</c> to that mount's band colour.
	/// </summary>
	private ShellGridPart?[] ExternalParts(ShellBayMachine machine) {
		var parts = new ShellGridPart?[ShellGrid.PartSlots];
		int type = machine.ChassisType;
		if (type < 0 || type >= _layouts.Length || _layouts[type] is not { } layout) {
			return parts;
		}

		foreach (var (id, record) in layout.BodyImages ?? new()) {
			// Ids 16 and up are a second part for the same group — the Razor's. Repair_ColorDiagram (0041469a) folds them
			// back onto 0-5 to read the condition, and still colours the slot the id names.
			int group = id > 0xf ? id - 0x10 : id;
			int condition = machine.Condition(ShellRepairCategory.ExternalGroup, group);
			SetPart(parts, id, Frame(_bodyBanks[type], record.FrameId), record.OriginX, record.OriginY,
				record.BlitFlags?.Val ?? 0, BodyInk, ShellRepairScreen.BandColor(condition));
		}

		for (int mount = 0; mount < machine.MountCapacity; mount++) {
			if (WeaponRecord(machine, mount) is not { } record) {
				continue;
			}

			int condition = machine.Condition(ShellRepairCategory.Hardpoint, mount);
			SetPart(parts, record.Id, Frame(_weaponBank, record.FrameId), record.OriginX, record.OriginY,
				record.BlitFlags?.Val ?? 0, WeaponInk, ShellRepairScreen.BandColor(condition));
		}

		return parts;
	}

	/// <summary>
	/// The internals picture: the chassis's single internals record in slot 0, whose nine component
	/// inks each remap to that internal's band colour. The Razor adds a second, uncoloured part.
	/// </summary>
	private ShellGridPart?[] InternalParts(ShellBayMachine machine) {
		var parts = new ShellGridPart?[ShellGrid.PartSlots];
		int type = machine.ChassisType;
		if (type < 0 || type >= _layouts.Length || _layouts[type]?.InternalImage is not { } record) {
			return parts;
		}

		var remaps = new (byte From, byte To)[InternalInks.Length];
		for (int i = 0; i < remaps.Length; i++) {
			remaps[i] = (InternalInks[i],
				ShellRepairScreen.BandColor(machine.Condition(ShellRepairCategory.Internal, i)));
		}

		if (record.Id >= 0 && record.Id < ShellGrid.PartSlots
			&& Frame(_internalBanks[type], record.FrameId) is { } frame) {
			// Repair_BuildDiagrams passes 0 for the internals part's flags rather than the record's.
			parts[record.Id] = new ShellGridPart(frame, record.OriginX, record.OriginY, 0, remaps);
		}

		if (type == FlyerChassisType && Frame(_internalBanks[type], FlyerSecondPartFrame) is { } second) {
			parts[1] = new ShellGridPart(second, FlyerSecondPartX, FlyerSecondPartY, 0, Array.Empty<(byte, byte)>());
		}

		return parts;
	}

	/// <summary>
	/// <c>RepairLayout_FindWeaponPart</c> (<c>00413ccc</c>) — the weapon part for one mount: the record in the fitted weapon's group
	/// whose id is <c>slot + 6</c>. An empty mount, or a weapon the chassis has no art for in that
	/// socket, has none.
	/// </summary>
	private UiHardpointGraphic? WeaponRecord(ShellBayMachine machine, int mount) {
		int weaponId = machine.WeaponAt(mount);
		if (weaponId <= 0 || machine.ChassisType < 0 || machine.ChassisType >= _layouts.Length
			|| _layouts[machine.ChassisType]?.WeaponHardpoints is not { } groups
			|| !groups.TryGetValue((short)weaponId, out var sockets)) {
			return null;
		}

		return sockets.FirstOrDefault(socket => socket.Id == FirstWeaponPart + mount);
	}

	private static void SetPart(ShellGridPart?[] parts, int slot, DynamixBitmap? frame, int x, int y, int flags,
			byte ink, byte color) {
		if (slot >= 0 && slot < ShellGrid.PartSlots && frame != null) {
			parts[slot] = new ShellGridPart(frame, x, y, flags, new[] { (ink, color) });
		}
	}

	private static DynamixBitmap? Frame(DynamixBitmap[]? bank, int frame) =>
		bank != null && frame >= 0 && frame < bank.Length ? bank[frame] : null;
}
