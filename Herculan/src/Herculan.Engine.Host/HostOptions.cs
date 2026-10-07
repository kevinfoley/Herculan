using Herculan.Engine.Cockpit;
using Herculan.Engine.Input;
using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Host;

/// <summary>
/// The command line, parsed. <see cref="HostArguments"/> reads each flag's value and holds the <c>--help</c>
/// text; docs/herculan/herculan-command-line.md describes every flag. Each comment below says why the flag
/// exists, which is what that page links back to.
/// </summary>
sealed class HostOptions {
	private readonly List<string> _positional = new();

	/// <summary>Whether <c>--help</c> was asked for, which ends parsing and the run.</summary>
	public bool ShowHelp { get; private set; }

	/// <summary>The install named on the command line, if any.</summary>
	public string? InstallPath => _positional.Count > 0 ? _positional[0] : null;

	/// <summary>The mission named on the command line, if any: a script.dat, a save slot or a .MSN name.</summary>
	public string? MissionPath => _positional.Count > 1 ? _positional[1] : null;

	public string? ScreenshotPath { get; private set; }
	public bool SavePrintScreens { get; private set; }
	public string? MoviePath { get; private set; }

	public bool SilentAudio { get; private set; }
	public string? CdDrive { get; private set; }
	public string? MusicDirectory { get; private set; }
	public int MusicTrackSelect { get; private set; }

	public bool WritePreferences { get; private set; } = true;
	public bool AskForInstall { get; private set; }
	public bool MissionWindowed { get; private set; }
	public bool ProbeJoystick { get; private set; }
	public bool WriteJoystickMap { get; private set; }

	public string? PlayTape { get; private set; }
	public bool DemoTape { get; private set; }
	public string? RecordTape { get; private set; }
	public bool DeveloperMode { get; private set; }

	/// <summary>Whether one mission flies in place of the front end, which is the default.</summary>
	public bool RunMission { get; private set; }

	public string? InstallSource { get; private set; }
	public string? InstallDestination { get; private set; }
	public RetailInstaller.Size InstallSize { get; private set; } = RetailInstaller.Size.Maximum;
	public RetailInstaller.Language InstallLanguage { get; private set; } = RetailInstaller.Language.English;

	public ShellOptions Shell { get; } = new();
	public StagedStart StagedStart { get; } = new();
	public StagedScreenshot StagedScreenshot { get; } = new();

	/// <summary>
	/// Parses <paramref name="args"/>, recording every problem in <paramref name="errors"/> rather than stopping
	/// at the first. <c>--help</c> stops at once and sets <see cref="ShowHelp"/>.
	/// </summary>
	public static HostOptions Parse(string[] args, List<string> errors) {
		var options = new HostOptions();
		var staging = options.StagedStart;
		var capture = options.StagedScreenshot;
		var shell = options.Shell;

		for (int i = 0; i < args.Length; i++) {
			if (HostArguments.IsHelp(args[i])) {
				options.ShowHelp = true;
				return options;
			} else if (args[i] == "--screenshot") {
				if (HostArguments.TryReadString(args, ref i, errors, out string path)) {
					options.ScreenshotPath = path;
				}
			} else if (args[i] == "--save-prtscn") {
				// Also writes each frame [PrtScn] copies to the clipboard while full screen into the install's Screenshots
				// folder, so a run's captures are kept without pasting each one somewhere. See PrintScreenFiles.
				options.SavePrintScreens = true;
			} else if (args[i] == "--mfd") {
				// Which MFD screen to power up on. F1-F6 switch it live; this exists so a --screenshot run,
				// which never sees a keystroke, can be pointed at a specific screen.
				if (HostArguments.TryReadInt(args, ref i, 0, 5, errors, out int mfdIndex)) {
					staging.Mfd = (MfdMode)mfdIndex;
				}
			} else if (args[i] == "--quit") {
				// Power up with the [Q] mission-status alert already raised, for the same reason as
				// --objectives. With no argument it shows whatever the mission evaluates to at that moment,
				// which is what pressing [Q] would give; an optional status number forces one of the twenty
				// GNL_ALRT.STR rows instead, so the one-button and single-line layouts — and 0 and 1, the
				// pause panel's own two — can be looked at without arranging a mission that produces them.
				staging.StatusAlert = true;
				if (HostArguments.TryReadOptionalInt(args, ref i, 0, StatusAlertPanel.StatusCount - 1, out int forcedStatus)) {
					staging.StatusAlertStatus = forcedStatus;
				}
			} else if (args[i] == "--objectives") {
				// Power up with the [F11] objectives panel already open, for the same reason as --mfd: a
				// --screenshot run never sees a keystroke.
				staging.Objectives = true;
			} else if (args[i] == "--preferences") {
				// Likewise for the [F12] preferences panel.
				staging.Preferences = true;
			} else if (args[i] == "--controls") {
				// And for the CONTROLS panel it raises, which opens over it.
				staging.Preferences = true;
				staging.Controls = true;
			} else if (args[i] == "--joystick") {
				// Pretends a fully-featured stick is attached, so the CONTROLS panel's rows go live and show
				// what prefs.cfg has them bound to even where there is no hardware — a --screenshot run has
				// none. A real device, when one is attached, overrides this. An optional count sets how many of
				// the eight button rows are live.
				staging.Joystick = new JoystickCapabilities(Present: true,
					ButtonCount: JoystickCapabilities.MaxButtons,
					HasThrottle: true, HasRudder: true, HasHat: true);
				if (HostArguments.TryReadOptionalInt(args, ref i, 0, JoystickCapabilities.MaxButtons, out int stickButtons)) {
					staging.Joystick = staging.Joystick with { ButtonCount = stickButtons };
				}
			} else if (args[i] == "--joystick-probe") {
				// Prints each axis and button of the attached stick as it moves. Its whole purpose is filling
				// in data\herculan-joystick.cfg by hand: a modern stick's axis order is its own, and nothing
				// but moving each control in turn will say which index is the throttle.
				options.ProbeJoystick = true;
			} else if (args[i] == "--write-joystick-map") {
				// Writes the map the engine is actually using out to data\herculan-joystick.cfg, comments and
				// all, so there is something to edit rather than a file to compose from scratch. With no file
				// there already, that is the device's own reported shape in retail's X/Y/Z/R order.
				options.WriteJoystickMap = true;
			} else if (args[i] == "--no-write-prefs") {
				// Leaves data\prefs.cfg alone. Saving is on by default, as it is in the original: each panel
				// writes its own options back as it closes. This is the way out for anyone who would rather
				// their retail install were not touched at all.
				options.WritePreferences = false;
			} else if (args[i] == "--ask-install") {
				// Opens the install prompt without searching — ES2_GAME_PATH, the remembered install and the ES2 folder
				// above the executable are all passed over — and remembers nothing, neither the folder picked there nor one
				// the Settings menu switches to. For trying another install without losing the one remembered.
				options.AskForInstall = true;
			} else if (args[i] == "--hdd") {
				// Power up already panned down to the Heads-Down Display, for the same reason as --mfd: a
				// --screenshot run never sees a keystroke. An optional 0 or 1 picks which of its two screens
				// to land on — the command display or the damage detail, as [F7] and [F8] do live.
				staging.HeadsDown = true;
				if (HostArguments.TryReadOptionalInt(args, ref i, 0, 1, out int hddIndex)) {
					staging.HddPage = (HddPage)hddIndex;
				}
			} else if (args[i] == "--throttle") {
				// Power up with the throttle already open, for the same reason as --mfd and --hdd: a
				// --screenshot run never sees a keystroke, and a walking machine is the only way to see the
				// gait, the cockpit bob or the slider anywhere but its centre. ±1024 is full travel.
				if (HostArguments.TryReadInt(args, ref i, int.MinValue, int.MaxValue, errors, out int throttleSetting)) {
					staging.Throttle = (short)Math.Clamp(throttleSetting, -ThrottleTrack.Full, ThrottleTrack.Full);
				}
			} else if (args[i] == "--heading") {
				// Point the machine's lower body somewhere other than along the first leg of its route, which
				// is where the mission spawns it. A --screenshot run never sees a steering key, so this is the
				// only way to reach anything the body's own heading drives — the compass tape, and the
				// waypoint indicator's off-tape arrows. A binary angle: 0x4000 is a quarter turn.
				if (HostArguments.TryReadInt(args, ref i, int.MinValue, int.MaxValue, errors, out int headingAngle)) {
					staging.Heading = headingAngle;
				}
			} else if (args[i] == "--turret") {
				// Hold the two turret axes for the whole run, for the same reason as --throttle: a
				// --screenshot run never sees a keystroke, and the turret only moves while a key is held.
				// ±256 is full deflection on each.
				if (HostArguments.TryReadInt(args, ref i, int.MinValue, int.MaxValue, errors, out int twistAxis)
						&& HostArguments.TryReadInt(args, ref i, int.MinValue, int.MaxValue, errors, out int pitchAxis, "--turret")) {
					staging.HeldTwist = (short)Math.Clamp(twistAxis, -MechControls.AxisFull, MechControls.AxisFull);
					staging.HeldPitch = (short)Math.Clamp(pitchAxis, -MechControls.AxisFull, MechControls.AxisFull);
				}
			} else if (args[i] == "--weapon") {
				// Arm a weapon panel row at power-up, 1-based as the row prints it, and optionally link it.
				// Same reason as --mfd and --throttle: a --screenshot run never sees a keystroke, and the
				// armed row and a linked pair are only visible once something has selected one.
				if (HostArguments.TryReadInt(args, ref i, 1, 10, errors, out int weaponRow)) {
					staging.WeaponRow = weaponRow - 1;
				}
			} else if (args[i] == "--link") {
				staging.Link = true;
			} else if (args[i] == "--hit-shake") {
				// Take a hit on the cockpit, for the same reason as --fire: a --screenshot run never sees one
				// land, and the shake and its palette flash are gone again in under a second. The capture then
				// holds until the flash is actually up, so the frame photographed is a red one.
				staging.HitShake = true;
			} else if (args[i] == "--fire") {
				// Hold the trigger down for the whole run, for the same reason as --turret: a --screenshot
				// run never sees a keystroke, and a beam is only on screen for the tick after it was fired.
				staging.HeldFire = true;
			} else if (args[i] == "--impact") {
				// Hold the capture until an impact effect is carrying a light, for the same reason as
				// --fire: rounds land on their own once the trigger is held, but a light lasts about two
				// thirds of a second and a fixed frame count is as likely to photograph the gap between two
				// as one of them. Only useful alongside --fire and --screenshot.
				capture.WaitForEffectLight = true;
			} else if (args[i] == "--target") {
				// Acquire a target at power-up, for the same reason as --weapon and --mfd: a --screenshot run
				// never sees a keystroke, and the HUD's target box, the reticle's on-target frame and the F5
				// screen all have nothing to show until something is selected. It switches the scanner on
				// first, as [R] does, because a passive HERC can only see what it has eyes on.
				staging.AcquireTarget = true;
			} else if (args[i] == "--track") {
				// Power up with Automatic Turret Tracking latched, for the same reason as --target, which it
				// only does anything alongside: a --screenshot run never sees a keystroke, and the turret
				// only slews on its own once ATT has something to hold.
				staging.AutoTrack = true;
			} else if (args[i] == "--movie") {
				// Play one cutscene instead of a mission — see MovieHost. Takes a path, or a name to look up
				// in the install's AVI folder. It exists so a video decoder can be looked at rather than only
				// asserted about; see docs/retail/formats/avi-video.md.
				if (HostArguments.TryReadString(args, ref i, errors, out string movie)) {
					options.MoviePath = movie;
				}
			} else if (args[i] == "--mission") {
				// Fly one mission instead of the front end, which is what runs by default — the DBSIM.EXE turn alone,
				// for looking at the simulator without clicking through the shell to reach it. A named mission,
				// --play and --demo imply it.
				options.RunMission = true;
			} else if (args[i] == "--shell-palette") {
				// Which dpl\<name>.DPL the shell decodes its art through, pinned for the whole run. Without it
				// the palette follows the tab, as the original's does — see ShellPalette for the table and for
				// which screen picks which entry.
				if (HostArguments.TryReadString(args, ref i, errors, out string palette)) {
					shell.Palette = palette;
				}
				shell.Staged = true;
			} else if (args[i] == "--shell-tab") {
				// Which tab the shell comes up on, 0-7. The original always enters on the main menu; this is here
				// so --screenshot can land on a tab that has content, and so the save screen is one argument away
				// rather than a click away.
				if (HostArguments.TryReadInt(args, ref i, 0, ShellLayout.TabCount - 1, errors, out int requestedTab)) {
					shell.Tab = requestedTab;
				}
				shell.Staged = true;
			} else if (args[i] == "--shell-bay") {
				// Which hangar bay the repair tab opens on, 0-7 — DAT_00482ae5, which the squad roster moves once
				// the screen is up.
				if (HostArguments.TryReadInt(args, ref i, 0, ShellHangar.BayCount - 1, errors, out int requestedBay)) {
					shell.Bay = requestedBay;
				}
				shell.Staged = true;
			} else if (args[i] == "--shell-training") {
				// Run the front end in training mode rather than the mode prefs.cfg option 42 holds — DAT_0048260c,
				// the flag that gates REPAIR, BUILD and ARMORY off. PRACTICE MISSIONS sets it too, but only from the
				// main menu, where the strip hides nothing; this is how the gated strip is reachable on the other tabs.
				shell.Mode = ShellCampaignMode.Training;
				shell.Staged = true;
			} else if (args[i] == "--shell-practice") {
				// Come up on the practice screen, as the main menu's PRACTICE MISSIONS puts it up, so --screenshot
				// can land on it.
				shell.Practice = true;
				shell.Tab = ShellScreen.MainMenuTab;
				shell.Staged = true;
			} else if (args[i] == "--shell-windowed") {
				// Keep the front end windowed at startup whatever prefs.cfg option 6 says. This engine's own flag:
				// retail's -d reads as the same switch, and its store is overwritten before anything reads it.
				shell.Windowed = true;
				shell.Staged = true;
			} else if (args[i] == "--windowed") {
				// Keep every mission's window windowed at startup whatever prefs.cfg option 6 says -- --shell-windowed's
				// counterpart for the simulator. This engine's own flag, not retail's -Z0: see the startup toggle in
				// SimulatorStartup for what it does to the option's write-back.
				options.MissionWindowed = true;
			} else if (args[i] == "--shell-no-movies") {
				// Turn the shell's movies off — retail's -a, which clears Shell_MoviesEnabled (00482275) so the movie queue takes
				// nothing and plays nothing. See ShellMovieQueue.
				shell.Movies = false;
				shell.Staged = true;
			} else if (args[i] == "--cd-drive") {
				// Which drive the music CD is in. Retail asks MCI for the device type alone and takes whichever
				// CD drive it answers with -- nothing in either executable reads a drive letter from anywhere --
				// so this is the engine's own, for a machine with more than one drive. See CdAudio.Open.
				if (HostArguments.TryReadString(args, ref i, errors, out string drive)) {
					options.CdDrive = drive;
				}
			} else if (args[i] == "--music-dir") {
				// A directory of Track02.wav ... Track07.wav to play instead of the disc -- the engine's own,
				// for a machine with no drive. See WaveFileMusicSource.
				if (HostArguments.TryReadString(args, ref i, errors, out string musicFolder)) {
					options.MusicDirectory = musicFolder;
				}
			} else if (args[i] == "--music") {
				// DBSIM's own -R<n>: the mission's track is n % 5 + 2, so 0-4 pick tracks 2 to 6. Retail's
				// launcher passes its count of simulator launches here, as --shell's loop does, counting on from
				// this value; a lone mission without the switch plays track 2.
				if (HostArguments.TryReadInt(args, ref i, 0, int.MaxValue, errors, out int trackSelect)) {
					options.MusicTrackSelect = trackSelect;
				}
			} else if (args[i] == "--no-sound" || args[i] == "--silent") {
				// Skip the output device entirely. Same effect as running on a machine with no sound card:
				// the catalog, the director and the message port all still run, nothing is heard. For a
				// --screenshot run, an automated run, or anywhere the noise is not what is being looked at.
				options.SilentAudio = true;
			} else if (args[i] == "--external") {
				// Power up in the outside view, for the same reason as --mfd and --throttle: the player's own
				// machine is the one thing the cockpit view never shows, so a --screenshot run has no other way
				// to see its own legs move. It is [V] pressed at launch, so it lands two ticks in.
				staging.External = true;
			} else if (args[i] == "--hdd-damage") {
				// Which component category the damage screen powers up listing. [S], [I] and [W] switch it
				// live; this is the same reason --mfd and --hdd exist.
				if (HostArguments.TryReadInt(args, ref i, 0, 2, errors, out int damageIndex)) {
					staging.HddDamageView = (HddDamageView)damageIndex;
				}
			} else if (args[i] == "--hdd-subject") {
				// Whose herc the damage screen powers up inspecting, by selector slot. The arrows step it live;
				// this exists for the same reason --hdd-damage does.
				if (HostArguments.TryReadInt(args, ref i, 0, HddDamageSubject.SlotCount - 1, errors, out int subjectSlot)) {
					staging.HddSubject = subjectSlot;
				}
			} else if (args[i] == "--hdd-pilot") {
				// Which comm box the command display powers up with selected, and which order it powers up
				// armed. [1]-[3] and the order hotkeys do both live; these exist for the same reason
				// --hdd-damage does, and because the order list only leaves its unavailable blue once a pilot
				// is selected — a screenshot run has no other way to reach that.
				if (HostArguments.TryReadInt(args, ref i, 0, HddLayout.PilotSlotCount - 1, errors, out int pilotSlot)) {
					staging.HddPilot = pilotSlot;
				}
			} else if (args[i] == "--hdd-order") {
				if (HostArguments.TryReadInt(args, ref i, 0, HddLayout.OrderCount - 1, errors, out int orderIndex)) {
					staging.HddOrder = (HddOrder)orderIndex;
				}
			} else if (args[i] == "--flash-comm") {
				// Which FLASH COMM row the cursor sits on at power-up. The seven order letters do it live;
				// this exists for the same reason --mfd does.
				if (HostArguments.TryReadInt(args, ref i, 0, MfdFlashCommScreen.RowCount - 1, errors, out int flashCommRow)) {
					staging.FlashCommRow = flashCommRow;
				}
			} else if (args[i] == "--flash-comm-xmit") {
				// Presses XMIT on that row once the mission is up — the [X] key, or a click on the button, or
				// a second click on the row. A --screenshot run sees no keystroke, so this is the only way to
				// reach the squad broadcast and the reply that comes back from it.
				staging.FlashCommTransmit = true;
			} else if (args[i] == "--wait-transmission") {
				// Holds the capture until a squadmate's portrait is actually up rather than the static either
				// side of it, which is the one frame worth photographing. Only useful with --flash-comm-xmit.
				capture.WaitForTransmission = true;
			} else if (args[i] == "--hdd-xmit") {
				// Presses XMIT on the armed order once the screen is up, and reports what the squad did with
				// it. A --screenshot run sees no keystroke and no map click, so this is the only way to reach
				// the squadmate AI from the command line; an order that wants a pick takes the map centre.
				staging.HddTransmit = true;
			} else if (args[i] == "--play") {
				// DBSIM's own -p<name>: replay an input tape — a path, or a stem looked up in the install's
				// TAPES folder, as -p's own "tapes\demo1" is. The mission comes out of the tape, so it replaces
				// the positional mission argument. See InputTapePlayer.
				if (HostArguments.TryReadString(args, ref i, errors, out string tape)) {
					options.PlayTape = tape;
				}
			} else if (args[i] == "--record") {
				// DBSIM's own -r<name>: record this mission's input to <name>.tap, which --play replays. The
				// extension is forced, as -r forces it. See InputTapeRecorder.
				if (HostArguments.TryReadString(args, ref i, errors, out string tape)) {
					options.RecordTape = Path.ChangeExtension(tape, InputTapePlayer.Extension);
				}
			} else if (args[i] == "--developer") {
				// DBSIM's own -SPRUNKNOWN: the developer keys. See DeveloperKeys and docs/retail/key-bindings.md.
				options.DeveloperMode = true;
			} else if (args[i] == "--demo") {
				// DBSIM's own -D: a tape picked from TAPES\demolist.str, as VIEW DEMO plays one, which ends the
				// mission when it runs out or the moment a key is pressed. With --play, that tape instead.
				options.DemoTape = true;
			} else if (args[i] == "--install") {
				// Install from a retail disc folder or disc image into a new or empty folder, as the Settings menu's
				// install window does, then exit. See RetailInstaller.
				if (HostArguments.TryReadString(args, ref i, errors, out string source)
						&& HostArguments.TryReadString(args, ref i, errors, out string destination)) {
					options.InstallSource = source;
					options.InstallDestination = destination;
				}
			} else if (args[i] == "--install-size") {
				if (HostArguments.TryReadString(args, ref i, errors, out string size)) {
					if (Enum.TryParse(size, ignoreCase: true, out RetailInstaller.Size parsed) && Enum.IsDefined(parsed)) {
						options.InstallSize = parsed;
					} else {
						errors.Add($"--install-size needs minimum, medium or maximum, not '{size}'.");
					}
				}
			} else if (args[i] == "--install-language") {
				if (HostArguments.TryReadString(args, ref i, errors, out string language)) {
					if (Enum.TryParse(language, ignoreCase: true, out RetailInstaller.Language parsed) && Enum.IsDefined(parsed)) {
						options.InstallLanguage = parsed;
					} else {
						errors.Add($"--install-language needs english, french or german, not '{language}'.");
					}
				}
			} else if (args[i].StartsWith("--")) {
				errors.Add($"Unknown option {args[i]}.");
			} else {
				options._positional.Add(args[i]);
			}
		}

		if (options.MissionPath != null || options.PlayTape != null || options.DemoTape) {
			options.RunMission = true;
		}
		if (options.RunMission && shell.Staged) {
			errors.Add("The --shell-* options stage the front end, which does not run alongside --mission, a named mission, --play or --demo.");
		}
		if (options.RecordTape != null && (options.PlayTape != null || options.DemoTape)) {
			errors.Add("--record cannot be combined with --play or --demo.");
		}
		if (options.AskForInstall && options.ScreenshotPath != null && options._positional.Count == 0) {
			errors.Add("--ask-install needs someone to ask: name the install, or drop --screenshot.");
		}
		if (options._positional.Count > 2) {
			errors.Add($"Unexpected argument {options._positional[2]}: the only positional arguments are the install and the mission.");
		}

		return options;
	}
}

/// <summary>The front end's flags. Each <c>--shell-*</c> flag sets <see cref="Staged"/>.</summary>
sealed class ShellOptions {
	/// <summary>Whether any <c>--shell-*</c> flag was given, which a lone mission cannot honour.</summary>
	public bool Staged { get; set; }
	public string? Palette { get; set; }
	public ShellCampaignMode? Mode { get; set; }
	public int Tab { get; set; } = ShellScreen.MainMenuTab;
	public int Bay { get; set; }
	public bool Practice { get; set; }
	public bool Windowed { get; set; }
	public bool Movies { get; set; } = true;
}
