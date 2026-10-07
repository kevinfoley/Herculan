using System.Buffers.Binary;
using HercWorks.Video.Avi;

namespace HercWorks.Video.Codecs.Indeo3;

/// <summary>
/// Intel Indeo Video 3.2 (<c>IV32</c>): three 7-bit YUV 4:1:0 planes, each cut into cells by a
/// binary tree, each cell coded as vector-quantised deltas from the row above or from a
/// motion-compensated block of the other frame buffer.
///
/// <para>The decoder keeps two buffers per plane, as the codec does. A frame's header names the one
/// it is decoded into, and inter cells read from the other; a frame is not necessarily decoded on
/// top of the one before it. Each buffer carries one extra row above the picture, filled with
/// <c>0x40</c>, which is what intra cells on the top edge predict from.</para>
///
/// <para>See <c>docs/retail/formats/indeo3.md</c> for the frame layout, the cell tree, the cell modes and
/// the codebooks.</para>
/// </summary>
internal sealed class Indeo3Decoder : IVideoCodec {
	private const int FrameHeaderBytes = 16;
	private const int BitstreamHeaderBytes = 48;
	private const int AltQuantOffset = 32;
	private const ushort BitstreamVersion = 0x20;

	/// <summary><c>FRMH</c>, big-endian: what the frame header's checksum is XORed against.</summary>
	private const uint FrameHeaderTag = 0x4652_4D48;

	/// <summary>A bitstream this many bytes long is a sync frame: a header and no picture.</summary>
	private const int SyncFrameBytes = 16;

	private const int FlagEightBitPixels = 1 << 1;
	private const int FlagHalfPelX = 1 << 5;
	private const int FlagHalfPelY = 1 << 4;
	private const int BufferSelectShift = 9;

	/// <summary>Strip width in cells: 160 pixels of luma, 40 of chroma.</summary>
	private const int LumaStripCells = 40;
	private const int ChromaStripCells = 10;

	private const int MaxMotionVectors = 256;

	// The two-bit tree codes.
	private const int HorizontalSplit = 0;
	private const int VerticalSplit = 1;
	private const int IntraOrNull = 2;
	private const int InterOrData = 3;

	private Indeo3Decoder(int width, int height, VideoLimits limits) {
		_width = width;
		_height = height;
		_limits = limits;
		_codebooks = Indeo3Codebooks.Shared;
		_planes = [
			new Plane(width, height),
			new Plane(AlignUp(width >> 2, 4), AlignUp(height >> 2, 4)),
			new Plane(AlignUp(width >> 2, 4), AlignUp(height >> 2, 4)),
		];
	}

	private readonly int _width;
	private readonly int _height;
	private readonly VideoLimits _limits;
	private readonly Indeo3Codebooks _codebooks;
	private readonly Plane[] _planes;

	private byte[] _packet = [];
	private int _bufferSelect;
	private int _codebookOffset;
	private int _altQuant;

	// Per-plane parse state. The tree's two-bit codes and the cells' byte-wide data share one
	// stream: the codes are read MSB-first from a bit cursor, and each cell's data starts at the
	// next byte boundary. The bytes a cell consumes are skipped by the bit cursor lazily, the first
	// time it is byte-aligned again.
	private int _bitBase;
	private long _bitCount;
	private long _bitPos;
	private long _pendingSkipBits;
	private bool _needResync;
	private int _nextCellData;
	private int _planeEnd;
	private int _vectorBase;
	private int _vectorCount;

	/// <summary>
	/// Creates a decoder for <paramref name="format"/>, or returns null when its dimensions are not
	/// ones the codec can code: at least 16 and a multiple of 4 on each axis, since cells are
	/// measured in 4x4 blocks.
	/// </summary>
	internal static Indeo3Decoder? Create(AviVideoFormat format, VideoLimits limits) {
		int width = format.Width;
		int height = format.Height;

		if (width < 16 || height < 16 || (width & 3) != 0 || (height & 3) != 0) {
			return null;
		}

		if (width > limits.MaxDimension || height > limits.MaxDimension
			|| (long)width * height > limits.MaxPixelsPerFrame) {
			return null;
		}

		return new Indeo3Decoder(width, height, limits);
	}

	public bool DecodeFrame(ReadOnlySpan<byte> packet, VideoFrame frame) {
		// An empty packet is a dropped frame: nothing changed.
		if (packet.Length == 0) {
			return true;
		}

		if (frame.Width != _width || frame.Height != _height || packet.Length > _limits.MaxChunkBytes) {
			return false;
		}

		if (packet.Length < FrameHeaderBytes + SyncFrameBytes) {
			return false;
		}

		if (_packet.Length < packet.Length) {
			_packet = new byte[packet.Length];
		}

		packet.CopyTo(_packet);

		FrameResult result = ReadHeaders(packet.Length, out PlaneSpan y, out PlaneSpan u, out PlaneSpan v);
		if (result == FrameResult.Sync) {
			return true;
		}

		if (result == FrameResult.Bad) {
			return false;
		}

		if (!DecodePlane(_planes[0], y, LumaStripCells)
			|| !DecodePlane(_planes[1], u, ChromaStripCells)
			|| !DecodePlane(_planes[2], v, ChromaStripCells)) {
			return false;
		}

		Output(frame);
		return true;
	}

	private enum FrameResult { Picture, Sync, Bad }

	private readonly record struct PlaneSpan(int Start, int Length);

	/// <summary>
	/// Reads the 16-byte frame header and the 48-byte bitstream header that follows it, and finds
	/// the three planes' data. Every offset and size in them is stream-supplied, so all of it is
	/// range-checked against the packet here, before any plane is read.
	/// </summary>
	private FrameResult ReadHeaders(int length, out PlaneSpan y, out PlaneSpan u, out PlaneSpan v) {
		y = u = v = default;
		ReadOnlySpan<byte> p = _packet.AsSpan(0, length);

		uint frameNumber = BinaryPrimitives.ReadUInt32LittleEndian(p);
		uint second = BinaryPrimitives.ReadUInt32LittleEndian(p[4..]);
		uint checksum = BinaryPrimitives.ReadUInt32LittleEndian(p[8..]);
		uint frameSize = BinaryPrimitives.ReadUInt32LittleEndian(p[12..]);
		if ((frameNumber ^ second ^ frameSize ^ FrameHeaderTag) != checksum) {
			return FrameResult.Bad;
		}

		ReadOnlySpan<byte> bs = p[FrameHeaderBytes..];
		if (BinaryPrimitives.ReadUInt16LittleEndian(bs) != BitstreamVersion) {
			return FrameResult.Bad;
		}

		int flags = BinaryPrimitives.ReadUInt16LittleEndian(bs[2..]);
		long dataBytes = ((long)BinaryPrimitives.ReadUInt32LittleEndian(bs[4..]) + 7) >> 3;
		if (dataBytes == SyncFrameBytes) {
			return FrameResult.Sync;
		}

		if (bs.Length < BitstreamHeaderBytes) {
			return FrameResult.Bad;
		}

		dataBytes = Math.Min(dataBytes, bs.Length);
		_codebookOffset = bs[8];

		int height = BinaryPrimitives.ReadUInt16LittleEndian(bs[12..]);
		int width = BinaryPrimitives.ReadUInt16LittleEndian(bs[14..]);
		if (width != _width || height != _height) {
			return FrameResult.Bad;
		}

		// The header lists Y, V, U; the file lays them out U, V, Y. Each plane runs to the next
		// plane's start or to the end of the data, whichever is nearer.
		Span<long> starts = [
			BinaryPrimitives.ReadUInt32LittleEndian(bs[16..]),
			BinaryPrimitives.ReadUInt32LittleEndian(bs[20..]),
			BinaryPrimitives.ReadUInt32LittleEndian(bs[24..]),
		];
		Span<long> ends = stackalloc long[3];
		for (int j = 0; j < 3; j++) {
			ends[j] = dataBytes;
			for (int i = 0; i < 3; i++) {
				if (starts[i] > starts[j] && starts[i] < ends[j]) {
					ends[j] = starts[i];
				}
			}

			if (starts[j] < BitstreamHeaderBytes || starts[j] >= dataBytes || ends[j] - starts[j] <= 0) {
				return FrameResult.Bad;
			}
		}

		if ((flags & FlagEightBitPixels) != 0 || (flags & (FlagHalfPelX | FlagHalfPelY)) != 0) {
			// Neither appears in the retail corpus, and neither is implemented.
			return FrameResult.Bad;
		}

		_bufferSelect = (flags >> BufferSelectShift) & 1;
		_altQuant = FrameHeaderBytes + AltQuantOffset;

		y = new PlaneSpan(FrameHeaderBytes + (int)starts[0], (int)(ends[0] - starts[0]));
		v = new PlaneSpan(FrameHeaderBytes + (int)starts[1], (int)(ends[1] - starts[1]));
		u = new PlaneSpan(FrameHeaderBytes + (int)starts[2], (int)(ends[2] - starts[2]));
		return FrameResult.Picture;
	}

	/// <summary>
	/// Decodes one plane: a motion-vector count, that many (y, x) signed byte pairs, then the cell
	/// tree with the cells' data interleaved in it.
	/// </summary>
	private bool DecodePlane(Plane plane, PlaneSpan span, int stripCells) {
		if (span.Length < 4) {
			return false;
		}

		uint vectors = BinaryPrimitives.ReadUInt32LittleEndian(_packet.AsSpan(span.Start));
		if (vectors > MaxMotionVectors || (vectors * 2) > span.Length - 4) {
			return false;
		}

		_vectorBase = span.Start + 4;
		_vectorCount = (int)vectors;
		_bitBase = _vectorBase + (_vectorCount * 2);
		_planeEnd = span.Start + span.Length;
		_bitCount = (long)(_planeEnd - _bitBase) * 8;
		_bitPos = 0;
		_pendingSkipBits = 0;
		_needResync = false;
		_nextCellData = _bitBase;

		// The whole plane is one intra cell of the motion-compensation tree to begin with.
		var root = new Cell(0, 0, plane.Width >> 2, plane.Height >> 2, false, -1);
		return ParseTree(plane, IntraOrNull, ref root, _limits.MaxCellDepth, stripCells);
	}

	/// <summary>
	/// A region of a plane in 4x4 blocks. <see cref="VqTree"/> is false while the tree is still
	/// deciding motion compensation, true once it has descended into the VQ tree below that.
	/// <see cref="Vector"/> is the motion-vector index, or -1 for an intra cell.
	/// </summary>
	private struct Cell(int x, int y, int width, int height, bool vqTree, int vector) {
		public int X = x;
		public int Y = y;
		public int Width = width;
		public int Height = height;
		public bool VqTree = vqTree;
		public int Vector = vector;
	}

	/// <summary>
	/// One node of the cell tree. A split code carves the first half off <paramref name="parent"/>
	/// and recurses into it, leaving the second half in <paramref name="parent"/> for the caller's
	/// next code. Recursion depth comes from the stream, so it is bounded by
	/// <see cref="VideoLimits.MaxCellDepth"/>.
	/// </summary>
	private bool ParseTree(Plane plane, int code, ref Cell parent, int depth, int stripCells) {
		if (depth <= 0) {
			return false;
		}

		Cell cell = parent;
		if (code == HorizontalSplit) {
			cell.Height = SplitSize(parent.Height);
			parent.Y += cell.Height;
			parent.Height -= cell.Height;
			if (parent.Height <= 0 || cell.Height <= 0) {
				return false;
			}
		} else if (code == VerticalSplit) {
			// Wider than a strip: the first split cuts at a strip boundary rather than in half.
			cell.Width = cell.Width > stripCells
				? (cell.Width <= stripCells * 2 ? 1 : 2) * stripCells
				: SplitSize(parent.Width);
			parent.X += cell.Width;
			parent.Width -= cell.Width;
			if (parent.Width <= 0 || cell.Width <= 0) {
				return false;
			}
		}

		while (true) {
			Resync();
			if (_bitCount - _bitPos < 2) {
				return false;
			}

			code = ReadCode();
			switch (code) {
				case HorizontalSplit:
				case VerticalSplit:
					if (!ParseTree(plane, code, ref cell, depth - 1, stripCells)) {
						return false;
					}

					break;

				case IntraOrNull:
					if (!cell.VqTree) {
						// Motion tree: this region is intra. Descend into its VQ tree.
						cell.Vector = -1;
						cell.VqTree = true;
						break;
					}

					// VQ tree: a null cell, copied unchanged from the reference. One more code
					// follows, 0 for copy and 1 for skip; both are treated as a copy.
					Resync();
					if (_bitCount - _bitPos < 2 || ReadCode() >= 2) {
						return false;
					}

					if (!CellFits(plane, cell) || cell.Vector < 0) {
						return false;
					}

					return CopyCell(plane, cell);

				case InterOrData:
					if (!cell.VqTree) {
						// Motion tree: this region is inter. A motion-vector index byte follows.
						if (!_needResync) {
							_nextCellData = _bitBase + (int)((_bitPos + 7) >> 3);
						}

						if (_nextCellData >= _planeEnd) {
							return false;
						}

						int vector = _packet[_nextCellData++];
						if (vector >= _vectorCount) {
							return false;
						}

						cell.Vector = vector;
						cell.VqTree = true;
						_pendingSkipBits += 8;
						_needResync = true;
						break;
					}

					// VQ tree: a coded cell. Its data starts at the next byte boundary.
					if (!_needResync) {
						_nextCellData = _bitBase + (int)((_bitPos + 7) >> 3);
					}

					if (!CellFits(plane, cell)) {
						return false;
					}

					int used = DecodeCell(plane, cell, _nextCellData);
					if (used < 0) {
						return false;
					}

					_pendingSkipBits += (long)used * 8;
					_needResync = true;
					_nextCellData += used;
					return true;
			}
		}
	}

	private static int SplitSize(int size) => size > 2 ? ((size + 2) >> 2) << 1 : 1;

	private static bool CellFits(Plane plane, Cell cell) =>
		cell.X + cell.Width <= plane.Width >> 2 && cell.Y + cell.Height <= plane.Height >> 2;

	/// <summary>Skips the bytes cell data consumed, once the bit cursor is on a byte boundary.</summary>
	private void Resync() {
		if (_needResync && (_bitPos & 7) == 0) {
			_bitPos += _pendingSkipBits;
			_pendingSkipBits = 0;
			_needResync = false;
		}
	}

	/// <summary>
	/// Reads one two-bit code, MSB first. The cursor only ever advances by two bits or by whole
	/// bytes, so a code never straddles a byte.
	/// </summary>
	private int ReadCode() {
		int b = _packet[_bitBase + (int)(_bitPos >> 3)];
		int code = (b >> (6 - (int)(_bitPos & 7))) & 3;
		_bitPos += 2;
		return code;
	}

	/// <summary>A cell's motion vector, (0, 0) for an intra cell. Stored y first.</summary>
	private void MotionVector(Cell cell, out int dy, out int dx) {
		dy = dx = 0;
		if (cell.Vector < 0) {
			return;
		}

		dy = (sbyte)_packet[_vectorBase + (cell.Vector * 2)];
		dx = (sbyte)_packet[_vectorBase + (cell.Vector * 2) + 1];
	}

	/// <summary>
	/// Whether a cell displaced by (<paramref name="dy"/>, <paramref name="dx"/>) stays inside the
	/// plane. One row above the top is allowed, because the buffer carries the prediction row there.
	/// </summary>
	private static bool VectorInBounds(Plane plane, Cell cell, int dy, int dx) =>
		(cell.Y << 2) + dy >= -1
		&& (cell.X << 2) + dx >= 0
		&& ((cell.Y + cell.Height) << 2) + dy <= plane.Height
		&& ((cell.X + cell.Width) << 2) + dx <= plane.Width;

	/// <summary>Copies a cell from the reference buffer, displaced by its motion vector.</summary>
	private bool CopyCell(Plane plane, Cell cell) {
		MotionVector(cell, out int dy, out int dx);
		if (!VectorInBounds(plane, cell, dy, dx)) {
			return false;
		}

		byte[] dst = plane.Buffers[_bufferSelect];
		byte[] src = plane.Buffers[_bufferSelect ^ 1];
		int dstAt = plane.Offset(cell.X << 2, cell.Y << 2);
		int srcAt = dstAt + (dy * plane.Pitch) + dx;
		int rows = cell.Height << 2;
		int bytes = cell.Width << 2;

		for (int r = 0; r < rows; r++) {
			Array.Copy(src, srcAt + (r * plane.Pitch), dst, dstAt + (r * plane.Pitch), bytes);
		}

		return true;
	}

	/// <summary>
	/// Decodes one coded cell starting at <paramref name="at"/>, and returns how many bytes it used,
	/// or -1 when the data is malformed.
	///
	/// <para>The first byte is the cell's descriptor: the mode in the high nibble and the codebook
	/// index in the low. Modes 0 and 1 code 4x4 blocks; 3 and 4 code 4x8 blocks, every other row
	/// interpolated; 10 codes 8x8 blocks; 11 codes 4x8 inter blocks. Modes 1 and 4 alternate between
	/// two codebooks row by row, picked through the frame's <c>alt_quant</c> pairs.</para>
	/// </summary>
	private int DecodeCell(Plane plane, Cell cell, int at) {
		int start = at;
		if (at >= _planeEnd) {
			return -1;
		}

		int descriptor = _packet[at++];
		int mode = descriptor >> 4;
		int vqIndex = descriptor & 0xF;
		bool intra = cell.Vector < 0;

		byte[] cur = plane.Buffers[_bufferSelect];
		int block = plane.Offset(cell.X << 2, cell.Y << 2);
		byte[] refBuf = cur;
		int refAt = block;
		bool hasRef = true;

		if (intra) {
			// Intra cells predict from the row above.
			refAt = block - plane.Pitch;
		} else if (mode >= 10) {
			// Modes 10 and 11 inter copy the whole predicted cell first and then add deltas in place.
			if (!CopyCell(plane, cell)) {
				return -1;
			}

			hasRef = false;
		} else {
			MotionVector(cell, out int dy, out int dx);
			if (!VectorInBounds(plane, cell, dy, dx)) {
				return -1;
			}

			refBuf = plane.Buffers[_bufferSelect ^ 1];
			refAt = block + (dy * plane.Pitch) + dx;
		}

		int primary;
		int secondary;
		if (mode == 1 || mode == 4) {
			int pair = _packet[_altQuant + vqIndex];
			primary = (pair >> 4) + _codebookOffset;
			secondary = (pair & 0xF) + _codebookOffset;
		} else {
			vqIndex += _codebookOffset;
			primary = secondary = vqIndex;
		}

		if (primary >= Indeo3Codebooks.BlockCount || secondary >= Indeo3Codebooks.BlockCount) {
			return -1;
		}

		// Requantise the prediction row onto this codebook's staircase. For modes 1 and 4 the test is
		// on the bare descriptor nibble, for the others on the nibble plus the codebook offset. The
		// row is rewritten in place, in whichever buffer it sits in.
		if (vqIndex >= 8 && hasRef) {
			int row = (vqIndex & 7) * 128;
			for (int x = 0; x < cell.Width << 2; x++) {
				refBuf[refAt + x] = Indeo3Requant.Table[row + (refBuf[refAt + x] & 127)];
			}
		}

		var books = new CellBooks(secondary, primary);
		CellResult error;

		switch (mode) {
			case 0:
			case 1:
			case 3:
			case 4:
				if (mode >= 3 && !intra) {
					return -1;
				}

				error = DecodeCellData(plane, cell, block, refBuf, refAt, 0, mode >= 3 ? 1 : 0, mode, books, ref at);
				break;

			case 10:
				error = intra
					? DecodeCellData(plane, cell, block, refBuf, refAt, 1, 1, mode, books, ref at)
					: DecodeCellData(plane, cell, block, cur, block, 1, 1, mode, books, ref at);
				break;

			case 11:
				if (intra) {
					return -1;
				}

				error = DecodeCellData(plane, cell, block, cur, block, 0, 1, mode, books, ref at);
				break;

			default:
				return -1;
		}

		return error == CellResult.Ok ? at - start : -1;
	}

	/// <summary>
	/// The two codebooks a cell reads: <see cref="Even"/> on even block rows and
	/// <see cref="Odd"/> on odd ones for modes 0 to 4, <see cref="Odd"/> throughout for 10 and 11.
	/// Books 16 and up swap the two halves of a quad.
	/// </summary>
	private readonly record struct CellBooks(int Even, int Odd);

	private enum CellResult { Ok, BadRle, BadData, BadCounter, Unsupported, OutOfData }

	// The escape codes, 0xF8 to 0xFF. Everything below 0xF8 is a VQ code.
	private const int EscapeSkipTwoBlocks = 0xF9;
	private const int EscapeSkipBlock = 0xFA;
	private const int EscapeBlockRun = 0xFB;
	private const int EscapeNullTwoBlocks = 0xFC;
	private const int EscapeNullToBlockEnd = 0xFD;
	private const int EscapeNullToLine3 = 0xFE;
	private const int EscapeNullToLine2 = 0xFF;
	private const int FirstEscape = 0xF8;

	/// <summary>
	/// The block loop shared by every mode. <paramref name="hZoom"/> and <paramref name="vZoom"/>
	/// double a block's width and height: a block is always four coded lines of two dyads, and the
	/// zoom decides how many pixels each one lands on.
	/// </summary>
	private CellResult DecodeCellData(
		Plane plane, Cell cell, int block, byte[] refBuf, int refBlock,
		int hZoom, int vZoom, int mode, CellBooks books, ref int at) {
		if ((cell.Height & vZoom) != 0 || (cell.Width & hZoom) != 0) {
			return CellResult.BadData;
		}

		byte[] dstBuf = plane.Buffers[_bufferSelect];
		int pitch = plane.Pitch;
		bool intra = cell.Vector < 0;
		int rleBlocks = 0;
		bool skip = false;
		bool firstRow = true;

		for (int y = 0; y < cell.Height; y += 1 + vZoom, firstRow = false) {
			for (int x = 0; x < cell.Width; x += 1 + hZoom) {
				int rowStart = (y << 2) * pitch + (x << 2);
				int dst = block + rowStart;
				int rf = refBlock + rowStart;

				if (rleBlocks > 0) {
					if (mode <= 4) {
						if (!intra || !skip) {
							CopyRows(dstBuf, dst, refBuf, rf, pitch, 4 << vZoom, 4);
						}
					} else if (mode == 10 && intra) {
						FillFromAbove(dstBuf, dst, refBuf, rf, pitch, 8, firstRow);
					}

					rleBlocks--;
					continue;
				}

				for (int line = 0; line < 4;) {
					int lines = 1;
					bool topOfCell = firstRow && line == 0;
					bool odd = mode > 4 || (line & 1) != 0;
					int book = odd ? books.Odd : books.Even;

					if (at >= _planeEnd) {
						return CellResult.OutOfData;
					}

					int code = _packet[at++];
					if (code < FirstEscape) {
						int dyads = _codebooks.DyadCount(book);
						int dyad1;
						int dyad2;
						if (code < dyads) {
							// A dyad code carries the second pair; the next byte carries the first.
							if (at >= _planeEnd) {
								return CellResult.OutOfData;
							}

							dyad1 = _packet[at++];
							dyad2 = code;
							if (dyad1 >= dyads || dyad1 >= FirstEscape) {
								return CellResult.BadData;
							}
						} else {
							// A quad code names an ordered pair of the book's first few dyads.
							int side = _codebooks.QuadSide(book);
							dyad1 = (code - dyads) / side;
							dyad2 = (code - dyads) % side;
							if (book >= 16) {
								(dyad1, dyad2) = (dyad2, dyad1);
							}
						}

						if (mode <= 4) {
							ApplyNarrow(dstBuf, dst, refBuf, rf, pitch, vZoom, book, dyad1, dyad2);
							if (mode >= 3) {
								Interpolate(dstBuf, dst, refBuf, rf, pitch, 4, topOfCell && cell.Y == 0);
							}
						} else if (mode == 10 && intra) {
							ApplyWideIntra(dstBuf, dst, refBuf, rf, pitch, book, dyad1, dyad2, topOfCell);
							Interpolate(dstBuf, dst, refBuf, rf, pitch, 8, topOfCell && cell.Y == 0);
						} else {
							ApplyInter(dstBuf, dst, pitch, mode, book, dyad1, dyad2);
						}
					} else {
						switch (code) {
							case EscapeNullTwoBlocks:
							case EscapeNullToBlockEnd:
							case EscapeNullToLine3:
							case EscapeNullToLine2:
								if (code == EscapeNullTwoBlocks) {
									skip = false;
									rleBlocks = 1;
									code = EscapeNullToBlockEnd;
								}

								lines = 257 - code - line;
								if (lines <= 0) {
									return CellResult.BadRle;
								}

								NullLines(dstBuf, dst, refBuf, rf, pitch, mode, vZoom, intra, lines, topOfCell);
								break;

							case EscapeBlockRun: {
								if (at >= _planeEnd) {
									return CellResult.OutOfData;
								}

								int count = _packet[at++];
								rleBlocks = (count & 0x1F) - 1;
								if (count >= 64 || rleBlocks < 0) {
									return CellResult.BadCounter;
								}

								skip = (count & 0x20) != 0;
								lines = 4 - line;
								if (mode >= 10 || !intra || !skip) {
									NullLines(dstBuf, dst, refBuf, rf, pitch, mode, vZoom, intra, lines, topOfCell);
								}

								break;
							}

							case EscapeSkipTwoBlocks:
							case EscapeSkipBlock:
								if (code == EscapeSkipTwoBlocks) {
									skip = true;
									rleBlocks = 1;
								}

								if (line != 0) {
									return CellResult.BadRle;
								}

								lines = 4;
								// An intra skip leaves the block as the buffer already holds it.
								if (!intra && mode <= 4) {
									CopyRows(dstBuf, dst, refBuf, rf, pitch, lines << vZoom, 4);
								}

								break;

							default:
								return CellResult.Unsupported;
						}
					}

					line += lines;
					rf += pitch * (lines << vZoom);
					dst += pitch * (lines << vZoom);
				}
			}
		}

		return CellResult.Ok;
	}

	/// <summary>A null delta over <paramref name="lines"/> coded lines: the prediction, unchanged.</summary>
	private static void NullLines(
		byte[] dstBuf, int dst, byte[] refBuf, int rf, int pitch,
		int mode, int vZoom, bool intra, int lines, bool topOfCell) {
		if (mode <= 4) {
			CopyRows(dstBuf, dst, refBuf, rf, pitch, lines << vZoom, 4);
		} else if (mode == 10 && intra) {
			FillFromAbove(dstBuf, dst, refBuf, rf, pitch, lines << 1, topOfCell);
		}
	}

	/// <summary>
	/// Copies <paramref name="rows"/> rows of <paramref name="bytes"/> each, top to bottom. For an
	/// intra cell the source is one row above the destination, so the copy is a smear of the
	/// prediction row downwards, and has to run in order.
	/// </summary>
	private static void CopyRows(byte[] dstBuf, int dst, byte[] srcBuf, int src, int pitch, int rows, int bytes) {
		for (int r = 0; r < rows; r++) {
			for (int i = 0; i < bytes; i++) {
				dstBuf[dst + (r * pitch) + i] = srcBuf[src + (r * pitch) + i];
			}
		}
	}

	/// <summary>
	/// Mode 10 intra's null delta: eight pixels of the row above repeated down <paramref name="rows"/>
	/// rows. At the top of a cell the row above is first sampled at every other pixel, and the first
	/// row is then interpolated between it and the second, as a coded line would be.
	/// </summary>
	private static void FillFromAbove(byte[] dstBuf, int dst, byte[] refBuf, int rf, int pitch, int rows, bool topOfCell) {
		ulong pixels = BinaryPrimitives.ReadUInt64LittleEndian(refBuf.AsSpan(rf));
		if (topOfCell) {
			pixels = ReplicateEven(pixels);
			for (int r = 1; r < rows; r++) {
				BinaryPrimitives.WriteUInt64LittleEndian(dstBuf.AsSpan(dst + (r * pitch)), pixels);
			}

			Average(dstBuf, dst, refBuf, rf, dstBuf, dst + pitch, 8);
		} else {
			for (int r = 0; r < rows; r++) {
				BinaryPrimitives.WriteUInt64LittleEndian(dstBuf.AsSpan(dst + (r * pitch)), pixels);
			}
		}
	}

	/// <summary>
	/// Modes 0 to 4: two dyads added to four prediction pixels, as two 16-bit adds. The add is
	/// deliberately 16 bits wide: a dyad's first delta is signed, and a negative one borrows from
	/// the second byte, which the dyad's value already compensates for.
	/// </summary>
	private void ApplyNarrow(
		byte[] dstBuf, int dst, byte[] refBuf, int rf, int pitch, int vZoom, int book, int dyad1, int dyad2) {
		int target = dst + (vZoom != 0 ? pitch : 0);
		AddNarrow(dstBuf, target, refBuf, rf, _codebooks.Dyad(book, dyad1));
		AddNarrow(dstBuf, target + 2, refBuf, rf + 2, _codebooks.Dyad(book, dyad2));
	}

	/// <summary>
	/// Mode 10 intra: two widened dyads added to eight prediction pixels, landing on the second row
	/// of the pair. At the top of a cell the prediction is the row above sampled at every other pixel.
	/// </summary>
	private void ApplyWideIntra(
		byte[] dstBuf, int dst, byte[] refBuf, int rf, int pitch, int book, int dyad1, int dyad2, bool topOfCell) {
		uint left = BinaryPrimitives.ReadUInt32LittleEndian(refBuf.AsSpan(rf));
		uint right = BinaryPrimitives.ReadUInt32LittleEndian(refBuf.AsSpan(rf + 4));
		if (topOfCell) {
			left = ReplicateEven(left);
			right = ReplicateEven(right);
		}

		WriteWide(dstBuf, dst + pitch, left + _codebooks.WideDyad(book, dyad1));
		WriteWide(dstBuf, dst + pitch + 4, right + _codebooks.WideDyad(book, dyad2));
	}

	/// <summary>
	/// Modes 10 and 11 inter: the cell already holds its motion-compensated copy, and the deltas
	/// are added in place to both rows of the pair.
	/// </summary>
	private void ApplyInter(byte[] buf, int dst, int pitch, int mode, int book, int dyad1, int dyad2) {
		for (int r = 0; r < 2; r++) {
			int at = dst + (r * pitch);
			if (mode == 10) {
				WriteWide(buf, at, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(at)) + _codebooks.WideDyad(book, dyad1));
				WriteWide(buf, at + 4, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(at + 4)) + _codebooks.WideDyad(book, dyad2));
			} else {
				AddNarrow(buf, at, buf, at, _codebooks.Dyad(book, dyad1));
				AddNarrow(buf, at + 2, buf, at + 2, _codebooks.Dyad(book, dyad2));
			}
		}
	}

	private static void AddNarrow(byte[] dstBuf, int dst, byte[] srcBuf, int src, short dyad) {
		int sum = (BinaryPrimitives.ReadUInt16LittleEndian(srcBuf.AsSpan(src)) + dyad) & 0x7F7F;
		BinaryPrimitives.WriteUInt16LittleEndian(dstBuf.AsSpan(dst), (ushort)sum);
	}

	private static void WriteWide(byte[] buf, int at, uint sum) =>
		BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(at), sum & 0x7F7F_7F7F);

	/// <summary>
	/// Fills the uncoded row of a 4x8 or 8x8 pair: the average of the rows above and below it, or a
	/// copy of the row below when the pair is the plane's top row and there is nothing above.
	/// </summary>
	private static void Interpolate(byte[] dstBuf, int dst, byte[] refBuf, int rf, int pitch, int bytes, bool copyBelow) {
		if (copyBelow) {
			Array.Copy(dstBuf, dst + pitch, dstBuf, dst, bytes);
		} else {
			Average(dstBuf, dst, refBuf, rf, dstBuf, dst + pitch, bytes);
		}
	}

	/// <summary>The truncating average of two runs of 7-bit pixels.</summary>
	private static void Average(byte[] dstBuf, int dst, byte[] aBuf, int a, byte[] bBuf, int b, int bytes) {
		for (int i = 0; i < bytes; i++) {
			dstBuf[dst + i] = (byte)(((aBuf[a + i] + bBuf[b + i]) >> 1) & 0x7F);
		}
	}

	/// <summary>Repeats every even-indexed pixel over its odd neighbour: ABCD becomes AACC.</summary>
	private static uint ReplicateEven(uint pixels) {
		pixels &= 0x00FF_00FF;
		return pixels | (pixels << 8);
	}

	private static ulong ReplicateEven(ulong pixels) {
		pixels &= 0x00FF_00FF_00FF_00FF;
		return pixels | (pixels << 8);
	}

	/// <summary>
	/// Converts the buffer just decoded to RGBA. Pixels are 7-bit and doubled to 8; chroma is one
	/// sample per 4x4 luma block, and is read nearest-neighbour.
	/// </summary>
	private void Output(VideoFrame frame) {
		Plane luma = _planes[0];
		Plane cb = _planes[1];
		Plane cr = _planes[2];
		byte[] yBuf = luma.Buffers[_bufferSelect];
		byte[] uBuf = cb.Buffers[_bufferSelect];
		byte[] vBuf = cr.Buffers[_bufferSelect];

		for (int y = 0; y < _height; y++) {
			for (int x = 0; x < _width; x++) {
				int lum = (yBuf[luma.Offset(x, y)] & 0x7F) << 1;
				int u = (uBuf[cb.Offset(x >> 2, y >> 2)] & 0x7F) << 1;
				int v = (vBuf[cr.Offset(x >> 2, y >> 2)] & 0x7F) << 1;

				// ITU-R BT.601, studio range.
				int c = 298 * (lum - 16);
				int d = u - 128;
				int e = v - 128;
				frame.SetPixel(
					x,
					y,
					Clamp((c + (409 * e) + 128) >> 8),
					Clamp((c - (100 * d) - (208 * e) + 128) >> 8),
					Clamp((c + (516 * d) + 128) >> 8));
			}
		}
	}

	private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

	private static int AlignUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

	/// <summary>
	/// One plane's pair of buffers. Each holds the picture plus one prediction row above it, filled
	/// with <c>0x40</c>, which is what an intra cell on the top edge predicts from.
	/// </summary>
	private sealed class Plane {
		internal Plane(int width, int height) {
			Width = width;
			Height = height;
			Pitch = width;
			Buffers = [new byte[width * (height + 1)], new byte[width * (height + 1)]];
			foreach (byte[] buffer in Buffers) {
				buffer.AsSpan(0, width).Fill(0x40);
			}
		}

		internal int Width { get; }
		internal int Height { get; }
		internal int Pitch { get; }
		internal byte[][] Buffers { get; }

		/// <summary>Index of pixel (<paramref name="x"/>, <paramref name="y"/>); row -1 is the prediction row.</summary>
		internal int Offset(int x, int y) => ((y + 1) * Pitch) + x;
	}
}
