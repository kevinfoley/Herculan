using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using HercWorks.Core.Io.Read;

namespace Herculan.Engine.Input;

/// <summary>
/// How a device's controls reach the game: through the game's own controls panel, or through a
/// <see cref="ControllerProfile"/>. <b>This engine's own</b>; each joystick in <see cref="JoystickMapFile"/> has one, and a
/// gamepad is always <see cref="Custom"/>.
/// </summary>
public enum ControllerMode {
	/// <summary>
	/// The game's own controls panel says what each control does, over <see cref="JoystickDeviceMap"/>'s slots, as retail
	/// reads a stick. A joystick's section of <see cref="JoystickMapFile"/> written before modes existed reads as this.
	/// </summary>
	Original,

	/// <summary>
	/// <see cref="ControllerProfile"/> binds each axis to a game function and each input to an action. A gamepad is always
	/// in this mode, and a joystick the file has no section for starts in it.
	/// </summary>
	Custom,
}

/// <summary>A game function an axis drives in a Custom profile: four for a HERC, four for a RAZOR.</summary>
public enum AxisFunction {
	/// <summary>A HERC's steering.</summary>
	Steer,

	/// <summary>A HERC's throttle, read as <see cref="ControllerProfile.ThrottleReading"/> says.</summary>
	Throttle,

	/// <summary>A HERC's turret twist.</summary>
	TurretTwist,

	/// <summary>A HERC's turret pitch.</summary>
	TurretPitch,

	/// <summary>A RAZOR's roll.</summary>
	RazorRoll,

	/// <summary>A RAZOR's pitch.</summary>
	RazorPitch,

	/// <summary>A RAZOR's yaw.</summary>
	RazorYaw,

	/// <summary>A RAZOR's throttle, read as <see cref="ControllerProfile.RazorThrottleReading"/> says.</summary>
	RazorThrottle,
}

/// <summary>How a throttle axis is read.</summary>
public enum ThrottleReading {
	/// <summary>The axis moves the throttle up and down at a rate, and the throttle stays where it is left.</summary>
	Rate,

	/// <summary>The axis is a lever whose position is the throttle, end to end.</summary>
	Lever,

	/// <summary>
	/// The axis is a lever whose centre is idle, forward of it forward and aft of it reverse, as
	/// <see cref="JoystickDeviceMap.BipolarThrottle"/>. A HERC's alone: a RAZOR's lever is already signed end to end.
	/// </summary>
	Bipolar,
}

/// <summary>The device axis driving one <see cref="AxisFunction"/>, with its sense and its centre band.</summary>
/// <param name="Index">The device axis, 0-based, or <see cref="JoystickDeviceMap.Unmapped"/>.</param>
/// <param name="Invert">Whether the function reads the axis the other way round.</param>
/// <param name="Deadzone">The centre band, in <see cref="JoystickReading.RawFull"/> units, as <see cref="JoystickDeviceMap.Deadzone"/>.</param>
public readonly record struct ControllerAxis(int Index, bool Invert = false, int Deadzone = JoystickReading.Deadzone) {
	/// <summary>No axis drives the function.</summary>
	public static readonly ControllerAxis Unbound = new(JoystickDeviceMap.Unmapped);

	/// <summary>Whether a device axis drives the function.</summary>
	public bool IsBound => Index >= 0;
}

/// <summary>
/// A device's Custom profile (<see cref="ControllerMode.Custom"/>): which device axis drives each game function, and which
/// action each button, axis end and hat direction sends, one list for a HERC and one for a RAZOR. <b>This engine's own</b>,
/// for both games (docs/herculan/plan-controller-profiles.md); <i>Metaltech: Earthsiege</i> reads the HERC's half. One
/// profile is part of one section of <see cref="JoystickMapFile"/>, beside the section's <see cref="JoystickDeviceMap"/>,
/// which Original mode reads.
///
/// <para>An axis's <see cref="ControllerAxis.Invert"/> is against the sense the game function reads with no invert, which
/// is the sense a game's own panel gives that function from a stick with no invert.</para>
/// </summary>
public sealed record ControllerProfile {
	/// <summary>A HERC's four functions, in the order the file lists them.</summary>
	public static readonly ImmutableArray<AxisFunction> HercFunctions =
		[AxisFunction.Steer, AxisFunction.Throttle, AxisFunction.TurretTwist, AxisFunction.TurretPitch];

	/// <summary>A RAZOR's four functions, in the order the file lists them.</summary>
	public static readonly ImmutableArray<AxisFunction> RazorFunctions =
		[AxisFunction.RazorRoll, AxisFunction.RazorPitch, AxisFunction.RazorYaw, AxisFunction.RazorThrottle];

	private const string AxisPrefix = "Axis.";
	private const string HercPrefix = "Herc.";
	private const string RazorPrefix = "Razor.";
	private const string ThrottleReadingKey = "ThrottleReading";
	private const string RazorThrottleReadingKey = "RazorThrottleReading";

	private static readonly ImmutableHashSet<KeyAction> Offered = [.. ControllerActions.Offered.Select(entry => entry.Action)];

	/// <summary>
	/// A gamepad's Select/Back, which opens the menu bar in every profile (<see cref="WithGamepadMenuBar"/>), as it does over the
	/// front end, where no profile is read (<see cref="Platform.GamepadHostButtons"/>).
	/// </summary>
	public static readonly ControllerInput GamepadMenuBarInput = ControllerInput.Button(ControllerDevice.GamepadBack);

	/// <summary>The bound functions; one not here has no axis.</summary>
	public ImmutableDictionary<AxisFunction, ControllerAxis> Axes { get; init; } = ImmutableDictionary<AxisFunction, ControllerAxis>.Empty;

	/// <summary>How a HERC's <see cref="AxisFunction.Throttle"/> axis is read.</summary>
	public ThrottleReading ThrottleReading { get; init; }

	/// <summary>
	/// How a RAZOR's <see cref="AxisFunction.RazorThrottle"/> axis is read: <see cref="ThrottleReading.Rate"/> or
	/// <see cref="ThrottleReading.Lever"/>; a file's <c>Bipolar</c> reads as the lever.
	/// </summary>
	public ThrottleReading RazorThrottleReading { get; init; }

	/// <summary>The action each input sends in a HERC.</summary>
	public ImmutableDictionary<ControllerInput, KeyAction> HercBindings { get; init; } = ImmutableDictionary<ControllerInput, KeyAction>.Empty;

	/// <summary>The action each input sends in a RAZOR.</summary>
	public ImmutableDictionary<ControllerInput, KeyAction> RazorBindings { get; init; } = ImmutableDictionary<ControllerInput, KeyAction>.Empty;

	/// <summary>The axis driving <paramref name="function"/>, or <see cref="ControllerAxis.Unbound"/>.</summary>
	public ControllerAxis Axis(AxisFunction function) => Axes.GetValueOrDefault(function, ControllerAxis.Unbound);

	/// <summary>
	/// A gamepad's default profile, read in GLFW's standard layout, the same in both machines
	/// (docs/herculan/joystick-config.md, "A gamepad's own controls"): Back, Start and the sticks' clicks on the menu bar,
	/// the pause menu and the two radials, and the d-pad on the four views.
	/// </summary>
	public static ControllerProfile ForGamepad() {
		var bindings = new Dictionary<ControllerInput, KeyAction>(HatViews(0)) {
			[ControllerInput.Axis(5, 1)] = KeyAction.Fire,
			[ControllerInput.Axis(4, 1)] = KeyAction.NextWeapon,
			[ControllerInput.Button(ControllerDevice.GamepadA)] = KeyAction.NextTarget,
			[ControllerInput.Button(ControllerDevice.GamepadB)] = KeyAction.RadarMode,
			[ControllerInput.Button(ControllerDevice.GamepadX)] = KeyAction.LinkWeapon,
			[ControllerInput.Button(ControllerDevice.GamepadY)] = KeyAction.NextChain,
			[ControllerInput.Button(ControllerDevice.GamepadLeftBumper)] = KeyAction.ShieldsRear,
			[ControllerInput.Button(ControllerDevice.GamepadRightBumper)] = KeyAction.ShieldsFront,
			[GamepadMenuBarInput] = KeyAction.MenuBar,
			[ControllerInput.Button(ControllerDevice.GamepadStart)] = KeyAction.PauseMenu,
			[ControllerInput.Button(ControllerDevice.GamepadLeftStick)] = KeyAction.SquadOrdersWheel,
			[ControllerInput.Button(ControllerDevice.GamepadRightStick)] = KeyAction.ScreensWheel,
		}.ToImmutableDictionary();

		// The senses are Original's with the gamepad's map: the right stick's Y is inverted for the turret, and retail
		// negates a RAZOR's stick Y on its THROTTLE / YAW row, which the left stick's Y is on.
		return new ControllerProfile {
			Axes = new Dictionary<AxisFunction, ControllerAxis> {
				[AxisFunction.Steer] = new(0),
				[AxisFunction.Throttle] = new(1),
				[AxisFunction.TurretTwist] = new(2),
				[AxisFunction.TurretPitch] = new(3, Invert: true),
				[AxisFunction.RazorYaw] = new(0),
				[AxisFunction.RazorThrottle] = new(1, Invert: true),
				[AxisFunction.RazorRoll] = new(2),
				[AxisFunction.RazorPitch] = new(3),
			}.ToImmutableDictionary(),
			HercBindings = bindings,
			RazorBindings = bindings,
		};
	}

	/// <summary>
	/// A joystick's default profile for the counts it reports: ES2's RECOMMEND (<c>Input_RecommendedBindings</c>,
	/// <c>0045a258</c>; docs/retail/simulation/preferences.md#recommend) over its first four axes and eight buttons, with
	/// the first hat on the four views where RECOMMEND puts it on the turret, and the RAZOR's buttons from RECOMMEND's
	/// flying half. A control the device does not report is left unbound.
	///
	/// <para>RECOMMEND's axis rows are JOYSTICK on movement, no throttle lever and RUDDER on the turret pair, so axis 0 and
	/// 1 steer and work the throttle at a rate in a HERC and roll and pitch a RAZOR, axis 3 twists a HERC's turret and yaws
	/// a RAZOR, and axis 2 is unbound. The walking BUTTON 6 is <c>NEXT WEAPON</c>, the code RECOMMEND names, where retail's
	/// own RECOMMEND stores <c>LINK WEAPON</c> (preferences.md, "Rejected readings").</para>
	/// </summary>
	public static ControllerProfile ForJoystick(int axisCount, int buttonCount, int hatCount) {
		KeyAction[] walking = [
			KeyAction.Fire, KeyAction.NextTarget, KeyAction.CenterTurret, KeyAction.AutoTurretTracking,
			KeyAction.NextChain, KeyAction.NextWeapon, KeyAction.ShieldsFront, KeyAction.ShieldsRear,
		];
		KeyAction[] flying = [
			KeyAction.Fire, KeyAction.NextTarget, KeyAction.LinkWeapon, KeyAction.MfdNextScreen,
			KeyAction.NextChain, KeyAction.NextWeapon, KeyAction.ShieldsFront, KeyAction.ShieldsRear,
		];

		var axes = new Dictionary<AxisFunction, ControllerAxis>();
		void Bind(AxisFunction function, int axis) {
			if (axis < axisCount) {
				axes[function] = new ControllerAxis(axis);
			}
		}

		Bind(AxisFunction.Steer, 0);
		Bind(AxisFunction.Throttle, 1);
		Bind(AxisFunction.TurretTwist, 3);
		Bind(AxisFunction.RazorRoll, 0);
		Bind(AxisFunction.RazorPitch, 1);
		Bind(AxisFunction.RazorYaw, 3);

		ImmutableDictionary<ControllerInput, KeyAction> Buttons(KeyAction[] actions) =>
			Enumerable.Range(0, Math.Min(buttonCount, actions.Length))
				.Select(button => KeyValuePair.Create(ControllerInput.Button(button), actions[button]))
				.Concat(hatCount > 0 ? HatViews(0) : Enumerable.Empty<KeyValuePair<ControllerInput, KeyAction>>())
				.ToImmutableDictionary();

		return new ControllerProfile {
			Axes = axes.ToImmutableDictionary(),
			HercBindings = Buttons(walking),
			RazorBindings = Buttons(flying),
		};
	}

	/// <summary>
	/// This profile as a gamepad reads it: <see cref="GamepadMenuBarInput"/> on the menu bar in both machines, whatever the
	/// file says, and no other input on it, so a gamepad's Select/Back opens the menu bar in a mission as it does everywhere else.
	/// </summary>
	public ControllerProfile WithGamepadMenuBar() => this with {
		HercBindings = Fixed(HercBindings),
		RazorBindings = Fixed(RazorBindings),
	};

	private static ImmutableDictionary<ControllerInput, KeyAction> Fixed(ImmutableDictionary<ControllerInput, KeyAction> bindings) =>
		bindings.Where(entry => entry.Value != KeyAction.MenuBar).ToImmutableDictionary()
			.SetItem(GamepadMenuBarInput, KeyAction.MenuBar);

	/// <summary>The default profile for a device: <see cref="ForGamepad"/> or <see cref="ForJoystick"/>.</summary>
	public static ControllerProfile ForDevice(bool isGamepad, int axisCount, int buttonCount, int hatCount) =>
		isGamepad ? ForGamepad() : ForJoystick(axisCount, buttonCount, hatCount);

	private static IEnumerable<KeyValuePair<ControllerInput, KeyAction>> HatViews(int hat) => [
		KeyValuePair.Create(ControllerInput.Hat(hat, JoystickHat.North), KeyAction.ViewUp),
		KeyValuePair.Create(ControllerInput.Hat(hat, JoystickHat.South), KeyAction.ViewDown),
		KeyValuePair.Create(ControllerInput.Hat(hat, JoystickHat.West), KeyAction.ViewLeft),
		KeyValuePair.Create(ControllerInput.Hat(hat, JoystickHat.East), KeyAction.ViewRight),
	];

	/// <summary>
	/// The profile one section of <see cref="JoystickMapFile"/> holds, or null when the section has no Custom settings and
	/// the device's default (<see cref="ForDevice"/>) applies. A section with any holds exactly what it says: a function
	/// it does not name has no axis and an input it does not name sends nothing, which is how a binding removed from a
	/// default stays removed. A line that does not parse, or names an action no control binds, is skipped.
	/// </summary>
	public static ControllerProfile? FromValues(Dictionary<string, string> values) {
		bool HasPrefix(string key, string prefix) => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
		if (!values.Keys.Any(key => HasPrefix(key, AxisPrefix) || HasPrefix(key, HercPrefix) || HasPrefix(key, RazorPrefix)
				|| key.Equals(ThrottleReadingKey, StringComparison.OrdinalIgnoreCase)
				|| key.Equals(RazorThrottleReadingKey, StringComparison.OrdinalIgnoreCase))) {
			return null;
		}

		var axes = new Dictionary<AxisFunction, ControllerAxis>();
		foreach (var function in HercFunctions.Concat(RazorFunctions)) {
			string key = AxisPrefix + function;
			int index = IniSection.IntValue(values, key, JoystickDeviceMap.Unmapped);
			if (index >= 0) {
				axes[function] = new ControllerAxis(
					index,
					IniSection.BoolValue(values, key + ".Invert", false),
					Math.Clamp(IniSection.IntValue(values, key + ".Deadzone", JoystickReading.Deadzone), 0, JoystickReading.RawFull - 1));
			}
		}

		var razorReading = Reading(RazorThrottleReadingKey);
		return new ControllerProfile {
			Axes = axes.ToImmutableDictionary(),
			ThrottleReading = Reading(ThrottleReadingKey),
			RazorThrottleReading = razorReading == ThrottleReading.Bipolar ? ThrottleReading.Lever : razorReading,
			HercBindings = Bindings(HercPrefix),
			RazorBindings = Bindings(RazorPrefix),
		};

		ThrottleReading Reading(string key) =>
			values.TryGetValue(key, out string? text) && Enum.TryParse(text, ignoreCase: true, out ThrottleReading reading)
				&& Enum.IsDefined(reading) && !char.IsDigit(text.Trim()[0])
				? reading
				: ThrottleReading.Rate;

		ImmutableDictionary<ControllerInput, KeyAction> Bindings(string prefix) {
			var bindings = new Dictionary<ControllerInput, KeyAction>();
			foreach (var (key, text) in values) {
				if (HasPrefix(key, prefix) && ControllerInput.Parse(key[prefix.Length..]) is { } input
					&& text.Length > 0 && char.IsLetter(text[0])
					&& Enum.TryParse(text, ignoreCase: true, out KeyAction action) && Offered.Contains(action)) {
					bindings[input] = action;
				}
			}

			return bindings.ToImmutableDictionary();
		}
	}

	/// <summary>Writes the settings <see cref="FromValues"/> reads, comments and all, into one section of <see cref="JoystickMapFile"/>.</summary>
	public void WriteValues(StringBuilder text) {
		text.AppendLine(";; Custom: what each control does in either game while Mode is Custom.");
		text.AppendLine(";; Axis.<function> is a device axis, or -1 for none, with its .Invert and its");
		text.AppendLine($";; .Deadzone, of {JoystickReading.RawFull}. A HERC's are Steer, Throttle, TurretTwist and");
		text.AppendLine(";; TurretPitch; a RAZOR's RazorRoll, RazorPitch, RazorYaw and RazorThrottle.");
		foreach (var function in HercFunctions.Concat(RazorFunctions)) {
			var axis = Axis(function);
			string key = AxisPrefix + function;
			text.AppendLine($"{key} = {axis.Index.ToString(CultureInfo.InvariantCulture)}");
			if (axis.IsBound) {
				text.AppendLine($"{key}.Invert = {(axis.Invert ? "on" : "off")}");
				text.AppendLine($"{key}.Deadzone = {axis.Deadzone.ToString(CultureInfo.InvariantCulture)}");
			}
		}

		text.AppendLine($"{$"{ThrottleReadingKey} = {ThrottleReading}",-28} ;; Rate, Lever or Bipolar");
		text.AppendLine($"{$"{RazorThrottleReadingKey} = {RazorThrottleReading}",-28} ;; Rate or Lever");
		text.AppendLine();
		text.AppendLine(";; Herc.<input> and Razor.<input> send an action in each machine. An input is");
		text.AppendLine(";; button3, axis5+ (axis 5 pushed past halfway toward its + end), or hat0up,");
		text.AppendLine(";; hat0right, hat0down or hat0left. An input not listed sends nothing.");
		WriteBindings(HercPrefix, HercBindings);
		WriteBindings(RazorPrefix, RazorBindings);

		void WriteBindings(string prefix, ImmutableDictionary<ControllerInput, KeyAction> bindings) {
			foreach (var (input, action) in bindings.OrderBy(entry => entry.Key)) {
				text.AppendLine($"{prefix}{input} = {action}");
			}
		}
	}
}
