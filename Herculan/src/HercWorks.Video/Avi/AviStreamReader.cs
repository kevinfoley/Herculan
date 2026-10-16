using HercWorks.Video.Riff;

namespace HercWorks.Video.Avi;

/// <summary>One packet <see cref="AviStreamReader"/> read: which of the two streams it belongs to, and its bytes.</summary>
public readonly record struct AviStreamPacket(bool IsVideo, byte[] Data);

/// <summary>
/// Reads an AVI front to back from a stream, one packet at a time, without holding the file: a movie on a
/// CD plays as it is read rather than after.
///
/// <para>Everything before the first <c>movi</c> list is read and parsed as an <see cref="AviFile"/>
/// (<see cref="Header"/>), so the formats and the stream mapping are the in-memory reader's own. The movie
/// lists are then walked chunk by chunk under <see cref="AviFile"/>'s rules: a chunk whose length runs past
/// its list ends that list's walk and the parent carries on after it, a list nested past
/// <see cref="VideoLimits.MaxRiffDepth"/> is skipped whole, a chunk over
/// <see cref="VideoLimits.MaxChunkBytes"/> is skipped, and each stream keeps at most
/// <see cref="VideoLimits.MaxFrames"/> packets. Only a packet's own bytes are allocated, and only once its
/// length has passed those checks.</para>
///
/// <para>Unlike <see cref="AviFile.Open"/>, which takes a list claiming more than the file holds as damage
/// and drops it whole, this reads to wherever the stream ends, so a truncated file plays up to the cut. A
/// file whose header comes after its first movie list is refused.</para>
///
/// <para>Not thread-safe; one reader runs on one thread at a time. It owns the stream it was opened on.</para>
/// </summary>
public sealed class AviStreamReader : IDisposable {
	private static readonly uint List = RiffReader.FourCc('L', 'I', 'S', 'T');
	private static readonly uint MovieList = RiffReader.FourCc('m', 'o', 'v', 'i');

	private readonly Stream _stream;
	private readonly long _riffEnd;
	private readonly byte[] _scratch = new byte[12];

	// The lists the walk is inside, innermost on top: where each ends, and where its parent carries on.
	private readonly Stack<(long End, long Next)> _lists = new();

	// The logical cursor, and how far the stream has actually been read; both from the start of the file.
	private long _at;
	private long _read;
	private int _videoPackets;
	private int _audioPackets;
	private bool _done;

	private AviStreamReader(Stream stream, AviFile header, long riffEnd, long at, long read) {
		_stream = stream;
		Header = header;
		_riffEnd = riffEnd;
		_at = at;
		_read = read;
	}

	/// <summary>The file's header, parsed as an <see cref="AviFile"/> holding no packets.</summary>
	public AviFile Header { get; }

	/// <summary>
	/// Reads the header from the stream's current position, or answers null, with the stream disposed, when
	/// it is not an AVI, its header is malformed or over <see cref="VideoLimits.MaxHeaderBytes"/>, or it has
	/// no video stream this assembly understands. Blocks while the header is read.
	/// </summary>
	/// <exception cref="IOException">The stream failed while the header was read; it has been disposed.</exception>
	public static AviStreamReader? Open(Stream stream, VideoLimits? limits = null) {
		ArgumentNullException.ThrowIfNull(stream);
		limits ??= VideoLimits.Default;

		try {
			if (ReadHeader(stream, limits) is { } reader) {
				return reader;
			}
		} catch {
			stream.Dispose();
			throw;
		}

		stream.Dispose();
		return null;
	}

	// The RIFF header, then every top-level chunk up to the first movie list copied whole into the prefix
	// AviFile parses.
	private static AviStreamReader? ReadHeader(Stream stream, VideoLimits limits) {
		var prefix = new MemoryStream();
		var head = new byte[12];
		if (stream.ReadAtLeast(head, 12, throwOnEndOfStream: false) < 12
			|| RiffReader.ReadU32(head, 0) != RiffReader.FourCc('R', 'I', 'F', 'F')
			|| RiffReader.ReadU32(head, 8) != RiffReader.FourCc('A', 'V', 'I', ' ')) {
			return null;
		}

		prefix.Write(head);
		long riffEnd = 8 + (long)RiffReader.ReadU32(head, 4);
		long at = 12;
		while (at + 8 <= riffEnd) {
			if (stream.ReadAtLeast(head.AsSpan(0, 8), 8, throwOnEndOfStream: false) < 8) {
				return null;
			}

			uint id = RiffReader.ReadU32(head, 0);
			uint declared = RiffReader.ReadU32(head, 4);
			long body = at + 8;
			if (declared > int.MaxValue || declared > riffEnd - body) {
				return null;
			}

			int length = (int)declared;
			if (id == List && length >= 4) {
				if (stream.ReadAtLeast(head.AsSpan(8, 4), 4, throwOnEndOfStream: false) < 4) {
					return null;
				}

				if (RiffReader.ReadU32(head, 8) == MovieList) {
					if (AviFile.Open(prefix.ToArray(), limits) is not { VideoFormat: not null } header) {
						return null;
					}

					var reader = new AviStreamReader(stream, header, riffEnd, body + 4, body + 4);
					reader._lists.Push((body + length, body + length + (length & 1)));
					return reader;
				}
			}

			int copied = id == List && length >= 4 ? 12 : 8;
			int whole = length + (length & 1);
			if (prefix.Length + 8 + whole > limits.MaxHeaderBytes) {
				return null;
			}

			prefix.Write(head, 0, copied);
			var rest = new byte[whole - (copied - 8)];
			int got = stream.ReadAtLeast(rest, rest.Length, throwOnEndOfStream: false);
			// The pad byte of the file's last chunk may be missing; nothing else may.
			if (got < rest.Length - (length & 1)) {
				return null;
			}

			prefix.Write(rest, 0, got);
			at = body + whole;
		}

		return null;
	}

	/// <summary>
	/// Reads up to the next packet of the video or the audio stream, or answers false once the file is
	/// exhausted, truncated or damaged past reading. Blocks while the stream is read.
	/// </summary>
	/// <exception cref="IOException">The stream failed.</exception>
	public bool TryReadNext(out AviStreamPacket packet) {
		packet = default;
		VideoLimits limits = Header.Limits;
		while (!_done) {
			while (_lists.Count > 0 && _at + 8 > _lists.Peek().End) {
				_at = _lists.Pop().Next;
			}

			if (_lists.Count == 0) {
				if (!EnterNextMovieList()) {
					_done = true;
				}

				continue;
			}

			if (!ReadAt(_at, _scratch.AsSpan(0, 8))) {
				_done = true;
				break;
			}

			uint id = RiffReader.ReadU32(_scratch, 0);
			uint declared = RiffReader.ReadU32(_scratch, 4);
			long body = _at + 8;
			if (declared > int.MaxValue || declared > _lists.Peek().End - body) {
				// The list's walk stops here; its parent carries on after it.
				_at = _lists.Pop().Next;
				continue;
			}

			int length = (int)declared;
			long next = body + length + (length & 1);
			if (id == List && length >= 4) {
				// AviFile's leaf walk descends a list only while its depth budget lasts; the movie list
				// itself spends the first level.
				if (_lists.Count < limits.MaxRiffDepth) {
					_lists.Push((body + length, next));
					_at = body + 4;
				} else {
					_at = next;
				}

				continue;
			}

			_at = next;
			if (length > limits.MaxChunkBytes || !Header.Classify(id, out bool isVideo)) {
				continue;
			}

			ref int kept = ref isVideo ? ref _videoPackets : ref _audioPackets;
			if (kept >= limits.MaxFrames) {
				continue;
			}

			var data = new byte[length];
			if (!ReadAt(body, data)) {
				_done = true;
				break;
			}

			kept++;
			packet = new AviStreamPacket(isVideo, data);
			return true;
		}

		return false;
	}

	public void Dispose() => _stream.Dispose();

	// Back at the top level after a movie list: on through the RIFF body to the next one, as AviFile reads
	// every movie list it holds. False when there is none.
	private bool EnterNextMovieList() {
		while (_at + 8 <= _riffEnd) {
			if (!ReadAt(_at, _scratch.AsSpan(0, 8))) {
				return false;
			}

			uint id = RiffReader.ReadU32(_scratch, 0);
			uint declared = RiffReader.ReadU32(_scratch, 4);
			long body = _at + 8;
			if (declared > int.MaxValue || declared > _riffEnd - body) {
				return false;
			}

			int length = (int)declared;
			long next = body + length + (length & 1);
			if (id == List && length >= 4) {
				if (!ReadAt(body, _scratch.AsSpan(8, 4))) {
					return false;
				}

				if (RiffReader.ReadU32(_scratch, 8) == MovieList) {
					_lists.Push((body + length, next));
					_at = body + 4;
					return true;
				}
			}

			_at = next;
		}

		return false;
	}

	// Fills into from the file at at, skipping forward to it: by seeking when the stream can, by reading
	// otherwise. False at the end of the stream. The walk never goes backwards.
	private bool ReadAt(long at, Span<byte> into) {
		if (at < _read) {
			return false;
		}

		if (at > _read) {
			long skip = at - _read;
			if (_stream.CanSeek) {
				_stream.Seek(skip, SeekOrigin.Current);
			} else {
				Span<byte> discard = stackalloc byte[4096];
				while (skip > 0) {
					int got = _stream.Read(discard[..(int)Math.Min(skip, discard.Length)]);
					if (got == 0) {
						return false;
					}

					skip -= got;
				}
			}

			_read = at;
		}

		int read = _stream.ReadAtLeast(into, into.Length, throwOnEndOfStream: false);
		_read += read;
		return read == into.Length;
	}
}
