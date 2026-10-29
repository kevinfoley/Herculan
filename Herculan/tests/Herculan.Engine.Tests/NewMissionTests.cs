using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary><see cref="NewMission.Build"/>, written and generated as a mission load writes its <c>script.dat</c>.</summary>
public class NewMissionTests {
	/// <summary>
	/// Catches a GUID renumbered in one record so a ref to it resolves to <c>-1</c> (the mission check allows an unset
	/// ref, so each ref the build sets is checked here), the box corners dropped or moved so the start sits on or outside
	/// block 1's extent, and the route losing its nav point so the objective holds where the player stands.
	/// </summary>
	[Fact]
	public void BuildsAMissionWhoseObjectiveRouteLeadsAwayFromTheStartInsideTheBox() {
		var (mission, text) = NewMission.Build(theater: 1, zone: 111, night: true, zoneWidth: 2_000_000, zoneHeight: 1_500_000);
		byte[] msn = new MissionFileTransformer().Write(mission)!;
		byte[] eng = new MissionStringFileTransformer().Write(text)!;
		var generator = MissionGenerator.Load(msn, eng, new short[MissionGenerator.CampaignFlagCount],
			new short[MissionGenerator.ClearListLength], _ => 0);
		var script = (ScriptDat)new ScriptDatTransformer().Parse(generator.WriteScriptDat())!;

		Assert.Empty(ScriptDatLint.Check(script));

		var squad = script.Groups[0];
		var order = script.Orders[Resolved(squad.OrderRefs[0], script.Orders.Length)];
		var objective = Assert.Single(script.Objectives);
		Resolved(squad.HeadingRef, script.Headings.Length);
		Resolved(squad.RouteRef, script.WaypointGroups.Length);
		Resolved(order.RouteRef, script.WaypointGroups.Length);
		Resolved(objective.SubjectRef, script.Mechs.Length);

		var start = script.Coordinates[Resolved(squad.PositionRef, script.Coordinates.Length)];
		Assert.InRange(start.X, script.Coordinates.Min(c => c.X) + 1, script.Coordinates.Max(c => c.X) - 1);
		Assert.InRange(start.Y, script.Coordinates.Min(c => c.Y) + 1, script.Coordinates.Max(c => c.Y) - 1);

		var route = script.WaypointGroups[Resolved(objective.RouteRef, script.WaypointGroups.Length)];
		var last = script.Coordinates[Resolved(route.Waypoints[^1], script.Coordinates.Length)];
		Assert.NotEqual((start.X, start.Y), (last.X, last.Y));
	}

	private static int Resolved(short reference, int count) {
		Assert.InRange(reference, 0, count - 1);
		return reference;
	}
}
