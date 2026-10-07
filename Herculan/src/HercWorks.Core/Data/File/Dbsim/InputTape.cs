namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - TAPES\&lt;name&gt;.TAP — DBSIM's input tape: a bundle of the seven files a mission starts
/// from, the recording machine's joystick capability block, then one record per input build of
/// everything the player did. Loose files beside the install, not VOL entries.
///
/// The layout is DBSIM's own writer and reader,
/// <c>Tape_PackFile</c> (<c>0045cc88</c>) for the bundle and <c>Input_BuildPlayerDevice</c>
/// (<c>0045a7f4</c>) for the rest. See docs/retail/formats/tap-input-tape.md for what each field is and how
/// the simulator consumes it.
/// </summary>
public class InputTape {
	/// <summary>How many files the bundle carries.</summary>
	public const int BundleFileCount = 7;

	/// <summary>The size of the capability block that follows the bundle.</summary>
	public const int CapabilityBlockSize = 8;

	/// <summary>
	/// The bundle's files, in bundle order and as <c>-r</c> names them: <c>script.dat</c>,
	/// <c>player.mec</c>, <c>mission.var</c>, <c>prefs.cfg</c>, <c>restore.dat</c>,
	/// <c>object.str</c> and <c>keyjoy.cfg</c>. A file the recorder could not open is an empty entry.
	/// </summary>
	public byte[][] Bundle { get; set; } = new byte[BundleFileCount][];

	/// <summary>The eight bytes <c>Input_QueryCapabilities</c> (<c>004777f8</c>) returned on the recording machine.</summary>
	public byte[] Capabilities { get; set; } = new byte[CapabilityBlockSize];

	/// <summary>One record per input build, in the order they were written.</summary>
	public List<Frame> Frames { get; set; } = new();

	/// <summary>One input build — a 24-byte header, its mouse events and its command codes.</summary>
	public class Frame {
		/// <summary>
		/// The command word at the head of <c>PlayerInputBlock</c> (<c>004d234a</c>): the one key event
		/// the build took off the keyboard ring, as a set-1 scancode with <c>0x200</c> for Alt,
		/// <c>0x400</c> for Ctrl and <c>0x80</c> on a release. Zero on a frame with none.
		/// </summary>
		public short CommandWord { get; set; }

		/// <summary>The four game axes, <c>004d2358</c>-<c>004d235e</c>: steer, throttle, twist, pitch.</summary>
		public short[] Axes { get; set; } = new short[4];

		/// <summary>The <c>SimTickDelta</c> the frame ran with — Q8, 1.0 = 125 ms.</summary>
		public short TickDelta { get; set; }

		/// <summary>
		/// Header <c>+0x0c</c>: buttons 1-4 (<c>004d2360</c>-<c>004d2363</c>) in bits 0-3 and the four
		/// hat bytes (<c>004d2368</c>-<c>004d236b</c>) in bits 4-7.
		/// </summary>
		public byte ButtonBank { get; set; }

		/// <summary>Header <c>+0x0d</c>: the stick's eight raw buttons, before the bindings.</summary>
		public byte RawButtonBank { get; set; }

		/// <summary>Header <c>+0x0e</c>: the trigger, <c>004d2357</c>, in bit 0.</summary>
		public byte TriggerBank { get; set; }

		/// <summary>Header <c>+0x0f</c>: the high byte of the bit word, which the writer never sets.</summary>
		public byte Spare { get; set; }

		/// <summary>The cockpit mouse queue's records for this build.</summary>
		public List<MouseEvent> MouseEvents { get; set; } = new();

		/// <summary>The command queue at <c>004d2148</c> for this build.</summary>
		public short[] Commands { get; set; } = Array.Empty<short>();
	}

	/// <summary>
	/// One cockpit mouse record, <c>CockpitMouseQueue_Push</c>'s own 14-byte layout: a position in the
	/// game's screen space, the held-button mask and a coarse-tick timestamp.
	/// </summary>
	public class MouseEvent {
		public int X { get; set; }
		public int Y { get; set; }

		/// <summary>Bit 0 the left button, bit 1 the right.</summary>
		public ushort Buttons { get; set; }

		/// <summary><c>Time_GetCoarseTicks</c> when the event was queued — 16 ms units.</summary>
		public int Time { get; set; }
	}
}
