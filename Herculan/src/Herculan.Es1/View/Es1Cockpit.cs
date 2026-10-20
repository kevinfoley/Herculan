using Herculan.Es1.Input;
using Herculan.Es1.Sim;

namespace Herculan.Es1.View;

/// <summary>
/// The cockpit object, <c>panel.cpp</c>'s <c>0x23d</c> bytes (docs/es1/simulation/cockpit-panel.md#the-cockpit-object), as
/// far as the view keys, the mouse and the cursors read it: <c>CockpitView_Ctor</c> (<c>ES1DBSIM 0002ae5c</c>), the
/// state <c>ColorSchemePanels_LoadAll</c> leaves and <c>Cockpit_LoadPanel</c>'s root widget and canvas context; the
/// widgets-off byte and the view device's button latch, with <c>CockpitWidgets_HandleCommand</c> (<c>0002d24c</c>) and
/// <c>CockpitView_PollViewDevice</c> (<c>0002d1a0</c>) (docs/es1/simulation/cockpit-panel.md#keys-and-the-view-device);
/// and the screen-edge strips and the cursor slots (docs/es1/simulation/cockpit-panel.md#the-screen-edges-and-the-cursors),
/// with <c>Cockpit_DrawFrame</c>'s steps that reach them. The instruments, the target selection and the message boxes
/// join it with the plan steps that port them (docs/herculan/plan-es1-first-mission.md, step 6).
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
	internal Es1Cockpit(Es1ViewManager views, Es1Mech? machine, Es1CockpitMouse mouse) {
		_views = views;
		_mouse = mouse;
		Machine = machine;
		Cursors = new Es1CockpitCursors(views.Files.Cursors, mouse.Cursor);
		var layout = views.Files.Layout;
		var head = new Es1Rect(layout.Origin.X, layout.Origin.Y, BitConverter.ToInt32(layout.Bytes8, 0), BitConverter.ToInt32(layout.Bytes8, 4));
		Root = LoadPanel(head);
		var gunsight = layout.Gunsight;
		GunsightRect = new Es1Rect(gunsight.Origin.X, gunsight.Origin.Y, gunsight.Int2, gunsight.Int3);
		BuildGunsight(gunsight.ReticleCentre.X, gunsight.ReticleCentre.Y);
	}

	/// <summary><c>+0x1ff</c>, the machine.</summary>
	public Es1Mech? Machine { get; }

	/// <summary><c>+0</c>, the root widget, built over the <c>.GAU</c> record's first 16 bytes.</summary>
	public Es1WidgetRoot Root { get; }

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

	/// <summary>
	/// The rect <c>+0</c>–<c>+0xc</c> of the roving gunsight <c>+0x1f1</c>, the <c>.GAU</c> block at 1104's first 16
	/// bytes as <c>PanelGauge_Ctor</c> copies them (docs/es1/simulation/cockpit-gunsight-hud.md#building-the-gunsight),
	/// which the gunsight drag tests the pointer against.
	/// </summary>
	public Es1Rect GunsightRect { get; }

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
	/// <c>Cockpit_DrawFrame</c> (<c>0002cf28</c>)(cockpit), once a frame from <c>Sim_RenderFrame</c> before the 3D pass:
	/// the strips built the first time; with the widgets off nothing more this engine draws; otherwise, at step 6, the
	/// edge cursor. <c>Cockpit_SkipFrameDraw</c>, whose only writer is the unported <c>00057820</c>, is never set.
	/// </summary>
	public void DrawFrame() {
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

		// 4. and 5. PLACEHOLDER: PilotMessagePort_RestoreBackground while no view change is staged, and between the canvas
		// push and pop the weapon rows', the pod row's and six instruments' slot +0xc: plan step 6c.
		// 6. PLACEHOLDER: Cockpit_RunTtmScripts and the ticker's MessageTicker_Update, plan step 6c; then the edge cursor.
		Cursors.UpdateEdgeCursor();

		// 7. PLACEHOLDER: TargetSelect_CanTarget on the selection +0x20c, plan step 6b.
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
	/// other code to the instruments. Returns 1 for a code a case took or an instrument accepted, else 0.
	/// </summary>
	public short HandleCommand(ushort code) {
		if (WidgetsOff) {
			return 0;
		}

		int view = _views.CurrentView;
		switch (code) {
			case 0:
				return 0;
			case >= Key1 and <= Key9:
				if (view == 1) {
					return Unlisted(code, view);
				}

				// PLACEHOLDER: row code - 2: the pod row (+0x23b) flips bit 0 of 00037b50's block and hands it to 00037b14,
				// another row's gauge (+0x70 + 4 x row), when set, goes to CockpitView_ArmWeaponByGauge (0002d098); the
				// weapon rows are plan step 6c's, and until then no row has a gauge.
				return 1;
			case >= F1 and <= F6:
				// PLACEHOLDER: the press of the MFD's (+0x1e9) button code - F1; the MFD is plan step 6c's.
				Queue(view switch { 1 => 1, 2 or 3 => 6, _ => -1 });
				return 1;
			case F7 or F8:
				// PLACEHOLDER: the press of +0x1f5's button code - F7; the instrument is plan step 6c's.
				Queue(0);
				return 1;
			case F9:
				Queue(view switch { 0 => 5, 2 => 6, _ => -1 });
				return 1;
			case F10:
				Queue(view switch { 0 => 4, 3 => 6, _ => -1 });
				return 1;
			case Esc:
				Queue(view switch { 0 => 0, 1 => 1, 4 => -1, _ => 6 });
				return 1;
			case Tab:
				if (view == 4) {
					return 0;
				}

				if (view == 1) {
					return Unlisted(code, view);
				}

				// PLACEHOLDER: TargetSelect_Cycle (0002d898)(cockpit), the target selection, plan step 6b.
				return 1;
			case F11:
				// PLACEHOLDER: 0004a1b4(0, 0, 1), the objectives panel, run through slot +0xc of its +0x314 and destroyed by
				// 00045d0c(panel, 3): the in-mission panels, plan step 6a2.
				return 1;
			case R:
				Machine?.ToggleRadarMode();
				return 1;
			default:
				return Unlisted(code, view);
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
	/// <c>Cockpit_LoadPanel</c> (<c>0002c9d8</c>)'s first steps on the <c>.GAU</c> record's first 16 bytes,
	/// <paramref name="head"/>: the canvas context's rect (<c>Cockpit_CreateCanvasContext</c>), the root widget
	/// (<c>CTLWindow_Ctor</c>) made the root the mouse's events go to (<c>Widget_ClearPressed</c>).
	/// </summary>
	private Es1WidgetRoot LoadPanel(Es1Rect head) {
		CanvasRect = head;
		var root = new Es1WidgetRoot(head);
		_mouse.Widgets.ClearPressed(root);

		// PLACEHOLDER: the .TTM manager, the pilot channel (record + 1668) and the ticker (record + 1684), plan step 6c;
		// and the instruments, the gunsight's but for what BuildGunsight builds, plan steps 6b and 6c. The gunsight's
		// click surface registers its gadget on the root before the strips, so it wins where they overlap.
		return root;
	}

	/// <summary>
	/// What <c>Gau_RovingGunsightWidget</c> (<c>00034aec</c>) does with the pointer between its children 0 and 1, the rest
	/// of the gunsight being plan steps 6b and 6c's: the drawn cursor and the driver's pointer put on the reticle's centre
	/// (<paramref name="x"/>, <paramref name="y"/>), and a move with no button down posted as the driver would send it
	/// (docs/es1/simulation/cockpit-gunsight-hud.md#building-the-gunsight).
	/// </summary>
	private void BuildGunsight(int x, int y) {
		_mouse.Cursor.SyncPosition(x, y);
		if (_mouse.Driver is { } driver) {
			driver.SetPosition(x, y);
			driver.PostEvent(new Es1MouseRecord(x, y, 0), Es1HostMouse.Moved);
		}
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
	/// accepts. PLACEHOLDER: the instruments are plan step 6c's; with none built, none accepts and the code answers 0.
	/// </summary>
	private static short Unlisted(ushort code, int view) {
		_ = code;
		_ = view;
		return 0;
	}
}
