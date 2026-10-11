namespace HercWorks.Core.Io.Transform;

/// <summary>
/// Which game an install holds, which decides the reader <see cref="TransformerRegistry"/> binds to an archive entry:
/// the two games share archive names, extensions and headers but not every layout behind them
/// (docs/herculan/plan-es1-support.md#what-hercworks-reads).
/// </summary>
public enum RetailGame {
	/// <summary>Earthsiege 2, whose install root holds <c>VSHELL.EXE</c>.</summary>
	Earthsiege2,

	/// <summary>Metaltech: Earthsiege, whose install root holds <c>GO.EXE</c>.</summary>
	Earthsiege1,
}

/// <summary>Tells the games' installs apart by the executable each keeps in its root.</summary>
public static class RetailGameDetector {
	/// <summary>ES1's launcher, in its install root.</summary>
	public const string Es1Marker = "GO.EXE";

	/// <summary>
	/// <see cref="RetailGame.Earthsiege1"/> when <paramref name="installRoot"/> holds <see cref="Es1Marker"/>, in any
	/// case; otherwise <see cref="RetailGame.Earthsiege2"/>, whether it holds <c>VSHELL.EXE</c> or neither, so a
	/// folder that is not an install keeps ES2's readers.
	/// </summary>
	public static RetailGame Detect(string? installRoot) =>
		installRoot != null && Directory.Exists(installRoot)
			&& Directory.EnumerateFiles(installRoot, Es1Marker, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Any()
			? RetailGame.Earthsiege1
			: RetailGame.Earthsiege2;

	/// <summary>
	/// The game of the install a <c>.VOL</c> belongs to: the parent of the folder holding <paramref name="archivePath"/>,
	/// where an install keeps its <c>VOL</c> folder.
	/// </summary>
	public static RetailGame DetectFromArchive(string? archivePath) =>
		Detect(string.IsNullOrEmpty(archivePath) ? null : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(archivePath))));
}
