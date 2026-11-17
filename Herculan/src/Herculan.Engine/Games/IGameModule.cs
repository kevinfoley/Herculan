using Herculan.Engine.Localization;
using Herculan.Engine.Settings;

namespace Herculan.Engine.Games;

/// <summary>
/// One game the application runs, as its assembly presents it to the host: who it is, the command-line flags only it
/// takes, the accessibility settings it adds to the file every game shares, how it asks for its install, and its run. The
/// host holds a list of them and never tests which game it holds (docs/herculan/planning.md#how-the-two-games-share-code).
/// This engine's own; retail has no counterpart.
/// </summary>
public interface IGameModule {
	GameIdentity Identity { get; }

	/// <summary>
	/// The game's own accessibility settings, which the host's <see cref="A11ySettings"/> holds beside the core's and
	/// every other game's whichever game runs, so a save keeps them all.
	/// </summary>
	IReadOnlyList<A11ySettingDefinition<bool>> AccessibilitySettings { get; }

	/// <summary>
	/// Takes the flag at <c>args[i]</c> when it is one of this game's own, with its values, leaving <paramref name="i"/>
	/// on the last argument it took and recording a malformed value in <paramref name="errors"/>; false, with
	/// <paramref name="i"/> unchanged, for a flag that is not this game's. <see cref="OptionValues"/> reads the values.
	/// </summary>
	bool TryParseOption(string[] args, ref int i, List<string> errors);

	/// <summary>
	/// The first of this game's own flags the command line gave, as given, or null for none. A launch with no install
	/// named runs the game whose flag was given, and an install of another game refuses it.
	/// </summary>
	string? FirstOwnOption { get; }

	/// <summary>Records in <paramref name="errors"/> what the parsed flags cannot do together, once parsing ends.</summary>
	void CheckOptions(CommonOptions options, List<string> errors);

	/// <summary>
	/// Runs what the game's flags ask for that needs no install, and so runs before any is looked for, returning its exit
	/// code; null when they ask for nothing of the kind.
	/// </summary>
	int? RunWithoutInstall();

	/// <summary>
	/// The game's install prompt, opened when its install was not found or <c>--ask-install</c> skipped the search: the
	/// install root chosen, or null when the player gave up. <paramref name="refused"/> is a folder found that cannot be
	/// the install, which the prompt opens on with the reason showing; <paramref name="dismissKey"/> is the string of the
	/// button that gives up.
	/// </summary>
	string? AskForInstall(LocalizationTable localization, string fontPath, string? refused, string dismissKey);

	/// <summary>
	/// Runs the game on <see cref="GameRunContext.InstallRoot"/>, loading its tweak settings first, until its run ends or
	/// a <see cref="Install.GameSwitch"/> request ends it. Returns the exit code.
	/// </summary>
	int Run(GameRunContext context);

	/// <summary>
	/// The install the game's run is on now, which its Settings window may have changed since the run started; null while
	/// it is not running. <c>--save-prtscn</c> writes there.
	/// </summary>
	string? ActiveInstall { get; }

	/// <summary>The game's section of <c>--help</c>: its own flags, each on a line.</summary>
	string Usage { get; }
}
