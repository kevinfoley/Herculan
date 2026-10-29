using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Util;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="ZoneFiles"/>, a mission's own terrain zone: what it writes reads back as built, and a pair that is not a zone
/// is refused with a reason rather than read or thrown on, since a bundle's files come from anyone. Constructed zones
/// only, so every case runs without an install.
/// </summary>
public class ZoneFilesTests {
	// Byte offsets the malformed cases patch: the header's four int32s, and the heightmap's int32 frame count.
	private const int HeaderWidthShift = 0;
	private const int HeaderCellSizeShift = 8;
	private const int HeaderHeightScale = 12;
	private const int HeightmapFrameCount = 8;

	/// <summary>
	/// A written pair reads back as the zone it was built from. Catches a wrong envelope size or frame layout in
	/// <see cref="ZoneFiles.WriteHeightmap"/>, header shifts that do not match the side, and a change to the order of
	/// <see cref="ZoneFiles.Heights"/>.
	/// </summary>
	[Theory]
	[InlineData(128, 14, 149)]
	[InlineData(256, 12, 256)]
	public void AWrittenZoneReadsBackAsBuilt(int side, int cellSizeShift, int heightScale) {
		var heights = Heights(side);
		var built = ZoneFiles.Create(heights, cellSizeShift, heightScale);

		var read = ZoneFiles.Read(built.WriteHeader(), built.WriteHeightmap(), out var failure);

		Assert.Null(failure);
		Assert.NotNull(read);
		Assert.Equal(side, read.Side);
		Assert.Equal(cellSizeShift, read.CellSizeShift);
		Assert.Equal(heightScale, read.HeightScale);
		Assert.Equal(heights, read.Heights.ToArray());
	}

	/// <summary>A header whose width shift disagrees with the image is refused. Catches dropping or loosening the shift check.</summary>
	[Fact]
	public void HeaderShiftsThatDisagreeWithTheImageAreRefused() {
		var zone = ZoneFiles.Create(Heights(128), 14, 149);
		var header = zone.WriteHeader();
		BitConverter.GetBytes(8).CopyTo(header, HeaderWidthShift);

		AssertRefused(header, zone.WriteHeightmap());
	}

	/// <summary>A heightmap whose frame count is not one is refused. Catches dropping the frame-count check.</summary>
	[Theory]
	[InlineData(0)]
	[InlineData(2)]
	public void AHeightmapOfOtherThanOneImageIsRefused(int frames) {
		var zone = ZoneFiles.Create(Heights(128), 14, 149);
		var heightmap = zone.WriteHeightmap();
		BitConverter.GetBytes(frames).CopyTo(heightmap, HeightmapFrameCount);

		AssertRefused(zone.WriteHeader(), heightmap);
	}

	/// <summary>
	/// An image that is not square, or square at a side other than 128 or 256, is refused even with header shifts that
	/// match it. Catches loosening the side check, for instance to any power of two.
	/// </summary>
	[Theory]
	[InlineData(64, 64)]
	[InlineData(128, 256)]
	public void AnImageOfAnotherSizeIsRefused(int cols, int rows) {
		var zone = ZoneFiles.Create(Heights(128), 14, 149);
		var header = zone.WriteHeader();
		BitConverter.GetBytes(ZoneFiles.GridShift(cols)).CopyTo(header, HeaderWidthShift);
		BitConverter.GetBytes(ZoneFiles.GridShift(rows)).CopyTo(header, HeaderWidthShift + 4);

		AssertRefused(header, Heightmap(cols, rows));
	}

	/// <summary>
	/// A heightmap that ends before its image does is refused, not thrown on. Catches removing the guard around the bitmap
	/// array's parse, which a bundle's untrusted bytes go through. (-2 ends one pixel short; the last byte alone is the
	/// record's even-length pad.)
	/// </summary>
	[Theory]
	[InlineData(4)]
	[InlineData(30)]
	[InlineData(8000)]
	[InlineData(-2)]
	public void ACutShortHeightmapIsRefused(int length) {
		var zone = ZoneFiles.Create(Heights(128), 14, 149);
		var heightmap = zone.WriteHeightmap();

		AssertRefused(zone.WriteHeader(), heightmap[..(length < 0 ? heightmap.Length + length : length)]);
	}

	/// <summary>
	/// At every allowed cell size shift the largest height scale builds and reads, one more is refused, and the largest keeps
	/// within the two bounds it stands for: the height query's 32-bit product of a 255-step slope across a cell, and face
	/// normals no coarser than retail's coarsest, whose vertical part is 16 (docs/retail/simulation/terrain-heightmap.md#the-height-scales-range).
	/// Catches dropping the height-scale check and a <see cref="ZoneFiles.MaxHeightScale"/> raised past either bound.
	/// </summary>
	[Theory]
	[InlineData(12)]
	[InlineData(13)]
	[InlineData(14)]
	[InlineData(15)]
	public void TheHeightScaleKeepsWithinWhatTheTerrainArithmeticHolds(int cellSizeShift) {
		int largest = ZoneFiles.MaxHeightScale(cellSizeShift);
		int cellSize = 1 << cellSizeShift;
		Assert.True((long)largest * byte.MaxValue * cellSize <= int.MaxValue);
		Assert.True(cellSize / largest >= 16);

		var zone = ZoneFiles.Create(Heights(128), cellSizeShift, largest);
		Assert.NotNull(ZoneFiles.Read(zone.WriteHeader(), zone.WriteHeightmap(), out _));

		foreach (int refused in new[] { largest + 1, ZoneFiles.MinHeightScale - 1 }) {
			var header = zone.WriteHeader();
			BitConverter.GetBytes(refused).CopyTo(header, HeaderHeightScale);
			AssertRefused(header, zone.WriteHeightmap());
			Assert.Throws<ArgumentOutOfRangeException>(() => ZoneFiles.Create(Heights(128), cellSizeShift, refused));
		}
	}

	/// <summary>A cell size shift outside the retail zones' range is refused. Catches dropping or widening the cell-size-shift check.</summary>
	[Theory]
	[InlineData(ZoneFiles.MinCellSizeShift - 1)]
	[InlineData(ZoneFiles.MaxCellSizeShift + 1)]
	public void ACellSizeShiftOutsideTheRetailRangeIsRefused(int cellSizeShift) {
		var zone = ZoneFiles.Create(Heights(128), 14, 149);
		var header = zone.WriteHeader();
		BitConverter.GetBytes(cellSizeShift).CopyTo(header, HeaderCellSizeShift);

		AssertRefused(header, zone.WriteHeightmap());
		Assert.Throws<ArgumentOutOfRangeException>(() => ZoneFiles.Create(Heights(128), cellSizeShift, 149));
	}

	private static void AssertRefused(byte[] header, byte[] heightmap) {
		ZoneFiles? zone = null;
		string? failure = null;
		Assert.Null(Record.Exception(() => zone = ZoneFiles.Read(header, heightmap, out failure)));
		Assert.Null(zone);
		Assert.False(string.IsNullOrEmpty(failure));
	}

	private static byte[] Heights(int side) {
		var heights = new byte[side * side];
		new Random(side).NextBytes(heights);
		return heights;
	}

	// A raw 8-bit bitmap array of one cols x rows image, in the layout ZoneFiles.WriteHeightmap writes.
	private static byte[] Heightmap(int cols, int rows) {
		var image = new DynamixBitmap {
			Rows = (short)rows,
			Cols = (short)cols,
			BitsPerPixel = 8,
			Packing = BitmapPacking.Raw,
			ImageDataLen = cols * rows,
			ImageData = new byte[cols * rows],
		};
		int frameBytes = new DynamixBitmapTransformer().Write(image)!.Length;
		return new DynamixBitmapArrayTransformer().Write(new DynamixBitmapArray {
			FileSize = EndianOps.GetIntLEBytes(4 + frameBytes),
			FrameCount = 1,
			Images = [image],
		})!;
	}
}
