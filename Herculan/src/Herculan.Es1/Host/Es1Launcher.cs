using HercWorks.Core.Io;
using Herculan.Engine.Platform;
using Herculan.Es1.Shell;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Host;

/// <summary>
/// ES1's launcher, <c>ESCD.EXE</c>'s <c>Launcher_Main</c> (<c>000005cc</c>) in <c>ES1ESCD</c> (docs/es1/shell/launcher.md):
/// the start-up's checks and drive prefixes, then the shell (<see cref="Es1ShellHost"/>) and the simulator
/// (<see cref="Es1ZoneViewer"/>) one after the other, each by the number the program before left in <c>data\exit.cfg</c>,
/// until that number is 0. The programs' exit statuses are not read, as retail keeps none of <c>spawnv</c>'s answers.
///
/// <para>Retail spawns each program as a DOS executable; here each is a turn in one <see cref="HostWindow"/> the launcher
/// opens once its start-up checks pass, which shows black between turns (<see cref="HostWindow.Hold"/>). The DOS current
/// folder a prefix-less path is relative to is the install folder here.</para>
///
/// <para><b>Engine guard:</b> a window closed by the player, or a termination signal (<see cref="TerminationSignal"/>),
/// has no DOS counterpart: it ends the loop after the program it closed, whatever <c>data\exit.cfg</c> then holds, as
/// ES2's launcher does. The shell's close writes the code it started with (the class summary of
/// <see cref="Es1ShellHost"/>), which would otherwise run the shell or a mission again. A <c>--screenshot</c> run, which
/// has nobody to close the window, ends the loop after the first shell turn, another engine guard.</para>
/// </summary>
public static class Es1Launcher {
	/// <summary>The status <c>Assert_Report</c>'s fatal path ends the program with, <c>__exit(1)</c> (docs/es1/simulation/asserts.md#severities).</summary>
	public const int AssertExitStatus = 1;

	/// <summary><c>Launcher_State</c> (<c>00005854</c>) as the image holds it, so the first pass is state 1.</summary>
	private const int InitialState = 1;

	/// <summary>The free bytes <c>Launcher_HasDiskSpace</c> is asked for.</summary>
	private const long DiskSpaceNeeded = 40_000;

	private const string AltDriveVariable = "ALT_DRIVE", PreferAltVariable = "PREFER_ALT";
	private const string ExitPath = @"data\exit.cfg", PrefsPath = @"data\prefs.cfg", DetectProgram = "detect.exe";
	private const string FlicPlayer = "playflic.exe", IntroFlic = @"flic\intro3.dfl", EndFlic = @"flic\end.dfl";
	private static readonly string[] FlicSwitches = ["-B1000000", "-S83"];

	/// <summary>The switches <c>Launcher_BuildArgs</c> (<c>00000518</c>) adds.</summary>
	private const string IntroSwitch = "-a", AltDriveSwitch = "-c", DemoSwitch = "-D", Eggplant = "-eggplant";

	/// <summary>
	/// Runs the launcher on <paramref name="installRoot"/> with <paramref name="arguments"/>, its own command line after
	/// the program's path, which it hands on whole to each program (<see cref="BuildArgs"/>). Returns 0 when the loop
	/// ends, as it does when a start-up check refuses (<c>exit</c>(0)), and <see cref="AssertExitStatus"/> after a fatal
	/// assert or the fault of a <c>PREFER_ALT</c> without <c>ALT_DRIVE</c>. Retail's <c>Launcher_Main</c> returns no value.
	/// </summary>
	/// <param name="imguiFontPath">The faces the shell's ImGui draws in (<see cref="Es1ShellHost.Run"/>).</param>
	/// <param name="screenshotPath"><c>--screenshot</c>'s file, which the first shell turn captures; none for a play.</param>
	/// <param name="silent">The host's <c>--no-sound</c>: the shell's and the simulator's turns open no audio device.</param>
	public static int Run(string installRoot, IReadOnlyList<string> arguments, string imguiFontPath, string imguiItalicFontPath,
			string? screenshotPath, bool silent = false) {
		// 1. Mem_New(argc * 4 + 8), its answer not kept: no counterpart.
		// 2. The memory check, X32_LargestFreeBlock() - 25,000 against 3,435,000: the extender's largest free block has no
		// counterpart, so the check always passes, an engine choice, and its figure is not computed.

		// 3. The drive prefixes. With PREFER_ALT set and ALT_DRIVE not, retail's copy reads from address 0 and faults
		// (docs/es1/shell/launcher.md#starting--launcher_main-000005cc); the fault ends retail's, so it is reported and ends
		// this one. Assert_SetLogDir: this engine keeps no assert log.
		string? altDrive = Environment.GetEnvironmentVariable(AltDriveVariable);
		bool preferAlt = Environment.GetEnvironmentVariable(PreferAltVariable) != null;
		if (preferAlt && altDrive == null) {
			Console.Error.WriteLine($"{PreferAltVariable} is set and {AltDriveVariable} is not: ESCD.EXE reads its drive prefix from address 0 here, which faults.");
			return AssertExitStatus;
		}

		var prefixes = new Prefixes(installRoot, preferAlt ? altDrive! : "",
			preferAlt && altDrive != null ? "" : altDrive ?? "");

		// 4. The disk check.
		switch (HasDiskSpace(DiskSpaceNeeded, prefixes)) {
			case null:
				Console.Error.WriteLine($"{AltDriveVariable}'s drive {prefixes.Preferred} is not a letter (src\\es.cpp line 0x4f asserts here).");
				return AssertExitStatus;
			case false:
				Console.WriteLine("Insufficient disk space.");
				Console.WriteLine("You need at least 40,000 bytes of hard drive space to play EarthSiege.");
				return 0;
		}

		// 5. PLACEHOLDER: data\es.got, which holds step 2's free-memory figure and which no reader is known of
		// (launcher.md's Open), is not written: step 2's figure has no counterpart.
		// 6. Mem_InitHeap(20,000): this engine has no fixed heap.
		// 7. Launcher_SaveVideoMode, the BIOS's equipment byte and video mode, and at the end Launcher_RestoreVideoMode: this
		// engine sets no DOS video mode, so there is nothing to save or restore.

		// 8. The flics' command lines, only with ALT_DRIVE set; 9. the flic player looked for (docs/es1/shell/launcher.md#the-flics).
		Flics? flics = altDrive != null ? new Flics(prefixes) : null;
		bool flicPlayerFound = FileReadable(flics?.Probe);

		using var window = new HostWindow();
		int state = InitialState;
		bool firstShell = true;
		while (state != 0) {
			switch (state) {
				case 1:
					// data\prefs.cfg opened rb to see that it is there. PLACEHOLDER: DETECT.EXE, which retail runs when it is
					// not, is not ported; the shell then loads every option as 0 (Es1Preferences.FromFile of no file).
					if (!Opens(prefixes.Path(PrefsPath))) {
						string detect = flicPlayerFound ? prefixes.OtherPath(DetectProgram) : prefixes.Path(DetectProgram);
						Console.Error.WriteLine($"PLACEHOLDER: data\\prefs.cfg is missing and {detect}{(flicPlayerFound ? " " + AltDriveSwitch : "")}, which ESCD.EXE runs then, is not ported.");
					}

					if (flicPlayerFound) {
						flics!.Play(flics.Intro);
					}

					goto case 3;
				case 3:
				case 6:
					flicPlayerFound = FileReadable(flics?.Probe);
					RunShell(installRoot, BuildArgs(arguments, state, flicPlayerFound), imguiFontPath, imguiItalicFontPath,
						firstShell ? screenshotPath : null, window, silent);
					firstShell = false;
					break;
				case 4:
					flicPlayerFound = FileReadable(flics?.Probe);
					if (flicPlayerFound) {
						flics!.Play(flics.End);
					}

					RunShell(installRoot, BuildArgs(arguments, state, flicPlayerFound), imguiFontPath, imguiItalicFontPath,
						firstShell ? screenshotPath : null, window, silent);
					firstShell = false;
					break;
				case 2:
				case 5:
					flicPlayerFound = FileReadable(flics?.Probe);
					RunSimulator(installRoot, BuildArgs(arguments, state, flicPlayerFound), window, silent);
					break;
				default:
					// Any other state runs nothing, and the file is read again unchanged, so retail goes on reading it. The
					// window's events are handled meanwhile, an engine guard, so that its close can end the loop.
					window.PumpEvents();
					break;
			}

			// The engine guards of the class summary.
			if (window.Closed || TerminationSignal.Requested || (screenshotPath != null && !firstShell)) {
				return 0;
			}

			if (ReadExitCfg(prefixes, state) is not { } next) {
				Console.Error.WriteLine($"{prefixes.Path(ExitPath)} does not open (src\\es.cpp line 0x67 asserts here).");
				return AssertExitStatus;
			}

			state = next;
		}

		// The end: Launcher_RestoreVideoMode (step 7) and Mem_HeapOff.
		return 0;
	}

	/// <summary>A turn of the shell, <c>spawnv</c>(0, <c>go.exe</c>, ...), its status not kept.</summary>
	private static void RunShell(string installRoot, IReadOnlyList<string> arguments, string imguiFontPath,
			string imguiItalicFontPath, string? screenshotPath, HostWindow window, bool silent) {
		Es1ShellHost.Run(installRoot, arguments, imguiFontPath, imguiItalicFontPath, screenshotPath, window, silent);
		window.Hold(null);
	}

	/// <summary>
	/// A turn of the simulator on the install's <c>data\</c> files, <c>spawnv</c>(0, <c>dbsim.exe</c>, ...), its status not
	/// kept: <paramref name="arguments"/> read as <c>Sim_ParseCommandLine</c> (<c>00058e74</c>) reads them
	/// (docs/es1/simulation/main-loop.md#the-command-line), each switch looked up by the character after its <c>-</c>,
	/// case-sensitive, a character not in its table passed over, and handed to <see cref="Es1ZoneViewer.Run"/> as far as
	/// it takes them. <c>-D</c> is its demo; <c>-p</c> and <c>-r</c> its tape to play and to record, each name taken only
	/// when none was given before, and <c>-D</c>'s pick replacing any; <c>-c</c> (<c>Sim_PreParseCommandLine</c>) lets it
	/// honor <c>ALT_DRIVE</c>, which the install's paths stand in for here; <c>-eggplant</c> is what step 6's refusal
	/// tests, which <see cref="Es1ZoneViewer"/> does not make; and each <c>-s</c> toggles <c>000b5b68</c> from 1, which
	/// clear makes no sound manager.
	/// </summary>
	private static void RunSimulator(string installRoot, IReadOnlyList<string> arguments, HostWindow window, bool silent) {
		bool demo = false, nameGiven = false, soundOn = true;
		string? playTape = null, recordTape = null;
		var notHanded = new List<string>();
		foreach (string argument in arguments) {
			if (argument.Length < 2 || argument[0] != '-') {
				continue;
			}

			switch (argument[1]) {
				case 'D':
					demo = true;
					nameGiven = true;
					playTape = null;
					break;
				case 'p':
					if (!nameGiven) {
						playTape = argument[2..];
						nameGiven = true;
					}

					break;
				case 'r':
					if (!nameGiven) {
						recordTape = argument[2..];
						nameGiven = true;
					}

					break;
				case 's':
					soundOn = !soundOn;
					break;
				case 'B' or 'S' or 'm' or 'C' or 'P' or 't' or 'V' or 'W' or 'T' or 'd' or 'l':
					notHanded.Add(argument);
					break;
			}
		}

		if (notHanded.Count > 0) {
			// PLACEHOLDER: the simulator takes none of DBSIM.EXE's other switches yet.
			Console.Error.WriteLine($"PLACEHOLDER: DBSIM.EXE's {string.Join(" ", notHanded)} not handed to the simulator.");
		}

		Es1ZoneViewer.Run(installRoot, null, new Es1HeaderOverrides(null, null, null), playTape, demo, recordTape, window,
			soundOn, silent);
		window.Hold(null);
	}

	/// <summary>
	/// <c>Launcher_BuildArgs</c> (<c>00000518</c>): the launcher's own <paramref name="arguments"/>, then by
	/// <paramref name="state"/> <c>-a</c> and <c>-c</c> for the shell's states and <c>-c</c> for the simulator's with the
	/// flic player found, <c>-D</c> first for state 5, and last <c>-eggplant</c>. Retail's vector opens with the
	/// launcher's <c>argv[0]</c>, which neither program reads; both hosts take the arguments after it.
	/// </summary>
	private static List<string> BuildArgs(IReadOnlyList<string> arguments, int state, bool flicPlayerFound) {
		var vector = new List<string>(arguments);
		switch (state) {
			case 1 or 3 or 4 or 6:
				if (flicPlayerFound) {
					vector.Add(IntroSwitch);
					vector.Add(AltDriveSwitch);
				}

				break;
			case 2 or 5:
				if (state == 5) {
					vector.Add(DemoSwitch);
				}

				if (flicPlayerFound) {
					vector.Add(AltDriveSwitch);
				}

				break;
		}

		vector.Add(Eggplant);
		return vector;
	}

	/// <summary>
	/// <c>Launcher_HasDiskSpace</c> (<c>000003bc</c>), (<paramref name="bytes"/>, <c>Launcher_PreferredPrefix</c>): the
	/// drive is the install's with an empty prefix, standing in for DOS's current drive, and otherwise the prefix's first
	/// character's. True when the drive has at least <paramref name="bytes"/> free, and for a drive with no free-space
	/// answer, which DOS gives as -1 and the unsigned comparison passes; false when it has fewer; null for a first
	/// character that is not a letter, which asserts at severity 4.
	/// </summary>
	private static bool? HasDiskSpace(long bytes, Prefixes prefixes) {
		string? root;
		if (prefixes.Preferred.Length == 0) {
			root = Path.GetPathRoot(Path.GetFullPath(prefixes.InstallRoot));
		} else {
			char letter = prefixes.Preferred[0];
			if (!char.IsAsciiLetter(letter)) {
				return null;
			}

			root = $"{char.ToUpperInvariant(letter)}:\\";
		}

		try {
			return string.IsNullOrEmpty(root) || new DriveInfo(root).AvailableFreeSpace >= bytes;
		} catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) {
			return true;
		}
	}

	/// <summary>
	/// <c>Launcher_ReadExitCfg</c> (<c>00000448</c>): <c>data\exit.cfg</c> opened <c>r+b</c> through
	/// <c>Launcher_PreferredPrefix</c> and <c>fread</c>(<c>Launcher_State</c>, 4, 1) from it: up to 4 bytes over
	/// <paramref name="state"/>'s low bytes, the bytes past the file's end keeping their value, so the programs' 2-byte
	/// code lands in the low word; that this <c>fread</c> keeps a short item's bytes is a reading launcher.md lists as
	/// Open. Null when the file does not open, where retail asserts at severity 4.
	/// </summary>
	private static int? ReadExitCfg(Prefixes prefixes, int state) => Es1ExitCfg.ReadOver(prefixes.Path(ExitPath), state);

	/// <summary>
	/// <c>Launcher_FileReadable</c> (<c>000004ac</c>): false for a null path, otherwise whether <paramref name="path"/>
	/// opens <c>rb</c>. Retail installs a critical-error handler around the open that retries once and then fails, so a
	/// missing disc fails quietly; the host's file system raises no critical error, so it has no counterpart.
	/// </summary>
	private static bool FileReadable(string? path) => path != null && Opens(path);

	/// <summary>Whether <paramref name="path"/> opens for reading, then closed.</summary>
	private static bool Opens(string path) {
		try {
			using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			return true;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
			return false;
		}
	}

	/// <summary>
	/// <c>Launcher_PreferredPrefix</c> (<c>0000657c</c>) and <c>Launcher_OtherPrefix</c> (<c>00006580</c>), each upper-cased
	/// as <c>strupr</c> does, with the paths built through them (docs/es1/shell/launcher.md#the-files).
	/// </summary>
	private sealed class Prefixes(string installRoot, string preferred, string other) {
		public string InstallRoot { get; } = installRoot;
		public string Preferred { get; } = preferred.ToUpperInvariant();
		public string Other { get; } = other.ToUpperInvariant();

		/// <summary><c>Launcher_PrefixPath</c>(<c>Launcher_PreferredPrefix</c>, <paramref name="path"/>).</summary>
		public string Path(string path) => PrefixPath(Preferred, path);

		/// <summary><c>Launcher_PrefixPath</c>(<c>Launcher_OtherPrefix</c>, <paramref name="path"/>).</summary>
		public string OtherPath(string path) => PrefixPath(Other, path);

		/// <summary>
		/// <c>Launcher_PrefixPath</c> (<c>00000304</c>): <paramref name="prefix"/> followed by <paramref name="path"/> less
		/// a drive letter of its own. With an empty prefix the path is relative to the install folder, which stands in for
		/// DOS's current folder, matched ignoring case; with a prefix it is taken as the host's file system resolves it, a
		/// drive-relative one against the process's current folder on that drive, as DOS's does.
		/// </summary>
		private string PrefixPath(string prefix, string path) {
			string own = path.Length > 1 && path[1] == ':' ? path[2..] : path;
			return prefix.Length == 0 ? CaseInsensitivePath.Combine(InstallRoot, own) : System.IO.Path.GetFullPath(prefix + own);
		}
	}

	/// <summary>
	/// The two flics' command lines <c>Launcher_Main</c> builds with <c>ALT_DRIVE</c> set, and the flic player's probe path
	/// (docs/es1/shell/launcher.md#the-flics).
	/// </summary>
	private sealed class Flics {
		/// <summary>The flic drive's paths for <c>PLAYFLIC.EXE</c> and its flic.</summary>
		public (string Program, string File) Intro { get; }
		public (string Program, string File) End { get; }

		/// <summary><c>Launcher_PrefixPath</c>(<c>Launcher_OtherPrefix</c>, <c>playflic.exe</c>).</summary>
		public string Probe { get; }

		private readonly char? _preferredDrive;

		/// <summary>
		/// The starting drive is the install's, standing in for <c>dos_getdrive</c>'s, or none off Windows; the preferred
		/// drive the preferred prefix's first character, or the starting drive when that prefix is empty; the flic drive
		/// the other prefix's first character when the preferred prefix is empty, and the starting drive otherwise.
		/// </summary>
		public Flics(Prefixes prefixes) {
			string? root = Path.GetPathRoot(Path.GetFullPath(prefixes.InstallRoot));
			char? starting = root is { Length: >= 2 } && root[1] == ':' ? char.ToUpperInvariant(root[0]) : null;
			_preferredDrive = prefixes.Preferred.Length > 0 ? prefixes.Preferred[0] : starting;
			char? flicDrive = prefixes.Preferred.Length == 0 ? (prefixes.Other.Length > 0 ? prefixes.Other[0] : null) : starting;
			Intro = (DrivePath(flicDrive, FlicPlayer), DrivePath(flicDrive, IntroFlic));
			End = (DrivePath(flicDrive, FlicPlayer), DrivePath(flicDrive, EndFlic));
			Probe = prefixes.OtherPath(FlicPlayer);
		}

		/// <summary>
		/// A flic played: <c>dos_setdrive</c>(the preferred drive), <c>spawnl</c>(0, program, program, file, <c>-B1000000</c>,
		/// <c>-S83</c>) and <c>dos_setdrive</c>(the starting drive). PLACEHOLDER: this engine has no <c>.DFL</c> player, so
		/// the flic is not played.
		/// </summary>
		public void Play((string Program, string File) flic) =>
			Console.Error.WriteLine($"PLACEHOLDER: {flic.Program} {flic.File} {string.Join(" ", FlicSwitches)} on drive {_preferredDrive}: no .DFL player, not played.");

		/// <summary>
		/// <c>Launcher_DrivePath</c> (<c>00000360</c>): the drive's letter and <c>:</c> followed by <paramref name="path"/>
		/// less a drive letter of its own; with no drive, the path alone.
		/// </summary>
		private static string DrivePath(char? drive, string path) {
			string own = path.Length > 1 && path[1] == ':' ? path[2..] : path;
			return drive is { } letter ? $"{letter}:{own}" : own;
		}
	}
}
