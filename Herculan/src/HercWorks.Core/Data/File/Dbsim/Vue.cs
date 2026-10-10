namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - /SIMVOL0/VUE/(herc).VUE — one record per cockpit view, read by
/// <c>CockpitViewManager_LoadViews</c> (<c>00429834</c>) and installed into the render context by
/// <c>CockpitView_ApplyViewState</c> (<c>00429e60</c>). See docs/retail/formats/vue-view-geometry.md;
/// what the view does with it is docs/retail/simulation/cockpit-views.md.
///   0 - UINT32 - view count (4 in every retail file)
///   4 - SEQ_0 (INT32 each): 3D viewport rect x0/y0/x1/y1, view centre cx/cy, canvas origin x/y.
/// All coordinates are authored in the 320-wide space; the loader shifts them by
/// <c>VideoMode_X/YCoordShift</c> (1 in the 640x480 modes) before use.
/// </summary>
public class Vue {
	public int TotalViewports { get; set; }
	public Entry[]? Entries { get; set; }

	public Entry NewEntry() => new();

	public class Entry {
		/// <summary>Left edge of this view's 3D viewport rect. View 1 (heads-down) is a zero-size rect
		/// in every herc but RAZOR, which is why that view shows no 3D scene.</summary>
		public int ViewportX0 { get; set; }
		public int ViewportY0 { get; set; }
		public int ViewportX1 { get; set; }
		public int ViewportY1 { get; set; }

		/// <summary>
		/// Projection centre, stored negated and before the canvas origin is added — the same pair in
		/// every view of a herc. The centre in a view's own window is the rect's top-left less (this +
		/// canvas origin); see docs/retail/simulation/cockpit-views.md, "The projection centre is not the middle of
		/// the view".
		/// </summary>
		public int CenterX { get; set; }
		public int CenterY { get; set; }

		/// <summary>
		/// Where this view's 640x480 (or 320x240) window sits inside the cockpit canvas — the
		/// 320x480 / 640x960 virtual space the views scroll over. Forward is (0,0), heads-down
		/// (0,237), and the two side glances (+/-320,0).
		/// </summary>
		public int CanvasOriginX { get; set; }
		public int CanvasOriginY { get; set; }
	}
}
