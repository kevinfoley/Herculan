using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.View;

namespace Herculan.Engine.Render;

/// <summary>
/// Where every piece of the cockpit lands on screen for one frame, and how to get back from a screen
/// pixel to the art pixel under it.
///
/// <para><b>Why this exists.</b> DBSIM blits the cockpit at a fixed canvas origin, so a widget's
/// authored rect <i>is</i> a screen rect and its mouse code hit-tests authored coordinates directly
/// (docs/retail/simulation/cockpit-input.md §4-6). Herculan does not: it fits the art by height, centres a
/// three-panel content block whose width depends on the window, and offsets vertically by the
/// heads-down pan — with the forward and heads-down art on two separately placed surfaces. Screen to
/// widget is therefore a real transform, and it has to be the <i>same</i> transform the art was drawn
/// with or click regions drift off their buttons. Both the draw path and the hit-test path take their
/// geometry from here so there is only one definition to be right.</para>
///
/// <para><b>Two nested placements.</b> Each panel gets a GL viewport (outer), and inside it the art
/// quad is fit by height and horizontally centred (inner) — the fit
/// <see cref="Cockpit.CanopyPanelPainter.Draw"/> and <see cref="Cockpit.HeadsDownPainter.Draw"/> each compute
/// for themselves. <see cref="PlacedSurface"/> carries both halves so a caller can cross the whole
/// chain in one step.</para>
///
/// <para><b>Coordinate conventions</b>, which differ between the three spaces this type joins:</para>
/// <list type="bullet">
/// <item><description><b>Window</b> — origin top-left, +Y down. What the mouse reports.</description></item>
/// <item><description><b>Viewport</b> — GL's own: origin bottom-left, so a viewport's
/// <see cref="Viewport.Y"/> is measured up from the bottom of the window and a <i>positive</i> pan
/// offset moves a panel <i>up</i> the screen.</description></item>
/// <item><description><b>Art</b> — origin top-left of the cockpit frame, +Y down, in the frame's own
/// device pixels. The space <see cref="MfdLayout"/>, <see cref="HddLayout"/> and the overlay shader
/// all work in.</description></item>
/// </list>
/// </summary>
public sealed class CockpitScreenLayout {
	private CockpitScreenLayout(int windowWidth, int windowHeight, int panPixels, int sideWidth,
			PlacedSurface left, PlacedSurface center, PlacedSurface right, PlacedSurface? headsDown,
			Viewport world) {
		WindowWidth = windowWidth;
		WindowHeight = windowHeight;
		PanPixels = panPixels;
		_sideWidth = sideWidth;
		Left = left;
		Center = center;
		Right = right;
		HeadsDown = headsDown;
		World = world;
	}

	private readonly int _sideWidth;

	/// <summary>Window width in pixels this layout was computed for.</summary>
	public int WindowWidth { get; }

	/// <summary>Window height in pixels this layout was computed for.</summary>
	public int WindowHeight { get; }

	/// <summary>
	/// How far up the screen the cockpit panels have travelled, in window pixels — the pan's canvas-row
	/// offset scaled into screen space. Zero at the forward view.
	/// </summary>
	public int PanPixels { get; }

	/// <summary>The left side panel. Mirrored art; carries no clickable widgets.</summary>
	public PlacedSurface Left { get; }

	/// <summary>The forward view — the panel the console, HUD and MFD are drawn on.</summary>
	public PlacedSurface Center { get; }

	/// <summary>The right side panel. Carries no clickable widgets.</summary>
	public PlacedSurface Right { get; }

	/// <summary>
	/// The Heads-Down Display, or null when the herc has no <c>.HB1</c>. Spans the full window width
	/// rather than sitting in a panel column, and rides at a negative viewport Y so it sits below the
	/// screen until the pan brings it up.
	/// </summary>
	public PlacedSurface? HeadsDown { get; }

	/// <summary>
	/// The one viewport the 3D scene behind all three panels is drawn into: their combined span, cut
	/// to the window's width, riding the pan with them. Its height is the panels' own, so the
	/// camera's vertical field of view — fixed by the focal length over the view's rows — is the same
	/// as it would be over any one panel.
	///
	/// <para>One viewport because retail's three views are one image plane. A glance keeps the
	/// forward view's camera and focal length, and its projection centre lands off the panel's inner
	/// edge at the forward view's reticle — see docs/retail/simulation/cockpit-views.md, "The side glances are
	/// one image plane". Cutting to the window is what keeps the side views from costing anything past
	/// the pixels actually shown.</para>
	/// </summary>
	public Viewport World { get; }

	/// <summary>
	/// The panel a <see cref="CockpitSurface"/> names, or null when this cockpit has no such panel.
	/// For converting a window point to that surface's art pixels without first hit-testing which
	/// surface the point is on — what a captured drag needs, since it follows the pointer off its own
	/// panel.
	/// </summary>
	public PlacedSurface? Surface(CockpitSurface surface) => surface switch {
		CockpitSurface.Forward => Center,
		CockpitSurface.HeadsDown => HeadsDown,
		_ => null,
	};

	/// <summary>
	/// Places every panel for one frame.
	/// </summary>
	/// <param name="windowWidth">Framebuffer width in pixels.</param>
	/// <param name="windowHeight">Framebuffer height in pixels.</param>
	/// <param name="art">The herc's cockpit art, for the three frames' native sizes.</param>
	/// <param name="panOffsetRows">
	/// <see cref="CockpitPan.OffsetRows"/> — how far down the cockpit canvas the display window
	/// currently sits, in the canvas's own device rows.
	/// </param>
	/// <param name="travelRows">
	/// <see cref="CockpitPan.TravelRows"/> — the full forward-to-heads-down distance in canvas rows,
	/// which is what the heads-down surface's rest position is measured back from.
	/// </param>
	/// <param name="glanceOffsetPanels">
	/// <see cref="CockpitGlance.OffsetPanels"/> — how far the strip has slid toward a side window, in
	/// side-panel widths, positive toward the right. Clamped to <see cref="GlanceReachPanels"/>.
	/// </param>
	public static CockpitScreenLayout Create(int windowWidth, int windowHeight, CockpitArt art,
			float panOffsetRows, int travelRows, float glanceOffsetPanels = 0f) {
		ArgumentNullException.ThrowIfNull(art);
		return Create(windowWidth, windowHeight, art.Front, art.Side, art.HeadsDown, panOffsetRows, travelRows,
			glanceOffsetPanels);
	}

	/// <summary>
	/// Places every panel from frame sizes alone, without a loaded <see cref="CockpitArt"/> — the form
	/// the geometry tests drive, since none of this math needs pixels.
	/// </summary>
	public static CockpitScreenLayout Create(int windowWidth, int windowHeight,
			CockpitFrame front, CockpitFrame side, CockpitFrame? headsDown,
			float panOffsetRows, int travelRows, float glanceOffsetPanels = 0f) {
		ArgumentNullException.ThrowIfNull(front);
		ArgumentNullException.ThrowIfNull(side);

		windowWidth = Math.Max(windowWidth, 1);
		windowHeight = Math.Max(windowHeight, 1);

		// Device-pixel-to-screen scale, shared by the panel art and the pan distance so both move
		// together — the same fit-by-height factor each surface's inner quad uses for the art itself.
		float panScale = windowHeight / (float)CockpitViewGeometry.ViewHeight;
		int panPixels = (int)MathF.Round(panOffsetRows * panScale);
		int headsDownTopPixels = (int)MathF.Round((travelRows - panOffsetRows) * panScale);

		int centerWidth = PanelWidthForHeight(front, windowHeight);
		int sideWidth = PanelWidthForHeight(side, windowHeight);

		// A glance slides the whole strip the other way, stopping where the side panel's outer edge
		// meets the window's — see CockpitGlance for why the stop is not retail's full panel.
		int overhang = SideOverhang(windowWidth, centerWidth, sideWidth);
		int glancePixels = Math.Clamp((int)MathF.Round(glanceOffsetPanels * sideWidth), -overhang, overhang);

		int leftX = (windowWidth - (sideWidth + centerWidth + sideWidth)) / 2 - glancePixels;
		int centerX = leftX + sideWidth;
		int rightX = centerX + centerWidth;

		int worldX0 = Math.Max(leftX, 0);
		int worldX1 = Math.Min(rightX + sideWidth, windowWidth);
		var world = new Viewport(worldX0, panPixels, Math.Max(worldX1 - worldX0, 1), windowHeight);

		return new CockpitScreenLayout(windowWidth, windowHeight, panPixels, sideWidth,
			left: Place(new Viewport(leftX, panPixels, sideWidth, windowHeight), side, windowHeight),
			center: Place(new Viewport(centerX, panPixels, centerWidth, windowHeight), front, windowHeight),
			right: Place(new Viewport(rightX, panPixels, sideWidth, windowHeight), side, windowHeight),
			headsDown: headsDown is null
				? null
				: Place(new Viewport(0, -headsDownTopPixels, windowWidth, windowHeight), headsDown, windowHeight),
			world: world);
	}

	/// <summary>
	/// How far a glance may slide the strip, in side-panel widths: the part of one side panel the window
	/// cuts off at rest, so the glance stops with that panel's outer edge on the window's edge. 1 in a
	/// 4:3 window, which is retail's full-panel glance; 0 once the window shows all three panels.
	/// </summary>
	public static float GlanceReachPanels(int windowWidth, int windowHeight, CockpitFrame front, CockpitFrame side) {
		ArgumentNullException.ThrowIfNull(front);
		ArgumentNullException.ThrowIfNull(side);

		windowHeight = Math.Max(windowHeight, 1);
		int sideWidth = PanelWidthForHeight(side, windowHeight);
		int overhang = SideOverhang(Math.Max(windowWidth, 1), PanelWidthForHeight(front, windowHeight), sideWidth);
		return Math.Min(overhang / (float)sideWidth, 1f);
	}

	/// <inheritdoc cref="GlanceReachPanels(int, int, CockpitFrame, CockpitFrame)"/>
	public static float GlanceReachPanels(int windowWidth, int windowHeight, CockpitArt art) {
		ArgumentNullException.ThrowIfNull(art);
		return GlanceReachPanels(windowWidth, windowHeight, art.Front, art.Side);
	}

	/// <summary>Pixels of one side panel the window cuts off when the strip is centred; never negative.</summary>
	private static int SideOverhang(int windowWidth, int centerWidth, int sideWidth) =>
		Math.Max(0, (sideWidth + centerWidth + sideWidth - windowWidth) / 2);

	/// <summary>
	/// The width a panel needs to show <paramref name="frame"/> at <paramref name="height"/> without
	/// distorting it. Never zero, so a degenerate window still yields a usable viewport.
	/// </summary>
	public static int PanelWidthForHeight(CockpitFrame frame, int height) =>
		Math.Max(1, (int)MathF.Round(frame.Width * (height / (float)Math.Max(frame.Height, 1))));

	private static PlacedSurface Place(Viewport viewport, CockpitFrame frame, int windowHeight) {
		// Fit by height, preserving the art's aspect ratio, quad centred in the viewport — mirroring
		// CanopyPanelPainter's and HeadsDownPainter's own copies of this fit. When the panel is narrower than the art the
		// quad overhangs and GL clips it; when it is wider the quad sits centred with a margin.
		float scale = viewport.Height / (float)Math.Max(frame.Height, 1);
		float quadX0 = (viewport.Width - frame.Width * scale) / 2f;
		return new PlacedSurface(viewport, scale, quadX0, frame.Width, frame.Height, windowHeight);
	}

	/// <summary>
	/// The art pixel under a window pixel on whichever widget-bearing surface owns it, or null when the
	/// point is over neither.
	///
	/// <para>The forward view is tested first, and wins where the two overlap. That is the same
	/// precedence the draw order gives: <see cref="Cockpit.HeadsDownPainter.Draw"/> runs before the
	/// panels so the forward canopy's art covers the heads-down art's top rows, matching how
	/// <c>Sim_InitMissionSession</c> (<c>004614fc</c>) blits view 1 and then view 0 into the shared
	/// canvas. Without it, a click in the overlap band mid-pan could land on a widget the player cannot
	/// see.</para>
	/// </summary>
	public CockpitSurfaceHit? WindowToArt(float windowX, float windowY) {
		var (cx, cy) = Center.WindowToArt(windowX, windowY);
		if (Center.ContainsArt(cx, cy)) {
			return new CockpitSurfaceHit(CockpitSurface.Forward, cx, cy);
		}

		if (HeadsDown is { } headsDown) {
			var (hx, hy) = headsDown.WindowToArt(windowX, windowY);
			if (headsDown.ContainsArt(hx, hy)) {
				return new CockpitSurfaceHit(CockpitSurface.HeadsDown, hx, hy);
			}
		}

		return null;
	}

	/// <summary>
	/// The left or right screen-edge strip under a window pixel, or null. These lead to the side
	/// windows, and <see cref="Input.CockpitInput"/> tests them only after every art-space widget, the
	/// same last-registered precedence the original's strips have (docs/retail/simulation/cockpit-input.md §10).
	///
	/// <para>Retail builds them into the forward view's own left and right columns, which in its 4:3
	/// frame are the screen's edges. Here the forward view's edges are the seams between panels, in the
	/// middle of a wide window, so the strips sit on the <i>window's</i> edges instead — the edges the
	/// manual's "screen edge leading to the view you want" means to the player. That placement is this
	/// engine's; the width is retail's <see cref="CockpitWidgets.ViewEdgeBandColumns"/>, scaled like
	/// the art.</para>
	/// </summary>
	public CockpitWidget? SideViewEdgeAt(float windowX, float windowY) {
		if (windowY < 0f || windowY >= WindowHeight) {
			return null;
		}

		int band = Math.Max(1, (int)MathF.Round(
			CockpitWidgets.ViewEdgeBandColumns * (WindowHeight / (float)CockpitViewGeometry.ViewHeight)));
		ViewEdgeStrip? strip = windowX >= 0f && windowX < band ? ViewEdgeStrip.Left
			: windowX >= WindowWidth - band && windowX < WindowWidth ? ViewEdgeStrip.Right
			: null;
		if (strip is not { } edge) {
			return null;
		}

		int x0 = edge == ViewEdgeStrip.Left ? 0 : WindowWidth - band;
		return new CockpitWidget(CockpitWidgetId.ViewEdge(edge), CockpitSurface.Window,
			x0, 0, x0 + band - 1, WindowHeight - 1, Lit: false);
	}

	/// <summary>A GL viewport rect: origin bottom-left, <see cref="Y"/> measured up from the window's bottom edge.</summary>
	public readonly record struct Viewport(int X, int Y, int Width, int Height);

	/// <summary>
	/// One cockpit frame as placed on screen: the viewport it draws into, and the fit-by-height quad
	/// inside that viewport. Converts between window pixels and the frame's own art pixels.
	/// </summary>
	/// <param name="Viewport">The GL viewport this surface draws into.</param>
	/// <param name="Scale">Art pixels to screen pixels, uniform on both axes.</param>
	/// <param name="QuadX0">The art quad's left edge in viewport-local pixels; negative when the art overhangs a narrow panel.</param>
	/// <param name="ArtWidth">The frame's native width in art pixels.</param>
	/// <param name="ArtHeight">The frame's native height in art pixels.</param>
	/// <param name="WindowHeight">
	/// The window height the viewport's bottom-left Y is measured against — needed to flip into the
	/// mouse's top-left space, and carried here so callers never have to pair a surface with the window
	/// it came from.
	/// </param>
	public readonly record struct PlacedSurface(Viewport Viewport, float Scale, float QuadX0,
			int ArtWidth, int ArtHeight, int WindowHeight) {

		/// <summary>
		/// The window-space Y of the viewport's <i>top</i> edge, converting GL's bottom-left origin into
		/// the mouse's top-left one.
		/// </summary>
		public float ViewportTopInWindow => WindowHeight - Viewport.Y - Viewport.Height;

		/// <summary>The art pixel under a window pixel. Not clamped — see <see cref="ContainsArt"/>.</summary>
		public (float X, float Y) WindowToArt(float windowX, float windowY) {
			if (Scale <= 0f) {
				return (float.NaN, float.NaN);
			}

			return ((windowX - Viewport.X - QuadX0) / Scale, (windowY - ViewportTopInWindow) / Scale);
		}

		/// <summary>The window pixel an art pixel lands on — the exact inverse of <see cref="WindowToArt"/>.</summary>
		public (float X, float Y) ArtToWindow(float artX, float artY) =>
			(artX * Scale + QuadX0 + Viewport.X, artY * Scale + ViewportTopInWindow);

		/// <summary>
		/// Whether an art coordinate falls inside the frame. Uses the frame's own bounds rather than the
		/// viewport's: a narrow panel clips the art's flanks, so points outside the viewport are still
		/// valid art coordinates and points inside it may be past the art's edge.
		/// </summary>
		public bool ContainsArt(float artX, float artY) =>
			artX >= 0f && artY >= 0f && artX < ArtWidth && artY < ArtHeight;
	}
}

/// <summary>Which of the cockpit's two widget-bearing surfaces a point landed on.</summary>
public enum CockpitSurface {
	/// <summary>The forward view — <c>.HB0</c>, the console and MFD.</summary>
	Forward = 0,

	/// <summary>The Heads-Down Display — <c>.HB1</c>.</summary>
	HeadsDown = 1,

	/// <summary>
	/// No cockpit frame: the window's own pixels. The side screen-edge strips
	/// (<see cref="CockpitScreenLayout.SideViewEdgeAt"/>) and the two system buttons
	/// (<see cref="SystemButtons"/>) live here; <see cref="CockpitScreenLayout.Surface"/>
	/// has no placement for it, so coordinates pass through unconverted.
	/// </summary>
	Window = 2,
}

/// <summary>A window point resolved to a surface and that surface's own art pixel.</summary>
/// <param name="Surface">Which surface owns the point.</param>
/// <param name="ArtX">Art-space x, in that surface's frame's device pixels.</param>
/// <param name="ArtY">Art-space y.</param>
public readonly record struct CockpitSurfaceHit(CockpitSurface Surface, float ArtX, float ArtY);
