using Herculan.Engine.Content;
using Herculan.Engine.Editor;
using Herculan.Engine.Install;

// The mission editor on its own: find the install and run the editor (EditorHost) over it. The game runs the
// same editor as a turn of its own loop, and is built beside this one for Play to start (PlayInGame). The second
// argument names a mission to open, as File's mission lists name it (C1_05); without one the editor opens the mission
// it last had open.

string? installRoot = GameInstall.Locate(args.Length > 0 ? args[0] : null);
if (installRoot == null) {
	Console.Error.WriteLine(
		"Could not find an Earthsiege 2 installation.\n" +
		$"Pass its path as the first argument, or set {GameInstall.PathVariable}.\n" +
		$"The path should be the folder containing the '{GameInstall.ArchiveFolderName}' directory.");
	return 1;
}
if (GameInstall.IsOnDisc(installRoot)) {
	Console.Error.WriteLine($"{installRoot}: The CD cannot be used as the install location because save files cannot be written to the CD.");
	return 1;
}
GameInstall.Remember(installRoot);

GameContent? shellContent = null;
EditorHost.Run(new EditorLaunch(installRoot, GameContent.MountSimulator(installRoot),
	() => shellContent ??= GameContent.MountShell(installRoot),
	Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Open_Sans", "static", "OpenSans-Regular.ttf")) {
	Mission = args.Length > 1 ? args[1] : null,
	BoldFontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Open_Sans", "static", "OpenSans-Bold.ttf"),
	ItalicFontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Open_Sans", "static", "OpenSans-Italic.ttf"),
});

return 0;
