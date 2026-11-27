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
/// (a second stick's Y, the Flightstick's throttle). An axis answers detection while a device is attached, its map is
/// known and the map gives that role an axis. Buttons 0–3 are the map's logical buttons 1–4, a button the device keeps
/// for the host (<see cref="ControllerDevice.Reserved"/>) reading up.</para>
///
/// <para>The map's hat arrives as the stick option 7 names (<see cref="Wire"/>) sent its own
/// (docs/es1/simulation/joystick.md#the-hat-on-the-port): under the Thrustmaster FCS, on axis 3 in place of the throttle
/// role, which then answers detection whenever the map is known; under the Flightstick Pro, on buttons 0–3, in place of
/// the buttons while it is pushed; under any other stick, on no line. Those sticks sent one direction at a time, so a
/// diagonal reads centred, as Earthsiege 2 reads one, whatever the map's <see cref="JoystickDeviceMap.HatDiagonals"/>
/// says.</para>
/// </summary>
public sealed class Es1ControllerPort : IEs1GamePort {
	/// <summary>Option 7's Thrustmaster FCS and CH Flightstick Pro (docs/es1/simulation/joystick.md#building-the-devices).</summary>
	private const byte ThrustmasterFcs = 3, FlightstickPro = 5;

	/// <summary>
	/// The FCS's hat on axis 3 for up, right, down and left: the line's travel, from up's end to the rest at the other,
	/// over −128 to 0 in five even steps, the middle of each of <c>IMThrustmasterFCS_ReadState</c>'s bands
	/// (docs/es1/simulation/joystick.md#the-hat-on-the-port). Centred reads 0.
	/// </summary>
	private const int FcsHatUp = -JoystickReading.RawFull, FcsHatStep = JoystickReading.RawFull / 4;

	/// <summary>
	/// The Flightstick Pro's hat on buttons 0–3 for up, down, right and left: buttons 0 and 1 with both of 2 and 3, 2
	/// alone, 3 alone or neither (docs/es1/simulation/joystick.md#the-hat-on-the-port).
	/// </summary>
	private const byte ProHatUp = 0xf, ProHatDown = 0x7, ProHatRight = 0xb, ProHatLeft = 0x3;

	private readonly Func<JoystickDeviceMap?> _map;
	private readonly Func<int, float> _axis;
	private readonly Func<int, bool> _button;
	private readonly Func<int, JoystickHat> _hat;
	private readonly Func<bool> _anyButton;
	private readonly MappedController? _controller;

	// Option 7 as Wire last gave it.
	private byte _stickConfiguration;

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
	/// <param name="hat">A device hat's directions, centred for one it does not have; with none, every hat is centred.</param>
	public Es1ControllerPort(Func<JoystickDeviceMap?> map, Func<int, float> axis, Func<int, bool> button,
			Func<bool>? anyButton = null, MappedController? controller = null, Func<int, JoystickHat>? hat = null) {
		_map = map;
		_axis = axis;
		_button = button;
		_hat = hat ?? (_ => JoystickHat.None);
		_anyButton = anyButton ?? (() => false);
		_controller = controller;
	}

	/// <summary>The port over the controller's device and map, and its Custom profile.</summary>
	/// <param name="controller">The controller.</param>
	public static Es1ControllerPort Over(MappedController controller) => new(
		() => controller.Map,
		index => controller.Device?.Axis(index) ?? 0f,
		index => controller.Device?.BindableButton(index) == true,
		() => controller.Device?.AnyButton() == true,
		controller,
		index => controller.Device?.Hat(index) ?? JoystickHat.None);

	/// <summary>
	/// <b>This engine's own:</b> the device's Custom profile while it is in Custom mode
	/// (<see cref="JoystickMapFile.ModeFor"/>), as a gamepad always is: its section's profile, or the device's default for
	/// the counts it reports (<see cref="ControllerProfile.ForDevice"/>). Null in Original mode, and until the device has
	/// reported its shape.
	/// </summary>
	public Es1CustomControls? Custom {
		get {
			if (_controller is not { Device: { } device } controller || !InCustomMode(controller)) {
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
	/// Reads through the file's maps from now on, as the Controller window saves them: the device and its map
	/// are picked again (<see cref="MappedController.Reset"/>), and its Custom profile read again. The joystick devices keep
	/// the axes they detected as they were built (<see cref="AxisAnswers"/>), as retail's do until they are built again.
	/// </summary>
	/// <param name="file">The saved joystick maps.</param>
	public void Configure(JoystickMapFile file) {
		_controller?.Reset(file);
		_custom = null;
	}

	/// <summary>
	/// Whether the controller has a device with a known map that is in Custom mode: what <see cref="Custom"/>
	/// answers a profile for, and what the shell's CONTROLS panel asks of the device the next mission would read.
	/// </summary>
	/// <param name="controller">The controller.</param>
	public static bool InCustomMode(MappedController controller) =>
		controller is { Device: { } device, Map: not null }
		&& controller.File.ModeFor(device.IsGamepad, device.Guid) == ControllerMode.Custom;

	/// <summary>
	/// The Custom profile's frame for the input read (<see cref="Es1PlayerInputReader.Custom"/>): null in Original mode, and
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
	/// The input holding the action in the Custom profile, whether or not the port is quiet, for the host's
	/// own actions, which work the menus that quiet it; null in Original mode or with none held.
	/// </summary>
	/// <param name="action">The action.</param>
	public ControllerInput? HostActionInput(KeyAction action) =>
		Custom is { } custom && _controller?.Device is { } device ? custom.HeldInput(ControllerState.Of(device), action) : null;

	/// <summary>
	/// The input a joystick's Custom profile holds on the menu bar (<see cref="HostActionInput"/>); null on a gamepad,
	/// whose Select/Back the front-end window reads itself (<see cref="Engine.Platform.FrontEndWindow.BeginFrame"/>).
	/// </summary>
	public ControllerInput? JoystickMenuBarInput =>
		_controller?.Device is { IsGamepad: false } ? HostActionInput(KeyAction.MenuBar) : null;

	/// <summary>
	/// Whether the device is the port's controller and holds the action in its Custom profile
	/// (<see cref="HostActionInput"/>): what opens a radial (<see cref="Engine.Menus.PadRadial.Update"/>).
	/// </summary>
	/// <param name="device">The device.</param>
	/// <param name="action">The action.</param>
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
	public void Wire(byte stickConfiguration) => _stickConfiguration = stickConfiguration;

	/// <inheritdoc/>
	public bool AxisAnswers(int axis) => _map() is { } map
		&& ((HatOnAxis3 && axis == 3) || RoleAxis(map, axis) != JoystickDeviceMap.Unmapped);

	/// <inheritdoc/>
	public int ReadAxis(int axis) {
		if (Read() is not { } positions) {
			return 0;
		}

		return axis switch {
			0 => positions.StickX,
			1 => positions.StickY,
			2 => positions.Rudder,
			3 when HatOnAxis3 => positions.Hat switch {
				JoystickHat.North => FcsHatUp,
				JoystickHat.East => FcsHatUp + FcsHatStep,
				JoystickHat.South => FcsHatUp + 2 * FcsHatStep,
				JoystickHat.West => FcsHatUp + 3 * FcsHatStep,
				_ => 0,
			},
			3 => positions.Throttle,
			_ => 0,
		};
	}

	/// <inheritdoc/>
	public byte Buttons {
		get {
			if (Read() is not { } positions) {
				return 0;
			}

			if (_stickConfiguration != FlightstickPro) {
				return positions.Buttons;
			}

			return positions.Hat switch {
				JoystickHat.North => ProHatUp,
				JoystickHat.South => ProHatDown,
				JoystickHat.East => ProHatRight,
				JoystickHat.West => ProHatLeft,
				_ => positions.Buttons,
			};
		}
	}

	/// <summary>
	/// What the port reads through the map: each role's axis in <see cref="JoystickReading.RawFull"/> units,
	/// the hat at one of its four directions or centred, and buttons 0–3, the map's logical buttons 1–4. Which line the
	/// hat goes on is the wiring's (<see cref="Wire"/>).
	/// </summary>
	/// <param name="map">The map the device is read through.</param>
	/// <param name="axis">A device axis as a fraction of its travel, −1 to +1, 0 for one it does not have.</param>
	/// <param name="button">Whether a device button is held, false for one the host keeps.</param>
	/// <param name="hat">A device hat's directions, centred for one it does not have.</param>
	public static JoystickPositions Read(JoystickDeviceMap map, Func<int, float> axis, Func<int, bool> button,
			Func<int, JoystickHat> hat) {
		var positions = map.ReadPositions(axis, button, hat(map.HatIndex));
		var cardinal = positions.Hat is JoystickHat.North or JoystickHat.South or JoystickHat.East or JoystickHat.West
			? positions.Hat
			: JoystickHat.None;
		return positions with { Hat = cardinal, Buttons = (byte)(positions.Buttons & 0xf) };
	}

	// Whether the wiring puts the hat on axis 3, the FCS's.
	private bool HatOnAxis3 => _stickConfiguration == ThrustmasterFcs;

	private JoystickPositions? Read() => _map() is { } map && !Quiet() ? Read(map, _axis, _button, _hat) : null;

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

	/// <summary>The device axis the map gives to a role port axis.</summary>
	/// <param name="map">The device map.</param>
	/// <param name="axis">The role port axis.</param>
	private static int RoleAxis(JoystickDeviceMap map, int axis) => axis switch {
		0 => map.StickXAxis,
		1 => map.StickYAxis,
		2 => map.RudderAxis,
		3 => map.ThrottleAxis,
		_ => JoystickDeviceMap.Unmapped,
	};
}
