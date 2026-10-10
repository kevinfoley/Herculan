using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Engine.Scene;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// One simulator frame's update, in its order: the input, the ticks, the view and the cockpit. The host runs the
/// three phases in turn and puts its own steps between them — the window's keys after
/// <see cref="BeginFrame"/>, the draw items after <see cref="Update"/>, which read the view's camera before
/// <see cref="Finish"/> places it — so that what runs before what is this class's, and the same with or without a
/// window.
/// </summary>
public sealed class SimulatorFrame {
	private readonly SimulatorStart _start;
	private readonly MissionScene _scene;
	private readonly GameAudio _audio;
	private readonly MessagePorts _ports;
	private readonly CockpitArt? _art;
	private readonly CockpitView _view;
	private readonly CockpitDisplays _displays;
	private readonly ModalPanels _panels;
	private readonly TapePlayback _tape;
	private readonly DeveloperKeys _developerKeys;
	private readonly SimulatorStaging _staging;
	private readonly MissionOutcome _outcome;
	private readonly ISimulatorInput _input;
	private readonly IEscapeMenu? _escapeMenu;
	private readonly Func<Vector2D<int>> _framebufferSize;
	private readonly bool[] _systemButtonsShowing = new bool[SystemButtons.Count];

	private bool _escapeDown;
	private bool _tapeEscapeDown;

	/// <param name="systemButtons">What the two system buttons reach on the window.</param>
	/// <param name="escapeMenu">The host's own layer that [Esc] reaches before the game's views, if it has one.</param>
	/// <param name="framebufferSize">The window's framebuffer size, read where each step places something on it.</param>
	public SimulatorFrame(SimulatorStart start, CockpitArt? art, CockpitView view, CockpitDisplays displays,
			ModalPanels panels, TapePlayback tape, TapeRecording recording, DeveloperKeys developerKeys,
			SimulatorStaging staging, MissionOutcome outcome, ISimulatorInput input, ISystemButtonActions systemButtons,
			IEscapeMenu? escapeMenu, Func<Vector2D<int>> framebufferSize) {
		_start = start;
		_scene = start.Scene;
		_audio = start.Audio;
		_ports = start.Ports;
		_art = art;
		_view = view;
		_displays = displays;
		_panels = panels;
		_tape = tape;
		_developerKeys = developerKeys;
		_staging = staging;
		_outcome = outcome;
		_input = input;
		_escapeMenu = escapeMenu;
		_framebufferSize = framebufferSize;

		Commands = new CockpitCommands(displays, view, _scene, _audio, systemButtons, tape);
		Pilot = new PilotControls(start, view, displays, Commands, tape, recording, developerKeys, staging.Start);
		Keyboard = new CockpitKeyboard(displays, view, Commands, _scene, _audio);
		CockpitUpdate = new PlayerCockpitUpdate(displays, view, Commands, _scene, _ports);
		Stepper = new SimulationStepper(_scene.World, tape, recording, panels, outcome, developerKeys, view, Pilot, input);
	}

	public CockpitCommands Commands { get; }

	public PilotControls Pilot { get; }

	public CockpitKeyboard Keyboard { get; }

	public PlayerCockpitUpdate CockpitUpdate { get; }

	public SimulationStepper Stepper { get; }

	/// <summary>
	/// Which of the two system buttons show, by the pointer's row, decided where Sim_RenderFrame ends, which no
	/// frame reaches while a modal panel's own loop holds the screen: the pair stays as it was when the panel went
	/// up. Nothing shows them in the external view; see docs/retail/simulation/cockpit-input.md#open.
	/// </summary>
	public bool[] SystemButtonsShowing => _systemButtonsShowing;

	/// <summary>Whether the simulator is suspended, between <see cref="Suspend"/> and <see cref="Resume"/>.</summary>
	public bool Suspended { get; private set; }

	/// <summary>
	/// Whether the frame is held: a suspended Sim_Run loop sleeps and pumps messages instead of ticking, rendering
	/// or reading input, so the mission stands still until it resumes, and nothing accumulates meanwhile, so it
	/// carries on rather than catching up. A modal panel's own loop never tests the flag, and runs on over a sim it
	/// already holds. A held frame runs none of the three phases.
	/// </summary>
	public bool Held => Suspended && !_panels.AnyOpen;

	/// <summary>
	/// Sim_Suspend (0045f0b8): every sound and both message ports stop, and the frame is <see cref="Held"/>. Only a
	/// change goes through, here and in <see cref="Resume"/>, since a resume without its suspend would start the
	/// mission's track again from the top.
	/// </summary>
	public void Suspend() {
		if (Suspended) {
			return;
		}

		Suspended = true;
		_ports.Suspended = true;
		_audio.Suspend();
	}

	/// <summary>Sim_Resume (0045f0ec): the sounds and the message ports start again, and the frame runs on.</summary>
	public void Resume() {
		if (!Suspended) {
			return;
		}

		Suspended = false;
		_ports.Suspended = false;
		_audio.Resume();
	}

	/// <summary>
	/// The top of the frame: a replay's next frame going in, then the modal panels' clock and keys, which take the
	/// keyboard before anything else does, then [Esc].
	/// </summary>
	public void BeginFrame(double deltaSeconds) {
		Stepper.BeginFrame(deltaSeconds);
		_panels.AdvanceClock(deltaSeconds);
		bool panelHandledKey = _panels.ReadKeys(_input.KeyboardCapturedByImGui ? null : _input.Keyboard, _outcome.Over);
		ReadEscapeKey(panelHandledKey);
	}

	/// <summary>
	/// The input and the ticks: the camera, view and developer keys, the stick and the cockpit's keys and clicks,
	/// the panels raised by what they decided, the live preferences, then the simulation's ticks and the orbit
	/// behind the preferences panel.
	/// </summary>
	public void Update(double deltaSeconds) {
		// Everything below reads `controls` rather than the device itself: while the debug panel has keyboard
		// focus it is null, so piloting and camera keys go dead instead of the panel and the machine both
		// acting on the same keystroke.
		var controls = _input.KeyboardCapturedByImGui ? null : _input.Keyboard;
		var liveFreeKeys = _input.ImGuiHasKeyboard ? null : _input.LiveKeys;

		// The developer keys, which reach the dispatcher only while no modal panel holds the input, and the
		// view chain's own.
		var pilotMech = _view.PilotMech;
		if (pilotMech != null && _view.Chain != null && controls != null && !_panels.AnyOpen && !_outcome.Over) {
			_developerKeys.Read(controls, _scene.World, pilotMech, _view.Chain, _tape.Playing, deltaSeconds);
		}

		if (_view.Chain != null && controls != null && !_panels.AnyOpen && !_outcome.Over) {
			_view.ReadViewKeys(controls);
		}

		_view.ReadOrbitDrag(_input.Mouse, _input.ImGuiWantsMouse);
		Pilot.Update(controls, _panels.AnyOpen, liveFreeKeys);
		var keyPointer = _input.Pointer();
		var keyFramebuffer = _framebufferSize();
		Keyboard.Read(controls, _panels.AnyOpen, pilotInput: _view.Piloting || _tape.Playing,
			(keyPointer.X, keyPointer.Y), keyFramebuffer.X, keyFramebuffer.Y);

		ReadPointer(deltaSeconds);

		// Player_PerFrameCockpitUpdate's own copy: whatever the cockpit has selected becomes the
		// machine's mech+0x1a4, once a frame and before the sim ticks, so a weapon fired during the tick
		// sees this frame's target. The drop that precedes it is the cockpit update's own — see
		// TargetSelection.DropIfInvalid.
		if (_scene.Targeting is { } playerTargeting) {
			playerTargeting.DropIfInvalid(cockpitShown: !_view.ExternalViewActive);
			playerTargeting.PushToPilot();
		}

		_displays.HddCommand?.Update(TimeSpan.FromSeconds(deltaSeconds));

		// The page's paint copies the display's row onto the screen every time it runs, and here every
		// frame the page is up is a repaint.
		if (_displays.Hud.Mfd == MfdMode.FlashComm) {
			_displays.FlashComm.Sync();
		}

		var framebuffer = _framebufferSize();
		_view.Advance(deltaSeconds, _art, framebuffer.X, framebuffer.Y);

		// The objectives panel stops the clock the way every modal does: the original's modal loop polls
		// input, repaints its own widgets and presents, and never reaches the sim tick. The poll raises the
		// status alert by itself once the mission is decided — Sim_MainTick's own arm, latched on
		// MissionRuntime.PendingAlert by the tick that produced it.
		_panels.RaisePendingMissionAlert(_outcome.Over);
		_staging.RaiseStatusAlert(_panels, _scene.World);
		if (_panels.TakeMissionEnding(out bool quitGame)) {
			_outcome.End(quitGame);
		}

		ApplyLivePreferences();

		// The same panels pause both message ports (AlertPanel_Enter, 00454630), so a line on screen when
		// one comes up is still there, with the rest of its time, when it goes.
		_ports.Paused = _panels.AnyOpen;
		Stepper.Advance(deltaSeconds);

		// The preferences panel's own loop turns the camera behind it, and the controls panel's, run from inside
		// it, does not.
		bool preferencesUp = _panels.Preferences is { IsOpen: true };
		_view.AdvancePanelOrbit(preferencesUp, preferencesUp && _panels.Controls is not { IsOpen: true },
			deltaSeconds);
	}

	/// <summary>The view and the presentation: the kick and the shake, the camera, the sound, and the cockpit.</summary>
	public void Finish(double deltaSeconds) {
		if (_view.InMachine) {
			_view.UpdateKickAndShake(deltaSeconds);
			_staging.StageHitShake(_view.Shake);
		}

		_view.PlaceCamera();

		// The listener is the camera, as it is in the original — so the external view hears the machine
		// from behind it rather than from inside it. Camera yaw runs opposite to a simulation heading
		// (see CockpitView.PlaceCamera), and the placement rules work in the simulation's, so it is negated
		// back here.
		var camera = _view.Camera;
		_audio.SetListener(camera.Position, -camera.Yaw & 0xffff);
		_ports.Computer.ExternalView = _view.ExternalViewActive;
		_ports.Update(TimeSpan.FromSeconds(deltaSeconds));
		_audio.Update();

		_displays.UpdateSquadVideos();

		_staging.AcquireTarget(_view.PilotMech, _scene.Targeting);
		CockpitUpdate.Update(deltaSeconds);
	}

	// [Esc] backs out one layer at a time: the host's menu, if it has one up, else the external view, a glance or
	// the Heads-Down Display, else it raises the host's menu.
	private void ReadEscapeKey(bool consumedByPanel) {
		// During a replay the two halves of this key come apart: the tape's [Esc] is the game's and only
		// ever backs out of a view, and the live one keeps the host's menu.
		bool tapePlaying = _tape.Playing;
		var keyboard = _input.Keyboard;
		if (tapePlaying && keyboard != null) {
			bool tapeDown = keyboard.IsKeyPressed(Key.Escape);
			if (tapeDown && !_tapeEscapeDown && !consumedByPanel) {
				_view.BackOut(hasCockpit: _art != null);
			}

			_tapeEscapeDown = tapeDown;
			consumedByPanel = false;
		}

		var liveKeys = _input.LiveKeys;
		if (liveKeys == null) {
			return;
		}

		// Tracked every frame independent of consumedByPanel, exactly like the modal panels track their own
		// Escape edge regardless of who else claims it — otherwise a press that is still held on the frame a
		// retail panel lets go of Escape reads as a second, fresh press here.
		bool down = liveKeys.IsKeyPressed(Key.Escape);
		bool pressed = down && !_escapeDown;
		_escapeDown = down;

		if (!pressed || consumedByPanel || _escapeMenu?.BackOut() == true) {
			return;
		}

		if (tapePlaying || !_view.BackOut(hasCockpit: _art != null)) {
			_escapeMenu?.Show();
		}
	}

	// The pointer's frame. A modal owns it: the cockpit behind it takes no clicks, and the queue is drained to
	// nothing so a click made while it was up cannot land on a console button afterwards. Otherwise the cockpit
	// takes it — but there is nothing to click while the cockpit is off screen, so the whole click path sits
	// out the external view rather than hit-testing a console the player cannot see, and likewise while the
	// pointer is over the debug panel, so a click on a checkbox is not also a click on the console behind it.
	private void ReadPointer(double deltaSeconds) {
		_panels.PrimePanelStickLatch();

		var framebuffer = _framebufferSize();
		if (_panels.AnyOpen) {
			_panels.ReadPointer(framebuffer.X, framebuffer.Y, Pilot.Joystick);
			_panels.Present();
			_input.Cockpit.Drain(deltaSeconds, (_, _) => null);

			// And nothing behind it stays depressed: entering a panel calls Widget_ClearPressed (00452b94), which swaps the
			// panel's own clickable list in and clears Widget_PressedIndex to -1, dropping whatever the
			// cockpit had held when the panel was raised.
			_displays.Hud = _displays.Hud with { PressedWidget = null };
		} else if (_art != null && !_view.ExternalViewActive && (_tape.Playing || !_input.ImGuiWantsMouse)) {
			Commands.DrainClicks(_input.Cockpit, deltaSeconds, framebuffer.X, framebuffer.Y);
		}

		_panels.SyncPointer(framebuffer.X, framebuffer.Y);

		// WidgetRoot_ServicePressFlashes runs at the end of the cockpit's own per-frame widget pass, which
		// neither a modal panel's loop nor the external view reaches. A flash ending lets the button up even
		// under a held pointer.
		if (!_panels.AnyOpen && _art != null && !_view.ExternalViewActive) {
			foreach (var popped in _displays.PressFlashes.Service(_ports.CoarseTicks)) {
				_input.Cockpit.PopUp(popped);
			}

			_displays.Hud = _displays.Hud with {
				PressedWidget = _input.Cockpit.Depressed,
				FlashingWidgets = _displays.PressFlashes.Lit,
			};
		}

		if (!_panels.AnyOpen) {
			float pointerRow = _input.Pointer().Y;
			for (int i = 0; i < SystemButtons.Count; i++) {
				_systemButtonsShowing[i] = _art != null && !_view.ExternalViewActive
					&& SystemButtons.Showing((SystemButton)i, framebuffer.X, framebuffer.Y, pointerRow);
			}
		}
	}

	// The preferences read where they are used, every frame rather than watched for changes: the preferences
	// panel that steps them is drawn over a frozen scene that is still being rendered behind it, so the player
	// sees each change take effect under the panel, which is what the original shows them too. TERRAIN TEXTURE
	// is the draw's, and the host's.
	private void ApplyLivePreferences() {
		// EFFECTS DETAIL: Sound_DetailSetting (004d1fc7) is prefs option 11, read where it is used -- by a
		// collapsing structure's smoke, a debris piece's burst and the sound throttle.
		byte effectsDetail = _start.Preferences[Prefs.EffectsDetailOption];
		_scene.World.EffectsDetail = effectsDetail;
		if (_audio.Director is { } soundDirector) {
			soundDirector.DetailSetting = effectsDetail;
		}

		// And the two message channels' modes, which each port tests as it shows a line: COMPUTER MESSAGE
		// (ComputerMessageMode, 004d1fbf) in MessagePort_Show, PILOT MESSAGE (004d1fbe) in the pilot
		// port's paint. The voice half of PILOT MESSAGE is its handler's, registered at startup.
		_ports.Computer.Mode = (MessageChannelMode)_start.Preferences[Prefs.ComputerMessageOption];
		if (_ports.Squad is { } squadChannel) {
			squadChannel.Port.Mode = (MessageChannelMode)_start.Preferences[Prefs.PilotMessageOption];
		}
	}
}

/// <summary>
/// The host's own layer over the simulator that [Esc] reaches before the game's views do, and raises once the
/// game has no use for the key.
/// </summary>
public interface IEscapeMenu {
	/// <summary>Takes down whatever of the menu is up; false when nothing was.</summary>
	bool BackOut();

	/// <summary>Raises the menu.</summary>
	void Show();
}
