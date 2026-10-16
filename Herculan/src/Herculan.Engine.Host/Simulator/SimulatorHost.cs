using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io;
using Herculan.Engine.Audio;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Host.Debugging;
using Herculan.Engine.Host.Settings;
using Herculan.Engine.Host.Simulator.Rendering;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Settings;
using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.World;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// The simulator's turn: one mission, from a shell launch, a demo tape or the command line, run in the host's
/// window until it ends, behind <see cref="LoadingScreen"/> while it loads. This class is the composition, the render's
/// order, and the host's own steps between the phases of <see cref="SimulatorFrame"/>, which owns the update's order;
/// each step is the named component's.
/// </summary>
sealed class SimulatorHost : IDisposable {
	private readonly HostOptions _options;
	private readonly SimulatorStaging _staging;
	private readonly SimulatorStart _start;
	private readonly HostSession _session;
	private readonly MissionScene _scene;
	private readonly GameAudio _audio;
	private readonly MessagePorts _ports;
	private readonly SimulatorPreferences _preferences;
	private readonly EngineWindow _window;
	private readonly MissionOutcome _outcome = new();
	private readonly CockpitArt? _art;
	private readonly CockpitView _view;
	private readonly CockpitDisplays _displays;
	private readonly ModalPanels _panels;
	private readonly WindowKeys _windowKeys;
	private readonly SimulatorInput _input;
	private readonly TapeRecording _recording;
	private readonly SimulatorFrame _frame;
	private readonly DebugOptions _debugOptions;
	private readonly DebugProbes _debugProbes;
	private readonly DebugPanel _debugPanel;
	private readonly HostMenuBar _menuBar;
	private readonly DeveloperKeys _developerKeys;

	private readonly TapePlayback _tape;
	private readonly PauseMenu _pauseMenu;
	private readonly GamepadHostButtons _padButtons = new();

	// The left stick's click: FLASH COMM's six orders. The right stick's: the MFD's six screens, then the Heads-Down
	// Display's two pages. Each label radius keeps its ring's neighbouring labels apart.
	private readonly PadRadial _orderRadial = new(ControllerDevice.GamepadLeftStick, 0, MfdFlashCommScreen.RowCount, 200f);
	private readonly PadRadial _screenRadial = new(ControllerDevice.GamepadRightStick, 2, ScreenCount, 260f);
	private const int ScreenCount = 8;
	private const int MfdScreenCount = 6;

	// The mission's frame with the high-resolution tweak off: retail's resolution (docs/retail/launch-options.md,
	// "Display").
	private static readonly Vector2D<int> RetailFrameSize = new(640, 480);

	// Built once the window has a GL context.
	private IInputContext? _inputContext;
	private ScaledImGui? _imgui;
	private ImGuiGamepad? _padNav;
	private JoystickSource? _joystick;
	private SceneUploads? _uploads;
	private DrawFiling? _filing;
	private WorldDrawItems? _world;
	private TransientDrawItems? _transient;
	private WorldPassRenderer? _passes;
	private CockpitTextures? _textures;
	private DamageFlash? _flash;
	private CockpitRenderer? _cockpit;
	private FixedResolutionTarget? _retailFrame;

	/// <summary>
	/// Runs one mission in <paramref name="window"/> with -R&lt;n&gt;'s track select, and returns its exit code
	/// (Sim_Shutdown, 00461eec): 1 when there was nothing to run.
	/// </summary>
	public static int Run(HostSession session, HostWindow window, HostOptions options, SimulatorStaging staging,
			ShellLaunch? shellLaunch, bool demoTape, int trackSelect) {
		using var loading = new LoadingScreen(window);
		if (SimulatorStartup.Load(session, options, shellLaunch, demoTape, loading) is not { } start) {
			return 1;
		}

		using var host = new SimulatorHost(session, window, options, staging, start, trackSelect);
		window.PumpEvents();
		return host.RunWindow();
	}

	private SimulatorHost(HostSession session, HostWindow window, HostOptions options, SimulatorStaging staging,
			SimulatorStart start, int trackSelect) {
		_session = session;
		_options = options;
		_staging = staging;
		_start = start;
		_scene = start.Scene;
		_audio = start.Audio;
		_ports = start.Ports;
		_preferences = start.Preferences;
		var mission = start.Mission;

		_art = LoadCockpitArt(start);
		_view = new CockpitView(_scene, _art, staging.Start);

		_window = new EngineWindow(window, $"HERCULAN Engine — zone {mission.Header.ZoneIndex}");

		var cockpitInput = new CockpitInput();
		var tape = new TapePlayback(start.TapePlayer, _preferences, () => Fit().Frame, cockpitInput);
		_tape = tape;
		_recording = new TapeRecording(start.TapeRecorder, _preferences, _ports);
		_input = new SimulatorInput(_window, tape, cockpitInput, Fit);

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
		// options and measurements still exist, with the overlays off, since the renderer reads the one and the
		// host takes the other.
		_debugOptions = new DebugOptions(options.DeveloperMode, _view.SteadyEye);
		_debugProbes = new DebugProbes();
		_debugPanel = new DebugPanel(_debugOptions, _debugProbes);

		// Hidden until [Shift+Esc] first raises it — see SimulatorFrame's [Esc] — since it is the only way
		// to reach its panels and every key from F1 to F12, [Esc] and [Tab] among the rest, is already retail's. A
		// mission has no shell turn to restart, so Settings shows the folders greyed.
		_menuBar = new HostMenuBar(session.Localization, new TweaksMenu(TweakSettings.Current, session.Localization),
			new SettingsWindow(session, restartShell: null), new ControllerWindow(session.Localization, () => _start.DataDirectory),
			options.DeveloperMode ? _debugPanel : null);

		// The -SPRUNKNOWN keys, and the Alt+S freeze a replay honours without them.
		_developerKeys = new DeveloperKeys(options.DeveloperMode);
		if (options.DeveloperMode) {
			Console.WriteLine("Developer keys on (docs/retail/key-bindings.md).");
		}

		staging.StageMachine(_view.PilotMech, _scene.World);

		_windowKeys = new WindowKeys(_window, _input, tape, start.InstallRoot, start.Disc);
		_frame = new SimulatorFrame(start, _art, _view, _displays, _panels, tape, _recording, _developerKeys, staging,
			_outcome, _input, _windowKeys, _menuBar, () => Fit().Frame);
		_frame.Stepper.Ticked += () => _debugProbes.SampleBeams(_scene.World);
		_pauseMenu = new PauseMenu(session.Localization, _frame, _panels, _outcome, cockpitInput);
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
		_window.FocusChanged += OnFocusChanged;
	}

	public void Dispose() => _window.Dispose();

	// Where the mission's frame sits in the window: the whole window, or with the high-resolution tweak off retail's
	// resolution scaled to fit it (TweakSettingDefinitions.HighResolutionWidescreen). Read each time it is asked, so the
	// tweak takes effect on the frame it is set.
	private FrameFit Fit() {
		var window = _window.FramebufferSize;
		return TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.HighResolutionWidescreen)
			? FrameFit.Whole(window)
			: new FrameFit(RetailFrameSize, window);
	}

	// MainWndProc's WM_KILLFOCUS calls Sim_Suspend (0045f0b8) and its WM_SETFOCUS Sim_Resume (0045f0ec); see
	// SimulatorFrame.Suspend. A --screenshot run is not held, so a capture never waits on the window having the focus.
	private void OnFocusChanged(bool focused) {
		if (_options.ScreenshotPath != null) {
			return;
		}

		if (focused) {
			_frame.Resume();
		} else {
			_frame.Suspend();
		}
	}

	// The player's own cockpit canopy art + HUD, drawn as three simultaneous panels (front/left/right) rather
	// than the original's single keyboard-panned view — see docs/herculan/planning.md's Milestone 8 section and
	// docs/retail/simulation/cockpit-views.md for why. Falls back to a single full-window 3D view when there's no
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
			File.WriteAllBytes(CaseInsensitivePath.Combine(resultsFolder, MissionResults.FileName), results);
			File.WriteAllBytes(CaseInsensitivePath.Combine(resultsFolder, MissionLoader.CountersFileName), counters);
			Console.WriteLine($"Wrote {MissionResults.FileName} ({results.Length} bytes, {_scene.World.Mission.Salvage.Count} salvaged "
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
		_imgui = new ScaledImGui(gl, _window, input, _session.ImGuiFontPath, _session.ImGuiItalicFontPath);
		_menuBar.Tweaks.Fonts = _imgui;
		_padNav = new ImGuiGamepad(input);
		_imgui.GamepadFeed = _padNav.Feed;
		_input.ImGui = _imgui;
		_textures = new CockpitTextures(gl, _art, _displays.HddCommand, _displays.HddMapFlashRaster);
		_flash = new DamageFlash(_scene, _art, _passes.Scene, _textures);
		_cockpit = new CockpitRenderer(gl, _art, _view, _displays, _textures, _passes);
		_retailFrame = new FixedResolutionTarget(gl, RetailFrameSize);

		_input.Attach(input);
		_inputContext = input;
		// Nothing is read off the device yet: GLFW publishes a stick's shape a frame late (see JoystickSource), so
		// what it can do is announced on the first frame that knows.
		_joystick = JoystickSource.Open(input, _start.DataDirectory, _options.ProbeJoystick);
		_frame.Pilot.Joystick = _joystick;
		_menuBar.Controller.Attach(input, _joystick);

		// WinMain's toggle, after the pointer is known so it is confined and centred as Video_ToggleFullscreen's is.
		_windowKeys.StartFullScreen(_start.StartFullScreen);

		// No fixed-function face culling. DTS geometry is not reliably wound — the WinForms model viewer
		// reached the same conclusion and never culls by winding either — so culling by it would punch
		// holes in the mech rather than save fill rate. The format's own front/back choice, which does
		// leave faces undrawn, is made per poly in the scene shader instead: see MeshVertex.Side.
		gl.Disable(EnableCap.CullFace);

		// The load is done: Sim_InitMissionSession takes the loading screen down to black before the first frame.
		_window.PresentBlack();
	}

	private void OnUpdate(double deltaSeconds) {
		// A mission that ended last frame closes its window here, between frames rather than in the frame that
		// ended it: Close raises Closing at once, and OnClosing disposes what the rest of a frame still reads,
		// ImGui's context among it.
		if (_outcome.Over) {
			_window.Close();
			return;
		}

		if (_padNav != null) {
			_padNav.Suspended = _menuBar.Controller.Listening;
		}

		_imgui?.Update((float)deltaSeconds);

		// A gamepad's own CONTROLS settings stand in for prefs.cfg's while it is the stick being read.
		_preferences.ControlsOverride = _joystick?.ControlsOverride;

		// Before the hold, which the pause menu's Start ends.
		ReadGamepad();

		// The one owner of the mute: the mission's stick drives nothing while the pad works the menu bar and its
		// windows, the pause menu or the squad-order radial.
		if (_joystick != null) {
			_joystick.Muted = _menuBar.Up || _pauseMenu.Showing || _orderRadial.IsOpen || _screenRadial.IsOpen;
		}

		ReadRadarToggle();

		if (_frame.Held) {
			return;
		}

		_joystick?.Announce(_frame.Pilot.Bindings, _panels.Controls, _options.WriteJoystickMap, _start.DataDirectory);

		_input.LiveKeys?.AdvanceRepeat(deltaSeconds);
		_frame.BeginFrame(deltaSeconds);
		_windowKeys.ReadManualKey(_displays.FlashCommHasKeyboard, _panels.AnyOpen);
		_windowKeys.ReadFullScreenKeys();

		_frame.Update(deltaSeconds);
		ApplyTerrainTexture();
		RefreshDrawItems();

		_frame.Finish(deltaSeconds);

		// The damage flash is the cockpit's own, so dropping out of the cockpit for the fly camera puts the
		// palette back rather than leaving a flash up with nothing ticking it.
		_flash?.Apply(_view.InMachine && _view.Shake.FlashActive);

		// What the debug panel reports about the walk — see DebugProbes.Sample for why it is measured
		// every frame rather than only while the panel is up.
		_debugProbes.Sample(_view.PilotMech);
	}

	// A gamepad's own buttons, which a mission's stick never sees (ControllerDevice.Reserved). Back raises the menu bar or
	// backs out of it, as [Shift+Esc] does, and puts ImGui's navigation on the bar it leaves up; Start raises the pause
	// menu or resumes from it (GamepadHostButtons); and the sticks' clicks work the two radials. A radial's choice goes
	// in as its key, held in the live keyboard for this one frame before the frame reads its keys, so the cockpit takes
	// it through its own keys and a recording takes the key itself: an order as its [Alt] key on FLASH COMM, a screen
	// as its function key. Nothing is read while the window is out of focus.
	private void ReadGamepad() {
		_input.LiveKeys?.HeldByHost.Clear();
		if (_inputContext == null || _options.ScreenshotPath != null || _frame.Suspended) {
			_orderRadial.Cancel();
			_screenRadial.Cancel();
			return;
		}

		_padButtons.Update(_inputContext);
		if (_padButtons.BackPressed) {
			_menuBar.PadBack();
		}

		if (_padButtons.StartPressed) {
			_pauseMenu.Start();
		}

		var liveKeys = _input.LiveKeys;
		if (_orderRadial.Update(_inputContext, RadialAllowed() && !_screenRadial.IsOpen) is { } row && liveKeys != null
				&& CockpitKeyboard.FlashCommKey(row, _displays.FlashComm.VerbOf(row)) is { } orderKey) {
			liveKeys.HeldByHost.Add(Key.AltLeft);
			liveKeys.HeldByHost.Add(orderKey);
		}

		if (_screenRadial.Update(_inputContext, RadialAllowed() && !_orderRadial.IsOpen) is { } screen && liveKeys != null) {
			liveKeys.HeldByHost.Add(Key.F1 + screen);
		}
	}

	// The device map's radar toggle (docs/herculan/joystick-config.md, "The radar toggle"), held as [R] for the frame the
	// way a radial's choice goes in, so the cockpit's own [R] decides what it does and a recording takes the key. After
	// the mute is set for the frame, so it is quiet whenever the stick is; a replay's keys are the tape's.
	private void ReadRadarToggle() {
		if (_inputContext != null && _options.ScreenshotPath == null && !_frame.Suspended && !_tape.Playing
				&& _joystick?.RadarToggleHeld() == true) {
			_input.LiveKeys?.HeldByHost.Add(Key.R);
		}
	}

	// A radial opens only where its choice would be heard: in the machine with the cockpit up, since FLASH COMM's
	// [Alt] keys and the function keys go through the cockpit's widgets, which the external view takes away, and with
	// nothing else holding the input. A replay's keys are the tape's.
	private bool RadialAllowed() =>
		_input.LiveKeys != null && !_tape.Playing && !_outcome.Over && !_pauseMenu.IsOpen && !_panels.AnyOpen
		&& !_menuBar.Up && _view.Piloting && !_view.CockpitWidgetsOff;

	// An order's label on the radial: the verb FLASH COMM's row names, in STRINGS0.STR's own words.
	private string OrderLabel(int row) {
		int verb = _displays.FlashComm.VerbOf(row);
		return _art?.Strings?.Text(MfdLayout.OrderGroup, verb) is { Length: > 0 } text ? text : ((SquadCommand)verb).ToString();
	}

	// A screen's label on the radial: which display, its function key, and its title in STRINGS0.STR's own words, the
	// MFD's from its title bar and the Heads-Down Display's from its own.
	private string ScreenLabel(int screen) {
		var strings = _art?.Strings;
		string? title = screen < MfdScreenCount
			? MfdLayout.Title(strings, (MfdMode)screen)
			: strings?.Text(HddLayout.PageTitleGroup, screen - MfdScreenCount);
		return $"{(screen < MfdScreenCount ? "MFD" : "HDD")} F{screen + 1} {title}".TrimEnd();
	}

	// TERRAIN TEXTURE, the one live preference the draw reads rather than the simulation (see
	// SimulatorFrame.ApplyLivePreferences): one byte and one nullable handle.
	private void ApplyTerrainTexture() {
		if (_world is not null) {
			_world.Terrain.TextureHandle = TerrainTextureHandle();
		}
	}

	// The frame's draw items, after the frame's ticks: the kept ones moved, posed and gated, the transient ones
	// rebuilt, and every one filed for this frame's passes.
	private void RefreshDrawItems() {
		if (_world is null || _transient is null || _filing is null) {
			return;
		}

		int focalPixels = DetailMetrics.FocalPixels(Fit().Frame.Y);
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
		if (_passes == null || _cockpit == null || _textures == null || _flash == null || _retailFrame == null) {
			return;
		}

		// The mission draws into the frame, the window's own or retail's 640x480; the host's ImGui goes over
		// the window at its own resolution once the frame is in it.
		var fit = Fit();
		var size = fit.Frame;
		var window = fit.Window;
		if (fit.Scaled) {
			_retailFrame.Bind();
		}

		_passes.Scene.Clear();

		var pilotMech = _view.PilotMech;
		if (pilotMech != null) {
			_textures.RefreshShieldRings(pilotMech, _displays.PowerUp, _ports.CoarseTicks, _flash.Shown);
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
			_cockpit.DrawSystemButtons(size.X, size.Y, _frame.SystemButtonsShowing);
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

		if (fit.Scaled) {
			_retailFrame.Present(fit);
		}

		// The menu bar and its panels: hidden until [Shift+Esc] raises the bar (see SimulatorFrame), and never in a
		// --screenshot capture, which sees no input to raise it. Nor does either of the pad's own menus show in one.
		if (_options.ScreenshotPath == null) {
			_menuBar.Draw(_window.View.Native?.Win32?.Hwnd ?? 0, _window.FullScreen);
			_pauseMenu.Draw();
			_orderRadial.Draw(OrderLabel, _imgui!.LargeFont);
			_screenRadial.Draw(ScreenLabel, _imgui.LargeFont);
		}

		_debugPanel.Draw(
			new DebugPanelContext(_view.Piloting, _view.ExternalViewActive, pilotMech, _scene.Targeting, _scene.World,
				_scene.PlayerObject?.Model?.Segments.Length ?? 0, _scene.World.Terrain),
			window.Y);

		_imgui?.Render();

		if (_staging.AfterFrame(_scene.World, _passes.DrawsBeams, _displays.SquadComm, _view.Shake)) {
			Screenshot.Capture(gl, window.X, window.Y, _options.ScreenshotPath!);
			_window.Close();
		}
	}

	private void OnClosing() {
		_recording.Finish();
		_audio.Dispose();
		_imgui?.Dispose();
		_passes?.Dispose();
		_cockpit?.Dispose();
		_retailFrame?.Dispose();
		_textures?.Dispose();
		_uploads?.Dispose();
	}
}
