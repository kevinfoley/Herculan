using System.Runtime.CompilerServices;
using Herculan.Es1.Render;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Host;

/// <summary>What <see cref="Es1FrameLoop"/> paces: the simulation runner, or a test's clock.</summary>
internal interface IEs1FramePacer {
	/// <summary>Host time of <paramref name="elapsed"/> with nothing waiting: the clock. Returns whether a tick is due.</summary>
	bool AdvanceClock(TimeSpan elapsed);

	/// <summary>
	/// <c>Sim_MainTick</c>, yielding a wait at each pass of a panel's loop; <paramref name="ended"/> is set when it ends
	/// the mission.
	/// </summary>
	IEnumerable<Es1Wait> Tick(StrongBox<bool> ended);

	/// <summary>Host time of <paramref name="elapsed"/> while the frame stands waiting: the PC timer runs on, and no tick runs.</summary>
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
///
/// <para>The tick stands waiting the same way inside a panel's loop, which retail runs within <c>Sim_MainTick</c> (plan
/// decision 11, <see cref="Es1Wait"/>): a pass of the loop that waits for no retrace ends with the next host frame, and
/// the tick and the frame after it go on from there. So the frame's work from the tick on, the tick, the camera, the
/// 3D pass and the frame's end, is one piece the loop holds and resumes, and no other tick starts until it has run. A
/// routine inside the tick that draws frames of its own, <c>PreferencesPanel_Raise</c>, runs this loop's
/// <c>Sim_RenderFrame</c> and <c>Sim_EndFrame</c> through <see cref="IEs1Frame"/>, which the loop hands the simulation
/// when it is wired to the runner; their waits for the monitor stand as the tick's.</para>
/// </summary>
public sealed class Es1FrameLoop : IEs1Frame {
	/// <summary>
	/// The vertical retraces a second of the VGA's 320 × 200 mode, the period hardware's 70 Hz refresh, which
	/// <c>VGA8_ShowPage</c> and <c>Vga_WaitVerticalRetrace</c> wait for (docs/es1/rendering/screen-pages.md#the-simulators-own-page-routines).
	/// </summary>
	public const int RetracesPerSecond = 70;

	private readonly Action _camUpdate;
	private readonly Action? _beginWorldPass;
	private readonly Es1AppliedDetail? _detail;
	private readonly Func<Es1Camera>? _viewCamera;

	// The frame's work from the tick on, standing at its current wait; null once it has run.
	private IEnumerator<Es1Wait>? _work;

	// The current wait ends with the next host frame, at the end of the host time that frame advances by.
	private bool _untilNextHostFrame;

	private bool _missionEnded;
	private Es1SimulationRunner? _wired;

	/// <param name="views">The view manager, on the display it flips.</param>
	/// <param name="camUpdate"><c>Cam_Update</c>(<c>ViewObjectPtr</c>): the current camera (<see cref="Es1ViewChain.Camera"/>) placed.</param>
	/// <param name="beginWorldPass">
	/// The start of <c>Sim_RenderFrame</c>'s 3D pass: the current camera put on the host's camera with the installed
	/// window's projection, the external rect's in view 4, and the scene readied for it, as retail's pass reads the
	/// camera and the render context as they stand when it draws.
	/// </param>
	/// <param name="detail">The detail setting the 3D pass draws the HERCs at, which each frame applies again when option 6 has changed.</param>
	/// <param name="viewCamera">
	/// <c>ViewObjectPtr</c>, the current camera, whose view the 3D pass installs (<see cref="Es1ViewManager.InstallViewProjection"/>)
	/// for what draws over the scene; with none, none is installed.
	/// </param>
	public Es1FrameLoop(Es1ViewManager views, Action camUpdate, Action? beginWorldPass = null, Es1AppliedDetail? detail = null,
			Func<Es1Camera>? viewCamera = null) {
		Views = views;
		_camUpdate = camUpdate;
		_beginWorldPass = beginWorldPass;
		_detail = detail;
		_viewCamera = viewCamera;
	}

	public Es1ViewManager Views { get; }

	/// <summary>The host time the loop has been advanced by, in <see cref="TimeSpan"/> ticks, on which the retraces lie.</summary>
	internal long HostTime { get; private set; }

	/// <summary>
	/// The host time the frame's current wait ends at, the start of the retrace it waits for; null while none waits and
	/// while the wait ends with the next host frame.
	/// </summary>
	internal long? WaitEnds { get; private set; }

	/// <summary>Whether the frame's work stands waiting, the frame's end for the monitor or the tick in a panel's loop.</summary>
	internal bool Waiting => _work != null;

	/// <summary>The host time retrace <paramref name="index"/> starts at, rounded up to a <see cref="TimeSpan"/> tick.</summary>
	internal static long RetraceStart(long index) =>
		(index * TimeSpan.TicksPerSecond + RetracesPerSecond - 1) / RetracesPerSecond;

	/// <summary>
	/// The display's part of <c>Sim_StartMission</c>: step 11, the pages at (0, 0), (0, 200), (320, 0) and (320, 200),
	/// the pair {0, 2}, page 0 shown and page 3 drawn on; step 13, the bring-up flag set (<see cref="Es1Cockpit.BringUp"/>),
	/// the player's weapon rows built (<c>WeaponMounts_BuildGauges</c>, <see cref="Sim.Es1WeaponMounts.BuildGauges"/>),
	/// the squadmates and their pilots (<see cref="Es1Cockpit.LoadSquadmatePilots"/>), <paramref name="cursor"/> installed on the display's cursor, <c>Player_PerFrameCockpitUpdate</c>, <c>Cam_Update</c>,
	/// the draw page 2 again and views 1 and 0 set, so both canopies lie in page 2's half; step 14 without the lift, the
	/// first frame's 3D pass; and step 15, the flag cleared, the whole of page 2 copied onto page 0, the cursor shown and
	/// the cockpit's mouse made live.
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

		// Step 13 opens with 000b5bca set, under which Widget_DrawToCockpit copies nothing.
		Views.Cockpit.BringUp = true;
		Views.Cockpit.Machine?.Mounts?.BuildGauges(Views.Cockpit);
		Views.Cockpit.LoadSquadmatePilots();
		display.Cursor.SetImage(cursor);
		PlayerCockpitUpdate();
		_camUpdate();
		display.DrawPage = display.PairDrawn;
		Views.SetView(1);
		Views.SetView(0);

		// Step 14 without the lift, which needs a friendly structure within 40000 and no structure is spawned: the
		// pitch gradient (Palette_BlendRangeByPitch) is not run (Es1Backdrop), and Palette_FlushDirtyRange has nothing
		// to send; then Sim_RenderFrame.
		RenderFrame();

		// Step 15: 000b5bca cleared; Widget_DrawToCockpit(0, {0, 0, 320, 400}, 1), page +0x8c copied onto +0x88 through
		// the canvas context and the draw page put back.
		Views.Cockpit.BringUp = false;
		Views.Cockpit.DrawToCockpit(new Es1Rect(0, 0, 320, 400));
		// The driver's slot 30 (0009df4c) shows the cursor; and with no tape playing, CockpitMouseLive is set.
		display.Cursor.Show();
		Views.Mouse.Live = true;
	}

	/// <summary>
	/// One host frame of <paramref name="deltaSeconds"/>: while the frame's work waits, the timer runs on until the wait
	/// ends and the work goes on from there, a wait for the next host frame ending with this one; then the runner's
	/// clock for the rest of the host frame, and when it answers a tick the tick, the camera, the 3D pass and the frame's
	/// end. Returns whether the tick ended the mission.
	/// </summary>
	public bool AdvanceHostFrame(double deltaSeconds, Es1SimulationRunner? runner) {
		if (runner != null && !ReferenceEquals(runner, _wired)) {
			_wired = runner;
			Views.SetSimInputEnabled = enabled => runner.AxisKeys.SetEnabled(enabled);
			var clock = runner.Simulation.Globals.Clock;
			Views.Mouse.Now = () => clock.Clock;
			runner.Simulation.Frame = this;
		}

		return Advance(TimeSpan.FromSeconds(deltaSeconds), runner);
	}

	/// <summary><see cref="AdvanceHostFrame"/> on <paramref name="pacer"/>, which a test supplies.</summary>
	internal bool Advance(TimeSpan elapsed, IEs1FramePacer? pacer) {
		ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
		long end = HostTime + elapsed.Ticks;
		if (_untilNextHostFrame) {
			_untilNextHostFrame = false;
			WaitEnds = end;
		}

		if (!RunWork(pacer, end)) {
			return false;
		}

		if (_missionEnded) {
			return true;
		}

		if (pacer == null) {
			HostTime = end;
			return false;
		}

		bool due = pacer.AdvanceClock(TimeSpan.FromTicks(end - HostTime));
		HostTime = end;
		if (!due) {
			return false;
		}

		_work = RunTickAndFrame(pacer).GetEnumerator();
		Resume();
		RunWork(pacer, end);
		return _missionEnded;
	}

	/// <summary>
	/// The frame's work held until each of its waits ends and run on from there, up to the host time
	/// <paramref name="end"/>. Returns whether it has run to its end.
	/// </summary>
	private bool RunWork(IEs1FramePacer? pacer, long end) {
		while (_work != null) {
			if (_untilNextHostFrame || WaitEnds is not { } waitEnds || waitEnds > end) {
				Hold(pacer, end);
				return false;
			}

			Hold(pacer, waitEnds);
			Resume();
		}

		return true;
	}

	/// <summary>
	/// The frame's work from the tick on: the tick, standing wherever a panel's loop waits; when it ends the mission
	/// nothing more, as <c>Sim_Main</c> leaves its loop; otherwise <see cref="RunFrame"/>, standing at each of the frame's
	/// end's waits for the monitor.
	/// </summary>
	private IEnumerable<Es1Wait> RunTickAndFrame(IEs1FramePacer pacer) {
		var ended = new StrongBox<bool>();
		foreach (var wait in pacer.Tick(ended)) {
			yield return wait;
		}

		if (ended.Value) {
			_missionEnded = true;
			yield break;
		}

		foreach (var retrace in RunFrame(pacer.Frozen)) {
			yield return Es1Wait.For(retrace);
		}

		// Subsystem phase 6, Vcr_FlushCheckpoints: tapes are not taken.
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

	/// <summary>
	/// The frame's work run on to its next wait, whose end is kept in <see cref="WaitEnds"/>, or marked for the next host
	/// frame; or to its end.
	/// </summary>
	private void Resume() {
		if (_work!.MoveNext()) {
			if (_work.Current.Retrace is { } retrace) {
				WaitEnds = WaitEnd(HostTime, retrace);
			} else {
				WaitEnds = null;
				_untilNextHostFrame = true;
			}

			return;
		}

		_work.Dispose();
		_work = null;
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
		_camUpdate();
		RenderFrame();
		return EndFrame(Views, frozen);
	}

	/// <inheritdoc/>
	void IEs1Frame.RenderFrame() => RenderFrame();

	/// <inheritdoc/>
	IEnumerable<Es1Retrace> IEs1Frame.EndFrame(bool frozen) => EndFrame(Views, frozen);

	/// <summary>
	/// <c>Sim_EndFrame</c> (<c>00059b40</c>): <c>Display_FlipWithoutPan</c> set unless frozen; the staged view change;
	/// <c>+0x8c</c> shown and <c>+0x88</c> drawn on, and the pair swapped; the view command. Yields each wait for the
	/// monitor its routines make, after those the frame's draw owes (<see cref="Es1ViewManager.TakeOwedRetraces"/>).
	/// </summary>
	internal static IEnumerable<Es1Retrace> EndFrame(Es1ViewManager views, bool frozen) {
		// The hit shake's steps in the frame's draw moved the page on the monitor, each move waiting for it there.
		foreach (var wait in views.TakeOwedRetraces()) {
			yield return wait;
		}

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
	/// <c>Sim_RenderFrame</c> (<c>00059c08</c>): subsystem phase 5, whose ported functions are <c>EffectLightPool_FlushDeletes</c>
	/// (<see cref="Effects.Es1EffectLights.FlushDeletes"/>), <c>ExplosionPool_FlushDeletes</c>
	/// (<see cref="Effects.Es1ExplosionPool.FlushDeletes"/>) and <c>Projectile_FlushDeletes</c>
	/// (<see cref="Projectiles.Es1ProjectilePool.FlushDeletes"/>); the detail setting applied again when option 6 has changed, the view
	/// shake's offset added to the projection pair (<see cref="Es1ViewManager.MoveProjectionPair"/>), the cockpit's
	/// frame (<c>Cockpit_DrawFrame</c>), then the 3D pass, which installs the current camera's view and draws the world
	/// into the draw page in the installed window and clip block when the current view is not 1 or a view change is
	/// staged, answering a pending pixel pick from what it drew (<see cref="Es1ObjectPick.Resolve"/>),
	/// <c>Player_PerFrameCockpitUpdate</c>, and the gunsight's per-frame update over it
	/// (<see cref="Gunsight.Es1Gunsight.UpdateAndPaint"/>), which reads the pick's answer in the same frame, as retail's does;
	/// and last the pilot channel's slot <c>+0x10</c> (<see cref="Messages.Es1PilotChannel.Update"/>), after which the
	/// shake's offset is taken back out.
	/// </summary>
	private void RenderFrame() {
		// PLACEHOLDER: phase 5's debris and smoke flushes, plan step 7e part B. FlatObjPool_FlushDeletes (00006d50) has
		// nothing to give back: this engine does not count g_FlatObjPool.
		var globals = Views.Cockpit.Globals;
		globals.EffectLights.FlushDeletes();
		globals.Explosions.FlushDeletes();
		globals.Projectiles.FlushDeletes();
		_detail?.Refresh();
		// The shake's offset, as it stands now, added to the projection pair across the frame's draws and taken out after.
		var (shakeX, shakeY) = Views.ShakeOffset;
		Views.MoveProjectionPair(shakeX, shakeY);
		Views.Cockpit.DrawFrame();
		if (Views.CurrentView != 1 || Views.Staged) {
			if (_viewCamera?.Invoke() is { } camera) {
				Views.InstallViewProjection(camera);
			}

			_beginWorldPass?.Invoke();
			Views.Display.DrawWorld(Views.Window, Views.Clip);
			// ObjList_DrawFrameObjects' end, with the pick pending: the object the pass has just drawn at its point.
			Views.Cockpit.Globals.ObjectPick.Resolve(Views.Display.WorldObjectAt);
		}

		PlayerCockpitUpdate();
		Views.Cockpit.Gunsight.UpdateAndPaint();
		Views.Cockpit.PilotChannel.Update();
		Views.MoveProjectionPair(-shakeX, -shakeY);
	}

	/// <summary>
	/// <c>Player_PerFrameCockpitUpdate</c>(<c>LocalPlayerMech</c>), the view manager's machine; with none, which retail
	/// always has, nothing.
	/// </summary>
	private void PlayerCockpitUpdate() {
		if (Views.Cockpit.Machine is { } machine) {
			Es1PlayerCockpitUpdate.Run(machine, Views.Cockpit);
		}
	}
}
