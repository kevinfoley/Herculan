using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// The HUD throttle gauge, content offset 1016: the slider track rect (the inherited Origin/Size),
/// then <see cref="BarCorners"/>, the forward and reverse fill bars either side of the track's
/// centre. <c>ThrottleGauge_Ctor</c> (<c>00447b84</c>) reads the whole record from offset 1000.
///
/// <para>Neither bar is ever drawn, by DBSIM either: the slider keeps them only to widen the region it
/// invalidates. They are a cut feature. See docs/retail/formats/cockpit-hud-widgets.md, "Throttle
/// gauge".</para>
/// </summary>
public class HThrottle : WidgetBase {
	/// <summary>
	/// The two fill bars' corners — top-left then bottom-right of each — in the file's own order.
	/// Read them through <see cref="ForwardBar"/> and <see cref="ReverseBar"/>.
	/// </summary>
	public PixelPoint[] BarCorners { get; set; } = new PixelPoint[4];

	/// <summary>
	/// The forward fill bar, above the track's centre — <c>ThrottleGauge_Ctor</c>'s first LED bar,
	/// constructed with a range of <c>+0x400</c>.
	/// </summary>
	public (PixelPoint TopLeft, PixelPoint BottomRight) ForwardBar =>
		(BarCorners[0], BarCorners[1]);

	/// <summary>The reverse fill bar, below the centre — the second LED bar, range <c>-0x400</c>.</summary>
	public (PixelPoint TopLeft, PixelPoint BottomRight) ReverseBar =>
		(BarCorners[2], BarCorners[3]);

	/// <summary>
	/// The `SLIDE_DIR` int at file offset 1064, decoded out of
	/// <see cref="GAUFile.RemainderBeforeHeadingTape"/> rather than parsed as its own field, so the
	/// remainder still round-trips byte-exact. 1 in all 9 real files, selecting the vertical slider
	/// the gauge constructor builds at <c>00447e24</c>; the 0 branch (<c>004483c0</c>, a fixed 12px
	/// knob spanning the track's full height) is never exercised by retail data.
	/// </summary>
	public int SlideMode { get; set; } = 1;

	/// <summary>
	/// The int at file offset 1072, likewise decoded out of the remainder. It is the x nudge the
	/// gauge applies to the small tick sprite it parks beside the track — <c>ThrottleGauge_Ctor</c>
	/// adds it, shifted by the video mode's x coordinate shift, to the knob's own left edge. -2 to -4
	/// on seven of the nine retail files, +14 on TOMAHAWK and +17 on RAZOR.
	/// </summary>
	public int TickOffsetX { get; set; }
}
