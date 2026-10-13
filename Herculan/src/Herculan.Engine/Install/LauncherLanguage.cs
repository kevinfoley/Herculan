using HercWorks.Core.Io;
using Herculan.Engine.Content;
using Herculan.Engine.World;

namespace Herculan.Engine.Install;

/// <summary>
/// The language each program of an install runs in, worked out as the install's launcher, <c>ES.EXE</c>, would set it
/// (docs/retail/command-line.md, "v1.10's language switch"; docs/retail/retail-builds.md, "How a language is chosen").
///
/// <para>v1.0's launcher passes no language switch, so its shell and simulator run in English whatever
/// <c>data\language.cfg</c> says. v1.10's appends <c>-</c> and the file's first byte to both command lines unless the
/// byte is <c>E</c> or there is no file. Retail tells the two apart by which <c>ES.EXE</c> is run; this tells them
/// apart by <see cref="V110ArchiveName"/> in the install's <c>VOL</c> folder, which v1.10's installer copies at every
/// size and v1.0's never does.</para>
///
/// <para>Only the language switches are taken from the byte. A byte that is some other switch of either program
/// (VSHELL's <c>-s</c> turns its sound off) is not acted on here.</para>
/// </summary>
public static class LauncherLanguage {
	/// <summary>The archive only a v1.10 install holds.</summary>
	public const string V110ArchiveName = "SIMLANG.VOL";

	/// <summary>
	/// <c>prefs.cfg</c> option 43, <c>ShellOption_Language</c> (VSHELL <c>004824e3</c>): the shell's language when its
	/// command line names none, 0 English, 1 French, 2 German (docs/retail/simulation/preferences.md).
	/// </summary>
	public const int ShellLanguageOption = 43;

	/// <summary>
	/// The byte the install's launcher passes as a switch, or null when it passes none: a v1.0 install, no
	/// <c>language.cfg</c>, or <c>E</c>.
	/// </summary>
	public static byte? Switch(string installRoot) =>
		IsV110(installRoot) && GameInstall.ReadLanguageLetter(installRoot) is { } letter && letter != (byte)'E'
			? letter
			: null;

	/// <summary>Whether the install is v1.10's: whether its <c>VOL</c> folder holds <see cref="V110ArchiveName"/>.</summary>
	public static bool IsV110(string installRoot) {
		string archives = GameInstall.ArchiveDirectory(installRoot);
		return Directory.Exists(archives) && Directory.EnumerateFiles(archives, V110ArchiveName,
			new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Any();
	}

	/// <summary>
	/// VSHELL's language, <c>Shell_Language</c> (<c>0048227a</c>): <c>-f</c> or <c>-g</c> in either case from the
	/// launcher, and otherwise <see cref="ShellLanguageOption"/> from the install's <c>data\prefs.cfg</c>, which
	/// <c>EsGlobal_Init</c> (<c>004073bc</c>) copies in before the switches are parsed (docs/retail/command-line.md, "VSHELL").
	///
	/// <para>A value past German — option 43 above 2, or slot 3, which <c>-e</c> stores — gives no folder to
	/// <c>WeaponsBin_Open</c> (<c>00408605</c>) and no extension to <c>Msn_LoadEngText</c> (<c>0041768c</c>), so retail
	/// opens a path no archive holds. This engine reads English for it instead.</para>
	/// </summary>
	public static GameLanguage Shell(string installRoot) {
		if (Switch(installRoot) is { } letter) {
			switch ((char)letter) {
				case 'f' or 'F':
					return GameLanguage.French;
				case 'g' or 'G':
					return GameLanguage.German;
				case 'e':
					return GameLanguage.English;
			}
		}

		return SimulatorPreferences.Load(CaseInsensitivePath.Combine(installRoot, MissionLoader.DataFolderName))?[ShellLanguageOption] switch {
			1 => GameLanguage.French,
			2 => GameLanguage.German,
			_ => GameLanguage.English,
		};
	}

	/// <summary>
	/// DBSIM's language, the letter at <c>004d25ba</c>: <c>-F</c> or <c>-G</c> from the launcher, matched in that case
	/// only, and English otherwise (docs/retail/command-line.md, "DBSIM").
	/// </summary>
	public static GameLanguage Simulator(string installRoot) => Switch(installRoot) switch {
		(byte)'F' => GameLanguage.French,
		(byte)'G' => GameLanguage.German,
		_ => GameLanguage.English,
	};
}
