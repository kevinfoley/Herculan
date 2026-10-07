using System.Text;
using Xunit;
using static HercWorks.Disc.Tests.TestImages;

namespace HercWorks.Disc.Tests;

/// <summary>
/// <see cref="DiscImage"/> and its file system over hand-built images: each sector format read without
/// a cue sheet, cue sheets laying out data and audio over one file or several, and the file system's
/// names, lookups and reads.
/// </summary>
public class DiscImageTests : IDisposable {
	private readonly string _root = Directory.CreateTempSubdirectory("hercworks-disc-").FullName;

	private static readonly Dictionary<string, byte[]> Files = new() {
		["README.TXT"] = Encoding.ASCII.GetBytes("Earthsiege 2"),
		["VOL/SIMVOL0.VOL"] = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray(),
		["VOL/EMPTY.DAT"] = Array.Empty<byte>(),
		["DATA/SUB/DEEP.CFG"] = Encoding.ASCII.GetBytes("d:\\"),
	};

	public void Dispose() => Directory.Delete(_root, recursive: true);

	private string Write(string name, byte[] bytes) {
		string path = Path.Combine(_root, name);
		File.WriteAllBytes(path, bytes);
		return path;
	}

	private string WriteText(string name, string text) {
		string path = Path.Combine(_root, name);
		File.WriteAllText(path, text);
		return path;
	}

	private static void AssertFiles(DiscImage image) {
		var fs = image.OpenFileSystem();
		Assert.Empty(fs.Problems);
		Assert.Equal("TESTDISC", fs.VolumeIdentifier);
		Assert.Equal(new[] { "DATA", "README.TXT", "VOL" }, fs.Root.Children.Select(c => c.Name));
		foreach (var (path, data) in Files) {
			Assert.Equal(data, fs.ReadAllBytes(path));
		}

		var vol = fs.Find(@"vol\simvol0.vol")!;
		Assert.Equal("VOL/SIMVOL0.VOL", vol.FullPath);
		Assert.Equal(5000, vol.Length);
		Assert.Equal(RecordTime, vol.LastWriteTime);
		Assert.True(fs.DirectoryExists("/data/sub"));
		Assert.False(fs.FileExists("DATA/SUB"));
		Assert.Null(fs.Find("VOL/../README.TXT"));
		Assert.Equal(Files.Keys.Order(StringComparer.Ordinal),
			fs.EnumerateFiles().Select(f => f.FullPath).Order(StringComparer.Ordinal));
	}

	[Theory]
	[InlineData(TrackFormat.Mode1Cooked)]
	[InlineData(TrackFormat.Mode1Raw)]
	[InlineData(TrackFormat.Mode2Raw)]
	[InlineData(TrackFormat.Mode2Xa)]
	public void ReadsASingleFileImageInEachSectorFormat(TrackFormat format) {
		using var image = DiscImage.Open(Write("disc.iso", ToFormat(BuildIso(Files), format)));

		var track = Assert.Single(image.Tracks);
		Assert.Equal(format, track.Format);
		Assert.Equal(0, track.StartLba);
		AssertFiles(image);
		Assert.Throws<ArgumentException>(() => image.OpenAudioTrack(1));
	}

	[Fact]
	public void StreamsSeekAndReadAcrossSectorBoundaries() {
		using var image = DiscImage.Open(Write("disc.iso", ToFormat(BuildIso(Files), TrackFormat.Mode1Raw)));
		using var stream = image.OpenFileSystem().OpenRead("VOL/SIMVOL0.VOL");

		stream.Position = 2040;
		byte[] buffer = new byte[20];
		stream.ReadExactly(buffer);
		Assert.Equal(Files["VOL/SIMVOL0.VOL"].AsSpan(2040, 20).ToArray(), buffer);

		stream.Seek(-3, SeekOrigin.End);
		Assert.Equal(3, stream.Read(buffer));
		Assert.Equal(0, stream.Read(buffer));
	}

	[Fact]
	public void PrefersJolietNamesWhenTheDiscHasThem() {
		var files = new Dictionary<string, byte[]> { ["Vol/Long File Name.dat"] = new byte[] { 1, 2, 3 } };
		using var image = DiscImage.Open(Write("disc.iso", BuildIso(files, joliet: true)));

		var joliet = image.OpenFileSystem();
		Assert.True(joliet.IsJoliet);
		Assert.Equal("Long File Name.dat", joliet.Find("VOL/long file name.DAT")!.Name);
		Assert.Equal(new byte[] { 1, 2, 3 }, joliet.ReadAllBytes("Vol/Long File Name.dat"));

		var primary = image.OpenFileSystem(preferJoliet: false);
		Assert.False(primary.IsJoliet);
		Assert.Equal("LONG FILE NAME.DAT", primary.Find("vol/long file name.dat")!.Name);
	}

	[Fact]
	public void ARawImageWithoutACueSheetKeepsItsTrailingAudioAsOneTrack() {
		byte[] data = ToFormat(BuildIso(Files), TrackFormat.Mode1Raw);
		int dataSectors = data.Length / DiscImage.RawSectorSize;
		byte[] audio = Audio(2, 300);
		using var image = DiscImage.Open(Write("disc.iso", Concat(data, EmptyRawData(150, dataSectors), audio)));

		Assert.Equal(2, image.Tracks.Count);
		Assert.Equal(dataSectors + 150, image.Tracks[0].SectorCount);
		var trailing = image.Tracks[1];
		Assert.True(trailing.IsAudio);
		Assert.True(trailing.BoundaryUnknown);
		Assert.Equal(dataSectors + 150, trailing.StartLba);
		Assert.Equal(300, trailing.SectorCount);
		Assert.Equal(TimeSpan.FromSeconds(4), trailing.Duration);
		Assert.Equal(audio, ReadAll(image.OpenAudioTrack(2)));
		AssertFiles(image);
	}

	[Fact]
	public void LaysOutAMixedModeDiscInOneFile() {
		byte[] data = ToFormat(BuildIso(Files), TrackFormat.Mode1Raw);
		int dataSectors = data.Length / DiscImage.RawSectorSize;
		byte[] track2 = Audio(2, 150 + 100);
		byte[] track3 = Audio(3, 150 + 80);
		Write("disc.bin", Concat(data, track2, track3));
		string cue = WriteText("disc.cue", $"""
			REM GENRE Game
			FILE "disc.bin" BINARY
			  TRACK 01 MODE1/2352
			    INDEX 01 00:00:00
			  TRACK 02 AUDIO
			    TITLE "Track "Two"
			    INDEX 00 {Msf(dataSectors)}
			    INDEX 01 {Msf(dataSectors + 150)}
			  TRACK 03 AUDIO
			    INDEX 00 {Msf(dataSectors + 250)}
			    INDEX 01 {Msf(dataSectors + 400)}
			""");

		using var image = DiscImage.Open(cue);

		Assert.Equal(new[] { 0, dataSectors + 150, dataSectors + 400 }, image.Tracks.Select(t => t.StartLba));
		Assert.Equal(new[] { dataSectors + 150, 250, 80 }, image.Tracks.Select(t => t.SectorCount));
		Assert.Equal(new[] { 0, 150, 150 }, image.Tracks.Select(t => t.PregapSectors));
		Assert.Equal(dataSectors + 480, image.EndLba);

		// Track 2 plays to track 3's INDEX 01, through track 3's pregap.
		byte[] played = ReadAll(image.OpenAudioTrack(2));
		Assert.Equal(Concat(track2[(150 * DiscImage.RawSectorSize)..], track3[..(150 * DiscImage.RawSectorSize)]), played);
		Assert.Equal(track3[(150 * DiscImage.RawSectorSize)..], ReadAll(image.OpenAudioTrack(3)));
		AssertFiles(image);
		Assert.Throws<DiscFormatException>(() => image.ReadSectors(dataSectors + 150, 1, new byte[Sector]));
	}

	[Fact]
	public void LaysOutAOneFilePerTrackDiscAndFindsItsCueSheetFromTheBin() {
		byte[] data = ToFormat(BuildIso(Files), TrackFormat.Mode1Raw);
		int dataSectors = data.Length / DiscImage.RawSectorSize;
		string bin = Write("Game (Track 01).bin", data);
		Write("Game (Track 02).bin", Audio(2, 150 + 60));
		Write("Game (Track 03).bin", Audio(3, 150 + 40));
		WriteText("Game (Track 01).cue", """
			FILE "Game (Track 01).bin" BINARY
			  TRACK 01 MODE1/2352
			    INDEX 01 00:00:00
			FILE "Game (Track 02).bin" BINARY
			  TRACK 02 AUDIO
			    INDEX 00 00:00:00
			    INDEX 01 00:02:00
			FILE "Game (Track 03).bin" BINARY
			  TRACK 03 AUDIO
			    INDEX 00 00:00:00
			    INDEX 01 00:02:00
			""");

		using var image = DiscImage.Open(bin);

		Assert.EndsWith(".cue", image.Path);
		Assert.Equal(new[] { 0, dataSectors + 150, dataSectors + 360 }, image.Tracks.Select(t => t.StartLba));
		Assert.Equal(new[] { dataSectors + 150, 210, 40 }, image.Tracks.Select(t => t.SectorCount));
		Assert.Equal(Audio(3, 190)[(150 * DiscImage.RawSectorSize)..], ReadAll(image.OpenAudioTrack(3)));
		AssertFiles(image);
	}

	[Fact]
	public void APregapTheFileDoesNotHoldReadsAsSilence() {
		byte[] data = BuildIso(Files);
		int dataSectors = data.Length / Sector;
		Write("data.iso", data);
		Write("music.bin", Audio(2, 50));
		string cue = WriteText("disc.cue", """
			FILE "data.iso" BINARY
			  TRACK 01 MODE1/2048
			    INDEX 01 00:00:00
			    POSTGAP 00:00:10
			FILE "music.bin" MOTOROLA
			  TRACK 02 AUDIO
			    PREGAP 00:02:00
			    INDEX 01 00:00:00
			""");

		using var image = DiscImage.Open(cue);

		Assert.Equal(dataSectors + 10 + 150, image.Tracks[1].StartLba);
		Assert.Equal(150, image.Tracks[1].PregapSectors);
		byte[] gap = new byte[DiscImage.RawSectorSize * 160];
		image.ReadAudioSectors(dataSectors, 160, gap);
		Assert.All(gap, b => Assert.Equal(0, b));

		// MOTOROLA: the file's samples are big-endian, so a little-endian read swaps each pair.
		byte[] expected = Audio(2, 50);
		for (int i = 0; i < expected.Length; i += 2) {
			(expected[i], expected[i + 1]) = (expected[i + 1], expected[i]);
		}

		Assert.Equal(expected, ReadAll(image.OpenAudioTrack(2)));
		AssertFiles(image);
	}

	[Fact]
	public void TrackOnesPregapSitsBelowAddressZero() {
		Write("disc.bin", Concat(new byte[150 * Sector], BuildIso(Files)));
		string cue = WriteText("disc.cue", """
			FILE "disc.bin" BINARY
			  TRACK 01 MODE1/2048
			    INDEX 00 00:00:00
			    INDEX 01 00:02:00
			""");

		using var image = DiscImage.Open(cue);

		Assert.Equal(-150, image.FirstLba);
		Assert.Equal(0, image.Tracks[0].StartLba);
		AssertFiles(image);
	}

	[Fact]
	public void RefusesWhatItCannotRead() {
		Assert.Throws<DiscFormatException>(() => DiscImage.Open(Write("noise.iso", new byte[40 * Sector])));
		Assert.Throws<DiscFormatException>(() => DiscImage.Open(Write("tiny.iso", new byte[10])));

		Write("a.wav", new byte[1000]);
		Assert.Throws<DiscFormatException>(() => DiscImage.Open(WriteText("wave.cue", "FILE \"a.wav\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n")));
		Assert.Throws<FileNotFoundException>(() => DiscImage.Open(WriteText("missing.cue", "FILE \"gone.bin\" BINARY\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n")));

		// The file holds 10 sectors; track 2's INDEX 01 is past them.
		Write("short.bin", Audio(1, 10));
		Assert.Throws<DiscFormatException>(() => DiscImage.Open(WriteText("short.cue",
			"FILE \"short.bin\" BINARY\nTRACK 01 AUDIO\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nINDEX 01 00:00:20\n")));

		using var audioOnly = DiscImage.Open(WriteText("audio.cue", "FILE \"short.bin\" BINARY\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n"));
		Assert.Null(audioOnly.DataTrack);
		Assert.Throws<DiscFormatException>(() => audioOnly.OpenFileSystem());
		Assert.Throws<DiscFormatException>(() => audioOnly.ReadAudioSectors(10, 1, new byte[DiscImage.RawSectorSize]));
	}

	private static string Msf(int sectors) => $"{sectors / 75 / 60:00}:{sectors / 75 % 60:00}:{sectors % 75:00}";

	private static byte[] ReadAll(Stream stream) {
		using (stream) {
			var copy = new MemoryStream();
			stream.CopyTo(copy);
			return copy.ToArray();
		}
	}
}
