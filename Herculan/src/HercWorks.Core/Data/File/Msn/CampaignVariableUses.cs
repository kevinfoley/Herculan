namespace HercWorks.Core.Data.File.Msn;

/// <summary>How a mission's records touch a campaign variable (<see cref="CampaignVariableUses"/>).</summary>
public enum VariableUse {
	/// <summary>A row-1 flag test compares it.</summary>
	Tested,

	/// <summary>A row-10 action adds one to it (<see cref="ActionEditing.CounterIncrement"/>).</summary>
	TriggerAdds,

	/// <summary>A row-10 action clears it (<see cref="ActionEditing.CounterClear"/>).</summary>
	TriggerClears,

	/// <summary>A row-10 action names it with an operation other than those two.</summary>
	TriggerNames,

	/// <summary>A row-17 objective sets it to 1 (<see cref="ObjectiveEditing.CounterSet"/>).</summary>
	ObjectiveSets,

	/// <summary>A row-17 objective clears it (<see cref="ObjectiveEditing.CounterClear"/>).</summary>
	ObjectiveClears,

	/// <summary>A row-17 objective adds one to it (<see cref="ObjectiveEditing.CounterIncrement"/>).</summary>
	ObjectiveAdds,

	/// <summary>A row-17 objective takes one from it (<see cref="ObjectiveEditing.CounterDecrement"/>).</summary>
	ObjectiveSubtracts,

	/// <summary>A row-17 objective names it with an operation other than those four.</summary>
	ObjectiveNames,

	/// <summary>A herc, flyer, structure or group record writes it when out of the fight, its out-of-action report.</summary>
	OutOfFight,

	/// <summary>A row-2 header patch clears it as the mission loads.</summary>
	HeaderClears,
}

/// <summary>One campaign variable a mission touches, and how many of its records touch it each way.</summary>
public sealed record CampaignVariableUse(short Variable, IReadOnlyDictionary<VariableUse, int> Counts);

/// <summary>
/// The campaign variables a mission's <c>.MSN</c> touches, each with how many records touch it each way, read from the file
/// itself, whatever each record's condition, and never from another mission: row 1's flag tests
/// (docs/retail/formats/msn-mission-file.md#the-conditions--row-1), row 10's and row 17's counter pairs
/// (docs/retail/formats/msn-mission-file.md#row-10-field-decode--the-action-record-dat_00470660-82-bytesrecord,
/// docs/retail/simulation/mission-objectives.md#the-record), the out-of-action reports of rows 12 to 14 and 16
/// (docs/retail/simulation/mission-deployment.md#the-out-of-action-report) and the flags row 2's header patches clear
/// (docs/retail/formats/msn-mission-file.md#the-header-patch--row-2). A record counts once per way it touches a variable.
/// </summary>
public static class CampaignVariableUses {
	/// <summary>Every campaign variable <paramref name="mission"/> touches, in number order.</summary>
	public static IReadOnlyList<CampaignVariableUse> Of(MissionFile mission) {
		var counts = new SortedDictionary<short, Dictionary<VariableUse, int>>();

		void Count(IEnumerable<(short Variable, VariableUse Use)> touches) {
			foreach (var (variable, use) in touches.Distinct()) {
				if (variable is < 0 or >= ConditionEditing.Flags) {
					continue;
				}

				if (!counts.TryGetValue(variable, out var uses)) {
					counts[variable] = uses = new Dictionary<VariableUse, int>();
				}

				uses[use] = uses.GetValueOrDefault(use) + 1;
			}
		}

		foreach (var condition in mission.Conditions ?? []) {
			if (condition.Type == ConditionEditing.FlagTest) {
				Count([(condition.FlagIndexOrRangeLower, VariableUse.Tested)]);
			}
		}

		foreach (var action in mission.Actions ?? []) {
			Count(Pairs(action.CounterPairs).Select(pair => (pair.Variable, pair.Op switch {
				ActionEditing.CounterIncrement => VariableUse.TriggerAdds,
				ActionEditing.CounterClear => VariableUse.TriggerClears,
				_ => VariableUse.TriggerNames,
			})));
		}

		foreach (var objective in mission.Objectives ?? []) {
			Count((objective?.Pairs ?? []).Where(pair => pair.CounterRef != -1).Select(pair => (pair.CounterRef, pair.Op switch {
				ObjectiveEditing.CounterSet => VariableUse.ObjectiveSets,
				ObjectiveEditing.CounterClear => VariableUse.ObjectiveClears,
				ObjectiveEditing.CounterIncrement => VariableUse.ObjectiveAdds,
				ObjectiveEditing.CounterDecrement => VariableUse.ObjectiveSubtracts,
				_ => VariableUse.ObjectiveNames,
			})));
		}

		var reports = (mission.Mechs ?? []).Select(record => record.OutOfActionReport)
			.Concat((mission.Flyers ?? []).Select(record => record.OutOfActionReport))
			.Concat((mission.Bases ?? []).Select(record => record.OutOfActionReport))
			.Concat((mission.Groups ?? []).Select(record => record.OutOfActionReport));
		foreach (var report in reports) {
			Count(Pairs(report).Select(pair => (pair.Variable, VariableUse.OutOfFight)));
		}

		foreach (var patch in mission.SettingsPatches ?? []) {
			Count(patch.Data.Skip(MissionSettingsPatch.ClearListStart).Where(flag => flag != -1)
				.Select(flag => (flag, VariableUse.HeaderClears)));
		}

		return counts.Select(entry => new CampaignVariableUse(entry.Key, entry.Value)).ToList();
	}

	// The filled pairs of interleaved (variable, operation) words, a variable of -1 for an unused one.
	private static IEnumerable<(short Variable, short Op)> Pairs(short[] words) {
		for (int i = 0; i + 1 < words.Length; i += 2) {
			if (words[i] != -1) {
				yield return (words[i], words[i + 1]);
			}
		}
	}
}
