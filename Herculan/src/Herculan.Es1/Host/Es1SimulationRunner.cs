using System.Runtime.CompilerServices;
using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Silk.NET.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// The simulator in a host window: the host's keyboard and the mouse driver into ES1's input (<see cref="Es1HostKeyboard"/>,
/// <see cref="Es1HostMouse"/>), the input build (<see cref="Es1InputBuild"/>), and once a host frame the clock and,
/// when it answers a tick, <see cref="Es1Simulation.MainTick"/>, in the order of <c>Sim_Main</c>'s frame
/// (docs/es1/simulation/main-loop.md#the-frame). It is built once the window's devices are known, since the keyboard
/// seeds its modifiers from the host's.
/// </summary>
public sealed class Es1SimulationRunner : IEs1FramePacer, IDisposable {
	/// <summary>What a host frame did.</summary>
	public enum Frame {
		/// <summary>The clock answered no tick.</summary>
		NoTick,

		/// <summary>A tick ran.</summary>
		Ticked,

		/// <summary>A tick ran and ended the mission.</summary>
		MissionEnded,
	}

	/// <summary>
	/// The most host time a playback holds towards its next frame (<see cref="AdvanceClock"/>): the longest delta
	/// <c>Time_BeginSimTick</c>'s clamp records, <c>0x1c2</c>, so time the tick stood in a panel's loop, or a stalled host
	/// frame, starts at most one frame at once. This engine's own, with plan decision 20's pace.
	/// </summary>
	private static readonly double MaxTapeHeldSeconds = Es1TapePlayback.SecondsOf(0x1c2);

	private readonly Es1HostKeyboard _hostKeyboard;

	// The host time held towards the tape's next frame.
	private double _tapeHeld;

	/// <param name="preferences">The install's <c>data\prefs.cfg</c> as the simulator reads it.</param>
	/// <param name="mouse">The mouse driver, which the host attaches to its mouse; one with no host device when null.</param>
	/// <param name="port">The game port the joystick devices are built on, and detect their axes on, once.</param>
	/// <param name="viewChain">The views and cameras the mission start built, which the tick moves between.</param>
	/// <param name="widgets">What <c>Widget_LoadStylesAndFonts</c> loaded at the mission start, which the panels draw with.</param>
	/// <param name="content">The volumes the panels' own files are read from.</param>
	/// <param name="tape">
	/// The tape <c>-p</c> or <c>-D</c> opened, which the input build plays and whose <c>DemoMode</c> the axis keys take;
	/// <c>Keyjoy_LoadConfig</c> then reads the tape's <c>tapes\keyjoy.cfg</c>.
	/// </param>
	/// <param name="recording">
	/// The tape <c>-r</c> started, to which the input build appends each frame with the tick's <c>SimTickDelta</c>.
	/// </param>
	public Es1SimulationRunner(Es1World world, string installRoot, Es1Preferences preferences, IKeyboard? keyboard,
			Es1HostMouse? mouse, IEs1GamePort port, View.Es1ViewChain? viewChain = null, Es1WidgetResources? widgets = null,
			GameContent? content = null, Es1TapePlayback? tape = null, Es1TapeRecording? recording = null) {
		var keyboardInput = new Es1KeyboardInput(Es1HostKeyboard.ReadBiosShiftFlags(keyboard));
		_hostKeyboard = new Es1HostKeyboard(keyboardInput.Keyboard);
		if (keyboard != null) {
			_hostKeyboard.Attach(keyboard);
		}

		keyboardInput.AxisKeys.DemoMode = tape?.DemoMode == true;
		var input = new Es1InputBuild(keyboardInput, mouse ?? new Es1HostMouse(), preferences,
			Es1KeyjoyConfig.Load(installRoot, tape?.Playing == true), new Es1ControlsState(preferences), port) {
			CockpitMouse = viewChain?.Manager?.Mouse,
			Tape = tape,
			Recording = recording,
		};
		if (recording != null) {
			var clock = world.Globals.Clock;
			recording.TickDelta = () => clock.TickDelta;
		}

		AxisKeys = keyboardInput.AxisKeys;
		Simulation = new Es1Simulation(world, input, viewChain);

		// Sim_StartMission's step 4 sets the panels' two hooks to SimInput_PushDisable and SimInput_PopEnable.
		if (viewChain?.Manager is { } manager) {
			Simulation.Panels = new Es1AlertGlobals(manager.Display, manager.Mouse, input, widgets, content) {
				EnterHook = AxisKeys.PushDisable,
				LeaveHook = AxisKeys.PopEnable,
				InstallRoot = installRoot,
				Sound = world.Globals.Sound,
			};
			manager.Cockpit.Panels = Simulation.Panels;
		}
	}

	public Es1Simulation Simulation { get; }

	/// <summary>The keyboard's axis keys, which <c>SimInput_SetEnabled</c> turns on and off.</summary>
	public Es1AxisKeys AxisKeys { get; }

	/// <summary>
	/// One host frame of <paramref name="elapsed"/> where nothing shows the monitor, as with no display: the clock and,
	/// when a tick is due, the whole tick. With no display no panel is raised, so the tick never waits.
	/// </summary>
	/// <exception cref="InvalidOperationException">The tick waited, which only a panel's loop does.</exception>
	public Frame AdvanceHostFrame(TimeSpan elapsed) {
		if (!AdvanceClock(elapsed)) {
			return Frame.NoTick;
		}

		var ended = new StrongBox<bool>();
		foreach (var _ in Tick(ended)) {
			throw new InvalidOperationException("The tick waited with nothing to show the monitor.");
		}

		return ended.Value ? Frame.MissionEnded : Frame.Ticked;
	}

	/// <summary>
	/// Host time of <paramref name="elapsed"/> with nothing waiting: the keyboard's typematic, then the frame's steps
	/// before the draw, the frame's sounds and the clock. Returns whether a tick is due.
	///
	/// <para>While a tape plays <c>Sim_Main</c> skips <c>Time_BeginSimTick</c>, so the PC timer runs on and the clock
	/// measures no tick. <b>Not retail:</b> retail's playback then ticks as fast as its frames are drawn; this one holds
	/// each frame that ticks for its recorded <c>SimTickDelta</c> (<see cref="Es1TapePlayback.SecondsOf"/>), the host
	/// time with nothing waiting and the time the frame stood waiting both counting (<see cref="Hold"/>), so a tape plays
	/// in the time it was recorded in. A tick is due once the time held reaches the next frame's delta, at once past the
	/// tape's end, whose short read ends it. The user's choice, plan decision 20 (docs/herculan/plan-es1-first-mission.md#decisions);
	/// a frame a panel's loop reads is not held, the loop running one pass a host frame (decision 11).</para>
	/// </summary>
	public bool AdvanceClock(TimeSpan elapsed) {
		_hostKeyboard.Advance(elapsed.TotalSeconds);

		// Subsystem phase 4 calls no function (docs/es1/simulation/main-loop.md#the-subsystem-phases).
		if (Simulation.Input.Tape is not { Playing: true } tape) {
			if (!Simulation.Globals.Clock.AdvanceHostFrame(elapsed)) {
				return false;
			}

			FrameSoundTick();
			return true;
		}

		Simulation.Globals.Clock.Hold(elapsed);
		HoldTowardsTape(elapsed);
		double due = tape.Peek() is { } next ? Es1TapePlayback.SecondsOf(next.TickDelta) : 0;
		if (_tapeHeld < due) {
			return false;
		}

		_tapeHeld -= due;
		FrameSoundTick();
		return true;
	}

	/// <summary>
	/// <c>Sim_FrameSoundTick</c> (<c>00058574</c>) (docs/es1/simulation/main-loop.md#the-frames-sounds--sim_framesoundtick-00058574),
	/// once a pass of <c>Sim_Main</c>'s loop, which here is a host frame that ticks: retail's comes before
	/// <c>Time_BeginSimTick</c>'s wait, which touches no sound. Its last step, <c>Sound_UpdateListener</c>(<c>ViewObjectPtr</c>)
	/// (<see cref="Sound.Es1Sound.UpdateListener"/>).
	/// Before it, the engine hum: in the view manager's outside view (its <c>+0x10</c> 4)
	/// <see cref="Sound.Es1Sound.HumRow"/> stopped while it plays, in any other view started while neither it nor
	/// <see cref="Sound.Es1Sound.StartRow"/> plays. With no view manager, where retail always has one, the hum's step is
	/// skipped, an engine guard.
	/// PLACEHOLDER: its first step, <c>Music_Tick</c> (item 7's module D).
	/// </summary>
	private void FrameSoundTick() {
		var sound = Simulation.Globals.Sound;
		if (Simulation.ViewChain.Manager is { } views) {
			bool humming = sound.IsPlaying(Sound.Es1Sound.HumRow);
			if (views.CurrentView == View.Es1CockpitViews.ExternalView) {
				if (humming) {
					sound.Stop(Sound.Es1Sound.HumRow);
				}
			} else if (!humming && !sound.IsPlaying(Sound.Es1Sound.StartRow)) {
				sound.Play(Sound.Es1Sound.HumRow);
			}
		}

		sound.UpdateListener(Simulation.ViewChain.Camera);
	}

	/// <summary>
	/// <c>Sim_Main</c>'s loop test, then <see cref="Es1Simulation.MainTick"/>: with <c>DemoAbort</c> set, which the axis
	/// keys under <c>DemoMode</c> and the end of a tape set, the loop is left before the tick, and the mission ends.
	/// </summary>
	public IEnumerable<Es1Wait> Tick(StrongBox<bool> ended) {
		if (AxisKeys.DemoAbort) {
			ended.Value = true;
			return [];
		}

		return Simulation.MainTick(ended);
	}

	/// <summary>
	/// Host time of <paramref name="elapsed"/> while the frame stands waiting, for the monitor or in a panel's loop: the
	/// keyboard's typematic and the PC timer run on, and no tick runs (<see cref="Es1SimClock.Hold"/>); a playing tape
	/// holds the time towards its next frame (<see cref="AdvanceClock"/>).
	/// </summary>
	public void Hold(TimeSpan elapsed) {
		_hostKeyboard.Advance(elapsed.TotalSeconds);
		Simulation.Globals.Clock.Hold(elapsed);
		if (Simulation.Input.TapePlayback) {
			HoldTowardsTape(elapsed);
		}
	}

	/// <summary>The time held towards the tape's next frame, up to <see cref="MaxTapeHeldSeconds"/>.</summary>
	private void HoldTowardsTape(TimeSpan elapsed) =>
		_tapeHeld = Math.Min(_tapeHeld + elapsed.TotalSeconds, MaxTapeHeldSeconds);

	/// <inheritdoc/>
	public bool Frozen => Simulation.Frozen;

	public void Dispose() => _hostKeyboard.Detach();
}
