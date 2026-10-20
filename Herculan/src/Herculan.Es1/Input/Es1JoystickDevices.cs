namespace Herculan.Es1.Input;

/// <summary>
/// The simulator's two joystick devices, kinds 3 and 4, as <c>Input_CreateJoystickDevices</c> builds them, with the
/// once-a-tick poll that times the port for both and the read of each device's record
/// (docs/es1/simulation/joystick.md#the-joystick-devices, docs/es1/simulation/input.md#the-live-block--input_polldeviceblock-0004c2d0).
/// </summary>
public sealed class Es1JoystickDevices {
	/// <summary>The input device kinds of the first and second joystick.</summary>
	public const int FirstKind = 3, SecondKind = 4;

	/// <summary>The response mode <c>Input_CreateJoystickDevices</c> gives every device: 1, the squared response.</summary>
	private const int SquaredResponse = 1;

	/// <summary>The normalisation's shift <c>Joystick_LoadCalibration</c> sets.</summary>
	private const ushort CalibratedShift = 7;

	private readonly IEs1GamePort _port;

	private Es1JoystickDevices(IEs1GamePort port) {
		_port = port;
	}

	/// <summary>The device recorded for kind 3, the stick option 7 names; null for none.</summary>
	public Es1JoystickDevice? First { get; private set; }

	/// <summary>The device recorded for kind 4: <c>IMJoystick2</c> under option 7 = 2, the rudder pedals with option 9; null for none.</summary>
	public Es1JoystickDevice? Second { get; private set; }

	/// <summary>The globals the two devices share, which outlive a rebuild of the devices as retail's globals do.</summary>
	public Es1JoystickGlobals Globals { get; } = new();

	/// <summary>
	/// <c>Input_CreateJoystickDevices</c> (<c>ES1DBSIM 0004af0c</c>), at the end of <c>Prefs_Init</c>: the devices
	/// <see cref="Build"/> builds on <paramref name="port"/>.
	/// </summary>
	public static Es1JoystickDevices Create(Es1Preferences preferences, IEs1GamePort port) {
		var devices = new Es1JoystickDevices(port);
		devices.Build(preferences);
		return devices;
	}

	/// <summary>
	/// <c>ControlsPanel_ApplyDevices</c>'s (<c>00048e98</c>) rebuild
	/// (docs/es1/simulation/preferences.md#rebuilding-the-joystick-devices--controlspanel_applydevices-00048e98): the
	/// first and the second device destroyed (<see cref="Es1JoystickDevice.Destroy"/>), then built again for options 7
	/// and 9 as the mission start builds them (<see cref="Build"/>), on the port they were detected on. The calibration
	/// files are not read, as at the mission start (plan decision 9), where retail's rebuild loads <c>data\joy1.cal</c>
	/// and, from the current directory, <c>joy2.cal</c>.
	///
	/// <para><b>This engine's departure</b> (plan decision 14): a device the rebuild does not build anew is left
	/// destroyed, so <c>NONE</c> leaves no stick enabled. Retail keeps the destroyed devices' pointers and loads and
	/// enables them where it builds none in their place, the first for <c>NONE</c> and the second for every model but
	/// <c>2 STANDARD JOYSTICKS</c> without the rudder, so the freed device is polled again; what it then reads is the
	/// doc's Open item.</para>
	/// </summary>
	public void Rebuild(Es1Preferences preferences) {
		First?.Destroy();
		Second?.Destroy();
		First = null;
		Second = null;
		Build(preferences);
	}

	/// <summary>
	/// The devices <c>Input_CreateJoystickDevices</c> builds: the first device option 7 names, then, with option 9 set,
	/// <c>IMThrustmasterRCS</c> as the second unless option 7 is 2, where it clears option 9 instead; every device with
	/// mode 1. Each device's calibration is then loaded and the device enabled, whether or not its axes answered
	/// (docs/es1/simulation/joystick.md#building-the-devices).
	/// </summary>
	private void Build(Es1Preferences preferences) {
		var port = _port;
		var globals = Globals;
		byte option7 = preferences.StickConfiguration;
		Es1JoystickDevice? first = option7 switch {
			1 or 2 => new Es1Joystick1Device(SquaredResponse, port, globals),
			3 => new Es1ThrustmasterFcsDevice(SquaredResponse, port, globals),
			4 => new Es1FlightstickDevice(SquaredResponse, port, globals),
			5 => new Es1FlightstickProDevice(SquaredResponse, port, globals),
			_ => null,
		};

		Es1JoystickDevice? second = option7 == 2 ? new Es1Joystick2Device(SquaredResponse, port, globals) : null;
		if (preferences.ThirdAxis) {
			if (option7 == 2) {
				preferences.ClearThirdAxis();
			} else {
				second = new Es1ThrustmasterRcsDevice(SquaredResponse, port, globals);
			}
		}

		foreach (var device in new[] { first, second }) {
			if (device == null) {
				continue;
			}

			// PLACEHOLDER: Joystick_LoadCalibration (0004ae0c) reads data\joy1.cal or data\joy2.cal (or its default block)
			// into the joystick object's calibration block, which the calibration panel (00049a7c and its neighbours,
			// 00048e98, 00049bbc, 00049f30, 0004a0e8) measures and saves; the port gives calibrated values instead, so
			// only the loader's shift 7 is set. No plan step retires the panel or the files.
			device.Joystick.Shift = CalibratedShift;
			device.Enable();
		}

		First = first;
		Second = second;
	}

	/// <summary>
	/// <c>Input_PollJoysticks</c> (<c>0004bd90</c>): with one device, the first or else the second, its poll; with two,
	/// the first's poll over every claimed axis, its mask and count put back after, and what it read copied into the
	/// second's joystick object, so one read of the port serves both
	/// (docs/es1/simulation/input.md#the-live-block--input_polldeviceblock-0004c2d0).
	/// </summary>
	public void Poll() {
		var first = First ?? Second;
		var second = First != null ? Second : null;
		if (first == null) {
			return;
		}

		var joystick = first.Joystick;
		if (second == null) {
			joystick.Poll();
			return;
		}

		byte savedMask = (byte)joystick.AxisMask, savedCount = (byte)joystick.AxisCount;
		joystick.AxisMask = Globals.ClaimedAxes;
		joystick.AxisCount = Globals.ClaimedAxisCount;
		joystick.Poll();
		joystick.AxisMask = savedMask;
		joystick.AxisCount = savedCount;
		second.Joystick.CopyPollFrom(joystick);
	}

	/// <summary>
	/// <c>Input_ReadJoystickState</c> (<c>0004c4ac</c>)(kind, record): a copy of the record slot <c>+4</c> of the
	/// device's vtable returns, or null, the answer 0, for no device of <paramref name="kind"/> or one not enabled
	/// (docs/es1/simulation/input.md#the-live-block--input_polldeviceblock-0004c2d0).
	/// </summary>
	public Es1JoystickRecord? ReadJoystickState(int kind) {
		var device = kind switch {
			FirstKind => First,
			SecondKind => Second,
			_ => null,
		};

		return device is { InputEnabled: true } ? device.ReadState().Clone() : null;
	}
}
