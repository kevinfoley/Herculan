using System.Buffers;
using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace HercWorks.Disc;

/// <summary>
/// A CD image read in place: a plain <c>.iso</c> of 2,048-byte sectors, a raw image of 2,352-byte
/// (or 2,336-byte) sectors, or a cue sheet and the <c>.bin</c> files it names, data and Red Book audio
/// tracks alike. <see cref="OpenFileSystem"/> reads the data track's ISO 9660 file system and
/// <see cref="OpenAudioTrack"/> an audio track's PCM, so nothing has to be mounted.
///
/// <para>Addresses are disc LBAs, track 1's <c>INDEX 01</c> being 0, which is what ISO 9660's extent
/// addresses count from on a single-session disc. Every read is positional
/// (<see cref="RandomAccess"/>), so one image serves several threads at once.</para>
///
/// <para>The image is untrusted input. A cue sheet's <c>FILE</c> names are reduced to a bare file name
/// opened beside the sheet (see <see cref="ResolveCueFile"/>), every size and address taken from the
/// image is bounds-checked before it is used, and nothing read from it is ever executed or written.</para>
/// </summary>
public sealed class DiscImage : IDisposable {
	/// <summary>Bytes of user data in a data sector.</summary>
	public const int UserDataSize = 2048;

	/// <summary>Bytes in a raw sector, and in an audio sector.</summary>
	public const int RawSectorSize = 2352;

	/// <summary>Sectors a CD plays per second.</summary>
	public const int SectorsPerSecond = 75;

	/// <summary>Red Book audio: 44,100 frames a second...</summary>
	public const int AudioSampleRate = 44100;

	/// <summary>...of two channels...</summary>
	public const int AudioChannels = 2;

	/// <summary>...of signed 16-bit samples, little-endian as <see cref="OpenAudioTrack"/> returns them.</summary>
	public const int AudioBitsPerSample = 16;

	/// <summary>
	/// The most sectors an image may span: well past a dual-layer DVD, and small enough that every byte
	/// offset computed from an address fits a <see cref="long"/> with room to spare.
	/// </summary>
	public const int MaxSectors = 1 << 26;

	/// <summary>The volume descriptor set starts at sector 16 of the data track.</summary>
	internal const int VolumeDescriptorStart = 16;

	/// <summary>Raw sectors read in one call when copying user data out of them.</summary>
	private const int RawBatchSectors = 32;

	private static readonly byte[] Sync = { 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 };

	private readonly List<SafeFileHandle> _handles;
	private readonly SectorRun[] _runs;
	private bool _disposed;

	private DiscImage(string path, List<SafeFileHandle> handles, List<SectorRun> runs, List<DiscTrack> tracks) {
		Path = path;
		_handles = handles;
		_runs = runs.ToArray();
		Tracks = tracks;
		FirstLba = _runs[0].FirstLba;
		EndLba = _runs[^1].EndLba;
	}

	/// <summary>The file opened: the cue sheet, or the image itself when there is none.</summary>
	public string Path { get; }

	/// <summary>The tracks, in order.</summary>
	public IReadOnlyList<DiscTrack> Tracks { get; }

	/// <summary>The lowest address the image holds; below 0 when track 1 has a pregap.</summary>
	public int FirstLba { get; }

	/// <summary>One past the highest address the image holds: the lead-out.</summary>
	public int EndLba { get; }

	/// <summary>The first data track, which holds the ISO 9660 file system; null for an audio-only disc.</summary>
	public DiscTrack? DataTrack => Tracks.FirstOrDefault(track => !track.IsAudio);

	/// <summary>
	/// Opens an image. A <c>.cue</c> is read as a cue sheet. Any other file is read as a single-track
	/// image, unless a cue sheet of the same name beside it names it in a <c>FILE</c>, in which case
	/// that sheet is opened instead.
	///
	/// <para>A single file's sector size comes from its content: the CD sync pattern makes it raw, and
	/// the <c>CD001</c> volume descriptor at sector 16 places the user data. A raw image may carry audio
	/// after its data track (some images hold a whole mixed-mode disc in one file); without a cue sheet
	/// that audio has no track boundaries and is returned as one <see cref="DiscTrack.BoundaryUnknown"/>
	/// track.</para>
	/// </summary>
	/// <exception cref="DiscFormatException">The image or cue sheet is malformed or unsupported.</exception>
	public static DiscImage Open(string path) {
		string full = System.IO.Path.GetFullPath(path);
		if (string.Equals(System.IO.Path.GetExtension(full), ".cue", StringComparison.OrdinalIgnoreCase)) {
			return OpenCue(full);
		}

		string sibling = System.IO.Path.ChangeExtension(full, ".cue");
		if (File.Exists(sibling) && ReadCueSheet(sibling).Files
				.Any(file => string.Equals(CueFileLeaf(file.Name), System.IO.Path.GetFileName(full), StringComparison.OrdinalIgnoreCase))) {
			return OpenCue(sibling);
		}

		return OpenSingleFile(full);
	}

	/// <summary>Reads the ISO 9660 file system on the data track.</summary>
	/// <param name="preferJoliet">
	/// Use the Joliet tree when the disc has one, as Windows does when it mounts a disc: its names keep
	/// their case and may be long. Otherwise, and on a disc without one, the primary tree's names are used,
	/// which are what DOS sees.
	/// </param>
	/// <exception cref="DiscFormatException">There is no data track, or its file system is malformed.</exception>
	public Iso9660.IsoFileSystem OpenFileSystem(bool preferJoliet = true) =>
		Iso9660.IsoFileSystem.Read(this, preferJoliet);

	/// <summary>
	/// Opens audio track <paramref name="trackNumber"/> as a seekable stream of raw PCM
	/// (<see cref="AudioSampleRate"/>, <see cref="AudioChannels"/>, <see cref="AudioBitsPerSample"/>,
	/// little-endian), from its <c>INDEX 01</c> for <see cref="DiscTrack.SectorCount"/> sectors.
	/// </summary>
	/// <exception cref="ArgumentException">There is no such track, or it is a data track.</exception>
	public Stream OpenAudioTrack(int trackNumber) {
		var track = Tracks.FirstOrDefault(t => t.Number == trackNumber)
			?? throw new ArgumentException($"The image has no track {trackNumber}.", nameof(trackNumber));
		if (!track.IsAudio) {
			throw new ArgumentException($"Track {trackNumber} is a data track.", nameof(trackNumber));
		}

		return new SectorStream(this, track.StartLba, (long)track.SectorCount * RawSectorSize, audio: true);
	}

	/// <summary>
	/// Reads the 2,048-byte user data of <paramref name="count"/> sectors from <paramref name="lba"/> into
	/// <paramref name="destination"/>. A gap the image does not hold (a cue sheet's <c>PREGAP</c> or
	/// <c>POSTGAP</c>) reads as zeros.
	/// </summary>
	/// <exception cref="DiscFormatException">A sector is outside the image or on an audio track.</exception>
	public void ReadSectors(int lba, int count, Span<byte> destination) =>
		Read(lba, count, destination, audio: false);

	/// <summary>
	/// Reads <paramref name="count"/> audio sectors from <paramref name="lba"/> into
	/// <paramref name="destination"/> as little-endian PCM, 2,352 bytes each. A sector that is not audio
	/// (a data track, or a gap the image does not hold) reads as silence.
	/// </summary>
	/// <exception cref="DiscFormatException">A sector is outside the image.</exception>
	public void ReadAudioSectors(int lba, int count, Span<byte> destination) =>
		Read(lba, count, destination, audio: true);

	private void Read(int lba, int count, Span<byte> destination, bool audio) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		int outSize = audio ? RawSectorSize : UserDataSize;
		if (count < 0 || (long)count * outSize > destination.Length) {
			throw new ArgumentOutOfRangeException(nameof(count));
		}

		while (count > 0) {
			var run = FindRun(lba)
				?? throw new DiscFormatException($"Sector {lba} is outside the image (sectors {FirstLba} to {EndLba - 1}).");
			int take = (int)Math.Min(count, (long)run.EndLba - lba);
			var output = destination[..(take * outSize)];
			bool isAudio = run.Format == TrackFormat.Audio;

			if (!audio && isAudio) {
				throw new DiscFormatException($"Sector {lba} is on an audio track.");
			}

			if (audio != isAudio || run.Handle == null) {
				output.Clear();
			} else {
				ReadFromRun(run, lba, take, output, audio);
			}

			destination = destination[(take * outSize)..];
			lba += take;
			count -= take;
		}
	}

	private static void ReadFromRun(SectorRun run, int lba, int count, Span<byte> output, bool audio) {
		int sectorSize = run.Format.SectorSize();
		int dataOffset = run.Format.UserDataOffset();
		int dataSize = audio ? RawSectorSize : UserDataSize;
		long offset = run.FileOffset + (long)(lba - run.FirstLba) * sectorSize;

		if (sectorSize == dataSize) {
			ReadExactly(run.Handle!, output, offset);
		} else {
			byte[] buffer = ArrayPool<byte>.Shared.Rent(RawBatchSectors * sectorSize);
			try {
				for (int done = 0; done < count;) {
					int batch = Math.Min(RawBatchSectors, count - done);
					ReadExactly(run.Handle!, buffer.AsSpan(0, batch * sectorSize), offset + (long)done * sectorSize);
					for (int i = 0; i < batch; i++) {
						buffer.AsSpan(i * sectorSize + dataOffset, dataSize).CopyTo(output[((done + i) * dataSize)..]);
					}

					done += batch;
				}
			} finally {
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}

		if (audio && run.BigEndian) {
			for (int i = 0; i + 1 < output.Length; i += 2) {
				(output[i], output[i + 1]) = (output[i + 1], output[i]);
			}
		}
	}

	private static void ReadExactly(SafeFileHandle handle, Span<byte> destination, long offset) {
		while (!destination.IsEmpty) {
			int read = RandomAccess.Read(handle, destination, offset);
			if (read <= 0) {
				throw new EndOfStreamException("The image file ended early; it may have been truncated since it was opened.");
			}

			destination = destination[read..];
			offset += read;
		}
	}

	private SectorRun? FindRun(int lba) {
		int low = 0, high = _runs.Length - 1;
		while (low <= high) {
			int mid = low + (high - low) / 2;
			var run = _runs[mid];
			if (lba < run.FirstLba) {
				high = mid - 1;
			} else if (lba >= run.EndLba) {
				low = mid + 1;
			} else {
				return run;
			}
		}

		return null;
	}

	/// <inheritdoc />
	public void Dispose() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		foreach (var handle in _handles) {
			handle.Dispose();
		}
	}

	// ---- Single file ----

	private static DiscImage OpenSingleFile(string path) {
		var handle = OpenImageFile(path);
		try {
			long length = RandomAccess.GetLength(handle);
			var format = ProbeFormat(handle, length)
				?? throw new DiscFormatException($"{path} holds no ISO 9660 volume descriptor at sector 16 in any sector size read.");

			long sectors = length / format.SectorSize();
			if (sectors > MaxSectors) {
				throw new DiscFormatException($"{path} spans {sectors} sectors; the most read is {MaxSectors}.");
			}

			int total = (int)sectors;
			int dataEnd = format.SectorSize() == RawSectorSize ? FindDataTrackEnd(handle, format, total) : total;

			var runs = new List<SectorRun> { new(0, dataEnd, handle, 0, format, false) };
			var tracks = new List<DiscTrack> { new(1, format, 0, dataEnd, 0, false) };
			if (dataEnd < total) {
				runs.Add(new SectorRun(dataEnd, total - dataEnd, handle, (long)dataEnd * RawSectorSize, TrackFormat.Audio, false));
				tracks.Add(new DiscTrack(2, TrackFormat.Audio, dataEnd, total - dataEnd, 0, true));
			}

			return new DiscImage(path, new List<SafeFileHandle> { handle }, runs, tracks);
		} catch {
			handle.Dispose();
			throw;
		}
	}

	/// <summary>
	/// The single-file sector format whose sector 16 carries an ISO 9660 volume descriptor, trying 2,048,
	/// then raw 2,352 (mode from the header), then 2,336; null when none does.
	/// </summary>
	private static TrackFormat? ProbeFormat(SafeFileHandle handle, long length) {
		Span<byte> probe = stackalloc byte[RawSectorSize];

		bool HasDescriptor(TrackFormat format, Span<byte> sector) {
			long offset = (long)VolumeDescriptorStart * format.SectorSize();
			if (length < offset + format.SectorSize()) {
				return false;
			}

			var bytes = sector[..format.SectorSize()];
			ReadExactly(handle, bytes, offset);
			return Iso9660.IsoFileSystem.IsVolumeDescriptor(bytes[format.UserDataOffset()..]);
		}

		if (HasDescriptor(TrackFormat.Mode1Cooked, probe)) {
			return TrackFormat.Mode1Cooked;
		}

		long rawOffset = (long)VolumeDescriptorStart * RawSectorSize;
		if (length >= rawOffset + RawSectorSize) {
			ReadExactly(handle, probe, rawOffset);
			if (probe[..Sync.Length].SequenceEqual(Sync)) {
				var format = probe[15] switch {
					1 => TrackFormat.Mode1Raw,
					2 => TrackFormat.Mode2Raw,
					_ => (TrackFormat?)null,
				};
				if (format is { } raw && HasDescriptor(raw, probe)) {
					return raw;
				}
			}
		}

		return HasDescriptor(TrackFormat.Mode2Xa, probe) ? TrackFormat.Mode2Xa : null;
	}

	/// <summary>
	/// Where a raw single-file image's data track ends: the first sector, from the end of the ISO 9660
	/// volume on, without the CD sync pattern. Every data sector, the track's postgap included, carries
	/// one; an audio sector is PCM.
	/// </summary>
	private static int FindDataTrackEnd(SafeFileHandle handle, TrackFormat format, int total) {
		Span<byte> descriptor = stackalloc byte[UserDataSize];
		ReadExactly(handle, descriptor, (long)VolumeDescriptorStart * RawSectorSize + format.UserDataOffset());
		uint volumeSize = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[Iso9660.IsoFileSystem.VolumeSpaceSizeOffset..]);

		// Sector 16 is known to be data; a volume size below that is nonsense, and starting past it
		// keeps a bogus one from leaving the data track empty.
		int lba = (int)Math.Clamp(volumeSize, (uint)VolumeDescriptorStart + 1, (uint)total);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(RawBatchSectors * RawSectorSize);
		try {
			while (lba < total) {
				int batch = Math.Min(RawBatchSectors, total - lba);
				ReadExactly(handle, buffer.AsSpan(0, batch * RawSectorSize), (long)lba * RawSectorSize);
				for (int i = 0; i < batch; i++, lba++) {
					if (!buffer.AsSpan(i * RawSectorSize, Sync.Length).SequenceEqual(Sync)) {
						return lba;
					}
				}
			}
		} finally {
			ArrayPool<byte>.Shared.Return(buffer);
		}

		return total;
	}

	// ---- Cue sheet ----

	private static CueSheet ReadCueSheet(string path) {
		using var handle = OpenImageFile(path);
		long length = RandomAccess.GetLength(handle);
		if (length > CueSheet.MaxBytes) {
			throw new DiscFormatException($"{path} is {length} bytes; the largest cue sheet read is {CueSheet.MaxBytes}.");
		}

		byte[] bytes = new byte[length];
		ReadExactly(handle, bytes, 0);
		return CueSheet.Parse(bytes);
	}

	private static DiscImage OpenCue(string cuePath) {
		var sheet = ReadCueSheet(cuePath);
		string directory = System.IO.Path.GetDirectoryName(cuePath)!;
		var handles = new List<SafeFileHandle>();
		try {
			var runs = new List<SectorRun>();
			var starts = new List<(CueSheet.CueTrack Cue, int StartLba, int Pregap)>();
			long lba = 0;

			foreach (var file in sheet.Files) {
				string path = ResolveCueFile(directory, file.Name);
				var handle = OpenImageFile(path);
				handles.Add(handle);
				long fileLength = RandomAccess.GetLength(handle);
				long byteOffset = 0;

				for (int i = 0; i < file.Tracks.Count; i++) {
					var track = file.Tracks[i];
					int sectorSize = track.Format.SectorSize();
					if (i == 0) {
						byteOffset = (long)track.FirstSector * sectorSize;
					} else {
						var previous = file.Tracks[i - 1];
						byteOffset += (long)(track.FirstSector - previous.FirstSector) * previous.Format.SectorSize();
					}

					long sectors = i + 1 < file.Tracks.Count
						? file.Tracks[i + 1].FirstSector - track.FirstSector
						: (fileLength - byteOffset) / sectorSize;
					int inFilePregap = track.Index1 - track.FirstSector;
					if (byteOffset + sectors * sectorSize > fileLength || sectors <= inFilePregap) {
						throw new DiscFormatException(
							$"{System.IO.Path.GetFileName(path)} ends before track {track.Number}'s INDEX 01.");
					}

					if (track.Pregap > 0) {
						runs.Add(new SectorRun(Checked(lba), track.Pregap, null, 0, track.Format, false));
						lba += track.Pregap;
					}

					starts.Add((track, Checked(lba + inFilePregap), track.Pregap + inFilePregap));
					runs.Add(new SectorRun(Checked(lba), Checked(sectors), handle, byteOffset, track.Format, file.BigEndian));
					lba += sectors;

					if (track.Postgap > 0) {
						runs.Add(new SectorRun(Checked(lba), track.Postgap, null, 0, track.Format, false));
						lba += track.Postgap;
					}

					Checked(lba);
				}
			}

			// Disc addresses count from track 1's INDEX 01; its pregap, if any, sits below 0.
			int origin = starts[0].StartLba;
			int end = (int)lba;
			var shifted = runs.Select(run => run with { FirstLba = run.FirstLba - origin }).ToList();
			var tracks = starts.Select((start, i) => new DiscTrack(
				start.Cue.Number,
				start.Cue.Format,
				start.StartLba - origin,
				(i + 1 < starts.Count ? starts[i + 1].StartLba : end) - start.StartLba,
				start.Pregap,
				false)).ToList();

			return new DiscImage(cuePath, handles, shifted, tracks);
		} catch {
			foreach (var handle in handles) {
				handle.Dispose();
			}

			throw;
		}
	}

	private static int Checked(long sectors) =>
		sectors is >= 0 and <= MaxSectors
			? (int)sectors
			: throw new DiscFormatException($"The cue sheet lays out more than {MaxSectors} sectors.");

	/// <summary>
	/// The last path segment of a cue sheet's <c>FILE</c> name. Tools often write an absolute path, or one
	/// relative to where they ran, for a file that sits beside the sheet; only the name is kept.
	/// </summary>
	private static string CueFileLeaf(string name) {
		int separator = name.LastIndexOfAny(new[] { '/', '\\' });
		return separator < 0 ? name : name[(separator + 1)..];
	}

	/// <summary>
	/// The file a cue sheet's <c>FILE</c> command names, which is always a plain file in the sheet's own
	/// directory: any directory part the sheet gives is dropped (<see cref="CueFileLeaf"/>), so a sheet
	/// cannot reach a file elsewhere on the machine or on a network share, and a name that is not a
	/// portable file name — a device such as <c>CON</c>, an NTFS stream (<c>a.bin:x</c>), a wildcard — is
	/// refused.
	/// </summary>
	internal static string ResolveCueFile(string cueDirectory, string name) {
		string leaf = CueFileLeaf(name).TrimEnd(' ', '.');
		if (leaf.Length is 0 or > 255 || leaf is "." or ".."
				|| leaf.Any(c => c < ' ' || c is ':' or '*' or '?' or '"' or '<' or '>' or '|')
				|| leaf.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
				|| IsReservedDeviceName(leaf)) {
			throw new DiscFormatException($"The cue sheet's FILE \"{name}\" is not a file name this reader opens.");
		}

		string root = System.IO.Path.GetFullPath(cueDirectory);
		string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, leaf));
		if (!string.Equals(System.IO.Path.GetDirectoryName(full), System.IO.Path.TrimEndingDirectorySeparator(root),
				StringComparison.OrdinalIgnoreCase)) {
			throw new DiscFormatException($"The cue sheet's FILE \"{name}\" resolves outside the sheet's directory.");
		}

		if (!File.Exists(full)) {
			throw new FileNotFoundException($"The cue sheet names {leaf}, which is not beside it.", full);
		}

		if (new FileInfo(full).LinkTarget != null) {
			throw new DiscFormatException($"The cue sheet's FILE \"{name}\" is a link; only a plain file beside the sheet is opened.");
		}

		return full;
	}

	/// <summary>Windows device names, which open a device rather than a file whatever the extension.</summary>
	private static bool IsReservedDeviceName(string leaf) {
		string stem = leaf.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
		if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" or "CLOCK$") {
			return true;
		}

		return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
			&& (char.IsAsciiDigit(stem[3]) || stem[3] is '¹' or '²' or '³');
	}

	private static SafeFileHandle OpenImageFile(string path) {
		if (Directory.Exists(path)) {
			throw new DiscFormatException($"{path} is a directory.");
		}

		return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
	}

	/// <summary>
	/// A stretch of consecutive disc addresses stored one way: in one file from <see cref="FileOffset"/>, or
	/// not at all (<see cref="Handle"/> null) for a gap the cue sheet adds.
	/// </summary>
	private sealed record SectorRun(int FirstLba, int Count, SafeFileHandle? Handle, long FileOffset, TrackFormat Format,
			bool BigEndian) {
		public int EndLba => FirstLba + Count;
	}
}
