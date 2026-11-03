using System.IO.Compression;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Msn;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="MissionBundle"/>'s own zone: what Export Mission Bundle writes reads back, and half a zone is refused rather
/// than read as none. Constructed bundles only, so every case runs without an install.
/// </summary>
public class MissionBundleTests {
	private static readonly string[] TextExtensions = [".ENG"];

	/// <summary>
	/// Catches a bundle written without its zone's entries, or under extensions the reader does not take as the zone's.
	/// </summary>
	[Fact]
	public void AWrittenZoneReadsBackFromTheBundle() {
		var zone = ZoneFiles.Create(Heights(128), 14, 149);

		var bundle = MissionBundle.Read(Bundle(zone), TextExtensions, out string? failure);

		Assert.Null(failure);
		Assert.Equal(zone.Heights.ToArray(), bundle!.Zone!.Heights.ToArray());
		Assert.Equal(zone.CellSizeShift, bundle.Zone.CellSizeShift);
		Assert.Equal(zone.HeightScale, bundle.Zone.HeightScale);
	}

	/// <summary>
	/// Catches a bundle holding a zone header without its heightmap read as a mission with no zone, which would import it and
	/// play it on the archive's terrain, and a failure that does not name the missing file.
	/// </summary>
	[Fact]
	public void AZoneHeaderWithoutItsHeightmapIsRefusedNamingIt() {
		byte[] bytes = Bundle(ZoneFiles.Create(Heights(128), 14, 149));
		using (var buffer = new MemoryStream()) {
			buffer.Write(bytes);
			using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true)) {
				archive.GetEntry("MINE" + ZoneFiles.HeightmapExtension)!.Delete();
			}

			bytes = buffer.ToArray();
		}

		Assert.Null(MissionBundle.Read(bytes, TextExtensions, out string? failure));
		Assert.Contains("MINE" + ZoneFiles.HeightmapExtension, failure);
	}

	private static byte[] Bundle(ZoneFiles zone) =>
		MissionBundle.Write("MINE", [1], new Dictionary<string, byte[]> { [".ENG"] = [2] }, null, zone);

	private static byte[] Heights(int side) {
		var heights = new byte[side * side];
		new Random(side).NextBytes(heights);
		return heights;
	}
}
