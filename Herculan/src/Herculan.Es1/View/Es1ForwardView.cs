using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Herculan.Es1.View;

/// <summary>
/// The screen of the installed view, drawn as the simulator's 320 × 200 page and shown at 4:3, as a CRT showed it
/// (docs/herculan/plan-es1-first-mission.md, decision 7): the world in the view's window with its projection, and the
/// canopy the view sets blitted, over every pixel the view's clip regions leave to it.
///
/// <para>The page is drawn into a frame of 320<i>k</i> × 200<i>k</i> square pixels, <i>k</i> the least whole scale that
/// fills the window's 4:3 rect, so the projection computed in the page's own pixels keeps its shape; the frame is then
/// stretched to 4:3 in the window.</para>
///
/// <para>Retail blits the canopy once per view change and draws the world over it each frame inside the clip regions
/// (docs/es1/simulation/cockpit-views.md#cockpitview_setview-00022558). Here the world is drawn first and the canopy over
/// it with each pixel inside the window and its clip regions left transparent, which shows the same pixels.</para>
///
/// <para>PLACEHOLDER: the canopy is coloured through the theater's palette alone. <c>CockpitViewManager_LoadViews</c>
/// installs the chassis' colour scheme from <c>dpl\cockpit</c> into the live palette, but which entries it installs is not
/// in docs/es1/simulation/cockpit-views.md (ES2's is <c>Palette_InstallRange</c>(42, 24, ...),
/// docs/retail/rendering/cockpit-canopy-palette.md#palette); step 6's batch reads it.</para>
/// </summary>
public sealed class Es1ForwardView : IDisposable {
	/// <summary>The shape the page is shown at.</summary>
	private static readonly Vector2D<int> DisplayFrame = new(4000, 3000);

	private readonly GL _gl;
	private readonly Overlay2DRenderer _overlay;
	private readonly GpuTexture _canopy;
	private FixedResolutionTarget? _target;
	private int _scale;

	public Es1ForwardView(GL gl, Es1CockpitViews views, DynamixPalette? palette) {
		_gl = gl;
		Views = views;
		_overlay = new Overlay2DRenderer(gl);
		_canopy = new GpuTexture(gl, CanopyPixels(views, palette), Es1CockpitViews.ScreenWidth, Es1CockpitViews.ScreenHeight);
	}

	public Es1CockpitViews Views { get; }

	/// <summary>
	/// Draws one frame into the window: <paramref name="drawWorld"/> with the GL viewport of the view's window in the
	/// frame (x, y, width, height, bottom-left origin), then the canopy, then the frame stretched to 4:3.
	/// </summary>
	public void Render(Vector2D<int> windowSize, Action<int, int, int, int> drawWorld) {
		var fit = new FrameFit(DisplayFrame, windowSize);
		var (_, _, _, height) = fit.Placement;
		int scale = Math.Clamp((height + Es1CockpitViews.ScreenHeight - 1) / Es1CockpitViews.ScreenHeight, 1, 8);
		if (_target == null || scale != _scale) {
			_target?.Dispose();
			_scale = scale;
			_target = new FixedResolutionTarget(_gl,
				new Vector2D<int>(Es1CockpitViews.ScreenWidth * scale, Es1CockpitViews.ScreenHeight * scale));
		}

		_target.Bind();
		var window = Views.Window;
		var (width, rows) = Es1EyeCamera.Size(window);
		int bottom = Es1CockpitViews.ScreenHeight - (window.Y0 + rows);
		drawWorld(window.X0 * scale, bottom * scale, width * scale, rows * scale);

		var size = _target.Size;
		_overlay.Begin(0, 0, size.X, size.Y);
		_overlay.Clear();
		_overlay.AddTexturedQuad(0, 0, size.X, size.Y, 0f, 0f, 1f, 1f);
		_overlay.Submit(_canopy);
		_overlay.End();

		_target.Present(new FrameFit(DisplayFrame, windowSize));
	}

	/// <summary>The canopy as RGBA: each pixel the screen's canopy index through the palette, transparent where the world shows.</summary>
	private static byte[] CanopyPixels(Es1CockpitViews views, DynamixPalette? palette) {
		var pixels = new byte[Es1CockpitViews.ScreenWidth * Es1CockpitViews.ScreenHeight * 4];
		for (int y = 0; y < Es1CockpitViews.ScreenHeight; y++) {
			for (int x = 0; x < Es1CockpitViews.ScreenWidth; x++) {
				int at = (y * Es1CockpitViews.ScreenWidth + x) * 4;
				if (views.WorldShowsAt(x, y)) {
					continue;
				}

				short index = views.Canopy[y * Es1CockpitViews.ScreenWidth + x];
				if (index >= 0 && palette != null && palette.Colors.TryGetValue(index, out var entry)) {
					var colour = entry.GetColor();
					pixels[at] = colour.R;
					pixels[at + 1] = colour.G;
					pixels[at + 2] = colour.B;
				}

				pixels[at + 3] = 255;
			}
		}

		return pixels;
	}

	public void Dispose() {
		_overlay.Dispose();
		_canopy.Dispose();
		_target?.Dispose();
	}
}
