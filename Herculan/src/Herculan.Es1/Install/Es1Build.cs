using Herculan.Engine.Content;
using Herculan.Engine.Localization;

namespace Herculan.Es1.Install;

/// <summary>
/// Which retail build of Metaltech: Earthsiege an install holds: the base game, or the base game with the expansion
/// pack, Earthsiege Battleset 1, installed over it.
///
/// <para>The expansion is told by <see cref="ExpansionArchiveName"/> in the install's <c>VOL</c> folder, which holds
/// battle set 1. Of the expansion disc's installer (<c>INSTALL\ENGLISH\INSTALL.SCR</c>), only the <c>maxInst</c> branch
/// copies the archive; its minimum and medium layouts leave it on the disc, so their installs are taken for base ones.
/// Its <c>SIMPATCH.VOL</c> and <c>PSHELL1.VOL</c> are not used to tell it, since the same script takes them for the v1.1
/// patch's on an install without the expansion.</para>
///
/// <para>This engine does not run the expansion yet, so an install that holds it is refused as an ES1 install
/// (<see cref="InstallRefusal"/>).</para>
/// </summary>
public static class Es1Build {
	/// <summary>The archive only an install with the expansion holds.</summary>
	public const string ExpansionArchiveName = "PZONES1.VOL";

	/// <summary>Whether the expansion is installed in <paramref name="installRoot"/>: whether its <c>VOL</c> folder holds <see cref="ExpansionArchiveName"/>.</summary>
	/// <param name="installRoot">The install's root folder.</param>
	public static bool IsExpansion(string installRoot) {
		string archives = GameContent.ArchiveDirectory(installRoot);
		return Directory.Exists(archives) && Directory.EnumerateFiles(archives, ExpansionArchiveName,
			new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Any();
	}

	/// <summary>
	/// <see cref="Host.Es1GameModule.Identity"/>'s install refusal: why <paramref name="installRoot"/> cannot be used when it
	/// holds the expansion (<see cref="IsExpansion"/>), in the player's language, otherwise null.
	/// </summary>
	/// <param name="localization">The table the reason is read from.</param>
	/// <param name="installRoot">An install root of Metaltech: Earthsiege.</param>
	public static string? InstallRefusal(LocalizationTable localization, string installRoot) =>
		IsExpansion(installRoot)
			? string.Format(localization.GetStringOrKey("es1_install_prompt.expansion"), installRoot, ExpansionArchiveName)
			: null;
}
