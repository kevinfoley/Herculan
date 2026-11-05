namespace HercWorks.Core.Io.Read;

/// <summary>
/// One of ES1's profile files, the <c>.ini</c>-style <c>resource.cfg</c> the sound set-up writes, as the simulator's own
/// copy of the Windows profile calls reads it (docs/es1/formats/profile-files.md): its lines as <c>Profile_Load</c>
/// (<c>ES1DBSIM 00050e20</c>) splits them, and a value as <c>Profile_GetString</c> (<c>000511a4</c>) and
/// <c>Profile_GetInt</c> (<c>00050ffc</c>) find it. Unlike <see cref="IniSection"/>, a section and a key match by their
/// first characters, ignoring case, with no space allowed around the <c>=</c>.
/// </summary>
public sealed class Es1Profile {
	private readonly string[] _lines;

	private Es1Profile(string[] lines) => _lines = lines;

	/// <summary>The file's lines, in order.</summary>
	public IReadOnlyList<string> Lines => _lines;

	/// <summary>
	/// <c>Profile_Load</c>'s split of <paramref name="bytes"/>, or none for a file that does not open, which leaves the list
	/// empty: bytes below <c>0x20</c> or above <c>0x7e</c> before a line are skipped, and a line runs while its bytes are
	/// <c>0x20</c>–<c>0x7e</c>, so a tab ends a line and a CR LF ends one line and is skipped before the next.
	/// </summary>
	public static Es1Profile Parse(byte[]? bytes) {
		var lines = new List<string>();
		int at = 0;
		while (bytes != null && at < bytes.Length) {
			if (!Printable(bytes[at])) {
				at++;
				continue;
			}

			int start = at;
			while (at < bytes.Length && Printable(bytes[at])) {
				at++;
			}

			lines.Add(System.Text.Encoding.ASCII.GetString(bytes, start, at - start));
		}

		return new Es1Profile(lines.ToArray());
	}

	/// <summary>
	/// <c>Profile_GetString</c>(file, size, buffer, default, <paramref name="key"/>, <paramref name="section"/>)'s find:
	/// what follows the first <c>=</c> of the first line beginning <c>key=</c> in the section, which is the first line
	/// beginning <c>[section]</c> and runs to the next line beginning <c>[</c>; none when the section or the key is missing,
	/// where retail copies its default instead. Retail's buffer size holds only the section listing a null key asks for,
	/// which is not ported.
	/// </summary>
	public string? GetString(string section, string key) {
		string header = "[" + section + "]", prefix = key + "=";
		int line = Array.FindIndex(_lines, l => l.StartsWith(header, StringComparison.OrdinalIgnoreCase));
		if (line < 0) {
			return null;
		}

		for (line++; line < _lines.Length && !_lines[line].StartsWith('['); line++) {
			if (_lines[line].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
				return _lines[line][(_lines[line].IndexOf('=') + 1)..];
			}
		}

		return null;
	}

	/// <summary>
	/// <c>Profile_GetInt</c>(file, <paramref name="fallback"/>, <paramref name="key"/>, <paramref name="section"/>):
	/// <see cref="Atol"/> of the value <see cref="GetString"/> finds, or <paramref name="fallback"/> when the section or
	/// the key is missing.
	/// </summary>
	public int GetInt(string section, string key, int fallback) => GetString(section, key) is { } value ? Atol(value) : fallback;

	/// <summary>
	/// The C runtime's <c>atol</c>: leading whitespace, an optional sign, then decimal digits up to the first character that
	/// is not one, wrapping as a 32-bit sum; 0 when there are none.
	/// </summary>
	public static int Atol(string text) {
		int at = SkipSpace(text, 0);
		bool negative = at < text.Length && text[at] == '-';
		if (at < text.Length && text[at] is '+' or '-') {
			at++;
		}

		int value = 0;
		for (; at < text.Length && text[at] is >= '0' and <= '9'; at++) {
			value = unchecked(value * 10 + (text[at] - '0'));
		}

		return negative ? unchecked(-value) : value;
	}

	/// <summary>
	/// The C runtime's <c>sscanf</c>(<paramref name="text"/>, <c>"%X"</c>, &amp;value): leading whitespace, an optional
	/// sign, an optional <c>0x</c> or <c>0X</c>, then hexadecimal digits up to the first character that is not one,
	/// wrapping as a 32-bit sum. False with no digit, where <c>sscanf</c> assigns nothing and the variable keeps what it held.
	/// </summary>
	public static bool TryScanHex(string text, out uint value) {
		value = 0;
		int at = SkipSpace(text, 0);
		bool negative = at < text.Length && text[at] == '-';
		if (at < text.Length && text[at] is '+' or '-') {
			at++;
		}

		if (at + 2 < text.Length && text[at] == '0' && text[at + 1] is 'x' or 'X' && Uri.IsHexDigit(text[at + 2])) {
			at += 2;
		}

		int start = at;
		for (; at < text.Length && Uri.IsHexDigit(text[at]); at++) {
			value = unchecked(value * 16 + (uint)Uri.FromHex(text[at]));
		}

		if (at == start) {
			value = 0;
			return false;
		}

		value = negative ? unchecked(0u - value) : value;
		return true;
	}

	private static bool Printable(byte b) => b is >= 0x20 and <= 0x7e;

	private static int SkipSpace(string text, int at) {
		while (at < text.Length && char.IsWhiteSpace(text[at])) {
			at++;
		}

		return at;
	}
}
