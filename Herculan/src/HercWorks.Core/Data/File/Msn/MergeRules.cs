namespace HercWorks.Core.Data.File.Msn;

/// <summary>A run of <paramref name="Count"/> words from <paramref name="First"/> that an overlay copies, skipping each word that reads <paramref name="Unset"/>.</summary>
public readonly record struct OverlaySpan(int First, int Count, short Unset);

/// <summary>A run of <paramref name="Count"/> words from <paramref name="First"/> that a variant copies from the record it draws.</summary>
public readonly record struct CopySpan(int First, int Count);

/// <summary>
/// How VSHELL's mission load combines the records of one GUID and copies a drawn variant, per <c>.MSN</c> row: the rules of
/// docs/retail/formats/msn-mission-file.md#repeated-guids and docs/retail/formats/msn-mission-file.md#variants as tables of
/// words (the model classes' <c>…Word</c> constants). <see cref="Io.Transform.Common.MissionGenerator"/> merges and copies by
/// them, and so does <see cref="MissionLayout"/>; <see cref="MsnField.Unset"/> reads its unset value here.
/// </summary>
public static class MergeRules {
	/// <summary>Word 1, <c>0x02</c>: the condition ref of every GUID-keyed row.</summary>
	public const int ConditionWord = 0x02 / 2;

	/// <summary>Word 2, <c>0x04</c>: the variant key of rows 6, 7, 8 and 12 to 15.</summary>
	public const int VariantKeyWord = 0x04 / 2;

	/// <summary>Row 8's word 4, its waypoint count, which stands for the count and the list after it in a copy and a supplier.</summary>
	public const int RouteListWord = 0x08 / 2;

	private static readonly OverlaySpan[] Actions = [
		new(MissionAction82.TypeWord, 10, -1),
		new(MissionAction82.CounterPairsWord, 20, -1),
		new(MissionAction82.TextRefsWord, 5, -1),
		new(MissionAction82.MessageIdWord, 1, 0),
		new(MissionAction82.TargetRefWord, 1, -1),
	];

	private static readonly OverlaySpan[] Timers = [
		new(ActionTimer30.PrimaryActionRefWord, 1, -1),
		new(ActionTimer30.DelayWord, 1, 0),
		new(ActionTimer30.SequenceRefsWord, 10, -1),
	];

	/// <summary>Row 12's overlay; <c>0x74</c>'s unset value is the filler 5 a mount carrying nothing guided reads.</summary>
	private static readonly OverlaySpan[] Hercs = [
		new(MechRosterEntry144.AiRadarActiveWord, 20, 0),
		new(MechRosterEntry144.WeaponRefsWord, 10, -1),
		new(MechRosterEntry144.WeaponSecondaryWord, 10, 5),
		new(MechRosterEntry144.TypeIndexWord, 1, -1),
		new(MechRosterEntry144.PositionRefWord, 2, -1),
		new(MechRosterEntry144.OutOfActionReportWord, 20, -1),
		new(MechRosterEntry144.Constant2Word, 1, 2),
		new(MechRosterEntry144.EngagementActionRefWord, 2, -1),
		new(MechRosterEntry144.StartingConditionWord, 1, 100),
	];

	private static readonly OverlaySpan[] Flyers = [
		new(FlyerRosterEntry102.FlagSpanWord, 20, 0),
		new(FlyerRosterEntry102.PositionRefWord, 3, -1),
		new(FlyerRosterEntry102.OutOfActionReportWord, 22, -1),
		new(FlyerRosterEntry102.UnkVal_100Word, 1, 100),
	];

	private static readonly OverlaySpan[] Bases = [
		new(BaseRosterEntry62.TypeIndexWord, 3, -1),
		new(BaseRosterEntry62.OutOfActionReportWord, 22, -1),
		new(BaseRosterEntry62.StartingConditionWord, 1, 100),
	];

	private static readonly OverlaySpan[] Orders = [new(MissionOrder22.VerbWord, 7, -1)];

	private static readonly OverlaySpan[] Groups = [
		new(MissionGroup164.PaintsGroundWord, 20, 0),
		new(MissionGroup164.MemberRefsWord, 30, -1),
		new(MissionGroup164.MemberKindWord, 5, -1),
		new(MissionGroup164.SideWord, 2, -1),
		new(MissionGroup164.OutOfActionReportWord, 21, -1),
	];

	private static readonly CopySpan[] HercCopy = [
		new(MechRosterEntry144.AiRadarActiveWord, 20),
		new(MechRosterEntry144.WeaponRefsWord, 10),
		new(MechRosterEntry144.PositionRefWord, 2),
		new(MechRosterEntry144.TypeIndexWord, 1),
		new(MechRosterEntry144.OutOfActionReportWord, 20),
		new(MechRosterEntry144.WeaponSecondaryWord, 10),
		new(MechRosterEntry144.Constant2Word, 4),
	];

	private static readonly CopySpan[] FlyerCopy = [
		new(FlyerRosterEntry102.FlagSpanWord, 23),
		new(FlyerRosterEntry102.OutOfActionReportWord, 23),
	];

	private static readonly CopySpan[] BaseCopy = [
		new(BaseRosterEntry62.TypeIndexWord, 3),
		new(BaseRosterEntry62.OutOfActionReportWord, 23),
	];

	/// <summary>
	/// Whether a later record of a GUID in row <paramref name="row"/> replaces the earlier one whole: rows 3 and 6 to 9. Rows 10
	/// to 16 overlay it instead (<see cref="Overlay"/>).
	/// </summary>
	public static bool Replaces(int row) => row is 3 or (>= 6 and <= 9);

	/// <summary>
	/// The spans a later record of a GUID in row <paramref name="row"/> overlays onto the earlier one, each word that is not
	/// its span's unset value, in the order the load copies them (<c>Msn_MergeRow10</c> (<c>00416521</c>) and its siblings);
	/// empty for a row that replaces or has no GUID. A word outside them stays the first record's.
	/// </summary>
	public static IReadOnlyList<OverlaySpan> Overlay(int row) => row switch {
		10 => Actions,
		11 => Timers,
		12 => Hercs,
		13 => Flyers,
		14 => Bases,
		15 => Orders,
		16 => Groups,
		_ => [],
	};

	/// <summary>
	/// The spans a record of row <paramref name="row"/> with a variant key copies from the record it draws, in the order the
	/// load copies them; empty for a row with no variant key. Row 8's is <see cref="RouteListWord"/>, its waypoint list.
	/// </summary>
	public static IReadOnlyList<CopySpan> VariantCopy(int row) => row switch {
		6 => [new(MapPoint22.XWord, 6)],
		7 => [new(Heading10.DegreesWord, 1)],
		8 => [new(RouteListWord, 1)],
		12 => HercCopy,
		13 => FlyerCopy,
		14 => BaseCopy,
		15 => [new(MissionOrder22.VerbWord, 7)],
		_ => [],
	};

	/// <summary>
	/// The unset value of word <paramref name="word"/> of row <paramref name="row"/>'s overlay, the value a later record of
	/// the GUID leaves the word at; throws for a word no overlay span of the row covers.
	/// </summary>
	public static short Unset(int row, int word) {
		foreach (var span in Overlay(row)) {
			if (word >= span.First && word < span.First + span.Count) {
				return span.Unset;
			}
		}

		throw new ArgumentOutOfRangeException(nameof(word), word, $"No overlay of row {row} covers word {word}.");
	}
}
