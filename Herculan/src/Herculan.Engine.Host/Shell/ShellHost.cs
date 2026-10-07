using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Content;
using Herculan.Engine.Host.Shell.Tabs;
using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using Herculan.Engine.Sim;
using Herculan.Engine.World;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// Runs the front end, which the host does unless <c>--mission</c> asks for one mission alone. The same thin-host arrangement the
/// mission loop uses (docs/herculan/planning.md, "Engine internal architecture"): everything here is
/// wiring, and every rule about what the shell looks like and where its widgets are lives in
/// <c>Herculan.Engine.Shell</c>. This class is the composition and the frame's order — what runs before what
/// each update and each render — and each step is the named component's. The window, its menu bar and the
/// polled buttons are game-agnostic (<see cref="FrontEndWindow"/>, <see cref="PolledButtons"/>); everything else
/// under this namespace is VSHELL's.
///
/// <para>It is a separate entry point rather than a mode of the mission loop because the two share
/// nothing: the shell mounts different archives, loads no zone, runs no simulation and needs no fixed
/// timestep. In the retail game they are two executables for the same reason
/// (docs/retail/shell/campaign-loop.md).</para>
///
/// <para>The pointer is polled once per update, and each change in its position or in either button
/// becomes one of the events VSHELL's queue carries, which <see cref="ShellPointer"/> delivers as
/// <c>EventQueue_Pump</c> (<c>00469ba4</c>) does: two changes inside one update arrive together, move first.</para>
/// </summary>
sealed class ShellHost : IDisposable {
	/// <summary>The movie <c>Shell_Main</c> (<c>00401525</c>) opens on the disc at startup to tell that the disc is there.</summary>
	public static readonly string DiscCheckMovie = Path.Combine(MovieHost.MovieFolderName, "PT1.AVI");

	/// <summary>
	/// <c>VIEW DEMO</c>'s exit code, <c>Shell_SetExitCode(5)</c>, which the launcher answers by starting the
	/// simulator with <c>-D</c>; the caller answers it with a demo tape.
	/// </summary>
	public const int DemoExitCode = 5;

	/// <summary>The state <c>ES.EXE</c>'s loop starts in, which runs the shell as a first start (docs/retail/command-line.md#the-loop).</summary>
	public const int StartupCode = 1;

	/// <summary>
	/// The Settings menu's exit code, this engine's and outside retail's 0-6: the shell closes so that the
	/// caller can run it again on the install and disc folders the menu changed.
	/// </summary>
	public const int SettingsRestartCode = -1;

	/// <summary>
	/// The simulator's code for a destroyed player in <c>MissionModeFlag</c>, which the shell takes into the
	/// debrief as it does 3. The simulator never returns it (<see cref="MissionResults.ExitCode"/>).
	/// </summary>
	public const int DebriefDestroyedCode = 4;

	private readonly int _returnCode;
	private readonly bool _fromMission;
	private readonly bool _startPractice;
	private readonly bool _startWindowed;

	private readonly ShellCanvas _canvas;
	private readonly GameInProgress _game;
	private readonly ShellScreen _screen;
	private readonly ShellAudio _audio;
	private readonly ShellPointer _pointer;
	private readonly WidgetEvents _widgets;
	private readonly ShellOutcome _outcome = new();
	private readonly FrontEndWindow _window;
	private readonly PolledButtons _buttons = new(Key.Escape, Key.Space);
	private readonly StartupScreen _startup;
	private readonly ShellMovies _movies;
	private readonly MissionTabScreens _mission;
	private readonly TabNavigation _navigation;
	private readonly CampaignScreens _loop;
	private readonly MainMenuPanels _menu;
	private readonly EditFields _fields;
	private readonly KeyboardRouting _keys;
	private readonly CanvasContent _content;

	/// <summary>Whether a simulator exit code brings the shell back up rather than ending the run: 3, 4 and 6.</summary>
	public static bool ReturnsToShell(int code) =>
		code is MissionResults.DebriefExitCode or DebriefDestroyedCode or MissionResults.DemoExitCode;

	/// <summary>
	/// Runs the front end until its window closes. Returns the exit code and, when <c>Rock &amp; Roll &gt;</c>,
	/// <c>Begin Mission</c>, <c>INSTANT ACTION</c> or the debrief's <c>REPLAY MISSION?</c> closed it, the mission
	/// to run — <c>Shell_SetExitCode(2)</c> (<c>0040876a</c>), the code the retail launcher answers by starting the simulator.
	/// <c>VIEW DEMO</c> closes it with <see cref="DemoExitCode"/> and no mission.
	///
	/// <para><paramref name="returnCode"/> is <c>-X</c>, the state the launcher starts the shell in
	/// (<c>Shell_StartupCode</c>, <c>0048227e</c>): <see cref="StartupCode"/> on a first start, or the code the simulator returned —
	/// <see cref="MissionResults.DebriefExitCode"/> into the debrief, <see cref="MissionResults.DemoExitCode"/>
	/// after a demo (<c>Shell_BuildScreensAndStart</c>, <c>004012b0</c>) — or this engine's own
	/// <see cref="SettingsRestartCode"/>, the Settings menu's restart. <paramref name="forcedMode"/> overrides
	/// the campaign/training mode the startup seeds from <c>prefs.cfg</c> option 42 (<c>Shell_InitGameState</c> (<c>0040e17e</c>)); the mode
	/// the shell ends in is returned for the next turn, which is how it survives a return from the simulator when
	/// <c>--no-write-prefs</c> keeps option 42 off the disk.</para>
	///
	/// <para>Retail ignores <c>WM_CLOSE</c> while the startup sequence runs (<c>Shell_CloseAllowed</c> (<c>0046c098</c>) clear); this
	/// window closes.</para>
	/// </summary>
	public static (int ExitCode, ShellLaunch? Launch, ShellCampaignMode? Mode) Run(HostSession session, string? paletteName, string? screenshotPath = null,
			ShellCampaignMode? forcedMode = null, int startTab = ShellScreen.MainMenuTab,
			int startBay = 0, bool startPractice = false, bool silentAudio = false, bool writePreferences = true,
			bool startWindowed = false, bool moviesEnabled = true, int returnCode = StartupCode) {
		var content = GameContent.MountShell(session.InstallRoot, session.Disc);
		Console.WriteLine($"Mounted archives: {string.Join(", ", content.MountedArchives)}");

		// Shell_Main (00401525) opens avi\pt1.avi through Path_UnderDriveCfg (0040d429) and, failing,
		// shows 'Please insert ESII CD and restart' and quits. This warns and carries on without the movies;
		// see KNOWN_ISSUES.md.
		if (!GameInstall.DiscFileExists(session.InstallRoot, session.Disc, DiscCheckMovie)) {
			Console.Error.WriteLine($"No {DiscCheckMovie} on the disc or in the install: the shell's movies will not play.");
		}

		// Before any game is loaded the mission tab would open on stage 1's map.
		string startPalette = ShellCanvas.PaletteFor(paletteName, startTab, ShellMissionView.Map, campaignStage: 1);
		if (ShellArt.Load(content, startPalette) is not { } art) {
			Console.Error.WriteLine(
				$"Could not load the shell's art from {string.Join(", ", content.MountedArchives)}.\n" +
				$"It needs a dpl\\{startPalette}.DPL palette and "
				+ $"dbm\\{ShellArt.BackdropName}.DBM.");
			return (1, null, forcedMode);
		}

		using var host = new ShellHost(session, content, art, paletteName, screenshotPath, forcedMode, startTab, startBay,
			startPractice, silentAudio, writePreferences, startWindowed, moviesEnabled, returnCode);
		return host.RunWindow();
	}

	private ShellHost(HostSession session, GameContent content, ShellArt art, string? paletteName, string? screenshotPath,
			ShellCampaignMode? forcedMode, int startTab, int startBay, bool startPractice, bool silentAudio, bool writePreferences,
			bool startWindowed, bool moviesEnabled, int returnCode) {
		_returnCode = returnCode;
		_fromMission = returnCode is MissionResults.DebriefExitCode or DebriefDestroyedCode;
		_startPractice = startPractice;
		_startWindowed = startWindowed;
		string installRoot = session.InstallRoot;
		var disc = session.Disc;
		Action repaint = Repaint;

		_canvas = new ShellCanvas(content, art, paletteName);
		if (art.Sprites == null) {
			Console.Error.WriteLine("No sprite banks or fonts could be loaded — backdrop only.");
		}

		// The save screen reads real files: sav\GAMEFILE.STR for the slot list and each GAME_?.SAV it
		// says is in use for that slot's summary. Both are loose files beside the VOL folder rather than
		// archive entries, so they are read from the install root and not through GameContent.
		var slots = ShellSaveSlots.Load(installRoot, art.Text);

		// SAVE is gated on there being a game in progress, which the startup clears.
		var saveScreen = new ShellSaveScreen(slots, canSave: false);
		var mainMenu = new ShellMainMenu(slots.ElementAtOrDefault(GameInProgress.CurrentGameSlot)?.InUse == true);
		if (slots.Count == 0) {
			Console.Error.WriteLine($"No {ShellSaveSlots.DirectoryFileName} in {ShellSaveSlots.Directory(installRoot)} — "
				+ "the save screen draws its furniture and no rows.");
		}

		// The repair screen works over a loaded game, which the original only has once one is started or
		// restored. Until the save screen's RESTORE replaces it, this opens the first slot the directory
		// marks in use — enough to put a real machine's damage on the screen, and stated rather than
		// hidden. It is not a game in progress, so nothing saves it.
		int loadedSlot = slots.ToList().FindIndex(s => s.InUse);
		var loadedGame = loadedSlot >= 0 ? ShellSaveSlots.LoadSave(installRoot, slots[loadedSlot].FileName) : null;

		// The preferences array: the practice screen's parameters, the preferences screen's options, the
		// repair mode and the build mode are all options of it.
		var preferences = SimulatorPreferences.Load(Path.Combine(installRoot, "DATA"));
		var options = preferences ?? SimulatorPreferences.Defaults();
		options.SaveEnabled = writePreferences;
		_game = new GameInProgress(installRoot, saveScreen, mainMenu, options, forcedMode, loadedGame, ShellHangar.From(loadedGame),
			ShellWorkingFiles.ForSlot(installRoot, loadedSlot));

		_canvas.Restage(_canvas.PaletteFor(startTab,
			MissionTabScreens.ViewFor(debriefUp: false, mapShown: false, _game.MissionInStage), _game.CampaignStage));

		_screen = ShellScreen.CreateFrame(_canvas.Art.Text, startTab, _game.Mode);
		_audio = new ShellAudio(content, options, silentAudio, SoundCfg.Load(GameInstall.SoundCfgPath(installRoot)));
		_pointer = new ShellPointer(_screen, () => _audio.Sound?.PlayPress());
		_widgets = new WidgetEvents(_pointer);
		var dialogs = new ShellDialogs();

		// The Settings menu's restart: the shell closes with SettingsRestartCode, which the caller answers by
		// running it again on the session's folders.
		_window = new FrontEndWindow(session, "HERCULAN Engine — shell", screenshotPath,
			() => _outcome.ExitCode = SettingsRestartCode);

		_startup = new StartupScreen(_canvas, content, _audio, repaint);
		if (!_fromMission && startTab == ShellScreen.MainMenuTab && !startPractice && screenshotPath == null) {
			_startup.Begin();
		}

		var missionScreen = new ShellMissionScreen(ShellMissionArt.Load(content));
		_movies = new ShellMovies(installRoot, disc, content, _canvas, _screen, missionScreen, _game, _window, _buttons,
			moviesEnabled, repaint);
		_mission = new MissionTabScreens(installRoot, content, _canvas, missionScreen, _movies, _game, _screen, dialogs, _outcome,
			_window, _widgets, repaint);
		var hangar = new HangarTabs(content, _game, dialogs, _screen, _widgets, startBay, repaint);
		_navigation = new TabNavigation(_screen, _canvas, _game, saveScreen, hangar, _mission, _audio, _widgets, repaint);
		_loop = new CampaignScreens(installRoot, content, _game, saveScreen, hangar, _mission, _navigation, _startup, _movies, _canvas,
			dialogs, _screen, _outcome, _window, _widgets, repaint);
		var save = new SaveRestoreTab(saveScreen, _game, _loop, _mission, _navigation, _screen, _widgets, repaint);
		_menu = new MainMenuPanels(installRoot, disc, content, _window, _screen, _widgets, _game, saveScreen, mainMenu, dialogs,
			_movies, _audio, _outcome, _loop, _navigation, repaint);
		_fields = new EditFields(_window, _audio, _movies, _pointer, _screen, saveScreen, _menu, _canvas, _widgets, repaint);
		_keys = new KeyboardRouting(_window, _movies, _mission, _pointer, _startup, _game, _menu, _fields, repaint);
		_content = new CanvasContent(_canvas, _screen, _pointer, _startup, dialogs, _movies, _menu, save, hangar, _mission);

		_navigation.EnterTab(_screen.SelectedTab);

		if (_canvas.Art.Text == null) {
			Console.Error.WriteLine("No estext.bin — the tabs draw their plates and no captions.");
		}
		if (paletteName != null) {
			Console.WriteLine($"Palette pinned to {_canvas.Art.PaletteName} on every tab.");
		}

		_window.Load += OnLoad;
		_window.FocusChanged += OnFocusChanged;
		_window.Update += OnUpdate;
		_window.Render += OnRender;
		_window.Closing += OnClosing;
	}

	public void Dispose() => _window.Dispose();

	// Every component's repaint, which reaches the canvas built last, over all of them.
	private void Repaint() => _content.Repaint();

	private (int ExitCode, ShellLaunch? Launch, ShellCampaignMode? Mode) RunWindow() {
		_window.Run();

		// The main loop's exit, whichever way it was left: QUIT, a launch or the window closing.
		_game.AutoSave();
		return (_outcome.ExitCode, _outcome.Launch, _game.Mode);
	}

	private void OnLoad(GL gl, IInputContext input) {
		_window.Attach(gl, input);
		_canvas.Attach(gl);
		_audio.Start(fadeIn: !_startup.Present || _returnCode != StartupCode);
		_movies.Attach(gl, _audio.Sound, _audio.Backend);

		// The startup (Shell_WinMain, 00406507) goes full screen when option 6 is set, before the shell's screens
		// are built. --shell-windowed keeps the window, which retail's -d does not.
		if (_game.Options[Prefs.DisplayModeOption] != 0 && !_startWindowed) {
			_window.ToggleFullScreen();
		}

		if (_window.Keyboard is { } keyboard) {
			keyboard.KeyDown += (_, key, _) => _keys.KeyDown(key);
			keyboard.KeyUp += (_, key, _) => _keys.KeyUp(key);
		}

		if (_startPractice) {
			_menu.OpenPractice();
		}

		// The startup's two intro movies (Shell_BuildScreensAndStart, 004012b0), played before the startup
		// sequence — except after a demo, whose -X6 goes straight to the sequence, after a mission, whose -X3
		// goes to the debrief, and after the Settings menu's restart, which goes to the sequence as a demo's does.
		if (_fromMission) {
			_loop.ReturnFromMission();
		} else if (_startup.Present && _returnCode is not (MissionResults.DemoExitCode or SettingsRestartCode)) {
			_movies.Queue.Enqueue(ShellMovieQueue.IntroPart1, ShellMovieQueue.FullRect);
			_movies.Queue.Enqueue(ShellMovieQueue.IntroPart2, ShellMovieQueue.FullRect);
			_movies.Start();
		}

		_content.Repaint();
	}

	// Shell_HasFocus (0046c094) is the window's HasFocus, which WM_SETFOCUS and WM_KILLFOCUS write; it holds the
	// main loop and the movie queue (docs/retail/shell/startup.md#the-main-loop), and starts set, as the image's 1 does,
	// until the first WM_KILLFOCUS.
	private void OnFocusChanged(bool focused) => _audio.FocusChanged(focused, _movies.Playing);

	private void OnUpdate(double delta) {
		_audio.Update();

		// The movie queue holds the shell while it plays out, as Movie_PlayQueue's loop does.
		if (_movies.Active) {
			_movies.Update(delta);
			return;
		}

		if (_movies.CreditsUp && !_audio.Fading) {
			_movies.EndCredits();
		}

		// Without the focus Shell_Main's loop skips everything in a pass but the message pump, so nothing
		// is delivered and no alarm ticks: the startup sequence, the caret and the widgets all wait. The
		// map's intro is its own loop once running (ShellMap_RunIntro, 0040146a), which does not test the flag.
		if (!_window.HasFocus && _mission.IntroUp() == null) {
			return;
		}

		// The startup sequence goes up once the intro movies and the fade in after them have run. It is
		// the only widget up and takes no mouse events, so a click meanwhile reaches nothing.
		if (_startup.Running && !_audio.Fading) {
			_startup.Advance();
			if (_window.Mouse is { } startupMouse) {
				_buttons.SpendButtons(startupMouse);
			}

			return;
		}

		if (_window.Mouse is not { } mouse) {
			return;
		}

		// A fade holds the shell until it is done: it pumps window messages but not the widget layer
		// (docs/retail/shell/screen-layout.md#sound). Retail queues the clicks made meanwhile and delivers them
		// after; this host polls, so the buttons' state is taken without delivering it, and an edge
		// made during the fade is spent, a divergence from retail.
		if (_audio.Fading) {
			_buttons.SpendAll(mouse, _window.Keyboard);
			return;
		}

		// The mission tab's arrows draw a lit face while pressed, so a change in what the pointer has lit
		// repaints that tab; the other screens draw no pressed state.
		var litBefore = _pointer.Lit;

		var framebuffer = _window.FramebufferSize;
		var (windowX, windowY) = _window.PointerIn(mouse, framebuffer);
		var layout = ShellScreenLayout.Create(framebuffer.X, framebuffer.Y);
		var (canvasX, canvasY) = layout.WindowToCanvas(windowX, windowY);

		// The map's intro runs as the original's does, in a loop of its own that takes nothing but a
		// button going down or Esc or Space, each of which skips to its closing zoom
		// (ShellMap_RunIntro (0040146a)). Every widget is out of reach until it ends — a divergence from
		// retail.
		if (_mission.IntroUp() is { } intro) {
			if (_buttons.SkipPressed(mouse, _window.Keyboard)) {
				intro.Skip();
			}

			intro.Advance(MissionTabScreens.MapClock());
			_content.Repaint();
			return;
		}

		// While the pointer is over the menu bar or one of its windows, the shell under them takes nothing:
		// the buttons' state is taken without delivering it, as during a fade.
		if (_window.ImGuiWantsMouse) {
			_buttons.SpendButtons(mouse);
		} else {
			_pointer.Move(_content.HitAt(canvasX, canvasY));
			_buttons.Deliver(mouse, _widgets.Deliver);
			if (_pointer.Lit != litBefore && _screen.SelectedTab == ShellScreen.MissionTab) {
				_content.Repaint();
			}
		}

		_fields.BlinkCaret();

		// The main loop's Movie_PlayQueue(1), once a pass after the widgets have had their events: what
		// the campaign map and the briefing queued starts here.
		_movies.Start();
	}

	private void OnRender(double delta, GL gl) {
		_window.BeginFrame(delta);

		// Black behind the canvas: the shell is a fixed 640x480 layout scaled to the window, so a
		// window that is not 4:3 has margin left over and the original has nothing to put in it.
		gl.ClearColor(0f, 0f, 0f, 1f);
		gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

		// QUIT's blank fills the whole client area with palette index 0, strip and all. The startup blanks
		// the same way just before it shows the sequence, and nothing is drawn before then but the intro
		// movies; CREDITS blanks round its movie.
		var framebuffer = _window.FramebufferSize;
		var layout = ShellScreenLayout.Create(framebuffer.X, framebuffer.Y);
		if (_outcome.Blanked || _movies.CreditsUp || _startup.BlankBeforeShow) {
			var blank = _canvas.Art.Palette.Colors.TryGetValue(0, out var entry) ? entry.GetColor() : default;
			gl.ClearColor(blank.R / 255f, blank.G / 255f, blank.B / 255f, 1f);
			gl.Clear(ClearBufferMask.ColorBufferBit);
			_movies.DrawMovie(layout);
			_window.DrawMenuBar();
			return;
		}

		_canvas.Draw(layout, _screen);
		_movies.DrawLocationPicture(layout);
		_movies.DrawMovie(layout);
		_window.DrawMenuBar();
		_window.FrameDrawn(gl);
	}

	private void OnClosing() {
		_window.DisposeImGui();
		_movies.Dispose();
		_canvas.Dispose();
		_audio.Dispose();
	}
}
