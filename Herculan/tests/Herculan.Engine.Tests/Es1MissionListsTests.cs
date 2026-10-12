using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's readers for <c>ZONES.VOL</c>'s battle sets, mission lists and equipment files (docs/es1/formats/bst-battle-sets.md),
/// and the opaque reader its four <c>dat\WORLD*.DAT</c> take (docs/es1/formats/wld-world.md#the-world-tables-in-zonesvol).
/// Each test passes vacuously when <c>ES1/VOL/ZONES.VOL</c> is absent, so the suite does not depend on the install.
/// </summary>
public class Es1MissionListsTests {
	private static readonly Lazy<IReadOnlyList<VolEntry>?> Entries = new(Load);

	[Fact]
	public void TheTwentyThreeListAndTableFilesRoundTripThroughTheirReaders() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var files = entries.Where(e => e.Ext == FileType.Dat && !e.FileName!.Trim().StartsWith("ZONE", StringComparison.OrdinalIgnoreCase)
			&& !e.FileName.Trim().Equals("MFORMS.DAT", StringComparison.OrdinalIgnoreCase)
			&& !e.FileName.Trim().Equals("STYLES.DAT", StringComparison.OrdinalIgnoreCase)).ToList();
		Assert.Equal(23, files.Count);
		foreach (var entry in files) {
			var transformer = TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege1);
			Assert.NotNull(transformer);
			object model = transformer!.ParseToObject(entry.RawBytes)!;
			byte[]? written = transformer switch {
				Es1BattleSetTransformer t => t.Write((Es1BattleSet)model),
				Es1NameListTransformer t => t.Write((Es1NameList)model),
				Es1SingleMissionsTransformer t => t.Write((Es1SingleMissions)model),
				Es1TechFileTransformer t => t.Write((Es1TechFile)model),
				Es1OpaqueFileTransformer t => t.Write((HercWorks.Core.Data.File.Es1OpaqueFile)model),
				_ => throw new InvalidOperationException($"{entry.FileName} took {transformer.GetType().Name}"),
			};
			Assert.True(entry.RawBytes.AsSpan().SequenceEqual(written), $"{entry.FileName} does not round-trip");
		}
	}

	/// <summary>docs/es1/formats/bst-battle-sets.md#the-retail-battle-sets: campaign 1 of BTLSET_0 and the any_msn.dat count.</summary>
	[Fact]
	public void BattleSetZeroHoldsTheDocumentedCampaigns() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var set = new Es1BattleSetTransformer().Parse(Named(entries, "BTLSET_0.DAT").RawBytes)!;
		Assert.Equal(new[] { 7, 8, 8, 9, 9, 8, 8, 8 }, set.Campaigns.Select(c => c.Positions.Length));
		Assert.Equal(new[] { "B0_C1_10.MSN", "B0_C1_11.MSN" }, set.Campaigns[1].Positions[1].Names);
		Assert.Equal(38, set.Campaigns.SelectMany(c => c.Positions).SelectMany(p => p.Names).Count(n => n == "any_msn.dat"));
	}

	/// <summary>docs/es1/formats/bst-battle-sets.md#name-lists: the one-byte lists, a count of 0 and nothing after it.</summary>
	[Fact]
	public void TheEmptyListsEndAtTheirCount() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var list = new Es1NameListTransformer().Parse(Named(entries, "ANY_CNV.DAT").RawBytes)!;
		Assert.Empty(list.Names);
		Assert.Null(list.SkippedByte);
	}

	/// <summary>docs/es1/formats/bst-battle-sets.md#the-retail-files: TECH_0's chassis 3 (Tomahawk) after 8 or 9 missions.</summary>
	[Fact]
	public void CareerTechFileHoldsTheDocumentedThresholds() {
		if (Entries.Value is not { } entries) {
			return;
		}

		var tech = new Es1TechFileTransformer().Parse(Named(entries, "TECH_0.DAT").RawBytes)!;
		Assert.Equal(25, tech.Records.Length);
		var tomahawk = tech.Records.Single(r => r.Kind == 0 && r.Index == 3);
		Assert.Equal((8, 9), ((int)tomahawk.GoodGradeMissions, (int)tomahawk.AnyGradeMissions));
	}

	private static VolEntry Named(IReadOnlyList<VolEntry> entries, string name) =>
		entries.Single(e => e.FileName!.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

	private static IReadOnlyList<VolEntry>? Load() {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES1", "VOL", "ZONES.VOL");
			if (File.Exists(vol)) {
				return VolFileReader.ParseVolFile(vol).FilesSet.ToList();
			}
		}

		return null;
	}
}
