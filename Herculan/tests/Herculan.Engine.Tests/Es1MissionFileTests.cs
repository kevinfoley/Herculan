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
	/// <summary>The files in the other layout (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout), which the reader rejects.</summary>
	private static readonly HashSet<string> OtherLayout = new(StringComparer.OrdinalIgnoreCase) { "B0_C1_20", "B0_C1_30" };

	private static readonly Lazy<IReadOnlyDictionary<string, byte[]>?> Missions = new(LoadMissions);

	[Fact]
	public void EveryMissionRoundTripsOrIsInTheOtherLayout() {
		if (Missions.Value is not { } missions) {
			return;
		}

		Assert.Equal(77, missions.Count);
		var transformer = new Es1MissionFileTransformer();
		var rejected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, bytes) in missions) {
			Es1MissionFile file;
			try {
				file = transformer.Parse(bytes)!;
			} catch (InvalidDataException ex) {
				rejected[name] = ex.Message;
				continue;
			}

			Assert.True(bytes.AsSpan().SequenceEqual(transformer.Write(file)), $"{name} does not round-trip");
		}

		Assert.True(OtherLayout.SetEquals(rejected.Keys), string.Join("\n", rejected.Select(r => $"{r.Key}: {r.Value}")));
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

	/// <summary><c>B0_C1_30.MSN</c> read by the loader's layout: no conditions, then 1,130 header patches in a 2,694-byte file.</summary>
	[Fact]
	public void TheOtherLayoutIsRejectedAtTheHeaderPatchCount() {
		if (Missions.Value is not { } missions) {
			return;
		}

		var ex = Assert.Throws<InvalidDataException>(() => new Es1MissionFileTransformer().Parse(missions["B0_C1_30"]));
		Assert.StartsWith("The header patches' count at 0x4, 1130,", ex.Message);
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
