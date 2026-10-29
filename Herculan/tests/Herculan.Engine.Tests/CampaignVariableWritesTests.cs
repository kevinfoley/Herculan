using Herculan.Engine.Editor;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The editor's sentence for a campaign variable write against what the simulation does with it: each layer's codes, those
/// the editor offers and the ones around them, run through the layer's own runtime walk on a variable holding 5. Fails when
/// <see cref="CampaignVariableWrites"/> reads a code as the wrong operation (the action layer's 5 and 6 swapped, the
/// objective layer's 4 read as a clear), or when a code the editor writes (<c>ActionEditing</c>, <c>ObjectiveEditing</c>)
/// stops being the one the runtime applies.
/// </summary>
public class CampaignVariableWritesTests {
	private const short Variable = 30;
	private const short Start = 5;

	[Fact]
	public void EveryCodeDoesWhatItsSentenceSays() {
		foreach (var layer in Enum.GetValues<WriteLayer>()) {
			for (short code = -1; code <= 0x12; code++) {
				var operation = CampaignVariableWrites.Operation(layer, code);
				int expected = operation.Kind switch {
					WriteKind.Add => Start + operation.Value,
					WriteKind.Subtract => Start - operation.Value,
					WriteKind.Set => operation.Value,
					_ => Start,
				};

				Assert.True(expected == Applied(layer, code),
					$"{layer} code {code}: \"{CampaignVariableWrites.Sentence(layer, Variable, code)}\", but the variable ends at {Applied(layer, code)}.");
			}
		}
	}

	// Variable <Variable>, holding <Start>, after one write of <code> by <layer>'s runtime walk.
	private static short Applied(WriteLayer layer, short code) {
		var world = new SimWorld(FlatTerrain());
		var counters = new short[MissionRuntime.CounterSlots];
		counters[Variable] = Start;
		world.Mission.LoadCounters(counters);
		switch (layer) {
			case WriteLayer.Action:
				new MissionActionState(new MissionAction(0, 0, [], [Variable], [code], -1, -1)).Activate(world);
				break;
			case WriteLayer.Objective:
				new MissionObjectiveState(new MissionObjective(MissionObjective.Mandatory, 1, MissionObjectiveSubject.Group, 0, null,
					-1, -1, [Variable], [code])).ApplyCounters(world);
				break;
			default:
				world.Mission.ApplyOutOfActionReport(new OutOfActionReport([Variable], [code]));
				break;
		}

		return world.Mission.Counters[Variable];
	}

	private static HeightGrid FlatTerrain() {
		const int widthShift = 4;
		const int cellCount = 1 << (widthShift * 2);
		return new HeightGrid(widthShift, widthShift, 12, 16, 10, new byte[cellCount], new byte[cellCount]);
	}
}
