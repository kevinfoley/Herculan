namespace Herculan.Engine.Games;

/// <summary>
/// Reads the values after a command-line flag, for the host's parser and each game's (<see cref="IGameModule.TryParseOption"/>).
///
/// <para>A malformed command line is an error rather than a guess. Anything no parser claims would otherwise land in the
/// positional arguments, so <c>--mfd abc</c> would take <c>abc</c> as the install path and fail with a message about the
/// install.</para>
/// </summary>
public static class OptionValues {
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
}
