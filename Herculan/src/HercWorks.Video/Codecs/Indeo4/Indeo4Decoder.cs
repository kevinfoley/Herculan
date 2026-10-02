using HercWorks.Video.Avi;

namespace HercWorks.Video.Codecs.Indeo4;

/// <summary>
/// Intel Indeo Video 4.1 (<c>IV41</c>), decoded to the same YUV planes as <c>IR41_32.DLL</c>.
///
/// <para>Each plane is one band of 16-bit samples, cut into macroblocks: 16x16 for luma, coded as
/// four 8x8 Haar blocks, and 4x4 for chroma, one 4x4 slant block each. A frame decodes into one of
/// three buffers, predicting from another; see docs/formats/indeo4.md.</para>
///
/// <para>The buffers keep the codec's own sample order, in which each 32-bit word holds two samples
/// a block width apart, because motion compensation and the stores write whole words or single
/// lanes and a block's neighbour is sometimes written as a side effect. Keeping the layout is what
/// reproduces those side effects; the output is converted to raster order only at the end.</para>
///
/// <para>Only what <c>ES2DROP3.AVI</c> exercises is decoded: one tile, one band per plane, 4:1:0
/// chroma, full-pel motion, and frame types 0, 1, 2 and 4. Anything else is refused rather than
/// guessed at; see docs/formats/indeo4.md#open.</para>
/// </summary>
internal sealed class Indeo4Decoder : IVideoCodec {
	private const uint SyncCode = 0x3FFF8;

	private const int FrameIntra = 0;
	private const int FrameIntraNoResize = 1;
	private const int FrameInter = 2;
	private const int FrameBidirectional = 3;
	private const int FrameInterDroppable = 4;
	private const int FrameRepeat = 5;

	/// <summary>Bytes of padding the codec allocates after each band, and after each buffer.</summary>
	private const int BandSlackBytes = 0x400;
	private const int BufferSlackBytes = 0x620;

	private const uint BiasedZeroPair = 0x4000_4000;
	private const uint NoMotion = 0x8080;

	private Indeo4Decoder(int width, int height, VideoLimits limits) {
		_width = width;
		_height = height;
		_limits = limits;
	}

	private readonly int _width;
	private readonly int _height;
	private readonly VideoLimits _limits;

	private ushort[]? _mem;
	private int _bufferWords;
	private Plane[] _planes = [];

	// The buffer roles, as word offsets into _mem: the one a frame decodes into and the one it
	// predicts from. A third buffer exists, for the bidirectional frames this decoder refuses.
	private int _current;
	private int _reference;

	private uint _lastFrameId;
	private readonly BandState[] _bands = [new BandState(), new BandState(), new BandState()];
	private readonly uint[] _coefficients = new uint[8 * Indeo4Transforms.Stride];

	/// <summary>
	/// Creates a decoder for <paramref name="format"/>, or returns null when its dimensions are
	/// outside the limits or not a multiple of 4, which the 4:1:0 chroma planes require.
	/// </summary>
	internal static Indeo4Decoder? Create(AviVideoFormat format, VideoLimits limits) {
		int width = format.Width;
		int height = format.Height;
		if (width < 16 || height < 16 || (width & 3) != 0 || (height & 3) != 0) {
			return null;
		}

		if (width > limits.MaxDimension || height > limits.MaxDimension
			|| (long)width * height > limits.MaxPixelsPerFrame) {
			return null;
		}

		return new Indeo4Decoder(width, height, limits);
	}

	public bool DecodeFrame(ReadOnlySpan<byte> packet, VideoFrame frame) {
		ArgumentNullException.ThrowIfNull(frame);

		// An empty packet is a dropped frame: nothing changed.
		if (packet.Length == 0) {
			return true;
		}

		if (frame.Width != _width || frame.Height != _height || packet.Length > _limits.MaxChunkBytes) {
			return false;
		}

		if (!ReadPictureHeader(packet, out PictureHeader picture)) {
			return false;
		}

		if (picture.Type == FrameRepeat) {
			return true;
		}

		if (_mem == null) {
			if (picture.Type != FrameIntra) {
				return false;
			}

			Allocate();
		}

		int position = picture.HeaderBytes;
		var luma = new List<Entry>();
		for (int plane = 0; plane < 3; plane++) {
			if (!DecodeBand(packet, ref position, plane, picture, luma)) {
				return false;
			}
		}

		// The codec checks where the bands ended against the header's data size, counting the
		// version string an intra frame carries after them. See docs/formats/indeo4.md#frame-layout.
		const int VersionStringBytes = 0x13;
		int end = position + (picture.Type == FrameIntra ? VersionStringBytes : 0);
		if (picture.DataSize != 0 && ((end + 8) & ~3) != picture.DataSize) {
			return false;
		}

		Output(frame, picture.ClampMask);

		// The buffer just decoded becomes the reference, except after a droppable frame, whose
		// buffer the next frame decodes over.
		if (picture.Type != FrameInterDroppable) {
			(_current, _reference) = (_reference, _current);
		}

		return true;
	}

	// --- Picture header -------------------------------------------------------------------------

	private readonly struct PictureHeader {
		internal int Type { get; init; }
		internal int HeaderBytes { get; init; }
		internal Indeo4Codebook MacroblockCodebook { get; init; }
		internal byte[] BlockDescriptor { get; init; }
		internal int RunValueMap { get; init; }
		internal bool QuantiserEveryMacroblock { get; init; }
		internal int ClampMask { get; init; }
		internal int DataSize { get; init; }
	}

	private bool ReadPictureHeader(ReadOnlySpan<byte> packet, out PictureHeader header) {
		header = default;
		var bits = new Indeo4Bits(packet, 0);

		if (bits.Read(18) != SyncCode) {
			return false;
		}

		int type = (int)bits.Read(3);
		bool transparency = bits.ReadBit() == 1;
		if (type == 7 || bits.ReadBit() == 1) {
			return false;
		}

		int dataSize = 0;
		if (bits.ReadBit() == 1) {
			dataSize = (int)bits.Read(24);
		}

		if (type == FrameRepeat) {
			header = new PictureHeader { Type = type };
			return true;
		}

		// Bidirectional frames, the null frame that ends a bidirectional group, transparency and
		// access keys appear nowhere in the corpus.
		if (type is FrameBidirectional or 6 || transparency || bits.ReadBit() == 1) {
			return false;
		}

		int sizeCode = (int)bits.Read(3);
		int height;
		int width;
		if (sizeCode == 7) {
			height = (int)bits.Read(16);
			width = (int)bits.Read(16);
		} else {
			height = Indeo4Tables.PictureHeights[sizeCode];
			width = Indeo4Tables.PictureWidths[sizeCode];
		}

		if (width != _width || height != _height) {
			return false;
		}

		// One tile, 4:1:0 chroma, and a single band in each plane.
		if (bits.ReadBit() == 1 || bits.Read(2) != 0 || bits.Read(2) != 3 || bits.Read(2) != 3) {
			return false;
		}

		uint frameId = 0;
		if (bits.ReadBit() == 1) {
			frameId = bits.Read(20);
			if (type != FrameIntra && frameId == _lastFrameId) {
				type = FrameRepeat;
			}
		}

		_lastFrameId = frameId;

		if (bits.ReadBit() == 1) {
			bits.Skip(8); // the codec skips this field too
		}

		Indeo4Codebook? macroblockCodebook;
		if (bits.ReadBit() == 0) {
			macroblockCodebook = s_macroblockCodebooks[7];
		} else {
			int code = (int)bits.Read(3);
			if (code == 7) {
				macroblockCodebook = Indeo4Codebook.Create(ReadDescriptor(ref bits));
			} else {
				macroblockCodebook = s_macroblockCodebooks[code];
			}
		}

		byte[] blockDescriptor;
		if (bits.ReadBit() == 0) {
			blockDescriptor = PredefinedBlockDescriptor(7);
		} else {
			int code = (int)bits.Read(3);
			blockDescriptor = code == 7 ? ReadDescriptor(ref bits) : PredefinedBlockDescriptor(code);
		}

		int runValueMap = bits.ReadBit() == 1 ? (int)bits.Read(3) : 8;

		bits.ReadBit(); // set throughout the corpus; see docs/formats/indeo4.md#open
		bool quantiserEveryMacroblock = bits.ReadBit() == 1;
		bits.Skip(5); // a picture quantiser; the decode uses each band's own

		// A set flag here would make the packet carry further frames.
		if (bits.ReadBit() == 1 && bits.Read(3) != 0) {
			return false;
		}

		if (bits.ReadBit() == 1) {
			bits.Skip(16); // read only by the access-key check
		}

		int clampMask = 0;
		while (bits.ReadBit() == 1) {
			clampMask = (int)bits.Read(8);
		}

		// One bit per 64x64 area, which the codec skips over.
		if (bits.ReadBit() == 1) {
			bits.Skip(((_height + 63) / 64) * ((_width + 63) / 64));
		}

		if (bits.Overrun || macroblockCodebook == null) {
			return false;
		}

		header = new PictureHeader {
			Type = type,
			HeaderBytes = bits.BytePosition,
			MacroblockCodebook = macroblockCodebook,
			BlockDescriptor = blockDescriptor,
			RunValueMap = runValueMap,
			QuantiserEveryMacroblock = quantiserEveryMacroblock,
			ClampMask = clampMask,
			DataSize = dataSize,
		};
		return true;
	}

	private static readonly Indeo4Codebook?[] s_macroblockCodebooks = BuildPredefined(Indeo4Tables.MacroblockCodebooks);

	private static Indeo4Codebook?[] BuildPredefined(ReadOnlySpan<byte> table) {
		var books = new Indeo4Codebook?[8];
		for (int i = 0; i < 8; i++) {
			ReadOnlySpan<byte> descriptor = table.Slice(i * 17, 17);
			books[i] = Indeo4Codebook.Create(descriptor.Slice(1, descriptor[0]));
		}

		return books;
	}

	private static byte[] PredefinedBlockDescriptor(int index) {
		ReadOnlySpan<byte> descriptor = Indeo4Tables.BlockCodebooks.Slice(index * 17, 17);
		return descriptor.Slice(1, descriptor[0]).ToArray();
	}

	private static byte[] ReadDescriptor(ref Indeo4Bits bits) {
		int rows = (int)bits.Read(4);
		var widths = new byte[rows];
		for (int i = 0; i < rows; i++) {
			widths[i] = (byte)bits.Read(4);
		}

		return widths;
	}

	// --- Buffers --------------------------------------------------------------------------------

	/// <summary>One plane's band within a buffer, in the codec's sample order.</summary>
	private sealed class Plane {
		internal Plane(int width, int height, int macroblock, int offsetWords) {
			Width = width;
			Height = height;
			Macroblock = macroblock;
			Rows = (height + 15) & ~15;
			PitchWords = ((width * 2) + 31) / 32 * 16;
			OffsetWords = offsetWords;

			// Samples come in groups of two block widths: a 32-bit word holds the sample at
			// column j of the group and the one a block width to its right.
			Half = macroblock == 16 ? 8 : macroblock;
		}

		internal int Width { get; }
		internal int Height { get; }
		internal int Macroblock { get; }
		internal int Rows { get; }
		internal int PitchWords { get; }
		internal int OffsetWords { get; }
		internal int Half { get; }

		internal int SizeWords => (Rows * PitchWords) + (BandSlackBytes / 2);

		/// <summary>
		/// The word holding sample <paramref name="x"/> of a row, relative to the row. Columns
		/// outside the row continue into the neighbouring rows, as linear addressing does.
		/// </summary>
		internal int Column(int x) {
			int group = x >> (Half == 8 ? 4 : 3);
			int inGroup = x & ((2 * Half) - 1);
			return (group * 2 * Half) + ((inGroup & (Half - 1)) * 2) + (inGroup >= Half ? 1 : 0);
		}

		internal int Row(int y) => OffsetWords + (y * PitchWords);
	}

	private void Allocate() {
		int chromaWidth = _width >> 2;
		int chromaHeight = _height >> 2;
		var luma = new Plane(_width, _height, 16, 0);
		var cr = new Plane(chromaWidth, chromaHeight, 4, luma.SizeWords);
		var cb = new Plane(chromaWidth, chromaHeight, 4, luma.SizeWords + cr.SizeWords);
		_planes = [luma, cr, cb];
		_bufferWords = luma.SizeWords + cr.SizeWords + cb.SizeWords + (BufferSlackBytes / 2);

		_mem = new ushort[_bufferWords * 3];
		_mem.AsSpan().Fill(0x4000);
		_current = 0;
		_reference = _bufferWords;
	}

	// --- Bands ----------------------------------------------------------------------------------

	/// <summary>A band's settings that persist from frame to frame when a header leaves them out.</summary>
	private sealed class BandState {
		internal bool HasTransform;
		internal int Transform;
		internal int Scan;
		internal int Matrix;
	}

	/// <summary>Everything one band of one frame decodes with.</summary>
	private sealed class BandContext {
		internal required Plane Plane;
		internal required int PlaneIndex;
		internal required int Quantiser;
		internal required bool Small;
		internal required int Matrix;
		internal required byte[] Scan;
		internal required byte[] Scan9;
		internal required int[] Scale;
		internal required Indeo4Codebook BlockCodebook;
		internal required int[] Run;
		internal required int[] Level;
		internal required int EndOfBlock;
		internal required int Escape;
		internal required bool InheritMotion;
		internal required bool InheritQuantiser;
	}

	private bool DecodeBand(ReadOnlySpan<byte> packet, ref int position, int planeIndex, PictureHeader picture, List<Entry> luma) {
		var bits = new Indeo4Bits(packet, position);
		if (bits.Read(2) != planeIndex || bits.Read(4) != 0) {
			return false;
		}

		// An empty band appears nowhere in the corpus.
		if (bits.ReadBit() == 1) {
			return false;
		}

		if (bits.ReadBit() == 1) {
			bits.Skip(16); // the band header's length in bytes
		}

		// Half-pel motion appears nowhere in the corpus.
		if (bits.Read(2) != 0) {
			return false;
		}

		if (bits.ReadBit() == 1) {
			bits.Skip(16); // see docs/formats/indeo4.md#open
		}

		int macroblock = (int)bits.Read(2) switch { 0 => 16, 1 => 8, 2 => 4, _ => 0 };
		Plane plane = _planes[planeIndex];
		if (macroblock != plane.Macroblock) {
			return false;
		}

		bool inheritMotion = bits.ReadBit() == 1;
		bool inheritQuantiser = bits.ReadBit() == 1;
		int quantiser = (int)bits.Read(5);

		// The corpus's luma band inherits nothing, and its chroma bands inherit both from it.
		if (inheritMotion != (planeIndex != 0) || inheritQuantiser != (planeIndex != 0)) {
			return false;
		}

		BandState state = _bands[planeIndex];
		if (bits.ReadBit() == 0) {
			state.Transform = (int)bits.Read(5);
			state.Scan = (int)bits.Read(4);
			state.Matrix = (int)bits.Read(5);
			state.HasTransform = true;
		}

		// Custom scans and matrices appear nowhere in the corpus.
		if (!state.HasTransform || state.Scan == 15 || state.Matrix >= Indeo4Dequant.MatrixCount) {
			return false;
		}

		// Luma is Haar 8x8 and chroma slant 4x4 throughout the corpus.
		bool small = macroblock == 4;
		if (state.Transform != (small ? 11 : 0)) {
			return false;
		}

		byte[] blockDescriptor = picture.BlockDescriptor;
		if (bits.ReadBit() == 1) {
			int code = (int)bits.Read(3);
			blockDescriptor = code == 7 ? ReadDescriptor(ref bits) : PredefinedBlockDescriptor(code);
		}

		int runValueMap = picture.RunValueMap;
		if (bits.ReadBit() == 1) {
			runValueMap = (int)bits.Read(3);
		}

		var swaps = new List<(int A, int B)>();
		if (bits.ReadBit() == 1) {
			int count = (int)bits.Read(8);
			if (count > 61) {
				return false;
			}

			for (int i = 0; i < count; i++) {
				swaps.Add(((int)bits.Read(8), (int)bits.Read(8)));
			}
		}

		if (bits.Overrun) {
			return false;
		}

		Indeo4Codebook? blockCodebook = Indeo4Codebook.Create(blockDescriptor);
		if (blockCodebook == null || !BuildRunValues(runValueMap, swaps, out int[] run, out int[] level, out int endOfBlock, out int escape)) {
			return false;
		}

		// The codec decodes the end-of-block symbol through a ten-bit lookup and nothing else; a
		// longer code would never end a block.
		if (blockCodebook.CodeLength(endOfBlock) is < 0 or > 10) {
			return false;
		}

		byte[] scan = s_scans.AsSpan(state.Scan * 64, 64).ToArray();
		var scan9 = new byte[64];
		for (int i = 0; i < 64; i++) {
			scan9[i] = (byte)(scan[i] + (scan[i] >> 3));
		}

		var context = new BandContext {
			Plane = plane,
			PlaneIndex = planeIndex,
			Quantiser = quantiser,
			Small = small,
			Matrix = state.Matrix,
			Scan = scan,
			Scan9 = scan9,
			Scale = small ? s_unitScale : s_haarScale,
			BlockCodebook = blockCodebook,
			Run = run,
			Level = level,
			EndOfBlock = endOfBlock,
			Escape = escape,
			InheritMotion = inheritMotion && picture.Type > FrameIntraNoResize,
			InheritQuantiser = inheritQuantiser,
		};

		position = bits.BytePosition;
		return DecodeTile(packet, ref position, context, picture, luma);
	}

	private static readonly byte[] s_scans = LoadScans();

	/// <summary>
	/// The scan orders as the codec uses them. A table whose last two entries are equal is a 4x4
	/// order stored with four positions to a row; the codec respaces its first 16 entries to eight
	/// to a row when it loads. 1002a560.
	/// </summary>
	private static byte[] LoadScans() {
		byte[] scans = Indeo4Tables.Scans.ToArray();
		for (int table = 0; table < scans.Length; table += 64) {
			if (scans[table + 62] != scans[table + 63]) {
				continue;
			}

			for (int k = 0; k < 16; k++) {
				int e = scans[table + k];
				scans[table + k] = (byte)((e & 0xC) + e);
			}
		}

		return scans;
	}

	/// <summary>The Haar 8x8 band's coefficient scale: 2 in the high-high quarter, 1 elsewhere. <c>100607e0</c>.</summary>
	private static readonly int[] s_haarScale = BuildHaarScale();

	private static readonly int[] s_unitScale = Enumerable.Repeat(1, 64).ToArray();

	private static int[] BuildHaarScale() {
		var scale = new int[64];
		for (int i = 0; i < 64; i++) {
			scale[i] = (i >> 3) >= 4 && (i & 7) >= 4 ? 2 : 1;
		}

		return scale;
	}

	/// <summary>
	/// Builds a symbol's run and level code from a run/value map and the band's swaps. See
	/// docs/formats/indeo4.md#runvalue-maps.
	/// </summary>
	private static bool BuildRunValues(int index, List<(int A, int B)> swaps, out int[] run, out int[] level, out int endOfBlock, out int escape) {
		run = new int[256];
		level = new int[256];
		ReadOnlySpan<byte> map = Indeo4Tables.RunValueMaps.Slice(index * 332, 332);
		ReadOnlySpan<byte> order = map.Slice(0x48, 256);

		// The symbol each run/value slot is coded as.
		var symbolOf = new int[256];
		for (int symbol = 0; symbol < 256; symbol++) {
			symbolOf[order[symbol]] = symbol;
		}

		endOfBlock = symbolOf[0];
		escape = symbolOf[1];

		int slot = 2;
		for (int r = 1; r <= 64; r++) {
			int count = map[r];
			if (count == 0) {
				break;
			}

			if (slot + (2 * count) > 256) {
				return false;
			}

			for (int v = count * 2; v > 0; v -= 2) {
				int symbol = symbolOf[slot++];
				run[symbol] = r;
				level[symbol] = v;
			}

			for (int v = 1; v <= (count * 2) - 1; v += 2) {
				int symbol = symbolOf[slot++];
				run[symbol] = r;
				level[symbol] = v;
			}
		}

		if (slot != 256) {
			return false;
		}

		foreach ((int a, int b) in swaps) {
			(run[a], run[b]) = (run[b], run[a]);
			(level[a], level[b]) = (level[b], level[a]);
		}

		return true;
	}

	private bool DecodeTile(ReadOnlySpan<byte> packet, ref int position, BandContext band, PictureHeader picture, List<Entry> luma) {
		var bits = new Indeo4Bits(packet, position);
		int tileStart = position;

		if (bits.ReadBit() == 1) {
			// An empty tile: an inter frame keeps the reference's samples, an intra frame keeps
			// whatever the buffer held.
			position = bits.BytePosition;
			if (bits.Overrun) {
				return false;
			}

			if (picture.Type > FrameIntraNoResize) {
				Plane plane = band.Plane;
				int words = plane.Column(plane.Width - 1) + 1;
				for (int y = 0; y < plane.Rows; y++) {
					int row = plane.Row(y);
					_mem.AsSpan(_reference + row, words).CopyTo(_mem.AsSpan(_current + row, words));
				}
			}

			if (band.PlaneIndex == 0) {
				luma.Clear();
			}

			return true;
		}

		// A tile without its size cannot be stepped over, and the codec steps over every tile.
		if (bits.ReadBit() == 0) {
			return false;
		}

		int tileSize = (int)bits.Read(8);
		if (tileSize == 0xFF) {
			tileSize = (int)bits.Read(24);
		}

		if (bits.Overrun || tileSize == 0 || tileStart + tileSize > packet.Length) {
			return false;
		}

		var entries = new List<Entry>();
		int macroblockStart = bits.BytePosition;
		if (!DecodeMacroblocks(packet, macroblockStart, band, picture, luma, entries, out int blockStart)) {
			return false;
		}

		if (!DecodeBlocks(packet, blockStart, band, entries, out int end) || end - tileStart != tileSize) {
			return false;
		}

		if (band.PlaneIndex == 0) {
			luma.Clear();
			luma.AddRange(entries);
		}

		position = tileStart + tileSize;
		return true;
	}

	// --- Macroblocks ----------------------------------------------------------------------------

	/// <summary>One block, as the macroblock pass leaves it for the block pass.</summary>
	private struct Entry {
		/// <summary>The word, relative to a buffer, of the block's top-left 32-bit pair.</summary>
		internal int Pair;
		internal bool High;
		internal bool Intra;
		internal bool Coded;

		/// <summary>Index of the step table: 0-31 inter quantisers, 32-63 intra.</summary>
		internal int StepSet;

		internal uint Motion;

		/// <summary>The quantiser delta plus 32, which a chroma band inheriting the quantiser reads.</summary>
		internal int QuantiserField;
	}

	/// <summary>
	/// Reads a macroblock code and returns its value as the codec's table at <c>1005b000</c> holds
	/// it: 0, 1, -1, 2, -2, ..., as a byte biased by 0x80. A symbol past 255 has no entry and sets
	/// <paramref name="invalid"/>.
	/// </summary>
	private static int ReadMacroblockCode(Indeo4Codebook codebook, ref Indeo4Bits bits, ref bool invalid) {
		int symbol = codebook.Read(ref bits);
		if (symbol > 255) {
			invalid = true;
			return 0x80;
		}

		int magnitude = (symbol + 1) >> 1;
		return (byte)(0x80 + ((symbol & 1) != 0 ? magnitude : -magnitude));
	}

	private bool DecodeMacroblocks(ReadOnlySpan<byte> packet, int start, BandContext band, PictureHeader picture, List<Entry> luma, List<Entry> entries, out int end) {
		end = 0;
		var bits = new Indeo4Bits(packet, start);
		Plane plane = band.Plane;
		int mb = plane.Macroblock;
		int columns = (plane.Width + mb - 1) / mb;
		int rows = (plane.Height + mb - 1) / mb;
		bool intraFrame = picture.Type <= FrameIntraNoResize;
		int typeBits = intraFrame || band.InheritMotion ? 0 : 1;
		Indeo4Codebook codebook = picture.MacroblockCodebook;
		bool quantiserForced = band.PlaneIndex == 0 && picture.QuantiserEveryMacroblock;

		if (band.Small && (columns & 1) != 0) {
			return false;
		}

		uint predictor = NoMotion;
		bool invalid = false;
		bool previousMoved = false;
		uint previousMotion = 0;
		int index = 0;

		for (int my = 0; my < rows; my++) {
			for (int mx = 0; mx < columns; mx++, index++) {
				bool high = band.Small && (mx & 1) == 1;
				int pairX = band.Small ? mx & ~1 : mx;
				int pair = plane.Row(my * mb) + plane.Column(pairX * mb);
				Entry reference = default;
				if (band.InheritQuantiser || band.InheritMotion) {
					if (index >= luma.Count / 4) {
						return false;
					}

					reference = luma[index * 4];
				}

				bool intra;
				int cbp = 0;
				int field;
				int stepSet;
				uint motion;

				if (bits.ReadBit() == 1) {
					// A skipped macroblock: no coefficients, and in an inter frame a copy of the
					// reference at the same place. See docs/formats/indeo4.md#skipped-macroblocks.
					intra = intraFrame;
					field = 0;
					if (quantiserForced) {
						field = (ReadMacroblockCode(codebook, ref bits, ref invalid) - 0x80 + 32) & 63;
					}

					stepSet = band.Quantiser;
					motion = NoMotion;

					if (intra) {
						previousMoved = false;
						previousMotion = 0;
					} else {
						if (!high) {
							if (!CopyBlock(plane, my * mb, mx * mb, 0, 0, mb == 16 ? 16 : 8, mb)) {
								return false;
							}

							previousMoved = true;
						} else if (!(previousMoved && previousMotion == NoMotion)) {
							if (!CopyBlock(plane, my * mb, mx * mb, 0, 0, 4, 4)) {
								return false;
							}
						}

						previousMotion = NoMotion;
					}
				} else {
					int type = (int)bits.Read(typeBits);
					cbp = (int)bits.Read(mb == 16 ? 4 : 1);

					if (band.InheritMotion) {
						intra = reference.Intra;
						motion = ScaleMotion(reference.Motion, (mb >> 3) - 2);
					} else {
						intra = type == 0;
						motion = 0;
					}

					int delta;
					if (band.InheritQuantiser) {
						delta = Math.Clamp(band.Quantiser + reference.QuantiserField - 32, 0, 31) - band.Quantiser;
						field = 0;
					} else if (cbp != 0 || quantiserForced) {
						delta = ReadMacroblockCode(codebook, ref bits, ref invalid) - 0x80;
						field = (delta + 32) & 63;
					} else {
						delta = 0;
						field = 32;
					}

					// A quantiser outside 0-31 indexes past the codec's 32 step tables per set; an
					// inter one reads the intra tables, and anything further reads unrelated memory.
					stepSet = band.Quantiser + delta + (intra ? 32 : 0);
					if (stepSet is < 0 or > 63) {
						return false;
					}

					if (intra) {
						motion = 0;
						previousMoved = false;
						previousMotion = 0;
					} else {
						if (!band.InheritMotion) {
							int dy = ReadMacroblockCode(codebook, ref bits, ref invalid);
							int dx = ReadMacroblockCode(codebook, ref bits, ref invalid);
							predictor = predictor + (uint)((dy << 8) | dx) - NoMotion;
							motion = predictor;
						}

						int mvx = (int)(motion & 0xFF) - 0x80;
						int mvy = (int)((motion >> 8) & 0xFF) - 0x80;
						int width = mb == 16 ? 16 : high ? 4 : 8;
						if (!CopyBlock(plane, my * mb, mx * mb, mvx, mvy, width, mb)) {
							return false;
						}

						previousMoved = true;
						previousMotion = motion;
					}
				}

				var entry = new Entry { Pair = pair, High = high, Intra = intra, Coded = (cbp & 1) != 0, StepSet = stepSet, Motion = motion, QuantiserField = field };
				if (mb == 16) {
					// Four 8x8 blocks, left then right, top then bottom; the right-hand block of
					// each row shares the left-hand one's words, in the high lane.
					int lower = plane.PitchWords * 8;
					entries.Add(entry);
					entries.Add(entry with { High = true, Coded = (cbp & 2) != 0 });
					entries.Add(entry with { Pair = pair + lower, Coded = (cbp & 4) != 0 });
					entries.Add(entry with { Pair = pair + lower, High = true, Coded = (cbp & 8) != 0 });
				} else {
					entries.Add(entry);
				}
			}
		}

		if (bits.Overrun || invalid) {
			return false;
		}

		end = bits.BytePosition;
		return true;
	}

	/// <summary>
	/// Rescales an inherited motion vector to a band whose macroblocks are smaller by 2^-<paramref name="shift"/>,
	/// byte by byte as the codec does it, rounding half away from zero. See
	/// docs/formats/indeo4.md#inherited-type-motion-and-quantiser.
	/// </summary>
	private static uint ScaleMotion(uint motion, int shift) {
		if (shift == 0) {
			return motion;
		}

		uint result = 0;
		for (int i = 0; i < 4; i++) {
			int b = (int)((motion >> (8 * i)) & 0xFF);
			int carry = b >= 0x80 ? 1 : 0;
			int scaled = shift == -1
				? ((((b + 0x80 + carry) & 0xFF) ^ 0x80) >> 1) + 0x40
				: ((((b + 0x81 + carry) & 0xFF) ^ 0x80) >> 2) + 0x60;
			result |= (uint)(scaled & 0xFF) << (8 * i);
		}

		return result;
	}

	/// <summary>
	/// Copies <paramref name="width"/> by <paramref name="height"/> samples from the reference,
	/// displaced by the motion vector, into the current buffer. Returns false when the source
	/// leaves the codec's allocation.
	/// </summary>
	private bool CopyBlock(Plane plane, int y, int x, int mvx, int mvy, int width, int height) {
		ushort[] mem = _mem!;
		for (int r = 0; r < height; r++) {
			int sourceRow = _reference + plane.Row(y + r + mvy);
			int destRow = _current + plane.Row(y + r);
			for (int c = 0; c < width; c++) {
				int source = sourceRow + plane.Column(x + c + mvx);
				if ((uint)source >= (uint)mem.Length) {
					return false;
				}

				mem[destRow + plane.Column(x + c)] = mem[source];
			}
		}

		return true;
	}

	// --- Blocks ---------------------------------------------------------------------------------

	/// <summary>
	/// The block pass for one tile: runs of uncoded blocks are filled, and coded blocks are decoded a
	/// pair at a time, predicted, transformed and stored. See docs/formats/indeo4.md#blocks.
	/// </summary>
	private bool DecodeBlocks(ReadOnlySpan<byte> packet, int start, BandContext band, List<Entry> entries, out int end) {
		end = 0;
		var bits = new Indeo4Bits(packet, start);
		Span<uint> buffer = _coefficients;
		uint fill = band.Small ? Indeo4Transforms.SlantFill : Indeo4Transforms.HaarFill;
		int predictor = 0;
		int n = entries.Count;
		int i = 0;

		while (i < n) {
			int runEnd = i;
			if (!entries[i].Coded) {
				bool anyIntra = false;
				while (runEnd < n && !entries[runEnd].Coded) {
					anyIntra |= entries[runEnd].Intra;
					runEnd++;
				}

				if (anyIntra) {
					FillUncoded(band, entries, i, runEnd, predictor);
				}

				i = runEnd;
				continue;
			}

			while (runEnd < n && entries[runEnd].Coded) {
				runEnd++;
			}

			int count = runEnd - i;
			int p = i;
			while (true) {
				int span = band.Small ? 4 : 8;
				for (int r = 0; r < span; r++) {
					buffer.Slice(r * Indeo4Transforms.Stride, span).Fill(fill);
				}

				if (entries[p].High) {
					// A run starting on a right-hand block decodes into the pair it shares with
					// the block to its left, which an earlier run has already handled. Entry 0 is
					// always a left-hand block, so p is never 0 here; the check states that rather
					// than leaving it to the layout DecodeMacroblocks happens to produce.
					if (p == 0) {
						return false;
					}

					p--;
					if (!DecodeCoefficients(ref bits, band, entries[p + 1], buffer, 16)) {
						return false;
					}

					count--;
				} else {
					if (!DecodeCoefficients(ref bits, band, entries[p], buffer, 0)) {
						return false;
					}

					count--;
					if (count > 0) {
						if (!DecodeCoefficients(ref bits, band, entries[p + 1], buffer, 16)) {
							return false;
						}

						count--;
					}
				}

				Entry left = entries[p];
				Entry right = p + 1 < n ? entries[p + 1] : default;
				predictor = PredictDc(buffer, left, right, fill, predictor);

				if (band.Small) {
					Indeo4Transforms.InverseSlant4x4(buffer);
				} else {
					Indeo4Transforms.InverseHaar8x8(buffer);
				}

				Store(band, left, right, buffer);
				p += 2;
				if (count == 0) {
					break;
				}
			}

			i = runEnd;
		}

		if (bits.Overrun) {
			return false;
		}

		end = bits.BytePosition;
		return true;
	}

	private static bool DecodeCoefficients(ref Indeo4Bits bits, BandContext band, Entry entry, Span<uint> buffer, int shift) {
		int set = entry.StepSet >> 5;
		int level = entry.StepSet & 31;
		int position = 0xFF;
		Indeo4Codebook codebook = band.BlockCodebook;

		while (true) {
			int symbol = codebook.Read(ref bits);
			if (bits.Overrun || symbol > 255) {
				return false;
			}

			int code;
			if (symbol == band.Escape) {
				int run = codebook.Read(ref bits);
				int low = codebook.Read(ref bits);
				int high = codebook.Read(ref bits);
				if ((run | low | high) > 255) {
					return false;
				}

				position = (position + 1 + run) & 0xFF;
				code = (high << 6) | low;
			} else if (symbol == band.EndOfBlock) {
				return true;
			} else {
				position = (position + band.Run[symbol]) & 0xFF;
				code = band.Level[symbol];
			}

			if (position >= 64 || code == 0) {
				return false;
			}

			int natural = band.Scan[position];
			int step = Indeo4Dequant.Step(band.Matrix, set, level, natural);
			if (code - 1 >= Indeo4Dequant.CodeCount(step)) {
				return false;
			}

			int value = Indeo4Dequant.Value(code, step) * band.Scale[natural];
			buffer[band.Scan9[position]] += (uint)value << shift;
		}
	}

	/// <summary>
	/// Adds the running DC prediction to the DC of each intra block of a pair, in left then right
	/// order, and returns the updated prediction. See docs/formats/indeo4.md#dc-prediction.
	/// </summary>
	private static int PredictDc(Span<uint> buffer, Entry left, Entry right, uint fill, int predictor) {
		int fillLow = (int)(fill & 0xFFFF);
		int fillHigh = (int)(fill >> 16);
		int maskLeft = left.Intra ? -1 : 0;
		int maskRight = right.Intra ? -1 : 0;

		uint dc = buffer[0];
		int deltaLow = (int)(dc & 0xFFFF) - fillLow;
		int deltaHigh = (int)(dc >> 16) - fillHigh;

		int low = (predictor & maskLeft) + deltaLow;
		predictor += deltaLow & maskLeft;
		int high = (predictor & maskRight) + deltaHigh;
		predictor += deltaHigh & maskRight;

		buffer[0] = ((uint)(high + fillHigh) << 16) | (uint)(low + fillLow);
		return predictor;
	}

	private enum StoreOp {
		CopyBoth,
		CopyLow,
		CopyHigh,
		AddBoth,
		AddLow,
		AddHigh,
	}

	/// <summary>
	/// Writes a transformed pair into the current buffer, choosing per lane between replacing the
	/// samples (intra) and adding to the prediction already there (inter). The choice follows the
	/// codec's routine at <c>100212a0</c> case by case, including the cases where a lane is written
	/// that the obvious reading would leave alone; see docs/formats/indeo4.md#stores.
	/// </summary>
	private void Store(BandContext band, Entry a, Entry b, Span<uint> buffer) {
		bool pair = b.High;
		if (!pair) {
			Apply(band, a, buffer, a.Intra ? StoreOp.CopyLow : StoreOp.AddLow);
			return;
		}

		if (a.Intra && b.Intra) {
			if (a.Coded) {
				Apply(band, a, buffer, StoreOp.CopyBoth);
			} else if (b.Coded) {
				Apply(band, a, buffer, StoreOp.CopyHigh);
			}
		} else if (a.Coded && b.Coded) {
			Apply(band, a, buffer, StoreOp.AddBoth);
			if (b.Intra) {
				Apply(band, a, buffer, StoreOp.CopyHigh);
			} else if (a.Intra) {
				Apply(band, a, buffer, StoreOp.CopyLow);
			}
		} else if (a.Coded) {
			if (b.Intra) {
				Apply(band, a, buffer, StoreOp.AddBoth);
			} else {
				Apply(band, a, buffer, a.Intra ? StoreOp.CopyLow : StoreOp.AddLow);
			}
		} else if (b.Coded) {
			Apply(band, a, buffer, b.Intra ? StoreOp.CopyHigh : StoreOp.AddHigh);
		}
	}

	private void Apply(BandContext band, Entry a, Span<uint> buffer, StoreOp op) {
		ushort[] mem = _mem!;
		int span = band.Small ? 4 : 8;
		int pitch = band.Plane.PitchWords;
		int origin = _current + a.Pair;
		for (int r = 0; r < span; r++) {
			int row = origin + (r * pitch);
			for (int c = 0; c < span; c++) {
				int at = row + (2 * c);
				uint t = buffer[(r * Indeo4Transforms.Stride) + c];
				uint d = mem[at] | ((uint)mem[at + 1] << 16);
				switch (op) {
					case StoreOp.CopyBoth:
						mem[at] = (ushort)t;
						mem[at + 1] = (ushort)(t >> 16);
						break;
					case StoreOp.CopyLow:
						mem[at] = (ushort)t;
						break;
					case StoreOp.CopyHigh:
						mem[at + 1] = (ushort)(t >> 16);
						break;
					case StoreOp.AddBoth: {
						uint sum = t + d + 0xBFFF_C000;
						mem[at] = (ushort)sum;
						mem[at + 1] = (ushort)(sum >> 16);
						break;
					}
					case StoreOp.AddLow:
						mem[at] = (ushort)(t + d + 0xBFFF_C000);
						break;
					case StoreOp.AddHigh:
						mem[at + 1] = (ushort)((t + d + 0xC000_0000) >> 16);
						break;
				}
			}
		}
	}

	/// <summary>
	/// Fills each intra block of a run of uncoded blocks with the running DC prediction, scaled to
	/// samples as the codec's routines at <c>10021930</c> (8x8) and <c>10022640</c> (4x4) do.
	/// </summary>
	private void FillUncoded(BandContext band, List<Entry> entries, int start, int end, int predictor) {
		uint v = band.Small ? (uint)(((predictor * 2) + 2) & ~3) : (uint)((predictor & ~7) >> 1);
		uint both = BiasedZeroPair + v + (v << 16);
		ushort word = (ushort)both;

		for (int k = start; k < end; k++) {
			Entry e = entries[k];
			if (e.High) {
				if (e.Intra) {
					FillLanes(band, e, both, word, low: false, high: true);
				}

				continue;
			}

			bool nextIsRightIntra = k + 1 < end && entries[k + 1].High && entries[k + 1].Intra;
			if (nextIsRightIntra) {
				k++;
				if (e.Intra) {
					FillLanes(band, e, both, word, low: true, high: true);
				} else {
					FillLanes(band, e, both, word, low: false, high: true);
				}
			} else if (e.Intra) {
				FillLanes(band, e, both, word, low: true, high: false);
			}
		}
	}

	private void FillLanes(BandContext band, Entry e, uint both, ushort word, bool low, bool high) {
		ushort[] mem = _mem!;
		int span = band.Small ? 4 : 8;
		int pitch = band.Plane.PitchWords;
		int origin = _current + e.Pair;
		for (int r = 0; r < span; r++) {
			int row = origin + (r * pitch);
			for (int c = 0; c < span; c++) {
				int at = row + (2 * c);
				if (low && high) {
					mem[at] = (ushort)both;
					mem[at + 1] = (ushort)(both >> 16);
				} else if (low) {
					mem[at] = word;
				} else {
					mem[at + 1] = word;
				}
			}
		}
	}

	// --- Output ---------------------------------------------------------------------------------

	private readonly byte[][] _output = [[], [], []];

	/// <summary>
	/// Converts the buffer just decoded to raster-order 8-bit planes and then to RGBA. A plane whose
	/// bit is set in <paramref name="clampMask"/> (4 for luma, 2 and 1 for the chroma planes) is
	/// truncated to a byte rather than clamped, as the codec does.
	/// </summary>
	private void Output(VideoFrame frame, int clampMask) {
		ushort[] mem = _mem!;
		for (int p = 0; p < 3; p++) {
			Plane plane = _planes[p];
			if (_output[p].Length != plane.Width * plane.Height) {
				_output[p] = new byte[plane.Width * plane.Height];
			}

			bool truncate = (clampMask & (4 >> p)) != 0;
			byte[] target = _output[p];
			for (int y = 0; y < plane.Height; y++) {
				int row = _current + plane.Row(y);
				for (int x = 0; x < plane.Width; x++) {
					int word = plane.Column(x);
					int pairBase = row + (word & ~1);
					uint pairValue = mem[pairBase] | ((uint)mem[pairBase + 1] << 16);
					uint shifted = (pairValue >> 2) + 0x0080_0080;
					uint index = (word & 1) == 0 ? shifted & 0x3FF : (shifted >> 16) & 0x3FF;
					target[(y * plane.Width) + x] = truncate ? (byte)index : Saturate(index);
				}
			}
		}

		byte[] lum = _output[0];
		byte[] cr = _output[1];
		byte[] cb = _output[2];
		int chromaWidth = _planes[1].Width;
		for (int y = 0; y < _height; y++) {
			for (int x = 0; x < _width; x++) {
				int l = lum[(y * _width) + x];
				int chroma = ((y >> 2) * chromaWidth) + (x >> 2);
				int d = cb[chroma] - 128;
				int e = cr[chroma] - 128;

				// ITU-R BT.601, studio range, as the Indeo 3 decoder converts.
				int c = 298 * (l - 16);
				frame.SetPixel(
					x,
					y,
					Clamp((c + (409 * e) + 128) >> 8),
					Clamp((c - (100 * d) - (208 * e) + 128) >> 8),
					Clamp((c + (516 * d) + 128) >> 8));
			}
		}

		frame.Touch();
	}

	/// <summary>The codec's clamp table at <c>100af450</c>: 0-255 as is, 256-511 to 255, 512-1023 (negative) to 0.</summary>
	private static byte Saturate(uint index) => index < 256 ? (byte)index : index < 512 ? (byte)255 : (byte)0;

	private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

	/// <summary>The planes of the frame last decoded, raster order: luma, then the two chroma planes as the codec orders them.</summary>
	internal byte[][] Planes => _output;
}
