using System.Runtime.InteropServices;
using Silk.NET.GLFW;
using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>
/// The keyboard layout, as the key a game reads for each physical key. Silk names a key by where it sits on a US
/// keyboard, since GLFW's key codes are positions, while a Windows game that reads a key message's virtual-key code
/// takes whatever key the layout puts there: on a French AZERTY keyboard the key in the US Q's place sends
/// <c>VK_A</c>. So each key a layout can move (<see cref="Movable"/>) is translated through the game's own reading of
/// virtual-key codes; every other key is itself.
///
/// <para>On Windows a key's code comes from its GLFW scancode, through <c>MapVirtualKeyExW</c> under the thread's
/// current layout. <see cref="Refresh"/> rebuilds the translation when that layout changes, so switching layouts
/// mid-game takes effect on the next frame.</para>
///
/// <para>Elsewhere there is no virtual-key code, and the translation is HERCULAN's own approximation, not read from
/// any retail binary: a key whose GLFW name is a letter is that letter, a key in a letter's US place whose name is
/// no letter is no key (its letter is elsewhere on the layout), and the digits and symbols stay where they are.</para>
/// </summary>
public sealed class KeyboardLayout {
	/// <summary>
	/// The keys a layout can move: the letters, the digit row, the US symbol keys and the ISO keyboard's extra key,
	/// which GLFW calls <see cref="Key.World2"/> on Windows and <see cref="Key.World1"/> elsewhere. The arrows, the
	/// keypad, the function keys, the modifiers and the rest keep their places on every layout.
	/// </summary>
	public static readonly Key[] Movable = [
		.. Enumerable.Range((int)Key.A, 26).Select(key => (Key)key),
		.. Enumerable.Range((int)Key.Number0, 10).Select(key => (Key)key),
		Key.Minus, Key.Equal, Key.LeftBracket, Key.RightBracket, Key.BackSlash, Key.Semicolon, Key.Apostrophe,
		Key.GraveAccent, Key.Comma, Key.Period, Key.Slash, Key.World1, Key.World2,
	];

	// The GLFW the window and input already run on; GetApi loads the same native library.
	private static readonly Lazy<Glfw> GlfwApi = new(Glfw.GetApi);

	private readonly Func<int, Key?> _keyOfVirtualKey;

	// What each Movable key reads as, by index, and for each key that some Movable key reads as, every physical key
	// that does. A key not in _sources is read off its own place alone.
	private readonly Key?[] _keys = Movable.Select(key => (Key?)key).ToArray();
	private readonly Dictionary<Key, Key[]> _sources = new();
	private nint _windowsLayout;
	private bool _built;

	/// <param name="keyOfVirtualKey">
	/// The game's reading of a Windows virtual-key code: the key, by its US place, that its handlers take the code
	/// for, or null for a code it ignores.
	/// </param>
	public KeyboardLayout(Func<int, Key?> keyOfVirtualKey) => _keyOfVirtualKey = keyOfVirtualKey;

	/// <summary>The key the game reads for a physical key, or null for one it ignores on this layout.</summary>
	public Key? Translate(Key physical) {
		int index = Array.IndexOf(Movable, physical);
		return index >= 0 ? _keys[index] : physical;
	}

	/// <summary>Whether any physical key the game reads as <paramref name="key"/> is down.</summary>
	public bool IsDown(IKeyboard device, Key key) {
		if (!_sources.TryGetValue(key, out var sources)) {
			return device.IsKeyPressed(key);
		}

		foreach (var source in sources) {
			if (device.IsKeyPressed(source)) {
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Takes up the current layout, if it has changed since the last call: on Windows a check of the layout handle,
	/// elsewhere the keys' names read again. Called before keys are read, once a frame or once a key event.
	/// </summary>
	public void Refresh() {
		if (OperatingSystem.IsWindows()) {
			nint layout = GetKeyboardLayout(0);
			if (_built && layout == _windowsLayout) {
				return;
			}

			_windowsLayout = layout;
		}

		bool changed = !_built;
		for (int i = 0; i < Movable.Length; i++) {
			Key? key;
			if (OperatingSystem.IsWindows()) {
				key = WindowsKeyOf(Movable[i], _windowsLayout);
			} else {
				key = PrintedKeyOf(Movable[i]);
			}

			changed |= key != _keys[i];
			_keys[i] = key;
		}

		_built = true;
		if (changed) {
			RebuildSources();
		}
	}

	private void RebuildSources() {
		_sources.Clear();
		var targets = Movable.Concat(_keys.OfType<Key>()).Distinct();
		foreach (var target in targets) {
			var sources = Movable.Where((_, i) => _keys[i] == target).ToList();
			if (Array.IndexOf(Movable, target) < 0) {
				sources.Insert(0, target);
			}

			_sources[target] = sources.ToArray();
		}
	}

	// The game's key for a physical key under a Windows layout. GLFW gives no scancode to a key it never reports on
	// this platform (World1 on Windows), which is left as it is.
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private Key? WindowsKeyOf(Key physical, nint layout) {
		int scancode = GlfwApi.Value.GetKeyScancode((int)physical);
		if (scancode < 0) {
			return physical;
		}

		return _keyOfVirtualKey((int)MapVirtualKeyExW((uint)scancode, MapVkVscToVkEx, layout));
	}

	// The non-Windows approximation: the letter GLFW names the key by, if it names one.
	private static Key? PrintedKeyOf(Key physical) {
		int scancode = GlfwApi.Value.GetKeyScancode((int)physical);
		if (scancode < 0) {
			return physical;
		}

		if (GlfwApi.Value.GetKeyName((int)physical, scancode) is [var c] && char.ToUpperInvariant(c) is >= 'A' and <= 'Z' and var letter) {
			return Key.A + (letter - 'A');
		}

		return physical is >= Key.A and <= Key.Z ? null : physical;
	}

	private const uint MapVkVscToVkEx = 3;

	[DllImport("user32.dll")]
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static extern nint GetKeyboardLayout(uint threadId);

	[DllImport("user32.dll")]
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static extern uint MapVirtualKeyExW(uint code, uint mapType, nint layout);
}
