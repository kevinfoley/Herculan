using HercWorks.Core.Data.File.Dbsim;

namespace Herculan.Es1.View;

/// <summary>
/// A <c>0x204</c>-byte clip block as the render context holds it in clip mode 2: a list of regions, each a rect or a block
/// of one span a row, inside any of which the world is drawn (docs/es1/formats/hd-ed-clip-regions.md). A view's block is
/// its <c>ed&lt;i&gt;</c> file's (<c>CockpitClipRegions_Load</c>, <c>ES1DBSIM 00024d44</c>); the external view's,
/// <c>ExternalView_ClipBlock</c> (<c>000afb94</c>), holds the one rect <c>ExternalView_ClipRegion</c>
/// (docs/es1/simulation/cockpit-views.md#the-external-view).
/// </summary>
public sealed class Es1ClipBlock {
	private readonly (int X0, int Y0, int X1, int Y1)[] _rects;
	private readonly Es1ClipSpanBlock[] _spans;

	private Es1ClipBlock((int X0, int Y0, int X1, int Y1)[] rects, Es1ClipSpanBlock[] spans) {
		_rects = rects;
		_spans = spans;
	}

	/// <summary>The rect regions, their bounds included.</summary>
	public IReadOnlyList<(int X0, int Y0, int X1, int Y1)> Rects => _rects;

	/// <summary>The span regions, one span a row each.</summary>
	public IReadOnlyList<Es1ClipSpanBlock> Spans => _spans;

	/// <summary>A block of no region, in which nothing is drawn.</summary>
	public static Es1ClipBlock Empty { get; } = new([], []);

	/// <summary>
	/// The block a view's clip file loads: its rects across the screen's width, columns 0 to 319, then its span blocks,
	/// held in an array of its own, so <see cref="MoveRows"/> moves this block's regions and not the file's.
	/// </summary>
	public static Es1ClipBlock FromFile(Es1ClipRegions regions) => new(
		regions.Rects.Select(rect => (0, (int)rect.Top, Es1CockpitViews.ScreenWidth - 1, (int)rect.Bottom)).ToArray(),
		regions.Blocks.ToArray());

	/// <summary>
	/// Every region moved down by <paramref name="dy"/> rows in place, as the view shake moves the current view's
	/// (docs/es1/simulation/cockpit-views.md#the-view-shake): a rect's two rows, a span block's first row as an
	/// <c>int16</c>. A block holds its regions by pointer, so the render context's copy of it moves with it.
	/// </summary>
	public void MoveRows(int dy) {
		for (int i = 0; i < _rects.Length; i++) {
			var rect = _rects[i];
			_rects[i] = (rect.X0, rect.Y0 + dy, rect.X1, rect.Y1 + dy);
		}

		for (int i = 0; i < _spans.Length; i++) {
			_spans[i] = new Es1ClipSpanBlock { FirstRow = (short)(_spans[i].FirstRow + dy), Rows = _spans[i].Rows };
		}
	}

	/// <summary>
	/// The energy meter's shift of its <c>edg\ENG_CLP</c> regions (docs/es1/formats/hd-ed-clip-regions.md#which-files): each
	/// span block's first row moved down by <paramref name="dy"/> and each of its spans' two columns right by
	/// <paramref name="dx"/>, as <c>int16</c>. Retail reads every region's data as a span block's; a rect region, which no
	/// retail <c>ENG_CLP</c> file holds, is kept as loaded here, an engine guard.
	/// </summary>
	public Es1ClipBlock Shifted(int dx, int dy) => new(_rects, _spans.Select(block => new Es1ClipSpanBlock {
		FirstRow = (short)(block.FirstRow + dy),
		Rows = block.Rows.Select(span => new Es1ClipSpan { Start = (short)(span.Start + dx), End = (short)(span.End + dx) }).ToArray(),
	}).ToArray());

	/// <summary>A block of the one rect region {<paramref name="x0"/>, <paramref name="y0"/>, <paramref name="x1"/>, <paramref name="y1"/>}, its bounds included.</summary>
	public static Es1ClipBlock FromRect(int x0, int y0, int x1, int y1) => new([(x0, y0, x1, y1)], []);

	/// <summary>A block of the rect regions <paramref name="rects"/>, in order, their bounds included.</summary>
	public static Es1ClipBlock FromRects(params (int X0, int Y0, int X1, int Y1)[] rects) => new(rects.ToArray(), []);

	/// <summary>
	/// Whether a pixel of the page lies in one of the regions: a rect's bounds included, and a span's last column
	/// included as ES2's span ends are (docs/es1/formats/hd-ed-clip-regions.md#open).
	/// </summary>
	public bool Contains(int x, int y) {
		foreach (var rect in _rects) {
			if (x >= rect.X0 && x <= rect.X1 && y >= rect.Y0 && y <= rect.Y1) {
				return true;
			}
		}

		foreach (var block in _spans) {
			int row = y - block.FirstRow;
			if (row >= 0 && row < block.Rows.Length && x >= block.Rows[row].Start && x <= block.Rows[row].End) {
				return true;
			}
		}

		return false;
	}
}
