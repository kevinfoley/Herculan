using Herculan.Engine.Content;

namespace Herculan.Engine.Render;

/// <summary>
/// The theater's <c>.RMP</c> expanded through its palette: every palette index at every shade row,
/// as one small RGBA image the fragment shader indexes.
///
/// <para>This is the whole of how the original colours a <b>lit textured</b> surface. It is an 8-bit
/// indexed rasterizer, so a span writes</para>
/// <code>
/// framebuffer[x] = Raster_ShadeRampRow(shade)[ texelPaletteIndex ]
/// </code>
/// <para>— the light level picks a <i>row</i> of the ramp and the texel picks the column within it,
/// per pixel. Row <c>r</c>, column <c>i</c> of this table is that lookup's result expanded to RGB,
/// so a shader that samples <see cref="TextureAtlas.IndexPixels"/> for <c>i</c> and this for the row
/// produces the exact byte the original would have written.</para>
///
/// <para><b>It is not a brightness multiplier</b>, which is the shape of thing an RGB pipeline
/// reaches for: take the texel's expanded colour and scale it by how bright the ramp row is on
/// average. That is a different operation. The ramp is not a uniform dimmer — it is a per-colour
/// remap that preserves hue and compresses unevenly, and near its ends it collapses distinct colours
/// together, which no multiplier does.</para>
///
/// <para><b>Distance is in here too.</b> The original fogs by adding whole depth slices to that same
/// row offset (<c>Raster_ShadeRampRow</c>, and the span setups do the identical arithmetic inline),
/// so the table carries every slice: rows run slice-major, slice <c>s</c> occupying rows
/// <c>s * ShadeRows</c> through <c>s * ShadeRows + ShadeRows - 1</c>. The fragment shader picks
/// <c>s</c> from its own distance with <see cref="ShadeRamp.DepthSliceFor"/>'s formula, so a fogged
/// pixel is still one sample and still the exact byte the original would have written.</para>
///
/// <para>A blend toward a flat fog colour is not the same operation, even though the ramp's slices
/// average out to about that much fade: the ramp fogs each palette index at its own rate and keeps
/// them distinguishable almost to the last slice, where a uniform blend flattens the whole surface
/// evenly and washes distant terrain into featureless pastel.</para>
/// </summary>
public sealed class PaletteRampTable {
	/// <summary>Palette indices across — one column each.</summary>
	public const int Width = 256;

	private PaletteRampTable(byte[] pixels, int shadeRows, int depthSlices) {
		Pixels = pixels;
		ShadeRows = shadeRows;
		DepthSlices = depthSlices;
	}

	/// <summary>RGBA8, row-major, <see cref="Width"/> by <see cref="Height"/>.</summary>
	public byte[] Pixels { get; }

	/// <summary>
	/// The row a <b>flat solid face</b> reads — <see cref="ShadeRamp.UnlitShade"/>'s row in slice 0,
	/// the fixed shade <c>TSSolidPoly_Render</c> passes. Sampling this row at the surface's palette
	/// index is byte-for-byte what <c>DtsMeshBuilder.ResolveSolidColors</c> computes on the CPU, so
	/// the two are interchangeable and the shader uses whichever table the damage flash has bound.
	/// </summary>
	public int UnlitRow { get; private init; }

	/// <summary>
	/// The row an untextured terrain cell reads — <see cref="TerrainMeshBuilder.UntexturedRowShade"/>'s
	/// row in slice 0. Unlike
	/// <see cref="UnlitRow"/> it is fogged: the cell's depth slice is added to it, as
	/// <c>Raster_ShadeRampRow</c> adds the fade <c>Terrain_DrawCellQuad</c> installed.
	/// </summary>
	public int GroundRow { get; private init; }

	/// <summary>
	/// The ramp's own rows — <see cref="ShadeRamp.ShadeLevels"/>, 32 in every retail theater. The
	/// shade byte selects among these; <see cref="FullbrightRow"/> sits past them.
	/// </summary>
	public int ShadeRows { get; }

	/// <summary>
	/// The ramp's depth slices — <see cref="ShadeRamp.DepthSlices"/>, 12 in every retail theater.
	/// Slice <c>s</c>'s shade rows start at row <c>s * <see cref="ShadeRows"/></c>.
	/// </summary>
	public int DepthSlices { get; }

	/// <summary>
	/// The row a fullbright textured surface reads instead of a shade row: the palette straight
	/// through, with no <c>.RMP</c> step at all.
	///
	/// <para><c>TSTexture4Poly_Render</c> (<c>00474e9c</c>) is the only reader of the ramp's row
	/// count (<c>DAT_004a5b1c</c>), and when that count is zero it fills through
	/// <c>Raster_SetupTexturedSpan</c>'s mode 0 — a plain texture copy, with neither a light term nor
	/// a ramp lookup. <c>Bullet_Draw</c> (<c>0040a120</c>) zeroes it for the duration of a projectile's
	/// shape render and restores it after, which is what makes a round's textured polys fullbright.
	/// The one retail shape it reaches is <c>BULLETS.DTS</c> root 8, the plasma cannon's round; every
	/// other projectile shape is untextured. See <see cref="SceneItem.Fullbright"/> and
	/// docs/retail/formats/dts-texture-binding.md's "<c>TSTexture4Poly</c> — frame index, ramp row by light,
	/// fullbright on demand".</para>
	///
	/// <para>It sits past every depth slice, and there is only one of it: a fill that skips the ramp
	/// skips the depth bias with it, so a fullbright surface does not fog.</para>
	/// </summary>
	public int FullbrightRow => ShadeRows * DepthSlices;

	/// <summary>
	/// Rows in the uploaded image: every slice's shade rows, then <see cref="FullbrightRow"/>.
	/// </summary>
	public int Height => ShadeRows * DepthSlices + 1;

	/// <summary>
	/// Builds the table for a theater, or returns null without a ramp or a palette — in which case a
	/// textured surface falls back to its expanded colour and the renderer's own light term.
	/// </summary>
	public static PaletteRampTable? Build(SurfaceShading? shading) {
		if (shading?.Palette is not { } palette) {
			return null;
		}

		var ramp = shading.Ramp;
		int shadeRows = ramp.ShadeLevels;
		int slices = ramp.DepthSlices;
		int rows = shadeRows * slices + 1;
		var pixels = new byte[Width * rows * 4];

		for (int row = 0; row < rows; row++) {
			// Slice-major: the shader adds slice * shadeRows to the row the shade byte picked, which
			// is the original's own arithmetic — its depth bias is a whole number of slices, each one
			// shadeRows * 256 bytes long.
			int slice = row / shadeRows;
			int shadeRow = row % shadeRows;

			for (int index = 0; index < Width; index++) {
				int at = (row * Width + index) * 4;

				// The last row skips the ramp entirely — that is what the original's plain copy
				// writes, and it is not any of the ramp's own rows: row 0 lands at 0.36x the source
				// colour and row 31 at 1.16x, so none of them is the identity.
				byte resolved = row == rows - 1 ? (byte)index : ramp.AtRow(index, shadeRow, slice);

				if (palette.Colors.TryGetValue(resolved, out var entry)) {
					var color = entry.GetColor();
					pixels[at] = color.R;
					pixels[at + 1] = color.G;
					pixels[at + 2] = color.B;
				}

				pixels[at + 3] = 255;
			}
		}

		return new PaletteRampTable(pixels, shadeRows, slices) {
			UnlitRow = ramp.RowFor(ShadeRamp.UnlitShade),
			GroundRow = ramp.RowFor(TerrainMeshBuilder.UntexturedRowShade),
		};
	}
}
