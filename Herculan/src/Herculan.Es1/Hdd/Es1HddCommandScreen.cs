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
/// <see cref="Show"/> and <see cref="KeyDispatch"/>. The map, its camera, markers and paint, is <see cref="Es1HddMap"/>;
/// the markers are <see cref="Es1HddMarker"/> and the two click regions <see cref="Es1HddListGadget"/>.
/// </summary>
public sealed class Es1HddCommandScreen : Es1HddPage {
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

	/// <summary>The clock counts between two blink ticks: half a second.</summary>
	public const uint BlinkCounts = 0x1e;

	/// <summary>The blink ticks a message row set with flag 1 stays up: three seconds.</summary>
	public const short MessageTicks = 6;

	/// <summary>The palette indices the screen draws in.</summary>
	public const byte ScreenFill = 0x3f, BarColor = 0x3b, MessageFill = 0x3a;

	/// <summary>The keys <see cref="KeyDispatch"/> takes, <c>HddCommandScreen_KeyCodes</c> (<c>000948a4</c>).</summary>
	public static readonly ushort[] KeyCodes = [0x20, 0x1e, 0x21, 0x14, 0x22, 0x18, 0x2e, 0x12, 0x2d, 0x0e, 0x34, 0x33, 0x1c, 0x0f, 0x4c];

	/// <summary><c>Label_SetRect</c>'s flags 1, which start the text at the anchor, and the order rows' margin.</summary>
	private const int LeftAligned = 1, RowMargin = 5;

	private readonly Es1HddPickRecord[] _picks = [new(), new(), new()];
	private readonly Es1Label[] _rows = new Es1Label[RowCount];
	private readonly bool[] _available = new bool[RowCount];
	private readonly Es1Label _message;

	/// <summary>
	/// <c>HddCommandScreen_Ctor</c> (<c>ES1DBSIM 0003ee58</c>)(0, <paramref name="display"/>, <paramref name="orderColumn"/>,
	/// <paramref name="screen"/>) (docs/es1/simulation/hdd-command-screen.md#building-it--hddcommandscreen_ctor-0003ee58):
	/// the page base; the three pick records; the blink deadline the clock + 30; the map (<see cref="Es1HddMap"/>) on the
	/// map viewport, the screen rect with its <i>x1</i> the order column's <i>x0</i> − 2; the two click regions over the
	/// order column and the map viewport, registered in that order on the cockpit's root; the message row, a
	/// <c>cpred</c> label centered over the column's width one <c>cpgreen</c> cell tall, on <see cref="MessageFill"/>; the
	/// eight order rows, <c>cpgreen</c> labels whose hotkey font is <c>cpred</c>, left-aligned with the margin (5, 0), row
	/// <i>i</i> from <i>y0</i> + (<i>i</i> + 1) <i>r</i>, <i>r</i> the column's height / 9; <see cref="DisableOrders"/>;
	/// no order armed; the bar's <i>x</i> from the column's <i>x0</i> + 1 to <i>x0</i> + 3; the map clean and the blink
	/// phase set; and no squadmate selected.
	/// </summary>
	internal Es1HddCommandScreen(Es1HddDisplay display, Es1Rect orderColumn, Es1Rect screen)
		: base(display, orderColumn, screen) {
		var cockpit = display.Cockpit;
		BlinkDeadline = cockpit.Globals.Clock.Clock + BlinkCounts;
		Map = new Es1HddMap(this, screen with { X1 = orderColumn.X0 - 2 });

		OrderRegion = new Es1HddListGadget(this, orderColumn);
		cockpit.Root.RegisterClickable(OrderRegion);
		MapRegion = new Es1HddListGadget(this, Map.MapViewport);
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
		BlinkPhase = true;
		SelectedSlot = None;
	}

	/// <summary>The map: its camera, view, markers and paint, kept in this screen's own object in retail.</summary>
	public Es1HddMap Map { get; }

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

	/// <summary><c>+0x85</c>, the three pick records, one per squadmate (docs/retail/simulation/heads-down-display.md#the-link-line).</summary>
	public IReadOnlyList<Es1HddPickRecord> Picks => _picks;

	/// <summary><c>+0x103</c>, set while the armed order wants a pick: a comm box's press then puts up the map-pick cursor.</summary>
	public bool WantsPick { get; private set; }

	/// <summary><c>+0x104</c>, the order is ready to send, which <c>XMIT</c>'s press and paint test.</summary>
	public bool ReadyToSend { get; private set; }

	/// <summary><c>+0x105</c>, the selected squadmate's slot, or <see cref="None"/>.</summary>
	public int SelectedSlot { get; private set; }

	/// <summary><c>+0x11c</c>, a map click is waiting, and <c>+0x11d</c>, its point.</summary>
	public bool MapClickWaiting { get; private set; }

	/// <inheritdoc cref="MapClickWaiting"/>
	public (int X, int Y) MapClickPoint { get; private set; }

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
	public bool MapDirty { get; internal set; }

	private Es1Cockpit Cockpit => Display.Cockpit;

	// ---- The slots.

	/// <summary>
	/// Slot <c>+4</c>, <c>HddCommandScreen_Repaint</c> (<c>0003f490</c>) (docs/es1/simulation/hdd-command-screen.md#drawing-it):
	/// the screen rect filled <see cref="ScreenFill"/>; <see cref="RefreshOrders"/>; with an order armed, the bar from
	/// its row's <i>y0</i> + 3 to its <i>y1</i> filled <see cref="BarColor"/>; the paints of <c>XMIT</c> and
	/// <c>CANCEL</c>; <see cref="Es1HddMap.BuildMapMarkers"/> while the markers are not built; <see cref="Es1HddMap.DrawMap"/>; and the
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
		if (!Map.MarkersBuilt) {
			Map.BuildMapMarkers();
		}

		Map.DrawMap();
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
	/// <see cref="Es1HddMap.DrawMap"/>, the display's source and draw pages both made its <c>+0x8c</c> page, the pilot channel's
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
			if (SelectedSlot != None && Map.LiveMarker(SelectedSlot) is { } marker) {
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
			var viewport = Map.MapViewport;
			canvas.FillRect(viewport.X0, viewport.Y0, viewport.X1, viewport.Y1, ScreenFill);
			Map.DrawMap();
			var video = Cockpit.Display;
			video.SourcePage = video.PairDrawn;
			video.DrawPage = video.PairDrawn;
			var channel = Cockpit.PilotChannel;
			channel.Paint();
			Cockpit.DrawToCockpit(viewport);
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
				Map.PanX = 0;
				Map.PanY = 0;
				break;
		}

		return 1;
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
							((Map.MapViewport.X1 - Map.MapViewport.X0) >> 1) + Map.MapViewport.X0 + Cockpit.CanvasRect.X0 - Cockpit.CanvasOrigin.X,
							((Map.MapViewport.Y1 - Map.MapViewport.Y0) >> 1) + Map.MapViewport.Y0 + Cockpit.CanvasRect.Y0 - Cockpit.CanvasOrigin.Y);
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
	/// camera and <see cref="Es1HddMap.ToViewport"/>; each to <see cref="QueueListClick"/>. Pick 1 with no chosen unit, where retail
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
				Map.InstallCamera();
				QueueListClick(Map.ToViewport(Map.Project(unit.Position)), region);
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
		if (Map.LiveMarker(slot) is { } marker) {
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
	/// <see cref="Es1HddMap.HitTestMarker"/> on the map click; an object not destroyed in a squad slot is selected with
	/// <see cref="Es1HddDisplay.SelectPilot"/>, whether or not it already is; then the click done and the map dirty.
	/// </summary>
	public void PickPilot() {
		if (Map.HitTestMarker(MapClickPoint) is { Destroyed: false } found) {
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
	/// <see cref="Es1HddMap.HitTestMarker"/> on the map click, then by the armed order. For 11 and 12, a found object inside the MFD
	/// scanner's range (<see cref="Es1MfdDisplay.InScannerRange"/>) whose group's side is 1 for 11 or 0 for 12 becomes the
	/// selected squadmate's current pick, a unit, with ready to send, the pick's sound and <see cref="ClearMessageRow"/>;
	/// then for 11, or once ready, it is done. For 12 not ready, 13 and 14, <see cref="Es1HddMap.MapCursor"/> becomes the current
	/// pick, a point, the same way. Then, for any order, the click done and the map dirty. An object with no group, through
	/// which retail reads, is not taken, an engine guard.
	/// </summary>
	public void PickTarget() {
		var found = Map.HitTestMarker(MapClickPoint);
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
	/// destroyed, <see cref="Es1HddMap.IsOnMap"/> passes its position, its <c>+0x92</c> is set, and on a side other than 0 the
	/// cockpit's machine knows it (<see cref="Es1SensorModel.KnowsObject"/>). Every HERC, flyer and structure constructor
	/// sets <c>+0x92</c>, and no clear of it is known, so it holds for every object here. No object, no group or no
	/// machine, through which retail reads, answers false, engine guards.
	/// </summary>
	public bool IsEligibleUnit(int side, IEs1SimObject? simObject) =>
		simObject is { Group: { } group } && (sbyte)group.Side == side && !simObject.Destroyed && Map.IsOnMap(simObject.Position)
		&& (side == 0 || Cockpit.Machine is { } machine && Es1SensorModel.KnowsObject(machine, simObject));

	// ---- Internals.

	/// <summary>The selected squadmate's pick record, or none.</summary>
	private Es1HddPickRecord? SelectedPick => SelectedSlot is >= 0 and < 3 ? _picks[SelectedSlot] : null;

	/// <summary>The show's and the hide's states and cursor, <see cref="Show"/> and <see cref="Hide"/>.</summary>
	private void SetShown(short state, int cursor) {
		OrderRegion.State = state;
		Display.Buttons[Es1HddDisplay.XmitButton].State = state;
		Display.Buttons[Es1HddDisplay.CancelButton].State = state;
		if (PickCursorUp) {
			Cockpit.Cursors.SetOffForwardCursor(cursor);
		}
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
	/// <see cref="Es1HddMap.MapCursor"/> as a point; held, ready to send, the pick's sound and <see cref="ClearMessageRow"/>.
	/// </summary>
	private void TakePick(Es1HddPickRecord? pick, IEs1SimObject? unit) {
		if (pick != null) {
			if (unit != null) {
				pick.CurrentUnit = unit;
				pick.CurrentKind = Es1HddPickRecord.UnitKind;
			} else {
				pick.CurrentPoint = Map.MapCursor;
				pick.CurrentKind = Es1HddPickRecord.PointKind;
			}

			pick.CurrentHeld = true;
		}

		ReadyToSend = true;
		// PLACEHOLDER: Sound_Play(0x14); no plan step ports ES1's sound yet.
		ClearMessageRow();
	}

	private static int IndexOf(List<IEs1SimObject> list, IEs1SimObject? simObject) =>
		simObject == null ? -1 : list.FindIndex(item => ReferenceEquals(item, simObject));
}
