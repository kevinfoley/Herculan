namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// A record of a GUID under a condition other than its base's that names a heading, and the heading's degrees, or null for a
/// heading the mission does not have, which resolves to none.
/// </summary>
public sealed record HeadingOverride(short Condition, short? Degrees);

/// <summary>
/// The own heading of a structure or ground vehicle (row 14) or of a group (row 16), in whole degrees, as the row-7 record its
/// GUID's base names (docs/retail/formats/msn-mission-file.md, "Row #7 field decode"), and the edit of it. With none, a
/// structure faces its group's heading turned by its formation slot's own turn, and one of its own replaces both
/// (docs/retail/formats/script-dat.md, "A base formation slot also turns the structure"); a group faces along its first
/// order's route's first leg, or 0 with no leg, and its heading turns its formation and every member with none of its own
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 4 and 7).
///
/// <para>An edit leaves the records under other conditions alone. A heading is written into the row-7 record the base names
/// when nothing else names that record (<see cref="Private"/>); otherwise the base is pointed at a shareable heading of the same
/// degrees (<see cref="MissionDocument.ShareableHeading"/>) or at one appended. Taking it away unsets the base's own records
/// only (<see cref="MissionDocument.UnsetBaseField"/>) and removes a heading record nothing else names.</para>
/// </summary>
public static class OwnHeadings {
	private const int StructureRow = 14;
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int HeadingRow = 7;

	/// <summary>Whether a heading of row <paramref name="row"/>'s own is edited here: the structures' and the groups'.</summary>
	public static bool Carries(int row) => row is StructureRow or GroupRow;

	/// <summary>Row <paramref name="row"/>'s GUID <paramref name="guid"/>'s own heading in degrees; null when its base names none, or one the mission lacks.</summary>
	public static short? Degrees(MissionDocument document, int row, short guid) =>
		DegreesOf(document, Field(row).Get(Entry(document, row, guid).Fields));

	/// <summary>
	/// Each condition other than the base's under which a record of the GUID names a heading, in file order, with the heading
	/// the last such record names.
	/// </summary>
	public static IReadOnlyList<HeadingOverride> Overrides(MissionDocument document, int row, short guid) {
		var field = Field(row);
		var overrides = new List<HeadingOverride>();
		foreach (var variation in Entry(document, row, guid).Variations.Where(variation => variation.Words.Contains(field.Word))) {
			var set = new HeadingOverride(variation.Condition, DegreesOf(document, field.Get(variation.Fields)));
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
	/// Why row <paramref name="row"/>'s GUID <paramref name="guid"/>'s heading cannot be taken away, or null when it can: a
	/// group whose base's first order slot names no order, as DBSIM faults loading a group with neither
	/// (<see cref="MissionCheckKind.NoHeadingNoOrder"/>).
	/// </summary>
	public static string? WhyNoNone(MissionDocument document, int row, short guid) =>
		row == GroupRow && Entry(document, row, guid).Fields is MissionGroup164 group
			&& document.Layout.Entry(OrderRow, group.OrderRefs[0]) == null
			? "It has no first order to take its heading from, and the mission crashes loading a group with neither."
			: null;

	/// <summary>
	/// Sets row <paramref name="row"/>'s GUID <paramref name="guid"/>'s own heading to <paramref name="degrees"/>, taken to 0
	/// to 359, or with null takes it away, by the rules in the class summary. Throws <see cref="InvalidOperationException"/>
	/// for a GUID no record carries, where <see cref="WhyNoNone"/> answers, and for null where a variant the base draws
	/// supplies a heading, which an overlay cannot unset.
	/// </summary>
	public static MsnEdit Set(MissionDocument document, int row, short guid, short? degrees) {
		var field = Field(row);
		short named = field.Get(Entry(document, row, guid).Fields);
		bool ownsNamed = named != -1 && Private(document, named);
		if (degrees == null && WhyNoNone(document, row, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return document.Change(() => {
			if (degrees is not { } wanted) {
				document.UnsetBaseField(field, guid);
				if (ownsNamed) {
					document.Remove(HeadingRow, named);
				}

				return;
			}

			short turned = (short)((wanted % 360 + 360) % 360);
			if (ownsNamed) {
				document.RewriteEvery<Heading10>(HeadingRow, named, heading => heading.Degrees = turned);
				return;
			}

			short shared = document.ShareableHeading(turned);
			if (shared == -1) {
				document.Append(HeadingRow, new Heading10 { ConditionRef = -1, VariantKey = -1, Unk06 = -1, Degrees = turned }, out shared);
			}

			document.SetField(field, guid, shared);
		});
	}

	/// <summary>
	/// Whether row-7 GUID <paramref name="heading"/> is one record, unconditioned and drawing no variant, that one field alone
	/// names: the heading fields of rows 12, 13, 14 and 16, every record counted whatever its condition, variant alternatives
	/// included. Such a record can be rewritten without turning anything else.
	/// </summary>
	public static bool Private(MissionDocument document, short heading) {
		var records = (document.Mission.Headings ?? []).Where(record => record.GUID == heading).ToList();
		if (records is not [{ ConditionRef: -1, VariantKey: -1 }]) {
			return false;
		}

		var mission = document.Mission;
		int named = (mission.Mechs ?? []).Count(record => record.HeadingRef == heading)
			+ (mission.Flyers ?? []).Count(record => record.HeadingRef == heading)
			+ (mission.Bases ?? []).Count(record => record.HeadingRef == heading)
			+ (mission.Groups ?? []).Count(record => record.HeadingRef == heading);
		return named == 1;
	}

	private static MsnField Field(int row) => row switch {
		StructureRow => MsnField.RosterHeading(StructureRow),
		GroupRow => MsnField.GroupHeading,
		_ => throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 14 and 16 carry a heading edited here."),
	};

	private static short? DegreesOf(MissionDocument document, short heading) =>
		heading != -1 && document.Layout.Entry<Heading10>(heading)?.Fields is { } record ? record.Degrees : null;

	private static LayoutEntry Entry(MissionDocument document, int row, short guid) =>
		document.Layout.Entry(row, guid) ?? throw new InvalidOperationException($"Row {row} has no record with GUID {guid}.");
}
