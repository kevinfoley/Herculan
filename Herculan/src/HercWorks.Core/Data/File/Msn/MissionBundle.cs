using System.IO.Compression;
using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// A mission bundle, a <c>.hcm</c> file: HERCULAN's own way to pass a custom mission round as one file, which retail has no
/// part in. It is a zip archive holding, at its top level, one <c>.MSN</c>, the mission's text under the same stem in one
/// or more of the languages the shell reads, optionally its about file under the same stem (<see cref="MissionAbout"/>), and
/// optionally the header and heightmap of the mission's own zone under the same stem (<see cref="ZoneFiles"/>): the files a
/// saved mission keeps in the install, as they are there. Any other file in it is ignored.
///
/// <para>A bundle comes from anyone, so the reader trusts nothing in it. It reads the archive into memory, at most
/// <see cref="MaxFileSize"/>, and extracts nothing to disk, so no entry's name ever reaches a path. It refuses an archive of
/// more than <see cref="MaxEntries"/> entries, a file in a folder, two entries of one name, a text, about or zone file not
/// named for the mission, an entry that claims or inflates to more than <see cref="MaxEntrySize"/>
/// (<see cref="MissionAbout.MaxSize"/> for the about file, <see cref="ZoneFiles.MaxHeightmapSize"/> for each zone file), one
/// zone file without the other, a zone <see cref="ZoneFiles.Read"/> refuses, and any archive the zip reader cannot read; it
/// inflates only the entries it takes, so the cap on each bounds the whole read.</para>
/// </summary>
public sealed class MissionBundle {
	/// <summary>The bundle's file extension.</summary>
	public const string Extension = ".hcm";

	/// <summary>The mission file's extension inside a bundle.</summary>
	public const string MissionExtension = ".MSN";

	/// <summary>The largest bundle file read, in bytes.</summary>
	public const int MaxFileSize = 16 * 1024 * 1024;

	/// <summary>The most entries a bundle may hold, folders and ignored files included.</summary>
	public const int MaxEntries = 32;

	/// <summary>The largest mission or text file read from a bundle, in bytes, inflated.</summary>
	public const int MaxEntrySize = 4 * 1024 * 1024;

	private readonly Dictionary<string, byte[]> _texts;

	private MissionBundle(string name, byte[] msn, Dictionary<string, byte[]> texts, MissionAbout? about, ZoneFiles? zone) {
		Name = name;
		Msn = msn;
		_texts = texts;
		About = about;
		Zone = zone;
	}

	/// <summary>The mission's name: its <c>.MSN</c>'s file name without the extension, as the bundle spells it.</summary>
	public string Name { get; }

	/// <summary>The mission file.</summary>
	public byte[] Msn { get; }

	/// <summary>The text extensions the bundle holds the mission's text in, upper case with their dots.</summary>
	public IReadOnlyCollection<string> TextExtensions => _texts.Keys;

	/// <summary>What the mission's about file says, or null when the bundle has none.</summary>
	public MissionAbout? About { get; }

	/// <summary>The mission's own zone, or null when the bundle has none.</summary>
	public ZoneFiles? Zone { get; }

	/// <summary>The mission's text under <paramref name="extension"/> (with its dot, any case), or null when the bundle has none.</summary>
	public byte[]? Text(string extension) => _texts.GetValueOrDefault(extension.ToUpperInvariant());

	/// <summary>
	/// The bundle at <paramref name="path"/>, or null with the reason it is not one. <paramref name="textExtensions"/> are the
	/// extensions, with their dots, the mission's text may take; the bundle has to hold at least one of them.
	/// </summary>
	public static MissionBundle? Read(string path, IReadOnlyCollection<string> textExtensions, out string? failure) =>
		ReadFile(path, out failure) is { } bytes ? Read(bytes, textExtensions, out failure) : null;

	/// <summary>
	/// The bundle file at <paramref name="path"/>, read whole, or null with the reason: it is missing, cannot be read, or is
	/// larger than <see cref="MaxFileSize"/>. The size is checked as the bytes are read, not taken from the directory.
	/// </summary>
	public static byte[]? ReadFile(string path, out string? failure) {
		try {
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (ReadCapped(stream, MaxFileSize) is { } bytes) {
				failure = null;
				return bytes;
			}

			failure = $"{Path.GetFileName(path)} is larger than {MaxFileSize / (1024 * 1024)} MB.";
			return null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			failure = ex.Message;
			return null;
		}
	}

	/// <summary>The bundle <paramref name="bundle"/>, the file's bytes, or null with the reason; as <see cref="Read(string, IReadOnlyCollection{string}, out string?)"/>.</summary>
	public static MissionBundle? Read(byte[] bundle, IReadOnlyCollection<string> textExtensions, out string? failure) {
		if (bundle.Length > MaxFileSize) {
			failure = $"The bundle is larger than {MaxFileSize / (1024 * 1024)} MB.";
			return null;
		}

		try {
			using var archive = new ZipArchive(new MemoryStream(bundle, writable: false), ZipArchiveMode.Read);
			return Read(archive, textExtensions, out failure);
		} catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException) {
			failure = $"The bundle is not a readable zip archive: {ex.Message}";
			return null;
		}
	}

	private static MissionBundle? Read(ZipArchive archive, IReadOnlyCollection<string> textExtensions, out string? failure) {
		var entries = archive.Entries;
		if (entries.Count > MaxEntries) {
			failure = $"The bundle holds {entries.Count} files, more than the {MaxEntries} a bundle may hold.";
			return null;
		}

		ZipArchiveEntry? mission = null;
		ZipArchiveEntry? about = null;
		ZipArchiveEntry? zoneHeader = null;
		ZipArchiveEntry? zoneHeightmap = null;
		var texts = new List<(string Extension, ZipArchiveEntry Entry)>();
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in entries) {
			string name = entry.FullName;
			if (name.Contains('/') || name.Contains('\\')) {
				failure = $"The bundle holds {name}, in a folder; its files belong at its top level.";
				return null;
			}

			if (name.Length == 0 || !names.Add(name)) {
				failure = $"The bundle holds two files named {name}.";
				return null;
			}

			string extension = Path.GetExtension(name);
			if (extension.Equals(MissionExtension, StringComparison.OrdinalIgnoreCase)) {
				if (mission != null) {
					failure = $"The bundle holds two missions, {mission.FullName} and {name}.";
					return null;
				}

				mission = entry;
			} else if (extension.Equals(MissionAbout.Extension, StringComparison.OrdinalIgnoreCase)) {
				if (about != null) {
					failure = $"The bundle holds two about files, {about.FullName} and {name}.";
					return null;
				}

				about = entry;
			} else if (extension.Equals(ZoneFiles.HeaderExtension, StringComparison.OrdinalIgnoreCase)) {
				if (zoneHeader != null) {
					failure = $"The bundle holds two zone headers, {zoneHeader.FullName} and {name}.";
					return null;
				}

				zoneHeader = entry;
			} else if (extension.Equals(ZoneFiles.HeightmapExtension, StringComparison.OrdinalIgnoreCase)) {
				if (zoneHeightmap != null) {
					failure = $"The bundle holds two zone heightmaps, {zoneHeightmap.FullName} and {name}.";
					return null;
				}

				zoneHeightmap = entry;
			} else if (textExtensions.Any(text => text.Equals(extension, StringComparison.OrdinalIgnoreCase))) {
				texts.Add((extension.ToUpperInvariant(), entry));
			}
		}

		if (mission == null) {
			failure = $"The bundle holds no {MissionExtension} file.";
			return null;
		}

		string stem = Path.GetFileNameWithoutExtension(mission.FullName);
		if (texts.Select(text => text.Entry).Append(about).Append(zoneHeader).Append(zoneHeightmap)
				.FirstOrDefault(entry => entry != null && !Path.GetFileNameWithoutExtension(entry.FullName).Equals(stem, StringComparison.OrdinalIgnoreCase))
				is { } stray) {
			failure = $"The bundle's {stray.FullName} is not named for its mission, {mission.FullName}.";
			return null;
		}

		if (texts.Count == 0) {
			failure = $"The bundle holds no text for {mission.FullName}: a {string.Join(", ", textExtensions)} file named {stem}.";
			return null;
		}

		if ((zoneHeader == null) != (zoneHeightmap == null)) {
			string missing = stem + (zoneHeader == null ? ZoneFiles.HeaderExtension : ZoneFiles.HeightmapExtension);
			failure = $"The bundle holds {(zoneHeader ?? zoneHeightmap)!.FullName} and no {missing}; a zone takes both.";
			return null;
		}

		if (ReadEntry(mission, MaxEntrySize, out failure) is not { } msn) {
			return null;
		}

		var textBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
		foreach (var (extension, entry) in texts) {
			if (ReadEntry(entry, MaxEntrySize, out failure) is not { } text) {
				return null;
			}

			textBytes[extension] = text;
		}

		MissionAbout? aboutText = null;
		if (about != null) {
			if (ReadEntry(about, MissionAbout.MaxSize, out failure) is not { } aboutBytes) {
				return null;
			}

			aboutText = MissionAbout.Parse(aboutBytes);
		}

		ZoneFiles? zone = null;
		if (zoneHeader != null && zoneHeightmap != null) {
			if (ReadEntry(zoneHeader, ZoneFiles.MaxHeightmapSize, out failure) is not { } header
					|| ReadEntry(zoneHeightmap, ZoneFiles.MaxHeightmapSize, out failure) is not { } heightmap) {
				return null;
			}

			zone = ZoneFiles.Read(header, heightmap, out string? why);
			if (zone == null) {
				failure = $"{zoneHeader.FullName} and {zoneHeightmap.FullName}: {why}";
				return null;
			}
		}

		failure = null;
		return new MissionBundle(stem, msn, textBytes, aboutText, zone);
	}

	/// <summary>
	/// The bundle file for mission <paramref name="name"/> (no extension): its <c>.MSN</c> <paramref name="msn"/>, its text in
	/// each language <paramref name="texts"/> holds, by extension with its dot, <paramref name="about"/> unless it is null or
	/// blank, and <paramref name="zone"/>'s header and heightmap unless it is null, each under <paramref name="name"/>.
	/// </summary>
	public static byte[] Write(string name, byte[] msn, IReadOnlyDictionary<string, byte[]> texts, MissionAbout? about,
			ZoneFiles? zone) {
		using var buffer = new MemoryStream();
		using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true)) {
			Add(MissionExtension, msn);
			foreach (var (extension, text) in texts) {
				Add(extension.ToUpperInvariant(), text);
			}

			if (about is { IsBlank: false }) {
				Add(MissionAbout.Extension, about.Write());
			}

			if (zone != null) {
				Add(ZoneFiles.HeaderExtension, zone.WriteHeader());
				Add(ZoneFiles.HeightmapExtension, zone.WriteHeightmap());
			}

			void Add(string extension, byte[] bytes) {
				using var stream = archive.CreateEntry(name + extension, CompressionLevel.Optimal).Open();
				stream.Write(bytes);
			}
		}

		return buffer.ToArray();
	}

	// The entry inflated, or null when it claims, or inflates to, more than <limit> bytes: its own claim is checked first and
	// then not trusted, since the reader inflates whatever the compressed data holds.
	private static byte[]? ReadEntry(ZipArchiveEntry entry, int limit, out string? failure) {
		failure = $"{entry.FullName} is larger than the {limit / 1024} KB a bundle's file may be.";
		if (entry.Length > limit) {
			return null;
		}

		using var stream = entry.Open();
		if (ReadCapped(stream, limit) is not { } bytes) {
			return null;
		}

		failure = null;
		return bytes;
	}

	// The stream read to its end, or null once it passes <limit> bytes.
	private static byte[]? ReadCapped(Stream stream, int limit) {
		using var buffer = new MemoryStream();
		var chunk = new byte[81920];
		int read;
		while ((read = stream.Read(chunk, 0, chunk.Length)) > 0) {
			if (buffer.Length + read > limit) {
				return null;
			}

			buffer.Write(chunk, 0, read);
		}

		return buffer.ToArray();
	}
}
