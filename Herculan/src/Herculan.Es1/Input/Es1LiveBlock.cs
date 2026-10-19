namespace Herculan.Es1.Input;

/// <summary>
/// <c>Input_LiveBlock</c> (<c>ES1DBSIM 000b1774</c>, <c>0x29</c> bytes), what the devices gave this tick
/// (docs/es1/simulation/input.md#the-live-block--input_polldeviceblock-0004c2d0). The input build stores the masked
/// command word back and zeroes stick axes it takes, so the block is mutable.
/// </summary>
public sealed class Es1LiveBlock {
	/// <summary><c>+0</c>: one word from the keyboard, <c>0x1000</c> and <c>0x2000</c> cleared.</summary>
	public ushort CommandWord { get; set; }

	/// <summary><c>+2</c>, <c>+6</c>: the mouse's x and y.</summary>
	public int PointerX { get; init; }

	public int PointerY { get; init; }

	/// <summary><c>+0xa</c>, <c>+0xb</c>, <c>+0xc</c>: 1 for the mouse's buttons 0, 1 and 2.</summary>
	public byte[] MouseButtons { get; } = new byte[3];

	/// <summary><c>+0xd</c>, <c>+0x11</c>, <c>+0x15</c>, <c>+0x19</c>: the joysticks' four axis dwords, the source table's <c>000b1810</c>–<c>000b181c</c>.</summary>
	public int[] StickAxes { get; } = new int[4];

	/// <summary><c>+0x1d</c>–<c>+0x24</c>: the joysticks' eight button bytes; the first four are the source table's <c>000b1830</c>–<c>000b183c</c>.</summary>
	public byte[] StickButtons { get; } = new byte[8];

	/// <summary><c>+0x25</c>: <c>Input_ReadKeyboardCommand</c>'s answer, the keyboard device enabled.</summary>
	public bool KeyboardEnabled { get; init; }

	/// <summary><c>+0x26</c>, <c>+0x27</c>: the joysticks' record bytes <c>+0x18</c> and <c>+0x19</c>.</summary>
	public byte[] StickExtra { get; } = new byte[2];

	/// <summary><c>+0x28</c>: <c>Input_ReadMouseState</c>'s answer, the mouse device enabled.</summary>
	public bool MouseEnabled { get; init; }
}

/// <summary>
/// <c>Input_PollDeviceBlock</c> (<c>ES1DBSIM 0004c2d0</c>) over the simulator's keyboard and the host's mouse
/// (docs/es1/simulation/input.md#the-live-block--input_polldeviceblock-0004c2d0): a fresh <see cref="Es1LiveBlock"/>
/// a call.
/// </summary>
public sealed class Es1DevicePoll {
	/// <summary>The bits the live block clears from the command word, Num Lock's and Scroll Lock's.</summary>
	private const ushort LockBits = Es1Keyboard.NumLockBit | Es1Keyboard.ScrollLockBit;

	private readonly Es1KeyboardInput _keyboard;
	private readonly Es1HostMouse _mouse;

	public Es1DevicePoll(Es1KeyboardInput keyboard, Es1HostMouse mouse) {
		_keyboard = keyboard;
		_mouse = mouse;
	}

	/// <summary>
	/// <c>Input_ReadKeyboardCommand</c> (<c>0004c458</c>)(1, out) on the enabled keyboard device: the next queued word,
	/// 0 when the queue is empty, and the answer 1.
	/// </summary>
	public ushort ReadKeyboardCommand() => _keyboard.PopCommand();

	/// <summary>
	/// One poll, in retail's order: the keyboard's word with <c>0x1000</c> and <c>0x2000</c> cleared and <c>+0x25</c>;
	/// the joysticks; the mouse's buttons 0, 1 and 2 from <see cref="Es1HostMouse.Buttons"/> and <c>+0x28</c>.
	/// </summary>
	public Es1LiveBlock Poll() {
		ushort word = (ushort)(ReadKeyboardCommand() & ~LockBits);

		// PLACEHOLDER: Input_PollJoysticks (0004bd90), Input_ReadJoystickState (0004c4ac) and
		// Input_MergeJoystickRecord (0004c3cc) fill +0xd-+0x27 from the joysticks' records; they read 0 until plan step 5.

		// PLACEHOLDER: the pointer at +2/+6 reads 0 until plan step 6 maps the host cursor into the 320x200 screen;
		// the cockpit's gunsight is its one reader on step 4's path.
		ushort buttons = _mouse.Buttons;
		var block = new Es1LiveBlock {
			CommandWord = word,
			KeyboardEnabled = true,
			MouseEnabled = true,
		};

		block.MouseButtons[0] = (byte)((buttons & Es1HostMouse.LeftBit) != 0 ? 1 : 0);
		block.MouseButtons[1] = (byte)((buttons & Es1HostMouse.RightBit) != 0 ? 1 : 0);
		block.MouseButtons[2] = (byte)((buttons & Es1HostMouse.MiddleBit) != 0 ? 1 : 0);
		return block;
	}
}
