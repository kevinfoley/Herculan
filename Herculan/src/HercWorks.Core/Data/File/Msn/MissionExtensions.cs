namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// HERCULAN's own settings for a mission, which the retail format has no place for. This is HERCULAN's invention, not
/// retail's: a block written after row 17, which neither of VSHELL's readers of a <c>.MSN</c> reaches
/// (docs/retail/formats/msn-mission-file.md#what-the-loads-read), so retail loads the mission as if it were absent.
///
/// <para>The block is the eight ASCII bytes <see cref="Signature"/>, an <c>int16</c> layout version, an <c>int16</c> payload
/// length in bytes, then the payload. Version 1's payload is one <c>int16</c> of flags, bit 0 <see cref="HerculanOnly"/>.
/// Payload bytes past what this version reads are kept in <see cref="Unread"/>, and the version as read, so a write gives a
/// later version's block back as it found it.</para>
/// </summary>
public sealed record MissionExtensions {
	/// <summary>What the block opens with.</summary>
	public static ReadOnlySpan<byte> Signature => "HERCULAN"u8;

	/// <summary>The layout version this code writes.</summary>
	public const short CurrentVersion = 1;

	private const short HerculanOnlyFlag = 1;

	// The signature, version and payload length.
	private const int HeaderSize = 12;

	/// <summary>The layout version the block was read with, or <see cref="CurrentVersion"/> for a new one.</summary>
	public short Version { get; init; } = CurrentVersion;

	/// <summary>
	/// Whether the mission is meant to be played in HERCULAN alone, so the editor holds it to none of the limits retail alone
	/// has (<see cref="RosterLimits"/>, <see cref="SkipArrayLimits"/>, <see cref="MapMarkerLimits"/>, <see cref="ObjectLimits"/>).
	/// </summary>
	public bool HerculanOnly { get; init; }

	/// <summary>The flag bits besides <see cref="HerculanOnly"/>, kept as read.</summary>
	public short OtherFlags { get; init; }

	/// <summary>The payload's bytes past its flags, kept as read.</summary>
	public byte[] Unread { get; init; } = [];

	/// <summary>Whether the block says anything, so a write needs it: a mission with none is written as retail writes it.</summary>
	public bool Any => HerculanOnly || OtherFlags != 0 || Unread.Length > 0 || Version != CurrentVersion;

	/// <summary>
	/// The block at the start of <paramref name="bytes"/> and how many bytes it takes, or null and 0 when they do not open with
	/// <see cref="Signature"/> and a whole header and payload.
	/// </summary>
	public static MissionExtensions? Read(ReadOnlySpan<byte> bytes, out int length) {
		length = 0;
		if (bytes.Length < HeaderSize || !bytes[..Signature.Length].SequenceEqual(Signature)) {
			return null;
		}

		short version = Word(bytes, 8);
		int payload = (ushort)Word(bytes, 10);
		if (bytes.Length < HeaderSize + payload) {
			return null;
		}

		var body = bytes.Slice(HeaderSize, payload);
		short flags = body.Length >= 2 ? Word(body, 0) : (short)0;
		length = HeaderSize + payload;
		return new MissionExtensions {
			Version = version,
			HerculanOnly = (flags & HerculanOnlyFlag) != 0,
			OtherFlags = (short)(flags & ~HerculanOnlyFlag),
			Unread = body.Length > 2 ? body[2..].ToArray() : [],
		};
	}

	/// <summary>The block's bytes.</summary>
	public byte[] Write() {
		var bytes = new byte[HeaderSize + 2 + Unread.Length];
		Signature.CopyTo(bytes);
		Put(bytes, 8, Version);
		Put(bytes, 10, (short)(2 + Unread.Length));
		Put(bytes, HeaderSize, (short)((ushort)OtherFlags | (HerculanOnly ? HerculanOnlyFlag : 0)));
		Unread.CopyTo(bytes, HeaderSize + 2);
		return bytes;
	}

	private static short Word(ReadOnlySpan<byte> bytes, int at) => (short)(bytes[at] | (bytes[at + 1] << 8));

	private static void Put(byte[] bytes, int at, short value) {
		bytes[at] = (byte)value;
		bytes[at + 1] = (byte)(value >> 8);
	}
}
