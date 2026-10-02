namespace HercWorks.Video.Codecs.Indeo4;

/// <summary>
/// The inverse transforms and the stores that write their output, on the codec's packed form: each
/// 32-bit word carries two 16-bit lanes, the low lane for one block and the high lane for the block
/// paired with it, every value biased so the lanes stay apart. See
/// docs/formats/indeo4.md#transforms.
///
/// <para>The arithmetic is the codec's, constant for constant, on whole words. Carries and borrows
/// between the lanes are therefore exactly the codec's too, which is what makes the output match
/// <c>IR41_32.DLL</c> bit for bit rather than only to within rounding.</para>
/// </summary>
internal static class Indeo4Transforms {
	/// <summary>Words per row of the coefficient buffer: eight columns and one unused.</summary>
	internal const int Stride = 9;

	/// <summary>The coefficient buffer's fill for an 8x8 Haar block: zero in both lanes.</summary>
	internal const uint HaarFill = 0x4000_4000;

	/// <summary>The coefficient buffer's fill for a 4x4 slant block: zero in both lanes.</summary>
	internal const uint SlantFill = 0x0800_0800;

	/// <summary>Inverse 8x8 Haar, <c>Inv Haar 8x8</c> at <c>10021010</c>.</summary>
	internal static void InverseHaar8x8(Span<uint> b) {
		// Columns 4 to 7, then 0 to 3 with a coarser mask, then the rows.
		for (int c = 7; c >= 4; c--) {
			HaarColumn(b, c, 0xFFFC_FFFC);
		}

		for (int c = 3; c >= 0; c--) {
			HaarColumn(b, c, 0xFFFE_FFFE);
		}

		for (int r = 7; r >= 0; r--) {
			HaarRow(b, r * Stride);
		}
	}

	private static void HaarColumn(Span<uint> b, int c, uint mask) {
		int r0 = c, r1 = c + Stride, r2 = c + (2 * Stride), r3 = c + (3 * Stride);
		int r4 = c + (4 * Stride), r5 = c + (5 * Stride), r6 = c + (6 * Stride), r7 = c + (7 * Stride);

		uint eax = b[r0] + 0xC000_C000;
		uint ebx = b[r1];
		eax -= ebx;
		uint edx = b[r2];
		uint ecx = b[r3];
		uint edi = b[r7];
		ebx = eax + (ebx * 2) + 0x7FFF_8000;
		eax -= ecx;
		ebx -= edx;
		uint esi = b[r6];
		ecx = eax + (ecx * 2) + 0x7FFF_8000;
		eax -= edi;
		ecx -= esi;
		edx = ebx + (edx * 2) + 0x7FFF_8000;
		edi = eax + (edi * 2) + 0x7FFF_8000;
		eax &= mask;
		esi = ecx + (esi * 2) + 0x7FFF_8000;
		ecx &= mask;
		b[r7] = eax;
		edi &= mask;
		eax = b[r5];
		b[r5] = ecx;
		ecx = b[r4];
		esi &= mask;
		ebx -= eax;
		b[r6] = edi;
		edx -= ecx;
		b[r4] = esi;
		eax = ebx + (eax * 2) + 0x7FFF_8000;
		ebx &= mask;
		ecx = edx + (ecx * 2) + 0x7FFF_8000;
		edx &= mask;
		b[r3] = ebx;
		eax &= mask;
		b[r1] = edx;
		ecx &= mask;
		b[r0] = ecx;
		b[r2] = eax;
	}

	private static void HaarRow(Span<uint> b, int row) {
		uint eax = b[row] >> 1;
		uint ebx = b[row + 1] >> 1;
		eax += 0x8000_8000;
		uint ecx = b[row + 3] >> 1;
		eax -= ebx;
		uint edx = b[row + 2] >> 1;
		ebx = eax + (ebx * 2) + 0xBFFF_C000;
		uint edi = b[row + 7] >> 1;
		eax -= ecx;
		uint esi = b[row + 6] >> 1;
		ebx -= edx;
		ecx = eax + (ecx * 2) + 0xBFFF_C000;
		eax -= edi;
		edx = ebx + (edx * 2) + 0xBFFF_C000;
		ecx -= esi;
		edi = eax + (edi * 2) + 0xBFFF_C000;
		eax &= 0xFFFC_FFFC;
		esi = ecx + (esi * 2) + 0xBFFF_C000;
		b[row + 7] = eax;
		ecx &= 0xFFFC_FFFC;
		eax = b[row + 5];
		b[row + 5] = ecx;
		ecx = b[row + 4];
		eax >>= 1;
		edi &= 0xFFFC_FFFC;
		ecx >>= 1;
		b[row + 6] = edi;
		ebx -= eax;
		esi &= 0xFFFC_FFFC;
		b[row + 4] = esi;
		edx -= ecx;
		eax = ebx + (eax * 2) + 0xBFFF_C000;
		ebx &= 0xFFFC_FFFC;
		ecx = edx + (ecx * 2) + 0xBFFF_C000;
		edx &= 0xFFFC_FFFC;
		b[row + 3] = ebx;
		eax &= 0xFFFC_FFFC;
		b[row + 1] = edx;
		ecx &= 0xFFFC_FFFC;
		b[row] = ecx;
		b[row + 2] = eax;
	}

	/// <summary>Inverse 4x4 slant, <c>Inv Slant 4x4</c> at <c>1001e1c0</c>.</summary>
	internal static void InverseSlant4x4(Span<uint> b) {
		for (int c = 3; c >= 0; c--) {
			int i0 = c, i1 = c + Stride, i2 = c + (2 * Stride), i3 = c + (3 * Stride);
			uint eax = b[i3];
			uint ebx = b[i1];
			uint ecx = b[i0];
			uint edx = b[i2];
			uint edi = (ebx * 5) + 0x0002_0002;
			uint esi = (eax * 5) + 0xFFFD_FFFE;
			esi = ~esi;
			ecx += 0x2000_2000;
			edi = edi + (eax * 2) + 0x0800_0800;
			esi = esi + (ebx * 2) + 0x5800_5801;
			edi >>= 2;
			eax = ecx + edx + 0xEFFF_F000;
			esi >>= 2;
			edi &= 0xFFFF_3FFF;
			ecx -= edx;
			esi &= 0xFFFF_3FFF;
			edx = eax + edi + 0xDFFF_E000;
			eax -= edi;
			edi = ecx + esi + 0xDFFF_E000;
			b[i0] = edx;
			b[i1] = edi;
			ecx -= esi;
			b[i2] = ecx;
			b[i3] = eax;
		}

		for (int r = 3; r >= 0; r--) {
			int row = r * Stride;
			uint eax = b[row + 3];
			uint ebx = b[row + 1];
			uint ecx = b[row];
			uint edx = b[row + 2];
			uint edi = (ebx * 5) + 0x0002_0002;
			uint esi = (eax * 5) + 0xFFFD_FFFE;
			esi = ~esi;
			ecx += 0x2000_2000;
			edi = edi + (eax * 2) + 0x1000_1000;
			esi = esi + (ebx * 2) + 0xB000_B001;
			edi >>= 2;
			eax = ecx + edx + 0xDFFF_E000;
			esi >>= 2;
			edi &= 0xFFFF_3FFF;
			ecx -= edx;
			esi &= 0xFFFF_3FFF;
			edx = eax + edi + 0xE000_E001;
			eax += 0x2001_2001;
			eax -= edi;
			edi = ecx + esi + 0xE000_E001;
			ecx += 0x2001_2001;
			edx += edx;
			edx &= 0xFFFC_FFFC;
			edi += edi;
			edi &= 0xFFFC_FFFC;
			b[row] = edx;
			ecx -= esi;
			b[row + 1] = edi;
			ecx += ecx;
			eax += eax;
			ecx &= 0xFFFC_FFFC;
			eax &= 0xFFFC_FFFC;
			b[row + 2] = ecx;
			b[row + 3] = eax;
		}
	}
}
