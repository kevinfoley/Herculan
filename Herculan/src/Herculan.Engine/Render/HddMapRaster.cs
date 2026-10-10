using System.Numerics;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Render;

/// <summary>
/// The mission's map raster: its own patch of the height grid, coloured by height alone, as one RGBA
/// image both maps draw — the Heads-Down Display's command display and the MFD's NAV MAP.
/// </summary>
/// <remarks>
/// <c>HddMap_BuildTerrainRaster</c> (<c>0044f6cc</c>): an integer-upscaled bitmap of the grown
/// mission box, two triangles per cell, each filled in contour bands of palette index — see
/// docs/retail/simulation/heads-down-display.md, "Terrain raster". The band rule is evaluated at each pixel
/// centre here rather than through a polygon rasterizer. Index 0, which the original leaves off the
/// grid and in the undrawn last row and column, decodes transparent, since the original's blit skips
/// it — docs/retail/rendering/dts-billboards.md, "Brush mode 5 skips palette index 0".
/// </remarks>
public sealed class HddMapRaster {
	/// <summary>The bitmap's largest extent, which the upscale is fitted inside.</summary>
	public const int FitWidth = 640;

	/// <summary>See <see cref="FitWidth"/>.</summary>
	public const int FitHeight = 400;

	private HddMapRaster(byte[] pixels, int width, int height, int worldX0, int worldY0, int worldX1, int worldY1) {
		Pixels = pixels;
		Width = width;
		Height = height;
		WorldX0 = worldX0;
		WorldY0 = worldY0;
		WorldX1 = worldX1;
		WorldY1 = worldY1;
	}

	/// <summary>RGBA8, row 0 at the <i>top</i> — i.e. at <see cref="WorldY1"/>, since world +y is up.</summary>
	public byte[] Pixels { get; }

	/// <summary>Pixels across.</summary>
	public int Width { get; }

	/// <summary>Pixels down.</summary>
	public int Height { get; }

	/// <summary>World x the bitmap's left edge is stretched to: the grown box's, not a cell edge.</summary>
	public int WorldX0 { get; }

	/// <summary>World y of its bottom edge.</summary>
	public int WorldY0 { get; }

	/// <summary>World x of its right edge.</summary>
	public int WorldX1 { get; }

	/// <summary>World y of its top edge.</summary>
	public int WorldY1 { get; }

	/// <summary>
	/// Builds the raster for <paramref name="bounds"/> out of <paramref name="grid"/>, resolving each
	/// palette index through <paramref name="palette"/> — <see cref="CockpitArt.PaletteEntry"/>, so the
	/// map takes the same live palette the terrain does. Returns null for an empty box.
	/// </summary>
	public static HddMapRaster? Build(HeightGrid grid, MissionBox bounds, Func<int, Vector3?> palette) {
		ArgumentNullException.ThrowIfNull(grid);
		ArgumentNullException.ThrowIfNull(palette);
		if (bounds.IsEmpty) {
			return null;
		}

		var grown = bounds.Grown(HddMap.Margin);
		int shift = grid.CellShift;
		int x0 = grown.MinX >> shift;
		int y0 = grown.MinY >> shift;
		int x1 = grown.MaxX >> shift;
		int y1 = grown.MaxY >> shift;

		int upscale = Math.Min(FitHeight / (y1 - y0 + 1), FitWidth / (x1 - x0 + 1));
		if (upscale <= 0) {
			return null;
		}

		int width = (x1 - x0 + 1) * upscale;
		int height = (y1 - y0 + 1) * upscale;
		var indices = new byte[width * height];

		int Index(int cellX, int cellY) =>
			cellX < 0 || cellX >= grid.Width || cellY < 0 || cellY >= grid.Height
				? 0
				: HddMap.RasterPalette(grid.RawHeightAt(cellX, cellY));

		for (int y = y1; y > y0; y--) {
			int top = (y1 - y) * upscale;
			for (int x = x0; x < x1; x++) {
				int left = (x - x0) * upscale;
				int southWest = Index(x, y);
				int northWest = Index(x, y + 1);
				int northEast = Index(x + 1, y + 1);
				int southEast = Index(x + 1, y);

				for (int row = 0; row < upscale; row++) {
					// Fractions across the square from its south-west corner, sampled at pixel centres.
					float north = (upscale - row - 0.5f) / upscale;
					int at = (top + row) * width + left;
					for (int column = 0; column < upscale; column++) {
						float east = (column + 0.5f) / upscale;
						float value = north > east
							? southWest + (northWest - southWest) * north + (northEast - northWest) * east
							: southWest + (southEast - southWest) * east + (northEast - southEast) * north;
						indices[at + column] = (byte)Math.Ceiling(value - 1e-4f);
					}
				}
			}
		}

		// Resolved once per palette index rather than once per pixel. Every index the bands can reach
		// lies between 0 (off the grid) and the top of the ramp.
		const int RampTop = HddMap.RasterBasePalette + HddMap.RasterHeightClamp / HddMap.RasterHeightDivisor;
		var ramp = new byte[(RampTop + 1) * 4];
		for (int index = 1; index <= RampTop; index++) {
			var color = palette(index) ?? Vector3.Zero;
			ramp[index * 4] = (byte)Math.Clamp(color.X * 255f, 0f, 255f);
			ramp[index * 4 + 1] = (byte)Math.Clamp(color.Y * 255f, 0f, 255f);
			ramp[index * 4 + 2] = (byte)Math.Clamp(color.Z * 255f, 0f, 255f);
			ramp[index * 4 + 3] = 255;
		}

		var pixels = new byte[width * height * 4];
		for (int i = 0; i < indices.Length; i++) {
			Buffer.BlockCopy(ramp, indices[i] * 4, pixels, i * 4, 4);
		}

		return new HddMapRaster(pixels, width, height, grown.MinX, grown.MinY, grown.MaxX, grown.MaxY);
	}
}
