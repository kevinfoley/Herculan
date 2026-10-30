namespace Herculan.Es1.Input;

/// <summary>
/// <b>This engine's own:</b> the stand-in for the PC BIOS's keyboard buffer, which the shell's <c>INT 15h</c> keyboard
/// lets have the bytes no subscriber claims (<see cref="Es1Keyboard.Bios"/>) and DOS's functions <c>0x0b</c> and 8 read
/// for <c>Input_ReadDeviceEvent</c>'s mode <c>0x20000</c> (docs/es1/simulation/input.md#the-input-devices). Retail's BIOS
/// turns a make byte into a character by its own US table; here the character is the one the host types for the key, so
/// letters follow the player's keyboard layout, the user's choice (docs/herculan/plan-es1-first-mission.md#decisions,
/// decision 22).
///
/// <para>Each make byte the keyboard passes on adds an entry, up to the BIOS's 15, past which a key is dropped as the
/// BIOS drops it; break bytes add none. The character the host types after the key's host key-down
/// (<see cref="HostKeyDown"/>, <see cref="HostChar"/>) is its entry's, and a typematic repeat this engine sends takes the
/// character the key typed last. A key the host types no printable ASCII character for reads as the BIOS's control
/// character for <c>Enter</c>, <c>Esc</c>, <c>Backspace</c> and <c>Tab</c>, and otherwise as 0 and its scan code, as the
/// BIOS gives a key with no character. The <c>E0</c> bytes are dropped: a grey key reads as the key whose code it shares,
/// by what the host types for it, so the grey arrows read as 0 and their code. <c>Ctrl</c> and <c>Alt</c> with a letter,
/// which the BIOS gives control or extended codes, read as the host types them, which is mostly nothing.</para>
/// </summary>
public sealed class Es1BiosKeyBuffer {
	/// <summary>The keys the BIOS's 16-word ring buffer holds.</summary>
	public const int Capacity = 15;

	/// <summary><c>Input_ReadDeviceEvent</c>'s bit for a word DOS gave, and its bit for a key read as 0 and then its code.</summary>
	public const ushort DosBit = 0x4000, ExtendedBit = 0x8000;

	/// <summary>The BIOS's characters for the keys a host types nothing for: <c>Enter</c>, <c>Esc</c>, <c>Backspace</c> and <c>Tab</c>.</summary>
	private static readonly Dictionary<byte, char> ControlCharacters = new() {
		[0x1c] = '\r', [0x01] = '\x1b', [0x0e] = '\b', [0x0f] = '\t',
	};

	private sealed class Entry(byte scancode, char? character) {
		public byte Scancode { get; } = scancode;
		public char? Character { get; set; } = character;
	}

	private readonly Queue<Entry> _entries = new();
	private readonly List<Entry> _unresolved = new();
	private readonly Dictionary<byte, char> _typed = new();
	private byte? _lastDown;

	/// <summary>Whether DOS's function <c>0x0b</c>, <c>Dos_KeyAvailable</c>, answers that a key is waiting.</summary>
	public bool KeyAvailable => _entries.Count > 0;

	/// <summary>A byte the keyboard lets the BIOS have (<see cref="Es1Keyboard.Bios"/>).</summary>
	public void Accept(byte value) {
		if (value == Es1Keyboard.ExtendedPrefix || (value & Es1Keyboard.BreakBit) != 0 || _entries.Count >= Capacity) {
			return;
		}

		var entry = new Entry(value, _typed.TryGetValue(value, out char typed) ? typed : null);
		_entries.Enqueue(entry);
		if (entry.Character == null) {
			_unresolved.Add(entry);
		}
	}

	/// <summary>A host key-down of the key whose make byte is <paramref name="make"/>, before its byte is sent: the key's last character forgotten.</summary>
	public void HostKeyDown(byte make) {
		_lastDown = make;
		_typed.Remove(make);
	}

	/// <summary>
	/// A character the host types, the key last pressed's: kept for that key's repeats, and given to its newest entry still
	/// without one. A character outside printable ASCII is not kept.
	/// </summary>
	public void HostChar(char character) {
		if (_lastDown is not { } make || character is < ' ' or > '~') {
			return;
		}

		_typed[make] = character;
		int newest = _unresolved.FindLastIndex(entry => entry.Scancode == make);
		if (newest >= 0) {
			_unresolved[newest].Character = character;
			_unresolved.RemoveAt(newest);
		}
	}

	/// <summary>
	/// <c>Dos_KeyAvailable</c> and <c>Dos_ReadKeyNoEcho</c> as <c>Input_ReadDeviceEvent</c> reads them: 0 with no key
	/// waiting; otherwise the oldest key's character with <see cref="DosBit"/>, or for a key with none, the second read's
	/// scan code with <see cref="ExtendedBit"/> and <see cref="DosBit"/>.
	/// </summary>
	public ushort Read() {
		if (!_entries.TryDequeue(out var entry)) {
			return 0;
		}

		_unresolved.Remove(entry);
		char character = entry.Character ?? (ControlCharacters.TryGetValue(entry.Scancode, out char control) ? control : '\0');
		return character != '\0' ? (ushort)(DosBit | character) : (ushort)(DosBit | ExtendedBit | entry.Scancode);
	}
}
