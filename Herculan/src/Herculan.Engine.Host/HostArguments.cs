namespace Herculan.Engine.Host;

/// <summary>
/// The command line's value readers and its <c>--help</c> text. <see cref="HostOptions"/> owns the flags themselves;
/// docs/herculan/herculan-command-line.md describes each one.
///
/// <para>A malformed command line is an error rather than a guess. Anything the parser does not claim
/// would otherwise land in the positional arguments, so <c>--mfd abc</c> would take <c>abc</c> as the
/// install path and fail with a message about the install.</para>
/// </summary>
static class HostArguments {
	/// <summary>
	/// Reads the number after the flag at <c>args[i]</c>, advancing past it. On failure records why in
	/// <paramref name="errors"/>, and still steps over a value that is present, so it is not reported
	/// a second time as a stray positional argument. A flag with several values passes its own name as
	/// <paramref name="flag"/> for the second and later, when <c>args[i]</c> is no longer the flag.
	/// </summary>
	public static bool TryReadInt(string[] args, ref int i, int min, int max, List<string> errors, out int value,
			string? flag = null) {
		flag ??= args[i];
		value = 0;
		if (!HasValue(args, i)) {
			errors.Add($"{flag} needs {Range(min, max)}.");
			return false;
		}

		string text = args[++i];
		if (!int.TryParse(text, out value) || value < min || value > max) {
			errors.Add($"{flag} needs {Range(min, max)}, not '{text}'.");
			return false;
		}
		return true;
	}

	/// <summary>
	/// Reads an optional number after the flag at <c>args[i]</c>: a following argument is taken only
	/// when it parses and is in range, as <c>--quit</c>, <c>--hdd</c> and <c>--joystick</c> want.
	/// </summary>
	public static bool TryReadOptionalInt(string[] args, ref int i, int min, int max, out int value) {
		if (i + 1 < args.Length && int.TryParse(args[i + 1], out value) && value >= min && value <= max) {
			i++;
			return true;
		}
		value = 0;
		return false;
	}

	/// <summary>Reads the text after the flag at <c>args[i]</c> — a path or a name — advancing past it.</summary>
	public static bool TryReadString(string[] args, ref int i, List<string> errors, out string value) {
		if (!HasValue(args, i)) {
			errors.Add($"{args[i]} needs a value.");
			value = "";
			return false;
		}
		value = args[++i];
		return true;
	}

	// Another flag is never a value: "--screenshot --mfd 2" is a missing path, not a file called "--mfd".
	static bool HasValue(string[] args, int i) => i + 1 < args.Length && !args[i + 1].StartsWith("--");

	static string Range(int min, int max) =>
		(min, max) == (int.MinValue, int.MaxValue) ? "a number" : $"a number from {min} to {max}";

	public static bool IsHelp(string arg) => arg is "--help" or "-h" or "-?" or "/?";

	public const string Usage = """
		Usage: Herculan.Engine.Host [<install>] [<mission>] [options]

		  <install>   the Earthsiege 2 folder; default ES2_GAME_PATH, then the last one used, then an ES2 folder
		              above the executable, then a window asking for it
		  <mission>   a script.dat with its mission.var and player.mec, or a mission's .MSN name (C1_03); default DATA\script.dat in the install

		What runs (default: the front end)
		  --mission                   one mission, without the front end; a named <mission> implies it
		  --movie <name>              one cutscene, by path or AVI folder name
		  --play <tape>               replay an input tape, by path or TAPES folder stem; implies --mission
		  --demo                      a demo tape from TAPES\demolist.str, as VIEW DEMO plays it; implies --mission
		  --record <tape>             record the mission's input to <tape>.tap

		Front end (none combines with --mission)
		  --shell-tab <0-7>  --shell-bay <0-7>  --shell-training  --shell-practice
		  --shell-windowed  --shell-no-movies
		  --shell-palette <name>

		Sound and music
		  --no-sound, --silent        no audio device
		  --music <n>                 CD track n % 5 + 2
		  --cd-drive <drive>  --music-dir <dir>

		Settings and input devices
		  --ask-install               ask for the install at once, and remember none
		  --windowed  --no-write-prefs  --joystick [0-8]  --joystick-probe  --write-joystick-map

		Installing
		  --install <disc> <folder>   install from a disc folder or disc image (.iso, .bin, .cue), then exit
		  --install-size minimum|medium|maximum         default maximum
		  --install-language english|french|german      default english

		Developer
		  --developer                 the developer keys

		Screenshots and staged state
		  --save-prtscn               also save each full-screen [PrtScn] capture to <install>\Screenshots
		  --screenshot <file>         capture after 30 frames (or what the options below wait for), then exit
		  --mfd <0-5>  --hdd [0|1]  --hdd-damage <0-2>  --external
		  --objectives  --quit [0-19]  --preferences  --controls  --hit-shake
		  --throttle <n>  --heading <n>  --turret <twist> <pitch>  --track  --target
		  --weapon <1-10>  --link  --fire  --impact
		  --hdd-pilot <0-2>  --hdd-order <0-7>  --hdd-xmit
		  --flash-comm <0-5>  --flash-comm-xmit  --wait-transmission

		  --help, -h, -?              this text

		Every option is described in Herculan/docs/herculan/herculan-command-line.md.
		""";
}
