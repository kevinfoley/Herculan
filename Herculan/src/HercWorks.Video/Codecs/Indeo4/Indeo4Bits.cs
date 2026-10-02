namespace HercWorks.Video.Codecs.Indeo4;

/// <summary>
/// The Indeo 4 bit reader: least significant bit first, so a field's first bit is bit 0 of the byte
/// the cursor is in, and multi-bit fields are assembled with their first bit as the low bit.
///
/// <para>Reading past the end of the data returns zero bits and sets <see cref="Overrun"/>; a
/// caller checks it once a unit of the stream is parsed rather than after every field.</para>
/// </summary>
internal ref struct Indeo4Bits {
	private readonly ReadOnlySpan<byte> _data;
	private long _pos;

	/// <summary>Starts reading <paramref name="data"/> at byte <paramref name="byteOffset"/>.</summary>
	internal Indeo4Bits(ReadOnlySpan<byte> data, int byteOffset) {
		_data = data;
		_pos = (long)byteOffset * 8;
		Overrun = byteOffset > data.Length;
	}

	/// <summary>Whether a read has run past the end of the data.</summary>
	internal bool Overrun { get; private set; }

	/// <summary>Bytes from the start of the data to the first byte not yet wholly read.</summary>
	internal int BytePosition => (int)((_pos + 7) >> 3);

	/// <summary>Reads <paramref name="count"/> bits, at most 32.</summary>
	internal uint Read(int count) {
		uint value = 0;
		for (int i = 0; i < count; i++) {
			value |= (uint)ReadBit() << i;
		}

		return value;
	}

	/// <summary>Reads one bit.</summary>
	internal int ReadBit() {
		long byteIndex = _pos >> 3;
		int bit = 0;
		if (byteIndex < _data.Length) {
			bit = (_data[(int)byteIndex] >> (int)(_pos & 7)) & 1;
		} else {
			Overrun = true;
		}

		_pos++;
		return bit;
	}

	/// <summary>Skips <paramref name="count"/> bits.</summary>
	internal void Skip(long count) {
		_pos += count;
		if ((_pos + 7) >> 3 > _data.Length) {
			Overrun = true;
		}
	}
}
