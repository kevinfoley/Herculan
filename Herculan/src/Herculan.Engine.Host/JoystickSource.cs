using Herculan.Engine.Cockpit;
using Herculan.Engine.Input;

using Silk.NET.Input;

namespace Herculan.Engine.Host;

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
/// <para><b>Two GLFW behaviours shape the rest.</b> It publishes a fixed sixteen joystick slots and
/// leaves the unused ones reporting <see cref="IJoystick.IsConnected"/> false, so a device has to be
/// picked by connectedness and not by index. And a connected device reports <b>zero</b> axes, buttons
/// and hats until the first <c>Update</c> — the counts arrive a frame after <c>CreateInput</c>. So the
/// map is derived lazily, from whatever the device is reporting now, and re-derived whenever that
/// changes; deriving one at load time maps nothing at all.</para>
/// </summary>
sealed class JoystickSource : IJoystickSource {
	private readonly IInputContext _input;

	/// <summary>The map read from the install, or null to derive one from the device.</summary>
	private readonly JoystickDeviceMap? _configured;

	/// <summary>The derived map, and the device shape it was derived for.</summary>
	private JoystickDeviceMap? _derived;
	private (int Axes, int Buttons, int Hats) _derivedFor;

	private IJoystick? _device;

	/// <summary>Prints every axis and button as it moves, so a player can fill in the map by hand.</summary>
	private readonly bool _probe;
	private readonly float[] _lastAxes = Array.Empty<float>();
	private readonly bool[] _lastButtons = Array.Empty<bool>();

	private bool _announced;

	private JoystickSource(IInputContext input, JoystickDeviceMap? configured, bool probe) {
		_input = input;
		_configured = configured;
		_probe = probe;

		if (probe) {
			_lastAxes = new float[64];
			_lastButtons = new bool[64];
		}
	}

	/// <summary>
	/// Opens the joystick layer. <paramref name="dataDirectory"/> is the game's <c>data</c> folder,
	/// where the map file lives beside <c>keyjoy.cfg</c>; with no file there the device's own reported
	/// shape supplies the defaults, once it reports one.
	///
	/// <para>Returns a source even when nothing is attached — a stick plugged in later is picked up on
	/// a following frame, and until then <see cref="Capabilities"/> reads
	/// <see cref="JoystickCapabilities.None"/>.</para>
	/// </summary>
	public static JoystickSource Open(IInputContext input, string? dataDirectory, bool probe) =>
		new(input,
			dataDirectory is null
				? null
				: JoystickDeviceMap.Load(Path.Combine(dataDirectory, JoystickDeviceMap.FileName)),
			probe);

	/// <summary>
	/// The device being read: the slot the configured map names when that one is connected, otherwise
	/// the first connected slot. Null when nothing is attached.
	/// </summary>
	public IJoystick? Device {
		get {
			if (_device is { IsConnected: true }) {
				return _device;
			}

			_device = Pick();
			if (_device is not null) {
				// Silk's own deadzone off: the original's band is applied in JoystickDeviceMap.Read, and
				// two stacked deadzones would eat a fifth of the travel twice over.
				_device.Deadzone = new Deadzone(0f, DeadzoneMethod.Traditional);
			}

			return _device;
		}
	}

	/// <summary>
	/// The map in force — the install's, or one derived from what the device currently reports. Null
	/// until a device has reported a shape, which is not the same thing as being connected.
	/// </summary>
	public JoystickDeviceMap? Map {
		get {
			if (_configured is not null) {
				return _configured;
			}

			if (Device is not { } device) {
				return null;
			}

			var shape = (device.Axes.Count, device.Buttons.Count, device.Hats.Count);
			if (shape is (0, 0, 0)) {
				// Connected, but the counts have not arrived yet. Deriving now would map nothing.
				return null;
			}

			if (_derived is null || shape != _derivedFor) {
				_derived = JoystickDeviceMap.ForDevice(shape.Item1, shape.Item2, shape.Item3,
					IndexOf(device));
				_derivedFor = shape;
			}

			return _derived;
		}
	}

	/// <summary>
	/// What the CONTROLS panel greys its rows against. Nothing attached, or a device that has not
	/// reported its shape yet, reads <see cref="JoystickCapabilities.None"/> — which greys all twelve,
	/// the same thing retail shows for a stick it cannot enumerate.
	/// </summary>
	public JoystickCapabilities Capabilities =>
		Device is null || Map is not { } map ? JoystickCapabilities.None : map.Capabilities();

	/// <inheritdoc/>
	public JoystickReading Read() {
		if (Device is not { } device || Map is not { } map) {
			return JoystickReading.Neutral;
		}

		if (_probe) {
			Probe(device);
		}

		return map.Read(
			index => index >= 0 && index < device.Axes.Count ? device.Axes[index].Position : 0f,
			index => index >= 0 && index < device.Buttons.Count && device.Buttons[index].Pressed,
			map.HatIndex >= 0 && map.HatIndex < device.Hats.Count
				? HatOf(device.Hats[map.HatIndex].Position)
				: JoystickHat.None);
	}

	/// <summary>
	/// Says what the stick can do, once — and not before it will answer. Silk.NET's GLFW backend publishes a
	/// connected device a frame before it publishes that device's axis, button and hat counts, so this waits
	/// for a map to exist rather than running at load. Anything keyed off the device's shape has to wait with
	/// it: the derived map itself, the CONTROLS panel's capabilities, and --write-joystick-map.
	/// </summary>
	public void Announce(JoystickBindings bindings, ControlsPanel? controlsPanel, bool writeMap, string? dataDirectory) {
		if (_announced || Map is not { } map) {
			return;
		}

		_announced = true;

		foreach (string line in Describe()) {
			Console.WriteLine(line);
		}

		// The lever's mode lives in the map but is read through the bindings, the control law having no
		// route to the map. Derived maps never set it, so this only ever carries a file's own choice.
		bindings.BipolarThrottle = map.BipolarThrottle;

		// Only when a stick really answered: with none attached the panel keeps whatever --joystick staged,
		// which is the whole point of that flag.
		if (controlsPanel is not null && Capabilities.Present) {
			controlsPanel.Capabilities = Capabilities;
		}

		if (_probe) {
			Console.WriteLine("Move one control at a time; put what it prints into "
				+ $"data\\{JoystickDeviceMap.FileName}.");
		}

		if (writeMap && dataDirectory is not null) {
			string mapPath = Path.Combine(dataDirectory, JoystickDeviceMap.FileName);
			try {
				map.Save(mapPath);
				Console.WriteLine($"Wrote {mapPath}.");
			} catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
				Console.WriteLine($"Could not write {mapPath}: {error.Message}");
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

		yield return $"* joystick [{IndexOf(device)}] {device.Name}: {device.Axes.Count} axes, "
			+ $"{device.Buttons.Count} buttons, {device.Hats.Count} hats";

		if (device.Buttons.Count > JoystickCapabilities.MaxButtons) {
			yield return $"  The retail format binds the first {JoystickCapabilities.MaxButtons} "
				+ "buttons and has no room for the rest.";
		}

		if (_configured is null) {
			yield return "  Axis order is the driver's, not a meaning — a throttle and a twist grip can "
				+ $"come out swapped. --joystick-probe and data\\{JoystickDeviceMap.FileName} settle it.";
		}

		if (!_probe) {
			yield break;
		}

		// Where each axis is sitting before anything has been touched. A self-centring control reads
		// about zero; one parked hard over is a lever.
		for (int i = 0; i < device.Axes.Count; i++) {
			float resting = device.Axes[i].Position;
			yield return $"  at rest: axis {i} = {resting,6:0.00}"
				+ (MathF.Abs(resting) > 0.5f
					? " — parked at a stop, so a lever rather than a self-centring control"
					: string.Empty);
		}
	}

	/// <summary>The configured slot when it is connected, else the first connected one.</summary>
	private IJoystick? Pick() {
		int wanted = _configured?.DeviceIndex ?? -1;
		if (wanted >= 0 && wanted < _input.Joysticks.Count
			&& _input.Joysticks[wanted] is { IsConnected: true } named) {
			return named;
		}

		foreach (var stick in _input.Joysticks) {
			if (stick.IsConnected) {
				return stick;
			}
		}

		return null;
	}

	private int IndexOf(IJoystick device) {
		for (int i = 0; i < _input.Joysticks.Count; i++) {
			if (ReferenceEquals(_input.Joysticks[i], device)) {
				return i;
			}
		}

		return 0;
	}

	/// <summary>
	/// Silk's own eight-way hat position, cut back to the four the format has bytes for.
	/// <see cref="JoystickDeviceMap"/> decides what happens to a diagonal — retail drops it.
	/// </summary>
	private static JoystickHat HatOf(Position2D position) => position switch {
		Position2D.Up => JoystickHat.North,
		Position2D.Down => JoystickHat.South,
		Position2D.Left => JoystickHat.West,
		Position2D.Right => JoystickHat.East,
		Position2D.UpLeft => JoystickHat.North | JoystickHat.West,
		Position2D.UpRight => JoystickHat.North | JoystickHat.East,
		Position2D.DownLeft => JoystickHat.South | JoystickHat.West,
		Position2D.DownRight => JoystickHat.South | JoystickHat.East,
		_ => JoystickHat.None,
	};

	/// <summary>Reports an axis that moved appreciably or a button that went down, once per change.</summary>
	private void Probe(IJoystick device) {
		for (int i = 0; i < device.Axes.Count && i < _lastAxes.Length; i++) {
			float position = device.Axes[i].Position;
			if (MathF.Abs(position - _lastAxes[i]) > 0.2f) {
				_lastAxes[i] = position;
				Console.WriteLine($"joystick: axis {i} = {position,6:0.00}");
			}
		}

		for (int i = 0; i < device.Buttons.Count && i < _lastButtons.Length; i++) {
			bool pressed = device.Buttons[i].Pressed;
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
