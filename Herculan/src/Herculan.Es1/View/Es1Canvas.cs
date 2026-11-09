namespace Herculan.Es1.View;

/// <summary>
/// <b>This engine's own:</b> the cockpit's canvas beside the monitor that a window wider than 4:3 shows under the
/// "High resolution widescreen" tweak (docs/herculan/plan-es1-first-mission.md#decisions, decision 30). Retail's views 0,
/// 2 and 3 are three windows on one image plane (docs/es1/simulation/cockpit-views.md#the-views-geometry), and the
/// skyline and the textured ground are placed in canvas columns (docs/es1/rendering/ground-and-sky.md#the-horizon-skyline),
/// so one projection continued across the three panels gives what retail draws in each view; this store holds the three
/// side by side in the canvas's own columns, view 3 at <see cref="PageColumn"/> 0, view 0 at 320 and view 2 at 640, in
/// <see cref="Width"/> × <see cref="Rows"/> cells outside retail's 256 KB, with a world layer of its own at the monitor's
/// scale.
///
/// <para>After each <c>Sim_RenderFrame</c> the page retail has just drawn, its cells and world pixels, is copied into the
/// page of the view whose window it was drawn in (<see cref="TakeDrawnPage"/>). A side page takes the page's first
/// <see cref="SideRows"/> rows: the strip below them that the view shake fills with color <c>0x3f</c> in a side view
/// (<see cref="Es1ViewManager.SetShakeBand"/>) stays in the monitor, and the canvas shows the heads-down display's edge
/// columns there instead. View 0's page takes all <see cref="Rows"/>, whose last rows are the heads-down display's top,
/// which lies at canvas row 197 under it.</para>
///
/// <para>Nothing retail reads comes from here: the pixel pick reads the display's own pass
/// (<see cref="Es1VideoMemory.WorldObjectAt"/>), and the canvas is only shown.</para>
/// </summary>
public sealed class Es1Canvas {
	/// <summary>The canvas's columns, the three views' 320 each.</summary>
	public const int Width = 3 * Es1VideoMemory.PageWidth;

	/// <summary>The rows each page holds: the screen's 200 and the ten below it that the view shake can scroll onto the monitor (<see cref="Es1ViewManager.StripY1"/>).</summary>
	public const int Rows = Es1ViewManager.StripY1;

	/// <summary>The rows a side page takes from the page retail drew.</summary>
	public const int SideRows = Es1VideoMemory.PageHeight;

	/// <summary>The canvas's three pages laid out as <see cref="Page"/> numbers them, each at its <see cref="PageColumn"/>.</summary>
	public Es1Canvas() {
		foreach (int view in (ReadOnlySpan<int>)[3, 0, 2]) {
			Memory.SetPageBase(Page(view), PageColumn(view)!.Value, 0);
		}
	}

	/// <summary>The canvas's cells and world layer.</summary>
	public Es1VideoMemory Memory { get; } = new(Width, Width * Rows);

	// The canvas memory's page that holds view `view`'s, numbered by column: view 3's 0, view 0's 1 and view 2's 2.
	private static int Page(int view) => PageColumn(view)!.Value / Es1VideoMemory.PageWidth;

	/// <summary>The canvas column view <paramref name="view"/>'s page starts at, view 0's at 320; none for views 1 and 4, which are not on the image plane.</summary>
	public static int? PageColumn(int view) => view switch {
		3 => 0,
		0 => Es1VideoMemory.PageWidth,
		2 => 2 * Es1VideoMemory.PageWidth,
		_ => null,
	};

	/// <summary>The rows view <paramref name="view"/>'s page shows: <see cref="Rows"/> for view 0 and <see cref="SideRows"/> for a side view.</summary>
	public static int PageRows(int view) => view == 0 ? Rows : SideRows;

	/// <summary>
	/// <paramref name="display"/>'s draw page, drawn in view <paramref name="view"/>'s window, copied into that view's page
	/// (<see cref="Es1VideoMemory.CopyPageFrom"/>); nothing for a view with no page.
	/// </summary>
	public void TakeDrawnPage(Es1VideoMemory display, int view) {
		if (PageColumn(view) is { } column) {
			Memory.CopyPageFrom(display, display.DrawPage, Es1VideoMemory.PageWidth, PageRows(view), column, 0);
		}
	}

	/// <summary>
	/// The side pages the frame's 3D pass did not draw, drawn by this engine as retail would draw them in their own view.
	/// This runs for views 0, 2 and 3 and for view 4; view 1's 3D pass draws the heads-down display's window.
	///
	/// <list type="bullet">
	/// <item>For a glance (view 2 or 3), view 0's world is drawn through <c>ed0</c> with record 0's window. Its page keeps
	/// the canopy of the last frame drawn in view 0.</item>
	/// <item>For views 0, 2 and 3, each of views 2 and 3 that is not the installed view gets its canopy art, mirrored for
	/// view 3 as <see cref="Es1ViewManager.SetView"/> blits it. Then the world is drawn through its clip block, with its
	/// record's window at its canvas origin (<see cref="Es1ViewWindow.Install"/>).</item>
	/// <item>For view 4, both side pages are filled with the border color <see cref="Es1ViewManager.BorderColor"/>. Then
	/// the world is drawn across each page's full width, in the rows of <see cref="Es1ViewManager.ExternalViewRect"/>. The
	/// rect's window is moved to that side view's canvas origin, so the external view's projection continues
	/// across.</item>
	/// </list>
	///
	/// Every pass draws from the current camera, as the frame's own pass did. <paramref name="beginWorldPass"/> readies the
	/// host's camera from <see cref="Es1ViewManager.Window"/>, so each pass runs with its own window installed there. The
	/// installed view's window is put back after, and readied again. With a <see cref="Es1ViewManager.CanvasOverlay"/>,
	/// each pass's world is drawn in the window the overlay cuts, and the overlay's draw goes over it, cut to the pass's
	/// clip block. The art is drawn every frame: it would otherwise need
	/// redrawing after any page copy, any switch into or out of view 4, and any change to the external rect.
	/// </summary>
	internal void DrawSidePanels(Es1ViewManager views, Action? beginWorldPass) {
		int installed = views.InstalledView;
		if (installed is not (0 or 2 or 3 or Es1CockpitViews.ExternalView)) {
			return;
		}

		var files = views.Files;
		var installedWindow = views.Window;
		foreach (int view in (ReadOnlySpan<int>)[3, 0, 2]) {
			if (view == installed || view >= files.Count || (view == 0 && installed is not (2 or 3))) {
				continue;
			}

			var record = files.Record(view);
			Memory.DrawPage = Page(view);
			Es1ViewWindow window;
			Es1ClipBlock clip;
			if (view == 0) {
				// The page keeps the canopy and instruments of the last frame drawn in view 0, which the instruments' repaint
				// then draws over (Es1ViewManager.DrawCanvasPanels).
				window = Es1ViewWindow.Install(record, record.CanvasOriginX, record.CanvasOriginY);
				clip = files.Clip(0);
			} else if (installed == Es1CockpitViews.ExternalView) {
				var rect = views.ExternalViewRect;
				Memory.FillRect(0, 0, Es1VideoMemory.PageWidth - 1, SideRows - 1, Es1ViewManager.BorderColor);
				window = Es1ViewWindow.Install(rect, record.CanvasOriginX, record.CanvasOriginY)
					with { X0 = 0, X1 = Es1VideoMemory.PageWidth - 1 };
				clip = Es1ClipBlock.FromRect(0, rect.ViewportY0, Es1VideoMemory.PageWidth - 1, rect.ViewportY1);
			} else {
				if (files.Canopy(view) is { } art) {
					Memory.Blit(art, mirrored: view == 3);
				}

				window = Es1ViewWindow.Install(record, record.CanvasOriginX, record.CanvasOriginY);
				clip = files.Clip(view);
			}

			var overlay = views.CanvasOverlay;
			var drawn = overlay?.Cut(window) ?? window;
			views.Window = drawn;
			beginWorldPass?.Invoke();
			Memory.DrawWorld(drawn, clip);
			overlay?.DrawOver(new Es1SceneCanvas(Memory, clip), window);
		}

		views.Window = installedWindow;
		beginWorldPass?.Invoke();
	}

	/// <summary>
	/// Makes view 0's page the draw, source, shown and drawn page of <see cref="Memory"/>, so an instrument's repaint that
	/// switches between the display's pages stays on that page.
	/// </summary>
	internal void BeginForwardPaint() {
		int page = Page(0);
		Memory.DrawPage = Memory.SourcePage = Memory.PairDrawn = Memory.PairVisible = page;
	}
}
