using System.Buffers.Binary;
using System.Text;

namespace HercWorks.Disc.Tests;

/// <summary>
/// Builds small ISO 9660 images and CD sectors for the tests: a cooked image from a set of files, its
/// sectors re-framed raw in any <see cref="TrackFormat"/>, and recognisable audio.
/// </summary>
internal static class TestImages {
	public const int Sector = 2048;

	/// <summary>The fixed recording time every record carries: 1996-09-01 12:34:56 at UTC+1.</summary>
	public static readonly DateTimeOffset RecordTime = new(1996, 9, 1, 12, 34, 56, TimeSpan.FromHours(1));

	/// <summary>
	/// A cooked image holding <paramref name="files"/> ('/'-separated paths), with each directory in one
	/// sector. The primary tree's names are upper-cased; with <paramref name="joliet"/> a Joliet tree keeps
	/// them as given.
	/// </summary>
	public static byte[] BuildIso(IReadOnlyDictionary<string, byte[]> files, bool joliet = false, string volumeId = "TESTDISC") {
		var primaryRoot = new Dir("", null);
		var jolietRoot = new Dir("", null);
		foreach (var (path, _) in files.OrderBy(f => f.Key, StringComparer.Ordinal)) {
			string[] parts = path.Split('/');
			var primary = primaryRoot;
			var joli = jolietRoot;
			for (int i = 0; i < parts.Length - 1; i++) {
				primary = primary.Sub(parts[i].ToUpperInvariant());
				joli = joli.Sub(parts[i]);
			}

			primary.Files.Add((parts[^1].ToUpperInvariant() + ";1", path));
			joli.Files.Add((parts[^1] + ";1", path));
		}

		// Layout: 16 PVD, 17 SVD (Joliet), then terminator, then the directories, then the files.
		int next = joliet ? 19 : 18;
		var primaryDirs = primaryRoot.All().ToList();
		var jolietDirs = joliet ? jolietRoot.All().ToList() : new List<Dir>();
		foreach (var dir in primaryDirs.Concat(jolietDirs)) {
			dir.Lba = next++;
		}

		var fileLba = new Dictionary<string, int>();
		foreach (var (path, data) in files.OrderBy(f => f.Key, StringComparer.Ordinal)) {
			fileLba[path] = next;
			next += Math.Max(1, (data.Length + Sector - 1) / Sector);
		}

		byte[] image = new byte[next * Sector];
		WriteDescriptor(image.AsSpan(16 * Sector, Sector), 1, volumeId, primaryRoot.Lba, next, joliet: false);
		if (joliet) {
			WriteDescriptor(image.AsSpan(17 * Sector, Sector), 2, volumeId, jolietRoot.Lba, next, joliet: true);
		}

		var terminator = image.AsSpan((joliet ? 18 : 17) * Sector, Sector);
		terminator[0] = 0xff;
		"CD001"u8.CopyTo(terminator[1..]);
		terminator[6] = 1;

		foreach (var dir in primaryDirs) {
			WriteDirectory(image, dir, fileLba, files, joliet: false);
		}

		foreach (var dir in jolietDirs) {
			WriteDirectory(image, dir, fileLba, files, joliet: true);
		}

		foreach (var (path, data) in files) {
			data.CopyTo(image, fileLba[path] * Sector);
		}

		return image;
	}

	/// <summary>Re-frames a cooked image's sectors in <paramref name="format"/>, with sync and header where it has them.</summary>
	public static byte[] ToFormat(byte[] cooked, TrackFormat format, int firstLba = 0) {
		if (format == TrackFormat.Mode1Cooked) {
			return cooked;
		}

		int size = format.SectorSize();
		int offset = format.UserDataOffset();
		int count = cooked.Length / Sector;
		byte[] raw = new byte[count * size];
		for (int i = 0; i < count; i++) {
			var sector = raw.AsSpan(i * size, size);
			if (format is TrackFormat.Mode1Raw or TrackFormat.Mode2Raw) {
				WriteHeader(sector, firstLba + i, format == TrackFormat.Mode1Raw ? (byte)1 : (byte)2);
			}

			if (format is TrackFormat.Mode2Raw or TrackFormat.Mode2Xa) {
				// XA subheader, Form 1 data, written twice.
				var subheader = sector.Slice(offset - 8, 8);
				subheader[2] = subheader[6] = 0x08;
			}

			cooked.AsSpan(i * Sector, Sector).CopyTo(sector[offset..]);
		}

		return raw;
	}

	/// <summary>Raw data sectors with a sync and header but no user data: a data track's postgap.</summary>
	public static byte[] EmptyRawData(int count, int firstLba) {
		byte[] raw = new byte[count * DiscImage.RawSectorSize];
		for (int i = 0; i < count; i++) {
			WriteHeader(raw.AsSpan(i * DiscImage.RawSectorSize), firstLba + i, 1);
		}

		return raw;
	}

	/// <summary>
	/// <paramref name="sectors"/> of audio for track <paramref name="track"/>: each sample word encodes the
	/// track and its index, so a read can be checked byte for byte. Sector 0 of a track's pattern is the
	/// first sector the file holds for it.
	/// </summary>
	public static byte[] Audio(int track, int sectors) {
		byte[] pcm = new byte[sectors * DiscImage.RawSectorSize];
		for (int i = 0; i < pcm.Length; i += 2) {
			BinaryPrimitives.WriteUInt16LittleEndian(pcm.AsSpan(i), (ushort)(track * 0x1000 + i / 2 % 0x1000));
		}

		return pcm;
	}

	public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

	private static void WriteHeader(Span<byte> sector, int lba, byte mode) {
		sector[0] = 0;
		sector.Slice(1, 10).Fill(0xff);
		sector[11] = 0;
		int frames = lba + 150;
		sector[12] = Bcd(frames / (75 * 60));
		sector[13] = Bcd(frames / 75 % 60);
		sector[14] = Bcd(frames % 75);
		sector[15] = mode;
	}

	private static byte Bcd(int value) => (byte)(value / 10 * 16 + value % 10);

	private static void WriteDescriptor(Span<byte> d, byte type, string volumeId, int rootLba, int volumeSize, bool joliet) {
		d[0] = type;
		"CD001"u8.CopyTo(d[1..]);
		d[6] = 1;
		byte[] id = joliet
			? Encoding.BigEndianUnicode.GetBytes(volumeId.PadRight(16))
			: Encoding.ASCII.GetBytes(volumeId.PadRight(32));
		id.CopyTo(d[40..]);
		Both32(d[80..], volumeSize);
		if (joliet) {
			"%/E"u8.CopyTo(d[88..]);
		}

		Both16(d[120..], 1);
		Both16(d[124..], 1);
		Both16(d[128..], Sector);
		WriteRecord(d[156..], new byte[] { 0 }, rootLba, Sector, directory: true);
	}

	private static void WriteDirectory(byte[] image, Dir dir, Dictionary<string, int> fileLba,
			IReadOnlyDictionary<string, byte[]> files, bool joliet) {
		var span = image.AsSpan(dir.Lba * Sector, Sector);
		int pos = 0;
		pos += WriteRecord(span[pos..], new byte[] { 0 }, dir.Lba, Sector, directory: true);
		pos += WriteRecord(span[pos..], new byte[] { 1 }, (dir.Parent ?? dir).Lba, Sector, directory: true);

		var entries = dir.Subs.Select(s => (Name: s.Name, Lba: s.Lba, Length: Sector, Directory: true))
			.Concat(dir.Files.Select(f => (Name: f.Name, Lba: fileLba[f.Path], Length: files[f.Path].Length, Directory: false)))
			.OrderBy(e => e.Name, StringComparer.Ordinal);
		foreach (var entry in entries) {
			byte[] name = joliet ? Encoding.BigEndianUnicode.GetBytes(entry.Name) : Encoding.ASCII.GetBytes(entry.Name);
			pos += WriteRecord(span[pos..], name, entry.Lba, entry.Length, entry.Directory);
		}
	}

	/// <summary>Writes one directory record and returns its length.</summary>
	public static int WriteRecord(Span<byte> r, byte[] name, int lba, int length, bool directory) {
		int recordLength = 33 + name.Length + (name.Length % 2 == 0 ? 1 : 0);
		r[..recordLength].Clear();
		r[0] = (byte)recordLength;
		Both32(r[2..], lba);
		Both32(r[10..], length);
		r[18] = (byte)(RecordTime.Year - 1900);
		r[19] = (byte)RecordTime.Month;
		r[20] = (byte)RecordTime.Day;
		r[21] = (byte)RecordTime.Hour;
		r[22] = (byte)RecordTime.Minute;
		r[23] = (byte)RecordTime.Second;
		r[24] = (byte)(RecordTime.Offset.TotalMinutes / 15);
		r[25] = directory ? (byte)2 : (byte)0;
		Both16(r[28..], 1);
		r[32] = (byte)name.Length;
		name.CopyTo(r[33..]);
		return recordLength;
	}

	private static void Both32(Span<byte> s, int value) {
		BinaryPrimitives.WriteInt32LittleEndian(s, value);
		BinaryPrimitives.WriteInt32BigEndian(s[4..], value);
	}

	private static void Both16(Span<byte> s, int value) {
		BinaryPrimitives.WriteInt16LittleEndian(s, (short)value);
		BinaryPrimitives.WriteInt16BigEndian(s[2..], (short)value);
	}

	private sealed class Dir {
		public Dir(string name, Dir? parent) {
			Name = name;
			Parent = parent;
		}

		public string Name { get; }
		public Dir? Parent { get; }
		public int Lba { get; set; }
		public List<Dir> Subs { get; } = new();
		public List<(string Name, string Path)> Files { get; } = new();

		public Dir Sub(string name) {
			var sub = Subs.FirstOrDefault(s => s.Name == name);
			if (sub == null) {
				sub = new Dir(name, this);
				Subs.Add(sub);
			}

			return sub;
		}

		public IEnumerable<Dir> All() => new[] { this }.Concat(Subs.SelectMany(s => s.All()));
	}
}
