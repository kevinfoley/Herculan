using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Query;

/// <summary>Which roster a group's members name — the values of <see cref="MissionGroup164.MemberKind"/>.</summary>
internal enum RosterKind : short {
	Mech = 0,
	Flyer = 1,
	Base = 2,
}

/// <summary>
/// The fields a query reads from a record of rows #12, #13 or #14, which the three model classes
/// carry under the same names.
/// </summary>
/// <param name="Index">The record's position in its row.</param>
/// <param name="StartingCondition">Null for row #13, which has none.</param>
internal sealed record RosterRecord(
	RosterKind Kind, int Index, short Guid, short ConditionRef, short VariantKey, short TypeIndex,
	short? StartingCondition, short[] OutOfActionReport);

internal static class Roster {
	/// <summary>The <c>.MSN</c> row a roster is.</summary>
	public static int Row(RosterKind kind) => kind switch {
		RosterKind.Mech => 12,
		RosterKind.Flyer => 13,
		_ => 14,
	};

	public static string Noun(RosterKind kind) => kind switch {
		RosterKind.Mech => "mech",
		RosterKind.Flyer => "flyer",
		_ => "base",
	};

	public static IReadOnlyList<RosterRecord> Records(MissionFile file, RosterKind kind) => kind switch {
		RosterKind.Mech => (file.Mechs ?? []).Select((r, i) => new RosterRecord(
			kind, i, r.GUID, r.ConditionRef, r.VariantKey, r.TypeIndex, r.StartingCondition, r.OutOfActionReport)).ToList(),
		RosterKind.Flyer => (file.Flyers ?? []).Select((r, i) => new RosterRecord(
			kind, i, r.GUID, r.ConditionRef, r.VariantKey, r.TypeIndex, null, r.OutOfActionReport)).ToList(),
		_ => (file.Bases ?? []).Select((r, i) => new RosterRecord(
			kind, i, r.GUID, r.ConditionRef, r.VariantKey, r.TypeIndex, r.StartingCondition, r.OutOfActionReport)).ToList(),
	};
}
