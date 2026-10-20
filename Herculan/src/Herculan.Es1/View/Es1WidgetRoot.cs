namespace Herculan.Es1.View;

/// <summary>
/// The root widget, <c>CTLWindow</c> (docs/es1/simulation/cockpit-mouse.md#the-root-widget--ctlwindow_ctor-00043cac):
/// its rect, whose origin is the canvas origin a press is tested from, its viewport's rect, and one flat list of the
/// widgets a click can reach, the first registered winning where two overlap; with the press, the release and the
/// pressed widget's tracking that the frame's mouse events run on it
/// (docs/es1/simulation/cockpit-mouse.md#press-release-and-drag). The viewport's clip mode and the repaint flag
/// <c>+0x223</c> are not kept: nothing this engine draws reads them yet.
/// </summary>
public sealed class Es1WidgetRoot {
	/// <summary>The list's 64 widget pointers.</summary>
	public const int Capacity = 64;

	private readonly List<Es1Widget> _clickables = new(Capacity);

	/// <summary><c>CTLWindow_Ctor</c> (<c>ES1DBSIM 00043cac</c>)(0, <paramref name="rect"/>): the rect copied to <c>+0</c> and to the viewport's rect, and the list empty.</summary>
	public Es1WidgetRoot(Es1Rect rect) {
		Rect = rect;
		ViewportRect = rect;
	}

	/// <summary><c>+0</c>–<c>+0xc</c>.</summary>
	public Es1Rect Rect { get; private set; }

	/// <summary><c>+0x227</c>–<c>+0x233</c>, the viewport's rect <c>+0x210</c>, whose origin the frame's mouse events take off a point for <c>CockpitMouse_CanvasPoint</c>.</summary>
	public Es1Rect ViewportRect { get; private set; }

	/// <summary><c>+0x252</c>, the clickable list, its <c>int16</c> count at <c>+0x250</c>.</summary>
	public IReadOnlyList<Es1Widget> Clickables => _clickables;

	/// <summary>
	/// <c>Widget_RegisterClickable</c> (<c>000443d4</c>)(widget, root): appended to the list. Retail checks no bound and
	/// writes past the 64 slots; this one refuses a 65th, an engine guard that no retail cockpit reaches.
	/// </summary>
	public void RegisterClickable(Es1Widget widget) {
		if (_clickables.Count >= Capacity) {
			throw new InvalidOperationException($"The root widget's list holds {Capacity} widgets.");
		}

		_clickables.Add(widget);
	}

	/// <summary><c>Widget_HitTest</c> on the root itself, whose hit-shape byte is 0: whether the point lies in <see cref="Rect"/>.</summary>
	public bool HitTest(int x, int y) => Rect.Contains(x, y);

	/// <summary>
	/// <c>Widget_HitTestChildren</c> (<c>0004401c</c>)(root, point): the index of the first widget of the list that
	/// <see cref="Es1Widget.HitTest"/> passes and whose state is not <see cref="Es1Widget.Hidden"/>, else −1.
	/// </summary>
	public short HitTestChildren(int x, int y) {
		for (short i = 0; i < _clickables.Count; i++) {
			var widget = _clickables[i];
			if (widget.HitTest(x, y) && widget.State != Es1Widget.Hidden) {
				return i;
			}
		}

		return -1;
	}

	/// <summary>
	/// <c>Widget_OnMouseDown</c> (<c>00043e00</c>)(root, point in screen pixels): the point less the root's origin
	/// hit-tested into <see cref="Es1WidgetGlobals.PressedIndex"/>; a widget found is pressed, given the drag with that
	/// point when its drag flag is 1, and repainted.
	/// </summary>
	public void OnMouseDown(Es1WidgetGlobals globals, int x, int y) {
		int canvasX = x - Rect.X0, canvasY = y - Rect.Y0;
		globals.PressedIndex = HitTestChildren(canvasX, canvasY);
		if (globals.PressedIndex == -1) {
			return;
		}

		var widget = _clickables[globals.PressedIndex];
		widget.State = Es1Widget.Pressed;
		if (widget.DragFlag == 1) {
			widget.DragTo(canvasX, canvasY);
			globals.DragCapture = true;
		}

		Repaint(widget);
	}

	/// <summary>
	/// <c>Widget_OnMouseUp</c> (<c>00043eac</c>)(root, point): the point less the root's origin hit-tested into
	/// <see cref="Es1WidgetGlobals.PressedIndex"/>; a widget found is clicked with its value when it is the one that was
	/// pressed or the right button is in <see cref="Es1WidgetGlobals.ClickButtons"/>, put up, repainted, and the index
	/// cleared. Over no widget nothing changes, and the index keeps the −1 the hit test gave it.
	/// </summary>
	public void OnMouseUp(Es1WidgetGlobals globals, int x, int y) {
		short pressed = globals.PressedIndex;
		globals.PressedIndex = HitTestChildren(x - Rect.X0, y - Rect.Y0);
		if (globals.PressedIndex == -1) {
			return;
		}

		var widget = _clickables[globals.PressedIndex];
		if (globals.PressedIndex == pressed || (globals.ClickButtons & 2) != 0) {
			widget.OnClick(widget.GetValue(globals));
		}

		widget.State = Es1Widget.Up;
		Repaint(widget);
		globals.PressedIndex = -1;
	}

	/// <summary>
	/// <c>Widget_TrackPressedWidget</c> (<c>00043f68</c>)(root, point): while a widget is pressed, the point hit-tested
	/// as given, not rebased; over the pressed widget an up one goes down, anywhere else a down one comes up, repainted
	/// either way.
	/// </summary>
	public void TrackPressedWidget(Es1WidgetGlobals globals, int x, int y) {
		if (globals.PressedIndex == -1) {
			return;
		}

		var widget = _clickables[globals.PressedIndex];
		if (HitTestChildren(x, y) == globals.PressedIndex) {
			if (widget.State == Es1Widget.Up) {
				widget.State = Es1Widget.Pressed;
				Repaint(widget);
			}
		} else if (widget.State == Es1Widget.Pressed) {
			widget.State = Es1Widget.Up;
			Repaint(widget);
		}
	}

	/// <summary><c>Widget_Repaint</c> (<c>0004410c</c>)(root, widget): the widget's invalidate slot.</summary>
	public static void Repaint(Es1Widget widget) => widget.Invalidate();

	/// <summary>
	/// <c>Widget_TranslateRootForView</c> (<c>00043d9c</c>)(root, delta): the delta added to the viewport's rect and to
	/// the root's rect, so a press is tested from the moved origin. The display's attributes 0 and 1 it reads are
	/// dropped.
	/// </summary>
	public void TranslateForView(int dx, int dy) {
		ViewportRect = ViewportRect.Offset(dx, dy);
		Rect = Rect.Offset(dx, dy);
	}
}
