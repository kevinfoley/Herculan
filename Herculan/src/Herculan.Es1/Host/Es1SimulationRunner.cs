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

	private readonly Es1HostKeyboard _hostKeyboard;

	/// <param name="preferences">The install's <c>data\prefs.cfg</c> as the simulator reads it.</param>
	/// <param name="mouse">The mouse driver, which the host attaches to its mouse; one with no host device when null.</param>
	/// <param name="port">The game port the joystick devices are built on, and detect their axes on, once.</param>
	/// <param name="viewChain">The views and cameras the mission start built, which the tick moves between.</param>
	/// <param name="widgets">What <c>Widget_LoadStylesAndFonts</c> loaded at the mission start, which the panels draw with.</param>
	/// <param name="content">The volumes the panels' own files are read from.</param>
	public Es1SimulationRunner(Es1World world, string installRoot, Es1Preferences preferences, IKeyboard? keyboard,
			Es1HostMouse? mouse, IEs1GamePort port, View.Es1ViewChain? viewChain = null, Es1WidgetResources? widgets = null,
			GameContent? content = null) {
		var keyboardInput = new Es1KeyboardInput(Es1HostKeyboard.ReadBiosShiftFlags(keyboard));
		_hostKeyboard = new Es1HostKeyboard(keyboardInput.Keyboard);
		if (keyboard != null) {
			_hostKeyboard.Attach(keyboard);
		}

		var input = new Es1InputBuild(keyboardInput, mouse ?? new Es1HostMouse(), preferences, Es1KeyjoyConfig.Load(installRoot),
			new Es1ControlsState(preferences), port) {
			CockpitMouse = viewChain?.Manager?.Mouse,
		};
		AxisKeys = keyboardInput.AxisKeys;
		Simulation = new Es1Simulation(world, input, viewChain);

		// Sim_StartMission's step 4 sets the panels' two hooks to SimInput_PushDisable and SimInput_PopEnable.
		if (viewChain?.Manager is { } manager) {
			Simulation.Panels = new Es1AlertGlobals(manager.Display, manager.Mouse, input, widgets, content) {
				EnterHook = AxisKeys.PushDisable,
				LeaveHook = AxisKeys.PopEnable,
				InstallRoot = installRoot,
			};
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
	/// </summary>
	public bool AdvanceClock(TimeSpan elapsed) {
		_hostKeyboard.Advance(elapsed.TotalSeconds);

		// Subsystem phase 4 calls no function (docs/es1/simulation/main-loop.md#the-subsystem-phases).
		// PLACEHOLDER: Sim_FrameSoundTick (00058574), the music, the engine hum and the listener; no plan step ports
		// ES1's sound yet.
		return Simulation.Globals.Clock.AdvanceHostFrame(elapsed);
	}

	/// <inheritdoc/>
	public IEnumerable<Es1Wait> Tick(StrongBox<bool> ended) => Simulation.MainTick(ended);

	/// <summary>
	/// Host time of <paramref name="elapsed"/> while the frame stands waiting, for the monitor or in a panel's loop: the
	/// keyboard's typematic and the PC timer run on, and no tick runs (<see cref="Es1SimClock.Hold"/>).
	/// </summary>
	public void Hold(TimeSpan elapsed) {
		_hostKeyboard.Advance(elapsed.TotalSeconds);
		Simulation.Globals.Clock.Hold(elapsed);
	}

	/// <inheritdoc/>
	public bool Frozen => Simulation.Frozen;

	public void Dispose() => _hostKeyboard.Detach();
}
