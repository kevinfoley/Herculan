using Herculan.Engine.Content;
using Herculan.Engine.Editor;
using Herculan.Engine.Install;
using Herculan.Engine.World;

// The mission editor on its own: find the install and run the editor (EditorHost) over it. The game runs the
// same editor as a turn of its own loop.

string? installRoot = GameInstall.Locate(args.Length > 0 ? args[0] : null);
if (installRoot == null) {
	Console.Error.WriteLine(
		"Could not find an Earthsiege 2 installation.\n" +
		$"Pass its path as the first argument, or set {GameInstall.PathVariable}.\n" +
		$"The path should be the folder containing the '{GameInstall.ArchiveFolderName}' directory.");
	return 1;
}
GameInstall.Remember(installRoot);

// A mission named on the command line has to be there; without one the editor opens DATA\script.dat, or no mission
// when there is none.
string? scriptPath = args.Length > 1 ? args[1] : null;
if (scriptPath != null && !File.Exists(scriptPath)) {
	Console.Error.WriteLine(
		$"No mission at {scriptPath}.\n" +
		$"Pass one as the second argument — {MissionLoader.ScriptFileName} from the install's " +
		$"{MissionLoader.DataFolderName} folder, with the {MissionLoader.CountersFileName} and " +
		$"{MissionLoader.PlayerFileName} beside it.");
	return 1;
}

GameContent? shellContent = null;
EditorHost.Run(new EditorLaunch(installRoot, GameContent.MountSimulator(installRoot),
	() => shellContent ??= GameContent.MountShell(installRoot),
	Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Open_Sans", "static", "OpenSans-Regular.ttf")) {
	ScriptPath = scriptPath,
	BoldFontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Open_Sans", "static", "OpenSans-Bold.ttf"),
});

return 0;
