using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;

namespace Herculan.Es1.Scene;

/// <summary>
/// The eight texture colour tables <c>Palette_BuildTextureShadeTables</c> (<c>ES1DBSIM 00028b28</c>) builds from the first
/// palette made active, which a textured face's light picks one of (docs/es1/rendering/polygon-shading.md#the-texture-colour-tables),
/// and the renderer's palette-by-row table that carries them.
/// </summary>
public static class Es1TextureShades {
	/// <summary>How many tables there are, <c>000973f4</c>.</summary>
	public const int TableCount = 8;

	/// <summary>The Q10 factor of the ramp's middle reference entry, about 0.63 of the way along.</summary>
	private const int MiddleFactor = 0x28a;

	/// <summary>The distance below which a palette colour is taken as the nearest.</summary>
	private const int NearestLimit = 1000;

	/// <summary>
	/// The tables: entry <c>c</c> of table <c>L</c> is <c>c</c> scaled by how bright step <c>L</c>/7 of the last ramp holding
	/// <c>c</c> is against the step at 0.63, re-quantised to the palette; <c>c</c> itself when no ramp holds it.
	/// </summary>
	public static byte[][] Build(DynamixPalette palette) {
		var tables = new byte[TableCount][];
		for (int level = 0; level < TableCount; level++) {
			tables[level] = new byte[256];
			for (int c = 0; c < 256; c++) {
				tables[level][c] = Entry(palette, level, c);
			}
		}

		return tables;
	}

	private static byte Entry(DynamixPalette palette, int level, int c) {
		short[]? holder = null;
		foreach (var ramp in palette.ShadeRamps) {
			if (ramp.Contains((short)c)) {
				holder = ramp;
			}
		}

		if (holder is not { Length: > 0 }) {
			return (byte)c;
		}

		var reference = Channels(palette, holder[(holder.Length - 1) * level / (TableCount - 1)]);
		var middle = Channels(palette, holder[SimMath.Q10MultiplyRounded(holder.Length - 1, MiddleFactor)]);
		var own = Channels(palette, c);
		var target = new int[3];
		for (int k = 0; k < 3; k++) {
			int ratio = middle[k] == 0 ? 0 : (reference[k] << 10) / middle[k];
			target[k] = SimMath.Q10MultiplyRounded(own[k], (short)ratio); // Retail keeps the ratio in a word.
		}

		int best = c;
		int bestDistance = NearestLimit;
		for (int index = 0; index < 256; index++) {
			var colour = Channels(palette, index);
			int distance = SimMath.FastMagnitude3D(colour[0] - target[0], colour[1] - target[1], colour[2] - target[2]);
			if (distance < bestDistance) {
				bestDistance = distance;
				best = index;
			}
		}

		return (byte)best;
	}

	/// <summary>An entry's three channels, the dword's low bytes as the palette holds them (<c>Palette_UnpackColour</c>).</summary>
	private static int[] Channels(DynamixPalette palette, int index) =>
		palette.Colors.TryGetValue(index, out var entry) ? [entry.Array[0], entry.Array[1], entry.Array[2]] : [0, 0, 0];

	/// <summary>
	/// The renderer's palette-by-row table over 256 shade rows, one per light the shader can pick: the scene shader takes
	/// row <c>floor(light × 255 / 256)</c>, so row <c>r</c> stands for light <c>r</c> + 1 and holds table
	/// (8 − 1) · (<c>r</c> + 1) / 255, <c>TSTexture4Poly_Render</c>'s pick (docs/es1/rendering/polygon-shading.md#the-four-poly-types).
	/// The tables are built from <paramref name="first"/>, the first palette made active, and their entries shown in
	/// <paramref name="live"/>'s colours.
	/// </summary>
	public static PaletteRampTable? RampTable(DynamixPalette first, DynamixPalette live) {
		var tables = Build(first);
		const int rows = 256;
		var bytes = new byte[rows * ShadeRamp.RowLength];
		for (int row = 0; row < rows; row++) {
			var table = tables[(TableCount - 1) * Math.Min(row + 1, 255) / 255];
			Array.Copy(table, 0, bytes, row * ShadeRamp.RowLength, ShadeRamp.RowLength);
		}

		return PaletteRampTable.Build(new SurfaceShading(ShadeRamp.FromRows(rows, 1, bytes), live));
	}
}
