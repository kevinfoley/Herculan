using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>
/// Turns the game's own mission handoff — <c>DATA\script.dat</c>, with the <c>mission.var</c> and
/// <c>player.mec</c> beside it — into a <see cref="Mission"/>: one <see cref="MissionPlacement"/> per
/// object the mission spawns, the player's squad last, with the groups' orders, the actions, timers,
/// objectives and text.
///
/// <para>One pass over the file stands in for DBSIM's two (<c>DBSim_LoadScriptDat</c> (<c>00424308</c>)
/// counts and allocates, <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) places) and yields what the
/// second leaves: which group claims each roster slot, where each object stands and faces, and the squad
/// (<see cref="LoadPlayerSquad"/>). <see cref="MissionPlacementRules"/> places them from the points, headings,
/// routes and member refs this loader resolves out of the blocks. The rule and its RE are
/// docs/retail/formats/script-dat.md#placement--the-actual-rule.</para>
///
/// <para>A group waiting on a deployment action is placed like any other, and its position is a
/// placeholder its arrival replaces; the loader keeps which action it waits on, since the arrival
/// verb is read off that action — see docs/retail/simulation/mission-deployment.md.</para>
/// </summary>
public static class MissionLoader {
	/// <summary>Folder inside an install root holding the loose mission handoff files.</summary>
	public const string DataFolderName = "DATA";

	/// <summary>The mission handoff itself.</summary>
	public const string ScriptFileName = "script.dat";

	/// <summary>The player's squad, written beside it.</summary>
	public const string PlayerFileName = "player.mec";

	/// <summary>The mission's own text, written beside it as well.</summary>
	public const string TextFileName = "mission.str";

	/// <summary>The campaign flag array the mission counters start from, and are written back to.</summary>
	public const string CountersFileName = "mission.var";

	/// <summary>
	/// The squad file that goes with a mission file. The live pair in <c>DATA\</c> is
	/// <c>script.dat</c>/<c>player.mec</c>; the save-slot snapshots in <c>SAV\</c> keep the same
	/// pairing with a slot number on both halves (<c>script3.dat</c>/<c>player3.mec</c>).
	/// </summary>
	public static string PlayerPathFor(string scriptPath) {
		string directory = Path.GetDirectoryName(scriptPath) ?? ".";
		string slot = Path.GetFileNameWithoutExtension(scriptPath);

		slot = slot.StartsWith("script", StringComparison.OrdinalIgnoreCase)
			? slot["script".Length..]
			: string.Empty;

		return CaseInsensitivePath.Combine(directory, $"player{slot}.mec");
	}

	/// <summary>
	/// The text file that goes with a mission file. The live triple in <c>DATA\</c> is
	/// <c>script.dat</c>/<c>player.mec</c>/<c>mission.str</c>; the save-slot snapshots in <c>SAV\</c>
	/// keep the pairing but rename the text half, so <c>script3.dat</c> goes with
	/// <c>missn3.str</c>. DBSIM itself only ever opens the <c>DATA\</c> one — VSHELL copies a slot's
	/// into place before launching it — so the second spelling is this loader's, for opening a
	/// snapshot where it lies.
	/// </summary>
	public static string TextPathFor(string scriptPath) {
		string directory = Path.GetDirectoryName(scriptPath) ?? ".";
		string name = Path.GetFileNameWithoutExtension(scriptPath);

		if (!name.StartsWith("script", StringComparison.OrdinalIgnoreCase)) {
			return CaseInsensitivePath.Combine(directory, TextFileName);
		}

		string slot = name["script".Length..];
		return CaseInsensitivePath.Combine(directory, slot.Length == 0 ? TextFileName : $"missn{slot}.str");
	}

	/// <summary>Row-15 links a block-11 record can name.</summary>
	private const int GroupRouteSlots = 10;

	/// <summary>The conventional path of a mission handoff inside an install root.</summary>
	public static string DefaultScriptPath(string installRoot) =>
		CaseInsensitivePath.Combine(installRoot, DataFolderName, ScriptFileName);

	/// <summary>
	/// Loads the mission at <paramref name="scriptPath"/>. <paramref name="content"/> supplies the
	/// type-name lists, which live in the archives rather than beside the mission.
	///
	/// <para><c>mission.var</c> and <c>player.mec</c> are looked for next to the script, and the load
	/// throws <see cref="MissingHandoffFileException"/> when either is not there, as the original
	/// asserts on each.</para>
	/// </summary>
	public static Mission Load(GameContent content, string scriptPath) => Load(content, ReadScript(scriptPath), scriptPath);

	/// <summary>Parses the handoff at <paramref name="scriptPath"/>, throwing <see cref="InvalidDataException"/> when it does not.</summary>
	public static ScriptDat ReadScript(string scriptPath) =>
		new ScriptDatTransformer().Parse(File.ReadAllBytes(scriptPath)) as ScriptDat
			?? throw new InvalidDataException($"{scriptPath} did not parse as a script.dat.");

	/// <summary>
	/// <see cref="Load(GameContent, string)"/> over a <paramref name="script"/> already read from
	/// <paramref name="scriptPath"/>, which still locates the files beside it.
	/// </summary>
	public static Mission Load(GameContent content, ScriptDat script, string scriptPath) {
		var header = ScriptDatHeader.From(script);
		var mechNames = UnitTypeNames.LoadMechs(content);
		var flyerNames = UnitTypeNames.LoadFlyers(content);
		var mechFormations = MechFormationTable.Load(content);
		var flyerFormations = FlyerFormationTable.Load(content);
		var baseFormations = BaseFormationTable.Load(content);

		var groups = ResolveGroups(script, baseFormations);
		var members = GroupMembers(script, groups);
		var mechClaims = MissionPlacementRules.ClaimMembers(MissionUnitKind.Mech, members);
		var flyerClaims = MissionPlacementRules.ClaimMembers(MissionUnitKind.Flyer, members);
		var baseClaims = MissionPlacementRules.ClaimMembers(MissionUnitKind.Base, members);

		// The three rosters in the order DBSIM's spawn pass walks them, one placement per live slot.
		var placements = new List<MissionPlacement>();
		AddMechs(script, groups, mechClaims, mechNames, mechFormations, placements);
		AddFlyers(script, groups, flyerClaims, flyerNames, flyerFormations, placements);
		AddBases(script, groups, baseClaims, baseFormations, placements);

		var counters = LoadCounters(CaseInsensitivePath.Combine(Path.GetDirectoryName(scriptPath) ?? ".", CountersFileName));
		var player = LoadPlayerSquad(scriptPath, script, groups, mechFormations, mechNames, placements);
		var basePads = ResolveBasePads(groups, baseClaims, baseFormations, placements);

		var coordinates = new List<Vec3i>(script.Coordinates.Length);
		foreach (var point in script.Coordinates) {
			coordinates.Add(new Vec3i(point.X, point.Y, point.Z));
		}

		var playerRoute = groups.Length > MissionPlacementRules.SquadGroupIndex
			? Route(groups[MissionPlacementRules.SquadGroupIndex].Orders)
			: Array.Empty<Vec3i>();

		var groupOrders = new IReadOnlyList<MissionOrder?>[groups.Length];
		for (int i = 0; i < groups.Length; i++) {
			groupOrders[i] = groups[i].Orders;
		}

		var actions = ResolveActions(script);
		var actionTimers = ResolveActionTimers(script);
		var objectives = ResolveObjectives(script);
		var text = LoadText(TextPathFor(scriptPath));

		var deploymentActions = new int[groups.Length];
		var groupKinds = new MissionUnitKind[groups.Length];
		var groupSides = new MissionSide[groups.Length];
		var groupReports = new OutOfActionReport[groups.Length];
		for (int i = 0; i < groups.Length; i++) {
			deploymentActions[i] = groups[i].DeploymentAction;
			groupKinds[i] = groups[i].Kind;
			groupSides[i] = groups[i].Side;

			// Record 0 included: the spawn pass overwrites the player squad's member list and nothing
			// else, so its counter slots are read like any other group's.
			var record = script.Groups[i];
			groupReports[i] = new OutOfActionReport(record.CounterRefs, record.CounterOps);
		}

		return new Mission(scriptPath, header, placements, player, basePads, coordinates, playerRoute,
			groupOrders, actions, actionTimers, deploymentActions, groupKinds, groupSides,
			objectives, Array.ConvertAll(script.ObjectiveTextRefs, line => (int)line), text, counters,
			groupReports, Array.ConvertAll(groups, group => group.Setup));
	}

	/// <summary>
	/// The mission counters' starting values — <c>DBSim_LoadScriptDat</c> (<c>00424308</c>) reads
	/// 2,000 bytes of <c>data\mission.var</c> straight into <c>DAT_004a9ef4</c>, then zeroes slot 20,
	/// slot 10 and slots 21 to 42, before it opens <c>player.mec</c>. See
	/// docs/retail/simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4.
	///
	/// <para>Looked for beside the script, as <c>player.mec</c> is. A missing one throws
	/// <see cref="MissingHandoffFileException"/>, as the original asserts on it — see
	/// docs/retail/formats/script-dat.md#call-chain--confirmed.</para>
	/// </summary>
	private static short[] LoadCounters(string countersPath) {
		if (!File.Exists(countersPath)) {
			throw new MissingHandoffFileException(countersPath);
		}

		var counters = new short[Sim.MissionRuntime.CounterSlots];
		byte[] bytes = File.ReadAllBytes(countersPath);
		Buffer.BlockCopy(bytes, 0, counters, 0, Math.Min(bytes.Length, counters.Length * 2));

		counters[SalvageBonusCounter] = 0;
		counters[SquadmatesDownedCounter] = 0;
		for (int i = FirstClearedCounter; i <= LastClearedCounter; i++) {
			counters[i] = 0;
		}

		return counters;
	}

	/// <summary>
	/// Counter 20 (<c>DAT_004a9f1c</c>): <c>Mission_WriteResults</c> (<c>0042412c</c>) adds 25,000 kg of salvage per unit of it
	/// to <c>results.dat</c>'s award as the mission ends.
	/// </summary>
	public const int SalvageBonusCounter = 20;

	/// <summary>
	/// Counter 10 (<c>DAT_004a9f08</c>): how many of the player's own squad the player has put out of
	/// the fight this mission — see <see cref="Sim.MechObject.CreditNeutralised"/>.
	/// </summary>
	public const int SquadmatesDownedCounter = 10;

	/// <summary>The run of counters the load also zeroes, 21 to 42 inclusive.</summary>
	private const int FirstClearedCounter = 21;

	/// <inheritdoc cref="FirstClearedCounter"/>
	private const int LastClearedCounter = 42;

	/// <summary>
	/// Block 12 — the mission's objectives, as <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>)
	/// builds them in its final pass. <b>Pass 1 reads this block and throws it away</b>, which is why
	/// reading only that pass leaves the block looking dead; the spawn pass comes back for it and
	/// turns each 54-byte record into a 76-byte one with its refs resolved. See
	/// <see cref="MissionObjective"/>.
	/// </summary>
	private static MissionObjective[] ResolveObjectives(ScriptDat script) {
		var objectives = new MissionObjective[script.Objectives.Length];

		for (int i = 0; i < objectives.Length; i++) {
			var record = script.Objectives[i];
			var kind = (MissionObjectiveSubject)record.SubjectKind;

			objectives[i] = new MissionObjective(
				record.Required,
				record.ConditionCode,
				kind,
				record.SubjectRef,
				Coordinate(script, record.PointRef),
				record.RouteRef,
				record.TextRef,
				record.CounterRefs,
				record.CounterOps);
		}

		return objectives;
	}

	/// <summary>
	/// <c>data\mission.str</c>, flattened to one list of lines. It is an ordinary <c>.STR</c> table
	/// of a single group with no attribute bytes, and the original loads at most
	/// <see cref="TextLineLimit"/> of them into a pointer array. A mission with no text file loads
	/// with none rather than failing: nothing in the simulation needs it, only what displays it.
	/// </summary>
	private static IReadOnlyList<string> LoadText(string textPath) {
		if (!File.Exists(textPath)) {
			return Array.Empty<string>();
		}

		if (SimStrings.Parse(File.ReadAllBytes(textPath)) is not { } table) {
			return Array.Empty<string>();
		}

		var lines = new List<string>();

		for (int group = 0; group < table.GroupCount && lines.Count < TextLineLimit; group++) {
			foreach (var entry in table.Group(group)) {
				if (lines.Count >= TextLineLimit) {
					break;
				}

				// The load pass strips one trailing newline per line, which is how the retail file
				// authors an intentional line break at the end of a briefing paragraph without it
				// reaching the screen.
				lines.Add(entry.Text.EndsWith('\n') ? entry.Text[..^1] : entry.Text);
			}
		}

		return lines;
	}

	/// <summary>
	/// Lines the original's <c>mission.str</c> pointer array holds — its literal 0x32. A file with
	/// more is read no further.
	/// </summary>
	public const int TextLineLimit = 0x32;

	/// <summary>
	/// Block 6 — the mission's timers, as <c>DBSim_LoadScriptDat</c> (<c>00424308</c>) resolves them
	/// through <c>DBSim_BuildActionTimerRecord</c> (<c>00423104</c>). The stored delay is shifted into
	/// the simulation's timer unit here, which is what <c>FUN_004679c0</c> does as it arms the
	/// countdown.
	/// </summary>
	private static MissionActionTimer[] ResolveActionTimers(ScriptDat script) {
		var timers = new MissionActionTimer[script.ActionTimers.Length];

		for (int i = 0; i < timers.Length; i++) {
			var record = script.ActionTimers[i];
			timers[i] = new MissionActionTimer(
				ActionRef(script, record.PrimaryActionRef),
				record.Delay << MissionActionTimer.DelayShift,
				record.SequenceRefs);
		}

		return timers;
	}

	/// <summary>
	/// One block-5 ref, bounds-checked against the action array. Everything that names an action —
	/// a group's arrival gate, an order, a timer, and a roster record's own two — goes through this.
	/// </summary>
	private static short ActionRef(ScriptDat script, short reference) =>
		reference >= 0 && reference < script.Actions.Length ? reference : (short)-1;

	/// <summary>
	/// Block 5, with each record's block-4 refs resolved into trigger areas — the load-time half of
	/// <c>DBSim_LoadScriptDat</c> (<c>00424308</c>)'s action pass.
	///
	/// <para><b>The record's field-to-offset mapping is that pass's read order</b>, which is the only
	/// statement of it anywhere: two shorts, then sixteen bytes of block-4 refs into a stack buffer,
	/// then twenty bytes to the runtime record's <c>+0x0c</c> and twenty more to its <c>+0x20</c>,
	/// then ten bytes read and dropped, then <c>+0x34</c> and <c>+0x36</c>. So
	/// <see cref="ScriptAction.CounterRefs"/> is the counter refs, <see cref="ScriptAction.CounterOps"/> the
	/// operations that go with them, <see cref="ScriptAction.TextRefs"/> is read and discarded by
	/// DBSIM entirely, <see cref="ScriptAction.MessageId"/> is the mission message and
	/// <see cref="ScriptAction.TargetRef"/> the trigger's own subject.</para>
	///
	/// <para>Two things the pass does that reading the file alone would not show: it <b>subtracts one
	/// from the message id</b> as it stores it, and it stops counting block-4 refs at the first
	/// negative one rather than at the eighth slot.</para>
	/// </summary>
	private static MissionAction[] ResolveActions(ScriptDat script) {
		var actions = new MissionAction[script.Actions.Length];

		for (int i = 0; i < actions.Length; i++) {
			var record = script.Actions[i];
			var areas = new List<MissionTriggerArea>(MissionAction.AreaSlots);

			for (int slot = 0; slot < MissionAction.AreaSlots && slot < record.AreaRefs.Length; slot++) {
				short reference = record.AreaRefs[slot];
				if (reference < 0) {
					break;
				}

				if (TriggerArea(script, reference) is { } area) {
					areas.Add(area);
				}
			}

			actions[i] = new MissionAction(
				record.Type,
				record.Verb,
				areas,
				record.CounterRefs,
				record.CounterOps,
				(short)(record.MessageId - 1),
				record.TargetRef);
		}

		return actions;
	}

	/// <summary>
	/// One block-4 record resolved — <c>TriggerArea_Resolve</c> (<c>00423358</c>). The type flag
	/// picks what the record's second field is: another coordinate, making a box, or a literal that
	/// is scaled by ten into a radius. Null for a reference outside block 4, or a record naming a point block 1 does not
	/// have.
	/// </summary>
	public static MissionTriggerArea? TriggerArea(ScriptDat script, short reference) {
		if (reference < 0 || reference >= script.TriggerAreas.Length) {
			return null;
		}

		var record = script.TriggerAreas[reference];
		if (Coordinate(script, record.PointRef) is not { } anchor) {
			return null;
		}

		if (record.Shape != 0) {
			return new MissionTriggerArea(MissionTriggerShape.Circle, anchor, anchor,
				record.SecondPointOrRadius * TriggerRadiusScale, reference);
		}

		return Coordinate(script, record.SecondPointOrRadius) is { } opposite
			? new MissionTriggerArea(MissionTriggerShape.Box, anchor, opposite, 0, reference)
			: null;
	}

	/// <summary>
	/// What a circular trigger's stored literal is multiplied by, once, as the record is resolved.
	/// </summary>
	private const int TriggerRadiusScale = 10;

	/// <summary>
	/// The ground each base group repaints: one pad per group with a <see cref="MissionPlacementRules.PadLayout"/>, read
	/// from where its <see cref="MissionPlacementRules.PadLead{TKey}"/> stands.
	/// </summary>
	private static List<MissionBasePad> ResolveBasePads(Group[] groups,
			IReadOnlyDictionary<int, MissionMemberClaim> baseClaims, BaseFormationTable baseFormations,
			List<MissionPlacement> placements) {
		var pads = new List<MissionBasePad>();

		foreach (var group in groups) {
			if (MissionPlacementRules.PadLayout(group.Kind, group.Setup.PaintsGround, group.Setup.FormationId,
					baseFormations) is not { } layout
				|| MissionPlacementRules.PadLead(baseClaims, group.Index) is not { } leadSlot) {
				continue;
			}

			var lead = placements.FirstOrDefault(
				p => p.Kind == MissionUnitKind.Base && p.SlotIndex == leadSlot);
			if (lead != null) {
				pads.Add(new MissionBasePad(lead.Position, layout, group.Index));
			}
		}

		return pads;
	}

	/// <summary>A block-11 record reduced to what placement needs.</summary>
	/// <param name="DeploymentAction">
	/// <b>Which</b> block-5 action the record names (its <c>DeploymentActionRef</c>), or <c>-1</c>. DBSIM
	/// resolves it into the group record's <c>+0x14</c> action pointer, and that pointer is both the
	/// gate and the arrival instruction: which verb the group turns up on is read off this very
	/// action. Reducing it to a bool loses the arrival — see <see cref="Sim.MissionGroup.DeploymentCheck"/>.
	/// </param>
	/// <param name="Side">
	/// The record's <c>0x6e</c> — see <see cref="MissionSide"/>. Anything other than 1 is taken as
	/// human, since the detection sweep is the only place that tests for a literal value, and it tests
	/// for Cybrid.
	/// </param>
	/// <param name="Orders">
	/// The record's ten row-15 links resolved into order records, nulls kept in the slots it left
	/// unset — <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>) copies the array position for position
	/// into <c>group+0x44</c>, and <c>Group_OrderTick</c> reads it by index. See
	/// docs/retail/simulation/ai-goals.md.
	/// </param>
	/// <param name="Setup">
	/// The record's own point, heading and formation, as <see cref="MissionPlacementRules.GroupSetup"/> places it, for
	/// its members and for <see cref="Mission.GroupSetups"/>.
	/// </param>
	private readonly record struct Group(int Index, MissionUnitKind Kind, MissionGroupSetup Setup,
		int DeploymentAction, MissionSide Side, MissionOrder?[] Orders);

	private static Group[] ResolveGroups(ScriptDat script, BaseFormationTable baseFormations) {
		var groups = new Group[script.Groups.Length];

		for (int i = 0; i < groups.Length; i++) {
			var record = script.Groups[i];
			var orders = Orders(script, record);
			var kind = KindOf(record.MemberKind);

			groups[i] = new Group(
				i,
				kind,
				MissionPlacementRules.GroupSetup(kind, Coordinate(script, record.PositionRef),
					Heading(script, record.HeadingRef), Route(orders), record.FormationId, record.PaintsGround != 0,
					baseFormations),
				ActionRef(script, record.DeploymentActionRef),
				record.Side == (short)MissionSide.Cybrid
					? MissionSide.Cybrid
					: MissionSide.Human,
				orders);
		}

		return groups;
	}

	/// <summary>
	/// Each block-11 record's kind and member refs, a negative ref naming nothing, as
	/// <see cref="MissionPlacementRules.ClaimMembers{TKey}"/> takes them: the claims are keyed by roster slot.
	/// </summary>
	private static (MissionUnitKind Kind, IReadOnlyList<int?> Members)[] GroupMembers(ScriptDat script, Group[] groups) {
		var members = new (MissionUnitKind Kind, IReadOnlyList<int?> Members)[groups.Length];

		for (int i = 0; i < groups.Length; i++) {
			members[i] = (groups[i].Kind,
				Array.ConvertAll(script.Groups[i].MemberRefs, member => member >= 0 ? (int?)member : null));
		}

		return members;
	}

	/// <summary>Block 7's live slots, one placement each, in slot order.</summary>
	private static void AddMechs(ScriptDat script, Group[] groups, IReadOnlyDictionary<int, MissionMemberClaim> claims,
			UnitTypeNames mechNames, MechFormationTable mechFormations, List<MissionPlacement> placements) {
		for (int slot = 0; slot < script.Mechs.Length; slot++) {
			if (!claims.TryGetValue(slot, out var claim)) {
				continue;
			}

			var group = groups[claim.Group];
			var record = script.Mechs[slot];
			var place = MissionPlacementRules.PlaceHerc(group.Setup, mechFormations, claim.MemberIndex,
				Coordinate(script, record.PositionRef), Heading(script, record.HeadingRef));

			placements.Add(new MissionPlacement(
				MissionUnitKind.Mech,
				record.TypeIndex,
				mechNames[record.TypeIndex],
				slot,
				group.Index,
				place.Position,
				place.Heading,
				record.WeaponRefs,
				record.WeaponSecondary,
				Side: group.Side,
				AiCruiseSpeed: record.AiCruiseSpeed,
				AiRadarActive: record.AiRadarActive != 0,
				FormationOffset: place.FormationOffset,
				EngagementActionRef: ActionRef(script, record.EngagementActionRef),
				DefeatActionRef: ActionRef(script, record.DefeatActionRef),
				StartingCondition: record.StartingCondition,
				OutOfActionReport: new OutOfActionReport(record.CounterRefs, record.CounterOps),
				GroundPoint: place.GroundPoint));
		}
	}

	/// <summary>Block 8's live slots, one placement each, in slot order.</summary>
	private static void AddFlyers(ScriptDat script, Group[] groups, IReadOnlyDictionary<int, MissionMemberClaim> claims,
			UnitTypeNames flyerNames, FlyerFormationTable flyerFormations, List<MissionPlacement> placements) {
		for (int slot = 0; slot < script.Flyers.Length; slot++) {
			if (!claims.TryGetValue(slot, out var claim)) {
				continue;
			}

			var group = groups[claim.Group];
			var record = script.Flyers[slot];
			var place = MissionPlacementRules.PlaceFlyer(group.Setup, flyerFormations, claim.MemberIndex,
				Coordinate(script, record.PositionRef), Heading(script, record.HeadingRef));

			placements.Add(new MissionPlacement(
				MissionUnitKind.Flyer,
				record.TypeIndex,
				flyerNames[record.TypeIndex],
				slot,
				group.Index,
				place.Position,
				place.Heading,
				Array.Empty<short>(),
				Array.Empty<short>(),
				Side: group.Side,
				FlyerFormationOffset: place.FlyerFormationOffset,
				EngagementActionRef: ActionRef(script, record.EngagementActionRef),
				DefeatActionRef: ActionRef(script, record.DefeatActionRef),
				OutOfActionReport: new OutOfActionReport(record.CounterRefs, record.CounterOps)));
		}
	}

	/// <summary>Block 9's live slots, one placement each, in slot order.</summary>
	private static void AddBases(ScriptDat script, Group[] groups, IReadOnlyDictionary<int, MissionMemberClaim> claims,
			BaseFormationTable baseFormations, List<MissionPlacement> placements) {
		for (int slot = 0; slot < script.Bases.Length; slot++) {
			if (!claims.TryGetValue(slot, out var claim)) {
				continue;
			}

			var group = groups[claim.Group];
			var record = script.Bases[slot];
			var place = MissionPlacementRules.PlaceStructure(group.Setup, baseFormations, claim.MemberIndex,
				Coordinate(script, record.PositionRef), Heading(script, record.HeadingRef));

			placements.Add(new MissionPlacement(
				MissionUnitKind.Base,
				record.TypeIndex,
				null,
				slot,
				group.Index,
				place.Position,
				place.Heading,
				Array.Empty<short>(),
				Array.Empty<short>(),
				Side: group.Side,
				FormationOffset: place.FormationOffset,
				EngagementActionRef: ActionRef(script, record.EngagementActionRef),
				DefeatActionRef: ActionRef(script, record.DefeatActionRef),
				StartingCondition: record.StartingCondition,
				OutOfActionReport: new OutOfActionReport(record.CounterRefs, record.CounterOps)));
		}
	}

	/// <summary>
	/// Reads the player's squad and places it around block 11's reserved record-0 point, each entry as
	/// <see cref="MissionPlacementRules.PlaceSquadMember"/> spreads it. Returns the entry the player themself pilots,
	/// which is the one worth putting a camera on.
	///
	/// <para>Entry <i>i</i> takes as its <see cref="MissionPlacement.SlotIndex"/> the block-7 slot that
	/// record 0's member ref <i>i</i> names, or -1 when that ref is unset, so the mission's own mech
	/// refs to that slot resolve to it. The squad is added after the roster, so where a live group
	/// names the same slot the squad member wins. See
	/// docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 6 and 7.</para>
	///
	/// <para>A missing <c>player.mec</c> throws <see cref="MissingHandoffFileException"/>, as the
	/// original asserts on it — see docs/retail/formats/script-dat.md#call-chain--confirmed.</para>
	/// </summary>
	private static MissionPlacement? LoadPlayerSquad(string scriptPath, ScriptDat script, Group[] groups,
			MechFormationTable mechFormations, UnitTypeNames mechNames,
			List<MissionPlacement> placements) {
		string playerPath = PlayerPathFor(scriptPath);

		if (!File.Exists(playerPath)) {
			throw new MissingHandoffFileException(playerPath);
		}

		if (groups.Length == 0) {
			return null;
		}

		if (new MecFileTransformer().Parse(File.ReadAllBytes(playerPath)) is not MecFile squad) {
			return null;
		}

		var spawn = groups[MissionPlacementRules.SquadGroupIndex];
		var squadRefs = script.Groups[MissionPlacementRules.SquadGroupIndex].MemberRefs;
		MissionPlacement? player = null;

		for (int i = 0; i < squad.Entries.Length; i++) {
			var entry = squad.Entries[i];
			int slot = i < MissionPlacementRules.GroupMemberSlots && i < squadRefs.Length && squadRefs[i] >= 0
				? squadRefs[i]
				: -1;
			var place = MissionPlacementRules.PlaceSquadMember(spawn.Setup, mechFormations, i);

			var placement = new MissionPlacement(
				MissionUnitKind.Mech,
				entry.MechType,
				mechNames[entry.MechType],
				slot,
				MissionPlacementRules.SquadGroupIndex,
				place.Position,
				place.Heading,
				entry.WeaponRefs,
				entry.WeaponAmmoTypes,
				IsPlayerSquad: true,
				PilotIndex: entry.PilotNameIndex,
				Side: spawn.Side,
				FormationOffset: place.FormationOffset,
				SquadCondition: SquadCondition.Of(entry),
				GroundPoint: place.GroundPoint);

			placements.Add(placement);
			if (i == squad.PlayerEntryIndex) {
				player = placement;
			}
		}

		return player;
	}

	private static MissionUnitKind KindOf(short discriminator) => discriminator switch {
		0 => MissionUnitKind.Mech,
		1 => MissionUnitKind.Flyer,
		2 => MissionUnitKind.Base,
		// Retail data only ever carries 0/1/2. An unknown discriminator names no roster, so the
		// record claims nothing and its members simply never spawn — which is what DBSIM's own
		// if/else-if chain does with it too.
		_ => (MissionUnitKind)(-1)
	};

	private static Vec3i? Coordinate(ScriptDat script, short reference) =>
		reference >= 0 && reference < script.Coordinates.Length
			? new Vec3i(
				script.Coordinates[reference].X,
				script.Coordinates[reference].Y,
				script.Coordinates[reference].Z)
			: null;

	private static int? Heading(ScriptDat script, short reference) =>
		reference >= 0 && reference < script.Headings.Length
			? MissionPlacementRules.HeadingFromDegrees(script.Headings[reference].Degrees)
			: null;

	/// <summary>
	/// A block-11 record's ten row-15 links, resolved into the order records DBSIM builds from the
	/// same data — see <see cref="MissionOrder"/>. Slots the record left unset stay null, because
	/// <c>Group_OrderTick</c> (<c>00423a74</c>) advances only while the <i>next</i> slot holds
	/// something, so a gap stops a group where it stands.
	/// </summary>
	private static MissionOrder?[] Orders(ScriptDat script, ScriptGroup record) {
		var orders = new MissionOrder?[MissionOrder.Slots];

		for (int i = 0; i < orders.Length && i < record.OrderRefs.Length; i++) {
			short linkRef = record.OrderRefs[i];
			if (linkRef < 0 || linkRef >= script.Orders.Length) {
				continue;
			}

			var link = script.Orders[linkRef];
			var kind = (MissionOrderSubject)link.SubjectKind;

			orders[i] = new MissionOrder(
				link.Verb,
				Enum.IsDefined(kind) ? kind : MissionOrderSubject.None,
				link.SubjectRef,
				Waypoints(script, link.RouteRef),
				ActionRef(script, link.ActionRef),
				link.RouteRef);
		}

		return orders;
	}

	/// <summary>One block-3 waypoint group, resolved to block-1 coordinates.</summary>
	private static IReadOnlyList<Vec3i> Waypoints(ScriptDat script, short waypointGroupRef) {
		if (waypointGroupRef < 0 || waypointGroupRef >= script.WaypointGroups.Length) {
			return Array.Empty<Vec3i>();
		}

		return script.WaypointGroups[waypointGroupRef].Waypoints
			.Select(reference => Coordinate(script, reference))
			.OfType<Vec3i>()
			.ToArray();
	}

	/// <summary>
	/// The route a group walks, and the fallback for a group with no point of its own: it stands on
	/// the route's first waypoint and faces along its first leg (see <see cref="MissionPlacementRules.GroupSetup"/>),
	/// which is how every patrolling group and the player's own squad are placed.
	///
	/// <para><b>Slot 0's route, and only slot 0's.</b> <c>DBSim_BuildGroupRecord</c>
	/// (<c>00423b34</c>) loads the group's route cursor from the first order and nothing re-points
	/// it, so a group that advances to a later order keeps walking this one — see
	/// docs/retail/simulation/ai-goals.md.</para>
	/// </summary>
	private static IReadOnlyList<Vec3i> Route(MissionOrder?[] orders) =>
		orders[0]?.Route ?? Array.Empty<Vec3i>();
}
