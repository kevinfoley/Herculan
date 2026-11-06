using System.Buffers.Binary;
using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// Reads and writes ES1's <c>FLIC\*.DFL</c> (<see cref="Es1Flic"/>) as <c>PLAYFLIC.EXE</c> walks them
/// (docs/es1/formats/dfl-flic.md#the-frames): the 128-byte header, then from the first frame's offset as many top-level
/// chunks as the header's frames, each a frame's subchunks split by their own sizes.
///
/// <para>The player reads a chunk at a time and stops at the first it cannot use; this reader rejects a chunk that does
/// not fit, which it could not give back whole. A top-level chunk of another type than a frame keeps its bytes after its
/// 6-byte header in <see cref="Es1FlicFrame.Tail"/>.</para>
/// </summary>
public class Es1FlicTransformer : ByteTransformer<Es1Flic> {
	/// <exception cref="InvalidDataException">A chunk does not fit the file or its frame.</exception>
	public override Es1Flic? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		byte[] b = inputArray;
		if (b.Length < Es1Flic.HeaderLength) {
			throw new InvalidDataException($"The file is {b.Length} bytes, shorter than its header.");
		}

		var flic = new Es1Flic {
			FileSize = U32(b, 0x00),
			Magic = U16(b, 0x04),
			FrameCount = U16(b, 0x06),
			Width = U16(b, 0x08),
			Height = U16(b, 0x0a),
			Depth = U16(b, 0x0c),
			Flags = U16(b, 0x0e),
			Speed = U32(b, 0x10),
			Unread14 = b[0x14..0x26],
			AspectX = U16(b, 0x26),
			AspectY = U16(b, 0x28),
			Unread2A = b[0x2a..0x50],
			Frame1Offset = U32(b, 0x50),
			Frame2Offset = U32(b, 0x54),
			Unread58 = b[0x58..0x80],
		};

		if (flic.Frame1Offset < Es1Flic.HeaderLength || flic.Frame1Offset > b.Length) {
			throw new InvalidDataException($"The first frame's offset {flic.Frame1Offset} is not in the file's {b.Length} bytes past its header.");
		}

		int at = (int)flic.Frame1Offset;
		flic.Gap = b[Es1Flic.HeaderLength..at];
		flic.Frames = new Es1FlicFrame[flic.FrameCount];
		for (int i = 0; i < flic.FrameCount; i++) {
			if (at + Es1Flic.ChunkHeaderLength > b.Length) {
				throw new InvalidDataException($"Frame {i} at {at} does not fit the file's {b.Length} bytes.");
			}

			int size = (int)U32(b, at);
			if (size < Es1Flic.ChunkHeaderLength || at + size > b.Length) {
				throw new InvalidDataException($"Frame {i} at {at} is {size} bytes, which does not fit the file's {b.Length}.");
			}

			flic.Frames[i] = ReadFrame(b, at, size, i);
			at += size;
		}

		flic.Tail = b[at..];
		return flic;
	}

	public override byte[]? Write(Es1Flic source) {
		if (source.Unread14.Length != 0x12 || source.Unread2A.Length != 0x26 || source.Unread58.Length != 0x28
			|| Es1Flic.HeaderLength + source.Gap.Length != source.Frame1Offset) {
			throw new InvalidDataException("The header's spans or the gap before the first frame have the wrong length.");
		}

		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write(source.FileSize);
		w.Write(source.Magic);
		w.Write(checked((ushort)source.Frames.Length));
		w.Write(source.Width);
		w.Write(source.Height);
		w.Write(source.Depth);
		w.Write(source.Flags);
		w.Write(source.Speed);
		w.Write(source.Unread14);
		w.Write(source.AspectX);
		w.Write(source.AspectY);
		w.Write(source.Unread2A);
		w.Write(source.Frame1Offset);
		w.Write(source.Frame2Offset);
		w.Write(source.Unread58);
		w.Write(source.Gap);
		foreach (var frame in source.Frames) {
			WriteFrame(w, frame);
		}

		w.Write(source.Tail);
		w.Flush();
		return o.ToArray();
	}

	private static Es1FlicFrame ReadFrame(byte[] b, int at, int size, int index) {
		var frame = new Es1FlicFrame { Type = U16(b, at + 4) };
		if (frame.Type != Es1Flic.FrameType) {
			frame.Unread08 = [];
			frame.Tail = b[(at + Es1Flic.ChunkHeaderLength)..(at + size)];
			return frame;
		}

		if (size < Es1Flic.FrameHeaderLength) {
			throw new InvalidDataException($"Frame {index} at {at} is {size} bytes, shorter than its header.");
		}

		int count = U16(b, at + 6);
		frame.Unread08 = b[(at + 8)..(at + Es1Flic.FrameHeaderLength)];
		frame.Chunks = new Es1FlicChunk[count];
		int end = at + size, sub = at + Es1Flic.FrameHeaderLength;
		for (int i = 0; i < count; i++) {
			int chunkSize = sub + Es1Flic.ChunkHeaderLength <= end ? (int)U32(b, sub) : -1;
			if (chunkSize < Es1Flic.ChunkHeaderLength || sub + chunkSize > end) {
				throw new InvalidDataException($"Subchunk {i} of frame {index}, at {sub}, does not fit its frame's end at {end}.");
			}

			frame.Chunks[i] = new Es1FlicChunk {
				Type = U16(b, sub + 4),
				Data = b[(sub + Es1Flic.ChunkHeaderLength)..(sub + chunkSize)],
			};
			sub += chunkSize;
		}

		frame.Tail = b[sub..end];
		return frame;
	}

	private static void WriteFrame(BinaryWriter w, Es1FlicFrame frame) {
		if (frame.Type != Es1Flic.FrameType) {
			w.Write(checked((uint)(Es1Flic.ChunkHeaderLength + frame.Tail.Length)));
			w.Write(frame.Type);
			w.Write(frame.Tail);
			return;
		}

		if (frame.Unread08.Length != 8) {
			throw new InvalidDataException("A frame's header span has the wrong length.");
		}

		int size = Es1Flic.FrameHeaderLength + frame.Tail.Length;
		foreach (var chunk in frame.Chunks) {
			size += Es1Flic.ChunkHeaderLength + chunk.Data.Length;
		}

		w.Write(checked((uint)size));
		w.Write(frame.Type);
		w.Write(checked((ushort)frame.Chunks.Length));
		w.Write(frame.Unread08);
		foreach (var chunk in frame.Chunks) {
			w.Write(checked((uint)(Es1Flic.ChunkHeaderLength + chunk.Data.Length)));
			w.Write(chunk.Type);
			w.Write(chunk.Data);
		}

		w.Write(frame.Tail);
	}

	private static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));

	private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
