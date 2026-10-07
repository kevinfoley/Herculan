namespace Herculan.Engine.Render;

/// <summary>
/// The theater's shaded-surface colours, every ramp against every light level, as one small RGBA
/// image the fragment shader indexes instead of doing two table lookups per pixel.
///
/// <para>Row <c>r</c>, column <c>s</c> is <see cref="SurfaceShading.ShadedColor"/> for ramp
/// <c>r</c> at shade <c>s</c> — so this is not an approximation of the original's colouring, it
/// <i>is</i> the original's colouring, evaluated once for all 65536 combinations at load. Sampling
/// it nearest-neighbour reproduces the exact palette byte a <c>TSShadedPoly</c> would have been
/// filled with, quantisation included.</para>
///
/// <para>Why a table rather than a bake: the shade a face is drawn at comes from its <b>world</b>
/// normal (<see cref="MissionSun.ShadeForFace"/>), and one built mesh is shared by every object of a
/// type standing at its own heading. See <see cref="Gl.MeshVertex.ShadeRamp"/>.</para>
///
/// <para>The shaded chain's alpha channel carries the palette byte itself
/// (<see cref="SurfaceShading.ShadedByte"/>), which is what a <c>TSShadedPoly</c>'s outline is
/// tested on — see <see cref="Gl.MeshVertex.OutlineFillRamp"/>. The Gouraud block's alpha is 255.</para>
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

	private SurfaceRampTable(byte[] pixels, int depthSlices) {
		Pixels = pixels;
		DepthSlices = depthSlices;
	}

	/// <summary>RGBA8, row-major, <see cref="Width"/> by <see cref="Height"/>.</summary>
	public byte[] Pixels { get; }

	/// <summary>
	/// How many depth slices the shaded chain carries — <see cref="Content.ShadeRamp.DepthSlices"/>,
	/// 12 in every retail theater.
	///
	/// <para><c>TSShadedPoly_Render</c> ends in <c>Raster_ShadeRampRow</c>, so its colour is fogged by
	/// the same slice offset every other <c>.RMP</c> read is — see <see cref="PaletteRampTable"/>.
	/// <c>TSGouraudPoly_Render</c> does not call it, and its chain has no <c>.RMP</c> step to add a
	/// slice to, so the Gouraud block is stored once and those surfaces take the renderer's own fog
	/// blend instead.</para>
	/// </summary>
	public int DepthSlices { get; }

	/// <summary>
	/// Rows in the uploaded image: one <see cref="RampCount"/>-row block of the shaded chain per
	/// depth slice, then one block of the Gouraud chain.
	/// </summary>
	public int Height => RampCount * (DepthSlices + 1);

	/// <summary>The first row of the Gouraud block — what the shader adds a ramp number to.</summary>
	public int GouraudBlockRow => RampCount * DepthSlices;

	/// <summary>
	/// Builds the table for a theater, or returns null when <paramref name="shading"/> is absent or
	/// its palette carries no ramp table — in which case shaded surfaces keep whatever colour the
	/// mesh builder gave them.
	/// </summary>
	public static SurfaceRampTable? Build(SurfaceShading? shading) {
		if (shading is not { HasShadeRamps: true }) {
			return null;
		}

		int slices = shading.Ramp.DepthSlices;
		int height = RampCount * (slices + 1);
		var pixels = new byte[Width * height * 4];
		for (int row = 0; row < height; row++) {
			// Blocks of RampCount rows: one per depth slice of the shaded chain, then the Gouraud
			// chain's single block past them all.
			int block = row / RampCount;
			int ramp = row % RampCount;
			bool gouraud = block >= slices;

			for (int shade = 0; shade < Width; shade++) {
				int at = (row * Width + shade) * 4;
				var color = gouraud
					? shading.GouraudColor(ramp, shade)
					: shading.ShadedColor(ramp, shade, block);

				// A ramp slot the palette leaves empty falls back to mid grey rather than to black:
				// no retail surface names one, and a black hole would be a louder lie than a flat
				// tone if some mission's data ever did.
				pixels[at] = color is { } c ? Quantise(c.X) : (byte)128;
				pixels[at + 1] = color is { } c1 ? Quantise(c1.Y) : (byte)128;
				pixels[at + 2] = color is { } c2 ? Quantise(c2.Z) : (byte)128;
				pixels[at + 3] = gouraud ? (byte)255 : shading.ShadedByte(ramp, shade, block) ?? 255;
			}
		}

		return new SurfaceRampTable(pixels, slices);
	}

	private static byte Quantise(float channel) =>
		(byte)Math.Clamp((int)MathF.Round(channel * 255f), 0, 255);
}
