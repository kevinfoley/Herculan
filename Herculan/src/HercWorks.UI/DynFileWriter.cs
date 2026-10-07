using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform.Common;
using System.Drawing;
using System.Drawing.Imaging;

namespace HercWorks.UI;

/// <summary>
/// Ported from org.hercworks.core.io.write.DynFileWriter. Lives in HercWorks.UI rather than
/// HercWorks.Core: it dumps parsed .DBM data to real .png/.bmp files on disk for a human to look
/// at, which is an MDK export feature the future engine port will never call — the engine
/// consumes DynamixBitmap/DynamixPalette data directly, not files written back to disk. Core's
/// ColorBytes carries a cross-platform-safe RgbaColor internally (see docs/herculan/planning.md's
/// "Known technical debt" section), converted to a real System.Drawing.Color here at the UI
/// boundary, since this project (net8.0-windows, WinForms) is free to use GDI+ directly — same
/// pattern as DynamixImageRenderer.cs.
/// </summary>
public static class DynFileWriter {
	private static Color ToGdiColor(RgbaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

	public static void WriteDBMToFile(DynamixBitmap dbm, bool index0Alpha, DynamixPalette palette, string filePath) {
		string file = filePath + dbm.FileName + ".png";

		using var imageOut = new Bitmap(dbm.Cols, dbm.Rows, PixelFormat.Format32bppRgb);

		int i = 0;
		for (int r = 0; r < dbm.Rows; r++) {
			for (int c = 0; c < dbm.Cols; c++) {
				if (i >= dbm.ImageData!.Length) {
					break;
				}

				int cell = r * dbm.Cols + c;
				int idx = dbm.ImageData[cell] & 0xFF;

				try {
					RgbaColor pixel = index0Alpha && idx == 0
						? palette.Index0AlphaKey.GetColor()
						: palette.ColorAt(idx).GetColor();

					imageOut.SetPixel(c, r, ToGdiColor(pixel));
				} catch (KeyNotFoundException) {
					Console.WriteLine($"PIXEL({c},{r})={(byte)idx}~MISSING");
				} catch (Exception e) {
					Console.WriteLine("ERROR - " + e.Message);
				}
				i++;
			}
		}

		try {
			imageOut.Save(file, ImageFormat.Png);
			Console.WriteLine("write filetrue");
		} catch (Exception t) {
			Console.WriteLine(t.Message);
			Console.WriteLine("write filefalse");
		}
	}

	public static void WriteDBMToFile(DynamixBitmap dbm, string filePath) {
		string file = filePath + dbm.FileName + ".DBM";

		try {
			var transform = new DynamixBitmapTransformer();
			byte[]? data = transform.Write(dbm);
			if (data != null) {
				File.WriteAllBytes(file, data);
			}
		} catch (IOException e) {
			Console.WriteLine(e.Message);
		} catch (IndexOutOfRangeException arryErr) {
			Console.WriteLine("ERROR - " + arryErr.Message);
		}
	}

	/// <summary>
	/// Example: ZONES.VOL/DBA/ZONE_X.DBA. These are assumed to be grayscale heightmaps, thus
	/// they have no corresponding DPL files, and instead use an implicit, derived grayscale
	/// palette.
	/// </summary>
	public static void WriteDBMToHeightmap(DynamixBitmap dbm, string filePath) {
		string file = filePath + dbm.FileName + ".png";

		using var imageOut = new Bitmap(dbm.Cols, dbm.Rows, PixelFormat.Format8bppIndexed);

		int i = 0;
		for (int r = 0; r < dbm.Rows; r++) {
			for (int c = 0; c < dbm.Cols; c++) {
				if (i >= dbm.ImageData!.Length) {
					break;
				}

				int cell = r * dbm.Cols + c;
				int idx = dbm.ImageData[cell] & 0xFF;

				try {
					// Convert to a basic grayscale value.
					byte clr = (byte)(idx / 255.0 * 200);
					int gray = clr;
					imageOut.SetPixel(c, r, Color.FromArgb(gray, gray, gray));
				} catch (Exception e) {
					Console.WriteLine("ERROR - " + e.Message);
				}
				i++;
			}
		}

		try {
			imageOut.Save(file, ImageFormat.Png);
			Console.WriteLine("write filetrue");
		} catch (Exception t) {
			Console.WriteLine(t.Message);
			Console.WriteLine("write filefalse");
		}
	}

	public static void WriteDBMToFileNoPalette(DynamixBitmap dbm, string filePath) {
		string file = filePath + dbm.FileName + "_RAW.bmp";

		using var imageOut = new Bitmap(dbm.Cols, dbm.Rows, PixelFormat.Format8bppIndexed);

		var bmpData = imageOut.LockBits(new Rectangle(0, 0, dbm.Cols, dbm.Rows), ImageLockMode.WriteOnly, imageOut.PixelFormat);
		try {
			int copyLen = Math.Min(dbm.ImageData!.Length, bmpData.Stride * dbm.Rows);
			System.Runtime.InteropServices.Marshal.Copy(dbm.ImageData, 0, bmpData.Scan0, copyLen);
		} finally {
			imageOut.UnlockBits(bmpData);
		}

		try {
			imageOut.Save(file, ImageFormat.Bmp);
		} catch (Exception e) {
			Console.WriteLine(e.Message);
		}
	}
}
