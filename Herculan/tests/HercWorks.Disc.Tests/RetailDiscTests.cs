using System.Security.Cryptography;
using Xunit;

namespace HercWorks.Disc.Tests;

/// <summary>
/// The v1.10 disc image at the repo root, when it is present: one raw file holding the data track and
/// the music after it, with no cue sheet. Its files are checked against <c>ES2v110\CD\</c>, the copy
/// <c>tools/scripts/es2_build_v110_installs.py</c> extracts.
/// </summary>
public class RetailDiscTests {
	private const string ImageName = "EarthSiege2_Freeware_GoldGames_1r11_withAudio.iso";

	[Fact]
	public void ReadsTheV110Disc() {
		if (FindAbove(ImageName) is not { } path) {
			return;
		}

		using var image = DiscImage.Open(path);

		// Mode 1 raw sectors; the data track's 207,041 volume sectors are followed by 152 more with a sync
		// pattern (its postgap), then audio to the end of the file.
		Assert.Equal(2, image.Tracks.Count);
		Assert.Equal(TrackFormat.Mode1Raw, image.Tracks[0].Format);
		Assert.Equal(207193, image.Tracks[0].SectorCount);
		Assert.True(image.Tracks[1].BoundaryUnknown);
		Assert.Equal(279988, image.EndLba);

		var fs = image.OpenFileSystem();
		Assert.Empty(fs.Problems);
		Assert.False(fs.IsJoliet);
		Assert.Equal("EARTHSIEGE2", fs.VolumeIdentifier);
		Assert.True(fs.FileExists(@"vol\simvol0.vol"));

		if (FindAbove(Path.Combine("ES2v110", "CD", "VOL")) is not { } extracted) {
			return;
		}

		string[] copies = Directory.GetFiles(extracted);
		Assert.NotEmpty(copies);
		foreach (string copy in copies) {
			using var stream = fs.OpenRead("VOL/" + Path.GetFileName(copy));
			Assert.Equal(SHA256.HashData(File.ReadAllBytes(copy)), SHA256.HashData(stream));
		}
	}

	private static string? FindAbove(string relative) {
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent) {
			string candidate = Path.Combine(directory.FullName, relative);
			if (File.Exists(candidate) || Directory.Exists(candidate)) {
				return candidate;
			}
		}

		return null;
	}
}
