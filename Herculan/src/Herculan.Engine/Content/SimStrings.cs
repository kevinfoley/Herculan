using HercWorks.Core.Data.File;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Content;

/// <summary>
/// Where DBSIM's <c>.STR</c> text lives, and the reader for it. The simulator keeps every piece
/// of UI text out of its code; the layout and the <c>STRINGS0.STR</c> group index are in
/// docs/retail/formats/str-strings.md, and <see cref="StringFile"/> models it.
/// </summary>
public static class SimStrings {
	/// <summary>
	/// The folder of the English tables, and the one <c>ResourcePath_BuildFolderName</c> (<c>00492ae0</c>) gives a
	/// <c>.STR</c> by its extension whatever the language.
	/// </summary>
	public const string ResourceFolder = "str";

	/// <summary>The simulator's general UI text, and the file the MFD's own captions come from.</summary>
	public const string SimulatorStrings = "STRINGS0.STR";

	/// <summary>
	/// The folder of <paramref name="language"/>'s tables: <c>Language_StringFilePath</c> (<c>0045ef00</c>) patches the
	/// language letter over the last letter of <c>str</c>, giving <c>stf</c> and <c>stg</c>
	/// (docs/retail/simulation/alert-panels.md, "What the family shares").
	/// </summary>
	public static string LanguageFolder(GameLanguage language) => language switch {
		GameLanguage.French => "stf",
		GameLanguage.German => "stg",
		_ => ResourceFolder,
	};

	/// <summary>
	/// Reads one <c>.STR</c> as <c>Language_StringFilePath</c> finds it, in the folder of the content's language, or null
	/// when it is absent or does not parse — see <see cref="StringFileTransformer.Parse"/> for why a partial table is
	/// never returned.
	/// </summary>
	public static StringFile? Load(GameContent content, string name = SimulatorStrings) =>
		content.Read(LanguageFolder(content.Language), name) is { } bytes ? Parse(bytes) : null;

	/// <summary>
	/// Reads one <c>.STR</c> from <see cref="ResourceFolder"/> whatever the language, as
	/// <c>ResourcePath_BuildFolderName</c> finds it, or null as <see cref="Load"/> is.
	/// </summary>
	public static StringFile? LoadUntranslated(GameContent content, string name) =>
		content.Read(ResourceFolder, name) is { } bytes ? Parse(bytes) : null;

	/// <summary>Parses a <c>.STR</c>'s bytes, or null when they do not parse.</summary>
	public static StringFile? Parse(byte[] bytes) => new StringFileTransformer().Parse(bytes);
}
