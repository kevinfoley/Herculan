using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>
/// <b>This engine's own:</b> the controller a game reads and the map it is read through, for any game's host. The device
/// is picked lazily (<see cref="ControllerDevice.Pick"/> against <see cref="File"/>) and picked again once it
/// disconnects; the map is the file's for it, or else the device's defaults (<see cref="ControllerDevice.Defaults"/>)
/// once it has reported its shape.
///
/// <para>A GLFW joystick reports no axes, buttons or hats until the input update after it is published, so the defaults
/// are derived from whatever the device reports now and derived again whenever that changes; deriving them when the
/// device is first seen would map nothing.</para>
/// </summary>
public sealed class MappedController {
	private readonly IInputContext _input;

	/// <summary>The map <see cref="File"/> holds for <see cref="_device"/>, or null to derive one from the device.</summary>
	private JoystickDeviceMap? _saved;

	/// <summary>The derived map, and the device shape it was derived for.</summary>
	private JoystickDeviceMap? _derived;
	private (int Axes, int Buttons, int Hats) _derivedFor;

	private ControllerDevice? _device;

	/// <param name="input">The input context whose devices are picked from.</param>
	/// <param name="file">The install's maps, one per kind of device.</param>
	public MappedController(IInputContext input, JoystickMapFile file) {
		_input = input;
		File = file;
	}

	/// <summary>The maps the device and its map are picked against.</summary>
	public JoystickMapFile File { get; private set; }

	/// <summary>Picks against <paramref name="file"/> from now on: the device and its map are picked again when next asked for.</summary>
	public void Reset(JoystickMapFile file) {
		File = file;
		_saved = null;
		_derived = null;
		_device = null;
	}

	/// <summary>
	/// The device being read: the first connected one the file has a map for, otherwise the first
	/// connected one (<see cref="ControllerDevice.Pick"/>). Null when nothing is attached.
	/// </summary>
	public ControllerDevice? Device {
		get {
			if (_device is not { IsConnected: true }) {
				_device = ControllerDevice.Pick(_input, File);
				_saved = _device?.MapIn(File);
			}

			return _device;
		}
	}

	/// <summary>Whether <see cref="Map"/> is the file's map for the device rather than one derived from it.</summary>
	public bool MapFromFile => _saved is not null;

	/// <summary>
	/// The map in force — the one the file holds for the device, or its defaults: a gamepad's own map, or for a joystick
	/// one derived from what it currently reports. Null until a device has reported a shape, which is not the same thing
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

			// The same defaults the Controller window offers: a gamepad's own map, else retail's order for the counts.
			if (_derived is null || shape != _derivedFor) {
				_derived = device.Defaults();
				_derivedFor = shape;
			}

			return _derived;
		}
	}
}
