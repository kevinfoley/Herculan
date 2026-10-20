using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// The in-mission panels' base, <c>AlertPanel_CtorBase</c> (<c>ES1DBSIM 00045988</c>) and <c>AlertVtable</c>
/// (<c>00094e26</c>) (docs/es1/simulation/alert-panels.md#the-panel-object--alertpanel_ctorbase-00045988): a rect on the
/// screen, a render context on it (<see cref="Canvas"/>), a widget root of its own, up to ten widgets, a close byte the
/// member's loop runs until, and what <see cref="Enter"/> takes from the simulator and <see cref="Leave"/> puts back.
/// The table's slots are the virtual members: the paint <c>+4</c> (<see cref="Paint"/>), the title <c>+8</c>
/// (<see cref="SetTitleText"/>), the run <c>+0xc</c> (<see cref="Run"/>), a child's click <c>+0x10</c>
/// (<see cref="OnChildClick"/>), the keys <c>+0x14</c> (<see cref="HandleEvent"/>) and a button's caption <c>+0x18</c>
/// (<see cref="GetButtonCaption"/>).
///
/// <para>A member's loop runs inside the tick, which the port suspends at each pass (<see cref="RunModal"/>, plan
/// decision 11). Retail's fields that hold a render context, the panel's <c>+0x44</c> and the one <see cref="Enter"/>
/// found at <c>+0x27d</c>, are not kept: the panel's draws take <see cref="Canvas"/>, and nothing else draws while a
/// panel is up.</para>
/// </summary>
public abstract class Es1AlertPanel {
	/// <summary>The widget list's ten pointers, <c>+0x28d</c>.</summary>
	public const int MaxWidgets = 10;

	/// <summary><c>AlertPanel_ScreenRect</c> (<c>000b1708</c>), which <c>Alert_StaticInit</c> sets: the 320 × 200 screen.</summary>
	public static readonly Es1Rect ScreenRect = new(0, 0, 319, 199);

	/// <summary>The title bar's frame colors: the top and left edges, the right edge, the bottom edge.</summary>
	private const byte TitleLightEdge = 0x10, TitleRightEdge = 0x1b, TitleBottomEdge = 0x1c;

	/// <summary>The title bar's corners' inset from its right and bottom edges.</summary>
	private const int TitleCornerInset = 4;

	/// <summary>The commands <c>AlertPanel_HandleEvent</c> takes, the five dwords at <c>00094eec</c>.</summary>
	private const ushort Esc = 0x01, Tab = 0x0f, AltTab = 0x20f, Return = 0x1c, Keypad0 = 0x52;

	private readonly List<Es1Widget> _widgets = new(MaxWidgets);

	/// <summary>
	/// <c>AlertPanel_CtorBase</c>(panel, <paramref name="rect"/>, <paramref name="keepsScreen"/>): the rect, the region's
	/// rect (0, 0, <i>x1</i> − <i>x0</i>, <i>y1</i> − <i>y0</i>), a buffer for the screen under the panel with
	/// <paramref name="keepsScreen"/>, the root the mouse works on now kept, a root of the panel's own on its rect, the
	/// display's draw page kept, no cancel widget and the cursor to be put back on leaving.
	/// </summary>
	protected Es1AlertPanel(Es1AlertGlobals globals, Es1Rect rect, bool keepsScreen) {
		Globals = globals;
		Rect = rect;
		Region = new Es1Rect(0, 0, rect.X1 - rect.X0, rect.Y1 - rect.Y0);
		Canvas = new Es1PanelCanvas(globals.Display, rect);
		KeepsScreen = keepsScreen;
		PreviousRoot = globals.Mouse.Widgets.ActiveRoot;
		Root = new Es1WidgetRoot(rect);
		DrawPageAtConstruction = globals.Display.DrawPage;
	}

	public Es1AlertGlobals Globals { get; }

	/// <summary><c>+0</c>–<c>+0xc</c>: the rect on the screen.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x10</c>, the rect region, type 0, its rect panel-local.</summary>
	public Es1Rect Region { get; }

	/// <summary><c>+0x44</c>, the render context on <see cref="Rect"/>.</summary>
	public Es1PanelCanvas Canvas { get; }

	/// <summary>Whether <c>+0x28</c> holds a buffer, which <see cref="Enter"/> saves the screen under the panel into.</summary>
	public bool KeepsScreen { get; }

	/// <summary><c>+0x28</c>: the screen under the panel as <see cref="Enter"/> saved it; none until then, or without <see cref="KeepsScreen"/>.</summary>
	public ushort[]? ScreenUnder { get; private set; }

	/// <summary><c>+0x2c</c>: the plate, frame 0 of a bitmap array the member loads, which its paint blits; none from the base.</summary>
	public DynamixBitmap? Plate { get; protected set; }

	/// <summary><c>+0x30</c>: the panel's string table, which the member loads.</summary>
	public StringFile? Strings { get; protected set; }

	/// <summary><c>+0x40</c>: the title label.</summary>
	public Es1Label? TitleLabel { get; protected set; }

	/// <summary><c>+0x281</c>: the panel's root widget, its rect the panel's.</summary>
	public Es1WidgetRoot Root { get; }

	/// <summary><c>+0x285</c>: the root the mouse worked on when the panel was built.</summary>
	public Es1WidgetRoot? PreviousRoot { get; }

	/// <summary><c>+0x289</c>: the display's draw page when the panel was built, which <see cref="Leave"/> puts back.</summary>
	public int DrawPageAtConstruction { get; }

	/// <summary><c>+0x28d</c> and its count <c>+0x2b5</c>: the widgets, at most <see cref="MaxWidgets"/>.</summary>
	public IReadOnlyList<Es1Widget> Widgets => _widgets;

	/// <summary><c>+0x2b9</c>, the close byte, which a member's loop runs until it is set; its bit 0 is the loop's answer.</summary>
	public byte Close { get; protected set; }

	/// <summary><c>+0x2ba</c>–<c>+0x2c6</c>: the title bar's rect, panel-local, which <see cref="SetTitleText"/> frames.</summary>
	public Es1Rect TitleBar { get; protected set; }

	/// <summary><c>+0x2ce</c> and <c>+0x2d2</c>: the cursor that was up, its image and hotspot, as <see cref="LoadCursor"/> found it.</summary>
	public Es1CursorImage? SavedCursor { get; private set; }

	/// <summary><c>+0x2da</c>: the panel's cursor.</summary>
	public Es1CursorImage? PanelCursor { get; private set; }

	/// <summary><c>+0x2de</c>: <c>Cursor_FollowsMouse</c> as it was.</summary>
	public bool SavedFollowsMouse { get; private set; }

	/// <summary><c>+0x2df</c>: the focused widget, −1 for none.</summary>
	public int Focus { get; private set; }

	/// <summary><c>+0x2e3</c>: the cancel widget, which [Esc] presses; −1 from the base.</summary>
	public int Cancel { get; protected set; } = -1;

	/// <summary>
	/// <c>+0x2e8</c>–<c>+0x312</c>: <c>PlayerInputBlock</c> as it was, which <see cref="Leave"/> puts back with its
	/// pointer. Each input build fills a new block (<see cref="Es1InputBuild.PlayerInputBlock"/>), so the block itself is
	/// kept rather than a copy of its bytes.
	/// </summary>
	public Es1PlayerInputBlock? SavedInput { get; private set; }

	/// <summary><c>+0x313</c>: 1 from the base, to put the cursor back on leaving.</summary>
	public bool RestoreCursorOnLeave { get; protected set; } = true;

	/// <summary>
	/// <c>AlertPanel_SetRect</c> (<c>0004675c</c>)(<paramref name="height"/>, <paramref name="width"/>,
	/// <paramref name="y"/>, <paramref name="x"/>): the rect (<i>x</i>, <i>y</i>, <i>x</i> + <i>width</i>, <i>y</i> +
	/// <i>height</i>), which retail returns in <c>AlertPanel_RectScratch</c> (<c>000b1724</c>).
	/// </summary>
	public static Es1Rect SetRect(int height, int width, int y, int x) => new(x, y, x + width, y + height);

	/// <summary>
	/// <c>AlertPanel_CenterRect</c> (<c>0004679c</c>)(<paramref name="height"/>, <paramref name="width"/>):
	/// <see cref="SetRect"/> at ((319 − width) &gt;&gt; 1, (199 − height) &gt;&gt; 1), the corner of
	/// <see cref="ScreenRect"/>'s <i>x1</i> and <i>y1</i> less the size, halved.
	/// </summary>
	public static Es1Rect CenterRect(int height, int width) =>
		SetRect(height, width, (ScreenRect.Y1 - height) >> 1, (ScreenRect.X1 - width) >> 1);

	/// <summary>Slot <c>+4</c>, the paint, which <see cref="Enter"/> calls; the base's is 0.</summary>
	protected abstract void Paint();

	/// <summary>Slot <c>+0xc</c>, the run: the member's loop, its answer in <paramref name="answer"/>; the base's is 0.</summary>
	public abstract IEnumerable<Es1Wait> Run(StrongBox<int> answer);

	/// <summary>Slot <c>+0x18</c>, the caption a button's paint asks its owner for; the base's is 0.</summary>
	public abstract string? GetButtonCaption(int index);

	/// <summary>Slot <c>+0x10</c>, a child's click: <c>AlertPanel_OnChildClickNoOp</c> (<c>00045d06</c>) does nothing.</summary>
	public virtual void OnChildClick(short flags, Es1Widget child) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>AlertPanel_SetTitleText</c> (<c>000461ec</c>): <see cref="TitleBar"/> framed, its top and left
	/// edges in <c>0x10</c>, its right edge in <c>0x1b</c> and its bottom edge in <c>0x1c</c>; corner frames 0, 1, 2 and
	/// 3 of <c>WidgetCorners</c> blitted with flags 0 at (<i>x0</i>, <i>y0</i>), (<i>x1</i> − 4, <i>y0</i>), (<i>x0</i>,
	/// <i>y1</i> − 4) and (<i>x1</i> − 4, <i>y1</i> − 4); then <paramref name="text"/> on the title label.
	/// </summary>
	public virtual void SetTitleText(string? text) {
		var bar = TitleBar;
		Canvas.DrawLine(bar.X0, bar.Y0, bar.X1, bar.Y0, TitleLightEdge);
		Canvas.DrawLine(bar.X0, bar.Y0, bar.X0, bar.Y1, TitleLightEdge);
		Canvas.DrawLine(bar.X1, bar.Y1, bar.X1, bar.Y0, TitleRightEdge);
		Canvas.DrawLine(bar.X1, bar.Y1, bar.X0, bar.Y1, TitleBottomEdge);
		var corners = Globals.Widgets.Corners;
		Canvas.Blit(Es1CornerFrame.Frame(corners, 0), bar.X0, bar.Y0);
		Canvas.Blit(Es1CornerFrame.Frame(corners, 1), bar.X1 - TitleCornerInset, bar.Y0);
		Canvas.Blit(Es1CornerFrame.Frame(corners, 2), bar.X0, bar.Y1 - TitleCornerInset);
		Canvas.Blit(Es1CornerFrame.Frame(corners, 3), bar.X1 - TitleCornerInset, bar.Y1 - TitleCornerInset);
		TitleLabel?.SetText(Canvas, text);
	}

	/// <summary>
	/// Slot <c>+0x14</c>, <c>AlertPanel_HandleEvent</c> (<c>00046670</c>)(panel, <paramref name="block"/>)
	/// (docs/es1/simulation/alert-panels.md#keys--alertpanel_handleevent-00046670): button 0 presses the focused widget,
	/// or focuses widget 0 with none, and latches the button; button 1 focuses the next and latches it; then the command
	/// word: [Esc] presses the cancel widget when there is one, [Tab] focuses the next, [Alt+Tab] the previous, [Return]
	/// acts as button 0 without the latch, and keypad 0 focuses the next with preferences option 10 on. A press is made
	/// with the flags 1. Returns 1 for each of these, whether or not it acted, and 0 for any other code.
	/// </summary>
	public virtual int HandleEvent(Es1PlayerInputBlock block) {
		var latches = Globals.Input.Controls.Latches;
		if (block.Buttons[0] != 0) {
			PressFocused();
			latches.Latch(1, 1);
			return 1;
		}

		if (block.Buttons[1] != 0) {
			FocusNext();
			latches.Latch(1, 2);
			return 1;
		}

		switch (block.CommandWord) {
			case Esc:
				if (Cancel != -1) {
					PressWidget(1, Cancel);
				}

				return 1;
			case Tab:
				FocusNext();
				return 1;
			case AltTab:
				FocusPrevious();
				return 1;
			case Return:
				PressFocused();
				return 1;
			case Keypad0:
				if (Globals.Input.Preferences.ThrottleKeys) {
					FocusNext();
				}

				return 1;
			default:
				return 0;
		}
	}

	/// <summary>The index of <paramref name="widget"/> in the list, as the panels' walks over it find it; −1 for one not in it.</summary>
	public int IndexOf(Es1Widget widget) => _widgets.IndexOf(widget);

	/// <summary>Adds <paramref name="widget"/> to the list, as a member's constructor does after registering it with <see cref="Root"/>.</summary>
	protected void AddWidget(Es1Widget widget) {
		if (_widgets.Count >= MaxWidgets) {
			throw new InvalidOperationException($"A panel holds {MaxWidgets} widgets.");
		}

		_widgets.Add(widget);
	}

	/// <summary>
	/// <c>AlertPanel_Enter</c> (<c>00045f00</c>), puts the panel up
	/// (docs/es1/simulation/alert-panels.md#putting-a-panel-up-and-taking-it-down): <c>PlayerInputBlock</c> kept; buttons
	/// 0–3 latched with state 1; the focus from the pointer (<see cref="SetFocus"/>(−1)); the mouse handed to the panel's
	/// root; the display's source and draw pages made the page on the screen, <c>+0x88</c>; <c>Cursor_FollowsMouse</c>
	/// kept and set; the enter hook; <c>dci\alert</c> loaded the first time; the cursor hidden; the screen under the
	/// panel saved when it keeps it; the paint; the alert cursor made the drawn one and shown; and the open count up by 1.
	/// </summary>
	public void Enter() {
		var globals = Globals;
		var display = globals.Display;
		SavedInput = globals.Input.PlayerInputBlock;
		for (int button = 1; button <= Es1ButtonLatches.Count; button++) {
			globals.Input.Controls.Latches.Latch(1, button);
		}

		SetFocus(-1);
		globals.Mouse.Widgets.ClearPressed(Root);
		display.SourcePage = display.PairVisible;
		display.DrawPage = display.PairVisible;
		SavedFollowsMouse = globals.Mouse.FollowsMouse;
		globals.Mouse.FollowsMouse = true;
		globals.EnterHook?.Invoke();
		var alert = globals.LoadCursorImage();
		display.Cursor.Hide();
		if (KeepsScreen) {
			// Region_Save moves the region's rect by the render context's translation and the driver's grab by it again,
			// so AlertPanel_Enter moves the rect back by the panel's corner for the call: the screen under the panel.
			ScreenUnder = display.ReadRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1);
		}

		Paint();
		LoadCursor(alert);
		display.Cursor.Show();
		globals.OpenCount++;
	}

	/// <summary>
	/// <c>AlertPanel_Leave</c> (<c>000460b8</c>), takes the panel down: <c>Cursor_FollowsMouse</c> cleared and the cursor
	/// hidden; the saved screen put back; the cursor that was up put back (<see cref="RestoreCursorImage"/>);
	/// <c>Cursor_FollowsMouse</c> as it was, and the cursor shown when it is set; the leave hook; the draw page of the
	/// panel's construction; the mouse handed back to the root it worked on; <c>PlayerInputBlock</c> put back, and with a
	/// mouse device the pointer and the drawn cursor moved to its pointer; and the open count down by 1.
	/// </summary>
	public void Leave() {
		var globals = Globals;
		var display = globals.Display;
		globals.Mouse.FollowsMouse = false;
		display.Cursor.Hide();
		if (ScreenUnder != null) {
			display.WriteRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, ScreenUnder);
		}

		RestoreCursorImage();
		globals.Mouse.FollowsMouse = SavedFollowsMouse;
		if (SavedFollowsMouse) {
			display.Cursor.Show();
		}

		globals.LeaveHook?.Invoke();
		display.DrawPage = DrawPageAtConstruction;
		// WidgetRoot_Delete (00044350) frees the panel's root.
		if (PreviousRoot != null) {
			globals.Mouse.Widgets.ClearPressed(PreviousRoot);
		}

		globals.Input.PlayerInputBlock = SavedInput;
		if (globals.MouseDevice is { } mouse) {
			int x = SavedInput?.PointerX ?? 0, y = SavedInput?.PointerY ?? 0;
			mouse.SetPosition(x, y);
			display.Cursor.SyncPosition(x, y);
		}

		globals.OpenCount--;
	}

	/// <summary>
	/// <c>AlertPanel_LoadCursor</c> (<c>000463c0</c>)(panel, <paramref name="cursor"/>): the cursor that is up kept, the
	/// panel's kept, and <paramref name="cursor"/> made the drawn one (<c>Cursor_SetImage</c>).
	/// </summary>
	public void LoadCursor(Es1CursorImage? cursor) {
		var drawn = Globals.Display.Cursor;
		SavedCursor = drawn.Image;
		PanelCursor = cursor;
		drawn.SetImage(cursor);
	}

	/// <summary><c>AlertPanel_RestoreCursorImage</c> (<c>00046428</c>): while <see cref="RestoreCursorOnLeave"/>, the kept cursor made the drawn one again.</summary>
	public void RestoreCursorImage() {
		if (RestoreCursorOnLeave) {
			Globals.Display.Cursor.SetImage(SavedCursor);
		}
	}

	/// <summary>
	/// <c>AlertPanel_SetFocus</c> (<c>00046470</c>)(panel, <paramref name="index"/>) (docs/es1/simulation/alert-panels.md#focus):
	/// for −1, with a mouse device, an input build, and the first widget whose rect holds the block's pointer, the
	/// pointer in the screen's pixels and the rect in the panel's, edges included; −1 when none does or there is no mouse
	/// device. For any other index the pointer goes to the widget's center plus the panel's corner, the driver's with a
	/// mouse device and the drawn cursor's either way. The index becomes the focus. An index past the list, where retail
	/// reads past it, moves nothing, an engine guard.
	/// </summary>
	public void SetFocus(int index) {
		var globals = Globals;
		var mouse = globals.MouseDevice;
		if (index == -1) {
			if (mouse != null) {
				var block = globals.Input.Build();
				for (int i = 0; i < _widgets.Count; i++) {
					if (_widgets[i].Rect.Contains(block.PointerX, block.PointerY)) {
						index = i;
						break;
					}
				}
			}
		} else if (index >= 0 && index < _widgets.Count) {
			var rect = _widgets[index].Rect;
			int x = rect.X0 + ((rect.X1 - rect.X0) >> 1) + Rect.X0;
			int y = rect.Y0 + ((rect.Y1 - rect.Y0) >> 1) + Rect.Y0;
			mouse?.SetPosition(x, y);
			globals.Display.Cursor.SyncPosition(x, y);
		}

		Focus = index;
	}

	/// <summary><c>AlertPanel_FocusChild</c> (<c>00046584</c>)(panel, <paramref name="widget"/>): the widget's index focused; nothing for one not in the list.</summary>
	public void FocusChild(Es1Widget widget) {
		int index = IndexOf(widget);
		if (index != -1) {
			SetFocus(index);
		}
	}

	/// <summary><c>AlertPanel_FocusNext</c> (<c>000465d8</c>): the focus + 1 focused, 0 when that is the count.</summary>
	public void FocusNext() {
		int next = Focus + 1;
		SetFocus(next == _widgets.Count ? 0 : next);
	}

	/// <summary><c>AlertPanel_FocusPrevious</c> (<c>00046600</c>): the focus − 1 focused, the last widget from 0 or −1.</summary>
	public void FocusPrevious() {
		int previous = Focus == -1 ? -1 : Focus - 1;
		SetFocus(previous < 0 ? _widgets.Count - 1 : previous);
	}

	/// <summary>
	/// <c>AlertPanel_PressWidget</c> (<c>0004662c</c>)(panel, <paramref name="flags"/>, <paramref name="index"/>): the
	/// widget's click with the flags, unless it is in state 2 or 3; its state is not changed, so a key's press shows no
	/// pressed look. An index past the list, where retail reads past it, presses nothing, an engine guard.
	/// </summary>
	public void PressWidget(short flags, int index) {
		if (index < 0 || index >= _widgets.Count) {
			return;
		}

		var widget = _widgets[index];
		if (widget.State is not (Es1Widget.Hidden or 3)) {
			widget.OnClick(flags);
		}
	}

	/// <summary>
	/// <c>StatusAlertPanel_RunModal</c> (<c>0004730c</c>), which <c>ObjectivesPanel_RunModal</c> (<c>0004a4ec</c>) repeats
	/// (docs/es1/simulation/alert-panels.md#the-loop): <see cref="Enter"/>; the focus on widget 0; until the close byte
	/// is set, a pass: the input build, its block to <see cref="HandleEvent"/>, and each widget's per-frame call; then
	/// <see cref="Leave"/>, and the close byte's bit 0 the answer. Retail's loop goes round with no wait; here a pass that
	/// leaves the close byte clear waits for the next host frame, the tick standing there (plan decision 11).
	/// </summary>
	protected IEnumerable<Es1Wait> RunModal(StrongBox<int> answer) {
		Enter();
		SetFocus(0);
		while (Close == 0) {
			var block = Globals.Input.Build();
			HandleEvent(block);
			for (int i = 0; i < _widgets.Count; i++) {
				_widgets[i].Tick();
			}

			if (Close == 0) {
				yield return Es1Wait.NextHostFrame;
			}
		}

		Leave();
		answer.Value = Close & 1;
	}

	/// <summary>
	/// <c>Alert_Dtor</c> (<c>00045d0c</c>)(panel, 3), with which the tick's callers destroy a panel after its run: the
	/// widgets, the screen buffer, the title label, the plate, the cursor records and the string table let go. Retail
	/// frees none of the panel itself, which stays allocated; nothing here keeps it.
	/// </summary>
	public void Destroy() {
		_widgets.Clear();
		ScreenUnder = null;
		TitleLabel = null;
		Plate = null;
		SavedCursor = null;
		PanelCursor = null;
		Strings = null;
	}

	/// <summary>Button 0's and [Return]'s press: the focused widget pressed with the flags 1, or widget 0 focused with none.</summary>
	private void PressFocused() {
		if (Focus == -1) {
			SetFocus(0);
		} else {
			PressWidget(1, Focus);
		}
	}
}
