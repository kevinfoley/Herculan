using System.Text;
using HercWorks.Core.Io;

namespace Herculan.Es1.Install;

/// <summary>Where an install's programs are, as its <c>ES.BAT</c> has <c>ESCD.EXE</c> find them (<see cref="Es1EsBat"/>).</summary>
public enum Es1Layout {
	/// <summary>
	/// The full install's: the programs and the data files in the install, the current folder, and the flics on the CD
	/// <c>ALT_DRIVE</c> names, with <c>PREFER_ALT</c> cleared.
	/// </summary>
	Full,

	/// <summary>
	/// The minimum and medium installs': the data files in the install, which <c>ALT_DRIVE</c> names with
	/// <c>PREFER_ALT</c> set, and the programs, the flics and the archives the install left behind on the CD, the
	/// current folder.
	/// </summary>
	MinimumOrMedium,
}

/// <summary>
/// The environment an install's <c>ES.BAT</c> gives <c>ESCD.EXE</c>: the <c>set</c> lines before the line that runs it,
/// which is how retail's launcher learns its two drive prefixes (docs/es1/shell/launcher.md#the-installers-esbat). The
/// file is not run: of its other lines, the drive change the minimum and medium sizes' file makes before <c>escd</c> is
/// what <see cref="Es1Layout.MinimumOrMedium"/> stands for, and a line the installer does not write is not read.
/// </summary>
/// <param name="AltDrive"><c>ALT_DRIVE</c>'s value, or null when the file leaves it unset.</param>
/// <param name="PreferAlt">Whether <c>PREFER_ALT</c> is set: <c>getenv</c> finds it at all.</param>
public sealed record Es1EsBat(string? AltDrive, bool PreferAlt) {
	/// <summary>The batch file the installer writes in the install's root.</summary>
	public const string FileName = "ES.BAT";

	private const string Launcher = "escd";

	/// <summary>
	/// The layout the file sets up: <see cref="Es1Layout.MinimumOrMedium"/> when it sets <c>PREFER_ALT</c>, otherwise
	/// <see cref="Es1Layout.Full"/>.
	/// </summary>
	public Es1Layout Layout => PreferAlt ? Es1Layout.MinimumOrMedium : Es1Layout.Full;

	/// <summary>
	/// The install's <see cref="FileName"/>, read as <c>COMMAND.COM</c> reads its <c>set</c> lines up to the line that
	/// runs <c>escd</c>: <c>set NAME=value</c> sets the variable, its name upper-cased, and <c>set NAME=</c> removes it. Null
	/// when the install has no such file or it does not open, which leaves the full layout, as an install this engine
	/// made before its other sizes has the full install's file.
	/// </summary>
	/// <param name="installRoot">The install's root folder.</param>
	public static Es1EsBat? Read(string installRoot) {
		string path = CaseInsensitivePath.Combine(installRoot, FileName);
		string text;
		try {
			if (!File.Exists(path)) {
				return null;
			}

			text = File.ReadAllText(path, Encoding.Latin1);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); the install is taken for a full one.");
			return null;
		}

		return Parse(text);
	}

	/// <summary><see cref="Read"/> on a batch file's <paramref name="text"/>.</summary>
	public static Es1EsBat Parse(string text) {
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string raw in text.Replace("\r\n", "\n").Split('\n')) {
			string line = raw.Trim().TrimStart('@').Trim();
			if (line.Equals(Launcher, StringComparison.OrdinalIgnoreCase)
					|| line.Equals(Launcher + ".exe", StringComparison.OrdinalIgnoreCase)) {
				break;
			}

			if (!line.StartsWith("set ", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			string assignment = line[4..];
			int equals = assignment.IndexOf('=');
			if (equals <= 0) {
				continue;
			}

			string name = assignment[..equals];
			string value = assignment[(equals + 1)..];
			if (value.Length == 0) {
				variables.Remove(name);
			} else {
				variables[name] = value;
			}
		}

		return new Es1EsBat(variables.GetValueOrDefault("ALT_DRIVE"), variables.ContainsKey("PREFER_ALT"));
	}

	/// <summary>The layout of <paramref name="installRoot"/>'s <see cref="FileName"/> (<see cref="Read"/>).</summary>
	/// <param name="installRoot">The install's root folder.</param>
	public static Es1Layout LayoutOf(string installRoot) => Read(installRoot)?.Layout ?? Es1Layout.Full;
}
