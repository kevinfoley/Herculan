using Herculan.Engine.Content;
using Herculan.Engine.Editor;
using Herculan.Engine.Install;

// The mission editor on its own: find the install and run the editor (EditorHost) over it. The game runs the
// same editor as a turn of its own loop. The second argument names a mission to open, as File > Load from VOL lists
// it (C1_05); without one the editor starts with that list up.

string? installRoot = GameInstall.Locate(args.Length > 0 ? args[0] : null);
if (installRoot == null) {
	Console.Error.WriteLine(
		"Could not find an Earthsiege 2 installation.\n" +
		$"Pass its path as the first argument, or set {GameInstall.PathVariable}.\n" +
		$"The path should be the folder containing the '{GameInstall.ArchiveFolderName}' directory.");
	return 1;
}
GameInstall.Remember(installRoot);

GameContent? shellContent = null;
EditorHost.Run(new EditorLaunch(installRoot, GameContent.MountSimulator(installRoot),
	() => shellContent ??= GameContent.MountShell(installRoot),
	Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Open_Sans", "static", "OpenSans-Regular.ttf")) {
	Mission = args.Length > 1 ? args[1] : null,
	BoldFontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Open_Sans", "static", "OpenSans-Bold.ttf"),
});

return 0;
