using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Writes the type of a herc, flyer, structure or ground vehicle and a herc's weapon fit and ammunition types, as record
/// edits on a <see cref="MissionDocument"/> (docs/herculan/handoff-editor-roster.md#the-players-squad and docs/herculan/handoff-editor-conditions.md#variants).
///
/// <list type="bullet">
/// <item>Asked to write the alternative, a field the record copies from a variant alternative of a table the editor edits
/// (<see cref="VariantEditing.WhyNoDrawnField"/>) is written there (<see cref="MissionDocument.SetAlternativeField"/>), so
/// every record drawing that alternative changes, and an unset value is written as any other.</item>
/// <item>Any other field is written in the record that supplies it (<see cref="MissionDocument.SetField"/>): an overlay
/// record for a field a variant supplies. An overlay cannot carry a field's unset value, so such a field keeps the variant's
/// value where the edit would unset it, and a launcher's ammunition type is then written SARH (0), which DBSIM reads the
/// filler 5 as: the editor's choice (docs/herculan/handoff-editor-roster.md#the-players-squad). Asked to write the alternative, a field of the record's own that the edit unsets, which the alternative then
/// supplies again, is written in the alternative too.</item>
/// </list>
/// </summary>
public static class RosterFit {
	/// <summary>The weapon slots a row-12 record carries.</summary>
	public const int Slots = 10;

	/// <summary>The guidance kind DBSIM reads the filler 5 as on a launcher.</summary>
	public const short Sarh = 0;

	/// <summary>
	/// The fields a herc's chassis change writes: its type <paramref name="type"/>, and in each of the ten slots
	/// <paramref name="weapons"/>' weapon and <paramref name="ammo"/>'s ammunition type, the whole fit a new herc of the type
	/// gets, so no slot keeps the old chassis's weapon.
	/// </summary>
	public static IReadOnlyList<(MsnField Field, short Value)> Chassis(short type, short[] weapons, short[] ammo) {
		var fields = new List<(MsnField, short)> { (MsnField.MechType, type) };
		for (int slot = 0; slot < Slots; slot++) {
			fields.Add((MsnField.MechWeapon(slot), weapons[slot]));
			fields.Add((MsnField.MechAmmo(slot), ammo[slot]));
		}

		return fields;
	}

	/// <summary>
	/// The generated value of <paramref name="field"/> (a type, a weapon or an ammunition type) of roster GUID
	/// <paramref name="guid"/>'s record, as <c>script.dat</c> holds it. Throws <see cref="InvalidOperationException"/> for a
	/// GUID no record exports.
	/// </summary>
	public static short Current(MissionDocument document, MsnField field, short guid) {
		int index = document.ExportedIndex(field.Row, guid)
			?? throw new InvalidOperationException($"No record of row {field.Row} with GUID {guid} reaches script.dat.");
		int word = field.Word;
		switch (field.Row) {
			case 13 when word == FlyerRosterEntry102.TypeIndexWord:
				return document.Script.Flyers[index].TypeIndex;
			case 14 when word == BaseRosterEntry62.TypeIndexWord:
				return document.Script.Bases[index].TypeIndex;
			case 12:
				var record = document.Script.Mechs[index];
				if (word == MechRosterEntry144.TypeIndexWord) {
					return record.TypeIndex;
				}

				if (word >= MechRosterEntry144.WeaponSecondaryWord) {
					int mount = word - MechRosterEntry144.WeaponSecondaryWord;
					return mount < record.WeaponSecondary.Length ? record.WeaponSecondary[mount] : field.Unset;
				}

				return record.WeaponRefs[word - MechRosterEntry144.WeaponRefsWord];
			default:
				throw new ArgumentOutOfRangeException(nameof(field), $"Row {field.Row}'s word {word} is not a type or a fit.");
		}
	}

	/// <summary>Whether any of <paramref name="fields"/> of GUID <paramref name="guid"/> is generated as other than its value.</summary>
	public static bool Differs(MissionDocument document, short guid, IReadOnlyList<(MsnField Field, short Value)> fields) =>
		fields.Any(field => Current(document, field.Field, guid) != field.Value);

	/// <summary>
	/// The variant alternative an edit of <paramref name="fields"/> of GUID <paramref name="guid"/> asked to write the
	/// alternative changes: the one the first of them that <see cref="VariantEditing.WhyNoDrawnField"/> passes is copied from;
	/// null when none is.
	/// </summary>
	public static MsnRecordId? DrawnAlternative(MissionDocument document, short guid, IEnumerable<MsnField> fields) =>
		fields.Where(field => VariantEditing.WhyNoDrawnField(document, field, guid) == null)
			.Select(field => document.AlternativeOf(field, guid)).FirstOrDefault();

	/// <summary>
	/// Writes each of <paramref name="fields"/> of roster GUID <paramref name="guid"/> whose generated value differs, by the
	/// rules in the class summary, as one <see cref="MissionDocument.Change"/>: in the alternative it draws the field from when
	/// <paramref name="toAlternative"/>, else in the record that supplies it. <paramref name="takesGuidance"/> says whether a
	/// weapon id takes a guidance kind, which a launcher's ammunition type falls back to SARH for.
	/// </summary>
	public static MsnEdit Write(MissionDocument document, short guid, IReadOnlyList<(MsnField Field, short Value)> fields,
			bool toAlternative, Func<short, bool> takesGuidance) =>
		document.Change(() => {
			foreach (var (field, value) in fields) {
				if (Current(document, field, guid) == value) {
					continue;
				}

				if (toAlternative && VariantEditing.WhyNoDrawnField(document, field, guid) == null) {
					document.SetAlternativeField(field, guid, value);
					continue;
				}

				if (value != field.Unset || !document.SuppliedByVariant(field, guid)) {
					document.SetField(field, guid, value);
				}

				// An overlay's field unset, which the alternative then supplies again.
				if (toAlternative && Current(document, field, guid) != value && VariantEditing.WhyNoDrawnField(document, field, guid) == null) {
					document.SetAlternativeField(field, guid, value);
					continue;
				}

				// An unset value a variant still supplies something else for.
				if (Current(document, field, guid) != value && field.Row == 12 && field.Word >= MechRosterEntry144.WeaponSecondaryWord
						&& Launcher(document, guid, field.Word, takesGuidance) && Current(document, field, guid) != Sarh) {
					document.SetField(field, guid, Sarh);
				}
			}
		});

	// Whether the mount whose ammunition type is at row-12 word <ammoWord> holds, as generated now, a weapon that takes a
	// guidance kind.
	private static bool Launcher(MissionDocument document, short guid, int ammoWord, Func<short, bool> takesGuidance) {
		int mount = ammoWord - MechRosterEntry144.WeaponSecondaryWord;
		return takesGuidance(Current(document, MsnField.MechWeapon(mount), guid));
	}
}
