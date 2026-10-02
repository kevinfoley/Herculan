using System.Numerics;
using HercWorks.Core.Data.Struct.Herc;
using Herculan.Engine;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Host;
using Herculan.Engine.Host.Debugging;
using Herculan.Engine.Host.Install;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Host.Settings;
using Herculan.Engine.Input;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Settings;
using Herculan.Engine.Shell;
using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.Sim.Anim;
using Herculan.Engine.World;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using InputTape = HercWorks.Core.Data.File.Dbsim.InputTape;
using HercWorks.Core.Data.File.Cfg;

// The thin front-end host from docs/engine/planning.md's "Engine internal architecture" section:
// it locates an install, asks the engine to build a scene from a real mission, and runs a real-time
// loop over it. Everything it does is wiring — no simulation rules, no rendering rules, no file
// formats — so a second host (the mission editor that decision exists to keep possible) can differ
// here and reuse everything below it unchanged.

var positional = new List<string>();
string? screenshotPath = null;
MfdMode? initialMfdMode = null;
bool startOnHeadsDown = false;
HddPage initialHddPage = CockpitHudState.Default.Hdd;
HddDamageView initialHddDamageView = CockpitHudState.Default.HddDamage;
short initialThrottle = 0;
int? initialHeading = null;
bool startExternal = false;
short heldTwist = 0;
short heldPitch = 0;
int? initialWeaponRow = null;
bool initialLink = false;
bool heldFire = false;
bool stageHitShake = false;
bool acquireTarget = false;
bool autoTrack = false;
bool waitForEffectLight = false;
bool silentAudio = false;
string? cdDrive = null;
string? musicDirectory = null;
int musicTrackSelect = 0;
int initialHddPilot = -1;
int initialHddSubject = HddDamageSubject.PlayerSlot;
HddOrder? initialHddOrder = null;
bool initialHddTransmit = false;

// Set by --hdd-xmit, and called again once the run has ticked so a standing order can be seen to
// survive its own reassess rather than only to have been installed.
Action<string>? reportSquadOrders = null;
bool runShell = false;
string? moviePath = null;
string? shellPalette = null;
ShellCampaignMode? shellMode = null;
int shellTab = ShellScreen.MainMenuTab;
int shellBay = 0;
bool shellPractice = false;
bool shellWindowed = false;
bool shellMovies = true;

// Ticks to let the sensor model run before --target takes its pick: nothing is targetable until a
// sweep has painted it, and the sweep only runs from the world tick.
int acquireTargetDelay = 5;
int initialFlashCommRow = -1;
bool initialFlashCommTransmit = false;
bool waitForTransmission = false;
bool startWithObjectives = false;
bool startWithPreferences = false;
bool startWithControls = false;
var stagedJoystick = JoystickCapabilities.None;
bool probeJoystick = false;
bool writeJoystickMap = false;
bool writePreferences = true;
bool startWithStatusAlert = false;
int stagedStatusAlert = -1;
string? playTape = null;
bool demoTape = false;
string? recordTape = null;
bool developerMode = false;
var argumentErrors = new List<string>();
for (int i = 0; i < args.Length; i++) {
	if (HostArguments.IsHelp(args[i])) {
		Console.WriteLine(HostArguments.Usage);
		return 0;
	} else if (args[i] == "--screenshot") {
		if (HostArguments.TryReadString(args, ref i, argumentErrors, out string path)) {
			screenshotPath = path;
		}
	} else if (args[i] == "--mfd") {
		// Which MFD screen to power up on. F1-F6 switch it live; this exists so a --screenshot run,
		// which never sees a keystroke, can be pointed at a specific screen.
		if (HostArguments.TryReadInt(args, ref i, 0, 5, argumentErrors, out int mfdIndex)) {
			initialMfdMode = (MfdMode)mfdIndex;
		}
	} else if (args[i] == "--quit") {
		// Power up with the [Q] mission-status alert already raised, for the same reason as
		// --objectives. With no argument it shows whatever the mission evaluates to at that moment,
		// which is what pressing [Q] would give; an optional status number forces one of the twenty
		// GNL_ALRT.STR rows instead, so the one-button and single-line layouts — and 0 and 1, the
		// pause panel's own two — can be looked at without arranging a mission that produces them.
		startWithStatusAlert = true;
		if (HostArguments.TryReadOptionalInt(args, ref i, 0, StatusAlertPanel.StatusCount - 1, out int forcedStatus)) {
			stagedStatusAlert = forcedStatus;
		}
	} else if (args[i] == "--objectives") {
		// Power up with the [F11] objectives panel already open, for the same reason as --mfd: a
		// --screenshot run never sees a keystroke.
		startWithObjectives = true;
	} else if (args[i] == "--preferences") {
		// Likewise for the [F12] preferences panel.
		startWithPreferences = true;
	} else if (args[i] == "--controls") {
		// And for the CONTROLS panel it raises, which opens over it.
		startWithPreferences = true;
		startWithControls = true;
	} else if (args[i] == "--joystick") {
		// Pretends a fully-featured stick is attached, so the CONTROLS panel's rows go live and show
		// what prefs.cfg has them bound to even where there is no hardware — a --screenshot run has
		// none. A real device, when one is attached, overrides this. An optional count sets how many of
		// the eight button rows are live.
		stagedJoystick = new JoystickCapabilities(Present: true,
			ButtonCount: JoystickCapabilities.MaxButtons,
			HasThrottle: true, HasRudder: true, HasHat: true);
		if (HostArguments.TryReadOptionalInt(args, ref i, 0, JoystickCapabilities.MaxButtons, out int stickButtons)) {
			stagedJoystick = stagedJoystick with { ButtonCount = stickButtons };
		}
	} else if (args[i] == "--joystick-probe") {
		// Prints each axis and button of the attached stick as it moves. Its whole purpose is filling
		// in data\herculan-joystick.cfg by hand: a modern stick's axis order is its own, and nothing
		// but moving each control in turn will say which index is the throttle.
		probeJoystick = true;
	} else if (args[i] == "--write-joystick-map") {
		// Writes the map the engine is actually using out to data\herculan-joystick.cfg, comments and
		// all, so there is something to edit rather than a file to compose from scratch. With no file
		// there already, that is the device's own reported shape in retail's X/Y/Z/R order.
		writeJoystickMap = true;
	} else if (args[i] == "--no-write-prefs") {
		// Leaves data\prefs.cfg alone. Saving is on by default, as it is in the original: each panel
		// writes its own options back as it closes. This is the way out for anyone who would rather
		// their retail install were not touched at all.
		writePreferences = false;
	} else if (args[i] == "--hdd") {
		// Power up already panned down to the Heads-Down Display, for the same reason as --mfd: a
		// --screenshot run never sees a keystroke. An optional 0 or 1 picks which of its two screens
		// to land on — the command display or the damage detail, as [F7] and [F8] do live.
		startOnHeadsDown = true;
		if (HostArguments.TryReadOptionalInt(args, ref i, 0, 1, out int hddIndex)) {
			initialHddPage = (HddPage)hddIndex;
		}
	} else if (args[i] == "--throttle") {
		// Power up with the throttle already open, for the same reason as --mfd and --hdd: a
		// --screenshot run never sees a keystroke, and a walking machine is the only way to see the
		// gait, the cockpit bob or the slider anywhere but its centre. ±1024 is full travel.
		if (HostArguments.TryReadInt(args, ref i, int.MinValue, int.MaxValue, argumentErrors, out int throttleSetting)) {
			initialThrottle = (short)Math.Clamp(throttleSetting, -ThrottleTrack.Full, ThrottleTrack.Full);
		}
	} else if (args[i] == "--heading") {
		// Point the machine's lower body somewhere other than along the first leg of its route, which
		// is where the mission spawns it. A --screenshot run never sees a steering key, so this is the
		// only way to reach anything the body's own heading drives — the compass tape, and the
		// waypoint indicator's off-tape arrows. A binary angle: 0x4000 is a quarter turn.
		if (HostArguments.TryReadInt(args, ref i, int.MinValue, int.MaxValue, argumentErrors, out int headingAngle)) {
			initialHeading = headingAngle;
		}
	} else if (args[i] == "--turret") {
		// Hold the two turret axes for the whole run, for the same reason as --throttle: a
		// --screenshot run never sees a keystroke, and the turret only moves while a key is held.
		// ±256 is full deflection on each.
		if (HostArguments.TryReadInt(args, ref i, int.MinValue, int.MaxValue, argumentErrors, out int twistAxis)
				&& HostArguments.TryReadInt(args, ref i, int.MinValue, int.MaxValue, argumentErrors, out int pitchAxis, "--turret")) {
			heldTwist = (short)Math.Clamp(twistAxis, -MechControls.AxisFull, MechControls.AxisFull);
			heldPitch = (short)Math.Clamp(pitchAxis, -MechControls.AxisFull, MechControls.AxisFull);
		}
	} else if (args[i] == "--weapon") {
		// Arm a weapon panel row at power-up, 1-based as the row prints it, and optionally link it.
		// Same reason as --mfd and --throttle: a --screenshot run never sees a keystroke, and the
		// armed row and a linked pair are only visible once something has selected one.
		if (HostArguments.TryReadInt(args, ref i, 1, 10, argumentErrors, out int weaponRow)) {
			initialWeaponRow = weaponRow - 1;
		}
	} else if (args[i] == "--link") {
		initialLink = true;
	} else if (args[i] == "--hit-shake") {
		// Take a hit on the cockpit, for the same reason as --fire: a --screenshot run never sees one
		// land, and the shake and its palette flash are gone again in under a second. The capture then
		// holds until the flash is actually up, so the frame photographed is a red one.
		stageHitShake = true;
	} else if (args[i] == "--fire") {
		// Hold the trigger down for the whole run, for the same reason as --turret: a --screenshot
		// run never sees a keystroke, and a beam is only on screen for the tick after it was fired.
		heldFire = true;
	} else if (args[i] == "--impact") {
		// Hold the capture until an impact effect is carrying a light, for the same reason as
		// --fire: rounds land on their own once the trigger is held, but a light lasts about two
		// thirds of a second and a fixed frame count is as likely to photograph the gap between two
		// as one of them. Only useful alongside --fire and --screenshot.
		waitForEffectLight = true;
	} else if (args[i] == "--target") {
		// Acquire a target at power-up, for the same reason as --weapon and --mfd: a --screenshot run
		// never sees a keystroke, and the HUD's target box, the reticle's on-target frame and the F5
		// screen all have nothing to show until something is selected. It switches the scanner on
		// first, as [R] does, because a passive HERC can only see what it has eyes on.
		acquireTarget = true;
	} else if (args[i] == "--track") {
		// Power up with Automatic Turret Tracking latched, for the same reason as --target, which it
		// only does anything alongside: a --screenshot run never sees a keystroke, and the turret
		// only slews on its own once ATT has something to hold.
		autoTrack = true;
	} else if (args[i] == "--movie") {
		// Play one cutscene instead of a mission — see MovieHost. Takes a path, or a name to look up
		// in the install's AVI folder. It exists so a video decoder can be looked at rather than only
		// asserted about; see docs/formats/avi-video.md.
		if (HostArguments.TryReadString(args, ref i, argumentErrors, out string movie)) {
			moviePath = movie;
		}
	} else if (args[i] == "--shell") {
		// Run the front end instead of a mission — see ShellHost. It shares the install lookup below
		// and nothing else, so it takes over before any mission loading happens.
		runShell = true;
	} else if (args[i] == "--shell-palette") {
		// Which dpl\<name>.DPL the shell decodes its art through, pinned for the whole run. Without it
		// the palette follows the tab, as the original's does — see ShellPalette for the table and for
		// which screen picks which entry.
		if (HostArguments.TryReadString(args, ref i, argumentErrors, out string palette)) {
			shellPalette = palette;
		}
		runShell = true;
	} else if (args[i] == "--shell-tab") {
		// Which tab the shell comes up on, 0-7. The original always enters on the main menu; this is here
		// so --screenshot can land on a tab that has content, and so the save screen is one argument away
		// rather than a click away.
		if (HostArguments.TryReadInt(args, ref i, 0, ShellLayout.TabCount - 1, argumentErrors, out int requestedTab)) {
			shellTab = requestedTab;
		}
		runShell = true;
	} else if (args[i] == "--shell-bay") {
		// Which hangar bay the repair tab opens on, 0-7 — DAT_00482ae5, which the squad roster moves once
		// the screen is up.
		if (HostArguments.TryReadInt(args, ref i, 0, ShellHangar.BayCount - 1, argumentErrors, out int requestedBay)) {
			shellBay = requestedBay;
		}
		runShell = true;
	} else if (args[i] == "--shell-training") {
		// Run the front end in training mode rather than the mode prefs.cfg option 42 holds — DAT_0048260c,
		// the flag that gates REPAIR, BUILD and ARMORY off. PRACTICE MISSIONS sets it too, but only from the
		// main menu, where the strip hides nothing; this is how the gated strip is reachable on the other tabs.
		shellMode = ShellCampaignMode.Training;
		runShell = true;
	} else if (args[i] == "--shell-practice") {
		// Come up on the practice screen, as the main menu's PRACTICE MISSIONS puts it up, so --screenshot
		// can land on it.
		shellPractice = true;
		shellTab = ShellScreen.MainMenuTab;
		runShell = true;
	} else if (args[i] == "--shell-windowed") {
		// Keep the front end windowed at startup whatever prefs.cfg option 6 says. This engine's own flag:
		// retail's -d reads as the same switch, and its store is overwritten before anything reads it.
		shellWindowed = true;
		runShell = true;
	} else if (args[i] == "--shell-no-movies") {
		// Turn the shell's movies off — retail's -a, which clears DAT_00482275 so the movie queue takes
		// nothing and plays nothing. See ShellMovieQueue.
		shellMovies = false;
		runShell = true;
	} else if (args[i] == "--cd-drive") {
		// Which drive the music CD is in. Retail asks MCI for the device type alone and takes whichever
		// CD drive it answers with -- nothing in either executable reads a drive letter from anywhere --
		// so this is the engine's own, for a machine with more than one drive. See CdAudio.Open.
		if (HostArguments.TryReadString(args, ref i, argumentErrors, out string drive)) {
			cdDrive = drive;
		}
	} else if (args[i] == "--music-dir") {
		// A directory of Track02.wav ... Track07.wav to play instead of the disc -- the engine's own,
		// for a machine with no drive. See WaveFileMusicSource.
		if (HostArguments.TryReadString(args, ref i, argumentErrors, out string musicFolder)) {
			musicDirectory = musicFolder;
		}
	} else if (args[i] == "--music") {
		// DBSIM's own -R<n>: the mission's track is n % 5 + 2, so 0-4 pick tracks 2 to 6. Retail's
		// launcher passes its count of simulator launches here, as --shell's loop does, counting on from
		// this value; a lone mission without the switch plays track 2.
		if (HostArguments.TryReadInt(args, ref i, 0, int.MaxValue, argumentErrors, out int trackSelect)) {
			musicTrackSelect = trackSelect;
		}
	} else if (args[i] == "--no-sound" || args[i] == "--silent") {
		// Skip the output device entirely. Same effect as running on a machine with no sound card:
		// the catalog, the director and the message port all still run, nothing is heard. For a
		// --screenshot run, an automated run, or anywhere the noise is not what is being looked at.
		silentAudio = true;
	} else if (args[i] == "--external") {
		// Power up in the outside view, for the same reason as --mfd and --throttle: the player's own
		// machine is the one thing the cockpit view never shows, so a --screenshot run has no other way
		// to see its own legs move. It is [V] pressed at launch, so it lands two ticks in.
		startExternal = true;
	} else if (args[i] == "--hdd-damage") {
		// Which component category the damage screen powers up listing. [S], [I] and [W] switch it
		// live; this is the same reason --mfd and --hdd exist.
		if (HostArguments.TryReadInt(args, ref i, 0, 2, argumentErrors, out int damageIndex)) {
			initialHddDamageView = (HddDamageView)damageIndex;
		}
	} else if (args[i] == "--hdd-subject") {
		// Whose herc the damage screen powers up inspecting, by selector slot. The arrows step it live;
		// this exists for the same reason --hdd-damage does.
		if (HostArguments.TryReadInt(args, ref i, 0, HddDamageSubject.SlotCount - 1, argumentErrors, out int subjectSlot)) {
			initialHddSubject = subjectSlot;
		}
	} else if (args[i] == "--hdd-pilot") {
		// Which comm box the command display powers up with selected, and which order it powers up
		// armed. [1]-[3] and the order hotkeys do both live; these exist for the same reason
		// --hdd-damage does, and because the order list only leaves its unavailable blue once a pilot
		// is selected — a screenshot run has no other way to reach that.
		if (HostArguments.TryReadInt(args, ref i, 0, HddLayout.PilotSlotCount - 1, argumentErrors, out int pilotSlot)) {
			initialHddPilot = pilotSlot;
		}
	} else if (args[i] == "--hdd-order") {
		if (HostArguments.TryReadInt(args, ref i, 0, HddLayout.OrderCount - 1, argumentErrors, out int orderIndex)) {
			initialHddOrder = (HddOrder)orderIndex;
		}
	} else if (args[i] == "--flash-comm") {
		// Which FLASH COMM row the cursor sits on at power-up. The seven order letters do it live;
		// this exists for the same reason --mfd does.
		if (HostArguments.TryReadInt(args, ref i, 0, MfdFlashCommScreen.RowCount - 1, argumentErrors, out int flashCommRow)) {
			initialFlashCommRow = flashCommRow;
		}
	} else if (args[i] == "--flash-comm-xmit") {
		// Presses XMIT on that row once the mission is up — the [X] key, or a click on the button, or
		// a second click on the row. A --screenshot run sees no keystroke, so this is the only way to
		// reach the squad broadcast and the reply that comes back from it.
		initialFlashCommTransmit = true;
	} else if (args[i] == "--wait-transmission") {
		// Holds the capture until a squadmate's portrait is actually up rather than the static either
		// side of it, which is the one frame worth photographing. Only useful with --flash-comm-xmit.
		waitForTransmission = true;
	} else if (args[i] == "--hdd-xmit") {
		// Presses XMIT on the armed order once the screen is up, and reports what the squad did with
		// it. A --screenshot run sees no keystroke and no map click, so this is the only way to reach
		// the squadmate AI from the command line; an order that wants a pick takes the map centre.
		initialHddTransmit = true;
	} else if (args[i] == "--play") {
		// DBSIM's own -p<name>: replay an input tape — a path, or a stem looked up in the install's
		// TAPES folder, as -p's own "tapes\demo1" is. The mission comes out of the tape, so it replaces
		// the positional mission argument. See InputTapePlayer.
		if (HostArguments.TryReadString(args, ref i, argumentErrors, out string tape)) {
			playTape = tape;
		}
	} else if (args[i] == "--record") {
		// DBSIM's own -r<name>: record this mission's input to <name>.tap, which --play replays. The
		// extension is forced, as -r forces it. See InputTapeRecorder.
		if (HostArguments.TryReadString(args, ref i, argumentErrors, out string tape)) {
			recordTape = Path.ChangeExtension(tape, InputTapePlayer.Extension);
		}
	} else if (args[i] == "--developer") {
		// DBSIM's own -SPRUNKNOWN: the developer keys. See DeveloperKeys and docs/key-bindings.md.
		developerMode = true;
	} else if (args[i] == "--demo") {
		// DBSIM's own -D: a tape picked from TAPES\demolist.str, as VIEW DEMO plays one, which ends the
		// mission when it runs out or the moment a key is pressed. With --play, that tape instead.
		demoTape = true;
	} else if (args[i].StartsWith("--")) {
		argumentErrors.Add($"Unknown option {args[i]}.");
	} else {
		positional.Add(args[i]);
	}
}

if (recordTape != null && (playTape != null || demoTape)) {
	argumentErrors.Add("--record cannot be combined with --play or --demo.");
}
if (positional.Count > 2) {
	argumentErrors.Add($"Unexpected argument {positional[2]}: the only positional arguments are the install and the mission.");
}
if (argumentErrors.Count > 0) {
	foreach (string error in argumentErrors) {
		Console.Error.WriteLine(error);
	}
	Console.Error.WriteLine("Run with --help for the list of options.");
	return 1;
}

// Host-lifetime, not mission-lifetime: neither reads the install, and both need to survive into
// --shell and --movie once those have a menu bar of their own to raise TweaksMenu from — see
// docs/engine/planning.md. Built before that branch so nothing below has to change when they do.
var localization = new LocalizationTable();
TweakSettings.Current.LoadFromDisk();

// The one font every ImGui window the host opens is drawn in.
string imguiFontPath = Path.Combine(AppContext.BaseDirectory,
	"Assets", "Fonts", "Open_Sans", "static", "OpenSans-Regular.ttf");

// An install named on the command line is never second-guessed, and a --screenshot run has nobody to ask,
// so only a search that came up empty asks the player.
string? installRoot = GameInstall.Locate(positional.Count > 0 ? positional[0] : null);
if (installRoot == null && positional.Count == 0 && screenshotPath == null) {
	installRoot = InstallPrompt.Run(localization, imguiFontPath);
	if (installRoot == null) {
		return 1;
	}
}
if (installRoot == null) {
	Console.Error.WriteLine(
		"Could not find an Earthsiege 2 installation.\n" +
		$"Pass its path as the first argument, or set {GameInstall.PathVariable}.\n" +
		$"The path should be the folder containing the '{GameInstall.ArchiveFolderName}' directory.");
	return 1;
}
GameInstall.Remember(installRoot);

// --movie shares even less: no archives, no zone, no shell art — one file and a quad. See MovieHost.
if (moviePath != null) {
	return MovieHost.Run(installRoot, moviePath, screenshotPath, silentAudio);
}

// Without --shell the one mission named runs. The codes that would bring a shell back up mean nothing with no
// launcher to read them, so a mission that ends normally leaves the host with 0.
if (!runShell) {
	int missionExit = RunMission(null, demoTape, musicTrackSelect);
	return ShellHost.ReturnsToShell(missionExit) ? 0 : missionExit;
}

// ES.EXE's loop (docs/command-line.md#the-loop): the front end and the simulator in turn, each one's exit code
// the next one's state. Rock & Roll closes the shell on 2 and hands back a mission; VIEW DEMO closes it on 5,
// which the launcher answers with -D, a demo tape; the mission's own code then brings the shell back up, 3 into
// the debrief and 6 after a demo; and 0, QUIT's, ends the run. The original spawns an executable per turn; here
// each is a window in this one process. Each handoff sits in a scratch folder, so the simulator's settings are
// still the install's. The --shell staging flags stage the first turn only; each later turn starts in the mode
// the last one ended in. Every simulator launch is handed the count of those before it, as -R<n>.
int state = ShellHost.StartupCode;
int launches = 0;
while (true) {
	bool firstTurn = state == ShellHost.StartupCode;
	(int shellExit, ShellLaunch? launched, shellMode) = ShellHost.Run(installRoot, shellPalette, screenshotPath, shellMode,
		firstTurn ? shellTab : ShellScreen.MainMenuTab, shellBay, firstTurn && shellPractice, silentAudio, writePreferences,
		shellWindowed, shellMovies, state);
	if (launched != null) {
		state = RunMission(launched, false, musicTrackSelect + launches++);
	} else if (shellExit == ShellHost.DemoExitCode) {
		if (recordTape != null) {
			Console.Error.WriteLine("--record cannot be combined with VIEW DEMO's tape.");
			return 1;
		}

		state = RunMission(null, true, musicTrackSelect + launches++);
	} else {
		return shellExit;
	}

	if (!ShellHost.ReturnsToShell(state)) {
		return state;
	}
}

// The simulator's turn: one mission, from a shell launch, a demo tape or the command line, run until its
// window closes, with -R<n>'s track select. Returns its exit code (Sim_Shutdown, 00461eec).
int RunMission(ShellLaunch? shellLaunch, bool demoTape, int trackSelect) {
	// An input tape to replay, which brings its own mission. -p and -D unpack the tape's bundle over the
	// install's data\; this unpacks it over a copy of it instead, so a replay leaves the install alone.
	InputTapePlayer? tapePlayer = null;
	string? tapeScriptPath = null;
	if (playTape != null || demoTape) {
		string tapesDirectory = Path.Combine(installRoot, InputTapePlayer.TapesFolderName);
		string? stem = playTape ?? InputTapePlayer.PickDemo(tapesDirectory);
		string? tapePath = stem == null
			? null
			: new[] { stem, stem + InputTapePlayer.Extension, Path.Combine(tapesDirectory, stem + InputTapePlayer.Extension) }
				.FirstOrDefault(File.Exists);
		if (tapePath == null) {
			Console.Error.WriteLine(stem == null
				? $"No tape to play: {Path.Combine(tapesDirectory, InputTapePlayer.DemoListFileName)} is missing or empty."
				: $"No tape at {stem}, {stem}{InputTapePlayer.Extension} or in {tapesDirectory}.");
			return 1;
		}

		tapePlayer = InputTapePlayer.Load(tapePath, demoTape);
		if (tapePlayer == null) {
			Console.Error.WriteLine($"{tapePath} is not an input tape.");
			return 1;
		}

		tapeScriptPath = tapePlayer.ExtractBundle(
			Path.Combine(Path.GetTempPath(), "herculan-tape", Path.GetFileNameWithoutExtension(tapePath)),
			Path.Combine(installRoot, MissionLoader.DataFolderName));

		var frames = tapePlayer.Tape.Frames;
		Console.WriteLine($"Tape {tapePlayer.Name}: {frames.Count} frames, {tapePlayer.RecordedSeconds:0.0} s as recorded, "
			+ $"unpacked to {Path.GetDirectoryName(tapeScriptPath)}. "
			+ (demoTape ? "Any key ends the demo." : "Ctrl+E stops it and hands the controls over."));
		foreach (var (first, last) in tapePlayer.InferredPanelSpans) {
			Console.WriteLine($"  Frames {first}-{last} hold one SimTickDelta ({frames[first].TickDelta}): the "
				+ "recording probably had a modal panel up across them.");
		}
	}

	// A mission named by its .MSN is loaded as the shell would load it and flown from a scratch handoff, with
	// the install's DATA\ still holding the simulator's settings, as a shell launch's does. See MissionFileLaunch.
	if (tapeScriptPath == null && shellLaunch == null && positional.Count > 1 && MissionFileLaunch.Names(positional[1])) {
		if (MissionFileLaunch.Write(installRoot, positional[1], out string? failure) is not { } generated) {
			Console.Error.WriteLine($"Cannot load {positional[1]}: {failure}");
			return 1;
		}

		shellLaunch = new ShellLaunch(generated, Path.Combine(installRoot, MissionLoader.DataFolderName));
	}

	// The mission handoff VSHELL writes and DBSIM reads. It states its own zone and theater, so nothing
	// else here needs configuring. Any of the save-slot snapshots in SAV\ works as an alternative.
	string scriptPath = tapeScriptPath
		?? shellLaunch?.ScriptPath
		?? (positional.Count > 1 ? positional[1] : MissionLoader.DefaultScriptPath(installRoot));
	if (!File.Exists(scriptPath)) {
		Console.Error.WriteLine(
			$"No mission at {scriptPath}.\n" +
			$"Pass one as the second argument — {MissionLoader.ScriptFileName} from the install's " +
			$"{MissionLoader.DataFolderName} folder, any of the SAV\\script*.dat snapshots, or a mission's .MSN name.");
		return 1;
	}

	Console.WriteLine($"HERCULAN Engine — loading {scriptPath} from {installRoot}");

	var content = GameContent.Mount(GameInstall.ArchiveDirectory(installRoot));
	Console.WriteLine($"Mounted archives: {string.Join(", ", content.MountedArchives)}");

	var scene = MissionScene.Load(content, scriptPath, shellLaunch?.DataDirectory);
	var mission = scene.Mission;
	var terrain = scene.World.Terrain;

	// Audio comes up against the same mounted archives and shares the simulation's generator, because
	// the variation roll draws on it exactly as weapon scatter does. It never throws: a machine with no
	// device gets a working GameAudio that happens to be silent.
	var audio = GameAudio.Create(content, scene.World.PresentationRandom, silent: silentAudio, cdDrive: cdDrive,
		musicDirectory: musicDirectory);
	audio.Attach(scene.World);
	Console.WriteLine($"Audio: {audio.Status}");

	Console.WriteLine(
		$"Mission: zone {mission.Header.ZoneIndex}, theater {mission.Header.TheaterIndex}" +
		$" variant {mission.Header.TheaterVariant}.");
	Console.WriteLine(
		$"Zone {mission.Header.ZoneIndex}: {terrain.Width}x{terrain.Height} cells, {terrain.CellSize} units per cell, " +
		$"height scale {terrain.HeightScale}, peak {terrain.MaxWorldHeight} units.");
	Console.WriteLine(
		$"Placed {scene.Objects.Count} objects — {mission.CountOf(MissionUnitKind.Mech)} mechs, " +
		$"{mission.CountOf(MissionUnitKind.Flyer)} flyers, {mission.CountOf(MissionUnitKind.Base)} structures — " +
		$"from {scene.Models.Count} distinct models.");

	int awaitingDeployment = scene.Objects.Count(o => o.Object.AwaitingDeployment);
	if (awaitingDeployment > 0) {
		Console.WriteLine(
			$"{awaitingDeployment} of them are waiting on a mission action and are not in the mission " +
			"yet — undrawn, unsimulated and non-solid until they arrive by drop pod or on foot.");
	}

	if (scene.UnmodelledCount > 0) {
		Console.WriteLine(
			$"{scene.UnmodelledCount} of them have no model (missing install files or an out-of-range " +
			"index); they are simulated and positioned but not drawn.");
	}

	Console.WriteLine(mission.Player is { } player
		? $"Player flies {player.TypeName} at {player.Position}."
		: "No player.mec beside the mission — camera starts at the first placed object.");

	foreach (var group in scene.Objects
			.GroupBy(o => (o.Placement.Kind, o.Placement.TypeName ?? $"base type {o.Placement.TypeIndex}"))
			.OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.Item2)) {
		var model = group.First().Model;
		string art = model == null
			? "no model"
			: model.Atlas is { } atlas
				? $"{model.Mesh.Length / 3} tris, {atlas.FrameCount} frames in {atlas.Width}x{atlas.Height}"
				: $"{model.Mesh.Length / 3} tris, untextured";
		Console.WriteLine($"  {group.Count()}x {group.Key.Item2} ({art})");
	}

	Console.WriteLine($"Theater {mission.Header.TheaterIndex} ({scene.Theater.PaletteName}): " + (scene.TerrainBank is { } bank
		? $"terrain bank {bank.BankName}, {bank.Atlas.FrameCount} frames in a {bank.Atlas.Width}x{bank.Atlas.Height} atlas, "
		  + $"{scene.TerrainMesh.Length / 3} triangles."
		: $"terrain bank could not be loaded — drawing {scene.TerrainMesh.Length / 3} triangles flat-shaded."));

	// Milestone 8: the player's own cockpit canopy art + HUD, drawn as three simultaneous panels
	// (front/left/right) rather than the original's single keyboard-panned view — see
	// docs/engine/planning.md's Milestone 8 section and docs/formats/cockpit-views.md for why. Falls back
	// to the old single full-window 3D view when there's no player.mec or its cockpit assets are missing
	// (e.g. a raw script.dat with no accompanying player.mec).
	// The theater's palette is the live palette — all 256 slots — with only this herc's own 24-entry
	// cockpit colour scheme installed over slots 42-65. See CockpitPalette.
	// Every other machine in the mission goes in with it: F5 draws the *target's* paper doll, and a doll
	// is that machine's own .HBA frames placed by its own .PDG, so both have to be resident.
	// The squad's portrait banks go in with them: a comm box talks with dba\PILOT<n>.DBA, n being that
	// pilot's roster index over three, and which three are in the mission is only known here.
	// The heads-down map's relief through the damage-flash palette, built beside the ordinary one below.
	HddMapRaster? hddMapFlashRaster = null;

	var squadPlacements = scene.Objects
		.Where(o => o.Placement.IsPlayerLance && !ReferenceEquals(o.Object, scene.PlayerObject?.Object))
		.Take(SquadCommChannel.SlotCount)
		.ToList();

	// The [F11] objectives panel. Built once with the mission's own block-13 text rather than on every
	// press, which is the one place this diverges from the original's own lifetime: it constructs the
	// panel, runs it and destroys it per press. Nothing in it changes during a mission.
	var objectivesPanel = ObjectivesPanel.Build(content, mission.ObjectiveTextRefs, mission.TextAt);

	// The two modal alert panels. Only one can be up at a time -- they are modal in the original, and
	// each owns the input while it is -- so they share the pointer's press state.
	var statusAlertPanel = StatusAlertPanel.Build(content);

	// The [F12] preferences panel, showing the install's own data\prefs.cfg — the same file the terrain
	// draw distance is already read out of, and the same folder the mission's script.dat came from. The
	// same folder holds keyjoy.cfg, the four axis-sense switches, and Herculan's own device map.
	string? dataDirectory = shellLaunch?.DataDirectory ?? Path.GetDirectoryName(scriptPath);
	// Nullable only long enough to answer "was there a file?": an install with no prefs.cfg should not
	// have one invented for it, and SimulatorPreferences.Save refuses to write where it did not read.
	// Everything downstream shares the one instance, so a rebinding made on the CONTROLS panel is the
	// same array the input layer reads.
	var loadedPreferences = SimulatorPreferences.Load(dataDirectory);
	var simulatorPreferences = loadedPreferences ?? SimulatorPreferences.Defaults();
	// Each panel writes its own options back as it closes, which is the original's own timing. --no-write-prefs
	// is the way out; a Defaults() instance has nowhere to write to and so is inert either way. A replay's
	// preferences are the tape's, in a scratch folder, so nothing there is worth writing.
	simulatorPreferences.SaveEnabled = writePreferences && tapePlayer == null;

	// -r writes its bundle before the mission starts, so the tape starts from the files as they are now,
	// before any panel writes its options back.
	InputTapeRecorder? tapeRecorder = null;
	if (recordTape != null) {
		try {
			tapeRecorder = InputTapeRecorder.Create(recordTape, scriptPath, dataDirectory);
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Cannot record to {recordTape}: {e.Message}");
			return 1;
		}

		Console.WriteLine($"Recording input to {tapeRecorder.Path}.");
	}

	// A replay applies the original's per-tick steps once per frame, as the recording did — see
	// SimMath.PerTickStepsScaled.
	SimMath.PerTickStepsScaled = tapePlayer == null;
	// The two gates the original's own panel reads. SfxManager being null greys its first four rows, and
	// VoiceArchivePresent (0049e9cd) -- which Voice_ArchiveExists (00459d6c) sets by trying to fopen the localised simvoice archive -- is
	// what lets the two message rows be stepped at all.
	bool soundAvailable = audio.Director != null;
	bool voiceAvailable = content.MountedArchives.Any(
		name => name.StartsWith("SIMVOIC", StringComparison.OrdinalIgnoreCase));

	// Prefs_Init (0045a19c): the option handlers, then the walk that runs each once over what was read,
	// then the voice check. Three of the original's five are registered here; TERRAIN TEXTURE and the
	// throttle row have handlers too, but this engine reads both bytes where they are used, every frame,
	// which comes to the same thing. The MUSIC and SOUNDS handlers skip their mute while the walk runs,
	// as the originals do under PrefsInitInProgress.
	if (audio.Director is { } optionDirector) {
		simulatorPreferences.RegisterHandler(Prefs.MusicOption,
			value => optionDirector.ApplyMusicOption(value != 0, simulatorPreferences.Initialising));
		simulatorPreferences.RegisterHandler(Prefs.SoundsOption,
			value => optionDirector.ApplySoundsOption(value != 0, simulatorPreferences.Initialising));
	}

	simulatorPreferences.RegisterHandler(Prefs.PilotMessageOption,
		value => audio.SpeechEnabled = value != 0);
	simulatorPreferences.ApplyAll();

	// With no voice archive both message rows are forced to TEXT ONLY, through the ordinary setter, so
	// the PILOT MESSAGE handler silences the speech channel with them.
	if (!voiceAvailable) {
		simulatorPreferences.Set(Prefs.PilotMessageOption, 0);
		simulatorPreferences.Set(Prefs.ComputerMessageOption, 0);
	}

	var preferencesPanel = PreferencesPanel.Build(content, simulatorPreferences,
		soundAvailable, voiceAvailable);

	// The CONTROLS panel the preferences panel raises. Which half of CTL_ALRT.STR it is, and which
	// twelve bytes of prefs.cfg it reads, both hang off whether the player's machine is the RAZOR --
	// DBSim_LoadScriptDat's own `playerMechType == 8`. It is built with whatever --joystick staged, which
	// is nothing by default: GLFW does not publish a device's shape until a frame after enumeration, so
	// the real capabilities arrive later through AnnounceJoystick. Until they do every row greys itself,
	// which is what retail also shows for a stick it cannot enumerate.
	bool pilotingRazor = scene.PlayerObject is { Placement.TypeName: { } playerTypeName }
		&& HercLUT.GetByAbbrev(playerTypeName)?.Id == ControlsPanel.RazorTypeIndex;
	var controlsPanel = ControlsPanel.Build(content, simulatorPreferences, pilotingRazor, stagedJoystick);

	// Retail-deviation toggles — see TweakSettingDefinitions. Not mission state, but built here
	// rather than up with localization/tweakSettings themselves because this is the first place
	// with an ImGui menu bar to raise it from; --shell and --movie return before reaching this point.
	var tweaksMenu = new TweaksMenu(TweakSettings.Current, localization);

	// The stick, once the window has an input context to enumerate it with. The bindings themselves are
	// the twelve bytes of prefs.cfg either way — nothing about them changes when the hardware does, which
	// is the whole point of the split; JoystickDeviceMap is what absorbs a modern device's own shape.
	JoystickSource? joystick = null;
	bool joystickAnnounced = false;

	// Which of the eight the CONTROLS panel has already acted on and is waiting to see released —
	// Input_LatchButton's mask, kept here because the panel's presses never go through JoystickBindings.
	byte controlsPanelLatched = 0;
	var joystickBindings = new JoystickBindings {
		PilotingRazor = pilotingRazor,
		Keyjoy = dataDirectory is null
			? new Keyjoy()
			: Keyjoy.Load(Path.Combine(dataDirectory, Keyjoy.FileName)),
	};
	var joystickInput = JoystickPilotInput.None;
	// CENTER LEGS has no latch of its own on the machine — MechObject reads it off the controls
	// record's rising edge, the way [\] reaches it — so a button press has to hold the flag up for
	// the one frame that record is built.
	bool joystickCenterBody = false;

	int objectivesKeysDown = 0;
	int preferencesKeysDown = 0;
	int statusAlertKeysDown = 0;
	bool manualKeyDown = false;
	string manualRoot = installRoot;
	bool panelMouseDown = false;
	bool panelRightButtonDown = false;
	bool missionOver = false;

	// DAT_004d2582, the global quit flag EXIT EARTHSIEGE?'s QUIT sets, which makes the mission's exit code 0.
	bool quitGame = false;

	var cockpitArt = mission.Player?.TypeName is { } pilotHerc
		? CockpitArt.Load(content, pilotHerc, scene.Theater.PaletteName,
			scene.World.Objects.OfType<MechObject>().Select(m => m.Name).Distinct(),
			squadPlacements
				.Where(o => o.Placement.PilotIndex >= 0)
				.Select(o => PilotRoster.BankName(PilotRoster.PortraitOf(o.Placement.PilotIndex)))
				.Distinct(),
			scene.Theater.ImpactPaletteName)
		: null;
	if (cockpitArt != null) {
		Console.WriteLine(
			$"Cockpit art loaded for {mission.Player!.TypeName} — drawing the three-panel cockpit view. "
			+ (cockpitArt.Sprites is { } hud
				? $"HUD sprites: {string.Join(", ", hud.BankNames)} in a {hud.Atlas.Width}x{hud.Atlas.Height} atlas."
				: "No HUD sprite banks could be loaded — canopy art only."));
		Console.WriteLine(cockpitArt.ColorSchemeIndex >= 0
			? $"Cockpit colour scheme {cockpitArt.ColorSchemeIndex} — palette slots "
			  + $"{CockpitPalette.CockpitSchemeFirstSlot}-"
			  + $"{CockpitPalette.CockpitSchemeFirstSlot + CockpitPalette.CockpitSchemeLength - 1}"
			  + $" from COCKPIT.DPL entries {CockpitPalette.SchemeFirstEntry(cockpitArt.ColorSchemeIndex)}+."
			: $"No cockpit colour scheme — {mission.Player!.TypeName}.DAT unreadable, so slots "
			  + $"{CockpitPalette.CockpitSchemeFirstSlot}+ keep the theater's filler colour.");
		if (!cockpitArt.ClipRegionsLoaded) {
			Console.WriteLine(
				"Viewport cutout fell back to inferring the hole from black pixels — at least one of the "
				+ "herc's .HD0/.HD2 region files could not be read.");
		}
	} else {
		Console.WriteLine("No cockpit art available — drawing a single full-window 3D view.");
	}

	// How far the display window travels down the cockpit canvas to reach the Heads-Down Display, read
	// from the herc's own vue\<HERC>.VUE rather than assumed. Every retail file says 237 authored rows
	// (474 device), but reading it is what makes the pan the file's statement instead of this host's.
	var viewGeometry = cockpitArt?.ViewGeometry;
	var cockpitPan = new CockpitPan(
		viewGeometry?.HeadsDownTravelY ?? CockpitViewGeometry.DefaultHeadsDownOriginY);

	// The sideways glance to the left and right windows — see CockpitGlance.
	var cockpitGlance = new CockpitGlance();
	bool glanceLeftKeyDown = false;
	bool glanceRightKeyDown = false;

	// The two view changes share CockpitView_QueueViewCommand's gate on the current view: the pan down
	// starts only from the forward view, and so does a glance. Every request for either goes through
	// these two so no input path can start one while the other is out. The way back is gated only by the
	// external view — neither can be out while the other is — whose view 4 none of the commands accepts.
	void RequestHeadsDown(bool headsDown) {
		if (ExternalViewActive()) {
			return;
		}

		if (!headsDown || cockpitGlance.AtForward) {
			cockpitPan.Request(headsDown);
		}
	}

	void CommandGlance(GlanceSide side) {
		if (!ExternalViewActive() && cockpitPan.AtForward && !cockpitPan.HeadsDownRequested) {
			cockpitGlance.Command(side);
		}
	}

	// The step kick. It rides the projection centre alongside the pan, which is where the original puts
	// it too — see CockpitViewKick.
	var cockpitViewKick = new CockpitViewKick();

	// And the damage shake, which rides the same centre and is ticked beside the kick in the original's
	// own cockpit pass — see CockpitHitShake.
	var cockpitHitShake = new CockpitHitShake(scene.World.PresentationRandom);

	// Straight to the pan rather than through RequestHeadsDown, whose external-view gate reads state not
	// yet set up here — at launch the glance is at rest and the external view is not up anyway.
	if (startOnHeadsDown) {
		cockpitPan.Request(headsDown: true);
		cockpitPan.Advance(CockpitPan.DurationSeconds);
	}

	if (startWithObjectives) {
		objectivesPanel?.Open();
	}

	if (startWithPreferences) {
		preferencesPanel?.Open();
	}

	if (startWithControls) {
		controlsPanel?.Open();
	}

	Console.WriteLine(statusAlertPanel != null
		? "[Q] mission-status alert: GNL_ALRT.STR loaded."
		: "[Q] mission-status alert: GNL_ALRT.STR is missing; the panel will not open.");

	Console.WriteLine(objectivesPanel is { } objectivesSummary
		? $"[F11] objectives panel: {objectivesSummary.Lines.Count} line(s) of block-13 text."
		: "[F11] objectives panel: OBJ_ALRT.STR is missing; the panel will not open.");

	Console.WriteLine(preferencesPanel is { } preferencesSummary
		? $"[F12] preferences panel: {string.Join(", ", preferencesSummary.Values)}."
		: "[F12] preferences panel: PRF_ALRT.STR is missing; the panel will not open.");

	Console.WriteLine(controlsPanel is { } controlsSummary
		? $"CONTROLS panel: {controlsSummary.Title}, "
		  + (controlsSummary.Capabilities.Present
			  ? string.Join(", ", controlsSummary.Values)
			  : "no joystick, so every row is greyed and reads blank.")
		: "CONTROLS panel: CTL_ALRT.STR is missing; the panel will not open.");

	if (cockpitArt?.HeadsDown != null) {
		Console.WriteLine(
			$"Heads-Down Display art loaded — pan travel {cockpitPan.TravelRows} device rows, "
			+ $"{CockpitPan.DurationSeconds:0.00}s"
			+ (viewGeometry == null ? " (no .VUE; using the retail default travel)." : "."));
		Console.WriteLine(cockpitArt.HeadsDownLayout is { } hddLayout
			? $"Heads-Down widgets from the herc's own .GAU — screen {hddLayout.Screen}, "
			  + $"arrow frame set {hddLayout.UnlitFrame(HddLayout.Widget.ArrowUp)}+."
			: "No Heads-Down widget block in this herc's .GAU — drawing its art only.");
	} else if (cockpitArt != null) {
		Console.WriteLine("No .HB1 for this herc — the Heads-Down Display is unavailable.");
	}

	// The Heads-Down Display's command display: the map camera, the mission's terrain raster, and the
	// three squad comm boxes. All three are per-mission, so they are built once here.
	//
	// The squad is the player.mec entries other than the player's own — the same three machines the
	// original keeps in g_SquadmateMachines (004d044c), in file order, which is the order the comm boxes are numbered in.
	HddCommandScreen? hddCommand = null;
	if (cockpitArt?.HeadsDownLayout is { } commandLayout && scene.World is { } commandWorld) {
		var mapBounds = HddMapBounds.Of(scene.Mission.Coordinates);
		var mapViewport = commandLayout.MapViewport;
		var squad = squadPlacements.Select(o => o.Object).ToList();

		// The same relief through the damage-flash palette. Rasterized here beside the ordinary one
		// rather than on each toggle: it is one texel per terrain cell, so a second copy is small, and
		// rebuilding it inside a flash would stutter.
		hddMapFlashRaster = HddMapRaster.Build(commandWorld.Terrain, mapBounds, cockpitArt.FlashPaletteEntry);

		hddCommand = new HddCommandScreen(
			new HddMapView(mapBounds, mapViewport.Width, mapViewport.Height),
			HddMapRaster.Build(commandWorld.Terrain, mapBounds, cockpitArt.PaletteEntry),
			squad,
			commandWorld);

		if (initialHddPilot >= 0) {
			hddCommand.SelectPilot(initialHddPilot);
		}

		if (initialHddOrder is { } startOrder) {
			hddCommand.SelectOrder(startOrder);
		}

		if (initialHddTransmit) {
			if (hddCommand.AwaitingPick) {
				// The camera is not on the player until the first paint, and a map click is read through
				// it. Put it there first so the centre of the viewport is where the player is standing.
				if (scene.PlayerObject?.Object is { } commandSubject) {
					hddCommand.View.Follow(commandSubject.Position);
				}

				// The two orders that want a unit get one picked for them: the nearest of the side they
				// take, hostile for ATTACK ENEMY and friendly for DEFEND POSITION. The rest take the
				// centre of the viewport, which is where the player is standing.
				var pick = HddCommandState.NeedsUnit(initialHddOrder ?? HddOrder.Disengage)
					? commandWorld.Objects
						.Where(candidate => !candidate.Removed && !candidate.AwaitingDeployment
							&& candidate.TargetClass != TargetClass.None
							&& (candidate.Side == MissionSide.Cybrid)
								== (initialHddOrder == HddOrder.AttackEnemy)
							&& !ReferenceEquals(candidate, scene.PlayerObject?.Object))
						.OrderBy(candidate => scene.PlayerObject?.Object is { } from
							? candidate.Position.ApproxDistanceTo(from.Position)
							: 0)
						.FirstOrDefault()
					: null;

				float pickX = pick != null
					? hddCommand.View.ToScreenX(pick.Position.X)
					: mapViewport.Width / 2f;
				float pickY = pick != null
					? hddCommand.View.ToScreenY(pick.Position.Y)
					: mapViewport.Height / 2f;

				hddCommand.ClickMap(pickX, pickY, commandWorld.Objects);
			}

			Console.WriteLine($"Command display: XMIT {initialHddOrder} to slot {initialHddPilot} "
				+ (hddCommand.Transmit() ? "reached its recipient." : "found nobody."));

			reportSquadOrders = when => {
				for (int slot = 0; slot < squad.Count; slot++) {
					if (squad[slot] is MechObject mate) {
						Console.WriteLine($"  {when} tick {commandWorld.TickCount}, slot {slot}: squad order {mate.SquadOrderVerb}, "
							+ $"state {mate.Behaviour.State?.Name ?? "none"}, "
							+ $"destination {mate.SquadOrderDestination}, "
							+ $"at {mate.Position}, "
							+ $"target {(mate.SquadOrderTarget == null ? "none" : mate.SquadOrderTarget.TargetClass.ToString())}, "
							+ $"reply {mate.LastSquadMessage}, radar {(mate.Scanner ? "ACTIVE" : "PASSIVE")}");
					}
				}
			};

			reportSquadOrders("on receipt,");
		}

		Console.WriteLine($"Command display map: mission box {mapBounds.MinX},{mapBounds.MinY} - "
			+ $"{mapBounds.MaxX},{mapBounds.MaxY}, {hddCommand.View.FullScale >> HddMapView.ScaleShift} "
			+ $"world units per pixel zoomed out, {squad.Count} squadmate(s) on the comm boxes.");
	}

	// The MFD's FLASH COMM page and the comm channel behind it. The page is six order rows and a
	// selection; the channel is the three video boxes and the queue in front of them, which is what turns
	// a squadmate's reply into static, a talking portrait and a recorded line. Both are per-mission: the
	// boxes have to know who is in them, and that comes off each machine's own pilot index.
	var flashComm = new MfdFlashCommScreen();
	var pilotRoster = PilotRoster.Load(content);
	var squadComm = new SquadCommChannel(content, pilotRoster, scene.World.PresentationRandom,
		mission.Header.TrainingMissionNumber);
	var squadSeats = new SimObject?[SquadCommChannel.SlotCount];
	var pilotVideos = new SquadTransmission?[SquadCommChannel.SlotCount];

	for (int slot = 0; slot < squadPlacements.Count; slot++) {
		squadComm.Seat(slot, squadPlacements[slot].Placement.PilotIndex, squadPlacements[slot].Object);
		squadSeats[slot] = squadPlacements[slot].Object;
	}

	// A training mission's instructor speaks from loose files beside the archives, in the folder named
	// after whichever voice archive is mounted.
	audio.InstructorVoiceDirectory = InstructorVoice.Directory(dataDirectory,
		Path.GetFileNameWithoutExtension(content.MountedArchives.FirstOrDefault(
			name => name.StartsWith("SIMVOIC", StringComparison.OrdinalIgnoreCase)) ?? "SIMVOICE"));
	audio.AttachSquad(squadComm);

	// A comm box captions itself with its pilot's roster name, the same one the MFD's transmission plate
	// carries — both are the gauge's own +0x137.
	if (hddCommand != null) {
		hddCommand.PilotNameOf = squadComm.Name;
	}

	if (initialFlashCommRow >= 0) {
		flashComm.Select(initialFlashCommRow, flashCommIsUp: true);
	}

	if (initialFlashCommTransmit && scene.World is { } flashCommStartWorld) {
		int verb = flashComm.SelectedVerb;
		bool taken = flashComm.Transmit(flashCommStartWorld, flashCommStartWorld.PlayerMech?.Group);
		Console.WriteLine($"FLASH COMM: XMIT row {flashComm.SelectedRow} (group 0 verb {verb}) "
			+ (taken ? "was taken." : "was refused by everyone."));
	}
	Console.WriteLine(pilotRoster != null
		? $"Squad comm: {squadPlacements.Count} box(es) — "
		  + string.Join(", ", Enumerable.Range(0, squadPlacements.Count)
			  .Select(slot => squadComm.Occupied(slot)
				  ? $"{pilotRoster.Name(squadPlacements[slot].Placement.PilotIndex)} "
					+ $"(pilot {squadPlacements[slot].Placement.PilotIndex}, "
					+ $"portrait {PilotRoster.PortraitOf(squadPlacements[slot].Placement.PilotIndex)}, "
					+ $"voice {squadComm.VoiceBank(slot)})"
				  : "empty"))
		: $"No {PilotRoster.ResourceName} — the comm boxes have no names and no portraits.");

	// FLASH COMM's seven order keys, in the order MfdFlashComm_HandleAltKey (00446c10) and MfdDisplay_KeyDispatch (004469c0) both switch on their
	// scancodes: which row each selects, and — for the two rows that carry two orders — which verb has to
	// be showing before [Alt] will transmit it. -1 means transmit whatever the row reads.
	(Key Key, int Row, int Verb)[] FlashCommKeys = {
		(Key.A, 0, -1),   // ATTACK MY TARGET
		(Key.G, 1, -1),   // IGNORE MY TARGET
		(Key.H, 2, -1),   // HELP ME OUT!
		(Key.O, 3, -1),   // JOIN ON ME
		(Key.C, 4, (int)SquadCommand.ScanForHostiles),
		(Key.E, 4, (int)SquadCommand.Emcon),
		(Key.F, 5, -1),   // FIRE AT WILL / HOLD YOUR FIRE
	};
	bool[] flashCommKeysDown = new bool[FlashCommKeys.Length];
	bool flashCommTransmitKeyDown = false;
	bool flashCommNextRowKeyDown = false;
	bool flashCommPreviousRowKeyDown = false;

	// Edge state for the command display's keyboard — see the block that reads them.
	//
	// The order hotkeys are the manual's own, and they are not a table in the code: each STRINGS0 group
	// 0 entry carries the index of its hotkey character within its own text, and the screen's scancode
	// dispatch maps that character to the order. Listed here in order-list order.
	Key[] HddCommandKeys = { Key.D, Key.A, Key.F, Key.T, Key.G, Key.O, Key.C, Key.E };
	Key[] HddPilotKeys = { Key.Number1, Key.Number2, Key.Number3 };
	bool[] hddOrderKeysDown = new bool[HddCommandKeys.Length];
	bool[] hddPilotKeysDown = new bool[HddPilotKeys.Length];
	bool hddPreviousOrderKeyDown = false;
	bool hddNextOrderKeyDown = false;
	bool hddZoomInKeyDown = false;
	bool hddZoomOutKeyDown = false;
	bool hddZoomInPadKeyDown = false;
	bool hddZoomOutPadKeyDown = false;
	bool hddRecentreKeyDown = false;
	bool hddTransmitKeyDown = false;
	bool hddCancelKeyDown = false;
	Key[] HddArrowKeys = { Key.Up, Key.Down, Key.Left, Key.Right };
	bool[] hddArrowKeysDown = new bool[HddArrowKeys.Length];
	bool[] hddDamageArrowKeysDown = new bool[HddArrowKeys.Length];

	// Whose herc the damage detail is inspecting — the display's subject selector, which starts on the
	// player and which the left and right arrows step. See HddDamageSubject. A --hdd-subject naming an
	// empty squad slot, which no step can land on, starts on the player instead.
	int hddSubjectSlot = initialHddSubject is > HddDamageSubject.PlayerSlot and < HddDamageSubject.TargetSlot
		&& squadSeats[initialHddSubject - 1] == null
		? HddDamageSubject.PlayerSlot
		: initialHddSubject;

	// A Heads-Down Display button pressed while the sensor dropout has the display dark. The original's
	// press handler (HddDisplay_HandleWidgetPress, 0044a178) returns before it acts and before it clears
	// the press for anything but the two page buttons, so the last such press waits and fires the first
	// frame the display is back — see ApplyHddClick.
	HddLayout.Widget? pendingHddPress = null;

	// The cockpit readouts' live values. The hardpoint names come from the shell weapon catalog keyed by
	// player.mec's own hardpoint ids; speed, throttle, turret, the shield numbers and the energy bar are
	// taken off the piloted machine each frame. What is left sits at the power-up defaults in
	// CockpitHudState.Default until the sim carries the state behind it.
	var hudState = CockpitHudState.Default with { Hdd = initialHddPage, HddDamage = initialHddDamageView };
	if (initialMfdMode is { } startMfdMode) {
		hudState = hudState with { Mfd = startMfdMode };
	}

	// The MFD switching itself to the missile camera, and the camera screen itself — see MfdMissileCam.
	// Every change of screen goes through SetMfdMode, which is how the switch learns of one made by hand.
	var missileCamSwitch = new MfdMissileCamSwitch();
	var missileCamScreen = new MfdMissileCamScreen();
	bool missileCamUpdatedLastFrame = false;

	// Input_LatchButton(1, 1), the first button row — always FIRE — held masked until it is let go, which
	// also holds the axes still. Two things latch it: a flown round ending, and AlertPanel_Enter (00454630)
	// opening any modal panel. The joystick's own latches are JoystickBindings'; this one also masks
	// [Space], the row's key. DAT_0049ebe5 is the keyboard hold that goes with it — see
	// docs/formats/joystick-input.md. Live input only: what a replay does with a latch is that doc's Open.
	bool fireRowLatched = false;
	int missileKeyboardHold = 0;

	void SetMfdMode(MfdMode requested) => hudState = hudState with {
		Mfd = missileCamSwitch.SetMode(hudState.Mfd, requested),
	};

	// The weapon panel, off the piloted machine's own mounts, which are already built, in cockpit-row
	// order. See WeaponRowState.
	if (cockpitArt?.Gau is { } weaponGau && scene.PlayerMech is { } armedMech) {
		hudState = hudState with {
			Weapons = WeaponRowState.Build(armedMech.Weapons, weaponGau.WeaponListTotal, cockpitArt.Strings),
		};

		if (initialWeaponRow is { } startRow) {
			armedMech.Weapons.SelectBySlot(startRow);
			if (initialLink) {
				armedMech.Weapons.ToggleLink();
			}
		}

		Console.WriteLine("Hardpoints: " + string.Join(", ", hudState.Weapons
			.Select((row, i) => $"{i + 1} {row.Name}")
			.Where(entry => entry.Length > 2)));
	}

	// Milestone 9: the player walks. A HERC has no velocity vector — the walk and run animations' root
	// motion is what moves it — so piloting is the arrow keys on the throttle and the stick, and the
	// machine covers whatever ground its own gait covers. See docs/simulation/mech-locomotion.md.
	//
	// Retail's own bindings, from the manual's keyboard table and its throttle section: left and right
	// arrows steer, up and down arrows open and close the throttle, and keypad [5] is all stop. The
	// manual says the numeric keypad with NUM LOCK off, which on a real keyboard is the same key as the
	// arrow cluster; both are accepted here since a host window has no NUM LOCK to read.
	var pilotMech = scene.PlayerMech;
	bool piloting = pilotMech != null;
	bool allStopKeyDown = false;
	bool shieldRearKeyDown = false;
	bool shieldFrontKeyDown = false;

	// The manual's three target keys, on the scancodes DBSIM's own command handlers switch on:
	// [Enter] (0x1c) cycles, ['] (0x28) takes the nearest, and [;] (0x27) clears. All three fire on
	// their own edge — the original dispatches a command per keypress, so holding one does nothing.
	bool cycleTargetKeyDown = false;
	bool nearestTargetKeyDown = false;
	bool clearTargetKeyDown = false;

	// And [Tab] (0x0f), the fourth, which is the Targeting Pod's rather than the selection's: it steps
	// the component lock on whatever is already selected. See MechObject.CycleTargetComponent.
	bool cycleComponentKeyDown = false;

	// [R], the manual's radar mode: PASSIVE at power-up, ACTIVE once pressed. It is the input target
	// selection needs at any real range — see MechObject.ToggleScanner.
	bool radarKeyDown = false;

	// [T], Automatic Turret Tracking — the same toggle as the console's TRACK button, on the scancode
	// (0x14) Sim_DispatchCommand switches on. Turning it *off* also centres the turret there, which is
	// the one asymmetry in the pair.
	bool autoTrackKeyDown = false;

	// The weapon panel's row keys, in row order: [1] is row 1 and [0] is row 10, which is the same
	// wrap-around the row's own printed digit uses ((slot + 1) % 10).
	Key[] weaponRowKeys = {
		Key.Number1, Key.Number2, Key.Number3, Key.Number4, Key.Number5,
		Key.Number6, Key.Number7, Key.Number8, Key.Number9, Key.Number0,
	};
	bool[] weaponRowKeyDown = new bool[weaponRowKeys.Length];
	bool cycleWeaponKeyDown = false;
	bool linkKeyDown = false;
	bool powerUpKeyDown = false;
	bool powerDownKeyDown = false;

	// The chain of views [V] and the joystick's OUTSIDE VIEW and CHASE VIEW step through, and the camera
	// behind each — see ExternalViewChain. [Esc], [Enter], [Tab] and [N] reach it too, but only from the
	// external view, where the cockpit's own widgets are off.
	var viewChain = pilotMech != null ? new ExternalViewChain(pilotMech) : null;
	if (startExternal) {
		viewChain?.ToggleOutside(fromKeyboard: true);
	}

	bool externalViewKeyDown = false;
	bool viewControlKeyDown = false;
	bool viewNextKeyDown = false;

	// The mouse outside view's orbit, for the tweak that swaps retail's for it (see ExternalCamera). Held
	// here because it is state the host owns across frames — the same reason cockpitViewKick and the
	// throttle gauge live here. Yaw starts directly behind the machine and pitch level with
	// ExternalCamera's own fixed framing; both only move once the player drags.
	float externalOrbitYaw = 0f;
	float externalOrbitPitch = ExternalCamera.DefaultOrbitPitchRadians;
	bool externalOrbitDragging = false;
	System.Numerics.Vector2 externalOrbitLastMouse = System.Numerics.Vector2.Zero;

	// The tick's camera axes and trigger, built with the machine's controls each frame and handed to the
	// chain on each tick — Sim_PollPlayerInput's arguments to Cam_Steer (00401c74).
	PilotAxes viewCameraAxes = PilotAxes.Centred;
	bool viewCameraTrigger = false;

	// Retail's pause is a modal panel, not a mode: [P] raises PAUSE over the frozen cockpit and
	// [Return], [Esc] or its CONTINUE button puts it away. It is the same panel class as the [Q] status
	// alert — see StatusAlertPanel — so the pause lives there and nothing is needed here.

	// The debug panel. It owns its own view options and readouts; see DebugPanel for what it shows and
	// why it is ImGui rather than the game's own HUD font.
	var debugPanel = new DebugPanel();

	// Hidden until [Esc] first raises it — see ReadMenuBarEscapeKey below — since it is the only way to
	// reach either debugPanel or tweaksMenu and every key from F1 to F12 is already taken.
	bool menuBarVisible = false;
	bool menuBarEscapeDown = false;

	// The -SPRUNKNOWN keys, and the Alt+S freeze a replay honours without them.
	var developerKeys = new DeveloperKeys(developerMode);
	if (developerMode) {
		Console.WriteLine("Developer keys on: Alt+S freezes, Alt+keypad + steps one tick, Ctrl+Alt+1-9 size "
			+ "the Alt+arrow moves and Ctrl+Left/Right turns, Ctrl+N/P view other objects, Ctrl+T hands the "
			+ "controls off, Ctrl+Alt+,/. pick a component, Ctrl+Alt+D damages it, Ctrl+Alt+N hits a Cybrid.");
	}
	bool tapeEscapeDown = false;

	// The front window's TIME: readout. It is driven from the frames the gunsight is painted on,
	// which is what stalls it in the external view, exactly as the original's does.
	var missionClock = new MissionClock();

	// The nav marker the player drops on themselves with [Alt+D], and the second subject the front
	// window's waypoint indicators can be pointing at. Cockpit-view state, not the machine's, so it
	// lives here beside the clock.
	var navMarker = new NavMarker();
	bool navMarkerKeyDown = false;

	// The console's throttle slider and the machine's throttle setting are two-way bound, so the gauge's
	// own value is state in its own right: it is what the machine reads on any frame the machine did not
	// itself move the throttle. See MechObject.ExchangeCockpitThrottle.
	var throttleTrack = cockpitArt != null ? ThrottleTrack.From(cockpitArt) : null;
	if (pilotMech != null && initialHeading is { } stagedHeading) {
		pilotMech.Heading = (ushort)stagedHeading;
	}

	short throttleGauge = initialThrottle;
	if (pilotMech != null && initialThrottle != 0) {
		pilotMech.Throttle = initialThrottle;
	}

	// The scanner goes on right away; the selection itself has to wait for the sensor model to have run,
	// since nothing is targetable until a sweep has painted it. It is taken on the first tick after that.
	if (acquireTarget && pilotMech != null) {
		pilotMech.ToggleScanner(scene.World);
	}

	if (autoTrack && pilotMech != null) {
		pilotMech.Weapons.AutoTrack = true;
	}

	// The cockpit powers up the moment the player has a machine — the start-up sequence and, for a
	// flyer, the engine hum that runs for the rest of the mission. See GameAudio.PowerUp.
	//
	// The listener has to be placed first. The hum is positional and PowerUp is a one-shot: SoundDirector
	// refuses to start a source past its catalog row's cutoff range, so with the listener still at its
	// default origin and the machine tens of thousands of units away at its mission spawn, the hum would
	// be judged inaudible, never started, and never retried. The camera does not exist yet at this point
	// in setup and would not be positioned if it did, so the machine's own eye stands in — which is where
	// the camera opens anyway.
	// The compass winds up from north over the same power-up, on a walking machine only — the sweep
	// decides that for itself off the same FlyerFlag the engine hum is gated on. Built here rather
	// than at cockpit-build time because it needs the tick the power-up began on.
	// Sim_InitMissionSession's music arm, which plays only when the flag the MUSIC handler left at startup
	// is up.
	audio.StartMissionMusic(mission.Header, trackSelect);

	// The weapon rows winking on and the shield rings filling are the same sequence's. A --screenshot
	// run starts with them finished: its warm-up is half a second, and the last row arms at 3.2 s.
	HeadingTapeSweep? headingSweep = null;
	var cockpitPowerUp = CockpitPowerUp.Finished;

	// The displays' sensor dropouts, one per display, for the whole mission.
	var cockpitDropouts = new CockpitDropouts();
	if (pilotMech != null) {
		audio.SetListener(pilotMech.EyePosition, pilotMech.Heading);
		audio.PowerUp(pilotMech);
		headingSweep = HeadingTapeSweep.ForPowerUp(pilotMech, audio.CoarseTicks);
		if (screenshotPath == null) {
			cockpitPowerUp = CockpitPowerUp.ForPowerUp(pilotMech, audio.CoarseTicks);
		}
	}

	if (pilotMech != null) {
		Console.WriteLine(
			$"Piloting {pilotMech.Name}: top speed {pilotMech.Type.DisplaySpeedKph(pilotMech.Type.MaxForward)} km/h, "
			+ $"walk/run threshold at {pilotMech.Type.DisplaySpeedKph(pilotMech.Type.GaitThreshold)} km/h"
			+ (pilotMech.Thread == null ? " — no animation data, so it cannot walk." : "."));
		Console.WriteLine("Up/Down arrows throttle — hold Down through zero for reverse — Left/Right "
			+ "arrows turn, keypad 5 all stop, C switches to the free camera, V to the external view.");
		Console.WriteLine("In the external view, hold the left mouse button and drag to orbit the camera "
			+ "around the machine; vertical orbit is clamped to 45 degrees up or down.");
		Console.WriteLine("J/K twist the turret, I/M pitch it, Backspace re-centres it — the manual's own "
			+ "keyboard turret set. The cockpit view looks where the turret points.");
		Console.WriteLine("T or the TRACK button toggles Automatic Turret Tracking, which holds the "
			+ "turret on the selected target; turning it off with T re-centres the turret, and touching "
			+ "either turret axis overrides it for as long as you hold the key.");
		Console.WriteLine(throttleTrack != null
			? "Drag the console's throttle slider with the mouse to set it; it tracks the keys either way."
			: "No throttle slider in this herc's .GAU — keyboard throttle only.");
	}

	Console.WriteLine("Free camera: W/A/S/D move, R/F rise and fall, arrow keys look, Shift boosts.");
	Console.WriteLine("Esc no longer quits — it raises a menu bar at the top of the window (Debug: "
		+ "skeleton view, animation readouts; Tweaks: this engine's own retail-deviation switchboard). "
		+ "Esc again backs out one layer at a time: closes whichever of the two is open, then hides the "
		+ "empty bar. Clicking outside the debug panel closes it too.");
	Console.WriteLine("F1-F6 switch the MFD screen: STATUS, FLASH COMM, NAV MAP, SCANNER, TARGET, MISSILE CAM.");
	Console.WriteLine("On FLASH COMM, A/G/H/O/C/E/F pick an order (, and . step through them) and X "
		+ "transmits it; Alt with any of those seven transmits that order straight from whichever screen "
		+ "is showing.");
	Console.WriteLine("F7/F8 pan down to the Heads-Down Display's command and damage screens; "
		+ "F1-F6 pan back up.");
	Console.WriteLine("On the damage screen, S/I/W switch between structural, internal and weapon systems.");
	Console.WriteLine("F11 shows the mission objectives; Return, Esc or the RETURN button puts it away. "
		+ "The simulation is stopped while it is up, as it is in the original.");
	Console.WriteLine("F12 shows the simulator preferences; Return, Esc or DONE puts it away. Clicking a "
		+ "row steps its setting and the right button steps back, where that row allows it.");
	Console.WriteLine("CONTROLS opens the joystick bindings over it. An axis row steps on every click; a "
		+ "button row selects on the first click and steps on the next, and RECOMMEND writes the "
		+ "recommended set. Pressing a button on the stick itself picks that button's row, and pressing "
		+ "it again steps it. A rebinding is live on the next tick, and the panel merges its own options "
		+ "into prefs.cfg as it closes — --no-write-prefs turns that off.");
	Console.WriteLine("Q asks how the mission stands and offers a way out of it. Return, Esc or the "
		+ "left button carries on; the right button, when the status offers one, ends the mission.");
	Console.WriteLine("P pauses the mission and Ctrl+Q asks to leave the game — the same panel at a "
		+ "smaller size. Return, Esc or CONTINUE dismisses either.");
	Console.WriteLine("On the command display, 1-3 pick a squadmate, D/A/F/T/G/O/C/E pick an order "
		+ "(, and . step through them), X transmits and Backspace cancels.");
	Console.WriteLine("Its map: + and - or the two magnifiers zoom, the arrows scroll it (the keypad "
		+ "keeps steering while it is down), keypad 5 re-centres it on your machine, and clicking a "
		+ "squadmate's marker selects that pilot.");
	Console.WriteLine("1-0 arm a weapon row (left-click the row does the same), W and Alt+W step through "
		+ "the firing chain, Alt+1-0 or a right-click add and remove a row from it.");
	Console.WriteLine("L or the LINK button links the armed weapon to its opposite hardpoint, when that "
		+ "hardpoint carries the same weapon; both rows then light together.");

	using var window = new EngineWindow($"HERCULAN Engine — zone {mission.Header.ZoneIndex}");

	SceneRenderer? renderer = null;
	Overlay2DRenderer? overlay = null;
	WireframeRenderer? wireframe = null;
	BeamRenderer? beams = null;
	SpriteRenderer? sprites = null;
	ImGuiController? imgui = null;
	GpuMesh? terrainMesh = null;
	GpuTexture? terrainTexture = null;

	// The terrain's own draw item, kept because its texture follows the TERRAIN TEXTURE preference and so
	// changes after the item list is built -- see the per-frame update.
	SceneItem? terrainItem = null;

	// The terrain again, with the ground shapes painted in its cell order -- see
	// SceneRenderer.Render. Its shape list is rebuilt each frame by RefreshGroundShapeItems.
	GroundShapeLayer? groundLayer = null;
	GpuTexture? cockpitFrontTexture = null;
	GpuTexture? cockpitSideTexture = null;
	GpuTexture? cockpitHeadsDownTexture = null;
	GpuTexture? hudSpriteTexture = null;
	GpuTexture? hddMapTexture = null;
	bool hitShakeStaged = false;
	// Whether the scene and the canopy are currently showing the theater's impact palette. Tracked so
	// the swap costs nothing on the frames — the great majority — where it did not change: the scene
	// half is two texture handles, but the canopy half re-uploads three full-panel textures.
	bool damageFlashShown = false;

	void ApplyDamageFlash(bool active) {
		if (active == damageFlashShown || renderer == null) {
			return;
		}

		damageFlashShown = active;
		renderer.ImpactPaletteActive = active;

		// The HUD's own colours — the resolved COLORS.DAT ids and raw palette slots every widget draws
		// through. Most of the twenty-seven move under the impact palette, so without this the
		// instruments would be the one part of the screen refusing to flash.
		if (cockpitArt != null) {
			cockpitArt.FlashActive = active;
		}

		// The sky and the fog colour come out of the palette too, and in an open zone they are most of
		// what is on screen — see Scene.ImpactFlash.
		if (scene.ImpactFlash is { } flash) {
			(active ? flash.Atmosphere : scene.Atmosphere).ApplyTo(renderer);
		}

		if (cockpitArt is { } art && cockpitFrontTexture != null) {
			cockpitFrontTexture.Update(art.Front.PixelsFor(active), art.Front.Width, art.Front.Height);
			cockpitSideTexture?.Update(art.Side.PixelsFor(active), art.Side.Width, art.Side.Height);
			if (art.HeadsDown is { } headsDown && cockpitHeadsDownTexture != null) {
				cockpitHeadsDownTexture.Update(headsDown.PixelsFor(active), headsDown.Width, headsDown.Height);
			}

			// And the plates and glyphs themselves. One handle re-uploaded rather than a second texture
			// bound per draw: the sheet goes into a dozen overlay calls, and a flash is a one-second event.
			if (art.Sprites is { Atlas: { } atlas } && art.ImpactSpritePixels is { } flashPixels
					&& hudSpriteTexture != null) {
				hudSpriteTexture.Update(active ? flashPixels : atlas.Pixels, atlas.Width, atlas.Height);
			}
		}

		// And the heads-down map's relief, for a player who takes a hit while panned down to it.
		if (hddMapTexture != null && hddMapFlashRaster is { } flashRaster
				&& hddCommand?.Raster is { } raster) {
			var shown = active ? flashRaster : raster;
			hddMapTexture.Update(shown.Pixels, shown.Width, shown.Height);
		}
	}

	var modelMeshes = new Dictionary<string, GpuMesh>();
	var modelTextures = new Dictionary<string, GpuTexture>();

	// Billboards blit a frame unlit, so they need the atlas as COLOUR where a lit mesh needs it as
	// palette indices (see PaletteRampTable). Only the shapes that actually carry sprites get the
	// second upload.
	var spriteTextures = new Dictionary<string, GpuTexture>();
	var disposables = new List<IDisposable>();
	SceneItem[]? items = null;
	SceneItem[]? pilotedItems = null;
	var movers = new List<(SceneObject Object, SceneItem Item)>();

	// The structures that can change what they are drawn as: one that leaves a wreck swaps its geometry
	// for the hulk the moment its last part collapses, and one that leaves nothing drops out of the
	// world. Neither moves otherwise, so they are not in `movers`.
	//
	// A structure is drawn a piece at a time -- one per cell, or one per node and cell for a type that
	// animates -- so the swap covers a set of items and puts a wreck item of its own in their place
	// rather than overwriting one item's mesh. The wreck is drawn a cell at a time too, for its detail
	// levels' sake, so it is a set as well.
	var wreckable = new List<(SceneObject Object, BaseObject Structure, SceneItem[] Items,
		(SceneItem Item, CellGate Gate)[] Hulk, bool Posed)>();

	// Every drawn piece that stands on one cell of one animation sequence, and so is on screen only
	// while its object's ShapeCellFrames says that cell is showing. This is how a machine's destroyed
	// limb comes off and a structure's collapsed part turns to rubble: the shape holds all its cells
	// built and uploaded, and damage moves which one is picked -- see DtsMeshBuilder.BuildCells.
	var gatedParts = new List<(SimObject Object, CellGate Gate, SceneItem Item)>();

	// The objects the mission has not deployed yet, with the items that draw them and the visibility
	// each was built with. Entries leave the list the frame their group arrives; see the build loop.
	var undeployed = new List<(SimObject Object, (SceneItem Item, bool Visible)[] Parts)>();
	var projectileItems = new List<SceneItem>();

	// The guns bolted to the machines on the field, rebuilt every frame for the same reason a rocket's
	// is: which mesh a mount draws is its muzzle-flash cell, and that moves tick to tick.
	var weaponItems = new List<SceneItem>();

	// And the wreckage in the air. Same deal: a piece of debris tumbles every tick and the pool churns
	// as pieces settle and burst, so the list is rebuilt each frame rather than kept.
	var debrisItems = new List<SceneItem>();
	var dropPodItems = new List<SceneItem>();

	// The billboards to draw this frame: the EMP rounds in flight and every impact effect playing. Both
	// churn from tick to tick, so the list is rebuilt each frame rather than kept — see SpriteRenderer.
	var spriteBatches = new List<SpriteBatch>();

	// An object whose shape animates is drawn a node at a time, so each entry here is one geometry
	// segment riding one transform of one object — see MissionScene.PosedTransformOf. A machine and an
	// animated structure are both drawn this way: a radar mast's dish and an armed tower's turret are
	// nodes an animation thread moves, exactly as a leg is.
	var posedParts = new List<(SimObject Subject, int TransformId, SceneItem Item)>();
	var segmentMeshes = new Dictionary<string, GpuMesh[]>();

	// The machines on the field, each with every LOD root of its shape built and uploaded and one of
	// them selected. Only a machine is here: nothing else in the original carries a detail table, so
	// nothing else changes which shape it is drawn as — see Render.ShapeDetail.
	var detailChains = new List<MechDetailChain>();

	// Every drawn piece that is one level of a TSDetailPart, and so is on screen only while that part's
	// projected size selects its level -- TSDetailPart_Render's choice, made per object per frame by
	// SelectDetailLevels. Only what is built by cell or by node is here: a structure, its wreck and a
	// flyer, which are drawn under STRUCTURE DETAIL's bias. Guns, launcher rounds and debris carry detail
	// parts too, and choose their levels as their items are rebuilt each frame -- see AddAtDetail.
	var detailLevelParts = new List<(SimObject Object, CellGate Gate, SceneItem Item)>();

	// SelectDetailLevels' per-frame scratch: the level each object's detail part chose this frame.
	var detailLevelChoice = new Dictionary<(SimObject, PartDetail), int>();

	// And AddAtDetail's, for the one transient shape it is making items for.
	var transientLevelChoice = new Dictionary<PartDetail, int>();

	// And the same for a shape split by cell rather than by node -- a flyer, or a structure of one of the
	// 57 types that carry no animation, which loses parts to damage but has no posed nodes. One upload
	// per distinct model, as with the other two.
	var cellMeshes = new Dictionary<string, GpuMesh[]>();
	// What every key handler below reads: the window's own keyboard, or a replaying tape's keystrokes.
	// liveKeys stays the window's throughout, because a replay still listens to it for [Ctrl+E], for -D's
	// any-key abort and for the menu bar.
	IKeyState? keyboard = null;
	LiveKeys? liveKeys = null;
	var tapeKeys = tapePlayer != null ? new TapeKeys() : null;

	// The replay's own state. tapeFrame is the frame taken this host frame, if one was due; the keys it
	// pressed are down for this frame only, and the next frame leaves them up before another press can
	// land (tapePulsed). A frame taken while a modal panel is up is the panel's, not the simulation's.
	InputTape.Frame? tapeFrame = null;
	bool tapeFrameUnderPanel = false;
	bool tapePulsed = false;
	double tapeAccumulator = 0;
	bool tapePanelShown = false;

	// The tick of a frame whose own input raised a modal panel. Sim_PollPlayerInput runs the panel from
	// inside Sim_MainTick, so that tick finishes once the panel is down, with the frame's SimTickDelta.
	short? tapeDeferredTick = null;

	// The pointer as the tape's mouse events leave it, in framebuffer pixels. The live pointer does not
	// reach the cockpit or a panel during a replay, as CockpitMouseLive (004d1e5a) shuts it out in retail.
	(float X, float Y, CockpitMouseButtons Buttons) tapePointer = (0, 0, CockpitMouseButtons.None);
	bool liveStopRequested = false;

	// A recording's own state: whether a modal panel was up as this host frame began, which makes the frame
	// one of the panel's; whether a panel raised by a frame's own input is holding that frame's tick back;
	// and which scancodes were down last host frame, for the key-down edges.
	bool recordPanelAtStart = false;
	bool recordDeferredTick = false;
	var recordKeysDown = new HashSet<int>();
	IMouse? mouse = null;
	bool cameraKeyDown = false;
	var cockpitInput = new CockpitInput();
	var camera = new Camera();
	int framesRendered = 0;

	// How long a --screenshot run lets the scene settle before it captures. Long enough for the
	// power-up sequence and the first sensor sweep, which is what most staged flags wait on.
	const int ScreenshotWarmupFrames = 30;
	bool screenshotTaken = false;

	// Fixed-timestep accumulator: the simulation always advances in whole ticks of the same length, so
	// the ported fixed-point integration stays reproducible no matter how the frame rate varies.
	double tickAccumulator = 0;
	const double SecondsPerTick = 1.0 / SimWorld.TicksPerSecond;
	const double MaxAccumulatedSeconds = 0.25;

	window.Load += (gl, input) => {
		renderer = new SceneRenderer(gl);

		// How far this zone is visible and what it fades into, both off the zone and its theater rather
		// than hand-picked — see Scene.Atmosphere. The sky is deliberately left alone.
		scene.Atmosphere.ApplyTo(renderer);

		// The lights impact effects claim, which the renderer selects out of per drawn object — see
		// EffectLightSelection. Live slots only ever come from the simulation, so this is the whole of
		// the wiring.
		renderer.EffectLights = scene.World.EffectLights;

		// And the same distance as the camera's far plane, so the view stops where the original's
		// terrain draw region does instead of drawing fully-fogged geometry past it.
		scene.Atmosphere.ApplyTo(camera);

		// The theater's shaded-surface colours — what a TSShadedPoly is actually drawn through. See
		// SurfaceRampTable.
		renderer.SetShadeRamps(scene.ShadeRamps);
		renderer.SetPaletteRamp(scene.PaletteRamp);

		// And the same two through the theater's damage-flash palette, which the cockpit shake swaps the
		// scene to for a fraction of a second at a time — see Scene.ImpactFlash. After the two above,
		// which this is measured against.
		renderer.SetImpactRamps(scene.ImpactFlash?.ShadeRamps, scene.ImpactFlash?.PaletteRamp);

		overlay = new Overlay2DRenderer(gl);
		wireframe = new WireframeRenderer(gl);

		// Beams draw only if their two resources loaded; a scene without them still fires and still
		// damages, it just shows nothing.
		beams = scene.Beams != null ? new BeamRenderer(gl, scene.Beams) : null;

		// Billboards need no resource of their own — every sprite they draw belongs to a model already
		// built, so this is unconditional where the beam renderer is not.
		sprites = new SpriteRenderer(gl);

		imgui = new ImGuiController(gl, window.View, input, new ImGuiFontConfig(imguiFontPath, 16));

		terrainMesh = new GpuMesh(gl, scene.TerrainMesh);
		terrainTexture = scene.TerrainBank != null ? new GpuTexture(gl, scene.TerrainBank.Atlas, indexed: true) : null;

		if (cockpitArt != null) {
			cockpitFrontTexture = new GpuTexture(gl, cockpitArt.Front.Pixels, cockpitArt.Front.Width, cockpitArt.Front.Height);
			cockpitSideTexture = new GpuTexture(gl, cockpitArt.Side.Pixels, cockpitArt.Side.Width, cockpitArt.Side.Height);

			if (cockpitArt.HeadsDown is { } headsDownFrame) {
				cockpitHeadsDownTexture = new GpuTexture(gl, headsDownFrame.Pixels, headsDownFrame.Width, headsDownFrame.Height);
			}

			if (cockpitArt.Sprites is { } hudSprites) {
				hudSpriteTexture = new GpuTexture(gl, hudSprites.Atlas);
			}

			// Nearest, like everything else: the original blits it through its palettized texture mapper.
			if (hddCommand?.Raster is { } mapRaster) {
				hddMapTexture = new GpuTexture(gl, mapRaster.Pixels, mapRaster.Width, mapRaster.Height);
			}
		}

		// Which models are actually going to be drawn a node at a time: one whose segments exist *and*
		// whose object has an animation thread to pose them with. A shape that carries no ANAnimList has
		// neither, and has to keep the flat mesh or its cells, which have the rest pose baked in — its
		// segments alone would put every part at the shape's origin.
		//
		// The question is asked of the object rather than of its class: the eight animated-library
		// structure types get a shape instance and threads out of Base_Construct's tail the same way a
		// machine does (see BaseObject's constructor), and every other structure type gets neither.
		//
		// Asked of every root of a machine's LOD chain, not of the one it starts on: the crude roots are
		// posed by the same thread on the same transform ids (see SceneModelLibrary.MechDetailRoots), so
		// each of them has to be uploaded as segments too or a machine loses its animation the moment the
		// distance to the eye picks a coarser one.
		var animatedKeys = scene.Objects
			.Where(o => o.Object.Shape is { Threads.Count: > 0 })
			.SelectMany(o => o.Detail?.Roots ?? (o.Model is { } single ? new[] { single } : Array.Empty<SceneModel>()))
			.Where(m => m.Segments.Length > 0)
			.Select(m => m.Key)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		// One upload per distinct model, however many objects share it — a mission routinely fields
		// several of the same machine and a row of identical structures. A model that is drawn posed
		// uploads its segments instead of its flat mesh: the two are the same triangles, and only one of
		// them is ever drawn.
		foreach (var model in scene.Models) {
			if (animatedKeys.Contains(model.Key)) {
				segmentMeshes[model.Key] = model.Segments.Select(segment => new GpuMesh(gl, segment.Vertices, segment.TriangleVertexCount)).ToArray();
				disposables.AddRange(segmentMeshes[model.Key]);
			} else if (model.Cells.Length > 0) {
				// A shape whose cells damage drives uploads every one of them and draws the ones its
				// object's cell frames name, rather than being rebuilt each time a part comes off.
				cellMeshes[model.Key] = model.Cells.Select(cell => new GpuMesh(gl, cell.Vertices, cell.TriangleVertexCount)).ToArray();
				disposables.AddRange(cellMeshes[model.Key]);
			} else if (model.Mesh.Length > 0) {
				// A pure billboard shape — every EMP round, every impact effect — has no triangles at all
				// and gets no mesh; its atlas below is the whole of it.
				modelMeshes[model.Key] = new GpuMesh(gl, model.Mesh, model.TriangleVertexCount);
			}

			if (model.Atlas != null) {
				modelTextures[model.Key] = new GpuTexture(gl, model.Atlas, indexed: true);
				if (model.Sprites.Length > 0) {
					spriteTextures[model.Key] = new GpuTexture(gl, model.Atlas);
				}
			}
		}

		disposables.AddRange(modelMeshes.Values);
		disposables.AddRange(modelTextures.Values);
		disposables.AddRange(spriteTextures.Values);

		terrainItem = new SceneItem(terrainMesh, Matrix4x4.Identity, TerrainTextureHandle()) {
			CellQuantisedFog = true,
			GroundFill = true,
		};
		groundLayer = new GroundShapeLayer(terrainItem, scene.World.Terrain);
		var built = new List<SceneItem> { terrainItem };

		// The player's own machine, kept aside so the cockpit view can leave it out — see below. A
		// segmented machine contributes one item per node, so this is a set rather than one item.
		var playerItems = new HashSet<SceneItem>();

		foreach (var sceneObject in scene.Objects) {
			if (sceneObject.Model is not { } model) {
				continue;
			}

			uint? texture = modelTextures.TryGetValue(model.Key, out var bound) ? bound.Handle : null;
			bool isPlayer = ReferenceEquals(sceneObject, scene.PlayerObject);

			// A machine is uploaded once per LOD root and drawn as whichever one its projected size on
			// screen selects, so the build below runs once per root and leaves all but root 0 deselected.
			// Everything else has one root and the loop runs once — see Render.ShapeDetail, and
			// SelectDetailRoots for the per-frame half.
			var detailRoots = sceneObject.Detail?.Roots ?? new[] { model };

			if (segmentMeshes.ContainsKey(model.Key)) {
				var subject = sceneObject.Object;
				var rootItems = new SceneItem[detailRoots.Count][];

				for (int root = 0; root < detailRoots.Count; root++) {
					var rootModel = detailRoots[root];
					if (!segmentMeshes.TryGetValue(rootModel.Key, out var segments)) {
						rootItems[root] = Array.Empty<SceneItem>();
						continue;
					}

					uint? rootTexture = modelTextures.TryGetValue(rootModel.Key, out var rootBound)
						? rootBound.Handle
						: null;
					var segmentItems = new SceneItem[segments.Length];

					for (int i = 0; i < segments.Length; i++) {
						var segment = rootModel.Segments[i];
						var part = new SceneItem(segments[i],
							MissionScene.PosedTransformOf(subject, segment.TransformId), rootTexture) {
							LightSubject = subject,
							DetailSelected = root == 0
						};

						segmentItems[i] = part;
						built.Add(part);
						posedParts.Add((subject, segment.TransformId, part));
						if (segment.Gate.IsGated) {
							gatedParts.Add((subject, segment.Gate, part));
						}

						if (segment.Gate.IsDetailGated) {
							detailLevelParts.Add((subject, segment.Gate, part));
						}

						if (isPlayer) {
							playerItems.Add(part);
						}
					}

					rootItems[root] = segmentItems;
				}

				if (sceneObject.Detail is { } chain && rootItems.Length > 1) {
					detailChains.Add(new MechDetailChain(subject, chain.ShapeRadius, rootItems));
				}

				// Nothing here goes in `movers`: the posed refresh carries the object's own frame in
				// front of each node's, so a segmented object that moves is followed by that alone.
				//
				// An animated structure still leaves a wreck like any other, and every one of the eight
				// types that reach here does have a hulk. A structure has the one root, so the wreck swap
				// covers rootItems[0] and there is nothing else for it to cover.
				if (subject is BaseObject segmentedStructure) {
					RegisterWreckable(sceneObject, segmentedStructure, rootItems[0], posed: true);
				}

				continue;
			}

			// A shape split by cell contributes one item per cell instead of one for the whole model,
			// with the object's own transform on every one of them: unlike a machine's segments these
			// are already placed, so only the gate differs between them.
			if (cellMeshes.TryGetValue(model.Key, out var cells)) {
				var cellItems = new SceneItem[cells.Length];
				for (int i = 0; i < cells.Length; i++) {
					var cell = model.Cells[i];
					var part = new SceneItem(cells[i], MissionScene.TransformOf(sceneObject), texture) {
						LightSubject = sceneObject.Object
					};

					cellItems[i] = part;
					built.Add(part);
					if (cell.Gate.IsGated) {
						gatedParts.Add((sceneObject.Object, cell.Gate, part));
					}

					if (cell.Gate.IsDetailGated) {
						detailLevelParts.Add((sceneObject.Object, cell.Gate, part));
					}

					// A flyer flies, and every cell of it rides the one object transform, so all of them
					// need refreshing each frame. So does a ground vehicle, the one structure class that
					// drives; every other structure stands still and keeps the transform built here.
					if (sceneObject.Object is FlyerObject
							or BaseObject { Class: BaseObject.StructureClass.GroundVehicle }) {
						movers.Add((sceneObject, part));
					}

					if (isPlayer) {
						playerItems.Add(part);
					}
				}

				if (sceneObject.Object is BaseObject celledStructure) {
					RegisterWreckable(sceneObject, celledStructure, cellItems);
				}

				continue;
			}

			if (!modelMeshes.TryGetValue(model.Key, out var mesh)) {
				continue;
			}

			var item = new SceneItem(mesh, MissionScene.TransformOf(sceneObject), texture) {
				LightSubject = sceneObject.Object
			};
			built.Add(item);

			if (isPlayer) {
				playerItems.Add(item);
			}

			if (sceneObject.Object is BaseObject wreckableStructure) {
				RegisterWreckable(sceneObject, wreckableStructure, new[] { item });
			}

			// Anything that can move needs its transform refreshed every frame. Structures never do, so
			// they stay on the one built here.
			if (sceneObject.Object is MechObject) {
				movers.Add((sceneObject, item));
			}
		}

		// The two ways a structure can stop being drawn as itself. A type that leaves a wreck gets a
		// second, hidden item carrying the hulk, so the swap is a visibility flip rather than a mesh
		// rebuild at the moment the last part falls; a one-part type that leaves nothing sinks instead
		// and needs only its transform refreshed. A type with neither does neither, and is not listed.
		void RegisterWreckable(SceneObject sceneObject, BaseObject structure, SceneItem[] structureItems,
				bool posed = false) {
			if (structure.Type.HulkTypeIndex < 0 && structure.Type.Components.Length > 1) {
				return;
			}

			var hulkItems = Array.Empty<(SceneItem Item, CellGate Gate)>();
			if (structure.Type.HulkTypeIndex >= 0
					&& scene.HulkModels.TryGetValue(structure.Type.HulkTypeIndex, out var hulk)
					&& cellMeshes.TryGetValue(hulk.Key, out var hulkCells)) {
				uint? hulkTexture = modelTextures.TryGetValue(hulk.Key, out var hulkBound) ? hulkBound.Handle : null;
				hulkItems = new (SceneItem, CellGate)[hulkCells.Length];
				for (int i = 0; i < hulkCells.Length; i++) {
					var gate = hulk.Cells[i].Gate;
					var hulkItem = new SceneItem(hulkCells[i], MissionScene.TransformOf(sceneObject), hulkTexture) {
						LightSubject = structure,
						Visible = false
					};

					hulkItems[i] = (hulkItem, gate);
					built.Add(hulkItem);
					if (gate.IsDetailGated) {
						detailLevelParts.Add((structure, gate, hulkItem));
					}
				}
			}

			wreckable.Add((sceneObject, structure, structureItems, hulkItems, posed));
		}

		// A unit whose group is still waiting on its arrival action is not in the mission, and
		// Scene_SubmitFrameObjects does not submit it -- but it does submit it the moment the group
		// arrives, so this is a per-frame filter and not a build-time one. Its geometry is built like
		// everything else's and hidden until then; skipping the build instead leaves an arrived machine
		// with no body at all, and only its weapons, which are rebuilt every frame, on screen.
		//
		// The built-time visibility is captured rather than assumed: a wreckable structure's hulk item is
		// built hidden, and un-hiding it on arrival would show every waiting building as its own rubble.
		undeployed = built
			.Where(entry => entry.LightSubject is { AwaitingDeployment: true })
			.GroupBy(entry => entry.LightSubject!)
			.Select(group => (group.Key, group.Select(entry => (entry, entry.Visible)).ToArray()))
			.ToList();

		foreach (var (_, parts) in undeployed) {
			foreach (var (part, _) in parts) {
				part.Visible = false;
			}
		}

		items = built.ToArray();

		// Piloting means sitting inside the machine, and its own geometry is all around the eye — the
		// cockpit node the camera rides is well inside the torso, so drawing it fills the canopy and
		// hides the world. Both lists share the same SceneItem objects, so the per-frame transform
		// refresh reaches whichever one is being drawn.
		pilotedItems = playerItems.Count > 0
			? built.Where(entry => !playerItems.Contains(entry)).ToArray()
			: items;
		liveKeys = input.Keyboards.Count > 0 ? new LiveKeys(input.Keyboards[0]) : null;
		keyboard = tapeKeys ?? (IKeyState?)liveKeys;

		// The two things the live keyboard still does while a tape plays, both of which retail tests on
		// the live command word rather than the tape's: [Ctrl+E] stops the playback, and under -D any key
		// that makes a command code raises the abort.
		if (liveKeys != null && tapePlayer != null) {
			liveKeys.Device.KeyDown += (device, key, _) => {
				if (tapePlayer.Finished) {
					return;
				}

				bool ctrl = device.IsKeyPressed(Key.ControlLeft) || device.IsKeyPressed(Key.ControlRight);
				if (tapePlayer.DemoMode || (ctrl && key == Key.E)) {
					liveStopRequested = true;
				}
			};
		}

		// The stick. Its capabilities are what the CONTROLS panel greys its rows against, and the original
		// re-reads them every time that panel goes up rather than at startup, so nothing here has to be
		// the last word — but a device present now should light the rows up now.
		// Nothing is read off the device here. Silk.NET's GLFW backend reports a connected stick with zero
		// axes, buttons and hats until the first Update, so anything derived from its shape at load time
		// maps nothing at all — see JoystickSource. What it can do is announced on the first frame that
		// knows, in AnnounceJoystick.
		joystick = JoystickSource.Open(input, dataDirectory, probeJoystick);

		// Mouse events are queued here and nowhere else: everything that decides what a click means runs
		// once per frame in Update, out of CockpitInput.Drain. That is the original's own split — its
		// listener callback pushes a record and returns, and CockpitMouse_ProcessQueue does the work a
		// frame later (docs/formats/cockpit-input.md §3-4).
		if (input.Mice.Count > 0) {
			mouse = input.Mice[0];

			// The pointer reports window-client pixels while the cockpit is placed in framebuffer pixels,
			// which differ on a high-DPI display. Rescaling here is the same correction the original makes
			// for the same reason — Mouse_RecomputeScale (0048078c) converts client coordinates into the
			// game's own space before any listener sees them (§2).
			void Queue(IMouse m, CockpitMouseButtons buttons) {
				if (TapePlaying()) {
					return;
				}

				var client = window.ClientSize;
				var framebuffer = window.FramebufferSize;
				float x = m.Position.X * framebuffer.X / Math.Max(client.X, 1);
				float y = m.Position.Y * framebuffer.Y / Math.Max(client.Y, 1);
				cockpitInput.Enqueue(x, y, buttons);
				RecordMouse(x, y, buttons, framebuffer.X, framebuffer.Y);
			}

			// The mask is built from the event's own button rather than read back off the device, so it
			// does not depend on whether Silk.NET updates the held state before or after it raises.
			mouse.MouseMove += (m, _) => Queue(m, ButtonsHeld(m));
			mouse.MouseDown += (m, button) => Queue(m, ButtonsHeld(m) | ButtonFlag(button));
			mouse.MouseUp += (m, button) => Queue(m, ButtonsHeld(m) & ~ButtonFlag(button));
		}

		// No backface culling. DTS geometry is not reliably wound — the WinForms model viewer reached
		// the same conclusion and never culls either — so culling would punch holes in the mech rather
		// than save fill rate. The shader shades two-sided to match.
		gl.Disable(EnableCap.CullFace);
	};

	window.Update += deltaSeconds => {
		imgui?.Update((float)deltaSeconds);

		AnnounceJoystick();

		// A replay's next frame, when it is due, goes in ahead of every handler below, which then read its
		// keystrokes and pointer exactly as they would a player's.
		TakeTapeFrame(deltaSeconds);

		// And a recording's keystrokes, read at the same moment the handlers below will read them.
		RecordFrameStart();

		// The modal panels take the keyboard before anything else does. They are modal in the
		// original — each runs its own event loop, which owns input until the panel comes down — and
		// [Esc], which dismisses any of them, is also this host's menu-bar key, so they have to be asked
		// first or two things would act on one keystroke. All are asked every frame — single `|`, not
		// `||` — so each keeps its own key-edge state whether or not another claimed the keystroke.
		bool objectivesHandledKey =
			ReadStatusAlertKeys() | ReadObjectivesKeys() | ReadPreferencesKeys();

		// [Esc] drives the menu bar and its two panels, once none of the three above claims this frame's
		// press. Called unconditionally regardless — like them, it tracks its own key edge every frame —
		// so a press held across the frame a retail panel above consumes it doesn't read as a fresh,
		// unconsumed press the moment that panel closes.
		ReadMenuBarEscapeKey(objectivesHandledKey);
		ReadManualKey();

		// Everything below reads `controls` rather than the device itself: while the panel has keyboard
		// focus it is null, so piloting and camera keys go dead instead of the panel and the machine both
		// acting on the same keystroke.
		var controls = KeyboardCapturedByImGui() ? null : keyboard;

		// [C] swaps between flying the observer camera and piloting the machine, on the key's own edge
		// so holding it does not flicker between the two. It is this engine's key rather than the
		// original's, so during a replay it stays with the live keyboard.
		var cameraKeys = TapePlaying() ? (ImGuiHasKeyboard() ? null : liveKeys) : controls;
		if (pilotMech != null && cameraKeys != null) {
			bool cameraKey = !FlashCommHasKeyboard() && cameraKeys.IsKeyPressed(Key.C);
			if (cameraKey && !cameraKeyDown) {
				piloting = !piloting;
			}
			cameraKeyDown = cameraKey;
		}

		// The developer keys, which reach the dispatcher only while no modal panel holds the input.
		if (pilotMech != null && viewChain != null && controls != null && !AnyModalPanelOpen() && !missionOver) {
			developerKeys.Read(controls, scene.World, pilotMech, viewChain, TapePlaying(), deltaSeconds);
		}

		if (viewChain != null && controls != null && !AnyModalPanelOpen() && !missionOver) {
			// [V], scancode 0x2f: out to the outside view and back. The cockpit is not drawn in the
			// external view, and the machine — left out of the cockpit view because its geometry wraps the
			// eye — is.
			bool externalViewKey = !CtrlHeld(controls) && controls.IsKeyPressed(Key.V);
			if (externalViewKey && !externalViewKeyDown) {
				viewChain.ToggleOutside(fromKeyboard: true);
			}
			externalViewKeyDown = externalViewKey;

			// [Enter] and [Tab] swap the controls between the camera and the machine, and [N] moves the
			// outside view on to the next squadmate. All three are cockpit keys the widgets claim first,
			// so they reach these cases only while the widgets are off.
			bool viewControlKey = controls.IsKeyPressed(Key.Enter) || controls.IsKeyPressed(Key.Tab);
			if (viewControlKey && !viewControlKeyDown && CockpitWidgetsOff()) {
				viewChain.ToggleCameraControl();
			}
			viewControlKeyDown = viewControlKey;

			bool viewNextKey = !CtrlHeld(controls) && !AltHeld(controls) && controls.IsKeyPressed(Key.N);
			if (viewNextKey && !viewNextKeyDown) {
				viewChain.NextSquadmate(scene.World.Objects);
			}
			viewNextKeyDown = viewNextKey;
		}

		// The mouse outside view's orbit: hold the left mouse button and drag to swing the eye around the
		// machine, always aimed back at it. Gated the same way the cockpit's own clicks are — nothing to
		// drag while the pointer is over the debug panel — and only while that view is actually up, so a
		// drag started before switching views doesn't carry over.
		if (MouseOutsideView() && ExternalViewActive() && mouse != null
				&& (imgui == null || !ImGui.GetIO().WantCaptureMouse)) {
			bool dragging = mouse.IsButtonPressed(MouseButton.Left);
			var mousePosition = mouse.Position;
			if (dragging && externalOrbitDragging) {
				var delta = mousePosition - externalOrbitLastMouse;
				externalOrbitYaw += delta.X * ExternalCamera.OrbitSensitivity;
				externalOrbitPitch = Math.Clamp(
					externalOrbitPitch - delta.Y * ExternalCamera.OrbitSensitivity,
					-ExternalCamera.MaxOrbitPitchRadians, ExternalCamera.MaxOrbitPitchRadians);
			}
			externalOrbitDragging = dragging;
			externalOrbitLastMouse = mousePosition;
		} else {
			externalOrbitDragging = false;
		}

		// A modal takes the player's input away entirely — the stick as well as the keyboard, and not just
		// the command keys the key handler already gates. Each of these panels runs a loop of its own in
		// the original (AlertPanel_Enter, poll the device, present) and that loop never calls Sim_MainTick,
		// so Sim_PollPlayerInput and its action switch do not run at all while one is up.
		// Nothing the player does on the stick reaches the machine, which is what makes pressing a button
		// on the CONTROLS panel safe: it picks that button's row and does not also fire what it is bound to.
		//
		// Every edge latch is refreshed rather than left alone, so a key or button pressed to work the
		// panel does not fire the moment the panel goes down.
		// A replay pilots whether or not the live player has gone to the free camera: the machine is the
		// tape's to drive, and the camera is only a way to watch it.
		bool pilotInput = piloting || TapePlaying();
		if (pilotInput && pilotMech != null && controls != null && AnyModalPanelOpen()) {
			pilotMech.Controls = MechControls.Neutral;
			TakeCameraAxes(MechControls.Neutral);
			joystickInput = JoystickPilotInput.None;
			joystickCenterBody = false;
			joystickBindings.Suspend(joystick?.Read() ?? JoystickReading.Neutral);
			fireRowLatched = true;

			allStopKeyDown = controls.IsKeyPressed(Key.Keypad5);
			shieldRearKeyDown = controls.IsKeyPressed(Key.LeftBracket);
			shieldFrontKeyDown = controls.IsKeyPressed(Key.RightBracket);
			radarKeyDown = controls.IsKeyPressed(Key.R);
			autoTrackKeyDown = !HddCommandHasKeyboard() && controls.IsKeyPressed(Key.T);
			cycleTargetKeyDown = controls.IsKeyPressed(Key.Enter);
			nearestTargetKeyDown = controls.IsKeyPressed(Key.Apostrophe);
			clearTargetKeyDown = controls.IsKeyPressed(Key.Semicolon);
			cycleComponentKeyDown = controls.IsKeyPressed(Key.Tab);
			ApplyWeaponKeys(controls, null);
		} else if (pilotInput && pilotMech != null && controls != null) {
			// The stick, read once and used twice: its axes go into MechControls at the bottom of this
			// block and its button edges are dispatched here. Both come out of the same twelve bytes of
			// prefs.cfg, resolved by JoystickBindings — the panel edits those bytes live, so a rebinding
			// takes effect on the next tick with nothing to reload. A replay's stick is the tape's.
			var stickReading = TapePlaying() || joystick is null ? JoystickReading.Neutral : joystick.Read();
			joystickInput = TapePlaying()
				? TapeJoystickInput()
				: joystick is null
					? JoystickPilotInput.None
					: joystickBindings.Resolve(stickReading, joystick.Capabilities, simulatorPreferences);
			RecordStick(joystickInput);

			joystickCenterBody = false;
			foreach (var action in joystickInput.Pressed) {
				ApplyJoystickAction(action, pilotMech);
			}

			// The hat under VIEWS. The original passes its four bytes straight to
			// CockpitView_PollViewDevice (00432b14), which queues view commands 1, 0, 5 and 4 — up, down,
			// and the left and right glances. Level-triggered like the original's, which the glance gate
			// makes safe: a held direction repeats a command the gate then ignores.
			if (joystickInput.Views.HasFlag(JoystickHat.North)) {
				RequestHeadsDown(headsDown: false);
			} else if (joystickInput.Views.HasFlag(JoystickHat.South)) {
				RequestHeadsDown(headsDown: true);
			} else if (joystickInput.Views.HasFlag(JoystickHat.West)) {
				CommandGlance(GlanceSide.Left);
			} else if (joystickInput.Views.HasFlag(JoystickHat.East)) {
				CommandGlance(GlanceSide.Right);
			}

			// Keypad [5], all stop: zero the throttle and let the gauge follow the machine this frame
			// rather than putting the old setting straight back. On its own edge, so holding it does not
			// fight a throttle the player is trying to open again.
			bool allStopKey = controls.IsKeyPressed(Key.Keypad5);
			if (allStopKey && !allStopKeyDown) {
				pilotMech.AllStop();
			}
			allStopKeyDown = allStopKey;

			// [[] and []], the manual's shield-balance keys — rear and forward. Both fire on their own
			// edge: the original clears the gauge's flag byte after acting on it, so a held key nudges
			// once, not once a tick. Nothing is spent moving the balance; it changes where the next
			// recharge tick puts the charge it is already holding.
			//
			// They click, because in the original the key does not call the adjust at all: Mech_HandleCommand
			// (004157c8) hands scancodes 0x1a/0x1b to Widget_PressChild on the shield gauge, which fires the
			// facing's own press slot — Widget_ForwardClickToOwner, which calls slot +8 of its second vtable
			// (0049ca01), and that slot is Widget_ClickSound: catalog id 0x11. Pressing the widget is also
			// what makes the two input routes agree by construction.
			bool shieldRearKey = controls.IsKeyPressed(Key.LeftBracket);
			bool shieldFrontKey = controls.IsKeyPressed(Key.RightBracket);
			if (shieldRearKey && !shieldRearKeyDown) {
				pilotMech.Shields.AdjustBalance(towardFront: false);
				audio.Director?.Play(SoundId.ButtonClick);
			}
			if (shieldFrontKey && !shieldFrontKeyDown) {
				pilotMech.Shields.AdjustBalance(towardFront: true);
				audio.Director?.Play(SoundId.ButtonClick);
			}
			shieldRearKeyDown = shieldRearKey;
			shieldFrontKeyDown = shieldFrontKey;

			ApplyWeaponKeys(controls, pilotMech.Weapons);

			// [R] switches the radar between PASSIVE and ACTIVE. Worth knowing before wondering why
			// nothing can be targeted: passive, this machine only ever knows about what it can see inside
			// visual range, and in the original what makes a distant enemy targetable is usually that
			// *enemy's* radar being on — which is AI behaviour the engine does not have yet.
			bool radarKey = controls.IsKeyPressed(Key.R);
			if (radarKey && !radarKeyDown && !CockpitWidgetsOff()) {
				pilotMech.ToggleScanner(scene.World);
			}
			radarKeyDown = radarKey;

			// [T] toggles ATT. The command display owns [T] as an order hotkey while it is down, so the
			// two are split the same way the arrows and [Backspace] are, and for the same reason.
			bool autoTrackKey = !HddCommandHasKeyboard() && controls.IsKeyPressed(Key.T);
			if (autoTrackKey && !autoTrackKeyDown && !CtrlHeld(controls)) {
				// Sim_DispatchCommand's 0x14 case toggles the TRACK widget and, if that turned it off,
				// latches the centring mode — so [T] off brings the turret home rather than leaving it
				// wherever the tracker had it. Backspace's own case is the mirror image.
				if (!pilotMech.ToggleAutoTrack(scene.World)) {
					pilotMech.LatchCenterTorso();
				}
			}
			autoTrackKeyDown = autoTrackKey;

			// Target selection. It is the cockpit's, not the machine's, so it is driven from here and
			// copied onto the machine below — see TargetSelection. [Enter] and [;] are the widgets' cases
			// and go dead with them in the external view, where [Enter] is the view's own; ['] is the
			// dispatcher's and keeps working.
			if (scene.Targeting is { } targeting) {
				bool widgetsOff = CockpitWidgetsOff();
				bool cycleTargetKey = controls.IsKeyPressed(Key.Enter);
				bool nearestTargetKey = controls.IsKeyPressed(Key.Apostrophe);
				bool clearTargetKey = controls.IsKeyPressed(Key.Semicolon);

				if (cycleTargetKey && !cycleTargetKeyDown && !widgetsOff) {
					targeting.Cycle();
				}
				if (nearestTargetKey && !nearestTargetKeyDown) {
					targeting.SelectNearest();
				}
				if (clearTargetKey && !clearTargetKeyDown && !widgetsOff) {
					targeting.Clear();
				}

				cycleTargetKeyDown = cycleTargetKey;
				nearestTargetKeyDown = nearestTargetKey;
				clearTargetKeyDown = clearTargetKey;
			}

			// [Tab] steps the Targeting Pod's component lock. CockpitWidgets_HandleCommand hands scancode
			// 0x0f to the pod only when the view is not the heads-down one; while the display is down the
			// same case goes to its own command slot, which is the manual's Zoom Map In/Out.
			bool cycleComponentKey = !cockpitPan.AtHeadsDown && controls.IsKeyPressed(Key.Tab);
			if (cycleComponentKey && !cycleComponentKeyDown && !CockpitWidgetsOff()) {
				pilotMech.CycleTargetComponent();
			}

			cycleComponentKeyDown = cycleComponentKey;

			bool commandHasKeys = HddCommandHasKeyboard();

			// A replay's axes are the tape's, which already hold whatever the keyboard contributed when it
			// was recorded, and they change only when a frame is taken.
			if (TapePlaying()) {
				if (tapeFrame != null && !tapeFrameUnderPanel) {
					pilotMech.Controls = TapeControls(tapeFrame,
						centerTorso: !commandHasKeys && controls.IsKeyPressed(Key.Backspace),
						centerBody: joystickCenterBody || controls.IsKeyPressed(Key.BackSlash));
					TakeCameraAxes(pilotMech.Controls);
				}
			} else {
				PilotFromLiveInput(pilotMech, controls, HddHasArrows(), commandHasKeys, stickReading);
			}
		} else {
			if (!TapePlaying()) {
				scene.Camera.Input = ReadInput(controls);
				if (pilotMech != null) {
					pilotMech.Controls = MechControls.Neutral;
				}

				TakeCameraAxes(MechControls.Neutral);
			}

			// Hands off the stick, but keep swallowing whatever is held on it: a button pressed to dismiss
			// a panel must not also fire its action the moment the panel goes down.
			joystickInput = JoystickPilotInput.None;
			joystickBindings.Suspend(joystick?.Read() ?? JoystickReading.Neutral);
		}

		// What a round the player is flying reads out of the input block: the two axes its camera-axis
		// pointers address and the trigger. While the controls are on a camera, or on the round, they are
		// the steering and throttle axes; otherwise they follow the JOYSTICK row, as Input_BuildPlayerDevice
		// sets them at its tail. Built from the controls just made, before the machine is handed none of
		// them below.
		if (pilotInput && pilotMech != null) {
			var built = pilotMech.Controls;
			scene.World.MissileSteer = !ControlsOnCamera() && StickTurretPair()
				? new MissileSteerInput(built.TorsoTwist, built.TorsoPitch, built.Fire)
				: new MissileSteerInput(built.Turn, built.Throttle, built.Fire);
		} else {
			scene.World.MissileSteer = default;
		}

		// The controls on the camera — the outside view's default, [Enter]'s swap and [Ctrl+T]'s hand-off —
		// or on an electro-optical round in flight, over whichever of the two above built them. The axes
		// went to viewCameraAxes and the round instead; the two centring commands are dispatcher cases and
		// still reach the machine. In the original, Sim_PollPlayerInput's camera branch tests
		// InputDrivesCamera alone: under it the machine gets no steering, throttle or twist, skips
		// Mech_PlayerFireTick and Mech_ApplyThrottleInput ignores a lever, but its pitch still reads a
		// lever (docs/formats/joystick-input.md, "While the camera has the controls"), which this does
		// not port. A round flown with InputDrivesCamera clear takes the original's ordinary branch, which
		// this neutralises as well.
		if (pilotInput && pilotMech != null && ControlsOnCamera()) {
			pilotMech.Controls = MechControls.Neutral with {
				CenterTorso = pilotMech.Controls.CenterTorso,
				CenterBody = pilotMech.Controls.CenterBody,
			};
		}

		// The free camera during a replay, which the live keyboard flies while the tape pilots.
		if (TapePlaying() && !piloting) {
			scene.Camera.Input = ReadInput(ImGuiHasKeyboard() ? null : liveKeys);
		}

		// Stick sign convention is the device's, not the game's: forward and left are negative. No
		// throttle lever, so the throttle's range spans both directions and holding [Down] takes the
		// machine through zero into reverse — see MechControls.ThrottleLever.
		//
		// [I]/[M]/[J]/[K] aim the turret and [Backspace] re-centres it, which is the manual's own
		// keyboard turret set. The turret's axes are rates, so holding a key sweeps it rather than
		// putting it somewhere; [Backspace] latches until either axis is touched again.
		//
		// [\] is the other half of that pair, Center Body: it walks the legs round under the turret
		// instead of bringing the turret back, taking the steering and the twist axis until they line
		// up. It latches on the keypress, and [Backspace] cancels it.
		// A held key is worth MechControls.KeyboardAxis, half a stick's travel — DBSIM's own keyboard
		// scale, and the difference between turning at the machine's rate and at twice it.
		//
		// While the Heads-Down Display is down the four arrows are its own instead of steering, which is
		// what the manual binds them to there: they scroll the command display's map and, on the damage
		// detail, step the herc and the category being inspected. The keypad keeps steering throughout,
		// so the machine is never left without a stick; the command display also keeps [Backspace]
		// cancelling a transmission rather than re-centring the turret. This is the one place the two
		// keyboards are separated rather than allowed to overlap, because working the display and
		// turning the machine with the same press is the one overlap that would fight the player.
		//
		// The stick is combined with all of that rather than replacing it: the original registers the
		// keyboard's two axis pairs in the same source table as the joystick's four axes and takes
		// whichever has moved, so a pilot can steer with one hand and nudge with the other. What the
		// stick reaches at all is the twelve binding bytes' business — see JoystickBindings.
		void PilotFromLiveInput(MechObject mech, IKeyState keys, bool hddHasArrows, bool commandHasKeys,
				JoystickReading stickReading) {
			// The first button row's latch: a flown round's end and a modal panel ask for it, and letting go
			// of the row — [Space] and the stick's first button — drops it. While it holds, the trigger reads
			// released and the axes are held below.
			if (scene.World.TakeFireRowLatch()) {
				fireRowLatched = true;
			}

			if (fireRowLatched && !keys.IsKeyPressed(Key.Space) && !stickReading.Button(0)) {
				fireRowLatched = false;
			}

			bool fireRowIsTrigger = joystickBindings.Action(simulatorPreferences, 0) == JoystickAction.Fire;
			bool rowZeroLatched = fireRowLatched
				|| (joystickBindings.ButtonLatched(0) && !fireRowIsTrigger);

			// Under the developer flag an arrow held with Ctrl or Alt is a move or turn key, and this engine
			// takes it off the steering and throttle axes. Retail keeps it on them — see KNOWN_ISSUES.md.
			bool arrowsAreCommands = developerKeys.Enabled && (CtrlHeld(keys) || AltHeld(keys));
			var keyboardAxes = new PilotAxes(
				(short)((arrowsAreCommands ? 0
					: hddHasArrows
					? Axis(keys, Key.Keypad6, Key.Keypad4)
					: Axis(keys, Key.Right, Key.Left, Key.Keypad6, Key.Keypad4)) * MechControls.KeyboardAxis),
				(short)((arrowsAreCommands ? 0
					: hddHasArrows
					? Axis(keys, Key.Keypad2, Key.Keypad8)
					: Axis(keys, Key.Down, Key.Up, Key.Keypad2, Key.Keypad8)) * MechControls.KeyboardAxis),
				TurretAxis(Axis(keys, Key.K, Key.J), heldTwist),
				TurretAxis(Axis(keys, Key.I, Key.M), heldPitch));

			// DAT_0049ebe5: the keyboard's first pair goes dead from the moment the first button row is
			// latched while a round is being flown until that row is let go, so the arrows steering the
			// round do not walk the machine off once it has gone.
			if (scene.World.MissileFlown) {
				missileKeyboardHold = 1;
			}

			if (rowZeroLatched && missileKeyboardHold == 1) {
				missileKeyboardHold = 2;
			} else if (!rowZeroLatched) {
				missileKeyboardHold = 0;
			}

			if (missileKeyboardHold != 0) {
				keyboardAxes = keyboardAxes with { Steer = 0, Throttle = 0 };
			}

			// Flying the round, the keyboard's pitch is the other way up unless keyjoy.cfg's Missile says
			// Reverse. The stick's is left alone.
			if (scene.World.MissileFlown && !joystickBindings.Keyjoy.ReverseMissile) {
				keyboardAxes = keyboardAxes with { Throttle = (short)-keyboardAxes.Throttle };
			}

			// While the controls drive the camera or the round the stick is re-pointed first, and what the
			// tape records is that — the device's own axes, which the camera and the round read.
			bool onCamera = ControlsOnCamera();
			var recordedAxes = onCamera
				? joystickBindings.CombineForCamera(stickReading, StickCapabilities(), simulatorPreferences, keyboardAxes)
				: joystickBindings.CombineBeforeBackturn(joystickInput, keyboardAxes);

			// The first button row latched holds the pair the camera axes point at still until it is let
			// go — after the tape has the axes, as the original zeroes them after it records.
			var held = recordedAxes;
			if (!onCamera && rowZeroLatched) {
				held = StickTurretPair()
					? held with { TorsoTwist = 0, TorsoPitch = 0 }
					: held with { Steer = 0, Throttle = 0 };
			}

			var axes = joystickBindings.ApplyBackturn(held);

			mech.Controls = new MechControls(
				axes.Steer,
				axes.Throttle,
				// Set when the stick has a lever and it is bound to THROTTLE rather than to the turret —
				// Input_SetThrottleLeverMode's own pair of conditions (00459d20). It is what closes the
				// throttle clamp to one side of zero, so a lever pilot cannot walk backwards through the
				// detent the way a keyboard one does.
				ThrottleLever: joystickBindings.ThrottleLeverMode(StickCapabilities(), simulatorPreferences),
				TorsoTwist: axes.TorsoTwist,
				TorsoPitch: axes.TorsoPitch,
				CenterTorso: !commandHasKeys && keys.IsKeyPressed(Key.Backspace),
				CenterBody: joystickCenterBody || keys.IsKeyPressed(Key.BackSlash),
				// [Space] is held, not pressed — see MechControls.Fire. Holding it keeps the armed weapon
				// firing as fast as its refire delay and its capacitor allow. So is the joystick trigger,
				// for the same reason and through the same byte.
				Fire: heldFire || (joystickInput.Fire && !(fireRowLatched && fireRowIsTrigger))
					|| (keys.IsKeyPressed(Key.Space) && !fireRowLatched));
			TakeCameraAxes(mech.Controls);

			// A tape records the axes ahead of Backturn, which playback applies again.
			tapeRecorder?.SetHeld(recordedAxes, mech.Controls.Fire, stickReading.Buttons);
		}

		// F1-F6 pick the MFD screen, the same keys and the same order as the original's own mode buttons
		// — button i of the display's F-key column dispatches SetMode(i), and this sets the same value.
		// Selecting one also pans back up to the cockpit, which is the manual's own rule for leaving the
		// Heads-Down Display ("select an MFD screen [F1]-[F6], press [Esc], or click the top of the
		// screen") and matches view command 1, the "up" half of the pair at 0042a3f4.
		if (controls != null && !CockpitWidgetsOff() && ReadMfdMode(controls) is { } requestedMfdMode) {
			SetMfdMode(requestedMfdMode);
			RequestHeadsDown(headsDown: false);
		}

		// FLASH COMM's own keyboard, from the two dispatches that share it. The bare letters
		// (MfdDisplay_KeyDispatch (004469c0)'s tail) only move the cursor and only while the page is up; the same letters with
		// [Alt] (MfdFlashComm_HandleAltKey, 00446c10) select the row and transmit it in one go, from whichever screen is showing,
		// which is why they are the shortcuts the manual gives. Each letter is the one its order's own
		// attribute byte draws in red.
		//
		// Two of the six positions carry two orders, and the two keys that share them are not
		// interchangeable: [C] SCAN FOR HOSTILES and [E] EMCON both select row 4, but each only transmits
		// while that row is showing its own verb, so [Alt+C] on a row already reading EMCON selects and
		// says nothing. [F] has no such partner — row 5 transmits whichever of FIRE AT WILL and HOLD YOUR
		// FIRE it currently reads.
		if (controls != null && scene.World is { } flashCommWorld) {
			bool flashCommUp = FlashCommHasKeyboard();

			// The widgets are off in the external view, which is where these letters would go through them.
			bool alt = AltHeld(controls) && !CockpitWidgetsOff();

			// Every key here is a bare or an [Alt] code. With [Ctrl] down the code is another one — [Ctrl+F],
			// and the developer keys' [Ctrl+Alt+D], [.] and [,] — so none of them lands here.
			bool ctrl = CtrlHeld(controls);

			bool Transmit() {
				bool accepted = flashComm.Transmit(flashCommWorld, flashCommWorld.PlayerMech?.Group);
				audio.Director?.Play(SoundId.ButtonClick);
				return accepted;
			}

			for (int i = 0; i < FlashCommKeys.Length; i++) {
				var (key, row, requiredVerb) = FlashCommKeys[i];
				if (!Edge(key, ref flashCommKeysDown[i]) || ctrl) {
					continue;
				}

				if (!alt) {
					if (flashCommUp) {
						flashComm.Select(row, flashCommIsUp: true);
					}

					continue;
				}

				// The [Alt] arm writes the screen's own row first and only then tests the verb, so a key
				// whose order is not the one showing still moves the cursor. MfdFlashComm_SelectRow (00447130) is what refuses
				// to move the display's row from another screen.
				flashComm.Select(row, flashCommIsUp: hudState.Mfd == MfdMode.FlashComm);
				if (requiredVerb < 0 || flashComm.SelectedVerb == requiredVerb) {
					Transmit();
				}
			}

			if (flashCommUp) {
				// [X] presses XMIT, which is aux button 10 — the same press a click on the button makes.
				if (Edge(Key.X, ref flashCommTransmitKeyDown)) {
					Transmit();
				}

				// [.] and [,] walk the list past any row the squad cannot take.
				if (Edge(Key.Period, ref flashCommNextRowKeyDown) && !ctrl) {
					flashComm.StepRow(1);
				}
				if (Edge(Key.Comma, ref flashCommPreviousRowKeyDown) && !ctrl) {
					flashComm.StepRow(-1);
				}
			}

			// [Alt+D] is command 0x220, which the cockpit view claims in its own handler before the panel
			// below it ever sees it: it drops a nav marker rather than transmitting DISENGAGE.
			if (alt && Edge(Key.D, ref navMarkerKeyDown) && !ctrl && flashCommWorld.PlayerMech is { } marking) {
				navMarker.Drop(marking.Position);
			}

			bool Edge(Key key, ref bool held) {
				bool down = controls.IsKeyPressed(key);
				bool edge = down && !held;
				held = down;
				return edge;
			}
		}

		// F7 (Command Display) and F8 (Damage Detail) are the two HDD functions, and per the manual
		// either one opens the display — so each both pans down and selects its own screen, which is
		// what the display's own two page buttons dispatch (HddDisplay_SetPage (0044a5e4) with the button's index).
		if (cockpitHeadsDownTexture != null && controls != null && !CockpitWidgetsOff()) {
			if (controls.IsKeyPressed(Key.F7)) {
				hudState = hudState with { Hdd = HddPage.CommandDisplay };
				RequestHeadsDown(headsDown: true);
			} else if (controls.IsKeyPressed(Key.F8)) {
				hudState = hudState with { Hdd = HddPage.DamageDetail };
				RequestHeadsDown(headsDown: true);
			}
		}

		// F9 and F10 are the manual's left and right windows, view commands 5 and 4. On the key's edge,
		// not its level: from a glance the opposite key is a return, so a held key would otherwise go on
		// to start the other glance the moment the strip got back.
		if (cockpitArt != null && controls != null) {
			bool glanceLeftKey = controls.IsKeyPressed(Key.F9);
			bool glanceRightKey = controls.IsKeyPressed(Key.F10);
			if (glanceLeftKey && !glanceLeftKeyDown) {
				CommandGlance(GlanceSide.Left);
			} else if (glanceRightKey && !glanceRightKeyDown) {
				CommandGlance(GlanceSide.Right);
			}

			glanceLeftKeyDown = glanceLeftKey;
			glanceRightKeyDown = glanceRightKey;
		}

		// The command display's own keyboard, from the manual's COMMAND DISPLAY table and the screen's
		// own key dispatch (0044cc40, which switches on scancodes and matches that table exactly). Gated
		// on the screen actually being down, the same way [S]/[I]/[W] are gated on the damage screen:
		// most of these letters are also cockpit or camera bindings in this host, and in the original
		// they mean nothing anywhere else either.
		//
		// Everything here fires on the key's own edge. The original's dispatch is a keydown handler, and
		// a held order key that re-armed the same order every frame would clear the map pick that had
		// just been made for it.
		if (controls != null && hddCommand is { } command
			&& cockpitPan.AtHeadsDown && hudState.Hdd == HddPage.CommandDisplay) {
			// The screen's dispatch matches bare scancodes, so a key under [Ctrl] or [Alt] — a developer
			// key, most of the letters here — is not one of its own.
			bool modified = CtrlHeld(controls) || AltHeld(controls);
			for (int i = 0; i < HddCommandKeys.Length; i++) {
				if (Edge(HddCommandKeys[i], ref hddOrderKeysDown[i]) && !modified) {
					command.SelectOrder((HddOrder)i);
				}
			}

			// [,] and [.] walk the list without the pointer, and only once an order is already armed —
			// both functions return immediately otherwise.
			if (Edge(Key.Comma, ref hddPreviousOrderKeyDown) && !modified) {
				command.StepOrder(-1);
			}
			if (Edge(Key.Period, ref hddNextOrderKeyDown) && !modified) {
				command.StepOrder(1);
			}

			// [1]-[3] pick the pilot, left to right, which is what the number under each comm box says. The
			// display's key dispatch (HddDisplay_KeyDispatch, 00449fcc) presses the comm box's own widget for these, and the two
			// magnifiers' and the four arrows' for theirs, so all nine go through the button's press — which
			// is what holds them back while the sensor dropout has the display dark.
			for (int slot = 0; slot < HddPilotKeys.Length; slot++) {
				if (Edge(HddPilotKeys[slot], ref hddPilotKeysDown[slot]) && !modified) {
					ApplyHddClick(HddLayout.Widget.PilotBox0 + slot);
				}
			}

			// [+] and [-], the two magnifiers.
			if (Edge(Key.Equal, ref hddZoomInKeyDown) || Edge(Key.KeypadAdd, ref hddZoomInPadKeyDown)) {
				ApplyHddClick(HddLayout.Widget.ZoomIn);
			}
			if (Edge(Key.Minus, ref hddZoomOutKeyDown) || Edge(Key.KeypadSubtract, ref hddZoomOutPadKeyDown)) {
				ApplyHddClick(HddLayout.Widget.ZoomOut);
			}

			// The arrows scroll the map, held rather than edged: the four pan functions are written to be
			// called repeatedly and clamp themselves against the mission box. Dark, a press is one press of
			// the arrow's button, held back with the rest. Keypad [5] drops the scroll and puts the map back
			// on the machine.
			for (int i = 0; i < HddArrowKeys.Length; i++) {
				if (Edge(HddArrowKeys[i], ref hddArrowKeysDown[i]) && cockpitDropouts.HeadsDown.Dark && !modified) {
					ApplyHddClick(HddLayout.Widget.ArrowUp + i);
				}
			}

			if (!cockpitDropouts.HeadsDown.Dark && !modified) {
				command.View.Pan(
					(controls.IsKeyPressed(Key.Right) ? 1 : 0) - (controls.IsKeyPressed(Key.Left) ? 1 : 0),
					(controls.IsKeyPressed(Key.Up) ? 1 : 0) - (controls.IsKeyPressed(Key.Down) ? 1 : 0));
			}
			if (Edge(Key.Keypad5, ref hddRecentreKeyDown)) {
				command.View.Recentre();
			}

			// [X] transmits and [Backspace] cancels. The transmit's two blips are the radar-mode tone
			// pair, which HddCommandScreen_KeyDispatch reuses as accepted and rejected — see docs/formats/audio.md.
			if (Edge(Key.X, ref hddTransmitKeyDown)) {
				audio.Director?.Play(command.Transmit() ? SoundId.ScannerActive : SoundId.ScannerPassive);
			}
			if (Edge(Key.Backspace, ref hddCancelKeyDown)) {
				command.Cancel();
			}

			bool Edge(Key key, ref bool held) {
				bool down = controls.IsKeyPressed(key);
				bool edge = down && !held;
				held = down;
				return edge;
			}
		} else {
			Array.Clear(hddOrderKeysDown);
			Array.Clear(hddPilotKeysDown);
			Array.Clear(hddArrowKeysDown);
			hddPreviousOrderKeyDown = hddNextOrderKeyDown = false;
			hddZoomInKeyDown = hddZoomOutKeyDown = false;
			hddZoomInPadKeyDown = hddZoomOutPadKeyDown = false;
			hddRecentreKeyDown = hddTransmitKeyDown = hddCancelKeyDown = false;
		}

		// The damage detail's three component categories, on the manual's own [S]/[I]/[W] bindings — the
		// same three the display's up/down arrow buttons step through. Only while that screen is actually
		// down: [S] and [W] are also two thirds of this host's camera movement, and the original has no
		// such clash because its own [S]/[I]/[W] only mean anything on this screen either.
		if (controls != null && cockpitPan.AtHeadsDown && hudState.Hdd == HddPage.DamageDetail
			&& !CtrlHeld(controls) && !AltHeld(controls) && ReadHddDamageView(controls) is { } damageView) {
			hudState = hudState with { HddDamage = damageView };
		}

		// The four arrows press the display's own four arrow buttons, as HddDisplay_KeyDispatch (00449fcc)
		// does on either page: here, up and down step the category and left and right the herc. Once per
		// press, since each is one step.
		if (controls != null && HddHasArrows() && hudState.Hdd == HddPage.DamageDetail) {
			bool modified = CtrlHeld(controls) || AltHeld(controls);
			for (int i = 0; i < HddArrowKeys.Length; i++) {
				bool down = controls.IsKeyPressed(HddArrowKeys[i]);
				if (down && !hddDamageArrowKeysDown[i] && !modified) {
					ApplyHddClick(HddLayout.Widget.ArrowUp + i);
				}

				hddDamageArrowKeysDown[i] = down;
			}
		} else {
			Array.Clear(hddDamageArrowKeysDown);
		}

		// Clicks are drained before the pan advances and before the sim ticks, so a click and the tick
		// that reacts to it keep a fixed order every frame — the point of queueing them in the first
		// place. The layout is rebuilt from this frame's pan position, which is the same one Render will
		// use, so what the player is clicking is what they are looking at.
		// Nothing to click while the cockpit is off screen, so the whole click path sits out the external
		// view rather than hit-testing a console the player cannot see — and likewise while the pointer is
		// over the debug panel, so a click on a checkbox is not also a click on the console behind it.
		// Everything held while the CONTROLS panel is down counts as already acted on, so a button being
		// used for something else when the panel comes up does not also step a row. The mask then decays
		// to the buttons actually held as ReadControlsPanelJoystick intersects it — which on the panel's
		// first frame is exactly the set to swallow. It is the same priming JoystickBindings.Suspend does
		// for the simulation's own latch, and for the same reason.
		if (controlsPanel is not { IsOpen: true }) {
			controlsPanelLatched = 0xff;
		}

		if (AnyModalPanelOpen()) {
			// The modal owns the pointer: the cockpit behind it takes no clicks, and the queue is drained
			// to nothing so a click made while it was up cannot land on a console button afterwards.
			var framebufferForPanel = window.FramebufferSize;
			if (statusAlertPanel is { IsOpen: true } liveAlert) {
				// The panel's own placement, not a fixed one: the status alert and the pause panel are
				// different sizes and so centre to different origins.
				ReadPanelPointer(
					liveAlert.Place(framebufferForPanel.X, framebufferForPanel.Y),
					liveAlert.PointerDown, (x, y, _) => liveAlert.PointerUp(x, y));
			} else if (objectivesPanel is { IsOpen: true } liveObjectives) {
				ReadPanelPointer(
					ObjectivesPanelLayout.Place(framebufferForPanel.X, framebufferForPanel.Y),
					(x, y) => liveObjectives.PointerDown(x, y),
					(x, y, _) => liveObjectives.PointerUp(x, y));
			} else if (controlsPanel is { IsOpen: true } liveControls) {
				// The controls panel is the one modal this engine draws that opens over another: it takes the
				// pointer while it is up and the preferences strip below it stays visible but inert.
				ReadPanelPointer(
					ControlsPanelLayout.Place(framebufferForPanel.X, framebufferForPanel.Y),
					(x, y) => liveControls.PointerDown(x, y),
					(x, y, right) => liveControls.PointerUp(x, y, right));

				// And the stick itself, which on this one panel is an input device rather than a pair of
				// menu keys — see ControlsPanel.PressButtonRow.
				ReadControlsPanelJoystick(liveControls);
			} else if (preferencesPanel is { IsOpen: true } livePreferences) {
				// This one is pinned to the bottom of the screen rather than centred, so its placement is
				// its own — see PreferencesPanelLayout.
				ReadPanelPointer(
					PreferencesPanelLayout.Place(framebufferForPanel.X, framebufferForPanel.Y),
					(x, y) => livePreferences.PointerDown(x, y),
					(x, y, right) => livePreferences.PointerUp(x, y, right));

				// CONTROLS raises the controls panel over this one, which is how the original reaches it
				// and the only way in.
				if (livePreferences.ControlsRequested) {
					livePreferences.ClearControlsRequest();

					// Re-read what the stick can do on the way in rather than trusting what it could at
					// startup: ControlsPanel_Run (00458650) asks Input_QueryCapabilities at the top of its own
					// loop, and that function rebuilds its eight bytes every call. So a stick plugged in
					// mid-mission lights the rows up, and one unplugged greys them.
					if (controlsPanel is not null && joystick is { Capabilities.Present: true } liveStick) {
						controlsPanel.Capabilities = liveStick.Capabilities;
					}

					controlsPanel?.Open();
				}
			}

			cockpitInput.Drain(deltaSeconds, (_, _) => null);

			// And nothing behind it stays depressed: entering a panel calls Widget_ClearPressed (00452b94), which swaps the
			// panel's own clickable list in and clears Widget_PressedIndex to -1, dropping whatever the
			// cockpit had held when the panel was raised.
			hudState = hudState with { PressedWidget = null };
		} else if (cockpitArt != null && !ExternalViewActive()
				&& (TapePlaying() || imgui == null || !ImGui.GetIO().WantCaptureMouse)) {
			var framebuffer = window.FramebufferSize;
			var inputLayout = CockpitScreenLayout.Create(framebuffer.X, framebuffer.Y, cockpitArt,
				cockpitPan.OffsetRows, cockpitPan.TravelRows, cockpitGlance.OffsetPanels);

			foreach (var click in cockpitInput.Drain(deltaSeconds, inputLayout, cockpitArt, hudState)) {
				ApplyCockpitClick(click);
			}

			// The one draggable control. Dragging the slider sets the gauge, and the machine picks that up
			// on this frame's exchange unless its own input moved the throttle first.
			foreach (var drag in cockpitInput.Drags) {
				if (drag.Id.Kind == CockpitWidgetKind.Throttle && throttleTrack is { } track) {
					throttleGauge = track.ThrottleAt(drag.ArtY);

				}

				// A charge bar, listed only under the ChargeBarPowerLevel tweak. Retail's slider commits on
				// the release alone, so the positions on the way there do nothing.
				if (drag is { Released: true, Id.Kind: CockpitWidgetKind.WeaponChargeBar } && pilotMech != null
					&& ChargeBarSlider.For(cockpitArt, drag.Id.Index) is { } bar) {
					pilotMech.Weapons.SetPowerFromChargeBar(drag.Id.Index, bar.PositionAt(drag.ArtX));
				}
			}

			// Held buttons draw depressed, and pop back up if the pointer slides off them still held.
			hudState = hudState with { PressedWidget = cockpitInput.Depressed };
		}

		// Player_PerFrameCockpitUpdate's own copy: whatever the cockpit has selected becomes the
		// machine's mech+0x1a4, once a frame and before the sim ticks, so a weapon fired during the tick
		// sees this frame's target. The drop that precedes it is the cockpit update's own — see
		// TargetSelection.DropIfInvalid.
		if (scene.Targeting is { } playerTargeting) {
			playerTargeting.DropIfInvalid(cockpitShown: !ExternalViewActive());
			playerTargeting.PushToPilot();
		}

		hddCommand?.Update(TimeSpan.FromSeconds(deltaSeconds));

		// The page's paint copies the display's row onto the screen every time it runs, and here every
		// frame the page is up is a repaint.
		if (hudState.Mfd == MfdMode.FlashComm) {
			flashComm.Sync();
		}

		cockpitPan.Advance(deltaSeconds);
		if (cockpitArt != null) {
			var glanceFramebuffer = window.FramebufferSize;
			cockpitGlance.Advance(deltaSeconds,
				CockpitScreenLayout.GlanceReachPanels(glanceFramebuffer.X, glanceFramebuffer.Y, cockpitArt));
		}

		// Clamping the accumulator stops a long stall (a breakpoint, a window drag) from turning into
		// a burst of catch-up ticks that would teleport everything. A frozen sim neither ticks nor
		// accumulates, so dismissing a panel carries on from where it stopped rather than catching up.
		// The objectives panel stops the clock the same way: the original's modal loop polls input,
		// repaints its own widgets and presents, and never reaches the sim tick.
		// The poll raises the same panel by itself once the mission is decided — Sim_MainTick's own
		// arm, latched on SimWorld.PendingMissionAlert by the tick that produced it.
		RaisePendingMissionAlert();

		// --quit stages the [Q] panel, which needs a ticked world to evaluate against, so it is raised on
		// the first update rather than at load.
		if (startWithStatusAlert) {
			startWithStatusAlert = false;
			if (stagedStatusAlert >= 0) {
				OpenStatusAlert((MissionStatus)stagedStatusAlert, scene.World.Objectives);
			} else {
				RaiseStatusAlertForQuit();
			}
		}

		ApplyStatusAlertAnswer();

		// TERRAIN TEXTURE, re-read every frame rather than watched for changes: it is one byte and one
		// nullable handle, and the preferences panel that steps it is drawn over a frozen scene that is
		// still being rendered behind it — so the player sees the ground change under the panel, which is
		// what the original shows them too.
		if (terrainItem is not null) {
			terrainItem.TextureHandle = TerrainTextureHandle();
		}

		// EFFECTS DETAIL, the same way: Sound_DetailSetting (004d1fc7) is prefs option 11, read where it
		// is used -- by a collapsing structure's smoke, a debris piece's burst and the sound throttle.
		byte effectsDetail = simulatorPreferences[Prefs.EffectsDetailOption];
		scene.World.EffectsDetail = effectsDetail;
		if (audio.Director is { } soundDirector) {
			soundDirector.DetailSetting = effectsDetail;
		}

		// And the two message channels' modes, which each port tests as it shows a line: COMPUTER MESSAGE
		// (ComputerMessageMode, 004d1fbf) in MessagePort_Show, PILOT MESSAGE (004d1fbe) in the pilot
		// port's paint. The voice half of PILOT MESSAGE is its handler's, registered at startup.
		audio.Messages.Mode = (MessageChannelMode)simulatorPreferences[Prefs.ComputerMessageOption];
		if (audio.Squad is { } squadChannel) {
			squadChannel.Port.Mode = (MessageChannelMode)simulatorPreferences[Prefs.PilotMessageOption];
		}

		// Every modal freezes the simulation behind it, which is the original's own behaviour: each of
		// these panels raises DAT_004d2576 while it is up and restores it on the way out --
		// PreferencesPanel_Raise (0045cfd4) for the preferences panel, and the controls panel is raised
		// over that one. The accumulator is held with it, so closing a panel does not pay back the time
		// it was up as a burst of catch-up ticks.
		bool frozen = missionOver || AnyModalPanelOpen();

		// The same panels pause both message ports (AlertPanel_Enter, 00454630), so a line on screen when
		// one comes up is still there, with the rest of its time, when it goes.
		audio.MessagesPaused = AnyModalPanelOpen();

		// A replay ticks on the tape's frames instead, each for the time it recorded.
		if (TapePlaying() || tapeFrame != null) {
			RunTapeTicks();
		} else {
			// A recording writes one frame per host frame a modal panel is up for, as a panel's own loop
			// builds the input once a pass. The frame that takes the panel down also finishes the tick a
			// frame's own input held back when it raised the panel, as playback does.
			if (tapeRecorder != null && recordPanelAtStart) {
				tapeRecorder.EmitPanel(StickCapabilities());
				if (recordDeferredTick && !missionOver && !AnyModalPanelOpen()) {
					recordDeferredTick = false;
					TickLive(emit: false);
					frozen = missionOver || AnyModalPanelOpen();
				}
			}

			bool ticked = false;
			if (developerKeys.StepPending && !frozen) {
				// Alt+keypad +: this tick and no more. Sim_MainTick re-freezes at the top of the next one.
				scene.World.Tick();
				AdvanceViewChain();
				debugPanel.SampleBeams(scene.World);
				RecordTick();
				developerKeys.FinishStep();
				tickAccumulator = 0;
				ticked = true;
			} else {
				if (!frozen) {
					tickAccumulator = Math.Min(tickAccumulator + deltaSeconds, MaxAccumulatedSeconds);
				}
				while (!frozen && tickAccumulator >= SecondsPerTick) {
					tickAccumulator -= SecondsPerTick;
					ticked = true;
					frozen = TickLive(emit: true);
				}
			}

			// This frame's own input raised a panel, so nothing ticked: the frame goes on the tape as one
			// that ticks, and playback holds its tick back until the panel is down, as the recording does.
			if (tapeRecorder != null && !recordPanelAtStart && !ticked && !missionOver && AnyModalPanelOpen()) {
				tapeRecorder.EmitTick(SimWorld.TickDelta, StickCapabilities());
				recordDeferredTick = true;
			}
		}

		foreach (var (sceneObject, item) in movers) {
			item.Transform = MissionScene.TransformOf(sceneObject);
		}

		// Each node of an animating machine is re-read here, alongside the whole-object transforms above.
		// Reading more often than the simulation ticks costs nothing and gains nothing: the thread's
		// intra-frame fraction only moves in Advance, so consecutive reads between ticks return the same
		// pose. That is the original's cadence too — see docs/formats/dts-node-posing.md's "Evaluation cadence".
		// Which root of each machine's shape is drawn, and which level of every detail part, settled before
		// the two loops that follow so that a piece taken up this frame is posed and gated this frame
		// rather than one frame stale.
		SelectDetailRoots();
		SelectDetailLevels();

		foreach (var (subject, transformId, item) in posedParts) {
			if (!item.DetailSelected) {
				continue;
			}

			item.Transform = MissionScene.PosedTransformOf(subject, transformId);
		}

		// The arrival gate, run before the sequence gate below so that a part which is both waiting and
		// gated is answered by the gate once its group is in the mission. An entry is dropped the frame
		// it arrives -- a group deploys once and never goes back.
		for (int i = undeployed.Count - 1; i >= 0; i--) {
			var (owner, parts) = undeployed[i];
			if (owner.AwaitingDeployment) {
				continue;
			}

			foreach (var (part, visible) in parts) {
				part.Visible = visible;
			}

			undeployed.RemoveAt(i);
		}

		// Which cell of each animation sequence is on screen, read straight off the object the way
		// TSCellAnimPart_Render reads shapeInstance+8. Every piece the shape holds is already uploaded,
		// so a destroyed component or a collapsed structure part costs a flag rather than a rebuild.
		foreach (var (owner, gate, item) in gatedParts) {
			if (!item.DetailSelected) {
				continue;
			}

			item.Visible = !owner.AwaitingDeployment && gate.VisibleIn(owner.CellFrames);
		}

		RefreshWreckItems();
		RefreshProjectileItems();
		RefreshDebrisItems();
		RefreshDropPodItems();
		RefreshGroundShapeItems();
		RefreshWeaponItems();
		RefreshSpriteBatches();

		// Dropping out of the cockpit for the fly camera puts the palette back rather than leaving a
		// flash up with nothing ticking it. The piloted case is applied below, after the shake's own tick.
		if (!piloting || pilotMech == null) {
			ApplyDamageFlash(false);
		}

		if (piloting && pilotMech != null) {
			// The kick and the shake are the pilot's own, so they run only from inside the cockpit. The
			// original's view mode 4 drops a shake in progress rather than pausing it, and ignores the hits
			// taken meanwhile, which is what Reset does here.
			cockpitHitShake.FlashSurvivesRestart =
				TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.FlashThroughSecondHit);
			if (ExternalViewActive()) {
				cockpitViewKick.Reset();
				cockpitHitShake.Reset(pilotMech.CockpitHits);
			} else {
				cockpitViewKick.Update(deltaSeconds, pilotMech.Footfalls);
				cockpitHitShake.Update(deltaSeconds, pilotMech.CockpitHits);
			}

			// Staged once, at the earliest frame the capture could fire: a shake lasts under a second and
			// restarting it would stop the view half — see CockpitHitShake.
			if (stageHitShake && !hitShakeStaged && framesRendered >= ScreenshotWarmupFrames) {
				hitShakeStaged = true;
				cockpitHitShake.Start();
			}

			ApplyDamageFlash(cockpitHitShake.FlashActive);

			if (MouseOutsideView() && ExternalViewActive()) {
				// The tweak's outside view: the mouse orbit round whatever the chain is viewing — see
				// ExternalCamera.
				ExternalCamera.Place(camera, viewChain!.Watched, terrain,
					BinaryAngle.FromRadians(externalOrbitYaw), BinaryAngle.FromRadians(externalOrbitPitch));
			} else if (viewChain is { } chain
					&& (chain.Camera.Mode != ViewCameraMode.Attached || chain.Camera.Target != pilotMech)) {
				// Every view but the player's own cockpit is the chain's camera, placed on the last tick —
				// see ViewCamera. The cockpit stays below, where the eye is pinned and read every frame.
				chain.Camera.ApplyTo(camera);
			} else {
				// The eye rides the model node the type record names, so the walk cycle's bob comes with it —
				// see MechObject.EyePosition. Camera yaw runs opposite to a simulation heading; see
				// MissionScene.TransformOf.
				//
				// The debug panel's "steady eye" pins the eye's *height* to whatever it was the moment the
				// toggle went on and leaves everything else — the machine's own travel, its lean, the eye's
				// fore/aft swing — alone. That isolates the vertical bob from the ride without touching the
				// animation that produces either, which is the A/B for "is it the eye or the machine?".
				var eyeFrame = pilotMech.EyeTransform;
				var eye = debugPanel.PinEyeHeight(
					new Vec3i(eyeFrame.X, eyeFrame.Y, eyeFrame.Z), pilotMech.Position);

				// Orientation comes off the eye node too, not off the machine's heading: the camera node
				// hangs below the two nodes the torso sequences drive, so twisting and pitching the turret
				// turns the view without anything here having to add the angles in.
				//
				// All three angles are taken, roll included, which is what the cockpit branch of
				// Cam_Update (004011a0) does: it converts the pilot node's world matrix with Transform_RotationToEuler (0047f894) and
				// stores the whole triple in the view. A walking machine's node barely rotates, but one
				// turning on the spot rolls it several degrees a step — the rock through a turn-in-place.
				var look = eyeFrame.ToEuler();
				camera.Position = eye;
				camera.Yaw = -look.Z & 0xffff;
				camera.Pitch = look.X;
				camera.Roll = look.Y;
			}

			// Keep the observer camera on the machine, so switching to it lands where the player was
			// rather than wherever it was parked at mission start.
			scene.Camera.Position = camera.Position;
			scene.Camera.Heading = camera.Yaw;
		} else {
			scene.Camera.ApplyTo(camera);
		}

		// The listener is the camera, as it is in the original — so the external view hears the machine
		// from behind it rather than from inside it. Camera yaw runs opposite to a simulation heading
		// (see above), and the placement rules work in the simulation's, so it is negated back here.
		audio.SetListener(camera.Position, -camera.Yaw & 0xffff);
		audio.Update(TimeSpan.FromSeconds(deltaSeconds));

		// A destroyed squadmate's box goes to static. The original's idle paint reads the machine's own
		// destroyed flag, so the channel is handed it fresh each frame.
		for (int slot = 0; slot < SquadCommChannel.SlotCount; slot++) {
			squadComm.SetDestroyed(slot, squadSeats[slot] is { Destroyed: true });
			pilotVideos[slot] = squadComm.Video(slot);
		}

		// What the debug panel reports about the walk — see DebugPanel.Sample for why it is measured
		// every frame rather than only while the panel is up.
		debugPanel.Sample(pilotMech);

		if (acquireTarget && pilotMech != null && scene.Targeting is { } startupTargeting
				&& --acquireTargetDelay <= 0) {
			if (startupTargeting.SelectNearest() is { } acquired) {
				acquireTarget = false;
				Console.WriteLine($"Target acquired: {acquired.GetType().Name} at "
					+ $"{pilotMech.Position.ApproxDistanceTo(acquired.Position)} world units.");
			}
		}

		if (cockpitArt != null && pilotMech != null) {
			// Player_PerFrameCockpitUpdate's own order: the range to the selected target, then the weapon
			// manager's pass, then the gauge and the machine settle which of them moved this frame, then
			// the readouts are taken from the machine. The range is zero when nothing is selected, which
			// is what turns the manager's range gate off.
			int targetRange = pilotMech.Target is { } weaponTarget
				? pilotMech.Position.ApproxDistanceTo(weaponTarget.Position)
				: 0;
			pilotMech.Weapons.PerFrameUpdate(targetRange, scene.World.MissileFlown);

			// The pods' own tick runs from inside that pass in the original, and only ever for the machine
			// the cockpit belongs to. It is what carries the ECM and Turbo rows' buttons into the sim.
			pilotMech.PodTick(scene.World);
			throttleGauge = pilotMech.ExchangeCockpitThrottle(throttleGauge);

			// HudClock_Tick (0043dcac) runs inside the gunsight's paint, so a view with no gunsight in it does not
			// advance the clock at all.
			if (!ExternalViewActive()) {
				missionClock.Advance(deltaSeconds);
			}

			// NavMarker_Tick (004349ac) runs from the cockpit's own paint, one frame apart, and is what arms the marker
			// on leaving it and clears it — announcing WAYPOINT REACHED — on coming back.
			navMarker.Tick(pilotMech.Position, audio.Messages);

			// Player_ResolveTargetAimPoint, once a frame and once only: it runs the Targeting Pod's decay
			// countdown as a side effect, so asking twice would halve how long a damaged pod holds a
			// component. Both consumers — the front-window target box and the MFD's F5 doll — read this.
			var targetAim = pilotMech.ResolveTargetAimPoint();

			// Cockpit_PowerUpTick's arming pass, which runs from the cockpit's own per-frame update.
			cockpitPowerUp.Tick(audio.CoarseTicks);

			// The dish's power-up animation, on whichever screen the display is showing this frame. Asked
			// before the dropout, because it is what decides whether the MFD's update gets that far.
			bool scannerShowing = hudState.Mfd == MfdMode.Scanner;
			int? mfdPowerUpFrame = cockpitPowerUp.MfdFrame(scannerShowing, audio.CoarseTicks);

			// A Heads-Down Display press held while it was dark, acted on by the first update that finds it
			// back. The display's update handles its pending press before it ticks the dropout, so this goes
			// first too, and a press waits one frame past the flip.
			if (pendingHddPress is { } heldPress && !cockpitDropouts.HeadsDown.Dark && !ExternalViewActive()) {
				ApplyHddClick(heldPress);
			}

			// The sensor dropout, from each display's own update. The forward console's rows and the MFD only
			// update while they are on screen, which a fully panned heads-down view is not.
			if (scene.World is { } dropoutWorld) {
				var mounts = pilotMech.Weapons;
				cockpitDropouts.Tick(SensorDropout.SensorCondition(pilotMech), audio.CoarseTicks,
					dropoutWorld.PresentationRandom,
					cockpitUp: !ExternalViewActive(),
					consoleOnScreen: !cockpitPan.AtHeadsDown,
					mfdUpdating: cockpitPowerUp.MfdReachesDropout(scannerShowing),
					rowTicking: row => mounts.BySlot(row) != null && cockpitPowerUp.RowPowered(row));
			}

			hudState = hudState with {
				MissionTime = missionClock.Text,
				SpeedKph = pilotMech.DisplaySpeedKph,
				Throttle = throttleGauge,
				TorsoTwist = pilotMech.TorsoTwistAngle,

				// What the gunsight hands the heading tape: the machine's own heading out of mech+0x10, except
				// while the cockpit's power-up wind-up is still running, when it is that ramp instead. The
				// sweep latches itself as it goes, so this is the once-a-frame call it expects.
				Heading = headingSweep?.Angle((short)pilotMech.Heading, audio.CoarseTicks)
					?? (short)pilotMech.Heading,
				ShieldFront = pilotMech.Shields.FrontReadout,
				ShieldRear = pilotMech.Shields.RearReadout,
				EnergyFraction = pilotMech.EnergyPoolFraction,
				Weapons = WeaponRowState.Build(pilotMech.Weapons,
					cockpitArt.Gau.WeaponListTotal, cockpitArt.Strings, cockpitPowerUp, audio.CoarseTicks,
					cockpitDropouts),
				Dropout = cockpitDropouts.Snapshot,
				ChargeBarsDraggable = TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.ChargeBarPowerLevel),
				ChainGroup = pilotMech.Weapons.Group,
				AutoTrack = pilotMech.Weapons.AutoTrack,
				Target = ResolveTargetIndicator(pilotMech, targetAim),
				StatusSubject = MfdStatusSubject.For(pilotMech, pilotMech, cockpitArt.Strings),
				// F5's subject is the selection, and it carries the Targeting Pod's component on top of what
				// the subject itself says — the pod belongs to the machine looking, not to what it is
				// looking at. Only the id the pod's own present flag vouches for reaches it.
				TargetSubject = MfdStatusSubject.For(scene.Targeting?.Selected, pilotMech, cockpitArt.Strings)
					with { HighlightComponent = targetAim.ComponentTargeted ? targetAim.Component : -1 },

				// The damage detail's subject, re-read every frame: on the target slot it follows the
				// selection, as HddDisplay_Update (00449bd0) re-points it whenever the selection changes.
				HddSubject = HddDamageSubject.For(hddSubjectSlot, pilotMech, squadSeats, squadComm.Name,
					scene.Targeting?.Selected, cockpitArt.Strings),

				// Rebuilt every frame, whichever of the two scanners is up. The MFD screen's own update
				// slot runs while F4 is showing (mode 3's dirty flag is the one MfdDisplay_Update never
				// clears); the floating repeater calls that same slot itself while F4 is not. Exactly one
				// of them runs per frame in the original, so the list is always a frame old at most.
				Scanner = MfdScanner.Build(pilotMech, scene.World?.Objects,
					scene.Targeting?.Selected, hudState.Scanner),

				// The NAV MAP's paint re-centres on the machine every frame it runs, and blits the same
				// raster the command display does.
				NavMap = MfdNavMap.Build(pilotMech, hddCommand?.Raster),

				// The message port has already run for this frame inside audio.Update, above.
				Message = audio.Messages.Ticker,

				// The command display, rebuilt every frame whether or not it is the page showing: its map
				// follows the machine, so the camera has to keep up even while the damage screen is up.
				Command = hddCommand is { } commandScreen
					? commandScreen.Build(pilotMech, scene.World?.Objects ?? Array.Empty<SimObject>(),
						scene.Mission.PlayerRoute, cockpitArt.Strings)
					: hudState.Command,

				// MfdFlashCommScreen_Paint (0043f7a4)'s first act is to copy the display's row onto the screen, so the page's own
				// row only survives between repaints — which is what lets an [Alt] hotkey pressed from
				// another screen transmit a row the cursor never moved to.
				FlashComm = flashComm.Snapshot(),

				// The comm channel has already run for this frame inside audio.Update, above, on the same
				// clock as the computer's port.
				Transmission = squadComm.Transmission,

				// And the line that goes with it, over the canopy. Composed here rather than in the port
				// because the name in front of it is the comm box's, not the message's — PilotMessagePort_ComposeLine (00435d0c)
				// asks the box for it through Squad_IndexOf.
				PilotMessage = ComposePilotMessage(squadComm),

				MfdPowerUpFrame = mfdPowerUpFrame,

				// A training mission's port draws its own wrapped block instead of that line.
				TrainingMessage = ComposeTrainingMessage(squadComm),

				// Each box's own picture. The MFD shows one box's, full screen; the display shows all
				// three in place, and a destroyed squadmate's sits on static there without ever having
				// had a message to open it.
				PilotVideos = pilotVideos,

				// The two waypoint indicators over the compass. The route one follows the player group's
				// cursor, which the player's own think steps on arrival; the marker one is only there
				// while the player has dropped one.
				RouteWaypoint = WaypointMark.ForRoute(pilotMech),
				NavMarker = WaypointMark.ForNavMarker(pilotMech, navMarker),
			};

			// MfdDisplay_Update's share of the missile camera, run only while the display updates at all —
			// up on the cockpit view, not panned away, lit and powered up. The switch goes first; then the
			// screen's own update, unless a transmission has the display, which it can only while the
			// switch does not hold. The first frame the screen updates after not doing so stands for the
			// display's full repaint.
			int weaponRows = cockpitArt.Gau.WeaponListTotal;
			bool mfdUpdating = !ExternalViewActive() && !cockpitPan.AtHeadsDown
				&& !hudState.Dropout.MfdHidden && hudState.MfdPowerUpFrame is null;
			if (mfdUpdating) {
				hudState = hudState with {
					Mfd = missileCamSwitch.Sync(hudState.Mfd,
						MfdMissileCamSwitch.Condition(pilotMech.Weapons, weaponRows)),
				};
			}

			bool missileCamUpdating = mfdUpdating && hudState.Mfd == MfdMode.MissileCam
				&& (missileCamSwitch.Holding || squadComm.Transmission is null);
			var missileCam = hudState.MissileCam;
			if (missileCamUpdating && scene.World is { } camWorld) {
				missileCam = missileCamScreen.Update(camWorld, pilotMech.LockAcquired,
					MfdMissileCam.LauncherRounds(pilotMech.Weapons, weaponRows), audio.CoarseTicks,
					repaint: !missileCamUpdatedLastFrame);
			}

			missileCamUpdatedLastFrame = missileCamUpdating;
			hudState = hudState with { MissileCam = missileCam, MissileCamHolding = missileCamSwitch.Holding };
		}
	};

	// The pilot and squad channel's line, composed the way PilotMessagePort_ComposeLine (00435d0c) composes it: the speaker's name,
	// ": ", then the message, capped at the composer's own strncat length. The name comes from the comm
	// box rather than from the queued record — PilotMessagePort_ComposeLine resolves the record's speaker to a slot with
	// Squad_IndexOf and asks the display for that box's name.
	//
	// Nothing is drawn while the channel is on VoiceOnly: the paint's first test is PilotMessageMode != 1.
	PilotMessageLine? ComposePilotMessage(SquadCommChannel channel) {
		if (channel.Port.Training
			|| channel.Port.Mode == MessageChannelMode.VoiceOnly
			|| channel.Port.Current is not { Text.Length: > 0 } current) {
			return null;
		}

		string text = current.Text.Length > PilotMessageBoxLayout.MaxTextLength
			? current.Text[..PilotMessageBoxLayout.MaxTextLength]
			: current.Text;
		string name = channel.Name(current.Slot);
		return new PilotMessageLine(
			name.Length > 0 ? name + PilotMessageBoxLayout.NameSeparator + text : text,
			current.Slot);
	}

	// The training port's block: the instruction's sentences through PilotMessagePort_WrapText's own
	// wrap. The same VoiceOnly test as the ordinary port's paint opens with.
	TrainingMessageBox? ComposeTrainingMessage(SquadCommChannel channel) {
		if (!channel.Port.Training
			|| channel.Port.Mode == MessageChannelMode.VoiceOnly
			|| channel.Port.Current is not { Sentences.Count: > 0 } current) {
			return null;
		}

		var (lines, widest) = TrainingMessageLayout.Wrap(current.Sentences);
		return new TrainingMessageBox(lines, widest);
	}

	// Where the selection lands on the canopy, for the front-window target box and arrow — the original's
	// own projection rather than the GL one: view-space offsets scaled by the focal length about the
	// herc's .VUE projection centre, which is what Gunsight_TargetIndicatorPaint (0043b950) does with Raster_ProjectToScreen. It
	// agrees with the GL projection because the camera's field of view is derived from the same focal
	// length and CockpitPrincipalPoint installs the same centre, including the step kick.
	TargetIndicator? ResolveTargetIndicator(MechObject pilot,
			(Vec3i Point, bool ComponentTargeted, short Component) aimPoint) {
		if (scene.Targeting is not { IndicatorArmed: true, Selected: { } target }) {
			return null;
		}

		var (centerX, centerY) = viewGeometry?.ProjectionCenter(CockpitViewGeometry.ForwardViewIndex)
			?? (CockpitViewGeometry.DefaultProjectionCenterX, CockpitViewGeometry.DefaultProjectionCenterY);

		// With a Targeting Pod fitted and the target close enough, the aim point is a component of it
		// rather than its aim node, and the box reduces to its pip.
		var aim = aimPoint.Point;
		var offset = WorldScale.ToRender(aim) - WorldScale.ToRender(camera.Position);
		var forward = camera.Forward;
		var up = camera.Up;
		var right = Vector3.Cross(forward, up);

		float depth = Vector3.Dot(offset, forward);
		float across = Vector3.Dot(offset, right);
		bool inFront = depth >= camera.NearPlane;

		return new TargetIndicator(
			ScreenX: centerX + across * Camera.FocalLengthPixels / MathF.Max(depth, camera.NearPlane),
			ScreenY: centerY - cockpitViewKick.OffsetPixels + cockpitHitShake.OffsetPixels
				- Vector3.Dot(offset, up) * Camera.FocalLengthPixels / MathF.Max(depth, camera.NearPlane),
			InFront: inFront,
			BehindToLeft: across < 0f,
			ShapeRadius: target.ShapeRadius,
			Distance: pilot.Position.ApproxDistanceTo(aim),
			Locked: pilot.LockAcquired,
			ComponentTargeted: aimPoint.ComponentTargeted);
	}

	window.Render += (_, gl) => {
		if (renderer == null || overlay == null || items == null) {
			return;
		}

		var size = window.FramebufferSize;
		renderer.Clear();

		// The shield meter's rings are canopy pixels, not HUD geometry, and the original relights them by
		// rewriting six palette slots every frame. Decoding the art at load baked that palette in, so the
		// live version repaints those pixels and re-uploads — which UpdateShieldRings only asks for on the
		// frames where a ring's colour actually changed, so a settled array costs one comparison.
		if (cockpitArt != null && pilotMech != null && cockpitFrontTexture != null) {
			// While the cockpit is powering up the rings fill from dark instead — see CockpitPowerUp.
			var shieldRings = pilotMech.Shields;
			var (frontRings, rearRings) = cockpitPowerUp.ShieldCharges(
				CockpitPalette.ShieldFacingCharge(shieldRings.Front, shieldRings.BaseMax),
				CockpitPalette.ShieldFacingCharge(shieldRings.Rear, shieldRings.BaseMax),
				audio.CoarseTicks);
			bool repainted = cockpitArt.UpdateShieldRings(frontRings, rearRings);

			if (repainted) {
				// Through whichever buffer the damage flash is currently showing — the repaint writes the
				// rings into both, and re-uploading the other one here would cancel a flash mid-shake.
				var frontFrame = cockpitArt.Front;
				cockpitFrontTexture.Update(frontFrame.PixelsFor(damageFlashShown),
					frontFrame.Width, frontFrame.Height);
				cockpitSideTexture?.Update(cockpitArt.Side.PixelsFor(damageFlashShown),
					cockpitArt.Side.Width, cockpitArt.Side.Height);
				if (cockpitArt.HeadsDown is { } headsDownFrame && cockpitHeadsDownTexture != null) {
					cockpitHeadsDownTexture.Update(headsDownFrame.PixelsFor(damageFlashShown),
						headsDownFrame.Width, headsDownFrame.Height);
				}
			}
		}

		// The external view has no canopy over it — there is no cockpit to see from outside the machine.
		// Retail's is a band short of the screen, with its caption underneath; the tweak's mouse view uses
		// the whole window, as does the observer camera.
		if (cockpitArt != null && cockpitFrontTexture != null && cockpitSideTexture != null
				&& !ExternalViewActive()) {
			DrawThreePanelCockpitView(gl, size.X, size.Y);
		} else if (ExternalViewActive() && !MouseOutsideView()) {
			DrawExternalView(gl, size.X, size.Y);
		} else {
			renderer.Render(camera, VisibleItems(), groundLayer, 0, 0, size.X, size.Y);
			DrawBeams(camera, size.X, size.Y);
			DrawSprites(camera, size.X, size.Y);
			DrawSkeleton(camera, size.X, size.Y);
		}

		// The panel goes over whatever view is up. The cockpit path above draws through three sub-window
		// viewports and leaves the last one set, so the full-window viewport is restored first —
		// otherwise the panel is squeezed into the right-hand cockpit panel's rectangle and mostly
		// scissored away, which is why it only ever appeared in the external view.
		gl.Viewport(0, 0, (uint)Math.Max(size.X, 1), (uint)Math.Max(size.Y, 1));

		// Both panels are modal, so they go over everything the cockpit drew — and over the external view
		// too, where one stays up if the player switched views with it open. Only one can be up at a time.
		if (cockpitArt?.Sprites is { } panelSprites && hudSpriteTexture != null) {
			if (statusAlertPanel is { IsOpen: true } openAlert) {
				overlay.DrawStatusAlertPanel(size.X, size.Y, hudSpriteTexture, panelSprites, openAlert);
			} else if (objectivesPanel is { IsOpen: true } openObjectives) {
				overlay.DrawObjectivesPanel(size.X, size.Y, hudSpriteTexture, panelSprites, openObjectives);
			} else if (preferencesPanel is { IsOpen: true } openPreferences) {
				overlay.DrawPreferencesPanel(size.X, size.Y, hudSpriteTexture, panelSprites, openPreferences);

				// And the controls panel over it, in that order: the original's AlertPanel_Enter saves the
				// screen under the panel it raises, so the strip it was opened from is still there behind it.
				if (controlsPanel is { IsOpen: true } openControls) {
					overlay.DrawControlsPanel(size.X, size.Y, hudSpriteTexture, panelSprites, openControls);
				}
			}
		}

		// The menu bar itself: hidden until [Esc] raises it (see ReadMenuBarEscapeKey) and hidden outright
		// during --screenshot capture so it never lands in a reference image. A bare item per panel, no
		// checkmark, since each panel closes itself; mirrors Herculan.Engine.Host.Editor's BuildMenuBar.
		if (screenshotPath == null && menuBarVisible && ImGui.BeginMainMenuBar()) {
			if (ImGui.MenuItem("Debug")) {
				debugPanel.IsOpen = true;
			}

			if (ImGui.MenuItem("Tweaks")) {
				tweaksMenu.IsOpen = true;
			}

			// A click outside the bar hides it, but only while neither panel is up — with one open, that
			// same click either lands on it (nothing to do here) or is DebugPanel's own outside-click
			// close, or TweaksMenu's Save/Cancel is the only way out.
			if (!debugPanel.IsOpen && !tweaksMenu.IsOpen && !ImGui.IsWindowHovered()
					&& ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
				menuBarVisible = false;
			}

			ImGui.EndMainMenuBar();
		}

		debugPanel.Draw(
			new DebugPanelContext(piloting, ExternalViewActive(), pilotMech, scene.Targeting, scene.World,
				scene.PlayerObject?.Model?.Segments.Length ?? 0, terrain),
			size.Y);

		tweaksMenu.Draw();

		imgui?.Render();

		framesRendered++;
		// A tracer is on screen for one tick out of every refire period and a travelling shot for as long
		// as its flight lasts, so either one counts as "the trigger produced something visible".
		bool shotWanted = heldFire && (beams != null || scene.World.Bullets != null);

		// --target waits the same way --fire does: the sensor model has to run before anything is
		// targetable, so the capture holds until a selection exists rather than photographing a blank HUD.
		// --impact waits for a slot of the effect light field to be lit, which is the one moment the
		// dynamic lights are on screen at all. See EffectLightSelection.
		bool lightWanted = waitForEffectLight
			&& !scene.World.EffectLights.Slots.Any(slot => slot.IsLive);

		bool transmissionWanted = waitForTransmission && squadComm.Transmission is not { ShowName: true };

		// --hit-shake waits for the palette half to be up. It alternates on its own 0-9 tick timer, so
		// without this the capture would land on whichever side of the flash the frame count happened to
		// fall on.
		bool flashWanted = stageHitShake && !cockpitHitShake.FlashActive;

		if (screenshotPath != null && !screenshotTaken && framesRendered >= ScreenshotWarmupFrames
				&& !flashWanted && !acquireTarget
				&& !lightWanted && !transmissionWanted
				&& (!shotWanted || scene.World.Tracers.Count > 0 || scene.World.Projectiles.Count > 0
					|| scene.World.RocketsInFlight.Count > 0)) {
			screenshotTaken = true;
			reportSquadOrders?.Invoke($"after {framesRendered} frames,");
			Screenshot.Capture(gl, size.X, size.Y, screenshotPath);
			window.Close();
		}
	};

	window.Closing += () => {
		if (tapeRecorder != null) {
			tapeRecorder.Dispose();
			Console.WriteLine($"Recorded {tapeRecorder.FrameCount} frames to {tapeRecorder.Path}.");
		}

		audio.Dispose();
		imgui?.Dispose();
		renderer?.Dispose();
		overlay?.Dispose();
		wireframe?.Dispose();
		beams?.Dispose();
		sprites?.Dispose();
		terrainMesh?.Dispose();
		terrainTexture?.Dispose();
		cockpitFrontTexture?.Dispose();
		cockpitSideTexture?.Dispose();
		cockpitHeadsDownTexture?.Dispose();
		hudSpriteTexture?.Dispose();
		hddMapTexture?.Dispose();
		foreach (var disposable in disposables) {
			disposable.Dispose();
		}
	};

	window.Run();

	// Sim_Shutdown (00461eec) runs however the mission ends: Mission_WriteResults (0042412c) writes results.dat and
	// the counters back over mission.var beside the mission, and the exit code says where ES.EXE goes next. The
	// original writes into the install's data\; a mission run straight from the install's own files writes
	// nothing there, and a --screenshot run ends the host, both this engine's choice.
	//
	// A window closed by the player is the original's WM_CLOSE, which dispatches [Ctrl+Q] (0x410) outside a
	// demo and raises DemoAbort in one. This window cannot refuse the close, so the EXIT EARTHSIEGE? panel is
	// not asked and its QUIT is taken as answered; a demo simply ends.
	if (!missionOver && !demoTape && screenshotPath == null) {
		quitGame = true;
	}

	if ((shellLaunch != null || tapePlayer != null) && scene.World.PlayerMech is { } endPlayer) {
		var (results, counters) = MissionResults.Write(scene.World, endPlayer);
		string resultsFolder = Path.GetDirectoryName(scriptPath) ?? ".";
		File.WriteAllBytes(Path.Combine(resultsFolder, MissionResults.FileName), results);
		File.WriteAllBytes(Path.Combine(resultsFolder, MissionLoader.CountersFileName), counters);
		Console.WriteLine($"Wrote {MissionResults.FileName} ({results.Length} bytes, {scene.World.Salvage.Count} salvaged "
			+ $"weapon(s)) and {MissionLoader.CountersFileName} to {resultsFolder}.");
	}

	return screenshotPath != null ? 0 : MissionResults.ExitCode(demoTape, quitGame);

	// The three keys that raise a panel of the status-alert family, and the two that answer one.
	//
	// [Q] asks how the mission stands, [Ctrl+Q] asks to leave the game, and [P] pauses — the manual's
	// own "Quit Mission", "Quit EarthSiege 2" and "Pause Mission", and Sim_DispatchCommand's commands
	// 0x10, 0x410 and 0x19. [Return] and [Esc] both answer with button 0, which is CONTINUE on every
	// one of them. While a panel is up nothing else may act: AlertPanel_HandleEvent answers those two
	// keys and the panel's own loop owns the rest.
	//
	// Returns whether the panel claimed the keystroke.
	bool ReadStatusAlertKeys() {
		if (statusAlertPanel == null || keyboard == null
			|| KeyboardCapturedByImGui()) {
			statusAlertKeysDown = 0;
			return false;
		}

		bool ctrl = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
		bool q = Edge(Key.Q, 0);
		// `|`, not `||`: both edges must be read every frame or the one that is skipped never updates
		// its held state, and the next press of it is swallowed.
		bool enter = Edge(Key.Enter, 1) | Edge(Key.KeypadEnter, 2);
		bool escape = Edge(Key.Escape, 3);
		bool pause = Edge(Key.P, 4);

		if (statusAlertPanel.IsOpen) {
			return statusAlertPanel.HandleKey(enter, escape) || q || pause;
		}

		// Not while another panel is up, which is already holding the input. The external view keeps both:
		// they are the dispatcher's own cases, not the widgets'.
		if (missionOver || cockpitArt == null
			|| objectivesPanel is { IsOpen: true } || preferencesPanel is { IsOpen: true }) {
			return false;
		}

		// [Ctrl+Q] is command 0x410 — the Ctrl bit over Q's own scancode — and asks to leave the game
		// rather than the mission. [Q] alone asks how the mission stands, and [P] pauses.
		if (q) {
			return ctrl
				? OpenStatusAlert((MissionStatus)StatusAlertPanel.ExitGameStatus, scene.World.Objectives)
				: RaiseStatusAlertForQuit();
		}

		// [Ctrl+P] is 0x419, a developer key, and never reaches the pause.
		if (pause && !ctrl) {
			return OpenStatusAlert((MissionStatus)StatusAlertPanel.PauseStatus, scene.World.Objectives);
		}

		return false;

		bool Edge(Key key, int bit) {
			bool down = keyboard.IsKeyPressed(key);
			bool edge = down && (statusAlertKeysDown & (1 << bit)) == 0;
			statusAlertKeysDown = down
				? statusAlertKeysDown | (1 << bit)
				: statusAlertKeysDown & ~(1 << bit);
			return edge;
		}
	}

	// [/] is Sim_DispatchCommand's 0x35, the on-line manual; [?] is the same key, since SimCommandMask
	// strips the Shift bit (docs/formats/cockpit-input.md#keyboard-commands-are-scancodes). The dispatcher
	// never sees a key while a modal panel's own loop holds the input, so neither does this. Retail drops
	// the display out of full screen first; this window has no full screen to leave.
	void ReadManualKey() {
		if (keyboard == null || KeyboardCapturedByImGui() || FlashCommHasKeyboard()) {
			manualKeyDown = false;
			return;
		}

		bool down = keyboard.IsKeyPressed(Key.Slash);
		bool edge = down && !manualKeyDown;
		manualKeyDown = down;
		if (edge && statusAlertPanel is not { IsOpen: true } && objectivesPanel is not { IsOpen: true }
			&& preferencesPanel is not { IsOpen: true } && controlsPanel is not { IsOpen: true }) {
			OnlineManual.Open(manualRoot);
		}
	}

	// [F11] puts the objectives panel up, and [Return] or [Esc] takes it down — the three keys the
	// original answers, scancode 0x57 through CockpitWidgets_HandleCommand (00432bc8) on the way in and
	// 0x1c/0x01 through the panel's own handler (AlertPanel_HandleEvent, 00454e10) on the way out. Nothing in that handler
	// answers 0x57, so [F11] does not close the panel it opened; that is retail behaviour, not an
	// oversight here.
	//
	// Returns whether the panel claimed the keystroke, so [Esc] does not also reach the debug panel.
	bool ReadObjectivesKeys() {
		if (objectivesPanel == null || keyboard == null
			|| KeyboardCapturedByImGui()) {
			objectivesKeysDown = 0;
			return false;
		}

		bool open = Edge(Key.F11, 0);
		// `|`, not `||`: both edges must be read every frame or the one that is skipped never updates
		// its held state, and the next press of it is swallowed.
		bool enter = Edge(Key.Enter, 1) | Edge(Key.KeypadEnter, 2);
		bool escape = Edge(Key.Escape, 3);

		if (objectivesPanel.IsOpen) {
			return objectivesPanel.HandleKey(enter, escape) || open;
		}

		// Only from inside the machine, and not while another panel is up: the command reaches the
		// panel through the cockpit's own widget tree, which is not on screen in the external view, and
		// one modal is already holding the input.
		if (open && cockpitArt != null && !ExternalViewActive()
			&& statusAlertPanel is not { IsOpen: true } && preferencesPanel is not { IsOpen: true }) {
			objectivesPanel.Open();
			return true;
		}

		return false;

		bool Edge(Key key, int bit) {
			bool down = keyboard.IsKeyPressed(key);
			bool edge = down && (objectivesKeysDown & (1 << bit)) == 0;
			objectivesKeysDown = down
				? objectivesKeysDown | (1 << bit)
				: objectivesKeysDown & ~(1 << bit);
			return edge;
		}
	}

	// [F12] puts the preferences panel up, and [Return] or [Esc] takes it down — scancode 0x58 through
	// CockpitWidgets_HandleCommand on the way in (PreferencesPanel_Raise, 0045cfd4), and the panel's own
	// handler on the way out, where [Esc] presses the cancel widget the constructor set to DONE. As with
	// [F11], nothing in the panel's loop answers 0x58, so a second press does not close it.
	//
	// The original also reaches this panel on [Alt+P], command 0x219. Not bound here: [P] alone is the
	// pause panel, and this host has no Alt-modified command bank yet.
	//
	// Returns whether the panel claimed the keystroke, so [Esc] does not also reach the debug panel.
	bool ReadPreferencesKeys() {
		if (preferencesPanel == null || keyboard == null
			|| KeyboardCapturedByImGui()) {
			preferencesKeysDown = 0;
			return false;
		}

		bool open = Edge(Key.F12, 0);
		// `|`, not `||`: both edges must be read every frame or the one that is skipped never updates
		// its held state, and the next press of it is swallowed.
		bool enter = Edge(Key.Enter, 1) | Edge(Key.KeypadEnter, 2);
		bool escape = Edge(Key.Escape, 3);

		// The controls panel is modal over this one: while it is up it answers [Return] and [Esc], and
		// this panel answers nothing.
		if (controlsPanel is { IsOpen: true } liveControls) {
			return liveControls.HandleKey(enter, escape) || open;
		}

		if (preferencesPanel.IsOpen) {
			return preferencesPanel.HandleKey(enter, escape) || open;
		}

		// Not while another modal is up. Unlike the objectives panel it opens from the external view as
		// well: [F12] is the dispatcher's own case (0x58), not the widgets'.
		if (open && cockpitArt != null
			&& statusAlertPanel is not { IsOpen: true } && objectivesPanel is not { IsOpen: true }) {
			preferencesPanel.Open();
			return true;
		}

		return false;

		bool Edge(Key key, int bit) {
			bool down = keyboard.IsKeyPressed(key);
			bool edge = down && (preferencesKeysDown & (1 << bit)) == 0;
			preferencesKeysDown = down
				? preferencesKeysDown | (1 << bit)
				: preferencesKeysDown & ~(1 << bit);
			return edge;
		}
	}

	// [Esc] backs out one layer at a time: closes whichever of debugPanel/tweaksMenu is open, else
	// hides an empty menu bar, else returns to the cockpit from the external view, a side window or the
	// Heads-Down Display, else raises the menu bar. The menu bar is the only way to reach either panel,
	// since every key from F1 to F12 is already taken by the game.
	void ReadMenuBarEscapeKey(bool consumedByOtherPanel) {
		// During a replay the two halves of this key come apart: the tape's [Esc] is the game's and only
		// ever backs out of a view, and the live one keeps the menu bar, which is this engine's.
		bool tapePlaying = TapePlaying();
		if (tapePlaying && keyboard != null) {
			bool tapeDown = keyboard.IsKeyPressed(Key.Escape);
			if (tapeDown && !tapeEscapeDown && !consumedByOtherPanel && ExternalViewActive()) {
				viewChain?.Escape();
			} else if (tapeDown && !tapeEscapeDown && !consumedByOtherPanel && cockpitArt != null
					&& !ExternalViewActive() && (!cockpitGlance.AtForward || cockpitPan.HeadsDownRequested)) {
				cockpitGlance.Return();
				RequestHeadsDown(headsDown: false);
			}

			tapeEscapeDown = tapeDown;
			consumedByOtherPanel = false;
		}

		if (liveKeys == null) {
			return;
		}

		// Tracked every frame independent of consumedByOtherPanel, exactly like the three callers above
		// track their own Escape edge regardless of who else claims it — otherwise a press that is still
		// held on the frame a retail panel above lets go of Escape reads as a second, fresh press here.
		bool down = liveKeys.IsKeyPressed(Key.Escape);
		bool pressed = down && !menuBarEscapeDown;
		menuBarEscapeDown = down;

		if (pressed && !consumedByOtherPanel) {
			if (debugPanel.IsOpen || tweaksMenu.IsOpen) {
				// TweaksMenu goes through Cancel, not a bare close, so an edit made but not yet saved is
				// discarded rather than left applied-but-unpersisted; DebugPanel has no such state to lose.
				debugPanel.IsOpen = false;
				if (tweaksMenu.IsOpen) {
					tweaksMenu.Cancel();
				}
			} else if (menuBarVisible) {
				menuBarVisible = false;
			} else if (!tapePlaying && ExternalViewActive()) {
				// With the cockpit's widgets off, scancode 1 falls through them to the dispatcher's own
				// case, which is the way back from the external view — see ExternalViewChain.Escape.
				viewChain?.Escape();
			} else if (!tapePlaying && cockpitArt != null && !ExternalViewActive()
					&& (!cockpitGlance.AtForward || cockpitPan.HeadsDownRequested)) {
				// The manual's [Esc] is "the way back" from the side windows and the Heads-Down Display
				// alike — view command 6 from a glance, 1 from heads-down. Only once that is done does
				// [Esc] fall through to raising the menu bar.
				cockpitGlance.Return();
				RequestHeadsDown(headsDown: false);
			} else {
				menuBarVisible = true;
			}
		}
	}

	// A modal panel's buttons, pressed and released. Read straight off the device rather than through
	// CockpitInput: that queue is the cockpit's, and while a modal is up the cockpit is not taking
	// clicks at all. Press and release must both land on the same button for it to fire, which is
	// Widget_OnMouseUp's own re-hit-test.
	void ReadPanelPointer(AlertPanelLayout.Placement place, Action<float, float> onDown,
			Action<float, float, bool> onUp) {
		var (pointerX, pointerY, buttons) = PanelPointer();
		var (panelX, panelY) = place.ToPanel(pointerX, pointerY);

		// Both buttons press a widget; which one was released is what the click carries, since
		// CockpitMouse_ProcessQueue ORs the button bit into the click value on the release edge and the
		// panel reads bit 1 off it. Two of these panels step a setting backwards on the right button.
		bool right = buttons.HasFlag(CockpitMouseButtons.Right);
		bool down = buttons.HasFlag(CockpitMouseButtons.Left) || right;
		if (down && !panelMouseDown) {
			panelRightButtonDown = right;
			onDown(panelX, panelY);
		} else if (!down && panelMouseDown) {
			onUp(panelX, panelY, panelRightButtonDown);
			panelRightButtonDown = false;
		}

		panelMouseDown = down;
	}

	// Where the pointer is and what it holds, in framebuffer pixels: the live mouse, or during a replay
	// wherever the tape's own mouse events have left it.
	(float X, float Y, CockpitMouseButtons Buttons) PanelPointer() {
		if (TapePlaying()) {
			return tapePointer;
		}

		if (mouse == null) {
			return (float.NaN, float.NaN, CockpitMouseButtons.None);
		}

		var client = window.ClientSize;
		var framebuffer = window.FramebufferSize;
		return (mouse.Position.X * framebuffer.X / Math.Max(client.X, 1),
			mouse.Position.Y * framebuffer.Y / Math.Max(client.Y, 1),
			ButtonsHeld(mouse));
	}

	// [Q] asks the mission how it stands and offers a way out of it -- Sim_DispatchCommand's scancode
	// 0x10, which calls Mission_Status with its "just answer the question" flag set, raises the status
	// alert for the answer, and takes the button pressed as the decision. The same call records the
	// answer as the status already raised, so the poll will not raise that same status again as a
	// change, and it disarms the poll's pending alert delay.
	//
	// Returns whether the panel was raised, so [Q] does not also reach anything below it.
	bool RaiseStatusAlertForQuit() {
		if (statusAlertPanel == null || scene.World is not { } quitWorld
			|| quitWorld.PlayerMech is not { } quitPlayer || quitWorld.Objectives is not { } quitObjectives) {
			return false;
		}

		var status = quitObjectives.QueryForPlayer(quitWorld, quitPlayer);
		return OpenStatusAlert(status, quitObjectives);
	}

	// The panel for one status, with the outstanding objective's own failure text alongside it -- the
	// substitution the constructor makes for status 5 and no other.
	bool OpenStatusAlert(MissionStatus status, MissionObjectives objectives) {
		if (statusAlertPanel == null || !StatusAlertPanel.CanShow((int)status)) {
			return false;
		}

		var failure = objectives.Outstanding is { } outstanding
			? mission.DescriptionOf(outstanding.Record)
			: null;

		if (!statusAlertPanel.Open((int)status, failure)) {
			return false;
		}

		panelMouseDown = PanelPointer().Buttons.HasFlag(CockpitMouseButtons.Left);
		return true;
	}

	// What the player answered. Only the button the status's own table names ends the mission; every
	// other answer just puts the panel away and carries on.
	void ApplyStatusAlertAnswer() {
		if (statusAlertPanel == null || !statusAlertPanel.TryTakeAnswer(out int button, out bool ends)) {
			return;
		}

		if (!ends) {
			return;
		}

		// Both endings leave the simulator. EXIT EARTHSIEGE? is not a mission outcome at all -- its QUIT
		// sets DAT_004d2582, the global quit flag that AlertPanel_Present also watches to tear down any
		// panel still up -- while a mission-ending answer goes up through Sim_PollPlayerInput and
		// Sim_MainTick as the tick's own return, which ends the main loop. Either way Sim_Shutdown then
		// writes the results and picks the exit code, after the window has gone (RunMission's end). The
		// latch stops the last few frames before the window actually goes from ticking or raising another
		// panel.
		missionOver = true;
		quitGame = statusAlertPanel.Status == StatusAlertPanel.ExitGameStatus;
		Console.WriteLine(quitGame
			? "Quitting EarthSiege 2."
			: $"Mission over — status {statusAlertPanel.Status} "
				+ $"({(MissionStatus)statusAlertPanel.Status}), answered '{statusAlertPanel.Buttons[button]}'.");
		window.Close();
	}

	// Draws the front/left/right panels side by side, each sized by its own cockpit-art image's native
	// aspect ratio fit to the full window height — not an equal three-way split of the window — so the
	// quads butt together edge-to-edge with no seam or overlap regardless of how the front and side art's
	// proportions differ from each other. The resulting three-panel composite is anchored to the window's
	// horizontal center as one unit: a narrower window crops its outer edges symmetrically, a wider one
	// leaves equal empty margins, and no panel is ever stretched.
	//
	// The 3D scene behind the three panels is one camera over one viewport, CockpitScreenLayout.World:
	// retail's glances are the forward view's image plane continued sideways, not turned cameras, so the
	// world runs straight across the panel seams at any pitch. Each panel's pass shares that camera and
	// viewport and differs only in the scissor it is drawn under.
	//
	// The whole composite also slides vertically with the Heads-Down Display pan. The original's cockpit
	// is a canvas twice the screen's height with the forward view's art at canvas row 0 and the HDD's at
	// row 474, both blitted during mission bring-up, and switching between them is a scroll of the
	// display window over that canvas — never a redraw (see CockpitViewGeometry, and CockpitPan for the
	// transition's own machinery). This reproduces the same geometry with two quads at fixed canvas
	// offsets and a moving window: the three cockpit panels are offset up by the pan distance, the HDD
	// panel sits one travel-distance below them, and the 3D viewports ride along with their panels so
	// the world scrolls out of frame exactly as the art does. The two views' art overlaps by six rows on
	// the canvas — HB1 starts at row 474 and HB0 runs to 479 — and the original resolves that by blitting
	// view 1 before view 0, so the draw order below does the same.
	// The cockpit view manager's view 4: the world in the rows ExternalViewLayout gives it, across the
	// window's width, and below it the band View_FillOutside3dRect (0042da08) floods black and the caption on it. The focal
	// length stays the cockpit's; the view is only shorter, so its field of view is the angle that length
	// subtends over its own rows.
	void DrawExternalView(GL gl, int width, int height) {
		int viewHeight = Math.Max(1, (int)MathF.Round(
			ExternalViewLayout.ViewRows * height / (float)ExternalViewLayout.ScreenRows));
		int viewY = height - viewHeight;

		var externalCamera = CloneCockpitCamera(camera);
		externalCamera.FieldOfView = 2f * MathF.Atan(ExternalViewLayout.ViewRows / 2f / Camera.FocalLengthPixels);
		externalCamera.PrincipalPoint = new Vector2(0.5f, ExternalViewLayout.CentreRow / (float)ExternalViewLayout.ViewRows);

		gl.Enable(EnableCap.ScissorTest);
		gl.Scissor(0, viewY, (uint)Math.Max(width, 1), (uint)viewHeight);
		renderer!.Render(externalCamera, VisibleItems(), groundLayer, 0, viewY, width, viewHeight);
		DrawBeams(externalCamera, width, viewHeight);
		DrawSprites(externalCamera, width, viewHeight);
		DrawSkeleton(externalCamera, width, viewHeight);

		if (viewY > 0) {
			gl.Scissor(0, 0, (uint)Math.Max(width, 1), (uint)viewY);
			gl.ClearColor(0f, 0f, 0f, 1f);
			gl.Clear(ClearBufferMask.ColorBufferBit);
		}

		gl.Disable(EnableCap.ScissorTest);

		if (viewChain?.Caption is { } caption && cockpitArt?.Sprites is { } captionSprites
				&& hudSpriteTexture != null && cockpitArt.Strings is { } strings) {
			// VIEW: names the player YOU, a squadmate by the pilot in its comm box, and anything else not at
			// all — Squad_IndexOf's -1 takes an empty name.
			string viewed = caption.Viewed == pilotMech
				? strings.Text(ExternalViewLayout.PlayerNameGroup, 0) ?? string.Empty
				: Array.IndexOf(squadSeats, caption.Viewed) is var slot and >= 0 ? squadComm.Name(slot) : string.Empty;
			overlay!.DrawExternalViewCaption(width, height, hudSpriteTexture, captionSprites,
				(strings.Text(ExternalViewLayout.CaptionGroup, 0) ?? string.Empty) + viewed,
				(strings.Text(ExternalViewLayout.CaptionGroup, 1) ?? string.Empty)
					+ (strings.Text(ExternalViewLayout.CaptionGroup, caption.CameraControl ? 2 : 3) ?? string.Empty));
		}
	}

	void DrawThreePanelCockpitView(GL gl, int totalWidth, int totalHeight) {
		// One placement for the whole frame, shared with the input path so a widget's click region cannot
		// drift from the art it was drawn over — see CockpitScreenLayout.
		var layout = CockpitScreenLayout.Create(totalWidth, totalHeight, cockpitArt!,
			cockpitPan.OffsetRows, cockpitPan.TravelRows, cockpitGlance.OffsetPanels);

		var world = layout.World;
		var cockpitCamera = CloneCockpitCamera(camera);
		cockpitCamera.PrincipalPoint = CockpitPrincipalPoint(layout.Center, world);

		// First, so the six-row overlap where the two views' art meets on the canvas resolves the way the
		// original's VRAM does. Sim_InitMissionSession (004614fc) blits view 1 and then view 0, so HB0's
		// bottom rows win over HB1's top rows and no sliver of the HDD shows under the dashboard at rest.
		if (cockpitHeadsDownTexture != null && layout.HeadsDown is { } headsDown) {
			overlay!.DrawHeadsDown(headsDown.Viewport.X, headsDown.Viewport.Y,
				headsDown.Viewport.Width, headsDown.Viewport.Height,
				cockpitHeadsDownTexture, headsDown.ArtWidth, headsDown.ArtHeight,
				cockpitArt, hudSpriteTexture, hudState, hddMapTexture);
		}

		// GL's viewport origin is bottom-left, so a positive y offset moves a panel up the screen — which
		// is the direction the cockpit travels as the view pans down the canvas.
		// Each panel is one of DBSIM's views and carries that view's own .VUE 3D rect. The two glances
		// share a canopy bitmap but not a rect — view 3's runs the full width of the view where view 2's
		// stops short of it — so the mirrored panel takes view 3 rather than a mirrored copy of view 2.
		DrawPanel(layout.Left, cockpitSideTexture!, mirrorHorizontally: true, hud: null,
			CockpitViewGeometry.MirroredGlanceViewIndex);
		DrawPanel(layout.Center, cockpitFrontTexture!, mirrorHorizontally: false, hud: cockpitArt,
			CockpitViewGeometry.ForwardViewIndex);
		DrawPanel(layout.Right, cockpitSideTexture!, mirrorHorizontally: false, hud: null,
			CockpitViewGeometry.GlanceViewIndex);

		void DrawPanel(CockpitScreenLayout.PlacedSurface surface, GpuTexture texture,
				bool mirrorHorizontally, CockpitArt? hud, int viewIndex) {
			// The view's .VUE 3D rect, as a scissor around the whole 3D pass — the outer bound DBSIM's
			// rasterizer clips to, which the canopy's alpha cutout does not express on its own. See
			// CockpitViewGeometry.WorldViewport. The sky is inside the scissor because it is part of the
			// 3D view; the canopy below it must not be, so the test goes off again before the overlay.
			// The scissor is also what confines this pass to its own panel of the shared viewport.
			ApplyWorldViewportScissor(gl, surface, viewIndex, mirrorHorizontally);

			renderer!.Render(cockpitCamera, VisibleItems(), groundLayer, world.X, world.Y, world.Width, world.Height);

			DrawBeams(cockpitCamera, world.Width, world.Height);
			DrawSprites(cockpitCamera, world.Width, world.Height);

			// Before the canopy goes over it, so the skeleton is clipped by the viewport hole like the rest
			// of the world. Mostly of use with the machine's own model hidden, but it costs one draw call.
			DrawSkeleton(cockpitCamera, world.Width, world.Height);

			gl.Disable(EnableCap.ScissorTest);

			var viewport = surface.Viewport;
			overlay!.Draw(viewport.X, viewport.Y, viewport.Width, viewport.Height, texture,
				surface.ArtWidth, surface.ArtHeight, mirrorHorizontally, hud,
				spriteTexture: hudSpriteTexture, hudState: hudState, mapTexture: hddMapTexture,
				missileView: hud == null ? null : (x, y, width, height, view) => DrawMissileView(gl, x, y, width, height, view));
		}
	}

	// The MFD's MISSILE CAM, from inside the overlay's pass over the forward panel: the world as
	// MfdMissileViewScreen_Paint draws it, from the camera the screen placed, into the screen's own rect.
	// It is the cockpit view's draw again with three things changed, all of them the paint's own: no sky —
	// the overlay has already flooded the rect with the colour the paint floods it with — the terrain
	// untextured whatever TERRAIN TEXTURE says, and every object submitted, the machine being flown among
	// them, since the view object is the camera and not the cockpit.
	void DrawMissileView(GL gl, int x, int y, int width, int height, Camera view) {
		view.FarPlane = camera.FarPlane;

		gl.Enable(EnableCap.ScissorTest);
		gl.Scissor(x, y, (uint)width, (uint)height);
		gl.Clear(ClearBufferMask.DepthBufferBit);
		gl.Enable(EnableCap.DepthTest);
		gl.Disable(EnableCap.Blend);

		var sky = renderer!.Sky;
		renderer.Sky = null;
		uint? terrainTexture = terrainItem?.TextureHandle;
		if (terrainItem is not null) {
			terrainItem.TextureHandle = null;
		}

		renderer.Render(view, (items ?? Array.Empty<SceneItem>())
				.Concat(projectileItems)
				.Concat(weaponItems)
				.Concat(debrisItems)
				.Concat(dropPodItems),
			groundLayer, x, y, width, height);
		DrawBeams(view, width, height);
		DrawSprites(view, width, height);

		if (terrainItem is not null) {
			terrainItem.TextureHandle = terrainTexture;
		}

		renderer.Sky = sky;
		gl.Disable(EnableCap.ScissorTest);
	}

	// Confines the 3D pass for one panel to that view's .VUE viewport rect, or to the panel itself when
	// there is no rect to use. Either way the scissor is left on, and the caller turns it off once it has
	// finished drawing the world: every panel's pass renders the same shared viewport, so an unscissored
	// one would paint over its neighbours.
	//
	// The panel fallback covers a herc that ships no .VUE, and a view that declares a zero-size rect. The
	// zero case is not a degenerate rect to clamp away -- it is how every herc but RAZOR says its
	// heads-down view shows no world at all -- but no panel this draws is that view, so it cannot arise
	// here and "the whole panel" is the safe reading for a hand-edited file.
	void ApplyWorldViewportScissor(GL gl, CockpitScreenLayout.PlacedSurface surface, int viewIndex,
			bool mirrorHorizontally) {
		gl.Enable(EnableCap.ScissorTest);

		if (viewGeometry?.WorldViewport(viewIndex) is not { } rect) {
			var viewport = surface.Viewport;
			int panelX = Math.Max(viewport.X, 0);
			gl.Scissor(panelX, viewport.Y,
				(uint)Math.Max(viewport.X + viewport.Width - panelX, 0), (uint)viewport.Height);
			return;
		}

		// Art pixels to window pixels, through the same fit the canopy quad is drawn with. The rect is
		// stated in the view's own screen coordinates, and a mirrored panel's screen is the art
		// reflected about its width — so the rect is reflected the same way, which swaps its edges.
		float left = mirrorHorizontally ? surface.ArtWidth - rect.X1 : rect.X0;
		float right = mirrorHorizontally ? surface.ArtWidth - rect.X0 : rect.X1;
		var (windowX0, windowY0) = surface.ArtToWindow(left, rect.Y0);
		var (windowX1, windowY1) = surface.ArtToWindow(right, rect.Y1);

		// GL's scissor box is bottom-left origin in framebuffer pixels, where the window coordinates
		// above are top-left origin -- the same flip PlacedSurface.ViewportTopInWindow undoes. The
		// left edge is clamped to the window because a side panel's art overhangs it on a narrow one.
		int x = Math.Max((int)MathF.Floor(windowX0), 0);
		int y = (int)MathF.Floor(surface.WindowHeight - windowY1);
		int width = Math.Max((int)MathF.Ceiling(windowX1) - x, 0);
		int height = Math.Max((int)MathF.Ceiling(surface.WindowHeight - windowY0) - y, 0);

		gl.Scissor(x, y, (uint)width, (uint)height);
	}

	// Where the view axis lands in the cockpit's shared world viewport, as a fraction of it — the herc's
	// own .VUE projection centre on the forward panel, carried through the same art-to-window transform
	// the canopy is drawn with so it stays on the reticle through the heads-down pan.
	//
	// Only the forward view's centre is used, and that is retail's arithmetic rather than a
	// simplification: a glance's centre is its own .VUE pair offset by its canvas origin, which puts it
	// at the forward view's reticle, off the glance's inner edge — see docs/formats/cockpit-views.md, "The
	// side glances are one image plane". Without a .VUE the fallback is APOCA's, which is a guess — but a
	// far better one than the middle of the window, which is wrong for every herc in the game.
	Vector2 CockpitPrincipalPoint(CockpitScreenLayout.PlacedSurface surface, CockpitScreenLayout.Viewport world) {
		var (centerX, centerY) = viewGeometry?.ProjectionCenter(CockpitViewGeometry.ForwardViewIndex)
			?? (CockpitViewGeometry.DefaultProjectionCenterX, CockpitViewGeometry.DefaultProjectionCenterY);

		// The step kick and the damage shake both move the centre itself, in the art's own pixels, so
		// they go through the same art-to-window transform as everything else on the panel — which is how
		// the original applies them: straight onto the projection centre, before the view is installed.
		// Art y runs downward, and the two carry the original's opposite sign conventions — see
		// CockpitHitShake.OffsetPixels.
		var (windowX, windowY) = surface.ArtToWindow(centerX,
			centerY - cockpitViewKick.OffsetPixels + cockpitHitShake.OffsetPixels);

		// The world viewport shares the panels' top edge and height, so only x changes frame.
		return new Vector2(
			(windowX - world.X) / Math.Max(world.Width, 1),
			(windowY - surface.ViewportTopInWindow) / Math.Max(world.Height, 1));
	}

	// Every beam fired on the last tick, over the world already drawn into the current viewport. The
	// tracers outlive the tick that made them by exactly one tick, so a shot is on screen for every
	// frame drawn in that window and for none after — see BeamTracer.
	void DrawBeams(Camera view, int viewportWidth, int viewportHeight) {
		beams?.Render(view, scene.World.Tracers, viewportWidth, viewportHeight);
	}

	// The billboards, over the world already drawn into the current viewport: the EMP rounds crossing
	// the ground and the impact effects wherever shots have landed. After the beams for the same reason
	// they are after the world — they are alpha-tested and depth-tested against what is already there.
	void DrawSprites(Camera view, int viewportWidth, int viewportHeight) {
		sprites?.Render(view, spriteBatches, viewportWidth, viewportHeight);
	}

	// The animating skeleton, drawn over whatever was just rendered into the current viewport.
	//
	// This is the only view of the animation system there is. The mesh is baked at the shape's default
	// pose and drawn with one matrix per object (see DtsMeshBuilder.ResolveGroupOffset), so a playing
	// walk cycle moves nothing on screen; before this, the only observable output of the whole thread
	// was where the player's eye ended up. Bones are drawn through solid geometry on purpose — the
	// skeleton is inside the model it belongs to.
	void DrawSkeleton(Camera view, int viewportWidth, int viewportHeight) {
		if (!debugPanel.DrawSkeleton || wireframe == null || pilotMech == null) {
			return;
		}

		var joints = SkeletonPose.Build(pilotMech);
		debugPanel.SkeletonJointCount = joints.Length;
		if (joints.Length == 0) {
			return;
		}

		float aspect = (float)viewportWidth / Math.Max(viewportHeight, 1);
		wireframe.DrawLines(view, SkeletonWireframe.Build(joints), new Vector3(0.2f, 1f, 0.85f), aspect);

		// The node the eye rides, flagged in its own colour: it is the one joint whose motion the player
		// actually feels, so it wants to be findable among the rest.
		int cameraNode = SkeletonPose.CameraTransformId(pilotMech);
		if (cameraNode >= 0 && cameraNode < joints.Length) {
			wireframe.DrawLines(view,
				SkeletonWireframe.Marker(joints[cameraNode].World, SkeletonWireframe.CameraCrossMeters),
				new Vector3(1f, 0.85f, 0.1f), aspect);
		}
	}

	// A completed click, routed to the same state changes the corresponding key already makes — the
	// original's buttons and its keyboard bindings dispatch the same calls, so the two agree here by
	// construction rather than by two parallel implementations.
	//
	// The buttons with nothing behind them yet are deliberately silent rather than stubbed: the MFD's
	// SELECT/RANGE/TARGET/XMIT/PASS/ACTIVE, the Heads-Down Display's map arrows and zoom, its comm boxes
	// and XMIT/CANCEL all need squad, target or map state the engine does not have. They still hit-test
	// and will still light on press; they simply do nothing on release.
	void ApplyCockpitClick(CockpitClick click) {
		// The click itself is audible before anything is decided by it. In the original the sound is not
		// the handler's: it is a one-line virtual (0x438e2c, the only caller of catalog id 0x11 in the
		// image) sitting in fifteen widget vtables, so a widget clicks because it is that kind of widget,
		// not because its action did something. That is why a button with nothing behind it still clicks.
		//
		// The screen-edge strips are the exception, and for the same structural reason: that virtual
		// lives in the PanelGadget mixin every gadget carries as a second base, and ScrollTrigger is one
		// of the four classes that take no mixin at all. It is silent for want of the base, not for want
		// of an action. See docs/formats/cockpit-input.md, "The second vtable".
		if (click.Id.Kind != CockpitWidgetKind.ViewEdge) {
			audio.Director?.Play(SoundId.ButtonClick);
		}

		switch (click.Id.Kind) {
			case CockpitWidgetKind.MfdButton when click.Id.Index < MfdLayout.ModeCount:
				// Button i of the F-key column dispatches SetMode(i), and picking a screen pans back up —
				// the manual's own rule for leaving the Heads-Down Display.
				SetMfdMode((MfdMode)click.Id.Index);
				RequestHeadsDown(headsDown: false);
				break;

			case CockpitWidgetKind.MfdButton:
				ApplyMfdAuxClick(click.Id.Index);
				break;

			// A click on a FLASH COMM row: on the row already selected it presses XMIT and transmits, on
			// any other it moves the cursor — MfdFlashComm_HandleListClick's own two arms, and the same shortcut the
			// command display's order list has.
			case CockpitWidgetKind.MfdFlashCommRow when scene.World is { } clickedWorld:
				int pickedRow = click.Id.Index;
				if (pickedRow == flashComm.SelectedRow) {
					flashComm.Transmit(clickedWorld, clickedWorld.PlayerMech?.Group);
				} else {
					flashComm.Select(pickedRow, flashCommIsUp: true);
				}

				break;

			case CockpitWidgetKind.HddWidget:
				ApplyHddClick(click.Id.AsHddWidget!.Value);
				break;

			// Clicking an order arms it, which is the same thing its hotkey does — HddCommandScreen_HandleListClick walks the
			// eight label rects and calls the same HddCommandScreen_SelectOrder (0044d9cc) the key dispatch does. Clicking the one
			// already armed presses XMIT for you, which is that function's own shortcut.
			case CockpitWidgetKind.HddOrderRow when hddCommand != null:
				var picked = click.Id.AsHddOrder!.Value;
				if (hddCommand.SelectedOrder == picked && !hddCommand.AwaitingPick) {
					audio.Director?.Play(hddCommand.Transmit()
						? SoundId.ScannerActive : SoundId.ScannerPassive);
				} else {
					hddCommand.SelectOrder(picked);
				}

				break;

			// And a click in the map: a pick for an armed order, or the pilot selection the manual's
			// "select the pilot's marker on the map" describes.
			case CockpitWidgetKind.HddMapArea when hddCommand != null && cockpitArt?.HeadsDownLayout is { } mapArea:
				hddCommand.ClickMap(click.ArtX - mapArea.MapViewport.X0, click.ArtY - mapArea.MapViewport.Y0,
					scene.World?.Objects ?? Array.Empty<SimObject>());
				break;

			// A weapon row, dispatched by the class of gauge the row is — arm or chain on a weapon row,
			// the on/off button on an ECM or Turbo row, nothing at all on the other three pods. See
			// WeaponMounts.PressRow, which the number keys reach too.
			case CockpitWidgetKind.WeaponRow when pilotMech != null:
				pilotMech.Weapons.PressRow(click.Id.Index,
					click.Button.HasFlag(CockpitMouseButtons.Right));
				break;

			case CockpitWidgetKind.ConsoleButton when pilotMech != null:
				ApplyConsoleClick(click.Id.AsConsoleButton!.Value);
				break;

			// A shield facing: the manual's "click the respective shield symbol". Clicking the forward half
			// moves one step of balance forward and the rear half one step back — the same
			// Shield_BalanceAdjust the bracket keys reach, because in the original the key presses this
			// very widget (Mech_HandleCommand, 004157c8) rather than calling the adjust itself.
			case CockpitWidgetKind.ShieldFacing when pilotMech != null:
				pilotMech.Shields.AdjustBalance(
					towardFront: click.Id.AsShieldFacing!.Value == ShieldFacing.Front);
				break;

			// The screen-edge strip, one band of art that shows at the bottom of the forward view and
			// the top of the heads-down view. CockpitView_HandleEdgeTrigger (00433a88) picks the command
			// by current view -- 0 (pan down) from the forward view, 1 (pan up) from the heads-down one --
			// so the same widget means "down" or "up" according to where the pan already is.
			case CockpitWidgetKind.ViewEdge when click.Id.AsViewEdge == ViewEdgeStrip.Left:
				CommandGlance(GlanceSide.Left);
				break;

			case CockpitWidgetKind.ViewEdge when click.Id.AsViewEdge == ViewEdgeStrip.Right:
				CommandGlance(GlanceSide.Right);
				break;

			case CockpitWidgetKind.ViewEdge when cockpitHeadsDownTexture != null:
				RequestHeadsDown(headsDown: !cockpitPan.AtHeadsDown);
				break;
		}
	}

	// The MFD's aux buttons, from MfdButton_OnClick's own switch (0044681c).
	//
	// SELECT (7) and TARGET (9) are one case there, not two: it branches on the current mode, stepping
	// the status screen's own subject cursor on F1 and calling TargetSelect_Cycle everywhere else. So
	// F5's SELECT and F4's TARGET are the same action, and both do what [Enter] does. F1's arm walks a
	// squad roster the engine has no equivalent of, so it is left alone.
	//
	// XMIT (10) transmits the FLASH COMM page's selected row to the whole of the player's group.
	void ApplyMfdAuxClick(int index) {
		switch (index) {
			case MfdLayout.TransmitButton when hudState.Mfd == MfdMode.FlashComm && scene.World is { } xmitWorld:
				flashComm.Transmit(xmitWorld, xmitWorld.PlayerMech?.Group);
				break;

			case 8 when hudState.Mfd == MfdMode.Scanner:
				hudState = hudState with {
					Scanner = hudState.Scanner with {
						RangeIndex = MfdScanner.NextRangeIndex(hudState.Scanner.RangeIndex),
					},
				};

				break;

			case 7 or 9 when hudState.Mfd != MfdMode.Status:
				scene.Targeting?.Cycle();
				break;

			case 11:
				pilotMech?.SetScanner(false);
				break;

			case 12:
				pilotMech?.SetScanner(true);
				break;
		}
	}

	// Whether the command display is down and holding the letter keys. Both the split that leaves the
	// arrows scrolling its map and the one that leaves [T] as an order hotkey rather than the ATT toggle
	// read it; the original has no such clash, its own screens owning the keyboard outright while up.
	// Neither display holds anything from the external view, which leaves the pan where it was but takes
	// the cockpit's widgets, and the keys they would have taken, off.
	bool HddCommandHasKeyboard() =>
		hddCommand != null && cockpitPan.AtHeadsDown && hudState.Hdd == HddPage.CommandDisplay
		&& !ExternalViewActive();

	// Whether the display is down and holding the four arrows: the command display's map scroll, or the
	// damage detail's category and herc steps.
	bool HddHasArrows() =>
		cockpitPan.AtHeadsDown && !ExternalViewActive()
		&& (HddCommandHasKeyboard() || hudState.Hdd == HddPage.DamageDetail);

	// The same split for FLASH COMM's own letters. In the original both of the page's key dispatches are
	// mode-gated the same way — MfdDisplay_KeyDispatch (004469c0) takes the letters only while the MFD is on mode 1 — and none
	// of the seven letters means anything else anywhere in the cockpit. Here they collide with this
	// host's camera and view keys, so the page only takes them while it is the screen showing and the
	// Heads-Down Display is not down over it.
	bool FlashCommHasKeyboard() =>
		hudState.Mfd == MfdMode.FlashComm && !cockpitPan.AtHeadsDown && !ExternalViewActive();

	// The three console buttons, from ConsoleButtons_OnChildClick's own child switch. TRACK toggles ATT
	// and nothing else: the centring that [T] does on the way off belongs to Sim_DispatchCommand's
	// scancode case, not to the button, so clicking TRACK off leaves the turret where the tracker had
	// it.
	void ApplyConsoleClick(ConsoleButton button) {
		if (pilotMech == null) {
			return;
		}

		switch (button) {
			case ConsoleButton.Chain:
				pilotMech.Weapons.SetGroup((pilotMech.Weapons.Group + 1) % WeaponMounts.GroupCount);
				break;

			case ConsoleButton.Link:
				pilotMech.Weapons.ToggleLink();
				break;

			case ConsoleButton.Track:
				pilotMech.ToggleAutoTrack(scene.World);
				break;
		}
	}

	void ApplyHddClick(HddLayout.Widget widget) {
		// Dark, only the page buttons act. Anything else is held, the latest press replacing any earlier
		// one, and a page button clears it by being the press that is acted on.
		bool pageButton = widget is HddLayout.Widget.PageButton0 or HddLayout.Widget.PageButton1;
		if (cockpitDropouts.HeadsDown.Dark && !pageButton) {
			pendingHddPress = widget;
			return;
		}

		pendingHddPress = null;
		switch (widget) {
			// The two page buttons dispatch HddDisplay_SetPage (0044a5e4) with their own index, and either one opens the
			// display — the same pairing F7 and F8 have.
			case HddLayout.Widget.PageButton0:
				hudState = hudState with { Hdd = HddPage.CommandDisplay };
				RequestHeadsDown(headsDown: true);
				break;

			case HddLayout.Widget.PageButton1:
				hudState = hudState with { Hdd = HddPage.DamageDetail };
				RequestHeadsDown(headsDown: true);
				break;

			// On the damage screen the up and down arrows step the component category, which is the same
			// three [S]/[I]/[W] select — up is HddDamageScreen_NextView (00450bcc), down _PrevView (00450bf0).
			// They wrap, so the pair walks the list either way without dead ends.
			case HddLayout.Widget.ArrowUp or HddLayout.Widget.ArrowDown
				when hudState.Hdd == HddPage.DamageDetail:
				const int views = 3;
				int step = widget == HddLayout.Widget.ArrowUp ? 1 : views - 1;
				hudState = hudState with {
					HddDamage = (HddDamageView)(((int)hudState.HddDamage + step) % views),
				};
				break;

			// And left and right step the herc being inspected: the player, each seated squadmate, then the
			// target, wrapping — HddDisplay_PrevSubject (0044b9e0) and _NextSubject (0044b988).
			case HddLayout.Widget.ArrowLeft or HddLayout.Widget.ArrowRight
				when hudState.Hdd == HddPage.DamageDetail:
				hddSubjectSlot = HddDamageSubject.Step(hddSubjectSlot,
					widget == HddLayout.Widget.ArrowRight ? 1 : -1,
					slot => squadSeats[slot] != null);
				break;

			// On the command display all four arrows scroll the map instead, and the two magnifiers zoom
			// it — HddDisplay_HandleWidgetPress (0044a178)'s cases 2-7, which is one switch over the widget index for both pages.
			case HddLayout.Widget.ArrowUp when hddCommand != null:
				hddCommand.View.Pan(0, 1);
				break;

			case HddLayout.Widget.ArrowDown when hddCommand != null:
				hddCommand.View.Pan(0, -1);
				break;

			case HddLayout.Widget.ArrowLeft when hddCommand != null && hudState.Hdd == HddPage.CommandDisplay:
				hddCommand.View.Pan(-1, 0);
				break;

			case HddLayout.Widget.ArrowRight when hddCommand != null && hudState.Hdd == HddPage.CommandDisplay:
				hddCommand.View.Pan(1, 0);
				break;

			case HddLayout.Widget.ZoomIn when hddCommand != null:
				hddCommand.View.ZoomIn();
				break;

			case HddLayout.Widget.ZoomOut when hddCommand != null:
				hddCommand.View.ZoomOut();
				break;

			// A comm box selects its pilot, and selecting the one already selected drops it. Selecting a
			// pilot from the damage screen also switches back to the command display, which is what the
			// original's case 10-12 does before it selects.
			case HddLayout.Widget.PilotBox0 or HddLayout.Widget.PilotBox1 or HddLayout.Widget.PilotBox2
				when hddCommand != null:
				int slot = widget - HddLayout.Widget.PilotBox0;
				hudState = hudState with { Hdd = HddPage.CommandDisplay };
				hddCommand.SelectPilot(slot == hddCommand.SelectedPilot ? -1 : slot);
				break;

			case HddLayout.Widget.Transmit when hddCommand != null:
				audio.Director?.Play(hddCommand.Transmit() ? SoundId.ScannerActive : SoundId.ScannerPassive);
				break;

			case HddLayout.Widget.Cancel when hddCommand != null:
				hddCommand.Cancel();
				break;
		}
	}

	// What this frame's cameras draw. The player's own machine is left out while looking out of its
	// cockpit: the cockpit node the eye rides sits well inside the torso, so its geometry would wrap the
	// camera and fill the canopy. The observer camera and the external view both put it back, which is
	// the only way to see the machine you are flying.
	// Shots in flight ride on the end of both lists: they are never the player's own machine, so nothing
	// hides them, and they are rebuilt every frame rather than kept because a projectile pool churns.
	IEnumerable<SceneItem> VisibleItems() =>
		((piloting && !ExternalViewActive() ? pilotedItems : items) ?? Array.Empty<SceneItem>())
			.Concat(projectileItems)
			.Concat(weaponItems)
			.Concat(debrisItems)
			.Concat(dropPodItems);

	// Which root of each machine's shape is drawn this frame -- Shape_DrawAtDetailLevel (004033e4),
	// ported in Render.ShapeDetail and run here because this is where the camera and the window size
	// both are. The original runs it inside the machine's own draw slot, once per machine per frame,
	// which is this cadence.
	//
	// HERC DETAIL is re-read every frame for the same reason TERRAIN TEXTURE above is: the preferences
	// panel steps it over a scene that is still being drawn behind it, so the player watches the
	// machines coarsen as they step the row.
	// The focal length of the view being drawn, in its own pixels. Retail's is the video mode's fixed
	// 2^9 = 512 over 480 rows (docs/formats/cockpit-views.md); taking it off the window instead keeps the
	// detail thresholds a count of pixels on the screen actually being drawn, which is what makes them a
	// measure of apparent size rather than of a 1996 monitor's.
	int DetailFocalPixels() => Math.Max((int)MathF.Round(
		window!.FramebufferSize.Y * Camera.FocalLengthPixels / Camera.FocalViewHeightPixels), 1);

	void SelectDetailRoots() {
		if (detailChains.Count == 0) {
			return;
		}

		int focalPixels = DetailFocalPixels();
		int bias = ShapeDetail.BiasFor(simulatorPreferences[Prefs.HercDetailOption]);
		var eye = camera.Position;

		foreach (var chain in detailChains) {
			// Eye to the object's origin, in world units -- Math_FastMagnitude3D of the view-space
			// translation the model transform installs, which is the same distance by a shorter route.
			var offset = chain.Subject.Position - eye;
			int distance = (int)Math.Min(
				Math.Sqrt((double)offset.X * offset.X + (double)offset.Y * offset.Y
					+ (double)offset.Z * offset.Z),
				int.MaxValue);

			int root = ShapeDetail.SelectRoot(chain.Roots.Length, chain.ShapeRadius, distance,
				focalPixels, bias);
			if (root == chain.Active) {
				continue;
			}

			foreach (var part in chain.Roots[chain.Active]) {
				part.DetailSelected = false;
			}

			foreach (var part in chain.Roots[root]) {
				part.DetailSelected = true;
			}

			chain.Active = root;
		}
	}

	// Which level of every detail part is drawn this frame -- TSDetailPart_Render (004768bc), ported in
	// Render.PartDetail. Everything on the list is drawn by Structure_DrawWithDetailBias (004034f4) or
	// Flyer_Draw (004215cc), and both push the bias STRUCTURE DETAIL selects, so one bias serves the
	// whole list. STRUCTURE DETAIL is re-read every frame, as HERC DETAIL is above.
	void SelectDetailLevels() {
		if (detailLevelParts.Count == 0) {
			return;
		}

		int focalPixels = DetailFocalPixels();
		int bias = PartDetail.StructureBias(simulatorPreferences[Prefs.StructureDetailOption]);
		var eye = WorldScale.ToRender(camera.Position);

		// Many pieces share one detail part -- every cell of every level -- so each part is measured once.
		detailLevelChoice.Clear();
		foreach (var (owner, gate, item) in detailLevelParts) {
			var detail = gate.Detail!;
			if (!detailLevelChoice.TryGetValue((owner, detail), out int level)) {
				level = detail.Select(DetailDistance(detail, WorldScale.ToRenderMatrix(owner.WorldFrame), eye),
					focalPixels, bias);
				detailLevelChoice[(owner, detail)] = level;
			}

			item.DetailSelected = level == gate.Level;
		}
	}

	// Eye to a detail part's own node, in world units, for an object drawn at `transform`. To the node
	// rather than to the object's origin: the original measures after binding the part's transform, so
	// the distance is to where that node sits.
	int DetailDistance(PartDetail detail, Matrix4x4 transform, Vector3 eye) {
		var node = Vector3.Transform(detail.Origin, transform);
		return (int)Math.Min(Vector3.Distance(node, eye) * WorldScale.WorldUnitsPerMeter, int.MaxValue);
	}

	// One transient shape's items for this frame -- a gun on its mount, a launcher round or a piece of
	// wreckage, which are rebuilt every frame rather than kept. A shape with no TSDetailPart is its one
	// mesh; one with them is its ungated pieces plus the level of each detail part TSDetailPart_Render
	// (004768bc) selects under `bias`, chosen here as the item is made because that is when the
	// original chooses it.
	void AddAtDetail(List<SceneItem> into, SceneModel model, Matrix4x4 transform, int bias,
			Func<GpuMesh, SceneItem> make) {
		if (!cellMeshes.TryGetValue(model.Key, out var pieces)) {
			if (modelMeshes.TryGetValue(model.Key, out var mesh)) {
				into.Add(make(mesh));
			}

			return;
		}

		int focalPixels = DetailFocalPixels();
		var eye = WorldScale.ToRender(camera.Position);
		transientLevelChoice.Clear();

		for (int i = 0; i < pieces.Length; i++) {
			var gate = model.Cells[i].Gate;
			if (gate.Detail is { } detail) {
				if (!transientLevelChoice.TryGetValue(detail, out int level)) {
					level = detail.Select(DetailDistance(detail, transform, eye), focalPixels, bias);
					transientLevelChoice[detail] = level;
				}

				if (level != gate.Level) {
					continue;
				}
			}

			into.Add(make(pieces[i]));
		}
	}

	// The two things a collapsing structure does to what is on screen. A type that leaves a wreck is
	// redrawn as its BHULKS.DGS root the moment its last part falls -- the original writes that shape
	// straight onto the object's model instance, which here is the building's own items going dark and
	// the wreck item built beside them coming up. A type that leaves nothing is dropped a hundred
	// thousand units under the terrain, which is the original's own way of making a small structure
	// disappear, so its transform has to be refreshed once for it to go.
	//
	// This runs after the per-frame cell-gate pass, and overrides it: once the whole building is a wreck,
	// which of its parts were still standing stops meaning anything.
	void RefreshWreckItems() {
		foreach (var (sceneObject, structure, structureItems, hulkItems, posed) in wreckable) {
			if (structure.Sunk) {
				// A posed structure's items are in node space and the posed refresh above has already
				// put the sunk position on every one of them; writing the object transform over them
				// would stack each node's geometry at the shape's origin.
				if (!posed) {
					var sunk = MissionScene.TransformOf(sceneObject);
					foreach (var item in structureItems) {
						item.Transform = sunk;
					}
				}

				continue;
			}

			if (!structure.ShowingHulk || hulkItems.Length == 0) {
				continue;
			}

			foreach (var item in structureItems) {
				item.Visible = false;
			}

			// The swap installs the wreck shape into the same shape instance, whose cell frames the hulk
			// has never had stepped, so every cell-animation part of the wreck stands on its first cell.
			foreach (var (hulkItem, gate) in hulkItems) {
				hulkItem.Visible = gate.VisibleIn(null);
			}
		}
	}

	// One item per drop pod, from whichever of the pod's two shapes it is showing: the plain root while
	// it falls, and the cell of its opening flipbook its own counter has reached once it is down. That
	// swap is Meteor_Render's own -- the original keeps the two as separate shape instances on the
	// object and draws one or the other.
	void RefreshDropPodItems() {
		dropPodItems.Clear();

		foreach (var pod in scene.World.DropPods) {
			var model = pod.Landed
				? (pod.AnimationFrame < scene.DropPodOpeningModels.Count
					? scene.DropPodOpeningModels[pod.AnimationFrame]
					: null)
				: scene.DropPodModel;

			if (model == null || !modelMeshes.TryGetValue(model.Key, out var mesh)) {
				continue;
			}

			uint? texture = modelTextures.TryGetValue(model.Key, out var bound) ? bound.Handle : null;
			dropPodItems.Add(new SceneItem(mesh, WorldScale.ToRenderMatrix(pod.WorldTransform), texture));
		}
	}

	// One entry per ground shape within range of the camera -- the FlatObj walk at the head of
	// Scene_SubmitFrameObjects (0042841c), which submits a shape only under GroundShape.DrawRange of
	// the view object, oldest first. Each is drawn by FlatObj_Draw (0040991c), whose first act is to sit
	// the shape on the terrain under it, so the conform runs here, on exactly the shapes being drawn --
	// after its position is taken for filing, which the original's submit reads before any draw.
	//
	// The original draws a type-9 object on the spot inside its terrain cell's pass, with the ramp's
	// row count zeroed (so a textured face is a plain palette copy, fullbright) and the fade that
	// cell's quad installed (so it fogs as the ground it lies on does). Those are Fullbright and
	// CellQuantisedFog; the cell order is the renderer's, through the GroundShapeLayer.
	void RefreshGroundShapeItems() {
		if (groundLayer is null) {
			return;
		}

		groundLayer.Shapes.Clear();

		foreach (var shape in scene.World.GroundShapes) {
			if (shape.Position.ApproxDistanceTo(camera.Position) >= GroundShape.DrawRange
				|| shape.ShapeIndex < 0 || shape.ShapeIndex >= scene.GroundShapeModels.Count
				|| scene.GroundShapeModels[shape.ShapeIndex] is not { Count: > 0 } cells) {
				continue;
			}

			var submitted = shape.Position;
			shape.ConformToTerrain(scene.World.Terrain);

			var model = cells[shape.Frame % cells.Count];
			if (!modelMeshes.TryGetValue(model.Key, out var mesh)) {
				continue;
			}

			uint? texture = modelTextures.TryGetValue(model.Key, out var bound) ? bound.Handle : null;
			var item = new SceneItem(mesh, WorldScale.ToRenderMatrix(shape.WorldFrame), texture, fullbright: true) {
				CellQuantisedFog = true,
			};
			groundLayer.Shapes.Add(new GroundShapeDraw(item, submitted, shape.ShapeRadius));
		}
	}

	// One item per piece of wreckage in the air, from the shape file and root its own record names --
	// a debris table's .DTS for anything a destruction threw, and MECHWPN2.DTS for a gun knocked off its
	// hardpoint. The transform is the piece's own frame, so the tumble shows.
	//
	// Debris_Draw (00408e6c) pushes the piece's own detail bias: 0 for a table-thrown piece, HERC DETAIL's
	// for a gun -- see DebrisObject.HercDetailBias.
	void RefreshDebrisItems() {
		debrisItems.Clear();
		int hercBias = PartDetail.HercBias(simulatorPreferences[Prefs.HercDetailOption]);

		foreach (var piece in scene.World.DebrisInFlight) {
			if (!scene.DebrisModels.TryGetValue(piece.ShapeLibrary, out var shapes)
				|| piece.ShapeIndex < 0 || piece.ShapeIndex >= shapes.Count
				|| shapes[piece.ShapeIndex] is not { } model) {
				continue;
			}

			uint? texture = modelTextures.TryGetValue(model.Key, out var bound) ? bound.Handle : null;
			var transform = WorldScale.ToRenderMatrix(piece.WorldTransform);
			AddAtDetail(debrisItems, model, transform, piece.HercDetailBias ? hercBias : 0,
				mesh => new SceneItem(mesh, transform, texture));
		}
	}

	// One item per fitted, visibly-mounted weapon on every machine in the scene: the model its template
	// names for the mounting code it sits at, at the cell its own flipbook has reached.
	//
	// The flipbook IS the muzzle flash — DBSIM spawns no separate effect for one. Cell zero is the gun
	// at rest; firing starts the book and WeaponMount walks it a cell a tick until it wraps. An ELF's
	// spin-up walks the same book before the first shot, which is why that weapon takes seven ticks to
	// answer its trigger.
	//
	// The player's own guns are drawn too, unlike its hull. The hull is left out of the cockpit view
	// because the eye node sits inside the torso and its geometry would wrap the camera; a gun hangs off
	// an arm or a shoulder, out where a pilot can see it, and its flash is the whole point.
	// Scene_SubmitFrameObjects (0042841c) submits every mech in GlobalMechList with no
	// local-player test of any kind, so nothing in the original hides either.
	//
	// The mount's draw slot (WeaponMount_RenderWithDetailBias (0040ded8)) pushes HERC DETAIL's TSDetailPart bias around the render.
	void RefreshWeaponItems() {
		weaponItems.Clear();
		int bias = PartDetail.HercBias(simulatorPreferences[Prefs.HercDetailOption]);

		foreach (var sceneObject in scene.Objects) {
			if (sceneObject.Object is not MechObject mech || sceneObject.Object.AwaitingDeployment) {
				continue;
			}

			foreach (var mount in mech.Weapons.Mounts) {
				if (!scene.MechWeaponModels.TryGetValue(mount.ModelShapeIndex, out var cells)
					|| cells.Count == 0) {
					continue;
				}

				var model = cells[mount.FlashCell % cells.Count];
				uint? texture = modelTextures.TryGetValue(model.Key, out var bound) ? bound.Handle : null;
				var transform = WorldScale.ToRenderMatrix(mount.ModelFrame(mech));

				// Lit as part of the machine it hangs off, which is how the original draws it: a mount's
				// shape is composed into the mech's own render entry, so it takes that entry's selection.
				AddAtDetail(weaponItems, model, transform, bias,
					mesh => new SceneItem(mesh, transform, texture) { LightSubject = mech });
			}
		}
	}

	// One item per live projectile, from the shape its PROJ.DAT subtype names — see
	// MissionScene.BulletModels. The transform is the shot's own frame, which carries both where it is
	// and which way it is pointing, so a round is drawn nose-first along its flight.
	// Launcher rounds come out of their own table and their own shape file, and go in the same list:
	// both classes are drawn through the same vtable slot in the original.
	void RefreshProjectileItems() {
		projectileItems.Clear();

		foreach (var projectile in scene.World.Projectiles) {
			Add(scene.BulletModels, projectile.SubtypeId, projectile.Frame);
		}

		// A rocket's shape is a flipbook of geometry, not one mesh: its exhaust flame is a two-cell
		// TSCellAnimPart, and the cell is the round's own frame counter. Picking the mesh here is the
		// engine's equivalent of TSCellAnimPart_Render choosing one child.
		foreach (var rocket in scene.World.RocketsInFlight) {
			if (scene.RocketModels.TryGetValue(rocket.ShapeSubtypeId, out var cells) && cells.Count > 0) {
				AddModel(cells[rocket.AnimationFrame % cells.Count], rocket.Frame);
			}
		}

		void Add(IReadOnlyDictionary<int, SceneModel> models, int subtype, Transform3 frame) {
			if (models.TryGetValue(subtype, out var model)) {
				AddModel(model, frame);
			}
		}

		void AddModel(SceneModel model, Transform3 frame) {
			uint? texture = modelTextures.TryGetValue(model.Key, out var bound) ? bound.Handle : null;
			var transform = WorldScale.ToRenderMatrix(frame);

			// Fullbright, because Bullet_Draw — the vtable slot both classes are drawn through — zeroes
			// the ramp's row count around the shape render, which makes a round's textured polys a plain
			// palette copy with no light term. See SceneItem.Fullbright. The plasma round is the one
			// retail shape it shows on; every other projectile shape is untextured.
			//
			// Bias 0, because Bullet_Draw pushes none: a launcher round's detail parts are chosen by
			// projected size alone, whatever either detail setting says.
			AddAtDetail(projectileItems, model, transform, 0,
				mesh => new SceneItem(mesh, transform, texture, fullbright: true));
		}
	}

	// The frame's billboards, from the two things that have any.
	//
	// A shot in flight draws its shape's flipbook at its own frame counter, with the shot's own frame as
	// the transform — so an EMP round's puff leans with the round, which is what the original measures
	// its rotation and its squash off (see SpriteRenderer). An impact effect draws its EXPLOS.DTS root
	// at wherever the shot landed, upright: the original never gives one a rotation.
	void RefreshSpriteBatches() {
		spriteBatches.Clear();

		foreach (var projectile in scene.World.Projectiles) {
			if (scene.BulletModels.TryGetValue(projectile.SubtypeId, out var model)) {
				Add(model, WorldScale.ToRenderMatrix(projectile.Frame), projectile.AnimationFrame);
			}
		}

		// Nothing for rockets here: ROCKETS.DTS holds no billboards at all, only geometry — see
		// SceneModelLibrary.Rocket. Their flipbook is drawn in RefreshProjectileItems.
		foreach (var effect in scene.World.Effects) {
			if (scene.ExplosionModels.TryGetValue(effect.ShapeIndex, out var model)) {
				Add(model, Matrix4x4.CreateTranslation(WorldScale.ToRender(effect.Position)), effect.Frame);
			}
		}

		// A fire is the third: the same kind of billboard flipbook an impact effect is, upright at
		// wherever its owner has carried it to, and looping rather than playing once.
		foreach (var fire in scene.World.Fires) {
			if (fire.ShapeIndex >= 0 && fire.ShapeIndex < scene.FireModels.Count
				&& scene.FireModels[fire.ShapeIndex] is { } model) {
				Add(model, Matrix4x4.CreateTranslation(WorldScale.ToRender(fire.Position)), fire.Frame);
			}
		}

		void Add(SceneModel model, Matrix4x4 transform, int frame) {
			if (model.Sprites.Length == 0 || model.Atlas == null
				|| !spriteTextures.TryGetValue(model.Key, out var texture)) {
				return;
			}

			spriteBatches.Add(new SpriteBatch(model.Sprites, model.Atlas, texture.Handle, transform, frame));
		}
	}

	// Whether this frame is being drawn from the external camera — [V], or the developer keys viewing an
	// object other than the player's machine. Only meaningful while piloting — the free camera already
	// draws the whole scene with no cockpit over it.
	// Whether the cockpit view manager is in its view 4 — any of the external views, with no cockpit drawn.
	bool ExternalViewActive() => viewChain is { ExternalViewUp: true } && piloting && pilotMech != null;

	// Whether the outside view is the tweak's mouse orbit rather than retail's — see ExternalCamera. Never
	// once the player-death camera has the view: the tweak replaces the outside view the player steers,
	// not the one that sequence locks and steers itself.
	bool MouseOutsideView() => viewChain is { Mode: ExternalViewMode.Outside, DeathCameraRunning: false }
		&& TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.MouseExternalView);

	// Whether the cockpit's widgets are off, as CockpitView_ApplyViewState turns them off for view 4: the
	// keys CockpitWidgets_HandleCommand would have taken fall through to the dispatcher, or do nothing.
	// The tweak's outside view keeps them, being this engine's own.
	bool CockpitWidgetsOff() => ExternalViewActive() && !MouseOutsideView();

	// Whether the controls drive the camera rather than the machine. Never in the tweak's outside view,
	// where the mouse does.
	bool ControlsDriveCamera() => viewChain is { InputDrivesCamera: true } && !MouseOutsideView();

	// The controls go to a camera or to an electro-optical round: InputDrivesCamera or DAT_004d25aa, the
	// pair Input_BuildPlayerDevice tests together.
	bool ControlsOnCamera() => ControlsDriveCamera() || scene.World.MissileFlown;

	// The JOYSTICK row on the turret pair, with a stick there to bind — when the camera-axis pointers,
	// and the first button row's hold, take the turret pair rather than the movement pair.
	bool StickTurretPair() => StickCapabilities().Present
		&& joystickBindings.Assignment(simulatorPreferences, 0) == JoystickAxisAssignment.Turret;

	// The camera's half of the input, from the controls just built for the machine: the steering and
	// throttle axes after Backturn, and the trigger.
	void TakeCameraAxes(MechControls built) {
		viewCameraAxes = new PilotAxes(built.Turn, built.Throttle);
		viewCameraTrigger = built.Fire;
	}

	// The chain's share of each tick, after the simulation's — see ExternalViewChain.Advance.
	//
	// With the player's machine destroyed, Sim_MainTick runs the death camera where it would have polled
	// the objectives, and not under Alt+S — nor on the tick Alt+keypad + lets through, which freezes
	// again before it gets there. When the camera's countdown runs out it raises status 2 and ends the
	// mission whatever the answer, which the one-button panel's own table already does.
	void AdvanceViewChain() {
		bool deathCamera = pilotMech is { Destroyed: true } && !developerKeys.Frozen
			&& !developerKeys.StepPending;
		if (viewChain?.Advance(viewCameraAxes.Steer, viewCameraAxes.Throttle, viewCameraTrigger,
				ControlsDriveCamera(), terrain, deathCamera) == true) {
			scene.World.PendingMissionAlert = MissionStatus.PlayerDestroyed;
		}
	}

	// The weapon panel's keyboard set, on the manual's own bindings. Every one of these reaches exactly
	// the same call the corresponding mouse action does — the original routes them together too, through
	// the cockpit's ten-gauge array (CockpitViewInstance+0x70) and the console button panel.
	//
	//   [1]..[0]        arm that row                       -> WeaponMounts_ToggleChainMember (004110ac)'s sibling, WeaponMounts_SelectByGauge (004106ac)
	//   [Alt]+[1]..[0]  add/remove that row from the chain -> WeaponMounts_ToggleChainMember
	//   [W] / [Alt]+[W] step the armed weapon forward/back -> WeaponMounts_StepSelection (0041074c)
	//   [L]             toggle link fire on the armed pair -> WeaponMounts_ToggleLink (00410f14)
	//   [-] / [=]       lower/raise the armed weapon's power -> the armed mount's vtable +0x38
	//
	// Says what the stick can do, once — and not before it will answer.
	//
	// Silk.NET's GLFW backend publishes a connected device a frame before it publishes that device's axis,
	// button and hat counts, so this waits for a map to exist rather than running at load. Anything keyed
	// off the device's shape has to wait with it: the derived map itself, the CONTROLS panel's
	// capabilities, and --write-joystick-map.
	// The terrain's texture, or none when the player has TERRAIN TEXTURE off — prefs option 8, which in
	// the original reaches the draw as TerrainTexturingEnabled (004aab2c) and is tested per triangle by
	// Terrain_DrawCellQuad. The mesh here is built once at zone load and carries both treatments already:
	// every vertex holds the untextured fill's palette index beside its atlas UV (see TerrainMeshBuilder),
	// and the shader fills from it when no texture is bound. So the switch is the texture binding and nothing else, and it
	// applies on the frame it is thrown, as the original's does.
	uint? TerrainTextureHandle() =>
		simulatorPreferences[Prefs.TerrainTextureOption] != 0
			? terrainTexture?.Handle
			: null;

	// Whether a modal is up, and so holding the input. All four freeze the simulation behind them and all
	// four take the keyboard's command keys; the stick goes with them.
	bool AnyModalPanelOpen() =>
		statusAlertPanel is { IsOpen: true } || objectivesPanel is { IsOpen: true }
		|| preferencesPanel is { IsOpen: true } || controlsPanel is { IsOpen: true };

	// Sim_MainTick's own arms: once the mission is decided the poll raises the status alert by itself, and
	// once the player is down the death camera does. Latched on SimWorld.PendingMissionAlert by the tick
	// that produced it.
	void RaisePendingMissionAlert() {
		if (scene.World is { PendingMissionAlert: not MissionStatus.None } alerted
			&& statusAlertPanel is { IsOpen: false } && objectivesPanel is not { IsOpen: true }
			&& !missionOver) {
			var raised = alerted.PendingMissionAlert;
			alerted.PendingMissionAlert = MissionStatus.None;
			OpenStatusAlert(raised, alerted.Objectives);
		}
	}

	// Whether an input tape is driving the mission.
	bool TapePlaying() => tapePlayer is { Finished: false };

	bool ImGuiHasKeyboard() => imgui != null && ImGui.GetIO().WantCaptureKeyboard;

	// Whether the debug UI is typing and the game's keys should go dead. Never during a replay: the tape's
	// keystrokes are not the player's, and the player typing into the debug panel must not lose them.
	bool KeyboardCapturedByImGui() => !TapePlaying() && ImGuiHasKeyboard();

	// What the stick in use can do: the recording machine's during a replay, the attached one otherwise.
	JoystickCapabilities StickCapabilities() =>
		TapePlaying() ? tapePlayer!.Capabilities : joystick?.Capabilities ?? JoystickCapabilities.None;

	// One tick on the engine's own timestep. Returns whether the simulation has stopped behind a modal
	// panel or the mission's end, which only a recording asks about mid-loop — see RecordTick.
	bool TickLive(bool emit) {
		// Alt+S's freeze is not a panel's: the tick still runs, with only the player's input poll and the
		// mission poll in it. See SimWorld.TickFrozen.
		if (developerKeys.Frozen) {
			scene.World.TickFrozen();
		} else {
			scene.World.Tick();
		}

		AdvanceViewChain();

		// Beams are resolved and forgotten inside the tick, so anything that wants to see one has to look
		// between ticks — see SimWorld.Beams.
		debugPanel.SampleBeams(scene.World);
		return RecordTick(emit);
	}

	// A recording's half of a tick: the frame goes on the tape, and the mission alert goes up straight
	// after the tick that decided it rather than at the top of the next host frame, so the ticks the loop
	// would still run behind it do not happen — as in a replay, where Sim_MainTick raises it itself.
	bool RecordTick(bool emit = true) {
		if (tapeRecorder == null) {
			return false;
		}

		if (emit) {
			tapeRecorder.EmitTick(SimWorld.TickDelta, StickCapabilities());
		}

		RaisePendingMissionAlert();
		return missionOver || AnyModalPanelOpen();
	}

	// The top of a recorded host frame: whether it is a panel's, and the keys that went down since the last
	// one. Keys are edges of the polled state rather than the device's own events, because the handlers
	// poll too: a key pressed and let go between two frames is one no handler saw. Only what reaches the
	// game is recorded — nothing while the debug UI has the keyboard or the free camera is being flown.
	void RecordFrameStart() {
		if (tapeRecorder == null) {
			return;
		}

		recordPanelAtStart = AnyModalPanelOpen();
		tapeRecorder.SetHeld(PilotAxes.Centred, false, 0);
		if (liveKeys == null) {
			return;
		}

		bool listening = !ImGuiHasKeyboard() && (piloting || recordPanelAtStart);
		int modifiers = (AltHeld(liveKeys) ? InputTapePlayer.AltBit : 0)
			| (CtrlHeld(liveKeys) ? InputTapePlayer.CtrlBit : 0);
		foreach (int scancode in TapeKeys.RecordedScancodes) {
			bool down = TapeKeys.KeysOf(scancode).Any(liveKeys.IsKeyPressed);
			if (down && listening && !recordKeysDown.Contains(scancode)) {
				tapeRecorder.AddPress(scancode | modifiers);
			}

			if (down) {
				recordKeysDown.Add(scancode);
			} else {
				recordKeysDown.Remove(scancode);
			}
		}
	}

	// One live mouse event onto the tape, in the game's own screen space — ApplyTapeMouse run backwards.
	// Dropped where the cockpit would not see it: over the debug UI, or in the free camera.
	void RecordMouse(float x, float y, CockpitMouseButtons buttons, int framebufferWidth, int framebufferHeight) {
		if (tapeRecorder == null) {
			return;
		}

		if (!AnyModalPanelOpen() && (!piloting || (imgui != null && ImGui.GetIO().WantCaptureMouse))) {
			return;
		}

		int scale = simulatorPreferences[Prefs.VideoModeOption] == 1 ? 2 : 1;
		var (screenX, screenY) = AlertPanelLayout.Placement.CreateAt(framebufferWidth, framebufferHeight, 0, 0)
			.ToPanel(x, y);
		tapeRecorder.AddMouse(new InputTape.MouseEvent {
			X = (int)MathF.Round(screenX / scale),
			Y = (int)MathF.Round(screenY / scale),
			Buttons = (ushort)((int)buttons & 3),
			Time = (int)audio.CoarseTicks,
		});
	}

	// The stick's discrete half onto the tape: the button that fired and the hat's views. The trigger
	// scan's own button never claims the slot, so it is already absent, as retail zeroes its byte before
	// the frame is written.
	void RecordStick(JoystickPilotInput input) {
		if (tapeRecorder == null || TapePlaying()) {
			return;
		}

		tapeRecorder.SetDiscreteStick(input.ClaimedButton, input.Views);
	}

	// Takes the replay's next frame once it is due, before any handler reads input this host frame, and
	// puts its keystrokes and mouse events where those handlers look. At most one frame a host frame
	// goes in this way: every frame behind it that carries only held state follows in RunTapeTicks.
	//
	// A frame that presses anything waits for a host frame with nothing down, so every key-down edge the
	// tape records is one the handlers see — which also delays it by a host frame when two such frames
	// fall due back to back. The delay is time the pacing then catches up.
	void TakeTapeFrame(double deltaSeconds) {
		tapeFrame = null;
		if (tapePlayer == null) {
			return;
		}

		tapeKeys?.Release();
		if (tapePlayer.Finished || missionOver) {
			return;
		}

		if (liveStopRequested) {
			EndTape(stopped: true);
			return;
		}

		if (tapePlayer.Peek() is not { } next) {
			EndTape(stopped: false);
			return;
		}

		bool pulsedLastFrame = tapePulsed;
		tapePulsed = false;

		bool underPanel = AnyModalPanelOpen();
		double duration = InputTapePlayer.DurationOf(next, underPanel);
		tapeAccumulator = Math.Min(tapeAccumulator + deltaSeconds, MaxAccumulatedSeconds);
		if (tapeAccumulator < duration) {
			return;
		}

		bool discrete = InputTapePlayer.HasDiscreteInput(next);
		if (discrete && pulsedLastFrame) {
			return;
		}

		tapePlayer.Take();
		tapeAccumulator -= duration;
		tapeFrame = next;
		tapeFrameUnderPanel = underPanel;
		tapePulsed = discrete;

		foreach (int code in InputTapePlayer.PressesOf(next)) {
			tapeKeys!.Press(code);
		}

		foreach (var mouseEvent in next.MouseEvents) {
			ApplyTapeMouse(mouseEvent, underPanel);
		}
	}

	// One of the tape's mouse events. Its position is in the game's own screen space — Mouse_DispatchEvent
	// (0048083c) scales a client position to half the back buffer, which is the viewport: the 640x480 a
	// panel centres itself on and the forward view fills, or 320x240 in the low-resolution mode — so it
	// lands wherever this engine puts that screen.
	void ApplyTapeMouse(InputTape.MouseEvent mouseEvent, bool underPanel) {
		var framebuffer = window.FramebufferSize;
		int scale = simulatorPreferences[Prefs.VideoModeOption] == 1 ? 2 : 1;

		var (x, y) = AlertPanelLayout.Placement.CreateAt(framebuffer.X, framebuffer.Y, 0, 0)
			.ToWindow(mouseEvent.X * scale, mouseEvent.Y * scale);
		var buttons = (CockpitMouseButtons)(mouseEvent.Buttons & 3);
		tapePointer = (x, y, buttons);

		// The same queue a live click goes into, and drained by the same code. A panel takes its pointer
		// from tapePointer instead, as it takes the live one straight off the device.
		if (!underPanel) {
			cockpitInput.Enqueue(x, y, buttons);
		}
	}

	// The simulation's half of a replay, after every input handler has seen this host frame's tape frame.
	void RunTapeTicks() {
		NoteTapePanel();
		if (missionOver) {
			return;
		}

		if (tapeFrame is { } frame) {
			if (!tapeFrameUnderPanel && AnyModalPanelOpen()) {
				// The frame's own input put a panel up, which in the original runs from inside that frame's
				// tick; the rest of the tick waits for the panel.
				tapeDeferredTick = frame.TickDelta;
			} else if (!tapeFrameUnderPanel) {
				TickTape(frame.TickDelta);
			} else if (!AnyModalPanelOpen() && tapeDeferredTick is { } deferred) {
				// This frame took the panel down. The tick that raised it finishes now, and it reads the
				// last input the panel's own loop built, because both share the one input block.
				tapeDeferredTick = null;
				if (pilotMech != null) {
					pilotMech.Controls = TapeControls(frame, centerTorso: false, centerBody: false);
				}

				TickTape(deferred);
			}
		}

		// Then every frame behind it that is due and presses nothing.
		while (TapePlaying() && !missionOver && tapePlayer!.Peek() is { } next
				&& !InputTapePlayer.HasDiscreteInput(next)) {
			bool underPanel = AnyModalPanelOpen();
			double duration = InputTapePlayer.DurationOf(next, underPanel);
			if (tapeAccumulator < duration) {
				break;
			}

			tapePlayer.Take();
			tapeAccumulator -= duration;
			if (!underPanel) {
				if (pilotMech != null) {
					pilotMech.Controls = TapeControls(next, centerTorso: false, centerBody: false);
					TakeCameraAxes(pilotMech.Controls);
				}

				TickTape(next.TickDelta);
			}
		}
	}

	// One tick of the tape's own length. The mission alert goes up straight after the tick that decided
	// it, rather than at the top of the next host frame, so the frames behind it are the panel's — as
	// they are in the recording, where Sim_MainTick raises the panel itself.
	void TickTape(short tickDelta) {
		// Alt+S on the tape freezes the simulation and the tape runs on, as Sim_MainTick keeps polling it.
		if (developerKeys.Frozen) {
			scene.World.TickFrozen(tickDelta);
		} else {
			scene.World.Tick(tickDelta, InputTapePlayer.SecondsOf(tickDelta) * 1000);
		}

		AdvanceViewChain();

		debugPanel.SampleBeams(scene.World);
		RaisePendingMissionAlert();
		NoteTapePanel();
	}

	// Logs the frame each modal panel goes up and comes down on, to set against the tape's own
	// InferredPanelSpans: where they disagree, the replay has left the recording.
	void NoteTapePanel() {
		bool shown = AnyModalPanelOpen();
		if (shown != tapePanelShown && tapePlayer != null) {
			tapePanelShown = shown;
			Console.WriteLine($"Tape frame {tapePlayer.Position - 1}: a panel {(shown ? "went up" : "came down")}.");
		}
	}

	// The machine's controls from one frame: its axes, with this install's keyjoy.cfg Backturn applied as
	// the original applies it on playback, and its trigger.
	MechControls TapeControls(InputTape.Frame frame, bool centerTorso, bool centerBody) {
		var axes = joystickBindings.Combine(
			new JoystickPilotInput(InputTapePlayer.AxesOf(frame), false, Array.Empty<JoystickAction>()),
			PilotAxes.Centred);

		return new MechControls(axes.Steer, axes.Throttle,
			ThrottleLever: joystickBindings.ThrottleLeverMode(StickCapabilities(), simulatorPreferences),
			TorsoTwist: axes.TorsoTwist,
			TorsoPitch: axes.TorsoPitch,
			CenterTorso: centerTorso,
			CenterBody: centerBody,
			Fire: InputTapePlayer.TriggerOf(frame));
	}

	// The frame's stick, in the shape a live one is resolved to. Its buttons are already past the
	// press-once latch, so a set bit is an action this frame; the first set bit claims the tick's one
	// action, as the first pressed button does live — see JoystickBindings.
	JoystickPilotInput TapeJoystickInput() {
		if (tapeFrame is not { } frame || tapeFrameUnderPanel) {
			return JoystickPilotInput.None;
		}

		var pressed = Array.Empty<JoystickAction>();
		for (int i = 0; i < InputTapePlayer.RecordedButtonCount; i++) {
			if (!InputTapePlayer.ButtonOf(frame, i)) {
				continue;
			}

			var action = joystickBindings.Action(simulatorPreferences, i);
			if (action is not (JoystickAction.Off or JoystickAction.Fire)) {
				pressed = new[] { action };
			}

			break;
		}

		return new JoystickPilotInput(InputTapePlayer.AxesOf(frame), InputTapePlayer.TriggerOf(frame),
			pressed, InputTapePlayer.HatOf(frame));
	}

	// The tape has run out, or [Ctrl+E] stopped it. Under -D that ends the mission, as the abort does in
	// retail; otherwise the controls go back to the player and the engine's own timestep, which is -p's
	// hand-over.
	void EndTape(bool stopped) {
		tapePlayer!.Stop();
		tapeKeys?.Release();
		Console.WriteLine($"Tape {tapePlayer.Name} {(stopped ? "stopped" : "ended")} at frame {tapePlayer.Position}.");

		if (tapePlayer.DemoMode) {
			missionOver = true;
			Console.WriteLine("Demo over.");
			window.Close();
			return;
		}

		keyboard = liveKeys;
		SimMath.PerTickStepsScaled = true;
		tickAccumulator = 0;
		tapeDeferredTick = null;
	}

	// The stick as the CONTROLS panel reads it, which is not how the rest of the session reads it: here a
	// button press picks the row it belongs to rather than firing whatever that row is bound to. The panel
	// owns what a press means (ControlsPanel.PressButtonRow); this owns only which press is new.
	//
	// JoystickReading.Buttons is the device's own eight, before JoystickBindings lifts the trigger out of
	// them, so BUTTON 1's row is reachable with the trigger — the thing ControlsPanel_HandleEvent restores
	// by hand before it reads the device block.
	//
	// Retail latches the button it acts on and the next input build masks it to zero, so a held button is
	// one step and no more, and the panel sees a latched button as not pressed at all. Masking first is
	// the same arrangement, and it is why this can simply take the lowest pressed row — the original
	// breaks at the first set byte it finds, which is the same row.
	void ReadControlsPanelJoystick(ControlsPanel panel) {
		if (joystick is not { Capabilities.Present: true }) {
			return;
		}

		byte pressed = joystick.Read().Buttons;
		controlsPanelLatched &= pressed;

		for (int row = 0; row < JoystickCapabilities.MaxButtons; row++) {
			int bit = 1 << row;
			if ((pressed & ~controlsPanelLatched & bit) == 0) {
				continue;
			}

			controlsPanelLatched |= (byte)bit;
			panel.PressButtonRow(row);
			return;
		}
	}

	void AnnounceJoystick() {
		if (joystickAnnounced || joystick is not { Map: { } map }) {
			return;
		}

		joystickAnnounced = true;

		foreach (string line in joystick.Describe()) {
			Console.WriteLine(line);
		}

		// The lever's mode lives in the map but is read through the bindings, the control law having no
		// route to the map. Derived maps never set it, so this only ever carries a file's own choice.
		joystickBindings.BipolarThrottle = map.BipolarThrottle;

		// Only when a stick really answered: with none attached the panel keeps whatever --joystick staged,
		// which is the whole point of that flag.
		if (controlsPanel is not null && joystick.Capabilities.Present) {
			controlsPanel.Capabilities = joystick.Capabilities;
		}

		if (probeJoystick) {
			Console.WriteLine("Move one control at a time; put what it prints into "
				+ $"data\\{JoystickDeviceMap.FileName}.");
		}

		if (writeJoystickMap && dataDirectory is not null) {
			string mapPath = Path.Combine(dataDirectory, JoystickDeviceMap.FileName);
			try {
				map.Save(mapPath);
				Console.WriteLine($"Wrote {mapPath}.");
			} catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
				Console.WriteLine($"Could not write {mapPath}: {error.Message}");
			}
		}
	}

	// One joystick button's action — Sim_PollPlayerInput's own switch (00460764), nineteen cases for codes
	// 2-20, which it runs over SimOptions[ControlsOptionBase + 4 + button] for the one button that claims
	// the tick's slot.
	//
	// FIRE and OFF have no case, there or here; JoystickBindings keeps both out of Pressed while still
	// letting them claim the slot (docs/formats/joystick-input.md#the-buttons).
	//
	// Every case reaches the same code a key or a click does, which is also true in the original — the
	// switch is almost entirely made of calls into the widget tree and the mech's own command handler
	// rather than of gameplay of its own.
	void ApplyJoystickAction(JoystickAction action, MechObject mech) {
		switch (action) {
			case JoystickAction.Target:
				scene.Targeting?.Cycle();
				break;

			case JoystickAction.TargetNearest:
				scene.Targeting?.SelectNearest();
				break;

			case JoystickAction.CenterLegs:
				joystickCenterBody = true;
				break;

			case JoystickAction.CenterTurret:
				mech.LatchCenterTorso();
				break;

			// The lever's sense, and only when there is a lever bound to the throttle, so on a stick without
			// one this button does nothing at all. The original tests the capability block's +4 and the
			// walker's THROTTLE row through a literal, even in a RAZOR (docs/formats/joystick-input.md,
			// "The buttons"); this tests the current machine's row, which differs from it in a RAZOR whose
			// THROTTLE row is not the walker's.
			case JoystickAction.ChangeDirection
				when joystickBindings.ThrottleLeverMode(StickCapabilities(), simulatorPreferences) != 0:
				joystickBindings.ThrottleLeverInverted = !joystickBindings.ThrottleLeverInverted;
				break;

			// Command 0x14, the same one [T] dispatches: toggling ATT off also latches the centring mode,
			// so the turret comes home rather than staying where the tracker left it.
			case JoystickAction.AttitudeToggle:
				if (!mech.ToggleAutoTrack(scene.World)) {
					mech.LatchCenterTorso();
				}

				break;

			case JoystickAction.AllStop:
				mech.AllStop();
				break;

			// Mech commands 0x1a and 0x1b. They press the gauge's own facing widget rather than calling the
			// adjust, which is why they click — see the bracket keys.
			case JoystickAction.ShieldsFront:
			case JoystickAction.ShieldsRear:
				mech.Shields.AdjustBalance(towardFront: action == JoystickAction.ShieldsFront);
				audio.Director?.Play(SoundId.ButtonClick);
				break;

			// The original's two branches both send a scancode to CockpitWidgets_HandleCommand, which ignores
			// everything while the widgets are off — so neither does anything from the external view.
			case JoystickAction.HddView when !CockpitWidgetsOff():
				// A toggle, which is what the action's two branches were plainly meant to be. The original
				// tests the view manager's pointer rather than the view, so it can only ever leave the HDD
				// (docs/formats/joystick-input.md, "HDD VIEW can only leave").
				RequestHeadsDown(headsDown: !cockpitPan.HeadsDownRequested);
				break;

			case JoystickAction.CockpitView when !CockpitWidgetsOff():
				RequestHeadsDown(headsDown: false);
				break;

			case JoystickAction.LinkWeapon:
				mech.Weapons.ToggleLink();
				break;

			// MfdDisplay_CycleMode (00446e14): step the MFD's mode, wrapping at six. Selecting a screen also pans back up,
			// which is the manual's own rule for leaving the heads-down display.
			case JoystickAction.MfdDisplays:
				SetMfdMode((MfdMode)(((int)hudState.Mfd + 1) % MfdLayout.ModeCount));
				RequestHeadsDown(headsDown: false);
				break;

			// Weapon-manager command 0x202, which WeaponMounts_HandleCommand answers with
			// ToggleChainMember(0) — [Alt]+[1], row 1's fire-chain membership, and not a general toggle
			// despite the caption.
			case JoystickAction.WeaponToggle:
				mech.Weapons.ToggleChain(0);
				break;

			// The console chain button, scancode 0x29.
			case JoystickAction.NextChain:
				mech.Weapons.SetGroup((mech.Weapons.Group + 1) % WeaponMounts.GroupCount);
				break;

			case JoystickAction.NextWeapon:
				mech.Weapons.CycleSelection(1);
				break;

			case JoystickAction.PreviousWeapon:
				mech.Weapons.CycleSelection(-1);
				break;

			// OUTSIDE VIEW is [V]'s step without its lock test, and CHASE VIEW the chase view's — see
			// ExternalViewChain.
			case JoystickAction.OutsideView:
				viewChain?.ToggleOutside(fromKeyboard: false);
				break;

			case JoystickAction.ChaseView:
				viewChain?.ToggleChase();
				break;
		}
	}

	// All fire on their own key-down edge: they are toggles and steps, not held states. [Space] is the
	// exception and is not here — the trigger is a held state read straight off the device struct, so it
	// travels with the rest of the pilot's input in MechControls.
	//
	// A null <paramref name="mounts"/> is the swallow: every latch is brought up to date and nothing acts,
	// which is what a modal wants — no machine is listening while one is up, and a key pressed to work the
	// panel must not fire as it closes.
	void ApplyWeaponKeys(IKeyState keyboard, WeaponMounts? mounts) {
		bool alt = AltHeld(keyboard);

		// [Ctrl+Alt] and a number is 0x602-0x60a, the developer keys' step size, and [Alt+keypad +] is
		// 0x24e, their single tick. Neither is a weapon command.
		bool ctrl = CtrlHeld(keyboard);

		for (int slot = 0; slot < weaponRowKeys.Length; slot++) {
			bool down = keyboard.IsKeyPressed(weaponRowKeys[slot]);
			if (down && !weaponRowKeyDown[slot] && !ctrl) {
				// [Alt] and a number is command 0x202-0x20b, which the weapon manager answers itself; the
				// bare number is 0x02-0x0b, which CockpitWidgets_HandleCommand answers by pressing the
				// row's own select gadget. That is why only the bare key can toggle a pod.
				if (alt) {
					mounts?.ToggleChain(slot);
				} else if (!CockpitWidgetsOff()) {
					mounts?.PressRow(slot);
				}
			}

			weaponRowKeyDown[slot] = down;
		}

		bool cycleKey = keyboard.IsKeyPressed(Key.W);
		if (cycleKey && !cycleWeaponKeyDown) {
			mounts?.CycleSelection(alt ? -1 : 1);
		}

		cycleWeaponKeyDown = cycleKey;

		bool linkKey = keyboard.IsKeyPressed(Key.L);
		if (linkKey && !linkKeyDown) {
			mounts?.ToggleLink();
		}

		linkKeyDown = linkKey;

		// [-] and [=], with the keypad's own pair alongside them, move the armed energy weapon's power
		// level. Also an edge: each press is one step of 0x50 out of 1200.
		bool powerUpKey = keyboard.IsKeyPressed(Key.Equal) || (keyboard.IsKeyPressed(Key.KeypadAdd) && !alt);
		bool powerDownKey = keyboard.IsKeyPressed(Key.Minus) || keyboard.IsKeyPressed(Key.KeypadSubtract);
		if (powerUpKey && !powerUpKeyDown) {
			mounts?.AdjustPower(raise: true);
		}
		if (powerDownKey && !powerDownKeyDown) {
			mounts?.AdjustPower(raise: false);
		}

		powerUpKeyDown = powerUpKey;
		powerDownKeyDown = powerDownKey;
	}
}

/// <summary>Every mouse button currently held, as the cockpit's own flag pair.</summary>
static CockpitMouseButtons ButtonsHeld(IMouse mouse) =>
	(mouse.IsButtonPressed(MouseButton.Left) ? CockpitMouseButtons.Left : CockpitMouseButtons.None)
	| (mouse.IsButtonPressed(MouseButton.Right) ? CockpitMouseButtons.Right : CockpitMouseButtons.None);

/// <summary>One button as its flag. Anything but left and right is <see cref="CockpitMouseButtons.None"/> — the original watches only those two.</summary>
static CockpitMouseButtons ButtonFlag(MouseButton button) => button switch {
	MouseButton.Left => CockpitMouseButtons.Left,
	MouseButton.Right => CockpitMouseButtons.Right,
	_ => CockpitMouseButtons.None,
};

static Camera CloneCockpitCamera(Camera source) => new() {
	Position = source.Position,
	Yaw = source.Yaw,
	Pitch = source.Pitch,
	Roll = source.Roll,
	FieldOfView = source.FieldOfView,
	NearPlane = source.NearPlane,
	FarPlane = source.FarPlane,
};

static CameraInput ReadInput(IKeyState? keyboard) {
	if (keyboard == null) {
		return default;
	}

	return new CameraInput {
		Forward = Axis(keyboard, Key.W, Key.S),
		Strafe = Axis(keyboard, Key.D, Key.A),
		Vertical = Axis(keyboard, Key.R, Key.F),
		Yaw = Axis(keyboard, Key.Right, Key.Left),
		Pitch = Axis(keyboard, Key.Up, Key.Down),
		Boost = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight),
	};
}

// The MFD screen the function keys are asking for, or null when none of them is down — returning null
// rather than a default keeps the display on whatever screen it was already showing.
static MfdMode? ReadMfdMode(IKeyState keyboard) {
	Key[] keys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6 };
	for (int i = 0; i < keys.Length; i++) {
		if (keyboard.IsKeyPressed(keys[i])) {
			return (MfdMode)i;
		}
	}

	return null;
}

// The Heads-Down damage screen's component category, or null when none of its three keys is down —
// same rule as ReadMfdMode: returning null leaves the screen on whatever it was already showing.
static HddDamageView? ReadHddDamageView(IKeyState keyboard) {
	if (keyboard.IsKeyPressed(Key.S)) {
		return HddDamageView.Structural;
	}

	if (keyboard.IsKeyPressed(Key.I)) {
		return HddDamageView.Internal;
	}

	return keyboard.IsKeyPressed(Key.W) ? HddDamageView.Weapons : null;
}

static bool CtrlHeld(IKeyState keyboard) =>
	keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);

static bool AltHeld(IKeyState keyboard) =>
	keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);

// One signed axis from a pair of keys, plus optional aliases for each direction — the arrow cluster
// and the numeric keypad are the same key on the hardware the manual is describing, and a host window
// sees them as two.
static int Axis(IKeyState keyboard, Key positive, Key negative,
		Key? positiveAlias = null, Key? negativeAlias = null) {
	bool up = keyboard.IsKeyPressed(positive) || (positiveAlias is { } p && keyboard.IsKeyPressed(p));
	bool down = keyboard.IsKeyPressed(negative) || (negativeAlias is { } n && keyboard.IsKeyPressed(n));
	return (up ? 1 : 0) - (down ? 1 : 0);
}

/// <summary>
/// One turret axis: the key pair, or whatever <c>--turret</c> is holding when no key is down. Never
/// past full deflection, so holding a key during a <c>--turret</c> run cannot ask for more rate than
/// a stick can.
/// </summary>
static short TurretAxis(int keys, short held) =>
	keys != 0 ? (short)(keys * MechControls.KeyboardAxis) : held;

/// <summary>
/// One machine's LOD chain as the host holds it: every root's items, uploaded together, and which of
/// them is currently drawn. See <see cref="Herculan.Engine.Render.ShapeDetail"/> for the selection
/// and docs/formats/mech-shape-drawing.md for the mechanism it ports.
/// </summary>
/// <param name="Subject">The machine, whose position the distance to the eye is measured to.</param>
/// <param name="ShapeRadius">Root 0's own bounding radius in world units.</param>
/// <param name="Roots">The items of each root, finest first. A root that failed to upload is empty.</param>
sealed record MechDetailChain(SimObject Subject, int ShapeRadius, SceneItem[][] Roots) {
	/// <summary>The root currently selected, which starts at 0 as the build leaves it.</summary>
	public int Active { get; set; }
}
