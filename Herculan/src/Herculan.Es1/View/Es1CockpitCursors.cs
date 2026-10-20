namespace Herculan.Es1.View;

/// <summary>
/// The cockpit's cursor slots, its <c>+0x222</c>–<c>+0x236</c> (docs/es1/simulation/cockpit-panel.md#the-cockpit-object):
/// three slots holding a cursor each, the forward view's, the other views', and the edge slot that takes the arrow while
/// the pointer is over a screen-edge strip; the active slot, whose cursor the display's cursor shows; and the slot the
/// edge slot took over from. Each change of the active cursor goes to <c>Cursor_SetImage</c>
/// (docs/es1/simulation/cockpit-panel.md#the-screen-edges-and-the-cursors).
/// </summary>
public sealed class Es1CockpitCursors {
	/// <summary>The cursors' indices into <c>Cockpit_CursorImages</c> (<see cref="Es1CockpitViews.CursorNames"/>).</summary>
	public const int Arrow = 0, Gunsight = 1, MapPick = 2, North = 3, South = 4, East = 5, West = 6;

	private readonly IReadOnlyList<Es1CursorImage?> _images;
	private readonly Es1Cursor _cursor;

	/// <summary>
	/// <c>ColorSchemePanels_LoadAll</c>'s part: the arrow in the forward and the other views' slots, the forward slot the
	/// active one, no arrow picked.
	/// </summary>
	/// <param name="images"><c>Cockpit_CursorImages</c> (<c>00092d20</c>).</param>
	/// <param name="cursor">The display's cursor <c>Cursor_SetImage</c> installs them on.</param>
	internal Es1CockpitCursors(IReadOnlyList<Es1CursorImage?> images, Es1Cursor cursor) {
		_images = images;
		_cursor = cursor;
		Forward = Image(Arrow);
		OffForward = Image(Arrow);
		Active = Es1CursorSlot.Forward;
	}

	/// <summary><c>+0x22e</c>, the edge slot's cursor.</summary>
	public Es1CursorImage? Edge { get; private set; }

	/// <summary><c>+0x232</c>, the forward view's cursor.</summary>
	public Es1CursorImage? Forward { get; private set; }

	/// <summary><c>+0x236</c>, the other views' cursor.</summary>
	public Es1CursorImage? OffForward { get; private set; }

	/// <summary><c>+0x222</c>, the active slot.</summary>
	public Es1CursorSlot Active { get; private set; }

	/// <summary><c>+0x226</c>, the slot the edge slot took over from; none in the zero-filled cockpit.</summary>
	public Es1CursorSlot Saved { get; private set; }

	/// <summary><c>+0x22a</c>, the edge arrow <c>Cockpit_PickEdgeCursor</c> picked, an index into <c>Cockpit_CursorImages</c>, 0 for none.</summary>
	public int EdgeArrow { get; set; }

	/// <summary>
	/// <c>Cockpit_UpdateEdgeCursor</c> (<c>ES1DBSIM 0002de48</c>), each frame: with an arrow picked, its cursor in the edge
	/// slot, the edge slot made active (the slot it replaces saved) and its cursor passed on, every frame; with none and
	/// the edge slot active, the saved slot made active again and its cursor passed on.
	/// </summary>
	public void UpdateEdgeCursor() {
		if (EdgeArrow == 0) {
			if (Active == Es1CursorSlot.Edge) {
				Active = Saved;
				_cursor.SetImage(Of(Saved));
			}

			return;
		}

		Edge = Image(EdgeArrow);
		if (Active != Es1CursorSlot.Edge) {
			Saved = Active;
			Active = Es1CursorSlot.Edge;
		}

		_cursor.SetImage(Edge);
	}

	/// <summary><c>Cockpit_SetForwardCursor</c> (<c>0002df04</c>)(index): cursor <paramref name="index"/> in the forward slot, passed on when that slot is active.</summary>
	public void SetForwardCursor(int index) {
		Forward = Image(index);
		if (Active == Es1CursorSlot.Forward) {
			_cursor.SetImage(Forward);
		}
	}

	/// <summary><c>Cockpit_SetOffForwardCursor</c> (<c>0002df6c</c>)(index): cursor <paramref name="index"/> in the other views' slot, passed on when that slot is active.</summary>
	public void SetOffForwardCursor(int index) {
		OffForward = Image(index);
		if (Active == Es1CursorSlot.OffForward) {
			_cursor.SetImage(OffForward);
		}
	}

	/// <summary><c>Cockpit_UseForwardCursor</c> (<c>0002e060</c>): <see cref="Use"/> on the forward slot.</summary>
	public void UseForwardCursor() => Use(Es1CursorSlot.Forward);

	/// <summary><c>Cockpit_UseOffForwardCursor</c> (<c>0002dfd4</c>): <see cref="Use"/> on the other views' slot.</summary>
	public void UseOffForwardCursor() => Use(Es1CursorSlot.OffForward);

	/// <summary>
	/// <paramref name="slot"/> the active slot and its cursor passed on; while the edge slot is active, the slot it goes
	/// back to instead.
	/// </summary>
	private void Use(Es1CursorSlot slot) {
		if (Active == Es1CursorSlot.Edge) {
			Saved = slot;
			return;
		}

		Active = slot;
		_cursor.SetImage(Of(slot));
	}

	private Es1CursorImage? Of(Es1CursorSlot slot) => slot switch {
		Es1CursorSlot.Edge => Edge,
		Es1CursorSlot.Forward => Forward,
		Es1CursorSlot.OffForward => OffForward,
		_ => null,
	};

	private Es1CursorImage? Image(int index) => index >= 0 && index < _images.Count ? _images[index] : null;
}

/// <summary>A slot of <see cref="Es1CockpitCursors"/>, by its offset in the cockpit.</summary>
public enum Es1CursorSlot {
	/// <summary>A null slot pointer.</summary>
	None,

	/// <summary><c>+0x22e</c>.</summary>
	Edge,

	/// <summary><c>+0x232</c>.</summary>
	Forward,

	/// <summary><c>+0x236</c>.</summary>
	OffForward,
}
