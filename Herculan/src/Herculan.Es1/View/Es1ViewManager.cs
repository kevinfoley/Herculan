using HercWorks.Core.Data.File.Dbsim;
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
/// The cockpit view manager, <c>mechview.cpp</c> (docs/es1/simulation/cockpit-views.md): the manager's state
/// <c>CockpitViewManager_Ctor</c> (<c>ES1DBSIM 00021dd4</c>) sets up, the render context's window and clip block the
/// current view installs, and the view changes on the display's pages: <c>CockpitView_ApplyViewState</c>,
/// <c>CockpitView_SetView</c>, <c>CockpitViewManager_QueueViewCommand</c>, <c>CockpitView_ProcessViewCommand</c> and
/// <c>CockpitView_StepViewTransition</c>, with the external view's rect and border.
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
		Display = display;
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

	/// <summary><c>SimDisplay</c> (<c>000b5b5c</c>), the display the views draw on and the frame's end flips.</summary>
	public Es1VideoMemory Display { get; }

	/// <summary><c>+0x1b</c>, the cockpit, <c>CockpitViewInstance</c>.</summary>
	public Es1Cockpit Cockpit { get; }

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
	public Es1ViewWindow Window { get; private set; }

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

	/// <summary><c>CockpitViewShakeArmed</c> (<c>000925ec</c>). PLACEHOLDER: never set until plan step 7's hit shake arms it.</summary>
	public bool ShakeArmed { get; private set; }

	/// <summary><c>CockpitShakeReseedAfterSlide</c> (<c>000925ed</c>), set when a view change cleared an armed shake.</summary>
	public bool ShakeReseedAfterSlide { get; private set; }

	/// <summary>
	/// <c>SimInput_SetEnabled</c> (<c>0004d82c</c>), which the pans call after their slides: the keyboard's axis keys
	/// (<see cref="Input.Es1AxisKeys.SetEnabled"/>), once the host has built the input.
	/// </summary>
	public Action<bool>? SetSimInputEnabled { get; set; }

	/// <summary>
	/// <c>CockpitView_ApplyViewState</c> (<c>000222c0</c>): nothing for the current view or one above 4. An armed shake is
	/// cleared and reseeded after the slide. View 4 saves the current view, installs the external view's clip block and
	/// rect, becomes current, zeroes the canvas origin and paints the border. Any other view moves the draw page's origin
	/// to its page's plus the view's canvas origin when leaving view 0 or 1 for a view other than 2 and 3, and installs
	/// the view's clip block and record, the canvas origin added to the projection pair.
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
			// PLACEHOLDER: MessagePort_ShiftRect (0002eea0) on the squad message port by the same, plan step 6c's ports.
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
			// PLACEHOLDER: MessagePort_ShiftRect (0002eea0) on the squad message port by the same, plan step 6c's ports.
		}

		Clip = Files.Clip(view);
		Window = Es1ViewWindow.Install(record, record.CanvasOriginX, record.CanvasOriginY);
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
	/// pending command staged (docs/es1/simulation/cockpit-views.md#cockpitview_processviewcommand-000226f0).
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
				// PLACEHOLDER: with the squad message port's line up (its byte +0x436), the port's slot +0x14, plan step 6.
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
	}

	/// <summary>
	/// <c>CockpitView_StepViewTransition</c> (<c>00022904</c>), once a frame from <c>Sim_EndFrame</c> before anything
	/// else: runs the staged command, yielding a retrace for each step of its slide
	/// (docs/es1/simulation/cockpit-views.md#cockpitview_stepviewtransition-00022904). The first half copies the outgoing
	/// picture across the pages for commands 0, 1 and 3; the slide moves the monitor's page, <c>+0x88</c>, by the
	/// <c>.VUE</c> records' canvas origins, 5 rows or 10 columns a step and the rest of the way in a last one; the after
	/// column sets the current view, and a glance ends with the pair swapped and the far half copied back.
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

		// PLACEHOLDER: every command but 2 calls slot +0x18 of the cockpit's +0x207 object, the ticker, plan step 6c.
		var cursor = Display.Cursor;
		if (command == 2) {
			cursor.Hide();
			Mouse.FollowsMouse = false;
		} else if (command == 3) {
			Mouse.FollowsMouse = true;
		}

		if (copy is { } rect) {
			// The display's clip block's clip mode and translation are 0 for the copy.
			display.SourcePage = drawn;
			display.DrawPage = shown;
			cursor.Hide();
			display.CopyRect(rect.X0, rect.Y0, rect.X1, rect.Y1, dx, dy);
			cursor.Show();
			display.DrawPage = drawn;
		}

		Staged = false;
		// PLACEHOLDER: while the squad message port has a line up (its byte +0x436), a slide sets the source and draw
		// pages to +0x88, calls the port's slots +0x14 and +0x18 around each step and once more round the last, then
		// MessagePort_CopyBoxBetweenPages (0002fcd4)(port, 0): the port, plan step 6c.
		// VGA8_ShowPageIdle (00098d96) is 0 during a slide, holding the pointer's moves, and 1 after.
		cursor.BeginPageChange();
		var page = display.PageBase(shown);
		switch (command) {
			case 0: {
				int y = 0, moved = 0;
				short travel = (short)dy;
				if (travel > 0) {
					do {
						y += PanStep;
						if (display.SetPageOrigin(shown, page.X, page.Y + y)) {
							yield return default;
						}

						moved += PanStep;
					} while (moved < travel);
				}

				y += travel - moved;
				if (display.SetPageOrigin(shown, page.X, page.Y + y)) {
					yield return default;
				}

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
					if (display.SetPageOrigin(shown, page.X, page.Y + y)) {
						yield return default;
					}

					left -= PanStep;
				}

				y -= left;
				if (display.SetPageOrigin(shown, page.X, page.Y + y)) {
					yield return default;
				}

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
				// PLACEHOLDER: the squad message port's slot +0x18, plan step 6c's ports.
				CurrentView = SavedView;
				break;
			}
			case 4:
			case 5:
			case 6: {
				foreach (var wait in Glance(command, page)) {
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
	/// </summary>
	private IEnumerable<Es1Retrace> Glance(int command, (int X, int Y) page) {
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
						if (display.SetPageOrigin(shown, page.X + x, page.Y)) {
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
						if (display.SetPageOrigin(shown, page.X + x, page.Y)) {
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
					if (display.SetPageOrigin(shown, page.X + x, page.Y)) {
						yield return default;
					}

					left -= GlanceStep;
				}

				x -= left - limit;
				break;
			}
		}

		if (display.SetPageOrigin(shown, page.X + x, page.Y)) {
			yield return default;
		}

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
		CurrentView = command switch { 4 => 2, 5 => 3, _ => 0 };
	}

	/// <summary>Record <paramref name="to"/>'s canvas origin less record <paramref name="from"/>'s.</summary>
	private (int X, int Y) OriginDelta(int to, int from) {
		var a = Files.Record(to);
		var b = Files.Record(from);
		return (a.CanvasOriginX - b.CanvasOriginX, a.CanvasOriginY - b.CanvasOriginY);
	}

	/// <summary>
	/// <c>CockpitView_ClearShake</c> (<c>00024750</c>): while armed and not in view 4. PLACEHOLDER: the body, the pages'
	/// origins put back, the clip regions and widgets moved back and the strip restored, plan step 7's hit shake.
	/// </summary>
	private void ClearShake() {
		if (!ShakeArmed || CurrentView == Es1CockpitViews.ExternalView) {
			return;
		}
	}

	/// <summary>
	/// <c>CockpitView_SetShakeBand</c> (<c>000243d4</c>): nothing in view 4 or while a change is staged. PLACEHOLDER: the
	/// body, the band set on the canvas origin and the shake armed, plan step 7's hit shake.
	/// </summary>
	private void SetShakeBand(int amplitude) {
		if (CurrentView == Es1CockpitViews.ExternalView || Staged) {
			return;
		}

		_ = amplitude;
	}
}
