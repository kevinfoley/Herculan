using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's shell animations (docs/es1/formats/anm-animation.md), careers log and <c>CAREER1.MEC</c>
/// (docs/es1/formats/career-log-and-player-mec.md) through the Core readers ES1's table binds to them. Each test on the
/// archive passes vacuously when it is absent.
/// </summary>
public class Es1ShellCareerAndAnimationFilesTests {
	private static readonly Lazy<IReadOnlyList<VolEntry>?> Shell = new(() => Load("SHELL0.VOL"));

	/// <summary>
	/// Both <c>.ANM</c> round-trip through the animation reader as two pages; <c>ESIEGE2</c>'s first page holds records 0–6
	/// and its second record 7, <c>ESIEGE3</c>'s first record 8 and its second records 0–7, and each record's operations
	/// start after a 4-byte header.
	/// </summary>
	[Fact]
	public void EveryAnimationRoundTripsAsItsPages() {
		if (Shell.Value is not { } shell) {
			return;
		}

		var files = shell.Where(e => HasExtension(e, ".ANM")).ToDictionary(e => e.FileName!.Trim().ToUpperInvariant());
		Assert.Equal(["ESIEGE2.ANM", "ESIEGE3.ANM"], files.Keys.Order());
		foreach (var e in files.Values) {
			var reader = Assert.IsType<Es1DeluxePaintAnimationTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1));
			var anm = reader.Parse(e.RawBytes)!;
			Assert.Equal(e.RawBytes, reader.Write(anm));
			Assert.Equal((Es1DeluxePaintAnimation.LpfId, Es1DeluxePaintAnimation.AnimId, 15), (anm.Id, anm.ContentType, (int)anm.FramesPerSecond));
			Assert.All(anm.Pages.SelectMany(p => p.Records), r => Assert.Equal((0, 0), (r.HasExtra, r.Extra.Length)));
		}

		var two = new Es1DeluxePaintAnimationTransformer().Parse(files["ESIEGE2.ANM"].RawBytes)!;
		var three = new Es1DeluxePaintAnimationTransformer().Parse(files["ESIEGE3.ANM"].RawBytes)!;
		Assert.Equal(new (int, int)[] { (0, 7), (7, 1) }, two.Pages.Select(p => ((int)p.FirstRecord, p.Records.Length)));
		Assert.Equal(new (int, int)[] { (8, 1), (0, 8) }, three.Pages.Select(p => ((int)p.FirstRecord, p.Records.Length)));
		Assert.Equal((8, 9), (two.FrameCount, three.FrameCount));
	}

	/// <summary>The careers log holds no careers, and <c>CAREER1.MEC</c> takes the opaque reader; both round-trip.</summary>
	[Fact]
	public void TheCareerFilesRoundTrip() {
		if (Shell.Value is not { } shell) {
			return;
		}

		var log = shell.Single(e => e.FileName!.Trim().Equals("NAMES.LOG", StringComparison.OrdinalIgnoreCase));
		var logReader = Assert.IsType<Es1CareerLogTransformer>(TransformerRegistry.FindTransformer(log, RetailGame.Earthsiege1));
		var parsed = logReader.Parse(log.RawBytes)!;
		Assert.Equal((0, (short)-1), (parsed.Entries.Length, parsed.Current));
		Assert.Equal(log.RawBytes, logReader.Write(parsed));

		var mec = shell.Single(e => e.FileName!.Trim().Equals("CAREER1.MEC", StringComparison.OrdinalIgnoreCase));
		var mecReader = Assert.IsType<Es1OpaqueFileTransformer>(TransformerRegistry.FindTransformer(mec, RetailGame.Earthsiege1));
		Assert.Equal(mec.RawBytes, mecReader.Write(mecReader.Parse(mec.RawBytes)!));
	}

	/// <summary>A log with an entry writes its current entry and the entry's 100 bytes after the count, and reads them back.</summary>
	[Fact]
	public void ALogWithAnEntryRoundTrips() {
		var entry = new Es1CareerLogEntry { Word = 3 };
		"UNTITLED"u8.CopyTo(entry.Name);
		@"CAR\CAR_0.CAR"u8.CopyTo(entry.CareerFile);
		@"CAR\CAR_0.MEC"u8.CopyTo(entry.HangarFile);
		var reader = new Es1CareerLogTransformer();
		byte[] bytes = reader.Write(new Es1CareerLog { Current = 0, Entries = [entry] })!;

		Assert.Equal(2 + 2 + Es1CareerLogEntry.Length, bytes.Length);
		Assert.Equal(bytes, reader.Write(reader.Parse(bytes)!));
	}

	private static bool HasExtension(VolEntry e, string extension) =>
		e.FileName != null && e.FileName.Trim().EndsWith(extension, StringComparison.OrdinalIgnoreCase);

	private static IReadOnlyList<VolEntry>? Load(string archive) {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES1", "VOL", archive);
			if (File.Exists(vol)) {
				return VolFileReader.ParseVolFile(vol).FilesSet.ToList();
			}
		}

		return null;
	}
}
