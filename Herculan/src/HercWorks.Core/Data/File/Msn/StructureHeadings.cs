namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// A record of a structure's GUID under a condition other than its base's that names a heading, and the heading's degrees, or
/// null for a heading the mission does not have, which resolves to none.
/// </summary>
public sealed record HeadingOverride(short Condition, short? Degrees);

/// <summary>
/// The own heading of a structure or ground vehicle (row 14), in whole degrees, as the row-7 record its GUID's base names
/// (docs/retail/formats/msn-mission-file.md, "Row #7 field decode"), and the edit of it. With none, the structure faces its
/// group's heading turned by its formation slot's own turn; one of its own replaces both
/// (docs/retail/formats/script-dat.md, "A base formation slot also turns the structure").
///
/// <para>An edit leaves the records under other conditions alone. A heading is written into the row-7 record the base names
/// when nothing else names that record (<see cref="Private"/>); otherwise the base is pointed at a shareable heading of the same
/// degrees (<see cref="MissionDocument.ShareableHeading"/>) or at one appended. Taking it away unsets the base's own records
/// only (<see cref="MissionDocument.UnsetBaseField"/>) and removes a heading record nothing else names.</para>
/// </summary>
public static class StructureHeadings {
	private const int StructureRow = 14;
	private const int HeadingRow = 7;

	private static readonly MsnField Field = MsnField.RosterHeading(StructureRow);

	/// <summary>Row-14 GUID <paramref name="guid"/>'s own heading in degrees; null when its base names none, or one the mission lacks.</summary>
	public static short? Degrees(MissionDocument document, short guid) => DegreesOf(document, Field.Get(Entry(document, guid).Fields));

	/// <summary>
	/// Each condition other than the base's under which a record of the GUID names a heading, in file order, with the heading
	/// the last such record names.
	/// </summary>
	public static IReadOnlyList<HeadingOverride> Overrides(MissionDocument document, short guid) {
		var overrides = new List<HeadingOverride>();
		foreach (var variation in Entry(document, guid).Variations.Where(variation => variation.Words.Contains(Field.Word))) {
			var set = new HeadingOverride(variation.Condition, DegreesOf(document, Field.Get(variation.Fields)));
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
	/// Sets row-14 GUID <paramref name="guid"/>'s own heading to <paramref name="degrees"/>, taken to 0 to 359, or with null
	/// takes it away, by the rules in the class summary. Throws <see cref="InvalidOperationException"/> for a GUID no record
	/// carries, and for null where a variant the base draws supplies a heading, which an overlay cannot unset.
	/// </summary>
	public static MsnEdit Set(MissionDocument document, short guid, short? degrees) {
		short named = Field.Get(Entry(document, guid).Fields);
		bool ownsNamed = named != -1 && Private(document, named);
		return document.Change(() => {
			if (degrees is not { } wanted) {
				document.UnsetBaseField(Field, guid);
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

			document.SetField(Field, guid, shared);
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

	private static short? DegreesOf(MissionDocument document, short heading) =>
		heading != -1 && document.Layout.Entry<Heading10>(heading)?.Fields is { } record ? record.Degrees : null;

	private static LayoutEntry Entry(MissionDocument document, short guid) =>
		document.Layout.Entry(StructureRow, guid) ?? throw new InvalidOperationException($"Row {StructureRow} has no record with GUID {guid}.");
}
