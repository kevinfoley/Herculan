namespace HercWorks.Video.Codecs.Indeo3;

/// <summary>
/// The requantisation table, <c>[8][128]</c> at <c>1003d088</c> in <c>IR32_32.DLL</c>: row
/// <c>q</c> remaps a 7-bit pixel onto a staircase of step <c>q + 2</c>, so that the deltas of the
/// codebook a cell is about to use cannot carry the sum out of range.
///
/// <para>Built from the formula rather than held as bytes; <c>Indeo3DecoderTests</c> checks it
/// against the retail DLL whenever that file is present. The division truncates toward zero, as C's
/// does, which is what makes rows 3 and 4 start at 4 rather than going negative.</para>
///
/// <para>See <c>docs/retail/formats/indeo3.md</c>, "The requantisation table".</para>
/// </summary>
internal static class Indeo3Requant {
	/// <summary>Virtual address of the table in <c>IR32_32.DLL</c>.</summary>
	internal const uint TableVirtualAddress = 0x1003d088;

	/// <summary>The table, row-major, 128 bytes per row.</summary>
	internal static byte[] Table { get; } = Build();

	private static byte[] Build() {
		ReadOnlySpan<int> offsets = [1, 1, 2, -3, -3, 3, 4, 4];
		ReadOnlySpan<int> bias = [0, 1, 0, 4, 4, 1, 0, 1];

		var table = new byte[8 * 128];
		for (int q = 0; q < 8; q++) {
			int step = q + 2;
			for (int p = 0; p < 128; p++) {
				table[(q * 128) + p] = (byte)(((p + offsets[q]) / step * step) + bias[q]);
			}
		}

		// The staircase's top steps land at 128 or above; the table caps them at the step below.
		table[(0 * 128) + 127] = 126;
		table[(1 * 128) + 119] = 118;
		table[(1 * 128) + 120] = 118;
		table[(2 * 128) + 126] = 124;
		table[(2 * 128) + 127] = 124;
		for (int p = 124; p < 128; p++) {
			table[(6 * 128) + p] = 120;
		}

		// Two cells off the staircase.
		table[(1 * 128) + 7] = 10;
		table[(4 * 128) + 8] = 10;

		return table;
	}
}
