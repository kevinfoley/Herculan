using System.Buffers.Binary;
using System.Text;
using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes ES1's <c>.HMP</c> songs (<see cref="Es1HmpSong"/>) in the layout <c>HmiMidi_InitSong</c>
/// (<c>ES1DBSIM 0006e4c1</c>) reads (docs/es1/formats/hmp-music.md#layout). Writing a parsed song gives back its bytes.
///
/// <para>A file that does not open <c>HMIMIDIP</c>, is shorter than the header, has more than
/// <see cref="Es1HmpSong.MaxTracks"/> tracks, or whose tracks or branch table run past the end or overlap throws
/// <see cref="InvalidDataException"/>.</para>
/// </summary>
public class Es1HmpSongTransformer : ByteTransformer<Es1HmpSong> {
	private static readonly byte[] Magic = Encoding.ASCII.GetBytes("HMIMIDIP");

	/// <exception cref="InvalidDataException">The bytes do not walk the layout.</exception>
	public override Es1HmpSong? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		byte[] b = inputArray;
		if (b.Length < Es1HmpSong.HeaderSize || !b.AsSpan(0, Magic.Length).SequenceEqual(Magic)) {
			throw new InvalidDataException($"{b.Length} bytes do not open with an HMIMIDIP header.");
		}

		var song = new Es1HmpSong {
			Signature = b[..0x20],
			BranchTableOffset = Int(b, 0x20),
			Unread24 = b[0x24..0x30],
			TrackCount = Int(b, 0x30),
			Unread34 = Ints(b, 0x34, 3),
			Unread40 = Ints(b, 0x40, 16),
			TrackDevices = Enumerable.Range(0, Es1HmpSong.MaxTracks).Select(t => Ints(b, 0x80 + 0x14 * t, 5)).ToArray(),
			CallbackSlot = b[0x300..Es1HmpSong.HeaderSize],
		};
		if (song.TrackCount is < 0 or > Es1HmpSong.MaxTracks) {
			throw new InvalidDataException($"The track count, {song.TrackCount}, is outside 0 to {Es1HmpSong.MaxTracks}.");
		}

		int at = Es1HmpSong.HeaderSize;
		for (int t = 0; t < song.TrackCount; t++) {
			int length = at + 12 <= b.Length ? Int(b, at + 4) : -1;
			if (length < 12 || length > b.Length - at) {
				throw new InvalidDataException($"Track {t} at 0x{at:x} runs past the end of {b.Length} bytes.");
			}

			song.Tracks.Add(new Es1HmpTrack {
				ChunkNumber = Int(b, at), Length = length, Unread08 = Int(b, at + 8), Events = b[(at + 12)..(at + length)],
			});
			at += length;
		}

		int branches = song.BranchTableOffset;
		if (branches < at || branches > b.Length - song.TrackCount) {
			throw new InvalidDataException($"The branch table at 0x{branches:x} is not between the tracks' end, 0x{at:x}, and the file's.");
		}

		song.AfterTracks = b[at..branches];
		song.BranchCounts = b[branches..(branches + song.TrackCount)];
		at = branches + song.TrackCount;
		int entries = song.BranchCounts.Sum(c => c);
		if (entries * 24 > b.Length - at) {
			throw new InvalidDataException($"{entries} branch entries from 0x{at:x} run past the end of {b.Length} bytes.");
		}

		for (int i = 0; i < entries; i++, at += 24) {
			song.Branches.Add(new Es1HmpBranch {
				Head = b[at..(at + 8)], PointerSlot = Int(b, at + 8), DataOffset = Int(b, at + 12), Tail = b[(at + 16)..(at + 24)],
			});
		}

		song.BranchData = b[at..];
		return song;
	}

	public override byte[]? Write(Es1HmpSong source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write(source.Signature);
		w.Write(source.BranchTableOffset);
		w.Write(source.Unread24);
		w.Write(source.TrackCount);
		WriteInts(w, source.Unread34);
		WriteInts(w, source.Unread40);
		foreach (int[] devices in source.TrackDevices) {
			WriteInts(w, devices);
		}

		w.Write(source.CallbackSlot);
		foreach (var track in source.Tracks) {
			w.Write(track.ChunkNumber);
			w.Write(track.Length);
			w.Write(track.Unread08);
			w.Write(track.Events);
		}

		w.Write(source.AfterTracks);
		w.Write(source.BranchCounts);
		foreach (var branch in source.Branches) {
			w.Write(branch.Head);
			w.Write(branch.PointerSlot);
			w.Write(branch.DataOffset);
			w.Write(branch.Tail);
		}

		w.Write(source.BranchData);
		w.Flush();
		return o.ToArray();
	}

	private static int Int(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));

	private static int[] Ints(byte[] b, int at, int count) => Enumerable.Range(0, count).Select(i => Int(b, at + 4 * i)).ToArray();

	private static void WriteInts(BinaryWriter w, int[] values) {
		foreach (int v in values) {
			w.Write(v);
		}
	}
}
