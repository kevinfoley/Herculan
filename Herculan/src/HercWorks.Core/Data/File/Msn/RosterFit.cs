using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Writes the type of a herc, flyer, structure or ground vehicle and a herc's weapon fit and ammunition types, as record
/// edits on a <see cref="MissionDocument"/> (docs/herculan/handoff-editor-roster.md#the-players-squad and docs/herculan/handoff-editor-conditions.md#editing-a-record-that-picks).
///
/// <list type="bullet">
/// <item>Given the option being edited, a template of the table the record picks from, one the editor edits
/// (<see cref="RandomTableEditing.WhyNoOptionEdit"/>), a field the record copies from a template
/// (<see cref="RandomTableEditing.WhyNoTemplateField"/>) is written in that option's template, so every record picking that
/// option changes, and an unset value is written as any other. The span a pick copies is the same whichever option it picks,
/// so a field copied from the option the layout shows is copied from every option.</item>
/// <item>Any other field is written in the record that supplies it (<see cref="MissionDocument.SetField"/>): an overlay
/// record for a field a template supplies. An overlay cannot carry a field's unset value, so such a field keeps the template's
/// value where the edit would unset it, and a launcher's ammunition type is then written SARH (0), which DBSIM reads the
/// filler 5 as: the editor's choice (docs/herculan/handoff-editor-roster.md#the-players-squad). Given an option, a field of the
/// record's own that the edit unsets, which the template then supplies again, is written in the option's template too.</item>
/// </list>
/// </summary>
public static class RosterFit {
	/// <summary>The weapon slots a row-12 record carries.</summary>
	public const int Slots = 10;

	/// <summary>The guidance kind DBSIM reads the filler 5 as on a launcher.</summary>
	public const short Sarh = 0;

	/// <summary>The ammunition type retail records carry in a slot that holds no launcher (docs/retail/simulation/weapon-mounts.md#the-second-loadout-array-is-the-ammunition-type).</summary>
	public const short Filler = 5;

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
	/// The value of <paramref name="field"/> (a type, a weapon or an ammunition type) in roster GUID <paramref name="guid"/>'s
	/// base (<see cref="MissionDocument.Layout"/>), the option it shows copied in; given <paramref name="option"/>, a template
	/// of its table, as a load picking that option makes it: the template's value where a template supplies the field, the
	/// base's where the record supplies it itself. Throws <see cref="InvalidOperationException"/> for a GUID no record carries.
	/// </summary>
	public static short Current(MissionDocument document, MsnField field, short guid, MsnRecordId? option = null) {
		bool typeOrFit = field.Row switch {
			12 => field.Word == MechRosterEntry144.TypeIndexWord
				|| field.Word >= MechRosterEntry144.WeaponRefsWord && field.Word < MechRosterEntry144.WeaponRefsWord + Slots
				|| field.Word >= MechRosterEntry144.WeaponSecondaryWord && field.Word < MechRosterEntry144.WeaponSecondaryWord + Slots,
			13 => field.Word == FlyerRosterEntry102.TypeIndexWord,
			14 => field.Word == BaseRosterEntry62.TypeIndexWord,
			_ => false,
		};
		if (!typeOrFit) {
			throw new ArgumentOutOfRangeException(nameof(field), $"Row {field.Row}'s word {field.Word} is not a type or a fit.");
		}

		var entry = document.Layout.Entry(field.Row, guid)
			?? throw new InvalidOperationException($"Row {field.Row} has no record with GUID {guid}.");
		return option is { } template && document.SuppliedByTemplate(field, guid) && document.RecordAt(template) is { } copied
			? field.Get(copied)
			: field.Get(entry.Fields);
	}

	/// <summary>
	/// Whether any of <paramref name="fields"/> of GUID <paramref name="guid"/> is other than its value in the base, or with
	/// <paramref name="option"/> as a load picking that option makes it (<see cref="Current"/>).
	/// </summary>
	public static bool Differs(MissionDocument document, short guid, IReadOnlyList<(MsnField Field, short Value)> fields,
			MsnRecordId? option = null) =>
		fields.Any(field => Current(document, field.Field, guid, option) != field.Value);

	/// <summary>
	/// The template of the option the layout shows GUID <paramref name="guid"/> with that the first of <paramref name="fields"/>
	/// <see cref="RandomTableEditing.WhyNoTemplateField"/> passes is copied from; null when none is.
	/// </summary>
	public static MsnRecordId? PickedTemplate(MissionDocument document, short guid, IEnumerable<MsnField> fields) =>
		fields.Where(field => RandomTableEditing.WhyNoTemplateField(document, field, guid) == null)
			.Select(field => document.TemplateOf(field, guid)).FirstOrDefault();

	/// <summary>
	/// Writes each of <paramref name="fields"/> of roster GUID <paramref name="guid"/> whose value differs
	/// (<see cref="Current"/>), by the rules in the class summary, as one <see cref="MissionDocument.Change"/>: a field a
	/// template supplies in <paramref name="option"/>, the template of the option being edited, when one is given, else in the
	/// record that supplies it. <paramref name="takesGuidance"/> says whether a weapon id takes a guidance kind, which a
	/// launcher's ammunition type falls back to SARH for. Throws <see cref="InvalidOperationException"/> for an option
	/// <see cref="RandomTableEditing.WhyNoOptionEdit"/> refuses.
	/// </summary>
	public static MsnEdit Write(MissionDocument document, short guid, IReadOnlyList<(MsnField Field, short Value)> fields,
			MsnRecordId? option, Func<short, bool> takesGuidance) {
		if (option is { } given && fields.Count > 0
				&& RandomTableEditing.WhyNoOptionEdit(document, fields[0].Field.Row, guid, given) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return document.Change(() => {
			foreach (var (field, value) in fields) {
				if (Current(document, field, guid, option) == value) {
					continue;
				}

				if (option is { } template && RandomTableEditing.WhyNoTemplateField(document, field, guid) == null) {
					document.RewriteAt<MapObject>(template.Row, template.Index, record => field.Set(record, value));
					continue;
				}

				if (value != field.Unset || !document.SuppliedByTemplate(field, guid)) {
					document.SetField(field, guid, value);
				}

				// An overlay's field unset, which the template then supplies again.
				if (option is { } again && Current(document, field, guid, option) != value
						&& RandomTableEditing.WhyNoTemplateField(document, field, guid) == null) {
					document.RewriteAt<MapObject>(again.Row, again.Index, record => field.Set(record, value));
					continue;
				}

				// An unset value a template still supplies something else for.
				if (Current(document, field, guid, option) != value && field.Row == 12 && field.Word >= MechRosterEntry144.WeaponSecondaryWord
						&& Launcher(document, guid, field.Word, option, takesGuidance) && Current(document, field, guid, option) != Sarh) {
					document.SetField(field, guid, Sarh);
				}
			}
		});
	}

	// Whether the mount whose ammunition type is at row-12 word <ammoWord> holds, as it stands now in <option> or the base, a
	// weapon that takes a guidance kind.
	private static bool Launcher(MissionDocument document, short guid, int ammoWord, MsnRecordId? option, Func<short, bool> takesGuidance) {
		int mount = ammoWord - MechRosterEntry144.WeaponSecondaryWord;
		return takesGuidance(Current(document, MsnField.MechWeapon(mount), guid, option));
	}
}
