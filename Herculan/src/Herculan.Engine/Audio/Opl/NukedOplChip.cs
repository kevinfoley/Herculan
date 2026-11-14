using Herculan.Opl3;

namespace Herculan.Engine.Audio.Opl;

/// <summary>
/// <see cref="IOplChip"/> over Nuked-OPL3, the emulator of decision 27 (docs/herculan/plan-es1-sound.md#the-opl-emulator),
/// in its own LGPL assembly. The chip runs at its own rate, <see cref="Opl3Chip.NativeRate"/>, and writes go straight to
/// its registers; Nuked's resampler and write buffer are not used.
/// </summary>
public sealed class NukedOplChip : IOplChip {
	private readonly Opl3Chip _chip = new(Opl3Chip.NativeRate);

	/// <inheritdoc />
	public int SampleRate => Opl3Chip.NativeRate;

	/// <inheritdoc />
	public void WriteRegister(int register, byte value) {
		ArgumentOutOfRangeException.ThrowIfNegative(register);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(register, 0x1ff);
		_chip.WriteReg((ushort)register, value);
	}

	/// <inheritdoc />
	public void Generate(Span<short> frames) {
		for (int i = 0; i + 1 < frames.Length; i += 2) {
			_chip.Generate(frames.Slice(i, 2));
		}
	}
}
