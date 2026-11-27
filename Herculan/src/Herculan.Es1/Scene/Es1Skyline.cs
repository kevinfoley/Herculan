using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;

namespace Herculan.Es1.Scene;

/// <summary>
/// The horizon skyline: the <c>hzline</c>'s bitmap bank <c>+0x50</c>, <c>dba\world&lt;N&gt;</c>, which
/// <c>Hzline_BlitHorizonStrip</c> (<c>ES1DBSIM 00026ac8</c>) stands on the horizon line as a 360° panorama
/// (docs/es1/rendering/ground-and-sky.md#the-horizon-skyline).
/// </summary>
public sealed class Es1Skyline {
	/// <summary>The most frames the backdrop carries; retail banks hold 4.</summary>
	public const int MaxFrames = 16;

	/// <summary>
	/// Every frame's palette indices, <see cref="Width"/> across, frame <c>k</c>'s rows from row <c>k · </c><see cref="SlotRows"/>;
	/// 0 where the frame holds index 0 and past a frame's own rows.
	/// </summary>
	private readonly byte[] _indices;

	private Es1Skyline(byte[] indices, int width, int[] rows) {
		_indices = indices;
		Width = width;
		Rows = rows;
	}

	/// <summary>
	/// Every frame as RGBA8 shown in the palette, laid out as the indices are. The alpha is 0 where the
	/// frame holds palette index 0 or an entry the palette lacks.
	/// </summary>
	/// <param name="palette">The palette.</param>
	public byte[] PixelsIn(DynamixPalette palette) {
		var pixels = new byte[_indices.Length * 4];
		for (int i = 0; i < _indices.Length; i++) {
			byte index = _indices[i];
			if (index == 0 || !palette.Colors.TryGetValue(index, out var entry)) {
				continue;
			}

			var color = entry.GetColor();
			pixels[i * 4] = color.R;
			pixels[i * 4 + 1] = color.G;
			pixels[i * 4 + 2] = color.B;
			pixels[i * 4 + 3] = 255;
		}

		return pixels;
	}

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

	/// <summary>
	/// <c>Hzline_LoadBitmapBank</c>(<c>dba\</c>bank) (<c>ES1DBSIM 00026dc8</c>); null when the install
	/// lacks the bank or it has no frame to draw.
	/// </summary>
	/// <param name="content">The game's content.</param>
	/// <param name="bank">The bank's name.</param>
	public static Es1Skyline? Load(GameContent content, string bank) =>
		content.Read("dba", bank + ".DBA") is { } bytes ? From(new DynamixBitmapArrayTransformer().Parse(bytes)) : null;

	/// <summary>Builds the skyline from the bank, or returns null for a bank with no frame to draw.</summary>
	public static Es1Skyline? From(DynamixBitmapArray? bank) {
		if (bank?.Images is not { Length: > 0 } frames || frames[0].Cols < 1) {
			return null;
		}

		frames = frames.Take(MaxFrames).ToArray();
		int width = frames[0].Cols;
		int[] rows = frames.Select(frame => (int)frame.Rows).ToArray();
		int slot = rows.Max();
		var all = new byte[width * slot * frames.Length];
		for (int k = 0; k < frames.Length; k++) {
			var frame = frames[k];
			byte[] indices = frame.ImageData ?? [];
			for (int row = 0; row < frame.Rows; row++) {
				for (int column = 0; column < Math.Min(frame.Cols, width); column++) {
					int source = row * frame.Cols + column;
					if (source < indices.Length) {
						all[((k * slot) + row) * width + column] = indices[source];
					}
				}
			}
		}

		return new Es1Skyline(all, width, rows);
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
