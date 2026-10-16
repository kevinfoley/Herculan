using HercWorks.Video.Avi;

namespace HercWorks.Video;

/// <summary>
/// Where <see cref="MoviePlayback"/> takes a movie's packets from, each stream in file order: a file held
/// whole (<see cref="FilePacketSource"/>), or one read as it plays (<see cref="StreamedPacketSource"/>).
/// </summary>
internal interface IMoviePacketSource : IDisposable {
	/// <summary>Takes the next video packet, or answers false when none is waiting.</summary>
	bool TryTakeVideo(out ReadOnlyMemory<byte> packet);

	/// <summary>Takes the next audio packet, or answers false when none is waiting.</summary>
	bool TryTakeAudio(out ReadOnlyMemory<byte> packet);

	/// <summary>Whether every video packet the movie holds has been taken.</summary>
	bool VideoEnded { get; }

	/// <summary>Whether every audio packet the movie holds has been taken.</summary>
	bool AudioEnded { get; }

	/// <summary>How many video packets the movie holds, or null while that is not yet known.</summary>
	int? VideoPacketCount { get; }

	/// <summary>Why reading stopped early, or null.</summary>
	Exception? Failure { get; }

	/// <summary>Whether <see cref="Rewind"/> works.</summary>
	bool CanRewind { get; }

	/// <summary>Back to the first packet of each stream.</summary>
	void Rewind();

	/// <summary>Blocks until <paramref name="videoPackets"/> video packets are waiting, or the read can give no more.</summary>
	void WaitFor(int videoPackets);
}

/// <summary>A movie held whole: its packets are slices of the file.</summary>
internal sealed class FilePacketSource(AviFile file) : IMoviePacketSource {
	private int _video;
	private int _audio;

	public bool TryTakeVideo(out ReadOnlyMemory<byte> packet) => Take(file.VideoPackets, ref _video, out packet);

	public bool TryTakeAudio(out ReadOnlyMemory<byte> packet) => Take(file.AudioPackets, ref _audio, out packet);

	public bool VideoEnded => _video >= file.VideoPackets.Count;

	public bool AudioEnded => _audio >= file.AudioPackets.Count;

	public int? VideoPacketCount => file.VideoPackets.Count;

	public Exception? Failure => null;

	public bool CanRewind => true;

	public void Rewind() {
		_video = 0;
		_audio = 0;
	}

	public void WaitFor(int videoPackets) {
	}

	public void Dispose() {
	}

	private bool Take(IReadOnlyList<AviPacket> packets, ref int next, out ReadOnlyMemory<byte> packet) {
		if (next >= packets.Count) {
			packet = default;
			return false;
		}

		AviPacket at = packets[next++];
		packet = file.Bytes.AsMemory(at.At, at.Length);
		return true;
	}
}

/// <summary>
/// A movie read as it plays: a worker thread runs an <see cref="AviStreamReader"/> ahead of playback, by up
/// to <see cref="VideoLimits.ReadAheadBytes"/> of packets waiting, and the player takes them as they come.
///
/// <para>The worker owns the reader and its stream, and disposes them when it stops: at the end of the
/// file, on a read that throws (kept as <see cref="Failure"/>), or after <see cref="Dispose"/>, which does
/// not wait for a read under way to finish.</para>
/// </summary>
internal sealed class StreamedPacketSource : IMoviePacketSource {
	private readonly object _gate = new();
	private readonly Queue<byte[]> _video = new();
	private readonly Queue<byte[]> _audio = new();
	private readonly int _readAheadBytes;
	private long _waitingBytes;
	private int _videoRead;
	private bool _ended;
	private bool _stopping;
	private Exception? _failure;

	public StreamedPacketSource(AviStreamReader reader) {
		_readAheadBytes = reader.Header.Limits.ReadAheadBytes;
		new Thread(() => Run(reader)) { IsBackground = true, Name = "Movie read-ahead" }.Start();
	}

	public bool TryTakeVideo(out ReadOnlyMemory<byte> packet) => Take(_video, out packet);

	public bool TryTakeAudio(out ReadOnlyMemory<byte> packet) => Take(_audio, out packet);

	public bool VideoEnded {
		get {
			lock (_gate) {
				return _ended && _video.Count == 0;
			}
		}
	}

	public bool AudioEnded {
		get {
			lock (_gate) {
				return _ended && _audio.Count == 0;
			}
		}
	}

	public int? VideoPacketCount {
		get {
			lock (_gate) {
				return _ended ? _videoRead : null;
			}
		}
	}

	public Exception? Failure {
		get {
			lock (_gate) {
				return _failure;
			}
		}
	}

	public bool CanRewind => false;

	public void Rewind() => throw new InvalidOperationException("A streamed movie cannot rewind.");

	public void WaitFor(int videoPackets) {
		lock (_gate) {
			while (!_ended && !_stopping && _video.Count < videoPackets && _waitingBytes < _readAheadBytes) {
				Monitor.Wait(_gate);
			}
		}
	}

	public void Dispose() {
		lock (_gate) {
			_stopping = true;
			_video.Clear();
			_audio.Clear();
			_waitingBytes = 0;
			Monitor.PulseAll(_gate);
		}
	}

	private bool Take(Queue<byte[]> queue, out ReadOnlyMemory<byte> packet) {
		lock (_gate) {
			if (!queue.TryDequeue(out byte[]? data)) {
				packet = default;
				return false;
			}

			_waitingBytes -= data.Length;
			Monitor.PulseAll(_gate);
			packet = data;
			return true;
		}
	}

	private void Run(AviStreamReader reader) {
		using (reader) {
			while (true) {
				lock (_gate) {
					// A packet larger than the whole read-ahead is still taken once nothing else waits.
					while (!_stopping && _waitingBytes >= _readAheadBytes) {
						Monitor.Wait(_gate);
					}

					if (_stopping) {
						return;
					}
				}

				AviStreamPacket packet;
				bool more;
				try {
					more = reader.TryReadNext(out packet);
				} catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException
					or NotSupportedException) {
					lock (_gate) {
						_failure = ex;
						_ended = true;
						Monitor.PulseAll(_gate);
					}

					return;
				}

				lock (_gate) {
					if (_stopping) {
						return;
					}

					if (!more) {
						_ended = true;
						Monitor.PulseAll(_gate);
						return;
					}

					(packet.IsVideo ? _video : _audio).Enqueue(packet.Data);
					_waitingBytes += packet.Data.Length;
					if (packet.IsVideo) {
						_videoRead++;
					}

					Monitor.PulseAll(_gate);
				}
			}
		}
	}
}
