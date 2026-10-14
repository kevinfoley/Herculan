namespace HercWorks.UI;

/// <summary>
/// A tooltip on a grid cell, marked with a small triangle in the cell's top-right corner the way a
/// spreadsheet marks a cell with a comment. <see cref="Set"/> attaches one; <see cref="Paint"/> is wired
/// to the grid's <c>CellPainting</c>. Header cells are left unmarked, since every row header has one.
///
/// <para>The mark keys off the cell's <see cref="DataGridViewCell.Tag"/>, not its
/// <see cref="DataGridViewCell.ToolTipText"/>: on a grid that shows cell tooltips, that getter answers
/// with the cell's own value when no tooltip was set, so every cell would read as noted.</para>
/// </summary>
internal static class GridCellNotes {
	/// <summary>The triangle's two short sides, in pixels.</summary>
	private const int MarkerSize = 6;

	/// <summary>The tag a noted cell carries.</summary>
	private static readonly object NoteTag = new();

	/// <summary>Gives <paramref name="cell"/> the tooltip <paramref name="note"/> and marks it; a null note does nothing.</summary>
	public static void Set(DataGridViewCell cell, string? note) {
		if (string.IsNullOrEmpty(note)) {
			return;
		}

		cell.ToolTipText = note;
		cell.Tag = NoteTag;
	}

	public static void Paint(DataGridView grid, DataGridViewCellPaintingEventArgs e) {
		if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics == null
			|| grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag != NoteTag) {
			return;
		}

		e.Paint(e.CellBounds, DataGridViewPaintParts.All);

		var bounds = e.CellBounds;
		Point[] corner = {
			new(bounds.Right - MarkerSize - 1, bounds.Top),
			new(bounds.Right - 1, bounds.Top),
			new(bounds.Right - 1, bounds.Top + MarkerSize)
		};
		using var brush = new SolidBrush(Color.DarkOrange);
		e.Graphics.FillPolygon(brush, corner);
		e.Handled = true;
	}
}
