using HercWorks.Core.Io;
using System.Buffers.Binary;

namespace Herculan.Engine.Audio;

/// <summary>
/// Music from a directory of <c>Track02.wav</c> … <c>Track07.wav</c>: the disc's own audio tracks,
/// already extracted, as 44.1 kHz 16-bit stereo PCM. For a machine with no CD drive, and the format
/// <see cref="CdRipMusicSource"/> caches its own rips in. <b>This engine's own</b>; retail plays
/// only the disc.
/// </summary>
public sealed class WaveFileMusicSource : MusicSourceBase {
	private readonly string _directory;
	private readonly int _trackCount;

	/// <summary>Reads tracks from <paramref name="directory"/>, which need not exist.</summary>
	public WaveFileMusicSource(string directory) {
		_directory = directory;
		_trackCount = Directory.Exists(directory)
			? Enumerable.Range(1, 99).Count(track => File.Exists(PathFor(directory, track)))
			: 0;
	}

	/// <inheritdoc />
	public override bool IsAvailable => _trackCount > 0;

	/// <inheritdoc />
	public override string Status => IsAvailable
		? $"{_trackCount} track files in {_directory}"
		: $"no TrackNN.wav files in {_directory}";

	/// <summary>Where track <paramref name="track"/> lives under <paramref name="directory"/>.</summary>
	public static string PathFor(string directory, int track) =>
		CaseInsensitivePath.Combine(directory, $"Track{track:00}.wav");

	/// <inheritdoc />
	protected override (MusicTrack Track, Action<CancellationToken> Produce)? Prepare(int track) {
		string path = PathFor(_directory, track);
		if (!File.Exists(path)) {
			return null;
		}

		FileStream stream;
		try {
			stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
				bufferSize: 1, FileOptions.SequentialScan);
		} catch (IOException) {
			return null;
		} catch (UnauthorizedAccessException) {
			return null;
		}

		if (!TryReadHeader(stream, out long dataLength)) {
			stream.Dispose();
			return null;
		}

		long frames = dataLength / (MusicTrack.Channels * sizeof(short));
		if (frames <= 0) {
			stream.Dispose();
			return null;
		}

		var created = new MusicTrack(track, frames);
		return (created, token => {
			using (stream) {
				var chunk = new byte[256 * 1024];
				while (!created.IsComplete) {
					token.ThrowIfCancellationRequested();
					int read = stream.Read(chunk);
					if (read == 0) {
						created.Fail($"{path} ends before its data chunk does");
						return;
					}

					// Append takes whole frames; a read that splits one is topped up before the next.
					int whole = read - read % (MusicTrack.Channels * sizeof(short));
					created.Append(chunk.AsSpan(0, whole));
					if (whole != read) {
						stream.Seek(whole - read, SeekOrigin.Current);
					}
				}
			}
		});
	}

	/// <summary>
	/// Walks a RIFF/WAVE header up to its <c>data</c> chunk, leaving the stream at the first sample.
	/// Accepts only what a Red Book track is — PCM, two channels, 44100 Hz, 16 bits — because nothing
	/// here resamples.
	/// </summary>
	internal static bool TryReadHeader(Stream stream, out long dataLength) {
		dataLength = 0;
		Span<byte> head = stackalloc byte[12];
		if (stream.Read(head) != 12
			|| BinaryPrimitives.ReadUInt32LittleEndian(head) != Riff
			|| BinaryPrimitives.ReadUInt32LittleEndian(head[8..]) != Wave) {
			return false;
		}

		bool formatOk = false;
		Span<byte> chunk = stackalloc byte[8];
		Span<byte> format = stackalloc byte[16];
		while (stream.Read(chunk) == 8) {
			uint id = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
			long length = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);

			if (id == Fmt && length >= 16) {
				if (stream.Read(format) != 16) {
					return false;
				}

				formatOk = BinaryPrimitives.ReadUInt16LittleEndian(format) == 1
					&& BinaryPrimitives.ReadUInt16LittleEndian(format[2..]) == MusicTrack.Channels
					&& BinaryPrimitives.ReadInt32LittleEndian(format[4..]) == MusicTrack.SampleRate
					&& BinaryPrimitives.ReadUInt16LittleEndian(format[14..]) == 16;
				stream.Seek(length - 16 + (length & 1), SeekOrigin.Current);
			} else if (id == Data) {
				dataLength = length;
				return formatOk;
			} else {
				stream.Seek(length + (length & 1), SeekOrigin.Current);
			}
		}

		return false;
	}

	/// <summary>
	/// Writes a completed track as a canonical 44-byte-header WAV. Written to a temporary name and
	/// moved into place, so an interrupted write never leaves a short file that reads as a track.
	/// </summary>
	internal static void Write(string path, MusicTrack track) {
		ReadOnlySpan<byte> pcm = track.CompletedBytes;
		string partial = path + ".partial";

		using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None)) {
			Span<byte> header = stackalloc byte[44];
			const int blockAlign = MusicTrack.Channels * sizeof(short);
			BinaryPrimitives.WriteUInt32LittleEndian(header, Riff);
			BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)(36 + pcm.Length));
			BinaryPrimitives.WriteUInt32LittleEndian(header[8..], Wave);
			BinaryPrimitives.WriteUInt32LittleEndian(header[12..], Fmt);
			BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 16);
			BinaryPrimitives.WriteUInt16LittleEndian(header[20..], 1);
			BinaryPrimitives.WriteUInt16LittleEndian(header[22..], MusicTrack.Channels);
			BinaryPrimitives.WriteUInt32LittleEndian(header[24..], MusicTrack.SampleRate);
			BinaryPrimitives.WriteUInt32LittleEndian(header[28..], MusicTrack.SampleRate * blockAlign);
			BinaryPrimitives.WriteUInt16LittleEndian(header[32..], blockAlign);
			BinaryPrimitives.WriteUInt16LittleEndian(header[34..], 16);
			BinaryPrimitives.WriteUInt32LittleEndian(header[36..], Data);
			BinaryPrimitives.WriteUInt32LittleEndian(header[40..], (uint)pcm.Length);
			stream.Write(header);
			stream.Write(pcm);
		}

		File.Move(partial, path, overwrite: true);
	}

	private const uint Riff = 0x46464952;
	private const uint Wave = 0x45564157;
	private const uint Fmt = 0x20746d66;
	private const uint Data = 0x61746164;
}
