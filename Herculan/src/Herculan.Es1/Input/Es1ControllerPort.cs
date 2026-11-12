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
	private readonly Func<bool> _anyButton;
	private readonly MappedController? _controller;

	// The Custom profile read for the device, and the device and shape it was read for.
	private Es1CustomControls? _custom;
	private (int Slot, int Axes, int Buttons, int Hats) _customFor;

	// The port was muted at the last read, and since it came back every device button has not yet been let go.
	private bool _wasMuted;
	private bool _awaitingRelease;

	/// <param name="map">The map in force, or null while no device is attached or its map is not known.</param>
	/// <param name="axis">A device axis as a fraction of its travel, −1 to +1, 0 for one it does not have.</param>
	/// <param name="button">Whether a device button is held, false for one the host keeps.</param>
	/// <param name="anyButton">Whether any device button is held, the host's own among them; none answers false.</param>
	/// <param name="controller">The controller whose Custom profile <see cref="Custom"/> reads; none reads none.</param>
	public Es1ControllerPort(Func<JoystickDeviceMap?> map, Func<int, float> axis, Func<int, bool> button,
			Func<bool>? anyButton = null, MappedController? controller = null) {
		_map = map;
		_axis = axis;
		_button = button;
		_anyButton = anyButton ?? (() => false);
		_controller = controller;
	}

	/// <summary>The port over <paramref name="controller"/>'s device and map, and its Custom profile.</summary>
	public static Es1ControllerPort Over(MappedController controller) => new(
		() => controller.Map,
		index => controller.Device?.Axis(index) ?? 0f,
		index => controller.Device?.BindableButton(index) == true,
		() => controller.Device?.AnyButton() == true,
		controller);

	/// <summary>
	/// <b>This engine's own:</b> the device's Custom profile while it is in Custom mode
	/// (<see cref="JoystickMapFile.ModeFor"/>), as a gamepad always is: its section's profile, or the device's default for
	/// the counts it reports (<see cref="ControllerProfile.ForDevice"/>). Null in Original mode, and until the device has
	/// reported its shape.
	/// </summary>
	public Es1CustomControls? Custom {
		get {
			if (_controller is not { Device: { } device, Map: not null } controller
					|| controller.File.ModeFor(device.IsGamepad, device.Guid) != ControllerMode.Custom) {
				return null;
			}

			var shape = (device.Slot, device.AxisCount, device.ButtonCount, device.HatCount);
			if (_custom is null || shape != _customFor) {
				_custom = new Es1CustomControls(controller.File.ProfileFor(device.IsGamepad, device.Guid)
					?? ControllerProfile.ForDevice(device.IsGamepad, device.AxisCount, device.ButtonCount, device.HatCount));
				_customFor = shape;
			}

			return _custom;
		}
	}

	/// <summary>
	/// The Custom profile's frame for the input build (<see cref="Es1InputBuild.Custom"/>): null in Original mode, and
	/// while the port is quiet a frame with nothing held.
	/// </summary>
	public Es1CustomFrame? ReadCustom() {
		if (Custom is not { } custom || _controller?.Device is not { } device) {
			return null;
		}

		return Quiet() ? new Es1CustomFrame(0, 0, 0, 0, false, new byte[4]) : custom.Read(ControllerState.Of(device));
	}

	/// <summary>The Custom profile's actions held this frame that go in as keys (<see cref="Es1CustomControls.KeyActions"/>); none while quiet.</summary>
	public IEnumerable<KeyAction> CustomKeyActions() =>
		Custom is { } custom && _controller?.Device is { } device && !Quiet() ? custom.KeyActions(ControllerState.Of(device)) : [];

	/// <summary>
	/// The input holding <paramref name="action"/> in the Custom profile, whether or not the port is quiet, for the host's
	/// own actions, which work the menus that quiet it; null in Original mode or with none held.
	/// </summary>
	public ControllerInput? HostActionInput(KeyAction action) =>
		Custom is { } custom && _controller?.Device is { } device ? custom.HeldInput(ControllerState.Of(device), action) : null;

	/// <summary>
	/// Whether <paramref name="device"/> is the port's controller and holds <paramref name="action"/> in its Custom profile
	/// (<see cref="HostActionInput"/>): what opens a radial (<see cref="Engine.Menus.PadRadial.Update"/>).
	/// </summary>
	public bool HostActionHeldOn(ControllerDevice device, KeyAction action) =>
		_controller?.Device is { } read && read.Slot == device.Slot && HostActionInput(action) != null;

	/// <summary>
	/// <b>This engine's own:</b> whether the port reads every axis at 0 and every button up, as it does while the pad works
	/// HERCULAN's menu bar or one of its windows, so that steering through them does not also drive the HERC. After it
	/// comes down the port stays quiet until every device button has been let go, so the button that chose an item does
	/// not fire as the bar closes. With none, the port is never muted.
	/// </summary>
	public Func<bool>? Muted { get; set; }

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

	private JoystickPositions? Read() => _map() is { } map && !Quiet() ? Read(map, _axis, _button) : null;

	// Whether Muted holds the port, or its release wait does.
	private bool Quiet() {
		bool muted = Muted?.Invoke() == true;
		_awaitingRelease |= _wasMuted && !muted;
		_wasMuted = muted;
		if (muted) {
			return true;
		}

		if (_awaitingRelease && _anyButton()) {
			return true;
		}

		_awaitingRelease = false;
		return false;
	}

	/// <summary>The device axis the map gives the role port axis <paramref name="axis"/> carries.</summary>
	private static int RoleAxis(JoystickDeviceMap map, int axis) => axis switch {
		0 => map.StickXAxis,
		1 => map.StickYAxis,
		2 => map.RudderAxis,
		3 => map.ThrottleAxis,
		_ => JoystickDeviceMap.Unmapped,
	};
}
