using Herculan.Engine.Input;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;
using Silk.NET.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// <b>This engine's own:</b> builds the <see cref="Es1SimulationRunner"/>, and with it the joystick devices on the host's
/// controller (<see cref="Es1ControllerPort"/>), once the attached controller can be detected, and says on the console
/// what was found.
///
/// <para>Retail detects the stick once, when <c>Prefs_Init</c> builds the devices, and each device asks the port which
/// axes answer once, as it is built (docs/es1/simulation/joystick.md#building-the-devices). A GLFW joystick reports its
/// axes, buttons and hats only from the input update after it is published, and the host's input context may update
/// after the window's own frame handler, so a runner built in the window's <c>Load</c> or on the first frame could find a
/// connected joystick with no map and every axis silent. So the runner is built on the first frame after the first,
/// once no controller is attached or the picked one's map is known, waiting at most <see cref="MaxWaitFrames"/> frames
/// for a device that never reports a shape. Detection then holds for the rest of the run: a controller plugged in later
/// is not detected, as in retail.</para>
/// </summary>
public sealed class Es1SimulationStart : IDisposable {
	/// <summary>The most frames the start waits for the picked controller to report its shape.</summary>
	public const int MaxWaitFrames = 10;

	private readonly Es1World _world;
	private readonly string _installRoot;
	private readonly Es1Preferences _preferences;
	private readonly bool _stickModelFromFlag;
	private readonly IKeyboard? _keyboard;
	private readonly IMouse? _mouse;
	private readonly MappedController _controller;
	private int _frames;

	/// <param name="preferences">The install's <c>data\prefs.cfg</c>, with <c>--stick-model</c>'s option 7 applied.</param>
	/// <param name="stickModelFromFlag">Whether option 7 came from <c>--stick-model</c>.</param>
	public Es1SimulationStart(Es1World world, string installRoot, Es1Preferences preferences, bool stickModelFromFlag,
			IInputContext input, IKeyboard? keyboard, IMouse? mouse) {
		_world = world;
		_installRoot = installRoot;
		_preferences = preferences;
		_stickModelFromFlag = stickModelFromFlag;
		_keyboard = keyboard;
		_mouse = mouse;
		// The install's maps, read and never written; with no file the device's defaults apply.
		_controller = new MappedController(input, JoystickMapFile.Load(Es1MissionFiles.DataPath(installRoot, JoystickMapFile.FileName)));
	}

	/// <summary>The runner, once built.</summary>
	public Es1SimulationRunner? Runner { get; private set; }

	/// <summary>Once a host frame, before the tick: builds the runner when the controller can be detected, and returns it once built.</summary>
	public Es1SimulationRunner? Advance() {
		if (Runner != null) {
			return Runner;
		}

		_frames++;
		if (_frames < 2 || (_controller.Device != null && _controller.Map == null && _frames <= MaxWaitFrames)) {
			return null;
		}

		var port = Es1ControllerPort.Over(_controller);
		Runner = new Es1SimulationRunner(_world, _installRoot, _preferences, _keyboard, _mouse, port);
		Console.WriteLine(Describe(port));
		return Runner;
	}

	public void Dispose() => Runner?.Dispose();

	/// <summary>The controller found, option 7's model and where it came from, and the port axes that answer.</summary>
	private string Describe(Es1ControllerPort port) {
		string controller = _controller.Device is { } device
			? $"{(device.IsGamepad ? "gamepad" : "joystick")} \"{device.Name}\""
			: "none";
		byte option7 = _preferences.StickConfiguration;
		string model = option7 switch {
			1 => "IMJoystick1",
			2 => "IMJoystick1 and IMJoystick2",
			3 => "IMThrustmasterFCS",
			4 => "IMFlightstick",
			5 => "IMFlightstickPro",
			_ => "no stick",
		};

		string rudder = Runner?.Simulation.Input.Joysticks.Second is Es1ThrustmasterRcsDevice ? ", option 9 IMThrustmasterRCS" : "";
		var answering = Enumerable.Range(0, 4).Where(port.AxisAnswers).ToArray();
		return $"Controller: {controller}; option 7 = {option7}, {model} " +
			$"(from {(_stickModelFromFlag ? "--stick-model" : "data\\prefs.cfg")}){rudder}; " +
			$"port axes answering: {(answering.Length > 0 ? string.Join(", ", answering) : "none")}.";
	}
}
