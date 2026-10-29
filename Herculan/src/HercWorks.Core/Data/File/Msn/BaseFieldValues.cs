namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// A record of a roster object's GUID under a condition other than its base's that sets a field, and the value it sets when
/// that condition holds.
/// </summary>
public sealed record FieldOverride(short Condition, short Value);

/// <summary>
/// A roster field as a GUID's base states it, the values records under other conditions set over it, and the edit of the
/// base's value, which leaves those records alone: the shared half of <see cref="StartingConditions"/> and
/// <see cref="RadarSettings"/>.
///
/// <para>A value other than the field's unset value is written in the base's supplier (<see cref="MissionDocument.SetField"/>);
/// the unset value is written in the base's records only (<see cref="MissionDocument.UnsetBaseField"/>), where
/// <see cref="MissionDocument.SetField"/> would unset it in every record of the GUID and take the other versions' values with
/// it.</para>
/// </summary>
public static class BaseFieldValues {
	/// <summary><paramref name="field"/> of its row's GUID <paramref name="guid"/> in its base; throws for a GUID no record carries.</summary>
	public static short Current(MissionDocument document, MsnField field, short guid) => field.Get(Entry(document, field.Row, guid).Fields);

	/// <summary>
	/// Each condition other than the base's under which a record of the GUID sets <paramref name="field"/>, in file order, with
	/// the value the last such record sets.
	/// </summary>
	public static IReadOnlyList<FieldOverride> Overrides(MissionDocument document, MsnField field, short guid) {
		var overrides = new List<FieldOverride>();
		foreach (var variation in Entry(document, field.Row, guid).Variations.Where(variation => variation.Words.Contains(field.Word))) {
			var set = new FieldOverride(variation.Condition, field.Get(variation.Fields));
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
	/// Sets <paramref name="field"/> of its row's GUID <paramref name="guid"/> in its base to <paramref name="value"/>, by the
	/// rules in the class summary. Throws <see cref="InvalidOperationException"/> with <paramref name="templateRefusal"/> for
	/// the unset value where a template the base picks supplies another value, which an overlay cannot unset.
	/// </summary>
	public static MsnEdit Set(MissionDocument document, MsnField field, short guid, short value, string templateRefusal) {
		if (value != field.Unset) {
			return document.Change(() => document.SetField(field, guid, value));
		}

		try {
			return document.Change(() => document.UnsetBaseField(field, guid));
		} catch (InvalidOperationException) when (Current(document, field, guid) != value) {
			throw new InvalidOperationException(templateRefusal);
		}
	}

	private static LayoutEntry Entry(MissionDocument document, int row, short guid) =>
		document.Layout.Entry(row, guid) ?? throw new InvalidOperationException($"Row {row} has no record with GUID {guid}.");
}
