namespace Herculan.Es1.Input;

/// <summary>A keyboard subscriber's callback: the key code, answered <see cref="Es1Keyboard.Claimed"/> or <see cref="Es1Keyboard.PassOn"/>.</summary>
public delegate int Es1KeySubscriber(ushort code);

/// <summary>
/// ES1's keyboard library as the simulator installs it, mode <c>0x10000</c>: each set-1 byte the keyboard sends is
/// <c>Keyboard_Dispatch</c> (<c>ES1DBSIM 0007de20</c>)'s, which makes the byte a key code with the modifier bits,
/// updates the modifiers, and offers the code to the subscribers wanting it, newest first
/// (docs/es1/simulation/input.md#the-keyboard and its subsections).
///
/// <para>Left out: the translate table (<c>Keyboard_SetTranslateTable</c>, <c>0006c289</c>), which <c>es2_xref.py</c>
/// finds no call to, and the <c>INT 15h</c> mode with its <c>E0</c> prefix count, which the simulator's mode does not
/// hook, so bit <c>0x8000</c> of the modifiers is always clear here. <c>Keyboard_PostCode</c>,
/// <c>Keyboard_RemoveSubscriber</c> and <c>Keyboard_RemoveSubscriberCodes</c>, which <c>es2_xref.py</c> finds no call
/// to either (input.md's Open), are not ported.</para>
/// </summary>
public sealed class Es1Keyboard {
	/// <summary>A subscriber's answer that ends the walk.</summary>
	public const int Claimed = 1;

	/// <summary>A subscriber's answer that offers the code to the next older subscriber.</summary>
	public const int PassOn = 2;

	/// <summary>The slots of <c>Keyboard_Subscribers</c> (<c>0009bbc2</c>).</summary>
	public const int MaxSubscribers = 32;

	/// <summary><see cref="Modifiers"/>'s bits a key code carries: <c>Alt</c>, <c>Ctrl</c>, <c>Shift</c> xor <c>Caps Lock</c>, <c>Num Lock</c>, <c>Scroll Lock</c>.</summary>
	public const ushort AltBit = 0x200, CtrlBit = 0x400, ShiftBit = 0x800, NumLockBit = 0x1000, ScrollLockBit = 0x2000;

	/// <summary>The key-up bit of a byte and of a code.</summary>
	public const byte BreakBit = 0x80;

	// The BIOS shift-flag bits the low byte keeps: right and left Shift, Scroll Lock, Num Lock, Caps Lock.
	private const ushort RightShift = 0x01, LeftShift = 0x02, BiosScrollLock = 0x10, BiosNumLock = 0x20, BiosCapsLock = 0x40;

	private readonly Es1KeySubscriber[] _subscribers = new Es1KeySubscriber[MaxSubscribers];

	// Keyboard_CodeMasks (0009b7be): bit i for each subscriber i that wants the code its low byte indexes.
	private readonly uint[] _codeMasks = new uint[256];

	/// <summary>
	/// <c>Keyboard_Ctor</c> (<c>0006c600</c>) with the mode <c>0x10000</c>: <c>Keyboard_SetMode</c>'s seed of the
	/// modifiers from <paramref name="biosShiftFlags"/>, then <see cref="Queue"/>'s subscriber as the first, wanting no
	/// codes yet.
	/// </summary>
	/// <param name="biosShiftFlags">The shift-flag byte the BIOS's <c>INT 16h</c> function 2 answers.</param>
	public Es1Keyboard(byte biosShiftFlags) {
		SeedModifiers(biosShiftFlags);
		AddSubscriber(QueueSubscriber, ReadOnlySpan<byte>.Empty);
	}

	/// <summary><c>Keyboard_Modifiers</c> (<c>0009b6ae</c>): the BIOS's shift flags in the low byte, the code's bits in the high.</summary>
	public ushort Modifiers { get; private set; }

	/// <summary>The keyboard's queue, <c>KeyQueue</c>.</summary>
	public Es1KeyQueue Queue { get; } = new();

	/// <summary><c>KeyQueue_Subscriber</c>, the same delegate each time, as <see cref="AddSubscriberCodes"/> looks it up.</summary>
	public Es1KeySubscriber QueueSubscriber => _queueSubscriber ??= Queue.Subscriber;

	private Es1KeySubscriber? _queueSubscriber;

	/// <summary><c>Keyboard_SubscriberCount</c> (<c>0009bbbe</c>).</summary>
	public int SubscriberCount { get; private set; }

	/// <summary>
	/// <c>Keyboard_SetMode</c> (<c>0006c0e8</c>)'s seed: the BIOS's flag byte as the low byte, and <c>0x800</c> for its
	/// <c>Caps Lock</c> bit, <c>0x1000</c> for <c>Num Lock</c> and <c>0x2000</c> for <c>Scroll Lock</c>. A held
	/// <c>Alt</c> or <c>Ctrl</c> is not seeded into <c>0x200</c> or <c>0x400</c>.
	/// </summary>
	public void SeedModifiers(byte biosShiftFlags) {
		ushort modifiers = biosShiftFlags;
		if ((modifiers & BiosCapsLock) != 0) {
			modifiers |= ShiftBit;
		}

		if ((modifiers & BiosScrollLock) != 0) {
			modifiers |= ScrollLockBit;
		}

		if ((modifiers & BiosNumLock) != 0) {
			modifiers |= NumLockBit;
		}

		Modifiers = modifiers;
	}

	/// <summary>
	/// <c>Keyboard_AddSubscriber</c> (<c>0006c2bb</c>): appends the callback in a new slot, without looking for it in
	/// the others, and sets its bit for each of the codes. False with all 32 slots taken.
	/// </summary>
	public bool AddSubscriber(Es1KeySubscriber callback, ReadOnlySpan<byte> codes) {
		if (SubscriberCount == MaxSubscribers) {
			return false;
		}

		int slot = SubscriberCount++;
		_subscribers[slot] = callback;
		SetBits(slot, codes);
		return true;
	}

	/// <summary>
	/// <c>Keyboard_AddSubscriberCodes</c> (<c>0006c3b4</c>): sets the bit of the newest slot holding the callback for
	/// each of the codes. False when no slot holds it.
	/// </summary>
	public bool AddSubscriberCodes(Es1KeySubscriber callback, ReadOnlySpan<byte> codes) {
		for (int slot = SubscriberCount - 1; slot >= 0; slot--) {
			if (_subscribers[slot].Equals(callback)) {
				SetBits(slot, codes);
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>Keyboard_Dispatch</c> (<c>0007de20</c>) on one byte from the keyboard: the code is the byte or'd with the
	/// modifiers' high byte as it stood before the byte; a modifier key then updates the modifiers; and the code goes
	/// to each subscriber wanting its low byte, newest first, while they answer <see cref="PassOn"/>. Answers 1 when
	/// one claimed it and 0 when none did, which the <c>INT 9</c> handler drops.
	/// </summary>
	public int Dispatch(byte scancode) {
		ushort code = (ushort)(scancode | (Modifiers & 0xff00));
		UpdateModifiers(scancode);

		uint mask = _codeMasks[code & 0xff];
		while (mask != 0) {
			int slot = 31 - System.Numerics.BitOperations.LeadingZeroCount(mask);
			mask &= ~(1u << slot);
			if (_subscribers[slot](code) != PassOn) {
				return 1;
			}
		}

		return 0;
	}

	// Step 4 of the dispatch, by the byte's low seven bits.
	private void UpdateModifiers(byte scancode) {
		bool press = (scancode & BreakBit) == 0;
		switch (scancode & 0x7f) {
			case 0x2a:
				SetOrClear(LeftShift, press);
				UpdateShiftBit();
				break;
			case 0x36:
				SetOrClear(RightShift, press);
				UpdateShiftBit();
				break;
			case 0x3a:
				if (press) {
					Modifiers ^= BiosCapsLock;
				}

				UpdateShiftBit();
				break;
			case 0x38:
				SetOrClear(AltBit, press);
				break;
			case 0x1d:
				SetOrClear(CtrlBit, press);
				break;
			case 0x45:
				if (press) {
					Modifiers ^= NumLockBit;
				}

				break;
			case 0x46:
				if (press) {
					Modifiers ^= ScrollLockBit;
				}

				break;
		}
	}

	// 0x800 when exactly one of "a Shift held" and "Caps Lock on" holds.
	private void UpdateShiftBit() {
		bool shift = (Modifiers & (LeftShift | RightShift)) != 0;
		bool caps = (Modifiers & BiosCapsLock) != 0;
		SetOrClear(ShiftBit, shift != caps);
	}

	private void SetOrClear(ushort bit, bool set) => Modifiers = (ushort)(set ? Modifiers | bit : Modifiers & ~bit);

	private void SetBits(int slot, ReadOnlySpan<byte> codes) {
		foreach (byte code in codes) {
			_codeMasks[code] |= 1u << slot;
		}
	}
}
