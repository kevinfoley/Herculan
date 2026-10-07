using System.Text;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The Core readers for <c>.STR</c>, <c>keyjoy.cfg</c>, <c>prefs.cfg</c>, <c>drive.cfg</c> and
/// <c>zoneNNNN.dat</c>, over synthetic files so the tests need no Earthsiege 2 install.
/// </summary>
public class ConfigAndStringFileTests {
	[Fact]
	public void StringFileRoundTripsEveryGroupWithItsAttributes() {
		var file = new StringFile {
			Groups = [
				[new StringFile.Entry("FIRST", []), new StringFile.Entry("SECOND", [3, 7])],
				[],
				[new StringFile.Entry("", [1])],
			],
		};
		var transformer = new StringFileTransformer();

		byte[] bytes = transformer.Write(file)!;
		StringFile parsed = Assert.IsType<StringFile>(transformer.Parse(bytes));

		Assert.Equal(3, parsed.GroupCount);
		Assert.Equal("SECOND", parsed.Text(0, 1));
		Assert.Equal(new byte[] { 3, 7 }, parsed.Group(0)[1].Attributes);
		Assert.Empty(parsed.Group(1));
		Assert.Null(parsed.Text(5, 0));
		Assert.Equal(bytes, transformer.Write(parsed));
	}

	[Fact]
	public void StringFileRejectsATruncatedTableRatherThanShiftingLaterGroups() {
		byte[] bytes = new StringFileTransformer().Write(new StringFile {
			Groups = [[new StringFile.Entry("ONE", [])], [new StringFile.Entry("TWO", [])]],
		})!;

		Assert.Null(new StringFileTransformer().Parse(bytes[..^2]));
	}

	[Fact]
	public void KeyjoyReadsOnlyReverseAsSetWhateverItsCaseAndComments() {
		const string text = "; shipped comment\r\n[Keyjoy]\r\nTilt = reverse ; inverted\r\nBacKTurn=Default\r\n"
			+ "Missile=REVERSE\r\n[Other]\r\nRudder=Reverse\r\n";

		Keyjoy parsed = Assert.IsType<Keyjoy>(new KeyjoyTransformer().Parse(Encoding.ASCII.GetBytes(text)));

		Assert.True(parsed.ReverseTilt);
		Assert.False(parsed.ReverseBackturn);
		Assert.True(parsed.ReverseMissile);
		Assert.False(parsed.ReverseRudder);
	}

	[Fact]
	public void KeyjoyWriteReadsBackTheSameSwitches() {
		var transformer = new KeyjoyTransformer();
		var source = new Keyjoy { ReverseBackturn = true, ReverseRudder = true };

		Keyjoy parsed = Assert.IsType<Keyjoy>(transformer.Parse(transformer.Write(source)));

		Assert.False(parsed.ReverseTilt);
		Assert.True(parsed.ReverseBackturn);
		Assert.False(parsed.ReverseMissile);
		Assert.True(parsed.ReverseRudder);
	}

	[Fact]
	public void PrefsRejectsAShortFileAndKeepsALongOnesTail() {
		var transformer = new PrefsTransformer();
		Assert.Null(transformer.Parse(new byte[Prefs.Length - 1]));

		byte[] bytes = new byte[Prefs.Length + 3];
		bytes[Prefs.HercDetailOption] = 4;
		bytes[^1] = 0x5a;

		Prefs parsed = Assert.IsType<Prefs>(transformer.Parse(bytes));
		Assert.Equal(4, parsed[Prefs.HercDetailOption]);
		Assert.Equal(0, parsed[Prefs.Length + 100]);
		Assert.Equal(bytes, transformer.Write(parsed));
	}

	[Fact]
	public void DriveTakesTheFirstToken() {
		Drive parsed = Assert.IsType<Drive>(new DriveTransformer().Parse(Encoding.ASCII.GetBytes("  D:\\ES2\r\nignored")));

		Assert.Equal("D:\\ES2", parsed.Directory);
		Assert.Equal("ignored", parsed.InstallDirectory);
		Assert.Null(new DriveTransformer().Parse(Encoding.ASCII.GetBytes(" \r\n"))!.Directory);
	}

	[Fact]
	public void DriveWritesOneDirectoryPerLineAndRefusesWhitespace() {
		var transformer = new DriveTransformer();

		Assert.Equal("D:\\\r\nC:\\SIERRA\\ES2", Encoding.ASCII.GetString(
			transformer.Write(new Drive { Directory = "D:\\", InstallDirectory = "C:\\SIERRA\\ES2" })!));
		Assert.Equal("D:\\", Encoding.ASCII.GetString(transformer.Write(new Drive { Directory = "D:\\" })!));
		Assert.Throws<ArgumentException>(() => transformer.Write(new Drive { Directory = "C:\\Program Files\\ES2" }));

		// The install's line is discarded by the readers, so its spaces survive a round trip.
		var spaced = transformer.Parse(Encoding.ASCII.GetBytes("D:\\\r\nC:\\Program Files\\ES2"))!;
		Assert.Equal("C:\\Program Files\\ES2", spaced.InstallDirectory);
		Assert.Equal("D:\\\r\nC:\\Program Files\\ES2", Encoding.ASCII.GetString(transformer.Write(spaced)!));
	}

	[Fact]
	public void ZoneDatRoundTripsItsFourInts() {
		var transformer = new ZoneDatTransformer();
		var source = new ZoneDat { WidthShift = 8, HeightShift = 7, CellShift = 14, HeightScale = 120 };

		byte[] bytes = transformer.Write(source)!;
		ZoneDat parsed = Assert.IsType<ZoneDat>(transformer.Parse(bytes));

		Assert.Equal(ZoneDatTransformer.Size, bytes.Length);
		Assert.Equal((8, 7, 14, 120), (parsed.WidthShift, parsed.HeightShift, parsed.CellShift, parsed.HeightScale));
		Assert.Null(transformer.Parse(bytes[..^1]));
	}
}
