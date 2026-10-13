using System.Text;
using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's sound files through the Core readers ES1's table binds to them: the headerless <c>.SFX</c> and <c>.RAW</c> samples
/// (docs/es1/formats/sound-samples.md) and the <c>.HMP</c> songs (docs/es1/formats/hmp-music.md). Each test passes vacuously
/// when the ES1 archives are absent.
/// </summary>
public class Es1SoundFilesTests {
	private static readonly Lazy<IReadOnlyList<VolEntry>?> Entries = new(() => {
		var vols = new[] { "SIMSOUND.VOL", "SIMVOICE.VOL", "SHELL0.VOL", "SPEECH.VOL" }.Select(Load).ToList();
		return vols.Any(v => v == null) ? null : vols.SelectMany(v => v!).ToList();
	});

	/// <summary>Every <c>.SFX</c> and <c>.RAW</c> takes the sample reader and gives back its bytes.</summary>
	[Fact]
	public void EverySampleRoundTrips() {
		if (Entries.Value is not { } all) {
			return;
		}

		var effects = all.Where(e => HasExtension(e, ".SFX")).ToList();
		var raws = all.Where(e => HasExtension(e, ".RAW")).ToList();
		Assert.Equal((202, 175), (effects.Count, raws.Count));
		Assert.All(effects.Concat(raws), e => {
			var reader = Assert.IsType<Es1SoundSampleTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1));
			Assert.Equal(e.RawBytes, reader.Write(reader.Parse(e.RawBytes)!));
		});
	}

	/// <summary>
	/// Every <c>.HMP</c> walks the song loader's layout to its end and round-trips, with the header bytes the model's comments
	/// call fixed as they say, the tracks numbered in order and ending at the branch table, and each branch entry's pointer slot
	/// holding its offset into the bytes after the entries.
	/// </summary>
	[Fact]
	public void EverySongWalksTheLoadersLayout() {
		if (Entries.Value is not { } all) {
			return;
		}

		var songs = all.Where(e => HasExtension(e, ".HMP")).ToList();
		Assert.Equal(19, songs.Count);
		byte[] signature = [.. Encoding.ASCII.GetBytes("HMIMIDIP"), .. new byte[24]];
		Assert.All(songs, e => {
			var reader = Assert.IsType<Es1HmpSongTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1));
			var song = reader.Parse(e.RawBytes)!;
			Assert.Equal(e.RawBytes, reader.Write(song));
			Assert.Equal(signature, song.Signature);
			Assert.Equal(new byte[8], song.CallbackSlot);
			Assert.Empty(song.AfterTracks);
			Assert.Equal(Enumerable.Range(0, song.TrackCount), song.Tracks.Select(t => t.ChunkNumber));
			int dataStart = e.RawBytes!.Length - song.BranchData.Length;
			Assert.All(song.Branches, b => {
				Assert.Equal(b.DataOffset, b.PointerSlot);
				Assert.InRange(b.DataOffset, dataStart, e.RawBytes.Length);
			});
		});
	}

	private static bool HasExtension(VolEntry e, string extension) =>
		e.FileName != null && e.FileName.Trim().EndsWith(extension, StringComparison.OrdinalIgnoreCase);

	private static IReadOnlyList<VolEntry>? Load(string archive) {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES1", "VOL", archive);
			if (File.Exists(vol)) {
				return VolFileReader.ParseVolFile(vol).FilesSet.ToList();
			}
		}

		return null;
	}
}
