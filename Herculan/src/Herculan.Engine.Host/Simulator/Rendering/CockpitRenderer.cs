using System.Numerics;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Herculan.Engine.Render.Cockpit;
using Herculan.Engine.View;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Simulator.Rendering;

/// <summary>
/// The views from inside the machine: the three-panel cockpit with its world behind the canopy, the Heads-Down
/// Display below it on the same canvas, the RAZOR's heads-down window, the external view with its caption, and the
/// view behind the preferences panel.
/// Owns the 2D overlay every cockpit widget and modal panel is drawn through.
/// </summary>
sealed class CockpitRenderer : IDisposable {
	private readonly CockpitArt? _art;
	private readonly CockpitView _view;
	private readonly CockpitDisplays _displays;
	private readonly CockpitTextures _textures;
	private readonly WorldPassRenderer _passes;
	private readonly Overlay2DRenderer _overlay;
	private readonly CanopyPanelPainter _canopy;
	private readonly HeadsDownPainter _headsDown;
	private readonly ScreenChromePainter _screenChrome;

	public CockpitRenderer(GL gl, CockpitArt? art, CockpitView view, CockpitDisplays displays,
			CockpitTextures textures, WorldPassRenderer passes) {
		_art = art;
		_view = view;
		_displays = displays;
		_textures = textures;
		_passes = passes;
		_overlay = new Overlay2DRenderer(gl);
		_canopy = new CanopyPanelPainter(_overlay);
		_headsDown = new HeadsDownPainter(_overlay);
		_screenChrome = new ScreenChromePainter(_overlay);
		AlertPanels = new AlertPanelPainter(_overlay);
	}

	/// <summary>The modal panels' painter, which the panels' own loop draws through.</summary>
	public AlertPanelPainter AlertPanels { get; }

	/// <summary>Whether there is cockpit art to draw the three-panel view with.</summary>
	public bool HasCockpit => _art != null && _textures.Front != null && _textures.Side != null;

	// Draws the front/left/right panels side by side, each sized by its own cockpit-art image's native
	// aspect ratio fit to the full window height — not an equal three-way split of the window — so the
	// quads butt together edge-to-edge with no seam or overlap regardless of how the front and side art's
	// proportions differ from each other. The resulting three-panel composite is anchored to the window's
	// horizontal center as one unit: a narrower window crops its outer edges symmetrically, a wider one
	// leaves equal empty margins, and no panel is ever stretched.
	//
	// The 3D scene behind the three panels is one camera over one viewport, CockpitScreenLayout.World:
	// retail's glances are the forward view's image plane continued sideways, not turned cameras, so the
	// world runs straight across the panel seams at any pitch. Each panel's pass shares that camera and
	// viewport and differs only in the scissor it is drawn under.
	//
	// The whole composite also slides vertically with the Heads-Down Display pan. The original's cockpit
	// is a canvas twice the screen's height with the forward view's art at canvas row 0 and the HDD's at
	// row 474, both blitted during mission bring-up, and switching between them is a scroll of the
	// display window over that canvas — never a redraw (see CockpitViewGeometry, and CockpitPan for the
	// transition's own machinery). This reproduces the same geometry with two quads at fixed canvas
	// offsets and a moving window: the three cockpit panels are offset up by the pan distance, the HDD
	// panel sits one travel-distance below them, and the 3D viewports ride along with their panels so
	// the world scrolls out of frame exactly as the art does. The two views' art overlaps by six rows on
	// the canvas — HB1 starts at row 474 and HB0 runs to 479 — and the original resolves that by blitting
	// view 1 before view 0, so the draw order below does the same.
	public void DrawThreePanelCockpitView(GL gl, int totalWidth, int totalHeight) {
		// One placement for the whole frame, shared with the input path so a widget's click region cannot
		// drift from the art it was drawn over — see CockpitScreenLayout.
		var layout = CockpitScreenLayout.Create(totalWidth, totalHeight, _art!,
			_view.Pan.OffsetRows, _view.Pan.TravelRows, _view.Glance.OffsetPanels);

		var world = layout.World;
		var cockpitCamera = CloneCamera(_view.Camera);
		cockpitCamera.PrincipalPoint = CockpitPrincipalPoint(layout.Center, world, CockpitViewGeometry.ForwardViewIndex);

		// First, so the six-row overlap where the two views' art meets on the canvas resolves the way the
		// original's VRAM does. Sim_InitMissionSession (004614fc) blits view 1 and then view 0, so HB0's
		// bottom rows win over HB1's top rows and no sliver of the HDD shows under the dashboard at rest.
		if (_textures.HeadsDown != null && layout.HeadsDown is { } headsDown) {
			DrawHeadsDownWorld(gl, headsDown);
			_headsDown.Draw(headsDown.Viewport.X, headsDown.Viewport.Y,
				headsDown.Viewport.Width, headsDown.Viewport.Height,
				_textures.HeadsDown, headsDown.ArtWidth, headsDown.ArtHeight,
				_art, _textures.HudSprites, _displays.Hud, _textures.HddMap);
		}

		// GL's viewport origin is bottom-left, so a positive y offset moves a panel up the screen — which
		// is the direction the cockpit travels as the view pans down the canvas.
		// Each panel is one of DBSIM's views and carries that view's own .VUE 3D rect. The two glances
		// share a canopy bitmap but not a rect — view 3's runs the full width of the view where view 2's
		// stops short of it — so the mirrored panel takes view 3 rather than a mirrored copy of view 2.
		DrawPanel(layout.Left, _textures.Side!, mirrorHorizontally: true, hud: null,
			CockpitViewGeometry.MirroredGlanceViewIndex);
		DrawPanel(layout.Center, _textures.Front!, mirrorHorizontally: false, hud: _art,
			CockpitViewGeometry.ForwardViewIndex);
		DrawPanel(layout.Right, _textures.Side!, mirrorHorizontally: false, hud: null,
			CockpitViewGeometry.GlanceViewIndex);

		void DrawPanel(CockpitScreenLayout.PlacedSurface surface, GpuTexture texture,
				bool mirrorHorizontally, CockpitArt? hud, int viewIndex) {
			// The view's .VUE 3D rect, as a scissor around the whole 3D pass — the outer bound DBSIM's
			// rasterizer clips to, which the canopy's alpha cutout does not express on its own. See
			// CockpitViewGeometry.WorldViewport. The sky is inside the scissor because it is part of the
			// 3D view; the canopy below it must not be, so the test goes off again before the overlay.
			// The scissor is also what confines this pass to its own panel of the shared viewport.
			ApplyWorldViewportScissor(gl, surface, viewIndex, mirrorHorizontally);

			// The skeleton goes in before the canopy goes over it, so it is clipped by the viewport hole
			// like the rest of the world. Mostly of use with the machine's own model hidden, but it costs
			// one draw call.
			_passes.Draw(cockpitCamera, world.X, world.Y, world.Width, world.Height);

			gl.Disable(EnableCap.ScissorTest);

			var viewport = surface.Viewport;
			_canopy.Draw(viewport.X, viewport.Y, viewport.Width, viewport.Height, texture,
				surface.ArtWidth, surface.ArtHeight, mirrorHorizontally, hud,
				spriteTexture: _textures.HudSprites, hudState: _displays.Hud, mapTexture: _textures.HddMap,
				missileView: hud == null ? null : (x, y, width, height, view) => _passes.DrawMissileView(gl, x, y, width, height, view, _view.Camera));
		}
	}

	/// <summary>
	/// Sim_RenderFrame's last call, SystemButtons_PaintForPointer (00434520): over everything the cockpit drew,
	/// and under a modal panel, which is drawn by the panel's own loop.
	/// </summary>
	public void DrawSystemButtons(int width, int height, bool[] showing) {
		if (_art?.Sprites is { } systemSprites && _textures.HudSprites != null) {
			_screenChrome.DrawSystemButtons(width, height, _textures.HudSprites, systemSprites, showing);
		}
	}

	// The cockpit view manager's view 4: the world in the rows ExternalViewLayout gives it, across the
	// window's width, and below it the band View_FillOutside3dRect (0042da08) floods black and the caption on it.
	public void DrawExternalView(GL gl, int width, int height) {
		DrawWorldAbove(gl, width, height, ExternalViewLayout.ViewRows, ExternalViewLayout.CentreRow);

		if (_view.Chain?.Caption is { } caption && _art?.Sprites is { } captionSprites
				&& _textures.HudSprites != null && _art.Strings is { } strings) {
			// VIEW: names the player YOU, a squadmate by the pilot in its comm box, and anything else not at
			// all — Squad_IndexOf's -1 takes an empty name.
			string viewed = caption.Viewed == _view.PilotMech
				? strings.Text(ExternalViewLayout.PlayerNameGroup, 0) ?? string.Empty
				: Array.IndexOf(_displays.SquadSeats, caption.Viewed) is var slot and >= 0 ? _displays.SquadComm.Name(slot) : string.Empty;
			_screenChrome.DrawExternalViewCaption(width, height, _textures.HudSprites, captionSprites,
				(strings.Text(ExternalViewLayout.CaptionGroup, 0) ?? string.Empty) + viewed,
				(strings.Text(ExternalViewLayout.CaptionGroup, 1) ?? string.Empty)
					+ (strings.Text(ExternalViewLayout.CaptionGroup, caption.CameraControl ? 2 : 3) ?? string.Empty));
		}
	}

	// The view behind the [F12] preferences panel: view 4 in the 3D rect PreferencesPanel_Raise (0045cfd4) installs for as
	// long as the panel is up, which runs down to the panel's top edge, with no caption. Entering view 4 floods
	// everything outside that rect, so the band the panel stands in is cleared as the external view's is.
	public void DrawPanelOrbitView(GL gl, int width, int height) =>
		DrawWorldAbove(gl, width, height, PreferencesPanelLayout.ScreenTop, PreferencesPanelLayout.OrbitViewCentreRow);

	// The world across the window's width in the top viewRows of the 480-row screen, with the projection centre in the
	// middle across and centreRow down, and the band below it cleared black. The focal length stays the cockpit's; the
	// view is only shorter, so its field of view is the angle that length subtends over its own rows.
	private void DrawWorldAbove(GL gl, int width, int height, int viewRows, int centreRow) {
		int viewHeight = Math.Max(1, (int)MathF.Round(viewRows * height / (float)ExternalViewLayout.ScreenRows));
		int viewY = height - viewHeight;

		var externalCamera = CloneCamera(_view.Camera);
		externalCamera.FieldOfView = 2f * MathF.Atan(viewRows / 2f / Camera.FocalLengthPixels);
		externalCamera.PrincipalPoint = new Vector2(0.5f, centreRow / (float)viewRows);

		gl.Enable(EnableCap.ScissorTest);
		gl.Scissor(0, viewY, (uint)Math.Max(width, 1), (uint)viewHeight);
		_passes.Draw(externalCamera, 0, viewY, width, viewHeight);

		if (viewY > 0) {
			gl.Scissor(0, 0, (uint)Math.Max(width, 1), (uint)viewY);
			gl.ClearColor(0f, 0f, 0f, 1f);
			gl.Clear(ClearBufferMask.ColorBufferBit);
		}

		gl.Disable(EnableCap.ScissorTest);
	}

	// The world behind the heads-down view's art, for the one herc whose view 1 declares a 3D rect: the
	// RAZOR, through its .HD1 windows (docs/retail/formats/cockpit-views.md, "The RAZOR's heads-down view"). Sim_RenderFrame
	// (0045fb9c) draws the world into whatever view is current under CockpitView_ShowsWorld (0042db18),
	// with that view's own projection centre, which for view 1 sits above its window where the forward
	// view's reticle is — the forward image plane continued downward, the way the glances continue it
	// sideways. The camera is the forward view's; only the viewport and the centre within it differ.
	//
	// Retail draws only the current view, and draws nothing during the slide. This draws while any of
	// the heads-down art is on screen, which is what the engine's animated pan needs: the forward
	// panels keep their world through the pan in the same way.
	private void DrawHeadsDownWorld(GL gl, CockpitScreenLayout.PlacedSurface surface) {
		var viewport = surface.Viewport;
		if (_view.ViewGeometry?.HasWorldViewport(CockpitViewGeometry.HeadsDownViewIndex) != true
			|| viewport.Y + viewport.Height <= 0) {
			return;
		}

		var headsDownCamera = CloneCamera(_view.Camera);
		headsDownCamera.PrincipalPoint = CockpitPrincipalPoint(surface, viewport, CockpitViewGeometry.HeadsDownViewIndex);

		ApplyWorldViewportScissor(gl, surface, CockpitViewGeometry.HeadsDownViewIndex, mirrorHorizontally: false,
			reachWindowEdges: true);

		_passes.Draw(headsDownCamera, viewport.X, viewport.Y, viewport.Width, viewport.Height);

		// The forward panels' rects reach the rows where the two views' art overlaps, and they draw the
		// same image there; without this their terrain would fail the depth test against this pass's and
		// leave their sky showing.
		gl.Clear(ClearBufferMask.DepthBufferBit);
		gl.Disable(EnableCap.ScissorTest);
	}

	// Confines the 3D pass for one panel to that view's .VUE viewport rect, or to the panel itself when
	// there is no rect to use. Either way the scissor is left on, and the caller turns it off once it has
	// finished drawing the world: every panel's pass renders the same shared viewport, so an unscissored
	// one would paint over its neighbours.
	//
	// The panel fallback covers a herc that ships no .VUE, and a view that declares a zero-size rect. The
	// zero case is not a degenerate rect to clamp away -- it is how every herc but RAZOR says its
	// heads-down view shows no world at all -- but DrawHeadsDownWorld does not call this for such a view,
	// so "the whole panel" is the safe reading for a hand-edited file.
	//
	// reachWindowEdges carries a rect that touches the art's left or right edge on out to the window's,
	// for the heads-down art, whose margins on a window wider than 4:3 are its edge columns stretched
	// outward (HeadsDownPainter.Draw). Where such a column is a window, the stretch is one too,
	// and the world continues into it rather than leaving the cleared framebuffer showing.
	private void ApplyWorldViewportScissor(GL gl, CockpitScreenLayout.PlacedSurface surface, int viewIndex,
			bool mirrorHorizontally, bool reachWindowEdges = false) {
		gl.Enable(EnableCap.ScissorTest);

		if (_view.ViewGeometry?.WorldViewport(viewIndex) is not { } rect) {
			var viewport = surface.Viewport;
			int panelX = Math.Max(viewport.X, 0);
			gl.Scissor(panelX, viewport.Y,
				(uint)Math.Max(viewport.X + viewport.Width - panelX, 0), (uint)viewport.Height);
			return;
		}

		// Art pixels to window pixels, through the same fit the canopy quad is drawn with. The rect is
		// stated in the view's own screen coordinates, and a mirrored panel's screen is the art
		// reflected about its width — so the rect is reflected the same way, which swaps its edges.
		float left = mirrorHorizontally ? surface.ArtWidth - rect.X1 : rect.X0;
		float right = mirrorHorizontally ? surface.ArtWidth - rect.X0 : rect.X1;
		var (windowX0, windowY0) = surface.ArtToWindow(left, rect.Y0);
		var (windowX1, windowY1) = surface.ArtToWindow(right, rect.Y1);

		// GL's scissor box is bottom-left origin in framebuffer pixels, where the window coordinates
		// above are top-left origin -- the same flip PlacedSurface.ViewportTopInWindow undoes. The
		// left edge is clamped to the window because a side panel's art overhangs it on a narrow one.
		if (reachWindowEdges) {
			var bounds = surface.Viewport;
			if (left <= 0f) {
				windowX0 = bounds.X;
			}

			if (right >= surface.ArtWidth) {
				windowX1 = bounds.X + bounds.Width;
			}
		}

		// Rounded outward, then cut to the panel. A panel's width is its art's scaled width rounded to whole pixels,
		// so the art can overhang the panel by a fraction of a pixel, and rounded outward that fraction becomes a
		// column of the neighbouring panel -- whose canopy is already drawn, and which this pass would paint the
		// world over as a one-pixel seam.
		var panel = surface.Viewport;
		int x = Math.Max((int)MathF.Floor(windowX0), Math.Max(panel.X, 0));
		int y = (int)MathF.Floor(surface.WindowHeight - windowY1);
		int width = Math.Max(Math.Min((int)MathF.Ceiling(windowX1), panel.X + panel.Width) - x, 0);
		int height = Math.Max((int)MathF.Ceiling(surface.WindowHeight - windowY0) - y, 0);

		gl.Scissor(x, y, (uint)width, (uint)height);
	}

	// Where the view axis lands in a world viewport, as a fraction of it — the herc's own .VUE projection
	// centre for one view, carried through the same art-to-window transform that view's art is drawn with
	// so it stays on the reticle through the heads-down pan. The viewport must share the surface's top
	// edge and height.
	//
	// The three forward panels share one viewport and the forward view's centre, and that is retail's
	// arithmetic rather than a simplification: a glance's centre is its own .VUE pair offset by its canvas
	// origin, which puts it at the forward view's reticle, off the glance's inner edge — see
	// docs/retail/formats/cockpit-views.md, "The side glances are one image plane". Without a .VUE the fallback
	// is APOCA's, which is a guess — but a far better one than the middle of the window, which is wrong
	// for every herc in the game.
	private Vector2 CockpitPrincipalPoint(CockpitScreenLayout.PlacedSurface surface, CockpitScreenLayout.Viewport world,
			int viewIndex) {
		var (centerX, centerY) = _view.ViewGeometry?.ProjectionCenter(viewIndex)
			?? (CockpitViewGeometry.DefaultProjectionCenterX, CockpitViewGeometry.DefaultProjectionCenterY);

		// The step kick and the damage shake both move the centre itself, in the art's own pixels, so
		// they go through the same art-to-window transform as everything else on the panel — which is how
		// the original applies them: straight onto the projection centre, before the view is installed.
		// Art y runs downward, and the two carry the original's opposite sign conventions — see
		// CockpitHitShake.OffsetPixels.
		var (windowX, windowY) = surface.ArtToWindow(centerX,
			centerY - _view.Kick.OffsetPixels + _view.Shake.OffsetPixels);

		// The world viewport shares the panels' top edge and height, so only x changes frame.
		return new Vector2(
			(windowX - world.X) / Math.Max(world.Width, 1),
			(windowY - surface.ViewportTopInWindow) / Math.Max(world.Height, 1));
	}

	private static Camera CloneCamera(Camera source) => new() {
		Position = source.Position,
		Yaw = source.Yaw,
		Pitch = source.Pitch,
		Roll = source.Roll,
		FieldOfView = source.FieldOfView,
		NearPlane = source.NearPlane,
		FarPlane = source.FarPlane,
	};

	public void Dispose() => _overlay.Dispose();
}
