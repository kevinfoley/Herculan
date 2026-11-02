using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Bnd;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dgs;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Map;

/// <summary>A point as the map keeps it: a block-1 point of <c>data\script.dat</c>, or a squad member's position, 12 bytes.</summary>
public readonly record struct Es1MapPoint(int X, int Y, int Z);

/// <summary>
/// A block-8 base as the map keeps it at <c>+0x5e</c>, 16 bytes: its type <c>+0</c>, its point <c>+2</c> and its side
/// <c>+8</c>, which the load overwrites from the groups (docs/es1/shell/map.md#what-it-reads).
/// </summary>
public sealed class Es1MapBase {
	/// <inheritdoc cref="Es1ScriptBase.Type"/>
	public short Type { get; init; }

	/// <inheritdoc cref="Es1ScriptBase.PointRef"/>
	public short PointRef { get; init; }

	/// <summary>The side <c>+8</c>: the record's <see cref="Es1ScriptBase.Word10"/>, then the last group that names the base.</summary>
	public short Side { get; set; }
}

/// <summary>
/// A zone object, the shell's <c>TS_OBJ</c> as <c>ShellMap_LoadZoneShapes</c> builds it with <c>TSObj_Ctor</c>
/// (<c>0000e518</c>) (docs/es1/formats/zone-terrain.md#the-shells-load): the root shape it was built with, its angles at
/// <c>+4</c> and its position at <c>+0x1e</c>.
/// </summary>
/// <param name="ShapeIndex">The record's index into <see cref="Es1ShellMap.Shapes"/>, unchecked by retail.</param>
public readonly record struct Es1MapObject(short ShapeIndex, Es1MapPoint Position, (short A, short B, short C) Angles);

/// <summary>
/// <c>Shell_MissionMap</c> (<c>000809a4</c>), the war map's map (docs/es1/shell/map.md): the base class <c>Map</c>'s
/// camera, altitude and pan (docs/es1/shell/map.md#the-base-class), and what <c>ShellMap_Constructor</c>
/// (<c>000094d9</c>) loads and fits (docs/es1/shell/map.md#building-it). Each member names its retail field.
///
/// <para>PLACEHOLDER: the constructor's labels (step 4), its gadget window, gadgets and callout (steps 7 and 8), the
/// sun's registration with <c>Light_Register</c>, the viewport and camera objects, the paint, the tick and the intro are
/// the map's later parts', S5's; until they land the map draws nothing.</para>
/// </summary>
public sealed class Es1ShellMap {
	/// <summary>
	/// The rect <c>000a02d4</c> the shell builds the map with, (14, 11)–(282, 142): the static initialiser <c>00020b68</c>
	/// makes it from the points (282, 142) and (14, 11), the lesser of each coordinate first.
	/// </summary>
	public static readonly Es1Rect Area = new(14, 11, 282, 142);

	/// <summary>The focal shift <c>+0xc</c>, 7, which <c>Map_Ctor</c> stores and the views shift by.</summary>
	public const short FocalShift = 7;

	/// <summary>The squad slots <c>+0xa8</c> and the positions <c>+0xd4</c>.</summary>
	public const int SquadSlots = 8;

	/// <summary><c>maybe_TSLight_SetIntensity</c>'s value for the sun, slot <c>+8</c> of its table.</summary>
	public const int SunIntensity = 100;

	/// <summary>The sun's words <c>+0xc</c>, <c>+0xe</c> and <c>+0x10</c>.</summary>
	public static readonly (short X, short Y, short Z) SunDirection = (7000, 0, -7000);

	/// <summary>The entries of <c>cfg\filename.bin</c> the map opens: <c>data\script.dat</c> and <c>data\object.str</c>.</summary>
	private const int ScriptEntry = 3, ObjectivesEntry = 6;

	/// <summary>
	/// <c>ShellMap_TheaterNames</c> (<c>0007e228</c>), indexed by header word 0: the simulator orders its own
	/// (<see cref="World.Es1Theater.Names"/>) differently (docs/es1/formats/zone-terrain.md#open).
	/// </summary>
	private static readonly string[] TheaterNames = ["temper", "ice", "desert", "urban"];

	private readonly Es1MapBnd _mapBnd;
	private readonly Es1ShellMapBnd _bnd;

	/// <summary>
	/// <c>Map_Ctor</c> (<c>00008cfd</c>)(map, <paramref name="rect"/>): the size, the viewport's centre, the camera at
	/// (0, 0) and <see cref="Es1MapBnd.StartAltitude"/>, no pan, and the pan step for that altitude.
	/// </summary>
	private Es1ShellMap(Es1MapBnd mapBnd, Es1ShellMapBnd bnd, Es1Rect rect) {
		_mapBnd = mapBnd;
		_bnd = bnd;
		Rect = rect;
		Width = rect.X1 - rect.X0;
		Height = rect.Y1 - rect.Y0;
		ViewportCentre = (-Width >> 1, -Height >> 1);
		Altitude = mapBnd.StartAltitude;
		PanStep = PanStepFor(Altitude);
	}

	/// <summary>The viewport's rect, the <c>GLViewport</c>'s <c>+0x210</c>–<c>+0x21c</c>.</summary>
	public Es1Rect Rect { get; }

	/// <summary>The width <c>+0x42</c> and height <c>+0x46</c>, right less left and bottom less top.</summary>
	public int Width { get; }

	/// <inheritdoc cref="Width"/>
	public int Height { get; }

	/// <summary>The viewport's centre <c>+0x220</c>/<c>+0x224</c>, the negated half size.</summary>
	public (int X, int Y) ViewportCentre { get; }

	/// <summary>The camera's position <c>+0xe</c>, <c>+0x12</c> and its altitude <c>+0x16</c>.</summary>
	public int CameraX { get; set; }

	/// <inheritdoc cref="CameraX"/>
	public int CameraY { get; set; }

	/// <inheritdoc cref="CameraX"/>
	public int Altitude { get; set; }

	/// <summary>The pan <c>+0x36</c>, <c>+0x3a</c>.</summary>
	public int PanX { get; set; }

	/// <inheritdoc cref="PanX"/>
	public int PanY { get; set; }

	/// <summary>The pan step <c>+0x3e</c> (<see cref="PanStepFor"/>).</summary>
	public int PanStep { get; set; }

	/// <summary>
	/// <c>ShellMap_DrawShapes</c> (<c>000aa2b6</c>), the constructor's copy of <see cref="Es1ShellMapBnd.DrawShapes"/>:
	/// the zone's shapes in 3D, or with 0 the 2D icons (docs/es1/shell/map.md#the-tuning-files).
	/// </summary>
	public bool DrawShapes { get; private set; }

	/// <summary>The intro's slow-paint byte <c>+0x346</c>, 1 at the build (docs/es1/shell/map.md#the-intro).</summary>
	public bool SlowPaint { get; set; } = true;

	/// <summary>The intro's revealed squad members <c>+0x347</c>, path waypoints <c>+0x348</c> and markers <c>+0x349</c>, 0 at the build.</summary>
	public byte RevealedSquad { get; set; }

	/// <inheritdoc cref="RevealedSquad"/>
	public byte RevealedPath { get; set; }

	/// <inheritdoc cref="RevealedSquad"/>
	public byte RevealedMarkers { get; set; }

	/// <summary>The intro's state <c>+0x33e</c> and its deadline <c>+0x340</c> on <c>ShellMap_Ticks</c>, both 0 at the build.</summary>
	public short IntroState { get; set; }

	/// <inheritdoc cref="IntroState"/>
	public int IntroDeadline { get; set; }

	/// <summary>The intro's closing paints <c>+0x4c1</c>, 2 at the build, and its end byte <c>+0x4c0</c>, clear.</summary>
	public byte ClosingPaints { get; set; }

	/// <inheritdoc cref="ClosingPaints"/>
	public bool IntroEnded { get; set; }

	/// <summary>The selected gadget <c>+0x480</c>, −1 at the build.</summary>
	public short SelectedGadget { get; set; } = -1;

	/// <summary>
	/// The objective lines <c>+0x148</c>, counted at <c>+0x198</c>: group 0 of <c>data\object.str</c>, which
	/// <c>Mission_WriteObjectives</c> wrote.
	/// </summary>
	public IReadOnlyList<string> ObjectiveLines { get; private set; } = [];

	/// <summary><c>str\maplabel</c>'s group 0 at <c>+0x1aa</c> (count <c>+0x272</c>): YOU and SQUAD MEMBER.</summary>
	public IReadOnlyList<string> SquadLabels { get; private set; } = [];

	/// <summary><c>str\maplabel</c>'s group 1 at <c>+0x274</c> (count <c>+0x33c</c>): the base types' names in type order.</summary>
	public IReadOnlyList<string> BaseTypeNames { get; private set; } = [];

	/// <summary>The byte <c>+0x4c7</c>: 1 for the mission kinds 0, 1 and 2, header word 3, which hides side-1 bases' icons.</summary>
	public bool HidesSide1Bases { get; private set; }

	/// <summary>
	/// The bounds' low corner <c>+0x4a4</c>/<c>+0x4a8</c> and high corner <c>+0x4b0</c>/<c>+0x4b4</c>: every point's
	/// x and y, each side then moved out by <see cref="Es1ShellMapBnd.BoundsMargin"/>. The z words <c>+0x4ac</c> and
	/// <c>+0x4b8</c>, which take the margin from an unset value, are not kept.
	/// </summary>
	public int MinX { get; private set; } = int.MaxValue;

	/// <inheritdoc cref="MinX"/>
	public int MinY { get; private set; } = int.MaxValue;

	/// <inheritdoc cref="MinX"/>
	public int MaxX { get; private set; } = int.MinValue;

	/// <inheritdoc cref="MinX"/>
	public int MaxY { get; private set; } = int.MinValue;

	/// <summary>Block 1, the points, at <c>+0x52</c> (count <c>+0x56</c>).</summary>
	public IReadOnlyList<Es1MapPoint> Points { get; private set; } = [];

	/// <summary>Block 2, the headings, at <c>+0x58</c> (count <c>+0x5c</c>).</summary>
	public IReadOnlyList<short> Headings { get; private set; } = [];

	/// <summary>Block 3, the waypoint groups' point refs, at <c>+0x64</c> (count <c>+0x68</c>).</summary>
	public IReadOnlyList<short[]> WaypointGroups { get; private set; } = [];

	/// <summary>Block 6, the mechs, at <c>+0x6e</c> (count <c>+0x72</c>).</summary>
	public IReadOnlyList<Es1ScriptMech> Mechs { get; private set; } = [];

	/// <summary>Block 8, the bases, at <c>+0x5e</c> (count <c>+0x62</c>).</summary>
	public IReadOnlyList<Es1MapBase> Bases { get; private set; } = [];

	/// <summary>Block 9's record 0, the player's squad, at <c>+0x74</c>; none when the file holds no group, an engine guard.</summary>
	public Es1ScriptGroup? Squad { get; private set; }

	/// <summary>
	/// The nav path <c>+0x6a</c>, a copy of the waypoint group the squad names (<see cref="Es1ScriptGroup.WaypointGroupRef"/>),
	/// or none for −1.
	/// </summary>
	public short[]? NavPath { get; private set; }

	/// <summary>The squad's size <c>+0xc6</c>, <c>Squad_MemberCount</c> at the build.</summary>
	public short SquadSize { get; private set; }

	/// <summary>
	/// The eight positions <c>+0xd4</c>, one for each squad slot: <see cref="PlaceSquad"/> writes a slot whose member ref
	/// is not −1 and below <see cref="SquadSize"/>. Retail leaves the others as the object's allocation left them; here
	/// they are 0, an engine choice.
	/// </summary>
	public Es1MapPoint[] SquadPositions { get; } = new Es1MapPoint[SquadSlots];

	/// <summary>The theater's root shapes at <c>+0xca</c> (count <c>+0xce</c>), <c>SHELL0</c>'s <c>dgs\&lt;name&gt;</c>; none for a negative zone.</summary>
	public IReadOnlyList<GridShape>? Shapes { get; private set; }

	/// <summary>
	/// <c>dba\tex1</c> at <c>+0x4c3</c>, which the load stores in the <c>+0x26</c> of every placed object's root shape, the
	/// array a <c>TSShape</c> binds its bitmaps from; none for a negative zone.
	/// </summary>
	public DynamixBitmapArray? Textures { get; private set; }

	/// <summary>The zone objects at <c>+0xd0</c> (count <c>+0xc8</c>), in file order.</summary>
	public IReadOnlyList<Es1MapObject> Objects { get; private set; } = [];

	/// <summary>The full view <c>+0x34a</c>–<c>+0x352</c> (<see cref="FitFullView"/>).</summary>
	public (int X, int Y, int Altitude) FullView { get; private set; }

	/// <summary>The squad view <c>+0x356</c>–<c>+0x35e</c> (<see cref="FitSquad"/>).</summary>
	public (int X, int Y, int Altitude) SquadView { get; private set; }

	/// <summary>
	/// <c>ShellMap_Constructor</c> (<c>000094d9</c>)(0, <see cref="Area"/>) (docs/es1/shell/map.md#building-it), with
	/// <paramref name="squadMemberCount"/> <c>Squad_MemberCount</c>: the base class (<see cref="Es1ShellMap(Es1MapBnd, Es1ShellMapBnd, Es1Rect)"/>);
	/// the intro's bytes; <see cref="DrawShapes"/>; the objective lines and <c>str\maplabel</c>'s two groups; the load
	/// (<see cref="LoadScriptDat"/>) and the two views; the intro's state, the selected gadget, the closing paints and the
	/// end byte. Retail's allocation failing returns no map; here it cannot.
	/// </summary>
	public static Es1ShellMap Build(string installRoot, GameContent content, Es1ShellData data, short squadMemberCount) {
		var map = new Es1ShellMap(data.MapBnd, data.ShellMapBnd, Area) {
			DrawShapes = data.ShellMapBnd.DrawShapes != 0,
		};

		// StringTable_LoadFromFile and StringTable_LoadGroup read the groups in turn from the file's start.
		if (FileEntry(data, ObjectivesEntry) is { } objectives) {
			map.ObjectiveLines = ReadGroups(ReadInstallFile(Es1ShellData.InstallPath(installRoot, objectives)), 1, objectives)[0];
		}

		var labels = ReadGroups(content.Read("str", "MAPLABEL.STR"), 2, "str\\maplabel");
		map.SquadLabels = labels[0];
		map.BaseTypeNames = labels[1];

		// PLACEHOLDER: the 22 labels (step 4), the sun's light object and Light_Register (step 5), S5's.
		map.LoadScriptDat(installRoot, content, data, squadMemberCount);
		map.FitFullView();
		map.FitSquad();

		// PLACEHOLDER: CTLWindow_Ctor's gadget window made the active root, ShellMap_BuildGadgets and
		// ShellMap_PlaceCallout (steps 7 and 8), S5's; the gadget count +0x47e and the dword +0x4bc stay 0.
		map.ClosingPaints = 2;
		return map;
	}

	/// <summary>
	/// <c>Map_PanStepFor</c> (<c>0000911c</c>): ((altitude − low <c>&gt;&gt;</c> 3) × (step top − step bottom <c>&gt;&gt;</c>
	/// 3)) / (high − low <c>&gt;&gt;</c> 3) × 8 + step bottom, with <c>MAP.BND</c>'s altitude and step limits. Retail
	/// divides by zero when the two altitudes are equal; here that gives the step's bottom, an engine guard.
	/// </summary>
	public int PanStepFor(int altitude) {
		int range = _mapBnd.AltitudeHigh - _mapBnd.AltitudeLow >> 3;
		return unchecked(range == 0
			? _mapBnd.PanStepLow
			: ((altitude - _mapBnd.AltitudeLow >> 3) * (_mapBnd.PanStepHigh - _mapBnd.PanStepLow >> 3)) / range * 8 + _mapBnd.PanStepLow);
	}

	/// <summary><c>ShellMap_SkipIntro</c> (<c>0000a9c8</c>): an intro state below <c>0x12</c> becomes <c>0x11</c>.</summary>
	public void SkipIntro() {
		if (IntroState < 0x12) {
			IntroState = 0x11;
		}
	}

	/// <summary>
	/// <c>ShellMap_RevealAll</c> (<c>0000a9ea</c>) (docs/es1/shell/map.md#destroying-it): the slow paint cleared, the
	/// whole squad revealed, with a nav path all its waypoints and one less marker, and the intro's last state,
	/// <c>0x13</c>. <c>es2_xref.py</c> finds no reference to it (docs/es1/shell/map.md#open).
	/// </summary>
	public void RevealAll() {
		SlowPaint = false;
		RevealedSquad = unchecked((byte)SquadSize);
		if (NavPath != null) {
			RevealedPath = unchecked((byte)NavPath.Length);
			RevealedMarkers = unchecked((byte)(RevealedPath - 1));
		}

		IntroState = 0x13;
	}

	/// <summary>
	/// <c>ShellMap_LoadScriptDat</c> (<c>0000c068</c>) (docs/es1/shell/map.md#what-it-reads): <c>data\script.dat</c>'s
	/// header word 3 into <see cref="HidesSide1Bases"/>; the points, widening the bounds; the margin; the headings, the
	/// waypoint groups, the mechs and the bases, the trigger areas, actions and flyers skipped; the squad's record and
	/// the bases' sides from the later groups; then the zone's shapes (<see cref="LoadZoneShapes"/>) by header words 0
	/// and 1, the nav path, and the squad's positions (<see cref="PlaceSquad"/>). Retail asserts at severity 5
	/// (<c>src\shellmap.cpp</c> lines <c>0x4c1</c>, <c>0x4c2</c>) when the file does not open, and reads past a short
	/// one; here a file that is missing or does not read is reported and loads nothing, and with no group there is no
	/// squad, engine guards.
	/// </summary>
	private void LoadScriptDat(string installRoot, GameContent content, Es1ShellData data, short squadMemberCount) {
		Es1ScriptDat? script = null;
		if (FileEntry(data, ScriptEntry) is { } name) {
			try {
				script = new Es1ScriptDatTransformer().Parse(ReadInstallFile(Es1ShellData.InstallPath(installRoot, name)));
			} catch (InvalidDataException ex) {
				Console.Error.WriteLine($"{name} does not read ({ex.Message}): the map loads nothing.");
			}
		}

		script ??= new Es1ScriptDat();
		HidesSide1Bases = script.Header[3] is 0 or 1 or 2;

		Points = script.Points.Select(point => new Es1MapPoint(point.X, point.Y, point.Z)).ToArray();
		foreach (var point in Points) {
			MinX = Math.Min(MinX, point.X);
			MinY = Math.Min(MinY, point.Y);
			MaxX = Math.Max(MaxX, point.X);
			MaxY = Math.Max(MaxY, point.Y);
		}

		int margin = _bnd.BoundsMargin;
		(MinX, MinY, MaxX, MaxY) = unchecked((MinX - margin, MinY - margin, MaxX + margin, MaxY + margin));

		Headings = script.Headings;
		WaypointGroups = script.WaypointGroups;
		Mechs = script.Mechs;
		Bases = script.Bases.Select(record => new Es1MapBase { Type = record.Type, PointRef = record.PointRef, Side = record.Word10 }).ToArray();

		// Each later group of bases writes its side into every base its member refs name; retail does not check a ref
		// against the block's count, and here one past it is passed over, an engine guard.
		Squad = script.Groups.Length > 0 ? script.Groups[0] : null;
		foreach (var group in script.Groups.Skip(1)) {
			foreach (short member in group.MemberRefs) {
				if (member >= 0 && group.MemberKind == 2 && member < Bases.Count) {
					Bases[member].Side = group.Word4A;
				}
			}
		}

		LoadZoneShapes(content, script.Header[0], script.Header[1]);

		// A waypoint group past the block, which retail copies from past its array, gives no path here, an engine guard.
		short route = Squad?.WaypointGroupRef ?? -1;
		NavPath = route >= 0 && route < WaypointGroups.Count ? WaypointGroups[route] : null;
		PlaceSquad(content, squadMemberCount);
	}

	/// <summary>
	/// <c>ShellMap_LoadZoneShapes</c> (<c>0000c7e3</c>)(map, <paramref name="theater"/>, <paramref name="zone"/>)
	/// (docs/es1/formats/zone-terrain.md#the-shells-load): for a negative zone none; otherwise <c>dat\zone&lt;n&gt;</c>, the
	/// theater's root shapes from <c>dgs\&lt;name&gt;</c> by <see cref="TheaterNames"/>
	/// (<c>ShellMap_LoadShapeRoots</c>, <c>000092b9</c>), <c>dba\tex1</c> (<c>ShellMap_LoadResource</c>,
	/// <c>0000923c</c>), and an object for each record. Retail asserts at severity 5 (line <c>0x565</c>) when the zone
	/// file does not open, and indexes the names by an unchecked theater; here either is reported and loads no objects,
	/// engine guards. <c>ShellMap_LoadShapeRoots</c>'s arena limit, <see cref="Es1ShellMapBnd.ShapeArenaSize"/>, is not
	/// modelled: the shapes take what memory they need.
	/// </summary>
	private void LoadZoneShapes(GameContent content, short theater, short zone) {
		if (zone < 0) {
			return;
		}

		var file = content.Read("dat", $"ZONE{zone}.DAT") is { } zoneBytes ? new Es1ZoneDatTransformer().Parse(zoneBytes) : null;
		if (file == null || theater < 0 || theater >= TheaterNames.Length) {
			Console.Error.WriteLine($"dat\\zone{zone} does not open or theater {theater} has no name (src\\shellmap.cpp line 0x565 asserts here): the map has no shapes.");
			return;
		}

		string library = TheaterNames[theater];
		Shapes = content.Read("dgs", library.ToUpperInvariant() + ".DGS") is { } dgs ? new BasesDgsTransformer().Parse(dgs)?.Shapes : null;
		if (Shapes == null) {
			Console.Error.WriteLine($"dgs\\{library} is missing or does not read: the map's objects have no shapes.");
		}

		Textures = content.Read("dba", "TEX1.DBA") is { } tex1 ? new DynamixBitmapArrayTransformer().Parse(tex1) : null;
		Objects = file.Shapes
			.Select(record => new Es1MapObject(record.ShapeIndex, new Es1MapPoint(record.X, record.Y, record.Z),
				(record.Angles[0], record.Angles[1], record.Angles[2])))
			.ToArray();
	}

	/// <summary>
	/// <c>ShellMap_PlaceSquad</c> (<c>0000cab1</c>) (docs/es1/formats/formations-dat.md): <see cref="SquadSize"/> from
	/// <paramref name="squadMemberCount"/>; the squad's formation, <c>dat\mforms</c>'s record its
	/// <see cref="Es1ScriptGroup.Word30"/> names; the heading, the squad's heading ref's, or with none the first leg of a
	/// nav path of two waypoints or more, its <see cref="Es1ShellTrig.Atan2"/> less <c>0x4000</c>, else 0; the start, the
	/// squad's point, or with −1 the path's first waypoint's; then each slot whose member ref is not −1 and below the
	/// size at the start, every slot after the first moved by its formation offset turned by the heading
	/// (<see cref="Es1ShellTrig.Rotation2D"/>, <see cref="Es1ShellTrig.Rotate"/>), the height unmoved. Retail asserts at
	/// severity 5 (line <c>0x59e</c>) when the file does not open and reads a formation, a ref or a path without
	/// checking it; here a formation it cannot read has no offsets, and a ref past its block or a start with no path
	/// places at (0, 0, 0), engine guards.
	/// </summary>
	private void PlaceSquad(GameContent content, short squadMemberCount) {
		SquadSize = squadMemberCount;
		if (Squad is not { } squad) {
			return;
		}

		var formations = content.Read("dat", "MFORMS.DAT") is { } forms ? new Es1MechFormationsTransformer().Parse(forms) : null;
		var formation = squad.Word30 >= 0 && formations != null && squad.Word30 < formations.Formations.Length
			? formations.Formations[squad.Word30]
			: null;

		short heading;
		if (squad.HeadingRef < 0) {
			heading = 0;
			if (NavPath is { Length: >= 2 } path) {
				var from = Point(path[0]);
				var to = Point(path[1]);
				heading = unchecked((short)(Es1ShellTrig.Atan2(to.X - from.X, to.Y - from.Y) - 0x4000));
			}
		} else {
			heading = squad.HeadingRef < Headings.Count ? Headings[squad.HeadingRef] : (short)0;
		}

		var rotation = Es1ShellTrig.Rotation2D(heading);
		var start = squad.PointRef == -1 ? NavPath is { Length: > 0 } route ? Point(route[0]) : default : Point(squad.PointRef);
		for (int slot = 0; slot < SquadSlots; slot++) {
			short member = squad.MemberRefs[slot];
			if (member == -1 || member >= SquadSize) {
				continue;
			}

			var position = start;
			if (slot != 0 && formation != null && slot - 1 < formation.Slots.Length) {
				var offset = formation.Slots[slot - 1];
				var (x, y) = Es1ShellTrig.Rotate(offset.X, offset.Y, rotation);
				position = unchecked(new Es1MapPoint(position.X + x, position.Y + y, position.Z));
			}

			SquadPositions[slot] = position;
		}
	}

	/// <summary>
	/// <c>ShellMap_FitFullView</c> (<c>0000ce8a</c>) (docs/es1/shell/map.md#the-camera-views): the box of every base's
	/// point that is not −1, every squad position whose member ref is not −1 and every nav path point that is not −1,
	/// fitted with <see cref="Es1ShellMapBnd.FullViewMargin"/>. A ref past the points, which retail reads past its array,
	/// reads (0, 0, 0) here, an engine guard.
	/// </summary>
	private void FitFullView() {
		int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
		void Widen(Es1MapPoint point) {
			minX = Math.Min(minX, point.X);
			maxX = Math.Max(maxX, point.X);
			minY = Math.Min(minY, point.Y);
			maxY = Math.Max(maxY, point.Y);
		}

		foreach (var structure in Bases) {
			if (structure.PointRef != -1) {
				Widen(Point(structure.PointRef));
			}
		}

		WidenSquad(Widen);
		if (NavPath != null) {
			foreach (short waypoint in NavPath) {
				if (waypoint != -1) {
					Widen(Point(waypoint));
				}
			}
		}

		FullView = Fit(minX, minY, maxX, maxY, _bnd.FullViewMargin);
	}

	/// <summary>
	/// <c>ShellMap_FitSquad</c> (<c>0000d0a9</c>): the box of the squad positions whose member ref is not −1, fitted with
	/// <see cref="Es1ShellMapBnd.SquadViewMargin"/>.
	/// </summary>
	private void FitSquad() {
		int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
		WidenSquad(point => {
			minX = Math.Min(minX, point.X);
			minY = Math.Min(minY, point.Y);
			maxX = Math.Max(maxX, point.X);
			maxY = Math.Max(maxY, point.Y);
		});

		SquadView = Fit(minX, minY, maxX, maxY, _bnd.SquadViewMargin);
	}

	/// <summary>Each squad position whose slot's member ref is not −1, below the squad size or not; with no squad, none.</summary>
	private void WidenSquad(Action<Es1MapPoint> widen) {
		if (Squad is not { } squad) {
			return;
		}

		for (int slot = 0; slot < SquadSlots; slot++) {
			if (squad.MemberRefs[slot] != -1) {
				widen(SquadPositions[slot]);
			}
		}
	}

	/// <summary>
	/// The views' fit (docs/es1/shell/map.md#the-camera-views): the box widened by <paramref name="margin"/> on every
	/// side, its centre, and the altitude the larger of the half width shifted left by <see cref="FocalShift"/> over 160
	/// and the half height so over 100, doubled; in 32 bits, as retail's, so an empty box wraps as it does.
	/// </summary>
	private static (int X, int Y, int Altitude) Fit(int minX, int minY, int maxX, int maxY, int margin) => unchecked(
		FitAxes(minX - margin, maxX + margin, minY - margin, maxY + margin));

	private static (int X, int Y, int Altitude) FitAxes(int lowX, int highX, int lowY, int highY) {
		unchecked {
			int halfX = highX - lowX >> 1;
			int halfY = highY - lowY >> 1;
			int altitude = Math.Max((halfX << (FocalShift & 0x1f)) / 0xa0, (halfY << (FocalShift & 0x1f)) / 100);
			return (lowX + halfX, lowY + halfY, altitude << 1);
		}
	}

	/// <summary>The point <paramref name="index"/> of <see cref="Points"/>; one past the block, which retail reads past its array, is (0, 0, 0) here, an engine guard.</summary>
	private Es1MapPoint Point(int index) => index >= 0 && index < Points.Count ? Points[index] : default;

	/// <summary>Entry <paramref name="entry"/> of <c>cfg\filename.bin</c>, or none without the table, reported.</summary>
	private static string? FileEntry(Es1ShellData data, int entry) {
		if (data.FileNames is { } names && entry < names.Length) {
			return names[entry];
		}

		Console.Error.WriteLine($"cfg\\filename.bin has no entry {entry}: the map does not read it.");
		return null;
	}

	/// <summary>A file in the install, or none when it does not open, reported.</summary>
	private static byte[]? ReadInstallFile(string path) {
		try {
			return File.ReadAllBytes(path);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"{path} does not open ({ex.Message}).");
			return null;
		}
	}

	/// <summary>
	/// <c>StringTable_LoadFromFile</c> and <paramref name="count"/> calls of <c>StringTable_LoadGroup</c>: the first groups'
	/// texts. Retail reads whatever a missing file's stream gives; here a file that is missing or does not read, or a group
	/// past its end, has no lines, reported, an engine guard.
	/// </summary>
	private static IReadOnlyList<string>[] ReadGroups(byte[]? bytes, int count, string name) {
		var table = bytes != null ? SimStrings.Parse(bytes) : null;
		if (table == null || table.GroupCount < count) {
			Console.Error.WriteLine($"{name} is missing, does not read or holds fewer than {count} groups.");
		}

		var groups = new IReadOnlyList<string>[count];
		for (int i = 0; i < count; i++) {
			groups[i] = table != null && i < table.GroupCount ? table.Group(i).Select(entry => entry.Text).ToArray() : [];
		}

		return groups;
	}
}
