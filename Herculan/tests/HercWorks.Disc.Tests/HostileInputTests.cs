using System.Buffers.Binary;
using HercWorks.Disc.Iso9660;
using Xunit;
using static HercWorks.Disc.Tests.TestImages;

namespace HercWorks.Disc.Tests;

/// <summary>
/// Images and cue sheets built to misbehave: a cue sheet reaching for files outside its directory,
/// directory records that loop, escape or overrun, and randomly corrupted images, which must surface as
/// <see cref="DiscFormatException"/> (or a missing file) and never as a crash, a hang or a read elsewhere.
/// </summary>
public class HostileInputTests : IDisposable {
	private readonly string _root = Directory.CreateTempSubdirectory("hercworks-disc-hostile-").FullName;

	public void Dispose() => Directory.Delete(_root, recursive: true);

	[Theory]
	[InlineData(@"..\..\track.bin")]
	[InlineData(@"C:\Elsewhere\track.bin")]
	[InlineData(@"\\server\share\track.bin")]
	[InlineData("../track.bin")]
	[InlineData("/etc/track.bin")]
	public void ACueSheetsFileIsAlwaysTakenFromBesideIt(string name) {
		string expected = Path.Combine(_root, "track.bin");
		File.WriteAllBytes(expected, Array.Empty<byte>());

		Assert.Equal(expected, DiscImage.ResolveCueFile(_root, name), ignoreCase: true);
	}

	[Theory]
	[InlineData(@"\\server\share\")]
	[InlineData("..")]
	[InlineData("CON")]
	[InlineData("nul.bin")]
	[InlineData("COM1.bin")]
	[InlineData("LPT\u00b9")]
	[InlineData("CONOUT$")]
	[InlineData("track.bin:stream")]
	[InlineData("track*.bin")]
	[InlineData("track\u0001.bin")]
	[InlineData("")]
	public void ACueSheetsFileMustBeAPlainFileName(string name) {
		Assert.Throws<DiscFormatException>(() => DiscImage.ResolveCueFile(_root, name));
	}

	[Theory]
	[InlineData("TRACK 01 AUDIO\nINDEX 01 00:00:00")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 01 AUDIO")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 01 AUDIO\nINDEX 01 00:00:75")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 01 AUDIO\nINDEX 01 00:60:00")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 01 AUDIO\nINDEX 01 -1:00:00")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 01 AUDIO\nINDEX 02 00:00:00")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 01 AUDIO\nINDEX 01 00:01:00\nINDEX 02 00:00:00")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 02 AUDIO\nINDEX 01 00:00:00\nTRACK 01 AUDIO\nINDEX 01 00:01:00")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 01 AUDIO\nINDEX 01 00:01:00\nTRACK 02 AUDIO\nINDEX 01 00:00:00")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 100 AUDIO\nINDEX 01 00:00:00")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 01 CDG\nINDEX 01 00:00:00")]
	[InlineData("FILE \"a.bin\" BINARY\nTRACK 01 AUDIO\nINDEX 01 00:00:00\nPREGAP 00:02:00")]
	[InlineData("FILE \"a.bin\" BINARY")]
	[InlineData("FILE \"a.bin BINARY\nTRACK 01 AUDIO\nINDEX 01 00:00:00")]
	[InlineData("REM nothing")]
	public void RejectsAMalformedCueSheet(string text) {
		Assert.Throws<DiscFormatException>(() => CueSheet.Parse(text));
	}

	[Fact]
	public void RejectsAnOversizedCueSheet() {
		Assert.Throws<DiscFormatException>(() => CueSheet.Parse(new byte[CueSheet.MaxBytes + 1]));
	}

	[Fact]
	public void LeavesOutRecordsThatLoopEscapeOrOverrun() {
		var files = new Dictionary<string, byte[]> {
			["A/KEEP.DAT"] = new byte[] { 42 },
			["B.DAT"] = new byte[10],
			["C.DAT"] = new byte[10],
			["D.DAT"] = new byte[10],
		};
		byte[] iso = BuildIso(files);
		int root = 18, dirA = 19;

		// A second record in A pointing back at the root, a name that is a path, and an extent past the end.
		var a = iso.AsSpan(dirA * Sector, Sector);
		int pos = 0;
		while (a[pos] != 0) {
			pos += a[pos];
		}

		pos += WriteRecord(a[pos..], "LOOP"u8.ToArray(), root, Sector, directory: true);
		pos += WriteRecord(a[pos..], "..\\EVIL.DAT;1"u8.ToArray(), 20, 1, directory: false);
		WriteRecord(a[pos..], "FAR.DAT;1"u8.ToArray(), 1_000_000, 1, directory: false);

		using var image = DiscImage.Open(Write("hostile.iso", iso));
		var fs = image.OpenFileSystem();

		Assert.Equal(new[] { "KEEP.DAT", "LOOP" }, fs.Find("A")!.Children.Select(c => c.Name));
		Assert.Empty(fs.Find("A/LOOP")!.Children);
		Assert.Equal(new byte[] { 42 }, fs.ReadAllBytes("A/KEEP.DAT"));
		Assert.Equal(3, fs.Problems.Count);
		Assert.Contains(fs.Problems, p => p.Contains("already read"));
		Assert.Contains(fs.Problems, p => p.Contains("not a plain file name"));
		Assert.Contains(fs.Problems, p => p.Contains("outside the image"));
	}

	[Fact]
	public void RefusesARootOutsideTheImage() {
		byte[] iso = BuildIso(new Dictionary<string, byte[]> { ["A.DAT"] = new byte[1] });
		BinaryPrimitives.WriteInt32LittleEndian(iso.AsSpan(16 * Sector + 156 + 2), int.MaxValue);

		using var image = DiscImage.Open(Write("root.iso", iso));
		Assert.Throws<DiscFormatException>(() => image.OpenFileSystem());
	}

	/// <summary>
	/// Corrupts a valid image at random, in every format, many times over, and reads all of whatever
	/// opens. Seeded, so a failure reproduces.
	/// </summary>
	[Theory]
	[InlineData(TrackFormat.Mode1Cooked)]
	[InlineData(TrackFormat.Mode1Raw)]
	[InlineData(TrackFormat.Mode2Xa)]
	public void SurvivesRandomCorruption(TrackFormat format) {
		var files = new Dictionary<string, byte[]> {
			["A/B/C.DAT"] = new byte[3000],
			["A/D.DAT"] = new byte[100],
			["E.DAT"] = new byte[5],
		};
		byte[] clean = ToFormat(BuildIso(files, joliet: true), format);
		int sectorSize = format.SectorSize();
		var random = new Random(1996);
		string path = Path.Combine(_root, "fuzz.iso");
		int opened = 0, refused = 0;

		for (int round = 0; round < 600; round++) {
			byte[] bytes = (byte[])clean.Clone();
			int hits = random.Next(1, 12);
			for (int i = 0; i < hits; i++) {
				// Mostly the descriptors and directories (sectors 16 to 23), where the structure is.
				int sector = random.Next(4) == 0 ? random.Next(bytes.Length / sectorSize) : random.Next(16, 24);
				int at = sector * sectorSize + random.Next(sectorSize);
				bytes[at] = random.Next(3) switch {
					0 => 0,
					1 => 0xff,
					_ => (byte)random.Next(256),
				};
			}

			if (random.Next(10) == 0) {
				Array.Resize(ref bytes, random.Next(bytes.Length));
			}

			File.WriteAllBytes(path, bytes);
			try {
				using var image = DiscImage.Open(path);
				foreach (bool joliet in new[] { true, false }) {
					var fs = image.OpenFileSystem(joliet);
					foreach (var file in fs.EnumerateFiles()) {
						using var stream = fs.OpenRead(file);
						stream.CopyTo(Stream.Null);
					}
				}

				opened++;
			} catch (Exception e) when (e is DiscFormatException or EndOfStreamException) {
				refused++;
			}
		}

		// Both outcomes have to occur, or the corruption is too light or too heavy to test anything.
		Assert.True(opened > 50 && refused > 50, $"{opened} read through, {refused} refused");
	}

	[Fact]
	public void SurvivesRandomCueSheets() {
		string valid = "FILE \"a.bin\" BINARY\n TRACK 01 MODE1/2352\n  INDEX 01 00:00:00\n TRACK 02 AUDIO\n  PREGAP 00:02:00\n  INDEX 01 01:00:00\n";
		const string alphabet = "FILETRACKINDEXPREGAPOSTGAP0123456789:\" \n/.MODEAUDIO";
		var random = new Random(2);
		for (int round = 0; round < 5000; round++) {
			char[] text = valid.ToCharArray();
			for (int i = random.Next(1, 6); i > 0; i--) {
				text[random.Next(text.Length)] = alphabet[random.Next(alphabet.Length)];
			}

			try {
				CueSheet.Parse(new string(text));
			} catch (DiscFormatException) {
			}
		}
	}

	private string Write(string name, byte[] bytes) {
		string path = Path.Combine(_root, name);
		File.WriteAllBytes(path, bytes);
		return path;
	}
}
