using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;
using Herculan.Engine.Sim;
using Herculan.Engine.World;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using HercWorks.Core.Data.File.Cfg;

namespace Herculan.Engine.Host;

/// <summary>
/// Runs the front end instead of a mission — <c>--shell</c>. The same thin-host arrangement the
/// mission loop uses (docs/engine/planning.md, "Engine internal architecture"): everything here is
/// wiring, and every rule about what the shell looks like and where its widgets are lives in
/// <c>Herculan.Engine.Shell</c>.
///
/// <para>It is a separate entry point rather than a mode of the mission loop because the two share
/// nothing: the shell mounts different archives, loads no zone, runs no simulation and needs no fixed
/// timestep. In the retail game they are two executables for the same reason
/// (docs/shell/campaign-loop.md).</para>
///
/// <para>The pointer is polled once per update, and each change in its position or in either button
/// becomes one of the events VSHELL's queue carries, which <see cref="ShellPointer"/> delivers as
/// <c>EventQueue_Pump</c> (<c>00469ba4</c>) does. Polling rather than queueing the device's own
/// events means two changes inside one update arrive together, move first; a press and release both
/// inside one update are lost.</para>
/// </summary>
/// <summary>
/// A mission handed over by the shell — <c>Rock &amp; Roll &gt;</c> or a training launch — or by
/// <see cref="MissionFileLaunch"/>: the <c>script.dat</c> the handoff was written beside,
/// and the folder the simulator's own settings are read from and written back to.
/// </summary>
sealed record ShellLaunch(string ScriptPath, string DataDirectory);

static class ShellHost {
	/// <summary>
	/// Frames to let pass before <c>--screenshot</c> fires. The shell has nothing to settle — no
	/// simulation, no streaming — but the window manager can hand back a stale or part-sized
	/// framebuffer for the first frame or two, so the capture waits the same short beat the mission
	/// host waits.
	/// </summary>
	private const int ScreenshotFrame = 5;

	/// <summary>
	/// Slot 10, the current-game autosave, whose in-use byte (<c>00482a19</c>) gates CONTINUE GAME.
	/// </summary>
	private const int CurrentGameSlot = 10;

	/// <summary>
	/// The folder the launch handoff is written to. The original writes it over the install's own
	/// <c>data</c> folder; this engine leaves the install's copies as they are and writes a scratch folder
	/// instead, which is this engine's choice.
	/// </summary>
	private static string HandoffDirectory => Path.Combine(Path.GetTempPath(), "herculan-launch");

	/// <summary>
	/// Where a new career's mission load writes the three working files the original writes into the
	/// install's <c>data</c> folder (<see cref="ShellWorkingFiles"/>). A scratch folder for the same reason
	/// as <see cref="HandoffDirectory"/>, which is this engine's choice.
	/// </summary>
	private static string CareerDirectory => Path.Combine(Path.GetTempPath(), "herculan-career");

	/// <summary>
	/// Runs the front end until its window closes. Returns the exit code and, when <c>Rock &amp; Roll &gt;</c>,
	/// <c>Begin Mission</c>, <c>INSTANT ACTION</c> or the debrief's <c>REPLAY MISSION?</c> closed it, the mission
	/// to run — <c>Shell_SetExitCode(2)</c> (<c>0040876a</c>), the code the retail launcher answers by starting the simulator.
	/// <c>VIEW DEMO</c> closes it with <see cref="DemoExitCode"/> and no mission.
	///
	/// <para><paramref name="returnCode"/> is <c>-X</c>, the state the launcher starts the shell in
	/// (<c>0048227e</c>): <see cref="StartupCode"/> on a first start, or the code the simulator returned —
	/// <see cref="MissionResults.DebriefExitCode"/> into the debrief, <see cref="MissionResults.DemoExitCode"/>
	/// after a demo (<c>Shell_BuildScreensAndStart</c>, <c>004012b0</c>). <paramref name="forcedMode"/> overrides
	/// the campaign/training mode the startup seeds from <c>prefs.cfg</c> option 42 (<c>Shell_InitGameState</c> (<c>0040e17e</c>)); the mode
	/// the shell ends in is returned for the next turn, which is how it survives a return from the simulator when
	/// <c>--no-write-prefs</c> keeps option 42 off the disk.</para>
	///
	/// <para>Retail ignores <c>WM_CLOSE</c> while the startup sequence runs (<c>Shell_CloseAllowed</c> (<c>0046c098</c>) clear); this
	/// window closes.</para>
	/// </summary>
	public static (int ExitCode, ShellLaunch? Launch, ShellCampaignMode? Mode) Run(string installRoot, string? paletteName, string? screenshotPath = null,
			ShellCampaignMode? forcedMode = null, int startTab = ShellScreen.MainMenuTab,
			int startBay = 0, bool startPractice = false, bool silentAudio = false, bool writePreferences = true,
			bool startWindowed = false, bool moviesEnabled = true, int returnCode = StartupCode) {
		bool fromMission = returnCode is MissionResults.DebriefExitCode or DebriefDestroyedCode;
		var content = GameContent.Mount(GameInstall.ArchiveDirectory(installRoot),
			[.. ShellArt.Archives, ShellSound.ArchiveName]);
		Console.WriteLine($"Mounted archives: {string.Join(", ", content.MountedArchives)}");

		// The mission tab's palette depends on the campaign stage, the career's own 1-5, and on which of its
		// views the tab opens: the map while the campaign map's first-show flag (DAT_004778aa) is
		// clear and the mission-within-stage counter is zero, the briefing otherwise
		// (TabHandler_Mission, 0043a6ca). All three come from the loaded game below.
		int campaignStage = 1;
		int missionInStage = 0;
		bool missionMapShown = false;

		// MissionScreenView (0048106c) at 4, which only the debrief writes, and the tab handler's own map
		// or briefing overwrites; with it, the debrief's text, its movie, and that movie's once-per-load flag
		// (DAT_004778ac), which a load and a new career clear.
		bool debriefUp = false;
		string? debriefText = null;
		short? debriefMovie = null;
		bool debriefMovieQueued = false;

		// Reassigned when a tab switches palette, since the art is decoded through one palette at load
		// rather than re-mapped per frame — see SwitchPalette.
		string startPalette = PaletteFor(startTab);
		if (ShellArt.Load(content, startPalette) is not { } loaded) {
			Console.Error.WriteLine(
				$"Could not load the shell's art from {GameInstall.ArchiveDirectory(installRoot)}.\n" +
				$"It needs {string.Join(" and ", ShellArt.Archives)}, a "
				+ $"dpl\\{startPalette}.DPL palette and "
				+ $"dbm\\{ShellArt.BackdropName}.DBM.");
			return (1, null, forcedMode);
		}

		var art = loaded;

		Console.WriteLine(
			$"Shell art loaded — {ShellArt.BackdropName} backdrop {art.Backdrop.Width}x{art.Backdrop.Height}, "
			+ $"palette {art.PaletteName}. "
			+ (art.Sprites is { } sheet
				? $"Banks and fonts: {string.Join(", ", sheet.BankNames)} in a "
				  + $"{sheet.Atlas.Width}x{sheet.Atlas.Height} atlas."
				: "No sprite banks or fonts could be loaded — backdrop only."));

		// The save screen reads real files: sav\GAMEFILE.STR for the slot list and each GAME_?.SAV it
		// says is in use for that slot's summary. Both are loose files beside the VOL folder rather than
		// archive entries, so they are read from the install root and not through GameContent.
		var slots = ShellSaveSlots.Load(installRoot, art.Text);

		// maybe_HasGameInProgress (0048260a), whether there is a game in progress to save. The startup clears it and a load sets
		// it; Game_SaveSlot writes nothing while it is clear, and SAVE is gated on it.
		bool gameInProgress = false;
		var saveScreen = new ShellSaveScreen(slots, canSave: gameInProgress);
		var mainMenu = new ShellMainMenu(slots.ElementAtOrDefault(CurrentGameSlot)?.InUse == true);
		var contentSurface = new ShellSurface();
		Console.WriteLine(slots.Count > 0
			? $"Save slots: {slots.Count(s => s.InUse)} of {slots.Count} in use — "
			  + string.Join(", ", slots.Take(ShellSaveScreen.RowCount).Select(s => s.Label.Trim()))
			: $"No {ShellSaveSlots.DirectoryFileName} in {ShellSaveSlots.Directory(installRoot)} — "
			  + "the save screen draws its furniture and no rows.");

		// The repair screen works over a loaded game, which the original only has once one is started or
		// restored. Until the save screen's RESTORE replaces it, this opens the first slot the directory
		// marks in use — enough to put a real machine's damage on the screen, and stated rather than
		// hidden. It is not a game in progress, so nothing saves it.
		int loadedSlot = slots.ToList().FindIndex(s => s.InUse);
		var loadedGame = loadedSlot >= 0 ? ShellSaveSlots.LoadSave(installRoot, slots[loadedSlot].FileName) : null;
		var workingFiles = ShellWorkingFiles.ForSlot(installRoot, loadedSlot);
		var hangar = ShellHangar.From(loadedGame);
		if (loadedGame != null) {
			campaignStage = loadedGame.CampaignStage;
			missionInStage = loadedGame.MissionInStage;
		}

		// The mission tab's briefing, objectives and intelligence report, assembled from the loaded slot's
		// career block and its own missn%d.str, and the screen that shows them, built once and kept.
		var missionTexts = ShellMissionTexts.Load(workingFiles, loadedGame);
		var missionScreen = new ShellMissionScreen(ShellMissionArt.Load(content));
		var missionViewUp = ShellMissionView.Map;

		// The map inside the briefing's Mission Map panel, built for the loaded slot's mission the first
		// time the briefing comes up after a load, as the original builds it when a mission is loaded,
		// and kept until the next load. Its intro runs the first time it is shown.
		var mapArt = ShellMapArt.Load(content);
		ShellMap? missionMap = null;

		// The art was loaded before the game, so a start on the mission tab took stage 1's palette.
		if (!string.Equals(PaletteFor(startTab), art.PaletteName, StringComparison.OrdinalIgnoreCase)
				&& ShellArt.Load(content, PaletteFor(startTab)) is { } staged) {
			art = staged;
		}

		// The startup sequence that first brings the main menu up, drawn through palette 1 as the startup
		// installs it before showing the sequence. A run staged on another screen, or for a screenshot,
		// starts without it — the staging flags are this engine's own. A return from a mission puts it up only
		// where the debrief sends the player back to the menu (ReturnFromMission).
		ShellStartupSequence? startup = null;
		var startupFrames = Array.Empty<ShellImage?>();
		if (!fromMission && startTab == ShellScreen.MainMenuTab && !startPractice && screenshotPath == null) {
			BeginStartupSequence();
		}

		// The movie queue, played out by the run built with the window. The briefing movie plays once per
		// load (DAT_004778ab), and CREDITS blanks the screen round its movie.
		var movieQueue = new ShellMovieQueue(moviesEnabled);
		ShellMovieRun? movies = null;
		bool briefingMovieQueued = false;
		bool creditsUp = false;

		// The location picture a map movie leaves up for two seconds, and the scope's black fill below the
		// strip that the lunar movie plays over.
		ShellImage? locationPicture = null;
		Herculan.Engine.Gl.GpuTexture? locationTexture = null;
		bool scopeFilled = false;

		// The END OF GAME alert CONTINUE GAME puts up over the menu when the game it loaded is over, and
		// INSTANT ACTION's InstantAction_Active (0047363c), which nothing clears.
		var endOfGame = new ShellEndOfGameDialog();
		bool instantActionSet = false;

		// REPLAY MISSION?, which the debrief puts up when the campaign ends.
		var replayDialog = new ShellReplayDialog();
		int exitCode = 0;

		var repairCosts = ShellRepairCosts.Load(content);
		var repairDiagrams = ShellRepairDiagrams.Load(content);
		// The armory's prices, names and stat lines, and the preferences byte that says whether weapons are
		// built by hand (prefs.cfg option 45), which gates CLEAR. The prices are also what the repair and
		// build screens deduct the save's build queue at. The screen is built on first entry and kept.
		var armoryCatalog = ShellArmoryCatalog.Load(content);
		// Option 44 is the repair mode the repair screen's readout names. Both are read where the original
		// reads them, on the tab's entry and the armory's clicks, since the preferences screen changes them.
		var preferences = SimulatorPreferences.Load(Path.Combine(installRoot, "DATA"));
		ShellArmoryScreen? armoryScreen = null;

		// The practice screen's five parameters are options 37-41 of the same array, stepped in memory;
		// with no prefs.cfg they start where the memset leaves them, at 0. The screen stands in for the
		// main menu while it is up, and is built on first use and kept.
		var shellOptions = preferences ?? SimulatorPreferences.Defaults();
		shellOptions.SaveEnabled = writePreferences;

		// CampaignModeFlag (0048260c), which the startup (Shell_InitGameState, 0040e17e) seeds from option 42 so the mode
		// survives a restart, and a return from a mission with it: it is what picks GAME_R or GAME_T as slot 10.
		var mode = forcedMode ?? (shellOptions[CampaignModeOption] == (byte)ShellCampaignMode.Campaign
			? ShellCampaignMode.Campaign : ShellCampaignMode.Training);
		ShellPracticeScreen? practiceScreen = null;
		bool practiceUp = false;

		// The preferences screen shows six options of the same array. It stands in for the main menu while
		// it is up, as the practice screen does, and is built on first use and kept.
		ShellPreferencesScreen? preferencesScreen = null;
		bool preferencesUp = false;

		// The registration screen START NEW GAME opens, built once at startup as the original's is, so the name
		// and the skill it holds survive a CANCEL. It stands in for the main menu while it is up.
		var registration = new ShellRegistrationScreen();
		bool registrationUp = false;

		// VSHELL's one generator, seeded once at startup, and the row-2 flag clear list, which the
		// original keeps from one mission load to the next.
		var shellRandom = ShellTrainingLaunch.StartupRandom();
		var clearList = new short[HercWorks.Core.Io.Transform.Common.MissionGenerator.ClearListLength];

		var repairScreen = new ShellRepairScreen(hangar, repairCosts, startBay, repairDiagrams) {
			QueuedKilograms = armoryCatalog.QueuedTotal(hangar),
			RepairMode = shellOptions[RepairOption],
		};
		Console.WriteLine($"Damage diagram layouts loaded for {repairDiagrams.LayoutCount} chassis.");

		// The squad panel's three-quarter view, which the crew tab shows (and WEAPONS and BUILD would), and
		// the crew screen's two portrait banks. The crew screen is built on first entry and kept, as the
		// original's widgets are; each entry re-runs its entry routine.
		var bayPictures = ShellBayPictures.Load(content);
		var crewPortraits = ShellCrewPortraits.Load(content);
		ShellCrewScreen? crewScreen = null;
		Console.WriteLine($"Bay picture layouts loaded for {bayPictures.LayoutCount} chassis.");

		// The build screen's chassis figures and prices, and the same body layouts the repair diagram
		// draws, which its blueprints reuse. Built on first entry and kept, as the crew screen is.
		var chassisCatalog = ShellBuildScreen.LoadCatalog(content);
		ShellBuildScreen? buildScreen = null;

		// The WARNING dialog both SCRAP buttons open, and its twin the armory's Scrap opens, each built once
		// as the original builds them at startup. At most one is ever up.
		var scrapDialog = ShellScrapDialog.Herc();
		var weaponScrapDialog = ShellScrapDialog.Weapons();

		// The dialog Rock & Roll refuses through, and the mission it hands over when it does not.
		var launchRefusal = new ShellLaunchRefusalDialog();
		ShellLaunch? launch = null;

		// The weapons screen's pictures and prose, and the screen itself, built on first entry and kept.
		var weaponsArt = ShellWeaponsArt.Load(content);
		ShellWeaponsScreen? weaponsScreen = null;
		Console.WriteLine($"Weapon pictures loaded for {weaponsArt.WeaponCount} weapons.");
		Console.WriteLine(repairCosts == null
			? $"No gam\\{ShellRepairCosts.ValuesResourceName} or gam\\{ShellRepairCosts.ChassisResourceName}"
			  + " — the repair screen draws its labels and no cost figures."
			: $"Repair costs loaded for {repairCosts.ChassisCount} chassis.");

		for (int bay = 0; bay < ShellHangar.BayCount; bay++) {
			if (hangar.Bay(bay) is not { } machine) {
				continue;
			}

			Console.WriteLine($"  bay {bay}: chassis type {machine.ChassisType}, "
				+ $"{machine.MountCapacity} hardpoints, {machine.BuildPercent}% built"
				+ (machine.IsFlightworthy ? string.Empty : ", not flightworthy")
				+ $", rebuild {repairCosts?.HercCost(machine) ?? 0} kg");
		}

		Console.WriteLine(repairScreen.SelectedBay >= 0
			? $"Repair opens on bay {repairScreen.SelectedBay}, "
			  + $"{repairScreen.AvailableKilograms} kg available."
			: "No built machine in any hangar bay — the repair screen draws empty rows.");

		// The shell's sound, created with the window since it needs the device. With --no-sound there is
		// none at all, as with the original's -s, which skips the sound manager's setup and so leaves
		// option 5 alone too.
		IAudioBackend? audio = null;
		ShellSound? sound = null;

		var screen = ShellScreen.CreateFrame(art.Text, startTab, mode);
		var pointer = new ShellPointer(screen, () => sound?.PlayPress());
		EnterTab(screen.SelectedTab);

		Console.WriteLine(art.Text != null
			? $"Tabs: {string.Join(", ", screen.Buttons.Where(b => b.Id < ShellLayout.TabCount && b.Caption != null).Select(b => b.Caption))}"
			: "No estext.bin — the tabs draw their plates and no captions.");
		Console.WriteLine(mode == ShellCampaignMode.Training
			? "Training mode: REPAIR, BUILD and ARMORY are gated off, as the strip refresh gates them."
			: "Campaign: every tab is live.");
		Console.WriteLine("Every tab has a screen behind it. The main menu comes up after the intro movies and its startup "
			+ "sequence; a click, Esc or Space skips a movie. On it, "
			+ "INSTANT ACTION flies the next of the three demo missions, CONTINUE GAME loads the current game and "
			+ "puts the tab strip up (or says why that game is over), VIEW DEMO plays a demo tape, CREDITS plays the "
			+ "credits, START NEW GAME asks for a pilot name and a skill and ACCEPT starts a campaign on the mission tab's "
			+ "map, and ONLINE MANUAL opens the manual in the web browser. SAVE/RESTORE opens the save screen, whose EXIT comes back to the menu, and PRACTICE MISSIONS "
			+ "opens the practice screen: click a mission to select it, a parameter's button to step it (the right "
			+ "button steps back), Main Menu to go back, and Begin Mission to fly the lit mission. PREFERENCES shows the "
			+ "preferences screen: click a checkbox to set it, Accept to keep and save the settings or Cancel to put "
			+ "them back, either returning to the menu. Click a save "
			+ "slot row, or a repair list row, a part of the damage diagram or a Squad Inventory row, to "
			+ "select it and the panels beside it follow; REPAIR lifts the selected part one level, REPAIR ALL "
			+ "rebuilds the machine, and CANCEL undoes both since the bay was selected. On BUILD, click a chassis to see its blueprint and "
			+ "figures, or a Squad Inventory row to pick the bay SCRAP and BUILD are gated on. On WEAPONS, click an "
			+ "inventory row to see the weapon, and on a missile rack a guidance button to see that kind. BUILD orders "
			+ "the chassis into an empty bay, and SCRAP, there or on REPAIR, asks before it scraps the bay's machine. "
			+ "On ARMORY, click a row to see the weapon and its figures; with weapons built by hand, click the lit row "
			+ "again to queue one, right-click it to take one off, or CLEAR to take them all off; SCRAP sells the lit "
			+ "weapon's whole stock. On CREW, click "
			+ "a row to select it, then a squad portrait to put that pilot in the row, a Squad Inventory row "
			+ "to give the row's pilot that bay, or CLEAR to empty the row. MISSION shows the campaign map on the "
			+ "first visit of a stage's first mission and the briefing otherwise: the briefing's three text buttons "
			+ "switch the summary and the arrows beside it page through it, the six buttons beside the map move it, and "
			+ "Rock & Roll launches the mission once every machine going is fit and armed. The square button latches and "
			+ "shows the frame. The main menu and the save screen hide "
			+ "the strip, as the original's do: leave the save screen with EXIT, or RESTORE a slot to load it into the "
			+ "repair screen. Once a slot is restored, SAVE on a selected row lets you type its name and ACCEPT "
			+ "writes the game there; the game is also autosaved to GAME_R.SAV on a restore, on the MAIN MENU tab "
			+ "and on leaving the shell, and a practice mission's or INSTANT ACTION's career to GAME_T.SAV as the shell "
			+ "closes on it. QUIT on the main menu, or closing the window, quits.");
		Console.WriteLine(paletteName != null
			? $"Palette pinned to {art.PaletteName} on every tab."
			: "Each tab installs its own palette, as the original's do.");

		using var window = new EngineWindow("HERCULAN Engine — shell");

		ShellRenderer? renderer = null;
		GL? gl = null;
		IMouse? mouse = null;
		IKeyboard? keyboard = null;
		bool leftHeld = false;
		bool rightHeld = false;
		bool skipKeyHeld = false;

		// The edit field that had the focus last update, and when its blink alarm last fired: the alarm is
		// installed as a field takes the focus.
		ShellWidget? caretField = null;
		long caretClock = 0;

		// Set by QUIT, whose blank is the last thing the window shows.
		bool blanked = false;

		// Which button the event being delivered is, for the handlers that tell them apart: an armory
		// row's thunk calls one function on the left release and another on the right.
		var eventButton = ShellMouseButton.Left;
		int framesRendered = 0;

		window.Load += (loadedGl, input) => {
			gl = loadedGl;
			renderer = new ShellRenderer(loadedGl, art);
			mouse = input.Mice.Count > 0 ? input.Mice[0] : null;
			keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
			StartSound();
			movies = new ShellMovieRun(movieQueue, MovieHooks(), sound, audio);

			// The startup (Shell_WinMain, 00406507) goes full screen when option 6 is set, before the shell's screens
			// are built. --shell-windowed keeps the window, which retail's -d does not.
			if (shellOptions[DisplayModeOption] != 0 && !startWindowed) {
				ToggleFullScreen();
			}

			if (keyboard != null) {
				keyboard.KeyDown += (_, key, _) => {
					DisplayHotkey(key, released: false);
					WidgetKey(key, released: false);
				};
				keyboard.KeyUp += (_, key, _) => {
					DisplayHotkey(key, released: true);
					WidgetKey(key, released: true);
				};
			}

			if (startPractice) {
				OpenPractice();
			}

			// The startup's two intro movies (Shell_BuildScreensAndStart, 004012b0), played before the startup
			// sequence — except after a demo, whose -X6 goes straight to the sequence, and after a mission,
			// whose -X3 goes to the debrief.
			if (fromMission) {
				ReturnFromMission();
			} else if (startup != null && returnCode != MissionResults.DemoExitCode) {
				movieQueue.Enqueue(ShellMovieQueue.IntroPart1, ShellMovieQueue.FullRect);
				movieQueue.Enqueue(ShellMovieQueue.IntroPart2, ShellMovieQueue.FullRect);
				movies.Start();
			}

			RepaintContent();
		};

		// WM_SETFOCUS starts the sounds again, unless a movie is playing, and WM_KILLFOCUS stops them
		// (MainWndProc, 00404a2c). The stop reaches a movie's soundtrack too, the two sharing one backend
		// here where retail's MCI sound is not the sound manager's.
		window.View.FocusChanged += focused => {
			if (focused) {
				if (movies?.Playing != true) {
					sound?.Start();
				}
			} else {
				sound?.Stop();
			}
		};

		window.Update += delta => {
			sound?.Update();

			// The movie queue holds the shell while it plays out, as Movie_PlayQueue's loop does.
			if (movies is { Active: true }) {
				UpdateMovies(delta);
				return;
			}

			if (creditsUp && sound?.Fading != true) {
				EndCredits();
			}

			// The startup sequence goes up once the intro movies and the fade in after them have run. It is
			// the only widget up and takes no mouse events, so a click meanwhile reaches nothing.
			if (startup is { Done: false } && sound?.Fading != true) {
				AdvanceStartup();
				if (mouse != null) {
					leftHeld = mouse.IsButtonPressed(MouseButton.Left);
					rightHeld = mouse.IsButtonPressed(MouseButton.Right);
				}

				return;
			}

			if (mouse == null) {
				return;
			}

			// A fade holds the shell until it is done: it pumps window messages but not the widget layer
			// (docs/shell/screen-layout.md#sound). Retail queues the clicks made meanwhile and delivers them
			// after; this host polls, so the buttons' state is taken without delivering it, and an edge
			// made during the fade is spent.
			if (sound?.Fading == true) {
				leftHeld = mouse.IsButtonPressed(MouseButton.Left);
				rightHeld = mouse.IsButtonPressed(MouseButton.Right);
				skipKeyHeld = keyboard != null && (keyboard.IsKeyPressed(Key.Escape) || keyboard.IsKeyPressed(Key.Space));
				return;
			}

			// The mission tab's arrows draw a lit face while pressed, so a change in what the pointer has lit
			// repaints that tab; the other screens draw no pressed state.
			var litBefore = pointer.Lit;

			// The pointer reports window-client pixels while the canvas is placed in framebuffer pixels,
			// which differ on a scaled display — the same correction the mission host makes.
			var client = window.ClientSize;
			var framebuffer = window.FramebufferSize;
			float windowX = mouse.Position.X * framebuffer.X / Math.Max(client.X, 1);
			float windowY = mouse.Position.Y * framebuffer.Y / Math.Max(client.Y, 1);

			var layout = ShellScreenLayout.Create(framebuffer.X, framebuffer.Y);
			var (canvasX, canvasY) = layout.WindowToCanvas(windowX, windowY);

			// The map's intro runs as the original's does, in a loop of its own that takes nothing but a
			// button going down or Esc or Space, each of which skips to its closing zoom
			// (ShellMap_RunIntro (0040146a)). Every widget is out of reach until it ends — this engine's choice.
			if (MapIntroUp() is { } intro) {
				bool left = mouse.IsButtonPressed(MouseButton.Left);
				bool right = mouse.IsButtonPressed(MouseButton.Right);
				bool key = keyboard != null && (keyboard.IsKeyPressed(Key.Escape) || keyboard.IsKeyPressed(Key.Space));
				if ((left && !leftHeld) || (right && !rightHeld) || (key && !skipKeyHeld)) {
					intro.Skip();
				}

				leftHeld = left;
				rightHeld = right;
				skipKeyHeld = key;
				if (!intro.Advance(MapClock())) {
					Console.WriteLine("Mission map: the intro is over; the six buttons move the map.");
				}

				RepaintContent();
				return;
			}

			pointer.Move(HitAt(canvasX, canvasY));
			ButtonEdge(mouse.IsButtonPressed(MouseButton.Left), ref leftHeld, ShellMouseButton.Left);
			ButtonEdge(mouse.IsButtonPressed(MouseButton.Right), ref rightHeld, ShellMouseButton.Right);
			if (pointer.Lit != litBefore && screen.SelectedTab == ShellScreen.MissionTab) {
				RepaintContent();
			}

			BlinkCaret();

			// The main loop's Movie_PlayQueue(1), once a pass after the widgets have had their events: what
			// the campaign map and the briefing queued starts here.
			movies?.Start();
		};

		window.Render += (_, frameGl) => {
			// Black behind the canvas: the shell is a fixed 640x480 layout scaled to the window, so a
			// window that is not 4:3 has margin left over and the original has nothing to put in it.
			frameGl.ClearColor(0f, 0f, 0f, 1f);
			frameGl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

			// QUIT's blank fills the whole client area with palette index 0, strip and all. The startup blanks
			// the same way just before it shows the sequence, and nothing is drawn before then but the intro
			// movies; CREDITS blanks round its movie.
			var framebuffer = window.FramebufferSize;
			var layout = ShellScreenLayout.Create(framebuffer.X, framebuffer.Y);
			if (blanked || creditsUp || startup is { Done: false, IsUp: false }) {
				var blank = art.Palette.Colors.TryGetValue(0, out var entry) ? entry.GetColor() : default;
				frameGl.ClearColor(blank.R / 255f, blank.G / 255f, blank.B / 255f, 1f);
				frameGl.Clear(ClearBufferMask.ColorBufferBit);
				DrawMovie(layout);
				return;
			}

			renderer?.Draw(layout, screen);
			if (locationTexture != null && locationPicture != null) {
				renderer?.DrawTexture(layout, locationTexture, 0, 0, locationPicture.Width, locationPicture.Height);
			}

			DrawMovie(layout);

			framesRendered++;
			if (screenshotPath != null && framesRendered == ScreenshotFrame) {
				Screenshot.Capture(frameGl, framebuffer.X, framebuffer.Y, screenshotPath);
				window.Close();
			}
		};

		window.Closing += () => {
			// The movie and the location picture hold GL textures, released while the context is current.
			movies?.Dispose();
			locationTexture?.Dispose();
			locationTexture = null;
			renderer?.Dispose();
			renderer = null;
			sound?.Stop();
			sound = null;
			audio?.Dispose();
			audio = null;
		};

		window.Run();

		// The main loop's exit, whichever way it was left: QUIT, a launch or the window closing.
		AutoSave();
		return (exitCode, launch, mode);

		// One update of the startup sequence: shown on the first, then its alarm's ticks, the last of which
		// hides it and puts the menu up.
		void AdvanceStartup() {
			if (startup == null) {
				return;
			}

			long now = Environment.TickCount64;
			if (!startup.IsUp) {
				startup.Show(now);
			} else if (!startup.Advance(now, () => sound?.PlaySwitch())) {
				return;
			}

			renderer?.SetBackdropOverride(startup.Done ? null : startupFrames[startup.Frame]);
			if (startup.Done) {
				Console.WriteLine("Main menu.");
				RepaintContent();
			}
		}

		void Activate(int id) {
			if (id == ShellScreen.MenuButtonId) {
				Console.WriteLine("Menu button — no screen behind it yet.");
				return;
			}

			// Clicking the tab you are already on is a no-op in the original: every handler returns
			// early when CurrentTabIndex (0047581c) already holds its own index, before the teardown, the palette and
			// the click sound.
			if (id == screen.SelectedTab) {
				return;
			}

			// Tab 0's handler autosaves before it builds the menu; tab 1's writes where EXIT goes before it
			// enters the screen.
			if (id == ShellScreen.MainMenuTab) {
				AutoSave();
			} else if (id == ShellScreen.SaveTab) {
				saveScreen.ExitTarget = ShellSaveExitTarget.TabStrip;
			}

			// The teardown's mission arm, Mission_Leave (00444a05), takes the report texts down for good; and the
			// mission tab's own handler writes the map or the briefing over the debrief's view.
			if (screen.SelectedTab == ShellScreen.MissionTab) {
				missionScreen.Leave();
			}

			debriefUp = false;
			screen.SelectTab(id);
			Console.WriteLine($"Tab {id}"
				+ (screen.Button(id)?.Caption is { } caption ? $" ({caption})" : string.Empty)
				+ (HasScreen(id) ? "." : " — no screen behind it yet."));

			SwitchPalette(id);
			EnterTab(id);
			RepaintContent();

			// The handler's last act, after the screen is up (ShellSound_PlayTabClick, 0042ee89).
			sound?.PlayTabClick();
		}

		// The sound manager's setup (ShellSound_Init, 0042ec7c): the samples, the music track option 5 picks, and then
		// option 5 flipped, committed and all 54 options saved, so the next run plays the other track. The
		// startup then starts the music at volume 0 (Shell_BuildScreensAndStart's ShellSound_Start), and the movie queue
		// fades it in after the intro — or, with movies off, leaves it there. A run staged on another screen
		// has no intro, and fades it in here.
		void StartSound() {
			if (silentAudio) {
				return;
			}

			audio = OpenAlBackend.TryCreate(out string? failure) as IAudioBackend ?? new NullAudioBackend();
			if (failure != null) {
				Console.WriteLine($"Audio unavailable ({failure}) — the shell runs silent.");
			}

			sound = ShellSound.Load(content, audio, shellOptions);
			Console.WriteLine(sound.HasMusic
				? $"Shell music hmi\\{sound.MusicName}; option 5 flipped for the next run."
				: $"No hmi\\{sound.MusicName} in {ShellSound.ArchiveName} — the shell has no music.");
			shellOptions.Set(ShellSound.MusicTrackOption, (byte)(shellOptions[ShellSound.MusicTrackOption] ^ 1),
				apply: false);
			shellOptions.Commit();
			shellOptions.Save(Enumerable.Range(0, Prefs.Length).ToArray());

			sound.Start();
			if (startup == null || returnCode != StartupCode) {
				sound.FadeIn();
			}
		}

		// The startup sequence up, with its frames, where it has not been: a first start, and wherever a return
		// from the simulator goes back to the menu by way of it.
		void BeginStartupSequence() {
			startup = new ShellStartupSequence();
			startupFrames = ShellStartupSequence.FrameNames.Select(name => art.LoadBitmap(content, name)).ToArray();
		}

		// Shell_BuildScreensAndStart's -X3 and -X4 arm (004012b0): Game_LoadSlot(10) and then
		// Game_ProcessMissionResults (0040eae7) over the results.dat and mission.var the simulator left beside the
		// handoff, then wherever the debrief goes next (docs/shell/campaign-loop.md#where-the-debrief-goes-next).
		// A slot 10 not in use, or no results, cannot come from a mission this shell launched; it is reported,
		// and the menu comes up.
		void ReturnFromMission() {
			string resultsPath = Path.Combine(HandoffDirectory, MissionResults.FileName);
			string countersPath = Path.Combine(HandoffDirectory, MissionLoader.CountersFileName);
			if (!LoadSlot(CurrentGameSlot) || loadedGame == null || !File.Exists(resultsPath) || !File.Exists(countersPath)) {
				Console.WriteLine($"Back from the mission, but slot 10 or {resultsPath} could not be read — main menu.");
				BeginStartupSequence();
				return;
			}

			var result = ShellDebrief.Process(loadedGame, hangar, content, File.ReadAllBytes(countersPath),
				File.ReadAllBytes(resultsPath), mode == ShellCampaignMode.Campaign, shellOptions[RepairOption], ManualWeaponBuild(),
				bound => shellRandom.NextBelow(bound), out string? failure);
			if (result == null) {
				Console.WriteLine($"Debrief: {failure} Main menu.");
				BeginStartupSequence();
				return;
			}

			Console.WriteLine($"Debrief: {(result.Outcome != 0 ? "success" : "failure")}, {result.SalvageAwarded} kg salvage "
				+ $"and {result.SalvageItems} weapon(s) recovered, {result.MachinesScrapped} machine(s) scrapped, "
				+ $"{result.PilotsLost} pilot(s) lost; game state {result.State?.ToString() ?? "unchanged"}.");
			if (result.Report is { } report) {
				missionScreen.WriteReport(report, art.Text);
			}

			switch (result.State) {
				case ShellDebrief.CampaignOverState or ShellDebrief.ShellState:
					// ReplayDialog_Show(state) (0044ca57).
					replayDialog.Open(result.State.Value);
					Console.WriteLine("Replay mission? Yes flies it again from the autosave; No saves and goes to the main menu.");
					break;
				case ShellDebrief.CampaignWonState:
					// Game_SaveSlot(10), the two ending movies, palette 1 and the startup sequence.
					AutoSave();
					movieQueue.Enqueue(ShellMovieQueue.Victory, ShellMovieQueue.FullRect);
					movieQueue.Enqueue(ShellMovieQueue.Credits, ShellMovieQueue.FullRect);
					movies?.Start();
					InstallPalette(ShellPalette.ServiceBay);
					BeginStartupSequence();
					break;
				case ShellDebrief.NextMissionState:
					// MissionScreenView = 4, then Career_StartMissionLoad's Use Default: the next mission's load,
					// whose campaign end puts the frame up and the mission tab in that view.
					debriefUp = true;
					debriefText = result.Debrief is { } text ? ShellMissionTexts.AssembleDebrief(text) : null;
					debriefMovie = result.Debrief?.Movie;
					LoadNextCareerMission();
					break;
				default:
					// A training debrief leaves the state alone and shows the startup sequence.
					BeginStartupSequence();
					break;
			}
		}

		// Career_LoadCurrentMission's campaign load after a debrief, as StartCampaign runs it for a new career.
		void LoadNextCareerMission() {
			if (loadedGame == null) {
				return;
			}

			var game = loadedGame;
			if (ShellCampaignLaunch.LoadCareerMission(CareerDirectory, content, game, hangar, clearList,
					bound => shellRandom.NextBelow(bound), out string? failure) is not { } mission) {
				Console.WriteLine($"Next mission: {failure} Main menu.");
				debriefUp = false;
				BeginStartupSequence();
				return;
			}

			AdoptGame(game, hangar, ShellWorkingFiles.In(CareerDirectory));
			Console.WriteLine($"Next mission: {mission.MissionPath}, {mission.SquadPositions} squad position(s); "
				+ $"working files in {CareerDirectory}.");
			screen.ReturnToFrame(mode);
			ShowMissionView();
		}

		// A REPLAY MISSION? button. No (ReplayDialog_OnNo, 0044cbbd) saves slot 10, takes the dialog down and
		// shows the main menu. Yes (ReplayDialog_OnYes, 0044cb44) takes it down, loads slot 10 again, and ends
		// the shell on exit code 2 — so the simulator flies what the load's Career_LoadSlot copied in: the
		// slot's script.dat, mission.str and player.mec, beside the mission.var the simulator itself last wrote,
		// which no one rewrites.
		void ClickReplay(ShellReplayButton button) {
			replayDialog.Close();
			if (button == ShellReplayButton.No) {
				AutoSave();
				Console.WriteLine("Replay: no — main menu.");
				RepaintContent();
				return;
			}

			if (!LoadSlot(CurrentGameSlot)) {
				Console.WriteLine("Replay: slot 10 could not be read — main menu.");
				RepaintContent();
				return;
			}

			Directory.CreateDirectory(HandoffDirectory);
			var handoff = ShellWorkingFiles.In(HandoffDirectory);
			foreach (var (from, to) in new[] {
					(workingFiles.Script, handoff.Script), (workingFiles.Text, handoff.Text), (workingFiles.Player, handoff.Player) }) {
				if (from != null && to != null && File.Exists(from)) {
					File.Copy(from, to, overwrite: true);
				}
			}

			workingFiles = handoff;
			launch = new ShellLaunch(handoff.Script!, Path.Combine(installRoot, MissionLoader.DataFolderName));
			Console.WriteLine($"Replay: yes — slot 10's mission copied to {HandoffDirectory}; launching it.");
			window.Close();
		}

		// What a tab's entry does beyond showing it. The mission tab's map view sets the campaign map's
		// first-show flag (Mission_Show, 004441e3), so the next visit opens the briefing.
		void EnterTab(int id) {
			// The repair and build screens quote the pool net of the queue, which the armory tab changes.
			repairScreen.QueuedKilograms = armoryCatalog.QueuedTotal(hangar);

			if (id == ShellScreen.RepairTab) {
				repairScreen.RepairMode = shellOptions[RepairOption];
				repairScreen.Enter();
			} else if (id == ShellScreen.CrewTab) {
				EnterCrew();
			} else if (id == ShellScreen.WeaponsTab) {
				EnterWeapons();
			} else if (id == ShellScreen.BuildTab) {
				EnterBuild();
			} else if (id == ShellScreen.ArmoryTab) {
				EnterArmory();
			} else if (id == ShellScreen.MissionTab) {
				EnterMission();
			} else if (id == ShellScreen.SaveTab) {
				saveScreen.Enter();
			}
		}

		// The widget under a canvas point. The strip is drawn over the content and hit first; below it,
		// whichever tab is up.
		ShellHit? HitAt(float canvasX, float canvasY) {
			if (scrapDialog.IsOpen) {
				return scrapDialog.HitAt(canvasX, canvasY);
			}

			if (weaponScrapDialog.IsOpen) {
				return weaponScrapDialog.HitAt(canvasX, canvasY);
			}

			if (launchRefusal.IsOpen) {
				return launchRefusal.HitAt(canvasX, canvasY);
			}

			if (endOfGame.IsOpen) {
				return endOfGame.HitAt(canvasX, canvasY);
			}

			if (replayDialog.IsOpen) {
				return replayDialog.HitAt(canvasX, canvasY);
			}

			if (screen.HitAt(canvasX, canvasY) is { } strip) {
				return strip;
			}

			return screen.SelectedTab switch {
				ShellScreen.MainMenuTab when practiceUp => practiceScreen!.HitAt(canvasX, canvasY),
				ShellScreen.MainMenuTab when preferencesUp => preferencesScreen!.HitAt(canvasX, canvasY),
				ShellScreen.MainMenuTab when registrationUp => registration.HitAt(canvasX, canvasY),
				ShellScreen.MainMenuTab => mainMenu.HitAt(canvasX, canvasY),
				ShellScreen.SaveTab => saveScreen.HitAt(canvasX, canvasY),
				ShellScreen.RepairTab => ShellSquadPanel.HitAt(canvasX, canvasY)
					?? repairScreen.HitAt(canvasX, canvasY),
				ShellScreen.BuildTab when buildScreen != null => ShellSquadPanel.HitAt(canvasX, canvasY)
					?? buildScreen.HitAt(canvasX, canvasY),
				ShellScreen.WeaponsTab when weaponsScreen != null => ShellSquadPanel.HitAt(canvasX, canvasY)
					?? weaponsScreen.HitAt(canvasX, canvasY),
				ShellScreen.CrewTab when crewScreen != null => ShellSquadPanel.HitAt(canvasX, canvasY)
					?? ShellCrewScreen.HitAt(canvasX, canvasY),
				ShellScreen.ArmoryTab when armoryScreen != null => armoryScreen.HitAt(canvasX, canvasY),
				ShellScreen.MissionTab => missionScreen.HitAt(canvasX, canvasY),
				_ => null,
			};
		}

		// A button changing state since the last update, delivered as the press or release it is.
		void ButtonEdge(bool held, ref bool wasHeld, ShellMouseButton button) {
			if (held == wasHeld) {
				return;
			}

			wasHeld = held;
			eventButton = button;
			if (held) {
				pointer.Press(button, Fire);
			} else {
				pointer.Release(button, Fire);
			}
		}

		// Runs a widget's click handler — Window_DispatchCallback (0041f5d4) calling what the builder
		// passed the widget.
		void Fire(ShellWidget widget) {
			switch (widget.Kind) {
				case ShellWidgetKind.StripButton:
					Activate(widget.Index);
					break;
				case ShellWidgetKind.MainMenuButton:
					ClickMainMenuButton((ShellMainMenuButton)widget.Index);
					break;
				case ShellWidgetKind.PracticeRow:
					SelectPracticeRow(widget.Index);
					break;
				case ShellWidgetKind.PracticeButton:
					ClickPracticeButton((ShellPracticeButton)widget.Index);
					break;
				case ShellWidgetKind.PreferencesWidget:
					ClickPreferences((ShellPreferencesWidget)widget.Index);
					break;
				case ShellWidgetKind.SaveRow:
					SelectSaveSlot(widget.Index);
					break;
				case ShellWidgetKind.SaveButton:
					ClickSaveButton((ShellSaveButton)widget.Index);
					break;
				case ShellWidgetKind.RepairRow or ShellWidgetKind.RepairHotspot:
					SelectRepair(widget.Index, widget.Sub);
					break;
				case ShellWidgetKind.RepairButton when (ShellRepairButton)widget.Index == ShellRepairButton.Scrap:
					OpenScrapDialog(repairScreen.SelectedBay);
					break;
				case ShellWidgetKind.RepairButton:
					ClickRepairButton((ShellRepairButton)widget.Index);
					break;
				case ShellWidgetKind.SquadRow:
					ClickRoster(widget.Index);
					break;
				case ShellWidgetKind.BuildChassisRow:
					SelectChassis(widget.Index);
					break;
				case ShellWidgetKind.BuildButton:
					ClickBuildButton((ShellBuildButton)widget.Index);
					break;
				case ShellWidgetKind.WeaponsRow:
					SelectWeaponsRow(widget.Index);
					break;
				case ShellWidgetKind.WeaponsButton:
					ClickWeaponsButton((ShellWeaponsButton)widget.Index);
					break;
				case ShellWidgetKind.WeaponsHotspot:
					SelectHardpoint(widget.Index);
					break;
				case ShellWidgetKind.ArmoryRow:
					ClickArmoryRow(widget.Index);
					break;
				case ShellWidgetKind.ArmoryButton:
					ClickArmoryButton((ShellArmoryButton)widget.Index);
					break;
				case ShellWidgetKind.ScrapDialogButton:
					ClickScrapDialogButton((ShellScrapDialogButton)widget.Index);
					break;
				case ShellWidgetKind.MissionButton:
					ClickMissionButton((ShellMissionButton)widget.Index);
					break;
				case ShellWidgetKind.MissionArrow:
					ClickMissionArrow((ShellMissionArrow)widget.Index);
					break;
				case ShellWidgetKind.LaunchRefusalOkay:
					launchRefusal.Close();
					RepaintContent();
					break;
				case ShellWidgetKind.ReplayButton:
					ClickReplay((ShellReplayButton)widget.Index);
					break;
				case ShellWidgetKind.EndOfGameOkay:
					endOfGame.Close();
					RepaintContent();
					break;
				case ShellWidgetKind.RegistrationField:
					// Registration_OnNameEvent (0043bdee) acts on keys alone, which WidgetKey delivers; a press
					// only gives the field the focus.
					RepaintContent();
					break;
				case ShellWidgetKind.RegistrationButton:
					ClickRegistration((ShellRegistrationButton)widget.Index);
					break;
				default:
					ClickCrew(widget);
					break;
			}
		}

		// A main-menu button's handler.
		void ClickMainMenuButton(ShellMainMenuButton button) {
			switch (button) {
				case ShellMainMenuButton.OnlineManual:
					OpenOnlineManual();
					break;
				case ShellMainMenuButton.StartNewGame:
					StartNewGame();
					break;
				case ShellMainMenuButton.Credits:
					Credits();
					break;
				case ShellMainMenuButton.Quit:
					Quit();
					break;
				case ShellMainMenuButton.PracticeMissions:
					OpenPractice();
					break;
				case ShellMainMenuButton.Preferences:
					OpenPreferences();
					break;
				case ShellMainMenuButton.SaveRestore:
					OpenSaveRestore();
					break;
				case ShellMainMenuButton.InstantAction:
					InstantAction();
					break;
				case ShellMainMenuButton.ContinueGame:
					ContinueGame();
					break;
				case ShellMainMenuButton.ViewDemo:
					ViewDemo();
					break;
				default:
					Console.WriteLine($"{button} — the button is live and its action is not ported yet.");
					break;
			}
		}

		// ONLINE MANUAL, 004317ea: out of full screen with option 6 written to match, the options committed
		// and all 54 saved, the shell's sound stopped, then the help file — here through OnlineManual,
		// since there is no WinHelp to hand it to.
		void OpenOnlineManual() {
			if (window.FullScreen) {
				ToggleFullScreen();
			}

			shellOptions.Set(DisplayModeOption, 0);
			shellOptions.Commit();
			shellOptions.Save(Enumerable.Range(0, Prefs.Length).ToArray());
			sound?.Stop();
			OnlineManual.Open(installRoot);
		}

		// INSTANT ACTION, 004312a6: InstantAction_Active (0047363c) set, training mode, then InstantAction_SelectDemo (0044befb) —
		// row 8 + option 46 selected, option 46 stepped modulo 3, the options committed and all saved — and
		// Begin Mission's path from the new career on. The original blanks the screen first, full screen,
		// or shows and hides the palette scope in a window, whose black fill the window closing straight
		// after never shows.
		void InstantAction() {
			instantActionSet = true;
			SetMode(ShellCampaignMode.Training);
			practiceScreen ??= new ShellPracticeScreen(shellOptions);
			practiceScreen.SelectRow(ShellPracticeScreen.RowCount + shellOptions[InstantActionOption]);
			shellOptions.Step(InstantActionOption, InstantActionMissionCount);
			shellOptions.Commit();
			shellOptions.Save(Enumerable.Range(0, Prefs.Length).ToArray());

			if (LaunchTraining(practiceScreen.SelectedRow, "Instant Action")) {
				blanked = window.FullScreen;
			}
		}

		// CONTINUE GAME, MainMenu_OnContinue (004313e4): campaign mode, slot 10 loaded and selected on the save
		// screen, then the bare frame when the game goes on (state 2) and the END OF GAME alert over the menu
		// otherwise (EndOfGame_Show (0044cecf)). Unlike RESTORE it neither autosaves nor clears the campaign map's
		// first-show flag. The original wraps the load in the hourglass, which a load inside one update,
		// with no message pumped, would never show.
		void ContinueGame() {
			SetMode(ShellCampaignMode.Campaign);
			if (!LoadSlot(CurrentGameSlot) || loadedGame == null) {
				Console.WriteLine("The current game could not be read — nothing continued.");
				return;
			}

			saveScreen.SelectSlot(CurrentGameSlot);
			if (loadedGame.GameState == ContinuingGameState) {
				ReturnToFrame();
				return;
			}

			endOfGame.Open(loadedGame.GameState);
			Console.WriteLine($"End of game (state {loadedGame.GameState}): "
				+ $"{art.Text?.Text(endOfGame.FirstLineText) ?? string.Empty} OKAY takes the alert down.");
			RepaintContent();
		}

		// START NEW GAME, MainMenu_OnStartNewGame (00431379): MainMenu_Hide, the mode to 1 (Shell_SetCampaignMode (0040e69e)), then
		// Registration_Show (0043bc0a) — the screen up and a left press posted at its name field with the pointer
		// locked on it, as SAVE takes a save row, so keys reach the field at once.
		void StartNewGame() {
			SetMode(ShellCampaignMode.Campaign);
			registrationUp = true;
			pointer.Grab(ShellRegistrationScreen.FieldHit, Fire);
			Console.WriteLine("Start New Game — type a pilot name, SKILL LEVEL to pick the skill, then ACCEPT, or CANCEL.");
			RepaintContent();
		}

		// SKILL LEVEL (0043c01d) steps the skill; CANCEL (0043c098) is Registration_Hide then MainMenu_Show;
		// ACCEPT (0043c0fb) starts the career.
		void ClickRegistration(ShellRegistrationButton button) {
			switch (button) {
				case ShellRegistrationButton.SkillLevel:
					registration.StepSkill();
					Console.WriteLine($"Skill: {art.Text?.Text(0x35 + registration.Skill) ?? registration.Skill.ToString()}.");
					break;
				case ShellRegistrationButton.Cancel:
					registrationUp = false;
					Console.WriteLine("Cancel — main menu.");
					break;
				default:
					StartCampaign();
					return;
			}

			RepaintContent();
		}

		// ACCEPT, Registration_OnAccept (0043c0fb): gam\herc_inf.dat reloaded, the screen hidden, the campaign
		// map's first-show flag (DAT_004778aa, missionMapShown) cleared, Game_NewCareer(name, skill) in
		// campaign mode, and MissionScreenView from the position — the map, on stage 1 mission 0. The career's
		// position step posts the developer's mission-name dialog's Use Default click, which the original
		// delivers once the handler has returned and which runs Career_LoadCurrentMission; this goes straight
		// there, as LaunchTraining does. Its campaign end rebuilds the map and the texts (here on the adopt),
		// stages slot 10's summary, which no save row shows, puts the frame up with the strip regated, and
		// calls Mission_ShowView(MissionScreenView, 1). Nothing is saved: slot 10 is first written by the next
		// autosave.
		void StartCampaign() {
			registrationUp = false;
			missionMapShown = false;

			int Roll(short bound) => shellRandom.NextBelow(bound);
			if (ShellCampaignLaunch.NewCareer(content, registration.Name, registration.Skill, ShellCampaignMode.Campaign, Roll,
					HeldGame(), out string? failure) is not { } game) {
				Console.WriteLine($"Accept: {failure} No career started; main menu.");
				RepaintContent();
				return;
			}

			var careerHangar = ShellHangar.From(game);
			if (ShellCampaignLaunch.LoadCareerMission(CareerDirectory, content, game, careerHangar, clearList, Roll, out failure)
					is not { } mission) {
				Console.WriteLine($"Accept: {failure} No career started; main menu.");
				RepaintContent();
				return;
			}

			AdoptGame(game, careerHangar, ShellWorkingFiles.In(CareerDirectory));
			Console.WriteLine($"New campaign for {registration.Name}, skill {registration.Skill}: {mission.MissionPath}, "
				+ $"{mission.SquadPositions} squad position(s), {game.SalvageTotal} kg salvage; working files in {CareerDirectory}.");
			screen.ReturnToFrame(mode);
			ShowMissionView();
		}

		// Mission_ShowView(MissionScreenView, 1) (0043a857): the mission tab put up in its view, with no tab
		// click, and then the left press it posts at MISSION, which lights the tab and makes the press sound —
		// its handler finds tab 7 already current and does nothing more.
		void ShowMissionView() {
			screen.SelectTab(ShellScreen.MissionTab);
			SwitchPalette(ShellScreen.MissionTab);
			EnterTab(ShellScreen.MissionTab);
			RepaintContent();
			sound?.PlayPress();
		}

		// VIEW DEMO, 0043156f: the screen blanked, full screen only, then exit code 5 and the loop's end. The
		// launcher answers 5 by starting the simulator with -D, which the caller does here.
		void ViewDemo() {
			blanked = window.FullScreen;
			exitCode = DemoExitCode;
			Console.WriteLine("View Demo.");
			window.Close();
		}

		// CREDITS, MainMenu_OnCredits (004315ec): a bare window shown and the screen blanked, the credits queued
		// and played straight away, then the screen blanked again, the window hidden and the menu repainted
		// under it (EndCredits).
		void Credits() {
			creditsUp = true;
			movieQueue.Enqueue(ShellMovieQueue.Credits, ShellMovieQueue.FullRect);
			movies?.Start();
			Console.WriteLine(movies?.Active == true ? "Credits." : "Credits — movies are off, so nothing plays.");
		}

		void EndCredits() {
			creditsUp = false;
			RepaintContent();
		}

		// One update while the queue plays out. A mouse button or Esc or Space going down ends the movie on
		// screen and reaches nothing else: MainWndProc drops the button's messages while one plays, and
		// WinButton_HandleEvent and ESButtonBitmap_HandleEvent every mouse event while the queue runs. The buttons'
		// state is still taken, so a press made meanwhile is spent rather than delivered afterwards.
		void UpdateMovies(double delta) {
			bool left = mouse?.IsButtonPressed(MouseButton.Left) == true;
			bool right = mouse?.IsButtonPressed(MouseButton.Right) == true;
			bool key = keyboard != null && (keyboard.IsKeyPressed(Key.Escape) || keyboard.IsKeyPressed(Key.Space));
			bool stop = (left && !leftHeld) || (right && !rightHeld) || (key && !skipKeyHeld);
			leftHeld = left;
			rightHeld = right;
			skipKeyHeld = key;

			if (gl != null) {
				movies!.Update(gl, TimeSpan.FromSeconds(delta), stop);
			}
		}

		void DrawMovie(ShellScreenLayout layout) {
			if (movies?.Movie is { Texture: { } texture } && renderer != null) {
				var rect = movies.Rect;
				renderer.DrawTexture(layout, texture, rect.X, rect.Y, rect.Width, rect.Height);
			}
		}

		// What playing the queue does to the rest of the shell.
		ShellMovieHooks MovieHooks() => new() {
			ReadMovie = name => {
				string path = Path.Combine(installRoot, MovieHost.MovieFolderName, name);
				return File.Exists(path) ? MovieHost.ReadMovieFile(path) : null;
			},
			InstallPalette = index => {
				InstallPalette(index);
				RepaintContent();
			},
			SetPaletteScope = index => {
				scopeFilled = true;
				InstallPalette(index);
				RepaintContent();
			},
			RepaintRoot = () => {
				scopeFilled = false;
				RepaintContent();
			},
			FrameUp = () => screen.StripVisible,
			LightMissionTab = () => {
				screen.LightOnly(ShellScreen.MissionTab);
				RepaintContent();
			},
			LeaveMissionTab = () => {
				screen.LeaveTab();
				Console.WriteLine("The mission tab is left, no tab up.");
				RepaintContent();
			},
			CampaignStage = () => campaignStage,
			ShowLocationPicture = (palette, bank) => {
				InstallPalette(palette);
				locationPicture = bank == null ? null : art.LoadBankFrame(content, bank, 0);
				locationTexture?.Dispose();
				locationTexture = locationPicture == null || gl == null ? null
					: new Herculan.Engine.Gl.GpuTexture(gl, locationPicture.Pixels, locationPicture.Width, locationPicture.Height);
				Console.WriteLine(locationPicture != null
					? $"The location picture, dba\\{bank}, for two seconds."
					: $"No location picture for stage {campaignStage}.");
			},
			HideLocationPicture = () => {
				locationTexture?.Dispose();
				locationTexture = null;
				locationPicture = null;
				InstallPalette(ShellPalette.ServiceBay);
				RepaintContent();
			},
			Report = (name, what) => Console.WriteLine($"Movie avi\\{name} {what}."),
		};

		// Shell_InstallPalette (004075b2) by index, unless --shell-palette pins one.
		void InstallPalette(int index) {
			if (paletteName == null && ShellPalette.Name(index) is { } name) {
				LoadPalette(name);
			}
		}

		// SAVE/RESTORE, 00431498: hide the menu, set the campaign mode to 1 (Shell_SetCampaignMode (0040e69e)), point the
		// save screen's EXIT back here, and enter it.
		void OpenSaveRestore() {
			SetMode(ShellCampaignMode.Campaign);
			saveScreen.ExitTarget = ShellSaveExitTarget.MainMenu;
			screen.SelectTab(ShellScreen.SaveTab);
			saveScreen.Enter();
			Console.WriteLine("Save/Restore — the save screen, with EXIT back to the main menu.");
			RepaintContent();
		}

		// QUIT, 00431727: blank the screen (Shell_BlankScreen, 0040723d) and end the main loop, with no prompt
		// and no exit code of its own, so the shell returns the 0 its startup left and the launcher stops
		// (docs/shell/screen-layout.md#quit). The loop's common exit autosaves after window.Run returns.
		void Quit() {
			blanked = true;
			Console.WriteLine("Quit.");
			window.Close();
		}

		// Shell_SetCampaignMode (0040e69e), the mode write the main menu's handlers make: the mode, and prefs.cfg option 42 set to
		// it without its handler and written alone. The tabs are regated by the strip refresh the frame comes
		// back up through (ReturnToFrame), the strip being hidden until then.
		void SetMode(ShellCampaignMode newMode) {
			mode = newMode;
			shellOptions.Set(CampaignModeOption, (byte)newMode, apply: false);
			shellOptions.Save([CampaignModeOption]);
		}

		// PRACTICE MISSIONS, 004318ab: MainMenu_Hide, PracticeScreen_Show (0044bc92), then the mode to 0.
		void OpenPractice() {
			practiceScreen ??= new ShellPracticeScreen(shellOptions);
			practiceScreen.Show();
			practiceUp = true;
			SetMode(ShellCampaignMode.Training);
			Console.WriteLine("Practice missions — training mode.");
			LogPractice();
			RepaintContent();
		}

		// PREFERENCES, 0043150c: MainMenu_Hide, then PreferencesScreen_Enter (004366b5), which seeds the
		// checkboxes from the options and shows the screen.
		void OpenPreferences() {
			preferencesScreen ??= new ShellPreferencesScreen(shellOptions,
				ShellArt.ReadBankFrames(content, ShellPreferencesScreen.CheckBoxBank),
				isFullScreen: () => window.FullScreen, toggleFullScreen: ToggleFullScreen);
			preferencesUp = true;
			Console.WriteLine("Preferences.");
			LogPreferences();
			RepaintContent();
		}

		// A widget's handler. Cancel (00436b90) and Accept (00436c51) end in FUN_00436717 and
		// MainMenu_Show, which take the screen down and put the menu back.
		void ClickPreferences(ShellPreferencesWidget widget) {
			if (preferencesScreen == null) {
				return;
			}

			if (preferencesScreen.Click(widget, sound)) {
				preferencesUp = false;
				screen.SelectTab(ShellScreen.MainMenuTab);
				Console.WriteLine($"{widget} — main menu.");
			}

			LogPreferences();
			RepaintContent();
		}

		bool ManualWeaponBuild() => shellOptions[WeaponsBuildingOption] != 0;

		// Display_ToggleFullScreen (00407085). Retail sets an exclusive 640x480 8-bit display mode with the window's frame pushed
		// off the screen, confines the pointer to the screen and centres it; going back releases DirectDraw,
		// which restores the desktop's mode, and centres the window. Here full screen covers the monitor at
		// its current mode (EngineWindow.ToggleFullScreen), with the canvas scaled into it as it is in a
		// window — a divergence the user chose, so that no display mode changes. The pointer is confined and
		// centred as retail's is.
		void ToggleFullScreen() {
			window.ToggleFullScreen();
			if (mouse?.Cursor is { } cursor) {
				cursor.IsConfined = window.FullScreen;
			}

			if (window.FullScreen && mouse != null) {
				var client = window.ClientSize;
				mouse.Position = new System.Numerics.Vector2(client.X / 2, client.Y / 2);
			}

			Console.WriteLine(window.FullScreen ? "Full screen." : "Windowed.");
		}

		// MainWndProc (00404a2c)'s display keys, each gated on no movie playing and the startup sequence
		// being over. Alt+Enter toggles full screen on the Enter key's release;
		// Alt+Tab, Alt+Esc and Ctrl+Esc leave it on either edge (Display_LeaveFullScreen, 0040722e). Each then writes option 6
		// from the window and, with the preferences screen up, relights its display group; otherwise it
		// commits the options without their handlers and writes all 54.
		void DisplayHotkey(Key key, bool released) {
			if (keyboard == null || startup is { Done: false } || movies?.Playing == true) {
				return;
			}

			bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
			bool alt = keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);
			bool ctrl = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
			bool altOnly = alt && !shift && !ctrl;

			if (altOnly && key is Key.Enter or Key.KeypadEnter) {
				if (!released) {
					return;
				}

				ToggleFullScreen();
			} else if ((altOnly && key is Key.Tab or Key.Escape) || (ctrl && !alt && !shift && key == Key.Escape)) {
				if (window.FullScreen) {
					ToggleFullScreen();
				}
			} else {
				return;
			}

			shellOptions.Set(DisplayModeOption, (byte)(window.FullScreen ? 1 : 0));
			if (preferencesUp) {
				RepaintContent();
			} else {
				shellOptions.Commit(apply: false);
				shellOptions.Save(Enumerable.Range(0, Prefs.Length).ToArray());
			}
		}

		void LogPreferences() {
			if (preferencesScreen == null) {
				return;
			}

			var ticked = Enum.GetValues<ShellPreferencesWidget>()
				.Where(w => ShellPreferencesScreen.IsCheckBox(w) && preferencesScreen.IsChecked(w));
			Console.WriteLine($"Preferences: {string.Join(", ", ticked)}"
				+ (preferencesScreen.AlertOpen ? "; the Alert! dialog is up." : "."));
		}

		// A practice row's handler, PracticeScreen_OnRow0-7 (0044c413-0044c6ba): PracticeScreen_SelectRow
		// (0044bd7c), a no-op on the row already lit.
		void SelectPracticeRow(int row) {
			if (practiceScreen?.SelectRow(row) == true) {
				RepaintContent();
				LogPractice();
			}
		}

		// The five parameter labels step their option, forward on the left release and back on the right
		// (0044bf29-0044c21d). Main Menu (0044c2da) hides the screen and shows the menu. Begin Mission
		// (0044c396) is BeginPractice.
		void ClickPracticeButton(ShellPracticeButton button) {
			if (practiceScreen == null) {
				return;
			}

			switch (button) {
				case ShellPracticeButton.MainMenu:
					practiceUp = false;
					screen.SelectTab(ShellScreen.MainMenuTab);
					Console.WriteLine("Main menu.");
					break;
				case ShellPracticeButton.BeginMission:
					BeginPractice();
					return;
				default:
					practiceScreen.Step(button, eventButton == ShellMouseButton.Left);
					LogPractice();
					break;
			}

			RepaintContent();
		}

		// Begin Mission (0044c396): the options committed and saved to prefs.cfg, then LaunchTraining on the
		// lit row.
		void BeginPractice() {
			shellOptions.Commit();
			shellOptions.Save(Enumerable.Range(0, Prefs.Length).ToArray());
			LaunchTraining(practiceScreen!.SelectedRow, "Begin Mission");
		}

		// Game_NewCareer("TRAINEE", option 0x27) in training mode on stage 0's mission at row: the career
		// started, its mission loaded and the handoff written, and the shell closed on exit code 2. The
		// original gets from the career to the load through the developer's mission-name dialog, which
		// clicks its own Use Default at once; this goes straight there. The career is the game in progress,
		// which the loop exit's autosave writes as slot 11 with the handoff's three working files. Returns
		// whether it launched.
		bool LaunchTraining(int row, string label) {
			var handoff = ShellTrainingLaunch.Write(HandoffDirectory, content, shellOptions, row, instantActionSet,
				shellRandom, clearList, HeldGame(), out string? failure);
			if (handoff == null) {
				Console.WriteLine($"{label}: {failure}");
				return false;
			}

			AdoptGame(handoff.Game, handoff.Hangar, ShellWorkingFiles.In(HandoffDirectory));

			var squad = Enumerable.Range(0, ShellHangar.BayCount)
				.Select(bay => handoff.Hangar.Bay(bay) is { } machine
					? $"bay {bay} chassis {machine.ChassisType}" + (handoff.Hangar.PilotFor(bay) is { } pilot ? $" ({pilot.Name})" : string.Empty)
					: null)
				.OfType<string>();
			Console.WriteLine($"{label} — {handoff.MissionPath}, {handoff.SquadPositions} squad position(s): "
				+ $"{string.Join(", ", squad)}; {handoff.Hangar.MachinesOnStrength} machine(s) going. "
				+ $"Handoff written to {HandoffDirectory}; launching the mission.");
			launch = new ShellLaunch(handoff.ScriptPath, Path.Combine(installRoot, MissionLoader.DataFolderName));
			window.Close();
			return true;
		}

		void LogPractice() {
			if (practiceScreen == null) {
				return;
			}

			var values = Enumerable.Range(0, (int)ShellPracticeButton.HercType + 1).Select(i =>
				art.Text?.Text(practiceScreen.ValueText((ShellPracticeButton)i)) ?? $"{practiceScreen.ValueText((ShellPracticeButton)i)}");
			Console.WriteLine($"Practice: row {practiceScreen.SelectedRow} "
				+ $"({art.Text?.Text(0xf2 + practiceScreen.SelectedRow) ?? "?"}) — {string.Join(", ", values)}"
				+ (practiceScreen.IsEnabled(ShellPracticeButton.HercType) ? "." : "; Herc Type is the mission's own."));
		}

		// A save row's handler, SaveScreen_SelectSlot (0043795f). Clicking the row already selected is a
		// no-op, the same early return the original's selection move opens with, and so is any row while
		// a rename is live.
		void SelectSaveSlot(int slot) {
			if (!saveScreen.SelectSlot(slot)) {
				return;
			}

			RepaintContent();
			Console.WriteLine($"Slot {slot + 1}: "
				+ (saveScreen.Slots.ElementAtOrDefault(slot) is { InUse: true, Summary: { } summary }
					? $"{summary.PilotName}, sector {summary.Sector}, mission {summary.Mission + 1}, "
					  + $"{summary.SalvageKilograms} kg salvage"
					: "empty."));
		}

		void ClickSaveButton(ShellSaveButton button) {
			switch (button) {
				case ShellSaveButton.Save:
					BeginRename();
					break;
				case ShellSaveButton.Accept:
					AcceptRename();
					break;
				case ShellSaveButton.Cancel:
					saveScreen.CancelRename();
					Console.WriteLine("Cancel — the rename is abandoned.");
					RepaintContent();
					break;
				case ShellSaveButton.Restore:
					RestoreSelectedSlot();
					break;
				case ShellSaveButton.Exit:
					LeaveSaveScreen();
					break;
			}
		}

		// SAVE, SaveScreen_OnSave (00437bd3): the rename starts, and the pointer is taken onto the row so
		// that keystrokes reach it.
		void BeginRename() {
			int slot = saveScreen.SelectedSlot;
			saveScreen.BeginRename();
			pointer.Grab(new ShellHit(new ShellWidget(ShellWidgetKind.SaveRow, slot), ShellHandler.EditField), Fire);
			Console.WriteLine($"Save — type a name for slot {slot + 1}, then ACCEPT to save or CANCEL.");
			RepaintContent();
		}

		// ACCEPT, SaveScreen_OnAccept (00437ffa): Game_SaveSlot under the row's string, then
		// Stats_StageCurrentGame for the slot's summary.
		void AcceptRename() {
			int slot = saveScreen.SelectedSlot;
			string label = saveScreen.RowText(slot);
			if (SaveGame(slot, label) is { } entry && loadedGame != null) {
				saveScreen.SetSlot(slot, entry with { Summary = ShellSaveSummary.From(loadedGame) });
			}

			saveScreen.EndRename();
			RepaintContent();
		}

		// Game_SaveSlot (0040e37b): the live game, as the screens have left it, written as a slot. Nothing
		// without a game in progress. Returns the slot's new directory entry, which the screen now shows.
		ShellSaveSlot? SaveGame(int slot, string? label) {
			if (!gameInProgress || loadedGame == null) {
				return null;
			}

			hangar.Store(loadedGame);
			bool training = mode == ShellCampaignMode.Training;
			var entry = ShellSaveSlots.SaveGame(installRoot, saveScreen.Slots, slot, label, training, loadedGame,
				workingFiles, out string? failure);
			int written = slot == CurrentGameSlot && training ? CurrentGameSlot + 1 : slot;
			if (entry == null) {
				Console.WriteLine($"Could not save slot {written}: {failure}");
				return null;
			}

			saveScreen.SetSlot(written, entry);
			if (written == CurrentGameSlot) {
				mainMenu.CanContinue = true;
			}

			Console.WriteLine($"Saved {entry.FileName} as \"{entry.Label.Trim()}\" in {ShellSaveSlots.Directory(installRoot)}.");
			return entry;
		}

		// Game_SaveSlot(10, NULL), the current-game autosave.
		void AutoSave() => SaveGame(CurrentGameSlot, null);

		// The game the shell's memory holds, which a new career keeps parts of: the last one loaded or started,
		// or none since the startup. The slot opened at startup for the repair screen is not one.
		HercWorks.Core.Data.File.Sav.PlayerSave? HeldGame() => gameInProgress ? loadedGame : null;

		// RESTORE, SaveScreen_OnRestore (00437d03): load the selected slot and write it straight back out as
		// the slot-10 autosave, then leave exactly as EXIT does on the tab-strip path, whichever way the
		// screen was entered. It clears the campaign map's first-show flag (DAT_004778aa), which
		// missionMapShown is.
		void RestoreSelectedSlot() {
			int slot = saveScreen.SelectedSlot;
			if (!LoadSlot(slot)) {
				Console.WriteLine($"Slot {slot + 1} could not be read — nothing restored.");
				return;
			}

			missionMapShown = false;
			AutoSave();
			saveScreen.Leave();
			ReturnToFrame();
		}

		// Game_LoadSlot (0040e4f2): a slot in use read in whole — the hangar, the career and its mission, and
		// the game in progress that saving needs. Slot 10 is slot 11 in training, as it is to Game_SaveSlot. The
		// mission map is rebuilt, here on the briefing's next visit, and the briefing's and debrief's movies play
		// again (DAT_004778ab and DAT_004778ac cleared). Returns false for a slot not in use, which the original
		// refuses, or one that cannot be read.
		bool LoadSlot(int slot) {
			if (slot == CurrentGameSlot && mode == ShellCampaignMode.Training) {
				slot = CurrentGameSlot + 1;
			}

			if (saveScreen.Slots.ElementAtOrDefault(slot) is not { InUse: true } entry
					|| ShellSaveSlots.LoadSave(installRoot, entry.FileName) is not { } restored) {
				return false;
			}

			AdoptGame(restored, ShellHangar.From(restored), ShellWorkingFiles.ForSlot(installRoot, slot));
			Console.WriteLine($"Loaded {entry.FileName}: "
				+ (ShellSaveSummary.From(restored) is { } summary
					? $"{summary.PilotName}, sector {summary.Sector}, mission {summary.Mission + 1}."
					: "no pilot record.")
				+ (repairScreen.SelectedBay >= 0
					? $" Repair opens on bay {repairScreen.SelectedBay}."
					: " No built machine in any hangar bay."));
			return true;
		}

		// The game in progress from here on, from a load or a new career — maybe_HasGameInProgress (0048260a) set, the briefing's and
		// debrief's movies to play again (DAT_004778ab and DAT_004778ac cleared), and the mission map rebuilt on
		// the briefing's next visit.
		void AdoptGame(HercWorks.Core.Data.File.Sav.PlayerSave game, ShellHangar gameHangar, ShellWorkingFiles files) {
			hangar = gameHangar;
			loadedGame = game;
			workingFiles = files;
			gameInProgress = true;
			missionMap = null;
			briefingMovieQueued = false;
			debriefMovieQueued = false;
			missionTexts = ShellMissionTexts.Load(files, game);
			campaignStage = game.CampaignStage;
			missionInStage = game.MissionInStage;
			repairScreen = new ShellRepairScreen(hangar, repairCosts, startBay, repairDiagrams) {
				QueuedKilograms = armoryCatalog.QueuedTotal(hangar),
				RepairMode = shellOptions[RepairOption],
			};
			saveScreen.CanSave = true;
		}

		// A keystroke, delivered as VSHELL's queue delivers one: to the pointer's target, which on the save
		// screen may be one of its rows (ESDialog_HandleEvent, 0040beaf). The row takes a character or a
		// command, Enter releases the pointer, and whatever the key, the row's handler then runs, which
		// selects it. The registration screen's name field takes keys the same way, its handler regating
		// ACCEPT. Nothing else ported here takes a key. A fade or the movie queue drops keys as it drops
		// clicks.
		void WidgetKey(Key key, bool released) {
			if (keyboard == null || sound?.Fading == true || movies?.Active == true
					|| pointer.Target?.Widget is not { } row
					|| !(row.Kind == ShellWidgetKind.SaveRow && screen.SelectedTab == ShellScreen.SaveTab
						|| row.Kind == ShellWidgetKind.RegistrationField && registrationUp)
					|| ShellKeyboard.Index(key) is not { } index) {
				return;
			}

			bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
			bool ctrl = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
			bool alt = keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);
			if (ShellKeyboard.Event(index, released, shift, ctrl, alt) is not { } shellKey) {
				return;
			}

			bool focused = pointer.Focused == row;
			bool nameField = row.Kind == ShellWidgetKind.RegistrationField;
			var font = art.Sprites?.Font(ShellArt.ScreenFont);
			bool changed = nameField ? registration.Key(shellKey, focused, font) : saveScreen.Key(row.Index, shellKey, focused, font);
			if (shellKey.Command == ShellKey.Enter && focused && (nameField || saveScreen.CaretEnabled(row.Index))) {
				pointer.ReleaseFocus();
				changed = true;
			}

			if (changed) {
				RepaintContent();
			}

			Fire(row);
		}

		// The focused edit field's blink alarm (WinTimer_InstallAlarm, 500 and 500), installed as the field
		// takes the focus. A change of focus repaints too, as the field's paint on the press does.
		void BlinkCaret() {
			var focused = pointer.Focused;
			long now = Environment.TickCount64;
			if (focused != caretField) {
				caretField = focused;
				caretClock = now;
				if (screen.SelectedTab == ShellScreen.SaveTab || registrationUp) {
					RepaintContent();
				}

				return;
			}

			if (now - caretClock < ShellSaveScreen.CaretBlinkMilliseconds) {
				return;
			}

			if (focused is { Kind: ShellWidgetKind.SaveRow } row && screen.SelectedTab == ShellScreen.SaveTab) {
				saveScreen.CaretTick(row.Index);
			} else if (focused is { Kind: ShellWidgetKind.RegistrationField } && registrationUp) {
				registration.CaretTick();
			} else {
				return;
			}

			caretClock += ShellSaveScreen.CaretBlinkMilliseconds;
			RepaintContent();
		}

		// EXIT, SaveScreen_OnExit (00437d94): the teardown, then wherever the handler that entered the
		// screen said to go.
		void LeaveSaveScreen() {
			saveScreen.Leave();
			if (saveScreen.ExitTarget == ShellSaveExitTarget.MainMenu) {
				screen.SelectTab(ShellScreen.MainMenuTab);
				Console.WriteLine("Exit — main menu.");
				RepaintContent();
				return;
			}

			ReturnToFrame();
		}

		void ReturnToFrame() {
			screen.ReturnToFrame(mode);
			Console.WriteLine("Back to the tab strip, no tab up.");
			RepaintContent();
		}

		// A repair row or hotspot's handler, Repair_SelectHotspot (00433eb9): the selection moves and the
		// detail panel follows. A hardpoint row past the machine's capacity, or one holding no weapon,
		// refuses the selection outright — nothing moves and nothing repaints.
		void SelectRepair(int column, int row) {
			if (!repairScreen.Select(column, row)) {
				return;
			}

			RepaintContent();
			var category = ShellRepairScreen.CategoryOf(column, row);
			Console.WriteLine($"{category} {ShellRepairScreen.IndexOf(category, row)}: "
				+ $"condition {repairScreen.SelectionCondition}, "
				+ $"{repairScreen.SelectionCost} kg to repair one level.");
		}

		// REPAIR (00434b2d), REPAIR ALL (00434c59) and CANCEL (00434d73). Each refills the rows and the readout
		// panels after it, which the repaint does here.
		void ClickRepairButton(ShellRepairButton button) {
			switch (button) {
				case ShellRepairButton.Repair:
					int cost = repairScreen.Repair();
					Console.WriteLine($"Repaired {repairScreen.SelectedCategory} {repairScreen.SelectedIndex} to "
						+ $"{repairScreen.SelectionCondition} for {cost} kg.");
					break;
				case ShellRepairButton.RepairAll:
					Console.WriteLine($"Repaired bay {repairScreen.SelectedBay} to 100 for {repairScreen.RepairAll()} kg.");
					break;
				case ShellRepairButton.Cancel:
					repairScreen.Cancel();
					Console.WriteLine($"Repairs to bay {repairScreen.SelectedBay} undone.");
					break;
				default:
					return;
			}

			Console.WriteLine($"{hangar.SalvageKilograms} kg in the pool, {repairScreen.AvailableKilograms} kg available.");
			RepaintContent();
		}

		// A Squad Inventory row's handler, Squad_SelectBay (0043d64d), whose arm is the tab that is up.
		void ClickRoster(int bay) {
			if (screen.SelectedTab == ShellScreen.CrewTab) {
				if (crewScreen?.ClickRoster(bay) == true) {
					RepaintContent();
					LogCrew();
				}

				return;
			}

			if (screen.SelectedTab == ShellScreen.BuildTab) {
				if (buildScreen?.ClickRoster(bay) == true) {
					RepaintContent();
					LogBuild();
				}

				return;
			}

			if (screen.SelectedTab == ShellScreen.WeaponsTab) {
				if (weaponsScreen?.ClickRoster(bay) == true) {
					RepaintContent();
					LogWeapons();
				}

				return;
			}

			if (repairScreen.SelectBay(bay)) {
				RepaintContent();
				Console.WriteLine($"Bay {bay}: chassis type {repairScreen.Machine?.ChassisType}.");
			}
		}

		// The crew panel's handlers. A row, or the portrait inside it, selects the row; a squad portrait
		// and CLEAR assign against the selected row. Every one of them repaints.
		void ClickCrew(ShellWidget widget) {
			if (crewScreen == null) {
				return;
			}

			switch (widget.Kind) {
				case ShellWidgetKind.CrewRow or ShellWidgetKind.CrewRowPortrait:
					crewScreen.SelectRow(widget.Index);
					break;
				case ShellWidgetKind.CrewSquadPortrait:
					crewScreen.ClickPortrait(widget.Index);
					break;
				case ShellWidgetKind.CrewClear:
					crewScreen.Clear();
					break;
				default:
					return;
			}

			RepaintContent();
			LogCrew();
		}

		void LogCrew() {
			if (crewScreen == null) {
				return;
			}

			var rows = Enumerable.Range(0, ShellCrewScreen.RowCount).Select(row =>
				crewScreen.RowPilot(row) is { } pilot ? $"{row}: {pilot.Name} in bay {pilot.Bay}" : $"{row}: empty");
			Console.WriteLine($"Crew row {crewScreen.SelectedRow}, bay {crewScreen.SelectedBay} — "
				+ string.Join("; ", rows) + $". {hangar.MachinesOnStrength} on strength.");
		}

		// A chassis row's handler, Build_SelectChassis (00446c3b); the chassis already selected is a no-op.
		void SelectChassis(int chassis) {
			if (buildScreen?.SelectChassis(chassis) == true) {
				RepaintContent();
				LogBuild();
			}
		}

		void LogBuild() {
			if (buildScreen == null) {
				return;
			}

			Console.WriteLine($"Build: chassis {buildScreen.SelectedChassis}"
				+ (buildScreen.SelectedEntry is { } entry ? $" ({entry.SalvageReq} tons)" : string.Empty)
				+ $", bay {buildScreen.SelectedBay}, {buildScreen.AvailableKilograms} kg available; "
				+ $"SCRAP {(buildScreen.IsEnabled(ShellBuildButton.Scrap) ? "live" : "dead")}, "
				+ $"BUILD {(buildScreen.IsEnabled(ShellBuildButton.Build) ? "live" : "dead")}.");
		}

		// BUILD's handler (00446f3e) orders the chassis; SCRAP's (00446ee0) puts the dialog up.
		void ClickBuildButton(ShellBuildButton button) {
			if (buildScreen == null) {
				return;
			}

			if (button == ShellBuildButton.Scrap) {
				OpenScrapDialog(buildScreen.SelectedBay);
				return;
			}

			buildScreen.Build();
			Console.WriteLine($"Built chassis {buildScreen.SelectedChassis} into bay {buildScreen.SelectedBay}.");
			RepaintContent();
			LogBuild();
		}

		// Both SCRAP handlers, the build tab's (00446ee0) and the repair tab's (00434d15): 00447711 quotes
		// the selected bay's machine and shows the dialog.
		void OpenScrapDialog(int bay) {
			scrapDialog.Open(bay, (repairCosts?.ScrapValue(hangar.Bay(bay)) ?? 0) / ShellRepairCosts.KilogramsPerTon);
			Console.WriteLine($"Scrap bay {bay}? It will yield {scrapDialog.YieldTons} tons.");
			RepaintContent();
		}

		// CANCEL (00447c38) only takes the dialog down. ACCEPT (00447c96) takes it down, scraps the bay
		// (0040e757), and on the repair tab moves to the first bay holding a finished machine; the build
		// tab keeps the bay, now empty, and regates.
		void ClickScrapDialogButton(ShellScrapDialogButton button) {
			if (weaponScrapDialog.IsOpen) {
				ClickWeaponScrapDialogButton(button);
				return;
			}

			scrapDialog.Close();
			if (button == ShellScrapDialogButton.Accept) {
				int value = hangar.Scrap(scrapDialog.Subject, repairCosts);
				Console.WriteLine($"Scrapped bay {scrapDialog.Subject} for {value} kg; {hangar.SalvageKilograms} kg in the pool.");
				if (screen.SelectedTab == ShellScreen.RepairTab) {
					repairScreen.SelectBay(hangar.FirstBuiltBay());
				}
			}

			RepaintContent();
		}

		// The weapon dialog's CANCEL (00447d59) only takes it down. ACCEPT (WeaponScrapDialog_OnAccept,
		// 00447db7) takes it down, sells the whole stock (Armory_ScrapWeapons, 0040e7b2), trims or refills
		// the queue by the build mode (Armory_RefreshQueue, 00412413), and refreshes the rows and readout.
		void ClickWeaponScrapDialogButton(ShellScrapDialogButton button) {
			weaponScrapDialog.Close();
			if (button == ShellScrapDialogButton.Accept && armoryScreen != null) {
				int weapon = weaponScrapDialog.Subject;
				hangar.ScrapStock(weapon, armoryCatalog.ScrapValueTons(hangar, weapon));
				armoryCatalog.RefreshQueue(hangar, ManualWeaponBuild());
				armoryScreen.RefreshAfterScrap();
				Console.WriteLine($"Scrapped weapon {weapon}'s stock; {hangar.SalvageKilograms} kg in the pool.");
				LogArmory();
			}

			RepaintContent();
		}

		// Tab 4's entry, from the bay the repair screen has selected, as the crew tab's is.
		void EnterBuild() {
			if (buildScreen == null) {
				buildScreen = new ShellBuildScreen(hangar, repairScreen.SelectedBay, chassisCatalog, repairDiagrams,
					bayPictures);
			} else {
				buildScreen.Enter(hangar, repairScreen.SelectedBay);
			}

			buildScreen.QueuedKilograms = armoryCatalog.QueuedTotal(hangar);

			LogBuild();
		}

		// Tab 2's entry, from the bay the repair screen has selected, as the build and crew tabs' are.
		void EnterWeapons() {
			if (weaponsScreen == null) {
				weaponsScreen = new ShellWeaponsScreen(hangar, repairScreen.SelectedBay, weaponsArt, bayPictures);
			} else {
				weaponsScreen.Enter(hangar, repairScreen.SelectedBay);
			}

			LogWeapons();
		}

		// An inventory row's handler, one of the thunks from 00440300: Arming_SelectRow (0043f71c) with the
		// fit armed, so with a hardpoint selected the row's weapon goes into it.
		void SelectWeaponsRow(int row) {
			if (weaponsScreen?.SelectRow(row, fit: true) == true) {
				RepaintContent();
				LogWeapons();
			}
		}

		// A hotspot over the bay picture, Arming_SelectHardpoint (0043dbb2).
		void SelectHardpoint(int hardpoint) {
			if (weaponsScreen?.SelectHardpoint(hardpoint) == true) {
				RepaintContent();
				LogWeapons();
			}
		}

		// The four guidance buttons show their kind and write it to the mount, the rack's own button
		// selects its row again without fitting it (0044012a), and the steppers move the hardpoint.
		void ClickWeaponsButton(ShellWeaponsButton button) {
			if (weaponsScreen == null) {
				return;
			}

			bool changed = button switch {
				ShellWeaponsButton.Arm or ShellWeaponsButton.Arh or ShellWeaponsButton.Sarh or ShellWeaponsButton.Eo =>
					ShowWeaponsGuidance((int)button),
				ShellWeaponsButton.Weapon => weaponsScreen.SelectRow(weaponsScreen.SelectedRow, fit: false),
				ShellWeaponsButton.PreviousHardpoint => weaponsScreen.PreviousHardpoint(),
				_ => weaponsScreen.NextHardpoint(),
			};

			if (changed) {
				RepaintContent();
				LogWeapons();
			}
		}

		bool ShowWeaponsGuidance(int button) {
			weaponsScreen!.ShowGuidance(button);
			return true;
		}

		void LogWeapons() {
			if (weaponsScreen == null) {
				return;
			}

			int weapon = weaponsScreen.SelectedWeapon;
			int hardpoint = weaponsScreen.SelectedHardpoint;
			var mount = weaponsScreen.Machine?.Mount(hardpoint);
			Console.WriteLine($"Weapons: bay {weaponsScreen.SelectedBay}, row {weaponsScreen.SelectedRow} "
				+ $"({art.Text?.Text(0x7e + weapon) ?? $"weapon {weapon}"}, {hangar.WeaponsOwned(weapon)} held)"
				+ (hardpoint == -1 ? ", no hardpoint"
					: $", hardpoint {hardpoint + 1} holding " + (mount == null ? "nothing"
						: $"weapon {mount.WeaponId} at {weaponsScreen.Machine!.Condition(ShellRepairCategory.Hardpoint, hardpoint)}%, guidance {mount.Guidance}"))
				+ (weaponsScreen.ShowingGuidance ? $", showing guidance kind {weaponsScreen.ShownGuidance}." : "."));
		}

		// Tab 5's entry, Armory_Enter (004494f7). The armory has no squad panel and no bay.
		void EnterArmory() {
			if (armoryScreen == null) {
				armoryScreen = new ShellArmoryScreen(hangar, ManualWeaponBuild(), armoryCatalog, weaponsArt);
			} else {
				armoryScreen.Enter(hangar, ManualWeaponBuild());
			}

			Console.WriteLine($"Armory: weapons built {(ManualWeaponBuild() ? "by hand" : "automatically")}, "
				+ $"{hangar.QueueFreeSlots} of {ShellHangar.QueueSlots} queue slots free, "
				+ $"{armoryScreen.AllocatedKilograms} kg allocated, {armoryScreen.AvailableKilograms} kg available.");
			LogArmory();
		}

		// A row's release: Armory_ClickRow (0044969f) for the left button, Armory_RightClickRow (004499de)
		// for the right.
		void ClickArmoryRow(int row) {
			if (armoryScreen == null) {
				return;
			}

			bool changed = eventButton == ShellMouseButton.Left
				? armoryScreen.ClickRow(row)
				: armoryScreen.RightClickRow(row);
			if (!changed) {
				return;
			}

			RepaintContent();
			LogArmory();
		}

		// Clear (00449ef4) takes the lit weapon's units off the queue. Scrap (Armory_OnScrap, 00449e78) puts
		// the weapon scrap dialog up on the lit weapon's stock.
		void ClickArmoryButton(ShellArmoryButton button) {
			if (armoryScreen == null) {
				return;
			}

			if (button == ShellArmoryButton.Scrap) {
				if (armoryScreen.SelectedRow != -1) {
					int weapon = armoryScreen.SelectedWeapon;
					weaponScrapDialog.Open(weapon, armoryCatalog.ScrapValueTons(hangar, weapon));
					Console.WriteLine($"Scrap weapon {weapon}? {hangar.WeaponsOwned(weapon)} held will yield "
						+ $"{weaponScrapDialog.YieldTons} tons.");
					RepaintContent();
				}

				return;
			}

			armoryScreen.Clear();
			RepaintContent();
			LogArmory();
		}

		void LogArmory() {
			if (armoryScreen == null) {
				return;
			}

			int weapon = armoryScreen.SelectedWeapon;
			Console.WriteLine($"Armory: row {armoryScreen.SelectedRow} "
				+ $"({armoryCatalog.Names?.Text(weapon) ?? $"weapon {weapon}"}, {hangar.WeaponsOwned(weapon)} held, "
				+ $"{hangar.QueuedCount(weapon)} queued, {armoryCatalog.PriceKilograms(weapon)} kg); "
				+ $"{hangar.QueueFreeSlots} slots free, {armoryScreen.AllocatedKilograms} kg allocated.");
		}

		// Tab 7's entry, Mission_Show (004441e3), in the view the tab handler picks, or the debrief's. The map
		// view queues the stage's two movies and sets the map's first-show flag whether or not they play; the
		// briefing queues the career's briefing movie once per load, and the debrief its debrief movie, both
		// into the Telecomm picture through the view's palette. The main loop's pass plays them.
		void EnterMission() {
			missionViewUp = MissionView();
			if (missionViewUp == ShellMissionView.Debriefing) {
				missionScreen.EnterDebrief(debriefText, art.Sprites?.Font(ShellArt.ScreenFont));
				if (!debriefMovieQueued && debriefMovie is { } movie) {
					movieQueue.Enqueue(movie, ShellMovieQueue.TelecommRect, ShellPalette.FirstDebriefing - 1 + campaignStage);
					debriefMovieQueued = true;
				}

				Console.WriteLine($"Mission: the debrief, {missionScreen.DebriefBox.Lines.Count} lines"
					+ (missionScreen.ReportTextsUp ? ", with the mission report." : "."));
				return;
			}

			if (missionViewUp == ShellMissionView.Map) {
				int mapPalette = campaignStage - 1 > 3 ? ShellPalette.CampaignMapMoon : ShellPalette.CampaignMapEarth;
				movieQueue.Enqueue(ShellMovieQueue.StageMovieBase + campaignStage, ShellMovieQueue.TelecommRect, mapPalette);
				movieQueue.Enqueue(ShellMovieQueue.StageThumbnailBase + campaignStage, ShellMovieQueue.MapPanelRect,
					showsLocation: true);
				missionMapShown = true;
				string? campaignText = ShellCampaignText.Load(content, campaignStage);
				missionScreen.EnterMap(campaignStage, campaignText, art.Text, art.Sprites?.Font(ShellArt.ScreenFont));
				Console.WriteLine($"Mission: the campaign map, stage {campaignStage}"
					+ (campaignText == null ? $"; eng\\campaign.str has no text for it." : $", {missionScreen.MapBox.Lines.Count} lines of its text.")
					+ " The next visit opens the briefing.");
				return;
			}

			missionScreen.EnterBriefing(missionTexts, art.Sprites?.Font(ShellArt.ScreenFont));
			missionMap ??= loadedGame != null ? ShellMap.Load(installRoot, workingFiles, content) : null;
			if (!briefingMovieQueued && loadedGame != null) {
				movieQueue.Enqueue(loadedGame.BriefingMovie, ShellMovieQueue.TelecommRect,
					ShellPalette.FirstBriefing - 1 + campaignStage);
				briefingMovieQueued = true;
			}

			// The intro's first pass puts the camera on the full view before anything is painted. The main
			// loop runs it after the pass's movies (ShellMap_RunIntro after Movie_PlayQueue), so behind a
			// briefing movie it waits for the movie.
			if (missionMap is { IntroRunning: true } intro && !BriefingMoviePending()) {
				intro.Advance(MapClock());
			}
			Console.WriteLine(missionMap == null
				? $"Mission map: no working script.dat ({workingFiles.Script}) — the panel stays black."
				: $"Mission map: bounds {missionMap.MinX},{missionMap.MinY} - {missionMap.MaxX},{missionMap.MaxY}, "
				  + $"altitude {missionMap.FullView.Z}"
				  + (missionMap.IntroRunning ? "; its intro runs now — click, Esc or Space to skip it." : "."));
			LogMission();
		}

		// Whether a movie is queued or playing out, which on the briefing is its movie.
		bool BriefingMoviePending() => movies?.Active == true || movieQueue.Next != null;

		// The map, while its intro is still running on the briefing that is up.
		ShellMap? MapIntroUp() =>
			screen.SelectedTab == ShellScreen.MissionTab && missionViewUp == ShellMissionView.Briefing
				&& missionMap is { IntroRunning: true } map ? map : null;

		// A text button shows its text; Rock & Roll launches the mission.
		void ClickMissionButton(ShellMissionButton button) {
			if (button == ShellMissionButton.RockAndRoll) {
				RockAndRoll();
				return;
			}

			missionScreen.ShowText(button);
			RepaintContent();
			LogMission();
		}

		// The page arrows page the text that is up; the map's six call the map's methods and repaint it
		// (Mission_OnMapUp (00444ee7) to Mission_OnMapZoomOut (004452f2)), once each, the auto-repeat not being ported.
		void ClickMissionArrow(ShellMissionArrow arrow) {
			if (arrow is not (ShellMissionArrow.PageUp or ShellMissionArrow.PageDown)) {
				if (missionMap != null) {
					missionMap.Press(arrow);
					RepaintContent();
					Console.WriteLine($"{arrow}: camera {missionMap.CameraX + missionMap.PanX},"
						+ $"{missionMap.CameraY + missionMap.PanY}, altitude {missionMap.CameraZ}, pan step {missionMap.PanStep}.");
				}

				return;
			}

			if (missionScreen.Page(arrow == ShellMissionArrow.PageDown)) {
				RepaintContent();
				LogMission();
			}
		}

		// Mission_OnRockAndRoll (00445509): the first test to fail puts the refusal up; otherwise the handoff
		// is written and the shell's window closes, and the host runs the mission it names.
		void RockAndRoll() {
			if (ShellMissionLaunch.Check(hangar) is { } refusal) {
				launchRefusal.Open(refusal);
				Console.WriteLine($"Rock & Roll refused: {refusal}.");
				RepaintContent();
				return;
			}

			string? scriptPath = loadedGame == null ? null
				: ShellMissionLaunch.WriteHandoff(HandoffDirectory, workingFiles, loadedGame, hangar);
			if (scriptPath == null) {
				Console.WriteLine($"Rock & Roll: no working script.dat ({workingFiles.Script}) to launch.");
				return;
			}

			// The export rewrote the working player.mec, which the loop exit's autosave copies out.
			workingFiles = workingFiles with { Player = Path.Combine(HandoffDirectory, MissionLoader.PlayerFileName) };
			launch = new ShellLaunch(scriptPath, Path.Combine(installRoot, MissionLoader.DataFolderName));
			Console.WriteLine($"Rock & Roll — handoff written to {HandoffDirectory}; launching the mission.");
			window.Close();
		}

		void LogMission() {
			var box = missionScreen.Box(missionScreen.ShownText);
			Console.WriteLine($"Mission: {missionScreen.ShownText}, {box.Lines.Count} lines, "
				+ $"page {box.Page + 1} of {box.PageCount}.");
		}

		// Tab 6's entry. The bay it starts from is the one the previous tab left selected, SelectedBaySlot (00482ae5),
		// which here only the repair screen tracks; the entry then moves it.
		void EnterCrew() {
			if (crewScreen == null) {
				crewScreen = new ShellCrewScreen(hangar, repairScreen.SelectedBay, bayPictures, crewPortraits);
			} else {
				crewScreen.Enter(hangar, repairScreen.SelectedBay);
			}

			var player = hangar.Player;
			Console.WriteLine($"Crew: {hangar.SquadPositions} squad positions in play, "
				+ $"{hangar.SquadMembers.Count} squad members; "
				+ (player != null ? $"player {player.Name} in bay {player.Bay}; " : "no player record; ")
				+ $"the entry leaves bay {crewScreen.SelectedBay} selected.");
		}

		// Rasterizes the current tab's content and hands it to the renderer. Called on a state change
		// rather than per frame: it resolves a whole canvas of palette indices and uploads a texture.
		// The whole surface is painted each time, where the original repaints only the widgets that
		// moved. Rows overlap by a pixel and whichever paints second owns the shared border row, so
		// each screen paints its selected row last, as Repair_SelectHotspot's incoming repaint lands.
		void RepaintContent() {
			if (renderer == null) {
				return;
			}

			// Until the startup sequence has put it up, the menu is hidden and the sequence is all there is.
			if (startup is { Done: false }) {
				renderer.SetContent(null);
				return;
			}

			contentSurface.Clear();

			// REPLAY MISSION? stands on a picture of the backdrop over the whole display, which is what the
			// renderer draws beneath an empty content, so the dialog is all there is to paint.
			if (replayDialog.IsOpen) {
				replayDialog.Paint(contentSurface, art.Text, art.Sprites);
				renderer.SetContent(contentSurface);
				return;
			}

			// Tabs 2-7 switch palette through the scope, whose paint blacks out everything below the
			// strip; the tab's screen, if one is ported, draws over that. The lunar movie plays over the
			// same fill.
			bool filled = ShellPalette.FillsScope(screen.SelectedTab) || scopeFilled;
			if (filled) {
				ShellPalette.PaintScope(contentSurface);
			}

			switch (screen.SelectedTab) {
				case ShellScreen.MainMenuTab when practiceUp:
					practiceScreen!.Paint(contentSurface, art.Text, art.Sprites);
					break;
				case ShellScreen.MainMenuTab when preferencesUp:
					preferencesScreen!.Paint(contentSurface, art.Text, art.Sprites);
					break;
				case ShellScreen.MainMenuTab when registrationUp:
					registration.Paint(contentSurface, art.Text, art.Sprites,
						focused: pointer.Focused is { Kind: ShellWidgetKind.RegistrationField });
					break;
				case ShellScreen.MainMenuTab:
					mainMenu.Paint(contentSurface, art.Text, art.Sprites);
					break;
				case ShellScreen.RepairTab:
					repairScreen.Paint(contentSurface, art.Text, art.Sprites);
					break;
				case ShellScreen.SaveTab:
					saveScreen.Paint(contentSurface, art.Text, art.Sprites,
						pointer.Focused is { Kind: ShellWidgetKind.SaveRow } focused ? focused.Index : null);
					break;
				case ShellScreen.BuildTab when buildScreen != null:
					buildScreen.Paint(contentSurface, art.Text, art.Sprites);
					break;
				case ShellScreen.WeaponsTab when weaponsScreen != null:
					weaponsScreen.Paint(contentSurface, art.Text, art.Sprites);
					break;
				case ShellScreen.CrewTab when crewScreen != null:
					crewScreen.Paint(contentSurface, art.Text, art.Sprites);
					break;
				case ShellScreen.ArmoryTab when armoryScreen != null:
					armoryScreen.Paint(contentSurface, art.Text, art.Sprites);
					break;
				case ShellScreen.MissionTab:
					missionScreen.Paint(contentSurface, art.Text, art.Sprites, pointer.Lit);

					// PLACEHOLDER: the map panel is left bare while the briefing movie is queued or playing,
					// its intro not yet begun; what the original's panel shows then is not known.
					if (missionViewUp == ShellMissionView.Briefing && !BriefingMoviePending()) {
						missionMap?.Paint(contentSurface, mapArt, MapClock());
					}

					break;
				default:
					if (!filled) {
						renderer.SetContent(null);
						return;
					}

					break;
			}

			scrapDialog.Paint(contentSurface, art.Text, art.Sprites);
			weaponScrapDialog.Paint(contentSurface, art.Text, art.Sprites);
			launchRefusal.Paint(contentSurface, art.Text, art.Sprites);
			endOfGame.Paint(contentSurface, art.Text, art.Sprites);
			renderer.SetContent(contentSurface);
		}

		// Which palette a tab is drawn through: Shell_SelectTabPalette (0043b162)'s, unless --shell-palette
		// pins one entry everywhere. A tab with no screen ported shows only the strip over the scope's black
		// fill, so its palette colours the strip alone, as retail's does.
		string PaletteFor(int tab) {
			if (paletteName != null) {
				return paletteName;
			}

			return ShellPalette.ForTab(tab, MissionView(), campaignStage) is { } index
				&& ShellPalette.Name(index) is { } name
				? name : ShellArt.DefaultPaletteName;
		}

		ShellMissionView MissionView() =>
			debriefUp ? ShellMissionView.Debriefing
			: !missionMapShown && missionInStage == 0 ? ShellMissionView.Map : ShellMissionView.Briefing;

		// The original writes an index into the palette widget and shows it; here the whole of the art
		// is decoded through one palette at load, so a change means loading it again and rebuilding the
		// renderer's textures. That is a few milliseconds on a click, and it happens only when the
		// palette actually changes.
		void SwitchPalette(int tab) => LoadPalette(PaletteFor(tab));

		void LoadPalette(string name) {
			if (gl == null || string.Equals(name, art.PaletteName, StringComparison.OrdinalIgnoreCase)) {
				return;
			}

			if (ShellArt.Load(content, name) is not { } reloaded) {
				Console.WriteLine($"Palette {name} could not be loaded — keeping {art.PaletteName}.");
				return;
			}

			art = reloaded;
			renderer?.Dispose();
			renderer = new ShellRenderer(gl, art);

			// The new renderer has no content texture, and the old one's was resolved through the old
			// palette anyway — so the tab's content is rasterized again through the palette it is now
			// being drawn in. Activate calls RepaintContent after this returns.
			Console.WriteLine($"Palette dpl\\{name}.DPL.");
		}
	}

	/// <summary>The map's timer, <c>Shell_TimerTicks</c> (<c>00465a1c</c>): <c>GetTickCount()</c> in units of 16 ms.</summary>
	private static uint MapClock() => (uint)(Environment.TickCount64 >> 4);

	/// <summary>The tabs this engine has a screen behind.</summary>
	private static bool HasScreen(int tab) =>
		tab is ShellScreen.MainMenuTab or ShellScreen.SaveTab or ShellScreen.WeaponsTab or ShellScreen.RepairTab or ShellScreen.BuildTab
			or ShellScreen.ArmoryTab or ShellScreen.CrewTab or ShellScreen.MissionTab;

	/// <summary><c>prefs.cfg</c> option 45, VSHELL's <c>Weapons Building:</c> — 1 builds weapons by hand (docs/simulation/preferences.md).</summary>
	private const int WeaponsBuildingOption = 45;

	/// <summary><c>prefs.cfg</c> option 44, VSHELL's <c>Repair Options:</c> (docs/simulation/preferences.md).</summary>
	private const int RepairOption = 44;

	/// <summary><c>prefs.cfg</c> option 6, <c>Display Mode</c>: 0 a window, 1 full screen.</summary>
	private const int DisplayModeOption = 6;

	/// <summary><c>prefs.cfg</c> option 42, the campaign-or-training flag <c>Shell_SetCampaignMode</c> (<c>0040e69e</c>) writes.</summary>
	private const int CampaignModeOption = 42;

	/// <summary>
	/// <c>prefs.cfg</c> option 46, which of the three demo missions the next <c>INSTANT ACTION</c> plays,
	/// stepped modulo <see cref="InstantActionMissionCount"/> after each.
	/// </summary>
	private const int InstantActionOption = 46;
	private const int InstantActionMissionCount = 3;

	/// <summary>
	/// The game state (<c>0048260e</c>) a game goes on from — the debrief's; the others are why it ended
	/// (docs/shell/campaign-loop.md#where-the-debrief-goes-next).
	/// </summary>
	private const int ContinuingGameState = 2;

	/// <summary>
	/// <c>VIEW DEMO</c>'s exit code, <c>Shell_SetExitCode(5)</c>, which the launcher answers by starting the
	/// simulator with <c>-D</c>; the caller answers it with a demo tape.
	/// </summary>
	public const int DemoExitCode = 5;

	/// <summary>The state <c>ES.EXE</c>'s loop starts in, which runs the shell as a first start (docs/command-line.md#the-loop).</summary>
	public const int StartupCode = 1;

	/// <summary>
	/// The simulator's code for a destroyed player in <c>MissionModeFlag</c>, which the shell takes into the
	/// debrief as it does 3. The simulator never returns it (<see cref="MissionResults.ExitCode"/>).
	/// </summary>
	public const int DebriefDestroyedCode = 4;

	/// <summary>Whether a simulator exit code brings the shell back up rather than ending the run: 3, 4 and 6.</summary>
	public static bool ReturnsToShell(int code) =>
		code is MissionResults.DebriefExitCode or DebriefDestroyedCode or MissionResults.DemoExitCode;
}
