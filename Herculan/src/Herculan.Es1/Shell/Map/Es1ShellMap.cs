using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Bnd;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dgs;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Es1.Panels;
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
/// <para>The paint is <see cref="Es1ShellMapPaint"/>'s, the zone's shapes <see cref="Es1ShellShapes"/>' and the intro
/// <see cref="Es1ShellMapIntro"/>'s.</para>
/// </summary>
public sealed class Es1ShellMap {
	/// <summary>
	/// <c>ShellMap_Rect</c> (<c>000a02d4</c>), the rect the shell builds the map with, (14, 11)–(282, 142): <c>ShellData_StaticInit</c> (<c>00020b68</c>)
	/// makes it from the points (282, 142) and (14, 11), the lesser of each coordinate first.
	/// </summary>
	public static readonly Es1Rect Area = new(14, 11, 282, 142);

	/// <summary>The focal shift <c>+0xc</c>, 7, which <c>Map_Ctor</c> stores and the views shift by.</summary>
	public const short FocalShift = 7;

	/// <summary>The squad slots <c>+0xa8</c> and the positions <c>+0xd4</c>.</summary>
	public const int SquadSlots = 8;

	/// <summary>The labels at <c>+0x366</c>, one a row down the map.</summary>
	public const int ObjectiveLabelCount = 20;

	/// <summary>The room in the gadget array <c>+0x3b6</c>.</summary>
	public const int GadgetCapacity = 50;

	/// <summary>
	/// <c>Map_CameraOrientation</c> (<c>0009a444</c>), {<c>0xc000</c>, 0, 0}, which <c>Map_StaticInit</c> sets and
	/// <c>Map_Ctor</c> gives the camera: it looks straight down.
	/// </summary>
	public const short CameraPitch = unchecked((short)0xc000);

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
	private readonly List<Es1MapGadget> _gadgets = new(GadgetCapacity);

	/// <summary><c>Raster_WorldToView</c> as <see cref="InstallCamera"/> last left it; every caller of <see cref="Project"/> installs the camera first.</summary>
	private Transform3 _worldToView;

	/// <inheritdoc cref="_worldToView"/>
	public Transform3 WorldToView => _worldToView;

	/// <summary>
	/// <c>Map_Ctor</c> (<c>00008cfd</c>)(map, <paramref name="rect"/>): the size, the viewport's centre, the camera at
	/// (0, 0) and <see cref="Es1MapBnd.StartAltitude"/>, no pan, and the pan step for that altitude.
	/// </summary>
	private Es1ShellMap(Es1MapBnd mapBnd, Es1ShellMapBnd bnd, HudFont? labelFont, Es1Rect rect) {
		_mapBnd = mapBnd;
		_bnd = bnd;
		LabelFont = labelFont;
		Rect = rect;
		Width = rect.X1 - rect.X0;
		Height = rect.Y1 - rect.Y0;
		ViewportCentre = (-Width >> 1, -Height >> 1);
		Altitude = mapBnd.StartAltitude;
		PanStep = PanStepFor(Altitude);
	}

	/// <summary>The viewport's rect, the <c>GLViewport</c>'s <c>+0x210</c>–<c>+0x21c</c>.</summary>
	public Es1Rect Rect { get; }

	/// <summary>The shell's light list (<see cref="Es1ShellData.Lights"/>), which the sun is registered in and the shapes are lit by.</summary>
	public Es1ShellLights Lights { get; private set; } = new();

	/// <summary>The sun <c>+0x4e</c>, a <c>TSSunLight</c> of <see cref="SunIntensity"/> along <see cref="SunDirection"/>.</summary>
	public Es1ShellLight Sun { get; } = new(SunIntensity, SunDirection);

	/// <summary>The tuning <c>SHELLMAP.BND</c> the map reads its sizes, frames and waits from.</summary>
	public Es1ShellMapBnd Bnd => _bnd;

	/// <summary>
	/// What <c>ShellMap_LoadResources</c> loaded: the clock <c>ShellMap_Ticks</c> the intro and the slow paint wait on and
	/// the icons; none when the start-up did not load them, and then nothing waits and no icon is drawn.
	/// </summary>
	public Es1ShellMapResources? Resources { get; private set; }

	/// <summary><see cref="Es1ShellMapResources.LabelFont"/>, which the labels draw in and the gadgets measure in.</summary>
	public HudFont? LabelFont { get; }

	/// <summary>The top label <c>+0x362</c>, centred over the map's top row, which the intro's lines go into.</summary>
	public Es1Label TopLabel { get; private set; } = null!;

	/// <summary>The labels <c>+0x366</c>, one a row from the map's top, which the objective lines go into after the intro.</summary>
	public IReadOnlyList<Es1Label> ObjectiveLabels { get; private set; } = [];

	/// <summary>The callout label <c>+0x482</c>, its fill color <c>0x3f</c>, which <see cref="PlaceCallout"/> places.</summary>
	public Es1Label CalloutLabel { get; private set; } = null!;

	/// <summary>
	/// The gadget window <c>+0x134</c>, <c>CTLWindow_Ctor</c> (<c>0001144a</c>)(0, the map's rect), into whose clickables
	/// the gadgets go; ES1DBSIM's constructor in other code (docs/es1/simulation/cockpit-mouse.md#the-root-widget--ctlwindow_ctor-00043cac).
	/// </summary>
	public Es1WidgetRoot Window { get; private set; } = null!;

	/// <summary>The gadgets <c>+0x3b6</c>, counted at <c>+0x47e</c> (<see cref="BuildGadgets"/>).</summary>
	public IReadOnlyList<Es1MapGadget> Gadgets => _gadgets;

	/// <summary>The callout's pointer <c>+0x486</c>, three points in screen pixels (<see cref="PlaceCallout"/>).</summary>
	public (int X, int Y)[] CalloutPointer { get; } = new (int X, int Y)[3];

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

	/// <summary>
	/// The redraw byte <c>+0x4c2</c>, which <c>ShellMap_ResetViewAndClear</c> sets and every intro step clears once the
	/// intro has ended (docs/es1/shell/map.md#the-tick).
	/// </summary>
	public bool RedrawDue { get; set; }

	/// <summary>The intro's line <c>+0x344</c> of <see cref="ObjectiveLines"/>.</summary>
	public short IntroLine { get; set; }

	/// <summary>The state a wait of the intro goes on to, <c>+0x49e</c>.</summary>
	public short IntroResumeState { get; set; }

	/// <summary>The line <c>+0x4bc</c> the top label last took, which an empty line shows again; none at the build.</summary>
	public string? KeptLine { get; set; }

	/// <summary>The grid's spacing in pixels <c>+0x22</c>, which each paint's grid measures again.</summary>
	public int GridStep { get; set; }

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
	/// <paramref name="squadMemberCount"/> <c>Squad_MemberCount</c>: the base class (<see cref="Es1ShellMap(Es1MapBnd, Es1ShellMapBnd, HudFont, Es1Rect)"/>);
	/// the intro's bytes; <see cref="DrawShapes"/>; the objective lines and <c>str\maplabel</c>'s two groups; the labels
	/// (<see cref="BuildLabels"/>); the load (<see cref="LoadScriptDat"/>) and the two views; the intro's state; the gadget
	/// window made <paramref name="widgets"/>' active root with <c>Widget_SetActiveRoot</c> (<c>0001070d</c>), which leaves
	/// the pressed index alone; the gadgets (<see cref="BuildGadgets"/>); the selected gadget −1 and
	/// <see cref="PlaceCallout"/>; the closing paints and the end byte. Retail's allocation failing returns no map; here it
	/// cannot.
	/// </summary>
	public static Es1ShellMap Build(string installRoot, GameContent content, Es1ShellData data, short squadMemberCount, Es1WidgetGlobals widgets) {
		var map = new Es1ShellMap(data.MapBnd, data.ShellMapBnd, data.MapResources?.LabelFont, Area) {
			DrawShapes = data.ShellMapBnd.DrawShapes != 0,
			Resources = data.MapResources,
			Lights = data.Lights,
		};

		// StringTable_LoadFromFile and StringTable_LoadGroup read the groups in turn from the file's start.
		if (FileEntry(data, ObjectivesEntry) is { } objectives) {
			map.ObjectiveLines = ReadGroups(ReadInstallFile(Es1ShellData.InstallPath(installRoot, objectives)), 1, objectives)[0];
		}

		var labels = ReadGroups(content.Read("str", "MAPLABEL.STR"), 2, "str\\maplabel");
		map.SquadLabels = labels[0];
		map.BaseTypeNames = labels[1];

		map.BuildLabels();

		map.Lights.Register(map.Sun);
		map.LoadScriptDat(installRoot, content, data, squadMemberCount);
		map.FitFullView();
		map.FitSquad();

		// The intro's state and deadline are 0 from the object's initializers, and so is the dword +0x4bc, the kept line.
		map.Window = new Es1WidgetRoot(Area);
		widgets.ActiveRoot = map.Window;
		map.BuildGadgets();
		map.SelectedGadget = -1;
		map.PlaceCallout();
		map.ClosingPaints = 2;
		return map;
	}

	/// <summary>
	/// The constructor's step 4 (docs/es1/shell/map.md#building-it): <c>Label_Ctor</c> (<c>0000fd20</c>) in
	/// <see cref="LabelFont"/> for each, placed with <c>Label_SetRect</c> (<c>0000fe8e</c>) and a zero margin over a row of
	/// the map's width and <see cref="Es1ShellMapBnd.LineSpacing"/> high: <see cref="TopLabel"/> over the top row, centred;
	/// the <see cref="ObjectiveLabels"/> with flags 1, the first over the top row and each next a row lower; then
	/// <see cref="CalloutLabel"/>, unplaced, its fill color <c>0x3f</c>. Both functions are ES1DBSIM's statements in other
	/// code (docs/es1/simulation/cockpit-widgets.md#labels).
	/// </summary>
	private void BuildLabels() {
		int spacing = _bnd.LineSpacing;
		var row = new Es1Rect(Rect.X0, Rect.Y0, Rect.X1, unchecked(Rect.Y0 + spacing));
		TopLabel = new Es1Label(LabelFont);
		TopLabel.SetRect(Es1Label.Centered, row);

		var labels = new Es1Label[ObjectiveLabelCount];
		for (int i = 0; i < labels.Length; i++) {
			labels[i] = new Es1Label(LabelFont);
			labels[i].SetRect(1, row);
			row = unchecked(row with { Y0 = row.Y0 + spacing, Y1 = row.Y1 + spacing });
		}

		ObjectiveLabels = labels;
		CalloutLabel = new Es1Label(LabelFont) { FillColor = 0x3f };
	}

	/// <summary>
	/// <c>ShellMap_InstallCamera</c> (<c>00008fa2</c>) (docs/es1/shell/map.md#a-paint): the camera at the position plus the
	/// pan, at the altitude, installed with <c>Raster_InstallViewProjection</c> (<c>00053ce4</c>), and
	/// <c>Raster_SetModelTransform</c>(0): the world-to-view transform the inverse of the camera's frame,
	/// <c>BuildEulerRotationMatrixQ14</c>(<see cref="CameraPitch"/>, 0, 0) (<see cref="Es1ShellTrig.Rotation"/>) at that
	/// point.
	/// </summary>
	public void InstallCamera() {
		var frame = Es1ShellTrig.Rotation(CameraPitch, 0, 0);
		frame.X = unchecked(CameraX + PanX);
		frame.Y = unchecked(CameraY + PanY);
		frame.Z = Altitude;
		_worldToView = frame.Inverted();
	}

	/// <summary>
	/// <c>Map_Project</c> (<c>00008ee6</c>)(map, out, <paramref name="point"/>) (docs/es1/shell/map.md#a-paint), ES2's, the
	/// screen point under the camera <see cref="InstallCamera"/> installed: the <see cref="ViewPoint"/> and
	/// <c>Raster_ProjectToScreen</c>, which adds the centre, the viewport's rect's top-left less
	/// <see cref="ViewportCentre"/>.
	/// </summary>
	public (int X, int Y) Project(Es1MapPoint point) {
		var view = ViewPoint(point);
		return unchecked((view.X + Rect.X0 - ViewportCentre.X, view.Y + Rect.Y0 - ViewportCentre.Y));
	}

	/// <summary>
	/// The view point the clear and the grid draw at (docs/es1/shell/map.md#a-paint): <c>Raster_ModelToView</c> through
	/// the world-to-view transform <see cref="InstallCamera"/> installed and <c>Raster_PerspectiveDivide</c> by the
	/// camera's <see cref="FocalShift"/>, {(<i>x</i> &lt;&lt; 7) / <i>y</i>, (<i>z</i> &lt;&lt; 7) / <i>y</i>}, its
	/// <i>y</i> negated. A point at the camera's depth, where retail divides by 0, is taken at depth 1, an engine guard.
	/// </summary>
	public (int X, int Y) ViewPoint(Es1MapPoint point) {
		var view = _worldToView.TransformPoint(point.X, point.Y, point.Z);
		int depth = view.Y == 0 ? 1 : view.Y;
		return unchecked(((view.X << FocalShift) / depth, -((view.Z << FocalShift) / depth)));
	}

	/// <summary>
	/// <c>ShellMap_BuildGadgets</c> (<c>0000d1bb</c>) (docs/es1/shell/map.md#the-gadgets): the camera at the full view; the
	/// viewport and the camera installed (<c>Map_PushContext</c> (<c>00008f65</c>), <see cref="InstallCamera"/>); a gadget
	/// for each squad slot whose member ref is not −1, <see cref="SquadLabels"/>' line 0 for slot 0 and line 1 for the rest,
	/// of <see cref="Es1ShellMapBnd.SquadGadgetSize"/>, at the slot's position; then one for each base whose side is not
	/// −1, its type's name in <see cref="BaseTypeNames"/>, of <see cref="Es1ShellMapBnd.SmallBaseGadgetSize"/> for the
	/// types 5, 6 and 8 and <see cref="Es1ShellMapBnd.BaseGadgetSize"/> for the others, at its point; each appended and
	/// registered in <see cref="Window"/> with <c>Widget_RegisterClickable</c> (<c>0001084f</c>), ES1DBSIM's; then
	/// <c>Map_PopContext</c> (<c>00008f86</c>). Retail reads the lines, the type's name and the point unchecked, a base's
	/// point ref of −1 among them, and writes a 51st gadget over the count; here a line past its group has no text, a point
	/// ref past the block is (0, 0, 0) and a gadget past <see cref="GadgetCapacity"/> is reported and dropped, engine
	/// guards.
	/// </summary>
	private void BuildGadgets() {
		(CameraX, CameraY, Altitude) = FullView;
		InstallCamera();
		if (Squad is { } squad) {
			for (int slot = 0; slot < SquadSlots; slot++) {
				if (squad.MemberRefs[slot] != -1) {
					AddGadget(Line(SquadLabels, slot == 0 ? 0 : 1), _bnd.SquadGadgetSize, SquadPositions[slot]);
				}
			}
		}

		foreach (var structure in Bases) {
			if (structure.Side != -1) {
				short size = structure.Type is 5 or 6 or 8 ? _bnd.SmallBaseGadgetSize : _bnd.BaseGadgetSize;
				AddGadget(Line(BaseTypeNames, structure.Type), size, PointAt(structure.PointRef));
			}
		}

		static string? Line(IReadOnlyList<string> lines, int index) => index >= 0 && index < lines.Count ? lines[index] : null;
	}

	private void AddGadget(string? text, short size, Es1MapPoint position) {
		var gadget = new Es1MapGadget(text, size, this, position, _bnd.GadgetTextMargin);
		if (_gadgets.Count >= GadgetCapacity) {
			Console.Error.WriteLine($"The map's gadget array holds {GadgetCapacity}: gadget {_gadgets.Count} ({text}) is dropped.");
			return;
		}

		_gadgets.Add(gadget);
		Window.RegisterClickable(gadget);
	}

	/// <summary>
	/// <c>ShellMap_PlaceGadgets</c> (<c>0000da37</c>) (docs/es1/shell/map.md#picking-a-gadget), after a camera move: the
	/// viewport and the camera installed, then each gadget's <see cref="Es1MapGadget.Place"/>.
	/// </summary>
	public void PlaceGadgets() {
		InstallCamera();
		foreach (var gadget in _gadgets) {
			gadget.Place();
		}
	}

	/// <summary>
	/// <c>ShellMap_PlaceCallout</c> (<c>0000d72b</c>) (docs/es1/shell/map.md#the-callout): nothing for the selected gadget
	/// −1; otherwise the box <see cref="Es1ShellMapBnd.CalloutHeight"/> high beside it and the pointer
	/// <see cref="CalloutPointer"/>, in screen pixels, and <see cref="CalloutLabel"/> placed over the box, centred with a
	/// zero margin. The side test sets the gadget's viewport-relative left against the viewport's centre in screen pixels.
	/// A selected index past the gadgets, which retail reads past its array, places nothing here, an engine guard.
	/// </summary>
	public void PlaceCallout() {
		if (SelectedGadget == -1) {
			return;
		}

		if (SelectedGadget < 0 || SelectedGadget >= _gadgets.Count) {
			Console.Error.WriteLine($"The map's selected gadget {SelectedGadget} is past its {_gadgets.Count}: no callout.");
			return;
		}

		var gadget = _gadgets[SelectedGadget];
		int gap = _bnd.CalloutGap, pointer = _bnd.PointerWidth, height = _bnd.CalloutHeight, halfHeight = (short)(height >> 1);
		int centreX = (Rect.X1 - Rect.X0 >> 1) + Rect.X0;
		int boxLeft, boxRight;
		unchecked {
			if (centreX < gadget.Rect.X0) {
				int edge = gadget.Rect.X0 - gap;
				boxRight = edge - 1;
				boxLeft = boxRight - gadget.Width;
				CalloutPointer[0] = (boxRight + pointer + 1, halfHeight);
				CalloutPointer[1] = (edge, halfHeight + (pointer >> 1));
				CalloutPointer[2] = (edge, halfHeight - (pointer >> 1));
			} else {
				boxLeft = gadget.Rect.X0 + gadget.Size + gap;
				boxRight = boxLeft + gadget.Width;
				CalloutPointer[0] = (boxLeft - pointer - 1, halfHeight);
				CalloutPointer[1] = (boxLeft - 1, halfHeight - (pointer >> 1));
				CalloutPointer[2] = (boxLeft - 1, halfHeight + (pointer >> 1));
			}

			int top = (gadget.Size >> 1) + gadget.Rect.Y0 - (height >> 1);
			for (int i = 0; i < CalloutPointer.Length; i++) {
				CalloutPointer[i] = (CalloutPointer[i].X + Rect.X0, CalloutPointer[i].Y + top + Rect.Y0);
			}

			CalloutLabel.SetRect(Es1Label.Centered, new Es1Rect(boxLeft + Rect.X0, top + Rect.Y0, boxRight + Rect.X0, top + height + Rect.Y0));
		}
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

	/// <summary>
	/// Slot <c>+8</c> of <c>ShellMapVtable</c>, <c>ShellMap_ZoomIn</c> (<c>0000d43c</c>) (docs/es1/shell/map.md#the-camera-moves):
	/// once the intro has ended, <c>Map_ZoomIn</c> (<c>00009043</c>), the altitude down <see cref="Es1MapBnd.ZoomStep"/>
	/// when it stays at or above <see cref="Es1MapBnd.AltitudeLow"/>, with the pan step for it; then the gadgets and the
	/// callout placed again.
	/// </summary>
	public void ZoomIn() {
		if (!IntroEnded) {
			return;
		}

		int altitude = unchecked(Altitude - _mapBnd.ZoomStep);
		if (_mapBnd.AltitudeLow <= altitude) {
			Altitude = altitude;
			PanStep = PanStepFor(Altitude);
		}

		PlaceAfterMove();
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>ShellMap_ZoomOut</c> (<c>0000d469</c>): once the intro has ended, <c>Map_ZoomOut</c>
	/// (<c>00009010</c>), the altitude up <see cref="Es1MapBnd.ZoomStep"/> when it stays at or below
	/// <see cref="Es1MapBnd.AltitudeHigh"/>, with the pan step for it; an altitude above the full view's then becomes it,
	/// with the pan step for it again; then the gadgets and the callout placed again.
	/// </summary>
	public void ZoomOut() {
		if (!IntroEnded) {
			return;
		}

		int altitude = unchecked(Altitude + _mapBnd.ZoomStep);
		if (altitude <= _mapBnd.AltitudeHigh) {
			Altitude = altitude;
			PanStep = PanStepFor(Altitude);
		}

		if (FullView.Altitude < Altitude) {
			Altitude = FullView.Altitude;
			PanStep = PanStepFor(Altitude);
		}

		PlaceAfterMove();
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>ShellMap_PanNorth</c> (<c>0000d4b8</c>): once the intro has ended, <c>Map_PanNorth</c>
	/// (<c>000090be</c>), the pan's y up <see cref="PanStep"/> when the camera's y plus the step is at most
	/// <see cref="Es1MapBnd.PanLimit"/>; then the gadgets and the callout placed again.
	/// </summary>
	public void PanNorth() {
		if (!IntroEnded) {
			return;
		}

		if (unchecked(CameraY + PanStep) <= _mapBnd.PanLimit) {
			PanY = unchecked(PanY + PanStep);
		}

		PlaceAfterMove();
	}

	/// <summary>
	/// Slot <c>+0x14</c>, <c>ShellMap_PanSouth</c> (<c>0000d4e5</c>): <c>Map_PanSouth</c> (<c>000090df</c>), the pan's y
	/// down the step when the camera's y less the step is at least −<see cref="Es1MapBnd.PanLimit"/>, as
	/// <see cref="PanNorth"/>.
	/// </summary>
	public void PanSouth() {
		if (!IntroEnded) {
			return;
		}

		if (-_mapBnd.PanLimit <= unchecked(CameraY - PanStep)) {
			PanY = unchecked(PanY - PanStep);
		}

		PlaceAfterMove();
	}

	/// <summary>
	/// Slot <c>+0x18</c>, <c>ShellMap_PanWest</c> (<c>0000d512</c>): <c>Map_PanWest</c> (<c>00009076</c>), the pan's x
	/// down the step when the camera's x less the step is at least −<see cref="Es1MapBnd.PanLimit"/>, as
	/// <see cref="PanNorth"/>.
	/// </summary>
	public void PanWest() {
		if (!IntroEnded) {
			return;
		}

		if (-_mapBnd.PanLimit <= unchecked(CameraX - PanStep)) {
			PanX = unchecked(PanX - PanStep);
		}

		PlaceAfterMove();
	}

	/// <summary>
	/// Slot <c>+0x1c</c>, <c>ShellMap_PanEast</c> (<c>0000d53f</c>): <c>Map_PanEast</c> (<c>0000909d</c>), the pan's x up
	/// the step when the camera's x plus the step is at most <see cref="Es1MapBnd.PanLimit"/>, as <see cref="PanNorth"/>.
	/// </summary>
	public void PanEast() {
		if (!IntroEnded) {
			return;
		}

		if (unchecked(CameraX + PanStep) <= _mapBnd.PanLimit) {
			PanX = unchecked(PanX + PanStep);
		}

		PlaceAfterMove();
	}

	/// <summary>
	/// Slot <c>+0x20</c>, <c>ShellMap_ResetPan</c> (<c>0000d56c</c>): once the intro has ended, <c>Map_ResetPan</c>
	/// (<c>00009106</c>), the pan zeroed; then the gadgets and the callout placed again. Its caller is map.md's Open.
	/// </summary>
	public void ResetPan() {
		if (!IntroEnded) {
			return;
		}

		PanX = 0;
		PanY = 0;
		PlaceAfterMove();
	}

	/// <summary>The camera moves' last steps: <see cref="PlaceGadgets"/> and <see cref="PlaceCallout"/>.</summary>
	private void PlaceAfterMove() {
		PlaceGadgets();
		PlaceCallout();
	}

	/// <summary>
	/// <c>ShellMap_SelectGadget</c> (<c>0000d3d2</c>)(map, <paramref name="gadget"/>) (docs/es1/shell/map.md#picking-a-gadget),
	/// which a gadget's click runs: nothing when the gadget is not one of <see cref="Gadgets"/>; otherwise its index
	/// becomes <see cref="SelectedGadget"/>, <see cref="PlaceCallout"/> runs and <see cref="RedrawDue"/> is set.
	/// </summary>
	public void SelectGadget(Es1MapGadget gadget) {
		int index = _gadgets.IndexOf(gadget);
		if (index < 0) {
			return;
		}

		SelectedGadget = (short)index;
		PlaceCallout();
		RedrawDue = true;
	}

	/// <summary>
	/// <c>ShellMap_SelectNextGadget</c> (<c>0000d599</c>), the key <c>.</c>: <see cref="SelectedGadget"/> stepped forward,
	/// the count wrapping to 0, until a gadget passes <see cref="InsideViewport"/>, which <see cref="PlaceCallout"/> then
	/// places, or the index comes back to where it started. Nothing is set to redraw: the key's handler sets
	/// <c>000809a8</c> to 3. Retail reads its array's first slot past the count with no gadget, and from −1 with no gadget
	/// passing never comes back to its start (docs/es1/shell/map.md#open); here no gadget does nothing and a whole round
	/// with none passing is reported and stops, engine guards.
	/// </summary>
	public void SelectNextGadget() => StepSelection(1);

	/// <summary>
	/// <c>ShellMap_SelectPrevGadget</c> (<c>0000d657</c>), the key <c>,</c>: <see cref="SelectedGadget"/> stepped back,
	/// −1 becoming 0 and below 0 the count less 1, as <see cref="SelectNextGadget"/> otherwise, with its guards.
	/// </summary>
	public void SelectPrevGadget() => StepSelection(-1);

	private void StepSelection(int step) {
		int count = _gadgets.Count;
		if (count == 0) {
			Console.Error.WriteLine("The map has no gadget to step to.");
			return;
		}

		short start = SelectedGadget;
		for (int steps = 0; ; steps++) {
			if (steps == count) {
				Console.Error.WriteLine($"No map gadget lies inside the viewport, from gadget {start}: retail's search does not end.");
				return;
			}

			short index = SelectedGadget;
			if (step > 0) {
				index++;
				if (index == count) {
					index = 0;
				}
			} else if (index == -1) {
				index = 0;
			} else {
				index--;
				if (index < 0) {
					index = (short)(count - 1);
				}
			}

			SelectedGadget = index;
			if (InsideViewport(_gadgets[index].Rect)) {
				PlaceCallout();
				return;
			}

			if (index == start) {
				return;
			}
		}
	}

	/// <summary>
	/// The selection steps' test: a gadget's viewport-relative rect strictly inside the viewport's screen rect
	/// <see cref="Rect"/>, left and top beyond its left and top and right and bottom short of its right and bottom.
	/// </summary>
	private bool InsideViewport(Es1Rect rect) =>
		Rect.X0 < rect.X0 && Rect.Y0 < rect.Y0 && rect.X1 < Rect.X1 && rect.Y1 < Rect.Y1;

	/// <summary>
	/// <c>ShellMap_Dtor</c> (<c>00009a73</c>)'s step that reaches past the map (docs/es1/shell/map.md#destroying-it): the
	/// sun unregistered with <c>Light_Unregister</c> (<c>00053850</c>). The rest frees the map's own memory.
	/// </summary>
	public void Destroy() => Lights.Unregister(Sun);

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
				var from = PointAt(path[0]);
				var to = PointAt(path[1]);
				heading = unchecked((short)(Es1ShellTrig.Atan2(to.X - from.X, to.Y - from.Y) - 0x4000));
			}
		} else {
			heading = squad.HeadingRef < Headings.Count ? Headings[squad.HeadingRef] : (short)0;
		}

		var rotation = Es1ShellTrig.Rotation2D(heading);
		var start = squad.PointRef == -1 ? NavPath is { Length: > 0 } route ? PointAt(route[0]) : default : PointAt(squad.PointRef);
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
				Widen(PointAt(structure.PointRef));
			}
		}

		WidenSquad(Widen);
		if (NavPath != null) {
			foreach (short waypoint in NavPath) {
				if (waypoint != -1) {
					Widen(PointAt(waypoint));
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
	public Es1MapPoint PointAt(int index) => index >= 0 && index < Points.Count ? Points[index] : default;

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
