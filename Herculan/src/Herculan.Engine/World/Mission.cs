using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>What class of thing a <see cref="MissionPlacement"/> is, and hence which roster it came from.</summary>
public enum MissionUnitKind {
	/// <summary>A HERC — <c>script.dat</c> block 7, typed by <c>nam\MECHS.NAM</c>.</summary>
	Mech,

	/// <summary>A flyer or ground vehicle — block 8, typed by <c>nam\FLYERS.NAM</c>.</summary>
	Flyer,

	/// <summary>A structure — block 9, typed by <c>dat\BASES.DAT</c>.</summary>
	Base
}

/// <summary>
/// Which side of the war an object belongs to — <c>script.dat</c> block 11's <c>0x6e</c>, which
/// <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>) copies into the in-memory group record's
/// <c>+0x12</c>.
///
/// <para>Every "is this one of ours" test in the simulation is a comparison of that one byte
/// between two objects' group records — the target filter, the detection sweep and the contact
/// share all read it. It is a property of the <i>group</i>, so every member of a group is on the
/// same side.</para>
/// </summary>
public enum MissionSide {
	/// <summary>Human. The player's own squad, and everything that fights alongside it.</summary>
	Human = 0,

	/// <summary>Cybrid. The only side the detection sweep scans <i>for</i> — see <c>Detection</c>.</summary>
	Cybrid = 1
}

/// <summary>
/// One object the mission puts in the world: what it is, where it stands and which way it faces.
/// </summary>
/// <param name="Kind">Which roster it came from.</param>
/// <param name="TypeIndex">
/// The roster record's type, by <paramref name="Kind"/>:
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptMechRecord.TypeIndex"/>,
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptFlyerRecord.TypeIndex"/> or
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptBaseRecord.TypeIndex"/>; for the player's
/// squad, <c>player.mec</c>'s <see cref="HercWorks.Core.Data.File.Sav.MecEntry.MechType"/>.
/// </param>
/// <param name="TypeName">
/// The resolved resource base name (<c>HYPERION</c>, <c>SKIMMER</c>) for mechs and flyers, or null
/// for bases, which are named by table index rather than by string.
/// </param>
/// <param name="SlotIndex">
/// The roster slot the mission's refs name it by: its record index within its <c>script.dat</c>
/// block, or for a machine of the player's squad the block-7 slot block 11 record 0 names in its
/// position, -1 when none. Order subjects, action targets and objective subjects resolve through it.
/// </param>
/// <param name="GroupIndex">The block-11 record that activated and placed it.</param>
/// <param name="Position">Spawn position in world units. Z is left at zero — the ground under a
/// spawn point is a terrain query the scene does once the zone is loaded, exactly as DBSIM does.</param>
/// <param name="Heading">Facing as a binary angle, already converted from the file's degrees.</param>
/// <param name="WeaponRefs">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptMechRecord.WeaponRefs"/>
/// Empty for anything but a mech, and <c>player.mec</c>'s for the player's squad; carried as the
/// file states it, holes and all, since the chassis' <c>.GL</c> hardpoint list indexes it.
/// </param>
/// <param name="WeaponSecondary">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptMechRecord.WeaponSecondary"/>
/// Empty for anything but a mech. See <see cref="Herculan.Engine.Sim.MechLoadout.SecondaryKeys"/>.
/// </param>
/// <param name="IsPlayerLance">
/// Whether this came from <c>player.mec</c> rather than the mission's own roster.
/// </param>
/// <param name="PilotIndex">
/// Which of <c>str\PILOTS.STR</c>'s 36 pilots flies it, or -1 for a machine no pilot is named for.
/// Only the player's own squad carries one: <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) stamps
/// <c>player.mec</c>'s own <see cref="HercWorks.Core.Data.File.Sav.MecEntry.PilotNameIndex"/> onto the
/// machine at <c>mech+0x29c</c> and nothing else ever writes that field. It is what names a comm box
/// and picks the portrait that talks in it — see <see cref="Sim.MechObject.PilotIndex"/>.
/// </param>
/// <param name="Side">
/// Whose side the group that placed it is on — see <see cref="MissionSide"/>. Carried per placement
/// rather than per group because that is the form everything downstream wants: the simulation reads
/// it off the object, not off a group record it does not have.
/// </param>
/// <param name="AiCruiseSpeed">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptMechRecord.AiCruiseSpeed"/>
/// </param>
/// <param name="AiRadarActive">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptMechRecord.AiRadarActive"/>
/// Carried as a bool: any nonzero value is ACTIVE.
/// </param>
/// <param name="EngagementActionRef">
/// The roster record's engagement action —
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptMechRecord.EngagementActionRef"/>,
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptFlyerRecord.EngagementActionRef"/> or
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptBaseRecord.EngagementActionRef"/> — with a
/// ref outside block 5 read as <c>-1</c>. See
/// <see cref="Herculan.Engine.Sim.SimObject.EngagementAction"/>.
/// </param>
/// <param name="DefeatActionRef">
/// The roster record's defeat action —
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptMechRecord.DefeatActionRef"/>,
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptFlyerRecord.DefeatActionRef"/> or
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptBaseRecord.DefeatActionRef"/> — with a ref
/// outside block 5 read as <c>-1</c>. See <see cref="Herculan.Engine.Sim.SimObject.DefeatAction"/>.
/// </param>
/// <param name="StartingCondition">
/// The roster record's starting condition, per cent —
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptMechRecord.StartingCondition"/> or
/// <see cref="HercWorks.Core.Data.File.Msn.Script.ScriptBaseRecord.StartingCondition"/>, which the
/// two classes read differently: see <see cref="Sim.MechObject.ApplyStartingCondition"/> and
/// <see cref="Sim.BaseObject.ApplyStartingCondition"/>. <see cref="PristineCondition"/> for anything
/// else.
/// </param>
/// <param name="FormationOffset">
/// This member's unrotated spread offset out of <c>MFORMS.DAT</c>, or null for the group's slot 0
/// and for a formation that names none. Resolved here because it is wanted twice: once to place the
/// machine, and again every tick a follower holds formation on its leader.
/// </param>
/// <param name="OutOfActionReport">
/// The roster record's ten mission-counter writes, made when the object goes out of the fight. See
/// <see cref="Herculan.Engine.Sim.SimObject.OutOfActionReport"/>; null for the player's squad, which
/// has no roster record.
/// </param>
/// <param name="SquadCondition">
/// The condition a squad machine carries in from its <c>player.mec</c> entry; null for anything the
/// mission's own roster places, which takes <paramref name="StartingCondition"/> instead.
/// </param>
/// <param name="FlyerFormationOffset">
/// The flyer twin of <paramref name="FormationOffset"/>, out of <c>FFORMS.DAT</c> and carrying a Z
/// as well — see <see cref="FlyerFormationTable"/>. A flyer wingman re-reads it every tick it holds
/// station, through <see cref="Sim.FlyerObject.FormationOffset"/>.
/// </param>
/// <param name="GroundPoint">
/// Where a mech's spawn height is read off the terrain: its group's point, before the formation
/// spread — <c>Mech_AttachToGroup</c> (<c>00417aa8</c>) queries the ground there and keeps that Z for
/// every member. Null for anything else, and for a mech whose roster record names its own point,
/// which the scene settles on the ground under that point.
/// </param>
public sealed record MissionPlacement(
	MissionUnitKind Kind,
	int TypeIndex,
	string? TypeName,
	int SlotIndex,
	int GroupIndex,
	Vec3i Position,
	int Heading,
	IReadOnlyList<short> WeaponRefs,
	IReadOnlyList<short> WeaponSecondary,
	bool IsPlayerLance = false,
	int PilotIndex = -1,
	MissionSide Side = MissionSide.Human,
	short AiCruiseSpeed = 0,
	bool AiRadarActive = false,
	(int X, int Y)? FormationOffset = null,
	Vec3i? FlyerFormationOffset = null,
	int EngagementActionRef = -1,
	int DefeatActionRef = -1,
	short StartingCondition = 100,
	OutOfActionReport? OutOfActionReport = null,
	SquadCondition? SquadCondition = null,
	Vec3i? GroundPoint = null) {

	/// <summary>
	/// The condition a machine the mission says nothing about starts in — full health, and the
	/// literal default of the constructor parameter above. Anything below
	/// <see cref="Sim.MechObject.UndamagedCondition"/> is pre-damaged at spawn; see
	/// <see cref="Sim.MechObject.ApplyStartingCondition"/>.
	/// </summary>
	public const short PristineCondition = 100;
}

/// <summary>
/// One patch of ground a base group paints with its formation's own material — the concrete pad a
/// retail base stands on. Produced for a group whose block-11 record sets its <c>PaintsGround</c> and
/// whose formation declares a layout; see
/// <see cref="Herculan.Engine.Terrain.HeightGrid.PaintFormationPad"/> for what is done with it.
/// </summary>
/// <param name="Anchor">
/// The position the painted tile is chosen from: the group's first-attached member, which is where
/// the original reads it. Only which tile it falls in matters, not where within that tile.
/// </param>
/// <param name="Layout">The formation's material index and occupancy map.</param>
public sealed record MissionBasePad(Vec3i Anchor, BaseFormationLayout Layout);

/// <summary>
/// A mission ready to be turned into a scene: which zone and theater it plays in, and every object
/// it places. This is the output of <see cref="MissionLoader"/> and carries no file-format detail —
/// a scene builder, a mission editor or a headless test all consume the same shape.
/// </summary>
public sealed class Mission {
	public Mission(string sourcePath, ScriptDatHeader header, IReadOnlyList<MissionPlacement> placements,
			MissionPlacement? player, IReadOnlyList<MissionBasePad> basePads,
			IReadOnlyList<Vec3i> coordinates, IReadOnlyList<Vec3i> playerRoute,
			IReadOnlyList<IReadOnlyList<MissionOrder?>> groupOrders,
			IReadOnlyList<MissionAction> actions,
			IReadOnlyList<MissionActionTimer> actionTimers,
			IReadOnlyList<int> groupDeploymentActions,
			IReadOnlyList<MissionUnitKind> groupKinds,
			IReadOnlyList<MissionSide> groupSides,
			IReadOnlyList<MissionObjective>? objectives = null,
			IReadOnlyList<int>? objectiveTextRefs = null,
			IReadOnlyList<string>? text = null,
			IReadOnlyList<short>? counters = null,
			IReadOnlyList<OutOfActionReport>? groupOutOfActionReports = null) {
		SourcePath = sourcePath;
		Header = header;
		Placements = placements;
		Player = player;
		BasePads = basePads;
		Coordinates = coordinates;
		PlayerRoute = playerRoute;
		GroupOrders = groupOrders;
		Actions = actions;
		ActionTimers = actionTimers;
		GroupDeploymentActions = groupDeploymentActions;
		GroupKinds = groupKinds;
		GroupSides = groupSides;
		Objectives = objectives ?? Array.Empty<MissionObjective>();
		ObjectiveTextRefs = objectiveTextRefs ?? Array.Empty<int>();
		Text = text ?? Array.Empty<string>();
		Counters = counters ?? Array.Empty<short>();
		GroupOutOfActionReports = groupOutOfActionReports ?? Array.Empty<OutOfActionReport>();
	}

	/// <summary>Where the <c>script.dat</c> was read from.</summary>
	public string SourcePath { get; }

	/// <summary>The zone, theater and theater variant to load.</summary>
	public ScriptDatHeader Header { get; }

	/// <summary>Every object the mission places, in spawn order.</summary>
	public IReadOnlyList<MissionPlacement> Placements { get; }

	/// <summary>
	/// The machine the player pilots, if <c>player.mec</c> was available — the natural place to put a
	/// camera, since it is where the mission actually starts.
	/// </summary>
	public MissionPlacement? Player { get; }

	/// <summary>Every patch of ground a base group repaints, in group order.</summary>
	public IReadOnlyList<MissionBasePad> BasePads { get; }

	/// <summary>
	/// <c>script.dat</c> block 1 in file order — every point the mission names, which is what its
	/// spawn points, waypoints and route legs all reference. Kept whole because the block's
	/// <i>extent</i> is a fact in its own right: <c>DBSim_LoadScriptDat</c> accumulates the bounding
	/// box as it reads and the Heads-Down Display's map is framed by it end to end. See
	/// <see cref="MissionBox"/>.
	/// </summary>
	public IReadOnlyList<Vec3i> Coordinates { get; }

	/// <summary>
	/// The route the player's own squad group carries, as block-1 points. The command display draws
	/// its first nine legs past the start as numbered waypoint markers.
	/// </summary>
	public IReadOnlyList<Vec3i> PlayerRoute { get; }

	/// <summary>
	/// Every group's order list, indexed by block-11 record index and ten slots wide with the unset
	/// ones left null — what the group works through, and where its AI machines get a state to be in.
	/// See <see cref="MissionOrder"/> and <see cref="Herculan.Engine.Sim.MissionGroup"/>.
	/// </summary>
	public IReadOnlyList<IReadOnlyList<MissionOrder?>> GroupOrders { get; }

	/// <summary>
	/// Block 5 in file order — every mission action, with its trigger areas resolved. See
	/// <see cref="MissionAction"/>; <see cref="Herculan.Engine.Sim.MissionTriggers"/> runs them.
	/// </summary>
	public IReadOnlyList<MissionAction> Actions { get; }

	/// <summary>
	/// Block 6 in file order — the mission's timers. See <see cref="MissionActionTimer"/>.
	/// </summary>
	public IReadOnlyList<MissionActionTimer> ActionTimers { get; }

	/// <summary>
	/// Which action each group is waiting on, by block-11 record index, with <c>-1</c> for a group
	/// that is in the mission from the start — the record's <c>0x70</c>, which becomes the group
	/// record's <c>+0x14</c> gate. See
	/// <see cref="Herculan.Engine.Sim.MissionGroup.AwaitingDeployment"/>.
	/// </summary>
	public IReadOnlyList<int> GroupDeploymentActions { get; }

	/// <summary>
	/// Each group's roster discriminator, by block-11 record index. A group with no live members
	/// still has one, which is why it is carried on the mission and not derived from a placement.
	/// </summary>
	public IReadOnlyList<MissionUnitKind> GroupKinds { get; }

	/// <summary>And each group's side, on the same terms.</summary>
	public IReadOnlyList<MissionSide> GroupSides { get; }

	/// <summary>
	/// Each group's mission-counter writes, made once all of it is out of the fight, by block-11
	/// record index. See <see cref="Herculan.Engine.Sim.MissionGroup.OutOfActionReport"/>.
	/// </summary>
	public IReadOnlyList<OutOfActionReport> GroupOutOfActionReports { get; }

	/// <summary>
	/// Block 12 in file order — what the mission wants done, and what loses it. See
	/// <see cref="MissionObjective"/>; <see cref="Herculan.Engine.Sim.MissionObjectives"/> runs them.
	/// </summary>
	public IReadOnlyList<MissionObjective> Objectives { get; }

	/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptDat.ObjectiveTextRefs"/>
	/// <remarks>Each is an index into <see cref="Text"/>.</remarks>
	public IReadOnlyList<int> ObjectiveTextRefs { get; }

	/// <summary>
	/// <c>data\mission.str</c>, flattened — the mission's own text: the objective lines the briefing
	/// screen lists and the description an objective's failure alert prints. A mission action's line
	/// is not in it — see <see cref="MissionAction.MessageId"/>. VSHELL writes it beside <c>script.dat</c> for the
	/// mission it is launching, so it is per-mission and not a shared catalogue.
	/// </summary>
	public IReadOnlyList<string> Text { get; }

	/// <summary>
	/// What the mission counters start at: <c>mission.var</c> as the shell left it, with the slots the
	/// load resets already zeroed. Empty for a mission built without a loader, which starts them all at
	/// zero. See <see cref="Herculan.Engine.Sim.SimWorld.MissionCounters"/>.
	/// </summary>
	public IReadOnlyList<short> Counters { get; }

	/// <summary>One line of <see cref="Text"/>, or the empty string for a ref outside it.</summary>
	public string TextAt(int reference) =>
		reference >= 0 && reference < Text.Count ? Text[reference] : string.Empty;

	/// <summary>
	/// The <see cref="MissionObjective.TextLines"/> consecutive lines that describe one objective,
	/// with a fourth empty one appended — the four the failure alert lays out. Lines past the end of
	/// the file come back empty, as the original's own pointer copy would leave them.
	/// </summary>
	public IReadOnlyList<string> DescriptionOf(MissionObjective objective) {
		var lines = new string[MissionObjective.TextLines + 1];

		for (int i = 0; i < MissionObjective.TextLines; i++) {
			lines[i] = objective.TextRef < 0 ? string.Empty : TextAt(objective.TextRef + i);
		}

		lines[MissionObjective.TextLines] = string.Empty;
		return lines;
	}

	/// <summary>How many placed objects of one kind the mission has.</summary>
	public int CountOf(MissionUnitKind kind) => Placements.Count(p => p.Kind == kind);
}
