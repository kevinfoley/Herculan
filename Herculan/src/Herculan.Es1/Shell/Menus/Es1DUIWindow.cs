using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>A hotspot's handler, called with the window's render context, the hotspot's rect <c>+2</c> and its id <c>+0x16</c>.</summary>
public delegate void Es1HotspotHandler(Es1ShellRenderContext context, Es1Rect rect, short id);

/// <summary>
/// One of a window's clickable areas, a <c>0x38</c>-byte entry <c>WindowHotspot_Ctor</c> (<c>0001ad4e</c>) builds
/// (docs/es1/shell/menus.md#the-window), filled by <see cref="Es1DUIWindow.AddHotspot"/>.
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
/// (docs/es1/shell/menus.md#the-window): its rect on the screen, its render context, up to 64 controls, the keys they
/// answer to, and up to 15 hotspots. The plain window draws no background and keeps no labels;
/// <see cref="Es1MTWindow"/> is the menu window.
/// </summary>
public class Es1DUIWindow {
	/// <summary>The hotspots a window keeps, <c>DUIWindow_AddHotspot</c>'s bound.</summary>
	public const int MaxHotspots = 15;

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
	/// <c>DUIWindow_Draw</c> (<c>0001bfa3</c>) (docs/es1/shell/menus.md#drawing-a-window--duiwindow_draw-0001bfa3): in
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
	/// <c>DUIWindow_Update</c> (<c>0001c067</c>) (docs/es1/shell/menus.md#updating-a-window--duiwindow_update-0001c067):
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
	/// <c>DUIWindow_SetActive</c> (<c>0001c536</c>)(window) (docs/es1/shell/menus.md#the-active-window): the window made
	/// <see cref="Es1MenuGlobals.Active"/>, <c>DUIWindow_Activate</c> (<c>0001b0ff</c>), and
	/// <see cref="Es1MenuGlobals.KeysEnabled"/> set.
	/// </summary>
	public void SetActive(Es1MenuGlobals globals) {
		globals.Active = this;
		// PLACEHOLDER: DUIWindow_Activate (0001b0ff): the enabled control 103 made pushed and the holding control, the window's
		// key subscriber registered unless -X, and DUIWindow_FocusIndex set to the control under the pointer; P2b's input.
		globals.KeysEnabled = true;
	}

	/// <summary>
	/// <c>DUIWindow_ClearActive</c> (<c>0001c57c</c>): <c>DUIWindow_Deactivate</c> (<c>0001b25b</c>), which sets
	/// <see cref="Es1MenuGlobals.HeldIndex"/> to −1 and clears <see cref="Es1MenuGlobals.Holding"/>, then
	/// <see cref="Es1MenuGlobals.Active"/> and <see cref="Es1MenuGlobals.KeysEnabled"/> cleared.
	/// </summary>
	public static void ClearActive(Es1MenuGlobals globals) {
		globals.HeldIndex = -1;
		globals.Holding = false;
		// PLACEHOLDER: DUIWindow_Deactivate's 00027bbf and Keyboard_RemoveSubscriber(DUIWindow_KeySubscriber); the key
		// subscriber is P2b's input and is never registered until it lands.
		globals.Active = null;
		globals.KeysEnabled = false;
	}
}
