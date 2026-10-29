using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io.Transform.Dbsim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="Es1InputTapeTransformer"/> over the retail tapes in <c>ES1/TAPES</c> (docs/es1/formats/tap-input-tape.md#the-frames).
/// The tests pass vacuously when that folder is absent, so the suite does not depend on the install.
/// </summary>
public class Es1InputTapeTests {
	private static readonly Lazy<IReadOnlyList<string>?> Tapes = new(Find);

	/// <summary>
	/// Every retail tape goes through the reader and back to its bytes. Catches a frame field read or written out of order
	/// or at the wrong size, the bundle's read or write changed, and a record dropped or cut short.
	/// </summary>
	[Fact]
	public void EveryRetailTapeWritesBackByteIdentical() {
		if (Tapes.Value is not { } tapes) {
			return;
		}

		var transformer = new Es1InputTapeTransformer();
		foreach (string path in tapes) {
			byte[] bytes = File.ReadAllBytes(path);
			var tape = Assert.IsType<Es1InputTape>(transformer.Parse(bytes));
			Assert.True(bytes.AsSpan().SequenceEqual(transformer.Write(tape)), $"{Path.GetFileName(path)} does not round-trip");
		}
	}

	/// <summary>
	/// Every retail tape's model accounts for each of its bytes: the bundle's sizes and files, then each frame's fixed part
	/// and mouse events, end exactly at the file's end. Catches the trailing-record rule dropping a whole record, and a
	/// fixed part or event size that parses to a different end, independently of the writer.
	/// </summary>
	[Fact]
	public void EveryRetailTapeParsesToItsLastByte() {
		if (Tapes.Value is not { } tapes) {
			return;
		}

		foreach (string path in tapes) {
			byte[] bytes = File.ReadAllBytes(path);
			var tape = new Es1InputTapeTransformer().Parse(bytes)!;
			long length = tape.Bundle.Sum(file => 4L + file.Length)
				+ tape.Frames.Sum(frame => Es1InputTapeTransformer.FrameFixedSize
					+ (long)frame.MouseEvents.Count * Es1InputTapeTransformer.MouseEventSize);
			Assert.True(length == bytes.Length, $"{Path.GetFileName(path)}: the model ends at {length} of {bytes.Length}");
		}
	}

	private static IReadOnlyList<string>? Find() {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string folder = Path.Combine(at.FullName, "ES1", "TAPES");
			if (!Directory.Exists(folder)) {
				continue;
			}

			return Directory.EnumerateFiles(folder)
				.Where(file => Path.GetExtension(file).Equals(".TAP", StringComparison.OrdinalIgnoreCase))
				.ToList();
		}

		return null;
	}
}
