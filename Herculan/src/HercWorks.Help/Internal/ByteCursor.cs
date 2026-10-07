using System.Buffers.Binary;

namespace HercWorks.Help.Internal;

/// <summary>
/// Thrown inside the parser when the file contradicts itself or runs out. It never leaves the
/// assembly: <see cref="HelpFile.Parse"/> and <see cref="HelpFile.TryGetPicture"/> catch it and report
/// failure instead.
/// </summary>
internal sealed class MalformedHelpException(string message) : Exception(message);

/// <summary>
/// A read position inside a window of a byte array. Every read is bounds-checked against the
/// window, so a length or offset taken from the file can at worst end the parse.
///
/// <para>The compressed integers are those described in docs/retail/formats/winhelp.md#compressed-integers;
/// <see cref="CompressedSignedLong"/> rejects the 4-byte form, whose bias is an Open item there and
/// which no retail file uses.</para>
/// </summary>
internal struct ByteCursor {
	private readonly byte[] _bytes;
	private readonly int _end;

	public ByteCursor(byte[] bytes, int start, int end) {
		if (start < 0 || end < start || end > bytes.Length) {
			throw new MalformedHelpException($"window {start}..{end} lies outside {bytes.Length} bytes");
		}

		_bytes = bytes;
		Position = start;
		_end = end;
	}

	public int Position { get; private set; }

	public readonly int End => _end;

	public readonly int Remaining => _end - Position;

	public readonly bool AtEnd => Position >= _end;

	/// <summary>A cursor over the next <paramref name="length"/> bytes, which this one skips.</summary>
	public ByteCursor Slice(int length) {
		Require(length);
		var slice = new ByteCursor(_bytes, Position, Position + length);
		Position += length;
		return slice;
	}

	public readonly byte Peek() {
		if (Position >= _end) {
			throw new MalformedHelpException($"read past the end at {Position}");
		}

		return _bytes[Position];
	}

	public byte U8() {
		byte value = Peek();
		Position++;
		return value;
	}

	public ushort U16() {
		Require(2);
		ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(Position));
		Position += 2;
		return value;
	}

	public short I16() => unchecked((short)U16());

	public uint U32() {
		Require(4);
		uint value = BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(Position));
		Position += 4;
		return value;
	}

	public int I32() => unchecked((int)U32());

	public void Skip(int length) {
		Require(length);
		Position += length;
	}

	public ReadOnlySpan<byte> Bytes(int length) {
		Require(length);
		var span = _bytes.AsSpan(Position, length);
		Position += length;
		return span;
	}

	public int CompressedUnsignedShort() {
		byte first = U8();
		return (first & 1) == 0 ? first >> 1 : (first | (U8() << 8)) >> 1;
	}

	public int CompressedSignedShort() {
		byte first = U8();
		return (first & 1) == 0 ? (first >> 1) - 0x40 : ((first | (U8() << 8)) >> 1) - 0x4000;
	}

	public long CompressedUnsignedLong() {
		ushort low = U16();
		return (low & 1) == 0 ? low >> 1 : (low | ((long)U16() << 16)) >> 1;
	}

	public int CompressedSignedLong() {
		ushort low = U16();
		if ((low & 1) != 0) {
			throw new MalformedHelpException("4-byte compressed signed long");
		}

		return (low >> 1) - 0x4000;
	}

	/// <summary>A NUL-terminated Windows-1252 string, stopping at the window's end if no NUL comes.</summary>
	public string String(int maxBytes) {
		int start = Position;
		int limit = Math.Min(_end, start + maxBytes);
		int nul = Array.IndexOf(_bytes, (byte)0, start, limit - start);
		if (nul < 0) {
			throw new MalformedHelpException($"unterminated string at {start}");
		}

		Position = nul + 1;
		return Windows1252.Decode(_bytes.AsSpan(start, nul - start));
	}

	private readonly void Require(int length) {
		if (length < 0 || length > _end - Position) {
			throw new MalformedHelpException($"{length} bytes wanted at {Position}, {_end - Position} left");
		}
	}
}
