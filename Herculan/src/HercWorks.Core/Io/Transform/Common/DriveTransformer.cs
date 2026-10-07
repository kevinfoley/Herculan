using System.Text;
using HercWorks.Core.Data.File.Cfg;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes <c>data\drive.cfg</c> (<see cref="Drive"/>). Both executables read the disc's directory
/// with <c>fscanf("%s")</c>, so it ends at the first whitespace; the install's, the next line, they read the
/// same way and discard, so it is kept whole here. <see cref="Write"/> puts each on a line of its own, as the
/// installer's <c>BATCH.EXE</c> does, and refuses a disc directory the readers would cut.
///
/// <para>A line starting <see cref="ImagePrefix"/>, anywhere after the first token, is HERCULAN's
/// <see cref="Drive.DiscImage"/>. It is always written after the install's line, so retail's second read takes
/// the install and never reaches it. Its path is UTF-8 and may hold spaces; the two retail lines stay
/// single-byte.</para>
/// </summary>
public class DriveTransformer : ByteTransformer<Drive> {
	/// <summary>What starts the line naming <see cref="Drive.DiscImage"/>.</summary>
	public const string ImagePrefix = "HERCULAN-IMAGE=";

	public override Drive? Parse(byte[]? bytes) {
		if (bytes == null) {
			return null;
		}

		// Latin-1 maps byte for byte, so an index into the text is an offset into the bytes.
		string text = Encoding.Latin1.GetString(bytes);
		int start = 0;
		while (start < text.Length && char.IsWhiteSpace(text[start])) {
			start++;
		}

		int end = start;
		while (end < text.Length && !char.IsWhiteSpace(text[end])) {
			end++;
		}

		var drive = new Drive { Directory = end > start ? text[start..end] : null };
		for (int lineStart = end; lineStart < text.Length;) {
			int lineEnd = text.IndexOf('\n', lineStart);
			if (lineEnd < 0) {
				lineEnd = text.Length;
			}

			string line = text[lineStart..lineEnd].Trim();
			if (line.StartsWith(ImagePrefix, StringComparison.Ordinal)) {
				int offset = text.IndexOf(ImagePrefix, lineStart, StringComparison.Ordinal) + ImagePrefix.Length;
				string image = Encoding.UTF8.GetString(bytes, offset, lineEnd - offset).Trim();
				drive.DiscImage ??= image.Length == 0 ? null : image;
			} else if (line.Length > 0) {
				drive.InstallDirectory ??= line;
			}

			lineStart = lineEnd + 1;
		}

		return drive;
	}

	/// <exception cref="ArgumentException">
	/// The disc's directory holds whitespace, which the readers would cut it at; or there is a disc image but no
	/// disc directory and install directory to put before it, so retail's second read would take the image's
	/// line; or a path holds a line break.
	/// </exception>
	public override byte[]? Write(Drive source) {
		if (HasWhitespace(source.Directory)) {
			throw new ArgumentException("drive.cfg cannot hold a disc directory with whitespace in it.", nameof(source));
		}

		if (source.InstallDirectory?.IndexOfAny(['\r', '\n']) >= 0 || source.DiscImage?.IndexOfAny(['\r', '\n']) >= 0) {
			throw new ArgumentException("drive.cfg cannot hold a path with a line break in it.", nameof(source));
		}

		if (source.DiscImage != null && (source.Directory == null || source.InstallDirectory == null)) {
			throw new ArgumentException("drive.cfg needs the disc and install directories before a disc image.", nameof(source));
		}

		string text = source.Directory ?? string.Empty;
		if (source.InstallDirectory != null) {
			text += "\r\n" + source.InstallDirectory;
		}

		byte[] retail = Encoding.Latin1.GetBytes(text);
		return source.DiscImage == null
			? retail
			: [.. retail, .. Encoding.UTF8.GetBytes("\r\n" + ImagePrefix + source.DiscImage)];
	}

	/// <summary>Whether <paramref name="path"/> holds a character <c>fscanf("%s")</c> would stop at.</summary>
	public static bool HasWhitespace(string? path) => path != null && path.Any(char.IsWhiteSpace);
}
