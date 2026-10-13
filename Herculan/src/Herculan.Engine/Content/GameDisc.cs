using HercWorks.Core.Io;
using HercWorks.Disc;
using HercWorks.Disc.Iso9660;
using Herculan.Engine.Install;

namespace Herculan.Engine.Content;

/// <summary>
/// The Earthsiege 2 disc, as both programs reach it through <c>data\drive.cfg</c>: the <c>VOL</c> archives an
/// install did not copy, the movies, the on-line manual and the training instructor's clips
/// (docs/retail/retail-builds.md, "The installer"). Retail's disc is always a directory; here it is either a directory
/// (<see cref="OpenFolder"/>) or a CD image read in place (<see cref="OpenImage"/>), which is this engine's own
/// (<see cref="GameInstall.OpenDisc"/> says which an install names).
///
/// <para>Paths are relative to the disc's root, <c>\</c> or <c>/</c> separated, and matched ignoring case on an
/// image, as ISO 9660 names are. An image is untrusted input; <see cref="DiscImage"/> bounds-checks everything it
/// reads from one, and nothing read from a disc is executed.</para>
/// </summary>
public abstract class GameDisc : IDisposable {
	/// <summary>The directory or image file the disc is read from.</summary>
	public abstract string Location { get; }

	/// <summary>The image, when the disc is one; null for a directory.</summary>
	public virtual DiscImage? Image => null;

	/// <summary>Whether <paramref name="relativePath"/> names a file on the disc.</summary>
	public abstract bool FileExists(string relativePath);

	/// <summary>Whether <paramref name="relativePath"/> names a folder on the disc.</summary>
	public abstract bool DirectoryExists(string relativePath);

	/// <summary>Opens <paramref name="relativePath"/> as a seekable, read-only stream, or answers null when there is no such file.</summary>
	public abstract Stream? OpenRead(string relativePath);

	/// <summary>
	/// The names of the files in the disc's <c>VOL</c> folder matching <see cref="GameContent.ArchivePattern"/>,
	/// in no particular order.
	/// </summary>
	public abstract IReadOnlyList<string> ArchiveNames();

	/// <summary>
	/// The names of the files directly in the disc folder <paramref name="relativeDirectory"/>, in no particular
	/// order; empty when there is no such folder.
	/// </summary>
	public abstract IReadOnlyList<string> FileNames(string relativeDirectory);

	/// <summary>Where <paramref name="relativePath"/> is, for a message.</summary>
	public abstract string Describe(string relativePath);

	/// <summary>A directory, which must exist.</summary>
	public static GameDisc OpenFolder(string directory) => new FolderDisc(Path.GetFullPath(directory));

	/// <summary>
	/// A CD image (<c>.iso</c>, <c>.bin</c> or <c>.cue</c>) opened through <see cref="DiscImage.Open"/>, whose data
	/// track's file system is read once here.
	/// </summary>
	/// <exception cref="DiscFormatException">The image is malformed or has no readable file system.</exception>
	/// <exception cref="IOException">The image, or a file its cue sheet names, cannot be opened.</exception>
	public static GameDisc OpenImage(string path) {
		var image = DiscImage.Open(path);
		try {
			return new ImageDisc(image, image.OpenFileSystem());
		} catch {
			image.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Reads <paramref name="relativePath"/> whole, or answers null when it is absent or longer than
	/// <paramref name="maxBytes"/>. The length is checked before anything is allocated.
	/// </summary>
	public byte[]? ReadAllBytes(string relativePath, long maxBytes) {
		using var stream = OpenRead(relativePath);
		if (stream == null || stream.Length > maxBytes) {
			return null;
		}

		var bytes = new byte[stream.Length];
		stream.ReadExactly(bytes);
		return bytes;
	}

	/// <inheritdoc />
	public virtual void Dispose() => GC.SuppressFinalize(this);

	private sealed class FolderDisc(string directory) : GameDisc {
		public override string Location => directory;

		public override bool FileExists(string relativePath) => File.Exists(CaseInsensitivePath.Combine(directory, relativePath));

		public override bool DirectoryExists(string relativePath) => Directory.Exists(CaseInsensitivePath.Combine(directory, relativePath));

		public override Stream? OpenRead(string relativePath) {
			string path = CaseInsensitivePath.Combine(directory, relativePath);
			return File.Exists(path) ? File.OpenRead(path) : null;
		}

		public override IReadOnlyList<string> ArchiveNames() {
			string archives = CaseInsensitivePath.Combine(directory, GameInstall.ArchiveFolderName);
			return Directory.Exists(archives)
				? Directory.GetFiles(archives, GameContent.ArchivePattern, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
					.Select(Path.GetFileName).OfType<string>().ToList()
				: [];
		}

		public override IReadOnlyList<string> FileNames(string relativeDirectory) {
			string folder = CaseInsensitivePath.Combine(directory, relativeDirectory);
			return Directory.Exists(folder)
				? Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>().ToList()
				: [];
		}

		public override string Describe(string relativePath) => Path.Combine(directory, relativePath);
	}

	private sealed class ImageDisc(DiscImage image, IsoFileSystem fileSystem) : GameDisc {
		public override string Location => image.Path;

		public override DiscImage Image => image;

		public override bool FileExists(string relativePath) => fileSystem.FileExists(relativePath);

		public override bool DirectoryExists(string relativePath) => fileSystem.DirectoryExists(relativePath);

		public override Stream? OpenRead(string relativePath) =>
			fileSystem.Find(relativePath) is { IsDirectory: false } file ? fileSystem.OpenRead(file) : null;

		public override IReadOnlyList<string> ArchiveNames() =>
			fileSystem.Find(GameInstall.ArchiveFolderName) is { IsDirectory: true } archives
				? archives.Children
					.Where(entry => !entry.IsDirectory && entry.Name.EndsWith(".vol", StringComparison.OrdinalIgnoreCase))
					.Select(entry => entry.Name)
					.ToList()
				: [];

		public override IReadOnlyList<string> FileNames(string relativeDirectory) =>
			fileSystem.Find(relativeDirectory) is { IsDirectory: true } folder
				? folder.Children.Where(entry => !entry.IsDirectory).Select(entry => entry.Name).ToList()
				: [];

		public override string Describe(string relativePath) => $"{relativePath} in {image.Path}";

		public override void Dispose() {
			image.Dispose();
			base.Dispose();
		}
	}
}
