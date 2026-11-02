using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Map;

/// <summary>
/// A <c>MapGadget</c>, the invisible square over a squad member's or a base's icon that a click picks
/// (docs/es1/shell/map.md#picking-a-gadget): a widget on <c>MapGadgetVtable</c> (<c>0007e3f8</c>), whose slots draw nothing,
/// with the map <c>+0x20</c>, the world position <c>+0x24</c>, the size <c>+0x30</c>, the text <c>+0x32</c> and the width
/// <c>+0x36</c>. Its rect <c>+0</c> is relative to the map's viewport (<see cref="Place"/>).
/// </summary>
public sealed class Es1MapGadget : Es1Widget {
	/// <summary>
	/// <c>MapGadget_Ctor</c> (<c>0000da8d</c>)(0, <paramref name="text"/>, <paramref name="size"/>, <paramref name="map"/>,
	/// <paramref name="position"/>) (docs/es1/shell/map.md#the-gadgets): <c>Widget_CtorRect</c> (<c>00011011</c>) on the
	/// rect <c>0009a524</c>, which the file's static initialiser <c>0000dcbd</c> zeroes, and the drag flag <c>+0x19</c> 0;
	/// the fields; the width the text's in <see cref="Es1ShellMapResources.LabelFont"/> plus twice
	/// <paramref name="textMargin"/>, in 16 bits; then <see cref="Place"/>. Retail measures with no check of the font or the
	/// text; here a missing one measures 0, an engine guard.
	/// </summary>
	public Es1MapGadget(string? text, short size, Es1ShellMap map, Es1MapPoint position, short textMargin) {
		Map = map;
		Position = position;
		Size = size;
		Text = text;
		Width = unchecked((short)((short)(textMargin << 1) + (text != null ? map.LabelFont?.Measure(text) ?? 0 : 0)));
		Place();
	}

	/// <summary><c>+0x20</c>.</summary>
	public Es1ShellMap Map { get; }

	/// <summary><c>+0x24</c>–<c>+0x2c</c>.</summary>
	public Es1MapPoint Position { get; }

	/// <summary><c>+0x30</c>, the square's side in pixels.</summary>
	public short Size { get; }

	/// <summary><c>+0x32</c>, a line of <c>str\maplabel</c>, which the callout shows.</summary>
	public string? Text { get; }

	/// <summary><c>+0x36</c>, the callout's width.</summary>
	public short Width { get; }

	/// <summary>
	/// <c>MapGadget_Place</c> (<c>0000dbfb</c>)(gadget) (docs/es1/shell/map.md#picking-a-gadget): with <i>p</i> the
	/// position's <see cref="Es1ShellMap.Project"/> through the camera last installed, the rect from <i>p</i> less
	/// <see cref="Size"/> <c>&gt;&gt;</c> 1 to that plus the size, less the installed render context's rect's left and top.
	/// Its callers install the map's viewport first, so that is <see cref="Es1ShellMap.Rect"/>.
	/// </summary>
	public void Place() {
		var (x, y) = Map.Project(Position);
		int half = Size >> 1;
		int left = unchecked(x - half - Map.Rect.X0), top = unchecked(y - half - Map.Rect.Y0);
		Rect = unchecked(new Es1Rect(left, top, left + Size, top + Size));
	}

	/// <summary>Slot <c>+8</c>, <c>CTLControl_InvalidateNoOpShellMapCopy</c> (<c>0000db7d</c>).</summary>
	public override void Invalidate() {
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>MapGadget_OnClick</c> (<c>0000dbd3</c>), the value not read. PLACEHOLDER: its
	/// <c>ShellMap_SelectGadget</c> (<c>0000d3d2</c>)(the map, gadget), S5's P19, with the mouse queue that reaches it.
	/// </summary>
	public override void OnClick(short value) {
	}

	/// <summary>Slot <c>+0x14</c>, <c>Widget_GetButtonValueShellMapCopy</c> (<c>0000db9e</c>): <c>Widget_ClickButtons</c> (<c>0007e66c</c>).</summary>
	public override short GetValue(Es1WidgetGlobals globals) => (short)globals.ClickButtons;

	/// <summary>Slot <c>+0x1c</c>, <c>CTLControl_DragMoveNoOpShellMapCopy</c> (<c>0000dbbc</c>).</summary>
	public override void DragTo(int x, int y) {
	}
}
