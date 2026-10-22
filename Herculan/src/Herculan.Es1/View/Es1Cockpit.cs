using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Herculan.Es1.Gunsight;
using Herculan.Es1.Input;
using Herculan.Es1.Messages;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.Throttle;

namespace Herculan.Es1.View;

/// <summary>
/// The cockpit object, <c>panel.cpp</c>'s <c>0x23d</c> bytes (docs/es1/simulation/cockpit-panel.md#the-cockpit-object), as
/// far as the view keys, the mouse and the cursors read it: <c>CockpitView_Ctor</c> (<c>ES1DBSIM 0002ae5c</c>), the
/// state <c>ColorSchemePanels_LoadAll</c> leaves and <c>Cockpit_LoadPanel</c>'s root widget and canvas context; the
/// widgets-off byte and the view device's button latch, with <c>CockpitWidgets_HandleCommand</c> (<c>0002d24c</c>) and
/// <c>CockpitView_PollViewDevice</c> (<c>0002d1a0</c>) (docs/es1/simulation/cockpit-panel.md#keys-and-the-view-device);
/// and the screen-edge strips and the cursor slots (docs/es1/simulation/cockpit-panel.md#the-screen-edges-and-the-cursors),
/// with <c>Cockpit_DrawFrame</c>'s steps that reach them; the throttle gauge <c>+0x1ed</c> and the roving gunsight
/// <c>+0x1f1</c> with the canvas context they draw through and <c>Widget_DrawToCockpit</c>
/// (docs/es1/simulation/cockpit-panel.md#drawing-it); the computer's ticker <c>+0x207</c>
/// (<see cref="Es1MessageTicker"/>); and the target selection <c>+0x20c</c> (<see cref="Es1TargetSelection"/>). The
/// other instruments and the pilot channel join it with plan step 6c (docs/herculan/plan-es1-first-mission.md).
///
/// <para><see cref="Es1ViewManager"/> builds it, as <c>CockpitViewManager_LoadViews</c> does, and it is
/// <c>CockpitViewInstance</c> (<c>00092d1c</c>), the first cockpit built.</para>
/// </summary>
public sealed class Es1Cockpit {
	/// <summary>The command codes <see cref="HandleCommand"/> takes by name: set-1 scancodes.</summary>
	public const ushort Esc = 0x01, Tab = 0x0f, R = 0x13, F1 = 0x3b, F6 = 0x40, F7 = 0x41, F8 = 0x42, F9 = 0x43, F10 = 0x44,
		F11 = 0x57;

	/// <summary>The number keys' codes, <c>1</c> (2) to <c>9</c> (10), rows 0 to 8.</summary>
	public const ushort Key1 = 0x02, Key9 = 0x0a;

	/// <summary>The screen's size, the strips' full-screen shift: 320 across and 200 down.</summary>
	private const int ScreenWidth = Es1CockpitViews.ScreenWidth, ScreenHeight = Es1CockpitViews.ScreenHeight;

	private readonly Es1ViewManager _views;
	private readonly Es1CockpitMouse _mouse;

	/// <summary>
	/// <c>CockpitView_Ctor</c>(0, <paramref name="machine"/>), <c>ColorSchemePanels_LoadAll</c>'s cursors and clears
	/// (the widgets on, the forward slot active), and <c>Cockpit_LoadPanel</c> on the manager's <c>.GAU</c> layout as
	/// far as this engine builds it.
	/// </summary>
	/// <param name="views">The manager whose current view the keys read and whose commands they queue, <c>CockpitViewManager_Published</c>.</param>
	/// <param name="machine"><c>+0x1ff</c>, the manager's machine.</param>
	/// <param name="mouse">The cockpit's mouse, whose root the panel's root becomes.</param>
	/// <param name="globals">The simulation's globals, whose live object list the target selection walks.</param>
	/// <param name="preferences">The simulator's options, whose <see cref="Es1Preferences.MessagePortShowEnabled"/> the message boxes read.</param>
	internal Es1Cockpit(Es1ViewManager views, Es1Mech? machine, Es1CockpitMouse mouse, Es1SimGlobals globals,
			Es1Preferences preferences) {
		_views = views;
		_mouse = mouse;
		Machine = machine;
		Globals = globals;
		Preferences = preferences;
		Targets = new Es1TargetSelection(this);
		Cursors = new Es1CockpitCursors(views.Files.Cursors, mouse.Cursor);
		globals.CockpitViewInstance ??= this;
		LoadPanel(views.Files);
	}

	/// <summary>The simulator's options, of which the message boxes read <see cref="Es1Preferences.MessagePortShowEnabled"/>.</summary>
	public Es1Preferences Preferences { get; }

	/// <summary><c>+0x207</c>, the computer's ticker, which <c>Cockpit_LoadPanel</c> builds on the <c>.GAU</c> record + 1684.</summary>
	public Es1MessageTicker Ticker { get; private set; }

	/// <summary>The message boxes' string tables, globals in retail, which the first port built fills.</summary>
	public Es1MessageTables MessageTables { get; } = new();

	/// <summary>
	/// The in-mission panels' shared state, whose <c>AlertPanel_OpenCount</c> the message boxes' page copy reads; none,
	/// as before the host wires it, reads as no panel up.
	/// </summary>
	internal Es1AlertGlobals? Panels { get; set; }

	/// <summary><c>SimDisplay</c>, the display the cockpit draws on.</summary>
	internal Es1VideoMemory Display => _views.Display;

	/// <summary>The view manager's <c>+0x18</c>, set while a view change is staged.</summary>
	internal bool ViewChangeStaged => _views.Staged;

	/// <summary><c>+0x1ff</c>, the machine.</summary>
	public Es1Mech? Machine { get; }

	/// <summary><c>+0</c>, the root widget, built over the <c>.GAU</c> record's first 16 bytes.</summary>
	public Es1WidgetRoot Root { get; private set; }

	/// <summary>
	/// The canvas context's rect, <c>+0x210</c>–<c>+0x21c</c> of <c>+4</c>: the <c>.GAU</c> record's first 16 bytes, as
	/// <c>Cockpit_CreateCanvasContext</c> (<c>0002ad78</c>) sets it.
	/// </summary>
	public Es1Rect CanvasRect { get; private set; }

	/// <summary>
	/// The canvas context's origin, <c>+0x220</c>/<c>+0x224</c> of <c>+4</c>, which <see cref="TranslateForView"/> takes
	/// each view's move from. It starts as the display's render context's, which <c>GLViewport_Ctor</c> (<c>00069a84</c>)
	/// copies when the cockpit is built; this engine takes (0, 0), the origin its view manager's window holds before the
	/// first view is installed.
	/// </summary>
	public (int X, int Y) CanvasOrigin { get; private set; }

	/// <summary><c>+0x1ed</c>, the throttle gauge, which <c>Gau_ThrottleWidget</c> (<c>0002cdf0</c>) builds on the <c>.GAU</c> block at 1000.</summary>
	public Es1ThrottleGauge Throttle { get; private set; }

	/// <summary><c>+0x1f1</c>, the roving gunsight, which <c>Gau_RovingGunsightWrapper</c> (<c>0002ce28</c>) builds on the <c>.GAU</c> block at 1104.</summary>
	public Es1Gunsight Gunsight { get; private set; }

	/// <summary>
	/// <c>000b5bca</c>, a global: set while <c>Sim_StartMission</c> brings the cockpit up, from its step 13 to its step 15
	/// (docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc), when <see cref="DrawToCockpit"/>
	/// copies nothing.
	/// </summary>
	public bool BringUp { get; set; }

	/// <summary>
	/// The rect <c>+0</c>–<c>+0xc</c> of the roving gunsight <c>+0x1f1</c>, the <c>.GAU</c> block at 1104's first 16
	/// bytes as <c>PanelGauge_Ctor</c> copies them (docs/es1/simulation/cockpit-gunsight-hud.md#building-the-gunsight),
	/// which the gunsight drag tests the pointer against.
	/// </summary>
	public Es1Rect GunsightRect => Gunsight.Rect;

	/// <summary>The simulation's globals: the live object list the target selection walks, and the scan range's header word.</summary>
	public Es1SimGlobals Globals { get; }

	/// <summary>The target selection, <c>+0x20c</c>–<c>+0x214</c>, and the functions that walk it.</summary>
	public Es1TargetSelection Targets { get; }

	/// <inheritdoc cref="Es1TargetSelection.Selection"/>
	public IEs1SimObject? Selection => Targets.Selection;

	/// <summary>The view manager's current view <c>+0x10</c>, which the gunsight's update reads through <c>CockpitViewManager_Published</c>.</summary>
	internal int CurrentView => _views.CurrentView;

	/// <inheritdoc cref="Es1ViewManager.Projection"/>
	internal Es1ViewProjection? Projection => _views.Projection;

	/// <inheritdoc cref="Es1ViewManager.SceneCanvas"/>
	internal Es1SceneCanvas SceneCanvas() => _views.SceneCanvas();

	/// <summary>The display library's cursor, which <c>Cursor_GetPosition</c> reads.</summary>
	internal Es1Cursor Cursor => _mouse.Cursor;

	/// <inheritdoc cref="Es1CockpitMouse.CanvasPoint"/>
	internal (int X, int Y) MouseCanvasPoint => _mouse.CanvasPoint;

	/// <summary><c>+0x216</c>, <c>+0x21a</c>, <c>+0x21e</c>, the bottom, left and right screen-edge strips, once built.</summary>
	public Es1ScrollTrigger? BottomStrip { get; private set; }

	/// <inheritdoc cref="BottomStrip"/>
	public Es1ScrollTrigger? LeftStrip { get; private set; }

	/// <inheritdoc cref="BottomStrip"/>
	public Es1ScrollTrigger? RightStrip { get; private set; }

	/// <summary><c>+0x222</c>–<c>+0x236</c>, the cursor slots.</summary>
	public Es1CockpitCursors Cursors { get; }

	/// <summary><c>+0x20b</c>, set while the widgets are off: in the external view, when every key the cockpit owns goes dead.</summary>
	public bool WidgetsOff { get; private set; }

	/// <summary><c>+0x23a</c>, set by <c>Cockpit_DrawFrame</c> once <c>CockpitView_BuildScrollTriggers</c> has run.</summary>
	public bool StripsBuilt { get; private set; }

	/// <summary><c>CockpitView_DeviceButtonLatch</c> (<c>00092f33</c>), a global: the mouse's <c>+0xb</c> has been pressed and not let go.</summary>
	public bool DeviceButtonLatch { get; private set; }

	/// <summary><c>CockpitView_DisableWidgets</c> (<c>0002b2d0</c>): <see cref="WidgetsOff"/> set.</summary>
	public void DisableWidgets() => WidgetsOff = true;

	/// <summary><c>CockpitView_EnableWidgets</c> (<c>0002b2ec</c>): <see cref="WidgetsOff"/> cleared.</summary>
	public void EnableWidgets() => WidgetsOff = false;

	/// <summary>
	/// <c>Cockpit_SkipFrameDraw</c> (<c>00092d7c</c>), a global: <see cref="DrawFrame"/> does nothing while it is set.
	/// <c>PreferencesPanel_Raise</c> sets it across one frame of its return to a cockpit view
	/// (docs/es1/simulation/cockpit-panel.md#drawing-it).
	/// </summary>
	public bool SkipFrameDraw { get; set; }

	/// <summary>
	/// <c>Cockpit_DrawFrame</c> (<c>0002cf28</c>)(cockpit), once a frame from <c>Sim_RenderFrame</c> before the 3D pass:
	/// nothing while <see cref="SkipFrameDraw"/> is set; the strips built the first time; with the widgets off nothing
	/// more this engine draws; otherwise, at step 5, the throttle gauge's per-frame slot inside the canvas push, at step 6
	/// the ticker's (<see cref="Es1MessageTicker.Update"/>) and the edge cursor, and at step 7 the selection dropped when
	/// <see cref="Es1TargetSelection.CanTarget"/> refuses it.
	/// </summary>
	public void DrawFrame() {
		if (SkipFrameDraw) {
			return;
		}

		// 1.
		if (!StripsBuilt) {
			BuildScrollTriggers();
			StripsBuilt = true;
		}

		// 2. PLACEHOLDER: the clock's slot +0xc (+0x1f9), plan step 6c, and Cockpit_HitShakeTick, plan step 7.
		// 3. With the widgets off, slot +0xc of the instrument +0x1f5, plan step 6c, and nothing more.
		if (WidgetsOff) {
			return;
		}

		// 4. PLACEHOLDER: PilotMessagePort_RestoreBackground while no view change is staged, plan step 6c.
		// 5. Between the canvas push and pop, slot +0xc of each set weapon row, the pod row, +0x1dd, +0x1e1, +0x1e5,
		// +0x1ed, +0x1e9 and +0x1f5. PLACEHOLDER: all but the throttle gauge +0x1ed, plan step 6c's instruments.
		Throttle.Update();
		// 6. PLACEHOLDER: Cockpit_RunTtmScripts, plan step 6c's .TTM scripts. Then the ticker's slot +0x10 and the edge
		// cursor.
		Ticker.Update();
		Cursors.UpdateEdgeCursor();

		// 7.
		if (Targets.Selection is { } selection && !Targets.CanTarget(selection)) {
			Targets.Selection = null;
		}
	}

	/// <summary>
	/// <c>Cockpit_PushCanvasContext</c> (<c>0002b00c</c>): the canvas context, its rect and origin as they stand, over the
	/// display's draw page. The pop, <c>Cockpit_PopRenderContext</c>, has nothing to put back: the context is handed to
	/// what draws rather than installed.
	/// </summary>
	internal Es1PanelCanvas PushCanvasContext() => new(_views.Display, CanvasRect, CanvasOrigin);

	/// <summary>
	/// <c>CockpitView_RectCornerVisible</c> (<c>0002b214</c>)(<paramref name="rect"/>): false while the widgets are off;
	/// otherwise whether the corner (<i>x0</i>, <i>y0</i>) or (<i>x1</i>, <i>y1</i>) lies in the box whose corner is the
	/// canvas context's rect corner plus its origin, columns 0 to 319 and rows 0 to 200 from it, both bounds included.
	/// </summary>
	public bool RectCornerVisible(Es1Rect rect) {
		if (WidgetsOff) {
			return false;
		}

		int left = CanvasRect.X0 + CanvasOrigin.X, top = CanvasRect.Y0 + CanvasOrigin.Y;
		return Inside(rect.X0, rect.Y0) || Inside(rect.X1, rect.Y1);

		bool Inside(int x, int y) => left <= x && x <= left + ScreenWidth - 1 && top <= y && y <= top + ScreenHeight;
	}

	/// <summary>
	/// <c>Widget_DrawToCockpit</c> (<c>0002b058</c>)(0, <paramref name="rect"/>, 1), a widget's rect copied from the page
	/// drawn on to the page on the screen (docs/es1/simulation/cockpit-panel.md#drawing-it): nothing, answering false,
	/// unless <see cref="RectCornerVisible"/> passes the rect, no view change is staged and the cockpit is not being
	/// brought up (<see cref="BringUp"/>); otherwise, inside the canvas push, the display's <c>+0x8c</c> made its source
	/// page and <c>+0x88</c> its draw page, the rect copied between them as a type-0 region at the offset (0, 0),
	/// <c>Cockpit_ZeroOffset</c>, with the cursor taken off and drawn again around the copy (the driver's slots 31 and 30),
	/// and the draw page put back; the source page stays the one it set.
	/// </summary>
	internal bool DrawToCockpit(Es1Rect rect) {
		if (!RectCornerVisible(rect) || _views.Staged || BringUp) {
			return false;
		}

		var display = _views.Display;
		var canvas = PushCanvasContext();
		int draw = display.DrawPage;
		display.SourcePage = display.PairDrawn;
		display.DrawPage = display.PairVisible;
		display.Cursor.Hide();
		canvas.CopyRegion(rect, 0, 0);
		display.Cursor.Show();
		display.DrawPage = draw;
		return true;
	}

	/// <summary>
	/// <c>CockpitView_InvalidateWidgets</c> (<c>0002b4a0</c>)(cockpit), which <c>CockpitView_SetView</c> runs for views 0
	/// to 3: unless the widgets are off, the instruments' slot <c>+8</c>, of which the throttle gauge's,
	/// <see cref="Es1ThrottleGauge.Paint"/>, and the gunsight's, <see cref="Es1Gunsight.Paint"/>, are ported.
	/// </summary>
	public void InvalidateWidgets() {
		if (WidgetsOff) {
			return;
		}

		// Between the canvas push and pop, slot +8 of the nine weapon rows, the pod row +0x1d8, +0x1dd, +0x1e1, +0x1e5,
		// +0x1ed, +0x1e9 and +0x1f5. PLACEHOLDER: all but the throttle gauge +0x1ed, plan step 6c's instruments.
		Throttle.Paint();
		Gunsight.Paint();
		// PLACEHOLDER: slot +8 of the clock +0x1f9, and Cockpit_RunTtmScripts: plan step 6c.
	}

	/// <summary>
	/// <c>CockpitWidgets_TranslateForView</c> (<c>0002ced0</c>)(cockpit, delta), from the view changes: the root widget
	/// moved by the delta, so a press is tested from the moved origin; the delta taken off the canvas context's origin;
	/// and once the strips exist, <see cref="FlipScrollTriggers"/>. The instruments' moves are plan step 6c's.
	/// </summary>
	public void TranslateForView(int dx, int dy) {
		Root.TranslateForView(dx, dy);
		CanvasOrigin = (CanvasOrigin.X - dx, CanvasOrigin.Y - dy);
		if (StripsBuilt) {
			FlipScrollTriggers(dx, dy);
		}
	}

	/// <summary>
	/// <c>CockpitView_HandleEdgeTrigger</c> (<c>0002dc2c</c>)(cockpit, strip), a strip's click: by the current view, the
	/// bottom strip queues command 0 from view 0 and 1 from view 1, the left 5 from view 0 and 6 from view 3, the right 4
	/// from view 0 and 6 from view 2.
	/// </summary>
	public void HandleEdgeTrigger(Es1ScrollTrigger strip) {
		int view = _views.CurrentView;
		int command = -1;
		if (ReferenceEquals(strip, BottomStrip)) {
			command = view switch { 0 => 0, 1 => 1, _ => -1 };
		} else if (ReferenceEquals(strip, LeftStrip)) {
			command = view switch { 0 => 5, 3 => 6, _ => -1 };
		} else if (ReferenceEquals(strip, RightStrip)) {
			command = view switch { 0 => 4, 2 => 6, _ => -1 };
		}

		Queue(command);
	}

	/// <summary>
	/// <c>Cockpit_PickEdgeCursor</c> (<c>0002dcc8</c>)(cockpit, point): the point rebased by the canvas context's rect and
	/// origin; no arrow, then for the first strip whose rect holds it, by the current view: the bottom
	/// <see cref="Es1CockpitCursors.South"/> in view 0 and <see cref="Es1CockpitCursors.North"/> in view 1, the left
	/// <see cref="Es1CockpitCursors.West"/> in view 0 and <see cref="Es1CockpitCursors.East"/> in view 3, the right
	/// <see cref="Es1CockpitCursors.East"/> in view 0 and <see cref="Es1CockpitCursors.West"/> in view 2. A replay's
	/// recorded point (<c>Input_GetCursorPos</c>) is not taken: no plan step ports tapes.
	/// </summary>
	public void PickEdgeCursor(int x, int y) {
		int canvasX = x - CanvasRect.X0 + CanvasOrigin.X;
		int canvasY = y - CanvasRect.Y0 + CanvasOrigin.Y;
		int view = _views.CurrentView;
		Cursors.EdgeArrow = 0;
		if (BottomStrip?.Rect.Contains(canvasX, canvasY) == true) {
			Cursors.EdgeArrow = view switch { 0 => Es1CockpitCursors.South, 1 => Es1CockpitCursors.North, _ => 0 };
		} else if (LeftStrip?.Rect.Contains(canvasX, canvasY) == true) {
			Cursors.EdgeArrow = view switch { 0 => Es1CockpitCursors.West, 3 => Es1CockpitCursors.East, _ => 0 };
		} else if (RightStrip?.Rect.Contains(canvasX, canvasY) == true) {
			Cursors.EdgeArrow = view switch { 0 => Es1CockpitCursors.East, 2 => Es1CockpitCursors.West, _ => 0 };
		}
	}

	/// <summary>
	/// <c>CockpitWidgets_HandleCommand</c>(cockpit, <paramref name="code"/>): 0 while the widgets are off; otherwise the
	/// case of the 24 codes at <c>CockpitWidgets_CommandCodes</c> (<c>00092f34</c>) by the manager's current view, and any
	/// other code to the instruments. Answers in <paramref name="answer"/> 1 for a code a case took or an instrument
	/// accepted, else 0. <c>F11</c>'s case runs the objectives panel's loop inside the tick, yielding a wait at each of its
	/// passes (plan decision 11), and answers once the panel is down.
	/// </summary>
	/// <param name="panels">The in-mission panels' shared state, from which <c>F11</c> builds its panel; with none it builds none and still answers 1, an engine guard as for <see cref="Es1Simulation.Panels"/>.</param>
	public IEnumerable<Es1Wait> HandleCommand(ushort code, Es1AlertGlobals? panels, StrongBox<short> answer) {
		answer.Value = 0;
		if (WidgetsOff) {
			yield break;
		}

		int view = _views.CurrentView;
		switch (code) {
			case 0:
				break;
			case >= Key1 and <= Key9:
				// PLACEHOLDER: outside view 1, row code - 2: the pod row (+0x23b) flips bit 0 of 00037b50's block and hands it
				// to 00037b14, another row's gauge (+0x70 + 4 x row), when set, goes to CockpitView_ArmWeaponByGauge
				// (0002d098); the weapon rows are plan step 6c's, and until then no row has a gauge.
				answer.Value = view == 1 ? Unlisted(code, view) : (short)1;
				break;
			case >= F1 and <= F6:
				// PLACEHOLDER: the press of the MFD's (+0x1e9) button code - F1; the MFD is plan step 6c's.
				Queue(view switch { 1 => 1, 2 or 3 => 6, _ => -1 });
				answer.Value = 1;
				break;
			case F7 or F8:
				// PLACEHOLDER: the press of +0x1f5's button code - F7; the instrument is plan step 6c's.
				Queue(0);
				answer.Value = 1;
				break;
			case F9:
				Queue(view switch { 0 => 5, 2 => 6, _ => -1 });
				answer.Value = 1;
				break;
			case F10:
				Queue(view switch { 0 => 4, 3 => 6, _ => -1 });
				answer.Value = 1;
				break;
			case Esc:
				Queue(view switch { 0 => 0, 1 => 1, 4 => -1, _ => 6 });
				answer.Value = 1;
				break;
			case Tab:
				if (view is not (1 or 4)) {
					Targets.Cycle();
				}

				answer.Value = view switch { 4 => 0, 1 => Unlisted(code, view), _ => 1 };
				break;
			case F11:
				foreach (var wait in RunObjectivesPanel(panels)) {
					yield return wait;
				}

				answer.Value = 1;
				break;
			case R:
				Machine?.ToggleRadarMode();
				answer.Value = 1;
				break;
			default:
				answer.Value = Unlisted(code, view);
				break;
		}
	}

	/// <summary>
	/// <c>CockpitView_PollViewDevice</c>(cockpit, <paramref name="block"/>), once a tick from <c>Sim_PollPlayerInput</c>:
	/// the input block's buttons 4 to 7 (<c>+0x19</c>–<c>+0x1c</c>) queue view commands 1, 0, 4 and 5; the press of the
	/// mouse's byte <c>+0xb</c>, latched, in view 1 sends <c>Tab</c> to the key slot of <c>+0x1f5</c>. It returns 1,
	/// which its caller discards.
	///
	/// <para>Retail's two stick models that make buttons 4–7 make them from a hat: option 3's from port axis 3 and
	/// option 5's from buttons 0–3 held together (docs/es1/simulation/joystick.md#hats-and-throttles). A modern
	/// controller's own hat is not read (docs/herculan/plan-es1-first-mission.md, "What the slice leaves out"), so on one
	/// these buttons come only from those two models: option 5's from its buttons, and option 3's from whatever the
	/// throttle role puts on axis 3.</para>
	/// </summary>
	public void PollViewDevice(Es1PlayerInputBlock block) {
		if (StripsBuilt) {
			PickEdgeCursor(block.PointerX, block.PointerY);
		}

		if (block.Buttons[4] != 0) {
			_views.QueueViewCommand(1);
		}

		if (block.Buttons[5] != 0) {
			_views.QueueViewCommand(0);
		}

		if (block.Buttons[6] != 0) {
			_views.QueueViewCommand(4);
		}

		if (block.Buttons[7] != 0) {
			_views.QueueViewCommand(5);
		}

		if (block.MouseButtons[1] == 0) {
			DeviceButtonLatch = false;
		} else {
			if (!DeviceButtonLatch && _views.CurrentView == 1) {
				// PLACEHOLDER: Tab (0x0f) to the key slot (+0x10 of the table at +0x76) of +0x1f5, plan step 6c.
			}

			DeviceButtonLatch = true;
		}
	}

	/// <summary>
	/// <c>Cockpit_LoadPanel</c> (<c>0002c9d8</c>) on <paramref name="files"/>' <c>.GAU</c> layout, as far as this engine
	/// builds it: over the record's first 16 bytes the canvas context's rect (<c>Cockpit_CreateCanvasContext</c>) and the
	/// root widget (<c>CTLWindow_Ctor</c>), made the root the mouse's events go to (<c>Widget_ClearPressed</c>); the
	/// ticker <c>+0x207</c> on the record + 1684; then, of
	/// the instruments in retail's order, the throttle gauge <c>+0x1ed</c> on the block at 1000 and the gunsight
	/// <c>+0x1f1</c> on the block at 1104. Each registers its gadget on the root in that order, the throttle's slider
	/// first, and both before the strips the first frame builds, so each wins where a later one overlaps it.
	/// </summary>
	[MemberNotNull(nameof(Root), nameof(Ticker), nameof(Throttle), nameof(Gunsight))]
	private void LoadPanel(Es1CockpitViews files) {
		var layout = files.Layout;
		var head = new Es1Rect(layout.Origin.X, layout.Origin.Y, BitConverter.ToInt32(layout.Bytes8, 0), BitConverter.ToInt32(layout.Bytes8, 4));
		CanvasRect = head;
		Root = new Es1WidgetRoot(head);
		_mouse.Widgets.ClearPressed(Root);

		// PLACEHOLDER: the .TTM manager, and the pilot channel, DialogPort_Ctor(0, record + 1668) at +0x203: plan step 6c.
		// Retail builds the pilot channel first, and its MessagePort_CtorBase loads str\system.str; with no pilot channel,
		// the ticker's loads it.
		Ticker = new Es1MessageTicker(this, Es1Rect.From(layout.TickerBox), MessageTables, files.SystemStrings,
			files.Fonts[Es1CockpitViews.CpRed]);
		// PLACEHOLDER: the instruments +0x1dd, +0x1e1, +0x1e5 and +0x1e9 before the throttle gauge: plan step 6c.
		Throttle = new Es1ThrottleGauge(this, layout.ThrottleBlock, files.ThrottleLights, files.PushButtons);
		Gunsight = new Es1Gunsight(this, layout.Gunsight, files.GunsightBanks, files.Fonts, _mouse);
		// PLACEHOLDER: the instrument +0x1f5, the clock +0x1f9 and Ttm_RunAll: plan step 6c.
	}

	/// <summary>
	/// <c>CockpitView_BuildScrollTriggers</c> (<c>0002d970</c>)(cockpit): the three strips on their fixed rects, each
	/// registered in the root's list, after whatever the panel registered.
	/// </summary>
	private void BuildScrollTriggers() {
		BottomStrip = new Es1ScrollTrigger(this) { Rect = new Es1Rect(0, 197, 319, 200) };
		Root.RegisterClickable(BottomStrip);
		LeftStrip = new Es1ScrollTrigger(this) { Rect = new Es1Rect(0, 0, 5, 200) };
		Root.RegisterClickable(LeftStrip);
		RightStrip = new Es1ScrollTrigger(this) { Rect = new Es1Rect(314, 0, 319, 200) };
		Root.RegisterClickable(RightStrip);
	}

	/// <summary>
	/// <c>CockpitView_FlipScrollTriggers</c> (<c>0002db1c</c>)(cockpit, delta): per axis, a delta of 6 or more sets the sign
	/// +1 and the shift 320 across or 200 down, one below −5 the sign −1 and −320 or −200. Each strip with a sign on an
	/// axis is reflected across its own edge there (+1: <i>x1</i> = <i>x0</i>, <i>x0</i> less the width; −1: <i>x0</i> =
	/// <i>x1</i>, <i>x1</i> plus the width), then moved by −(delta − shift), so each ends on the edge facing the view it
	/// leads to (docs/retail/simulation/cockpit-input.md#a-strip-changes-edge-with-the-view).
	/// </summary>
	private void FlipScrollTriggers(int dx, int dy) {
		var (signX, shiftX) = Classify(dx, ScreenWidth);
		var (signY, shiftY) = Classify(dy, ScreenHeight);
		foreach (var strip in new[] { BottomStrip, LeftStrip, RightStrip }) {
			if (strip == null) {
				continue;
			}

			var r = strip.Rect;
			(int x0, int x1) = Reflect(r.X0, r.X1, signX);
			(int y0, int y1) = Reflect(r.Y0, r.Y1, signY);
			strip.Rect = new Es1Rect(x0, y0, x1, y1).Offset(-(dx - shiftX), -(dy - shiftY));
		}

		static (int Sign, int Shift) Classify(int delta, int screen) =>
			delta >= 6 ? (1, screen) : delta < -5 ? (-1, -screen) : (0, 0);

		static (int Low, int High) Reflect(int low, int high, int sign) {
			int size = high - low;
			return sign switch {
				1 => (low - size, low),
				-1 => (high, high + size),
				_ => (low, high),
			};
		}
	}

	/// <summary>
	/// <c>F11</c>'s case: <c>ObjectivesPanel_Ctor</c>(0, 0, 1), run through its slot <c>+0xc</c>, whose answer the case
	/// does not read, then destroyed with <c>Alert_Dtor</c>(panel, 3)
	/// (docs/es1/simulation/cockpit-panel.md#keys-and-the-view-device). With no <paramref name="panels"/> nothing is raised.
	/// </summary>
	private static IEnumerable<Es1Wait> RunObjectivesPanel(Es1AlertGlobals? panels) {
		if (panels == null) {
			yield break;
		}

		var panel = new Es1ObjectivesPanel(panels);
		foreach (var wait in panel.Run(new StrongBox<int>())) {
			yield return wait;
		}

		panel.Destroy();
	}

	/// <summary><c>CockpitViewManager_QueueViewCommand</c> with <paramref name="command"/>, or nothing for −1.</summary>
	private void Queue(int command) {
		if (command != -1) {
			_views.QueueViewCommand(command);
		}
	}

	/// <summary>
	/// An unlisted code: <c>MfdFlashComm_HandleAltKey</c> (<c>0003a890</c>)(the MFD <c>+0x1e9</c>, code); when that answers
	/// 0, in view 1 the key slot of <c>+0x1f5</c> alone, and in any other view the key slots of <c>+0x1d4</c> when set,
	/// <c>+0x1dd</c>, <c>+0x1e1</c>, <c>+0x1e5</c>, <c>+0x1ed</c>, <c>+0x1f1</c> and <c>+0x1e9</c>, to the first that
	/// accepts; 1 when one does, else 0. The throttle gauge's and the gunsight's slots take no key.
	/// </summary>
	private static short Unlisted(ushort code, int view) {
		// PLACEHOLDER: MfdFlashComm_HandleAltKey on the MFD +0x1e9, and in view 1 the key slot of +0x1f5, plan step 6c's
		// instruments; until then neither accepts.
		if (view == 1) {
			return 0;
		}

		// PLACEHOLDER: the key slots of +0x1d4, +0x1dd, +0x1e1 and +0x1e5 before the throttle gauge's, and of +0x1e9 after
		// the gunsight's, plan step 6c's instruments; until then none accepts.
		return Es1ThrottleGauge.KeyDispatch(code) != 0 || Es1Gunsight.KeyDispatch(code) != 0 ? (short)1 : (short)0;
	}
}
