using Herculan.Engine.Cockpit;
using Herculan.Engine.Install;
using Herculan.Engine.Numerics;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Data.Struct.Herc;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// The simulator's load: everything <see cref="SimulatorStart"/> holds. <see cref="SimulatorStart.KeptWindowed"/>
/// is decided here.
/// </summary>
static class SimulatorStartup {
	/// <summary>
	/// Loads the mission from a shell launch, a demo tape or the command line. Null when there is nothing to
	/// run, having said why; the simulator's turn then ends with 1.
	/// </summary>
	public static SimulatorStart? Load(HostSession session, HostOptions options, ShellLaunch? shellLaunch, bool demoTape) {
		string installRoot = session.InstallRoot;

		// An input tape to replay, which brings its own mission. -p and -D unpack the tape's bundle over the
		// install's data\; this unpacks it over a copy of it instead, so a replay leaves the install alone.
		InputTapePlayer? tapePlayer = null;
		string? tapeScriptPath = null;
		if (options.PlayTape != null || demoTape) {
			string tapesDirectory = Path.Combine(installRoot, InputTapePlayer.TapesFolderName);
			string? stem = options.PlayTape ?? InputTapePlayer.PickDemo(tapesDirectory);
			string? tapePath = stem == null
				? null
				: new[] { stem, stem + InputTapePlayer.Extension, Path.Combine(tapesDirectory, stem + InputTapePlayer.Extension) }
					.FirstOrDefault(File.Exists);
			if (tapePath == null) {
				Console.Error.WriteLine(stem == null
					? $"No tape to play: {Path.Combine(tapesDirectory, InputTapePlayer.DemoListFileName)} is missing or empty."
					: $"No tape at {stem}, {stem}{InputTapePlayer.Extension} or in {tapesDirectory}.");
				return null;
			}

			tapePlayer = InputTapePlayer.Load(tapePath, demoTape);
			if (tapePlayer == null) {
				Console.Error.WriteLine($"{tapePath} is not an input tape.");
				return null;
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

		// A mission named by its .MSN is loaded as the shell would load it and flown from the handoff that writes
		// into the install's DATA\, as a shell launch is. See MissionFileLaunch.
		if (tapeScriptPath == null && shellLaunch == null && options.MissionPath is { } named && MissionFileLaunch.Names(named)) {
			if (MissionFileLaunch.Write(installRoot, named, out string? failure) is not { } generated) {
				Console.Error.WriteLine($"Cannot load {named}: {failure}");
				return null;
			}

			shellLaunch = new ShellLaunch(generated, Path.Combine(installRoot, MissionLoader.DataFolderName));
		}

		// The mission handoff VSHELL writes and DBSIM reads. It states its own zone and theater, so nothing
		// else here needs configuring.
		string scriptPath = tapeScriptPath
			?? shellLaunch?.ScriptPath
			?? options.MissionPath
			?? MissionLoader.DefaultScriptPath(installRoot);
		if (!File.Exists(scriptPath)) {
			Console.Error.WriteLine(
				$"No mission at {scriptPath}.\n" +
				$"Pass one as the second argument — {MissionLoader.ScriptFileName} from the install's " +
				$"{MissionLoader.DataFolderName} folder, or a mission's .MSN name.");
			return null;
		}

		Console.WriteLine($"HERCULAN Engine — loading {scriptPath} from {installRoot}");

		var disc = session.Disc;
		var content = GameContent.MountSimulator(installRoot, disc);
		Console.WriteLine($"Mounted archives: {string.Join(", ", content.MountedArchives)}");

		MissionScene scene;
		try {
			scene = MissionScene.Load(content, scriptPath, shellLaunch?.DataDirectory);
		} catch (MissingHandoffFileException missing) {
			ReportMissingFile(session, options, missing.FileName ?? missing.Message);
			return null;
		}

		var mission = scene.Mission;

		// Audio comes up against the same mounted archives and shares the simulation's generator, because
		// the variation roll draws on it exactly as weapon scatter does. It never throws: a machine with no
		// device gets a working GameAudio that happens to be silent.
		var ports = new MessagePorts(SystemMessages.Load(content));
		var audio = GameAudio.Create(content, ports, scene.World.PresentationRandom, silent: options.SilentAudio, cdDrive: options.CdDrive,
			musicDirectory: options.MusicDirectory, discImage: disc?.Image,
			soundCfg: SoundCfg.Load(GameInstall.SoundCfgPath(installRoot)));
		audio.Attach(scene.World);
		Console.WriteLine($"Audio: {audio.Status}");

		if (scene.UnmodelledCount > 0) {
			Console.Error.WriteLine(
				$"{scene.UnmodelledCount} placed objects have no model (missing install files or an out-of-range " +
				"index); they are simulated and positioned but not drawn.");
		}

		if (mission.Player == null) {
			Console.Error.WriteLine($"{MissionLoader.PlayerPathFor(scriptPath)} placed no player machine — camera starts at the "
				+ "first placed object.");
		}

		if (scene.Models.TerrainBank == null) {
			Console.Error.WriteLine($"Theater {mission.Header.TheaterIndex}'s terrain bank could not be loaded — "
				+ "drawing the terrain flat-shaded.");
		}

		var squadPlacements = scene.Objects
			.Where(o => o.Placement.IsPlayerLance && !ReferenceEquals(o.Object, scene.PlayerObject?.Object))
			.Take(SquadCommChannel.SlotCount)
			.ToList();

		// The install's own data\prefs.cfg — the same file the terrain draw distance is already read out of, and
		// the same folder the mission's script.dat came from. The same folder holds keyjoy.cfg, the four
		// axis-sense switches, and Herculan's own device map.
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
		simulatorPreferences.SaveEnabled = options.WritePreferences && tapePlayer == null;

		// Display Mode, prefs option 6 (docs/retail/simulation/preferences.md#the-video-mode-and-full-screen-bytes): WinMain
		// (00465288) takes the window to full screen through Video_ToggleFullscreen once it is created when the byte is
		// set, and Sim_Run writes the byte back at shutdown when the state differs from it. This engine's own two
		// exceptions keep the window: --windowed, and a --screenshot run, whose capture is the window's framebuffer
		// whatever the monitor. Retail has no switch that does this without also writing the option back, as -Z0 does,
		// so a run kept windowed by either that is still windowed when it ends writes nothing back; a full-screen
		// toggle made during it is written as retail writes it.
		bool keptWindowed = options.MissionWindowed || options.ScreenshotPath != null;
		bool startFullScreen = simulatorPreferences[Prefs.DisplayModeOption] != 0 && !keptWindowed;

		// -r writes its bundle before the mission starts, so the tape starts from the files as they are now,
		// before any panel writes its options back.
		InputTapeRecorder? tapeRecorder = null;
		if (options.RecordTape != null) {
			try {
				tapeRecorder = InputTapeRecorder.Create(options.RecordTape, scriptPath, dataDirectory);
			} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
				Console.Error.WriteLine($"Cannot record to {options.RecordTape}: {e.Message}");
				return null;
			}

			Console.WriteLine($"Recording input to {tapeRecorder.Path}.");
		}

		// A replay applies the original's per-tick steps once per frame, as the recording did — see
		// SimMath.PerTickStepsScaled.
		SimMath.PerTickStepsScaled = tapePlayer == null;
		// The two gates the original's own panel reads. SfxManager being null greys its first four rows, and
		// VoiceArchivePresent (0049e9cd) -- which Voice_ArchiveExists (00459d6c) sets by trying to fopen the localised
		// vol\simvoic?.vol under the install, not the disc -- is what lets the two message rows be stepped at all.
		bool soundAvailable = audio.Director != null;
		bool voiceAvailable = Directory.Exists(GameInstall.ArchiveDirectory(installRoot))
			&& Directory.EnumerateFiles(GameInstall.ArchiveDirectory(installRoot), ComputerVoice.VoiceFolder(content.Language) + ".VOL",
				new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Any();

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

		return new SimulatorStart {
			InstallRoot = installRoot,
			Disc = disc,
			Content = content,
			Scene = scene,
			Audio = audio,
			Ports = ports,
			Preferences = simulatorPreferences,
			ScriptPath = scriptPath,
			DataDirectory = dataDirectory,
			ShellLaunch = shellLaunch,
			TapePlayer = tapePlayer,
			TapeRecorder = tapeRecorder,
			DemoTape = demoTape,
			KeptWindowed = keptWindowed,
			StartFullScreen = startFullScreen,
			SoundAvailable = soundAvailable,
			VoiceAvailable = voiceAvailable,
			SquadPlacements = squadPlacements,
			PilotingRazor = scene.PlayerObject is { Placement.TypeName: { } playerTypeName }
				&& HercLUT.GetByAbbrev(playerTypeName)?.Id == ControlsPanel.RazorTypeIndex,
		};
	}

	/// <summary>
	/// A handoff file the load cannot do without is missing, which in the original is an assert's error box and an
	/// exit with code 1 (docs/retail/formats/script-dat.md#call-chain--confirmed). This shows <c>mission_load</c>'s message in the platform's own box
	/// (<see cref="NativeAlert"/>), and the turn then ends with 1, which no launcher state brings the shell back
	/// from. The wording is this engine's own. A <c>--screenshot</c> run has nobody to close a box, so it only
	/// prints.
	/// </summary>
	private static void ReportMissingFile(HostSession session, HostOptions options, string path) {
		var localization = session.Localization;
		string message = string.Format(localization.GetStringOrKey("mission_load.missing_file"), path);
		if (options.ScreenshotPath != null) {
			Console.Error.WriteLine(message);
			return;
		}

		NativeAlert.ShowError(localization.GetStringOrKey("mission_load.window_title"), message);
	}
}
