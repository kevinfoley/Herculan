using Herculan.Es1.View;

namespace Herculan.Es1.Host;

/// <summary>What <see cref="Es1FrameLoop"/> paces: the simulation runner, or a test's clock.</summary>
internal interface IEs1FramePacer {
	/// <summary>Host time of <paramref name="elapsed"/> with no frame's end waiting: the clock and, when it answers one, the tick.</summary>
	Es1SimulationRunner.Frame AdvanceHostFrame(TimeSpan elapsed);

	/// <summary>Host time of <paramref name="elapsed"/> while the frame's end waits: the PC timer runs on, and no tick runs.</summary>
	void Hold(TimeSpan elapsed);

	/// <summary><c>Sim_Frozen</c> (<c>ES1DBSIM 000b5b92</c>), which <c>Sim_EndFrame</c> reads.</summary>
	bool Frozen { get; }
}

/// <summary>
/// <c>Sim_Main</c>'s frame on the display (docs/es1/simulation/main-loop.md#the-frame), run by the host: on each tick
/// the camera, the world drawn into the draw page (<c>Sim_RenderFrame</c>'s 3D pass) and the frame's end,
/// <c>Sim_EndFrame</c> (<c>ES1DBSIM 00059b40</c>), with its view change, page flip and view command
/// (docs/es1/simulation/cockpit-views.md#cockpitview_stepviewtransition-00022904); and the display's part of
/// <c>Sim_StartMission</c>, steps 11 and 13 to 15 (docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc).
/// The host shows the monitor's picture every host frame (<see cref="Es1Monitor"/>).
///
/// <para>The monitor is a timeline of vertical retraces on host time, <see cref="RetracesPerSecond"/> a second, the
/// first at the loop's construction: the phase of the monitor against the PC timer is this engine's, the two running
/// apart in retail. A retrace's own length is taken as nil, so a retrace is the instant it starts. A frame's end that
/// waits for the monitor, a view's slide (<c>VGA8_ShowPage</c>) or the retrace wait that ends every frame past the
/// cooldown and the page gate (<c>Vga_WaitVerticalRetrace</c> in <c>CockpitView_ProcessViewCommand</c>), holds the
/// frame until the wait's retrace starts and goes on from that instant: the host goes on showing the monitor, and until the frame's end
/// has run no tick runs and no input is taken, while the PC timer runs on (<see cref="Es1SimulationRunner.Hold"/>).
/// Then the clock takes the rest of the host frame, as <c>Time_BeginSimTick</c> follows <c>Sim_EndFrame</c>.</para>
/// </summary>
public sealed class Es1FrameLoop {
	/// <summary>
	/// The vertical retraces a second of the VGA's 320 × 200 mode, the period hardware's 70 Hz refresh, which
	/// <c>VGA8_ShowPage</c> and <c>Vga_WaitVerticalRetrace</c> wait for (docs/es1/rendering/screen-pages.md#the-simulators-own-page-routines).
	/// </summary>
	public const int RetracesPerSecond = 70;

	private readonly Action _camUpdate;
	private IEnumerator<Es1Retrace>? _frameEnd;
	private Es1SimulationRunner? _wired;

	/// <param name="views">The view manager, on the display it flips.</param>
	/// <param name="camUpdate">
	/// <c>Cam_Update</c>(<c>ViewObjectPtr</c>): the current camera (<see cref="Es1ViewChain.Camera"/>) placed and put on
	/// the host's camera with the installed window's projection, the external rect's in view 4, and the scene readied for
	/// it, before the 3D pass is drawn.
	/// </param>
	public Es1FrameLoop(Es1ViewManager views, Action camUpdate) {
		Views = views;
		_camUpdate = camUpdate;
	}

	public Es1ViewManager Views { get; }

	/// <summary>The host time the loop has been advanced by, in <see cref="TimeSpan"/> ticks, on which the retraces lie.</summary>
	internal long HostTime { get; private set; }

	/// <summary>The host time the frame's end's wait ends at, the start of the retrace it waits for; null while none waits.</summary>
	internal long? WaitEnds { get; private set; }

	/// <summary>The host time retrace <paramref name="index"/> starts at, rounded up to a <see cref="TimeSpan"/> tick.</summary>
	internal static long RetraceStart(long index) =>
		(index * TimeSpan.TicksPerSecond + RetracesPerSecond - 1) / RetracesPerSecond;

	/// <summary>
	/// The display's part of <c>Sim_StartMission</c>: step 11, the pages at (0, 0), (0, 200), (320, 0) and (320, 200),
	/// the pair {0, 2}, page 0 shown and page 3 drawn on; step 13, <paramref name="cursor"/> installed on the display's
	/// cursor, the draw page 2 again and views 1 and 0 set, so both canopies lie in page 2's half; step 14 without the
	/// lift, the first frame's 3D pass; and step 15, the whole of page 2 copied onto page 0, the cursor shown and the
	/// cockpit's mouse made live.
	/// </summary>
	/// <param name="cursor">Step 5's cursor, <c>dci\cursor</c>, which <c>00057640</c> loads.</param>
	public void StartMission(Es1CursorImage? cursor = null) {
		var display = Views.Display;
		display.SetPageBase(0, 0, 0);
		display.SetPageBase(1, 0, Es1VideoMemory.PageHeight);
		display.SetPageBase(2, Es1VideoMemory.PageWidth, 0);
		display.SetPageBase(3, Es1VideoMemory.PageWidth, Es1VideoMemory.PageHeight);
		// -S, which gives the pair {0, 0}, is not taken.
		display.PairVisible = 0;
		display.PairDrawn = 2;
		display.DrawPage = display.PairDrawn;
		display.ShowPage(display.PairVisible);
		display.DrawPage = 3;

		display.Cursor.SetImage(cursor);
		display.DrawPage = display.PairDrawn;
		Views.SetView(1);
		Views.SetView(0);

		// Step 14 without the lift, which needs a friendly structure within 40000 and no structure is spawned: the
		// pitch gradient (Palette_BlendRangeByPitch) is not run (Es1Backdrop), and Palette_FlushDirtyRange has nothing
		// to send; then Sim_RenderFrame.
		RenderFrame();

		// Step 15: Widget_DrawToCockpit(0, {0, 0, 320, 400}, 1), page +0x8c copied onto +0x88 and the draw page put back.
		// PLACEHOLDER: retail tests CockpitView_RectCornerVisible against the cockpit's canvas context and copies under
		// that context's clip block, both the cockpit object's (plan step 6); the copy here is unclipped.
		int draw = display.DrawPage;
		display.SourcePage = display.PairDrawn;
		display.DrawPage = display.PairVisible;
		display.CopyRect(0, 0, 320, 400, 0, 0);
		display.DrawPage = draw;
		// The driver's slot 30 (0009df4c) shows the cursor; and with no tape playing, CockpitMouseLive is set.
		display.Cursor.Show();
		Views.Mouse.Live = true;
	}

	/// <summary>
	/// One host frame of <paramref name="deltaSeconds"/>: while a frame's end waits, the timer runs on until its retrace
	/// starts and the frame's end goes on from there; then the runner's clock and tick for the rest of the host frame, and
	/// after a tick the camera, the 3D pass and the frame's end. Returns whether the tick ended the mission.
	/// </summary>
	public bool AdvanceHostFrame(double deltaSeconds, Es1SimulationRunner? runner) {
		if (runner != null && !ReferenceEquals(runner, _wired)) {
			_wired = runner;
			Views.SetSimInputEnabled = enabled => runner.AxisKeys.SetEnabled(enabled);
			var clock = runner.Simulation.Globals.Clock;
			Views.Mouse.Now = () => clock.Clock;
		}

		return Advance(TimeSpan.FromSeconds(deltaSeconds), runner);
	}

	/// <summary><see cref="AdvanceHostFrame"/> on <paramref name="pacer"/>, which a test supplies.</summary>
	internal bool Advance(TimeSpan elapsed, IEs1FramePacer? pacer) {
		ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
		long end = HostTime + elapsed.Ticks;
		if (!RunFrameEnd(pacer, end)) {
			return false;
		}

		if (pacer == null) {
			HostTime = end;
			return false;
		}

		var frame = pacer.AdvanceHostFrame(TimeSpan.FromTicks(end - HostTime));
		HostTime = end;
		if (frame != Es1SimulationRunner.Frame.Ticked) {
			return frame == Es1SimulationRunner.Frame.MissionEnded;
		}

		_frameEnd = RunFrame(pacer.Frozen).GetEnumerator();
		ResumeFrameEnd();
		RunFrameEnd(pacer, end);
		// Subsystem phase 6, Vcr_FlushCheckpoints: tapes are not taken.
		return false;
	}

	/// <summary>
	/// The frame's end held until each of its waits ends and run on from there, up to the host time <paramref name="end"/>.
	/// Returns whether it has run to its end.
	/// </summary>
	private bool RunFrameEnd(IEs1FramePacer? pacer, long end) {
		while (_frameEnd != null) {
			long waitEnds = WaitEnds!.Value;
			if (waitEnds > end) {
				Hold(pacer, end);
				return false;
			}

			Hold(pacer, waitEnds);
			ResumeFrameEnd();
		}

		return true;
	}

	/// <summary>
	/// The host time of the first retrace start at or after <paramref name="now"/> when <paramref name="wait"/> ends in a
	/// retrace, and after it otherwise.
	/// </summary>
	internal static long WaitEnd(long now, Es1Retrace wait) {
		long index = now * RetracesPerSecond / TimeSpan.TicksPerSecond;
		while (wait.EndsInRetrace ? RetraceStart(index) < now : RetraceStart(index) <= now) {
			index++;
		}

		return RetraceStart(index);
	}

	/// <summary>The frame's end run on to its next wait, whose end is kept in <see cref="WaitEnds"/>, or to its end.</summary>
	private void ResumeFrameEnd() {
		if (_frameEnd!.MoveNext()) {
			WaitEnds = WaitEnd(HostTime, _frameEnd.Current);
			return;
		}

		_frameEnd.Dispose();
		_frameEnd = null;
		WaitEnds = null;
	}

	/// <summary>The host time up to <paramref name="until"/> held: the timer runs on.</summary>
	private void Hold(IEs1FramePacer? pacer, long until) {
		if (until > HostTime) {
			pacer?.Hold(TimeSpan.FromTicks(until - HostTime));
			HostTime = until;
		}
	}

	/// <summary>
	/// The frame after a tick, from <c>Cam_Update</c> on: the 3D pass and <see cref="EndFrame"/>, yielding a retrace for
	/// each wait the frame's end makes. The host runs it through <see cref="AdvanceHostFrame"/>; a test drives it directly.
	/// </summary>
	internal IEnumerable<Es1Retrace> RunFrame(bool frozen) {
		RenderFrame();
		return EndFrame(Views, frozen);
	}

	/// <summary>
	/// <c>Sim_EndFrame</c> (<c>00059b40</c>): <c>Display_FlipWithoutPan</c> set unless frozen; the staged view change;
	/// <c>+0x8c</c> shown and <c>+0x88</c> drawn on, and the pair swapped; the view command. Yields each wait for the
	/// monitor its routines make.
	/// </summary>
	internal static IEnumerable<Es1Retrace> EndFrame(Es1ViewManager views, bool frozen) {
		var display = views.Display;
		display.FlipWithoutPan = !frozen;
		foreach (var wait in views.StepViewTransition()) {
			yield return wait;
		}

		// Palette_CycleAnimatedRanges (00028260) and Palette_FlushDirtyRange (00027f74) have nothing to do: ES1's volumes
		// hold no cyc\ file, so no range is registered to cycle, and the cockpit's scheme is installed into the live
		// palette once, before the first frame (docs/es1/rendering/palette.md#differences-from-es2).
		if (display.ShowPage(display.PairDrawn)) {
			yield return default;
		}

		display.DrawPage = display.PairVisible;
		(display.PairVisible, display.PairDrawn) = (display.PairDrawn, display.PairVisible);
		foreach (var wait in views.ProcessViewCommand()) {
			yield return wait;
		}

		// Mem_TrackPeakUse (00059288) records the DOS heap's peak use, which this engine does not keep.
	}

	/// <summary>
	/// <c>Cam_Update</c> and <c>Sim_RenderFrame</c> (<c>00059c08</c>): the cockpit's frame (<c>Cockpit_DrawFrame</c>),
	/// then the 3D pass, which draws the world into the draw page in the installed window and clip block when the current
	/// view is not 1 or a view change is staged.
	/// </summary>
	private void RenderFrame() {
		_camUpdate();
		// The shake offset Sim_RenderFrame adds to the projection pair is (0, 0) while no shake is armed (plan step 7).
		Views.Cockpit.DrawFrame();
		if (Views.CurrentView != 1 || Views.Staged) {
			Views.Display.DrawWorld(Views.Window, Views.Clip);
		}

		// PLACEHOLDER: Player_PerFrameCockpitUpdate (000155a8), the gunsight's slot +0xc and the squad message port's slot
		// +0x10 after the 3D pass: the cockpit, plan steps 6b and 6c.
	}
}
