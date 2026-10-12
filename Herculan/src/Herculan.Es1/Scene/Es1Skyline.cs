using HercWorks.Core.Data.File.Dyn;

namespace Herculan.Es1.Scene;

/// <summary>
/// The horizon skyline: the <c>hzline</c>'s bitmap bank <c>+0x50</c>, <c>dba\world&lt;N&gt;</c>, which
/// <c>Hzline_BlitHorizonStrip</c> (<c>ES1DBSIM 00026ac8</c>) stands on the horizon line as a 360° panorama
/// (docs/es1/rendering/ground-and-sky.md#the-horizon-skyline).
/// </summary>
public sealed class Es1Skyline {
	/// <summary>The most frames the backdrop carries; retail banks hold 4.</summary>
	public const int MaxFrames = 16;

	private Es1Skyline(byte[] pixels, int width, int[] rows) {
		Pixels = pixels;
		Width = width;
		Rows = rows;
	}

	/// <summary>
	/// Every frame as RGBA8, <see cref="Width"/> across, frame <c>k</c>'s rows from row <c>k · </c><see cref="SlotRows"/>.
	/// The alpha is 0 where the frame holds palette index 0.
	/// </summary>
	public byte[] Pixels { get; }

	/// <summary>Frame 0's column count, <i>w</i>: every frame is stepped as this wide.</summary>
	public int Width { get; }

	/// <summary>Each frame's own row count.</summary>
	public int[] Rows { get; }

	/// <summary>The bank's frame count, <i>n</i>.</summary>
	public int FrameCount => Rows.Length;

	/// <summary>The rows each frame's slot in <see cref="Pixels"/> holds: the tallest frame's.</summary>
	public int SlotRows => Rows.Length == 0 ? 0 : Rows.Max();

	/// <summary>The image's row count.</summary>
	public int Height => SlotRows * FrameCount;

	/// <summary>Builds the skyline from the bank, or returns null for a bank with no frame to draw.</summary>
	public static Es1Skyline? From(DynamixBitmapArray? bank, DynamixPalette? palette) {
		if (bank?.Images is not { Length: > 0 } frames || palette == null || frames[0].Cols < 1) {
			return null;
		}

		frames = frames.Take(MaxFrames).ToArray();
		int width = frames[0].Cols;
		int[] rows = frames.Select(frame => (int)frame.Rows).ToArray();
		int slot = rows.Max();
		var pixels = new byte[width * slot * frames.Length * 4];
		for (int k = 0; k < frames.Length; k++) {
			var frame = frames[k];
			byte[] indices = frame.ImageData ?? [];
			for (int row = 0; row < frame.Rows; row++) {
				for (int column = 0; column < Math.Min(frame.Cols, width); column++) {
					int source = row * frame.Cols + column;
					if (source >= indices.Length) {
						continue;
					}

					byte index = indices[source];
					int at = (((k * slot) + row) * width + column) * 4;
					if (index == 0 || !palette.Colors.TryGetValue(index, out var entry)) {
						continue;
					}

					var color = entry.GetColor();
					pixels[at] = color.R;
					pixels[at + 1] = color.G;
					pixels[at + 2] = color.B;
					pixels[at + 3] = 255;
				}
			}
		}

		return new Es1Skyline(pixels, width, rows);
	}

	/// <summary>
	/// <i>X</i>, where frame 0 starts for a heading, before <c>Hzline_BlitHorizonStrip</c> steps it back a whole
	/// panorama at a time: <c>(heading · 2^16) / ((2^16 / n) · 2^16 / w)</c> in unsigned 32-bit arithmetic, so the
	/// <i>n</i> frames span one turn.
	/// </summary>
	/// <param name="heading">The camera's heading, as a binary angle.</param>
	public int StartFor(short heading) {
		uint numerator = (uint)(ushort)heading << 16;
		uint denominator = unchecked(((uint)(0x10000 / FrameCount) << 16) / (uint)Width);
		return denominator == 0 ? 0 : (int)(numerator / denominator);
	}
}
