namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// A record of a herc's or structure's GUID under a condition other than its base's that sets the starting condition, and the
/// value it sets when that condition holds.
/// </summary>
public sealed record StartingConditionOverride(short Condition, short Value);

/// <summary>
/// The starting condition of a herc (row 12) or a structure or ground vehicle (row 14) as its GUID's base states it, the
/// values records under other conditions set over it, which is how a campaign carries a structure's destruction into a later
/// mission (docs/retail/campaign-continuity.md), and the edit of the base's value.
///
/// <para>The edit leaves those records alone. A value other than 100 is written in the base's supplier
/// (<see cref="MissionDocument.SetField"/>); 100, the field's unset value, is written in the base's records only
/// (<see cref="MissionDocument.UnsetBaseField"/>), where <see cref="MissionDocument.SetField"/> would unset it in every
/// record of the GUID and take the carry-over with it.</para>
/// </summary>
public static class StartingConditions {
	/// <summary>Whether row <paramref name="row"/>'s records carry a starting condition: the herc and structure rosters.</summary>
	public static bool Carries(int row) => row is 12 or 14;

	/// <summary>Roster row <paramref name="row"/>'s GUID <paramref name="guid"/>'s starting condition in its base; throws for a GUID no record carries.</summary>
	public static short Current(MissionDocument document, int row, short guid) =>
		MsnField.StartingCondition(row).Get(Entry(document, row, guid).Fields);

	/// <summary>
	/// Each condition other than the base's under which a record of the GUID sets the starting condition, in file order, with
	/// the value the last such record sets.
	/// </summary>
	public static IReadOnlyList<StartingConditionOverride> Overrides(MissionDocument document, int row, short guid) {
		var field = MsnField.StartingCondition(row);
		var overrides = new List<StartingConditionOverride>();
		foreach (var variation in Entry(document, row, guid).Variations.Where(variation => variation.Words.Contains(field.Word))) {
			var set = new StartingConditionOverride(variation.Condition, field.Get(variation.Fields));
			int at = overrides.FindIndex(known => known.Condition == variation.Condition);
			if (at >= 0) {
				overrides[at] = set;
			} else {
				overrides.Add(set);
			}
		}

		return overrides;
	}

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
	/// <paramref name="value"/>, by the rules in the class summary. Throws <see cref="InvalidOperationException"/> where
	/// <see cref="WhyFixed"/> answers, and for 100 where a template the base picks supplies another value, which an overlay
	/// cannot unset.
	/// </summary>
	public static MsnEdit Set(MissionDocument document, int row, short guid, short value) {
		if (WhyFixed(document, row, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var field = MsnField.StartingCondition(row);
		if (value != field.Unset) {
			return document.Change(() => document.SetField(field, guid, value));
		}

		try {
			return document.Change(() => document.UnsetBaseField(field, guid));
		} catch (InvalidOperationException) when (Current(document, row, guid) != value) {
			throw new InvalidOperationException($"The random table it picks from gives it another starting condition, so it "
				+ $"can't be set to {value}% here.");
		}
	}

	private static LayoutEntry Entry(MissionDocument document, int row, short guid) =>
		document.Layout.Entry(row, guid) ?? throw new InvalidOperationException($"Row {row} has no record with GUID {guid}.");
}
