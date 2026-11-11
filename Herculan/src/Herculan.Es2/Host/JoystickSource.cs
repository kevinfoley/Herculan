using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Es2.Cockpit;
using Herculan.Es2.Input;

using Silk.NET.Input;

namespace Herculan.Es2.Host;

/// <summary>
/// The modern half of joystick support: finds a device through Silk.NET, reads it every frame and
/// hands the result to <see cref="JoystickDeviceMap"/>, which flattens it into the abstract
/// four-axis, one-hat, eight-button shape the retail bindings are written against.
///
/// <para>This is where <c>joyGetDevCapsA</c>/<c>joyGetPosEx</c> used to be — <c>Joystick_Enumerate</c>
/// (<c>00477568</c>) and <c>Joystick_Poll</c> (<c>00477614</c>). Nothing above this class knows the
/// difference: the capability block it produces has the same five fields the original's does, and the
/// bindings that consume it are the same twelve bytes of <c>prefs.cfg</c>.</para>
///
/// <para><b>Silk.NET reports axes positionally, and that is the layer's one real limitation.</b> GLFW
/// hands over an ordered array of floats with no HID usages attached, so nothing here can tell a
/// throttle from a twist grip — the order is whatever the driver enumerated, and on a HOTAS those two
/// commonly come out swapped against the 1996 convention the retail format assumes. There is no
/// automatic answer to that: HID has no "throttle" usage, so no API reports one. The device map is
/// where a player says which is which; <c>--joystick-probe</c> is how they find out.</para>
///
/// <para><b>Two GLFW behaviours shape the rest.</b> It publishes a fixed sixteen slots, each either a
/// joystick or a gamepad (<see cref="ControllerDevice"/>), and leaves the unused ones disconnected, so a
/// device has to be picked by connectedness and not by index. And a connected joystick reports <b>zero</b>
/// axes, buttons and hats until the first <c>Update</c> — the counts arrive a frame after
/// <c>CreateInput</c>. <see cref="MappedController"/> picks the device and settles its map with both
/// in mind.</para>
/// </summary>
sealed class JoystickSource : IJoystickSource {
	/// <summary>
	/// The device and its map, picked against the player's maps (<see cref="JoystickMapFile.PlayerPath"/>), one per kind
	/// of device, as read at the start or saved by the Controller window.
	/// </summary>
	private readonly MappedController _controller;

	/// <summary>Prints every axis and button as it moves, so a player can fill in the map by hand.</summary>
	private readonly bool _probe;
	private readonly float[] _lastAxes = Array.Empty<float>();
	private readonly bool[] _lastButtons = Array.Empty<bool>();

	private bool _announced;

	private bool _muted;

	// Set when Muted comes down, and cleared once the device holds no button: see Muted.
	private bool _awaitingRelease;

	// Whether the mission pilots a RAZOR, whose half of a Custom profile is read.
	private readonly bool _razor;

	// The device's Custom profile as read, and the device and shape it was built for: see Custom.
	private CustomControls? _custom;
	private (int Slot, int Axes, int Buttons, int Hats) _customFor;

	private JoystickSource(IInputContext input, JoystickMapFile file, bool probe, bool razor) {
		_controller = new MappedController(input, file);
		PersistGamepadControls();
		_probe = probe;
		_razor = razor;

		if (probe) {
			_lastAxes = new float[64];
			_lastButtons = new bool[64];
		}
	}

	/// <summary>
	/// Opens the joystick layer over the player's map file, which <paramref name="dataDirectory"/>, the game's
	/// <c>data</c> folder, seeds while it is missing (<see cref="JoystickMapFile.LoadForPlayer(string?)"/>); for a device
	/// the file has no map for, <see cref="ControllerDevice.Defaults"/> applies, once the device reports its shape.
	///
	/// <para>Returns a source even when nothing is attached — a stick plugged in later is picked up on
	/// a following frame, and until then <see cref="Capabilities"/> reads
	/// <see cref="JoystickCapabilities.None"/>.</para>
	/// </summary>
	/// <param name="razor">Whether the mission pilots a RAZOR, whose half of a Custom profile is read.</param>
	public static JoystickSource Open(IInputContext input, string? dataDirectory, bool probe, bool razor) =>
		new(input, JoystickMapFile.LoadForPlayer(dataDirectory), probe, razor);

	/// <summary>
	/// What each control does in place of <c>prefs.cfg</c>'s CONTROLS settings
	/// (<see cref="Es2.Content.SimulatorPreferences.ControlsOverride"/>): <see cref="Custom"/>'s fixed rows while the device
	/// being read is in Custom mode, and null while it is in Original.
	/// </summary>
	public IControlsOverride? ControlsOverride => Custom;

	/// <summary>
	/// The device's Custom profile, read for the mission's machine, while it is in Custom mode
	/// (<see cref="JoystickMapFile.ModeFor"/>), as a gamepad always is: its section's profile, or the device's default for
	/// the counts it reports (<see cref="ControllerProfile.ForDevice"/>). Null in Original mode, and until the device has
	/// reported its shape.
	/// </summary>
	public CustomControls? Custom {
		get {
			if (Device is not { } device || Map is null
					|| _controller.File.ModeFor(device.IsGamepad, device.Guid) != ControllerMode.Custom) {
				return null;
			}

			var shape = (device.Slot, device.AxisCount, device.ButtonCount, device.HatCount);
			if (_custom is null || shape != _customFor) {
				var profile = _controller.File.ProfileFor(device.IsGamepad, device.Guid)
					?? ControllerProfile.ForDevice(device.IsGamepad, device.AxisCount, device.ButtonCount, device.HatCount);
				_custom = new CustomControls(profile, _razor);
				_customFor = shape;
			}

			return _custom;
		}
	}

	/// <summary>Whether the stick is quiet, as <see cref="Read"/> reports it: while <see cref="Muted"/>, and after it until every button is let go.</summary>
	public bool Quiet => Muted || _awaitingRelease;

	/// <inheritdoc cref="MappedController.Device"/>
	public ControllerDevice? Device => _controller.Device;

	/// <inheritdoc cref="MappedController.Map"/>
	public JoystickDeviceMap? Map => _controller.Map;

	/// <summary>
	/// What the CONTROLS panel greys its rows against. Nothing attached, or a device that has not
	/// reported its shape yet, reads <see cref="JoystickCapabilities.None"/> — which greys all twelve,
	/// the same thing retail shows for a stick it cannot enumerate.
	/// </summary>
	public JoystickCapabilities Capabilities =>
		Device is null || Map is not { } map ? JoystickCapabilities.None : Custom?.Capabilities ?? map.Capabilities();

	/// <summary>
	/// Whether <see cref="Read"/> reports the stick centred and every button up, which it does while the pad is working
	/// something of the host's (<see cref="Simulator.SimulatorHost"/> sets it), so that moving a control to bind it, or
	/// steering through a menu, does not also drive the machine. After it comes down the stick stays quiet until every
	/// device button has been let go, so a button pressed to bind it or to choose an item does not fire its action as
	/// the window closes.
	/// </summary>
	public bool Muted {
		get => _muted;
		set {
			_awaitingRelease |= _muted && !value;
			_muted = value;
		}
	}

	/// <summary>
	/// Reads the stick through <paramref name="file"/>'s maps from now on, as the Controller window saves it, picking the
	/// device again, and says what it can do again on the next <see cref="Announce"/>, so the CONTROLS panel greys its rows
	/// against the new map.
	/// </summary>
	public void Configure(JoystickMapFile file) {
		_controller.Reset(file);
		PersistGamepadControls();
		_custom = null;
		_announced = false;
	}

	/// <inheritdoc/>
	public JoystickReading Read() {
		if (Device is not { } device || Map is not { } map) {
			return JoystickReading.Neutral;
		}

		if (Muted) {
			return JoystickReading.Neutral;
		}

		if (_awaitingRelease) {
			if (device.AnyButton()) {
				return JoystickReading.Neutral;
			}

			_awaitingRelease = false;
		}

		if (_probe) {
			Probe(device);
		}

		return Custom is { } custom ? custom.Read(device) : device.Read(map);
	}

	/// <summary>
	/// Whether the map's radar toggle is held (<see cref="JoystickDeviceMap.RadarToggle"/>), in Original mode; in Custom
	/// the radar is <see cref="KeyAction.RadarMode"/>, one of <see cref="HeldActions"/>. It is quiet whenever
	/// <see cref="Read"/> reports the stick neutral (<see cref="Quiet"/>).
	/// </summary>
	public bool RadarToggleHeld() =>
		!Quiet && Custom is null && Device is { } device && Map is { } map && device.Held(map.RadarToggle);

	/// <summary>
	/// The actions a Custom profile holds this frame that the reading does not carry (<see cref="CustomControls.HeldActions"/>),
	/// for the host to send: none in Original mode, or while the stick is <see cref="Quiet"/>.
	/// </summary>
	public IEnumerable<KeyAction> HeldActions() =>
		!Quiet && Custom is { } custom && Device is { } device ? custom.HeldActions(device) : [];

	/// <summary>
	/// Whether a Custom profile holds <paramref name="action"/>, whether or not the stick is <see cref="Quiet"/>: for the
	/// host's own actions, which work the menus that mute it. False in Original mode.
	/// </summary>
	public bool HostActionHeld(KeyAction action) => HostActionInput(action) != null;

	/// <summary>The input holding <paramref name="action"/>, as <see cref="HostActionHeld"/> reads it, or null for none.</summary>
	public ControllerInput? HostActionInput(KeyAction action) =>
		Custom is { } custom && Device is { } device ? custom.HeldInput(device, action) : null;

	/// <summary>
	/// Says what the stick can do, once — and not before it will answer. Silk.NET's GLFW backend publishes a
	/// connected device a frame before it publishes that device's axis, button and hat counts, so this waits
	/// for a map to exist rather than running at load. Anything keyed off the device's shape has to wait with
	/// it: the derived map itself, the CONTROLS panel's capabilities, and --write-joystick-map.
	/// </summary>
	public void Announce(JoystickBindings bindings, ControlsPanel? controlsPanel, bool writeMap) {
		if (_announced || Map is not { } map) {
			return;
		}

		_announced = true;

		foreach (string line in Describe()) {
			Console.WriteLine(line);
		}

		// The lever's mode and the RAZOR's pitch sense live in the map but are read through the bindings, the
		// control law having no route to the map. Derived maps never set them, so this only ever carries a file's
		// own choice.
		// A Custom profile's axes carry their own senses, so the map's RAZOR pitch is Original's alone.
		var custom = Custom;
		bindings.BipolarThrottle = custom?.BipolarThrottle ?? map.BipolarThrottle;
		bindings.InvertRazorPitch = custom is null && map.InvertRazorPitch;

		// Only when a stick really answered: with none attached the panel keeps whatever --joystick staged,
		// which is the whole point of that flag.
		if (controlsPanel is not null && Capabilities.Present) {
			controlsPanel.Capabilities = Capabilities;
		}

		if (_probe) {
			Console.WriteLine("Move one control at a time; put what it prints into "
				+ $"{JoystickMapFile.PlayerPath}, or set it up in the Controller window.");
		}

		// The map in force becomes the device's own section, the file's other sections kept. A failed write is logged.
		if (writeMap && Device is { } device) {
			_controller.File.Set(device.IsGamepad, device.Guid, device.Name, map);
			if (_controller.File.Save(JoystickMapFile.PlayerPath, out _)) {
				Console.WriteLine($"Wrote {JoystickMapFile.PlayerPath}.");
			}
		}
	}

	/// <summary>
	/// What was found. Only connected sticks are listed — the GLFW backend publishes sixteen slots and
	/// leaves the unused ones disconnected, so listing them all is noise.
	/// </summary>
	public IEnumerable<string> Describe() {
		if (Device is not { } device) {
			yield return "No joystick attached; the CONTROLS panel will grey every row.";
			yield break;
		}

		yield return $"* {(device.IsGamepad ? "gamepad" : "joystick")} [{device.Slot}] {device.Name}: {device.AxisCount} axes, "
			+ $"{device.ButtonCount} buttons, {device.HatCount} hats";

		if (device.ButtonCount > JoystickCapabilities.MaxButtons) {
			yield return $"  The retail format binds the first {JoystickCapabilities.MaxButtons} "
				+ "buttons and has no room for the rest.";
		}

		if (!_controller.MapFromFile && !device.IsGamepad) {
			yield return "  Axis order is the driver's, not a meaning — a throttle and a twist grip can "
				+ "come out swapped. The Controller window, or --joystick-probe and "
				+ $"{JoystickMapFile.PlayerPath}, settle it.";
		}

		if (!_probe) {
			yield break;
		}

		// Where each axis is sitting before anything has been touched. A self-centring control reads
		// about zero; one parked hard over is a lever.
		for (int i = 0; i < device.AxisCount; i++) {
			float resting = device.Axis(i);
			yield return $"  at rest: axis {i} = {resting,6:0.00}"
				+ (MathF.Abs(resting) > 0.5f
					? " — parked at a stop, so a lever rather than a self-centring control"
					: string.Empty);
		}
	}

	// The CONTROLS panel's DONE saves the gamepad's settings by writing the whole file, its other sections kept.
	private void PersistGamepadControls() {
		// A failed write is logged.
		_controller.File.Gamepad.Persist = () => _controller.File.Save(JoystickMapFile.PlayerPath, out _);
	}

	/// <summary>Reports an axis that moved appreciably or a button that went down, once per change.</summary>
	private void Probe(ControllerDevice device) {
		for (int i = 0; i < device.AxisCount && i < _lastAxes.Length; i++) {
			float position = device.Axis(i);
			if (MathF.Abs(position - _lastAxes[i]) > 0.2f) {
				_lastAxes[i] = position;
				Console.WriteLine($"joystick: axis {i} = {position,6:0.00}");
			}
		}

		for (int i = 0; i < device.ButtonCount && i < _lastButtons.Length; i++) {
			bool pressed = device.Button(i);
			if (pressed != _lastButtons[i]) {
				_lastButtons[i] = pressed;
				if (pressed) {
					Console.WriteLine($"joystick: button {i} down"
						+ (i < JoystickCapabilities.MaxButtons
							? $" (BUTTON {i + 1})"
							: " — past the eight the format binds"));
				}
			}
		}
	}
}
