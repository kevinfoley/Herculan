using HercWorks.Core.Io;
using Herculan.Engine.Install;
using Herculan.Engine.Platform;
using Herculan.Engine.Content;
using Herculan.Es1.Flic;
using Herculan.Es1.Input;
using Herculan.Es1.Install;
using Herculan.Es1.Shell;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Host;

/// <summary>
/// ES1's launcher, <c>ESCD.EXE</c>'s <c>Launcher_Main</c> (<c>000005cc</c>) in <c>ES1ESCD</c> (docs/es1/shell/launcher.md):
/// the start-up's checks and drive prefixes, then the shell (<see cref="Es1ShellHost"/>) and the simulator
/// (<see cref="Es1MissionHost"/>) one after the other, each by the number the program before left in <c>data\exit.cfg</c>,
/// until that number is 0. The programs' exit statuses are not read, as retail keeps none of <c>spawnv</c>'s answers.
///
/// <para>Retail spawns each program as a DOS executable; here each is a turn in the one <see cref="HostWindow"/> the host
/// hands the launcher, which shows black between turns (<see cref="HostWindow.Hold"/>) and stays open after the loop. The
/// DOS current folder a prefix-less path is relative to is the install folder here.</para>
///
/// <para><b>Engine guard:</b> a window closed by the player, a termination signal (<see cref="TerminationSignal"/>), or
/// the "Game" menu's "Switch to" (<see cref="GameSwitch"/>) has no DOS counterpart: it ends the loop after the program it
/// closed, whatever <c>data\exit.cfg</c> then holds, as ES2's launcher does; a mission's close leaves the 0 its start wrote,
/// and the shell's close writes 0 (the class summary of <see cref="Es1ShellHost"/>). A
/// <c>--screenshot</c> run, which has nobody to close the window, ends the loop after the first shell turn, another
/// engine guard.</para>
/// </summary>
public static class Es1Launcher {
	/// <summary>The status <c>Assert_Report</c>'s fatal path ends the program with, <c>__exit(1)</c> (docs/es1/simulation/asserts.md#severities).</summary>
	public const int AssertExitStatus = 1;

	/// <summary>
	/// The status a minimum or medium install ends with when no disc is kept, where retail's <c>ES.BAT</c> stops at the
	/// CD drive it changes to.
	/// </summary>
	public const int NoDiscExitStatus = 1;

	/// <summary><c>Launcher_State</c> (<c>00005854</c>) as the image holds it, so the first pass is state 1.</summary>
	private const int InitialState = 1;

	/// <summary>The free bytes <c>Launcher_HasDiskSpace</c> is asked for.</summary>
	private const long DiskSpaceNeeded = 40_000;

	/// <summary>The environment variable naming the CD drive, which the installer's <c>ES.BAT</c> sets.</summary>
	internal const string AltDriveVariable = "ALT_DRIVE";

	private const string PreferAltVariable = "PREFER_ALT";
	private const string ExitPath = @"data\exit.cfg", PrefsPath = @"data\prefs.cfg", DetectProgram = "detect.exe";
	private const string FlicPlayer = "playflic.exe", IntroFlic = @"flic\intro3.dfl", EndFlic = @"flic\end.dfl";
	private static readonly string[] FlicSwitches = ["-B1000000", "-S83"];

	/// <summary>The switches <c>Launcher_BuildArgs</c> (<c>00000518</c>) adds.</summary>
	private const string IntroSwitch = "-a", AltDriveSwitch = "-c", DemoSwitch = "-D", Eggplant = "-eggplant";

	/// <summary>
	/// Runs the launcher, which hands its own command line on whole to each program (<see cref="BuildArgs"/>).
	/// </summary>
	/// <param name="installRoot">The install folder, which stands in for DOS's current folder.</param>
	/// <param name="arguments">The launcher's own command line after the program's path.</param>
	/// <param name="imguiFontPath">The faces the shell's ImGui draws in (<see cref="Es1ShellHost.Run"/>).</param>
	/// <param name="imguiItalicFontPath">The italic faces the shell's ImGui draws in (<see cref="Es1ShellHost.Run"/>).</param>
	/// <param name="menus">
	/// Builds each shell turn's and each mission's menu bar and its windows (<see cref="Es1ShellHost.Run"/>,
	/// <see cref="Es1MissionHost.Run"/>), given whether the turn is a mission.
	/// </param>
	/// <param name="screenshotPath"><c>--screenshot</c>'s file, which the first shell turn captures; none for a play.</param>
	/// <param name="window">The window every turn runs in, which the host owns.</param>
	/// <param name="silent">The host's <c>--no-sound</c>: the flics', the shell's and the simulator's turns open no audio device.</param>
	/// <returns>
	/// 0 when the loop ends, as it does when a start-up check refuses (<c>exit</c>(0)); <see cref="AssertExitStatus"/>
	/// after a fatal assert or the fault of a <c>PREFER_ALT</c> without <c>ALT_DRIVE</c>; <see cref="NoDiscExitStatus"/>
	/// for a minimum or medium install with no disc kept. Retail's <c>Launcher_Main</c> returns no value.
	/// </returns>
	public static int Run(string installRoot, IReadOnlyList<string> arguments, string imguiFontPath, string imguiItalicFontPath,
			Func<bool, IFrontEndMenus> menus, string? screenshotPath, HostWindow window, bool silent = false) {
		// 1. Mem_New (00001664)(argc * 4 + 8), its answer not kept: no counterpart.
		// 2. The memory check, X32_LargestFreeBlock (00002e63)() - 25,000 against 3,435,000: the extender's largest free
		// block has no counterpart, so the check always passes, an engine choice, and its figure is not computed.

		// 3. The drive prefixes. With PREFER_ALT set and ALT_DRIVE not, retail's copy reads from address 0 and faults
		// (docs/es1/shell/launcher.md#starting--launcher_main-000005cc); the fault ends retail's, so it is reported and ends
		// this one. Assert_SetLogDir (00001814): this engine keeps no assert log.
		string? altDrive = Environment.GetEnvironmentVariable(AltDriveVariable);
		bool preferAlt = Environment.GetEnvironmentVariable(PreferAltVariable) != null;
		if (preferAlt && altDrive == null) {
			Console.Error.WriteLine($"{PreferAltVariable} is set and {AltDriveVariable} is not: ESCD.EXE reads its drive prefix from address 0 here, which faults.");
			return AssertExitStatus;
		}

		var prefixes = new Prefixes(installRoot, preferAlt ? altDrive! : "",
			preferAlt && altDrive != null ? "" : altDrive ?? "");

		// With neither variable in the host's environment, the install's own ES.BAT says which layout it has, and the disc
		// the Settings window names (Es1Disc) stands in for the CD drive, whose letters ES.BAT holds
		// (docs/es1/shell/launcher.md#the-installers-esbat). A full install's names the CD in ALT_DRIVE, PREFER_ALT cleared;
		// a minimum or medium install's names the install in ALT_DRIVE, PREFER_ALT set, and runs ESCD.EXE from the CD, so
		// the empty prefix is the CD's current folder. Either way the data files are the install's and the flic player and
		// the flics the disc's, so both prefixes stay empty, the empty one standing for the install's folder.
		var layout = Es1Layout.Full;
		if (altDrive == null && !preferAlt && Es1EsBat.Read(installRoot) is { } esBat) {
			if (esBat is { PreferAlt: true, AltDrive: null }) {
				Console.Error.WriteLine($"{Es1EsBat.FileName} sets {PreferAltVariable} and not {AltDriveVariable}: ESCD.EXE reads its drive prefix from address 0 here, which faults.");
				return AssertExitStatus;
			}

			layout = esBat.Layout;
		}

		using var disc = altDrive == null ? Es1Disc.OpenRemembered() : null;
		if (layout == Es1Layout.MinimumOrMedium && disc == null) {
			// Retail's ES.BAT changes to the CD drive before it runs escd there, which fails with no disc in the drive.
			Console.Error.WriteLine($"This install leaves the programs on the Metaltech: Earthsiege disc, and no disc is kept: its {Es1EsBat.FileName} changes to the CD drive to run ESCD.EXE there. Choose the disc in Settings.");
			return NoDiscExitStatus;
		}

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

		// 5. NO EFFECT: data\es.got, which holds step 2's free-memory figure, is not written: step 2's figure has no
		// counterpart, and no reader of the file is known (docs/es1/shell/launcher.md#open).
		// 6. Mem_InitHeap (000015dc)(20,000): this engine has no fixed heap.
		// 7. Launcher_SaveVideoMode (00000b60), the BIOS's equipment byte and video mode, and at the end
		// Launcher_RestoreVideoMode (00000b83): this engine sets no DOS video mode, so there is nothing to save or restore.

		// 8. The flics' command lines, only with ALT_DRIVE set or the disc standing in for it; 9. the flic player looked for
		// (docs/es1/shell/launcher.md#the-flics).
		Flics? flics = altDrive != null ? new Flics(prefixes, altDrive) : disc != null ? new Flics(prefixes, disc) : null;
		bool flicPlayerFound = flics?.PlayerFound() == true;

		int state = InitialState;
		bool firstShell = true;
		while (state != 0) {
			switch (state) {
				case 1:
					// data\prefs.cfg opened rb to see that it is there. PLACEHOLDER: DETECT.EXE, which retail runs when it is
					// not, is not ported; Es1Preferences.FirstRunOptions is written in its place.
					if (!Opens(prefixes.Path(PrefsPath))) {
						string detect = flicPlayerFound ? flics!.OtherPath(DetectProgram) : prefixes.Path(DetectProgram);
						try {
							File.WriteAllBytes(prefixes.Path(PrefsPath), Es1Preferences.FirstRunOptions());
							Console.WriteLine($"PLACEHOLDER: data\\prefs.cfg is missing; this engine's first-run options are written in place of {detect}{(flicPlayerFound ? " " + AltDriveSwitch : "")}, which ESCD.EXE runs then.");
						} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
							Console.Error.WriteLine($"data\\prefs.cfg is missing and does not open for writing ({ex.Message}); the shell loads every option as 0.");
						}
					}

					if (flicPlayerFound) {
						flics!.Play(flics.Intro, window, silent);
					}

					goto case 3;
				case 3:
				case 6:
					flicPlayerFound = flics?.PlayerFound() == true;
					RunShell(installRoot, disc, BuildArgs(arguments, state, flicPlayerFound), imguiFontPath, imguiItalicFontPath,
						menus, firstShell ? screenshotPath : null, window, silent);
					firstShell = false;
					break;
				case 4:
					flicPlayerFound = flics?.PlayerFound() == true;
					if (flicPlayerFound) {
						flics!.Play(flics.End, window, silent);
					}

					RunShell(installRoot, disc, BuildArgs(arguments, state, flicPlayerFound), imguiFontPath, imguiItalicFontPath,
						menus, firstShell ? screenshotPath : null, window, silent);
					firstShell = false;
					break;
				case 2:
				case 5:
					flicPlayerFound = flics?.PlayerFound() == true;
					RunSimulator(installRoot, disc, BuildArgs(arguments, state, flicPlayerFound), imguiFontPath, imguiItalicFontPath,
						menus, window, silent);
					break;
				default:
					// Any other state runs nothing, and the file is read again unchanged, so retail goes on reading it. The
					// window's events are handled meanwhile, an engine guard, so that its close can end the loop.
					window.PumpEvents();
					break;
			}

			// The engine guards of the class summary.
			if (window.Closed || TerminationSignal.Requested || GameSwitch.Requested != null || (screenshotPath != null && !firstShell)) {
				return 0;
			}

			if (ReadExitCfg(prefixes, state) is not { } next) {
				Console.Error.WriteLine($"{prefixes.Path(ExitPath)} does not open (src\\es.cpp line 0x67 asserts here).");
				return AssertExitStatus;
			}

			state = next;
		}

		// The end: Launcher_RestoreVideoMode (step 7) and Mem_HeapOff (00001658).
		return 0;
	}

	/// <summary>
	/// A turn of the shell, <c>spawnv</c>(0, <c>go.exe</c>, ...), its status not kept.
	/// </summary>
	/// <param name="installRoot">The install folder.</param>
	/// <param name="disc">The disc standing in for the CD drive, whose <c>vol\</c> the shell mounts under <c>-C</c>; null for none.</param>
	/// <param name="arguments">The shell's command line, <see cref="BuildArgs"/>'s vector.</param>
	/// <param name="imguiFontPath">The faces the shell's ImGui draws in.</param>
	/// <param name="imguiItalicFontPath">The italic faces the shell's ImGui draws in.</param>
	/// <param name="menus">Builds the turn's menu bar and its windows.</param>
	/// <param name="screenshotPath"><c>--screenshot</c>'s file, which this turn captures; none for a play.</param>
	/// <param name="window">The window the turn runs in.</param>
	/// <param name="silent">Whether the turn opens no audio device.</param>
	private static void RunShell(string installRoot, GameDisc? disc, IReadOnlyList<string> arguments, string imguiFontPath,
			string imguiItalicFontPath, Func<bool, IFrontEndMenus> menus, string? screenshotPath, HostWindow window, bool silent) {
		Es1ShellHost.Run(installRoot, arguments, imguiFontPath, imguiItalicFontPath, menus(false), screenshotPath, window, silent, disc);
		window.Hold(null);
	}

	/// <summary>
	/// A turn of the simulator on the install's <c>data\</c> files, <c>spawnv</c>(0, <c>dbsim.exe</c>, ...), its status not
	/// kept: the arguments read as <c>ES1DBSIM</c>'s <c>Sim_ParseCommandLine</c> (<c>00058e74</c>) and
	/// <c>Sim_PreParseCommandLine</c> (<c>00058e30</c>) read them (docs/es1/simulation/main-loop.md#the-command-line).
	/// <c>-D</c>, <c>-p</c>, <c>-r</c>, <c>-s</c> and whether an argument opens with <c>-c</c> are handed to
	/// <see cref="Es1MissionHost.Run"/>; <c>-c</c>'s <c>ALT_DRIVE</c> is the install's paths and the disc here. <c>-eggplant</c> is what the simulator's step 6 refusal
	/// tests, which <see cref="Es1MissionHost"/> does not make. The switches the simulator does not take are reported.
	/// </summary>
	/// <param name="installRoot">The install folder.</param>
	/// <param name="disc">The disc standing in for the CD drive, whose <c>vol\</c> is mounted after the install's under <c>-c</c>; null for none.</param>
	/// <param name="arguments">The simulator's command line, <see cref="BuildArgs"/>'s vector.</param>
	/// <param name="imguiFontPath">The faces the mission's ImGui draws in.</param>
	/// <param name="imguiItalicFontPath">The italic faces the mission's ImGui draws in.</param>
	/// <param name="menus">Builds the mission's menu bar and its windows, as a shell turn's.</param>
	/// <param name="window">The window the turn runs in.</param>
	/// <param name="silent">Whether the turn opens no audio device.</param>
	private static void RunSimulator(string installRoot, GameDisc? disc, IReadOnlyList<string> arguments, string imguiFontPath,
			string imguiItalicFontPath, Func<bool, IFrontEndMenus> menus, HostWindow window, bool silent) {
		bool demo = false, nameGiven = false, soundOn = true;
		bool altDriveAllowed = arguments.Any(argument => argument.StartsWith(AltDriveSwitch, StringComparison.Ordinal));
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

		Es1MissionHost.Run(installRoot, null, new Es1HeaderOverrides(null, null, null), imguiFontPath, imguiItalicFontPath,
			menus(true), playTape, demo, recordTape, window, soundOn, silent, altDriveAllowed ? disc : null);
		window.Hold(null);
	}

	/// <summary>
	/// <c>Launcher_BuildArgs</c> (<c>00000518</c>): the launcher's own <paramref name="arguments"/>, then by
	/// <paramref name="state"/> <c>-a</c> and <c>-c</c> for the shell's states and <c>-c</c> for the simulator's with the
	/// flic player found, <c>-D</c> first for state 5, and last <c>-eggplant</c>. Retail's vector opens with the
	/// launcher's <c>argv[0]</c>; this one leaves it out, as both hosts take a command line after the program's path.
	/// </summary>
	/// <param name="arguments">The launcher's own command line after the program's path.</param>
	/// <param name="state"><c>Launcher_State</c>, which says the program and its switches.</param>
	/// <param name="flicPlayerFound"><c>Launcher_FlicPlayerFound</c>, without which neither <c>-a</c> nor <c>-c</c> is given.</param>
	/// <returns>The program's command line.</returns>
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
	/// <c>Launcher_HasDiskSpace</c> (<c>000003bc</c>), (<paramref name="bytes"/>, <c>Launcher_PreferredPrefix</c>)
	/// (docs/es1/shell/launcher.md#the-disk-check--launcher_hasdiskspace-000003bc): the drive is the install's with an
	/// empty prefix, standing in for DOS's current drive, and otherwise the prefix's first character's.
	/// </summary>
	/// <param name="bytes">The free bytes needed.</param>
	/// <param name="prefixes">The prefixes, whose preferred one names the drive.</param>
	/// <returns>
	/// True when the drive has at least <paramref name="bytes"/> free, and for a drive with no free-space answer, which
	/// DOS gives as -1 and the unsigned comparison passes; false when it has fewer; null for a first character that is not
	/// a letter, which asserts at severity 4.
	/// </returns>
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
	/// code lands in the low word (docs/es1/shell/launcher.md#the-loop).
	/// </summary>
	/// <param name="prefixes">The prefixes, whose preferred one the file is read through.</param>
	/// <param name="state"><c>Launcher_State</c> as it stands, whose bytes the file's replace.</param>
	/// <returns>The next state; null when the file does not open, where retail asserts at severity 4.</returns>
	private static int? ReadExitCfg(Prefixes prefixes, int state) => Es1ExitCfg.ReadOver(prefixes.Path(ExitPath), state);

	/// <summary>
	/// <c>Launcher_FileReadable</c> (<c>000004ac</c>). Retail's critical-error handler around the open
	/// (docs/es1/shell/launcher.md#the-flic-player--launcher_filereadable-000004ac) has no counterpart: the host's file
	/// system raises no critical error.
	/// </summary>
	/// <param name="path">The path to open, or null.</param>
	/// <returns>False for a null path, otherwise whether <paramref name="path"/> opens <c>rb</c>.</returns>
	private static bool FileReadable(string? path) => path != null && Opens(path);

	/// <summary>Opens <paramref name="path"/> for reading and closes it.</summary>
	/// <param name="path">The file to open.</param>
	/// <returns>Whether it opened.</returns>
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
	/// <param name="installRoot">The install folder, which an empty prefix stands for.</param>
	/// <param name="preferred"><c>Launcher_PreferredPrefix</c> before upper-casing.</param>
	/// <param name="other"><c>Launcher_OtherPrefix</c> before upper-casing.</param>
	private sealed class Prefixes(string installRoot, string preferred, string other) {
		public string InstallRoot { get; } = installRoot;
		public string Preferred { get; } = preferred.ToUpperInvariant();
		public string Other { get; } = other.ToUpperInvariant();

		/// <summary><c>Launcher_PrefixPath</c>(<c>Launcher_PreferredPrefix</c>, <paramref name="path"/>).</summary>
		/// <param name="path">The DOS path, a drive of its own dropped.</param>
		/// <returns>The host path.</returns>
		public string Path(string path) => PrefixPath(Preferred, path);

		/// <summary><c>Launcher_PrefixPath</c>(<c>Launcher_OtherPrefix</c>, <paramref name="path"/>).</summary>
		/// <param name="path">The DOS path, a drive of its own dropped.</param>
		/// <returns>The host path.</returns>
		public string OtherPath(string path) => PrefixPath(Other, path);

		/// <summary>
		/// <c>Launcher_PrefixPath</c> (<c>00000304</c>): <paramref name="prefix"/> followed by <paramref name="path"/> less
		/// a drive letter of its own. With an empty prefix the path is relative to the install folder, which stands in for
		/// DOS's current folder, matched ignoring case; with a prefix it is taken as the host's file system resolves it, a
		/// drive-relative one against the process's current folder on that drive, as DOS's does.
		/// </summary>
		/// <param name="prefix">The prefix, empty for the install folder.</param>
		/// <param name="path">The DOS path.</param>
		/// <returns>The host path.</returns>
		private string PrefixPath(string prefix, string path) {
			string own = path.Length > 1 && path[1] == ':' ? path[2..] : path;
			return prefix.Length == 0 ? CaseInsensitivePath.Combine(InstallRoot, own) : System.IO.Path.GetFullPath(prefix + own);
		}
	}

	/// <summary>
	/// The two flics' command lines <c>Launcher_Main</c> builds with <c>ALT_DRIVE</c> set, and the flic player's probe path
	/// (docs/es1/shell/launcher.md#the-flics); or, with the disc the Settings window names standing in for
	/// <c>ALT_DRIVE</c> (<see cref="Es1Disc"/>), the same files on that disc.
	/// </summary>
	private sealed class Flics {
		private readonly Prefixes _prefixes;
		private readonly string _installRoot;
		private readonly string? _altDrive;
		private readonly GameDisc? _disc;

		/// <summary>The flic drive's paths for <c>PLAYFLIC.EXE</c> and its flic.</summary>
		public (string Program, string File) Intro { get; }
		public (string Program, string File) End { get; }

		/// <summary><c>Launcher_PrefixPath</c>(<c>Launcher_OtherPrefix</c>, <c>playflic.exe</c>).</summary>
		public string Probe { get; }

		/// <summary>
		/// The starting drive is the install's, standing in for <c>dos_getdrive</c>'s, or none off Windows; the flic drive
		/// the other prefix's first character when the preferred prefix is empty, and the starting drive otherwise. The
		/// preferred drive is not taken (<see cref="Play"/>).
		/// </summary>
		/// <param name="prefixes">The launcher's prefixes.</param>
		/// <param name="altDrive"><c>ALT_DRIVE</c>'s value, which the flics' sounds may be read under (<see cref="Es1FlicHost.Run"/>).</param>
		public Flics(Prefixes prefixes, string altDrive) {
			_prefixes = prefixes;
			_installRoot = prefixes.InstallRoot;
			_altDrive = altDrive;
			string? root = Path.GetPathRoot(Path.GetFullPath(prefixes.InstallRoot));
			char? starting = root is { Length: >= 2 } && root[1] == ':' ? char.ToUpperInvariant(root[0]) : null;
			char? flicDrive = prefixes.Preferred.Length == 0 ? (prefixes.Other.Length > 0 ? prefixes.Other[0] : null) : starting;
			Intro = (DrivePath(flicDrive, FlicPlayer), DrivePath(flicDrive, IntroFlic));
			End = (DrivePath(flicDrive, FlicPlayer), DrivePath(flicDrive, EndFlic));
			Probe = prefixes.OtherPath(FlicPlayer);
		}

		/// <summary>
		/// The flics on <paramref name="disc"/>: the paths are the same names on the disc's root, with no drive.
		/// </summary>
		/// <param name="prefixes">The launcher's prefixes.</param>
		/// <param name="disc">The disc standing for the CD drive, a full install's <c>ALT_DRIVE</c> or a minimum or medium install's current folder.</param>
		public Flics(Prefixes prefixes, GameDisc disc) {
			_prefixes = prefixes;
			_installRoot = prefixes.InstallRoot;
			_disc = disc;
			Intro = (FlicPlayer, IntroFlic);
			End = (FlicPlayer, EndFlic);
			Probe = FlicPlayer;
		}

		/// <summary><c>Launcher_FileReadable</c>(the probe path), the probe taken on the disc when there is one.</summary>
		public bool PlayerFound() => _disc != null ? _disc.FileExists(Probe) : FileReadable(Probe);

		/// <summary><c>Launcher_PrefixPath</c>(<c>Launcher_OtherPrefix</c>, <paramref name="path"/>), or where it is on the disc, for a message.</summary>
		/// <param name="path">The DOS path.</param>
		/// <returns>The path as a message shows it.</returns>
		public string OtherPath(string path) => _disc != null ? _disc.Describe(path) : _prefixes.OtherPath(path);

		/// <summary>
		/// A flic played: <c>dos_setdrive</c>(the preferred drive), <c>spawnl</c>(0, program, program, file, <c>-B1000000</c>,
		/// <c>-S83</c>) and <c>dos_setdrive</c>(the starting drive), its status not kept. The program is
		/// <see cref="Es1FlicHost"/>. The file's path is taken as the host's file system resolves it, a drive-relative one
		/// against the process's current
		/// folder on that drive, as DOS's does, and one with no drive against the current folder; with a disc it is taken
		/// on the disc. The preferred drive, DOS's current one while the player runs, is where it reads <c>RESOURCE.CFG</c>,
		/// which the install folder stands in for.
		/// </summary>
		/// <param name="flic">The flic's command line, <see cref="Intro"/> or <see cref="End"/>.</param>
		/// <param name="window">The window the flic plays in.</param>
		/// <param name="silent">Whether the player opens no audio device.</param>
		public void Play((string Program, string File) flic, HostWindow window, bool silent) {
			if (_disc != null) {
				Es1FlicHost.Run(_disc, flic.File, FlicSwitches, window, _installRoot, silent);
				return;
			}

			string path = flic.File.Length > 1 && flic.File[1] == ':'
				? Path.GetFullPath(flic.File)
				: CaseInsensitivePath.Combine(Directory.GetCurrentDirectory(), flic.File);
			Es1FlicHost.Run(path, FlicSwitches, window, _installRoot, _altDrive, silent);
		}

		/// <summary>
		/// <c>Launcher_DrivePath</c> (<c>00000360</c>): the drive's letter and <c>:</c> followed by <paramref name="path"/>
		/// less a drive letter of its own; with no drive, the path alone.
		/// </summary>
		/// <param name="drive">The drive's letter, or null for none.</param>
		/// <param name="path">The DOS path.</param>
		/// <returns>The path on the drive.</returns>
		private static string DrivePath(char? drive, string path) {
			string own = path.Length > 1 && path[1] == ':' ? path[2..] : path;
			return drive is { } letter ? $"{letter}:{own}" : own;
		}
	}
}
