using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;

namespace Herculan.Es1.View;

/// <summary>One view's window as <c>CockpitView_ApplyViewState</c> installs it in the render context: its rect, the
/// projection centre the projection reads from it, and its canvas origin.</summary>
/// <param name="X0">The rect's left column, <c>+0x210</c>.</param>
/// <param name="Y0">Its top row.</param>
/// <param name="X1">Its right column.</param>
/// <param name="Y1">Its bottom row.</param>
/// <param name="CentreX">Where the view axis lands on the screen: the rect's top-left less the record's pair with the canvas origin added.</param>
/// <param name="CentreY">The same, in rows.</param>
public readonly record struct Es1ViewWindow(int X0, int Y0, int X1, int Y1, int CentreX, int CentreY);

/// <summary>
/// The cockpit view manager as <c>CockpitViewManager_Ctor</c> (<c>ES1DBSIM 00021dd4</c>) and <c>CockpitViewManager_LoadViews</c>
/// (<c>00021fb0</c>) build it, and the view <c>CockpitView_SetView</c> (<c>00022558</c>) leaves installed
/// (docs/es1/simulation/cockpit-views.md): the <c>.VUE</c> records, each view's clip regions and canopy art, the cockpit
/// palette, and after <see cref="SetView"/> the current view's window, clip block and canopy on the screen.
///
/// <para>Not built: the cockpit object, its widgets and instruments (<c>Cockpit_LoadPanel</c>), which step 6 ports, and
/// the shake strip, the external view's rect and border, which no view change reaches in this step.</para>
/// </summary>
public sealed class Es1CockpitViews {
	/// <summary>The screen the simulator draws on, 320 by 200 (docs/es1/rendering/polygon-shading.md#the-display-drivers).</summary>
	public const int ScreenWidth = 320;

	/// <inheritdoc cref="ScreenWidth"/>
	public const int ScreenHeight = 200;

	/// <summary>The external view, which has no canopy and loads no art.</summary>
	public const int ExternalView = 4;

	private readonly Vue.Entry[] _records;
	private readonly Es1ClipRegions?[] _clips;
	private readonly DynamixBitmap?[] _canopies;

	private Es1CockpitViews(string name, Vue.Entry[] records, Es1ClipRegions?[] clips, DynamixBitmap?[] canopies,
			DynamixPalette? palette) {
		Name = name;
		_records = records;
		_clips = clips;
		_canopies = canopies;
		CockpitPalette = palette;
	}

	/// <summary>The chassis name the files are named by, <c>+0x29</c>.</summary>
	public string Name { get; }

	/// <summary><c>CockpitPaletteSingleton</c> (<c>000925f0</c>), <c>dpl\cockpit</c>.</summary>
	public DynamixPalette? CockpitPalette { get; }

	/// <summary><c>+0x10</c>, the current view, -1 before the first set (docs/es1/simulation/cockpit-views.md#open).</summary>
	public int CurrentView { get; private set; } = -1;

	/// <summary>The window installed in the render context by the last view set; zeros before.</summary>
	public Es1ViewWindow Window { get; private set; }

	/// <summary>The canvas origin of the current view, <c>CockpitViewCanvasOrigin</c> (<c>000afa9c</c>).</summary>
	public (int X, int Y) CanvasOrigin { get; private set; }

	/// <summary>
	/// The screen's canopy pixels: the palette index of each of the 320 × 200 pixels the view sets have blitted, for
	/// the page the screen shows; -1 where no blit has reached.
	/// </summary>
	public short[] Canopy { get; } = Enumerable.Repeat((short)-1, ScreenWidth * ScreenHeight).ToArray();

	/// <summary>The current view's clip regions, the block copied into the render context; null for none loaded.</summary>
	public Es1ClipRegions? Clip => CurrentView >= 0 && CurrentView < _clips.Length ? _clips[CurrentView] : null;

	/// <summary>
	/// <c>CockpitViewManager_LoadViews</c> for <paramref name="name"/>: <c>vue\&lt;name&gt;</c>, <c>ed&lt;i&gt;\&lt;name&gt;</c>
	/// for each view and <c>db&lt;n&gt;\&lt;name&gt;</c> for each canopy, view 3 sharing view 2's
	/// (<c>CockpitCanopy_LoadViewBitmap</c>, <c>00022184</c>); and <c>dpl\cockpit</c>. Null when the <c>.VUE</c> is missing.
	/// </summary>
	public static Es1CockpitViews? Load(GameContent content, string name) {
		if (content.Read("vue", name + ".VUE") is not { } vueBytes || new VueTransformer().Parse(vueBytes) is not { Entries: { } records }) {
			return null;
		}

		var clips = new Es1ClipRegions?[records.Length];
		var canopies = new DynamixBitmap?[records.Length];
		for (int view = 0; view < records.Length; view++) {
			string folder = "ed" + view;
			clips[view] = content.Read(folder, name + ".ED" + view) is { } clipBytes
				? new Es1ClipRegionsTransformer().Parse(clipBytes)
				: null;

			int file = view > 2 ? view - 1 : view;
			string dbFolder = "db" + file;
			canopies[view] = content.Read(dbFolder, name + ".DB" + file) is { } artBytes
				? new DynamixBitmapArrayTransformer().Parse(artBytes)?.Images?.FirstOrDefault()
				: null;
		}

		var palette = content.Read("dpl", "COCKPIT.DPL") is { } paletteBytes ? new DynamixPaletteTransformer().Parse(paletteBytes) : null;
		return new Es1CockpitViews(name, records, clips, canopies, palette);
	}

	/// <summary>
	/// <c>CockpitView_SetView</c>: <c>CockpitView_ApplyViewState</c> for a view below 5, then for any view but 4 the view's
	/// canopy blitted at (0, 0), mirrored for view 3, through the draw page's origin. The cockpit's repaint is step 6's.
	/// </summary>
	public void SetView(int view) {
		if (view < ExternalView + 1) {
			ApplyViewState(view);
		}

		if (view != ExternalView && view >= 0 && view < _canopies.Length && _canopies[view] is { } art) {
			Blit(art, mirrored: view == 3);
		}

		CurrentView = view;
	}

	/// <summary>
	/// <c>CockpitView_ApplyViewState</c> (<c>000222c0</c>) for views 0 to 3: nothing for the current view; the draw page's
	/// origin moved to the new view's canvas origin for views 0 and 1; the view's clip block, the record's six values and
	/// the canvas origin added to the projection pair installed.
	/// </summary>
	private void ApplyViewState(int view) {
		if (view == CurrentView || view < 0 || view >= _records.Length) {
			return;
		}

		var record = _records[view];
		CanvasOrigin = (record.CanvasOriginX, record.CanvasOriginY);

		// Raster_InstallViewProjection's centre is the rect's top-left less the context's pair, which the set made the
		// record's pair plus the canvas origin (docs/retail/simulation/cockpit-views.md#the-projection-centre-is-not-the-middle-of-the-view).
		int pairX = record.CenterX + record.CanvasOriginX;
		int pairY = record.CenterY + record.CanvasOriginY;
		Window = new Es1ViewWindow(record.ViewportX0, record.ViewportY0, record.ViewportX1, record.ViewportY1,
			record.ViewportX0 - pairX, record.ViewportY0 - pairY);
	}

	/// <summary>
	/// <c>Bitmap_Blit</c> of the canopy at (0, 0) with the clip mode 0, onto the page at the draw page's origin, which the
	/// view state moved down by views 0's and 1's canvas origins: so view 1's art lands 197 rows down, all but its first
	/// three rows below the screen, and view 0's covers the screen. A glance's sideways origin, which does not move the
	/// page, is not reached in this step. Every pixel of the art is written, as ES2's canopy blit writes every pixel
	/// (docs/retail/simulation/cockpit-views.md#the-viewport-cutout); every retail canopy's index-0 pixels lie in its
	/// view's clip regions, where the world is drawn over them.
	/// </summary>
	private void Blit(DynamixBitmap art, bool mirrored) {
		var pixels = art.ImageData ?? [];
		for (int y = 0; y < art.Rows; y++) {
			int screenY = y + CanvasOrigin.Y;
			if (screenY < 0 || screenY >= ScreenHeight) {
				continue;
			}

			for (int x = 0; x < art.Cols && x < ScreenWidth; x++) {
				int source = y * art.Cols + (mirrored ? art.Cols - 1 - x : x);
				if (source < pixels.Length) {
					Canopy[screenY * ScreenWidth + x] = pixels[source];
				}
			}
		}
	}

	/// <summary>
	/// Whether the world is drawn at a screen pixel in the current view: inside the window's rect and inside one of the
	/// clip block's regions, a rect across the width or a block's span on the pixel's row, its last column included as
	/// ES2's span ends are (docs/es1/formats/hd-ed-clip-regions.md#open).
	/// </summary>
	public bool WorldShowsAt(int x, int y) {
		var window = Window;
		if (x < window.X0 || x > window.X1 || y < window.Y0 || y > window.Y1 || Clip is not { } clip) {
			return false;
		}

		foreach (var rect in clip.Rects) {
			if (y >= rect.Top && y <= rect.Bottom) {
				return true;
			}
		}

		foreach (var block in clip.Blocks) {
			int row = y - block.FirstRow;
			if (row >= 0 && row < block.Rows.Length && x >= block.Rows[row].Start && x <= block.Rows[row].End) {
				return true;
			}
		}

		return false;
	}
}
