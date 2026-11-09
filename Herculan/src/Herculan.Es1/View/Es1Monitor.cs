using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Render;
using Herculan.Engine.Settings;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Herculan.Es1.View;

/// <summary>
/// The monitor on <see cref="Es1VideoMemory"/>: the 320 × 200 window at the start address the display last showed,
/// shown at 4:3 as a CRT showed it (docs/herculan/plan-es1-first-mission.md, decision 7), each cell's palette index
/// through the live palette and each world cell from the world layer under it (<see cref="IndexedWorldSurface"/>). It
/// holds the display's world layer: the 3D pass is drawn into it where <see cref="Es1VideoMemory.DrawWorld"/> marks the
/// cells, and a camera's view into a cockpit rect where <see cref="Es1VideoMemory.DrawWorldView"/> does, at the whole
/// scale <i>k</i> <see cref="Fit"/> picks, so the projection computed in the screen's own pixels keeps its shape.
///
/// <para>Built on a view manager with a canvas (<see cref="Es1ViewManager.Canvas"/>), it holds the canvas's world layer
/// too, at the same scale, and under the "High resolution widescreen" tweak shows a window wider than 4:3 the canvas
/// around the monitor (plan decision 30, <see cref="Es1CanvasLayout"/>).</para>
/// </summary>
public sealed class Es1Monitor : IDisposable {
	/// <summary>The shape the screen is shown at.</summary>
	internal static readonly Vector2D<int> DisplayFrame = new(4000, 3000);

	private readonly IndexedWorldSurface _surface;
	private readonly SurfaceLayer _layer;
	private readonly Es1ViewManager? _views;
	private readonly IndexedWorldSurface? _canvasSurface;
	private readonly SurfaceLayer? _canvasLayer;
	private DynamixPalette? _palette;
	private bool _paletteSet;
	private Es1CursorImage? _cursorImage;

	/// <summary>
	/// Holds <paramref name="display"/>'s world layer, and with <paramref name="views"/> that has a canvas, the canvas's,
	/// whose layout follows the monitor's canvas point.
	/// </summary>
	public Es1Monitor(GL gl, Es1VideoMemory display, Es1ViewManager? views = null) {
		_surface = new IndexedWorldSurface(gl, display.ScreenWidth, display.StoreRows);
		Display = display;
		_layer = new SurfaceLayer(this, _surface, display);
		display.Layer = _layer;
		if (views?.Canvas is { } canvas) {
			_views = views;
			var memory = canvas.Memory;
			_canvasSurface = new IndexedWorldSurface(gl, memory.ScreenWidth, memory.StoreRows);
			_canvasLayer = new SurfaceLayer(this, _canvasSurface, memory);
			memory.Layer = _canvasLayer;
		}
	}

	public Es1VideoMemory Display { get; }

	/// <summary>The 3D pass, given the GL viewport (x, y, width, height, from the bottom-left) in the world layer it draws into.</summary>
	public Action<int, int, int, int>? WorldPass { get; set; }

	/// <summary>
	/// The readback of the object <see cref="WorldPass"/> last drew at one pixel, given that pass's GL viewport, run with
	/// the world layer bound as the pass left it and the scissor on the pixel (<see cref="IndexedWorldSurface.BeginWorldPixel"/>).
	/// Null answers no object.
	/// </summary>
	public Func<(int X, int Y, int Width, int Height), object?>? WorldPick { get; set; }

	/// <summary>
	/// A camera's view into a rect of the cockpit (<see cref="Es1VideoMemory.DrawWorldView"/>), given the view, the
	/// rect's size in the screen's pixels and the GL viewport (x, y, width, height, from the bottom-left) in the world
	/// layer it draws into. Whatever it draws must write its depth, which tells the cells it drew into
	/// (<see cref="IndexedWorldSurface.ReadWorldCoverage"/>).
	/// </summary>
	public Action<Es1RectView, (int Width, int Height), (int X, int Y, int Width, int Height)>? ViewPass { get; set; }

	/// <summary>The live palette the cells' indices show through, as <see cref="SetPalette"/> last sent it.</summary>
	public DynamixPalette? Palette => _palette;

	/// <summary>
	/// How many times the live palette has been sent (<see cref="SetPalette"/>, <see cref="InstallRange"/>): what changes
	/// when it does, so a reader that resolves indices to colors itself, as the 3D pass does, can tell when to again.
	/// </summary>
	public int PaletteVersion { get; private set; }

	/// <summary>The live palette the cells' indices show through; a palette other than the last is sent again.</summary>
	public void SetPalette(DynamixPalette? palette) {
		if (_paletteSet && ReferenceEquals(palette, _palette)) {
			return;
		}

		var rgba = new byte[256 * 4];
		for (int index = 0; index < 256; index++) {
			if (palette != null && palette.Colors.TryGetValue(index, out var entry)) {
				var colour = entry.GetColor();
				rgba[index * 4] = colour.R;
				rgba[index * 4 + 1] = colour.G;
				rgba[index * 4 + 2] = colour.B;
			}

			rgba[index * 4 + 3] = 255;
		}

		_surface.SetPalette(rgba);
		_canvasSurface?.SetPalette(rgba);
		_palette = palette;
		_paletteSet = true;
		PaletteVersion++;
	}

	/// <summary>
	/// <c>Palette_InstallRange</c> (<c>ES1DBSIM 00027e98</c>)(<paramref name="entries"/>, count, <paramref name="first"/>)
	/// on the live palette: a copy of it with the entries from <paramref name="first"/> replaced and sent as
	/// <see cref="SetPalette"/> sends one. Each entry is retail's palette dword, six-bit red, green and blue from the low
	/// byte up, scaled by the live palette's <see cref="DynamixPalette.Scalar"/> as the palette reader scales a
	/// <c>.DPL</c>'s. With no live palette nothing is installed; when every entry already holds its color nothing is
	/// sent, which leaves the screen as it is.
	/// </summary>
	public void InstallRange(int first, IReadOnlyList<uint> entries) {
		if (_palette is not { } live) {
			return;
		}

		var colors = new Dictionary<int, ColorBytes>(live.Colors);
		int scale = live.Scalar;
		bool changed = false;
		for (int i = 0; i < entries.Count; i++) {
			int r = Math.Min((int)(entries[i] & 0xff) * scale, 255), g = Math.Min((int)(entries[i] >> 8 & 0xff) * scale, 255);
			int b = Math.Min((int)(entries[i] >> 16 & 0xff) * scale, 255);
			if (colors.TryGetValue(first + i, out var held) && held.GetColor() is var shown
					&& shown.R == r && shown.G == g && shown.B == b) {
				continue;
			}

			var color = new ColorBytes((byte)r, (byte)g, (byte)b, 255);
			color.SetColor(RgbaColor.FromArgb(255, r, g, b));
			colors[first + i] = color;
			changed = true;
		}

		if (!changed) {
			return;
		}

		SetPalette(new DynamixPalette {
			ColorCount = live.ColorCount,
			Scalar = live.Scalar,
			PaletteSizeByte = live.PaletteSizeByte,
			RawIndexBytes = live.RawIndexBytes,
			Colors = colors,
			Index0AlphaKey = live.Index0AlphaKey,
			ShadeRamps = live.ShadeRamps,
			ShadeRampBytes = live.ShadeRampBytes,
		});
	}

	/// <summary>
	/// Sets the world layers' scale for a window of <paramref name="windowSize"/>: under the "High resolution widescreen"
	/// tweak (<see cref="TweakSettingDefinitions.HighResolutionWidescreen"/>) the least whole scale that fills the 4:3
	/// rect's height, and with it off 1, the world in the 320 × 200 screen's own pixels as retail rasterizes it. Every
	/// world pass takes the layer's scale, the NAV MAP's, the missile camera's and the Heads-Down Display map's
	/// (plan decisions 17 and 19) as well as view 0's. Read on every <see cref="Present"/>, so the tweak takes effect on
	/// the frame it is set.
	/// </summary>
	public void Fit(Vector2D<int> windowSize) {
		int scale = 1;
		if (Wide) {
			var (_, _, _, height) = new FrameFit(DisplayFrame, windowSize).Placement;
			scale = (height + Es1VideoMemory.PageHeight - 1) / Es1VideoMemory.PageHeight;
		}

		_surface.SetScale(scale);
		_canvasSurface?.SetScale(scale);
	}

	private static bool Wide => TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.HighResolutionWidescreen);

	/// <summary>
	/// The canvas's layout in a window of <paramref name="windowSize"/> (<see cref="Es1CanvasLayout"/>) when it shows one:
	/// under the tweak, with a canvas, in a window wider than 4:3; null otherwise.
	/// </summary>
	private Es1CanvasLayout? CanvasLayout(Vector2D<int> windowSize) {
		if (_views == null || !Wide) {
			return null;
		}

		var (column, row) = _views.MonitorCanvasPoint;
		var layout = Es1CanvasLayout.Create(windowSize, column, row);
		return layout.SideColumns > 0 ? layout : null;
	}

	/// <summary>
	/// <see cref="Es1ViewManager.GlanceReach"/> in a window of <paramref name="windowSize"/>: the canvas layout's
	/// <see cref="Es1CanvasLayout.Reach"/> when it shows one, and a whole side panel, retail's slide, otherwise.
	/// </summary>
	public int GlanceReach(Vector2D<int> windowSize) => CanvasLayout(windowSize)?.Reach ?? Es1VideoMemory.PageWidth;

	/// <summary><see cref="Es1ViewManager.CanvasShown"/> in a window of <paramref name="windowSize"/>: whether there is a canvas layout to show.</summary>
	public bool ShowsCanvas(Vector2D<int> windowSize) => CanvasLayout(windowSize) != null;

	/// <summary>
	/// Shows the monitor's picture in a window of <paramref name="windowSize"/>: the cells written since the last, then the
	/// window at the start address, and over it the display's cursor while it is shown, its image at the pointer less the
	/// hotspot, as retail's slot 30 draws it on the visible page (<see cref="Es1Cursor"/>). With a canvas layout
	/// (<see cref="Es1CanvasLayout"/>), first the heads-down display's edge columns stretched out to the window's edges
	/// beside view 0's page, as ES2's port stretches its heads-down art, then the canvas's three pages over them at the
	/// monitor's canvas rows, and the monitor over those at its place, or in its stead the panels up lifted to the window's
	/// center (<see cref="LiftedPanels"/>).
	/// </summary>
	public void Present(Vector2D<int> windowSize) {
		Fit(windowSize);
		var (firstRow, rowCount) = Display.TakeDirtyRows();
		_surface.UploadCells(Display.Cells, firstRow, rowCount);
		if (_views?.Canvas is { } canvas && _canvasSurface != null) {
			var (canvasFirst, canvasCount) = canvas.Memory.TakeDirtyRows();
			_canvasSurface.UploadCells(canvas.Memory.Cells, canvasFirst, canvasCount);
		}

		var cursor = Display.Cursor;
		if (!ReferenceEquals(cursor.Image, _cursorImage)) {
			_cursorImage = cursor.Image;
			if (_cursorImage is { } image) {
				_surface.SetCursor(image.Pixels, image.Width, image.Rows, image.Opaque);
			} else {
				_surface.SetCursor([], 0, 0, false);
			}
		}

		if (CanvasLayout(windowSize) is not { } layout) {
			_surface.Present(new FrameFit(DisplayFrame, windowSize), Display.MonitorStart, Es1VideoMemory.PageWidth,
				Es1VideoMemory.PageHeight, Display.AddressCount, cursor.DrawnAt);
			return;
		}

		_surface.Clear(windowSize);
		// The heads-down display lies at the same canvas place in both halves of the store, its left edge on the half's
		// own column.
		var (baseX, baseY) = Display.PageBase(Display.VisiblePage);
		int edgeRow = (baseY + layout.MonitorRow) * Display.ScreenWidth + baseX;
		_surface.Draw(windowSize, layout.LeftStretch, edgeRow, 1, Es1VideoMemory.PageHeight, Display.AddressCount);
		_surface.Draw(windowSize, layout.RightStretch, edgeRow + Es1VideoMemory.PageWidth - 1, 1, Es1VideoMemory.PageHeight,
			Display.AddressCount);
		foreach (int view in (ReadOnlySpan<int>)[3, 0, 2]) {
			int column = Es1Canvas.PageColumn(view)!.Value;
			int row0 = Math.Max(0, layout.MonitorRow), row1 = Math.Min(Es1Canvas.PageRows(view), layout.MonitorRow + Es1VideoMemory.PageHeight);
			if (row1 > row0) {
				_canvasSurface!.Draw(windowSize, layout.CanvasRect(column, row0, column + Es1VideoMemory.PageWidth, row1),
					row0 * Es1Canvas.Width + column, Es1VideoMemory.PageWidth, row1 - row0, Es1Canvas.Width * Es1Canvas.Rows);
			}
		}

		if (LiftedPanels(layout) is not { } panels) {
			_surface.Draw(windowSize, layout.MonitorFit.Placement, Display.MonitorStart, Es1VideoMemory.PageWidth,
				Es1VideoMemory.PageHeight, Display.AddressCount, cursor.DrawnAt);
			return;
		}

		// The screen is not drawn at its place: the canvas's page there holds the frame the panels went up over.
		var centered = new FrameFit(DisplayFrame, windowSize).Placement;
		foreach (var panel in panels) {
			int x0 = Math.Max(panel.X0, 0), y0 = Math.Max(panel.Y0, 0);
			int x1 = Math.Min(panel.X1, Es1VideoMemory.PageWidth - 1), y1 = Math.Min(panel.Y1, Es1VideoMemory.PageHeight - 1);
			if (x1 < x0 || y1 < y0) {
				continue;
			}

			_surface.Draw(windowSize, ScreenRect(centered, x0, y0, x1 + 1, y1 + 1), Display.MonitorStart + y0 * Display.ScreenWidth + x0,
				x1 - x0 + 1, y1 - y0 + 1, Display.AddressCount);
		}

		if (cursor.DrawnAt is { } at) {
			_surface.DrawCursor(windowSize, centered, Es1VideoMemory.PageWidth, Es1VideoMemory.PageHeight, at);
		}
	}

	/// <summary>
	/// <b>This engine's own</b> (plan decision 30): the rects of the in-mission panels up
	/// (<see cref="Panels.Es1AlertGlobals.OpenRects"/>) when <paramref name="layout"/> puts the 320 × 200 screen off the
	/// window's center, after a glance; none otherwise. Such panels are lifted out of the screen: each one's rect is drawn
	/// at the window's centered 4:3 placement, where a 4:3 window shows it, with the cursor over that placement and the
	/// pointer mapped through it (<see cref="CockpitPointer"/>). The screen itself is not drawn, so the canvas's copy of
	/// the glance's page shows at its place (<see cref="Es1Canvas.TakeDrawnPage"/>), the frame the panels went up over.
	/// A panel's loop draws nothing else (<see cref="Panels.Es1AlertPanel"/>), so that copy stays the screen's picture
	/// under the panels.
	/// </summary>
	private IReadOnlyList<Es1Rect>? LiftedPanels(Es1CanvasLayout layout) {
		if (_views?.Cockpit.Panels is not { OpenRects.Count: > 0 } panels || layout.MonitorColumn == layout.WindowColumn
				|| _views.PageView(Display.VisiblePage) is not (2 or 3)) {
			return null;
		}

		return panels.OpenRects;
	}

	/// <summary>Where the screen's columns <paramref name="x0"/> up to <paramref name="x1"/> and rows <paramref name="y0"/> up to <paramref name="y1"/> land in the screen's <paramref name="placement"/>.</summary>
	private static (int Left, int Top, int Width, int Height) ScreenRect((int Left, int Top, int Width, int Height) placement,
			int x0, int y0, int x1, int y1) {
		int X(int column) => placement.Left + (int)MathF.Round(column * placement.Width / (float)Es1VideoMemory.PageWidth);
		int Y(int row) => placement.Top + (int)MathF.Round(row * placement.Height / (float)Es1VideoMemory.PageHeight);
		return (X(x0), Y(y0), X(x1) - X(x0), Y(y1) - Y(y0));
	}

	/// <summary>The window's mapping between the host's pointer, in client coordinates of <paramref name="clientSize"/>, and the 320 × 200 screen at the monitor's 4:3 placement.</summary>
	public static ScreenPointer Pointer(Vector2D<int> framebufferSize, Vector2D<int> clientSize) =>
		new(new FrameFit(DisplayFrame, framebufferSize), clientSize, new Vector2D<int>(Es1VideoMemory.PageWidth, Es1VideoMemory.PageHeight));

	/// <summary>
	/// <see cref="Pointer"/> at the monitor's place in the canvas layout when there is one (<see cref="Es1CanvasLayout.Pointer"/>),
	/// so the pointer stays on the monitor's 320 columns as a glance moves them; and at the window's center while panels are
	/// lifted there (<see cref="LiftedPanels"/>).
	/// </summary>
	public ScreenPointer CockpitPointer(Vector2D<int> framebufferSize, Vector2D<int> clientSize) =>
		CanvasLayout(framebufferSize) is { } layout && LiftedPanels(layout) == null
			? layout.Pointer(clientSize)
			: Pointer(framebufferSize, clientSize);

	public void Dispose() {
		if (ReferenceEquals(Display.Layer, _layer)) {
			Display.Layer = null;
		}

		if (_views?.Canvas is { } canvas && ReferenceEquals(canvas.Memory.Layer, _canvasLayer)) {
			canvas.Memory.Layer = null;
		}

		_surface.Dispose();
		_canvasSurface?.Dispose();
	}

	/// <summary>A store's world layer on one of the monitor's surfaces, drawn by the monitor's passes.</summary>
	private sealed class SurfaceLayer(Es1Monitor monitor, IndexedWorldSurface surface, Es1VideoMemory memory) : IEs1WorldLayer {
		// The GL viewport of the last world pass.
		private (int X, int Y, int Width, int Height)? _worldViewport;

		public void DrawWorld(int x, int y, int width, int height, IReadOnlyList<(int X, int Y, int Width)> keep) {
			surface.KeepWorld(keep);
			var (glX, glY, glWidth, glHeight) = surface.BeginWorld(x, y, width, height);
			_worldViewport = (glX, glY, glWidth, glHeight);
			monitor.WorldPass?.Invoke(glX, glY, glWidth, glHeight);
			surface.RestoreWorld();
			surface.EndWorld();
		}

		/// <summary>
		/// The cells written so far sent and painted into the layer under the rect, so a cell the view draws only part of
		/// shows the 2D drawing's colour in the rest; then the view's pass (<see cref="ViewPass"/>) and the cells it drew
		/// into, read back at once, or every cell for a scene that fills the rect (<see cref="Es1RectView.FillsRect"/>),
		/// whose backdrop writes no depth. Without a pass no cell is drawn into.
		/// </summary>
		public bool[] DrawWorldView(int x, int y, int width, int height, Es1RectView view) {
			if (monitor.ViewPass is not { } pass) {
				return new bool[width * height];
			}

			var (firstRow, rowCount) = memory.TakeDirtyRows();
			surface.UploadCells(memory.Cells, firstRow, rowCount);
			surface.PaintCells(x, y, width, height);
			var viewport = surface.BeginWorld(x, y, width, height);
			pass(view, (width, height), viewport);
			bool[] covered;
			if (view.FillsRect) {
				covered = new bool[width * height];
				Array.Fill(covered, true);
			} else {
				covered = surface.ReadWorldCoverage(x, y, width, height);
			}

			surface.EndWorld();
			return covered;
		}

		/// <summary>
		/// The pixel at the middle of the cell's <i>k</i> × <i>k</i> block of the world layer, the one the pick reads, put to
		/// <see cref="WorldPick"/>.
		/// </summary>
		public object? ObjectAt(int x, int y) {
			if (monitor.WorldPick is not { } pick || _worldViewport is not { } viewport) {
				return null;
			}

			surface.BeginWorldPixel(x, y);
			try {
				return pick(viewport);
			} finally {
				surface.EndWorld();
			}
		}

		public void Copy(int sourceX, int sourceY, int targetX, int targetY, int width, int height) =>
			surface.CopyWorld(sourceX, sourceY, targetX, targetY, width, height);

		public void CopyFrom(IEs1WorldLayer source, int sourceX, int sourceY, int targetX, int targetY, int width, int height) {
			if (source is SurfaceLayer from) {
				surface.CopyWorldFrom(from.Surface, sourceX, sourceY, targetX, targetY, width, height);
			}
		}

		private IndexedWorldSurface Surface => surface;
	}
}
