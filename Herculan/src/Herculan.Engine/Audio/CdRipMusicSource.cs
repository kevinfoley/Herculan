using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Herculan.Engine.Audio;

/// <summary>
/// Reads the disc's audio tracks digitally — <c>IOCTL_CDROM_RAW_READ</c> in CD-DA mode against the
/// raw drive device — so that <see cref="StreamedCdAudio"/> can play them through OpenAL rather than
/// asking the drive to play them. <b>The transport is this engine's own</b>; retail drives the disc
/// through MCI, and why this engine does not is in docs/retail/formats/audio.md's "CD audio".
///
/// <para>Windows-only, because the ioctls are. It needs no elevation: the raw device opens for
/// <c>GENERIC_READ</c> as an ordinary user.</para>
///
/// <para><b>Every completed rip is cached</b> as a <c>TrackNN.wav</c> under
/// <see cref="DefaultCacheRoot"/>, in a directory named for the disc's table of contents, and a
/// cached track is read from there rather than from the disc — so a track is ripped once per
/// machine, not once per mission.</para>
/// </summary>
public sealed class CdRipMusicSource : MusicSourceBase {
	/// <summary>
	/// Sectors per <c>IOCTL_CDROM_RAW_READ</c>, the measured ceiling: 52 fails with
	/// <c>ERROR_INVALID_PARAMETER</c>. A read never runs past the track's own end, because one that
	/// crosses the lead-out fails whole. The track is read on a worker thread, and playback starts on
	/// the first block, a few tens of milliseconds in: the read runs at about 7.8x realtime from a
	/// cold drive and 20x once it has spun up. A cached track reads in about 25 ms.
	/// </summary>
	public const int MaxSectorsPerRead = 26;

	/// <summary>Bytes in one CD-DA sector: 588 stereo 16-bit frames.</summary>
	public const int RawSectorSize = 2352;

	/// <summary>How many times a failed read is retried before its sectors go in as silence.</summary>
	public const int ReadRetries = 3;

	/// <summary>Where rips are cached when the caller names nowhere else.</summary>
	public static string DefaultCacheRoot => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
			Environment.SpecialFolderOption.DoNotVerify),
		"Herculan", "cd-audio");

	private readonly string _device;
	private readonly string _drive;
	private readonly Dictionary<int, (long Lba, long Sectors)> _tracks;
	private readonly string? _cacheDirectory;

	private CdRipMusicSource(string drive, Dictionary<int, (long, long)> tracks, string discId,
			string? cacheRoot) {
		_drive = drive;
		_device = $@"\\.\{drive}";
		_tracks = tracks;
		DiscId = discId;
		_cacheDirectory = cacheRoot == null ? null : Path.Combine(cacheRoot, discId);
	}

	/// <summary>
	/// A name for the disc derived from its table of contents — every track's start and the lead-out
	/// — so that two pressings with different track layouts never share a cache directory.
	/// </summary>
	public string DiscId { get; }

	/// <summary>The audio tracks on the disc, by number.</summary>
	public IReadOnlyCollection<int> AudioTracks => _tracks.Keys;

	/// <inheritdoc />
	public override bool IsAvailable => _tracks.Count > 0;

	/// <inheritdoc />
	public override string Status =>
		$"reading {_drive} digitally, audio tracks {string.Join(",", _tracks.Keys.Order())}"
		+ (_cacheDirectory == null ? "" : $", cached in {_cacheDirectory}");

	/// <summary>
	/// Finds a disc with audio tracks and proves it can be read raw. Never throws.
	/// </summary>
	/// <param name="drive">A drive letter, or null to take the first CD drive holding audio.</param>
	/// <param name="cacheRoot">Where rips are cached, or null for no cache.</param>
	/// <param name="failure">Why there is no source, when there is none.</param>
	public static CdRipMusicSource? TryCreate(string? drive, string? cacheRoot, out string failure) {
		failure = "";
		if (!OperatingSystem.IsWindows()) {
			failure = "raw CD reads are Windows-only";
			return null;
		}

		IEnumerable<string> candidates;
		if (drive != null) {
			if (NormaliseDrive(drive) is not { } named) {
				failure = $"'{drive}' is not a drive letter";
				return null;
			}

			candidates = new[] { named };
		} else {
			candidates = DriveInfo.GetDrives()
				.Where(info => info.DriveType == DriveType.CDRom)
				.Select(info => info.Name[..2]);
		}

		var reasons = new List<string>();
		foreach (string letter in candidates) {
			if (TryOpen(letter, cacheRoot, out string reason) is { } source) {
				return source;
			}

			reasons.Add(reason);
		}

		failure = reasons.Count == 0 ? "no CD drive" : string.Join("; ", reasons);
		return null;
	}

	private static CdRipMusicSource? TryOpen(string drive, string? cacheRoot, out string failure) {
		using var handle = OpenDevice($@"\\.\{drive}");
		if (handle.IsInvalid) {
			failure = $"{drive} would not open ({Marshal.GetLastPInvokeError()})";
			return null;
		}

		var toc = new byte[CdromTocSize];
		if (!DeviceIoControl(handle, IoctlCdromReadToc, IntPtr.Zero, 0, toc, toc.Length, out _,
				IntPtr.Zero)) {
			failure = $"no readable disc in {drive} ({Marshal.GetLastPInvokeError()})";
			return null;
		}

		var tracks = ParseToc(toc, out string discId);
		if (tracks.Count == 0) {
			failure = $"the disc in {drive} has no audio tracks";
			return null;
		}

		// A drive can report a table of contents and still refuse CD-DA reads; that drive is MCI's.
		var first = tracks[tracks.Keys.Min()];
		var probe = new byte[RawSectorSize];
		if (!RawRead(handle, first.Lba, 1, probe)) {
			failure = $"{drive} refuses raw CD-DA reads ({Marshal.GetLastPInvokeError()})";
			return null;
		}

		failure = "";
		return new CdRipMusicSource(drive, tracks, discId, cacheRoot);
	}

	/// <summary>
	/// The audio tracks of a <c>CDROM_TOC</c> as (start, length) in sectors, and the disc's id. A
	/// track's length runs to the next entry's start, the last one's to the lead-out (<c>0xAA</c>).
	/// </summary>
	internal static Dictionary<int, (long Lba, long Sectors)> ParseToc(byte[] toc, out string discId) {
		int first = toc[2];
		int last = toc[3];
		var entries = new List<(int Number, bool Data, long Lba)>();
		for (int i = 0; i <= last - first + 1 && 4 + i * 8 + 8 <= toc.Length; i++) {
			int at = 4 + i * 8;
			// TRACK_DATA's Control:4 is the low nibble; its 0x4 bit marks a data track.
			bool data = (toc[at + 1] & 0x4) != 0;
			long lba = (toc[at + 5] * 60L + toc[at + 6]) * 75 + toc[at + 7] - 150;
			entries.Add((toc[at + 2], data, lba));
		}

		var tracks = new Dictionary<int, (long, long)>();
		for (int i = 0; i + 1 < entries.Count; i++) {
			if (!entries[i].Data && entries[i].Number != LeadOut) {
				tracks[entries[i].Number] = (entries[i].Lba, entries[i + 1].Lba - entries[i].Lba);
			}
		}

		string layout = string.Join(",", entries.Select(entry => $"{entry.Number}:{entry.Lba}"));
		discId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(layout)))[..16]
			.ToLowerInvariant();
		return tracks;
	}

	/// <summary>
	/// The one cache directory that holds any tracks, for a machine whose disc is not in the drive
	/// today. More than one disc's worth is ambiguous and answers null.
	/// </summary>
	public static string? SoleCachedDisc(string cacheRoot) {
		if (!Directory.Exists(cacheRoot)) {
			return null;
		}

		var discs = Directory.GetDirectories(cacheRoot)
			.Where(dir => Directory.EnumerateFiles(dir, "Track??.wav").Any())
			.ToList();
		return discs.Count == 1 ? discs[0] : null;
	}

	/// <inheritdoc />
	protected override (MusicTrack Track, Action<CancellationToken> Produce)? Prepare(int track) {
		if (!_tracks.TryGetValue(track, out var extent) || extent.Sectors <= 0) {
			return null;
		}

		var created = new MusicTrack(track, extent.Sectors * MusicTrack.FramesPerSector);

		string? cached = _cacheDirectory == null ? null : WaveFileMusicSource.PathFor(_cacheDirectory, track);
		if (cached != null && IsCachedCopy(cached, created.FrameCount)) {
			return (created, token => ReadCached(cached, created, token));
		}

		return (created, token => {
			Rip(extent.Lba, extent.Sectors, created, token);
			if (cached != null && created.IsComplete && created.DamagedSectors == 0) {
				try {
					Directory.CreateDirectory(_cacheDirectory!);
					WaveFileMusicSource.Write(cached, created);
				} catch (IOException) {
					// A cache that cannot be written costs a re-rip next time, nothing more.
				} catch (UnauthorizedAccessException) {
				}
			}
		});
	}

	private static bool IsCachedCopy(string path, long frames) {
		try {
			using var stream = File.OpenRead(path);
			return WaveFileMusicSource.TryReadHeader(stream, out long dataLength)
				&& dataLength == frames * MusicTrack.Channels * sizeof(short)
				&& stream.Length - stream.Position >= dataLength;
		} catch (IOException) {
			return false;
		}
	}

	private static void ReadCached(string path, MusicTrack track, CancellationToken token) {
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
			bufferSize: 1, FileOptions.SequentialScan);
		WaveFileMusicSource.TryReadHeader(stream, out _);
		var chunk = new byte[MaxSectorsPerRead * RawSectorSize * 4];
		while (!track.IsComplete) {
			token.ThrowIfCancellationRequested();
			int length = (int)Math.Min(chunk.Length,
				(track.FrameCount - track.FramesReady) * MusicTrack.Channels * sizeof(short));
			stream.ReadExactly(chunk, 0, length);
			track.Append(chunk.AsSpan(0, length));
		}
	}

	private void Rip(long lba, long sectors, MusicTrack track, CancellationToken token) {
		using var handle = OpenDevice(_device);
		if (handle.IsInvalid) {
			track.Fail($"{_drive} would not open ({Marshal.GetLastPInvokeError()})");
			return;
		}

		var buffer = new byte[MaxSectorsPerRead * RawSectorSize];
		long end = lba + sectors;
		while (lba < end) {
			token.ThrowIfCancellationRequested();

			// Clamped to the track, and so never past the lead-out: a read that runs off the end of
			// the disc fails whole, taking the last good sectors with it.
			int count = (int)Math.Min(MaxSectorsPerRead, end - lba);
			if (ReadWithRetries(handle, lba, count, buffer)) {
				track.Append(buffer.AsSpan(0, count * RawSectorSize));
			} else {
				// Narrow the failure to the sectors that caused it rather than silencing the whole run.
				for (int i = 0; i < count; i++) {
					token.ThrowIfCancellationRequested();
					if (ReadWithRetries(handle, lba + i, 1, buffer)) {
						track.Append(buffer.AsSpan(0, RawSectorSize));
					} else {
						track.AppendDamaged(1);
					}
				}
			}

			lba += count;
		}
	}

	private static bool ReadWithRetries(SafeFileHandle handle, long lba, int count, byte[] buffer) {
		for (int attempt = 0; attempt <= ReadRetries; attempt++) {
			if (RawRead(handle, lba, count, buffer)) {
				return true;
			}
		}

		return false;
	}

	private static unsafe bool RawRead(SafeFileHandle handle, long lba, int count, byte[] buffer) {
		// RAW_READ_INFO: the offset is in 2048-byte cooked sectors whatever the mode, then the count,
		// then TRACK_MODE_TYPE, of which CDDA is 2.
		Span<byte> info = stackalloc byte[16];
		BinaryPrimitives.WriteInt64LittleEndian(info, lba * 2048);
		BinaryPrimitives.WriteInt32LittleEndian(info[8..], count);
		BinaryPrimitives.WriteInt32LittleEndian(info[12..], TrackModeCdda);

		fixed (byte* input = info) {
			return DeviceIoControl(handle, IoctlCdromRawRead, (IntPtr)input, info.Length, buffer,
				count * RawSectorSize, out int returned, IntPtr.Zero)
				&& returned == count * RawSectorSize;
		}
	}

	private static string? NormaliseDrive(string drive) {
		string trimmed = drive.Trim().TrimEnd('\\', '/');
		if (trimmed.EndsWith(':')) {
			trimmed = trimmed[..^1];
		}

		return trimmed.Length == 1 && char.IsAsciiLetter(trimmed[0])
			? $"{char.ToUpperInvariant(trimmed[0])}:"
			: null;
	}

	private static SafeFileHandle OpenDevice(string device) =>
		CreateFileW(device, GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0,
			IntPtr.Zero);

	private const int LeadOut = 0xaa;
	private const int CdromTocSize = 804;
	private const int TrackModeCdda = 2;
	private const uint IoctlCdromReadToc = 0x00024000;
	private const uint IoctlCdromRawRead = 0x0002403e;
	private const uint GenericRead = 0x80000000;
	private const uint FileShareRead = 1;
	private const uint FileShareWrite = 2;
	private const uint OpenExisting = 3;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
	private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
		IntPtr security, uint disposition, uint flags, IntPtr template);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input,
		int inputLength, [Out] byte[] output, int outputLength, out int returned, IntPtr overlapped);
}
