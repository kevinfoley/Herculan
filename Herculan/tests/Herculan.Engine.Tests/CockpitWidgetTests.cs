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
	/// The latching/momentary split is the two button classes <c>MfdDisplay_Ctor</c> constructs: 0-5
	/// and 11-12 through the selection-driven class, 7-10 through the press-driven one. It is also
	/// exactly the set <c>MfdButton_Repaint</c> gates its caption re-font on, which is the check that
	/// makes the two agree.
	///
	/// <para>Index 6 is deliberately not asserted either way. The constructor's switch has no case 6,
	/// so that button takes whichever class the previous loop iteration happened to leave on the
	/// stack — the original has no answer to give. It is the degenerate zero rect that no mode shows,
	/// so nothing observable depends on it.</para>
	/// </summary>
	[Theory]
	[InlineData(0, true)]    // F1
	[InlineData(5, true)]    // F6
	[InlineData(7, false)]   // SELECT
	[InlineData(8, false)]   // RANGE
	[InlineData(9, false)]   // TARGET
	[InlineData(10, false)]  // XMIT
	[InlineData(11, true)]   // PASS
	[InlineData(12, true)]   // ACTIVE
	public void MfdButtonsSplitIntoLatchingAndMomentary(int index, bool expected) =>
		Assert.Equal(expected, MfdLayout.IsLatching(index));

	/// <summary>
	/// The Heads-Down Display splits the same way inside one class, on the index its paint switches on:
	/// the two page buttons latch, the arrows and magnifiers are momentary.
	/// </summary>
	[Theory]
	[InlineData(HddLayout.Widget.PageButton0, true)]
	[InlineData(HddLayout.Widget.PageButton1, true)]
	[InlineData(HddLayout.Widget.ArrowUp, false)]
	[InlineData(HddLayout.Widget.ArrowDown, false)]
	[InlineData(HddLayout.Widget.ZoomIn, false)]
	[InlineData(HddLayout.Widget.ZoomOut, false)]
	[InlineData(HddLayout.Widget.Transmit, false)]
	[InlineData(HddLayout.Widget.Cancel, false)]
	public void HddWidgetsSplitIntoLatchingAndMomentary(HddLayout.Widget widget, bool expected) =>
		Assert.Equal(expected, HddLayout.IsLatching(widget));

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
