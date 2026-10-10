using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The squad panel's picture of the selected bay on the WEAPONS, BUILD and CREW tabs: the machine in
/// three-quarter view as two stacked halves, with each fitted weapon drawn over its socket. Filled by
/// <c>Squad_BuildBayPictures</c> (<c>00414e5b</c>) from <c>gam\arm_*.dat</c> into the eight bay pictures <c>Squad_BuildRosterList</c>
/// built, and by the same function into the empty picture shown when no bay is selected. See
/// docs/retail/shell/squad-and-crew.md, "The bay picture".
/// </summary>
public sealed class ShellBayPictures {
	/// <summary>
	/// Where <c>Squad_BuildBayPictures</c> (<c>00414e5b</c>) puts the eight bay pictures: moved to <c>{5, 0x2b}</c> and sized
	/// <c>0xe7</c> by <c>0x105</c>, which with the far corner inclusive is one row short of the rect
	/// they were built at.
	/// </summary>
	public static readonly ShellRect PictureRect = new(5, 0x2b, 5 + 0xe7 - 1, 0x2b + 0x105 - 1);

	/// <summary>The empty picture (<c>DAT_0048d4dc</c>), which keeps <c>Squad_BuildRosterList</c>'s own rect.</summary>
	public static readonly ShellRect EmptyPictureRect = new(5, 0x2b, 0xeb, 0x130);

	/// <summary>The bank the empty picture and an unstarted machine are drawn from: two halves of an empty bay.</summary>
	private const string EmptyBayBankName = "MT_3QTR";

	/// <summary>Where the empty bay's two halves sit, both with flags 0.</summary>
	private const int EmptyHalfX = 1;
	private const int EmptyTopY = 1;
	private const int EmptyBottomY = 0x8c;

	/// <summary>
	/// The part slot a mount's weapon is looked up by in its weapon's group, <c>slot + 2</c> — past the
	/// two halves of the body, which every retail layout puts in slots 0 and 1.
	/// </summary>
	private const int FirstWeaponPart = 2;

	/// <summary>
	/// The body frames an unfinished machine shows: 2 and 3 below this build percentage, 4 and 5 from it
	/// on. The retail banks do not all carry them — <c>out_bod</c> and <c>mav_bod</c> have two frames,
	/// <c>rap_bod</c> and <c>tom_bod</c> four and the rest six — and a frame the bank lacks draws nothing.
	/// </summary>
	private const int HalfBuiltPercent = 0x33;
	private const int EarlyBuildTopFrame = 2;
	private const int LateBuildTopFrame = 4;

	/// <summary>The layout files, in chassis-type order — the table at <c>0046ff2c</c>.</summary>
	private static readonly string[] LayoutStems = { "OUTL", "RAPT", "TOMA", "SAMS", "COLO", "APOC", "OGRE", "MAVR", "RAZR" };

	/// <summary>
	/// The banks' own stems, in the same order — the tables at <c>0046ff74</c> (<c>_bod</c>),
	/// <c>0046ff9c</c> (<c>_wep</c>) and <c>0046ffc0</c> (<c>_out</c>, the socket outlines). They are not
	/// the layout stems: the Razor's is <c>fly</c>.
	/// </summary>
	private static readonly string[] BankStems = { "OUT", "RAP", "TOM", "SAM", "COL", "APOC", "OGR", "MAV", "FLY" };

	/// <summary>
	/// The part slot <c>Arming_MarkHardpoint</c> (<c>004155db</c>) puts the selected socket's outline in,
	/// past every weapon slot the widest chassis uses.
	/// </summary>
	private const int OutlineSlot = 12;

	/// <summary>The outline's one remap pair: the bank's index <c>0xba</c> drawn as <c>99</c>.</summary>
	private const byte OutlineInk = 0xba;
	private const byte OutlineColor = 99;

	private const string HotspotResourceName = "ARM_HOTS.DAT";

	private readonly ArmHerc?[] _layouts;
	private readonly DynamixBitmap[]?[] _bodyBanks;
	private readonly DynamixBitmap[]?[] _weaponBanks;
	private readonly DynamixBitmap[]?[] _outlineBanks;
	private readonly DynamixBitmap[]? _emptyBay;
	private readonly HardpointOverlayConfig? _hotspots;

	private ShellBayPictures(ArmHerc?[] layouts, DynamixBitmap[]?[] bodyBanks, DynamixBitmap[]?[] weaponBanks,
			DynamixBitmap[]?[] outlineBanks, DynamixBitmap[]? emptyBay, HardpointOverlayConfig? hotspots) {
		_layouts = layouts;
		_bodyBanks = bodyBanks;
		_weaponBanks = weaponBanks;
		_outlineBanks = outlineBanks;
		_emptyBay = emptyBay;
		_hotspots = hotspots;
	}

	/// <summary>
	/// Reads the nine <c>gam\arm_*.dat</c> layouts, <c>gam\arm_hots.dat</c> and the banks they draw
	/// from. Anything missing leaves that chassis's picture or hotspots empty rather than failing the
	/// screen.
	/// </summary>
	public static ShellBayPictures Load(GameContent content) {
		var layouts = new ArmHerc?[LayoutStems.Length];
		var bodyBanks = new DynamixBitmap[]?[LayoutStems.Length];
		var weaponBanks = new DynamixBitmap[]?[LayoutStems.Length];
		var outlineBanks = new DynamixBitmap[]?[LayoutStems.Length];

		for (int type = 0; type < LayoutStems.Length; type++) {
			layouts[type] = content.Read(ShellRepairCosts.CatalogFolder, $"ARM_{LayoutStems[type]}.DAT") is { } bytes
				? new ArmHercTransformer().Parse(bytes) : null;
			bodyBanks[type] = ShellArt.ReadBankFrames(content, $"{BankStems[type]}_BOD");
			weaponBanks[type] = ShellArt.ReadBankFrames(content, $"{BankStems[type]}_WEP");
			outlineBanks[type] = ShellArt.ReadBankFrames(content, $"{BankStems[type]}_OUT");
		}

		var hotspots = content.Read(ShellRepairCosts.CatalogFolder, HotspotResourceName) is { } hotBytes
			? new HardpointOverlayTransformer().Parse(hotBytes) : null;

		return new ShellBayPictures(layouts, bodyBanks, weaponBanks, outlineBanks,
			ShellArt.ReadBankFrames(content, EmptyBayBankName), hotspots);
	}

	/// <summary>
	/// Whether the chassis's layout has a record for <paramref name="weaponId"/> in <paramref name="mount"/>'s
	/// socket, whatever its frame — <c>Arming_RowLive</c> (<c>004149fb</c>)'s test of
	/// <c>RepairLayout_FindWeaponPart</c>. Every retail layout has a <c>None</c> (0) record for every socket.
	/// </summary>
	public bool HasSocket(int chassisType, int weaponId, int mount) =>
		chassisType >= 0 && chassisType < _layouts.Length && _layouts[chassisType] is { } layout
		&& FindRecord(layout, weaponId, mount) != null;

	/// <summary>
	/// The outline <c>Arming_MarkHardpoint</c> (<c>004155db</c>) puts in part slot 12 for
	/// <paramref name="mount"/>: the socket's record for the weapon fitted there, <c>None</c>'s for an
	/// empty mount, drawn from the chassis's <c>_out</c> bank at the record's second position, one pixel
	/// in as the loader leaves it, with <c>0xba</c> remapped to <c>99</c>. Null when there is no record or
	/// its frame is <c>-1</c>, in which case the original leaves whatever outline the slot already holds.
	/// </summary>
	public ShellGridPart? Outline(ShellBayMachine machine, int mount) {
		int type = machine.ChassisType;
		if (type < 0 || type >= _layouts.Length || _layouts[type] is not { } layout
			|| FindRecord(layout, machine.WeaponAt(mount), mount) is not { FrameId: not -1 } record
			|| Frame(_outlineBanks[type], record.FrameId) is not { } frame) {
			return null;
		}

		return new ShellGridPart(frame, record.OutlineX + 1, record.OutlineY + 1, record.BlitFlags?.Val ?? 0,
			new[] { (OutlineInk, OutlineColor) });
	}

	/// <summary>
	/// The hardpoint whose hotspot is under a canvas point, or null — the panels
	/// <c>Hotspots_BuildOverlay(2)</c> (<c>0043c1a0</c>) lays over each bay's picture, one per mount below
	/// the capacity from the chassis's <c>arm_hots.dat</c> areas, which are rects in the picture. They are
	/// built with no chrome, so nothing of them is drawn. Where two overlap the one built later, the
	/// higher mount, answers, and a point off the picture reaches none of them.
	/// </summary>
	public int? HotspotAt(ShellBayMachine? machine, float canvasX, float canvasY) {
		if (machine == null || !PictureRect.Contains(canvasX, canvasY)) {
			return null;
		}

		var areas = _hotspots?.Entries?.FirstOrDefault(entry => entry.HercId == machine.ChassisType)?.Areas;
		float x = canvasX - PictureRect.X0;
		float y = canvasY - PictureRect.Y0;
		for (int mount = Math.Min(machine.MountCapacity, areas?.Length ?? 0) - 1; mount >= 0; mount--) {
			if (areas![mount] is { } area && x >= area.X0 && y >= area.Y0 && x <= area.X1 && y <= area.Y1) {
				return mount;
			}
		}

		return null;
	}

	/// <summary>How many chassis have a layout — nine when the archive is complete.</summary>
	public int LayoutCount => _layouts.Count(layout => layout != null);

	/// <summary>
	/// Paints the picture <c>Squad_ShowPanel</c> puts up: the selected bay's, or the empty picture when
	/// no bay is selected. Both have their grid lines off. <paramref name="outline"/> is the weapons
	/// screen's socket outline, drawn in part slot 12 of a bay's picture.
	/// </summary>
	public void Paint(ShellSurface surface, ShellHangar hangar, int selectedBay, ShellGridPart? outline = null) {
		if (selectedBay < 0) {
			ShellGrid.Paint(surface, EmptyPictureRect, gridLines: false, EmptyBayParts());
			return;
		}

		var parts = hangar.Bay(selectedBay) is { } machine ? MachineParts(machine) : EmptyBayParts();
		if (outline != null) {
			parts[OutlineSlot] = outline;
		}

		ShellGrid.Paint(surface, PictureRect, gridLines: false, parts);
	}

	/// <summary>The empty bay: <c>mt_3qtr</c>'s two frames, top and bottom, in slots 0 and 1.</summary>
	private ShellGridPart?[] EmptyBayParts() {
		var parts = new ShellGridPart?[ShellGrid.PartSlots];
		SetPart(parts, 0, Frame(_emptyBay, 0), EmptyHalfX, EmptyTopY, 0);
		SetPart(parts, 1, Frame(_emptyBay, 1), EmptyHalfX, EmptyBottomY, 0);
		return parts;
	}

	/// <summary>
	/// One machine: the layout's two body records, each in the slot its id names, and each fitted
	/// mount's record from its weapon's group. The body's frames follow the build: the record's own
	/// frame once the machine is finished, the empty bay while it is at 0%, and the body bank's frames 2
	/// and 3 or 4 and 5 in between. None of the parts is recoloured — every remap pair is left at the
	/// default.
	/// </summary>
	private ShellGridPart?[] MachineParts(ShellBayMachine machine) {
		var parts = new ShellGridPart?[ShellGrid.PartSlots];
		int type = machine.ChassisType;
		if (type < 0 || type >= _layouts.Length || _layouts[type] is not { } layout) {
			return parts;
		}

		var body = _bodyBanks[type];
		if (layout.HercTopImg is { } top) {
			var frame = machine.IsBuilt ? Frame(body, top.FrameId)
				: machine.BuildPercent == 0 ? Frame(_emptyBay, 0)
				: Frame(body, machine.BuildPercent < HalfBuiltPercent ? EarlyBuildTopFrame : LateBuildTopFrame);
			SetPart(parts, layout.TopImgPartId, frame, top.OriginX, top.OriginY, top.BlitFlags?.Val ?? 0);
		}

		if (layout.HercBotImg is { } bottom) {
			var frame = machine.IsBuilt ? Frame(body, bottom.FrameId)
				: machine.BuildPercent == 0 ? Frame(_emptyBay, 1)
				: Frame(body, (machine.BuildPercent < HalfBuiltPercent ? EarlyBuildTopFrame : LateBuildTopFrame) + 1);
			SetPart(parts, layout.BottomImgPartId, frame, bottom.OriginX, bottom.OriginY, bottom.BlitFlags?.Val ?? 0);
		}

		for (int mount = 0; mount < machine.MountCapacity; mount++) {
			if (WeaponRecord(layout, machine.WeaponAt(mount), mount) is { } record) {
				// LoadArmoryLayouts adds one to both corners of every weapon record as it reads them, and
				// to neither of the two body records.
				SetPart(parts, record.Id, Frame(_weaponBanks[type], record.FrameId), record.OriginX + 1,
					record.OriginY + 1, record.BlitFlags?.Val ?? 0);
			}
		}

		return parts;
	}

	/// <summary>
	/// <c>RepairLayout_FindWeaponPart</c> (<c>00413ccc</c>) over the arming layout — the record in the fitted weapon's group whose id is
	/// <c>slot + 2</c>. An empty mount, or a weapon with no art for that socket, has none, and so does a
	/// record whose frame is <c>-1</c>.
	/// </summary>
	private static UiHardpointGraphic? WeaponRecord(ArmHerc layout, int weaponId, int mount) =>
		weaponId > 0 && FindRecord(layout, weaponId, mount) is { FrameId: not -1 } record ? record : null;

	/// <summary><c>RepairLayout_FindWeaponPart</c> (<c>00413ccc</c>) itself: the record in the weapon's group whose id is <c>mount + 2</c>, whatever its frame.</summary>
	private static UiHardpointGraphic? FindRecord(ArmHerc layout, int weaponId, int mount) =>
		layout.WeaponHardpoints is { } groups && groups.TryGetValue((short)weaponId, out var sockets)
			? sockets.FirstOrDefault(socket => socket.Id == FirstWeaponPart + mount) : null;

	private static void SetPart(ShellGridPart?[] parts, int slot, DynamixBitmap? frame, int x, int y, int flags) {
		if (slot >= 0 && slot < ShellGrid.PartSlots && frame != null) {
			parts[slot] = new ShellGridPart(frame, x, y, flags, Array.Empty<(byte, byte)>());
		}
	}

	private static DynamixBitmap? Frame(DynamixBitmap[]? bank, int frame) =>
		bank != null && frame >= 0 && frame < bank.Length ? bank[frame] : null;
}
