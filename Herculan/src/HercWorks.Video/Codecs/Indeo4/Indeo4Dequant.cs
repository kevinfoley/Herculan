using System.IO.Compression;

namespace HercWorks.Video.Codecs.Indeo4;

/// <summary>
/// Quantisation: the step tables and the value a coded level stands for at a given step. See
/// docs/retail/formats/indeo4.md#dequantisation.
/// </summary>
internal static class Indeo4Dequant {
	/// <summary>Quantisation matrices the codec carries; index 31 in a band header means a custom one.</summary>
	internal const int MatrixCount = 31;

	/// <summary>Quantiser levels per matrix.</summary>
	internal const int Levels = 32;

	private static readonly Lazy<ushort[]> s_steps = new(Inflate);

	/// <summary>
	/// Steps indexed [matrix][inter 0 / intra 1][quantiser][position row * 8 + column], flattened,
	/// with the 4x4 matrices respaced to that layout as the codec does when it loads.
	/// </summary>
	internal static ushort[] Steps => s_steps.Value;

	/// <summary>The step for one coefficient.</summary>
	internal static int Step(int matrix, int set, int level, int position) =>
		Steps[(((((matrix * 2) + set) * Levels) + level) * 64) + position];

	/// <summary>
	/// How many level codes a step's table holds. A code past the end reads whatever the codec
	/// placed after the table, so the decoder refuses it.
	/// </summary>
	internal static int CodeCount(int step) => step <= 1 ? 16384 : 2 * (8192 / step);

	/// <summary>
	/// The value of level code <paramref name="code"/> (1-based) at <paramref name="step"/>, before
	/// the transform's scale. Odd codes are positive, even negative, magnitude <c>(code + 1) / 2</c>.
	/// </summary>
	internal static int Value(int code, int step) {
		int magnitude = (code + 1) >> 1;
		int value = step <= 1 ? magnitude : (magnitude * step) + (step >> 1) - (step & 1);
		return (code & 1) != 0 ? value : -value;
	}

	private static ushort[] Inflate() {
		byte[] packed = Convert.FromBase64String(Indeo4Tables.QuantStepsDeflated);
		using var input = new MemoryStream(packed);
		using var inflater = new DeflateStream(input, CompressionMode.Decompress);
		var steps = new ushort[MatrixCount * 2 * Levels * 64];
		var bytes = new byte[steps.Length * 2];
		inflater.ReadExactly(bytes);
		for (int i = 0; i < steps.Length; i++) {
			steps[i] = (ushort)(bytes[2 * i] | (bytes[(2 * i) + 1] << 8));
		}

		// A row whose last entry is zero belongs to a 4x4 matrix, stored with four positions to a
		// row. The codec respaces it to eight to a row when it loads, last entry first so nothing
		// is overwritten before it is read, and leaves the vacated entries as they were. 1002a5a0.
		for (int row = 0; row < steps.Length; row += 64) {
			if (steps[row + 63] != 0) {
				continue;
			}

			for (int k = 15; k >= 4; k--) {
				steps[row + k + (k & ~3)] = steps[row + k];
			}
		}

		return steps;
	}
}
