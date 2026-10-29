namespace HercWorks.Core.Data.File.Msn;

/// <summary>What a field of a roster record, an order or a group is to a designer (<see cref="RecordAspects"/>).</summary>
public enum RecordAspect {
	/// <summary>A herc's chassis, a flyer's, structure's or ground vehicle's type.</summary>
	Type,
	Weapons,

	/// <summary>A herc's missile guidance: what each launcher is loaded with (<see cref="MechRosterEntry144.WeaponSecondary"/>).</summary>
	Guidance,
	Radar,
	Speed,
	Position,
	Heading,
	StartingCondition,

	/// <summary>The campaign variables it writes when out of the fight.</summary>
	OutOfFightWrites,

	/// <summary>The actions a roster record fires when engaged and when defeated.</summary>
	FiredTriggers,
	Members,
	Orders,
	Formation,
	Route,
	Team,

	/// <summary>The action a group waits on before it is in the mission.</summary>
	WaitsOn,

	/// <summary>An order's verb.</summary>
	OrderKind,

	/// <summary>An order's subject.</summary>
	Target,

	/// <summary>The action that moves a group on from an order.</summary>
	EndsOn,

	/// <summary>A group's paints-ground flag.</summary>
	BasePad,

	/// <summary>A word no designer-facing field shows: one 0 in every retail record, or one whose reader is not established.</summary>
	Other,
}

/// <summary>
/// Names the words of a row-12 to 16 record by what they are to a designer, from the record types' <c>…Word</c> constants and
/// their field decodes (docs/retail/formats/msn-mission-file.md): for a view to say which of a thing's fields a variation
/// changes (<see cref="LayoutVariation.Words"/>).
/// </summary>
public static class RecordAspects {
	/// <summary>The aspects words <paramref name="words"/> of a row-<paramref name="row"/> record are, distinct, in <see cref="RecordAspect"/> order.</summary>
	public static IReadOnlyList<RecordAspect> Of(int row, IEnumerable<int> words) =>
		words.Select(word => Of(row, word)).Distinct().Order().ToList();

	/// <summary>What word <paramref name="word"/> of a row-<paramref name="row"/> record is; <see cref="RecordAspect.Other"/> for any other row.</summary>
	public static RecordAspect Of(int row, int word) => row switch {
		12 => Herc(word),
		13 => Flyer(word),
		14 => Structure(word),
		15 => Order(word),
		16 => Group(word),
		_ => RecordAspect.Other,
	};

	private static bool In(int word, int first, int count) => word >= first && word < first + count;

	private static RecordAspect Herc(int word) => word switch {
		MechRosterEntry144.AiRadarActiveWord => RecordAspect.Radar,
		MechRosterEntry144.AiRadarActiveWord + 1 => RecordAspect.Speed,
		MechRosterEntry144.TypeIndexWord => RecordAspect.Type,
		_ when In(word, MechRosterEntry144.WeaponRefsWord, 10) => RecordAspect.Weapons,
		MechRosterEntry144.PositionRefWord => RecordAspect.Position,
		MechRosterEntry144.HeadingRefWord => RecordAspect.Heading,
		_ when In(word, MechRosterEntry144.OutOfActionReportWord, 20) => RecordAspect.OutOfFightWrites,
		_ when In(word, MechRosterEntry144.WeaponSecondaryWord, 10) => RecordAspect.Guidance,
		MechRosterEntry144.EngagementActionRefWord or MechRosterEntry144.DefeatActionRefWord => RecordAspect.FiredTriggers,
		MechRosterEntry144.StartingConditionWord => RecordAspect.StartingCondition,
		_ => RecordAspect.Other,
	};

	private static RecordAspect Flyer(int word) => word switch {
		FlyerRosterEntry102.PositionRefWord => RecordAspect.Position,
		FlyerRosterEntry102.HeadingRefWord => RecordAspect.Heading,
		FlyerRosterEntry102.TypeIndexWord => RecordAspect.Type,
		_ when In(word, FlyerRosterEntry102.OutOfActionReportWord, 20) => RecordAspect.OutOfFightWrites,
		FlyerRosterEntry102.EngagementActionRefWord or FlyerRosterEntry102.DefeatActionRefWord => RecordAspect.FiredTriggers,
		_ => RecordAspect.Other,
	};

	private static RecordAspect Structure(int word) => word switch {
		BaseRosterEntry62.TypeIndexWord => RecordAspect.Type,
		BaseRosterEntry62.PositionRefWord => RecordAspect.Position,
		BaseRosterEntry62.HeadingRefWord => RecordAspect.Heading,
		_ when In(word, BaseRosterEntry62.OutOfActionReportWord, 20) => RecordAspect.OutOfFightWrites,
		BaseRosterEntry62.EngagementActionRefWord or BaseRosterEntry62.DefeatActionRefWord => RecordAspect.FiredTriggers,
		BaseRosterEntry62.StartingConditionWord => RecordAspect.StartingCondition,
		_ => RecordAspect.Other,
	};

	// The formation (0x0A) and point (0x0C) have no DBSIM reader (docs/retail/simulation/ai-goals.md#open).
	private static RecordAspect Order(int word) => word switch {
		MissionOrder22.VerbWord => RecordAspect.OrderKind,
		MissionOrder22.RouteRefWord => RecordAspect.Route,
		MissionOrder22.SubjectKindWord or MissionOrder22.SubjectRefWord => RecordAspect.Target,
		MissionOrder22.ActionRefWord => RecordAspect.EndsOn,
		_ => RecordAspect.Other,
	};

	private static RecordAspect Group(int word) => word switch {
		MissionGroup164.PaintsGroundWord => RecordAspect.BasePad,
		MissionGroup164.MemberKindWord => RecordAspect.Members,
		MissionGroup164.MemberKindWord + 1 => RecordAspect.Formation,
		MissionGroup164.PositionRefWord => RecordAspect.Position,
		MissionGroup164.HeadingRefWord => RecordAspect.Heading,
		MissionGroup164.RouteRefWord => RecordAspect.Route,
		_ when In(word, MissionGroup164.MemberRefsWord, 20) => RecordAspect.Members,
		_ when In(word, MissionGroup164.OrderRefsWord, 10) => RecordAspect.Orders,
		MissionGroup164.SideWord => RecordAspect.Team,
		MissionGroup164.DeploymentActionRefWord => RecordAspect.WaitsOn,
		_ when In(word, MissionGroup164.OutOfActionReportWord, 20) => RecordAspect.OutOfFightWrites,
		_ => RecordAspect.Other,
	};
}
