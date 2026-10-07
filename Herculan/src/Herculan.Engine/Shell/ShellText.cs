using HercWorks.Core.Data.File;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// <c>estext.bin</c> — every caption, title and readout label the shell prints, by index. The tab
/// strip's eight captions are entries <see cref="ShellLayout.FirstTabCaption"/> onward, which is why
/// nothing here hardcodes a tab's name: the original does not either, and reading them is what makes
/// the strip say what the game says.
///
/// <para>The container is the <c>.BIN</c> string table described in docs/retail/formats/weapons-dat.md, and
/// <see cref="BinStringFileTransformer"/> already parses it. VSHELL reaches it through
/// <c>WeaponsBin_LookupName</c> (<c>00408240</c>) against the handle at <c>0046dcc0</c>, opened by
/// literal filename in the shell's global init (<c>EsGlobal_Init</c> (<c>004073bc</c>), <c>esglobal.cpp</c>).</para>
///
/// <para>It lives in <c>LANG0.VOL</c>, not <c>SHELL0.VOL</c>, under one folder per language, which
/// <c>WeaponsBin_Open</c> (<c>00408605</c>) picks by the shell's language (docs/retail/formats/weapons-dat.md, "The
/// <c>.BIN</c> string tables").</para>
/// </summary>
public sealed class ShellText {
	/// <summary>The shell's own string table.</summary>
	public const string ResourceName = "ESTEXT.BIN";

	/// <summary>The folder of the English tables.</summary>
	public const string EnglishFolder = "ENG";

	/// <summary>The <c>LANG0.VOL</c> folder of <paramref name="language"/>'s tables.</summary>
	public static string LanguageFolder(GameLanguage language) => language switch {
		GameLanguage.French => "FRE",
		GameLanguage.German => "GER",
		_ => EnglishFolder,
	};

	private readonly string[] _values;

	private ShellText(string[] values) => _values = values;

	/// <summary>How many entries the table holds — 342 in the retail <c>estext.bin</c>.</summary>
	public int Count => _values.Length;

	/// <summary>
	/// Entry <paramref name="index"/>, or null when the index is out of range. Callers draw no text on
	/// null rather than substituting a placeholder: a missing caption should read as missing, the same
	/// call the cockpit's own label paths make.
	/// </summary>
	public string? Text(int index) =>
		index >= 0 && index < _values.Length && _values[index].Length > 0 ? _values[index] : null;

	/// <summary>
	/// Loads one <c>.BIN</c> table out of the content's language folder. Returns null when no
	/// mounted archive carries the file, in which case the shell draws its art and no words.
	/// </summary>
	public static ShellText? Load(GameContent content, string resourceName = ResourceName) {
		if (content.Read(LanguageFolder(content.Language), resourceName) is not { } bytes
			|| new BinStringFileTransformer().Parse(bytes) is not StringBinaryFile { Values: { } values }) {
			return null;
		}

		// The transformer slices each entry up to the next offset, so every string but the last
		// carries its own NUL terminator, and Trim() does not consider NUL whitespace.
		var trimmed = new string[values.Length];
		for (int i = 0; i < values.Length; i++) {
			trimmed[i] = values[i].Trim('\0').Trim();
		}

		return new ShellText(trimmed);
	}
}
