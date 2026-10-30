using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The shell's control, class <c>DUIControl</c> (vtable <c>0007ef92</c>, and the button file's copy <c>0007f1ea</c>)
/// (docs/es1/shell/menus.md#the-controls): a rect relative to its window, a hit shape, an id, a state and the slots the
/// window code calls, as virtual members named after the slot's function. The base's slots do nothing, as
/// <c>DUIControl</c>'s do.
/// </summary>
public class Es1DUIControl {
	/// <summary>The states <see cref="State"/> takes: up, pushed, inactive.</summary>
	public const short Up = 0, Pushed = 1, Inactive = 2;

	/// <summary>
	/// <c>DUIControl_CtorWithKey</c> (<c>0001a7e5</c>)(control, <paramref name="focusable"/>, <paramref name="key"/>,
	/// <paramref name="rect"/>), and <c>DUIControl_Ctor</c> (<c>0001a716</c>) with the key 0 and the id: the rect set with
	/// <see cref="SetRect"/>, the hit-shape byte <c>+0x10</c> cleared, the id 0.
	/// </summary>
	public Es1DUIControl(bool focusable, byte key, Es1Rect rect) {
		SetRect(rect);
		Key = key;
		Focusable = focusable;
	}

	/// <summary><c>+0</c>–<c>+0xc</c>, <c>int32</c>s relative to the window.</summary>
	public Es1Rect Rect { get; private set; }

	/// <summary>
	/// <c>+0x10</c>: 0 hit-tests <see cref="Rect"/>; otherwise the pointer must lie within <see cref="Distance"/> of
	/// <see cref="Center"/>, measured as |dx| + |dy|.
	/// </summary>
	public byte HitShape { get; private set; }

	/// <summary><c>+0x11</c>, <c>+0x13</c>, the hit shape's point, <c>int16</c>s.</summary>
	public (short X, short Y) Center { get; private set; }

	/// <summary><c>+0x15</c>, the hit shape's distance, an <c>int16</c>.</summary>
	public short Distance { get; private set; }

	/// <summary><c>+0x17</c>, the id: the screen a press names.</summary>
	public short Id { get; set; }

	/// <summary><c>+0x19</c>, <see cref="Up"/>, <see cref="Pushed"/> or <see cref="Inactive"/>.</summary>
	public short State { get; set; }

	/// <summary><c>+0x1b</c>: how a press is tracked, 0 a button's, 1 the control follows the pointer while pressed, 2 it holds the input.</summary>
	public short Tracking { get; set; }

	/// <summary><c>+0x1d</c>, the key, a scan code, or 0.</summary>
	public byte Key { get; }

	/// <summary><c>+0x1e</c>: <c>Tab</c> stops at the control.</summary>
	public bool Focusable { get; }

	/// <summary><c>+0x1f</c>, <i>changed</i>: the control needs drawing.</summary>
	public bool Changed { get; set; }

	/// <summary><c>DUIControl_SetRect</c> (<c>0001a8b3</c>): the rect kept, <see cref="Changed"/> and <see cref="State"/> cleared.</summary>
	public void SetRect(Es1Rect rect) {
		Rect = rect;
		Changed = false;
		State = Up;
	}

	/// <summary>
	/// <c>0001a4f4</c>(control, <paramref name="mode"/>), <c>DUIControl</c>'s: <see cref="HitShape"/> set to the mode, and
	/// for mode 1 the distance (right − left) &gt;&gt; 1 and the point (left, top) moved by it, the diamond inscribed in
	/// the rect's top square.
	/// </summary>
	public void SetHitShape(byte mode) {
		HitShape = mode;
		if (mode == 1) {
			Distance = (short)((Rect.X1 - Rect.X0) >> 1);
			Center = ((short)(Rect.X0 + Distance), (short)(Rect.Y0 + Distance));
		}
	}

	/// <summary>Whether the point, relative to the window, lies on the control by its hit shape, as <c>DUIWindow_HitControl</c> tests it.</summary>
	public bool Holds(int x, int y) {
		if (HitShape == 0) {
			return Rect.Contains(x, y);
		}

		return Math.Abs(x - Center.X) + Math.Abs(y - Center.Y) <= Distance;
	}

	/// <summary>Slot <c>+4</c>, <c>DUIControl_Dtor</c> (<c>0001a61a</c>): the vtable stored back and the control freed, which holds nothing the collector does not free.</summary>
	public virtual void Dtor() {
	}

	/// <summary>Slot <c>+8</c>, <c>DUIControl_Draw</c> (<c>0001a647</c>): nothing.</summary>
	public virtual void Draw(Es1ShellRenderContext context) {
	}

	/// <summary>Slot <c>+0xc</c>, called after every state change: <c>DUIControl_Invalidate</c> (<c>0001a651</c>), nothing.</summary>
	public virtual void Invalidate() {
	}

	/// <summary>Slot <c>+0x10</c>: <c>0001a65b</c>, nothing.</summary>
	public virtual void Highlight() {
	}

	/// <summary>Slot <c>+0x18</c>: <c>0001a673</c>, nothing.</summary>
	public virtual void Unhighlight() {
	}

	/// <summary>Slot <c>+0x2c</c>, the value a release posts: <c>DUIControl_GetValue</c> (<c>0001a6ac</c>), 1.</summary>
	public virtual short GetValue() => 1;

	/// <summary>Slot <c>+0x38</c>, a press or a move of a tracking control with the point relative to the window: <c>0001a6d4</c>, nothing.</summary>
	public virtual void SetTrackPoint(int x, int y) {
	}

	/// <summary>
	/// Slot <c>+0x3c</c>: <c>DUIWindow_SendChar</c> calls it with a typed character, <c>Menu_Open</c> on a button with a
	/// line of <c>eng\watext.bin</c>. <c>0001a6e1</c>, nothing.
	/// </summary>
	public virtual void SetTextFromTable(short value) {
	}

	/// <summary>Slot <c>+0x40</c>, a key code to the holding control: <c>0001a6ef</c>, nothing.</summary>
	public virtual void SendKey(short code) {
	}

	/// <summary>Slot <c>+0x48</c>, the rect <c>DUIWindow_Update</c> adds as dirty: <c>BarGraph_GetRectWindowCopy</c> (<c>0001a70a</c>), <see cref="Rect"/>.</summary>
	public virtual Es1Rect GetRect() => Rect;
}

/// <summary>
/// <c>DUIButtonControl</c> (vtable <c>0007f130</c>), the class <see cref="Es1MTButton"/> derives from
/// (docs/es1/shell/menus.md#buttons): <c>MTButton_Ctor</c> stores its vtable and zeroes <see cref="Es1DUIControl.Tracking"/>
/// before storing its own. Its slots that differ from the base's are no-ops or <see cref="Es1MTButton"/>'s too.
/// </summary>
public class Es1DUIButtonControl : Es1DUIControl {
	public Es1DUIButtonControl(byte key, Es1Rect rect) : base(true, key, rect) {
		Tracking = 0;
	}

	/// <summary>Slot <c>+0x2c</c>, <c>DUIButtonControl_GetValue</c> (<c>0001d088</c>): 1.</summary>
	public override short GetValue() => 1;
}
