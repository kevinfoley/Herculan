using HercWorks.Video.Codecs.Indeo3;
using Xunit;

namespace HercWorks.Video.Tests;

/// <summary>
/// Covers the expansion of the Indeo 3 seed area into the VQ codebooks.
///
/// <para>The codebooks are the one part of this codec that cannot be read out of the retail DLL, so
/// they are derived; and a derivation that is subtly wrong produces a picture that is plausible
/// rather than one that is obviously broken. These tests pin it at three separate places: the input
/// bytes against the retail DLL, the whole expanded image against a digest, and two entries against
/// values a real frame is known to produce.</para>
/// </summary>
public sealed class Indeo3CodebookTests {
	/// <summary>
	/// The seed bytes compiled into this assembly are the retail DLL's bytes.
	///
	/// <para>Passes vacuously when the DLL is not in the tree, because it lives in the installer
	/// directory and an install need not keep it. That makes this a check on the copy rather than a
	/// dependency on the original.</para>
	/// </summary>
	[Fact]
	public void SeedAreaMatchesRetailDll() {
		ReadOnlySpan<byte> seed = Indeo3SeedData.SeedArea;
		string? dll = RetailFiles.Find(Path.Combine("INDEO", "IR32_32.DLL"));
		if (dll is null) {
			return;
		}

		byte[] image = File.ReadAllBytes(dll);
		int fileOffset = RetailFiles.VirtualToFileOffset(image, Indeo3SeedData.SeedAreaVirtualAddress);

		Assert.True(
			image.AsSpan(fileOffset, seed.Length).SequenceEqual(seed),
			"The compiled-in seed area differs from IR32_32.DLL.");
	}

	/// <summary>
	/// The seed area's block structure. Two identical runs of eight, then one odd block and seven
	/// alike — the shape that says the walk stayed in step, since a desynchronised walk produces
	/// counts with no pattern at all.
	/// </summary>
	[Fact]
	public void SeedAreaWalksToTwentyFourBlocks() {
		ReadOnlySpan<byte> seed = Indeo3SeedData.SeedArea;
		Indeo3SeedBlock[] blocks = Indeo3Codebooks.ParseSeedArea(seed);

		Assert.Equal(24, blocks.Length);
		Assert.Equal(
			new[] {
				195, 159, 133, 115, 101, 93, 87, 77, 195, 159, 133, 115, 101, 93, 87, 77,
				128, 79, 79, 79, 79, 79, 79, 79,
			},
			blocks.Select(b => b.Count).ToArray());
		Assert.Equal(
			new[] {
				7, 9, 10, 11, 12, 12, 12, 13, 7, 9, 10, 11, 12, 12, 12, 13,
				-11, -13, -13, -13, -13, -13, -13, -13,
			},
			blocks.Select(b => (int)b.Expand).ToArray());

		// The blocks tile the area exactly, ending on the terminator at the last byte.
		int end = blocks[^1].Offset + blocks[^1].EncodedLength;
		Assert.Equal(seed.Length - 1, end);
		Assert.Equal(0, seed[end]);
	}

	/// <summary>
	/// The whole expanded image, as a digest. This is the test that fails if any one of the three
	/// passes is wrong anywhere, including in the regions no frame in the retail corpus reaches.
	/// </summary>
	[Fact]
	public void ExpandedImageMatchesDigest() {
		Indeo3Codebooks codebooks = Indeo3Codebooks.Shared;

		ulong hash = 0xcbf2_9ce4_8422_2325;
		for (int offset = 0; offset < Indeo3Codebooks.ImageWords * 4; offset += 4) {
			uint word = codebooks.WordAt(offset);
			for (int b = 0; b < 4; b++) {
				hash = (hash ^ (byte)(word >> (8 * b))) * 0x0000_0100_0000_01b3;
			}
		}

		Assert.Equal(0x60d8_ce28_421b_3ef5UL, hash);
	}

	/// <summary>
	/// The head of block 0, entry by entry, so a digest failure has somewhere to start from.
	/// </summary>
	[Fact]
	public void BlockZeroSeedsAndReplications() {
		Indeo3Codebooks codebooks = Indeo3Codebooks.Shared;

		Assert.Equal(0x8000_0000u, codebooks.Entry(0, 0));
		Assert.Equal(0x8202_0000u, codebooks.Entry(0, 1));
		Assert.Equal(0x7dfe_0000u, codebooks.Entry(0, 2));
		Assert.Equal(0x82ff_0000u, codebooks.Entry(0, 3));
		Assert.Equal(0x7d01_0000u, codebooks.Entry(0, 4));

		// The block's second sub-table repeats its first.
		Assert.Equal(0x8000_0000u, codebooks.WordAt(0x400));
		Assert.Equal(0x8202_0000u, codebooks.WordAt(0x404));

		// The replicated set, and its bit-31 complement in the second sub-table. The third entry is
		// the one that shows the borrow: (-2, -2) gives 0xFDFDFDFE, not 0xFEFEFEFE.
		Assert.Equal(0x0000_0000u, codebooks.WordAt(0xc000));
		Assert.Equal(0x0202_0202u, codebooks.WordAt(0xc004));
		Assert.Equal(0xfdfd_fdfeu, codebooks.WordAt(0xc008));
		Assert.Equal(0x8000_0000u, codebooks.WordAt(0xc400));
		Assert.Equal(0x8202_0202u, codebooks.WordAt(0xc404));
		Assert.Equal(0x7dfd_fdfeu, codebooks.WordAt(0xc408));
	}

	/// <summary>
	/// Where a block's real content stops and the prefill starts. Block 0 holds 195 seeds and 7x7
	/// expansions, so entry 244 is the first that is not codebook data and reads back as its own
	/// byte offset.
	/// </summary>
	[Fact]
	public void PrefillBeginsAfterSeedsAndExpansions() {
		Indeo3Codebooks codebooks = Indeo3Codebooks.Shared;

		Assert.NotEqual(4u * 243, codebooks.Entry(0, 243));
		Assert.Equal(4u * 244, codebooks.Entry(0, 244));
		Assert.Equal(4u * 255, codebooks.Entry(0, 255));
	}

	/// <summary>
	/// A negative expansion byte transposes the grid of ordered pairs and flips bit 31 of the
	/// replicated set's second sub-table. Shown on two blocks that differ only in that byte's sign,
	/// +2 and -2: the two off-diagonal entries of the 2x2 grid trade places, and only the negative
	/// block's diagonal differs between its two replicated sub-tables.
	/// </summary>
	[Fact]
	public void NegativeExpansionTransposes() {
		const int Seeds = 2;
		Indeo3Codebooks codebooks = Indeo3Codebooks.Build(SeedAreaOfTwoPairBlocks(firstExpand: 2, secondExpand: -2));

		Assert.NotEqual(codebooks.Entry(0, Seeds + 1), codebooks.Entry(0, Seeds + 2));
		Assert.Equal(codebooks.Entry(0, Seeds + 1), codebooks.Entry(1, Seeds + 2));
		Assert.Equal(codebooks.Entry(0, Seeds + 2), codebooks.Entry(1, Seeds + 1));
		Assert.Equal(codebooks.Entry(0, Seeds), codebooks.Entry(1, Seeds));
		Assert.Equal(codebooks.Entry(0, Seeds + 3), codebooks.Entry(1, Seeds + 3));

		uint Replicated(int block, int subTable) =>
			codebooks.WordAt(0xc000 + (block * 0x800) + (subTable * 0x400) + (4 * Seeds));

		Assert.Equal(Replicated(0, 0), Replicated(0, 1));
		Assert.Equal(Replicated(1, 0) ^ 0x8000_0000, Replicated(1, 1));
	}

	/// <summary>
	/// The codebooks against a real frame.
	///
	/// <para>These two values come from the top row of a decoded 160x120 <c>IV32</c> luma plane: from
	/// the strip boundary predictor, the mode byte <c>0x6C</c> with its continuation byte produces
	/// four pixels of 10, and the byte after it produces four of 8. They are worth more than the
	/// structural tests above, because they are the only ones here that would notice if the whole
	/// derivation were self-consistently wrong.</para>
	/// </summary>
	[Fact]
	public void RowDeltaReproducesKnownFrameValues() {
		Indeo3Codebooks codebooks = Indeo3Codebooks.Shared;
		const uint boundaryPredictor = 0x4040_4040;

		// 0x6C overflows on its own, which is how the decoder learns to read a second byte.
		Assert.Equal(
			Indeo3RowDelta.NeedsContinuation,
			codebooks.RowDelta(0, 0x6C, null, boundaryPredictor, out _));

		Assert.Equal(
			Indeo3RowDelta.Complete,
			codebooks.RowDelta(0, 0x6C, 0x6C, boundaryPredictor, out uint first));
		Assert.Equal(0x0a0a_0a0au, first);

		Assert.Equal(
			Indeo3RowDelta.Complete,
			codebooks.RowDelta(0, 0xD3, null, first, out uint second));
		Assert.Equal(0x0808_0808u, second);
	}

	/// <summary>A malformed seed area is rejected rather than walked off the end.</summary>
	[Fact]
	public void MalformedSeedAreasAreRejected() {
		// A count of 5 with only two body bytes.
		Assert.Throws<InvalidDataException>(() => Indeo3Codebooks.ParseSeedArea(new byte[] { 5, 1, 2 }));

		// A complete block with no terminator after it.
		Assert.Throws<InvalidDataException>(() => Indeo3Codebooks.ParseSeedArea(new byte[] { 1, 1, 2, 3 }));

		// Nothing at all.
		Assert.Throws<InvalidDataException>(() => Indeo3Codebooks.ParseSeedArea(ReadOnlySpan<byte>.Empty));
	}

	/// <summary>
	/// A seed area of the 24 blocks the codec addresses, each holding the two distinct pairs
	/// <c>(1, 2)</c> and <c>(3, 4)</c>. The first two blocks take the given expansion bytes, the rest 1.
	/// </summary>
	private static byte[] SeedAreaOfTwoPairBlocks(sbyte firstExpand, sbyte secondExpand) {
		var seed = new List<byte>();
		for (int block = 0; block < Indeo3Codebooks.BlockCount; block++) {
			sbyte expand = block switch { 0 => firstExpand, 1 => secondExpand, _ => 1 };
			seed.AddRange([2, 1, 2, 3, 4, (byte)expand]);
		}

		seed.Add(0);
		return [.. seed];
	}
}
