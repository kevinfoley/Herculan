using Herculan.Engine.Input;

namespace Herculan.Es1.Input;

/// <summary>
/// <b>This engine's own:</b> a modern controller on the game port, its mapping onto the port's four axes and four
/// buttons this engine's, not retail's. The controller is the one <see cref="MappedController"/> picks, read through
/// <see cref="JoystickDeviceMap.ReadPositions"/>, whose −128 to +128 per role is the calibrated value the port gives
/// (<see cref="IEs1GamePort"/>).
///
/// <para>The port axes take the map's roles as the game port's own wiring puts the 1990s controls on its lines
/// (docs/es1/simulation/joystick.md#the-classes, docs/es1/simulation/joystick.md#what-each-configuration-reads): axis 0
/// the stick X role, 1 the stick Y role, 2 the rudder role (a second stick's X, the RCS pedals), and 3 the throttle role
/// (a second stick's Y, the Flightstick's throttle, the FCS's hat line). An axis answers detection while a device is
/// attached, its map is known and the map gives that role an axis. Buttons 0–3 are the map's logical buttons 1–4, a
/// button the device keeps for the host (<see cref="ControllerDevice.Reserved"/>) reading up.</para>
/// </summary>
public sealed class Es1ControllerPort : IEs1GamePort {
	private readonly Func<JoystickDeviceMap?> _map;
	private readonly Func<int, float> _axis;
	private readonly Func<int, bool> _button;

	/// <param name="map">The map in force, or null while no device is attached or its map is not known.</param>
	/// <param name="axis">A device axis as a fraction of its travel, −1 to +1, 0 for one it does not have.</param>
	/// <param name="button">Whether a device button is held, false for one the host keeps.</param>
	public Es1ControllerPort(Func<JoystickDeviceMap?> map, Func<int, float> axis, Func<int, bool> button) {
		_map = map;
		_axis = axis;
		_button = button;
	}

	/// <summary>The port over <paramref name="controller"/>'s device and map.</summary>
	public static Es1ControllerPort Over(MappedController controller) => new(
		() => controller.Map,
		index => controller.Device?.Axis(index) ?? 0f,
		index => controller.Device?.BindableButton(index) == true);

	/// <inheritdoc/>
	public bool AxisAnswers(int axis) => _map() is { } map && RoleAxis(map, axis) != JoystickDeviceMap.Unmapped;

	/// <inheritdoc/>
	public int ReadAxis(int axis) {
		if (Read() is not { } positions) {
			return 0;
		}

		return axis switch {
			0 => positions.StickX,
			1 => positions.StickY,
			2 => positions.Rudder,
			3 => positions.Throttle,
			_ => 0,
		};
	}

	/// <inheritdoc/>
	public byte Buttons => Read() is { } positions ? positions.Buttons : (byte)0;

	/// <summary>
	/// What the port reads through <paramref name="map"/>: each role's axis in <see cref="JoystickReading.RawFull"/> units
	/// and buttons 0–3, the map's logical buttons 1–4.
	/// </summary>
	/// <param name="map">The map the device is read through.</param>
	/// <param name="axis">A device axis as a fraction of its travel, −1 to +1, 0 for one it does not have.</param>
	/// <param name="button">Whether a device button is held, false for one the host keeps.</param>
	public static JoystickPositions Read(JoystickDeviceMap map, Func<int, float> axis, Func<int, bool> button) {
		// PLACEHOLDER: the controller's hat is put on no port line. Which hat direction each of the record's bytes
		// +0x14-+0x17 is stays Open (docs/es1/simulation/joystick.md#open), until the retail check it lists under
		// DOSBox-X's joysticktype=fcs with option 7 at 3 and joysticktype=ch with option 7 at 5.
		var positions = map.ReadPositions(axis, button, JoystickHat.None);
		return positions with { Buttons = (byte)(positions.Buttons & 0xf) };
	}

	private JoystickPositions? Read() => _map() is { } map ? Read(map, _axis, _button) : null;

	/// <summary>The device axis the map gives the role port axis <paramref name="axis"/> carries.</summary>
	private static int RoleAxis(JoystickDeviceMap map, int axis) => axis switch {
		0 => map.StickXAxis,
		1 => map.StickYAxis,
		2 => map.RudderAxis,
		3 => map.ThrottleAxis,
		_ => JoystickDeviceMap.Unmapped,
	};
}
