namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// The starting condition of a herc (row 12) or a structure or ground vehicle (row 14) as its GUID's base states it, the
/// values records under other conditions set over it, which is how a campaign carries a structure's destruction into a later
/// mission (docs/retail/campaign-continuity.md), and the edit of the base's value, by <see cref="BaseFieldValues"/>' rules:
/// 100 is the field's unset value.
/// </summary>
public static class StartingConditions {
	/// <summary>Whether row <paramref name="row"/>'s records carry a starting condition: the herc and structure rosters.</summary>
	public static bool Carries(int row) => row is 12 or 14;

	/// <summary>Roster row <paramref name="row"/>'s GUID <paramref name="guid"/>'s starting condition in its base; throws for a GUID no record carries.</summary>
	public static short Current(MissionDocument document, int row, short guid) =>
		BaseFieldValues.Current(document, MsnField.StartingCondition(row), guid);

	/// <inheritdoc cref="BaseFieldValues.Overrides"/>
	public static IReadOnlyList<FieldOverride> Overrides(MissionDocument document, int row, short guid) =>
		BaseFieldValues.Overrides(document, MsnField.StartingCondition(row), guid);

	/// <summary>
	/// Why roster row <paramref name="row"/>'s GUID <paramref name="guid"/>'s starting condition cannot be set, or null when
	/// it can: as for its own two actions (<see cref="ActionWiring.WhyNoObjectAction"/>), a machine of the player's squad is
	/// built from <c>player.mec</c>, which holds its condition.
	/// </summary>
	public static string? WhyFixed(MissionDocument document, int row, short guid) {
		if (!Carries(row)) {
			throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 12 and 14 carry a starting condition.");
		}

		return ActionWiring.WhyNoObjectAction(document, row, guid);
	}

	/// <summary>
	/// Sets roster row <paramref name="row"/>'s GUID <paramref name="guid"/>'s starting condition in its base to
	/// <paramref name="value"/> (<see cref="BaseFieldValues.Set"/>). Throws <see cref="InvalidOperationException"/> where
	/// <see cref="WhyFixed"/> answers, and for 100 where a template the base picks supplies another value.
	/// </summary>
	public static MsnEdit Set(MissionDocument document, int row, short guid, short value) {
		if (WhyFixed(document, row, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return BaseFieldValues.Set(document, MsnField.StartingCondition(row), guid, value,
			$"The random table it picks from gives it another starting condition, so it can't be set to {value}% here.");
	}
}
