namespace Herculan.Es1.Input;

/// <summary>
/// The keyboard library's queue of key codes, <c>KeyQueue</c> (<c>ES1DBSIM 0009b6b2</c>): a ring of 128 words that
/// holds 127, which the simulator's command word is read from one word a tick
/// (docs/es1/simulation/input.md#a-key--keyboard_int9handler-0007dde4-and-keyboard_dispatch-0007de20, the library's
/// table, and #the-command-word).
/// </summary>
public sealed class Es1KeyQueue {
	/// <summary>The ring's length in words; one slot always stays free, so it holds one fewer.</summary>
	public const int Length = 128;

	private readonly ushort[] _words = new ushort[Length];

	// KeyQueue_Head (0009b7b2), the slot last written, and KeyQueue_Tail (0009b7b6), the slot last read.
	private int _head, _tail;

	/// <summary>Status bit <c>2</c> of the keyboard's status dword: the ring holds a word.</summary>
	public bool HasWords => _head != _tail;

	/// <summary>Status bit <c>0x80000</c>: a push found the ring full. Nothing in the library clears it.</summary>
	public bool Overflowed { get; private set; }

	/// <summary>
	/// <c>KeyQueue_Push</c> (<c>0006c1a0</c>): writes the word at the slot after the head unless that slot is the tail,
	/// in which case the word is dropped. Answers 1 either way.
	/// </summary>
	public int Push(ushort code) {
		int next = (_head + 1) % Length;
		if (next == _tail) {
			Overflowed = true;
			return 1;
		}

		_head = next;
		_words[next] = code;
		return 1;
	}

	/// <summary><c>KeyQueue_Pop</c> (<c>0006c1f8</c>): the oldest word, or 0 when the ring is empty.</summary>
	public ushort Pop() {
		if (_tail == _head) {
			return 0;
		}

		_tail = (_tail + 1) % Length;
		return _words[_tail];
	}

	/// <summary>
	/// <c>KeyQueue_Subscriber</c> (<c>0006c6e4</c>), the keyboard's oldest subscriber: queues the code and claims it.
	/// </summary>
	public int Subscriber(ushort code) {
		Push(code);
		return Es1Keyboard.Claimed;
	}
}
