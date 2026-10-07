namespace HercWorks.Video.Codecs.Indeo3;

/// <summary>
/// The Indeo 3 VQ codebooks: the table that turns a mode byte from the cell stream into the pixel
/// deltas it stands for.
///
/// <para>The retail codec does not ship these tables. It ships the seed area
/// (<see cref="Indeo3SeedData"/>) and expands it at codec init into a 96 KB image in <c>.bss</c>,
/// which is why the eight objects in <c>IR32_32.DLL</c> that have exactly a codebook's shape are
/// zero-filled on disk. This class is that expansion, run once for the process.</para>
///
/// <para>The image tiles as 24 blocks of <c>0x800</c> bytes, twice over: the delta set at
/// <c>+0x0000</c> and the byte-replicated set at <c>+0xc000</c>. Each <c>0x800</c> block is two
/// 256-entry sub-tables, at <c>+0x000</c> and <c>+0x400</c>. Which of the 24 blocks a cell reads is
/// chosen per frame by the bitstream header's <c>cb_offset</c> and <c>alt_quant</c> fields.</para>
///
/// <para>See <c>docs/retail/formats/indeo3.md</c>, "Codebooks", for the derivation and its evidence.</para>
/// </summary>
internal sealed class Indeo3Codebooks {
	/// <summary>Length of the expanded image, in 32-bit words.</summary>
	internal const int ImageWords = 0x18000 / 4;

	/// <summary>How many codebook blocks the seed area expands to.</summary>
	internal const int BlockCount = 24;

	/// <summary>Distance between one block and the next, in words.</summary>
	internal const int BlockStrideWords = 0x800 / 4;

	/// <summary>Word offset of the byte-replicated table set.</summary>
	internal const int ReplicatedSetWords = 0xc000 / 4;

	/// <summary>Entries in each of the two sub-tables a block holds.</summary>
	internal const int SubTableWords = 0x400 / 4;

	/// <summary>
	/// The expansion, built once. Deriving it costs a single pass over 24 KB, but every frame of
	/// every stream wants the same answer, so it is shared rather than rebuilt per decoder.
	/// </summary>
	internal static Indeo3Codebooks Shared { get; } = Build(Indeo3SeedData.SeedArea);

	private Indeo3Codebooks(uint[] words, Indeo3SeedBlock[] blocks, short[][] dyads, uint[][] wideDyads) {
		_words = words;
		Blocks = blocks;
		_dyads = dyads;
		_wideDyads = wideDyads;
	}

	private readonly uint[] _words;
	private readonly short[][] _dyads;
	private readonly uint[][] _wideDyads;

	/// <summary>The parsed seed blocks, in image order. One per codebook block.</summary>
	internal Indeo3SeedBlock[] Blocks { get; }

	/// <summary>
	/// Expands a seed area into the codebook image.
	///
	/// <para>Three passes, in this order, because the later ones overwrite the earlier:</para>
	///
	/// <list type="number">
	/// <item>Prefill: every word holds its own byte offset from the image base. Word 0 is left
	/// alone. These are not codebook entries — they are what an index past the end of a block's real
	/// content reads, and reproducing them matters only so that a stream which indexes out of its
	/// block gets the same garbage the retail codec gets.</item>
	/// <item>Seeding: each of the block's <c>N</c> pairs becomes one entry, biased into the top half
	/// of the word so the delta add cannot borrow across the two pixels it codes.</item>
	/// <item>Expansion: a further <c>d*d</c> entries over ordered pairs of the first <c>d</c> seeds,
	/// giving the two-pixel deltas the seeds themselves do not cover. The sign of the expansion byte
	/// transposes which index runs outermost.</item>
	/// </list>
	/// </summary>
	/// <exception cref="InvalidDataException">The seed area is malformed.</exception>
	internal static Indeo3Codebooks Build(ReadOnlySpan<byte> seed) {
		Indeo3SeedBlock[] blocks = ParseSeedArea(seed);

		uint[] words = new uint[ImageWords];
		for (int w = 1; w < ImageWords; w++) {
			words[w] = (uint)(w * 4);
		}

		var dyads = new short[BlockCount][];
		var wideDyads = new uint[BlockCount][];

		for (int q = 0; q < blocks.Length && q < BlockCount; q++) {
			Indeo3SeedBlock block = blocks[q];
			int primary = q * BlockStrideWords;
			int replicated = ReplicatedSetWords + (q * BlockStrideWords);

			dyads[q] = new short[block.Count];
			wideDyads[q] = new uint[block.Count];

			for (int k = 0; k < block.Count; k++) {
				dyads[q][k] = block.PairValue(seed, k);

				uint seeded = SeededWord(block.PairValue(seed, k));
				words[primary + k] = seeded;
				words[primary + SubTableWords + k] = seeded;

				uint repl = ReplicatedWord(block.Low(seed, k), block.High(seed, k));
				wideDyads[q][k] = repl;
				words[replicated + k] = repl;
				words[replicated + SubTableWords + k] = repl ^ 0x8000_0000;
			}

			int d = Math.Abs(block.Expand);
			bool transposed = block.Expand < 0;
			int index = block.Count;

			for (int outer = 0; outer < d; outer++) {
				for (int inner = 0; inner < d; inner++) {
					// For a non-negative expansion byte the j index is the outer loop, so the pair
					// is (inner, outer); a negative one swaps them.
					int i = transposed ? outer : inner;
					int j = transposed ? inner : outer;

					short vi = block.PairValue(seed, i);
					short vj = block.PairValue(seed, j);
					uint combined = unchecked((uint)((vi << 16) + vj));

					words[primary + index] = combined;
					words[primary + SubTableWords + index] = combined;

					uint replJ = ReplicatedWord(block.Low(seed, j), block.High(seed, j));
					uint replI = ReplicatedWord(block.Low(seed, i), block.High(seed, i));
					words[replicated + index] = replJ;
					words[replicated + SubTableWords + index] = transposed ? replI ^ 0x8000_0000 : replI;

					index++;
				}
			}
		}

		if (blocks.Length < BlockCount) {
			throw new InvalidDataException(
				$"Indeo 3 seed area holds {blocks.Length} blocks; the codec addresses {BlockCount}.");
		}

		return new Indeo3Codebooks(words, blocks, dyads, wideDyads);
	}

	/// <summary>
	/// Walks the seed area's block structure: a count byte, that many signed byte pairs, then one
	/// signed expansion byte, repeating until a count byte of zero.
	///
	/// <para>The count is unsigned. The first block's is <c>0xC3</c>, and reading it signed
	/// desynchronises the walk at the very first block.</para>
	/// </summary>
	/// <exception cref="InvalidDataException">A block runs past the end, or no terminator is found.</exception>
	internal static Indeo3SeedBlock[] ParseSeedArea(ReadOnlySpan<byte> seed) {
		List<Indeo3SeedBlock> blocks = [];

		int at = 0;
		while (true) {
			if (at >= seed.Length) {
				throw new InvalidDataException("Indeo 3 seed area has no terminating zero count byte.");
			}

			int count = seed[at];
			if (count == 0) {
				return [.. blocks];
			}

			// The count byte, 2*count body bytes, and the expansion byte must all fit.
			if (at + 1 + (2 * count) + 1 > seed.Length) {
				throw new InvalidDataException(
					$"Indeo 3 seed block at offset {at} runs past the end of the seed area.");
			}

			blocks.Add(new Indeo3SeedBlock(at, count, (sbyte)seed[at + 1 + (2 * count)]));
			at += 2 + (2 * count);
		}
	}

	/// <summary>
	/// The word a seed pair contributes directly: the pair's 16-bit value biased by <c>0x8000</c>
	/// and moved into the top half, leaving the bottom half zero.
	/// </summary>
	internal static uint SeededWord(short value) => (uint)(ushort)(value + 0x8000) << 16;

	/// <summary>
	/// The byte-replicated form of a pair, for the <c>+0xc000</c> table set: the two bytes emitted
	/// as <c>(b, b, a, a)</c>.
	///
	/// <para>Accumulated with 32-bit addition rather than assembled by OR, which is not the same
	/// thing: a negative low byte borrows into the byte above it, so <c>(-2, -2)</c> gives
	/// <c>0xFDFDFDFE</c> and not <c>0xFEFEFEFE</c>.</para>
	/// </summary>
	internal static uint ReplicatedWord(sbyte low, sbyte high) =>
		unchecked((uint)((high << 24) + (high << 16) + (low << 8) + low));

	/// <summary>The word at <paramref name="byteOffset"/> from the image base.</summary>
	internal uint WordAt(int byteOffset) {
		if (byteOffset < 0 || byteOffset > (ImageWords * 4) - 4 || (byteOffset & 3) != 0) {
			throw new ArgumentOutOfRangeException(nameof(byteOffset));
		}

		return _words[byteOffset / 4];
	}

	/// <summary>How many dyads block <paramref name="block"/> holds: mode bytes below this are dyad codes.</summary>
	internal int DyadCount(int block) => _dyads[block].Length;

	/// <summary>
	/// The side of block <paramref name="block"/>'s quad square: a mode byte at or past
	/// <see cref="DyadCount"/> names an ordered pair of the first this-many dyads.
	/// </summary>
	internal int QuadSide(int block) => Math.Abs(Blocks[block].Expand);

	/// <summary>
	/// Dyad <paramref name="index"/> of block <paramref name="block"/>: the deltas for two adjacent
	/// pixels, as one 16-bit value added to both at once, first pixel in the low byte.
	/// </summary>
	internal short Dyad(int block, int index) => _dyads[block][index];

	/// <summary>
	/// The same dyad widened for the 8x8 modes, each delta doubled horizontally: bytes
	/// <c>(a, a, b, b)</c>, the replicated set's entry.
	/// </summary>
	internal uint WideDyad(int block, int index) => _wideDyads[block][index];

	/// <summary>Entry <paramref name="index"/> of block <paramref name="block"/>'s first sub-table.</summary>
	internal uint Entry(int block, int index) {
		if ((uint)block >= BlockCount) {
			throw new ArgumentOutOfRangeException(nameof(block));
		}

		if ((uint)index >= SubTableWords) {
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		return _words[(block * BlockStrideWords) + index];
	}

	/// <summary>
	/// Applies one mode byte to a row predictor, which is the innermost step of cell reconstruction.
	///
	/// <para>The predictor holds four 7-bit pixels packed one per byte, and the codebook entry holds
	/// the deltas to add, so one 32-bit add updates all four at once. Bit 31 is the carry out of that
	/// add and doubles as the signal that the delta did not fit in one byte: when it comes back set,
	/// the stream carries a second byte whose entry supplies the bottom half's correction.</para>
	///
	/// <para>A second byte that still leaves bit 31 set is the retail codec's error code 2 — a
	/// malformed stream, reported here as <see cref="Indeo3RowDelta.RangeFault"/>.</para>
	/// </summary>
	/// <param name="block">Which of the 24 codebook blocks this cell reads.</param>
	/// <param name="modeByte">The mode byte, used as an index into the block's second sub-table.</param>
	/// <param name="continuation">The following stream byte, or null if the caller has not read one.</param>
	/// <param name="predictor">The four packed pixels this row predicts from.</param>
	/// <param name="result">The four updated pixels, when the return is <see cref="Indeo3RowDelta.Complete"/>.</param>
	internal Indeo3RowDelta RowDelta(int block, byte modeByte, byte? continuation, uint predictor, out uint result) {
		if ((uint)block >= BlockCount) {
			throw new ArgumentOutOfRangeException(nameof(block));
		}

		result = 0;

		int table = (block * BlockStrideWords) + SubTableWords;
		uint sum = unchecked(predictor + _words[table + modeByte]);

		if ((sum & 0x8000_0000) == 0) {
			result = sum;
			return Indeo3RowDelta.Complete;
		}

		if (continuation is not byte next) {
			return Indeo3RowDelta.NeedsContinuation;
		}

		// Clearing both halves' sign bits leaves the two pixel pairs independent again; the
		// continuation entry's own low half is then the correction for this row's low half.
		uint cleared = sum ^ 0x8000_8000;
		ushort correction = (ushort)(_words[table + next] >> 16);
		uint low = (ushort)(unchecked((ushort)cleared + correction));
		uint corrected = (cleared & 0xFFFF_0000) | low;

		if ((corrected & 0x8000_0000) != 0) {
			return Indeo3RowDelta.RangeFault;
		}

		result = corrected;
		return Indeo3RowDelta.Complete;
	}
}

/// <summary>The outcome of one <see cref="Indeo3Codebooks.RowDelta"/> application.</summary>
internal enum Indeo3RowDelta {
	/// <summary>The row updated. <c>result</c> holds the four new pixels.</summary>
	Complete,

	/// <summary>
	/// The delta needs a second stream byte. The caller consumes the next byte and calls again with
	/// the same mode byte and predictor.
	/// </summary>
	NeedsContinuation,

	/// <summary>The stream is malformed: the two-byte form still overflowed.</summary>
	RangeFault,
}

/// <summary>
/// One variable-length block of the seed area: a count, that many signed byte pairs, and a signed
/// expansion byte. The pairs themselves stay in the seed span rather than being copied out.
/// </summary>
/// <param name="Offset">Byte offset of this block's count byte within the seed area.</param>
/// <param name="Count">How many byte pairs the block body holds.</param>
/// <param name="Expand">The expansion byte: its magnitude is the expansion dimension, its sign the pair order.</param>
internal readonly record struct Indeo3SeedBlock(int Offset, int Count, sbyte Expand) {
	/// <summary>The low byte of pair <paramref name="index"/>, which is the first pixel's delta.</summary>
	internal sbyte Low(ReadOnlySpan<byte> seed, int index) => (sbyte)seed[Offset + 1 + (2 * index)];

	/// <summary>The high byte of pair <paramref name="index"/>, which is the second pixel's delta.</summary>
	internal sbyte High(ReadOnlySpan<byte> seed, int index) => (sbyte)seed[Offset + 2 + (2 * index)];

	/// <summary>
	/// Pair <paramref name="index"/> read as one signed 16-bit value, high byte first. This is the
	/// form the expansion arithmetic works in, and the fold to 16 bits is load-bearing: carrying the
	/// wider value through gives a different image.
	/// </summary>
	internal short PairValue(ReadOnlySpan<byte> seed, int index) =>
		(short)((High(seed, index) << 8) + Low(seed, index));

	/// <summary>How many bytes this block occupies, counting its count and expansion bytes.</summary>
	internal int EncodedLength => 2 + (2 * Count);
}
