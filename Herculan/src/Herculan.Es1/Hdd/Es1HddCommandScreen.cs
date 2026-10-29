using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Numerics;
using Herculan.Es1.Mfd;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Hdd;

/// <summary>
/// Page 0 of the Heads-Down Display, the command screen (docs/es1/simulation/hdd-command-screen.md): a map of the
/// mission area beside a column of eight squad orders under a message row, where the player picks a squadmate, an order
/// and, for four of the orders, a unit or a gridpoint on the map, then sends it with <c>XMIT</c>. Its slots are
/// <c>HDDMapVtable</c> (<c>00094886</c>)'s: <see cref="Repaint"/>, <see cref="Update"/>, <see cref="Hide"/>,
/// <see cref="Show"/> and <see cref="KeyDispatch"/>. The markers are <see cref="Es1HddMarker"/> and the two click
/// regions <see cref="Es1HddListGadget"/>.
/// </summary>
public sealed class Es1HddCommandScreen : Es1HddPage {
	/// <summary>The markers the constructor builds, 75.</summary>
	public const int MarkerCount = 75;

	/// <summary>The order rows, the squad orders 10 to 17 of <c>STRINGS0.STR</c> group 0.</summary>
	public const int RowCount = 8, FirstOrder = 10;

	/// <summary>The orders that want a pick: <c>ATTACK ENEMY</c>, <c>DEFEND POSITION</c>, <c>PATROL GRIDPOINT</c>, <c>GOTO GRIDPOINT</c>.</summary>
	public const int AttackOrder = 11, DefendOrder = 12, PatrolOrder = 13, GotoOrder = 14;

	/// <summary>The value of <see cref="ArmedOrder"/> and <see cref="SelectedSlot"/> for none.</summary>
	public const int None = -1;

	/// <summary><c>STRINGS0.STR</c>'s group of the squad orders, and of the message row's four texts (docs/es1/formats/str-strings.md#the-groups).</summary>
	public const int OrderGroup = 0, MessageGroup = 28;

	/// <summary>The message row's texts by entry: <c>SELECT PILOT</c>, <c>SELECT COMMAND</c>, <c>DESIGNATE LOCATION</c>, <c>DESIGNATE TARGET</c>.</summary>
	public const int SelectPilotText = 0, SelectCommandText = 1, DesignateLocationText = 2, DesignateTargetText = 3;

	/// <summary>The magnifiers' step and the scale's two limits.</summary>
	public const int ZoomStep = 50000, MaxScale = 1000000, MinScale = 50000;

	/// <summary>How far the map's center may be panned from the origin on each axis.</summary>
	public const int PanLimit = 5000000;

	/// <summary>What the constructor grows the mission box by on every edge.</summary>
	public const int BoxMargin = 60000;

	/// <summary>The world span whose screen span the grid's lines are a sixteenth of: squares of 50000 units.</summary>
	public const int GridWorldSpan = 800000;

	/// <summary><c>TSCamera_Ctor</c>'s near plane and perspective shift for the map's camera: a focal length of 2⁷ = 128 pixels.</summary>
	public const int NearPlane = 0x80, FocalShift = 7;

	/// <summary>
	/// The first <c>int16</c> of <c>HddMapView_Angles</c> (<c>000b1604</c>), which <c>HddCommandScreen_StaticInit</c>
	/// (<c>000420b0</c>) sets with roll and heading 0: the camera's pitch, straight down.
	/// </summary>
	public const short Pitch = unchecked((short)0xc000);

	/// <summary>The clock counts between two blink ticks: half a second.</summary>
	public const uint BlinkCounts = 0x1e;

	/// <summary>The blink ticks a message row set with flag 1 stays up: three seconds.</summary>
	public const short MessageTicks = 6;

	/// <summary>The palette indices the screen draws in.</summary>
	public const byte ScreenFill = 0x3f, BarColor = 0x3b, MessageFill = 0x3a, MapFill = 0x36, GridColor = 0x37,
		BorderColor = 0x35, ChosenFrameColor = 0x3c;

	/// <summary>The one palette entry the map's ground takes, <c>Palette_SetRampsToRange</c>({<c>0x38</c>, 1}, 1, 14).</summary>
	public const byte GroundEntry = 0x38;

	/// <summary>The keys <see cref="KeyDispatch"/> takes, <c>HddCommandScreen_KeyCodes</c> (<c>000948a4</c>).</summary>
	public static readonly ushort[] KeyCodes = [0x20, 0x1e, 0x21, 0x14, 0x22, 0x18, 0x2e, 0x12, 0x2d, 0x0e, 0x34, 0x33, 0x1c, 0x0f, 0x4c];

	/// <summary><c>Label_SetRect</c>'s flags 1, which start the text at the anchor, and the order rows' margin.</summary>
	private const int LeftAligned = 1, RowMargin = 5;

	private readonly Es1HddPickRecord[] _picks = [new(), new(), new()];
	private readonly Es1HddMarker[] _markers = new Es1HddMarker[MarkerCount];
	private readonly Es1Label[] _rows = new Es1Label[RowCount];
	private readonly bool[] _available = new bool[RowCount];
	private readonly Es1Label _message;
	private readonly IReadOnlyList<DynamixBitmap>? _icons;
	private Transform3 _cameraFrame, _worldToView;

	/// <summary>
	/// <c>HddCommandScreen_Ctor</c> (<c>ES1DBSIM 0003ee58</c>)(0, <paramref name="display"/>, <paramref name="orderColumn"/>,
	/// <paramref name="screen"/>) (docs/es1/simulation/hdd-command-screen.md#building-it--hddcommandscreen_ctor-0003ee58):
	/// the page base; the three pick records; the blink deadline the clock + 30; the map viewport, the screen rect with
	/// its <i>x1</i> the order column's <i>x0</i> − 2; its render context's origin minus half the viewport, so (0, 0) is
	/// its center; the center (0, 0); the grown mission box and the full zoom-out scale 2 × max((((<i>x1</i> −
	/// <i>x0</i>) &gt;&gt; 1) &lt;&lt; 7) / <i>w</i>, the same on <i>y</i>) on it, <i>w</i> and <i>h</i> the origin's
	/// halves doubled; <see cref="RecomputePanStep"/>; the two click regions over the order column and the map viewport,
	/// registered in that order on the cockpit's root; the message row, a <c>cpred</c> label centered over the column's
	/// width one <c>cpgreen</c> cell tall, on <see cref="MessageFill"/>; the eight order rows, <c>cpgreen</c> labels whose
	/// hotkey font is <c>cpred</c>, left-aligned with the margin (5, 0), row <i>i</i> from <i>y0</i> + (<i>i</i> + 1)
	/// <i>r</i>, <i>r</i> the column's height / 9; <see cref="DisableOrders"/>; no order armed; the bar's <i>x</i> from
	/// the column's <i>x0</i> + 1 to <i>x0</i> + 3; the 75 markers; nothing built; the map clean, its grid dirty and the
	/// blink phase set; and no squadmate selected. A viewport under two pixels wide or tall, where retail divides by
	/// zero, takes a width of 1, an engine guard.
	/// </summary>
	internal Es1HddCommandScreen(Es1HddDisplay display, Es1Rect orderColumn, Es1Rect screen)
		: base(display, orderColumn, screen) {
		var cockpit = display.Cockpit;
		BlinkDeadline = cockpit.Globals.Clock.Clock + BlinkCounts;
		MapViewport = screen with { X1 = orderColumn.X0 - 2 };
		MapOrigin = (-((MapViewport.X1 - MapViewport.X0) >> 1), -((MapViewport.Y1 - MapViewport.Y0) >> 1));
		var box = cockpit.Globals.MissionBox;
		GrownBox = new Es1MissionBox(box.MinX - BoxMargin, box.MinY - BoxMargin, box.MaxX + BoxMargin, box.MaxY + BoxMargin);
		int byWidth = (((GrownBox.MaxX - GrownBox.MinX) >> 1) << FocalShift) / Math.Max(MapOrigin.X * -2, 1);
		int byHeight = (((GrownBox.MaxY - GrownBox.MinY) >> 1) << FocalShift) / Math.Max(MapOrigin.Y * -2, 1);
		Scale = Math.Max(byWidth, byHeight) << 1;
		RecomputePanStep();

		OrderRegion = new Es1HddListGadget(this, orderColumn);
		cockpit.Root.RegisterClickable(OrderRegion);
		MapRegion = new Es1HddListGadget(this, MapViewport);
		cockpit.Root.RegisterClickable(MapRegion);

		var green = display.Font(Es1CockpitViews.CpGreen);
		var red = display.Font(Es1CockpitViews.CpRed);
		int cell = green?.CellHeight ?? 0, pitch = (orderColumn.Y1 - orderColumn.Y0) / 9;
		_message = new Es1Label(red) { FillColor = MessageFill };
		_message.SetRect(Es1Label.Centered, new Es1Rect(orderColumn.X0, orderColumn.Y0, orderColumn.X1, orderColumn.Y0 + cell));
		for (int i = 0; i < RowCount; i++) {
			int top = orderColumn.Y0 + (i + 1) * pitch;
			_rows[i] = new Es1Label(green) { HotkeyFont = red };
			_rows[i].SetRect(LeftAligned, new Es1Rect(orderColumn.X0, top, orderColumn.X1, top + cell), RowMargin);
		}

		DisableOrders();
		PreviousArmed = None;
		ArmedOrder = None;
		Bar = new Es1Rect(orderColumn.X0 + 1, 0, orderColumn.X0 + 3, 0);
		_icons = display.Resources?.Invoke("dba", "ICONS.DBA") is { } iconBytes
			? new DynamixBitmapArrayTransformer().Parse(iconBytes)?.Images
			: null;
		for (int i = 0; i < MarkerCount; i++) {
			_markers[i] = new Es1HddMarker(this);
		}

		GridDirty = true;
		BlinkPhase = true;
		SelectedSlot = None;
	}

	/// <summary><c>+0x18</c>, <c>+0x1c</c>, the map's center, the camera's <i>x</i> and <i>y</i>.</summary>
	public int CenterX { get; private set; }

	/// <inheritdoc cref="CenterX"/>
	public int CenterY { get; private set; }

	/// <summary><c>+0x20</c>, the scale, the camera's height: a scale <i>s</i> draws <i>s</i> / 128 world units to a pixel.</summary>
	public int Scale { get; private set; }

	/// <summary><c>+0x24</c>/<c>+0x28</c>, the world origin's point in the map's context, and <c>+0x2c</c>, the screen span of <see cref="GridWorldSpan"/>.</summary>
	public (int X, int Y) GridOrigin { get; private set; }

	/// <inheritdoc cref="GridOrigin"/>
	public int GridSpan { get; private set; }

	/// <summary><c>+0x30</c>, the grid wants recomputing.</summary>
	public bool GridDirty { get; private set; }

	/// <summary><c>+0x31</c>, the 75 markers; <see cref="MarkerTotal"/> of them built.</summary>
	public IReadOnlyList<Es1HddMarker> Markers => _markers;

	/// <summary><c>+0x35</c>, the order column's click region.</summary>
	public Es1HddListGadget OrderRegion { get; }

	/// <summary><c>+0x39</c>, the map's click region.</summary>
	public Es1HddListGadget MapRegion { get; }

	/// <summary><c>+0x3d</c>, the eight order rows' labels, orders 10 to 17.</summary>
	public IReadOnlyList<Es1Label> Rows => _rows;

	/// <summary><c>+0x5d</c>, the armed order, 10 to 17, or <see cref="None"/>; <c>XMIT</c> puts it in the order record as its verb.</summary>
	public int ArmedOrder { get; private set; }

	/// <summary><c>+0x81</c>, the order armed before it, which the update's row rewrite catches up.</summary>
	public int PreviousArmed { get; private set; }

	/// <summary><c>+0x61</c>, the armed row's bar: <i>x</i> from the column's <i>x0</i> + 1 to <i>x0</i> + 3, <i>y</i> from the row's.</summary>
	public Es1Rect Bar { get; private set; }

	/// <summary><c>+0x71</c>, the map viewport, in the canvas's coordinates.</summary>
	public Es1Rect MapViewport { get; }

	/// <summary>The map's render context's origin <c>+0x220</c>/<c>+0x224</c>, minus half the viewport.</summary>
	public (int X, int Y) MapOrigin { get; }

	/// <summary><c>+0x85</c>, the three pick records, one per squadmate (docs/retail/simulation/heads-down-display.md#the-link-line).</summary>
	public IReadOnlyList<Es1HddPickRecord> Picks => _picks;

	/// <summary><c>+0x103</c>, set while the armed order wants a pick: a comm box's press then puts up the map-pick cursor.</summary>
	public bool WantsPick { get; private set; }

	/// <summary><c>+0x104</c>, the order is ready to send, which <c>XMIT</c>'s press and paint test.</summary>
	public bool ReadyToSend { get; private set; }

	/// <summary><c>+0x105</c>, the selected squadmate's slot, or <see cref="None"/>.</summary>
	public int SelectedSlot { get; private set; }

	/// <summary><c>+0x109</c>, the markers are built.</summary>
	public bool MarkersBuilt { get; private set; }

	/// <summary><c>+0x10a</c>, the markers built, an <c>int16</c>.</summary>
	public short MarkerTotal { get; private set; }

	/// <summary><c>+0x114</c>, <c>+0x118</c>, the pan offsets added to the cockpit's machine's position for the map's center.</summary>
	public int PanX { get; private set; }

	/// <inheritdoc cref="PanX"/>
	public int PanY { get; private set; }

	/// <summary><c>+0x11c</c>, a map click is waiting, and <c>+0x11d</c>, its point.</summary>
	public bool MapClickWaiting { get; private set; }

	/// <inheritdoc cref="MapClickWaiting"/>
	public (int X, int Y) MapClickPoint { get; private set; }

	/// <summary><c>+0x125</c>–<c>+0x12d</c>, the center and the scale the map's last paint used.</summary>
	public (int X, int Y, int Scale) LastView { get; private set; }

	/// <summary><c>+0x131</c>, eight availability bytes, one per order.</summary>
	public IReadOnlyList<bool> Available => _available;

	/// <summary><c>+0x139</c>, the order list wants redrawing.</summary>
	public bool OrdersDirty { get; private set; }

	/// <summary><c>+0x13a</c>, the message row's label.</summary>
	public Es1Label MessageRow => _message;

	/// <summary><c>+0x13e</c>, the message row's countdown in blink ticks, an <c>int16</c>.</summary>
	public short MessageCountdown { get; private set; }

	/// <summary><c>+0x140</c>, the message row's text.</summary>
	public string? MessageText { get; private set; }

	/// <summary><c>+0x144</c>, clear the message row at the next update.</summary>
	public bool ClearMessagePending { get; private set; }

	/// <summary><c>+0x145</c>, the pan step, <see cref="RecomputePanStep"/>'s.</summary>
	public int PanStep { get; private set; }

	/// <summary><c>+0x149</c>, the clock count after which the next blink tick runs.</summary>
	public uint BlinkDeadline { get; private set; }

	/// <summary><c>+0x14d</c>, a list click is queued; <c>+0x14e</c> its click region; <c>+0x152</c> its point; <c>+0x15b</c> it was a double click.</summary>
	public bool ListClickWaiting { get; private set; }

	/// <inheritdoc cref="ListClickWaiting"/>
	public Es1HddListGadget? ListClickRegion { get; private set; }

	/// <inheritdoc cref="ListClickWaiting"/>
	public (int X, int Y) ListClickPoint { get; private set; }

	/// <inheritdoc cref="ListClickWaiting"/>
	public bool ListClickDouble { get; private set; }

	/// <summary><c>+0x15a</c>, the queued click came from a key.</summary>
	public bool KeyClick { get; private set; }

	/// <summary><c>+0x15c</c>, the chosen unit, which <c>Tab</c> sets and <c>Enter</c> picks.</summary>
	public IEs1SimObject? ChosenUnit { get; private set; }

	/// <summary><c>+0x160</c>, the mission box grown by <see cref="BoxMargin"/> on every edge.</summary>
	public Es1MissionBox GrownBox { get; }

	/// <summary><c>+0x170</c>, the map cursor: the last map click as a world point.</summary>
	public Vec3i MapCursor { get; private set; }

	/// <summary><c>+0x17c</c>, the pick cursor is up.</summary>
	public bool PickCursorUp { get; private set; }

	/// <summary><c>+0x17d</c>, a squadmate is selected.</summary>
	public bool PilotSelected { get; private set; }

	/// <summary>
	/// <c>HddCommandScreen_MarkerRepaintPending</c> (<c>00094866</c>), a global: the update repaints the selected
	/// squadmate's marker; kept with the screen, of which the simulator builds one, as the next two.
	/// </summary>
	public bool MarkerRepaintPending { get; private set; }

	/// <summary><c>HddCommandScreen_BlinkPhase</c> (<c>00094867</c>), a global: while set, the selected squadmate's marker is not painted.</summary>
	public bool BlinkPhase { get; private set; }

	/// <summary><c>HddCommandScreen_MapDirty</c> (<c>00094868</c>), a global: the update redraws the map.</summary>
	public bool MapDirty { get; private set; }

	/// <summary><c>HddMarker_Icons</c> (<c>00094870</c>), a global: the frames of <c>DBA\ICONS.DBA</c>; none when the file is missing.</summary>
	public IReadOnlyList<DynamixBitmap>? Icons => _icons;

	/// <summary>Frame <paramref name="index"/> of <see cref="Icons"/>, or none.</summary>
	internal DynamixBitmap? Icon(int index) => _icons != null && index >= 0 && index < _icons.Count ? _icons[index] : null;

	private Es1Cockpit Cockpit => Display.Cockpit;

	// ---- The slots.

	/// <summary>
	/// Slot <c>+4</c>, <c>HddCommandScreen_Repaint</c> (<c>0003f490</c>) (docs/es1/simulation/hdd-command-screen.md#drawing-it):
	/// the screen rect filled <see cref="ScreenFill"/>; <see cref="RefreshOrders"/>; with an order armed, the bar from
	/// its row's <i>y0</i> + 3 to its <i>y1</i> filled <see cref="BarColor"/>; the paints of <c>XMIT</c> and
	/// <c>CANCEL</c>; <see cref="BuildMapMarkers"/> while the markers are not built; <see cref="DrawMap"/>; and the
	/// message row's text again while its countdown runs and no clear is pending.
	/// </summary>
	public override void Repaint() {
		var canvas = Cockpit.PushCanvasContext();
		canvas.FillRect(Screen.X0, Screen.Y0, Screen.X1, Screen.Y1, ScreenFill);
		RefreshOrders(canvas);
		if (ArmedOrder != None) {
			MoveBar(_rows[ArmedOrder - FirstOrder]);
			canvas.FillRect(Bar.X0, Bar.Y0, Bar.X1, Bar.Y1, BarColor);
		}

		Display.Buttons[Es1HddDisplay.XmitButton].Paint();
		Display.Buttons[Es1HddDisplay.CancelButton].Paint();
		if (!MarkersBuilt) {
			BuildMapMarkers();
		}

		DrawMap();
		if (MessageCountdown != 0 && !ClearMessagePending) {
			_message.SetText(canvas, MessageText);
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>HddCommandScreen_Update</c> (<c>0003f578</c>) (docs/es1/simulation/hdd-command-screen.md#drawing-it):
	/// a queued list click to <see cref="HandleListClick"/>; once the clock passes <see cref="BlinkDeadline"/>, the
	/// deadline moved on and <see cref="BlinkTick"/>; a waiting map click to <see cref="PickPilot"/> with no pick wanted or
	/// no squadmate selected, else <see cref="PickTarget"/>; with a squadmate selected whose saved pick is a unit, either
	/// held byte set and that unit destroyed, both held bytes cleared. Then at stage 1 or 2 alone: the selected
	/// squadmate's marker painted (<see cref="Es1HddMarker.PaintUnlessPainting"/>) and its counter cleared while a marker
	/// repaint is pending; with the list clean and the armed order changed, the old row rewritten in <c>cpgreen</c>, or
	/// wholly in <c>ColorSchemePanels</c>[0] while unavailable, the bar filled <see cref="ScreenFill"/> and the row
	/// copied, then the new row the same in <c>cpylw</c> with the bar moved to it and filled <see cref="BarColor"/>; with
	/// it dirty, <see cref="RefreshOrders"/> and the column from the first row to the last copied; a pending clear
	/// (<see cref="ClearMessageRow"/>); and with the map dirty, the viewport filled <see cref="ScreenFill"/>,
	/// <see cref="DrawMap"/>, the display's source and draw pages both made its <c>+0x8c</c> page, the pilot channel's
	/// slot <c>+0x18</c>, the viewport copied to the page on the screen and the channel's slot <c>+0x14</c>, the stage 0
	/// and the map clean. Every copy is <see cref="Es1Cockpit.DrawToCockpit"/> with 1. A saved unit pick with no unit,
	/// which retail reads through, is left as it is, an engine guard.
	/// </summary>
	public override void Update() {
		if (ListClickWaiting) {
			HandleListClick();
		}

		uint now = Cockpit.Globals.Clock.Clock;
		if (BlinkDeadline < now) {
			BlinkDeadline = now + BlinkCounts;
			BlinkTick();
		}

		if (MapClickWaiting) {
			if (!WantsPick || SelectedSlot == None) {
				PickPilot();
			} else {
				PickTarget();
			}
		}

		if (SelectedPick is { } pick && (pick.SavedHeld || pick.CurrentHeld) && pick.SavedKind == Es1HddPickRecord.UnitKind
				&& pick.SavedUnit is { Destroyed: true }) {
			pick.SavedHeld = false;
			pick.CurrentHeld = false;
		}

		if (Stage is not (1 or 2)) {
			return;
		}

		if (MarkerRepaintPending) {
			if (SelectedSlot != None && LiveMarker(SelectedSlot) is { } marker) {
				marker.PaintUnlessPainting();
				marker.PaintCounter = 0;
			}

			MarkerRepaintPending = false;
		}

		var canvas = Cockpit.PushCanvasContext();
		if (!OrdersDirty) {
			if (PreviousArmed != ArmedOrder) {
				if (PreviousArmed != None) {
					var old = _rows[PreviousArmed - FirstOrder];
					SetRowFonts(old, PreviousArmed, Es1CockpitViews.CpGreen);
					WriteOrder(canvas, old, PreviousArmed);
					canvas.FillRect(Bar.X0, Bar.Y0, Bar.X1, Bar.Y1, ScreenFill);
					Cockpit.DrawToCockpit(old.Rect);
				}

				if (ArmedOrder != None) {
					var row = _rows[ArmedOrder - FirstOrder];
					SetRowFonts(row, ArmedOrder, Es1CockpitViews.CpYellow);
					WriteOrder(canvas, row, ArmedOrder);
					MoveBar(row);
					canvas.FillRect(Bar.X0, Bar.Y0, Bar.X1, Bar.Y1, BarColor);
					Cockpit.DrawToCockpit(row.Rect);
				}

				PreviousArmed = ArmedOrder;
			}
		} else {
			RefreshOrders(canvas);
			var first = _rows[0].Rect;
			var last = _rows[RowCount - 1].Rect;
			Cockpit.DrawToCockpit(new Es1Rect(first.X0, first.Y0, last.X1, last.Y1));
		}

		if (ClearMessagePending) {
			ClearMessageRow();
		}

		if (MapDirty) {
			canvas.FillRect(MapViewport.X0, MapViewport.Y0, MapViewport.X1, MapViewport.Y1, ScreenFill);
			DrawMap();
			var video = Cockpit.Display;
			video.SourcePage = video.PairDrawn;
			video.DrawPage = video.PairDrawn;
			var channel = Cockpit.PilotChannel;
			channel.Paint();
			Cockpit.DrawToCockpit(MapViewport);
			channel.RestoreBackground();
			Stage = 0;
			MapDirty = false;
		}
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>HddCommandScreen_Hide</c> (<c>0003fc14</c>): the order column's region and the display's
	/// <c>XMIT</c> and <c>CANCEL</c> put at state 2, the map's region left as it is, and with the pick cursor up the
	/// arrow in the other views' cursor slot.
	/// </summary>
	public override void Hide() => SetShown(Es1Widget.Hidden, Es1CockpitCursors.Arrow);

	/// <summary>
	/// Slot <c>+0x10</c>, <c>HddCommandScreen_Show</c> (<c>0003fbb8</c>): the same regions and buttons put at state 0,
	/// and with the pick cursor up the map-pick cursor, <c>MCURSOR</c>, in the other views' cursor slot.
	/// </summary>
	public override void Show() => SetShown(Es1Widget.Up, Es1CockpitCursors.MapPick);

	/// <summary>
	/// Slot <c>+0x14</c>, <c>HddCommandScreen_KeyDispatch</c> (<c>0003f938</c>)(screen, <paramref name="code"/>)
	/// (docs/es1/simulation/hdd-command-screen.md#clicks-and-keys): one of <see cref="KeyCodes"/> acts and answers 1, any
	/// other code 0. <c>D</c>, <c>A</c>, <c>F</c>, <c>T</c>, <c>G</c>, <c>O</c>, <c>C</c> and <c>E</c> click order rows 10
	/// to 17 (<see cref="SynthesizeListClick"/>); <c>X</c> and Backspace press <c>XMIT</c> and <c>CANCEL</c>
	/// (<see cref="Es1HddDisplay.PressButton"/>); <c>.</c> and <c>,</c> <see cref="NextOrder"/> and
	/// <see cref="PreviousOrder"/>; <c>Enter</c> and <c>Tab</c> with a pick wanted, <see cref="PickByEnter"/> and
	/// <see cref="CycleChosenUnit"/>; keypad 5 the pan offsets 0.
	/// </summary>
	public override short KeyDispatch(ushort code) {
		int key = Array.IndexOf(KeyCodes, code);
		switch (key) {
			case < 0:
				return 0;
			case < RowCount:
				SynthesizeListClick(FirstOrder + key, OrderRegion);
				break;
			case RowCount:
				Display.PressButton(Es1HddDisplay.XmitButton);
				break;
			case RowCount + 1:
				Display.PressButton(Es1HddDisplay.CancelButton);
				break;
			case RowCount + 2:
				NextOrder();
				break;
			case RowCount + 3:
				PreviousOrder();
				break;
			case RowCount + 4:
				PickByEnter();
				break;
			case RowCount + 5:
				CycleChosenUnit();
				break;
			default:
				PanX = 0;
				PanY = 0;
				break;
		}

		return 1;
	}

	// ---- The map.

	/// <summary>
	/// <c>HddMap_ZoomOut</c> (<c>0003fc70</c>) (docs/es1/simulation/hdd-command-screen.md#the-map): while the scale plus
	/// <see cref="ZoomStep"/> is at most <see cref="MaxScale"/>, the step added, <see cref="RecomputePanStep"/>, the grid
	/// dirty and the map dirty.
	/// </summary>
	public void ZoomOut() {
		if (Scale + ZoomStep <= MaxScale) {
			Zoom(ZoomStep);
		}
	}

	/// <summary><c>HddMap_ZoomIn</c> (<c>0003fca4</c>): while the scale less <see cref="ZoomStep"/> is at least <see cref="MinScale"/>, the step taken off, as <see cref="ZoomOut"/>.</summary>
	public void ZoomIn() {
		if (Scale - ZoomStep >= MinScale) {
			Zoom(-ZoomStep);
		}
	}

	/// <summary>
	/// <c>HddMap_PanLeft</c> (<c>0003fcd4</c>): while the center's <i>x</i> less the pan step stays at or above
	/// −<see cref="PanLimit"/>, the step taken off the <i>x</i> pan offset and the map dirty; otherwise nothing.
	/// </summary>
	public void PanLeft() {
		if (CenterX - PanStep >= -PanLimit) {
			PanX -= PanStep;
			MapDirty = true;
		}
	}

	/// <summary><c>HddMap_PanRight</c> (<c>0003fd00</c>): while the center's <i>x</i> plus the step stays at or below <see cref="PanLimit"/>, the step added to the <i>x</i> offset.</summary>
	public void PanRight() {
		if (CenterX + PanStep <= PanLimit) {
			PanX += PanStep;
			MapDirty = true;
		}
	}

	/// <summary><c>HddMap_PanUp</c> (<c>0003fd2c</c>): while the center's <i>y</i> plus the step stays at or below <see cref="PanLimit"/>, the step added to the <i>y</i> offset.</summary>
	public void PanUp() {
		if (CenterY + PanStep <= PanLimit) {
			PanY += PanStep;
			MapDirty = true;
		}
	}

	/// <summary><c>HddMap_PanDown</c> (<c>0003fd58</c>): while the center's <i>y</i> less the step stays at or above −<see cref="PanLimit"/>, the step taken off the <i>y</i> offset.</summary>
	public void PanDown() {
		if (CenterY - PanStep >= -PanLimit) {
			PanY -= PanStep;
			MapDirty = true;
		}
	}

	/// <summary>
	/// <c>HddMap_RecomputePanStep</c> (<c>00041994</c>): the pan step (((scale − <see cref="MinScale"/>) &gt;&gt; 3) ×
	/// 11875 / 118750) × 8 + 5000, the product a 32-bit one that wraps as retail's does.
	/// </summary>
	public void RecomputePanStep() => PanStep = unchecked(((Scale - MinScale) >> 3) * 11875) / 118750 * 8 + 5000;

	/// <summary><c>HddMap_ToViewport</c> (<c>0003fd84</c>)(screen, out, <paramref name="point"/>): the point plus the viewport's corner and its half-extent.</summary>
	public (int X, int Y) ToViewport((int X, int Y) point) =>
		(point.X + MapViewport.X0 + ((MapViewport.X1 - MapViewport.X0) >> 1),
			point.Y + MapViewport.Y0 + ((MapViewport.Y1 - MapViewport.Y0) >> 1));

	/// <summary><c>HddMap_FromViewport</c> (<c>0003fdc4</c>)(screen, out, <paramref name="point"/>): the point less the viewport's corner and its half-extent.</summary>
	public (int X, int Y) FromViewport((int X, int Y) point) =>
		(point.X - MapViewport.X0 - ((MapViewport.X1 - MapViewport.X0) >> 1),
			point.Y - MapViewport.Y0 - ((MapViewport.Y1 - MapViewport.Y0) >> 1));

	/// <summary>
	/// <c>HddCommandScreen_InstallMapContext</c> (<c>0003fe04</c>)(screen): the map's render context, its rect the map
	/// viewport less <paramref name="installed"/>'s origin, its origin <see cref="MapOrigin"/>, clip mode 1 and the
	/// translation set, so (0, 0) draws at the viewport's center. The context is handed to what draws rather than
	/// installed (<see cref="Es1PanelCanvas"/>).
	/// </summary>
	public Es1PanelCanvas InstallMapContext(Es1PanelCanvas installed) {
		var origin = installed.Origin;
		return new Es1PanelCanvas(installed.Display, new Es1Rect(MapViewport.X0 - origin.X, MapViewport.Y0 - origin.Y,
			MapViewport.X1 - origin.X, MapViewport.Y1 - origin.Y), MapOrigin);
	}

	/// <summary>
	/// <c>HddCommandScreen_HitTestMarker</c> (<c>00040410</c>)(screen, <paramref name="point"/>)
	/// (docs/es1/simulation/hdd-command-screen.md#the-map): the camera installed (<see cref="InstallCamera"/>); the point
	/// back through <see cref="FromViewport"/> with <i>y</i> negated, (<i>x</i> × scale &gt;&gt; 7, scale, <i>y</i> ×
	/// scale &gt;&gt; 7) put through the camera's frame into <see cref="MapCursor"/>; the same for (<i>x</i> − 5,
	/// <i>y</i> − 5), and <i>h</i> the cursor's <i>x</i> less that point's. Answers the first object of the live object
	/// list whose <i>x</i> and <i>y</i> both lie strictly within <i>h</i> of the cursor's, or none.
	/// </summary>
	public IEs1SimObject? HitTestMarker((int X, int Y) point) {
		InstallCamera();
		var (x, y) = FromViewport(point);
		y = -y;
		MapCursor = ViewToWorld(x, y);
		var corner = ViewToWorld(x - 5, y - 5);
		int reach = MapCursor.X - corner.X;
		foreach (var simObject in Cockpit.Globals.LiveObjects) {
			var p = simObject.Position;
			if (p.X - reach < MapCursor.X && p.Y - reach < MapCursor.Y && MapCursor.X < p.X + reach && MapCursor.Y < p.Y + reach) {
				return simObject;
			}
		}

		return null;
	}

	/// <summary>
	/// <c>HddCommandScreen_IsOnMap</c> (<c>00041bb8</c>)(screen, <paramref name="point"/>)
	/// (docs/es1/simulation/hdd-command-screen.md#tabs-step): the camera installed, and whether the point, projected and
	/// through <see cref="ToViewport"/>, lies in the map viewport, edges included.
	/// </summary>
	public bool IsOnMap(Vec3i point) {
		InstallCamera();
		var (x, y) = ToViewport(Project(point));
		return MapViewport.Contains(x, y);
	}

	// ---- Drawing.

	/// <summary>
	/// <c>HddCommandScreen_RefreshOrders</c> (<c>00040948</c>)(screen) (docs/es1/simulation/hdd-command-screen.md#the-order-list):
	/// each order row's fonts, wholly <c>ColorSchemePanels</c>[0] while unavailable or while the display is dark, else
	/// <c>cpylw</c> for the armed order and <c>cpgreen</c> for any other, each with its hotkey in <c>cpred</c>; its order
	/// written on <paramref name="canvas"/>; and the list clean. It copies nothing to the page on the screen.
	/// </summary>
	public void RefreshOrders(Es1PanelCanvas canvas) {
		bool dark = Display.Dropout.Dark;
		for (int row = 0; row < RowCount; row++) {
			int order = FirstOrder + row;
			var label = _rows[row];
			if (!_available[row] || dark) {
				label.Font = Display.Font(Es1CockpitViews.CpBlue);
				label.HotkeyFont = label.Font;
			} else {
				label.Font = Display.Font(order == ArmedOrder ? Es1CockpitViews.CpYellow : Es1CockpitViews.CpGreen);
				label.HotkeyFont = Display.Font(Es1CockpitViews.CpRed);
			}

			WriteOrder(canvas, label, order);
		}

		OrdersDirty = false;
	}

	/// <summary>
	/// <c>HddCommandScreen_BuildMapMarkers</c> (<c>00040a40</c>)(screen) (docs/es1/simulation/hdd-command-screen.md#building-the-markers),
	/// from <see cref="MarkerTotal"/> on: for the cockpit's machine's group's route, its points 1 to its count − 1, at
	/// most 9, marker <i>i</i> taking frame <i>i</i>, the point and the size 9; then each object of the live object list
	/// by its class, its group's side and, for a structure, its type's index: a HERC the player's own <c>0x12</c>, on
	/// side 0 in squad slot <i>n</i> <c>0x1b</c> + 9<i>n</i>, on side 0 otherwise <c>0x36</c>, on another side 9, each
	/// size 7; a flyer <c>0x3f</c>, size 5; a structure of type 5, 6 or 8 <c>0x4b</c> on side 0 and <c>0x49</c> on
	/// another, size 7, and of any other type <c>0x4a</c> or <c>0x48</c>, size 13, each ranged with that size, the pixel
	/// color <c>0x32</c> on side 0 and <c>0x35</c> on another and <see cref="Es1HddMarker.ApparentReferenceSize"/>; an
	/// object of another class none. Then the markers built. Retail counts past the 75 markers and writes through the
	/// pointer after the array (docs/es1/simulation/hdd-command-screen.md#open); this engine stops at 75, and skips an
	/// object with no group or a class-1 object that is not a structure, through which retail reads, engine guards. The
	/// markers past the count, which retail frees and leaves in the array, stay here unbuilt.
	/// </summary>
	public void BuildMapMarkers() {
		var machine = Cockpit.Machine;
		if (machine?.Group?.Route is { } route) {
			int count = Math.Min(route.Length - 1, 9);
			for (int i = 0; i < count && MarkerTotal < MarkerCount; i++) {
				var marker = _markers[MarkerTotal++];
				marker.Frame = (short)i;
				marker.RoutePoint = route[i + 1];
				marker.SetSize(9);
			}
		}

		foreach (var simObject in Cockpit.Globals.LiveObjects) {
			if (MarkerTotal >= MarkerCount) {
				break;
			}

			if (MarkerFor(simObject, machine) is not { } kind) {
				continue;
			}

			var marker = _markers[MarkerTotal++];
			marker.Frame = kind.Frame;
			marker.Object = simObject;
			marker.Class = simObject.TargetClass;
			marker.SetSize(kind.Size);
			marker.Ranged = kind.Ranged;
			if (kind.Ranged) {
				marker.RangedSize = (short)kind.Size;
				marker.MinimumApparentSize = 0;
				marker.PixelColor = kind.PixelColor;
				marker.ApparentReference = Es1HddMarker.ApparentReferenceSize;
			}
		}

		MarkersBuilt = true;
	}

	/// <summary>
	/// <c>HddCommandScreen_DrawMap</c> (<c>00040d4c</c>)(screen) (docs/es1/simulation/hdd-command-screen.md#drawing-the-map),
	/// in the map's context, in this order: the whole viewport filled <see cref="MapFill"/>, and nothing more while the
	/// display is dark. The center, the cockpit's machine's <i>x</i> and <i>y</i> plus the pan offsets, unclamped; the
	/// grid dirty and <see cref="LastView"/> taken when the center or the scale moved; the camera installed. The grid in
	/// <see cref="GridColor"/>: with it dirty, the world origin projected into <see cref="GridOrigin"/> and the screen
	/// <i>x</i> span to (<see cref="GridWorldSpan"/>, <see cref="GridWorldSpan"/>, 0) into <see cref="GridSpan"/>; the
	/// vertical lines at the origin's <i>x</i> + (<i>i</i> × span &gt;&gt; 4), <i>i</i> = 0, 1, … while left of the right
	/// edge and from − span &gt;&gt; 4 down while right of the left edge, each from the top edge to the bottom; the
	/// horizontal lines the same from the origin's <i>y</i>. The border: the grown box's two corners projected at height 0
	/// and framed (brush mode 4) in <see cref="BorderColor"/>. The ground: the zone's shapes, seen from the camera with
	/// every lit face in <see cref="GroundEntry"/>, drawn as a world pass into the map's rect
	/// (<see cref="Es1VideoMemory.DrawWorldView"/>, <see cref="Es1RectScene.HddMapZoneShapes"/>), where retail
	/// rasterizes them into the rect's palette indices (docs/herculan/plan-es1-first-mission.md#decisions, decision 19,
	/// this engine's departure); everything after is drawn over it. The markers, in build order (<see cref="PlaceMarker"/>).
	/// The chosen unit's frame: with its marker placed, the marker's rect back through <see cref="FromViewport"/>, grown
	/// by its <see cref="Es1HddMarker.DrawnHalfSize"/> on every side less a pixel at the right and the bottom, framed in
	/// <see cref="ChosenFrameColor"/>. With no machine, where retail reads through a null pointer, the center stays where
	/// it was, and a grid span of 0 or less, whose loops would never end in retail, draws no grid, engine guards.
	/// </summary>
	public void DrawMap() {
		var canvas = Cockpit.PushCanvasContext();
		var map = InstallMapContext(canvas);
		map.FillRect(MapOrigin.X, MapOrigin.Y, -MapOrigin.X, -MapOrigin.Y, MapFill);
		if (Display.Dropout.Dark) {
			return;
		}

		if (Cockpit.Machine is { } machine) {
			CenterX = machine.Position.X + PanX;
			CenterY = machine.Position.Y + PanY;
		}

		if (CenterX != LastView.X || CenterY != LastView.Y || Scale != LastView.Scale) {
			GridDirty = true;
			LastView = (CenterX, CenterY, Scale);
		}

		InstallCamera();
		if (GridDirty) {
			GridOrigin = Project(new Vec3i(0, 0, 0));
			GridSpan = Project(new Vec3i(GridWorldSpan, GridWorldSpan, 0)).X - GridOrigin.X;
			GridDirty = false;
		}

		DrawGrid(map);
		var low = Project(new Vec3i(GrownBox.MinX, GrownBox.MinY, 0));
		var high = Project(new Vec3i(GrownBox.MaxX, GrownBox.MaxY, 0));
		map.OutlineRect(low.X, low.Y, high.X, high.Y, BorderColor);
		canvas.Display.DrawWorldView(map.Rect, new Es1RectView(new Vec3i(CenterX, CenterY, Scale), (Pitch, 0, 0), NearPlane,
			FocalShift, (-MapOrigin.X, -MapOrigin.Y), map.Rect.X0, Es1RectScene.HddMapZoneShapes));

		Es1HddMarker? chosen = null;
		for (int i = 0; i < MarkerTotal; i++) {
			if (PlaceMarker(map, _markers[i])) {
				chosen = _markers[i];
			}
		}

		if (chosen != null) {
			var (x0, y0) = FromViewport((chosen.Rect.X0, chosen.Rect.Y0));
			var (x1, y1) = FromViewport((chosen.Rect.X1, chosen.Rect.Y1));
			int grow = chosen.DrawnHalfSize;
			map.OutlineRect(x0 - grow, y0 - grow, x1 + grow - 1, y1 + grow - 1, ChosenFrameColor);
		}
	}

	/// <summary>
	/// <c>HddCommandScreen_BlinkTick</c> (<c>0003fe83</c>): a marker repaint pending, the map dirty, the blink phase
	/// flipped, the display's page 0 update flag and paint stage 2, and the message countdown down by one while it runs,
	/// a clear pending when it reaches 0.
	/// </summary>
	public void BlinkTick() {
		MarkerRepaintPending = true;
		MapDirty = true;
		BlinkPhase = !BlinkPhase;
		Display.ScheduleUpdate(Es1HddDisplay.CommandPage);
		if (MessageCountdown != 0 && --MessageCountdown == 0) {
			ClearMessagePending = true;
		}
	}

	/// <summary>
	/// <c>HddCommandScreen_ClearMessageRow</c> (<c>000408dc</c>)(screen): inside the canvas push the message row's rect
	/// filled <see cref="ScreenFill"/> and copied to the page on the screen, and the pending clear cleared.
	/// </summary>
	public void ClearMessageRow() {
		var canvas = Cockpit.PushCanvasContext();
		var rect = _message.Rect;
		canvas.FillRect(rect.X0, rect.Y0, rect.X1, rect.Y1, ScreenFill);
		Cockpit.DrawToCockpit(rect);
		ClearMessagePending = false;
	}

	/// <summary>
	/// <c>HddCommandScreen_SetMessageRow</c> (<c>00040834</c>)(screen, <paramref name="flag"/>, <paramref name="text"/>)
	/// (docs/es1/simulation/hdd-command-screen.md#orders-and-picks): given no text, <c>SELECT PILOT</c> or
	/// <c>SELECT COMMAND</c> while no pick is wanted, by whether a squadmate is selected, and while one is
	/// <c>DESIGNATE TARGET</c> for <see cref="AttackOrder"/> and <c>DESIGNATE LOCATION</c> for any other;
	/// <see cref="ClearMessageRow"/>; the text kept, written on the row inside the canvas push and the row copied to the
	/// page on the screen; and a flag of 1 sets the countdown to <see cref="MessageTicks"/>. A pick is wanted only with
	/// 11 to 14 armed (<see cref="SelectOrder"/>), so the other orders, for which retail's register is stale, never reach
	/// the choice.
	/// </summary>
	public void SetMessageRow(int flag, string? text) {
		text ??= Display.Text(MessageGroup, !WantsPick
			? SelectedSlot == None ? SelectPilotText : SelectCommandText
			: ArmedOrder == AttackOrder ? DesignateTargetText : DesignateLocationText);
		ClearMessageRow();
		MessageText = text;
		_message.SetText(Cockpit.PushCanvasContext(), MessageText);
		Cockpit.DrawToCockpit(_message.Rect);
		if (flag == 1) {
			MessageCountdown = MessageTicks;
		}
	}

	// ---- Clicks and keys.

	/// <summary>
	/// <c>HddCommandScreen_QueueListClick</c> (<c>0003fed8</c>)(screen, <paramref name="point"/>, <paramref name="region"/>)
	/// (docs/es1/simulation/hdd-command-screen.md#clicks-and-keys): the click queued with the mouse's double-click flag,
	/// the region and the point, and the display's page 0 update flag and paint stage 2.
	/// </summary>
	public void QueueListClick((int X, int Y) point, Es1HddListGadget region) {
		ListClickWaiting = true;
		ListClickDouble = Cockpit.MouseDoubleClick;
		ListClickRegion = region;
		ListClickPoint = point;
		Display.ScheduleUpdate(Es1HddDisplay.CommandPage);
	}

	/// <summary>
	/// <c>HddCommandScreen_HandleListClick</c> (<c>0003ff34</c>)(screen): for the order column, the first row whose rect
	/// holds the point strictly on all four edges: with no squadmate selected, <see cref="SetMessageRow"/>(1,
	/// <c>SELECT PILOT</c>); otherwise on the armed row, with the double click and <see cref="ReadyToSend"/> set, a press
	/// of <c>XMIT</c>, on another row <see cref="SelectOrder"/> of its order, and for a click a key made the pointer put
	/// at the map viewport's center on the screen (<c>Cursor_SyncPosition</c>) and the key flag cleared. For the map, the
	/// point kept as the waiting map click and the display's page 0 update flag and paint stage 2. Then the queue clear.
	/// </summary>
	public void HandleListClick() {
		var region = ListClickRegion;
		var (x, y) = ListClickPoint;
		if (ReferenceEquals(region, OrderRegion)) {
			for (int row = 0; row < RowCount; row++) {
				var rect = _rows[row].Rect;
				if (!(rect.Y0 < y && y < rect.Y1 && rect.X0 < x && x < rect.X1)) {
					continue;
				}

				if (SelectedSlot == None) {
					SetMessageRow(1, Display.Text(MessageGroup, SelectPilotText));
				} else {
					if (FirstOrder + row == ArmedOrder) {
						if (ListClickDouble && ReadyToSend) {
							Display.PressButton(Es1HddDisplay.XmitButton);
						}
					} else {
						SelectOrder(FirstOrder + row);
					}

					if (KeyClick) {
						Cockpit.Cursor.SyncPosition(
							((MapViewport.X1 - MapViewport.X0) >> 1) + MapViewport.X0 + Cockpit.CanvasRect.X0 - Cockpit.CanvasOrigin.X,
							((MapViewport.Y1 - MapViewport.Y0) >> 1) + MapViewport.Y0 + Cockpit.CanvasRect.Y0 - Cockpit.CanvasOrigin.Y);
					}

					KeyClick = false;
				}

				break;
			}
		}

		if (ReferenceEquals(region, MapRegion)) {
			MapClickPoint = (x, y);
			MapClickWaiting = true;
			Display.ScheduleUpdate(Es1HddDisplay.CommandPage);
		}

		ListClickWaiting = false;
	}

	/// <summary>
	/// <c>HddCommandScreen_SynthesizeListClick</c> (<c>00040114</c>)(screen, <paramref name="pick"/>,
	/// <paramref name="region"/>), the keys' way in: the key flag set; for the order column, row <paramref name="pick"/> −
	/// 10's top-left plus (1, 1); for the map, pick 0 the pointer's position on the canvas (<c>Cursor_GetPosition</c>, less
	/// the canvas context's rect corner, plus its origin) and pick 1 the chosen unit's position projected through the
	/// camera and <see cref="ToViewport"/>; each to <see cref="QueueListClick"/>. Pick 1 with no chosen unit, where retail
	/// reads through a null pointer, queues nothing, an engine guard.
	/// </summary>
	public void SynthesizeListClick(int pick, Es1HddListGadget region) {
		KeyClick = true;
		if (ReferenceEquals(region, OrderRegion)) {
			var rect = _rows[pick - FirstOrder].Rect;
			QueueListClick((rect.X0 + 1, rect.Y0 + 1), region);
		} else if (ReferenceEquals(region, MapRegion)) {
			if (pick == 0) {
				var (x, y) = Cockpit.Cursor.GetPosition();
				QueueListClick((x - Cockpit.CanvasRect.X0 + Cockpit.CanvasOrigin.X, y - Cockpit.CanvasRect.Y0 + Cockpit.CanvasOrigin.Y),
					region);
			} else if (pick == 1 && ChosenUnit is { } unit) {
				InstallCamera();
				QueueListClick(ToViewport(Project(unit.Position)), region);
			}
		}
	}

	// ---- Orders and picks.

	/// <summary>
	/// <c>HddCommandScreen_SelectOrder</c> (<c>000405ac</c>)(screen, <paramref name="order"/>)
	/// (docs/es1/simulation/hdd-command-screen.md#orders-and-picks): for 11 to 14 the map-pick cursor, <c>MCURSOR</c>, in
	/// the other views' slot, <see cref="SetMessageRow"/>(0, <c>DESIGNATE TARGET</c> for 11 and <c>DESIGNATE LOCATION</c>
	/// for the rest), a pick wanted, the pick cursor up and not ready to send; for any other order, <see cref="None"/>
	/// included, the arrow, no pick wanted, ready to send, the pick cursor down and <see cref="ClearMessageRow"/>. A new
	/// order is armed, the old one kept as <see cref="PreviousArmed"/>, with the display's page 0 update flag and paint
	/// stage 2.
	/// </summary>
	public void SelectOrder(int order) {
		if (order is >= AttackOrder and <= GotoOrder) {
			Cockpit.Cursors.SetOffForwardCursor(Es1CockpitCursors.MapPick);
			SetMessageRow(0, Display.Text(MessageGroup, order == AttackOrder ? DesignateTargetText : DesignateLocationText));
			WantsPick = true;
			PickCursorUp = true;
			ReadyToSend = false;
		} else {
			Cockpit.Cursors.SetOffForwardCursor(Es1CockpitCursors.Arrow);
			WantsPick = false;
			ReadyToSend = true;
			PickCursorUp = false;
			ClearMessageRow();
		}

		if (ArmedOrder != order) {
			PreviousArmed = ArmedOrder;
			ArmedOrder = order;
			Display.ScheduleUpdate(Es1HddDisplay.CommandPage);
		}
	}

	/// <summary>
	/// <c>HddCommandScreen_SelectPilot</c> (<c>00040664</c>)(screen, <paramref name="slot"/>), from
	/// <see cref="Es1HddDisplay.SelectPilot"/> and <see cref="CancelTransmission"/>: with a squadmate selected, its
	/// <see cref="TogglePilotButton"/>, and with an order armed <see cref="SelectOrder"/>(<see cref="None"/>), its current
	/// pick dropped, not ready, no squadmate, no chosen unit, no pick wanted and a message clear pending; then the slot
	/// stored; for <see cref="None"/> <see cref="DisableOrders"/>, otherwise its <see cref="TogglePilotButton"/>,
	/// <see cref="EnableOrders"/> and a squadmate selected; the display's page 0 update flag and paint stage 2.
	/// </summary>
	public void SelectPilot(int slot) {
		if (SelectedSlot != None) {
			TogglePilotButton(SelectedSlot);
			if (ArmedOrder != None) {
				SelectOrder(None);
				_picks[SelectedSlot].CurrentHeld = false;
				ReadyToSend = false;
				PilotSelected = false;
				ClearMessagePending = true;
				ChosenUnit = null;
				WantsPick = false;
			}
		}

		SelectedSlot = slot;
		if (slot == None) {
			DisableOrders();
		} else {
			TogglePilotButton(slot);
			EnableOrders();
			PilotSelected = true;
		}

		Display.ScheduleUpdate(Es1HddDisplay.CommandPage);
	}

	/// <summary>
	/// <c>HddCommandScreen_FillOrderRecord</c> (<c>0004070c</c>)(screen, <paramref name="order"/>): with a squadmate
	/// selected, its saved pick into the record, a unit into the subject <c>+0x12</c> and a point into <c>+6</c>; the
	/// record's other field is left as it was.
	/// </summary>
	public void FillOrderRecord(Es1SquadOrder order) {
		if (SelectedPick is not { } pick) {
			return;
		}

		if (pick.SavedKind == Es1HddPickRecord.UnitKind) {
			order.Subject = pick.SavedUnit;
		} else if (pick.SavedKind == Es1HddPickRecord.PointKind) {
			order.Point = pick.SavedPoint;
		}
	}

	/// <summary>
	/// <c>HddCommandScreen_CommitPick</c> (<c>0004075c</c>)(screen), <c>XMIT</c>'s first step: on the selected squadmate's
	/// record, with no pick wanted the saved pick dropped; with one, the current pick's point or unit, by its kind, made
	/// the saved one, the current dropped and the saved held. Then not ready to send. With no squadmate selected, where
	/// retail writes before the first record, the records are left, an engine guard.
	/// </summary>
	public void CommitPick() {
		if (SelectedPick is { } pick) {
			if (!WantsPick) {
				pick.SavedHeld = false;
			} else {
				if (pick.CurrentKind == Es1HddPickRecord.PointKind) {
					pick.SavedPoint = pick.CurrentPoint;
				} else if (pick.CurrentKind == Es1HddPickRecord.UnitKind) {
					pick.SavedUnit = pick.CurrentUnit;
				}

				pick.CurrentHeld = false;
				pick.SavedKind = pick.CurrentKind;
				pick.SavedHeld = true;
			}
		}

		ReadyToSend = false;
	}

	/// <summary>
	/// <c>HddCommandScreen_CancelTransmission</c> (<c>000407d4</c>)(screen), <c>CANCEL</c>'s and <c>XMIT</c>'s last step:
	/// <see cref="SelectOrder"/>(<see cref="None"/>), the selected squadmate's current pick dropped,
	/// <see cref="SelectPilot"/>(<see cref="None"/>), then not ready, no squadmate, no chosen unit, no pick wanted and a
	/// message clear pending.
	/// </summary>
	public void CancelTransmission() {
		SelectOrder(None);
		if (SelectedPick is { } pick) {
			pick.CurrentHeld = false;
		}

		SelectPilot(None);
		ReadyToSend = false;
		PilotSelected = false;
		ClearMessagePending = true;
		ChosenUnit = null;
		WantsPick = false;
	}

	/// <summary><c>HddCommandScreen_EnableOrders</c> (<c>000418ac</c>)(screen): all eight orders available and the list dirty.</summary>
	public void EnableOrders() => SetAvailable(true);

	/// <summary><c>HddCommandScreen_DisableOrders</c> (<c>000418d8</c>)(screen): no order available and the list dirty.</summary>
	public void DisableOrders() => SetAvailable(false);

	/// <summary>
	/// <c>HddCommandScreen_TogglePilotButton</c> (<c>00041884</c>)(screen, <paramref name="slot"/>): the marker in that
	/// place's <see cref="Es1HddMarker.Toggled"/> flipped and its <see cref="Es1HddMarker.Invalidate"/>. A marker past
	/// the count, which retail has freed, is left, an engine guard.
	/// </summary>
	public void TogglePilotButton(int slot) {
		if (LiveMarker(slot) is { } marker) {
			marker.Toggled = !marker.Toggled;
			marker.Invalidate();
		}
	}

	/// <summary>
	/// <c>HddCommandScreen_NextOrder</c> (<c>00041904</c>)(screen), <c>.</c>: with an order armed, <see cref="SelectOrder"/>
	/// of the next, 17 wrapping to 10, and the display's page 0 update flag and paint stage 2.
	/// </summary>
	public void NextOrder() {
		if (ArmedOrder != None) {
			int next = ArmedOrder - FirstOrder + 1;
			SelectOrder(FirstOrder + (next > RowCount - 1 ? 0 : next));
			Display.ScheduleUpdate(Es1HddDisplay.CommandPage);
		}
	}

	/// <summary><c>HddCommandScreen_PreviousOrder</c> (<c>0004194c</c>)(screen), <c>,</c>: the same with the previous order, 10 wrapping to 17.</summary>
	public void PreviousOrder() {
		if (ArmedOrder != None) {
			int previous = ArmedOrder - FirstOrder - 1;
			SelectOrder(FirstOrder + (previous < 0 ? RowCount - 1 : previous));
			Display.ScheduleUpdate(Es1HddDisplay.CommandPage);
		}
	}

	/// <summary>
	/// <c>HddCommandScreen_PickPilot</c> (<c>000403a8</c>)(screen) (docs/es1/simulation/hdd-command-screen.md#picks):
	/// <see cref="HitTestMarker"/> on the map click; an object not destroyed in a squad slot is selected with
	/// <see cref="Es1HddDisplay.SelectPilot"/>, whether or not it already is; then the click done and the map dirty.
	/// </summary>
	public void PickPilot() {
		if (HitTestMarker(MapClickPoint) is { Destroyed: false } found) {
			int slot = Cockpit.SquadIndexOf(found);
			if (slot != None) {
				Display.SelectPilot(slot);
			}
		}

		MapClickWaiting = false;
		MapDirty = true;
	}

	/// <summary>
	/// <c>HddCommandScreen_PickTarget</c> (<c>00040254</c>)(screen) (docs/es1/simulation/hdd-command-screen.md#picks):
	/// <see cref="HitTestMarker"/> on the map click, then by the armed order. For 11 and 12, a found object inside the MFD
	/// scanner's range (<see cref="Es1MfdDisplay.InScannerRange"/>) whose group's side is 1 for 11 or 0 for 12 becomes the
	/// selected squadmate's current pick, a unit, with ready to send, the pick's sound and <see cref="ClearMessageRow"/>;
	/// then for 11, or once ready, it is done. For 12 not ready, 13 and 14, <see cref="MapCursor"/> becomes the current
	/// pick, a point, the same way. Then, for any order, the click done and the map dirty. An object with no group, through
	/// which retail reads, is not taken, an engine guard.
	/// </summary>
	public void PickTarget() {
		var found = HitTestMarker(MapClickPoint);
		var pick = SelectedPick;
		switch (ArmedOrder) {
			case AttackOrder or DefendOrder:
				if (found is { Group: { } group } && Cockpit.Mfd.InScannerRange(found)) {
					var side = (sbyte)group.Side;
					if (ArmedOrder == DefendOrder && side == 0 || ArmedOrder == AttackOrder && side == 1) {
						TakePick(pick, found);
					}
				}

				if (ArmedOrder == AttackOrder || ReadyToSend) {
					break;
				}

				TakePick(pick, null);
				break;
			case PatrolOrder or GotoOrder:
				TakePick(pick, null);
				break;
		}

		MapClickWaiting = false;
		MapDirty = true;
	}

	/// <summary>
	/// <c>HddCommandScreen_CycleUnit</c> (<c>000419f4</c>)(screen, <paramref name="side"/>), through
	/// <c>HddCommandScreen_CycleHostileUnit</c> (<c>000419cc</c>) for 1 and <c>HddCommandScreen_CycleFriendlyUnit</c>
	/// (<c>000419e0</c>) for 0 (docs/es1/simulation/hdd-command-screen.md#tabs-step): from after the chosen unit in the
	/// live object list when it is set and on that side, else from the list's head, which is tried first, wrapping, the
	/// first object <see cref="IsEligibleUnit"/> passes, or none after a full lap. A chosen unit no longer in the list,
	/// whose search never ends in retail, starts from the head, an engine guard.
	/// </summary>
	public IEs1SimObject? CycleUnit(int side) {
		var list = Cockpit.Globals.LiveObjects;
		int index;
		IEs1SimObject? start;
		int chosenAt = ChosenUnit is { Group: { } group } && (sbyte)group.Side == side ? IndexOf(list, ChosenUnit) : -1;
		if (chosenAt < 0) {
			start = list.Count > 0 ? list[0] : null;
			index = 1;
			if (IsEligibleUnit(side, start)) {
				return start;
			}
		} else {
			start = ChosenUnit;
			index = chosenAt + 1;
		}

		while (true) {
			var next = index < list.Count ? list[index] : null;
			index++;
			if (next == null) {
				index = 1;
				next = list.Count > 0 ? list[0] : null;
				if (next == null) {
					return null;
				}
			}

			if (IsEligibleUnit(side, next)) {
				return next;
			}

			if (ReferenceEquals(next, start)) {
				return null;
			}
		}
	}

	/// <summary>
	/// <c>HddCommandScreen_IsEligibleUnit</c> (<c>00041b1c</c>)(screen, <paramref name="side"/>, <paramref name="simObject"/>)
	/// (docs/es1/simulation/sim-object-base.md#the-live-object-list): its group's side is the side asked for, it is not
	/// destroyed, <see cref="IsOnMap"/> passes its position, its <c>+0x92</c> is set, and on a side other than 0 the
	/// cockpit's machine knows it (<see cref="Es1SensorModel.KnowsObject"/>). Every HERC, flyer and structure constructor
	/// sets <c>+0x92</c>, and no clear of it is known, so it holds for every object here. No object, no group or no
	/// machine, through which retail reads, answers false, engine guards.
	/// </summary>
	public bool IsEligibleUnit(int side, IEs1SimObject? simObject) =>
		simObject is { Group: { } group } && (sbyte)group.Side == side && !simObject.Destroyed && IsOnMap(simObject.Position)
		&& (side == 0 || Cockpit.Machine is { } machine && Es1SensorModel.KnowsObject(machine, simObject));

	// ---- Internals.

	/// <summary>The selected squadmate's pick record, or none.</summary>
	private Es1HddPickRecord? SelectedPick => SelectedSlot is >= 0 and < 3 ? _picks[SelectedSlot] : null;

	/// <summary>The marker at <paramref name="index"/>, unless retail has freed it: past the count once the markers are built.</summary>
	private Es1HddMarker? LiveMarker(int index) =>
		index >= 0 && index < MarkerCount && (!MarkersBuilt || index < MarkerTotal) ? _markers[index] : null;

	/// <summary>The show's and the hide's states and cursor, <see cref="Show"/> and <see cref="Hide"/>.</summary>
	private void SetShown(short state, int cursor) {
		OrderRegion.State = state;
		Display.Buttons[Es1HddDisplay.XmitButton].State = state;
		Display.Buttons[Es1HddDisplay.CancelButton].State = state;
		if (PickCursorUp) {
			Cockpit.Cursors.SetOffForwardCursor(cursor);
		}
	}

	/// <summary>A magnifier's step: the scale moved, <see cref="RecomputePanStep"/>, the grid dirty and the map dirty.</summary>
	private void Zoom(int step) {
		Scale += step;
		RecomputePanStep();
		GridDirty = true;
		MapDirty = true;
	}

	/// <summary>The eight availability bytes and the list dirty.</summary>
	private void SetAvailable(bool available) {
		Array.Fill(_available, available);
		OrdersDirty = true;
	}

	/// <summary>The bar moved to <paramref name="row"/>'s <i>y0</i> + 3 to its <i>y1</i>.</summary>
	private void MoveBar(Es1Label row) => Bar = Bar with { Y0 = row.Rect.Y0 + 3, Y1 = row.Rect.Y1 };

	/// <summary>The update's row fonts: wholly <c>ColorSchemePanels</c>[0] while the order is unavailable, else <paramref name="font"/> with the hotkey in <c>cpred</c>.</summary>
	private void SetRowFonts(Es1Label row, int order, int font) {
		if (!_available[order - FirstOrder]) {
			row.Font = Display.Font(Es1CockpitViews.CpBlue);
			row.HotkeyFont = row.Font;
		} else {
			row.Font = Display.Font(font);
			row.HotkeyFont = Display.Font(Es1CockpitViews.CpRed);
		}
	}

	/// <summary>
	/// <c>Label_SetTextWithHotkey</c>(the row, the order's <c>SquadOrderAttributes</c> byte, its <c>SquadOrderStrings</c>
	/// text) on <paramref name="canvas"/>. An order with no attribute byte, where retail reads one through its pointer,
	/// draws no hotkey here, an engine guard.
	/// </summary>
	private void WriteOrder(Es1PanelCanvas canvas, Es1Label row, int order) {
		var orders = Display.Strings?.Group(OrderGroup) ?? [];
		if (order >= orders.Count) {
			return;
		}

		var entry = orders[order];
		row.SetTextWithHotkey(canvas, entry.Attributes.Length > 0 ? entry.Attributes[0] : int.MaxValue, entry.Text);
	}

	/// <summary>
	/// The key slot's <c>Enter</c>: with a pick wanted, by the armed order through the table <c>0009491c</c>: for 11 and
	/// 12 with a chosen unit, <see cref="SynthesizeListClick"/>(1, the map); for 11 without, nothing; for 12 without, 13
	/// and 14, <see cref="SynthesizeListClick"/>(0, the map).
	/// </summary>
	private void PickByEnter() {
		if (!WantsPick) {
			return;
		}

		switch (ArmedOrder) {
			case AttackOrder or DefendOrder when ChosenUnit != null:
				SynthesizeListClick(1, MapRegion);
				break;
			case DefendOrder or PatrolOrder or GotoOrder:
				SynthesizeListClick(0, MapRegion);
				break;
		}
	}

	/// <summary>
	/// The key slot's <c>Tab</c>: with a pick wanted, the chosen unit <see cref="CycleUnit"/>(1) for 11 or (0) for 12,
	/// any other order keeping it; with one, it becomes the selected squadmate's current pick, a unit, with ready to send,
	/// <see cref="ClearMessageRow"/> and <c>Sound_Play</c>(<c>0x1a</c>); with none, <c>Sound_Play</c>(<c>0x1b</c>).
	/// With no squadmate selected, where retail writes before the first record, no record is written, an engine guard.
	/// </summary>
	private void CycleChosenUnit() {
		if (!WantsPick) {
			return;
		}

		if (ArmedOrder == AttackOrder) {
			ChosenUnit = CycleUnit(1);
		} else if (ArmedOrder == DefendOrder) {
			ChosenUnit = CycleUnit(0);
		}

		if (ChosenUnit == null) {
			// PLACEHOLDER: Sound_Play(0x1b); no plan step ports ES1's sound yet.
			return;
		}

		if (SelectedPick is { } pick) {
			pick.CurrentUnit = ChosenUnit;
			pick.CurrentKind = Es1HddPickRecord.UnitKind;
			pick.CurrentHeld = true;
		}

		ReadyToSend = true;
		ClearMessageRow();
		// PLACEHOLDER: Sound_Play(0x1a); no plan step ports ES1's sound yet.
	}

	/// <summary>
	/// A map pick taken into the selected squadmate's current pick: <paramref name="unit"/>, or for none
	/// <see cref="MapCursor"/> as a point; held, ready to send, the pick's sound and <see cref="ClearMessageRow"/>.
	/// </summary>
	private void TakePick(Es1HddPickRecord? pick, IEs1SimObject? unit) {
		if (pick != null) {
			if (unit != null) {
				pick.CurrentUnit = unit;
				pick.CurrentKind = Es1HddPickRecord.UnitKind;
			} else {
				pick.CurrentPoint = MapCursor;
				pick.CurrentKind = Es1HddPickRecord.PointKind;
			}

			pick.CurrentHeld = true;
		}

		ReadyToSend = true;
		// PLACEHOLDER: Sound_Play(0x14); no plan step ports ES1's sound yet.
		ClearMessageRow();
	}

	/// <summary>
	/// The grid's lines (<see cref="DrawMap"/>): the vertical lines from <see cref="GridOrigin"/>'s <i>x</i>, first
	/// rightward from <i>i</i> = 0 while left of the right edge, then leftward from the origin's <i>x</i> less span
	/// &gt;&gt; 4, the next at (<i>i</i> × span &gt;&gt; 4) for <i>i</i> = −2, −3, …, while right of the left edge; then
	/// the horizontal lines the same way from its <i>y</i> with the same span.
	/// </summary>
	private void DrawGrid(Es1PanelCanvas map) {
		if (GridSpan <= 0) {
			return;
		}

		int left = MapOrigin.X, right = -MapOrigin.X, top = MapOrigin.Y, bottom = -MapOrigin.Y;
		Lines(GridOrigin.X, left, right, x => map.DrawLine(x, top, x, bottom, GridColor));
		Lines(GridOrigin.Y, top, bottom, y => map.DrawLine(left, y, right, y, GridColor));

		void Lines(int origin, int low, int high, Action<int> draw) {
			int at = origin;
			for (int i = 1; at < high; i++) {
				draw(at);
				at = unchecked(i * GridSpan >> 4) + origin;
			}

			at = origin - (GridSpan >> 4);
			for (int i = -2; low < at; i--) {
				draw(at);
				at = unchecked(i * GridSpan >> 4) + origin;
			}
		}
	}

	/// <summary>
	/// One marker of <see cref="DrawMap"/>'s walk, <paramref name="marker"/> and <i>o</i> its object: a route marker at
	/// its point; an object on a side other than 0 that is immobilized or destroyed skipped; any other at its position
	/// while radar-visible, or on side 0 with its group holding no action; and otherwise at its last-known point on a
	/// paint with bit <c>0x20</c> of the clock clear and its <c>+0xa1</c> clear, else skipped. PLACEHOLDER: the port holds
	/// neither <c>+0xa1</c> nor the point <c>+0x164</c>; every constructor sets <c>+0xa1</c> and no ported code clears
	/// it, so the last case skips the object, as the scanner's does
	/// (<see cref="Es1MfdScannerScreen"/>, docs/es1/simulation/mfd-scanner.md#open). The point, projected, less the
	/// half-size and through <see cref="ToViewport"/>, becomes the rect's top-left (<see cref="Es1HddMarker.MoveTo"/>),
	/// the old one kept. For the selected squadmate's machine, the link line from the marker's center: to the current
	/// pick in <c>HudColorTable_Get</c>(slot) while it is held, else to the saved pick in that less 1 while it is held,
	/// each the pick's point or its unit's position, projected at its own height, drawn when the color is not 0; then,
	/// with a squadmate selected and the blink phase set, the marker is not painted. Otherwise, a ranged marker's
	/// <see cref="Es1HddMarker.Distance"/> the <c>Math_FastMagnitude3D</c> of its point from the camera, then its paint.
	/// Answers whether <i>o</i> is the chosen unit. An object with no group, and a pick whose kind is neither or whose
	/// unit is none, where retail reads through a null pointer or draws to a stale point, are skipped, engine guards.
	/// </summary>
	private bool PlaceMarker(Es1PanelCanvas map, Es1HddMarker marker) {
		Vec3i point;
		bool chosen = false;
		var simObject = marker.Object;
		if (simObject == null) {
			point = marker.RoutePoint;
		} else {
			if (simObject.Group is not { } group) {
				return false;
			}

			bool sideZero = group.Side == 0;
			if ((simObject.Immobilized || simObject.Destroyed) && !sideZero) {
				return false;
			}

			if (!simObject.RadarVisible && !(sideZero && group.Action == null)) {
				return false;
			}

			point = simObject.Position;
			chosen = ReferenceEquals(simObject, ChosenUnit);
		}

		var (x, y) = Project(point);
		var at = ToViewport((x - marker.HalfSize.X, y - marker.HalfSize.Y));
		marker.LastTopLeft = (marker.Rect.X0, marker.Rect.Y0);
		marker.MoveTo(at);
		if (simObject != null && SelectedSlot != None && ReferenceEquals(simObject, Cockpit.SquadmateMachines[SelectedSlot])) {
			DrawLinkLine(map, marker);
			if (PilotSelected && BlinkPhase) {
				return chosen;
			}
		}

		if (marker.Ranged) {
			marker.Distance = SimMath.FastMagnitude3D(point.X - CenterX, point.Y - CenterY, point.Z - Scale);
		}

		marker.Paint();
		return chosen;
	}

	/// <summary>The link line of <see cref="PlaceMarker"/>, from <paramref name="marker"/>'s center to the selected squadmate's held pick.</summary>
	private void DrawLinkLine(Es1PanelCanvas map, Es1HddMarker marker) {
		var pick = _picks[SelectedSlot];
		int color;
		Vec3i? end;
		if (pick.CurrentHeld) {
			color = Es1Cockpit.SquadSlotColor(SelectedSlot);
			end = pick.CurrentKind == Es1HddPickRecord.PointKind ? pick.CurrentPoint
				: pick.CurrentKind == Es1HddPickRecord.UnitKind ? pick.CurrentUnit?.Position : null;
		} else if (pick.SavedHeld) {
			color = Es1Cockpit.SquadSlotColor(SelectedSlot) - 1;
			end = pick.SavedKind == Es1HddPickRecord.PointKind ? pick.SavedPoint
				: pick.SavedKind == Es1HddPickRecord.UnitKind ? pick.SavedUnit?.Position : null;
		} else {
			return;
		}

		if (color == 0 || end is not { } target) {
			return;
		}

		var (x, y) = FromViewport((marker.Rect.X0, marker.Rect.Y0));
		var (ex, ey) = Project(target);
		map.DrawLine(x + marker.HalfSize.X, y + marker.HalfSize.Y, ex, ey, (byte)color);
	}

	/// <summary>
	/// What <see cref="BuildMapMarkers"/> gives <paramref name="simObject"/>: its frame, size, whether it is ranged and the
	/// ranged pixel's color, or none.
	/// </summary>
	private (short Frame, int Size, bool Ranged, short PixelColor)? MarkerFor(IEs1SimObject simObject, Es1Mech? machine) {
		switch (simObject.TargetClass) {
			case Es1MfdStatusScreen.HercClass:
				if (ReferenceEquals(simObject, machine)) {
					return (0x12, 7, false, 0);
				}

				if (simObject.Group is not { } group) {
					return null;
				}

				if (group.Side != 0) {
					return (9, 7, false, 0);
				}

				int slot = Cockpit.SquadIndexOf(simObject);
				return ((short)(slot == None ? 0x36 : 0x1b + 9 * slot), 7, false, 0);
			case Es1MfdStatusScreen.FlyerClass:
				return (0x3f, 5, false, 0);
			case Es1MfdStatusScreen.StructureClass:
				if (simObject is not Es1Base { Group: { } baseGroup } structure) {
					return null;
				}

				bool friendly = baseGroup.Side == 0;
				short color = friendly ? (short)0x32 : (short)0x35;
				return structure.Type.Index is Es1Base.FirstRadarType or Es1Base.LastRadarType or Es1Base.ArmedType
					? (friendly ? (short)0x4b : (short)0x49, 7, true, color)
					: (friendly ? (short)0x4a : (short)0x48, 13, true, color);
			default:
				return null;
		}
	}

	/// <summary>
	/// The camera's position (its <c>+0</c>–<c>+8</c>) the center and the scale, <c>Raster_InstallViewProjection</c> of it
	/// and <c>Raster_SetModelTransform</c>(0): the camera's frame from <see cref="Pitch"/> at that position, and its
	/// inverse, the world-to-view transform.
	/// </summary>
	private void InstallCamera() {
		_cameraFrame = Transform3.FromEuler(Pitch, 0, 0);
		_cameraFrame.X = CenterX;
		_cameraFrame.Y = CenterY;
		_cameraFrame.Z = Scale;
		_worldToView = _cameraFrame.Inverted();
	}

	/// <summary>
	/// <c>Raster_ModelToView</c> and <c>Raster_PerspectiveDivide</c> under the camera's <see cref="FocalShift"/> with
	/// <i>y</i> negated: the point in the map's context, world +<i>y</i> up. A point at depth 0, where retail divides by
	/// it, is taken at depth 1, an engine guard.
	/// </summary>
	private (int X, int Y) Project(Vec3i point) {
		var view = _worldToView.TransformPoint(point.X, point.Y, point.Z);
		int depth = view.Y == 0 ? 1 : view.Y;
		return (unchecked(view.X << FocalShift) / depth, -(unchecked(view.Z << FocalShift) / depth));
	}

	/// <summary>
	/// (<paramref name="x"/> × scale &gt;&gt; 7, scale, <paramref name="y"/> × scale &gt;&gt; 7) through
	/// <c>Transform_ApplyToPoint</c> with <c>g_ViewToModel</c>, the camera's frame: a point of the map's context on the
	/// ground.
	/// </summary>
	private Vec3i ViewToWorld(int x, int y) =>
		_cameraFrame.TransformPoint(unchecked(x * Scale) >> FocalShift, Scale, unchecked(y * Scale) >> FocalShift);

	private static int IndexOf(List<IEs1SimObject> list, IEs1SimObject? simObject) =>
		simObject == null ? -1 : list.FindIndex(item => ReferenceEquals(item, simObject));
}
