using System.Globalization;
using System.Text;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// What a mission's about file, <c>&lt;NAME&gt;.ABT</c> (<see cref="Extension"/>), says of it: who made it, what it is,
/// when, and which version of it this is. The file sits beside a saved mission's <c>.MSN</c> and goes into its mission
/// bundle (<see cref="MissionBundle"/>) as it is. It is HERCULAN's own, plain UTF-8 text that a text editor can write:
///
/// <list type="bullet">
/// <item>Lines up to the first blank line are fields, each <c>Name: value</c>: <c>Author</c>, <c>Date</c> and
/// <c>Version</c>, names in any case. The value runs to the line's end. <c>Version</c> is a whole number; one that is
/// not reads as none. A field named twice takes its first value, and a line that is not a known field is skipped, so a
/// later field can be added without an older reader taking it for something else.</item>
/// <item>Everything after that blank line is the <see cref="Description"/>, as many lines as it takes.</item>
/// </list>
///
/// <para>Control characters other than line breaks and tabs are dropped, and tabs read as spaces.</para>
/// </summary>
public sealed record MissionAbout(string? Author, string? Description, string? Date, int? Version) {
	/// <summary>The about file's extension, after its mission's name.</summary>
	public const string Extension = ".ABT";

	/// <summary>The largest about file read, in bytes.</summary>
	public const int MaxSize = 64 * 1024;

	/// <summary>An about file that says nothing.</summary>
	public static MissionAbout Blank { get; } = new(null, null, null, null);

	/// <summary>Whether the file says nothing: every field is missing or empty.</summary>
	public bool IsBlank => string.IsNullOrEmpty(Author) && string.IsNullOrEmpty(Description) && string.IsNullOrEmpty(Date)
		&& Version == null;

	/// <summary>Reads the file <paramref name="bytes"/>.</summary>
	public static MissionAbout Parse(byte[] bytes) {
		// UTF-8 with or without its byte order mark; a byte that is not UTF-8 reads as the replacement character.
		string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(bytes);
		string[] lines = Clean(text).Split('\n');

		string? author = null;
		string? date = null;
		int? version = null;
		bool versionSeen = false;
		int line = 0;
		for (; line < lines.Length && lines[line].Trim().Length > 0; line++) {
			int colon = lines[line].IndexOf(':');
			if (colon < 0) {
				continue;
			}

			string name = lines[line][..colon].Trim();
			string value = lines[line][(colon + 1)..].Trim();
			if (name.Equals("Author", StringComparison.OrdinalIgnoreCase)) {
				author ??= value;
			} else if (name.Equals("Date", StringComparison.OrdinalIgnoreCase)) {
				date ??= value;
			} else if (name.Equals("Version", StringComparison.OrdinalIgnoreCase) && !versionSeen) {
				versionSeen = true;
				version = int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number)
					? number
					: null;
			}
		}

		string description = string.Join('\n', lines.Skip(line + 1)).Trim();
		return new MissionAbout(author, description.Length > 0 ? description : null, date, version);
	}

	/// <summary>
	/// The file <see cref="Parse"/> reads back as this, as UTF-8 without a byte order mark and with Windows line breaks: each
	/// field that has a value, a line break in one read as a space, then the blank line, which is there even with no field so
	/// that a description line holding a colon is not taken for one, then the <see cref="Description"/>.
	/// </summary>
	public byte[] Write() {
		var text = new StringBuilder();
		Field("Author", Author);
		Field("Date", Date);
		Field("Version", Version?.ToString(CultureInfo.InvariantCulture));
		text.Append("\r\n");
		if (!string.IsNullOrWhiteSpace(Description)) {
			text.Append(Description.Trim().ReplaceLineEndings("\r\n")).Append("\r\n");
		}

		return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text.ToString());

		void Field(string name, string? value) {
			string line = value?.ReplaceLineEndings(" ").Trim() ?? "";
			if (line.Length > 0) {
				text.Append(name).Append(": ").Append(line).Append("\r\n");
			}
		}
	}

	// The text with its byte order mark gone, its line breaks as \n, tabs as spaces, and every other control character dropped.
	private static string Clean(string text) {
		var cleaned = new StringBuilder(text.Length);
		foreach (char c in text.TrimStart('\uFEFF').ReplaceLineEndings("\n")) {
			if (c == '\t') {
				cleaned.Append(' ');
			} else if (c == '\n' || !char.IsControl(c)) {
				cleaned.Append(c);
			}
		}

		return cleaned.ToString();
	}
}
