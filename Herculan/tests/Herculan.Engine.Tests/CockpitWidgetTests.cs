using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The parts of the clickable-widget layer that stand on their own. Enumerating the widgets
/// themselves needs a loaded <see cref="CockpitArt"/>, which needs a real game install, so what is
/// pinned here is the hit test's edge behaviour and the identity type a click reports back — the two
/// places an off-by-one or a mixed-up index would go unnoticed.
/// </summary>
public class CockpitWidgetTests {
	private static CockpitWidget Widget(int x0 = 10, int y0 = 20, int x1 = 19, int y1 = 27) =>
		new(CockpitWidgetId.Mfd(0), Herculan.Engine.Render.CockpitSurface.Forward, x0, y0, x1, y1, Lit: false);

	/// <summary>
	/// <c>Widget_HitTest</c>'s rectangular case is inclusive on all four edges, so the last pixel row
	/// and column of a button are still the button.
	/// </summary>
	[Theory]
	[InlineData(10, 20, true)]   // top-left corner
	[InlineData(19, 27, true)]   // bottom-right corner, inclusive
	[InlineData(14, 24, true)]   // interior
	[InlineData(9, 24, false)]   // one left
	[InlineData(20, 24, false)]  // one right
	[InlineData(14, 19, false)]  // one above
	[InlineData(14, 28, false)]  // one below
	public void HitTestIsInclusiveOnEveryEdge(float x, float y, bool expected) =>
		Assert.Equal(expected, Widget().Contains(x, y));

	/// <summary>Inclusive edges mean the covered extent is one larger than the difference.</summary>
	[Fact]
	public void ExtentCountsBothEdges() {
		var widget = Widget();
		Assert.Equal(10, widget.Width);
		Assert.Equal(8, widget.Height);
	}

	/// <summary>
	/// A widget id reads back only as the family it was built for — the guard that stops an MFD button
	/// index from being switched on as a Heads-Down widget.
	/// </summary>
	[Fact]
	public void IdentityReadsBackOnlyAsItsOwnFamily() {
		var mfd = CockpitWidgetId.Mfd(4);
		Assert.Equal(4, mfd.AsMfdButton);
		Assert.Null(mfd.AsHddWidget);

		var hdd = CockpitWidgetId.Hdd(HddLayout.Widget.Transmit);
		Assert.Equal(HddLayout.Widget.Transmit, hdd.AsHddWidget);
		Assert.Null(hdd.AsMfdButton);
	}

	/// <summary>
	/// The MFD's buttons 7 and 10 share a rect — the same physical button captioned SELECT on the
	/// status screens and XMIT on FLASH COMM — and no mode shows both, which is what lets the hit test
	/// treat overlaps as impossible.
	/// </summary>
	[Fact]
	public void NoModeShowsBothOfTheSharedRectButtons() {
		foreach (MfdMode mode in Enum.GetValues<MfdMode>()) {
			Assert.False(MfdLayout.ButtonVisible(mode, 7) && MfdLayout.ButtonVisible(mode, 10));
		}
	}
}
