using Herculan.Opl3;

namespace Herculan.Engine.Audio.Opl;

/// <summary>
/// <see cref="IOplChip"/> over Nuked-OPL3, the emulator of decision 27 (docs/herculan/plan-es1-sound.md#the-opl-emulator),
/// in its own LGPL assembly. The chip runs at its own rate, <see cref="Opl3Chip.NativeRate"/>; Nuked's resampler is not
/// used.
///
/// <para>Writes go through Nuked's write buffer (<see cref="Opl3Chip.WriteRegBuffered"/>), which applies each at least two
/// frames after the one before it. A real chip sees a driver's writes spread out by their settling time
/// (docs/es1/sound/fm-driver.md#the-chip), but this engine makes a whole timer tick's writes before generating the tick's
/// frames. Written straight to the registers, a voice keyed off and on again in one tick, as ES1's FM driver does to a
/// voice it takes while it still plays, would never be seen in release, and Nuked would start the new note with no
/// attack, on the old note's envelope. Two frames per write is Nuked's own spacing, not retail's measured timing.</para>
/// </summary>
public sealed class NukedOplChip : IOplChip {
	private readonly Opl3Chip _chip = new(Opl3Chip.NativeRate);

	/// <inheritdoc />
	public int SampleRate => Opl3Chip.NativeRate;

	/// <inheritdoc />
	public void WriteRegister(int register, byte value) {
		ArgumentOutOfRangeException.ThrowIfNegative(register);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(register, 0x1ff);
		_chip.WriteRegBuffered((ushort)register, value);
	}

	/// <inheritdoc />
	public void Generate(Span<short> frames) {
		for (int i = 0; i + 1 < frames.Length; i += 2) {
			_chip.Generate(frames.Slice(i, 2));
		}
	}
}
