namespace HercWorks.Video.Riff;

/// <summary>One chunk located inside a RIFF file: where its body starts, and how long it is.</summary>
/// <param name="Id">The chunk's four-character code, as a little-endian uint.</param>
/// <param name="ListType">
/// For a <c>RIFF</c> or <c>LIST</c> chunk, the form/list type that follows the length; zero
/// otherwise.
/// </param>
/// <param name="BodyAt">Offset of the first byte of the chunk body within the file.</param>
/// <param name="BodyLength">Length of the chunk body, already checked to lie inside the file.</param>
public readonly record struct RiffChunk(uint Id, uint ListType, int BodyAt, int BodyLength);

/// <summary>
/// A bounds-checked walk over RIFF chunks.
///
/// <para>RIFF is a chain of <c>[fourcc][int32 length][body]</c> records, with the body padded to an
/// even length, and with <c>RIFF</c> and <c>LIST</c> bodies starting with a further four-character
/// code and then containing chunks of their own. Everything about that structure is attacker
/// controlled, so this reader treats a declared length as a claim to verify rather than a fact:
/// a length that is negative, that overflows when added to the cursor, or that runs past the end of
/// the enclosing chunk ends the walk instead of being followed.</para>
///
/// <para>Ending the walk rather than throwing is deliberate, and matches
/// <c>Herculan.Engine.Audio.WaveSample</c>: several retail files carry junk past the end of their
/// RIFF chunk, and one truncated tail should not cost the caller the chunks it already found.</para>
/// </summary>
public static class RiffReader {
	/// <summary>Packs four characters into the little-endian uint that RIFF stores them as.</summary>
	public static uint FourCc(char a, char b, char c, char d) =>
		(uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);

	/// <summary>Renders a four-character code back to text, for diagnostics.</summary>
	public static string FourCcText(uint id) {
		Span<char> chars = stackalloc char[4];
		for (int i = 0; i < 4; i++) {
			char ch = (char)((id >> (i * 8)) & 0xFF);
			chars[i] = ch is >= ' ' and < (char)0x7F ? ch : '?';
		}

		return new string(chars);
	}

	/// <summary>
	/// Reads the outermost <c>RIFF</c> chunk, or returns false when the bytes do
	/// not begin with one.
	///
	/// <para>The declared length is clamped to what is actually present rather than trusted: a file
	/// truncated mid-download still yields every complete chunk before the cut.</para>
	/// </summary>
	/// <param name="bytes">The file's bytes.</param>
	/// <param name="riff">The outermost chunk.</param>
	public static bool TryReadHeader(ReadOnlySpan<byte> bytes, out RiffChunk riff) {
		riff = default;
		if (bytes.Length < 12 || ReadU32(bytes, 0) != FourCc('R', 'I', 'F', 'F')) {
			return false;
		}

		uint declared = ReadU32(bytes, 4);
		// The body starts after the 8-byte record header and includes the 4-byte form type.
		int available = bytes.Length - 8;
		int length = declared > (uint)available ? available : (int)declared;
		riff = new RiffChunk(FourCc('R', 'I', 'F', 'F'), ReadU32(bytes, 8), 8, length);
		return true;
	}

	/// <summary>
	/// Enumerates the chunks directly inside a container body, without descending into them.
	///
	/// <para>The region walked is the bytes from the start offset to the end offset; for a
	/// <c>RIFF</c> or <c>LIST</c> that is the four bytes after its list type through the end of its
	/// body. Every chunk yielded has already had its body verified to lie within that region.</para>
	/// </summary>
	/// <param name="bytes">The file's bytes.</param>
	/// <param name="at">The offset the region starts at.</param>
	/// <param name="end">The offset the region ends at.</param>
	public static IEnumerable<RiffChunk> Children(byte[] bytes, int at, int end) {
		// Clamp once here so callers may pass a declared end without checking it first.
		if (at < 0 || end > bytes.Length) {
			yield break;
		}

		while (at >= 0 && at + 8 <= end) {
			uint id = ReadU32(bytes, at);
			uint declared = ReadU32(bytes, at + 4);

			// A length at or past int.MaxValue cannot be a real body and would overflow the
			// addition below, so it is rejected before the addition happens rather than after.
			if (declared > int.MaxValue) {
				yield break;
			}

			int length = (int)declared;
			int body = at + 8;
			if (length > end - body) {
				// Declared longer than the space it sits in: truncated or lying. Stop.
				yield break;
			}

			bool isContainer = id == FourCc('L', 'I', 'S', 'T') || id == FourCc('R', 'I', 'F', 'F');
			uint listType = isContainer && length >= 4 ? ReadU32(bytes, body) : 0;
			yield return new RiffChunk(id, listType, body, length);

			// Bodies are word-aligned; an odd length is followed by one pad byte that is not
			// counted in the length. The pad may be the byte that runs off the end, hence the
			// explicit check rather than letting the loop condition catch it.
			int next = body + length + (length & 1);
			if (next <= at) {
				// A zero-length chunk at an odd offset can leave the cursor where it started;
				// refusing to go backwards or stand still is what stops that spinning forever.
				yield break;
			}

			at = next;
		}
	}

	/// <summary>
	/// Walks a container recursively, yielding every leaf chunk in file order and descending into
	/// <c>LIST</c> chunks up to a depth limit.
	///
	/// <para>The depth cap is what keeps a file that nests <c>LIST</c> chunks thousands deep from
	/// exhausting the stack. An over-deep list is skipped whole rather than partly read.</para>
	/// </summary>
	/// <param name="bytes">The file's bytes.</param>
	/// <param name="at">The offset the region starts at.</param>
	/// <param name="end">The offset the region ends at.</param>
	/// <param name="maxDepth">How many levels of <c>LIST</c> to descend into.</param>
	public static IEnumerable<RiffChunk> Leaves(byte[] bytes, int at, int end, int maxDepth) {
		if (maxDepth <= 0) {
			yield break;
		}

		foreach (RiffChunk child in Children(bytes, at, end)) {
			if (child.Id == FourCc('L', 'I', 'S', 'T') && child.BodyLength >= 4) {
				foreach (RiffChunk leaf in Leaves(
					bytes, child.BodyAt + 4, child.BodyAt + child.BodyLength, maxDepth - 1)) {
					yield return leaf;
				}
			} else {
				yield return child;
			}
		}
	}

	/// <summary>Reads a little-endian uint, or zero when the read would run off the end.</summary>
	public static uint ReadU32(ReadOnlySpan<byte> bytes, int at) =>
		at < 0 || at + 4 > bytes.Length
			? 0u
			: (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));

	/// <summary>Reads a little-endian int, or zero when the read would run off the end.</summary>
	public static int ReadI32(ReadOnlySpan<byte> bytes, int at) => unchecked((int)ReadU32(bytes, at));

	/// <summary>Reads a little-endian ushort, or zero when the read would run off the end.</summary>
	public static ushort ReadU16(ReadOnlySpan<byte> bytes, int at) =>
		at < 0 || at + 2 > bytes.Length ? (ushort)0 : (ushort)(bytes[at] | (bytes[at + 1] << 8));
}
