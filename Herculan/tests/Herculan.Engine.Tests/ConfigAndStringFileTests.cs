using System.Text;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The Core readers for <c>.STR</c>, <c>keyjoy.cfg</c>, <c>prefs.cfg</c>, <c>drive.cfg</c> and
/// <c>zoneNNNN.dat</c>, over synthetic files so the tests need no Earthsiege 2 install. The binary ones are
/// built by hand from their documented layouts rather than by the writer, so a reader and writer that agreed
/// on a wrong layout would fail.
/// </summary>
public class ConfigAndStringFileTests {
	/// <summary>
	/// A table of a group of two strings, one carrying attributes, an empty group, and a group holding an empty
	/// string, read into its groups and written back byte for byte.
	/// </summary>
	[Fact]
	public void StringFileReadsAndWritesEveryGroupWithItsAttributes() {
		byte[] bytes = StringTable();
		var transformer = new StringFileTransformer();

		StringFile parsed = Assert.IsType<StringFile>(transformer.Parse(bytes));

		Assert.Equal(3, parsed.GroupCount);
		Assert.Equal("FIRST", parsed.Text(0, 0));
		Assert.Equal("SECOND", parsed.Text(0, 1));
		Assert.Equal(new byte[] { 3, 7 }, parsed.Group(0)[1].Attributes);
		Assert.Empty(parsed.Group(1));
		Assert.Equal(string.Empty, parsed.Text(2, 0));
		Assert.Equal(new byte[] { 1 }, parsed.Group(2)[0].Attributes);
		Assert.Null(parsed.Text(5, 0));
		Assert.Equal(bytes, transformer.Write(parsed));
	}

	/// <summary>
	/// A table whose walk does not end exactly at its declared length reads as null rather than as a partial
	/// table, since group indices are positional. Each row trips a different check: a declared length past the
	/// file, then, with the length declared to match, the last string running into the end, its attribute
	/// missing, its whole entry missing, and a stray byte after the last group.
	/// </summary>
	[Theory]
	[InlineData(-2, false)]
	[InlineData(-2, true)]
	[InlineData(-1, true)]
	[InlineData(-5, true)]
	[InlineData(1, true)]
	public void StringFileRejectsATableItsWalkDoesNotFinish(int lengthChange, bool declared) {
		byte[] whole = StringTable();
		byte[] bytes = new byte[whole.Length + lengthChange];
		Array.Copy(whole, bytes, Math.Min(whole.Length, bytes.Length));
		if (declared) {
			BitConverter.GetBytes(bytes.Length - 4).CopyTo(bytes, 0);
		}

		Assert.Null(new StringFileTransformer().Parse(bytes));
	}

	[Fact]
	public void KeyjoyReadsOnlyReverseAsSetWhateverItsCaseAndComments() {
		const string text = "; shipped comment\r\n[Keyjoy]\r\nTilt = reverse ; inverted\r\nBacKTurn=REVERSE\r\n"
			+ "Missile=Default\r\n[Other]\r\nRudder=Reverse\r\n";

		Keyjoy parsed = Assert.IsType<Keyjoy>(new KeyjoyTransformer().Parse(Encoding.ASCII.GetBytes(text)));

		Assert.True(parsed.ReverseTilt);
		Assert.True(parsed.ReverseBackturn);
		Assert.False(parsed.ReverseMissile);
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

	/// <summary>A file of exactly the option array reads; one byte short does not; a longer one keeps its tail.</summary>
	[Fact]
	public void PrefsNeedsTheWholeArrayAndKeepsALongOnesTail() {
		var transformer = new PrefsTransformer();
		Assert.Null(transformer.Parse(new byte[Prefs.Length - 1]));
		Assert.NotNull(transformer.Parse(new byte[Prefs.Length]));

		byte[] bytes = new byte[Prefs.Length + 3];
		bytes[Prefs.HercDetailOption] = 4;
		bytes[^1] = 0x5a;

		Prefs parsed = Assert.IsType<Prefs>(transformer.Parse(bytes));
		Assert.Equal(4, parsed[Prefs.HercDetailOption]);
		Assert.Equal(0, parsed[Prefs.Length + 100]);
		Assert.Equal(bytes, transformer.Write(parsed));
	}

	/// <summary>The disc's directory is the first whitespace-delimited token, as <c>fscanf("%s")</c> reads it.</summary>
	[Fact]
	public void DriveTakesTheFirstToken() {
		var transformer = new DriveTransformer();
		Drive parsed = Assert.IsType<Drive>(transformer.Parse(Encoding.ASCII.GetBytes("  D:\\ES2\r\nC:\\SIERRA\\ES2")));

		Assert.Equal("D:\\ES2", parsed.Directory);
		Assert.Equal("C:\\SIERRA\\ES2", parsed.InstallDirectory);
		Assert.Equal("D:\\ES2", transformer.Parse(Encoding.ASCII.GetBytes("D:\\ES2 extra"))!.Directory);
		Assert.Null(transformer.Parse(Encoding.ASCII.GetBytes(" \r\n"))!.Directory);
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

	/// <summary>
	/// The four <c>int32</c>s in docs/retail/formats/zone-terrain.md's order — width shift, height shift, cell
	/// shift, height scale — read and written back; one byte short reads as null.
	/// </summary>
	[Fact]
	public void ZoneDatReadsAndWritesItsFourInts() {
		byte[] bytes = [8, 0, 0, 0, 7, 0, 0, 0, 14, 0, 0, 0, 120, 0, 0, 0];
		var transformer = new ZoneDatTransformer();

		ZoneDat parsed = Assert.IsType<ZoneDat>(transformer.Parse(bytes));

		Assert.Equal((8, 7, 14, 120), (parsed.WidthShift, parsed.HeightShift, parsed.CellSizeShift, parsed.HeightScale));
		Assert.Equal(bytes, transformer.Write(parsed));
		Assert.Null(transformer.Parse(bytes[..^1]));
	}

	/// <summary>
	/// Three groups built by hand from docs/retail/formats/str-strings.md#layout: an <c>int32</c> content
	/// length, then per group an <c>int16</c> count, and per string an <c>int16</c> length counting the NUL,
	/// the text, the NUL, a <c>uint8</c> attribute count and the attributes.
	/// </summary>
	private static byte[] StringTable() {
		var content = new List<byte>();
		void Short(int value) => content.AddRange(BitConverter.GetBytes((short)value));
		void Entry(string text, params byte[] attributes) {
			Short(text.Length + 1);
			content.AddRange(Encoding.ASCII.GetBytes(text));
			content.Add(0);
			content.Add((byte)attributes.Length);
			content.AddRange(attributes);
		}

		Short(2);
		Entry("FIRST");
		Entry("SECOND", 3, 7);
		Short(0);
		Short(1);
		Entry("", 1);

		return [.. BitConverter.GetBytes(content.Count), .. content];
	}
}
