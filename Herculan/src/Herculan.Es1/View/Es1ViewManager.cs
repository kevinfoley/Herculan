using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Settings;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;

namespace Herculan.Es1.View;

/// <summary>
/// A wait for the monitor's vertical retrace that a page routine made before its caller went on: by default
/// <c>VGA8_ShowPage</c>'s, which waits out a retrace in progress and then for the next one to start
/// (docs/es1/rendering/screen-pages.md#the-simulators-own-page-routines); <see cref="VerticalRetrace"/>,
/// <c>Vga_WaitVerticalRetrace</c>'s.
/// </summary>
public readonly record struct Es1Retrace {
	/// <summary>
	/// <c>Vga_WaitVerticalRetrace</c> (<c>ES1DBSIM 000523a4</c>), which spins on port <c>0x3DA</c> until bit 3, in
	/// retrace, is set, so it ends at once while a retrace is in progress.
	/// </summary>
	public static Es1Retrace VerticalRetrace => new() { EndsInRetrace = true };

	/// <summary>Set for a wait that a retrace in progress ends; clear for one that waits for the next retrace to start.</summary>
	public bool EndsInRetrace { get; init; }
}

/// <summary>
/// <b>This engine's own</b> (plan decision 30): a 2D draw over the frame's 3D pass that the canvas's side panels take
/// too (<see cref="Es1ViewManager.CanvasOverlay"/>).
/// </summary>
internal interface IEs1CanvasOverlay {
	/// <summary>The window a panel's world is drawn in, given the window it would be drawn in without the draw.</summary>
	Es1ViewWindow Cut(Es1ViewWindow window);

	/// <summary>The draw over the panel's world through <paramref name="canvas"/>, the panel's uncut window being <paramref name="window"/>.</summary>
	void DrawOver(Es1SceneCanvas canvas, Es1ViewWindow window);
}

/// <summary>
/// The cockpit view manager, <c>mechview.cpp</c> (docs/es1/simulation/cockpit-views.md): the manager's state
/// <c>CockpitViewManager_Ctor</c> (<c>ES1DBSIM 00021dd4</c>) sets up, the render context's window and clip block the
/// current view installs, and the view changes on the display's pages: <c>CockpitView_ApplyViewState</c>,
/// <c>CockpitView_SetView</c>, <c>CockpitViewManager_QueueViewCommand</c>, <c>CockpitView_ProcessViewCommand</c> and
/// <c>CockpitView_StepViewTransition</c>, with the external view's rect and border, and the view shake.
///
/// <para>A routine that waits for a vertical retrace in retail, <c>VGA8_ShowPage</c> with
/// <c>Display_FlipWithoutPan</c> clear or <c>Vga_WaitVerticalRetrace</c>, yields an <see cref="Es1Retrace"/> here, and
/// its caller is resumed when the wait ends on the monitor's retraces (<see cref="Host.Es1FrameLoop"/>), so a slide's
/// steps are seen one retrace each while the simulation stands, as when retail's
/// <c>CockpitView_StepViewTransition</c> runs the whole slide inside one call (docs/es1/rendering/screen-pages.md#the-simulators-own-page-routines).</para>
/// </summary>
public sealed class Es1ViewManager {
	/// <summary>
	/// <c>ExternalView_DefaultRect</c> (<c>000afb24</c>), which the constructor sets the first time: the rect {0, 30, 319,
	/// 170} and the projection pair (−160, −75) of a <c>.VUE</c> record.
	/// </summary>
	public static Vue.Entry ExternalViewDefaultRect => new() {
		ViewportX0 = 0, ViewportY0 = 30, ViewportX1 = 319, ViewportY1 = 170, CenterX = -160, CenterY = -75,
	};

	/// <summary><c>View_FillOutside3dRect</c>'s brush colour.</summary>
	public const byte BorderColor = 0x3f;

	/// <summary>A pan's step in rows, and a glance's in columns.</summary>
	public const int PanStep = 5, GlanceStep = 10;

	/// <summary>
	/// <c>CockpitViewManager_Ctor</c>(0, the player's machine) after <c>CockpitViewManager_LoadViews</c>: no command
	/// pending, none staged, no cooldown, no border asked for, and view 0 current, the manager being a zero-filled heap
	/// block (docs/es1/simulation/cockpit-views.md#the-view-manager). <c>ExternalView_Rect</c> takes the default rect and
	/// <c>ExternalView_ClipBlock</c> its one region; and <c>CockpitViewManager_LoadViews</c>'s cockpit,
	/// <c>CockpitView_Ctor</c>(0, <paramref name="machine"/>), at <c>+0x1b</c>.
	/// </summary>
	/// <param name="machine"><c>+0x25</c>, the player's machine, which the cockpit's <c>R</c> reaches.</param>
	/// <param name="mouse">
	/// The cockpit's mouse, <c>CockpitMouse_Init</c> run on it, as <c>Sim_StartMission</c> runs it before the cockpit is
	/// built; with none, one on <paramref name="display"/>'s cursor with no driver.
	/// </param>
	/// <param name="globals">The simulation's globals, whose live object list the cockpit's target selection walks; with none, an empty world's.</param>
	/// <param name="preferences">The simulator's options, which the cockpit's message boxes read; with none, those of a <c>data\prefs.cfg</c> that does not open.</param>
	public Es1ViewManager(Es1CockpitViews files, Es1VideoMemory display, Es1Mech? machine = null, Es1CockpitMouse? mouse = null,
			Es1SimGlobals? globals = null, Es1Preferences? preferences = null) {
		Files = files;
		_display = display;
		if (mouse == null) {
			mouse = new Es1CockpitMouse(display.Cursor);
			mouse.Init(null);
		}

		Mouse = mouse;
		Cockpit = new Es1Cockpit(this, machine, mouse, globals ?? new Es1SimGlobals(), preferences ?? Es1Preferences.Default);
		ExternalViewRect = ExternalViewDefaultRect;
		ExternalViewClip = Es1ClipBlock.FromRect(ExternalViewRect.ViewportX0, ExternalViewRect.ViewportY0,
			ExternalViewRect.ViewportX1, ExternalViewRect.ViewportY1);
	}

	/// <summary>The <c>.VUE</c> records, the clip blocks and the canopy art.</summary>
	public Es1CockpitViews Files { get; }

	/// <summary>
	/// <c>SimDisplay</c> (<c>000b5b5c</c>), the display the views draw on and the frame's end flips. While this engine
	/// repaints view 0's instruments into the canvas during a glance (<see cref="DrawCanvasPanels"/>), it is the canvas's
	/// memory instead.
	/// </summary>
	public Es1VideoMemory Display => _paintTarget ?? _display;

	private readonly Es1VideoMemory _display;

	// The canvas's memory while view 0's instruments are repainted into it; null otherwise.
	private Es1VideoMemory? _paintTarget;

	/// <summary><c>+0x1b</c>, the cockpit, <c>CockpitViewInstance</c>.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary>
	/// The chain of views built over this manager, whose camera is <c>ViewObjectPtr</c> (<c>000b5b8a</c>), a global in
	/// retail that the cockpit's instruments read; the chain's constructor sets it, so it is none before.
	/// </summary>
	public Es1ViewChain? Chain { get; internal set; }

	/// <summary>The cockpit's mouse, whose root is the cockpit's and which the view changes' cursor steps reach.</summary>
	public Es1CockpitMouse Mouse { get; }

	/// <summary><c>+0x10</c>, the current view.</summary>
	public int CurrentView { get; private set; }

	/// <summary><c>+0x14</c>, the pending view command, −1 for none.</summary>
	public int PendingCommand { get; private set; } = -1;

	/// <summary><c>+0x18</c>, set while a view change is staged; <c>ViewChain_Apply</c> and <c>Sim_RenderFrame</c> read it.</summary>
	public bool Staged { get; private set; }

	/// <summary><c>+0x19</c>, the frames <see cref="ProcessViewCommand"/> passes over after a view change.</summary>
	public short Cooldown { get; private set; }

	/// <summary><c>+0x1f</c>, the view the external view goes back to.</summary>
	public int SavedView { get; private set; }

	/// <summary><c>+0x32</c>, set to ask for the external view's border on the next frame's draw page.</summary>
	public bool BorderPending { get; private set; }

	/// <summary><c>ExternalView_Rect</c> (<c>000afb3c</c>): the external view's rect and projection pair.</summary>
	public Vue.Entry ExternalViewRect { get; private set; }

	/// <summary><c>ExternalView_ClipBlock</c> (<c>000afb94</c>), its one region <c>ExternalView_Rect</c>'s rect.</summary>
	public Es1ClipBlock ExternalViewClip { get; private set; }

	/// <summary>
	/// <c>ExternalView_SetRect</c> (<c>00024a00</c>)(offset, rect), both in <paramref name="rect"/>
	/// (docs/es1/simulation/cockpit-views.md#the-external-view): the rect and the projection pair stored at
	/// <c>ExternalView_Rect</c>, the rect at <c>ExternalView_ClipRegion</c>'s; and in view 4 the six installed in the
	/// render context. A clip block holds its regions by pointer (docs/es1/formats/hd-ed-clip-regions.md), so the clip
	/// block view 4 installed, which points at <c>ExternalView_ClipRegion</c>, clips to the new rect too.
	/// </summary>
	public void SetExternalViewRect(Vue.Entry rect) {
		ExternalViewRect = rect;
		ExternalViewClip = Es1ClipBlock.FromRect(rect.ViewportX0, rect.ViewportY0, rect.ViewportX1, rect.ViewportY1);
		if (CurrentView == Es1CockpitViews.ExternalView) {
			Window = Es1ViewWindow.Install(rect, 0, 0);
			Clip = ExternalViewClip;
		}
	}

	/// <summary><c>ExternalView_ResetRect</c> (<c>00024a7c</c>): <see cref="SetExternalViewRect"/> with <see cref="ExternalViewDefaultRect"/>.</summary>
	public void ResetExternalViewRect() => SetExternalViewRect(ExternalViewDefaultRect);

	/// <summary>The render context's six values <c>+0x210</c>–<c>+0x224</c> as the last view state installed them.</summary>
	public Es1ViewWindow Window { get; internal set; }

	/// <summary>
	/// <b>This engine's own:</b> the view whose window and clip block <see cref="Window"/> and <see cref="Clip"/> hold, the
	/// view the next 3D pass is drawn for. While a view change is staged <see cref="CurrentView"/> is still the old view and
	/// this the new one.
	/// </summary>
	public int InstalledView { get; private set; }

	/// <summary>
	/// <b>This engine's own:</b> the canvas beside the monitor (plan decision 30, <see cref="Es1Canvas"/>), which the frame
	/// fills under the "High resolution widescreen" tweak; none with no host to show it.
	/// </summary>
	public Es1Canvas? Canvas { get; set; }

	/// <summary>
	/// <b>This engine's own:</b> how far the monitor's canvas column may move from view 0's before the window showing the
	/// canvas stops following it (<see cref="Es1CanvasLayout.Reach"/>); a glance's slide steps past it set the page's origin
	/// without waiting for a retrace (<see cref="Glance"/>). None waits at every step, as retail's slide does.
	/// </summary>
	public Func<int>? GlanceReach { get; set; }

	// The view each page was last drawn for, or carried to it by a copy of a whole page; view 0 in the zero-filled manager.
	private readonly int[] _pageViews = new int[Es1VideoMemory.PageCount];

	/// <summary><b>This engine's own:</b> the view page <paramref name="page"/> holds: the last frame drawn on it, or the page a whole-page copy brought onto it.</summary>
	public int PageView(int page) => _pageViews[page];

	/// <summary>
	/// <b>This engine's own:</b> where the monitor's top-left pixel lies on the cockpit's canvas, from view 0's top-left:
	/// the visible page's origin less its own, plus the canvas origin of the view the page holds (<see cref="PageView"/>),
	/// in columns; less its own in rows. So it stays right on the retrace between a glance's last step and the view
	/// change, when the slid page and then the far half are on the monitor while <see cref="CurrentView"/> is still the
	/// old view.
	/// </summary>
	public (int Column, int Row) MonitorCanvasPoint {
		get {
			int page = Display.VisiblePage;
			var (originX, originY) = Display.PageOrigin(page);
			var (baseX, baseY) = Display.PageBase(page);
			int view = _pageViews[page];
			int canvasX = view >= 0 && view < Files.Count ? Files.Record(view).CanvasOriginX : 0;
			return (originX - baseX + canvasX, originY - baseY);
		}
	}

	/// <summary>
	/// <b>This engine's own</b>, at the end of each <c>Sim_RenderFrame</c>: the draw page recorded as holding
	/// <see cref="InstalledView"/>, and under the "High resolution widescreen" tweak, with a canvas, the page copied into
	/// that view's page of it (<see cref="Es1Canvas.TakeDrawnPage"/>).
	/// </summary>
	public void EndRenderFrame() {
		_pageViews[Display.DrawPage] = InstalledView;
		TakeCanvasPage();
	}

	/// <summary>
	/// <b>This engine's own:</b> under the "High resolution widescreen" tweak, with a canvas that is not
	/// <see cref="CanvasHeld"/>, the draw page copied into <see cref="InstalledView"/>'s page of it
	/// (<see cref="Es1Canvas.TakeDrawnPage"/>).
	/// </summary>
	internal void TakeCanvasPage() {
		if (Canvas is { } canvas && WideCanvas && !CanvasHeld) {
			canvas.TakeDrawnPage(Display, InstalledView);
		}
	}

	/// <summary>
	/// <b>This engine's own:</b> whether the window shows <see cref="Canvas"/> beside the monitor, as the host's monitor
	/// lays it out. With none, it is taken as shown.
	/// </summary>
	public Func<bool>? CanvasShown { get; set; }

	/// <summary>
	/// <b>This engine's own:</b> what a 2D draw over the frame's 3D pass does to the side panels as well
	/// (<see cref="Es1Canvas.DrawSidePanels"/>), so the canvas shows it as the view's own window does; none outside one.
	/// </summary>
	internal IEs1CanvasOverlay? CanvasOverlay { get; set; }

	/// <summary>
	/// <b>This engine's own:</b> set while the frame drawn is not yet shown, or never will be. The canvas then keeps the
	/// last frame shown, as the monitor does: <see cref="EndRenderFrame"/> copies nothing into it and
	/// <see cref="DrawCanvasPanels"/> draws nothing.
	/// </summary>
	internal bool CanvasHeld { get; set; }

	/// <summary>
	/// <b>This engine's own</b>, after <see cref="EndRenderFrame"/> in a frame whose 3D pass ran: draws the side panels
	/// into the canvas (<see cref="Es1Canvas.DrawSidePanels"/>). This needs the "High resolution widescreen" tweak, a
	/// canvas, and a window that shows the canvas (<see cref="CanvasShown"/>).
	///
	/// <para>In a glance it then repaints view 0's instruments over view 0's page (<see cref="Es1Cockpit.RepaintForward"/>),
	/// as plan decision 30 keeps view 0 live. For the repaint, view 0's state is put in place: <see cref="Display"/> is the
	/// canvas's memory on view 0's page, and <see cref="Window"/>, <see cref="Clip"/> and <see cref="Projection"/> are view
	/// 0's. The cockpit's canvas origin is view 0's: the glance's move and the shake's total are taken back out. Everything
	/// is put back after.</para>
	/// </summary>
	internal void DrawCanvasPanels(Action? beginWorldPass) {
		if (Canvas is not { } canvas || !WideCanvas || CanvasHeld || CanvasShown?.Invoke() == false) {
			return;
		}

		canvas.DrawSidePanels(this, beginWorldPass);
		if (InstalledView is not (2 or 3) || Files.Count == 0) {
			return;
		}

		var forward = Files.Record(0);
		var glance = Files.Record(InstalledView);
		var (window, clip, projection) = (Window, Clip, Projection);
		var origin = Cockpit.CanvasOrigin;
		Window = Es1ViewWindow.Install(forward, forward.CanvasOriginX, forward.CanvasOriginY);
		Clip = Files.Clip(0);
		if (Chain?.Camera is { } camera) {
			InstallViewProjection(camera);
		}

		canvas.BeginForwardPaint();
		_paintTarget = canvas.Memory;
		try {
			Cockpit.RepaintForward((origin.X - (glance.CanvasOriginX - forward.CanvasOriginX),
				origin.Y - ShakeTotal - (glance.CanvasOriginY - forward.CanvasOriginY)));
		} finally {
			_paintTarget = null;
			(Window, Clip, Projection) = (window, clip, projection);
		}
	}

	private static bool WideCanvas => TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.HighResolutionWidescreen);

	/// <summary>The render context's clip block as the last view state copied it in, clip mode 2.</summary>
	public Es1ClipBlock Clip { get; private set; } = Es1ClipBlock.Empty;

	/// <summary>
	/// The installed view, <c>Raster_InstallViewProjection</c>'s, which the 3D pass installs for <c>ViewObjectPtr</c> through
	/// <see cref="Window"/> and which stands until the next pass installs it again; null before the first pass.
	/// </summary>
	public Es1ViewProjection? Projection { get; private set; }

	/// <summary><c>Raster_InstallViewProjection</c>(<paramref name="camera"/>) through the render context's <see cref="Window"/>.</summary>
	public void InstallViewProjection(Es1Camera camera) => Projection = Es1ViewProjection.Install(camera, Window);

	/// <summary>The render context <c>000b5b6e</c> as the 2D draws over the scene find it: <see cref="Clip"/> on the display's draw page.</summary>
	public Es1SceneCanvas SceneCanvas() => new(Display, Clip);

	/// <summary><c>CockpitViewCanvasOrigin</c> (<c>000afa9c</c>), the current view's canvas origin.</summary>
	public (int X, int Y) CanvasOrigin { get; private set; }

	/// <summary><c>CockpitViewShakeArmed</c> (<c>000925ec</c>), which <see cref="SetShakeBand"/> sets and <see cref="ClearShake"/> clears.</summary>
	public bool ShakeArmed { get; private set; }

	/// <summary><c>CockpitShakeReseedAfterSlide</c> (<c>000925ed</c>), set when a view change cleared an armed shake.</summary>
	public bool ShakeReseedAfterSlide { get; private set; }

	/// <summary><c>CockpitShakeTotal</c> (<c>000afb6c</c>), the sum of the shake's steps (<see cref="StepShake"/>).</summary>
	public short ShakeTotal { get; private set; }

	/// <summary><c>CockpitView_GetShakeOffset</c> (<c>000249d4</c>): {0, <see cref="ShakeTotal"/>} (docs/es1/simulation/cockpit-views.md#the-view-shake).</summary>
	public (int X, int Y) ShakeOffset => (0, ShakeTotal);

	/// <summary><c>CockpitShakeRestOffset</c> (<c>000afb54</c>), the pages' resting offset, and <c>CockpitShakeOffset</c> (<c>000afb5c</c>), the moving one.</summary>
	public (int X, int Y) ShakeRestOffset { get; private set; }

	/// <inheritdoc cref="ShakeRestOffset"/>
	public (int X, int Y) ShakeMovingOffset { get; private set; }

	/// <summary><c>CockpitShakeBand</c> (<c>000afb64</c>), the two rows the moving offset's <i>y</i> stays between.</summary>
	public (int Low, int High) ShakeBand { get; private set; }

	/// <summary><c>CockpitShakeStripRegion</c>'s (<c>000afb78</c>) rect, rows past the screen's foot that a glance's shake fills, bounds included.</summary>
	public const int StripX0 = 0, StripY0 = 200, StripX1 = 320, StripY1 = 210;

	// CockpitShakeStripPixels (000afb90), the strip as SetShakeBand saved it; null before it is first saved.
	private ushort[]? _stripPixels;

	// The retrace waits the shake's page moves made where they could not yield, which the frame's end and the view
	// command yield in turn.
	private int _owedRetraces;

	/// <summary>
	/// <c>SimInput_SetEnabled</c> (<c>0004d82c</c>), which the pans call after their slides: the keyboard's axis keys
	/// (<see cref="Input.Es1AxisKeys.SetEnabled"/>), once the host has built the input.
	/// </summary>
	public Action<bool>? SetSimInputEnabled { get; set; }

	/// <summary>
	/// <c>CockpitView_ApplyViewState</c> (<c>000222c0</c>): nothing for the current view or one above 4. An armed shake is
	/// cleared and reseeded after the slide. View 4 saves the current view, installs the external view's clip block and
	/// rect, becomes current, zeroes the canvas origin and paints the border. Any other view moves the draw page's origin
	/// to its page's plus the view's canvas origin when leaving view 0 or 1 for a view other than 2 and 3, moves the
	/// cockpit (<see cref="Es1Cockpit.TranslateForView"/>) and the pilot channel's box
	/// (<see cref="Messages.Es1MessagePort.ShiftRect"/>) by the same delta when leaving a glance or view 0 or 1, and
	/// installs the view's clip block and record, the canvas origin added to the projection pair.
	/// </summary>
	public void ApplyViewState(int view) {
		// Retail indexes the records with any view below 5; no caller passes one below 0.
		if (view == CurrentView || view >= 5 || view < 0) {
			return;
		}

		if (ShakeArmed) {
			ClearShake();
			ShakeReseedAfterSlide = true;
		}

		if (view == Es1CockpitViews.ExternalView) {
			ShakeReseedAfterSlide = false;
			SavedView = CurrentView;
			Clip = ExternalViewClip;
			Window = Es1ViewWindow.Install(ExternalViewRect, 0, 0);
			InstalledView = Es1CockpitViews.ExternalView;
			CurrentView = Es1CockpitViews.ExternalView;
			Cockpit.DisableWidgets();
			CanvasOrigin = (0, 0);
			FillOutsideExternalRect();
			return;
		}

		bool leavingForward = true;
		if (CurrentView == Es1CockpitViews.ExternalView) {
			leavingForward = false;
			Cockpit.EnableWidgets();
		} else if (CurrentView is 2 or 3) {
			leavingForward = false;
			var glance = Files.Record(CurrentView);
			Cockpit.TranslateForView(glance.CanvasOriginX, glance.CanvasOriginY);
			Cockpit.PilotChannel.ShiftRect(glance.CanvasOriginX, glance.CanvasOriginY);
		}

		var record = Files.Record(view);
		if (leavingForward) {
			if (view is not (2 or 3)) {
				int page = Display.DrawPage;
				var origin = Display.PageBase(page);
				Display.SetPageOrigin(page, origin.X + record.CanvasOriginX, origin.Y + record.CanvasOriginY);
			}

			// The old view's canvas origin less the new one's; the current view is never -1 here, the manager being zero-filled.
			var old = Files.Record(CurrentView);
			Cockpit.TranslateForView(old.CanvasOriginX - record.CanvasOriginX, old.CanvasOriginY - record.CanvasOriginY);
			Cockpit.PilotChannel.ShiftRect(old.CanvasOriginX - record.CanvasOriginX, old.CanvasOriginY - record.CanvasOriginY);
		}

		Clip = Files.Clip(view);
		Window = Es1ViewWindow.Install(record, record.CanvasOriginX, record.CanvasOriginY);
		InstalledView = view;
		CanvasOrigin = (record.CanvasOriginX, record.CanvasOriginY);
	}

	/// <summary>
	/// <c>CockpitView_SetView</c> (<c>00022558</c>), for a view below 5: the view state, then for any view but 4 frame 0
	/// of the view's canopy art blitted at (0, 0) on the draw page with clip mode 0, mirrored for view 3; and the view
	/// made current.
	/// </summary>
	public void SetView(int view) {
		if (view >= 5) {
			return;
		}

		ApplyViewState(view);
		if (view != Es1CockpitViews.ExternalView) {
			// g_LowMemory's load and free of the art around the blit: -l is not taken, and the art stays loaded.
			if (view >= 0 && view < Files.Count && Files.Canopy(view) is { } art) {
				Display.Blit(art, mirrored: view == 3);
			}

			Cockpit.InvalidateWidgets();
		}

		CurrentView = view;
	}

	/// <summary>
	/// <c>CockpitViewManager_QueueViewCommand</c> (<c>00022634</c>): while no command is pending, latches one the current
	/// view allows: 0 from view 0, 1 from view 1, 2 from any view but 4, 3 from view 4; 4 from view 0, and as 6 from view
	/// 3; 5 from view 0, and as 6 from view 2; 6 from views 2 and 3.
	/// </summary>
	internal void QueueViewCommand(int command) {
		if (PendingCommand != -1) {
			return;
		}

		int? latched = (command, CurrentView) switch {
			(0, 0) => 0,
			(1, 1) => 1,
			(2, not 4) => 2,
			(3, 4) => 3,
			(4, 0) => 4,
			(4, 3) => 6,
			(5, 0) => 5,
			(5, 2) => 6,
			(6, 2 or 3) => 6,
			_ => null,
		};
		if (latched is { } value) {
			PendingCommand = value;
		}
	}

	/// <summary>
	/// <c>CockpitView_ProcessViewCommand</c> (<c>000226f0</c>), once a frame from <c>Sim_EndFrame</c> after the flip: the
	/// border when asked for; the cooldown; the page gate, which holds a glance until the page on the monitor,
	/// <c>+0x88</c>, is the half it slides from; then the wait for a vertical retrace, yielded as
	/// <see cref="Es1Retrace.VerticalRetrace"/>, <c>Display_FlipWithoutPan</c> cleared and, while nothing is staged, the
	/// pending command staged (docs/es1/simulation/cockpit-views.md#cockpitview_processviewcommand-000226f0), a pan first
	/// lifting the pilot channel's line off the draw page (<see cref="Messages.Es1PilotChannel.RestoreBackground"/>).
	/// </summary>
	internal IEnumerable<Es1Retrace> ProcessViewCommand() {
		if (BorderPending) {
			FlushBorderFill();
		}

		if (Cooldown != 0) {
			Cooldown--;
			yield break;
		}

		int shown = Display.PairVisible;
		bool held = PendingCommand switch {
			4 => shown != 0,
			5 => shown != 2,
			6 when CurrentView == 3 => shown != 0,
			6 when CurrentView == 2 => shown != 2,
			_ => false,
		};
		if (held) {
			yield break;
		}

		yield return Es1Retrace.VerticalRetrace;
		Display.FlipWithoutPan = false;
		if (Staged) {
			yield break;
		}

		int current = CurrentView;
		switch (PendingCommand) {
			case 0:
			case 1:
				if (Cockpit.PilotChannel.LineUp) {
					Cockpit.PilotChannel.RestoreBackground();
				}

				ApplyViewState(PendingCommand == 0 ? current + 1 : current - 1);
				Staged = true;
				break;
			case 2:
				ApplyViewState(Es1CockpitViews.ExternalView);
				Staged = true;
				break;
			case 3:
				SetView(SavedView);
				CurrentView = current;
				Staged = true;
				break;
			case 4:
			case 5:
			case 6:
				SetView(PendingCommand switch { 4 => 2, 5 => 3, _ => 0 });
				CurrentView = current;
				if (PendingCommand == 6) {
					Cockpit.EnableWidgets();
				}

				// The (0, 0, 0) View_GetActiveObject's angles +0x44 take changes nothing: a glance keeps the forward camera.
				Staged = true;
				break;
		}

		// An armed shake the view state cleared moved the page on the monitor.
		foreach (var wait in TakeOwedRetraces()) {
			yield return wait;
		}
	}

	/// <summary>
	/// <c>CockpitView_StepViewTransition</c> (<c>00022904</c>), once a frame from <c>Sim_EndFrame</c> before anything
	/// else: runs the staged command, yielding a retrace for each step of its slide
	/// (docs/es1/simulation/cockpit-views.md#cockpitview_stepviewtransition-00022904). The first half copies the outgoing
	/// picture across the pages for commands 0, 1 and 3, every command but 2 first painting the ticker
	/// (<see cref="Messages.Es1MessageTicker.Paint"/>); the slide moves the monitor's page, <c>+0x88</c>, by the
	/// <c>.VUE</c> records' canvas origins, 5 rows or 10 columns a step and the rest of the way in a last one, and while
	/// the pilot channel has a line up it draws on that page and lifts the line around each step
	/// (<see cref="SlideStep"/>, <see cref="EndLift"/>); the after column sets the current view, command 3 painting the
	/// pilot channel's box again, and a glance ends with the pair swapped and the far half copied back.
	/// </summary>
	internal IEnumerable<Es1Retrace> StepViewTransition() {
		if (PendingCommand == -1 || !Staged) {
			yield break;
		}

		var display = Display;
		int shown = display.PairVisible;
		int drawn = display.PairDrawn;
		display.FlipWithoutPan = false;

		int command = PendingCommand;
		int dx = 0, dy = 0;
		(int X0, int Y0, int X1, int Y1)? copy = null;
		switch (command) {
			case 0: {
				if (CurrentView == 1) {
					// Retail returns here with the command still pending; QueueViewCommand latches 0 only from view 0.
					yield break;
				}

				(dx, dy) = OriginDelta(CurrentView + 1, CurrentView);
				copy = (-dx, 200 - dy, 319, 199);
				break;
			}
			case 1: {
				if (CurrentView == 0) {
					yield break;
				}

				(dx, dy) = OriginDelta(CurrentView - 1, CurrentView);
				copy = (0, 0, 319 - dx, -dy);
				break;
			}
			case 3:
				// The rect reaches column 320, one past the page, as retail's does.
				copy = (0, 0, 320, 199);
				break;
		}

		var cursor = Display.Cursor;
		if (command == 2) {
			cursor.Hide();
			Mouse.FollowsMouse = false;
		} else {
			if (command == 3) {
				Mouse.FollowsMouse = true;
			}

			Cockpit.Ticker.Paint();
		}

		if (copy is { } rect) {
			// The display's clip block's clip mode and translation are 0 for the copy.
			display.SourcePage = drawn;
			display.DrawPage = shown;
			cursor.Hide();
			display.CopyRect(rect.X0, rect.Y0, rect.X1, rect.Y1, dx, dy);
			cursor.Show();
			display.DrawPage = drawn;
			// Command 3's copy is the whole page, which brings the drawn page's view onto the shown one.
			if (command == 3) {
				_pageViews[shown] = _pageViews[drawn];
			}
		}

		Staged = false;
		// VGA8_ShowPageIdle (00098d96) is 0 during a slide, holding the pointer's moves, and 1 after.
		cursor.BeginPageChange();
		var page = display.PageBase(shown);
		var channel = Cockpit.PilotChannel;
		bool lifted = command is not (2 or 3) && channel.LineUp;
		if (lifted) {
			display.SourcePage = shown;
			display.DrawPage = shown;
		}

		switch (command) {
			case 0: {
				int y = 0, moved = 0;
				short travel = (short)dy;
				if (travel > 0) {
					do {
						y += PanStep;
						if (SlideStep(lifted, shown, page.X, page.Y + y)) {
							yield return default;
						}

						moved += PanStep;
					} while (moved < travel);
				}

				y += travel - moved;
				if (SlideStep(lifted, shown, page.X, page.Y + y)) {
					yield return default;
				}

				EndLift(lifted, drawn);
				cursor.EndPageChange();
				Cockpit.Cursors.UseOffForwardCursor();
				CurrentView++;
				SetSimInputEnabled?.Invoke(false);
				break;
			}
			case 1: {
				short travel = (short)-dy;
				int y = travel, left = travel;
				while (PanStep <= left) {
					y -= PanStep;
					if (SlideStep(lifted, shown, page.X, page.Y + y)) {
						yield return default;
					}

					left -= PanStep;
				}

				y -= left;
				if (SlideStep(lifted, shown, page.X, page.Y + y)) {
					yield return default;
				}

				EndLift(lifted, drawn);
				cursor.EndPageChange();
				Cockpit.Cursors.UseForwardCursor();
				CurrentView--;
				SetSimInputEnabled?.Invoke(true);
				break;
			}
			case 2:
				cursor.EndPageChange();
				CurrentView = Es1CockpitViews.ExternalView;
				BorderPending = true;
				break;
			case 3: {
				cursor.EndPageChange();
				if (Mouse.Driver is { } driver) {
					var (x, y) = cursor.GetPosition();
					driver.SetPosition(x, y);
				}

				cursor.Show();
				channel.Paint();
				CurrentView = SavedView;
				break;
			}
			case 4:
			case 5:
			case 6: {
				foreach (var wait in Glance(command, page, lifted)) {
					yield return wait;
				}

				break;
			}
		}

		PendingCommand = -1;
		Cooldown = 2;
		if (ShakeReseedAfterSlide) {
			SetShakeBand(10);
		}
	}

	/// <summary>
	/// <c>CockpitViewManager_FlushBorderFill</c> (<c>00024af8</c>): the border on the draw page in view 4, and the ask
	/// cleared.
	/// </summary>
	public void FlushBorderFill() {
		if (CurrentView == Es1CockpitViews.ExternalView) {
			FillOutsideExternalRect();
		}

		BorderPending = false;
	}

	/// <summary>
	/// <c>View_FillOutside3dRect</c> (<c>00024b18</c>): with the brush {0, <see cref="BorderColor"/>} and the clip mode and
	/// translation 0, the four bands of the 320 × 200 screen around <see cref="ExternalViewRect"/>, each when not empty.
	/// </summary>
	public void FillOutsideExternalRect() {
		var rect = ExternalViewRect;
		int left = rect.ViewportX0, top = rect.ViewportY0, right = rect.ViewportX1, bottom = rect.ViewportY1;
		if (top - 1 >= 0) {
			Display.FillRect(0, 0, 319, top - 1, BorderColor);
		}

		if (bottom + 1 <= 199) {
			Display.FillRect(0, bottom + 1, 319, 199, BorderColor);
		}

		if (0 <= left - 1) {
			Display.FillRect(0, top, left - 1, bottom, BorderColor);
		}

		if (right + 1 <= 319) {
			Display.FillRect(right + 1, top, 319, bottom, BorderColor);
		}
	}

	/// <summary>
	/// A glance's slide and its after column: commands 4 and 5 slide the monitor's page 10 columns a step by record 2's
	/// <i>x</i> less record 0's, and by record 0's plus record 3's the other way; command 6 slides back by record 0's
	/// plus the current view's, toward the side it came from. Then the far half's page is shown, the near one drawn on,
	/// the pair swapped, the slid page put back on its own origin and the far half copied onto it.
	///
	/// <para><b>This engine's own</b> (plan decision 30, KNOWN_ISSUES.md): a step that leaves the monitor's canvas column
	/// (<see cref="MonitorCanvasPoint"/>) further than <see cref="GlanceReach"/> from view 0's does not wait for its
	/// retrace, so a window that already shows part of the side panel slides only until the panel's outer edge meets the
	/// window's edge, as ES2's port does, and the steps beyond happen at once. The retrace wait after the slide, the far
	/// half's show, is kept.</para>
	/// </summary>
	private IEnumerable<Es1Retrace> Glance(int command, (int X, int Y) page, bool lifted) {
		var display = Display;
		int shown = display.PairVisible;
		int drawn = display.PairDrawn;
		var record0 = Files.Record(0);
		int x = 0;
		switch (command) {
			case 4: {
				short travel = (short)-(record0.CanvasOriginX - Files.Record(2).CanvasOriginX);
				int moved = 0;
				if (0 < travel) {
					do {
						x += GlanceStep;
						if (GlanceSlideStep(lifted, shown, page.X + x, page.Y)) {
							yield return default;
						}

						moved += GlanceStep;
					} while (moved < travel);
				}

				x += travel - moved;
				break;
			}
			case 5: {
				short travel = (short)-(record0.CanvasOriginX + Files.Record(3).CanvasOriginX);
				int moved = 0;
				if (0 < travel) {
					do {
						x -= GlanceStep;
						if (GlanceSlideStep(lifted, shown, page.X + x, page.Y)) {
							yield return default;
						}

						moved += GlanceStep;
					} while (moved < travel);
				}

				// Retail adds what is left where its steps take away; every retail record leaves nothing.
				x += travel - moved;
				break;
			}
			default: {
				short sum = (short)(record0.CanvasOriginX + Files.Record(CurrentView).CanvasOriginX);
				int step, left, limit;
				if (CurrentView == 2) {
					step = GlanceStep;
					left = sum;
					limit = 0;
				} else {
					step = -GlanceStep;
					left = 0;
					limit = sum;
				}

				while (limit < left) {
					x -= step;
					if (GlanceSlideStep(lifted, shown, page.X + x, page.Y)) {
						yield return default;
					}

					left -= GlanceStep;
				}

				x -= left - limit;
				break;
			}
		}

		if (GlanceSlideStep(lifted, shown, page.X + x, page.Y)) {
			yield return default;
		}

		EndLift(lifted, drawn);
		var cursor = Display.Cursor;
		cursor.EndPageChange();
		Cockpit.Cursors.UseOffForwardCursor();
		cursor.BeginPageChange();
		if (display.ShowPage(drawn)) {
			yield return default;
		}

		cursor.EndPageChange();

		display.DrawPage = shown;
		(display.PairVisible, display.PairDrawn) = (display.PairDrawn, display.PairVisible);
		display.SetPageOrigin(shown, page.X, page.Y);
		display.SourcePage = drawn;
		display.DrawPage = shown;
		// The clip mode is 0 for the copy, and the cursor hidden around it.
		cursor.Hide();
		display.CopyRect(0, 0, 319, 199, 0, 0);
		cursor.Show();
		_pageViews[shown] = _pageViews[drawn];
		CurrentView = command switch { 4 => 2, 5 => 3, _ => 0 };
	}

	/// <summary>
	/// One set of the sliding page's origin, <c>VGA8_SetPageOrigin</c> through the driver's slot 15; answers whether it
	/// waited for a retrace. While the pilot channel's line is <paramref name="lifted"/>, its screen is put back
	/// (<see cref="Messages.Es1PilotChannel.RestoreBackground"/>) before the set and the box painted again
	/// (<see cref="Messages.Es1PilotChannel.Paint"/>) after, so the line rides along on the sliding page.
	/// </summary>
	private bool SlideStep(bool lifted, int page, int x, int y) {
		var channel = Cockpit.PilotChannel;
		if (lifted) {
			channel.RestoreBackground();
		}

		bool waited = Display.SetPageOrigin(page, x, y);
		if (lifted) {
			channel.Paint();
		}

		return waited;
	}

	/// <summary>
	/// A glance's <see cref="SlideStep"/>, answering a wait only while the step leaves the monitor's canvas column within
	/// <see cref="GlanceReach"/> of view 0's.
	/// </summary>
	private bool GlanceSlideStep(bool lifted, int page, int x, int y) =>
		SlideStep(lifted, page, x, y) && (GlanceReach is not { } reach || Math.Abs(MonitorCanvasPoint.Column) <= reach());

	/// <summary>
	/// A lifted slide's end: the draw page put back to <paramref name="drawn"/>, <c>+0x8c</c>, and the pilot channel's box
	/// copied back from the page on the screen (<c>MessagePort_CopyBoxBetweenPages</c>, 0).
	/// </summary>
	private void EndLift(bool lifted, int drawn) {
		if (lifted) {
			Display.DrawPage = drawn;
			Cockpit.PilotChannel.CopyBoxBetweenPages(0);
		}
	}

	/// <summary>Record <paramref name="to"/>'s canvas origin less record <paramref name="from"/>'s.</summary>
	private (int X, int Y) OriginDelta(int to, int from) {
		var a = Files.Record(to);
		var b = Files.Record(from);
		return (a.CanvasOriginX - b.CanvasOriginX, a.CanvasOriginY - b.CanvasOriginY);
	}

	/// <summary>
	/// <c>CockpitView_ClearShake</c> (<c>00024750</c>, docs/es1/simulation/cockpit-views.md#the-view-shake): while armed and
	/// not in view 4, both pages' origins, <c>+0x8c</c>'s then <c>+0x88</c>'s, put back on their own plus
	/// <see cref="ShakeRestOffset"/>; the current view's clip regions moved back down by <see cref="ShakeTotal"/>; in views
	/// 2 and 3 the strip put back on the draw page and on <c>+0x88</c>, <c>+0x8c</c> left the draw page; the cockpit
	/// moved by (0, total) (<see cref="Es1Cockpit.TranslateForView"/>); the total and the armed byte cleared.
	/// </summary>
	internal void ClearShake() {
		int view = CurrentView;
		if (!ShakeArmed || view == Es1CockpitViews.ExternalView) {
			return;
		}

		MovePages(ShakeRestOffset);
		Files.Clip(view).MoveRows(ShakeTotal);
		if (view is 2 or 3 && _stripPixels is { } strip) {
			Display.WriteRect(StripX0, StripY0, StripX1, StripY1, strip);
			Display.DrawPage = Display.PairVisible;
			Display.WriteRect(StripX0, StripY0, StripX1, StripY1, strip);
			Display.DrawPage = Display.PairDrawn;
		}

		Cockpit.TranslateForView(0, ShakeTotal);
		ShakeTotal = 0;
		ShakeArmed = false;
	}

	/// <summary>
	/// <c>CockpitView_SetShakeBand</c> (<c>000243d4</c>)(<paramref name="amplitude"/>), nothing in view 4 or while a change
	/// is staged: both offsets the canvas origin; the band [<i>y</i> − amplitude, <i>y</i>] in view 1 and [<i>y</i>,
	/// <i>y</i> + amplitude] in the others. In views 2 and 3 both offsets' <i>x</i> is 0 and the strip is saved
	/// (<c>Region_Save</c>, from the source page) and filled with <see cref="BorderColor"/> on the draw page and on
	/// <c>+0x88</c>, <c>+0x8c</c> left the draw page, with the clip mode and translation at 0. The total zeroed and the
	/// shake armed.
	/// </summary>
	internal void SetShakeBand(int amplitude) {
		if (CurrentView == Es1CockpitViews.ExternalView || Staged) {
			return;
		}

		ShakeRestOffset = CanvasOrigin;
		ShakeMovingOffset = CanvasOrigin;
		int y = CanvasOrigin.Y;
		if (CurrentView == 1) {
			ShakeBand = (y - amplitude, y);
		} else {
			if (CurrentView is 2 or 3) {
				ShakeRestOffset = (0, ShakeRestOffset.Y);
				ShakeMovingOffset = (0, ShakeMovingOffset.Y);
				_stripPixels = Display.ReadRect(StripX0, StripY0, StripX1, StripY1);
				Display.FillRect(StripX0, StripY0, StripX1, StripY1, BorderColor);
				Display.DrawPage = Display.PairVisible;
				Display.FillRect(StripX0, StripY0, StripX1, StripY1, BorderColor);
				Display.DrawPage = Display.PairDrawn;
			}

			ShakeBand = (y, y + amplitude);
		}

		ShakeTotal = 0;
		ShakeArmed = true;
	}

	/// <summary>
	/// <c>CockpitView_StepShake</c> (<c>00024564</c>)(<paramref name="step"/>), while armed and not in view 4: the moving
	/// offset's <i>y</i> moved by up to the step toward the farther of the band's two rows, the low one when both are as
	/// far; both pages' origins, <c>+0x8c</c>'s then <c>+0x88</c>'s, set to their own plus the moving offset,
	/// which scrolls the screen; the current view's clip regions moved up by the move
	/// (<see cref="Es1ClipBlock.MoveRows"/>); the move added to <see cref="ShakeTotal"/>; and the cockpit moved by (0,
	/// −move) (<see cref="Es1Cockpit.TranslateForView"/>).
	/// </summary>
	internal void StepShake(int step) {
		if (!ShakeArmed || CurrentView == Es1CockpitViews.ExternalView) {
			return;
		}

		var offset = ShakeMovingOffset;
		short below = (short)(offset.Y - ShakeBand.Low), above = (short)(ShakeBand.High - offset.Y);
		short move = below < above
			? step < above ? (short)step : above
			: (short)-(step < below ? step : below);
		ShakeMovingOffset = (offset.X, offset.Y + move);
		MovePages(ShakeMovingOffset);
		Files.Clip(CurrentView).MoveRows(-move);
		ShakeTotal += move;
		Cockpit.TranslateForView(0, -move);
	}

	/// <summary>
	/// The pages <c>+0x8c</c> and <c>+0x88</c>, in that order, set at their own origins plus <paramref name="offset"/>
	/// through the driver's slot 15, a wait for the monitor owed for each set that made one.
	/// </summary>
	private void MovePages((int X, int Y) offset) {
		foreach (int page in (ReadOnlySpan<int>)[Display.PairDrawn, Display.PairVisible]) {
			var origin = Display.PageBase(page);
			if (Display.SetPageOrigin(page, origin.X + offset.X, origin.Y + offset.Y)) {
				_owedRetraces++;
			}
		}
	}

	/// <summary>
	/// The waits for the monitor the shake's page moves owe, each <c>VGA8_ShowPage</c>'s, yielded where the frame can
	/// stand: retail waits inside the move, which runs within the cockpit's draw and the view state.
	/// </summary>
	internal IEnumerable<Es1Retrace> TakeOwedRetraces() {
		while (_owedRetraces > 0) {
			_owedRetraces--;
			yield return default;
		}
	}

	/// <summary>
	/// The projection pair, the render context's <c>+0x220</c>/<c>+0x224</c>, moved by (<paramref name="dx"/>,
	/// <paramref name="dy"/>): <see cref="Window"/>'s centre, which is its rect's corner less the pair, moved back by it.
	/// <c>Sim_RenderFrame</c> adds the shake's offset across the frame's draws and takes it out after.
	/// </summary>
	internal void MoveProjectionPair(int dx, int dy) =>
		Window = Window with { CentreX = Window.CentreX - dx, CentreY = Window.CentreY - dy };
}
