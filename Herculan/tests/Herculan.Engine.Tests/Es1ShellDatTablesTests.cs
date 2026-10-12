using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's readers for <c>SHELL0.VOL</c>'s <c>.DAT</c> tables (docs/es1/formats/car-hercs-and-stock.md, herc-diagrams.md,
/// shell-file-lists.md). Each test passes vacuously when <c>ES1/VOL/SHELL0.VOL</c> is absent, so the suite does not depend on
/// the install.
/// </summary>
public class Es1ShellDatTablesTests {
	private static readonly Lazy<IReadOnlyList<VolEntry>?> Entries = new(Load);

	[Fact]
	public void EveryDatFileRoundTripsThroughItsReader() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var files = entries.Where(e => e.Ext == FileType.Dat).ToList();
		Assert.Equal(39, files.Count);
		foreach (var entry in files) {
			var transformer = TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege1);
			Assert.NotNull(transformer);
			object model = transformer!.ParseToObject(entry.RawBytes)!;
			byte[]? written = (byte[]?)transformer.GetType().GetMethod("Write", [model.GetType()])!.Invoke(transformer, [model]);
			Assert.True(entry.RawBytes.AsSpan().SequenceEqual(written), $"{entry.FileName} does not round-trip");
		}
	}

	/// <summary>docs/es1/formats/car-hercs-and-stock.md#the-retail-herc-lists: a new career's four HERCs.</summary>
	[Fact]
	public void CareerStartsWithTheDocumentedHercs() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var list = new Es1HercListTransformer().Parse(Named(entries, "CAR_INIT.DAT").RawBytes)!;
		Assert.Equal((8, -1), ((int)list.SlotCount, (int)list.MarkedSlot));
		Assert.Equal(new short[] { 0, 0, 1, 1 }, list.Hercs.Select(h => h.Herc.Chassis));
		Assert.Equal(new[] { "0 0 17", "6 6 17", "7 7 17", "1 1 17" }, list.Hercs.Select(h => string.Join(' ', h.Herc.Weapons)));
		Assert.All(list.Hercs, h => Assert.All(h.Herc.Status, s => Assert.Equal(100, s)));
	}

	/// <summary>docs/es1/formats/car-hercs-and-stock.md#the-retail-stock: the single-mission stock's slots.</summary>
	[Fact]
	public void SingleMissionStockHoldsTheDocumentedSlots() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var stock = new Es1InventoryTransformer().Parse(Named(entries, "S_ARMORY.DAT").RawBytes)!;
		Assert.Equal(new short[] { 0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13 }, stock.Records.Select(r => r.Slot));
		Assert.All(stock.Records.SelectMany(r => r.Values), v => Assert.Equal(100, v));
	}

	/// <summary>docs/es1/formats/herc-diagrams.md: per chassis, as many layout mounts and areas as the mount table's count.</summary>
	[Fact]
	public void LayoutsAndAreasFollowTheMountCounts() {
		if (Entries.Value is not { } entries) {
			return;
		}

		int[] mounts = [3, 3, 4, 5, 6, 7, 8, 9];
		string[] files = ["ROAD", "GUNS", "RAPT", "TOMA", "PATR", "PANT", "HERC", "COLO"];
		Assert.Equal(mounts, files.Select(f => new Es1HercLayoutTransformer().Parse(Named(entries, f + ".DAT").RawBytes)!.Mounts.Length));
		var areas = new Es1HercHotspotsTransformer().Parse(Named(entries, "HOTSPOT.DAT").RawBytes)!;
		Assert.Equal(mounts, areas.Chassis.Select(c => c.Areas.Length));
	}

	/// <summary>docs/es1/formats/shell-file-lists.md#the-lip-sync-lists: each line names its lip-sync script and its sound.</summary>
	[Fact]
	public void LipSyncListsNameAScriptAndASoundPerLine() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var counts = new List<int>();
		foreach (string k in new[] { "C0", "C1", "C2", "C3", "D0", "D1", "D2", "D3", "G0", "T0", "T1" }) {
			var list = new Es1LipSyncListTransformer().Parse(Named(entries, k + "_FILES.DAT").RawBytes)!;
			Assert.EndsWith(".TTM", list.Script);
			Assert.All(list.Lines, l => Assert.Equal(new[] { ".SNC", ".RAW" }, l.Names.Select(n => n[^4..])));
			counts.Add(list.Lines.Length);
		}

		Assert.Equal(new[] { 9, 9, 9, 9, 8, 8, 8, 8, 55, 43, 43 }, counts);
	}

	/// <summary>docs/es1/formats/shell-file-lists.md#the-animation-lists: seven lists, four of them empty.</summary>
	[Fact]
	public void AnimationListsHoldTheDocumentedCounts() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var lists = new Es1TtmFileListsTransformer().Parse(Named(entries, "TM_FILES.DAT").RawBytes)!;
		Assert.Equal(new[] { 0, 0, 8, 25, 4, 0, 0 }, lists.Lists.Select(l => l.Names.Length));
	}

	private static VolEntry Named(IReadOnlyList<VolEntry> entries, string name) =>
		entries.Single(e => e.FileName!.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

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
