using HercWorks.Core.Data.File.Dyn;

namespace Herculan.Engine.Shell;

/// <summary>
/// One occupied part slot of a <c>Grid</c> widget: its frame, where it sits in the grid, its blit flags
/// and its colour remap pairs.
/// </summary>
public sealed record ShellGridPart(DynamixBitmap Frame, int X, int Y, int Flags, (byte From, byte To)[] Remaps);

/// <summary>
/// The <c>Grid</c> widget (<c>ESGrid_Ctor</c>, <c>0040b7e0</c>) every picture of a machine in the shell is:
/// a filled panel with 16-pixel grid lines and thirty part slots, each a bitmap, a position, blit flags
/// and ten colour remap pairs. The squad panel's bay pictures and the repair screen's damage diagrams
/// are both one. See docs/retail/shell/weapons-and-repair.md, "The damage diagram".
/// </summary>
public static class ShellGrid {
	/// <summary>The part slots a grid carries, and the range <c>ESGrid_SetPart</c> asserts.</summary>
	public const int PartSlots = 30;

	/// <summary>Colour remap pairs per part slot.</summary>
	public const int RemapPairs = 10;

	/// <summary>The border and grid-line colour, <c>ESGrid_Ctor</c>'s <c>0x22</c> at <c>+0x4d</c> and <c>+0x6e6</c>.</summary>
	public const byte GridColor = 0x22;

	/// <summary>The grid pitch, a literal in <c>ESGrid_Paint</c>.</summary>
	private const int GridPitch = 0x10;

	/// <summary>A remap pair whose target is this is skipped — <c>ESGrid_InitRow</c>'s default.</summary>
	private const byte NoRemap = 0x10;

	/// <summary>
	/// <c>ESGrid_Paint</c> (<c>0040b97c</c>): the filled, bordered panel, the grid lines, then each part
	/// blitted and its remap pairs applied over its rect, in slot order. Lines run from 16 up to but not
	/// onto the far edge, so the border is the last line on each axis. A part's colour is chosen here,
	/// at paint time, and by rect rather than by mask, so a recoloured part also recolours the matching
	/// pixels of any earlier part it overlaps.
	/// </summary>
	/// <param name="borderColor">The widget's <c>+0x4d</c>, which a builder may overwrite.</param>
	/// <param name="lineColor">And its <c>+0x6e6</c>.</param>
	public static void Paint(ShellSurface surface, ShellRect rect, bool gridLines, ShellGridPart?[] parts,
			byte borderColor = GridColor, byte lineColor = GridColor) {
		int w = rect.Width - 1;
		int h = rect.Height - 1;
		var clip = surface.PushClip(rect);

		ShellChrome.PaintPanel(surface, rect, borderColor, fill: true);

		if (gridLines) {
			for (int y = GridPitch; y < h; y += GridPitch) {
				surface.Line(rect.X0, rect.Y0 + y, rect.X0 + w, rect.Y0 + y, lineColor);
			}

			for (int x = GridPitch; x < w; x += GridPitch) {
				surface.Line(rect.X0 + x, rect.Y0, rect.X0 + x, rect.Y0 + h, lineColor);
			}
		}

		foreach (var part in parts) {
			if (part == null) {
				continue;
			}

			int left = rect.X0 + part.X;
			int top = rect.Y0 + part.Y;
			surface.Blit(part.Frame, left, top, part.Flags);

			foreach (var (from, to) in part.Remaps) {
				if (to != NoRemap) {
					surface.Remap(left, top, left + part.Frame.Cols, top + part.Frame.Rows, from, to);
				}
			}
		}

		surface.PopClip(clip);
	}
}
