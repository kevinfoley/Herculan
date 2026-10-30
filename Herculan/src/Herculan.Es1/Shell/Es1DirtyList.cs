using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's dirty boxes (docs/es1/rendering/screen-pages.md#dirty-boxes): every draw on a shell screen records the box
/// it covered, and the frames that follow copy the background on page 2 back over that box. The list is
/// <c>000a09c0</c>; its records come from blocks of ten, which this engine leaves to the allocator.
/// </summary>
public sealed class Es1DirtyList {
	/// <summary>A record, <c>0x13</c> bytes: the box, its area as added, and the frames before it is copied.</summary>
	private sealed class Box {
		public ushort X, Y, Width, Height, Area;
		public sbyte Delay;
	}

	private readonly Es1VideoMemory _display;

	// The list from its head; a new record goes in front.
	private readonly List<Box> _boxes = [];

	/// <summary>
	/// <c>DirtyList_CreateContext</c> (<c>00040d54</c>) on <paramref name="display"/>: the flush's render context, clip mode 1
	/// to (0, 0)–(319, 199) with the translation flag set and the origin (0, 0), so a copy is cut to the screen
	/// (<see cref="ClipRight"/>, <see cref="ClipBottom"/>).
	/// </summary>
	public Es1DirtyList(Es1VideoMemory display) {
		_display = display;
	}

	/// <summary>The flush context's clip rect's right and bottom, inclusive; its left and top are 0.</summary>
	public const int ClipRight = 319, ClipBottom = 199;

	/// <summary>The records the list holds.</summary>
	public int Count => _boxes.Count;

	/// <summary>
	/// <c>DirtyBox_Add</c> (<c>00040f00</c>)(delay, height, width, y, x), stack order: nothing for a box off the display,
	/// <see cref="Es1VideoMemory.PageWidth"/> × <see cref="Es1VideoMemory.PageHeight"/>; a negative <i>x</i> or <i>y</i>
	/// added to the width or height and set to 0; the box widened to whole groups of four columns; the width cut to
	/// <i>W</i> − 1 − <i>x</i> and the height to <i>H</i> − 1 − <i>y</i> when they run past; and with both non-zero a
	/// record made and given to <see cref="Insert"/>. The arithmetic is retail's on <c>int16</c>s.
	/// </summary>
	public void Add(int x, int y, int width, int height, int delay) {
		int right = Es1VideoMemory.PageWidth - 1, bottom = Es1VideoMemory.PageHeight - 1;
		short sx = (short)x, sy = (short)y, sw = (short)width, sh = (short)height;
		if (right < sx || sx + sw < 0 || bottom < sy || sy + sh < 0) {
			return;
		}

		if (sx < 0) {
			sw = (short)(sw + sx);
			sx = 0;
		}

		if (sy < 0) {
			sh = (short)(sh + sy);
			sy = 0;
		}

		sw = (short)(sw + sx % 4);
		short round = (short)(sw % 4);
		sw = (short)(sw + (round == 0 ? 0 : 4 - round));
		sx = (short)(sx - sx % 4);
		if (right < sx + sw) {
			sw = (short)(right - sx);
		}

		if (bottom < sy + sh) {
			sh = (short)(bottom - sy);
		}

		if (sw == 0 || sh == 0) {
			return;
		}

		Insert(new Box {
			X = (ushort)sx, Y = (ushort)sy, Width = (ushort)sw, Height = (ushort)sh, Area = (ushort)(sw * sh), Delay = (sbyte)delay,
		});
	}

	/// <summary><c>DirtyBox_AddRect</c> (<c>00041062</c>)(delay, inclusive rect): <see cref="Add"/> of its box.</summary>
	public void AddRect(int x0, int y0, int x1, int y1, int delay) => Add(x0, y0, x1 - x0 + 1, y1 - y0 + 1, delay);

	/// <summary>
	/// <c>DirtyList_Insert</c> (<c>000411a5</c>): among the records of the same delay whose box touches or overlaps the new
	/// one, unsigned, the one for which, with <i>U</i> the 16-bit area of the box round both and <i>S</i> the 16-bit sum of
	/// the two areas, <i>U</i> ≤ <i>S</i> and <i>S</i> − <i>U</i> is largest above 0, the first of equals, takes the box
	/// round both, keeping its area; with none the new record goes in front.
	/// </summary>
	private void Insert(Box added) {
		Box? best = null;
		int bestSaving = 0;
		(ushort X, ushort Y, ushort Width, ushort Height) merged = default;
		foreach (var box in _boxes) {
			if (box.Delay != added.Delay || box.X > added.X + added.Width || added.X > box.X + box.Width
					|| box.Y > added.Y + added.Height || added.Y > box.Y + box.Height) {
				continue;
			}

			ushort left = Math.Min(added.X, box.X), top = Math.Min(added.Y, box.Y);
			int rightEdge = Math.Max(added.X + added.Width, box.X + box.Width);
			int bottomEdge = Math.Max(added.Y + added.Height, box.Y + box.Height);
			ushort width = (ushort)(rightEdge - left), height = (ushort)(bottomEdge - top);
			ushort union = (ushort)(width * height);
			ushort sum = (ushort)(added.Area + box.Area);
			if (union <= sum && bestSaving < sum - union) {
				bestSaving = sum - union;
				best = box;
				merged = (left, top, width, height);
			}
		}

		if (best == null) {
			_boxes.Insert(0, added);
			return;
		}

		(best.X, best.Y, best.Width, best.Height) = merged;
	}

	/// <summary>
	/// <c>DirtyList_Flush</c> (<c>000413db</c>)(<paramref name="drawPage"/>, <paramref name="sourcePage"/>): the display's
	/// source and draw pages set; each record of delay 0 copied from the source page to the draw page as the inclusive
	/// rect {<i>x</i>, <i>y</i>, <i>x</i> + width, <i>y</i> + height}, cut to the flush context's clip rect, so one column
	/// and one row past the box; every record's delay lowered by 1; the two pages put back; and
	/// <c>DirtyList_Prune</c> (<c>0004109c</c>), every record whose delay is now below 0 freed.
	/// </summary>
	public void Flush(int drawPage, int sourcePage) {
		int keptDraw = _display.DrawPage, keptSource = _display.SourcePage;
		_display.SourcePage = sourcePage;
		_display.DrawPage = drawPage;
		foreach (var box in _boxes) {
			if (box.Delay == 0) {
				int x1 = Math.Min(box.X + box.Width, ClipRight), y1 = Math.Min(box.Y + box.Height, ClipBottom);
				_display.CopyRect(box.X, box.Y, x1, y1, 0, 0);
			}

			box.Delay--;
		}

		_display.DrawPage = keptDraw;
		_display.SourcePage = keptSource;
		_boxes.RemoveAll(box => box.Delay < 0);
	}
}
