using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>
/// Turns the game's own mission handoff — <c>DATA\script.dat</c>, with the <c>mission.var</c> and
/// <c>player.mec</c> beside it — into a <see cref="Mission"/>.
///
/// <para><b>The two-pass structure below is DBSIM's, not an invention.</b> It is easy to read
/// <c>DBSim_LoadScriptDat</c> (<c>00424308</c>) and conclude the format throws its placement data
/// away: that function reads all 134 bytes of a block-7 record and keeps only the type field, and it
/// reads block 11 into a stack buffer and keeps only which slots the record marks live. It does not
/// place anything because placing is not its job — it counts live objects and allocates the pools.
/// <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) then <b>re-opens the same file</b> and walks it again, and that is the pass
/// that builds objects. Anything reading only the first pass will conclude, wrongly, that
/// <c>script.dat</c> carries no positions.</para>
///
/// <para>The placement rule that pass implements, with the RE for each step:</para>
/// <list type="number">
/// <item><b>Which objects exist.</b> Every block-11 record past the first is an activation
/// directive: a 3-way discriminator naming a roster (mech/flyer/base) and up to 20 refs into it.
/// Each ref marks that roster slot live. Records that mark nothing spawn nothing, which is why a
/// mission's roster is routinely larger than its live object count.</item>
/// <item><b>Where they stand.</b> The same block-11 record carries a position ref, a heading ref and
/// a route, and <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>) builds it into a group record holding all three. Each member is
/// then attached by <c>Mech_AttachToGroup</c> (<c>00417aa8</c>), which fills in the member's position <i>only if the object
/// does not already have one of its own</i> — so a roster record's own position ref wins where it is
/// set, and the group's is the fallback. In retail data the roster refs are always unset, so in
/// practice every object stands at its group's point.</item>
/// <item><b>Groups with no point of their own</b> fall back to their route: the group's row-15 links
/// resolve to a waypoint group, and <c>Route_WaypointAt</c> (<c>00423b0c</c>) takes a waypoint from it. Patrol lances are
/// placed this way — the retail mission's three mech lances all are.</item>
/// <item><b>The player's lance</b> is not in <c>script.dat</c>'s roster at all. Block 11's
/// <b>record 0</b> exists only to hold its spawn point: <c>DBSim_LoadScriptDat</c> skips it during
/// activation, and <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) overwrites its member list with the entries read from
/// <c>player.mec</c>.</item>
/// </list>
///
/// <para><b>Formation offsets: mechs and bases implemented, flyers still a known gap.</b> Every
/// member of a group is placed on the group's own point by the rule above; the original then
/// spreads non-leader members off that point via a per-kind vtable <c>+0x78</c> call
/// (<c>Mech_ApplyFormationOffset</c> (<c>00417898</c>) for mechs, <c>Base_ApplyFormationOffset</c> (<c>00405c04</c>) for bases —
/// same slot, same "member index 0 takes no offset" rule). Both tables are implemented:
/// <see cref="MechFormationTable"/> (<c>dat\MFORMS.DAT</c>) and <see cref="BaseFormationTable"/>
/// (<c>dat\BFORMS.DAT</c>) — see each class's doc comment for its load-site RE and byte-exact
/// verification. Flyers remain unfixed; retail missions were not seen putting more than one flyer
/// in a group.</para>
///
/// <para><b>Not every group is in the mission when it starts.</b> A block-11 record whose
/// <c>ActionRef</c> names a block-5 action is <i>waiting on that action</i>, and until it fires the
/// group is not in the world at all — see <see cref="Sim.SimObject.AwaitingDeployment"/> for the
/// three places the original tests it. Such a group's placed position is a placeholder, which is
/// why retail missions happily leave several of them stacked on the player's own spawn point: the
/// three DIABLO groups in the shipped mission-10 handoff all sit exactly there, invisible, until
/// they arrive somewhere else entirely.</para>
///
/// <para><b>Arrival</b> is <c>Group_DeploymentCheck</c> (<c>004236c4</c>), whose per-verb rules —
/// drop pod, on foot, or in place — are in docs/retail/simulation/mission-deployment.md and implemented in
/// <see cref="Sim.MissionGroup.DeploymentCheck"/>. What this loader owes it is <b>which</b> action a
/// group waits on, not merely that it waits on one: the same record carries the verb the group
/// arrives on.</para>
/// </summary>
public static class MissionLoader {
	/// <summary>Folder inside an install root holding the loose mission handoff files.</summary>
	public const string DataFolderName = "DATA";

	/// <summary>The mission handoff itself.</summary>
	public const string ScriptFileName = "script.dat";

	/// <summary>The player's lance, written beside it.</summary>
	public const string PlayerFileName = "player.mec";

	/// <summary>The mission's own text, written beside it as well.</summary>
	public const string TextFileName = "mission.str";

	/// <summary>The campaign flag array the mission counters start from, and are written back to.</summary>
	public const string CountersFileName = "mission.var";

	/// <summary>
	/// The lance file that goes with a mission file. The live pair in <c>DATA\</c> is
	/// <c>script.dat</c>/<c>player.mec</c>; the save-slot snapshots in <c>SAV\</c> keep the same
	/// pairing with a slot number on both halves (<c>script3.dat</c>/<c>player3.mec</c>).
	/// </summary>
	public static string PlayerPathFor(string scriptPath) {
		string directory = Path.GetDirectoryName(scriptPath) ?? ".";
		string slot = Path.GetFileNameWithoutExtension(scriptPath);

		slot = slot.StartsWith("script", StringComparison.OrdinalIgnoreCase)
			? slot["script".Length..]
			: string.Empty;

		return Path.Combine(directory, $"player{slot}.mec");
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
			return Path.Combine(directory, TextFileName);
		}

		string slot = name["script".Length..];
		return Path.Combine(directory, slot.Length == 0 ? TextFileName : $"missn{slot}.str");
	}

	/// <summary>
	/// Degrees-to-binary-angle constant. DBSIM applies this to every block-2 value as it reads them
	/// (<c>*(short *)local_7c = local_102[0] * 0xb6</c>), which is what identifies that block as
	/// headings in degrees rather than an opaque discrete field.
	/// </summary>
	private const int DegreesToBinaryAngle = 0xb6;

	/// <summary>The block-11 record reserved for the player's lance; it activates nothing.</summary>
	private const int PlayerGroupIndex = 0;

	/// <summary>Members a block-11 record can name.</summary>
	private const int GroupMemberSlots = 20;

	/// <summary>Row-15 links a block-11 record can name.</summary>
	private const int GroupRouteSlots = 10;

	/// <summary>The conventional path of a mission handoff inside an install root.</summary>
	public static string DefaultScriptPath(string installRoot) =>
		Path.Combine(installRoot, DataFolderName, ScriptFileName);

	/// <summary>
	/// Loads the mission at <paramref name="scriptPath"/>. <paramref name="content"/> supplies the
	/// type-name lists, which live in the archives rather than beside the mission.
	///
	/// <para><c>mission.var</c> and <c>player.mec</c> are looked for next to the script, and the load
	/// throws <see cref="MissingHandoffFileException"/> when either is not there, as the original
	/// asserts on each.</para>
	/// </summary>
	public static Mission Load(GameContent content, string scriptPath) {
		byte[] scriptBytes = File.ReadAllBytes(scriptPath);
		var script = new ScriptDatTransformer().Parse(scriptBytes) as ScriptDat
			?? throw new InvalidDataException($"{scriptPath} did not parse as a script.dat.");

		var header = ScriptDatHeader.From(script);
		var mechNames = UnitTypeNames.LoadMechs(content);
		var flyerNames = UnitTypeNames.LoadFlyers(content);
		var mechFormations = MechFormationTable.Load(content);
		var flyerFormations = FlyerFormationTable.Load(content);
		var baseFormations = BaseFormationTable.Load(content);

		var groups = ResolveGroups(script, baseFormations);
		var claims = ClaimSlots(script, groups);

		var placements = new List<MissionPlacement>();
		AddRoster(script, claims, mechNames, flyerNames, mechFormations, flyerFormations, baseFormations,
			placements);

		var counters = LoadCounters(Path.Combine(Path.GetDirectoryName(scriptPath) ?? ".", CountersFileName));
		var player = LoadPlayerLance(scriptPath, script, groups, mechFormations, mechNames, placements);
		var basePads = ResolveBasePads(groups, claims[MissionUnitKind.Base], baseFormations, placements);

		var coordinates = new List<Vec3i>(script.Coordinates.Length);
		foreach (var point in script.Coordinates) {
			coordinates.Add(new Vec3i(point.X, point.Y, point.Z));
		}

		var playerRoute = groups.Length > PlayerGroupIndex
			? Route(groups[PlayerGroupIndex].Orders)
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
	/// is scaled by ten into a radius.
	/// </summary>
	private static MissionTriggerArea? TriggerArea(ScriptDat script, short reference) {
		if (reference >= script.TriggerAreas.Length) {
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
	/// The ground each base group repaints. <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) runs
	/// this for a group whose <c>MemberKind</c> is 2 and whose <c>PaintsGround</c> is set, passing
	/// <c>Base_ApplyFormationTerrain</c> (<c>00405db0</c>) the group's <i>first-attached</i> member —
	/// the lowest populated slot of its <c>MemberRefs</c> array, not necessarily slot 0,
	/// though every retail base group that paints does populate slot 0.
	/// </summary>
	private static List<MissionBasePad> ResolveBasePads(Group[] groups,
			Dictionary<int, Claim> baseClaims, BaseFormationTable baseFormations,
			List<MissionPlacement> placements) {
		var pads = new List<MissionBasePad>();

		foreach (var group in groups) {
			if (group.Kind != MissionUnitKind.Base || !group.PaintsGround) {
				continue;
			}

			if (baseFormations.LayoutFor(group.FormationId) is not { } layout) {
				continue;
			}

			int leadSlot = -1;
			int leadMember = int.MaxValue;
			foreach (var (slot, claim) in baseClaims) {
				if (claim.Group.Index == group.Index && claim.MemberIndex < leadMember) {
					leadMember = claim.MemberIndex;
					leadSlot = slot;
				}
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
	/// action. Reducing it to a bool loses the arrival — see this class's doc comment, and
	/// <see cref="Sim.MissionGroup.DeploymentCheck"/>.
	/// </param>
	/// <param name="Side">
	/// The record's <c>0x6e</c> — <c>ScriptGroup.Side</c>, which lands at the
	/// in-memory group record's <c>+0x12</c> (<c>DBSim_BuildGroupRecord</c>, <c>00423b34</c>:
	/// <c>group[+0x12] = record[0x6e]</c>). Anything other than 1 is taken as human, which is what
	/// every comparison in the simulation amounts to — the sweep in <c>Detection</c> is the only
	/// place that tests for a literal value, and it tests for Cybrid.
	/// </param>
	/// <param name="PaintsGround">
	/// The record's <c>PaintsGround</c> (raw msn offset <c>0x06</c>), set on 39 of the 61 retail
	/// block-11 records. For a base group it gates both halves of the base-formation terrain pass:
	/// the anchor move applied below, and the repaint of the same tile — see
	/// <see cref="MissionBasePad"/> and <see cref="BaseFormationLayout"/>.
	/// </param>
	/// <param name="Orders">
	/// The record's ten row-15 links resolved into order records, nulls kept in the slots it left
	/// unset — <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>) copies the array position for position
	/// into <c>group+0x44</c>, and <c>Group_OrderTick</c> reads it by index. See
	/// docs/retail/simulation/ai-goals.md.
	/// </param>
	/// <param name="AuthoredPoint"><inheritdoc cref="MissionGroupSetup.AuthoredPoint"/></param>
	/// <param name="PointSource"><inheritdoc cref="MissionGroupSetup.PointSource"/></param>
	/// <param name="HeadingFromRoute"><inheritdoc cref="MissionGroupSetup.HeadingFromRoute"/></param>
	private readonly record struct Group(int Index, MissionUnitKind Kind, Vec3i Position, int Heading,
		int FormationId, int DeploymentAction, MissionSide Side, bool PaintsGround,
		MissionOrder?[] Orders, Vec3i AuthoredPoint, MissionGroupPointSource PointSource,
		bool HeadingFromRoute) {

		/// <summary>The public view of the record, for <see cref="Mission.GroupSetups"/>.</summary>
		public MissionGroupSetup Setup => new(Position, AuthoredPoint, PointSource, Heading, HeadingFromRoute,
			FormationId, PaintsGround);
	}

	/// <summary>
	/// A roster slot's claim: which group activated it, and the slot's index within that group's
	/// <c>MemberRefs</c> array — the "member index" <see cref="BaseFormationTable.OffsetFor"/>
	/// needs, which is the array position, not a compacted count of live members (see
	/// <see cref="ClaimSlots"/>).
	/// </summary>
	private readonly record struct Claim(Group Group, int MemberIndex);

	private static Group[] ResolveGroups(ScriptDat script, BaseFormationTable baseFormations) {
		var groups = new Group[script.Groups.Length];

		for (int i = 0; i < groups.Length; i++) {
			var record = script.Groups[i];
			var orders = Orders(script, record);
			var route = Route(orders);
			var kind = KindOf(record.MemberKind);

			var ownPoint = Coordinate(script, record.PositionRef);
			var position = ownPoint
				?? (route.Count > 0 ? route[0] : (Vec3i?)null)
				?? Vec3i.Zero;
			var authored = position;
			var pointSource = ownPoint != null ? MissionGroupPointSource.OwnPoint
				: route.Count > 0 ? MissionGroupPointSource.RouteStart
				: MissionGroupPointSource.None;
			var ownHeading = Heading(script, record.HeadingRef);

			// A structure group that paints its ground also stands at a fixed spot on it. The
			// mission's own point only picks the tile; the formation supplies the position within
			// it. Machines never do this -- Mech_AttachToGroup (00417aa8) takes no such flag and
			// has no equivalent arithmetic.
			if (kind == MissionUnitKind.Base && record.PaintsGround != 0
				&& baseFormations.LayoutFor(record.FormationId) is { } layout) {
				var (snappedX, snappedY) = layout.SnapAnchor(position.X, position.Y);
				position = new Vec3i(snappedX, snappedY, position.Z);
			}

			groups[i] = new Group(
				i,
				kind,
				position,
				ownHeading ?? RouteBearing(route),
				record.FormationId,
				ActionRef(script, record.DeploymentActionRef),
				record.Side == (short)MissionSide.Cybrid
					? MissionSide.Cybrid
					: MissionSide.Human,
				record.PaintsGround != 0,
				orders,
				authored,
				pointSource,
				ownHeading == null);
		}

		return groups;
	}

	/// <summary>
	/// Which way a group faces when its own record names no heading: along the first leg of its
	/// route. <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) takes the route's first two
	/// waypoints and calls <c>Math_HeadingToward</c> (<c>00492828</c>) with the second one first, which is
	/// <c>atan2(dy, dx) - 0x4000</c> — the same quarter turn every bearing in the simulation carries,
	/// since a machine's forward axis is model Y rather than model X.
	///
	/// <para>A route with fewer than two waypoints leaves the heading at zero, which is the branch
	/// the original guards with its <c>1 &lt; waypointCount</c> test. Retail missions reach this for
	/// every patrolling group <i>and</i> for the player's own squad — none of those records carry a
	/// heading ref — so without it a whole mission faced due north and every formation spread was
	/// rotated by the wrong angle.</para>
	/// </summary>
	private static int RouteBearing(IReadOnlyList<Vec3i> route) {
		if (route.Count < 2) {
			return 0;
		}

		int dx = route[1].X - route[0].X;
		int dy = route[1].Y - route[0].Y;

		// Math_Atan2Guarded (00492800)'s degenerate guard, which nudges x — not y — so a zero-length first leg reads
		// as a bearing of zero rather than a quarter turn off it.
		if (dx == 0 && dy == 0) {
			dx = 1;
		}

		return (SimTrig.Atan2(dy, dx) - BinaryAngle.QuarterTurn) & 0xffff;
	}

	/// <summary>
	/// First-claim map, one entry per roster: slot index to the group that places it, and the slot's
	/// position within that group's member-ref array. Groups are walked in file order and the first
	/// to name a slot wins, matching <c>Mech_AttachToGroup</c> (<c>00417aa8</c>)'s "only if it has no
	/// position yet" test. The player's group is skipped, since it activates nothing.
	///
	/// <para>The member index recorded here is the raw array position (0-19), <b>not</b> a count of
	/// live members seen so far — <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>) passes the loop
	/// index itself as the attach-time slot, gaps and all, and that is what <c>BaseFormationTable</c>
	/// indexes by.</para>
	/// </summary>
	private static Dictionary<MissionUnitKind, Dictionary<int, Claim>> ClaimSlots(
			ScriptDat script, Group[] groups) {
		var claims = new Dictionary<MissionUnitKind, Dictionary<int, Claim>> {
			[MissionUnitKind.Mech] = new(),
			[MissionUnitKind.Flyer] = new(),
			[MissionUnitKind.Base] = new()
		};

		for (int i = PlayerGroupIndex + 1; i < groups.Length; i++) {
			var group = groups[i];
			if (!claims.TryGetValue(group.Kind, out var bySlot)) {
				continue;
			}

			var refs = script.Groups[i].MemberRefs;
			for (int slot = 0; slot < GroupMemberSlots && slot < refs.Length; slot++) {
				short member = refs[slot];
				if (member >= 0) {
					bySlot.TryAdd(member, new Claim(group, slot));
				}
			}
		}

		return claims;
	}

	/// <summary>
	/// Walks the three rosters in the order DBSIM's spawn pass does, emitting one placement per live
	/// slot.
	/// </summary>
	private static void AddRoster(ScriptDat script,
			Dictionary<MissionUnitKind, Dictionary<int, Claim>> claims,
			UnitTypeNames mechNames, UnitTypeNames flyerNames, MechFormationTable mechFormations,
			FlyerFormationTable flyerFormations, BaseFormationTable baseFormations,
			List<MissionPlacement> placements) {
		var mechClaims = claims[MissionUnitKind.Mech];
		for (int slot = 0; slot < script.Mechs.Length; slot++) {
			if (!mechClaims.TryGetValue(slot, out var claim)) {
				continue;
			}

			var group = claim.Group;
			var record = script.Mechs[slot];
			var offset = mechFormations.OffsetFor(group.FormationId, claim.MemberIndex);
			var ownPoint = Coordinate(script, record.PositionRef);
			var position = ownPoint ?? OffsetFromGroup(group, mechFormations, claim.MemberIndex);

			placements.Add(new MissionPlacement(
				MissionUnitKind.Mech,
				record.TypeIndex,
				mechNames[record.TypeIndex],
				slot,
				group.Index,
				position,
				Heading(script, record.HeadingRef) ?? group.Heading,
				record.WeaponRefs,
				record.WeaponSecondary,
				Side: group.Side,
				AiCruiseSpeed: record.AiCruiseSpeed,
				AiRadarActive: record.AiRadarActive != 0,
				FormationOffset: offset is { } o ? (o.X, o.Y) : null,
				EngagementActionRef: ActionRef(script, record.EngagementActionRef),
				DefeatActionRef: ActionRef(script, record.DefeatActionRef),
				StartingCondition: record.StartingCondition,
				OutOfActionReport: new OutOfActionReport(record.CounterRefs, record.CounterOps),
				GroundPoint: ownPoint == null ? group.Position : null));
		}

		var flyerClaims = claims[MissionUnitKind.Flyer];
		for (int slot = 0; slot < script.Flyers.Length; slot++) {
			if (!flyerClaims.TryGetValue(slot, out var claim)) {
				continue;
			}

			var group = claim.Group;
			var record = script.Flyers[slot];
			var offset = flyerFormations.OffsetFor(group.FormationId, claim.MemberIndex);
			placements.Add(new MissionPlacement(
				MissionUnitKind.Flyer,
				record.TypeIndex,
				flyerNames[record.TypeIndex],
				slot,
				group.Index,
				Coordinate(script, record.PositionRef)
					?? OffsetFromGroup(group, flyerFormations, claim.MemberIndex),
				Heading(script, record.HeadingRef) ?? group.Heading,
				Array.Empty<short>(),
				Array.Empty<short>(),
				Side: group.Side,
				FlyerFormationOffset: offset,
				EngagementActionRef: ActionRef(script, record.EngagementActionRef),
				DefeatActionRef: ActionRef(script, record.DefeatActionRef),
				OutOfActionReport: new OutOfActionReport(record.CounterRefs, record.CounterOps)));
		}

		var baseClaims = claims[MissionUnitKind.Base];
		for (int slot = 0; slot < script.Bases.Length; slot++) {
			if (!baseClaims.TryGetValue(slot, out var claim)) {
				continue;
			}

			var group = claim.Group;
			var record = script.Bases[slot];
			var offset = baseFormations.OffsetFor(group.FormationId, claim.MemberIndex);
			var position = Coordinate(script, record.PositionRef)
				?? OffsetFromGroup(group, baseFormations, claim.MemberIndex);

			placements.Add(new MissionPlacement(
				MissionUnitKind.Base,
				record.TypeIndex,
				null,
				slot,
				group.Index,
				position,
				Heading(script, record.HeadingRef)
					?? HeadingFromGroup(group, baseFormations, claim.MemberIndex),
				Array.Empty<short>(),
				Array.Empty<short>(),
				Side: group.Side,
				FormationOffset: offset is { } o ? (o.X, o.Y) : null,
				EngagementActionRef: ActionRef(script, record.EngagementActionRef),
				DefeatActionRef: ActionRef(script, record.DefeatActionRef),
				StartingCondition: record.StartingCondition,
				OutOfActionReport: new OutOfActionReport(record.CounterRefs, record.CounterOps)));
		}
	}

	/// <summary>
	/// Which way a structure faces when its own record names no heading: its group's heading plus its
	/// formation slot's own turn — see <see cref="BaseFormationTable.HeadingNudgeFor"/> for the trace
	/// and for the evidence that this is a real per-slot field and not a spread artefact.
	///
	/// <para>The cast is the original's: <c>Base_AttachToGroup</c> accumulates into a <c>short</c>, so
	/// the sum wraps rather than running past a full turn.</para>
	/// </summary>
	private static int HeadingFromGroup(Group group, BaseFormationTable baseFormations, int memberIndex) =>
		(short)(group.Heading + baseFormations.HeadingNudgeFor(group.FormationId, memberIndex));

	/// <summary>
	/// A base's spawn point when it carries no coordinate of its own: the group's point, spread by
	/// its <see cref="BaseFormationTable"/> entry for this member's slot (member 0, the group's
	/// first-claimed slot, always takes no offset — see that class's doc comment) and rotated by the
	/// group's own heading, mirroring <c>Formation_RotateAndAddOffset</c> (<c>00411d64</c>).
	/// </summary>
	private static Vec3i OffsetFromGroup(Group group, BaseFormationTable baseFormations, int memberIndex) {
		var offset = baseFormations.OffsetFor(group.FormationId, memberIndex);
		return offset is { } o ? Rotate(group.Position, group.Heading, o.X, o.Y) : group.Position;
	}

	/// <summary>
	/// A mech's spawn point when it carries no coordinate of its own: the group's point, spread by
	/// its <see cref="MechFormationTable"/> entry for this member's slot and rotated by the group's
	/// heading — the mech-side twin of the base overload above, both implementing
	/// <c>Mech_ApplyFormationOffset</c>/<c>Base_ApplyFormationOffset</c>'s shared
	/// <c>Formation_RotateAndAddOffset</c> (<c>00411d64</c>) rule.
	/// </summary>
	private static Vec3i OffsetFromGroup(Group group, MechFormationTable mechFormations, int memberIndex) {
		var offset = mechFormations.OffsetFor(group.FormationId, memberIndex);
		return offset is { } o ? Rotate(group.Position, group.Heading, o.X, o.Y) : group.Position;
	}

	/// <summary>
	/// A flyer's spawn point when its own record names no coordinate — <c>Flyer_AttachToGroup</c> (<c>00421ee8</c>)'s
	/// unset-position branch, which takes the group's point and runs it through the flyer's own
	/// <c>+0x78</c> formation slot. The Z rides along, which is what staggers a flight vertically;
	/// the ground classes' offsets have none.
	/// </summary>
	private static Vec3i OffsetFromGroup(Group group, FlyerFormationTable flyerFormations,
			int memberIndex) {
		if (flyerFormations.OffsetFor(group.FormationId, memberIndex) is not { } offset) {
			return group.Position;
		}

		var spread = Rotate(group.Position, group.Heading, offset.X, offset.Y);
		return new Vec3i(spread.X, spread.Y, spread.Z + offset.Z);
	}

	/// <summary>
	/// Rotates a formation offset by the group's heading and adds it to <paramref name="anchor"/> —
	/// the fixed-point 2D rotation <c>Formation_RotateAndAddOffset</c> (<c>00411d64</c>) reduces to
	/// for a heading-only object (no pitch/roll), shared by every formation kind.
	/// </summary>
	private static Vec3i Rotate(Vec3i anchor, int heading, int dx, int dy) {
		short cos = BinaryAngle.Cos(heading);
		short sin = BinaryAngle.Sin(heading);
		int worldDx = (int)(((long)dx * cos - (long)dy * sin + 0x2000) >> 14);
		int worldDy = (int)(((long)dx * sin + (long)dy * cos + 0x2000) >> 14);

		return new Vec3i(anchor.X + worldDx, anchor.Y + worldDy, anchor.Z);
	}

	/// <summary>
	/// Reads the player's squad and places it around block 11's reserved record-0 point. Returns the
	/// entry the player themself pilots, which is the one worth putting a camera on.
	///
	/// <para>The squad spreads exactly as any other group does, because in the original it <i>is</i>
	/// one: <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) gives every <c>player.mec</c> entry
	/// the unset-position sentinel and writes the entries into record 0's member array in file
	/// order, so <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>) attaches entry <i>i</i> as member
	/// slot <i>i</i> and <c>Mech_ApplyFormationOffset</c> (<c>00417898</c>) spreads every slot past
	/// the first. The same offset is the squadmate's post every tick it keeps formation on the
	/// player (<see cref="Sim.MechObject.FormationOffset"/>); without it the post is the player's own
	/// position, and <c>Mech_CollisionTest</c> refuses a position that overlaps another machine.</para>
	///
	/// <para>Entry <i>i</i> takes as its <see cref="MissionPlacement.SlotIndex"/> the block-7 slot that
	/// record 0's member ref <i>i</i> names, or -1 when that ref is unset, so the mission's own mech
	/// refs to that slot resolve to it. The squad is added after the roster, so where a live group
	/// names the same slot the squad member wins. See
	/// docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 6.</para>
	///
	/// <para>A missing <c>player.mec</c> throws <see cref="MissingHandoffFileException"/>, as the
	/// original asserts on it — see docs/retail/formats/script-dat.md#call-chain--confirmed.</para>
	/// </summary>
	private static MissionPlacement? LoadPlayerLance(string scriptPath, ScriptDat script, Group[] groups,
			MechFormationTable mechFormations, UnitTypeNames mechNames,
			List<MissionPlacement> placements) {
		string playerPath = PlayerPathFor(scriptPath);

		if (!File.Exists(playerPath)) {
			throw new MissingHandoffFileException(playerPath);
		}

		if (groups.Length == 0) {
			return null;
		}

		if (new MecFileTransformer().Parse(File.ReadAllBytes(playerPath)) is not MecFile lance) {
			return null;
		}

		var spawn = groups[PlayerGroupIndex];
		var squadRefs = script.Groups[PlayerGroupIndex].MemberRefs;
		MissionPlacement? player = null;

		for (int i = 0; i < lance.Entries.Length; i++) {
			var entry = lance.Entries[i];
			int slot = i < GroupMemberSlots && i < squadRefs.Length && squadRefs[i] >= 0
				? squadRefs[i]
				: -1;
			var offset = mechFormations.OffsetFor(spawn.FormationId, i);

			var placement = new MissionPlacement(
				MissionUnitKind.Mech,
				entry.MechType,
				mechNames[entry.MechType],
				slot,
				PlayerGroupIndex,
				OffsetFromGroup(spawn, mechFormations, i),
				spawn.Heading,
				entry.WeaponRefs,
				entry.WeaponAmmoTypes,
				IsPlayerLance: true,
				PilotIndex: entry.PilotNameIndex,
				Side: spawn.Side,
				FormationOffset: offset is { } o ? (o.X, o.Y) : null,
				SquadCondition: SquadCondition.Of(entry),
				GroundPoint: spawn.Position);

			placements.Add(placement);
			if (i == lance.PlayerEntryIndex) {
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
			? script.Headings[reference].Degrees * DegreesToBinaryAngle
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
	/// the route's first waypoint and faces along its first leg (see <see cref="RouteBearing"/>),
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
