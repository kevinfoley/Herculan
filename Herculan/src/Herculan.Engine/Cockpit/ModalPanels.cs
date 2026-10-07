using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Input;
using Herculan.Engine.Render.Cockpit;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.World;
using Silk.NET.Input;
using static Herculan.Engine.Input.KeyChords;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The simulator's four modal panels: the status-alert family ([Q], [Ctrl+Q], [P] and the mission's own
/// alerts), the [F11] objectives, the [F12] preferences and the CONTROLS panel it raises. Each is modal in
/// the original — it runs its own event loop, which owns input until the panel comes down and never reaches
/// Sim_MainTick — so while any is up the simulation is frozen and nothing else takes the keyboard, the
/// pointer or the stick.
/// </summary>
public sealed class ModalPanels {
	private readonly IPointerDevice _pointer;
	private readonly CockpitView _view;
	private readonly Mission _mission;
	private readonly SimWorld _world;
	private readonly SimulatorPreferences _preferences;
	private readonly bool _hasCockpit;

	private int _objectivesKeysDown;
	private int _preferencesKeysDown;
	private int _statusAlertKeysDown;
	private bool _pointerDown;
	private bool _rightButtonDown;

	// Which of the stick's eight the panels have already acted on and are waiting to see released —
	// Input_LatchButton's mask, kept here because the panels' presses never go through JoystickBindings.
	private byte _panelStickLatched;

	// Where the pointer was as each panel went up — panel+0x302, which AlertPanel_Enter copies out of the input
	// block — or null while that panel is down.
	private (float X, float Y)? _statusAlertReturn;
	private (float X, float Y)? _objectivesReturn;
	private (float X, float Y)? _preferencesReturn;
	private (float X, float Y)? _controlsReturn;

	private delegate bool FocusPointer(out int panelX, out int panelY);

	// Time_GetCoarseTicks for the panels' press flashes, in its 16 ms units. Retail's is wall time and keeps
	// running under a panel; GameAudio.CoarseTicks, the session's other copy, stops while one is up
	// (MessagesPaused), so a flash timed on it would never end.
	private double _panelTicks;
	private long PanelTicks => (long)_panelTicks;

	public ModalPanels(SimulatorStart start, IPointerDevice pointer, CockpitView view, bool hasCockpit,
			JoystickCapabilities stagedJoystick) {
		_pointer = pointer;
		_view = view;
		_mission = start.Mission;
		_world = start.World;
		_preferences = start.Preferences;
		_hasCockpit = hasCockpit;

		// The [F11] objectives panel. Built once with the mission's own block-13 text rather than on every
		// press, which is the one place this diverges from the original's own lifetime: it constructs the
		// panel, runs it and destroys it per press. Nothing in it changes during a mission.
		Objectives = ObjectivesPanel.Build(start.Content, _mission.ObjectiveTextRefs, _mission.TextAt);

		// The two modal alert panels. Only one can be up at a time -- they are modal in the original, and
		// each owns the input while it is -- so they share the pointer's press state.
		StatusAlert = StatusAlertPanel.Build(start.Content);

		// The [F12] preferences panel, showing the install's own data\prefs.cfg.
		Preferences = PreferencesPanel.Build(start.Content, start.Preferences,
			start.SoundAvailable, start.VoiceAvailable);

		// The CONTROLS panel the preferences panel raises. Which half of CTL_ALRT.STR it is, and which
		// twelve bytes of prefs.cfg it reads, both hang off whether the player's machine is the RAZOR. It is
		// built with whatever --joystick staged, which is nothing by default: GLFW does not publish a device's
		// shape until a frame after enumeration, so the real capabilities arrive later through
		// JoystickSource.Announce. Until they do every row greys itself, which is what retail also shows
		// for a stick it cannot enumerate.
		Controls = ControlsPanel.Build(start.Content, start.Preferences, start.PilotingRazor, stagedJoystick);
	}

	public StatusAlertPanel? StatusAlert { get; }
	public ObjectivesPanel? Objectives { get; }
	public PreferencesPanel? Preferences { get; }
	public ControlsPanel? Controls { get; }

	/// <summary>
	/// Whether a modal is up, and so holding the input. All four freeze the simulation behind them and all four
	/// take the keyboard's command keys; the stick goes with them.
	/// </summary>
	public bool AnyOpen =>
		StatusAlert is { IsOpen: true } || Objectives is { IsOpen: true }
		|| Preferences is { IsOpen: true } || Controls is { IsOpen: true };

	/// <summary>Advances the panels' own coarse clock by the frame's wall time. Called once a frame, before the panels read input.</summary>
	public void AdvanceClock(double deltaSeconds) => _panelTicks += deltaSeconds / GameAudio.CoarseTickSeconds;

	/// <summary>
	/// The tail of the open panel's loop pass: its press flashes serviced, and — for every panel but the
	/// preferences strip — the close a key asked for, held one frame while a flash is still queued so the button
	/// it pressed is drawn pressed before the panel goes (<c>AlertPanel_Present</c>, <c>00454ab0</c>). Only the
	/// panel on top runs: the controls panel's loop runs inside the preferences panel's, which does not reach
	/// its own present until the controls panel is down, and then focuses its DONE.
	/// </summary>
	public void Present() {
		if (StatusAlert is { IsOpen: true } presentAlert) {
			presentAlert.Present(PanelTicks);
		} else if (Objectives is { IsOpen: true } presentObjectives) {
			presentObjectives.Present(PanelTicks);
		} else if (Controls is { IsOpen: true } presentControls) {
			presentControls.Present(PanelTicks);
			if (!presentControls.IsOpen) {
				Preferences?.ReturnFromControls();
			}
		} else if (Preferences is { IsOpen: true } presentPreferences) {
			presentPreferences.Present(PanelTicks);
		}
	}

	/// <summary>Raises the panels the staging flags ask for, and says which panels are missing their text.</summary>
	public void OpenStaged(StagedStart staging) {
		if (staging.Objectives) {
			Objectives?.Open();
		}

		if (staging.Preferences) {
			Preferences?.Open();
		}

		if (staging.Controls) {
			Controls?.Open();
		}

		if (StatusAlert == null) {
			Console.Error.WriteLine("[Q] mission-status alert: GNL_ALRT.STR is missing; the panel will not open.");
		}
		if (Objectives == null) {
			Console.Error.WriteLine("[F11] objectives panel: OBJ_ALRT.STR is missing; the panel will not open.");
		}
		if (Preferences == null) {
			Console.Error.WriteLine("[F12] preferences panel: PRF_ALRT.STR is missing; the panel will not open.");
		}
		if (Controls == null) {
			Console.Error.WriteLine("CONTROLS panel: CTL_ALRT.STR is missing; the panel will not open.");
		}
	}

	/// <summary>
	/// The panels take the keyboard before anything else does. [Esc], which dismisses any of them, is also this
	/// host's menu-bar key, so they have to be asked first or two things would act on one keystroke. All are asked
	/// every frame — single <c>|</c>, not <c>||</c> — so each keeps its own key-edge state whether or not another
	/// claimed the keystroke. Returns whether any of them claimed it.
	/// </summary>
	/// <param name="keyboard">The keyboard, or null while the debug UI has it.</param>
	/// <param name="missionOver">Whether the mission is over, which stops [Q], [Ctrl+Q] and [P] raising a panel.</param>
	public bool ReadKeys(IKeyState? keyboard, bool missionOver) =>
		ReadStatusAlertKeys(keyboard, missionOver) | ReadObjectivesKeys(keyboard) | ReadPreferencesKeys(keyboard);

	/// <summary>
	/// The modal owns the pointer: the cockpit behind it takes no clicks. Reads the open panel's buttons, and for
	/// the CONTROLS panel the stick too.
	/// </summary>
	public void ReadPointer(int framebufferWidth, int framebufferHeight, IJoystickSource? joystick) {
		if (StatusAlert is { IsOpen: true } liveAlert) {
			// The panel's own placement, not a fixed one: the status alert and the pause panel are
			// different sizes and so centre to different origins.
			ReadPanelPointer(
				liveAlert.Place(framebufferWidth, framebufferHeight),
				liveAlert.PointerDown, (x, y, _) => liveAlert.PointerUp(x, y));
			ReadPanelJoystick(joystick, liveAlert.HandleStick);
		} else if (Objectives is { IsOpen: true } liveObjectives) {
			ReadPanelPointer(
				ObjectivesPanelLayout.Place(framebufferWidth, framebufferHeight),
				(x, y) => liveObjectives.PointerDown(x, y),
				(x, y, _) => liveObjectives.PointerUp(x, y));
			ReadPanelJoystick(joystick, liveObjectives.HandleStick);
		} else if (Controls is { IsOpen: true } liveControls) {
			// The controls panel is the one modal this engine draws that opens over another: it takes the
			// pointer while it is up and the preferences strip below it stays visible but inert.
			ReadPanelPointer(
				ControlsPanelLayout.Place(framebufferWidth, framebufferHeight),
				(x, y) => liveControls.PointerDown(x, y),
				(x, y, right) => liveControls.PointerUp(x, y, right));

			// And the stick itself, which on this one panel is an input device rather than a pair of
			// menu keys — see ControlsPanel.PressButtonRow.
			ReadControlsPanelJoystick(liveControls, joystick);
		} else if (Preferences is { IsOpen: true } livePreferences) {
			// This one is pinned to the bottom of the screen rather than centred, so its placement is
			// its own — see PreferencesPanelLayout.
			ReadPanelPointer(
				PreferencesPanelLayout.Place(framebufferWidth, framebufferHeight),
				(x, y) => livePreferences.PointerDown(x, y),
				(x, y, right) => livePreferences.PointerUp(x, y, right));
			ReadPanelJoystick(joystick, livePreferences.HandleStick);

			// CONTROLS raises the controls panel over this one, which is how the original reaches it
			// and the only way in.
			if (livePreferences.ControlsRequested) {
				livePreferences.ClearControlsRequest();

				// Re-read what the stick can do on the way in rather than trusting what it could at
				// startup: ControlsPanel_Run (00458650) asks Input_QueryCapabilities at the top of its own
				// loop, and that function rebuilds its eight bytes every call. So a stick plugged in
				// mid-mission lights the rows up, and one unplugged greys them.
				if (Controls is not null && joystick is { Capabilities.Present: true } liveStick) {
					Controls.Capabilities = liveStick.Capabilities;
				}

				// AlertPanel_Enter latches buttons 0-3 again as it raises it, so one still held from the press that
				// got here does not also act on the new panel.
				_panelStickLatched |= 0x0f;
				Controls?.Open();
			}
		}
	}

	/// <summary>
	/// Everything held while no panel is up counts as already acted on — <c>AlertPanel_Enter</c> latches buttons
	/// 0-3 as a panel goes up, and the simulation's own latch holds any other it has acted on — so a button being
	/// used for something else when a panel comes up does not also act on the panel. The mask then decays to the
	/// buttons actually held as a panel's stick read intersects it, which on the panel's first frame is exactly
	/// the set to swallow. It is the same priming JoystickBindings.Suspend does for the simulation's own latch,
	/// and for the same reason.
	/// </summary>
	public void PrimePanelStickLatch() {
		if (!AnyOpen) {
			_panelStickLatched = 0xff;
		}
	}

	/// <summary>
	/// The pointer the panels move. Every focus change puts it on the centre of the widget focused
	/// (<c>AlertPanel_SetFocus</c>, <c>00454c7c</c>), which every panel's loop does before its first pass; and a
	/// panel coming down puts it back where it was when the panel went up (<c>AlertPanel_Leave</c>,
	/// <c>004548ac</c>). The controls panel is taken first, so that coming down it puts the pointer back before
	/// the preferences panel under it focuses DONE. Call it once a frame, after <see cref="Present"/>.
	/// </summary>
	public void SyncPointer(int framebufferWidth, int framebufferHeight) {
		if (Controls != null) {
			SyncPanelPointer(Controls.IsOpen, ref _controlsReturn,
				ControlsPanelLayout.Place(framebufferWidth, framebufferHeight), Controls.TakeFocusPointer);
		}

		if (Preferences != null) {
			SyncPanelPointer(Preferences.IsOpen, ref _preferencesReturn,
				PreferencesPanelLayout.Place(framebufferWidth, framebufferHeight), Preferences.TakeFocusPointer);
		}

		if (Objectives != null) {
			SyncPanelPointer(Objectives.IsOpen, ref _objectivesReturn,
				ObjectivesPanelLayout.Place(framebufferWidth, framebufferHeight), Objectives.TakeFocusPointer);
		}

		if (StatusAlert != null) {
			SyncPanelPointer(StatusAlert.IsOpen, ref _statusAlertReturn,
				StatusAlert.Place(framebufferWidth, framebufferHeight), StatusAlert.TakeFocusPointer);
		}
	}

	private void SyncPanelPointer(bool open, ref (float X, float Y)? saved, AlertPanelLayout.Placement place,
			FocusPointer takeFocusPointer) {
		if (open && saved is null) {
			var (x, y, _) = _pointer.Pointer();
			saved = (x, y);
		} else if (!open && saved is { } back) {
			saved = null;
			_pointer.WarpPointer(back.X, back.Y);
		}

		if (open && takeFocusPointer(out int panelX, out int panelY)) {
			var (windowX, windowY) = place.ToWindow(panelX, panelY);
			_pointer.WarpPointer(windowX, windowY);
		}
	}

	/// <summary>
	/// Sim_MainTick's own arms: once the mission is decided the poll raises the status alert by itself, and once
	/// the player is down the death camera does. Latched on SimWorld.PendingMissionAlert by the tick that
	/// produced it.
	/// </summary>
	public void RaisePendingMissionAlert(bool missionOver) {
		if (_world is { PendingMissionAlert: not MissionStatus.None } alerted
			&& StatusAlert is { IsOpen: false } && Objectives is not { IsOpen: true }
			&& !missionOver) {
			var raised = alerted.PendingMissionAlert;
			alerted.PendingMissionAlert = MissionStatus.None;
			OpenStatusAlert(raised, alerted.Objectives);
		}
	}

	/// <summary>
	/// [Q] asks the mission how it stands and offers a way out of it -- Sim_DispatchCommand's scancode 0x10,
	/// which calls Mission_Status with its "just answer the question" flag set, raises the status alert for the
	/// answer, and takes the button pressed as the decision. The same call records the answer as the status
	/// already raised, so the poll will not raise that same status again as a change, and it disarms the poll's
	/// pending alert delay.
	///
	/// <para>Returns whether the panel was raised, so [Q] does not also reach anything below it.</para>
	/// </summary>
	public bool RaiseStatusAlertForQuit() {
		if (StatusAlert == null || _world is not { } quitWorld
			|| quitWorld.PlayerMech is not { } quitPlayer || quitWorld.Objectives is not { } quitObjectives) {
			return false;
		}

		var status = quitObjectives.QueryForPlayer(quitWorld, quitPlayer);
		return OpenStatusAlert(status, quitObjectives);
	}

	/// <summary>
	/// The panel for one status, with the outstanding objective's own failure text alongside it -- the
	/// substitution the constructor makes for status 5 and no other.
	/// </summary>
	public bool OpenStatusAlert(MissionStatus status, MissionObjectives objectives) {
		if (StatusAlert == null || !StatusAlertPanel.CanShow((int)status)) {
			return false;
		}

		var failure = objectives.Outstanding is { } outstanding
			? _mission.DescriptionOf(outstanding.Record)
			: null;

		if (!StatusAlert.Open((int)status, failure)) {
			return false;
		}

		_pointerDown = _pointer.Pointer().Buttons.HasFlag(CockpitMouseButtons.Left);
		return true;
	}

	/// <summary>
	/// What the player answered. Only the button the status's own table names ends the mission; every other
	/// answer just puts the panel away and carries on. Returns whether the answer ends the mission, and in
	/// <paramref name="quitGame"/> whether it leaves the game as well.
	/// </summary>
	public bool TakeMissionEnding(out bool quitGame) {
		quitGame = false;
		if (StatusAlert == null || !StatusAlert.TryTakeAnswer(out int button, out bool ends)) {
			return false;
		}

		if (!ends) {
			return false;
		}

		// Both endings leave the simulator. EXIT EARTHSIEGE? is not a mission outcome at all -- its QUIT
		// sets DAT_004d2582, the global quit flag that AlertPanel_Present also watches to tear down any
		// panel still up -- while a mission-ending answer goes up through Sim_PollPlayerInput and
		// Sim_MainTick as the tick's own return, which ends the main loop. Either way Sim_Shutdown then
		// writes the results and picks the exit code, after the window has gone (SimulatorHost's end).
		quitGame = StatusAlert.Status == StatusAlertPanel.ExitGameStatus;
		Console.WriteLine(quitGame
			? "Quitting EarthSiege 2."
			: $"Mission over — status {StatusAlert.Status} "
				+ $"({(MissionStatus)StatusAlert.Status}), answered '{StatusAlert.Buttons[button]}'.");
		return true;
	}

	/// <summary>
	/// Both panel families are modal, so they go over everything the cockpit drew — and over the external view
	/// too, where one stays up if the player switched views with it open. Only one can be up at a time.
	/// </summary>
	public void Draw(AlertPanelPainter painter, int width, int height, GpuTexture spriteTexture, HudSpriteSheet sprites) {
		if (StatusAlert is { IsOpen: true } openAlert) {
			painter.DrawStatusAlertPanel(width, height, spriteTexture, sprites, openAlert);
		} else if (Objectives is { IsOpen: true } openObjectives) {
			painter.DrawObjectivesPanel(width, height, spriteTexture, sprites, openObjectives);
		} else if (Preferences is { IsOpen: true } openPreferences) {
			painter.DrawPreferencesPanel(width, height, spriteTexture, sprites, openPreferences);

			// And the controls panel over it, in that order: the original's AlertPanel_Enter saves the
			// screen under the panel it raises, so the strip it was opened from is still there behind it.
			if (Controls is { IsOpen: true } openControls) {
				painter.DrawControlsPanel(width, height, spriteTexture, sprites, openControls);
			}
		}
	}

	// The three keys that raise a panel of the status-alert family, and the ones that answer one.
	//
	// [Q] asks how the mission stands, [Ctrl+Q] asks to leave the game, and [P] pauses — the manual's
	// own "Quit Mission", "Quit EarthSiege 2" and "Pause Mission", and Sim_DispatchCommand's commands
	// 0x10, 0x410 and 0x19. While a panel is up nothing else may act: AlertPanel_HandleEvent answers
	// its keys (PanelKey) and the panel's own loop owns the rest.
	//
	// Returns whether the panel claimed the keystroke.
	private bool ReadStatusAlertKeys(IKeyState? keyboard, bool missionOver) {
		if (StatusAlert == null || keyboard == null) {
			_statusAlertKeysDown = 0;
			return false;
		}

		bool ctrl = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
		bool q = Edge(Key.Q, 0);
		var key = PanelKey(keyboard, ref _statusAlertKeysDown);
		bool pause = Edge(Key.P, 4);

		if (StatusAlert.IsOpen) {
			return StatusAlert.HandleKey(key, PanelTicks) || q || pause;
		}

		// Not while another panel is up, which is already holding the input. The external view keeps both:
		// they are the dispatcher's own cases, not the widgets'.
		if (missionOver || !_hasCockpit
			|| Objectives is { IsOpen: true } || Preferences is { IsOpen: true }) {
			return false;
		}

		// [Ctrl+Q] is command 0x410 — the Ctrl bit over Q's own scancode — and asks to leave the game
		// rather than the mission. [Q] alone asks how the mission stands, and [P] pauses.
		if (q) {
			return ctrl
				? OpenStatusAlert((MissionStatus)StatusAlertPanel.ExitGameStatus, _world.Objectives)
				: RaiseStatusAlertForQuit();
		}

		// [Ctrl+P] is 0x419, a developer key, and never reaches the pause.
		if (pause && !ctrl) {
			return OpenStatusAlert((MissionStatus)StatusAlertPanel.PauseStatus, _world.Objectives);
		}

		return false;

		bool Edge(Key key, int bit) => KeyBit(keyboard, key, bit, ref _statusAlertKeysDown);
	}

	// [F11] puts the objectives panel up, and [Return] or [Esc] takes it down — scancode 0x57 through
	// CockpitWidgets_HandleCommand (00432bc8) on the way in and 0x1c/0x01 through the panel's own handler
	// (AlertPanel_HandleEvent, 00454e10) on the way out. Nothing in that handler answers 0x57, so [F11] does not
	// close the panel it opened; that is retail behaviour, not an oversight here.
	//
	// Returns whether the panel claimed the keystroke, so [Esc] does not also reach the debug panel.
	private bool ReadObjectivesKeys(IKeyState? keyboard) {
		if (Objectives == null || keyboard == null) {
			_objectivesKeysDown = 0;
			return false;
		}

		bool open = Edge(Key.F11, 0);
		var key = PanelKey(keyboard, ref _objectivesKeysDown);

		if (Objectives.IsOpen) {
			return Objectives.HandleKey(key, PanelTicks) || open;
		}

		// Only from inside the machine, and not while another panel is up: the command reaches the
		// panel through the cockpit's own widget tree, which is not on screen in the external view, and
		// one modal is already holding the input.
		if (open && _hasCockpit && !_view.ExternalViewActive
			&& StatusAlert is not { IsOpen: true } && Preferences is not { IsOpen: true }) {
			Objectives.Open();
			return true;
		}

		return false;

		bool Edge(Key key, int bit) => KeyBit(keyboard, key, bit, ref _objectivesKeysDown);
	}

	// [F12] puts the preferences panel up — scancode 0x58 through CockpitWidgets_HandleCommand
	// (PreferencesPanel_Raise, 0045cfd4) — and [Esc] takes it down: the panel's own handler presses the cancel
	// widget the constructor set to DONE. [Return] presses the focused widget (PreferencesPanel.Focus). As with [F11], nothing in the panel's loop answers 0x58, so a second
	// press does not close it.
	//
	// The original also reaches this panel on [Alt+P], command 0x219. Not bound here: [P] alone is the
	// pause panel, and this host has no Alt-modified command bank yet.
	//
	// Returns whether the panel claimed the keystroke, so [Esc] does not also reach the debug panel.
	private bool ReadPreferencesKeys(IKeyState? keyboard) {
		if (Preferences == null || keyboard == null) {
			_preferencesKeysDown = 0;
			return false;
		}

		bool open = Edge(Key.F12, 0);
		var key = PanelKey(keyboard, ref _preferencesKeysDown);

		// The controls panel is modal over this one: while it is up it answers the keys, and this panel
		// answers nothing. They are the same keys, on that panel's own focus and cancel widgets
		// (ControlsPanel.HandleKey).
		if (Controls is { IsOpen: true } liveControls) {
			return liveControls.HandleKey(key, PanelTicks) || open;
		}

		if (Preferences.IsOpen) {
			return Preferences.HandleKey(key, PanelTicks) || open;
		}

		// Not while another modal is up. Unlike the objectives panel it opens from the external view as
		// well: [F12] is the dispatcher's own case (0x58), not the widgets'.
		if (open && _hasCockpit
			&& StatusAlert is not { IsOpen: true } && Objectives is not { IsOpen: true }) {
			Preferences.Open();
			return true;
		}

		return false;

		bool Edge(Key key, int bit) => KeyBit(keyboard, key, bit, ref _preferencesKeysDown);
	}

	// The one code the panel's handler matches this frame, off bits 1-3 and 8-10 of the panel's held-key mask.
	// [Shift] is not tested: SimCommandMask strips it from the input block's command word the handler reads,
	// so [Shift+Return] is [Return] and [Shift+Tab] is [Tab]. With [Alt] or [Ctrl] down these are other codes,
	// which the handler does not answer.
	private static AlertPanelKey PanelKey(IKeyState keyboard, ref int keysDown) {
		// `|`, not `||`: every edge must be read every frame or the one that is skipped never updates its held
		// state, and the next press of it is swallowed.
		bool enter = KeyBit(keyboard, Key.Enter, 1, ref keysDown) | KeyBit(keyboard, Key.KeypadEnter, 2, ref keysDown);
		bool escape = KeyBit(keyboard, Key.Escape, 3, ref keysDown);
		bool next = KeyBit(keyboard, Key.Tab, 8, ref keysDown)
			| KeyBit(keyboard, Key.Keypad0, 9, ref keysDown) | KeyBit(keyboard, Key.Insert, 10, ref keysDown);

		if (!Unmodified(keyboard)) {
			return AlertPanelKey.None;
		}

		return enter ? AlertPanelKey.Return
			: escape ? AlertPanelKey.Escape
			: next ? AlertPanelKey.FocusNext
			: AlertPanelKey.None;
	}

	// One key's down edge, against its bit in a panel's held-key mask.
	private static bool KeyBit(IKeyState keyboard, Key key, int bit, ref int keysDown) {
		bool down = keyboard.IsKeyPressed(key);
		bool edge = down && (keysDown & (1 << bit)) == 0;
		keysDown = down
			? keysDown | (1 << bit)
			: keysDown & ~(1 << bit);
		return edge;
	}

	// A modal panel's buttons, pressed and released. Read straight off the device rather than through
	// CockpitInput: that queue is the cockpit's, and while a modal is up the cockpit is not taking
	// clicks at all. Press and release must both land on the same button for it to fire, which is
	// Widget_OnMouseUp's own re-hit-test.
	private void ReadPanelPointer(AlertPanelLayout.Placement place, Action<float, float> onDown,
			Action<float, float, bool> onUp) {
		var (pointerX, pointerY, buttons) = _pointer.Pointer();
		var (panelX, panelY) = place.ToPanel(pointerX, pointerY);

		// Both buttons press a widget; which one was released is what the click carries, since
		// CockpitMouse_ProcessQueue ORs the button bit into the click value on the release edge and the
		// panel reads bit 1 off it. Two of these panels step a setting backwards on the right button.
		bool right = buttons.HasFlag(CockpitMouseButtons.Right);
		bool down = buttons.HasFlag(CockpitMouseButtons.Left) || right;
		if (down && !_pointerDown) {
			_rightButtonDown = right;
			onDown(panelX, panelY);
		} else if (!down && _pointerDown) {
			onUp(panelX, panelY, _rightButtonDown);
			_rightButtonDown = false;
		}

		_pointerDown = down;
	}

	// The stick as AlertPanel_HandleEvent reads it, for every panel but the CONTROLS panel. The trigger is the
	// input build's +0x0d, the button on the walker's first FIRE row (JoystickBindings.TriggerScanRow) after the
	// latch; button 2 is +0x17, the second button's own byte, which that build zeroes when it is the trigger's.
	// Answering one latches it: Input_LatchButton(2, 1) for button 2, and (1, 1) for the trigger, which latches
	// button 0 wherever the trigger is bound — the same button on every binding the CONTROLS panel can set.
	private void ReadPanelJoystick(IJoystickSource? joystick, Func<bool, bool, long, bool> handleStick) {
		if (joystick is not { Capabilities.Present: true }) {
			return;
		}

		byte live = LiveStickButtons(joystick);
		int triggerRow = JoystickBindings.TriggerScanRow(_preferences);
		bool trigger = triggerRow >= 0 && (live & (1 << triggerRow)) != 0;
		bool button2 = triggerRow != 1 && (live & 0b10) != 0;
		if (handleStick(trigger, button2, PanelTicks)) {
			_panelStickLatched |= trigger ? (byte)0b01 : (byte)0b10;
		}
	}

	// The stick as the CONTROLS panel reads it, which is not how the rest of the session reads it: here a
	// button press picks the row it belongs to rather than firing whatever that row is bound to. The panel
	// owns what a press means (ControlsPanel.PressButtonRow); this owns only which press is new.
	//
	// JoystickReading.Buttons is the device's own eight, before JoystickBindings lifts the trigger out of
	// them, so BUTTON 1's row is reachable with the trigger — the thing ControlsPanel_HandleEvent restores
	// by hand before it reads the device block.
	//
	// The original breaks at the first set byte it finds, which is the lowest live row.
	private void ReadControlsPanelJoystick(ControlsPanel panel, IJoystickSource? joystick) {
		if (joystick is not { Capabilities.Present: true }) {
			return;
		}

		byte live = LiveStickButtons(joystick);
		for (int row = 0; row < JoystickCapabilities.MaxButtons; row++) {
			int bit = 1 << row;
			if ((live & bit) == 0) {
				continue;
			}

			_panelStickLatched |= (byte)bit;
			panel.PressButtonRow(row, PanelTicks);
			return;
		}
	}

	// The device's eight with the latch applied. Retail latches the button it acts on and the next input build
	// masks it to zero until it is let go, so a held button acts once and no more, and a panel sees a latched
	// button as not pressed at all.
	private byte LiveStickButtons(IJoystickSource joystick) {
		byte pressed = joystick.Read().Buttons;
		_panelStickLatched &= pressed;
		return (byte)(pressed & ~_panelStickLatched);
	}
}
