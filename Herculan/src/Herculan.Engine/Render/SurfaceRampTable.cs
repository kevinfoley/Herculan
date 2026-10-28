using System.Numerics;
using HercWorks.Core.Data.File.Dyn;

namespace Herculan.Engine.Render;

/// <summary>
/// The theater's shaded-surface colours, every ramp against every light level, as one small RGBA
/// image the fragment shader indexes instead of doing two table lookups per pixel.
///
/// <para>Row <c>r</c>, column <c>s</c> of a shaded-chain block is
/// <see cref="SurfaceShading.ShadedColor"/> for ramp <c>r</c> at shade <c>s</c>, evaluated for every
/// combination at load, so sampling it nearest-neighbour reproduces the exact palette byte a
/// <c>TSShadedPoly</c> would have been filled with, quantization included.</para>
///
/// <para>It is a table rather than a bake because the shade a face is drawn at comes from its
/// <b>world</b> normal (<see cref="MissionSun.ShadeForFace"/>), and one built mesh is shared by every
/// object of a type standing at its own heading. See <see cref="Gl.MeshVertex.ShadeRamp"/>.</para>
///
/// <para>The shaded chain's alpha channel carries the palette byte itself
/// (<see cref="SurfaceShading.ShadedByte"/>), which is what a <c>TSShadedPoly</c>'s outline is
/// tested on — see <see cref="Gl.MeshVertex.OutlineFillRamp"/>. The Gouraud block's alpha carries
/// the ramp entry's palette index (<see cref="SurfaceShading.RampedPaletteIndex"/>), which is what a
/// <c>TSGouraudPoly</c>'s corners interpolate. The layout of the blocks is <see cref="Height"/>.</para>
/// </summary>
public sealed class SurfaceRampTable {
	/// <summary>Light levels across — one column per shade byte.</summary>
	public const int Width = 256;

	/// <summary>Ramp slots in one block of rows — one row per slot of the palette's ramp table.</summary>
	public const int RampCount = 256;

	/// <summary>
	/// What a vertex adds to its ramp number to say which of the two chains it is on — see
	/// <see cref="Gl.MeshVertex.ShadeRamp"/>, which carries the biased value. It is a chain
	/// selector, not a row: the shader turns the pair into a row of <see cref="Pixels"/>, since the
	/// shaded chain has one block per depth slice and the Gouraud chain has one block in total.
	///
	/// <para>Folding both chains into one texture keeps the fragment shader at a single sample and
	/// the vertex format at a single float.</para>
	/// </summary>
	public const int GouraudRowOffset = RampCount;

	private SurfaceRampTable(byte[] pixels, int depthSlices, bool[]? indexed = null) {
		Pixels = pixels;
		DepthSlices = depthSlices;
		_indexed = indexed;
		_shadedNames = indexed != null ? ShadedNames(pixels, indexed, depthSlices) : null;
	}

	/// <summary>
	/// For a table built by <see cref="FromIndices"/>, which cells of the two chains name a palette index (the cell's
	/// alpha); null for one built by <see cref="Build"/>, whose colours are not all a palette entry's.
	/// </summary>
	private readonly bool[]? _indexed;

	/// <summary>For a table built by <see cref="FromIndices"/>, <see cref="ShadedCellsName"/> for each palette index.</summary>
	private readonly bool[]? _shadedNames;

	/// <summary>Whether <see cref="Recolor"/> can show the table in another palette: it was built by <see cref="FromIndices"/>.</summary>
	public bool CanRecolor => _indexed != null;

	/// <summary>
	/// For a table built by <see cref="FromIndices"/>, whether a cell of the shaded chain names palette entry
	/// <paramref name="index"/>; true for one built by <see cref="Build"/>. The shaded chain's cells are drawn in their
	/// own colours, where a Gouraud face takes only its cells' indices and is drawn in <see cref="PaletteRow"/>'s, so a
	/// change to an entry no shaded cell names reaches the faces through <see cref="PaletteRow"/> alone
	/// (<see cref="RecolorPaletteRow"/>).
	/// </summary>
	public bool ShadedCellsName(int index) => _shadedNames == null || (uint)index < Width && _shadedNames[index];

	private static bool[] ShadedNames(byte[] pixels, bool[] indexed, int slices) {
		var names = new bool[Width];
		int shadedCells = RampCount * slices * Width;
		for (int cell = 0; cell < shadedCells; cell++) {
			if (indexed[cell]) {
				names[pixels[cell * 4 + 3]] = true;
			}
		}

		return names;
	}

	/// <summary>RGBA8, row-major, <see cref="Width"/> by <see cref="Height"/>.</summary>
	public byte[] Pixels { get; }

	/// <summary>
	/// How many depth slices the shaded chain carries — <see cref="Content.ShadeRamp.DepthSlices"/>,
	/// 12 in every retail theater.
	///
	/// <para><c>TSShadedPoly_Render</c> ends in <c>Raster_ShadeRampRow</c>, so its colour is fogged by
	/// the same slice offset every other <c>.RMP</c> read is — see <see cref="PaletteRampTable"/>.
	/// <c>TSGouraudPoly_Render</c>'s fill writes plain palette indices and reads no slice offset, so
	/// the Gouraud block is stored once and those surfaces are not fogged
	/// (docs/retail/rendering/distance-fog-and-sky.md, "The fade").</para>
	/// </summary>
	public int DepthSlices { get; }

	/// <summary>
	/// Rows in the uploaded image: one <see cref="RampCount"/>-row block of the shaded chain per
	/// depth slice, one block of the Gouraud chain, then <see cref="PaletteRow"/>.
	/// </summary>
	public int Height => RampCount * (DepthSlices + 1) + 1;

	/// <summary>The first row of the Gouraud block — what the shader adds a ramp number to.</summary>
	public int GouraudBlockRow => RampCount * DepthSlices;

	/// <summary>
	/// The row holding the palette straight through, column <c>i</c> being palette index <c>i</c>'s
	/// colour: what a <c>TSGouraudPoly</c>'s contour bands are drawn in, since each band is a whole
	/// palette index between its corners' entries rather than a ramp entry
	/// (<c>Raster_FillContourTriangle</c>, <c>00486ddc</c>).
	/// </summary>
	public int PaletteRow => RampCount * (DepthSlices + 1);

	/// <summary>
	/// Builds the table for a theater, or returns null when <paramref name="shading"/> is absent or
	/// its palette carries no ramp table — in which case shaded surfaces keep whatever colour the
	/// mesh builder gave them.
	/// </summary>
	public static SurfaceRampTable? Build(SurfaceShading? shading) {
		if (shading is not { HasShadeRamps: true }) {
			return null;
		}

		return Fill(shading.Ramp.DepthSlices,
			(ramp, shade, block) => (shading.ShadedColor(ramp, shade, block), shading.ShadedByte(ramp, shade, block)),
			(ramp, shade) => (shading.GouraudColor(ramp, shade), (byte?)shading.RampedPaletteIndex(ramp, shade)),
			shading.PaletteColor);
	}

	/// <summary>
	/// Builds a table whose shaded chain has <paramref name="blocks"/> blocks, each cell the palette index
	/// <paramref name="shadedIndex"/> gives for (ramp, shade, block), and whose Gouraud block holds
	/// <paramref name="gouraudIndex"/>'s for (ramp, shade), all drawn in <paramref name="palette"/>'s colours. For a
	/// game whose ramps the original re-points per drawn object rather than fogs through a <c>.RMP</c>: each block is
	/// one state of the ramps, and a drawn item names its block (<see cref="SceneItem.RampBlock"/>). Null where an
	/// index has no entry.
	/// </summary>
	public static SurfaceRampTable FromIndices(int blocks, Func<int, int, int, byte?> shadedIndex,
			Func<int, int, byte?> gouraudIndex, DynamixPalette palette) {
		Vector3? ColorOf(byte? index) =>
			index is { } i && palette.Colors.TryGetValue(i, out var entry)
				? new Vector3(entry.GetColor().R / 255f, entry.GetColor().G / 255f, entry.GetColor().B / 255f)
				: null;

		return Fill(blocks,
			(ramp, shade, block) => shadedIndex(ramp, shade, block) is { } index ? (ColorOf(index), index) : (null, null),
			(ramp, shade) => gouraudIndex(ramp, shade) is { } index ? (ColorOf(index), index) : (null, null),
			index => ColorOf((byte)index), trackIndexed: true);
	}

	/// <summary>
	/// Shows a table built by <see cref="FromIndices"/> in <paramref name="palette"/>, in place: every cell that names a
	/// palette index, and every cell of <see cref="PaletteRow"/>, takes that entry's colour, mid grey where
	/// <paramref name="palette"/> lacks it, as <see cref="FromIndices"/> resolves them. The indices are unchanged; a cell
	/// with none keeps its grey. For a game whose screen shows the palette indices it draws through a palette that changes
	/// while they are on it.
	/// </summary>
	/// <exception cref="InvalidOperationException">The table was built by <see cref="Build"/> (<see cref="CanRecolor"/>).</exception>
	public void Recolor(DynamixPalette palette) {
		if (_indexed is not { } indexed) {
			throw new InvalidOperationException("Only a table built from palette indices can be shown in another palette.");
		}

		var rgb = PaletteBytes(palette);
		for (int cell = 0; cell < indexed.Length; cell++) {
			if (indexed[cell]) {
				SetRgb(cell, rgb, Pixels[cell * 4 + 3]);
			}
		}

		SetPaletteRow(rgb);
	}

	/// <summary>
	/// <see cref="Recolor"/> of <see cref="PaletteRow"/> alone, which is all of the table a change to an entry no shaded
	/// cell names (<see cref="ShadedCellsName"/>) reaches.
	/// </summary>
	public void RecolorPaletteRow(DynamixPalette palette) => SetPaletteRow(PaletteBytes(palette));

	private void SetPaletteRow(int[] rgb) {
		int paletteRow = PaletteRow * Width;
		for (int index = 0; index < Width; index++) {
			SetRgb(paletteRow + index, rgb, index);
		}
	}

	/// <summary>Each of the 256 entries' red, green and blue, or −1 in red where <paramref name="palette"/> lacks the entry.</summary>
	private static int[] PaletteBytes(DynamixPalette palette) {
		var rgb = new int[Width * 3];
		for (int index = 0; index < Width; index++) {
			if (palette.Colors.TryGetValue(index, out var entry)) {
				var color = entry.GetColor();
				rgb[index * 3] = color.R;
				rgb[index * 3 + 1] = color.G;
				rgb[index * 3 + 2] = color.B;
			} else {
				rgb[index * 3] = -1;
			}
		}

		return rgb;
	}

	private void SetRgb(int cell, int[] rgb, int index) {
		int at = cell * 4;
		bool known = rgb[index * 3] >= 0;
		Pixels[at] = known ? (byte)rgb[index * 3] : (byte)128;
		Pixels[at + 1] = known ? (byte)rgb[index * 3 + 1] : (byte)128;
		Pixels[at + 2] = known ? (byte)rgb[index * 3 + 2] : (byte)128;
	}

	private static SurfaceRampTable Fill(int slices, Func<int, int, int, (Vector3? Color, byte? Byte)> shaded,
			Func<int, int, (Vector3? Color, byte? Byte)> gouraud, Func<int, Vector3?> paletteColor, bool trackIndexed = false) {
		int paletteRow = RampCount * (slices + 1);
		int height = paletteRow + 1;
		var pixels = new byte[Width * height * 4];
		var indexed = trackIndexed ? new bool[Width * paletteRow] : null;
		for (int row = 0; row < paletteRow; row++) {
			// Blocks of RampCount rows: one per depth slice of the shaded chain, then the Gouraud
			// chain's single block past them all.
			int block = row / RampCount;
			int ramp = row % RampCount;
			bool isGouraud = block >= slices;

			for (int shade = 0; shade < Width; shade++) {
				int at = (row * Width + shade) * 4;
				var (color, value) = isGouraud ? gouraud(ramp, shade) : shaded(ramp, shade, block);

				// A ramp slot the palette leaves empty falls back to mid grey rather than to black:
				// no retail surface names one, and a black hole would be a louder lie than a flat
				// tone if some mission's data ever did.
				pixels[at] = color is { } c ? Quantise(c.X) : (byte)128;
				pixels[at + 1] = color is { } c1 ? Quantise(c1.Y) : (byte)128;
				pixels[at + 2] = color is { } c2 ? Quantise(c2.Z) : (byte)128;
				pixels[at + 3] = value ?? 255;
				if (indexed != null) {
					indexed[row * Width + shade] = value.HasValue;
				}
			}
		}

		for (int index = 0; index < Width; index++) {
			int at = (paletteRow * Width + index) * 4;
			var color = paletteColor(index);
			pixels[at] = color is { } c ? Quantise(c.X) : (byte)128;
			pixels[at + 1] = color is { } c1 ? Quantise(c1.Y) : (byte)128;
			pixels[at + 2] = color is { } c2 ? Quantise(c2.Z) : (byte)128;
			pixels[at + 3] = 255;
		}

		return new SurfaceRampTable(pixels, slices, indexed);
	}

	private static byte Quantise(float channel) =>
		(byte)Math.Clamp((int)MathF.Round(channel * 255f), 0, 255);
}
