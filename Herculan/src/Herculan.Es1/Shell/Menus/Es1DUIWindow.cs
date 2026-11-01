using Herculan.Es1.Input;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>A hotspot's handler, called with the window's render context, the hotspot's rect <c>+2</c> and its id <c>+0x16</c>.</summary>
public delegate void Es1HotspotHandler(Es1ShellRenderContext context, Es1Rect rect, short id);

/// <summary>
/// One of a window's clickable areas, a <c>0x38</c>-byte entry <c>WindowHotspot_Ctor</c> (<c>0001ad4e</c>) builds
/// (docs/es1/shell/menu-controls.md#the-window), filled by <see cref="Es1DUIWindow.AddHotspot"/>.
/// </summary>
public sealed class Es1WindowHotspot {
	/// <summary><c>+0</c>: the area was pressed, or the pointer met it, and is to be drawn by <see cref="Es1DUIWindow.UpdateHotspots"/>.</summary>
	public bool Pressed { get; set; }

	/// <summary><c>+1</c>, 1 once filled.</summary>
	public bool Used { get; init; }

	/// <summary><c>+2</c>–<c>+0xe</c>, relative to the window.</summary>
	public Es1Rect Rect { get; init; }

	/// <summary><c>+0x12</c>: a rect <c>DUIWindow_HitHotspot</c> refuses a point in, none for most; the <c>a</c> argument of <see cref="Es1DUIWindow.AddHotspot"/>.</summary>
	public Es1Rect? Excluded { get; init; }

	/// <summary><c>+0x16</c>.</summary>
	public short Id { get; init; }

	/// <summary><c>+0x18</c>, the handler that draws the area.</summary>
	public Es1HotspotHandler? Handler { get; init; }

	/// <summary><c>+0x24</c>–<c>+0x30</c>, the rect again.</summary>
	public Es1Rect RectCopy { get; init; }
}

/// <summary>
/// The shell's window, class <c>DUIWindow</c> (vtable <c>0007eff0</c>, <c>0x4ed</c> bytes)
/// (docs/es1/shell/menu-controls.md#the-window): its rect on the screen, its render context, up to 64 controls, the keys they
/// answer to, and up to 15 hotspots. The plain window draws no background and keeps no labels;
/// <see cref="Es1MTWindow"/> is the menu window.
/// </summary>
public class Es1DUIWindow {
	/// <summary>The hotspots a window keeps, <c>DUIWindow_AddHotspot</c>'s bound.</summary>
	public const int MaxHotspots = 15;

	/// <summary>The id of the control <see cref="Activate"/> makes the holding control, 103.</summary>
	private const short HoldingControlId = 103;

	/// <summary>
	/// <c>DUIWindow_Ctor</c> (<c>0001abcf</c>)(window, <paramref name="rect"/>): the 15 hotspots, the render context
	/// over the rect with the origin (0, 0) and clip mode 1, the rect, no controls, the word <c>+0x1d</c> <c>0xffff</c>
	/// and the key list zeroed.
	/// </summary>
	public Es1DUIWindow(Es1VideoMemory display, Es1Rect rect) {
		Rect = rect;
		RenderContext = new Es1ShellRenderContext(display, rect);
	}

	/// <summary><c>+0</c>, <c>int32</c>s on the screen.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x10</c>, 0 from the constructor: the window is hit-tested by its rect.</summary>
	public byte HitShape { get; }

	/// <summary><c>+0x17</c>, the render context.</summary>
	public Es1ShellRenderContext RenderContext { get; }

	/// <summary><c>+0x1d</c>, <c>0xffff</c> from the constructor.</summary>
	public short Word1D { get; } = -1;

	/// <summary><c>+0x1f</c>, the controls in the order they were added, counted at <c>+0x1b</c>.</summary>
	public List<Es1DUIControl> Controls { get; } = [];

	/// <summary><c>+0x11f</c>, the keys the window's key subscriber is registered for: scan codes and their release codes.</summary>
	public List<byte> Keys { get; } = [];

	/// <summary><c>+0x19f</c>, the hotspots in the order they were added, counted at <c>+0x4e7</c>.</summary>
	public List<Es1WindowHotspot> Hotspots { get; } = [];

	/// <summary>Whether the screen point lies in the window, by its rect while <see cref="HitShape"/> is 0, as the mouse's callbacks test it.</summary>
	public bool Holds(int x, int y) => Rect.Contains(x, y);

	/// <summary>Slot <c>+4</c>, a label of a kind-1, 4 or 5 record: <c>DUIWindow_AddLabel</c> (<c>0001ae9b</c>), nothing.</summary>
	public virtual void AddLabel(int mode, int width, int x, int y, string text) {
	}

	/// <summary>Slot <c>+8</c>, the window's own drawing: <c>DUIWindow_DrawBackground</c> (<c>0001aeb7</c>), nothing.</summary>
	public virtual void DrawBackground(Es1ShellRenderContext context) {
	}

	/// <summary>
	/// <c>DUIWindow_AddControl</c> (<c>0001b207</c>)(window, <paramref name="control"/>): the control appended, and a
	/// non-zero key passed to <see cref="AddKey"/> and again with bit 7 set, its release. Retail does not bound the 64
	/// slots; the list here has none to overrun.
	/// </summary>
	public void AddControl(Es1DUIControl control) {
		Controls.Add(control);
		if (control.Key != 0) {
			AddKey(control.Key);
			AddKey((byte)(control.Key | 0x80));
		}
	}

	/// <summary><c>DUIWindow_AddKey</c> (<c>0001b296</c>)(window, <paramref name="key"/>): the key appended unless the list holds it.</summary>
	public void AddKey(byte key) {
		if (!Keys.Contains(key)) {
			Keys.Add(key);
		}
	}

	/// <summary>
	/// <c>DUIWindow_AddHotspot</c> (<c>0001b2de</c>)(window, <paramref name="excluded"/>, <paramref name="id"/>,
	/// <paramref name="handler"/>, <paramref name="rect"/>): under 15 hotspots, one filled: used, the rect twice, the
	/// excluded rect, the id and the handler.
	/// </summary>
	public void AddHotspot(Es1Rect? excluded, short id, Es1HotspotHandler handler, Es1Rect rect) {
		if (Hotspots.Count < MaxHotspots) {
			Hotspots.Add(new Es1WindowHotspot { Used = true, Rect = rect, RectCopy = rect, Excluded = excluded, Id = id, Handler = handler });
		}
	}

	/// <summary>
	/// <c>DUIWindow_Draw</c> (<c>0001bfa3</c>) (docs/es1/shell/menu-controls.md#drawing-a-window--duiwindow_draw-0001bfa3): in
	/// the window's render context, the translation cleared for the window's slot <c>+8</c> and set again; each control's
	/// slot <c>+8</c>; each hotspot's handler with its rect and id. The draws go to the display's draw page.
	/// </summary>
	public void Draw() {
		RenderContext.Translate = false;
		DrawBackground(RenderContext);
		RenderContext.Translate = true;
		foreach (var control in Controls) {
			control.Draw(RenderContext);
		}

		foreach (var hotspot in Hotspots) {
			hotspot.Handler?.Invoke(RenderContext, hotspot.Rect, hotspot.Id);
		}
	}

	/// <summary>
	/// <c>DUIWindow_Update</c> (<c>0001c067</c>) (docs/es1/shell/menu-controls.md#updating-a-window--duiwindow_update-0001c067):
	/// the translation set and <see cref="Es1MenuGlobals.InputReady"/> cleared; each control whose
	/// <see cref="Es1DUIControl.Changed"/> is set or that holds the input (tracking 2) drawn on page 2 and then on the draw
	/// page the display held, and its slot <c>+0x48</c> rect, moved by the window's corner, added as dirty with delay 0;
	/// <see cref="UpdateHotspots"/>; <see cref="Es1MenuGlobals.InputReady"/> set.
	/// </summary>
	public void Update(Es1MenuGlobals globals, Es1DirtyList dirty) {
		var display = RenderContext.Display;
		int keptDraw = display.DrawPage;
		RenderContext.Translate = true;
		globals.InputReady = false;
		foreach (var control in Controls) {
			if (!control.Changed && control.Tracking != 2) {
				continue;
			}

			display.DrawPage = Es1ShellBackdrop.BackgroundPage;
			control.Draw(RenderContext);
			display.DrawPage = keptDraw;
			control.Draw(RenderContext);
			var rect = control.GetRect().Offset(Rect.X0, Rect.Y0);
			dirty.AddRect(rect.X0, rect.Y0, rect.X1, rect.Y1, 0);
		}

		UpdateHotspots(dirty);
		globals.InputReady = true;
	}

	/// <summary>
	/// <c>DUIWindow_UpdateHotspots</c> (<c>0001c270</c>): each hotspot whose <see cref="Es1WindowHotspot.Pressed"/> is set
	/// has its handler called on page 2 and then on the draw page the display held, its rect, moved by the window's
	/// corner, added as dirty with delay 0, and the byte cleared.
	/// </summary>
	public void UpdateHotspots(Es1DirtyList dirty) {
		var display = RenderContext.Display;
		int keptDraw = display.DrawPage;
		foreach (var hotspot in Hotspots) {
			if (!hotspot.Pressed) {
				continue;
			}

			display.DrawPage = Es1ShellBackdrop.BackgroundPage;
			hotspot.Handler?.Invoke(RenderContext, hotspot.Rect, hotspot.Id);
			display.DrawPage = keptDraw;
			hotspot.Handler?.Invoke(RenderContext, hotspot.Rect, hotspot.Id);
			var rect = hotspot.Rect.Offset(Rect.X0, Rect.Y0);
			dirty.AddRect(rect.X0, rect.Y0, rect.X1, rect.Y1, 0);
			hotspot.Pressed = false;
		}
	}

	/// <summary>
	/// <c>DUIWindow_PointAtControl</c> (<c>0001c3ec</c>)(window, <paramref name="index"/>): the pointer
	/// (<see cref="Es1MenuGlobals.Pointer"/>) put on the control's center on the screen, (<c>int16</c>) (right − left) / 2
	/// + left and likewise for y, truncating, plus the window's corner.
	/// </summary>
	public void PointAtControl(Es1MenuGlobals globals, short index) {
		var rect = Controls[index].Rect;
		globals.Pointer = ((short)(rect.X1 - rect.X0) / 2 + Rect.X0 + rect.X0, (short)(rect.Y1 - rect.Y0) / 2 + Rect.Y0 + rect.Y0);
	}

	/// <summary>
	/// <c>DUIWindow_HitControl</c> (<c>0001bc11</c>)(window, <paramref name="x"/>, <paramref name="y"/>): the first
	/// control in the order added that is not inactive and holds the point, relative to the window, by its hit shape; −1
	/// for none.
	/// </summary>
	public short HitControl(int x, int y) {
		for (short i = 0; i < Controls.Count; i++) {
			if (Controls[i].State != Es1DUIControl.Inactive && Controls[i].Holds(x, y)) {
				return i;
			}
		}

		return -1;
	}

	/// <summary><c>DUIWindow_FindControlByKey</c> (<c>0001bd41</c>)(window, <paramref name="key"/>): the first control not inactive whose key is the key; −1 for none.</summary>
	public short FindControlByKey(byte key) {
		for (short i = 0; i < Controls.Count; i++) {
			if (Controls[i].State != Es1DUIControl.Inactive && Controls[i].Key == key) {
				return i;
			}
		}

		return -1;
	}

	/// <summary><c>DUIWindow_FindEnabledControlById</c> (<c>0001bda4</c>)(window, <paramref name="id"/>): the first control not inactive with the id; −1 for none.</summary>
	public short FindEnabledControlById(short id) {
		for (short i = 0; i < Controls.Count; i++) {
			if (Controls[i].State != Es1DUIControl.Inactive && Controls[i].Id == id) {
				return i;
			}
		}

		return -1;
	}

	/// <summary><c>DUIWindow_FindControlById</c> (<c>0001be16</c>)(window, <paramref name="id"/>): the first control with the id, whatever its state; −1 for none.</summary>
	public short FindControlById(short id) {
		for (short i = 0; i < Controls.Count; i++) {
			if (Controls[i].Id == id) {
				return i;
			}
		}

		return -1;
	}

	/// <summary><c>DUIWindow_FindHotspotById</c> (<c>0001be6f</c>)(window, <paramref name="id"/>): the first hotspot with the id; −1 for none.</summary>
	public short FindHotspotById(short id) {
		for (short i = 0; i < Hotspots.Count; i++) {
			if (Hotspots[i].Id == id) {
				return i;
			}
		}

		return -1;
	}

	/// <summary>
	/// <c>DUIWindow_HitHotspot</c> (<c>0001beca</c>)(window, <paramref name="x"/>, <paramref name="y"/>): the first
	/// hotspot whose rect holds the point, relative to the window, unless its excluded rect holds it too; −1 for none.
	/// </summary>
	public short HitHotspot(int x, int y) {
		for (short i = 0; i < Hotspots.Count; i++) {
			var hotspot = Hotspots[i];
			if (hotspot.Rect.Contains(x, y) && (hotspot.Excluded is not { } excluded || !excluded.Contains(x, y))) {
				return i;
			}
		}

		return -1;
	}

	/// <summary>
	/// <c>DUIWindow_Dtor</c> (<c>0001afa1</c>)(window, flags): each control destroyed through its slot <c>+4</c>, each
	/// hotspot's <c>+0x34</c> freed, the render context and the hotspots destroyed, the window freed when bit 0 of the
	/// flags is set. Only the controls' slots run here; the rest holds nothing the collector does not free.
	/// </summary>
	public void Dtor() {
		foreach (var control in Controls) {
			control.Dtor();
		}
	}

	/// <summary>
	/// <c>DUIWindow_SetActive</c> (<c>0001c536</c>)(window) (docs/es1/shell/menu-input.md#the-active-window): the window made
	/// <see cref="Es1MenuGlobals.Active"/>, <see cref="Activate"/>, and <see cref="Es1MenuGlobals.KeysEnabled"/> set.
	/// </summary>
	public void SetActive(Es1MenuGlobals globals) {
		globals.Active = this;
		Activate(globals);
		globals.KeysEnabled = true;
	}

	/// <summary>
	/// <c>DUIWindow_Activate</c> (<c>0001b0ff</c>)(window): <see cref="Es1MenuGlobals.PressedIndex"/> set to the first
	/// enabled control with id 103, −1 for none; one found is made pushed and changed, the holding control, and has its slot
	/// <c>+0xc</c> called; unless <c>-X</c>, the window's key subscriber registered for its key list; and
	/// <see cref="Es1MenuGlobals.FocusIndex"/> set to the control under the pointer, relative to the window.
	/// </summary>
	private void Activate(Es1MenuGlobals globals) {
		globals.PressedIndex = FindEnabledControlById(HoldingControlId);
		if (globals.PressedIndex != -1) {
			var control = Controls[globals.PressedIndex];
			control.State = Es1DUIControl.Pushed;
			control.Changed = true;
			globals.HeldIndex = globals.PressedIndex;
			globals.Holding = true;
			control.Invalidate();
		}

		if (!globals.DebugMode) {
			// 00027bbf, the keyboard device's getter, whose answer is dropped.
			globals.Keyboard?.AddSubscriber(globals.KeySubscriber, Keys.ToArray());
		}

		globals.FocusIndex = HitControl(globals.Pointer.X - Rect.X0, globals.Pointer.Y - Rect.Y0);
	}

	/// <summary>
	/// <c>DUIWindow_ClearActive</c> (<c>0001c57c</c>): <c>DUIWindow_Deactivate</c> (<c>0001b25b</c>), which sets
	/// <see cref="Es1MenuGlobals.HeldIndex"/> to −1, clears <see cref="Es1MenuGlobals.Holding"/> and removes the window's
	/// key subscriber, whatever <c>-X</c> is; then <see cref="Es1MenuGlobals.Active"/> and
	/// <see cref="Es1MenuGlobals.KeysEnabled"/> cleared.
	/// </summary>
	public static void ClearActive(Es1MenuGlobals globals) {
		globals.HeldIndex = -1;
		globals.Holding = false;
		// 00027bbf, the keyboard device's getter, whose answer is dropped.
		globals.Keyboard?.RemoveSubscriber(globals.KeySubscriber);
		globals.Active = null;
		globals.KeysEnabled = false;
	}

	/// <summary>
	/// Whether <paramref name="index"/> names one of the controls. Retail indexes the array whatever the index is, reading
	/// past it for −1 (<c>DUIWindow_Tracking</c> outlives the pressed index that <c>Tab</c>, a key and a new window clear);
	/// the input's engine guards skip such an index.
	/// </summary>
	public bool HasControl(short index) => index >= 0 && index < Controls.Count;

	/// <summary>
	/// The holding control let go of, as <c>DUIWindow_OnPress</c> and <c>DUIWindow_PressControl</c> do it while
	/// <see cref="Es1MenuGlobals.Holding"/> is set: up and changed, with no slot called, <see cref="Es1MenuGlobals.Holding"/>
	/// cleared and <see cref="Es1MenuGlobals.HeldIndex"/> −1.
	/// </summary>
	private void LetGoOfHeld(Es1MenuGlobals globals) {
		if (!globals.Holding) {
			return;
		}

		if (HasControl(globals.HeldIndex)) {
			Controls[globals.HeldIndex].State = Es1DUIControl.Up;
			Controls[globals.HeldIndex].Changed = true;
		}

		globals.Holding = false;
		globals.HeldIndex = -1;
	}

	/// <summary>A control's message posted to <see cref="Es1MenuGlobals.PendingMessage"/>: (0, the index, its slot <c>+0x2c</c>, its id).</summary>
	private void Post(Es1MenuGlobals globals, short index) {
		var control = Controls[index];
		globals.PendingMessage = new Es1MenuMessage(0, index, control.GetValue(), control.Id);
	}

	/// <summary>
	/// <c>DUIWindow_OnPress</c> (<c>0001b436</c>)(window, <paramref name="x"/>, <paramref name="y"/>)
	/// (docs/es1/shell/menu-input.md#the-mouse), the point on the screen: the holding control let go of;
	/// <see cref="Es1MenuGlobals.PressedIndex"/> the control under the point; one found made pushed and changed, given the
	/// point relative to the window through its slot <c>+0x38</c> and <see cref="Es1MenuGlobals.Tracking"/> set when it
	/// follows the pointer (tracking 1), or given the point and made the holding control when it holds the input (2), and
	/// its slot <c>+0xc</c> called; the hotspot under the point marked pressed.
	/// </summary>
	public void OnPress(Es1MenuGlobals globals, int x, int y) {
		x -= Rect.X0;
		y -= Rect.Y0;
		LetGoOfHeld(globals);
		globals.PressedIndex = HitControl(x, y);
		if (globals.PressedIndex != -1) {
			var control = Controls[globals.PressedIndex];
			control.State = Es1DUIControl.Pushed;
			control.Changed = true;
			if (control.Tracking == 1) {
				control.SetTrackPoint(x, y);
				globals.Tracking = true;
			} else if (control.Tracking == 2) {
				control.SetTrackPoint(x, y);
				globals.HeldIndex = globals.PressedIndex;
				globals.Holding = true;
			}

			control.Invalidate();
		}

		MarkHotspot(x, y);
	}

	/// <summary>
	/// <c>DUIWindow_OnRelease</c> (<c>0001b683</c>)(window, <paramref name="x"/>, <paramref name="y"/>), the point on the
	/// screen: <see cref="Es1MenuGlobals.PressedIndex"/> becomes the control under the point; one found posts its message
	/// when it is the control pressed before, is put up and changed with its slot <c>+0xc</c> unless it holds the input, and
	/// <see cref="Es1MenuGlobals.PressedIndex"/> becomes −1; the hotspot under the point marked pressed.
	/// </summary>
	public void OnRelease(Es1MenuGlobals globals, int x, int y) {
		x -= Rect.X0;
		y -= Rect.Y0;
		short pressed = globals.PressedIndex;
		globals.PressedIndex = HitControl(x, y);
		if (globals.PressedIndex != -1) {
			if (globals.PressedIndex == pressed) {
				Post(globals, globals.PressedIndex);
			}

			var control = Controls[globals.PressedIndex];
			if (control.Tracking != 2) {
				control.State = Es1DUIControl.Up;
				control.Changed = true;
				control.Invalidate();
			}

			globals.PressedIndex = -1;
		}

		MarkHotspot(x, y);
	}

	/// <summary>
	/// <c>DUIWindow_OnDrag</c> (<c>0001b8fc</c>)(window, <paramref name="x"/>, <paramref name="y"/>), the point on the
	/// screen: <see cref="Es1MenuGlobals.FocusIndex"/> becomes the control under the point; with none pressed nothing more.
	/// The pressed control, when it is the one under the point or holds the input, is made pushed from up, and otherwise up
	/// from pushed, each change changed and followed by its slot <c>+0xc</c>; the hotspot under the point marked pressed.
	/// A pressed index past the controls is taken as none, an engine guard.
	/// </summary>
	public void OnDrag(Es1MenuGlobals globals, int x, int y) {
		x -= Rect.X0;
		y -= Rect.Y0;
		globals.FocusIndex = HitControl(x, y);
		if (globals.PressedIndex == -1 || !HasControl(globals.PressedIndex)) {
			return;
		}

		var control = Controls[globals.PressedIndex];
		if (globals.FocusIndex == globals.PressedIndex || control.Tracking == 2) {
			if (control.State == Es1DUIControl.Up) {
				control.State = Es1DUIControl.Pushed;
				control.Changed = true;
				control.Invalidate();
			}
		} else if (control.State == Es1DUIControl.Pushed) {
			control.State = Es1DUIControl.Up;
			control.Changed = true;
			control.Invalidate();
		}

		MarkHotspot(x, y);
	}

	/// <summary>The hotspot <see cref="HitHotspot"/> finds at the point, relative to the window, marked pressed.</summary>
	private void MarkHotspot(int x, int y) {
		short hotspot = HitHotspot(x, y);
		if (hotspot != -1) {
			Hotspots[hotspot].Pressed = true;
		}
	}

	/// <summary>
	/// <c>DUIWindow_PressControl</c> (<c>0001b5bb</c>)(window, <paramref name="index"/>) (docs/es1/shell/menu-input.md#the-keys):
	/// for an index of 0 or more whose control is not inactive, the holding control let go of, the control made
	/// <see cref="Es1MenuGlobals.PressedIndex"/>, pushed and changed, and its slot <c>+0xc</c> called. An index past the
	/// controls does nothing, an engine guard.
	/// </summary>
	public void PressControl(Es1MenuGlobals globals, short index) {
		if (!HasControl(index) || Controls[index].State == Es1DUIControl.Inactive) {
			return;
		}

		LetGoOfHeld(globals);
		globals.PressedIndex = index;
		var control = Controls[index];
		control.State = Es1DUIControl.Pushed;
		control.Changed = true;
		control.Invalidate();
	}

	/// <summary>
	/// <c>DUIWindow_ReleaseControl</c> (<c>0001b7d5</c>)(window, <paramref name="index"/>): for an index of 0 or more whose
	/// control is not inactive, the control's message posted when it was <see cref="Es1MenuGlobals.PressedIndex"/>, the
	/// control put up and changed with its slot <c>+0xc</c> unless it holds the input, and
	/// <see cref="Es1MenuGlobals.PressedIndex"/> −1. An index past the controls does nothing, an engine guard.
	/// </summary>
	public void ReleaseControl(Es1MenuGlobals globals, short index) {
		if (!HasControl(index) || Controls[index].State == Es1DUIControl.Inactive) {
			return;
		}

		short pressed = globals.PressedIndex;
		globals.PressedIndex = index;
		if (index == pressed) {
			Post(globals, index);
		}

		var control = Controls[index];
		if (control.Tracking != 2) {
			control.State = Es1DUIControl.Up;
			control.Changed = true;
			control.Invalidate();
		}

		globals.PressedIndex = -1;
	}

	/// <summary>
	/// <c>DUIWindow_SendChar</c> (<c>0001bb85</c>)(window, <paramref name="word"/>): while a control holds the input, a
	/// word with <c>0x4000</c> and without <c>0x8000</c>, a typed character, goes to its slot <c>+0x3c</c> as its low byte.
	/// </summary>
	public void SendChar(Es1MenuGlobals globals, ushort word) {
		if (globals.Holding && (word & 0x4000) != 0 && (word & 0x8000) == 0 && HasControl(globals.HeldIndex)) {
			Controls[globals.HeldIndex].SetTextFromTable((short)(word & 0xff));
		}
	}

	/// <summary>
	/// <c>DUIWindow_SendKey</c> (<c>0001bbd2</c>)(window, <paramref name="word"/>): while a control holds the input, the
	/// word's low seven bits (<c>AND DI,0x7f</c>) go to its slot <c>+0x40</c>.
	/// </summary>
	public void SendKey(Es1MenuGlobals globals, ushort word) {
		if (globals.Holding && HasControl(globals.HeldIndex)) {
			Controls[globals.HeldIndex].SendKey((short)(word & 0x7f));
		}
	}

	/// <summary>
	/// <c>DUIWindow_KeySubscriber</c> (<c>0001ba73</c>)(<paramref name="code"/>) (docs/es1/shell/menu-input.md#the-keys), the
	/// keyboard subscriber <see cref="Activate"/> registers for the window's key list: 1 while
	/// <see cref="Es1MenuGlobals.KeysEnabled"/> is clear; then the repeat test and the release conversion, below; the
	/// code kept as <see cref="Es1MenuGlobals.LastKey"/>; 2 for a code with <c>Alt</c>, <c>Ctrl</c> or <c>Shift</c>
	/// (<c>0xe00</c>); and the scan code looked up among the active window's enabled controls' keys, straight into
	/// <see cref="Es1MenuGlobals.PressedIndex"/>. Found, the control takes the pointer and the focus and is pressed on a
	/// press code or released on a release code, and the answer is 1; otherwise 2.
	///
	/// <para>The repeat test is <c>(LastKey == 0xff) &amp; code</c> and the release conversion
	/// <c>(code == 0x7f) &amp; LastKey</c> (disassembly <c>0001ba8e</c>–<c>0001baf2</c>): the source's <c>==</c> binds
	/// before its <c>&amp;</c>, so the first answers 2 only after the code <c>0xff</c> and for an odd code, and the second
	/// makes the code the last one's release only for the code <c>0x7f</c> after an odd press. Ported as the binary has
	/// them; a held key's repeats press its control again, which shows nothing new.</para>
	/// </summary>
	public static int KeySubscriber(Es1MenuGlobals globals, ushort code) {
		if (!globals.KeysEnabled) {
			return Es1Keyboard.Claimed;
		}

		ushort last = (ushort)globals.LastKey;
		if (last == 0xff && (code & 1) != 0) {
			return Es1Keyboard.PassOn;
		}

		if (last != 0 && (last & 0x80) == 0 && code == 0x7f && (last & 1) != 0) {
			code = (ushort)(last | 0x80);
		}

		globals.LastKey = (short)code;
		if ((code & 0xe00) != 0) {
			return Es1Keyboard.PassOn;
		}

		// Retail reads the window DUIWindow_Active points at; with none the key goes on, an engine guard.
		if (globals.Active is not { } window) {
			return Es1Keyboard.PassOn;
		}

		globals.PressedIndex = window.FindControlByKey((byte)(code & 0x7f));
		if (globals.PressedIndex == -1) {
			return Es1Keyboard.PassOn;
		}

		window.PointAtControl(globals, globals.PressedIndex);
		globals.FocusIndex = globals.PressedIndex;
		if ((code & 0x80) != 0) {
			window.ReleaseControl(globals, globals.PressedIndex);
		} else {
			window.PressControl(globals, globals.PressedIndex);
		}

		return Es1Keyboard.Claimed;
	}

	/// <summary>
	/// <c>DUIWindow_HandleNavKey</c> (<c>0001cc86</c>)(<paramref name="code"/>) (docs/es1/shell/menu-input.md#the-keys), the
	/// dialog's key handler: nothing without an active window; otherwise by the seven codes of the table at
	/// <c>0007f0e0</c>. <c>Tab</c> puts the pressed control up and changed, with no slot called, and moves the focus to the
	/// next focusable control, wrapping by the remainder of the count; <c>Shift+Tab</c> and <c>Alt+Tab</c> move it to the
	/// previous, a focus below 1 taken as the count first; both then put the pointer on it. <c>Enter</c> presses the
	/// focused control and its release releases it.
	///
	/// <para><b>Engine guard:</b> retail divides by the control count and walks until a focusable control turns up, so a
	/// window with no controls faults and one with no focusable control hangs; here either leaves the focus as it is.</para>
	/// </summary>
	public static void HandleNavKey(Es1MenuGlobals globals, ushort code) {
		if (globals.Active is not { } window) {
			return;
		}

		switch (code) {
			case 0x0f:
				if (!window.Controls.Exists(c => c.Focusable)) {
					return;
				}

				do {
					if (globals.PressedIndex != -1) {
						if (window.HasControl(globals.PressedIndex)) {
							window.Controls[globals.PressedIndex].State = Es1DUIControl.Up;
							window.Controls[globals.PressedIndex].Changed = true;
						}

						globals.PressedIndex = -1;
					}

					globals.FocusIndex = (short)((globals.FocusIndex + 1) % window.Controls.Count);
				} while (!window.Controls[globals.FocusIndex].Focusable);

				window.PointAtControl(globals, globals.FocusIndex);
				break;
			case 0x80f:
			case 0x20f:
				if (!window.Controls.Exists(c => c.Focusable)) {
					return;
				}

				do {
					if (globals.FocusIndex < 1) {
						globals.FocusIndex = (short)window.Controls.Count;
					}

					globals.FocusIndex--;
				} while (!window.Controls[globals.FocusIndex].Focusable);

				window.PointAtControl(globals, globals.FocusIndex);
				break;
			case 0x1c:
			case 0x81c:
				window.PressControl(globals, globals.FocusIndex);
				break;
			case 0x9c:
			case 0x89c:
				window.ReleaseControl(globals, globals.FocusIndex);
				break;
		}
	}
}
