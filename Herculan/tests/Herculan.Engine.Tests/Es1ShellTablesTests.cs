using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's readers for two of its <c>SHELL0.VOL</c> tables whose layout differs from ES2's under the same name: the ten
/// talking-head <c>ENG\*_TEXT.BIN</c> (docs/es1/formats/bin-string-tables.md) and <c>CFG\DAMAGE.DAT</c>
/// (docs/es1/formats/herc-catalogs.md). Each test passes vacuously when <c>ES1/VOL/SHELL0.VOL</c> is absent, so the suite does
/// not depend on the install.
/// </summary>
public class Es1ShellTablesTests {
	private static readonly Lazy<IReadOnlyList<VolEntry>?> Entries = new(Load);

	[Fact]
	public void EveryDialogTextFileRoundTripsAndTheOtherBinStayOnEs2sReader() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var bins = entries.Where(e => e.Ext == FileType.Bin).ToList();
		var texts = bins.Where(e => TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1) is Es1DialogTextTransformer).ToList();
		Assert.Equal(10, texts.Count);
		Assert.All(bins.Except(texts), e => Assert.IsType<BinStringFileTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1)));
		foreach (var entry in texts) {
			var transformer = new Es1DialogTextTransformer();
			var file = transformer.Parse(entry.RawBytes)!;
			Assert.True(entry.RawBytes.AsSpan().SequenceEqual(transformer.Write(file)), $"{entry.FileName} does not round-trip");
		}
	}

	/// <summary>docs/es1/formats/bin-string-tables.md#the-dialog-text-files: version 2 and each file's line count.</summary>
	[Fact]
	public void DialogTextFilesHoldTheDocumentedLines() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var files = entries.Where(e => e.FileName!.Trim().EndsWith("_TEXT.BIN", StringComparison.OrdinalIgnoreCase))
			.ToDictionary(e => e.FileName!.Trim().ToUpperInvariant(), e => new Es1DialogTextTransformer().Parse(e.RawBytes)!);
		Assert.All(files.Values, f => Assert.Equal(2, f.Version));
		Assert.Equal(
			new[] { 9, 9, 9, 9, 8, 8, 8, 8, 55, 41 },
			new[] { "C0", "C1", "C2", "C3", "D0", "D1", "D2", "D3", "G0", "T1" }.Select(n => files[n + "_TEXT.BIN"].Lines.Length));
	}

	/// <summary>docs/es1/formats/herc-catalogs.md#cfgdamagedat: the round-trip and the documented runs.</summary>
	[Fact]
	public void DamageDatRoundTripsWithTheDocumentedValues() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var entry = entries.Single(e => e.FileName!.Trim().Equals("DAMAGE.DAT", StringComparison.OrdinalIgnoreCase));
		var transformer = Assert.IsType<Es1DamageRepairCostTransformer>(TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege1));
		var table = transformer.Parse(entry.RawBytes)!;
		Assert.True(entry.RawBytes.AsSpan().SequenceEqual(transformer.Write(table)));
		Assert.Equal((800, 1000), ((int)table.ChassisScale, (int)table.WeaponScale));
		Assert.Equal(new short[] { 100, 100, 50, 50, 25, 100, 25, 25, 25, 0 }, table.InternalPercent);
	}

	private static IReadOnlyList<VolEntry>? Load() {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES1", "VOL", "SHELL0.VOL");
			if (File.Exists(vol)) {
				return VolFileReader.ParseVolFile(vol).FilesSet.ToList();
			}
		}

		return null;
	}
}
