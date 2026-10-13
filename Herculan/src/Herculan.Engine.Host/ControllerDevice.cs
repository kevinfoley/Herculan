using Herculan.Engine.Input;
using Silk.NET.GLFW;
using Silk.NET.Input;

namespace Herculan.Engine.Host;

/// <summary>
/// One connected controller as the joystick layer reads it, positional axes, buttons and hats, from whichever of
/// Silk.NET's two device lists it is in.
///
/// <para><b>Silk.NET's GLFW backend puts a device in one list or the other.</b> A device GLFW recognises as a gamepad,
/// which is every Xbox-compatible pad and Steam Input's virtual one, is an <see cref="IGamepad"/>, and the
/// <see cref="IJoystick"/> in the same slot reports itself disconnected. Anything else, such as a stick or a HOTAS, is an
/// <see cref="IJoystick"/> only. Both lists hold all sixteen of GLFW's slots, and a slot number names the same device in
/// either, so the slot is what identifies a connected device. Which model it is, for its saved map, is its
/// <see cref="Guid"/>.</para>
///
/// <para>A gamepad is read in GLFW's standard gamepad layout, which is the same on every platform (see
/// docs/herculan/joystick-config.md, "Gamepads"). A joystick's axes and buttons are in whatever order its driver
/// reports.</para>
/// </summary>
sealed class ControllerDevice {
	/// <summary>A gamepad's axes: the left stick's X and Y, the right stick's X and Y, then the two triggers.</summary>
	private const int GamepadAxes = 6;

	// The GLFW the window and input already run on; GetApi loads the same native library.
	private static readonly Lazy<Glfw> GlfwApi = new(Glfw.GetApi);

	private readonly IJoystick? _joystick;
	private readonly IGamepad? _gamepad;

	private ControllerDevice(int slot, IJoystick? joystick, IGamepad? gamepad) {
		Slot = slot;
		_joystick = joystick;
		_gamepad = gamepad;
	}

	/// <summary>GLFW's slot for the device.</summary>
	public int Slot { get; }

	/// <summary>Whether GLFW reads it as a gamepad, in the standard layout.</summary>
	public bool IsGamepad => _gamepad != null;

	/// <summary>Whether it is still attached.</summary>
	public bool IsConnected => _gamepad?.IsConnected ?? _joystick!.IsConnected;

	/// <summary>
	/// GLFW's GUID for the device's model, which picks its section of <see cref="JoystickMapFile"/>; null when GLFW gives
	/// none. Two of the same model share it, and which port it is plugged into does not change it.
	/// </summary>
	public string? Guid {
		get {
			string? guid = GlfwApi.Value.GetJoystickGUID(Slot);
			return string.IsNullOrWhiteSpace(guid) ? null : guid;
		}
	}

	/// <summary>The name its driver gives, or "?" when it gives none.</summary>
	public string Name => (_gamepad?.Name ?? _joystick!.Name) is { } name && !string.IsNullOrWhiteSpace(name) ? name : "?";

	/// <summary>
	/// How many axes it reports. A joystick reports none until the frame after it connects (see
	/// <see cref="JoystickSource"/>); a gamepad always reports its six.
	/// </summary>
	public int AxisCount => _gamepad != null ? GamepadAxes : _joystick!.Axes.Count;

	/// <summary>How many buttons it reports.</summary>
	public int ButtonCount => _gamepad?.Buttons.Count ?? _joystick!.Buttons.Count;

	/// <summary>How many hats it reports. A gamepad has one, its d-pad.</summary>
	public int HatCount => _gamepad != null ? 1 : _joystick!.Hats.Count;

	/// <summary>Axis <paramref name="index"/> as a fraction of its travel, -1 to +1; 0 for one it does not have.</summary>
	public float Axis(int index) {
		if (_gamepad == null) {
			return index >= 0 && index < _joystick!.Axes.Count ? _joystick.Axes[index].Position : 0f;
		}

		int stick = index / 2;
		return index switch {
			0 or 2 when stick < _gamepad.Thumbsticks.Count => _gamepad.Thumbsticks[stick].X,
			1 or 3 when stick < _gamepad.Thumbsticks.Count => _gamepad.Thumbsticks[stick].Y,
			4 or 5 when index - 4 < _gamepad.Triggers.Count => _gamepad.Triggers[index - 4].Position,
			_ => 0f,
		};
	}

	/// <summary>Whether button <paramref name="index"/> is held; false for one it does not have.</summary>
	public bool Button(int index) {
		var buttons = _gamepad?.Buttons ?? _joystick!.Buttons;
		return index >= 0 && index < buttons.Count && buttons[index].Pressed;
	}

	/// <summary>A gamepad's Back and Start, buttons 6 and 7 of the standard layout.</summary>
	public const int GamepadBack = 6;

	/// <inheritdoc cref="GamepadBack"/>
	public const int GamepadStart = 7;

	/// <summary>
	/// Whether button <paramref name="index"/> is kept off the eight the bindings name: a gamepad's Back and Start,
	/// which are the host's own (<see cref="GamepadHostButtons"/>).
	/// </summary>
	public bool Reserved(int index) => IsGamepad && index is GamepadBack or GamepadStart;

	/// <summary>Whether any button is held.</summary>
	public bool AnyButton() {
		for (int i = 0; i < ButtonCount; i++) {
			if (Button(i)) {
				return true;
			}
		}

		return false;
	}

	/// <summary>Which directions hat <paramref name="index"/> is pushed in, a gamepad's being its d-pad's buttons.</summary>
	public JoystickHat Hat(int index) {
		if (_gamepad == null) {
			return index >= 0 && index < _joystick!.Hats.Count ? HatOf(_joystick.Hats[index].Position) : JoystickHat.None;
		}

		if (index != 0) {
			return JoystickHat.None;
		}

		var hat = JoystickHat.None;
		foreach (var button in _gamepad.Buttons) {
			if (button.Pressed) {
				hat |= button.Name switch {
					ButtonName.DPadUp => JoystickHat.North,
					ButtonName.DPadDown => JoystickHat.South,
					ButtonName.DPadLeft => JoystickHat.West,
					ButtonName.DPadRight => JoystickHat.East,
					_ => JoystickHat.None,
				};
			}
		}

		return hat;
	}

	/// <summary>
	/// The device in <paramref name="slot"/>, or null when nothing is connected there. Silk's own deadzone is turned off:
	/// the original's band is applied in <see cref="JoystickDeviceMap.Read"/>, and two stacked deadzones would eat a fifth
	/// of the travel twice over.
	/// </summary>
	public static ControllerDevice? At(IInputContext input, int slot) {
		var off = new Deadzone(0f, DeadzoneMethod.Traditional);
		if (slot >= 0 && slot < input.Gamepads.Count && input.Gamepads[slot] is { IsConnected: true } gamepad) {
			gamepad.Deadzone = off;
			return new ControllerDevice(slot, null, gamepad);
		}

		if (slot >= 0 && slot < input.Joysticks.Count && input.Joysticks[slot] is { IsConnected: true } joystick) {
			joystick.Deadzone = off;
			return new ControllerDevice(slot, joystick, null);
		}

		return null;
	}

	/// <summary>Every connected device, by slot.</summary>
	public static IEnumerable<ControllerDevice> Connected(IInputContext input) {
		int slots = Math.Max(input.Gamepads.Count, input.Joysticks.Count);
		for (int slot = 0; slot < slots; slot++) {
			if (At(input, slot) is { } device) {
				yield return device;
			}
		}
	}

	/// <summary>
	/// The device a mission reads: the first connected one <paramref name="file"/> has a map for, else the first connected
	/// one; null when none is.
	/// </summary>
	public static ControllerDevice? Pick(IInputContext input, JoystickMapFile file) =>
		Connected(input).FirstOrDefault(device => device.MapIn(file) != null) ?? Connected(input).FirstOrDefault();

	/// <summary>The map <paramref name="file"/> holds for this device, or null when it holds none.</summary>
	public JoystickDeviceMap? MapIn(JoystickMapFile file) => file.For(IsGamepad, Guid);

	/// <summary>
	/// The map for this device's reported counts that applies when the file has none; null until a joystick has
	/// reported them (see <see cref="JoystickSource"/>).
	/// </summary>
	public JoystickDeviceMap? Defaults() =>
		IsGamepad ? JoystickDeviceMap.ForGamepad()
		: (AxisCount, ButtonCount, HatCount) is (0, 0, 0) ? null
		: JoystickDeviceMap.ForDevice(AxisCount, ButtonCount, HatCount);

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
}
