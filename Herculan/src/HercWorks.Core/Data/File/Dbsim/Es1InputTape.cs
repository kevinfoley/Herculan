namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - TAPES\&lt;name&gt;.TAP — ES1DBSIM's input tape: a bundle of the seven files a mission starts from, then one
/// record per input build of everything the player did. Loose files in the install's <c>TAPES</c> folder, not VOL entries.
///
/// <para>The layout is ES1DBSIM's own writer and reader, <c>Tape_PackFile</c> (<c>000574a0</c>) for the bundle and
/// <c>Input_BuildPlayerDevice</c> (<c>0004cb14</c>) for the frames. See docs/es1/formats/tap-input-tape.md#the-bundle and
/// docs/es1/formats/tap-input-tape.md#the-frames for what each field is and how the simulator consumes it. The bundle is
/// ES2's (<see cref="InputTape"/>); what follows it differs.</para>
/// </summary>
public class Es1InputTape {
	/// <inheritdoc cref="InputTape.Bundle"/>
	public byte[][] Bundle { get; set; } = new byte[InputTape.BundleFileCount][];

	/// <summary>One record per input build, in the order they were written.</summary>
	public List<Frame> Frames { get; set; } = new();

	/// <summary>One input build — an 18-byte fixed part and its mouse events.</summary>
	public class Frame {
		/// <summary>
		/// The command word at the head of <c>PlayerInputBlock</c> (<c>000b17ac</c>): the one key event the build took
		/// off the keyboard, a set-1 scancode with <c>0x200</c> for Alt, <c>0x400</c> for Ctrl and <c>0x80</c> on a
		/// release, masked with <c>SimCommandMask</c> (<c>000958b8</c>, <c>0x47ff</c>). Zero on a frame with none.
		/// </summary>
		public short CommandWord { get; set; }

		/// <summary>
		/// The input block's four axes, <c>+0xd</c>, <c>+0xf</c>, <c>+0x11</c> and <c>+0x13</c>: steering, throttle,
		/// twist, pitch, as they stand once the configuration has picked their sources, before the camera's axes and
		/// <c>Backturn</c>.
		/// </summary>
		public short[] Axes { get; set; } = new short[4];

		/// <summary>The <c>SimTickDelta</c> the frame ran with, which playback puts in the input block's <c>+0x29</c>.</summary>
		public short TickDelta { get; set; }

		/// <summary>The button word, a raw 16-bit word whose bits <see cref="Button"/> names.</summary>
		public Button Buttons { get; set; }

		/// <summary>The cockpit mouse queue's records for this build.</summary>
		public List<TapeMouseEvent> MouseEvents { get; set; } = new();
	}

	/// <summary>
	/// The bits of <see cref="Frame.Buttons"/>, as the recording builds the word. Bits 12–15 have no source and are
	/// zero in what the recording writes.
	/// </summary>
	[Flags]
	public enum Button : ushort {
		None = 0,

		/// <summary>The input block's button 0, <c>+0x15</c>, the trigger.</summary>
		Button0 = 1 << 0,

		/// <summary>The input block's buttons 1–7, <c>+0x16</c>–<c>+0x1c</c>.</summary>
		Button1 = 1 << 1,
		Button2 = 1 << 2,
		Button3 = 1 << 3,
		Button4 = 1 << 4,
		Button5 = 1 << 5,
		Button6 = 1 << 6,
		Button7 = 1 << 7,

		/// <summary>
		/// The four source buttons: live, the live block's <c>+0x1d</c>–<c>+0x20</c>, the joysticks' first four button
		/// bytes as read, before the configuration and the latches.
		/// </summary>
		Source0 = 1 << 8,
		Source1 = 1 << 9,
		Source2 = 1 << 10,
		Source3 = 1 << 11,
	}
}
