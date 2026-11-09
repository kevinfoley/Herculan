using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Host;

/// <summary>
/// The lift start (docs/es1/simulation/lift-start.md): step 14's search of the structures, <c>LiftStart_DarkenPalette</c>
/// (<c>ES1DBSIM 00057cf4</c>) in place of the first frame and <c>LiftStart_Rise</c> (<c>00057ffc</c>) in step 15, which
/// draws its frames through the frame loop with no tick.
/// </summary>
internal static class Es1LiftStart {
	/// <summary>How near a structure of the player's side starts the lift.</summary>
	public const int GateDistance = 40000;

	/// <summary>How far below its own height the machine starts the ride, and how far it rises a pass.</summary>
	public const int Depth = 4000, Rise = 0x23;

	/// <summary>How far ahead of the camera the point projected to the lift's row lies.</summary>
	public const int RowPointAhead = 1000;

	/// <summary>The row above which the ride does not step the fade.</summary>
	public const int FadeRow = -200;

	/// <summary>The flat color under the art.</summary>
	public const byte FillColor = 0x3f;

	/// <summary>The rows the rise plays, <c>tur1b</c>, and the jolt, <c>explo2</c>.</summary>
	private const int RiseSound = 0x21, JoltSound = 0x29;

	/// <summary>The jolt's length in counts of the clock <c>000981c4</c>, its band and the spread of its steps.</summary>
	public const uint JoltLength = 0x1e;

	/// <inheritdoc cref="JoltLength"/>
	public const int JoltBand = 5, JoltSpread = 5;

	/// <summary>The cross fade's length, in clock counts.</summary>
	public const int FadeLength = 0x78;

	/// <summary>
	/// <b>This engine's own:</b> the host time a pass's drawing stands for, three PC timer interrupts (about 25 ms), after
	/// which the frame's end waits for the next retrace. The port draws in no time, so each pass's
	/// <c>Vga_WaitVerticalRetrace</c>, which ends at once while a retrace is in progress, would otherwise end inside the
	/// retrace the last pass's wait ended on, and all 115 passes would run in one host frame. Retail's pass took as long as
	/// the machine drew it; this gives it the play frame's minimum on the hardware of its day, so the ride lasts about
	/// 3.3 s (115 passes of two 70 Hz retraces). The user's choice, plan decision 24
	/// (docs/herculan/plan-es1-first-mission.md#decisions); listed in <c>KNOWN_ISSUES.md</c>.
	/// </summary>
	public static TimeSpan PassDrawTime => TimeSpan.FromSeconds(Es1SimClock.InterruptsPerTick / (double)Es1SimClock.InterruptsPerSecond);

	/// <summary>The palette's two spans the darkening rewrites, each 16 entries.</summary>
	public const int FirstSpan = 0x20, SecondSpan = 0x40, SpanLength = 0x10;

	/// <summary>
	/// Step 14's search (docs/es1/simulation/lift-start.md#when-it-runs): <c>g_BasePool</c> oldest first, stopping at the
	/// first structure whose group has side 0 and whose point lies less than <see cref="GateDistance"/> from
	/// <c>LocalPlayerMech</c>'s (<c>Math_DistanceBetweenPoints</c>). With no machine, which retail always has, none; a
	/// structure with no group, whose side retail reads through the null, is passed over: engine guards.
	/// </summary>
	public static bool Gate(Es1SimGlobals globals) {
		if (globals.LocalPlayerMech is not { } player) {
			return false;
		}

		foreach (var structure in globals.Structures.Live) {
			if (structure.Group is { Side: 0 } && Es1PlayerThink.DistanceBetweenPoints(player.Position, structure.Position) < GateDistance) {
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>LiftStart_DarkenPalette</c> (docs/es1/simulation/lift-start.md#the-darkening--liftstart_darkenpalette-00057cf4)
	/// on <paramref name="loop"/>'s live palette: <c>PitchGradient_ForcedStep</c> 8 and the pitch gradient
	/// (<see cref="Es1FrameLoop.BlendPitchGradient"/>), so entries 64–79 hold its step 8; the live palette's entries then
	/// copied into a bright and a dark set; the dark set's two spans rewritten from the bright one, red = green / 2,
	/// green = red / 2, blue = red / 2 and the top byte 1, and installed through <see cref="Es1FrameLoop.InstallPaletteRange"/>;
	/// the cross fade from the dark set to the bright over <see cref="FadeLength"/>; and <c>PitchGradient_Enabled</c>
	/// cleared. The two palette objects retail builds around the sets are the sets here. With no live palette, which
	/// retail always has, nothing but the two flags and the gradient.
	/// </summary>
	public static void DarkenPalette(Es1FrameLoop loop) {
		var globals = loop.Views.Cockpit.Globals;
		var install = loop.InstallPaletteRange;
		globals.PitchGradientForcedStep = 8;
		loop.BlendPitchGradient();
		if (loop.LivePalette?.Invoke() is { } live) {
			var bright = Es1PaletteFade.Entries(live);
			var dark = (uint[])bright.Clone();
			foreach (int first in (ReadOnlySpan<int>)[FirstSpan, SecondSpan]) {
				for (int i = first; i < first + SpanLength && i < dark.Length; i++) {
					uint red = (bright[i] & 0xff) >> 1, green = (bright[i] >> 8 & 0xff) >> 1;
					dark[i] = green | red << 8 | red << 16 | 0x1000000;
				}
			}

			foreach (int first in (ReadOnlySpan<int>)[FirstSpan, SecondSpan]) {
				if (first < dark.Length) {
					install?.Invoke(first, dark[first..Math.Min(first + SpanLength, dark.Length)]);
				}
			}

			globals.PaletteFade.Begin(FadeLength, bright, dark);
		}

		globals.PitchGradientEnabled = false;
	}

	/// <summary>
	/// <c>LiftStart_Rise</c> (docs/es1/simulation/lift-start.md#the-ride--liftstart_rise-00057ffc) on
	/// <paramref name="loop"/>'s frames, yielding each wait for the monitor. Each pass from <see cref="Depth"/> below the
	/// machine's height, rising <see cref="Rise"/>, while short of it: the machine at the pass's height, <c>Cam_Update</c>
	/// and <c>Raster_InstallViewProjection</c>; the row the view point (0, <see cref="RowPointAhead"/>, −camera z)
	/// projects to; the render context's bottom cut to the row, or to its top while the row is below 1, for
	/// <c>Sim_RenderFrame</c> and put back; with the context's pair at 0, the art at (0, row) and the
	/// <see cref="FillColor"/> fills, the whole rect while the art lies above its top, from the art's bottom edge while it
	/// ends above its bottom; the gunsight's per-frame update; the pass's drawing standing <see cref="PassDrawTime"/>; while <c>LiftStart_FadeRemaining</c> is set and the row is
	/// greater than <see cref="FadeRow"/> the fade's step, its answer kept there; and <c>Sim_EndFrame</c>. Then the
	/// machine back at its height and the jolt: <c>CockpitView_SetShakeBand</c>(<see cref="JoltBand"/>), then until the
	/// clock reaches its value then plus <see cref="JoltLength"/>, <c>CockpitView_StepShake</c> by the presentation
	/// generator's next word mod <see cref="JoltSpread"/> and <c>Sim_RenderFrame</c>, each pass standing at the waits its
	/// page moves owe or, with none, until the next host frame (plan decision 11); <c>CockpitView_ClearShake</c> and
	/// <c>PitchGradient_Enabled</c> set.
	/// <para><b>This engine's own</b> (plan decision 30): the canvas's side panels draw each pass's cut, art and fills too
	/// (<see cref="Es1ViewManager.CanvasOverlay"/>). The canvas is held through each pass's drawing time
	/// (<see cref="Es1ViewManager.CanvasHeld"/>), while the monitor still shows the previous pass, and drawn just before
	/// <c>Sim_EndFrame</c> shows the new one (<see cref="Es1FrameLoop.DrawCanvas"/>); it holds the ride's last frame through
	/// the jolt.</para>
	/// <para>With no machine or camera, which retail always has, nothing; with no art, which retail reads unchecked, no
	/// art and no fill under it, the fill over the whole rect still drawn: engine guards.</para>
	/// </summary>
	public static IEnumerable<Es1Wait> RunRise(Es1FrameLoop loop, DynamixBitmap? art, Func<bool> frozen) {
		var views = loop.Views;
		var globals = views.Cockpit.Globals;
		if (globals.LocalPlayerMech is not { } machine || loop.ViewCamera is not { } camera) {
			yield break;
		}

		IEs1Frame frame = loop;
		var clock = globals.Clock;
		var fade = globals.PaletteFade;
		var home = machine.Position;
		var entry = views.Window;
		globals.Sound.Play(RiseSound);
		views.CanvasOverlay = new SidePanelRide(art, camera);
		for (int height = home.Z - Depth; height < home.Z; height += Rise) {
			machine.Position = new Vec3i(home.X, home.Y, height);
			loop.UpdateCamera();
			views.InstallViewProjection(camera);
			int row = Row(views.Projection!, camera);
			views.Window = Cut(views.Window, row);
			views.CanvasHeld = true;
			frame.RenderFrame();

			// The context's pair at 0 and its bottom back for the art and the gunsight; the pair back after.
			var window = views.Window with { Y1 = entry.Y1 };
			views.Window = window with { CentreX = window.X0, CentreY = window.Y0 };
			DrawArt(views.SceneCanvas(), window, art, row);
			views.Cockpit.Gunsight.UpdateAndPaint();
			views.Window = window with { CentreX = entry.CentreX, CentreY = entry.CentreY };
			yield return Es1Wait.ForTime(PassDrawTime);
			if (globals.LiftStartFadeRemaining != 0 && row > FadeRow) {
				globals.LiftStartFadeRemaining = (short)fade.Step(clock.Clock, frame.InstallPalette);
			}

			// The monitor shows the page drawn only from the frame's end on, so the canvas takes the pass just before it.
			views.CanvasHeld = false;
			loop.DrawCanvas();
			foreach (var retrace in frame.EndFrame(frozen())) {
				yield return Es1Wait.For(retrace);
			}
		}

		machine.Position = home;
		globals.Sound.Stop(RiseSound);
		views.CanvasOverlay = null;
		// The jolt's frames go to a page that is not shown, so the canvas keeps the ride's last frame, as the monitor does.
		views.CanvasHeld = true;
		uint end = clock.Clock + JoltLength;
		views.SetShakeBand(JoltBand);
		globals.Sound.Play(JoltSound);
		do {
			views.StepShake((ushort)globals.PresentationRandom.Next() % JoltSpread);
			frame.RenderFrame();
			bool waited = false;
			foreach (var retrace in views.TakeOwedRetraces()) {
				waited = true;
				yield return Es1Wait.For(retrace);
			}

			if (!waited) {
				yield return Es1Wait.NextHostFrame;
			}
		} while (clock.Clock < end);

		views.CanvasHeld = false;
		views.ClearShake();
		globals.PitchGradientEnabled = true;
	}

	/// <summary>
	/// The row the view point (0, <see cref="RowPointAhead"/>, −<paramref name="camera"/>'s height) projects to through
	/// <paramref name="projection"/>, which does not depend on the camera's angles
	/// (docs/es1/simulation/lift-start.md#the-ride--liftstart_rise-00057ffc).
	/// </summary>
	private static int Row(Es1ViewProjection projection, Es1Camera camera) =>
		projection.ProjectToScreen(Es1ViewProjection.PerspectiveDivide(new Vec3i(0, RowPointAhead, -camera.Position.Z))).Y;

	/// <summary><paramref name="window"/> with its bottom cut to <paramref name="row"/>, or to its top while the row is below 1.</summary>
	private static Es1ViewWindow Cut(Es1ViewWindow window, int row) =>
		window with { Y1 = row < 1 ? window.Y0 : Math.Min(row, window.Y1) };

	/// <summary>
	/// The art and the <see cref="FillColor"/> fills for <paramref name="row"/> through <paramref name="canvas"/>, at page
	/// coordinates: the whole of <paramref name="window"/>'s rect while the art lies above its top; the art at (0, row)
	/// and the fill from the art's bottom edge, from column 0, while it ends above its bottom; the art alone otherwise.
	/// </summary>
	private static void DrawArt(Es1SceneCanvas canvas, Es1ViewWindow window, DynamixBitmap? art, int row) {
		int artRows = art?.Rows ?? 0;
		if (row + artRows < window.Y0) {
			canvas.FillRect(window.X0, window.Y0, window.X1, window.Y1, FillColor);
		} else if (row + artRows < window.Y1) {
			if (art != null) {
				canvas.Blit(art, 0, row);
			}

			canvas.FillRect(0, row + artRows, window.X1, window.Y1, FillColor);
		} else if (art != null) {
			canvas.Blit(art, 0, row);
		}
	}

	/// <summary>
	/// <b>This engine's own</b> (plan decision 30): the ride's pass as the canvas's side panels draw it, each panel's
	/// window cut at the row its own projection gives and the art and fills drawn over it, as the ride draws them in the
	/// window it runs in. The views share one image plane, so the row lines up across the panels, and the art, one
	/// screen wide, repeats in each.
	/// </summary>
	private sealed class SidePanelRide(DynamixBitmap? art, Es1Camera camera) : IEs1CanvasOverlay {
		public Es1ViewWindow Cut(Es1ViewWindow window) => Es1LiftStart.Cut(window, RowIn(window));

		public void DrawOver(Es1SceneCanvas canvas, Es1ViewWindow window) => DrawArt(canvas, window, art, RowIn(window));

		private int RowIn(Es1ViewWindow window) => Row(Es1ViewProjection.Install(camera, window), camera);
	}
}
