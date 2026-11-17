using Herculan.Engine.Games;

namespace Herculan.Engine.Host;

/// <summary>
/// The command line's <c>--help</c> text: the host's own flags, then each game's section (<see cref="IGameModule.Usage"/>).
/// <see cref="HostOptions"/> owns the flags themselves, and <see cref="OptionValues"/> reads their values;
/// docs/herculan/herculan-command-line.md describes each one.
/// </summary>
static class HostArguments {
	public static bool IsHelp(string arg) => arg is "--help" or "-h" or "-?" or "/?";

	/// <summary>The <c>--help</c> text for a host running <paramref name="games"/>.</summary>
	public static string Usage(IReadOnlyList<IGameModule> games) {
		string variables = string.Join(" or ", games.Select(game => game.Identity.PathVariable));
		string folders = string.Join(" or ", games.Select(game => game.Identity.FolderName));
		string head = $"""
			Usage: Herculan.Engine.Host [<install>] [<mission>] [options]

			  <install>   the install of any game below, whichever game it holds runs; default the game whose own option
			              is given, else a window asking which game, unless "Don't ask again" was ticked there; then
			              that game's {variables},
			              the last one used, an {folders} folder above the executable, then a window asking for it
			  <mission>   a mission to fly, as the game reads one: a .MSN name (C1_03, SM_SCR01), or for Earthsiege 2 a
			              script.dat with its mission.var and player.mec; default the front end

			What runs (default: the front end)
			  --mission                   one mission, without the front end; a named <mission> implies it
			  --play <tape>               replay an input tape, by path or TAPES folder stem; implies --mission
			  --demo                      a demo tape from TAPES\demolist.str, as VIEW DEMO plays it; implies --mission
			  --record <tape>             record the mission's input to <tape>.tap

			Every game
			  --no-sound, --silent        no audio device
			  --ask-install               ask for the game's install at once, and remember none
			  --save-prtscn               also save each full-screen [PrtScn] capture to <install>\Screenshots
			  --screenshot <file>         capture after 30 frames (or what a game's staging waits for), then exit
			  --help, -h, -?              this text
			""";
		const string tail = "Every option is described in Herculan/docs/herculan/herculan-command-line.md.";
		string blank = Environment.NewLine + Environment.NewLine;
		return string.Join(blank, [head, .. games.Select(game => game.Usage), tail]);
	}
}
