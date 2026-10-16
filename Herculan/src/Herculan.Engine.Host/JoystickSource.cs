using HercWorks.Core.Io;
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
/// <para><b>Two GLFW behaviours shape the rest.</b> It publishes a fixed sixteen slots, each either a
/// joystick or a gamepad (<see cref="ControllerDevice"/>), and leaves the unused ones disconnected, so a
/// device has to be picked by connectedness and not by index. And a connected joystick reports <b>zero</b>
/// axes, buttons and hats until the first <c>Update</c> — the counts arrive a frame after
/// <c>CreateInput</c>. So the map is derived lazily, from whatever the device is reporting now, and
/// re-derived whenever that changes; deriving one at load time maps nothing at all.</para>
/// </summary>
sealed class JoystickSource : IJoystickSource {
	private readonly IInputContext _input;

	/// <summary>The install's maps, one per kind of device, as read at the start or saved by the Controller window.</summary>
	private JoystickMapFile _file;

	/// <summary>The map <see cref="_file"/> holds for <see cref="_device"/>, or null to derive one from the device.</summary>
	private JoystickDeviceMap? _saved;

	/// <summary>The derived map, and the device shape it was derived for.</summary>
	private JoystickDeviceMap? _derived;
	private (int Axes, int Buttons, int Hats) _derivedFor;

	private ControllerDevice? _device;

	/// <summary>Prints every axis and button as it moves, so a player can fill in the map by hand.</summary>
	private readonly bool _probe;
	private readonly float[] _lastAxes = Array.Empty<float>();
	private readonly bool[] _lastButtons = Array.Empty<bool>();

	private bool _announced;

	private bool _muted;

	// Set when Muted comes down, and cleared once the device holds no button: see Muted.
	private bool _awaitingRelease;

	/// <summary>Where <see cref="_file"/> is saved, or null when there is no data folder.</summary>
	private readonly string? _mapPath;

	private JoystickSource(IInputContext input, JoystickMapFile file, string? mapPath, bool probe) {
		_input = input;
		_file = file;
		_mapPath = mapPath;
		PersistGamepadControls();
		_probe = probe;

		if (probe) {
			_lastAxes = new float[64];
			_lastButtons = new bool[64];
		}
	}

	/// <summary>
	/// Opens the joystick layer. <paramref name="dataDirectory"/> is the game's <c>data</c> folder,
	/// where the map file lives beside <c>keyjoy.cfg</c>; for a device the file has no map for, the
	/// device's own reported shape supplies the defaults, once it reports one.
	///
	/// <para>Returns a source even when nothing is attached — a stick plugged in later is picked up on
	/// a following frame, and until then <see cref="Capabilities"/> reads
	/// <see cref="JoystickCapabilities.None"/>.</para>
	/// </summary>
	public static JoystickSource Open(IInputContext input, string? dataDirectory, bool probe) {
		string? mapPath = dataDirectory is null ? null : CaseInsensitivePath.Combine(dataDirectory, JoystickMapFile.FileName);
		return new(input, mapPath is null ? new JoystickMapFile() : JoystickMapFile.Load(mapPath), mapPath, probe);
	}

	/// <summary>
	/// What each control does in place of <c>prefs.cfg</c>'s CONTROLS settings
	/// (<see cref="Content.SimulatorPreferences.ControlsOverride"/>): the file's gamepad settings while the device being
	/// read is a gamepad, and null otherwise.
	/// </summary>
	public GamepadControls? ControlsOverride => Device is { IsGamepad: true } ? _file.Gamepad : null;

	/// <summary>
	/// The device being read: the first connected one the file has a map for, otherwise the first
	/// connected one (<see cref="ControllerDevice.Pick"/>). Null when nothing is attached.
	/// </summary>
	public ControllerDevice? Device {
		get {
			if (_device is not { IsConnected: true }) {
				_device = ControllerDevice.Pick(_input, _file);
				_saved = _device?.MapIn(_file);
			}

			return _device;
		}
	}

	/// <summary>
	/// The map in force — the one the install's file holds for the device, or one derived from what the
	/// device currently reports. Null until a device has reported a shape, which is not the same thing
	/// as being connected.
	/// </summary>
	public JoystickDeviceMap? Map {
		get {
			if (Device is not { } device) {
				return null;
			}

			if (_saved is not null) {
				return _saved;
			}

			var shape = (device.AxisCount, device.ButtonCount, device.HatCount);
			if (shape is (0, 0, 0)) {
				// Connected, but the counts have not arrived yet. Deriving now would map nothing.
				return null;
			}

			if (_derived is null || shape != _derivedFor) {
				_derived = JoystickDeviceMap.ForDevice(shape.Item1, shape.Item2, shape.Item3);
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
		_file = file;
		_saved = null;
		PersistGamepadControls();
		_derived = null;
		_device = null;
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

		return Read(map, device);
	}

	/// <summary>
	/// Whether the map's radar toggle is held (<see cref="JoystickDeviceMap.RadarToggle"/>). It is quiet whenever
	/// <see cref="Read"/> reports the stick neutral: while <see cref="Muted"/>, and after it until every button is let go.
	/// </summary>
	public bool RadarToggleHeld() =>
		!Muted && !_awaitingRelease && Device is { } device && Map is { } map && RadarToggleHeld(map, device);

	/// <summary>Whether <paramref name="map"/>'s radar toggle is held on <paramref name="device"/>, a button the host keeps reading as up.</summary>
	public static bool RadarToggleHeld(JoystickDeviceMap map, ControllerDevice device) =>
		map.RadarToggle.Pressed(device.Axis, index => !device.Reserved(index) && device.Button(index));

	/// <summary>
	/// One frame of <paramref name="device"/> read through <paramref name="map"/>, a button the host keeps
	/// (<see cref="ControllerDevice.Reserved"/>) reading as up.
	/// </summary>
	public static JoystickReading Read(JoystickDeviceMap map, ControllerDevice device) =>
		map.Read(device.Axis, index => !device.Reserved(index) && device.Button(index), device.Hat(map.HatIndex));

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

		// The lever's mode and the RAZOR's pitch sense live in the map but are read through the bindings, the
		// control law having no route to the map. Derived maps never set them, so this only ever carries a file's
		// own choice.
		bindings.BipolarThrottle = map.BipolarThrottle;
		bindings.InvertRazorPitch = map.InvertRazorPitch;

		// Only when a stick really answered: with none attached the panel keeps whatever --joystick staged,
		// which is the whole point of that flag.
		if (controlsPanel is not null && Capabilities.Present) {
			controlsPanel.Capabilities = Capabilities;
		}

		if (_probe) {
			Console.WriteLine("Move one control at a time; put what it prints into "
				+ $"data\\{JoystickMapFile.FileName}, or set it up in the Controller window.");
		}

		// The map in force becomes the device's own section, the file's other sections kept.
		if (writeMap && dataDirectory is not null && Device is { } device) {
			string mapPath = CaseInsensitivePath.Combine(dataDirectory, JoystickMapFile.FileName);
			try {
				_file.Set(device.IsGamepad, device.Guid, device.Name, map);
				_file.Save(mapPath);
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

		yield return $"* {(device.IsGamepad ? "gamepad" : "joystick")} [{device.Slot}] {device.Name}: {device.AxisCount} axes, "
			+ $"{device.ButtonCount} buttons, {device.HatCount} hats";

		if (device.ButtonCount > JoystickCapabilities.MaxButtons) {
			yield return $"  The retail format binds the first {JoystickCapabilities.MaxButtons} "
				+ "buttons and has no room for the rest.";
		}

		if (_saved is null && !device.IsGamepad) {
			yield return "  Axis order is the driver's, not a meaning — a throttle and a twist grip can "
				+ "come out swapped. The Controller window, or --joystick-probe and "
				+ $"data\\{JoystickMapFile.FileName}, settle it.";
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
		_file.Gamepad.Persist = _mapPath is null ? null : () => {
			try {
				_file.Save(_mapPath);
			} catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
				Console.Error.WriteLine($"Could not write {_mapPath}: {error.Message}");
			}
		};
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
