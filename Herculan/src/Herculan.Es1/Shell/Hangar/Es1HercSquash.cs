using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Hangar;

/// <summary>
/// The "Correct HERC Bay aspect ratio" tweak (<see cref="Settings.Es1TweakSettingDefinitions.SquashBayHercs"/>):
/// <b>this engine's own</b>. The HERCs' diagram art (docs/es1/formats/herc-diagrams.md) is drawn for 320 × 200 pixels
/// shown square, where the bay's other art is drawn for the 4:3 screen that shows each pixel 1.2 times taller than wide,
/// so a HERC stands a fifth too tall beside its 3D model and the studio's maquette. With the tweak on, every picture of
/// the diagram, the body, the damage marks, the mounts, their outlines and the Colossus' shoulder piece, is drawn at 5/6
/// of its height about the chassis' foot line (<see cref="Es1HercDiagrams.FootLine"/>), so the feet stay where they
/// stand. Every picture samples the same screen rows (<see cref="SourceRow"/>), so they stay aligned with one another,
/// and a press on the HERC is tested at the row it shows. The bay's scripts are left as they are.
/// </summary>
public static class Es1HercSquash {
	/// <summary>Whether the tweak is on.</summary>
	public static bool On =>
		Engine.Settings.TweakSettings.Current.GetSettingValue(Settings.Es1TweakSettingDefinitions.SquashBayHercs);

	/// <summary>
	/// The row the unsquashed diagram puts at screen row <paramref name="y"/>'s centre: <i>foot</i> − (<i>foot</i> −
	/// <i>y</i> − ½) × 6/5, rounded down. Six rows above the foot line show in five.
	/// </summary>
	/// <param name="y">The screen row.</param>
	/// <param name="foot">The foot line, the row below the feet.</param>
	public static int SourceRow(int y, int foot) {
		// foot − ⌈(6d − 3) / 5⌉ for d = foot − y, the division rounded up for either sign.
		int numerator = 6 * (foot - y) - 3;
		int up = numerator >= 0 ? (numerator + 4) / 5 : -(-numerator / 5);
		return foot - up;
	}

	/// <summary>The screen rows, top included and bottom not, that show the unsquashed rows <paramref name="top"/> to <paramref name="bottom"/> (not included).</summary>
	/// <param name="top">The first unsquashed row.</param>
	/// <param name="bottom">The row after the last.</param>
	/// <param name="foot">The foot line.</param>
	public static (int Top, int Bottom) Rows(int top, int bottom, int foot) {
		int first = foot - (foot - top) * 5 / 6 - 2;
		while (SourceRow(first, foot) < top) {
			first++;
		}

		int last = foot - (foot - bottom) * 5 / 6 + 2;
		while (last > first && SourceRow(last - 1, foot) >= bottom) {
			last--;
		}

		return (first, last);
	}

	/// <summary>
	/// <paramref name="rect"/> squashed: its top and bottom moved to the rows <see cref="Rows"/> gives for them, so a
	/// sprite's rect, whose bottom is its corner's row plus the bitmap's height, keeps that form.
	/// </summary>
	/// <param name="rect">The unsquashed rect.</param>
	/// <param name="foot">The foot line.</param>
	public static Es1Rect Rect(Es1Rect rect, int foot) {
		var (top, bottom) = Rows(rect.Y0, rect.Y1, foot);
		return new Es1Rect(rect.X0, top, rect.X1, bottom);
	}

	/// <summary>
	/// <paramref name="bitmap"/> with its top-left at (<paramref name="x"/>, <paramref name="y"/>) blitted squashed: each
	/// screen row of <see cref="Rows"/> takes the bitmap's row that <see cref="SourceRow"/> names, as
	/// <see cref="Es1PanelCanvas.BlitRow"/> draws it.
	/// </summary>
	/// <param name="canvas">The canvas drawn on.</param>
	/// <param name="bitmap">The picture, or null.</param>
	/// <param name="x">The x of the top-left.</param>
	/// <param name="y">The y of the top-left.</param>
	/// <param name="foot">The foot line.</param>
	public static void Blit(Es1PanelCanvas canvas, DynamixBitmap? bitmap, int x, int y, int foot) {
		if (bitmap == null) {
			return;
		}

		var (top, bottom) = Rows(y, y + bitmap.Rows, foot);
		for (int row = top; row < bottom; row++) {
			canvas.BlitRow(bitmap, SourceRow(row, foot) - y, x, row);
		}
	}
}
