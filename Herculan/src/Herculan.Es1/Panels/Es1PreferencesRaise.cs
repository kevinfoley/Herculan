using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File.Dbsim;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// <c>PreferencesPanel_Raise</c> (<c>ES1DBSIM 00057820</c>), <c>F12</c>'s and <c>Alt+P</c>'s case of
/// <c>Sim_DispatchCommand</c> (docs/es1/simulation/preferences.md#raising-the-panel--preferencespanel_raise-00057820): the
/// mission stopped, the outside view of the player's HERC above the preferences panel turning round it while the panel
/// is up, and the view put back as it was. Every frame it draws is the frame loop's (<see cref="IEs1Frame"/>) with
/// <c>Cam_Update</c> before its <c>Sim_RenderFrame</c> and no tick, and each wait for the monitor those frames and the
/// panel's loop make stands as the tick's (plan decision 11).
/// </summary>
internal static class Es1PreferencesRaise {
	/// <summary>The external view's rect above the panel: columns 0 to 319, rows 0 to the row above the panel.</summary>
	private const int RectLeft = 0, RectTop = 0, RectRight = 319;

	/// <summary>The projection pair (−160, −50): the center half way across and 50 rows down.</summary>
	private const int PairX = -160, PairY = -50;

	/// <summary>
	/// <c>PreferencesPanel_OrbitViewHook</c> (<c>000577f8</c>)'s steer, <c>0xffc0</c>, which the heading rate approaches
	/// as −<c>0x200</c> a pass.
	/// </summary>
	private const short OrbitSteer = unchecked((short)0xffc0);

	/// <summary>
	/// <c>PreferencesPanel_Raise</c> on <paramref name="simulation"/>, in its order:
	/// <list type="number">
	/// <item>the panel built (<see cref="Es1PreferencesPanel"/>);</item>
	/// <item><c>Sim_Frozen</c> kept and set, and <c>Display_FlipWithoutPan</c> cleared;</item>
	/// <item>the main camera copied into a camera of its own (<c>TSCamera_CopyFrom</c>);</item>
	/// <item>the external view's rect (0, 0)–(319, the panel's top − 1) with the pair (−160, −50) (<c>ExternalView_SetRect</c>);</item>
	/// <item>in view 4, the camera attached to the player's machine in mode 1 and one frame drawn; in any other view,
	/// frames drawn while a view change is staged or a command is pending, the view kept, view command 2 queued, and frames
	/// drawn until it has been staged and run, the camera attached on the first frame that finds it staged;</item>
	/// <item>the orbit hook installed, the panel run and destroyed;</item>
	/// <item><c>+0x8c</c> made the draw page;</item>
	/// <item>the camera copied back and the default rect put back (<c>ExternalView_ResetRect</c>);</item>
	/// <item>back to view 4: the border and the caption, a frame, and the border and the caption again; back to another
	/// view: <c>Cam_Update</c>, <c>CockpitView_SetView</c> with the kept view, <c>Cockpit_SkipFrameDraw</c> set across a
	/// <c>Sim_RenderFrame</c> and a <c>Sim_EndFrame</c>, <c>CockpitView_SetView</c> again on the other page, the skip
	/// cleared and a last <c>Sim_RenderFrame</c>, which the main loop's next <c>Sim_EndFrame</c> shows;</item>
	/// <item><c>Cursor_FollowsMouse</c> set and <c>Sim_Frozen</c> put back.</item>
	/// </list>
	/// The two loops of step 5 and the attach on the first staged frame are <c>00057820</c>'s statements. With no panels,
	/// no frame loop or no view manager, which retail always has, nothing is raised, an engine guard.
	/// </summary>
	public static IEnumerable<Es1Wait> Run(Es1Simulation simulation) {
		var chain = simulation.ViewChain;
		if (simulation.Panels is not { } globals || simulation.Frame is not { } frame || chain.Manager is not { } views) {
			yield break;
		}

		var display = views.Display;

		// 1.
		var panel = new Es1PreferencesPanel(globals);

		// 2.
		bool frozen = simulation.Frozen;
		simulation.Frozen = true;
		display.FlipWithoutPan = false;

		// 3. TSCamera_CtorDefault, then TSCamera_CopyFrom.
		var kept = new Es1Camera();
		kept.CopyFrom(chain.Camera);

		// 4.
		views.SetExternalViewRect(new Vue.Entry {
			ViewportX0 = RectLeft, ViewportY0 = RectTop, ViewportX1 = RectRight, ViewportY1 = panel.Rect.Y0 - 1,
			CenterX = PairX, CenterY = PairY,
		});

		// 5.
		int keptView;
		if (views.CurrentView == Es1CockpitViews.ExternalView) {
			AttachToPlayer(simulation);
			chain.Camera.Update();
			frame.RenderFrame();
			keptView = Es1CockpitViews.ExternalView;
		} else {
			while (views.Staged || views.PendingCommand != -1) {
				foreach (var wait in Frame(simulation, frame)) {
					yield return wait;
				}
			}

			keptView = views.CurrentView;
			views.QueueViewCommand(2);
			bool staged = false;
			do {
				foreach (var wait in EndFrame(simulation, frame)) {
					yield return wait;
				}

				if (!staged && views.Staged) {
					staged = true;
					AttachToPlayer(simulation);
				}

				chain.Camera.Update();
				frame.RenderFrame();
			} while (!staged || views.Staged);
		}

		// 6. PreferencesPanel_SetDrawHook with PreferencesPanel_OrbitViewHook: Cam_Steer(ViewObjectPtr, 0, 0, 0xffc0),
		// Cam_Update, Sim_RenderFrame.
		panel.DrawHook = () => {
			chain.Camera.Steer(0, 0, OrbitSteer);
			chain.Camera.Update();
			frame.RenderFrame();
		};
		foreach (var wait in panel.Run(new StrongBox<int>())) {
			yield return wait;
		}

		panel.Destroy();

		// 7.
		display.DrawPage = display.PairDrawn;

		// 8.
		chain.Camera.CopyFrom(kept);
		views.ResetExternalViewRect();

		// 9.
		if (keptView == Es1CockpitViews.ExternalView) {
			views.FillOutsideExternalRect();
			chain.DrawCaption(simulation.Controls);
			chain.Camera.Update();
			frame.RenderFrame();
			foreach (var wait in EndFrame(simulation, frame)) {
				yield return wait;
			}

			views.FillOutsideExternalRect();
			chain.DrawCaption(simulation.Controls);
		} else {
			chain.Camera.Update();
			views.SetView(keptView);
			views.Cockpit.SkipFrameDraw = true;
			frame.RenderFrame();
			foreach (var wait in EndFrame(simulation, frame)) {
				yield return wait;
			}

			views.SetView(keptView);
			views.Cockpit.SkipFrameDraw = false;
			frame.RenderFrame();
		}

		// 10. The kept camera is destroyed in place (TSContext_Dtor); nothing here keeps it.
		// PLACEHOLDER: the cursor, hidden since the external view's command, stays hidden until an instrument's next redraw
		// shows it again through Widget_DrawToCockpit (docs/es1/simulation/preferences.md#raising-the-panel--preferencespanel_raise-00057820);
		// of the instruments only the throttle gauge is built, which redraws when the throttle or the speed changes, and the
		// rest are plan step 6c's.
		views.Mouse.FollowsMouse = true;
		simulation.Frozen = frozen;
	}

	/// <summary>
	/// <c>Cam_AttachTo</c>(<c>LocalPlayerMech</c>) on <c>ViewObjectPtr</c>, which leaves the orbit at the least distance
	/// behind the machine, and the camera's mode <c>+0x36</c> set to 1, the orbit.
	/// </summary>
	private static void AttachToPlayer(Es1Simulation simulation) {
		var camera = simulation.ViewChain.Camera;
		camera.AttachTo(simulation.LocalPlayer);
		camera.Mode = Es1Camera.Orbit;
	}

	/// <summary>A frame of step 5's first loop: <c>Sim_EndFrame</c>, <c>Cam_Update</c> and <c>Sim_RenderFrame</c>.</summary>
	private static IEnumerable<Es1Wait> Frame(Es1Simulation simulation, IEs1Frame frame) {
		foreach (var wait in EndFrame(simulation, frame)) {
			yield return wait;
		}

		simulation.ViewChain.Camera.Update();
		frame.RenderFrame();
	}

	/// <summary><c>Sim_EndFrame</c> under the simulation's <c>Sim_Frozen</c>, each of its waits for the monitor yielded.</summary>
	private static IEnumerable<Es1Wait> EndFrame(Es1Simulation simulation, IEs1Frame frame) {
		foreach (var retrace in frame.EndFrame(simulation.Frozen)) {
			yield return Es1Wait.For(retrace);
		}
	}
}
