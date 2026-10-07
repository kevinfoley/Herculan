namespace HercWorks.Video.Codecs.Indeo4;

/// <summary>
/// A variable-length code built from a row descriptor; see docs/retail/formats/indeo4.md#codebooks.
/// Row <c>r</c>'s codes are <c>r</c> one bits, then a zero bit unless it is the last row, then
/// the row's suffix bits most significant first.
/// </summary>
internal sealed class Indeo4Codebook {
	/// <summary>The most rows a descriptor can name; the count is a 4-bit field.</summary>
	internal const int MaxRows = 15;

	private readonly int[] _suffixBits;
	private readonly int[] _firstSymbol;
	private readonly bool _plainByte;

	private Indeo4Codebook(int[] suffixBits, int[] firstSymbol, bool plainByte, int maxLength) {
		_suffixBits = suffixBits;
		_firstSymbol = firstSymbol;
		_plainByte = plainByte;
		MaxLength = maxLength;
	}

	/// <summary>The length of the longest code, in bits.</summary>
	internal int MaxLength { get; }

	/// <summary>
	/// Builds a codebook from a descriptor's suffix widths, or returns null when the codec would
	/// refuse it: no rows, a row wider than 8 bits, or a code longer than 13 bits.
	/// </summary>
	internal static Indeo4Codebook? Create(ReadOnlySpan<byte> suffixBits) {
		int rows = suffixBits.Length;
		if (rows == 0 || rows > MaxRows) {
			return null;
		}

		var widths = new int[rows];
		var first = new int[rows];
		int symbols = 0;
		int maxLength = 0;
		for (int r = 0; r < rows; r++) {
			int width = suffixBits[r];
			if (width > 8) {
				return null;
			}

			widths[r] = width;
			first[r] = symbols;
			symbols += 1 << width;

			int length = r + width + (r == rows - 1 ? 0 : 1);
			maxLength = Math.Max(maxLength, length);
		}

		if (maxLength > 13) {
			return null;
		}

		// A descriptor may describe more than 256 codes; the codec builds only the first 256, and
		// takes symbol 255's length from the last row it reaches, which is right only when 255 is
		// in the last row. A stream that sends a code past 255 is refused by the caller.
		if (symbols > 256 && first[rows - 1] > 255) {
			return null;
		}

		// One row of eight bits is special-cased by the codec as a plain byte, read least
		// significant bit first rather than through the suffix's most-significant-first order.
		bool plainByte = rows == 1 && widths[0] == 8;
		return new Indeo4Codebook(widths, first, plainByte, maxLength);
	}

	/// <summary>Reads one symbol; one above 255 is a code the codec never built.</summary>
	internal int Read(ref Indeo4Bits bits) {
		if (_plainByte) {
			return (int)bits.Read(8);
		}

		int last = _suffixBits.Length - 1;
		int row = 0;
		while (row < last && bits.ReadBit() == 1) {
			row++;
		}

		int suffix = 0;
		for (int i = 0; i < _suffixBits[row]; i++) {
			suffix = (suffix << 1) | bits.ReadBit();
		}

		return _firstSymbol[row] + suffix;
	}

	/// <summary>The length in bits of <paramref name="symbol"/>'s code, or -1 if it has none.</summary>
	internal int CodeLength(int symbol) {
		int last = _suffixBits.Length - 1;
		if (_plainByte) {
			return symbol is >= 0 and < 256 ? 8 : -1;
		}

		for (int r = 0; r <= last; r++) {
			if (symbol >= _firstSymbol[r] && symbol < _firstSymbol[r] + (1 << _suffixBits[r])) {
				return r + _suffixBits[r] + (r == last ? 0 : 1);
			}
		}

		return -1;
	}
}
