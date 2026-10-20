using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Render;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Herculan.Es1.View;

/// <summary>
/// The monitor on <see cref="Es1VideoMemory"/>: the 320 × 200 window at the start address the display last showed,
/// shown at 4:3 as a CRT showed it (docs/herculan/plan-es1-first-mission.md, decision 7), each cell's palette index
/// through the live palette and each world cell from the world layer under it (<see cref="IndexedWorldSurface"/>). It
/// is the display's world layer: the 3D pass is drawn into it where <see cref="Es1VideoMemory.DrawWorld"/> marks the
/// cells, at a whole scale <i>k</i>, the least that fills the window's 4:3 rect's height, so the projection computed in
/// the screen's own pixels keeps its shape.
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
		_surface = new IndexedWorldSurface(gl, Es1VideoMemory.Width, Es1VideoMemory.StoreRows);
		Display = display;
		display.Layer = this;
	}

	public Es1VideoMemory Display { get; }

	/// <summary>The 3D pass, given the GL viewport (x, y, width, height, from the bottom-left) in the world layer it draws into.</summary>
	public Action<int, int, int, int>? WorldPass { get; set; }

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
	}

	/// <summary>Sets the world layer's scale for a window of <paramref name="windowSize"/>.</summary>
	public void Fit(Vector2D<int> windowSize) {
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

	void IEs1WorldLayer.DrawWorld(int x, int y, int width, int height) {
		var (glX, glY, glWidth, glHeight) = _surface.BeginWorld(x, y, width, height);
		WorldPass?.Invoke(glX, glY, glWidth, glHeight);
		_surface.EndWorld();
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
