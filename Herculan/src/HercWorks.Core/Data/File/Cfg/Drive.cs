namespace HercWorks.Core.Data.File.Cfg;

/// <summary>
/// <c>data\drive.cfg</c> — the disc's directory, the file's first whitespace-separated token, which both
/// executables search for archives and put in front of the movie, manual and voice-clip paths; then the
/// install's, which they read and discard. Read and written by
/// <see cref="Io.Transform.Common.DriveTransformer"/>. See docs/retail/formats/vol-archive.md, "Which archives are
/// mounted", and docs/retail/retail-builds.md, "The installer".
/// </summary>
public class Drive {
	/// <summary>Where the game keeps the file, relative to its <c>data</c> folder.</summary>
	public const string FileName = "drive.cfg";

	/// <summary>The disc's directory, or null when the file holds no token.</summary>
	public string? Directory { get; set; }

	/// <summary>The install's directory, the second token, or null when there is none.</summary>
	public string? InstallDirectory { get; set; }

	/// <summary>
	/// A disc image (<c>.iso</c>, <c>.bin</c>, <c>.cue</c>) to read in place of <see cref="Directory"/>, or null.
	/// Not retail's: HERCULAN keeps it on a line of its own after the install's, prefixed
	/// <see cref="Io.Transform.Common.DriveTransformer.ImagePrefix"/>. Both retail programs stop reading the file
	/// after its second token (docs/retail/formats/vol-archive.md, "Which archives are mounted"), so the original game
	/// never sees it and still reads <see cref="Directory"/> as its disc.
	/// </summary>
	public string? DiscImage { get; set; }
}
