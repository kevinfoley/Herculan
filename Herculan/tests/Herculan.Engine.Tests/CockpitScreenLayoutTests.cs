using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Render;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="CockpitScreenLayout"/> is the one place window pixels and cockpit-art pixels are
/// related, and it is what a mouse hit test will run backwards through
/// (docs/retail/simulation/cockpit-input.md). Everything it does is pure geometry with no GL involved, so it
/// can be pinned exactly — which matters because the failure mode it guards against, a click region
/// sitting a few pixels off the button it belongs to, is invisible until someone misses a click.
/// </summary>
public class CockpitScreenLayoutTests {
	/// <summary>Every retail canopy frame is the full 640x480 view; the pixels are irrelevant to geometry.</summary>
	private static CockpitFrame Frame(int width = CockpitViewGeometry.ViewWidth,
		int height = CockpitViewGeometry.ViewHeight) => new(Array.Empty<byte>(), width, height);

	/// <summary>The travel every retail <c>.VUE</c> gives the heads-down view, in device rows.</summary>
	private const int TravelRows = CockpitViewGeometry.DefaultHeadsDownOriginY;

	private static CockpitScreenLayout Layout(int width, int height, float panOffsetRows) =>
		CockpitScreenLayout.Create(width, height, Frame(), Frame(), Frame(), panOffsetRows, TravelRows);

	/// <summary>
	/// The native 4:3, where the forward art fills its panel exactly, and an odd size with no clean
	/// scale factor, where every surface's quad is inset from its viewport; each at rest and half panned.
	/// </summary>
	public static TheoryData<int, int, float> WindowsAndPans() {
		var data = new TheoryData<int, int, float>();
		foreach ((int w, int h) in new[] { (640, 480), (1367, 769) }) {
			foreach (float pan in new[] { 0f, TravelRows / 2f }) {
				data.Add(w, h, pan);
			}
		}

		return data;
	}

	[Theory]
	[MemberData(nameof(WindowsAndPans))]
	public void WindowToArtAndBackIsIdentity(int width, int height, float pan) {
		var layout = Layout(width, height, pan);

		foreach (var surface in new[] { layout.Center, layout.Left, layout.HeadsDown!.Value }) {
			foreach ((float x, float y) in new[] { (0f, 0f), (1f, 1f), (319.5f, 240.25f), (639f, 479f) }) {
				var (windowX, windowY) = surface.ArtToWindow(x, y);
				var (artX, artY) = surface.WindowToArt(windowX, windowY);

				Assert.Equal(x, artX, 3);
				Assert.Equal(y, artY, 3);
			}
		}
	}

	/// <summary>
	/// The art quad's own corners land where the fit-by-height placement says they should: flush to the
	/// top and bottom of the window, and spanning the panel's full width at the native aspect ratio. At
	/// 1920x960 the art is doubled, so its panel is 1280 wide, the three-panel strip of 3840 overhangs
	/// the window by 960 a side, and the forward panel starts at 320.
	/// </summary>
	[Fact]
	public void ForwardArtCornersLandOnThePanelEdges() {
		var layout = Layout(1920, 960, panOffsetRows: 0f);
		var center = layout.Center;

		Assert.Equal(320, center.Viewport.X);
		Assert.Equal(1280, center.Viewport.Width);
		Assert.Equal(2f, center.Scale, 5);

		var (topLeftX, topLeftY) = center.ArtToWindow(0, 0);
		Assert.Equal(320f, topLeftX, 3);
		Assert.Equal(0f, topLeftY, 3);

		var (bottomRightX, bottomRightY) = center.ArtToWindow(CockpitViewGeometry.ViewWidth, CockpitViewGeometry.ViewHeight);
		Assert.Equal(1600f, bottomRightX, 3);
		Assert.Equal(960f, bottomRightY, 3);
	}

	/// <summary>
	/// The centre of a window narrower than three panels is still the centre of the forward art — the
	/// panel overhangs and GL crops its flanks rather than the art shifting or squashing.
	/// </summary>
	[Fact]
	public void NarrowWindowKeepsTheForwardArtCentred() {
		var layout = Layout(800, 600, panOffsetRows: 0f);
		var (artX, artY) = layout.Center.WindowToArt(400, 300);

		Assert.Equal(CockpitViewGeometry.ViewWidth / 2f, artX, 3);
		Assert.Equal(CockpitViewGeometry.ViewHeight / 2f, artY, 3);
	}

	/// <summary>At rest the heads-down surface sits entirely below the window, exactly one travel down.</summary>
	[Fact]
	public void HeadsDownParksBelowTheWindow() {
		var layout = Layout(1920, 480, panOffsetRows: 0f);

		Assert.Equal(TravelRows, layout.HeadsDown!.Value.ViewportTopInWindow, 3);
	}

	/// <summary>Fully panned, the heads-down art is flush to the top of the window and the cockpit has left it.</summary>
	[Fact]
	public void FullPanBringsHeadsDownFlushToTheTop() {
		var layout = Layout(1920, 480, panOffsetRows: TravelRows);

		Assert.Equal(TravelRows, layout.PanPixels);
		Assert.Equal(0f, layout.HeadsDown!.Value.ViewportTopInWindow, 3);
		Assert.Equal(-TravelRows, layout.Center.ViewportTopInWindow, 3);

		var hit = layout.WindowToArt(960, 240);
		Assert.NotNull(hit);
		Assert.Equal(CockpitSurface.HeadsDown, hit!.Value.Surface);
		Assert.Equal(320f, hit.Value.ArtX, 3);
		Assert.Equal(240f, hit.Value.ArtY, 3);
	}

	/// <summary>
	/// Panning moves the cockpit up the screen and slides a window point further down the art — the
	/// direction check that catches a sign error in the GL-bottom-left to mouse-top-left flip.
	/// </summary>
	[Fact]
	public void PanningMovesTheCockpitUpAndTheArtPointDown() {
		var rest = Layout(1920, 480, panOffsetRows: 0f);
		var panned = Layout(1920, 480, panOffsetRows: 100f);

		Assert.True(panned.Center.ArtToWindow(320, 240).Y < rest.Center.ArtToWindow(320, 240).Y);
		Assert.Equal(100f, panned.Center.WindowToArt(960, 240).Y - rest.Center.WindowToArt(960, 240).Y, 3);
	}

	/// <summary>
	/// In the six-row band where the two views' art overlaps on the canvas — HB1 starts at row 474 and
	/// HB0 runs to 479 — the forward view wins, matching the draw order that puts HB0's bottom rows over
	/// HB1's top rows. The first row past the band, where the forward art has ended, is heads-down.
	/// </summary>
	[Fact]
	public void ForwardWinsTheOverlapBand() {
		// Scale 1 at this height, so window rows and canvas rows are the same thing: the band is window
		// rows 237-242, the last forward row is 242 (art row 479) and 243 is past the forward art.
		var layout = Layout(1920, 480, panOffsetRows: TravelRows / 2f);

		Assert.Equal(CockpitSurface.Forward, layout.WindowToArt(960, 242)!.Value.Surface);
		Assert.Equal(CockpitSurface.HeadsDown, layout.WindowToArt(960, 243)!.Value.Surface);
	}

	/// <summary>A point past the art's edge belongs to no surface, even when it is inside a viewport.</summary>
	[Fact]
	public void PointsOutsideTheArtResolveToNothing() {
		var layout = Layout(1920, 480, panOffsetRows: 0f);

		// Over the left side panel: inside the window, but neither widget-bearing surface owns it.
		Assert.Null(layout.WindowToArt(100, 240));
		Assert.Null(layout.WindowToArt(960, -1));
	}

	/// <summary>
	/// A herc with no <c>.HB1</c> gets no heads-down surface at all, and a point off the forward art,
	/// which is where hit-testing would go on to look for one, resolves to nothing.
	/// </summary>
	[Fact]
	public void MissingHeadsDownArtYieldsNoSurface() {
		var layout = CockpitScreenLayout.Create(1920, 480, Frame(), Frame(), headsDown: null,
			panOffsetRows: 0f, travelRows: 0);

		Assert.Null(layout.HeadsDown);
		Assert.Null(layout.WindowToArt(100, 240));
	}

	/// <summary>
	/// The glance's reach is the part of a side panel the window cuts off: a whole panel at 4:3, which
	/// is retail's glance, and never more even when the window is narrower than a panel; less in wider
	/// windows (at 1920x1080 the panels are 1440 wide and 1200 of each side one is cut off); and nothing
	/// once all three panels fit.
	/// </summary>
	[Theory]
	[InlineData(640, 480, 1f)]
	[InlineData(320, 480, 1f)]
	[InlineData(1920, 1080, 0.8333f)]
	[InlineData(2560, 480, 0f)]
	public void GlanceReachIsTheCutOffPartOfASidePanel(int width, int height, float expected) =>
		Assert.Equal(expected, CockpitScreenLayout.GlanceReachPanels(width, height, Frame(), Frame()), 3);

	/// <summary>
	/// A full glance right lands the right panel's outer edge on the window's right edge, and a glance
	/// left mirrors it.
	/// </summary>
	[Fact]
	public void FullGlanceBringsTheSidePanelFlushToTheWindowEdge() {
		const int width = 1920, height = 1080;
		float reach = CockpitScreenLayout.GlanceReachPanels(width, height, Frame(), Frame());

		var right = CockpitScreenLayout.Create(width, height, Frame(), Frame(), Frame(), 0f, TravelRows, reach);
		Assert.Equal(width, right.Right.Viewport.X + right.Right.Viewport.Width);

		var left = CockpitScreenLayout.Create(width, height, Frame(), Frame(), Frame(), 0f, TravelRows, -reach);
		Assert.Equal(0, left.Left.Viewport.X);
	}

	/// <summary>An offset past the reach — a window narrowed mid-glance — stops at the edge rather than running off it.</summary>
	[Fact]
	public void GlancePastTheReachIsClamped() {
		var layout = CockpitScreenLayout.Create(1920, 1080, Frame(), Frame(), Frame(), 0f, TravelRows, 1f);
		Assert.Equal(1920, layout.Right.Viewport.X + layout.Right.Viewport.Width);
	}

	/// <summary>
	/// Glancing right moves the forward art left on screen — the direction check for the sign of the
	/// offset. Half a glance at 1920x1080 is half a 1440-wide side panel: 720 pixels.
	/// </summary>
	[Fact]
	public void GlancingRightMovesTheForwardArtLeft() {
		var rest = CockpitScreenLayout.Create(1920, 1080, Frame(), Frame(), Frame(), 0f, TravelRows);
		var glanced = CockpitScreenLayout.Create(1920, 1080, Frame(), Frame(), Frame(), 0f, TravelRows, 0.5f);

		float shift = rest.Center.ArtToWindow(320, 240).X - glanced.Center.ArtToWindow(320, 240).X;
		Assert.Equal(720f, shift, 3);
	}

	/// <summary>The side strips sit on the window's edges, retail's five columns wide at the art's scale.</summary>
	[Fact]
	public void SideViewEdgesSitOnTheWindowEdges() {
		var layout = Layout(1280, 960, panOffsetRows: 0f);
		int band = CockpitWidgets.ViewEdgeBandColumns * 2;

		Assert.Equal(ViewEdgeStrip.Left, layout.SideViewEdgeAt(0, 480)!.Value.Id.AsViewEdge);
		Assert.Equal(ViewEdgeStrip.Left, layout.SideViewEdgeAt(band - 1, 480)!.Value.Id.AsViewEdge);
		Assert.Null(layout.SideViewEdgeAt(-1, 480));
		Assert.Null(layout.SideViewEdgeAt(band, 480));
		Assert.Equal(ViewEdgeStrip.Right, layout.SideViewEdgeAt(1280 - band, 480)!.Value.Id.AsViewEdge);
		Assert.Null(layout.SideViewEdgeAt(1280, 480));
		Assert.Equal(CockpitSurface.Window, layout.SideViewEdgeAt(0, 0)!.Value.Surface);
	}

	/// <summary>
	/// The panel width is the art's aspect ratio at the window's height, rounded to the nearest pixel
	/// (770 rows is 1026.67 columns), and never degenerate.
	/// </summary>
	[Theory]
	[InlineData(960, 1280)]
	[InlineData(770, 1027)]
	[InlineData(0, 1)]
	public void PanelWidthFollowsTheArtAspectRatio(int windowHeight, int expectedWidth) =>
		Assert.Equal(expectedWidth, CockpitScreenLayout.PanelWidthForHeight(Frame(), windowHeight));
}
