namespace Herculan.Engine.Audio.Opl;

/// <summary>
/// A Yamaha OPL chip as an FM driver sees it, for a game whose music writes the chip's registers (docs/herculan/plan-es1-sound.md,
/// module D). The interface is this engine's own, not read from any binary: its two calls are what a driver does to the
/// real chip and what the mixer needs from it, so the emulator behind it can be replaced without touching the driver.
///
/// <para>Time is the caller's. A register write takes effect in a frame <see cref="Generate"/> produces after it, in the
/// order the writes were made and spaced as the real chip's settling time spaces a driver's writes, so that a key-off and
/// the key-on after it are each seen; a caller that times its writes, as a driver run from a timer interrupt does,
/// generates the frames up to each write's time before making it.</para>
/// </summary>
public interface IOplChip {
	/// <summary>The frames a second <see cref="Generate"/> produces: the chip's own rate, which the mixer resamples.</summary>
	int SampleRate { get; }

	/// <summary>
	/// Writes <paramref name="value"/> to <paramref name="register"/>, 0 to <c>0x1ff</c>: <c>0x00</c>–<c>0xff</c> is the
	/// register array an OPL2 has, <c>0x100</c>–<c>0x1ff</c> an OPL3's second.
	/// </summary>
	void WriteRegister(int register, byte value);

	/// <summary>Produces <paramref name="frames"/>.Length / 2 frames of stereo 16-bit output, left and right interleaved.</summary>
	void Generate(Span<short> frames);
}
