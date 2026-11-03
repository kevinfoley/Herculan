using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Util;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// A terrain zone's two files as a mission of its own carries them: the 16-byte <c>dat\zoneNNNN.dat</c> header
/// (<see cref="ZoneDat"/>) and the <c>dba\zoneNNNN.dba</c> heightmap, a bitmap array of one 8-bit image, one byte per cell.
/// See docs/retail/formats/zone-terrain.md#loading-pipeline.
///
/// <para>Stricter than retail, which sizes the grid from the image and never reads the header's shifts: a zone here is
/// square, <see cref="Sides"/> cells a side as every retail zone is, stored raw, its header's width and height shifts
/// match the image, its cell size shift is within the retail zones' range, and its height scale within what retail's terrain
/// arithmetic holds at that cell size shift. <see cref="Read"/> refuses anything else with the reason, without throwing, since a bundle's files come
/// from anyone. Bytes past the image, and the image's extra dwords, are not kept; retail's zones have neither.</para>
/// </summary>
public sealed class ZoneFiles {
	/// <summary>The cells a zone's side may have.</summary>
	public static readonly IReadOnlyList<int> Sides = [128, 256];

	/// <summary>The extension of a mission's own zone header, after its mission's name, beside its <c>.MSN</c> and in its bundle.</summary>
	public const string HeaderExtension = ".DAT";

	/// <summary>The extension of a mission's own zone heightmap, after its mission's name, beside its <c>.MSN</c> and in its bundle.</summary>
	public const string HeightmapExtension = ".DBA";

	/// <summary>The largest heightmap <see cref="WriteHeightmap"/> writes, in bytes: that of the largest of <see cref="Sides"/>.</summary>
	public static int MaxHeightmapSize { get; } =
		Create(new byte[Sides.Max() * Sides.Max()], MinCellSizeShift, MinHeightScale).WriteHeightmap().Length;

	/// <summary>The smallest <see cref="CellSizeShift"/> a zone may have, the retail zones' smallest.</summary>
	public const int MinCellSizeShift = 12;

	/// <summary>The largest <see cref="CellSizeShift"/> a zone may have, the retail zones' largest.</summary>
	public const int MaxCellSizeShift = 15;

	/// <summary>
	/// The smallest <see cref="HeightScale"/> a zone may have: the smallest at which every allowed cell size shift's face normals
	/// build. See docs/retail/simulation/terrain-heightmap.md#the-height-scales-range.
	/// </summary>
	public const int MinHeightScale = 3;

	// The vertical part of retail's coarsest face normal, ZONE234's cell size over its height scale.
	private const int CoarsestNormalVertical = 16;

	private const byte BitsPerPixel = 8;

	// The bitmap array's envelope, its size and its int32 frame count end here.
	private const int FramesEnd = 12;

	private static string SidesText => string.Join(" or ", Sides.Select(side => $"{side}x{side}"));

	private readonly byte[] _heights;

	private ZoneFiles(byte[] heights, int side, int cellSizeShift, int heightScale) {
		_heights = heights;
		Side = side;
		CellSizeShift = cellSizeShift;
		HeightScale = heightScale;
	}

	/// <summary>Cells a side.</summary>
	public int Side { get; }

	/// <inheritdoc cref="ZoneDat.CellSizeShift"/>
	public int CellSizeShift { get; }

	/// <inheritdoc cref="ZoneDat.HeightScale"/>
	public int HeightScale { get; }

	/// <summary>
	/// Each cell's raw height, <see cref="Side"/> rows of <see cref="Side"/>, in image order: row 0 is the zone's north
	/// edge and each row runs west to east.
	/// </summary>
	public ReadOnlySpan<byte> Heights => _heights;

	/// <summary>
	/// The zone of <paramref name="heights"/> (as <see cref="Heights"/> orders them, copied), whose count sets the side:
	/// the square of one of <see cref="Sides"/>, or this throws, as it does for a <paramref name="cellSizeShift"/> outside
	/// <see cref="MinCellSizeShift"/> to <see cref="MaxCellSizeShift"/> and a <paramref name="heightScale"/> outside
	/// <see cref="MinHeightScale"/> to <see cref="MaxHeightScale"/>.
	/// </summary>
	public static ZoneFiles Create(ReadOnlySpan<byte> heights, int cellSizeShift, int heightScale) {
		int count = heights.Length;
		int side = Sides.FirstOrDefault(side => side * side == count);
		if (side == 0) {
			throw new ArgumentException($"{count} heights make no zone; a zone is {SidesText} cells.", nameof(heights));
		}

		if (CellSizeShiftFailure(cellSizeShift) is { } failure) {
			throw new ArgumentOutOfRangeException(nameof(cellSizeShift), failure);
		}

		if (HeightScaleFailure(heightScale, cellSizeShift) is { } heightFailure) {
			throw new ArgumentOutOfRangeException(nameof(heightScale), heightFailure);
		}

		return new ZoneFiles(heights.ToArray(), side, cellSizeShift, heightScale);
	}

	/// <summary>
	/// The zone in <paramref name="header"/> and <paramref name="heightmap"/>, the two files' bytes, or null with the reason
	/// it is not one: the header is not 16 bytes, its cell size shift is outside <see cref="MinCellSizeShift"/> to
	/// <see cref="MaxCellSizeShift"/> or its height scale outside <see cref="MinHeightScale"/> to <see cref="MaxHeightScale"/>,
	/// the heightmap is not a bitmap array of one raw 8-bit image, the image is
	/// not square with one of <see cref="Sides"/> a side, or the header's width and height shifts differ from the image's.
	/// </summary>
	public static ZoneFiles? Read(byte[] header, byte[] heightmap, out string? failure) {
		if (header.Length != ZoneDatTransformer.Size) {
			failure = $"The zone header is {header.Length} bytes; it is {ZoneDatTransformer.Size}.";
			return null;
		}

		var dat = new ZoneDatTransformer().Parse(header)!;
		if ((CellSizeShiftFailure(dat.CellSizeShift) ?? HeightScaleFailure(dat.HeightScale, dat.CellSizeShift)) is { } headerFailure) {
			failure = headerFailure;
			return null;
		}

		if (heightmap.Length < FramesEnd || !heightmap.AsSpan().StartsWith(DynamixBitmapArray.HeaderMagic)) {
			failure = "The heightmap is not a bitmap array.";
			return null;
		}

		// Checked ahead of the parse, which would read as many frames as the count claims.
		int frames = BitConverter.ToInt32(heightmap, FramesEnd - 4);
		if (frames != 1) {
			failure = $"The heightmap holds {frames} images; a zone's holds one.";
			return null;
		}

		DynamixBitmapArray array;
		try {
			array = new DynamixBitmapArrayTransformer().Parse(heightmap)!;
		} catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidDataException) {
			failure = "The heightmap's bitmap array is cut short.";
			return null;
		}

		var image = array.Images![0];
		if (image.BitsPerPixel != BitsPerPixel) {
			failure = $"The heightmap is a {image.BitsPerPixel}-bit image; a zone's is {BitsPerPixel}-bit.";
			return null;
		}

		if (image.Packing != BitmapPacking.Raw) {
			failure = $"The heightmap is packed ({image.Packing}); a zone's is stored raw.";
			return null;
		}

		int side = image.Cols;
		if (image.Rows != side || !Sides.Contains(side)) {
			failure = $"The heightmap is {image.Cols}x{image.Rows} cells; a zone is {SidesText}.";
			return null;
		}

		if (image.ImageData!.Length != side * side) {
			failure = $"The heightmap's image holds {image.ImageData.Length} bytes; {side}x{side} cells take {side * side}.";
			return null;
		}

		int shift = GridShift(side);
		if (dat.WidthShift != shift || dat.HeightShift != shift) {
			failure = $"The zone header's width and height shifts are {dat.WidthShift} and {dat.HeightShift}; a {side}x{side} heightmap's are {shift}.";
			return null;
		}

		failure = null;
		return new ZoneFiles(image.ImageData, side, dat.CellSizeShift, dat.HeightScale);
	}

	private static string? CellSizeShiftFailure(int cellSizeShift) =>
		cellSizeShift is < MinCellSizeShift or > MaxCellSizeShift
			? $"The zone's cell size shift is {cellSizeShift}; a zone's is {MinCellSizeShift} to {MaxCellSizeShift}."
			: null;

	/// <summary>
	/// The largest <see cref="HeightScale"/> a zone of cell size shift <paramref name="cellSizeShift"/> may have: the lower of the
	/// height query's 32-bit bound for a full 255-step slope across one cell and the scale that makes face normals as
	/// coarse as retail's coarsest. See docs/retail/simulation/terrain-heightmap.md#the-height-scales-range.
	/// </summary>
	public static int MaxHeightScale(int cellSizeShift) {
		int cellSize = 1 << cellSizeShift;
		return Math.Min(int.MaxValue / (byte.MaxValue * cellSize), cellSize / CoarsestNormalVertical);
	}

	// Asked only of a cell size shift CellSizeShiftFailure accepts.
	private static string? HeightScaleFailure(int heightScale, int cellSizeShift) =>
		heightScale < MinHeightScale || heightScale > MaxHeightScale(cellSizeShift)
			? $"The zone's height scale is {heightScale}; at cell size shift {cellSizeShift} a zone's is {MinHeightScale} to {MaxHeightScale(cellSizeShift)}."
			: null;

	/// <summary>The <c>dat\zoneNNNN.dat</c> header's bytes, its width and height shifts those of <see cref="Side"/>.</summary>
	public byte[] WriteHeader() {
		int shift = GridShift(Side);
		return new ZoneDatTransformer().Write(new ZoneDat {
			WidthShift = shift,
			HeightShift = shift,
			CellSizeShift = CellSizeShift,
			HeightScale = HeightScale,
		})!;
	}

	/// <summary>The <c>dba\zoneNNNN.dba</c> heightmap's bytes: a bitmap array of <see cref="Heightmap"/> alone.</summary>
	public byte[] WriteHeightmap() {
		var image = Heightmap();
		int frameBytes = new DynamixBitmapTransformer().Write(image)!.Length;
		return new DynamixBitmapArrayTransformer().Write(new DynamixBitmapArray {
			// What follows the size: the int32 frame count and the frame.
			FileSize = EndianOps.GetIntLEBytes(4 + frameBytes),
			FrameCount = 1,
			Images = [image],
		})!;
	}

	/// <summary>The heightmap image: <see cref="Heights"/> as a raw 8-bit bitmap, <see cref="Side"/> pixels a side.</summary>
	public DynamixBitmap Heightmap() => new() {
		Rows = (short)Side,
		Cols = (short)Side,
		BitsPerPixel = BitsPerPixel,
		Packing = BitmapPacking.Raw,
		ImageDataLen = _heights.Length,
		ImageData = _heights.ToArray(),
	};

	/// <summary>
	/// The grid shift for <paramref name="cells"/> cells along one side, as <c>TerrainZone_PopulateFromBitmap</c>
	/// (<c>0046c3c0</c>) derives it from the heightmap image: its inline <c>for (i = 1; i &lt; value; i *= 2) shift++;</c>,
	/// ceil(log2), and 0 for 1 or less.
	/// </summary>
	public static int GridShift(int cells) {
		int shift = 0;
		for (int i = 1; i < cells; i *= 2) {
			shift++;
		}
		return shift;
	}
}
