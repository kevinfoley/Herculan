using HercWorks.Core.Io.Transform;

namespace Herculan.Engine.Games;

/// <summary>
/// What the application needs to know of a game before it runs: which game an install holds, and the names its install
/// lookup, its install prompt and the menus that name it go by. Each game assembly holds its own, which its
/// <see cref="IGameModule"/> hands the host. This engine's own; retail has no counterpart.
/// </summary>
/// <param name="Game">The game, as <see cref="RetailGameDetector"/> tells an install of it.</param>
/// <param name="NameKey">The string of the game's name, as the start-up chooser and the "Game" menu show it.</param>
/// <param name="FolderName">
/// The folder <see cref="Install.RetailInstalls.Locate"/> looks for beside the running binary and in each folder above it.
/// </param>
/// <param name="PathVariable">The environment variable that names the game's install, which the lookup checks first.</param>
/// <param name="RememberedPathFileName">
/// The file in <see cref="Platform.UserConfig.Folder"/> that remembers the game's last install, one line, the path.
/// </param>
/// <param name="PromptKeyPrefix">
/// The prefix of the game's own install prompt strings (<see cref="Install.InstallPromptWindow"/>).
/// </param>
public sealed record GameIdentity(RetailGame Game, string NameKey, string FolderName, string PathVariable,
	string RememberedPathFileName, string PromptKeyPrefix);
