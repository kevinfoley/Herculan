using System.Globalization;

namespace HercWorks.Core.Io.Read;

/// <summary>
/// One <c>[section]</c> of an INI-style <c>.cfg</c> as <c>GetPrivateProfileString</c> sees it — the
/// reader behind <see cref="Data.File.Cfg.Keyjoy"/> and the engine's joystick device map.
/// </summary>
public static class IniSection {
	/// <summary>
	/// <paramref name="section"/>'s <c>key = value</c> pairs, keys case-insensitive: <c>;</c> starts a
	/// comment, whitespace around both halves is dropped, and a repeated key keeps the first.
	/// </summary>
	public static Dictionary<string, string> Read(IEnumerable<string> lines, string section) {
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		bool inSection = false;

		foreach (string raw in lines) {
			string line = raw.Trim();
			int comment = line.IndexOf(';');
			if (comment >= 0) {
				line = line[..comment].TrimEnd();
			}

			if (line.Length == 0) {
				continue;
			}

			if (line[0] == '[') {
				int close = line.IndexOf(']');
				inSection = close > 1
					&& string.Equals(line[1..close].Trim(), section, StringComparison.OrdinalIgnoreCase);
				continue;
			}

			if (!inSection) {
				continue;
			}

			int equals = line.IndexOf('=');
			if (equals <= 0) {
				continue;
			}

			string key = line[..equals].Trim();
			if (key.Length > 0 && !values.ContainsKey(key)) {
				values[key] = line[(equals + 1)..].Trim();
			}
		}

		return values;
	}

	/// <summary>Every <c>[section]</c>'s name, in the order the file has them, a repeated one listed once.</summary>
	public static List<string> SectionNames(IEnumerable<string> lines) {
		var names = new List<string>();
		foreach (string raw in lines) {
			string line = raw.Trim();
			int close = line.IndexOf(']');
			if (line.Length > 0 && line[0] == '[' && close > 1) {
				string name = line[1..close].Trim();
				if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) {
					names.Add(name);
				}
			}
		}

		return names;
	}

	/// <summary>The lines of a <c>.cfg</c>'s bytes, read as Latin-1 so no byte is lost.</summary>
	public static string[] Lines(byte[] bytes) =>
		System.Text.Encoding.Latin1.GetString(bytes).Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

	/// <summary>An integer setting, or <paramref name="fallback"/> when it is absent or unparseable.</summary>
	public static int IntValue(Dictionary<string, string> values, string key, int fallback) =>
		values.TryGetValue(key, out string? value)
		&& int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
			? parsed
			: fallback;

	/// <summary>A boolean setting, taking <c>1</c>, <c>yes</c> and <c>on</c> alongside <c>true</c>.</summary>
	public static bool BoolValue(Dictionary<string, string> values, string key, bool fallback) =>
		values.TryGetValue(key, out string? value)
			? value.Trim().ToLowerInvariant() switch {
				"1" or "true" or "yes" or "on" => true,
				"0" or "false" or "no" or "off" => false,
				_ => fallback,
			}
			: fallback;
}
