namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// The least a mission needs to load, play and end cleanly, as an <c>.MSN</c> and its <c>.ENG</c>, for the mission
/// editor's File &gt; New mission: the player's machine alone, on a route of two points, with one mandatory objective
/// that is met when the player reaches the second (docs/herculan/handoff-editor-viewing.md#new-mission). What DBSIM reads of each record is docs/retail/formats/script-dat.md's, and how a mission ends
/// docs/retail/simulation/mission-objectives.md's.
///
/// <para>Every record copies one in a retail practice mission, cited where it is built, with the chosen theater,
/// zone and time of day put in. Each is unconditioned, draws no variant, and takes the GUID
/// <see cref="MissionDocument.Append"/> would give it: its row's largest plus one, so 0 for the first record of each
/// row.</para>
/// </summary>
public static class NewMission {
	/// <summary>
	/// <c>TRAIN1.MSN</c>'s row-6 points 0 and 1, its route's start and first waypoint, and its points 6 to 9, which no
	/// record names: the four corners that frame its mission box, block 1's extent
	/// (docs/retail/simulation/heads-down-display.md#the-maps-frame-of-reference). The new mission keeps their layout and
	/// moves it so the box the corners frame is centered on the zone.
	/// </summary>
	private static readonly (int X, int Y)[] Train1Points = [
		(1177720, 996723), (1177853, 1214057),
		(1684120, 1451524), (950296, 802876), (979780, 1441696), (1720156, 793048),
	];

	/// <summary>Where <see cref="Train1Points"/>' box corners start.</summary>
	private const int FirstCorner = 2;

	/// <summary>The player's point, which the route starts on, and the nav point, its one waypoint, as row-6 GUIDs.</summary>
	public const short StartPoint = 0;
	public const short NavPoint = 1;

	/// <summary>
	/// <c>TRAIN1.MSN</c>'s header objective type, <see cref="Script.ScriptDat.ObjectiveType"/>: 2, which runs none of
	/// the player think's objective arms (<c>Mech_BehaviourPlayerThink</c> (<c>0041c194</c>)), where the 0 a patch
	/// that sets none leaves would run the one-shot MISSION TARGET DETECTED arm. The objective here needs no arm: it
	/// reads the order's completion flag.
	/// </summary>
	private const short ObjectiveType = 2;

	// PLACEHOLDER: the mission's text, plain English an author replaces. The failure text is three consecutive lines,
	// the third blank as in TRAIN1.ENG; the objective line ends " \n" as TRAIN5.ENG's do.
	private const string FailureLine1 = "Your mission is not complete.";
	private const string FailureLine2 = "You have not reached the nav point.";
	private const string FailureLine3 = " ";
	private const string ObjectiveLine = "Reach the nav point. \n";

	/// <summary>The <c>.ENG</c> ids: the failure text's first line, and the objective line.</summary>
	private const short FailureText = 1;
	private const short ObjectiveText = 4;

	/// <summary>
	/// A new mission on <paramref name="theater"/> and <paramref name="zone"/>, by day or by <paramref name="night"/>,
	/// for a zone <paramref name="zoneWidth"/> by <paramref name="zoneHeight"/> world units.
	/// </summary>
	public static (MissionFile Mission, MissionStringFile Text) Build(short theater, short zone, bool night, int zoneWidth, int zoneHeight) {
		const short mech = 0, heading = 0, route = 0, order = 0, group = 0;

		var mission = new MissionFile {
			Revision = 5,
			Conditions = [],
			SettingsPatches = [Settings(theater, zone, night)],
			Variants = [],
			Texts = [Text()],
			DebriefBytes = [],
			Points = Points(zoneWidth, zoneHeight),
			// TRAIN1.MSN's row-7 record 45, a heading of 0 degrees, which faces the nav point: the first leg runs along +y.
			Headings = [new Heading10 { GUID = heading, ConditionRef = -1, VariantKey = -1, Unk06 = -1, Degrees = 0 }],
			// TRAIN1.MSN's route 10, cut to its first leg.
			WaypointGroups = [new WaypointGroup { GUID = route, ConditionRef = -1, VariantKey = -1, Unk06 = -1, Waypoints = [StartPoint, NavPoint] }],
			TriggerAreas = [],
			Actions = [],
			ActionTimers = [],
			Mechs = [PlayerMachine(mech)],
			Flyers = [],
			Bases = [],
			// TRAIN1.MSN's order 58: patrol (verb 3) on the route, in formation 0, naming no point, subject or action. A
			// patrol is finished once its route has no waypoint after the cursor (Group_IsOrderComplete (004239fc)),
			// which the player's own arrival steps (docs/retail/simulation/player-waypoints.md).
			Orders = [new MissionOrder22 {
				GUID = order, ConditionRef = -1, VariantKey = -1, CompoundConditionPartner = -1, Verb = 3, FormationId = 0,
				PointRef = -1, RouteRef = route, SubjectKind = -1, SubjectRef = -1, ActionRef = -1,
			}],
			Groups = [PlayerSquad(group, mech, heading, route, order)],
			// TRAIN1.MSN's one objective: mandatory, condition 0 asked of the player's machine on the route, so it holds once
			// the patrol on that route is finished (docs/retail/simulation/mission-objectives.md#what-each-condition-asks),
			// writing no counter.
			Objectives = [new MissionObjective58 {
				ConditionRef = -1, Required = 1, ConditionCode = 0, SubjectKind = 1, SubjectRef = mech, PointRef = -1,
				RouteRef = route, TextRef = FailureText, PairCount = 0,
				Pairs = Enumerable.Range(0, 10).Select(_ => new CounterPair()).ToArray(),
			}],
		};

		var text = new MissionStringFile {
			Strings = [
				Line(FailureText, FailureLine1),
				Line(FailureText + 1, FailureLine2),
				Line(FailureText + 2, FailureLine3),
				Line(ObjectiveText, ObjectiveLine),
			],
		};

		return (mission, text);
	}

	/// <summary>
	/// The header patch: the theater, the zone and the time of day, and <see cref="ObjectiveType"/>; every other word at
	/// its unset value, as <c>TRAIN5.MSN</c> to <c>TRAIN8.MSN</c> leave them, so the training mission number is 0 and
	/// the cheats and difficulty are what the launch writes. No flag is cleared. A practice load writes the theater and
	/// the time of day over the patch's (docs/retail/formats/script-dat.md#the-training-fields).
	/// </summary>
	private static MissionSettingsPatch Settings(short theater, short zone, bool night) {
		var data = new short[MissionSettingsPatch.Words];
		data[0] = -1;
		data[MissionSettingsPatch.HeaderWordsStart + 0 / 2] = theater;
		data[MissionSettingsPatch.HeaderWordsStart + 2 / 2] = zone;
		data[MissionSettingsPatch.HeaderWordsStart + 6 / 2] = ObjectiveType;
		data[MissionSettingsPatch.HeaderWordsStart + 18 / 2] = (short)(night ? 1 : 0);
		Array.Fill(data, (short)-1, MissionSettingsPatch.ClearListStart, MissionSettingsPatch.Words - MissionSettingsPatch.ClearListStart);
		return new MissionSettingsPatch { Data = data };
	}

	/// <summary>The text package of <c>TRAIN5.MSN</c>'s shape: objective lines alone, no briefing or intelligence, no movie.</summary>
	private static MissionText144 Text() {
		var objective = Unset(10);
		objective[0] = ObjectiveText;
		return new MissionText144 { ConditionRef = -1, ObjectiveLines = objective, BriefingLines = Unset(30), IntelligenceLines = Unset(30), MovieRef = -1 };
	}

	/// <summary><see cref="Train1Points"/>, moved so the box their corners frame is centered on the zone, each at Z 0.</summary>
	private static MapPoint22[] Points(int zoneWidth, int zoneHeight) {
		var corners = Train1Points[FirstCorner..];
		int dx = zoneWidth / 2 - (corners.Min(p => p.X) + corners.Max(p => p.X)) / 2;
		int dy = zoneHeight / 2 - (corners.Min(p => p.Y) + corners.Max(p => p.Y)) / 2;
		return Train1Points.Select((p, i) => new MapPoint22 {
			GUID = (short)i, ConditionRef = -1, VariantKey = -1, Unk06 = -1, SumFlag = 0, X = p.X + dx, Y = p.Y + dy, Z = 0,
		}).ToArray();
	}

	/// <summary>
	/// <c>TRAIN1.MSN</c>'s player record, mech 46, with the variant it draws resolved: its key 43 names one table whose
	/// bound is 1, so every load copies the one alternative, an OUTLAW (type 0) with weapon 8 in slots 0 to 2 and
	/// ammunition type 5 throughout (<see cref="SquadRoster.Machine"/>). Written whole, as a GUID-only record.
	/// </summary>
	private static MechRosterEntry144 PlayerMachine(short guid) {
		var weapons = Unset(10);
		weapons[0] = weapons[1] = weapons[2] = 8;
		var record = SquadRoster.Machine(0, weapons, Enumerable.Repeat((short)5, 10).ToArray());
		record.GUID = guid;
		return record;
	}

	/// <summary>
	/// <c>TRAIN1.MSN</c>'s group 59, the player's squad of one: human, formation 0, its paints-ground flag set, the
	/// player's machine in slot 0 and slot 1 empty, so no squadmate
	/// (docs/retail/shell/main-menu.md#starting-a-practice-mission), the patrol in order slot 0. It names the start point
	/// and the heading, as <c>TRAIN8.MSN</c>'s squad names its own.
	/// </summary>
	private static MissionGroup164 PlayerSquad(short guid, short mech, short heading, short route, short order) {
		var members = Unset(20);
		members[0] = mech;
		var orders = Unset(10);
		orders[0] = order;
		return new MissionGroup164 {
			GUID = guid, ConditionRef = -1, CompoundConditionPartner = -1, PaintsGround = 1, NearConstant = 0,
			DeadZone = new short[18], MemberKind = 0, FormationId = 0, PositionRef = StartPoint, HeadingRef = heading,
			RouteRef = route, MemberRefs = members, OrderRefs = orders, Side = 0, DeploymentActionRef = -1, PairCount = 0,
			OutOfActionReport = Unset(20), MapShown = -1,
		};
	}

	/// <summary>An unconditioned <c>.ENG</c> record, its text NUL-terminated as the files store it.</summary>
	private static MissionStringFile.StringEntry Line(int id, string text) => new() {
		Guid = (short)id, ConditionRef = -1, ParentRef = -1, Len = (short)(text.Length + 1), Val = text + "\0",
	};

	private static short[] Unset(int count) => Enumerable.Repeat((short)-1, count).ToArray();
}
