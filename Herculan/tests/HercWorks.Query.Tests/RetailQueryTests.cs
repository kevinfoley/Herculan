using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol.Io;
using Xunit;

namespace HercWorks.Query.Tests;

/// <summary>
/// The queries against the v1.0 install in <c>ES2/</c>. Each passes vacuously when the install is
/// absent, so the suite does not depend on one.
/// </summary>
public class RetailQueryTests {
	private static readonly Lazy<RetailData?> Retail = new(() =>
		RetailData.FindDefaultInstall() is { } install ? RetailData.Load(install) : null);

	[Fact]
	public void EveryMissionParses() {
		if (Retail.Value is not { } data) {
			return;
		}

		Assert.Equal(62, data.Missions.Count);
		Assert.Equal(1949, data.Missions.Sum(m => m.File.Bases!.Length));
		Assert.All(data.Missions, m => Assert.NotNull(m.Text));
	}

	[Fact]
	public void TransportStructures() {
		if (Retail.Value is not { } data) {
			return;
		}

		var result = TypeQuery.Run(data, RosterKind.Base, [0x22]);

		var expected = new Dictionary<string, int> {
			["C1_07"] = 1, ["C2_10"] = 3, ["C3_02"] = 3, ["C4_04"] = 3, ["C4_08"] = 6, ["C4_09"] = 6,
			["C4_10"] = 6, ["C5_01"] = 3, ["C5_02"] = 5, ["C5_05"] = 3, ["C5_08"] = 2,
		};
		Assert.Equal(expected, result.Missions.ToDictionary(m => m.Mission, m => m.Records));
		Assert.Equal(41, result.Records);
		Assert.Equal(41, result.Placed);
		Assert.Equal([(short)0], result.Missions.Single(m => m.Mission == "C5_01").Sides);
		Assert.Equal([(short)0], result.Missions.Single(m => m.Mission == "C5_05").Sides);

		// C4_09's GUID 75 starts destroyed when flag 625 is set: a later record with its GUID says so.
		var guid75 = result.Missions.Single(m => m.Mission == "C4_09").Hits.Single(h => h.Guid == 75);
		var variant = Assert.Single(guid75.SameGuid);
		Assert.Equal("flag 625 > 0", variant.Condition);
		Assert.Equal((short)0, variant.StartingCondition);

		Assert.Empty(TypeQuery.Run(data, RosterKind.Base, [0x0a]).Missions);
	}

	[Fact]
	public void TypesResolveByName() {
		if (Retail.Value is not { } data) {
			return;
		}

		Assert.Equal([0x0a, 0x22], TypeQuery.ResolveTypes(data, RosterKind.Base, "transport"));
		Assert.Equal([0x22], TypeQuery.ResolveTypes(data, RosterKind.Base, "0x22"));
		Assert.Equal([0], TypeQuery.ResolveTypes(data, RosterKind.Mech, "OUTLAW"));
		Assert.Null(TypeQuery.ResolveTypes(data, RosterKind.Flyer, "NOSUCHTYPE"));
	}

	[Fact]
	public void Flag625() {
		if (Retail.Value is not { } data) {
			return;
		}

		var result = FlagQuery.Run(data, 625);

		Assert.Equal(["C4_09 cond 23 flag 625 > 0", "C4_10 cond 14 flag 625 > 0"],
			result.Tests.Select(t => $"{t.Mission} cond {t.ConditionGuid} {t.Test}"));
		Assert.Equal(["C4_08 14 base GUID 73", "C4_09 14 base GUID 75"],
			result.Writes.Select(w => $"{w.Mission} {w.Row} {w.Subject[..w.Subject.IndexOf(',')]}"));
		Assert.All(result.Writes, w => Assert.Equal("add 1", w.Effect));
	}

	[Fact]
	public void Orders() {
		if (Retail.Value is not { } data) {
			return;
		}

		var result = OrderQuery.Run(data, [], routeSwitch: true, withPoint: false);

		Assert.Equal(637, result.OrderRecords);
		Assert.Equal(new Dictionary<short, int> { [2] = 25, [3] = 1, [4] = 10, [5] = 2, [6] = 9 }, result.PointsByVerb);
		Assert.Equal(14, result.RouteSwitchGroups);
		Assert.Equal(["C1_06", "C2_05", "C2_08", "C4_01", "C4_06", "C5_10"], result.RouteSwitchMissions);

		// C4_06's group 125 starts on a route with no waypoints and is then sent to travel along route 34.
		var group = result.Missions.Single(m => m.Mission == "C4_06").Groups.Single(g => g.Guid == 125);
		Assert.Equal([0, 1, 2], group.Slots.Select(s => s.Slot));
		Assert.Equal(0, group.Slots[0].Records.Single().RouteWaypoints);
		var travel = group.Slots[1].Records.Single();
		Assert.Equal((short)5, travel.Verb);
		Assert.Equal((short)34, travel.RouteRef);
		Assert.True(travel.RouteDiffers);
	}

	[Fact]
	public void BasesDatRoundTrips() {
		if (RetailData.FindDefaultInstall() is not { } install) {
			return;
		}

		var vol = VolFileReader.ParseVolFile(Path.Combine(install, RetailData.ArchiveFolder, RetailData.SimulatorArchive));
		var entry = vol.FilesSet.Single(e => string.Equals(e.FileName?.Trim(), "BASES.DAT", StringComparison.OrdinalIgnoreCase));
		byte[] bytes = entry.RawBytes!;

		var transformer = Assert.IsType<BasesDatTransformer>(TransformerRegistry.FindTransformer(entry));
		var file = transformer.Parse(bytes)!;

		Assert.Equal(65, file.Types.Length);
		Assert.Equal(bytes, transformer.Write(file));
	}

	[Fact]
	public void CommandLineReportsTheFlag() {
		if (Retail.Value is null) {
			return;
		}

		var output = new StringWriter();
		Assert.Equal(0, Program.Run(["flag", "625"], output));
		Assert.StartsWith("Campaign flag 625 across 62 missions: 2 tests, 2 writes.", output.ToString());
	}
}
