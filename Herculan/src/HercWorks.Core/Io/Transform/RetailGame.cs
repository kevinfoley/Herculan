namespace HercWorks.Core.Io.Transform;

/// <summary>
/// Which game an install holds, which decides the reader <see cref="TransformerRegistry"/> binds to an archive entry:
/// the two games share archive names, extensions and headers but not every layout behind them
/// (docs/herculan/plan-es1-support.md#what-hercworks-reads).
/// </summary>
public enum RetailGame {
	/// <summary>Earthsiege 2, whose install root holds <c>VSHELL.EXE</c>.</summary>
	Earthsiege2,

	/// <summary>Metaltech: Earthsiege, whose install root holds <c>GO.EXE</c> or <c>RESOURCE.CFG</c>.</summary>
	Earthsiege1,
}

/// <summary>Tells the games' installs apart by the files each keeps in its root.</summary>
public static class RetailGameDetector {
	/// <summary>ES1's shell, in the root of a full install and of its disc.</summary>
	public const string Es1Marker = "GO.EXE";

	/// <summary>
	/// The Sierra resource file every size of ES1's install copies into its root from the disc's <c>STUFF</c>, the
	/// minimum and medium sizes included, which leave <see cref="Es1Marker"/> on the disc
	/// (docs/es1/shell/launcher.md#the-installers-esbat). No Earthsiege 2 install holds one.
	/// </summary>
	public const string Es1ResourceMarker = "RESOURCE.CFG";

	/// <summary>
	/// <see cref="RetailGame.Earthsiege1"/> when the install root holds <see cref="Es1Marker"/> or
	/// <see cref="Es1ResourceMarker"/>, in any case; otherwise <see cref="RetailGame.Earthsiege2"/>, whether it holds
	/// <c>VSHELL.EXE</c> or neither, so a folder that is not an install keeps ES2's readers.
	/// </summary>
	/// <param name="installRoot">The install's folder, or null.</param>
	public static RetailGame Detect(string? installRoot) =>
		installRoot != null && Directory.Exists(installRoot) && (Holds(installRoot, Es1Marker) || Holds(installRoot, Es1ResourceMarker))
			? RetailGame.Earthsiege1
			: RetailGame.Earthsiege2;

	/// <summary>
	/// The game of the install a <c>.VOL</c> belongs to: the parent of the folder holding the archive,
	/// where an install keeps its <c>VOL</c> folder.
	/// </summary>
	/// <param name="archivePath">The archive's path, or null.</param>
	public static RetailGame DetectFromArchive(string? archivePath) =>
		Detect(string.IsNullOrEmpty(archivePath) ? null : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(archivePath))));

	private static bool Holds(string folder, string name) =>
		Directory.EnumerateFiles(folder, name, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Any();
}
