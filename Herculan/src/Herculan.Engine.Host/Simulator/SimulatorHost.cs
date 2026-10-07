using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Audio;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Host.Debugging;
using Herculan.Engine.Host.Settings;
using Herculan.Engine.Host.Simulator.Rendering;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Herculan.Engine.Scene;
using Herculan.Engine.Settings;
using Herculan.Engine.Sim;
using Herculan.Engine.World;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// The simulator's turn: one mission, from a shell launch, a demo tape or the command line, run in its own
/// window until it closes. This class is the composition and the frame's order — what runs before what each
/// update and each render — and nothing else; each step is the named component's.
/// </summary>
sealed class SimulatorHost : IDisposable {
	private readonly HostOptions _options;
	private readonly SimulatorStaging _staging;
	private readonly SimulatorStart _start;
	private readonly HostSession _session;
	private readonly MissionScene _scene;
	private readonly GameAudio _audio;
	private readonly SimulatorPreferences _preferences;
	private readonly EngineWindow _window;
	private readonly MissionOutcome _outcome = new();
	private readonly CockpitArt? _art;
	private readonly CockpitView _view;
	private readonly CockpitDisplays _displays;
	private readonly ModalPanels _panels;
	private readonly PilotControls _pilot;
	private readonly CockpitKeyboard _keyboard;
	private readonly CockpitCommands _commands;
	private readonly PlayerCockpitUpdate _cockpitUpdate;
	private readonly WindowKeys _windowKeys;
	private readonly SimulatorInput _input;
	private readonly TapePlayback _tape;
	private readonly TapeRecording _recording;
	private readonly SimulationStepper _stepper;
	private readonly DebugOptions _debugOptions;
	private readonly DebugProbes _debugProbes;
	private readonly DebugPanel _debugPanel;
	private readonly HostMenuBar _menuBar;
	private readonly DeveloperKeys _developerKeys;
	private readonly bool[] _systemButtonsShowing = new bool[SystemButtons.Count];
	private bool _suspended;

	// Built once the window has a GL context.
	private ScaledImGui? _imgui;
	private JoystickSource? _joystick;
	private SceneUploads? _uploads;
	private DrawFiling? _filing;
	private WorldDrawItems? _world;
	private TransientDrawItems? _transient;
	private WorldPassRenderer? _passes;
	private CockpitTextures? _textures;
	private DamageFlash? _flash;
	private CockpitRenderer? _cockpit;

	/// <summary>
	/// Runs one mission with -R&lt;n&gt;'s track select, and returns its exit code (Sim_Shutdown, 00461eec): 1
	/// when there was nothing to run.
	/// </summary>
	public static int Run(HostSession session, HostOptions options, SimulatorStaging staging, ShellLaunch? shellLaunch,
			bool demoTape, int trackSelect) {
		if (SimulatorStartup.Load(session, options, shellLaunch, demoTape) is not { } start) {
			return 1;
		}

		using var host = new SimulatorHost(session, options, staging, start, trackSelect);
		return host.RunWindow();
	}

	private SimulatorHost(HostSession session, HostOptions options, SimulatorStaging staging, SimulatorStart start,
			int trackSelect) {
		_session = session;
		_options = options;
		_staging = staging;
		_start = start;
		_scene = start.Scene;
		_audio = start.Audio;
		_preferences = start.Preferences;
		var mission = start.Mission;

		_art = LoadCockpitArt(start);
		_view = new CockpitView(_scene, _art, staging.Start);

		_window = new EngineWindow($"HERCULAN Engine — zone {mission.Header.ZoneIndex}",
			placement: session.WindowPlacement);
		if (session.SavePrintScreens) {
			PrintScreenFiles.Attach(_window, session);
		}

		_outcome.BindWindow(_window);

		var cockpitInput = new CockpitInput();
		_tape = new TapePlayback(start.TapePlayer, _preferences, () => _window.FramebufferSize, cockpitInput);
		_recording = new TapeRecording(start.TapeRecorder, _preferences, _audio);
		_input = new SimulatorInput(_window, _tape, cockpitInput);

		_panels = new ModalPanels(start, _input, _view, hasCockpit: _art != null, staging.Start.Joystick);
		_panels.OpenStaged(staging.Start);

		if (_art?.HeadsDown != null) {
			if (_art.HeadsDownLayout == null) {
				Console.Error.WriteLine("No Heads-Down widget block in this herc's .GAU — drawing its art only.");
			}
		} else if (_art != null) {
			Console.Error.WriteLine("No .HB1 for this herc — the Heads-Down Display is unavailable.");
		}

		_displays = new CockpitDisplays(start, _art, _view, staging);
		_view.BuildChain(staging.Start.External);

		// The debug panel, over its view options and its measurements; see DebugPanel for what it shows and
		// why it is ImGui rather than the game's own HUD font. Reachable only under --developer; without it the
		// options and measurements still exist, with the overlays off, since the renderer and stepper read them.
		_debugOptions = new DebugOptions(options.DeveloperMode, _view.SteadyEye);
		_debugProbes = new DebugProbes();
		_debugPanel = new DebugPanel(_debugOptions, _debugProbes);

		// Hidden until [Esc] first raises it — see WindowKeys.ReadMenuBarEscapeKey — since it is the only way
		// to reach its panels and every key from F1 to F12 is already taken. A mission has no shell turn to
		// restart, so Settings shows the folders greyed.
		_menuBar = new HostMenuBar(session.Localization, new TweaksMenu(TweakSettings.Current, session.Localization),
			new SettingsWindow(session, restartShell: null), options.DeveloperMode ? _debugPanel : null);

		// The -SPRUNKNOWN keys, and the Alt+S freeze a replay honours without them.
		_developerKeys = new DeveloperKeys(options.DeveloperMode);
		if (options.DeveloperMode) {
			Console.WriteLine("Developer keys on (docs/retail/key-bindings.md).");
		}

		staging.StageMachine(_view.PilotMech, _scene.World);

		_windowKeys = new WindowKeys(_window, _input, _tape, _menuBar, start.InstallRoot, start.Disc);
		_commands = new CockpitCommands(_displays, _view, _scene, _audio, _windowKeys, _tape);
		_pilot = new PilotControls(start, _view, _displays, _commands, _tape, _recording, _developerKeys, staging.Start);
		_keyboard = new CockpitKeyboard(_displays, _view, _commands, _scene, _audio);
		_cockpitUpdate = new PlayerCockpitUpdate(_displays, _view, _commands, _scene, _audio);
		_stepper = new SimulationStepper(_scene.World, _tape, _recording, _panels, _outcome, _developerKeys, _view,
			_debugProbes, _pilot, _input);
		_input.MouseQueued += (x, y, buttons, width, height) =>
			_recording.AddMouse(x, y, buttons, width, height, _panels.AnyOpen, _view.Piloting, _input.ImGuiWantsMouse);

		// Sim_InitMissionSession's music arm, which plays only when the flag the MUSIC handler left at startup
		// is up.
		_audio.StartMissionMusic(mission.Header, trackSelect);
		_displays.PowerUpCockpit(_audio, screenshotRun: options.ScreenshotPath != null);

		if (_view.PilotMech is { Thread: null } stillMech) {
			Console.Error.WriteLine($"{stillMech.Name} has no animation data, so it cannot walk.");
		}

		staging.BeginMission();

		_window.Load += OnLoad;
		_window.Update += OnUpdate;
		_window.Render += (_, gl) => OnRender(gl);
		_window.Closing += OnClosing;
		_window.View.FocusChanged += OnFocusChanged;
	}

	public void Dispose() => _window.Dispose();

	// MainWndProc's WM_KILLFOCUS calls Sim_Suspend (0045f0b8) and its WM_SETFOCUS Sim_Resume (0045f0ec): every sound
	// and both message ports stop and start again, and the flag between them holds Sim_Run's loop (see OnUpdate). Only
	// a change goes through, since a resume without its suspend would start the mission's track again from the top. A
	// --screenshot run is not held, so a capture never waits on the window having the focus.
	private void OnFocusChanged(bool focused) {
		if (focused != _suspended || _options.ScreenshotPath != null) {
			return;
		}

		_suspended = !focused;
		if (_suspended) {
			_audio.Suspend();
		} else {
			_audio.Resume();
		}
	}

	// The player's own cockpit canopy art + HUD, drawn as three simultaneous panels (front/left/right) rather
	// than the original's single keyboard-panned view — see docs/herculan/planning.md's Milestone 8 section and
	// docs/retail/formats/cockpit-views.md for why. Falls back to a single full-window 3D view when there's no
	// player.mec or its cockpit assets are missing (e.g. a raw script.dat with no accompanying player.mec).
	// The theater's palette is the live palette — all 256 slots — with only this herc's own 24-entry
	// cockpit colour scheme installed over slots 42-65. See CockpitPalette.
	// Every other machine in the mission goes in with it: F5 draws the *target's* paper doll, and a doll
	// is that machine's own .HBA frames placed by its own .PDG, so both have to be resident.
	// The squad's portrait banks go in with them: a comm box talks with dba\PILOT<n>.DBA, n being that
	// pilot's roster index over three, and which three are in the mission is only known here.
	private static CockpitArt? LoadCockpitArt(SimulatorStart start) {
		var scene = start.Scene;
		var mission = start.Mission;
		var cockpitArt = mission.Player?.TypeName is { } pilotHerc
			? CockpitArt.Load(start.Content, pilotHerc, scene.Theater.PaletteName,
				scene.World.Objects.OfType<MechObject>().Select(m => m.Name).Distinct(),
				start.SquadPlacements
					.Where(o => o.Placement.PilotIndex >= 0)
					.Select(o => PilotRoster.BankName(PilotRoster.PortraitOf(o.Placement.PilotIndex)))
					.Distinct(),
				scene.Theater.ImpactPaletteName)
			: null;
		if (cockpitArt != null) {
			if (cockpitArt.Sprites == null) {
				Console.Error.WriteLine("No HUD sprite banks could be loaded — canopy art only.");
			}
			if (cockpitArt.ColorSchemeIndex < 0) {
				Console.Error.WriteLine($"No cockpit colour scheme — {mission.Player!.TypeName}.DAT unreadable, so slots "
					+ $"{CockpitPalette.CockpitSchemeFirstSlot}+ keep the theater's filler colour.");
			}
			if (!cockpitArt.ClipRegionsLoaded) {
				Console.Error.WriteLine(
					"Viewport cutout fell back to inferring the hole from black pixels — at least one of the "
					+ "herc's .HD0/.HD2 region files could not be read.");
			}
		} else {
			Console.Error.WriteLine("No cockpit art available — drawing a single full-window 3D view.");
		}

		return cockpitArt;
	}

	private int RunWindow() {
		_window.Run();
		_session.WindowPlacement = _window.Placement;

		// Sim_Run's write-back (0045f3ee): option 6 against the live state, set through Prefs_SetOption and saved
		// alone through Prefs_SaveOption -- a read-modify-write of that one byte. See SimulatorStartup for the one
		// exception.
		byte fullScreenNow = (byte)(_window.FullScreen ? 1 : 0);
		if (_preferences[Prefs.DisplayModeOption] != fullScreenNow && !(_start.KeptWindowed && !_window.FullScreen)) {
			_preferences.Set(Prefs.DisplayModeOption, fullScreenNow);
			_preferences.Save(new[] { Prefs.DisplayModeOption });
		}

		// Sim_Shutdown (00461eec) runs however the mission ends: Mission_WriteResults (0042412c) writes results.dat and
		// the counters back over mission.var beside the mission, and the exit code says where ES.EXE goes next. The
		// original writes into the install's data\; a mission run straight from the install's own files writes
		// nothing there (a divergence from retail), and a --screenshot run ends the host (a unique HERCULAN feature).
		//
		// A window closed by the player is the original's WM_CLOSE, which dispatches [Ctrl+Q] (0x410) outside a
		// demo and raises DemoAbort in one. This window cannot refuse the close, so the EXIT EARTHSIEGE? panel is
		// not asked and its QUIT is taken as answered; a demo simply ends.
		if (!_outcome.Over && !_start.DemoTape && _options.ScreenshotPath == null) {
			_outcome.QuitGame = true;
		}

		if ((_start.ShellLaunch != null || _start.TapePlayer != null) && _scene.World.PlayerMech is { } endPlayer) {
			var (results, counters) = MissionResults.Write(_scene.World, endPlayer);
			string resultsFolder = Path.GetDirectoryName(_start.ScriptPath) ?? ".";
			File.WriteAllBytes(Path.Combine(resultsFolder, MissionResults.FileName), results);
			File.WriteAllBytes(Path.Combine(resultsFolder, MissionLoader.CountersFileName), counters);
			Console.WriteLine($"Wrote {MissionResults.FileName} ({results.Length} bytes, {_scene.World.Salvage.Count} salvaged "
				+ $"weapon(s)) and {MissionLoader.CountersFileName} to {resultsFolder}.");
		}

		return _options.ScreenshotPath != null ? 0 : MissionResults.ExitCode(_start.DemoTape, _outcome.QuitGame);
	}

	private void OnLoad(GL gl, IInputContext input) {
		_uploads = new SceneUploads(gl, _scene);
		_filing = new DrawFiling(_scene);
		_world = new WorldDrawItems(_scene, _uploads, _filing, TerrainTextureHandle());
		_transient = new TransientDrawItems(_scene, _uploads, _filing, _world);
		_passes = new WorldPassRenderer(gl, _scene, _view.Camera, _debugOptions, _world, _transient);
		_imgui = new ScaledImGui(gl, _window, input, _session.ImGuiFontPath);
		_input.ImGui = _imgui;
		_textures = new CockpitTextures(gl, _art, _displays.HddCommand, _displays.HddMapFlashRaster);
		_flash = new DamageFlash(_scene, _art, _passes.Scene, _textures);
		_cockpit = new CockpitRenderer(gl, _art, _view, _displays, _textures, _passes);

		_input.Attach(input);
		// Nothing is read off the device yet: GLFW publishes a stick's shape a frame late (see JoystickSource), so
		// what it can do is announced on the first frame that knows.
		_joystick = JoystickSource.Open(input, _start.DataDirectory, _options.ProbeJoystick);
		_pilot.Joystick = _joystick;

		// WinMain's toggle, after the pointer is known so it is confined and centred as Video_ToggleFullscreen's is.
		if (_start.StartFullScreen) {
			_windowKeys.ToggleFullScreen();
		}

		// No fixed-function face culling. DTS geometry is not reliably wound — the WinForms model viewer
		// reached the same conclusion and never culls by winding either — so culling by it would punch
		// holes in the mech rather than save fill rate. The format's own front/back choice, which does
		// leave faces undrawn, is made per poly in the scene shader instead: see MeshVertex.Side.
		gl.Disable(EnableCap.CullFace);
	}

	private void OnUpdate(double deltaSeconds) {
		_imgui?.Update((float)deltaSeconds);

		// A suspended Sim_Run loop sleeps and pumps messages instead of ticking, rendering or reading input, so the
		// mission stands still until the focus comes back; nothing accumulates meanwhile, so it carries on rather than
		// catching up. A modal panel's own loop never tests the flag, and runs on over a sim it already holds.
		if (_suspended && !_panels.AnyOpen) {
			return;
		}

		_joystick?.Announce(_pilot.Bindings, _panels.Controls, _options.WriteJoystickMap, _start.DataDirectory);
		_stepper.BeginFrame(deltaSeconds);

		// The modal panels take the keyboard before anything else does; [Esc], which dismisses any of them, is
		// also this host's menu-bar key. The menu bar is asked unconditionally regardless — like the panels, it
		// tracks its own key edge every frame — so a press held across the frame a retail panel consumes it
		// doesn't read as a fresh, unconsumed press the moment that panel closes.
		_panels.AdvanceClock(deltaSeconds);
		bool panelHandledKey = _panels.ReadKeys(_input.KeyboardCapturedByImGui ? null : _input.Keyboard, _outcome.Over);
		_windowKeys.ReadMenuBarEscapeKey(panelHandledKey, _view, hasCockpit: _art != null);
		_windowKeys.ReadManualKey(_displays.FlashCommHasKeyboard, _panels.AnyOpen);
		_windowKeys.ReadFullScreenKeys();

		// Everything below reads `controls` rather than the device itself: while the debug panel has keyboard
		// focus it is null, so piloting and camera keys go dead instead of the panel and the machine both
		// acting on the same keystroke.
		var controls = _input.KeyboardCapturedByImGui ? null : _input.Keyboard;
		var liveFreeKeys = _input.ImGuiHasKeyboard ? null : _input.LiveKeys;
		_view.ReadCameraKey(_tape.Playing ? liveFreeKeys : controls, _displays.FlashCommHasKeyboard);

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
		_pilot.Update(controls, _panels.AnyOpen, liveFreeKeys);
		var keyPointer = _input.Pointer();
		var keyFramebuffer = _window.FramebufferSize;
		_keyboard.Read(controls, _panels.AnyOpen, pilotInput: _view.Piloting || _tape.Playing,
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

		var framebuffer = _window.FramebufferSize;
		_view.Advance(deltaSeconds, _art, framebuffer.X, framebuffer.Y);

		// The objectives panel stops the clock the way every modal does: the original's modal loop polls
		// input, repaints its own widgets and presents, and never reaches the sim tick. The poll raises the
		// status alert by itself once the mission is decided — Sim_MainTick's own arm, latched on
		// SimWorld.PendingMissionAlert by the tick that produced it.
		_panels.RaisePendingMissionAlert(_outcome.Over);
		_staging.RaiseStatusAlert(_panels, _scene.World);
		if (_panels.TakeMissionEnding(out bool quitGame)) {
			_outcome.End(quitGame);
		}

		ApplyLivePreferences();

		// The same panels pause both message ports (AlertPanel_Enter, 00454630), so a line on screen when
		// one comes up is still there, with the rest of its time, when it goes.
		_audio.MessagesPaused = _panels.AnyOpen;
		_stepper.Advance(deltaSeconds);

		// The preferences panel's own loop turns the camera behind it, and the controls panel's, run from inside
		// it, does not.
		bool preferencesUp = _panels.Preferences is { IsOpen: true };
		_view.AdvancePanelOrbit(preferencesUp, preferencesUp && _panels.Controls is not { IsOpen: true },
			deltaSeconds);

		RefreshDrawItems();

		// Dropping out of the cockpit for the fly camera puts the palette back rather than leaving a
		// flash up with nothing ticking it.
		if (!_view.InMachine) {
			_flash?.Apply(false);
		}

		if (_view.InMachine) {
			_view.UpdateKickAndShake(deltaSeconds);
			_staging.StageHitShake(_view.Shake);
			_flash?.Apply(_view.Shake.FlashActive);
		}

		_view.PlaceCamera();

		// The listener is the camera, as it is in the original — so the external view hears the machine
		// from behind it rather than from inside it. Camera yaw runs opposite to a simulation heading
		// (see CockpitView.PlaceCamera), and the placement rules work in the simulation's, so it is negated
		// back here.
		var camera = _view.Camera;
		_audio.SetListener(camera.Position, -camera.Yaw & 0xffff);
		_audio.Update(TimeSpan.FromSeconds(deltaSeconds));

		_displays.UpdateSquadVideos();

		// What the debug panel reports about the walk — see DebugProbes.Sample for why it is measured
		// every frame rather than only while the panel is up.
		_debugProbes.Sample(pilotMech);

		_staging.AcquireTarget(pilotMech, _scene.Targeting);
		_cockpitUpdate.Update(deltaSeconds);
	}

	// The pointer's frame. A modal owns it: the cockpit behind it takes no clicks, and the queue is drained to
	// nothing so a click made while it was up cannot land on a console button afterwards. Otherwise the cockpit
	// takes it — but there is nothing to click while the cockpit is off screen, so the whole click path sits
	// out the external view rather than hit-testing a console the player cannot see, and likewise while the
	// pointer is over the debug panel, so a click on a checkbox is not also a click on the console behind it.
	private void ReadPointer(double deltaSeconds) {
		_panels.PrimePanelStickLatch();

		var framebuffer = _window.FramebufferSize;
		if (_panels.AnyOpen) {
			_panels.ReadPointer(framebuffer.X, framebuffer.Y, _pilot.Joystick);
			_panels.Present();
			_input.Cockpit.Drain(deltaSeconds, (_, _) => null);

			// And nothing behind it stays depressed: entering a panel calls Widget_ClearPressed (00452b94), which swaps the
			// panel's own clickable list in and clears Widget_PressedIndex to -1, dropping whatever the
			// cockpit had held when the panel was raised.
			_displays.Hud = _displays.Hud with { PressedWidget = null };
		} else if (_art != null && !_view.ExternalViewActive && (_tape.Playing || !_input.ImGuiWantsMouse)) {
			_commands.DrainClicks(_input.Cockpit, deltaSeconds, framebuffer.X, framebuffer.Y);
		}

		_panels.SyncPointer(framebuffer.X, framebuffer.Y);

		// WidgetRoot_ServicePressFlashes runs at the end of the cockpit's own per-frame widget pass, which
		// neither a modal panel's loop nor the external view reaches. A flash ending lets the button up even
		// under a held pointer.
		if (!_panels.AnyOpen && _art != null && !_view.ExternalViewActive) {
			foreach (var popped in _displays.PressFlashes.Service(_audio.CoarseTicks)) {
				_input.Cockpit.PopUp(popped);
			}

			_displays.Hud = _displays.Hud with {
				PressedWidget = _input.Cockpit.Depressed,
				FlashingWidgets = _displays.PressFlashes.Lit,
			};
		}

		// The system buttons show by the pointer's row, decided where Sim_RenderFrame ends, which no frame
		// reaches while a modal panel's own loop holds the screen: the pair stays as it was when the panel
		// went up. Nothing shows them in the external view; see docs/retail/formats/cockpit-input.md#open.
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
	// sees each change take effect under the panel, which is what the original shows them too.
	private void ApplyLivePreferences() {
		// TERRAIN TEXTURE: one byte and one nullable handle.
		if (_world is not null) {
			_world.Terrain.TextureHandle = TerrainTextureHandle();
		}

		// EFFECTS DETAIL: Sound_DetailSetting (004d1fc7) is prefs option 11, read where it is used -- by a
		// collapsing structure's smoke, a debris piece's burst and the sound throttle.
		byte effectsDetail = _preferences[Prefs.EffectsDetailOption];
		_scene.World.EffectsDetail = effectsDetail;
		if (_audio.Director is { } soundDirector) {
			soundDirector.DetailSetting = effectsDetail;
		}

		// And the two message channels' modes, which each port tests as it shows a line: COMPUTER MESSAGE
		// (ComputerMessageMode, 004d1fbf) in MessagePort_Show, PILOT MESSAGE (004d1fbe) in the pilot
		// port's paint. The voice half of PILOT MESSAGE is its handler's, registered at startup.
		_audio.Messages.Mode = (MessageChannelMode)_preferences[Prefs.ComputerMessageOption];
		if (_audio.Squad is { } squadChannel) {
			squadChannel.Port.Mode = (MessageChannelMode)_preferences[Prefs.PilotMessageOption];
		}
	}

	// The frame's draw items, after the frame's ticks: the kept ones moved, posed and gated, the transient ones
	// rebuilt, and every one filed for this frame's passes.
	private void RefreshDrawItems() {
		if (_world is null || _transient is null || _filing is null) {
			return;
		}

		int focalPixels = DetailMetrics.FocalPixels(_window.FramebufferSize.Y);
		_world.Refresh(_view.Camera, focalPixels, _preferences);
		_filing.BeginFrame();
		_transient.Refresh(_view.Camera, focalPixels, _preferences);
		_filing.Submit(_world.GroundLayer, _view.Piloting ? _view.Chain?.Camera.AttachedTo : null);
	}

	// The terrain's texture, or none when the player has TERRAIN TEXTURE off — prefs option 8, which in
	// the original reaches the draw as TerrainTexturingEnabled (004aab2c) and is tested per triangle by
	// Terrain_DrawCellQuad. The mesh here is built once at zone load and carries both treatments already:
	// every vertex holds the untextured fill's palette index beside its atlas UV (see TerrainMeshBuilder),
	// and the shader fills from it when no texture is bound. So the switch is the texture binding and nothing else, and it
	// applies on the frame it is thrown, as the original's does.
	private uint? TerrainTextureHandle() =>
		_preferences[Prefs.TerrainTextureOption] != 0
			? _uploads?.TerrainTexture?.Handle
			: null;

	private void OnRender(GL gl) {
		if (_passes == null || _cockpit == null || _textures == null || _flash == null) {
			return;
		}

		var size = _window.FramebufferSize;
		_passes.Scene.Clear();

		var pilotMech = _view.PilotMech;
		if (pilotMech != null) {
			_textures.RefreshShieldRings(pilotMech, _displays.PowerUp, _audio.CoarseTicks, _flash.Shown);
		}

		// The external view has no canopy over it — there is no cockpit to see from outside the machine.
		// Retail's is a band short of the screen, with its caption underneath; the tweak's mouse view uses
		// the whole window, as does the observer camera. The view behind the preferences panel is the
		// external view's too, in a rect of its own.
		_passes.LeavePlayerOut = _view.Piloting && !_view.ExternalViewActive && !_view.PanelOrbitUp;
		if (_view.PanelOrbitUp) {
			_cockpit.DrawPanelOrbitView(gl, size.X, size.Y);
		} else if (_cockpit.HasCockpit && !_view.ExternalViewActive) {
			_cockpit.DrawThreePanelCockpitView(gl, size.X, size.Y);
			_cockpit.DrawSystemButtons(size.X, size.Y, _systemButtonsShowing);
		} else if (_view.ExternalViewActive && !_view.MouseOutsideView) {
			_cockpit.DrawExternalView(gl, size.X, size.Y);
		} else {
			_passes.Draw(_view.Camera, 0, 0, size.X, size.Y);
		}

		// The panel goes over whatever view is up. The cockpit path above draws through three sub-window
		// viewports and leaves the last one set, so the full-window viewport is restored first —
		// otherwise the panel is squeezed into the right-hand cockpit panel's rectangle and mostly
		// scissored away, which is why it only ever appeared in the external view.
		gl.Viewport(0, 0, (uint)Math.Max(size.X, 1), (uint)Math.Max(size.Y, 1));

		if (_art?.Sprites is { } panelSprites && _textures.HudSprites != null) {
			_panels.Draw(_cockpit.AlertPanels, size.X, size.Y, _textures.HudSprites, panelSprites);
		}

		// The menu bar and its panels: hidden until [Esc] raises the bar (see WindowKeys.ReadMenuBarEscapeKey),
		// and never in a --screenshot capture, which sees no input to raise it.
		if (_options.ScreenshotPath == null) {
			_menuBar.Draw(_window.View.Native?.Win32?.Hwnd ?? 0);
		}

		_debugPanel.Draw(
			new DebugPanelContext(_view.Piloting, _view.ExternalViewActive, pilotMech, _scene.Targeting, _scene.World,
				_scene.PlayerObject?.Model?.Segments.Length ?? 0, _scene.World.Terrain),
			size.Y);

		_imgui?.Render();

		if (_staging.AfterFrame(_scene.World, _passes.DrawsBeams, _displays.SquadComm, _view.Shake)) {
			Screenshot.Capture(gl, size.X, size.Y, _options.ScreenshotPath!);
			_window.Close();
		}
	}

	private void OnClosing() {
		_recording.Finish();
		_audio.Dispose();
		_imgui?.Dispose();
		_passes?.Dispose();
		_cockpit?.Dispose();
		_textures?.Dispose();
		_uploads?.Dispose();
	}
}
