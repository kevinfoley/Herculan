namespace Herculan.Engine.Shell;

/// <summary>
/// One widget's box in shell-canvas pixels, with <b>both corners inclusive</b> — VSHELL's own
/// convention, and the reason the full-screen panel is <c>{0, 0, 0x27f, 0x1df}</c> rather than
/// <c>{0, 0, 640, 480}</c>. See docs/retail/shell/screen-layout.md.
/// </summary>
public readonly record struct ShellRect(int X0, int Y0, int X1, int Y1) {
	/// <summary>Width in pixels, inclusive of both edges.</summary>
	public int Width => X1 - X0 + 1;

	/// <summary>Height in pixels, inclusive of both edges.</summary>
	public int Height => Y1 - Y0 + 1;

	/// <summary>Whether a canvas point is inside the box — inclusive on all four edges, as the rect is.</summary>
	public bool Contains(float x, float y) => x >= X0 && y >= Y0 && x <= X1 && y <= Y1;
}

/// <summary>
/// Where the shell's fixed furniture sits, in the 640x480 canvas VSHELL authors every screen in.
///
/// <para><b>These are literals in the executable, not data.</b> Each tab screen's builder writes
/// every widget rect as four immediates onto its own stack and hands the block to a widget
/// constructor; no builder opens a file. The <c>gam\arm_*.dat</c> / <c>gam\rpr_*.dat</c> layout
/// records are the exception and cover only the arming and service-bay content panels, not the frame
/// around them. The derivation and the addresses are in docs/retail/shell/screen-layout.md.</para>
///
/// <para>The canvas is fixed at 640x480 and scaled to the window by
/// <see cref="ShellScreenLayout"/> — "fixed layout, scaled to fit" rather than a reflowing UI, which
/// is what keeps every one of these numbers usable exactly as the original states it.</para>
/// </summary>
public static class ShellLayout {
	/// <summary>Canvas width — the <c>0x27f</c> in every full-screen panel rect, plus one.</summary>
	public const int CanvasWidth = 640;

	/// <summary>Canvas height — the <c>0x1df</c> in the same rects, plus one.</summary>
	public const int CanvasHeight = 480;

	/// <summary>The whole canvas, as the full-screen panel every tab screen parents its widgets to.</summary>
	public static readonly ShellRect Screen = new(0, 0, CanvasWidth - 1, CanvasHeight - 1);

	/// <summary>
	/// The small square button left of the tab strip, <c>{7, 4, 0x17, 0x1b}</c>. It is the one button
	/// on the strip that does not caption itself and does not draw <see cref="ShellArt.ButtonBank"/>
	/// art: its two frames come from <c>dba\online.dba</c>.
	/// </summary>
	public static readonly ShellRect MenuButton = new(7, 4, 0x17, 0x1b);

	/// <summary>
	/// The palette scope: everything below the tab strip, <c>{0, 0x1e, 0x27f, 0x1df}</c>. It is a
	/// widget in its own right rather than a region — the builder gives it only a new top-left and
	/// leaves the full-screen panel's far corner on the stack — and showing it is what installs the
	/// screen's palette. See <see cref="ShellPalette"/>.
	/// </summary>
	public static readonly ShellRect PaletteScope = new(0, 0x1e, CanvasWidth - 1, CanvasHeight - 1);

	/// <summary>How many tabs the strip carries.</summary>
	public const int TabCount = 8;

	/// <summary>Left edge of tab 0 — the <c>0x19</c> in the first tab's rect.</summary>
	public const int FirstTabX = 0x19;

	/// <summary>A tab's width in pixels: <c>0x63 - 0x19 + 1</c>, the same for all eight.</summary>
	public const int TabWidth = 0x63 - FirstTabX + 1;

	/// <summary>
	/// Left edge to left edge. The tabs are butted up one pixel apart: tab 0 ends at <c>0x63</c> and
	/// tab 1 starts at <c>0x65</c>, and tab 7's <c>{0x22d, 0x277}</c> falls out of the same step.
	/// </summary>
	public const int TabPitch = TabWidth + 1;

	/// <summary>Top edge of the whole strip, buttons and tabs alike.</summary>
	public const int TabTop = 4;

	/// <summary>And its bottom edge, <c>0x1b</c>.</summary>
	public const int TabBottom = 0x1b;

	/// <summary>
	/// The first tab's caption index in <c>estext.bin</c>; the eight run consecutively from here. See
	/// <see cref="ShellText"/>.
	/// </summary>
	public const int FirstTabCaption = 0x13;

	/// <summary>Tab <paramref name="index"/>'s rect. Tab 0 is <c>{0x19, 4, 0x63, 0x1b}</c>.</summary>
	public static ShellRect Tab(int index) {
		int x0 = FirstTabX + index * TabPitch;
		return new ShellRect(x0, TabTop, x0 + TabWidth - 1, TabBottom);
	}
}
