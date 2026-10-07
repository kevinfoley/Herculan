namespace Herculan.Engine.Cockpit;

/// <summary>
/// The key codes an alert panel's handler matches — the switch <c>AlertPanel_HandleEvent</c> (<c>00454e10</c>)
/// and <c>ControlsPanel_HandleEvent</c> (<c>00458f9c</c>) share over the input block's command word.
/// </summary>
public enum AlertPanelKey {
	/// <summary>Nothing the handler answers.</summary>
	None,

	/// <summary>[Return], <c>0x1c</c>.</summary>
	Return,

	/// <summary>[Esc], <c>0x01</c>.</summary>
	Escape,

	/// <summary>
	/// [Tab], <c>0x0f</c>, or <c>0x52</c>, keypad 0 and [Insert]. [Shift+Tab] arrives as <c>0x0f</c> too, so it is this
	/// and not <see cref="FocusPrevious"/> — docs/retail/simulation/alert-panels.md#keys-and-the-press-flash.
	/// </summary>
	FocusNext,

	/// <summary>
	/// The handler's <c>0x80f</c> case, <c>AlertPanel_FocusPrevious</c>. No key reaches it, for the reason
	/// <see cref="FocusNext"/> gives.
	/// </summary>
	FocusPrevious,
}

/// <summary>
/// An alert panel's focus — <c>panel+0x2f7</c>, the widget [Return] and the trigger press — and the pointer
/// that goes with it: <c>AlertPanel_SetFocus</c> (<c>00454c7c</c>) puts the pointer on the centre of every
/// widget it focuses. See docs/retail/simulation/alert-panels.md#keys-and-the-press-flash.
///
/// <para>Widgets are named by their index in the panel's widget list (<c>panel+0x291</c>); <see cref="Count"/>
/// is that list's length, <c>panel+0x2cd</c>.</para>
///
/// <para>The function's <c>-1</c> arm, which adopts the widget under the pointer, is not here. Of the callers
/// <c>es2_xref.py</c> finds, <c>AlertPanel_Enter</c> is the one that passes <c>-1</c>, and every member's loop
/// focuses a widget outright straight after it, so what it picks is overwritten before anything reads it.</para>
/// </summary>
public sealed class AlertPanelFocus {
	private readonly Func<int, AlertPanelLayout.Rect> _rect;
	private (int X, int Y)? _pointer;

	/// <param name="rect">Widget <c>i</c>'s rect, in panel-local device pixels.</param>
	/// <param name="count">How many widgets the panel lists.</param>
	public AlertPanelFocus(Func<int, AlertPanelLayout.Rect> rect, int count) {
		_rect = rect;
		Count = count;
	}

	/// <summary>How many widgets the panel lists, <c>panel+0x2cd</c>.</summary>
	public int Count { get; set; }

	/// <summary>The focused widget, or -1.</summary>
	public int Index { get; private set; } = -1;

	/// <summary>
	/// <c>AlertPanel_SetFocus</c>: focuses <paramref name="index"/> and asks for the pointer at the centre of its
	/// rect. A greyed widget takes the focus like any other; pressing it is what refuses it.
	/// </summary>
	public void Set(int index) {
		Index = index;
		var rect = _rect(index);
		_pointer = (rect.X0 + ((rect.X1 - rect.X0) >> 1), rect.Y0 + ((rect.Y1 - rect.Y0) >> 1));
	}

	/// <summary>A store to <c>panel+0x2f7</c> that does not go through <c>AlertPanel_SetFocus</c>, and so leaves the pointer where it is.</summary>
	public void Move(int index) => Index = index;

	/// <summary>Forgets the focus and any pointer move still waiting, for a panel coming down.</summary>
	public void Clear() {
		Index = -1;
		_pointer = null;
	}

	/// <summary>
	/// Where the last <see cref="Set"/> asked for the pointer, in panel-local device pixels — once: a second call
	/// returns false until the focus is set again.
	/// </summary>
	public bool TakePointer(out int x, out int y) {
		(x, y) = _pointer ?? (0, 0);
		bool pending = _pointer.HasValue;
		_pointer = null;
		return pending;
	}

	/// <summary>
	/// The handler's key switch. <paramref name="press"/> is <c>AlertPanel_PressWidget</c> (<c>00454dcc</c>) for the
	/// widget it is given, and <paramref name="cancel"/> the widget [Esc] presses, <c>panel+0x2fb</c>.
	/// </summary>
	/// <returns>Whether the key was one the handler answers.</returns>
	public bool AnswerKey(AlertPanelKey key, int cancel, Action<int> press) {
		switch (key) {
			case AlertPanelKey.Return:
				PressFocused(press);
				return true;
			case AlertPanelKey.Escape:
				if (cancel != -1) {
					press(cancel);
				}

				return true;
			case AlertPanelKey.FocusNext:
				Next();
				return true;
			case AlertPanelKey.FocusPrevious:
				Previous();
				return true;
			default:
				return false;
		}
	}

	/// <summary>
	/// <c>AlertPanel_HandleEvent</c>'s stick half, which runs ahead of its keys: the trigger presses the focused
	/// widget as [Return] does, and failing that joystick button 2 focuses the next one. The caller latches
	/// whichever was answered.
	/// </summary>
	/// <returns>Whether either was answered.</returns>
	public bool AnswerStick(bool trigger, bool button2, Action<int> press) {
		if (trigger) {
			PressFocused(press);
		} else if (button2) {
			Next();
		}

		return trigger || button2;
	}

	// [Return]'s and the trigger's case: the focused widget, or a first focus on widget 0 when there is none.
	private void PressFocused(Action<int> press) {
		if (Index == -1) {
			Set(0);
		} else {
			press(Index);
		}
	}

	// AlertPanel_FocusNext (00454d7c): wraps to 0 at the count, and from -1 lands on 0.
	private void Next() {
		int index = Index + 1;
		Set(index == Count ? 0 : index);
	}

	// AlertPanel_FocusPrevious (00454da0): wraps from 0, and from -1, to the last widget.
	private void Previous() {
		int index = Index == -1 ? -1 : Index - 1;
		Set(index < 0 ? Count - 1 : index);
	}
}
