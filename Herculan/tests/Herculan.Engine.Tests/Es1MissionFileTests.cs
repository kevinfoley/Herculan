using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="Es1MissionFileTransformer"/> over the 77 <c>.MSN</c> files in ES1's <c>ZONES.VOL</c>
/// (docs/es1/formats/msn-mission-file.md). Each test passes vacuously when <c>ES1/VOL/ZONES.VOL</c> is
/// absent, so the suite does not depend on the install.
/// </summary>
public class Es1MissionFileTests {
	private static readonly Lazy<IReadOnlyDictionary<string, byte[]>?> Missions = new(LoadMissions);

	/// <summary>Every file round-trips, and the two in the other layout (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout) are read in it.</summary>
	[Fact]
	public void EveryMissionRoundTrips() {
		if (Missions.Value is not { } missions) {
			return;
		}

		Assert.Equal(77, missions.Count);
		var transformer = new Es1MissionFileTransformer();
		var otherLayout = new List<string>();
		foreach (var (name, bytes) in missions) {
			var file = transformer.Parse(bytes)!;
			Assert.True(bytes.AsSpan().SequenceEqual(transformer.Write(file)), $"{name} does not round-trip");
			if (file.HeaderWords != null) {
				otherLayout.Add(name);
			}
		}

		Assert.Equal(["B0_C1_20", "B0_C1_30"], otherLayout.Order());
	}

	/// <summary>The two files walked by hand to their last byte (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout).</summary>
	[Fact]
	public void HandWalkedFilesReadTheirRows() {
		if (Missions.Value is not { } missions) {
			return;
		}

		Assert.Equal([475, 20, 37, 12, 17, 1, 1, 436, 0, 37, 10], RowCounts(missions["SM_SCT50"]));
		Assert.Equal([425, 9, 25, 12, 4, 1, 5, 318, 1, 11, 7], RowCounts(missions["B0_C2_60"]));
	}

	/// <summary>The values docs/es1/formats/msn-mission-file.md quotes from <c>B0_C2_60.MSN</c>'s actions, bases and squad.</summary>
	[Fact]
	public void B0C260HoldsTheQuotedWords() {
		if (Missions.Value is not { } missions) {
			return;
		}

		var file = new Es1MissionFileTransformer().Parse(missions["B0_C2_60"])!;

		Assert.Equal([(37, 6), (38, 6), (39, 6), (40, 6)],
			file.Actions.Where(a => a.Word1A != -1 || a.Word1C != -1).Select(a => ((int)a.Word1A, (int)a.Word1C)));
		Assert.Equal(Enumerable.Repeat((41, 2), 4),
			file.Bases.Where(b => b.Word0E != -1 || b.Word10 != -1).Select(b => ((int)b.Word0E, (int)b.Word10)));
		Assert.Equal(0, file.Groups[0].Word4A);
	}

	/// <summary>The rows, header words and first mech docs/es1/formats/msn-mission-file.md#a-file-in-another-layout quotes from the two files in the other layout.</summary>
	[Fact]
	public void OtherLayoutFilesHoldTheQuotedWords() {
		if (Missions.Value is not { } missions) {
			return;
		}

		var c120 = new Es1MissionFileTransformer().Parse(missions["B0_C1_20"])!;
		var c130 = new Es1MissionFileTransformer().Parse(missions["B0_C1_30"])!;

		Assert.Equal([4, 0, 33, 12, 6, 0, 4, 34, 2, 5, 23], RowCounts(missions["B0_C1_20"]));
		Assert.Equal([5, 0, 26, 12, 4, 3, 3, 8, 2, 6, 8], RowCounts(missions["B0_C1_30"]));
		Assert.Equal(new short[] { 0, 1120, 1, 3, 2, 0, 0, 0, 0, 0 }, c120.HeaderWords);
		Assert.Equal(new short[] { 0, 1130, 1, 2, 1, 0, 0, 0, 0, 0 }, c130.HeaderWords);
		Assert.Equal([(4, 2, 100)], c120.Mechs.Concat(c130.Mechs)
			.Select(m => ((int)m.Words4E.Single(), (int)m.Word62, (int)m.Word68)).Distinct());
	}

	private static int[] RowCounts(byte[] bytes) {
		var f = new Es1MissionFileTransformer().Parse(bytes)!;
		return [f.Conditions.Length, f.HeaderPatches.Length, f.Points.Length, f.Headings.Length, f.WaypointGroups.Length,
			f.TriggerAreas.Length, f.Actions.Length, f.Mechs.Length, f.Flyers.Length, f.Bases.Length, f.Groups.Length];
	}

	private static IReadOnlyDictionary<string, byte[]>? LoadMissions() {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string path = Path.Combine(at.FullName, "ES1", "VOL", "ZONES.VOL");
			if (!File.Exists(path)) {
				continue;
			}

			Voln vol = VolFileReader.ParseVolFile(path);
			return vol.FilesSet
				.Where(e => e.FileName != null && e.FileName.Trim().EndsWith(".MSN", StringComparison.OrdinalIgnoreCase)
					&& vol.Folders.TryGetValue(e.DirIdx, out var dir) && string.Equals(dir.Label, "MSN", StringComparison.OrdinalIgnoreCase))
				.ToDictionary(e => Path.GetFileNameWithoutExtension(e.FileName!.Trim()).ToUpperInvariant(), e => e.RawBytes!);
		}

		return null;
	}
}
