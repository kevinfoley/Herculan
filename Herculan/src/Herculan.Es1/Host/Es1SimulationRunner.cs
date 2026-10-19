using Herculan.Es1.Input;
using Herculan.Es1.Sim;
using Silk.NET.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// The simulator in a host window: the host's keyboard and mouse into ES1's input (<see cref="Es1HostKeyboard"/>,
/// <see cref="Es1HostMouse"/>), the input build (<see cref="Es1InputBuild"/>), and once a host frame the clock and,
/// when it answers a tick, <see cref="Es1Simulation.MainTick"/>, in the order of <c>Sim_Main</c>'s frame
/// (docs/es1/simulation/main-loop.md#the-frame). It is built once the window's devices are known, since the keyboard
/// seeds its modifiers from the host's.
/// </summary>
public sealed class Es1SimulationRunner : IDisposable {
	private readonly Es1HostKeyboard _hostKeyboard;
	private readonly Es1HostMouse _hostMouse = new();

	/// <param name="preferences">The install's <c>data\prefs.cfg</c> as the simulator reads it.</param>
	public Es1SimulationRunner(Es1World world, string installRoot, Es1Preferences preferences, IKeyboard? keyboard,
			IMouse? mouse) {
		var keyboardInput = new Es1KeyboardInput(Es1HostKeyboard.ReadBiosShiftFlags(keyboard));
		_hostKeyboard = new Es1HostKeyboard(keyboardInput.Keyboard);
		if (keyboard != null) {
			_hostKeyboard.Attach(keyboard);
		}

		if (mouse != null) {
			_hostMouse.Attach(mouse);
		}

		var input = new Es1InputBuild(keyboardInput, _hostMouse, preferences, Es1KeyjoyConfig.Load(installRoot),
			new Es1ControlsState(preferences));
		Simulation = new Es1Simulation(world, input);
	}

	public Es1Simulation Simulation { get; }

	/// <summary>
	/// One host frame of <paramref name="deltaSeconds"/>: the keyboard's typematic, then the frame's steps before the
	/// draw, the frame's sounds, the clock and, when a tick is due, the tick. Returns whether the tick ended the mission.
	/// </summary>
	public bool AdvanceHostFrame(double deltaSeconds) {
		_hostKeyboard.Advance(deltaSeconds);

		// Subsystem phase 4 calls no function (docs/es1/simulation/main-loop.md#the-subsystem-phases).
		// PLACEHOLDER: Sim_FrameSoundTick (00058574), the music, the engine hum and the listener; no plan step ports
		// ES1's sound yet.
		return Simulation.Globals.Clock.AdvanceHostFrame(TimeSpan.FromSeconds(deltaSeconds)) && Simulation.MainTick();
	}

	public void Dispose() {
		_hostKeyboard.Detach();
		_hostMouse.Detach();
	}
}
