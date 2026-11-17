using Herculan.Engine.Games;
using Herculan.Engine.Input;
using Herculan.Es2.Cockpit;
using Herculan.Es2.Install;
using Herculan.Es2.Shell;
using Herculan.Es2.Sim;

namespace Herculan.Es2.Host;

/// <summary>
/// Earthsiege 2's own command-line flags (<see cref="IGameModule.TryParseOption"/>), parsed into <see cref="Es2Options"/>.
/// docs/herculan/herculan-command-line.md describes every flag. Each comment below says why the flag exists, which is
/// what that page links back to.
/// </summary>
static class Es2OptionParser {
	/// <summary>Takes the flag at <c>args[i]</c> when it is one of Earthsiege 2's (<see cref="IGameModule.TryParseOption"/>).</summary>
	public static bool TryParse(Es2Options options, string[] args, ref int i, List<string> errors) {
		string flag = args[i];
		if (!TryParseFlag(options, args, ref i, errors)) {
			return false;
		}

		options.FirstOwnOption ??= flag;
		return true;
	}

	/// <summary>What the flags cannot do together, once parsing ends (<see cref="IGameModule.CheckOptions"/>).</summary>
	public static void Check(Es2Options options, CommonOptions common, List<string> errors) {
		if (common.RunMission && options.Shell.Staged) {
			errors.Add("The --shell-* options stage the front end, which does not run alongside --mission, a named mission, --play or --demo.");
		}
	}

	private static bool TryParseFlag(Es2Options options, string[] args, ref int i, List<string> errors) {
		var staging = options.StagedStart;
		var capture = options.StagedScreenshot;
		var shell = options.Shell;
		switch (args[i]) {
			case "--mfd":
				// Which MFD screen to power up on. F1-F6 switch it live; this exists so a --screenshot run,
				// which never sees a keystroke, can be pointed at a specific screen.
				if (OptionValues.TryReadInt(args, ref i, 0, 5, errors, out int mfdIndex)) {
					staging.Mfd = (MfdMode)mfdIndex;
				}
				return true;
			case "--quit":
				// Power up with the [Q] mission-status alert already raised, for the same reason as
				// --objectives. With no argument it shows whatever the mission evaluates to at that moment,
				// which is what pressing [Q] would give; an optional status number forces one of the twenty
				// GNL_ALRT.STR rows instead, so the one-button and single-line layouts — and 0 and 1, the
				// pause panel's own two — can be looked at without arranging a mission that produces them.
				staging.StatusAlert = true;
				if (OptionValues.TryReadOptionalInt(args, ref i, 0, StatusAlertPanel.StatusCount - 1, out int forcedStatus)) {
					staging.StatusAlertStatus = forcedStatus;
				}
				return true;
			case "--objectives":
				// Power up with the [F11] objectives panel already open, for the same reason as --mfd: a
				// --screenshot run never sees a keystroke.
				staging.Objectives = true;
				return true;
			case "--preferences":
				// Likewise for the [F12] preferences panel.
				staging.Preferences = true;
				return true;
			case "--controls":
				// And for the CONTROLS panel it raises, which opens over it.
				staging.Preferences = true;
				staging.Controls = true;
				return true;
			case "--joystick":
				// Pretends a fully-featured stick is attached, so the CONTROLS panel's rows go live and show
				// what prefs.cfg has them bound to even where there is no hardware — a --screenshot run has
				// none. A real device, when one is attached, overrides this. An optional count sets how many of
				// the eight button rows are live.
				staging.Joystick = new JoystickCapabilities(Present: true,
					ButtonCount: JoystickCapabilities.MaxButtons,
					HasThrottle: true, HasRudder: true, HasHat: true);
				if (OptionValues.TryReadOptionalInt(args, ref i, 0, JoystickCapabilities.MaxButtons, out int stickButtons)) {
					staging.Joystick = staging.Joystick with { ButtonCount = stickButtons };
				}
				return true;
			case "--joystick-probe":
				// Prints each axis and button of the attached stick as it moves. Its whole purpose is filling
				// in the player's herculan-joystick.cfg by hand: a modern stick's axis order is its own, and nothing
				// but moving each control in turn will say which index is the throttle.
				options.ProbeJoystick = true;
				return true;
			case "--write-joystick-map":
				// Writes the map the engine is actually using out to the player's herculan-joystick.cfg, comments and
				// all, so there is something to edit rather than a file to compose from scratch. With no file
				// there already, that is the device's own reported shape in retail's X/Y/Z/R order.
				options.WriteJoystickMap = true;
				return true;
			case "--no-write-prefs":
				// Leaves data\prefs.cfg alone. Saving is on by default, as it is in the original: each panel
				// writes its own options back as it closes. This is the way out for anyone who would rather
				// their retail install were not touched at all.
				options.WritePreferences = false;
				return true;
			case "--hdd":
				// Power up already panned down to the Heads-Down Display, for the same reason as --mfd: a
				// --screenshot run never sees a keystroke. An optional 0 or 1 picks which of its two screens
				// to land on — the command display or the damage detail, as [F7] and [F8] do live.
				staging.HeadsDown = true;
				if (OptionValues.TryReadOptionalInt(args, ref i, 0, 1, out int hddIndex)) {
					staging.HddPage = (HddPage)hddIndex;
				}
				return true;
			case "--throttle":
				// Power up with the throttle already open, for the same reason as --mfd and --hdd: a
				// --screenshot run never sees a keystroke, and a walking machine is the only way to see the
				// gait, the cockpit bob or the slider anywhere but its centre. ±1024 is full travel.
				if (OptionValues.TryReadInt(args, ref i, int.MinValue, int.MaxValue, errors, out int throttleSetting)) {
					staging.Throttle = (short)Math.Clamp(throttleSetting, -ThrottleTrack.Full, ThrottleTrack.Full);
				}
				return true;
			case "--heading":
				// Point the machine's lower body somewhere other than along the first leg of its route, which
				// is where the mission spawns it. A --screenshot run never sees a steering key, so this is the
				// only way to reach anything the body's own heading drives — the compass tape, and the
				// waypoint indicator's off-tape arrows. A binary angle: 0x4000 is a quarter turn.
				if (OptionValues.TryReadInt(args, ref i, int.MinValue, int.MaxValue, errors, out int headingAngle)) {
					staging.Heading = headingAngle;
				}
				return true;
			case "--turret":
				// Hold the two turret axes for the whole run, for the same reason as --throttle: a
				// --screenshot run never sees a keystroke, and the turret only moves while a key is held.
				// ±256 is full deflection on each.
				if (OptionValues.TryReadInt(args, ref i, int.MinValue, int.MaxValue, errors, out int twistAxis)
						&& OptionValues.TryReadInt(args, ref i, int.MinValue, int.MaxValue, errors, out int pitchAxis, "--turret")) {
					staging.HeldTwist = (short)Math.Clamp(twistAxis, -MechControls.AxisFull, MechControls.AxisFull);
					staging.HeldPitch = (short)Math.Clamp(pitchAxis, -MechControls.AxisFull, MechControls.AxisFull);
				}
				return true;
			case "--weapon":
				// Arm a weapon panel row at power-up, 1-based as the row prints it, and optionally link it.
				// Same reason as --mfd and --throttle: a --screenshot run never sees a keystroke, and the
				// armed row and a linked pair are only visible once something has selected one.
				if (OptionValues.TryReadInt(args, ref i, 1, 10, errors, out int weaponRow)) {
					staging.WeaponRow = weaponRow - 1;
				}
				return true;
			case "--link":
				staging.Link = true;
				return true;
			case "--hit-shake":
				// Take a hit on the cockpit, for the same reason as --fire: a --screenshot run never sees one
				// land, and the shake and its palette flash are gone again in under a second. The capture then
				// holds until the flash is actually up, so the frame photographed is a red one.
				staging.HitShake = true;
				return true;
			case "--fire":
				// Hold the trigger down for the whole run, for the same reason as --turret: a --screenshot
				// run never sees a keystroke, and a beam is only on screen for the tick after it was fired.
				staging.HeldFire = true;
				return true;
			case "--impact":
				// Hold the capture until an impact effect is carrying a light, for the same reason as
				// --fire: rounds land on their own once the trigger is held, but a light lasts about two
				// thirds of a second and a fixed frame count is as likely to photograph the gap between two
				// as one of them. Only useful alongside --fire and --screenshot.
				capture.WaitForEffectLight = true;
				return true;
			case "--target":
				// Acquire a target at power-up, for the same reason as --weapon and --mfd: a --screenshot run
				// never sees a keystroke, and the HUD's target box, the reticle's on-target frame and the F5
				// screen all have nothing to show until something is selected. It switches the scanner on
				// first, as [R] does, because a passive HERC can only see what it has eyes on.
				staging.AcquireTarget = true;
				return true;
			case "--track":
				// Power up with Automatic Turret Tracking latched, for the same reason as --target, which it
				// only does anything alongside: a --screenshot run never sees a keystroke, and the turret
				// only slews on its own once ATT has something to hold.
				staging.AutoTrack = true;
				return true;
			case "--movie":
				// Play one cutscene instead of a mission — see MovieHost. Takes a path, or a name to look up
				// in the install's AVI folder. It exists so a video decoder can be looked at rather than only
				// asserted about; see docs/retail/formats/avi-video.md.
				if (OptionValues.TryReadString(args, ref i, errors, out string movie)) {
					options.MoviePath = movie;
				}
				return true;
			case "--own-world":
				// Play a practice, demo or user-made mission named by its .MSN on its own theater and time of day, where the
				// training load writes theater 1 and the practice screen's time of day over them. The mission editor's Play
				// passes it, so the game plays what the editor showed. This engine's own flag.
				options.OwnWorld = true;
				return true;
			case "--shell-palette":
				// Which dpl\<name>.DPL the shell decodes its art through, pinned for the whole run. Without it
				// the palette follows the tab, as the original's does — see ShellPalette for the table and for
				// which screen picks which entry.
				if (OptionValues.TryReadString(args, ref i, errors, out string palette)) {
					shell.Palette = palette;
				}
				shell.Staged = true;
				return true;
			case "--shell-tab":
				// Which tab the shell comes up on, 0-7. The original always enters on the main menu; this is here
				// so --screenshot can land on a tab that has content, and so the save screen is one argument away
				// rather than a click away.
				if (OptionValues.TryReadInt(args, ref i, 0, ShellLayout.TabCount - 1, errors, out int requestedTab)) {
					shell.Tab = requestedTab;
				}
				shell.Staged = true;
				return true;
			case "--shell-bay":
				// Which hangar bay the repair tab opens on, 0-7 — DAT_00482ae5, which the squad roster moves once
				// the screen is up.
				if (OptionValues.TryReadInt(args, ref i, 0, ShellHangar.BayCount - 1, errors, out int requestedBay)) {
					shell.Bay = requestedBay;
				}
				shell.Staged = true;
				return true;
			case "--shell-practice":
				// Come up on the practice screen, as the main menu's PRACTICE MISSIONS puts it up, so --screenshot
				// can land on it.
				shell.Practice = true;
				shell.Tab = ShellScreen.MainMenuTab;
				shell.Staged = true;
				return true;
			case "--shell-windowed":
				// Keep the front end windowed at startup whatever prefs.cfg option 6 says. This engine's own flag:
				// retail's -d reads as the same switch, and its store is overwritten before anything reads it.
				shell.Windowed = true;
				shell.Staged = true;
				return true;
			case "--windowed":
				// Keep every mission's window windowed at startup whatever prefs.cfg option 6 says -- --shell-windowed's
				// counterpart for the simulator. This engine's own flag, not retail's -Z0: see the startup toggle in
				// SimulatorStartup for what it does to the option's write-back.
				options.MissionWindowed = true;
				return true;
			case "--shell-no-movies":
				// Turn the shell's movies off — retail's -a, which clears Shell_MoviesEnabled (00482275) so the movie queue takes
				// nothing and plays nothing. See ShellMovieQueue.
				shell.Movies = false;
				shell.Staged = true;
				return true;
			case "--cd-drive":
				// Which drive the music CD is in. Retail asks MCI for the device type alone and takes whichever
				// CD drive it answers with -- nothing in either executable reads a drive letter from anywhere --
				// so this is the engine's own, for a machine with more than one drive. See CdAudio.Open.
				if (OptionValues.TryReadString(args, ref i, errors, out string drive)) {
					options.CdDrive = drive;
				}
				return true;
			case "--music-dir":
				// A directory of Track02.wav ... Track07.wav to play instead of the disc -- the engine's own,
				// for a machine with no drive. See WaveFileMusicSource.
				if (OptionValues.TryReadString(args, ref i, errors, out string musicFolder)) {
					options.MusicDirectory = musicFolder;
				}
				return true;
			case "--music":
				// DBSIM's own -R<n>: the mission's track is n % 5 + 2, so 0-4 pick tracks 2 to 6. Retail's
				// launcher passes its count of simulator launches here, as --shell's loop does, counting on from
				// this value; a lone mission without the switch plays track 2.
				if (OptionValues.TryReadInt(args, ref i, 0, int.MaxValue, errors, out int trackSelect)) {
					options.MusicTrackSelect = trackSelect;
				}
				return true;
			case "--external":
				// Power up in the outside view, for the same reason as --mfd and --throttle: the player's own
				// machine is the one thing the cockpit view never shows, so a --screenshot run has no other way
				// to see its own legs move. It is [V] pressed at launch, so it lands two ticks in.
				staging.External = true;
				return true;
			case "--hdd-damage":
				// Which component category the damage screen powers up listing. [S], [I] and [W] switch it
				// live; this is the same reason --mfd and --hdd exist.
				if (OptionValues.TryReadInt(args, ref i, 0, 2, errors, out int damageIndex)) {
					staging.HddDamageView = (HddDamageView)damageIndex;
				}
				return true;
			case "--hdd-subject":
				// Whose herc the damage screen powers up inspecting, by selector slot. The arrows step it live;
				// this exists for the same reason --hdd-damage does.
				if (OptionValues.TryReadInt(args, ref i, 0, HddDamageSubject.SlotCount - 1, errors, out int subjectSlot)) {
					staging.HddSubject = subjectSlot;
				}
				return true;
			case "--hdd-pilot":
				// Which comm box the command display powers up with selected, and which order it powers up
				// armed. [1]-[3] and the order hotkeys do both live; these exist for the same reason
				// --hdd-damage does, and because the order list only leaves its unavailable blue once a pilot
				// is selected — a screenshot run has no other way to reach that.
				if (OptionValues.TryReadInt(args, ref i, 0, HddLayout.PilotSlotCount - 1, errors, out int pilotSlot)) {
					staging.HddPilot = pilotSlot;
				}
				return true;
			case "--hdd-order":
				if (OptionValues.TryReadInt(args, ref i, 0, HddLayout.OrderCount - 1, errors, out int orderIndex)) {
					staging.HddOrder = (HddOrder)orderIndex;
				}
				return true;
			case "--flash-comm":
				// Which FLASH COMM row the cursor sits on at power-up. The seven order letters do it live;
				// this exists for the same reason --mfd does.
				if (OptionValues.TryReadInt(args, ref i, 0, MfdFlashCommScreen.RowCount - 1, errors, out int flashCommRow)) {
					staging.FlashCommRow = flashCommRow;
				}
				return true;
			case "--flash-comm-xmit":
				// Presses XMIT on that row once the mission is up — the [X] key, or a click on the button, or
				// a second click on the row. A --screenshot run sees no keystroke, so this is the only way to
				// reach the squad broadcast and the reply that comes back from it.
				staging.FlashCommTransmit = true;
				return true;
			case "--wait-transmission":
				// Holds the capture until a squadmate's portrait is actually up rather than the static either
				// side of it, which is the one frame worth photographing. Only useful with --flash-comm-xmit.
				capture.WaitForTransmission = true;
				return true;
			case "--hdd-xmit":
				// Presses XMIT on the armed order once the screen is up, and reports what the squad did with
				// it. A --screenshot run sees no keystroke and no map click, so this is the only way to reach
				// the squadmate AI from the command line; an order that wants a pick takes the map centre.
				staging.HddTransmit = true;
				return true;
			case "--developer":
				// DBSIM's own -SPRUNKNOWN: the developer keys. See DeveloperKeys and docs/retail/key-bindings.md.
				options.DeveloperMode = true;
				return true;
			case "--install":
				// Install from a retail disc folder or disc image into a new or empty folder, as the Settings menu's
				// install window does, then exit. See RetailInstaller.
				if (OptionValues.TryReadString(args, ref i, errors, out string source)
						&& OptionValues.TryReadString(args, ref i, errors, out string destination)) {
					options.InstallSource = source;
					options.InstallDestination = destination;
				}
				return true;
			case "--install-size":
				if (OptionValues.TryReadString(args, ref i, errors, out string size)) {
					if (Enum.TryParse(size, ignoreCase: true, out RetailInstaller.Size parsed) && Enum.IsDefined(parsed)) {
						options.InstallSize = parsed;
					} else {
						errors.Add($"--install-size needs minimum, medium or maximum, not '{size}'.");
					}
				}
				return true;
			case "--install-language":
				if (OptionValues.TryReadString(args, ref i, errors, out string language)) {
					if (Enum.TryParse(language, ignoreCase: true, out RetailInstaller.Language parsed) && Enum.IsDefined(parsed)) {
						options.InstallLanguage = parsed;
					} else {
						errors.Add($"--install-language needs english, french or german, not '{language}'.");
					}
				}
				return true;
			case "--install-disc-files":
				// Also copy what retail always reads from the disc, as the install window's box does. See RetailInstaller.Plan.
				options.InstallDiscFiles = true;
				return true;
			default:
				return false;
		}
	}

	/// <summary>Earthsiege 2's section of <c>--help</c> (<see cref="IGameModule.Usage"/>).</summary>
	public const string Usage = """
		Earthsiege 2 (an install of it refuses Metaltech: Earthsiege's options)
		  --movie <name>              one cutscene, by path or AVI folder name
		  --own-world                 a named .MSN mission plays its own theater and time of day

		  Front end (none combines with --mission)
		  --shell-tab <0-7>  --shell-bay <0-7>  --shell-practice
		  --shell-windowed  --shell-no-movies
		  --shell-palette <name>

		  Music
		  --music <n>                 CD track n % 5 + 2
		  --cd-drive <drive>  --music-dir <dir>

		  Settings and input devices
		  --windowed  --no-write-prefs  --joystick [0-8]  --joystick-probe  --write-joystick-map

		  Installing
		  --install <disc> <folder>   install from a disc folder or disc image (.iso, .bin, .cue), then exit
		  --install-size minimum|medium|maximum         default maximum
		  --install-language english|french|german      default english
		  --install-disc-files        also copy the movies, instructor clips and manual

		  Developer
		  --developer                 the developer keys

		  Staged state (with --screenshot, or in an interactive run)
		  --mfd <0-5>  --hdd [0|1]  --hdd-damage <0-2>  --hdd-subject <0-4>  --external
		  --objectives  --quit [0-19]  --preferences  --controls  --hit-shake
		  --throttle <n>  --heading <n>  --turret <twist> <pitch>  --track  --target
		  --weapon <1-10>  --link  --fire  --impact
		  --hdd-pilot <0-2>  --hdd-order <0-7>  --hdd-xmit
		  --flash-comm <0-5>  --flash-comm-xmit  --wait-transmission
		""";
}
