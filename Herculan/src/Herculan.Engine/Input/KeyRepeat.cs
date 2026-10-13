using System.Runtime.InteropServices;
using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>
/// The keyboard's auto-repeat, rebuilt from polled key state, because every handler polls: retail takes each
/// auto-repeat <c>WM_KEYDOWN</c> as a fresh command (docs/retail/simulation/cockpit-input.md#how-a-keystroke-becomes-one-of-those-codes),
/// and this says when one would have arrived. Windows repeats only the key pressed last, after its delay and
/// then at its rate, and stops when that key goes up even while others are still held. A modifier going down
/// is the key pressed last too, which takes the repeat off whatever had it.
/// </summary>
public sealed class KeyRepeat((double Delay, double Interval) typematic) {
	// Every key a set-1 scancode stands for, modifiers included, and the right-hand modifiers, which share
	// their left-hand twins' scancodes.
	private static readonly Key[] Watched = Enumerable.Range(1, 0x58).SelectMany(TapeKeys.KeysOf)
		.Append(Key.ControlRight).Append(Key.AltRight).Distinct().ToArray();

	private readonly HashSet<Key> _down = new();
	private Key? _repeating;
	private double _wait;

	/// <summary>The key whose repeat fell due on the last <see cref="Advance"/>, if any.</summary>
	public Key? Repeated { get; private set; }

	/// <summary>One host frame of the keyboard.</summary>
	public void Advance(Func<Key, bool> isDown, double deltaSeconds) {
		Repeated = null;
		bool pressed = false;
		foreach (var key in Watched) {
			if (!isDown(key)) {
				_down.Remove(key);
			} else if (_down.Add(key)) {
				_repeating = key;
				pressed = true;
			}
		}

		if (pressed) {
			_wait = typematic.Delay;
			return;
		}

		if (_repeating is not { } held || !isDown(held)) {
			_repeating = null;
			return;
		}

		_wait -= deltaSeconds;
		if (_wait <= 0) {
			_wait += typematic.Interval;
			Repeated = held;
		}
	}

	/// <summary>
	/// Windows' own keyboard delay (0-3, a quarter second each from 250 ms) and speed (0-31, about 2.5 to 30
	/// repeats a second), which is what paced retail's repeats. Their defaults elsewhere.
	/// </summary>
	public static (double Delay, double Interval) SystemTypematic() {
		int delay = 1;
		int speed = 31;
		if (OperatingSystem.IsWindows()) {
			SystemParametersInfo(GetKeyboardDelay, 0, ref delay, 0);
			SystemParametersInfo(GetKeyboardSpeed, 0, ref speed, 0);
		}

		return (0.25 * (Math.Clamp(delay, 0, 3) + 1), 1.0 / (2.5 + Math.Clamp(speed, 0, 31) * 27.5 / 31));
	}

	private const uint GetKeyboardSpeed = 0x0a;
	private const uint GetKeyboardDelay = 0x16;

	[DllImport("user32.dll")]
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);
}
