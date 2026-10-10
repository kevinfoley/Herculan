using Herculan.Engine.Sim;
using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>
/// The keyboard's two axis pairs — <c>Input_BuildKeyboardAxes</c> (<c>0045a4b0</c>) over the fourteen keys
/// <c>Input_KeyjoyAxisKey</c> (<c>0045a308</c>) holds. The first pair is steering and throttle, the second twist
/// and pitch, and a binding decides which game axes each reaches (<see cref="JoystickBindings"/>). Each key is
/// worth <see cref="MechControls.KeyboardAxis"/> on the axes its entry names. See
/// docs/retail/simulation/joystick-input.md#the-keyboard.
/// </summary>
public static class KeyboardAxes {
	// Keys 0-7, in the order of the original's wanted-codes list. Each scancode is two keys here: VkToScancode
	// (004a1104) gives the arrow and editing cluster the keypad's own codes (TapeKeys.KeysOf).
	private static readonly (Key Pad, Key Cluster, sbyte Dx, sbyte Dy, bool Diagonal)[] FirstPairKeys = {
		(Key.Keypad7, Key.Home, -1, -1, true),
		(Key.Keypad8, Key.Up, 0, -1, false),
		(Key.Keypad9, Key.PageUp, 1, -1, true),
		(Key.Keypad4, Key.Left, -1, 0, false),
		(Key.Keypad6, Key.Right, 1, 0, false),
		(Key.Keypad1, Key.End, -1, 1, true),
		(Key.Keypad2, Key.Down, 0, 1, false),
		(Key.Keypad3, Key.PageDown, 1, 1, true),
	};

	// Keys 8-13.
	private static readonly (Key Key, sbyte Dx, sbyte Dy)[] SecondPairKeys = {
		(Key.M, 0, -1),
		(Key.J, -1, 0),
		(Key.K, 1, 0),
		(Key.I, 0, 1),
		(Key.KeypadSubtract, 0, -1),
		(Key.KeypadAdd, 0, 1),
	};

	/// <summary>
	/// The frame's two pairs. Keys 0-7 add up until the first held diagonal, which replaces everything before
	/// it and ends the group; of keys 8-13 only the first held counts.
	/// </summary>
	/// <param name="throttleKeys">
	/// Keypad <c>-</c> and <c>+</c> are axis keys: a RAZOR's, whose second pair's pitch is its throttle.
	/// <c>Input_KeyjoyAxisKey</c> passes them on otherwise.
	/// </param>
	public static PilotAxes Build(IKeyState keys, bool throttleKeys) {
		int steer = 0, throttle = 0;
		foreach (var (pad, cluster, dx, dy, diagonal) in FirstPairKeys) {
			if (!keys.IsKeyPressed(pad) && !keys.IsKeyPressed(cluster)) {
				continue;
			}

			if (diagonal) {
				(steer, throttle) = (dx, dy);
				break;
			}

			steer += dx;
			throttle += dy;
		}

		int twist = 0, pitch = 0;
		foreach (var (key, dx, dy) in SecondPairKeys) {
			if (key is Key.KeypadSubtract or Key.KeypadAdd && !throttleKeys) {
				continue;
			}

			if (keys.IsKeyPressed(key)) {
				(twist, pitch) = (dx, dy);
				break;
			}
		}

		return new PilotAxes(
			(short)(steer * MechControls.KeyboardAxis),
			(short)(throttle * MechControls.KeyboardAxis),
			(short)(twist * MechControls.KeyboardAxis),
			(short)(pitch * MechControls.KeyboardAxis));
	}
}
