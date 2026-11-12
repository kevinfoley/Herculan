using HercWorks.Core.Io;
using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
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
	private readonly IKeyboard? _keyboard;
	private readonly Es1HostMouse? _mouse;
	private readonly MappedController _controller;
	private readonly View.Es1ViewChain? _viewChain;
	private readonly Panels.Es1WidgetResources? _widgets;
	private readonly GameContent? _content;
	private readonly Es1TapePlayback? _tape;
	private readonly Es1TapeRecording? _recording;
	private readonly Es1FullScreenKey? _fullScreenKey;
	private int _frames;

	/// <param name="preferences">The install's <c>data\prefs.cfg</c>.</param>
	/// <param name="viewChain">The views and cameras the mission start built, handed to the simulation.</param>
	/// <param name="widgets">What <c>Widget_LoadStylesAndFonts</c> loaded at the mission start, handed to the panels.</param>
	/// <param name="content">The volumes the panels' own files are read from.</param>
	/// <param name="tape">The tape <c>-p</c> or <c>-D</c> opened, handed to the simulation.</param>
	/// <param name="recording">The tape <c>-r</c> started, handed to the simulation.</param>
	/// <param name="fullScreenKey">The window's [Alt+Enter], handed to the simulation's keyboard.</param>
	public Es1SimulationStart(Es1World world, string installRoot, Es1Preferences preferences, IInputContext input, IKeyboard? keyboard, Es1HostMouse? mouse, View.Es1ViewChain? viewChain = null,
			Panels.Es1WidgetResources? widgets = null, GameContent? content = null, Es1TapePlayback? tape = null,
			Es1TapeRecording? recording = null, Es1FullScreenKey? fullScreenKey = null) {
		_fullScreenKey = fullScreenKey;
		_tape = tape;
		_recording = recording;
		_viewChain = viewChain;
		_widgets = widgets;
		_content = content;
		_world = world;
		_installRoot = installRoot;
		_preferences = preferences;
		_keyboard = keyboard;
		_mouse = mouse;
		// The player's maps, which the install's copy seeds while they are missing; with no map for the device its defaults apply.
		_controller = new MappedController(input,
			JoystickMapFile.LoadForPlayer(CaseInsensitivePath.Combine(installRoot, Es1MissionFiles.DataFolder)));
	}

	/// <summary>The runner, once built.</summary>
	public Es1SimulationRunner? Runner { get; private set; }

	/// <summary>
	/// The window whose HERCULAN menu bar is over the mission: the runner's keyboard gives the bar its keys
	/// (<see cref="Es1HostKeyboard.GiveMenuBarItsKeys"/>), and the port is muted while the bar or one of its windows is up
	/// (<see cref="Es1ControllerPort.Muted"/>). With none, every key and the whole controller are the mission's.
	/// </summary>
	public FrontEndWindow? MenuBarWindow { get; init; }

	/// <summary>Whether HERCULAN's pause menu is on screen, which mutes the port as the menu bar does; none never is.</summary>
	public Func<bool>? PauseMenuShowing { get; init; }

	/// <summary>The game port over the host's controller, once the runner is built.</summary>
	public Es1ControllerPort? Port { get; private set; }

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
		Runner = new Es1SimulationRunner(_world, _installRoot, _preferences, _keyboard, _mouse, port, _viewChain, _widgets,
			_content, _tape, _recording, _fullScreenKey);
		Runner.Simulation.Input.Custom = port.ReadCustom;
		Runner.CustomKeyActions = port.CustomKeyActions;
		Port = port;
		var pauseMenu = PauseMenuShowing;
		if (MenuBarWindow is { } window) {
			port.Muted = () => window.MenuBarUp || pauseMenu?.Invoke() == true;
			Runner.HostKeyboard.GiveMenuBarItsKeys(window);
		} else if (pauseMenu != null) {
			port.Muted = pauseMenu;
		}

		Console.WriteLine(Describe(port));
		return Runner;
	}

	public void Dispose() => Runner?.Dispose();

	/// <summary>The controller found, option 7's model at the mission start, and the port axes that answer.</summary>
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
			$"(from data\\prefs.cfg){rudder}; " +
			$"port axes answering: {(answering.Length > 0 ? string.Join(", ", answering) : "none")}.";
	}
}
