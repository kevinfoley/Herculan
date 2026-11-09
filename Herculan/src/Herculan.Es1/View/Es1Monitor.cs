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
/// is the display's world layer: the 3D pass is drawn into it where <see cref="Es1VideoMemory.DrawWorld"/> marks the
/// cells, and a camera's view into a cockpit rect where <see cref="Es1VideoMemory.DrawWorldView"/> does, at the whole
/// scale <i>k</i> <see cref="Fit"/> picks, so the projection computed in the screen's own pixels keeps its shape.
/// </summary>
public sealed class Es1Monitor : IEs1WorldLayer, IDisposable {
	/// <summary>The shape the screen is shown at.</summary>
	private static readonly Vector2D<int> DisplayFrame = new(4000, 3000);

	private readonly IndexedWorldSurface _surface;
	private DynamixPalette? _palette;
	private bool _paletteSet;
	private Es1CursorImage? _cursorImage;

	/// <summary>Becomes <paramref name="display"/>'s world layer.</summary>
	public Es1Monitor(GL gl, Es1VideoMemory display) {
		_surface = new IndexedWorldSurface(gl, display.ScreenWidth, display.StoreRows);
		Display = display;
		display.Layer = this;
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

	// The GL viewport of the last world pass.
	private (int X, int Y, int Width, int Height)? _worldViewport;

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
	/// Sets the world layer's scale for a window of <paramref name="windowSize"/>: under the "High resolution widescreen"
	/// tweak (<see cref="TweakSettingDefinitions.HighResolutionWidescreen"/>) the least whole scale that fills the 4:3
	/// rect's height, and with it off 1, the world in the 320 × 200 screen's own pixels as retail rasterizes it. Every
	/// world pass takes the layer's scale, the NAV MAP's, the missile camera's and the Heads-Down Display map's
	/// (plan decisions 17 and 19) as well as view 0's. Read on every <see cref="Present"/>, so the tweak takes effect on
	/// the frame it is set.
	/// </summary>
	public void Fit(Vector2D<int> windowSize) {
		if (!TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.HighResolutionWidescreen)) {
			_surface.SetScale(1);
			return;
		}

		var (_, _, _, height) = new FrameFit(DisplayFrame, windowSize).Placement;
		_surface.SetScale((height + Es1VideoMemory.PageHeight - 1) / Es1VideoMemory.PageHeight);
	}

	/// <summary>
	/// Shows the monitor's picture in a window of <paramref name="windowSize"/>: the cells written since the last, then the
	/// window at the start address, and over it the display's cursor while it is shown, its image at the pointer less the
	/// hotspot, as retail's slot 30 draws it on the visible page (<see cref="Es1Cursor"/>).
	/// </summary>
	public void Present(Vector2D<int> windowSize) {
		Fit(windowSize);
		var (firstRow, rowCount) = Display.TakeDirtyRows();
		_surface.UploadCells(Display.Cells, firstRow, rowCount);
		var cursor = Display.Cursor;
		if (!ReferenceEquals(cursor.Image, _cursorImage)) {
			_cursorImage = cursor.Image;
			if (_cursorImage is { } image) {
				_surface.SetCursor(image.Pixels, image.Width, image.Rows, image.Opaque);
			} else {
				_surface.SetCursor([], 0, 0, false);
			}
		}

		_surface.Present(new FrameFit(DisplayFrame, windowSize), Display.MonitorStart, Es1VideoMemory.PageWidth,
			Es1VideoMemory.PageHeight, Es1VideoMemory.CellCount, cursor.DrawnAt);
	}

	/// <summary>The window's mapping between the host's pointer, in client coordinates of <paramref name="clientSize"/>, and the 320 × 200 screen at the monitor's 4:3 placement.</summary>
	public static ScreenPointer Pointer(Vector2D<int> framebufferSize, Vector2D<int> clientSize) =>
		new(new FrameFit(DisplayFrame, framebufferSize), clientSize, new Vector2D<int>(Es1VideoMemory.PageWidth, Es1VideoMemory.PageHeight));

	void IEs1WorldLayer.DrawWorld(int x, int y, int width, int height, IReadOnlyList<(int X, int Y, int Width)> keep) {
		_surface.KeepWorld(keep);
		var (glX, glY, glWidth, glHeight) = _surface.BeginWorld(x, y, width, height);
		_worldViewport = (glX, glY, glWidth, glHeight);
		WorldPass?.Invoke(glX, glY, glWidth, glHeight);
		_surface.RestoreWorld();
		_surface.EndWorld();
	}

	/// <summary>
	/// The cells written so far sent and painted into the layer under the rect, so a cell the view draws only part of
	/// shows the 2D drawing's colour in the rest; then the view's pass (<see cref="ViewPass"/>) and the cells it drew
	/// into, read back at once, or every cell for a scene that fills the rect (<see cref="Es1RectView.FillsRect"/>),
	/// whose backdrop writes no depth. Without a pass no cell is drawn into.
	/// </summary>
	bool[] IEs1WorldLayer.DrawWorldView(int x, int y, int width, int height, Es1RectView view) {
		if (ViewPass == null) {
			return new bool[width * height];
		}

		var (firstRow, rowCount) = Display.TakeDirtyRows();
		_surface.UploadCells(Display.Cells, firstRow, rowCount);
		_surface.PaintCells(x, y, width, height);
		var viewport = _surface.BeginWorld(x, y, width, height);
		ViewPass(view, (width, height), viewport);
		bool[] covered;
		if (view.FillsRect) {
			covered = new bool[width * height];
			Array.Fill(covered, true);
		} else {
			covered = _surface.ReadWorldCoverage(x, y, width, height);
		}

		_surface.EndWorld();
		return covered;
	}

	/// <summary>
	/// The pixel at the middle of the cell's <i>k</i> × <i>k</i> block of the world layer, the one the pick reads, put to
	/// <see cref="WorldPick"/>.
	/// </summary>
	object? IEs1WorldLayer.ObjectAt(int x, int y) {
		if (WorldPick == null || _worldViewport is not { } viewport) {
			return null;
		}

		_surface.BeginWorldPixel(x, y);
		try {
			return WorldPick(viewport);
		} finally {
			_surface.EndWorld();
		}
	}

	void IEs1WorldLayer.Copy(int sourceX, int sourceY, int targetX, int targetY, int width, int height) =>
		_surface.CopyWorld(sourceX, sourceY, targetX, targetY, width, height);

	public void Dispose() {
		if (ReferenceEquals(Display.Layer, this)) {
			Display.Layer = null;
		}

		_surface.Dispose();
	}
}
