using System.Drawing;
using System.Drawing.Imaging;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;

namespace HercWorks.UI;

/// <summary>
/// Renders parsed Dynamix bitmap/palette data (from HercWorks.Core) into real
/// System.Drawing.Bitmap images, for previewing and PNG export. Kept in the UI project rather
/// than Core, since producing GDI+ bitmaps is a rendering concern, not a file-format concern —
/// Core's ColorBytes carries a cross-platform-safe <see cref="RgbaColor"/> internally (Core has
/// no System.Drawing.Common dependency — see docs/herculan/planning.md's "Known technical debt"
/// section), converted to a real System.Drawing.Color here at the UI boundary, since this
/// project (net8.0-windows, WinForms) is free to use GDI+ directly.
/// </summary>
public static class DynamixImageRenderer {
	private static Color ToGdiColor(RgbaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
	/// <summary>
	/// Renders a single DynamixBitmap frame using the given palette's colors (indexed color —
	/// each ImageData byte is a palette index). Without a palette, falls back to treating each
	/// index byte as a grayscale intensity, since a DBM doesn't bind its own palette (see
	/// DynamixBitmap's doc comment) — this fallback is only useful for a rough preview, not an
	/// accurate render.
	/// </summary>
	public static Bitmap RenderFrame(DynamixBitmap frame, DynamixPalette? palette) {
		int width = frame.Cols;
		int height = frame.Rows;
		var bitmap = new Bitmap(Math.Max(width, 1), Math.Max(height, 1), PixelFormat.Format32bppArgb);

		byte[] imageData = frame.ImageData ?? Array.Empty<byte>();
		int pixelCount = Math.Min(imageData.Length, width * height);

		for (int i = 0; i < pixelCount; i++) {
			int x = i % width;
			int y = i / width;
			byte index = imageData[i];

			Color color = palette != null && palette.Colors.TryGetValue(index, out var colorBytes)
				? ToGdiColor(colorBytes.GetColor())
				: Color.FromArgb(255, index, index, index);

			bitmap.SetPixel(x, y, color);
		}

		return bitmap;
	}

	/// <summary>
	/// Renders every color in a DynamixPalette as a grid of swatches — matches ES2Excavator's
	/// README-described ".DPL exports to a png image of colors in a grid" utility.
	/// </summary>
	public static Bitmap RenderPaletteGrid(DynamixPalette palette, int swatchSize = 20, int columns = 16) {
		int colorCount = Math.Max(palette.Colors.Count, 1);
		int rows = (int)Math.Ceiling(colorCount / (double)columns);

		var bitmap = new Bitmap(columns * swatchSize, rows * swatchSize, PixelFormat.Format32bppArgb);
		using var g = Graphics.FromImage(bitmap);
		g.Clear(Color.Black);

		foreach (var (index, colorBytes) in palette.Colors.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value))) {
			int col = index % columns;
			int row = index / columns;

			using var brush = new SolidBrush(ToGdiColor(colorBytes.GetColor()));
			g.FillRectangle(brush, col * swatchSize, row * swatchSize, swatchSize, swatchSize);
		}

		return bitmap;
	}

	public static void SaveAsPng(Bitmap bitmap, string path) => bitmap.Save(path, ImageFormat.Png);
}
