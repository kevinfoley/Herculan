using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The running simulation: one loaded zone's terrain plus the live object list, advanced on a fixed
/// timestep. This mirrors DBSIM's per-frame sim tick (<c>Sim_MainTick</c>, <c>0045f464</c>), which refreshes the
/// global timestep from a timer and then walks each global object list calling every live object's
/// per-tick update.
///
/// <para>Deliberately holds no rendering state. Per docs/herculan/planning.md's "library core + thin
/// front-end host" decision, a world can be ticked by a game loop, a future mission editor, or a
/// headless test with no assumption that a window exists.</para>
/// </summary>
public sealed class SimWorld {
	private readonly List<SimObject> _objects = new();
	private readonly List<WeaponShot> _beams = new();
	private readonly List<WeaponShot> _impacts = new();
	private readonly List<BeamTracer> _tracers = new();
	private readonly List<Projectile> _projectiles = new();
	private readonly List<Rocket> _rockets = new();
	private readonly List<ImpactEffect> _effects = new();
	private readonly List<DebrisObject> _debris = new();
	private readonly List<FireEffect> _fires = new();

	/// <param name="terrain">The loaded zone.</param>
	/// <param name="bullets">
	/// <c>dat\BULLETS.DAT</c>, which everything that fires a travelling shot needs — see
	/// <see cref="FireBullet"/>. Null leaves those weapons firing blanks, the same way an unported
	/// branch does.
	/// </param>
	/// <param name="explosions">
	/// <c>dat\EXPLOS.DAT</c>, which everything that lands needs — see
	/// <see cref="SpawnImpactEffect"/>. Null leaves impacts invisible.
	/// </param>
	/// <param name="rockets">
	/// <c>dat\ROCKETS.DAT</c>, which every launcher needs — see <see cref="FireRocket"/>. Null leaves
	/// them firing blanks, as null <paramref name="bullets"/> does for the guns.
	/// </param>
	/// <param name="beams">
	/// <c>dat\BEAM.DAT</c>. This is simulation state and not only a drawing detail: an ELF's tracer
	/// bakes the table's half-width into its geometry at construction time, and rolls the chain's
	/// jitter off <see cref="Random"/> while it does — see <see cref="BeamTracer"/>. Null leaves a
	/// jagged beam with a zero-width ribbon, which draws as nothing.
	/// </param>
	/// <param name="debris">
	/// The debris databases, headed by <c>DEF_DEB</c> — see <see cref="SpawnDebris"/>. Null leaves
	/// every destruction throwing nothing, which is what this engine did before the pool existed.
	/// </param>
	/// <param name="random">
	/// The generator this world rolls on. DBSIM's simulation shares one, from the load-time terrain
	/// pass onward, so a caller that rolls before the world exists should build it and hand the same
	/// instance in rather than letting this make a second one. Omitted, it starts at
	/// <see cref="SimRandom"/>'s vanilla state.
	/// </param>
	public SimWorld(HeightGrid terrain, BulletCatalog? bullets = null,
			ExplosionCatalog? explosions = null, RocketCatalog? rockets = null,
			BeamAppearance? beams = null, SimRandom? random = null, DebrisCatalog? debris = null) {
		Terrain = terrain;
		Bullets = bullets;
		Explosions = explosions;
		Rockets = rockets;
		BeamTable = beams;
		Debris = debris;
		Random = random ?? new SimRandom();
	}

	private int[] _fireShapeFrames = Array.Empty<int>();

	/// <summary>
	/// Tells the world how many frames each root of <c>dts\FIRE.DTS</c> has, which is what times a
	/// burning object's loop — see <see cref="FireEffect"/>. Supplied rather than read here for the
	/// reason <see cref="ExplosionCatalog.BindFrameCounts"/> is. Until it is, nothing burns.
	/// </summary>
	public void BindFireShapeFrames(IReadOnlyList<int> framesPerShape) {
		_fireShapeFrames = framesPerShape.ToArray();
	}

	/// <summary>The loaded zone's terrain. One zone is active at a time, as in the original.</summary>
	public HeightGrid Terrain { get; }

	/// <summary>The travelling-projectile table, or null when the resource was not loaded.</summary>
	public BulletCatalog? Bullets { get; }

	/// <summary>The impact-effect table, or null when the resource was not loaded.</summary>
	public ExplosionCatalog? Explosions { get; }

	/// <summary>The launcher-round table, or null when the resource was not loaded.</summary>
	public RocketCatalog? Rockets { get; }

	/// <summary>The beam appearance table, or null when the resource was not loaded.</summary>
	public BeamAppearance? BeamTable { get; }

	/// <summary>
	/// The simulation's pseudo-random generator — DBSIM's single global state block at
	/// <c>0x4d261d</c>, which every roll in the simulation shares. Weapon scatter is the first thing
	/// in the engine to draw on it during a tick.
	///
	/// <para>The mission scene builds one instance and hands it to both the terrain pass and this
	/// world, because the zone load draws from the same generator before anything else does; two
	/// instances would produce the same stream twice rather than one continuing stream.</para>
	/// </summary>
	public SimRandom Random { get; }

	/// <summary>
	/// DBSIM's second generator, the state block at <c>0x4d268f</c> that sounds, message variants,
	/// the comm-box portraits and the cockpit hit shake draw on, so none of them moves
	/// <see cref="Random"/>. <see cref="SpawnImpactEffect"/> makes <c>Explosion_Construct</c>'s
	/// discarded draw on it, and the host hands it to the sound director, the squad comm channel
	/// (message variants, the scream's roll, the portrait paint's discarded draw), the cockpit hit
	/// shake and the sensor dropout. Seeded beside it to the same vanilla state — see
	/// docs/retail/simulation/random-generator.md#the-presentation-generator.
	/// </summary>
	public SimRandom PresentationRandom { get; } = new();

	/// <summary>
	/// <c>MissionDifficulty</c> (<c>004a9ee0</c>) — the mission difficulty, <c>0</c>-<c>3</c>, out of
	/// <see cref="World.ScriptDatHeader.Difficulty"/>. It is the player pilot's own skill in a
	/// campaign and the practice missions screen's setting outside one, so the shell picks it once and
	/// the simulator only reads it.
	///
	/// <para>Three things index it: <see cref="DamageScaleFor"/>, which every direct-fire shot and
	/// the plasma round's blast pass through, and <see cref="MechObject.AiAimScatter"/>. The fourth
	/// consumer in the original is <c>Mech_CollisionTest</c>'s slide-landing damage, which is not
	/// implemented at all. See docs/retail/simulation/difficulty.md.</para>
	/// </summary>
	public int Difficulty { get; set; }

	/// <summary>
	/// <c>UnlimitedAmmoFlag</c> (<c>004a9edc</c>) — the mission's <b>unlimited ammunition and energy</b> flag, out of
	/// <see cref="World.ScriptDatHeader.UnlimitedAmmunition"/>. It reaches two places, both of them
	/// the locally piloted machine's mounts: <see cref="WeaponMounts.FireTick"/> passes it to the
	/// shot as its free-shot flag, and <see cref="WeaponMounts.ChargeTick"/> refunds the whole tick's
	/// draw to the Master Energy Pool. See docs/retail/simulation/difficulty.md.
	/// </summary>
	public bool UnlimitedAmmunition { get; set; }

	/// <summary>
	/// <c>PlayerInvulnerableFlag</c> (<c>004a9ede</c>) — the mission's <b>player invulnerability</b> flag, out of
	/// <see cref="World.ScriptDatHeader.PlayerInvulnerable"/>, and the whole of
	/// <c>Sim_DamageToPlayerDisabled</c> (<c>004240f4</c>) once the gate that function shares with
	/// the difficulty is accounted for. Read where the damage write starts.
	/// </summary>
	public bool PlayerInvulnerable { get; set; }

	/// <summary>
	/// <c>Damage_ScaleByDifficulty</c> (<c>00426b04</c>) — the Q10 factor a shot fired by
	/// <paramref name="side"/> has its damage multiplied by at this difficulty.
	///
	/// <para><b>The two sides move in opposite directions.</b> A human shot is scaled <i>up</i> at
	/// every level and a Cybrid one <i>down</i> at every level, and each step closes the gap: at
	/// <c>ROOKIE</c> the player hits for 3.42x and the enemy for 0.29x, at <c>ELITE</c> for 1.37x and
	/// 0.98x. Side is the <i>firing</i> side, and it is the group's — so a squadmate's shots scale
	/// like the player's.</para>
	///
	/// <para>Callers pass <see cref="SimObject.Side"/> where the original reads
	/// <c>owner-&gt;group-&gt;side</c>. The side is copied onto the object at spawn and nothing changes
	/// it mid-mission, so it is the same byte without the unguarded dereference.</para>
	/// </summary>
	public int DamageScaleFor(World.MissionSide side) =>
		(side == World.MissionSide.Human ? DamageScaleHuman : DamageScaleCybrid)[Difficulty];

	/// <summary><c>DamageScaleBySide0Difficulty</c> (<c>0049a73c</c>) — <see cref="DamageScaleFor"/>'s table for a shot fired by side 0.</summary>
	private static readonly int[] DamageScaleHuman = { 3500, 2800, 2100, 1400 };

	/// <summary><c>DamageScaleByCybridDifficulty</c> (<c>0049a744</c>) — the same, for a shot fired by any other side.</summary>
	private static readonly int[] DamageScaleCybrid = { 300, 600, 800, 1000 };

	/// <summary>
	/// Where the simulation's noises go, or null to run silent — which is what a headless tick, a
	/// test, and a machine with no audio device all do.
	///
	/// <para>The world only announces events; every rule about audibility, volume and stereo
	/// placement lives in the sink. See <see cref="ISoundSink"/>.</para>
	/// </summary>
	public ISoundSink? Sounds { get; set; }

	/// <summary>
	/// The tweak settings the simulation's non-retail switches read. A world given none reads every
	/// setting at its default.
	/// </summary>
	public TweakSettings Tweaks { get; set; } = new();

	/// <summary>
	/// Where the camera is, in world units — the original's own view object (<c>ViewObjectPtr</c> (<c>004d256e</c>)),
	/// which simulation code legitimately reads.
	///
	/// <para>Two ported sites need it and both are audio range gates: a footfall is only played for a
	/// machine within <see cref="MechObject.FootfallAudibleRange"/> of it, and a launcher round
	/// announces itself when it comes inside <see cref="Rocket.InboundWarningRange"/>. Neither
	/// is a rendering concern — the original makes both tests inside the simulation, before it calls
	/// the sound layer at all.</para>
	/// </summary>
	public Vec3i ListenerPosition { get; set; }

	/// <summary>
	/// The EFFECTS DETAIL preference, 0-2 — <c>prefs.cfg</c> byte 11, which the original names
	/// <c>Sound_DetailSetting</c> (<c>004d1fc7</c>) and reads straight out of the option array while
	/// the simulation runs. Two simulation sites read it: <see cref="BaseObject.SmokesAtStage"/> and a
	/// debris piece's burst (<see cref="DebrisObject"/>). The host copies the byte in every frame;
	/// 2, the fullest, until it does.
	/// </summary>
	public int EffectsDetail { get; set; } = 2;

	/// <summary>
	/// The theater, 0-4 — <see cref="World.ScriptDatHeader.TheaterIndex"/>, which the original tests
	/// straight out of its copy of the header (<c>ScriptDatHeader</c>). The simulation asks it one
	/// question, whether this is <see cref="MoonTheater"/>, and asks it twice: in a debris piece's
	/// gravity (<see cref="DebrisObject"/>) and in which fires light and for how long
	/// (<see cref="SpawnFire"/>). See docs/retail/simulation/destruction-effects.md.
	/// </summary>
	public int Theater { get; set; }

	/// <summary>Theater 4, the Moon — <c>WORLD8</c>/<c>WORLD9</c>.</summary>
	public const int MoonTheater = 4;

	/// <summary>Whether <see cref="Theater"/> is <see cref="MoonTheater"/>.</summary>
	internal bool OnMoon => Theater == MoonTheater;

	/// <summary>
	/// The bias every sound id stored in a data table carries — <c>BULLETS.DAT</c>'s fire sound,
	/// <c>ROCKETS.DAT</c>'s and an <c>EXPLOS.DAT</c> row's. Those tables index the effects half of
	/// the catalog from zero, so the id they store is <see cref="SoundId.FirstEffect"/> short of a
	/// real one; every spawn site in the original adds it back.
	/// </summary>
	internal void PlayTableSound(short storedId, Vec3i position) {
		// A negative id is the table's own "silent" — the impact rows use it, and nothing bounds the
		// addition in the original, so it would index below the catalog.
		if (storedId >= 0) {
			Sounds?.PlayAt(storedId + SoundId.FirstEffect, position);
		}
	}

	/// <summary>Live simulation objects, including any flagged <see cref="SimObject.Removed"/>.</summary>
	public IReadOnlyList<SimObject> Objects => _objects;

	/// <summary>
	/// The mission's groups, in block-11 order. The AI is driven from here rather than from
	/// <see cref="Objects"/> — see <see cref="MissionGroup"/>.
	/// </summary>
	public IReadOnlyList<MissionGroup> Groups => _groups;

	/// <summary>
	/// <c>PlayerMech</c>, DBSIM's own global for the machine the player is flying. Several AI
	/// decisions ask whether the player is the attacker, the group leader, or the holder of a target,
	/// and each of them reads this.
	/// </summary>
	public MechObject? PlayerMech { get; set; }

	/// <summary>
	/// <c>DAT_004a9c0c</c>, count <c>DAT_004a9d4c</c> — the player's <b>line of fire</b>: the points
	/// <see cref="MechObject.FireTick"/> stamps along his turret bearing on a shot. A global in the
	/// original because only one machine is ever the player's.
	///
	/// <para><b>Nothing draws it.</b> Its one reader is <see cref="MechObject.ObstacleAvoidance"/>,
	/// which steers a machine in the player's own squad out of the way — so the list exists only
	/// while the trigger is actually producing shots.</para>
	/// </summary>
	public IReadOnlyList<Vec3i> PlayerFiringLine => _playerFiringLine;

	/// <summary>Lays the line out from the player's position along a per-point step.</summary>
	internal void SetPlayerFiringLine(Vec3i from, int stepX, int stepY, int count) {
		_playerFiringLine.Clear();

		int x = from.X;
		int y = from.Y;

		for (int i = 0; i < count; i++) {
			x += stepX;
			y += stepY;
			_playerFiringLine.Add(new Vec3i(x, y, from.Z));
		}
	}

	/// <summary>Drops the line, which the original does by zeroing its count on a tick with no shot.</summary>
	internal void ClearPlayerFiringLine() => _playerFiringLine.Clear();

	private readonly List<Vec3i> _playerFiringLine = new();

	/// <summary>Registers a group with the world so its members' AI ticks.</summary>
	public void AddGroup(MissionGroup group) => _groups.Add(group);

	private readonly List<MissionGroup> _groups = new();

	/// <summary>
	/// <c>DAT_004a9eac</c>, count <c>DAT_004a9ea8</c> — the mission's block-5 actions, in file order.
	/// The trigger layer walks them once a frame; see <see cref="MissionTriggers"/>.
	/// </summary>
	public IReadOnlyList<MissionActionState> Actions => _actions;

	/// <summary>Installs the mission's action array. Done once, at load.</summary>
	public void SetActions(IReadOnlyList<MissionActionState> actions) {
		_actions.Clear();
		_actions.AddRange(actions);
	}

	private readonly List<MissionActionState> _actions = new();

	/// <summary>
	/// <c>ActionTimer_Array</c> (<c>004a9ebc</c>), count <c>ActionTimer_Count</c> (<c>004a9eb8</c>) — the mission's block-6 timers. See
	/// <see cref="MissionActionTimerState"/>.
	/// </summary>
	public IReadOnlyList<MissionActionTimerState> ActionTimers => _actionTimers;

	/// <summary>Installs the mission's action-timer array. Done once, at load.</summary>
	public void SetActionTimers(IReadOnlyList<MissionActionTimerState> timers) {
		_actionTimers.Clear();
		_actionTimers.AddRange(timers);
	}

	private readonly List<MissionActionTimerState> _actionTimers = new();

	/// <summary>
	/// The mission's objective layer — block 12's records and the status they add up to. Empty until
	/// a scene installs one, which is what a headless test or a mission with no objectives leaves it.
	/// See <see cref="MissionObjectives"/>.
	/// </summary>
	public MissionObjectives Objectives { get; private set; } = MissionObjectives.Empty;

	/// <summary>Installs the mission's objective layer. Done once, at load.</summary>
	public void SetObjectives(MissionObjectives objectives) => Objectives = objectives;

	/// <summary>
	/// The mission's <see cref="World.MissionBox"/>. Two things read it: the Heads-Down Display's map
	/// frames itself on it, and the objective layer's two boundary statuses are the player leaving it.
	/// An empty box turns both off.
	/// </summary>
	public MissionBox MissionBox { get; set; } =
		new(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

	/// <summary>
	/// The status the tick last handed up for the status alert, not yet shown: the objective poll's,
	/// or <see cref="MissionStatus.PlayerDestroyed"/> once the player-death camera has run its course.
	/// The original raises the modal panel inside the tick; the host raises it from this latch.
	/// </summary>
	public MissionStatus PendingMissionAlert { get; set; } = MissionStatus.None;

	/// <summary>
	/// <c>DAT_004a9ef4</c> — the mission counters, which are the campaign's flag array for the length
	/// of a mission: they start from <c>mission.var</c> (<see cref="LoadMissionCounters"/>) and
	/// <c>Mission_WriteResults</c> (<c>0042412c</c>) writes them back to it as the mission ends (<see cref="MissionResults"/>). See
	/// docs/retail/simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4.
	///
	/// <para>Written by an action firing (<see cref="MissionActionState.Activate"/>), an objective
	/// (<see cref="MissionObjectiveState"/>), the player downing a squadmate
	/// (<see cref="MechObject.CreditNeutralised"/>), and an object or a whole group going out of the
	/// fight (<see cref="ApplyOutOfActionReport"/>).</para>
	/// </summary>
	public IReadOnlyList<short> MissionCounters => _missionCounters;

	/// <summary>
	/// Starts the counters from <see cref="World.Mission.Counters"/>. Done once, at load; a shorter
	/// list leaves the rest at zero.
	/// </summary>
	public void LoadMissionCounters(IReadOnlyList<short> counters) {
		Array.Clear(_missionCounters);
		for (int i = 0; i < counters.Count && i < MissionCounterSlots; i++) {
			_missionCounters[i] = counters[i];
		}
	}

	/// <summary>Adds to one counter. Refs outside the array are dropped rather than throwing.</summary>
	internal void BumpMissionCounter(int index, short amount) {
		if ((uint)index < MissionCounterSlots) {
			_missionCounters[index] = (short)(_missionCounters[index] + amount);
		}
	}

	/// <summary>Writes one counter outright — the objective layer's operation 4, which sets it to 1.</summary>
	internal void SetMissionCounter(int index, short value) {
		if ((uint)index < MissionCounterSlots) {
			_missionCounters[index] = value;
		}
	}

	/// <summary>Zeroes one counter.</summary>
	internal void ClearMissionCounter(int index) {
		if ((uint)index < MissionCounterSlots) {
			_missionCounters[index] = 0;
		}
	}

	/// <summary>
	/// Runs one object's or group's out-of-action writes — the slot walk
	/// <c>Mech_ReportOutOfAction</c> (<c>00411bc8</c>) and <c>Group_ReportIfAllOutOfAction</c>
	/// (<c>00423f30</c>) both spell out inline. A slot with a negative ref is skipped, and an
	/// operation outside the set writes nothing.
	/// </summary>
	internal void ApplyOutOfActionReport(World.OutOfActionReport report) {
		for (int slot = 0; slot < World.OutOfActionReport.Slots && slot < report.CounterRefs.Count; slot++) {
			short counter = report.CounterRefs[slot];
			if (counter < 0 || slot >= report.CounterOps.Count) {
				continue;
			}

			short op = report.CounterOps[slot];
			if (op == World.OutOfActionReport.OpClear) {
				ClearMissionCounter(counter);
			} else if (op == World.OutOfActionReport.OpIncrement) {
				BumpMissionCounter(counter, 1);
			} else if (op >= World.OutOfActionReport.OpSetFirst && op <= World.OutOfActionReport.OpSetLast) {
				SetMissionCounter(counter, (short)(op - World.OutOfActionReport.SetBias));
			}
		}
	}

	/// <summary>
	/// How many counters the array holds — 1000: <c>mission.var</c> is 2,000 bytes each way.
	/// </summary>
	public const int MissionCounterSlots = 1000;

	private readonly short[] _missionCounters = new short[MissionCounterSlots];

	/// <summary>
	/// <c>DAT_004a9ef0</c>/<c>DAT_004a9eee</c> — the weapons the mission has recovered, each a catalog id and a
	/// percentage condition, which <see cref="MissionResults"/> hands the shell as <c>results.dat</c>'s salvage
	/// pairs. <c>DBSim_LoadScriptDat</c> empties it at load; <see cref="QueueSalvage"/> is its only writer.
	/// </summary>
	public IReadOnlyList<(short WeaponId, short Condition)> Salvage => _salvage;

	/// <summary>
	/// <c>Salvage_QueueWeapon</c> (<c>00426ac8</c>) — one weapon onto <see cref="Salvage"/>. Two callers: a
	/// Cybrid mount the destruction roll knocks off (<c>MechObject.RollWeaponMountDestruction</c>), and
	/// each surviving mount of an enemy wreck at the mission's end (<see cref="MechObject.SalvageValue"/>).
	///
	/// <para>The original's list is a fixed block the append never checks, and a long enough list
	/// writes past it (docs/retail/simulation/component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60).
	/// This list grows instead.</para>
	/// </summary>
	internal void QueueSalvage(short weaponId, short condition) => _salvage.Add((weaponId, condition));

	private readonly List<(short WeaponId, short Condition)> _salvage = new();

	/// <summary>
	/// The drop pods in the air — <c>g_MeteorPool</c>. See <see cref="MeteorObject"/>; a mission
	/// group launches one from <see cref="MissionGroup.DeploymentCheck"/>.
	/// </summary>
	public IReadOnlyList<MeteorObject> DropPods => _meteors;

	/// <summary>
	/// <c>Group_DeploymentCheck</c>'s pod branch — takes a pod off the pool and puts it in the air
	/// over <paramref name="target"/>. A full pool delivers nothing, which is the original's own
	/// answer to an exhausted pool: it checks the allocation and quietly does without.
	/// </summary>
	internal void LaunchDropPod(Vec3i target, MissionGroup group) {
		if (_meteors.Count >= MeteorObject.PoolSize) {
			return;
		}

		_meteors.Add(new MeteorObject(target, group, Random));
	}

	/// <summary>
	/// How many cells the pod's opening shape has, which is what ends its animation — supplied by
	/// whoever loaded the shapes, for the reason <see cref="BindFireShapeFrames"/> is. Until it is,
	/// a pod opens on the tick after it lands.
	/// </summary>
	public void BindDropPodFrameCount(int frames) => _dropPodFrames = frames;

	private int _dropPodFrames;
	private readonly List<MeteorObject> _meteors = new();

	/// <summary>
	/// The flat shapes lying on the ground — <c>g_FlatObjPool</c> (<c>004a9711</c>). See
	/// <see cref="GroundShape"/>: a HERC's, an impact effect's and a landed drop pod's, all out of the
	/// one pool.
	/// </summary>
	public IReadOnlyList<GroundShape> GroundShapes => _groundShapes;

	/// <summary>
	/// <c>Pool_Alloc(g_FlatObjPool)</c> and the <c>FlatObj</c> construction every spawner inlines after
	/// it: root <paramref name="shapeIndex"/> of the flat set at <paramref name="position"/>, heading 0.
	///
	/// <para>A full pool gives null. <b>None of the three spawners checks for that</b>: each goes on
	/// to write the position (or, in the HERC's case, the Z) through the pointer it got, which is a
	/// crash in the original (KNOWN_ISSUES.md). Here each caller simply does without the shape.</para>
	/// </summary>
	internal GroundShape? SpawnGroundShape(int shapeIndex, Vec3i position) {
		if (_groundShapes.Count >= GroundShape.PoolSize) {
			return null;
		}

		int radius = shapeIndex >= 0 && shapeIndex < _groundShapeRadii.Count ? _groundShapeRadii[shapeIndex] : 0;
		var shape = new GroundShape(shapeIndex, radius, position);
		_groundShapes.Add(shape);
		return shape;
	}

	/// <summary>
	/// <c>ObjectPool_QueueForDelete(g_FlatObjDeleteQueue, shape)</c>. The original frees the entry at
	/// the next <c>Sim_FlushDeleteQueue</c> (<c>00409904</c>) — the start of the next frame's draw, or
	/// the end of the damage write that queued it — so the shape is never drawn again either way.
	/// </summary>
	internal void ReleaseGroundShape(GroundShape? shape) {
		if (shape != null) {
			_groundShapes.Remove(shape);
		}
	}

	/// <summary>
	/// Each root of the flat set's bounding radius and cell count, supplied by whoever loaded the
	/// shapes, for the reason <see cref="BindDebrisShapeRadii"/> is. Until it is, a shape conforms at
	/// radius 0 and an impact effect's steps no cell.
	/// </summary>
	public void BindGroundShapes(IReadOnlyList<int> radii, IReadOnlyList<int> frameCounts) {
		_groundShapeRadii = radii;
		_groundShapeFrames = frameCounts;
	}

	/// <summary>How many cells root <paramref name="shapeIndex"/>'s sequence 0 has, or 0 unbound.</summary>
	internal int GroundShapeFrameCount(int shapeIndex) =>
		shapeIndex >= 0 && shapeIndex < _groundShapeFrames.Count ? _groundShapeFrames[shapeIndex] : 0;

	private IReadOnlyList<int> _groundShapeRadii = Array.Empty<int>();
	private IReadOnlyList<int> _groundShapeFrames = Array.Empty<int>();
	private readonly List<GroundShape> _groundShapes = new();

	/// <summary>Ticks elapsed since the world was created.</summary>
	public long TickCount { get; private set; }

	/// <summary>
	/// How much time the ticks so far stand for, in milliseconds: 40 per tick on the engine's own
	/// fixed timestep, or whatever a replayed tape's frames recorded.
	/// </summary>
	public double ElapsedMilliseconds { get; private set; }

	/// <summary>
	/// <c>Time_GetCoarseTicks</c>' timebase — <c>GetTickCount() &gt;&gt; 4</c>, so 16 ms units.
	///
	/// <para>This is the original's UI and event clock and is deliberately <b>not</b> the simulation
	/// timestep; gadgets that blink or repeat on a cadence count in these. Derived from
	/// <see cref="ElapsedMilliseconds"/> rather than from the wall clock so it stays in step with a
	/// simulation that is paused, stepped, or replayed.</para>
	/// </summary>
	public long CoarseTicks => (long)(ElapsedMilliseconds / 16);

	/// <summary>
	/// Simulation rate — <b>the original's own</b>. DBSIM's frame loop (<c>Time_BeginSimTick</c>, <c>004677bc</c>) spins on
	/// <c>GetTickCount</c> until 40 ms have passed, so the sim runs at a 25 Hz cap and its timestep is
	/// however long the frame actually took.
	///
	/// <para>The engine's sim is a fixed timestep decoupled from rendering, so unlike the original
	/// its behaviour does not vary with how fast frames are drawn.</para>
	///
	/// <para>One deliberate deviation goes with that, described in full on
	/// <see cref="SimMath.ScalePerTickStep"/>: the raw per-tick accel steps are routed through it, so
	/// changing this constant no longer silently rescales acceleration.</para>
	/// </summary>
	public const int TicksPerSecond = 25;

	/// <summary>
	/// The value written into <see cref="SimMath.TickDelta"/> for each tick.
	///
	/// <para>DBSIM computes it as <c>clamp((elapsedMs &lt;&lt; 8) / 125, 0x40, 0x1c2)</c>, so the Q8
	/// unit 0x100 is <b>125 ms</b> and every rate in the sim is per-125 ms. At the 40 ms frame cap
	/// that is <c>40 * 256 / 125 = 81</c>. The engine's fixed timestep pins it there, which is what
	/// the original produces on any machine fast enough to hit its own cap — so everything
	/// integrating through <see cref="SimMath.IntegrateRateOverTick"/> runs at the original's
	/// rate.</para>
	/// </summary>
	public const short TickDelta = SimMath.VanillaTickDelta;

	/// <summary>
	/// Every beam resolved during the tick just completed, in the order they were fired. Cleared at
	/// the top of each <see cref="Tick"/>.
	///
	/// <para><b>Not part of the original.</b> DBSIM resolves a beam and immediately spawns its tracer
	/// segments from inside <c>Bullet_FireBurst</c>, so the shot never outlives the call. Visuals are
	/// deliberately the last piece of this milestone, and until they exist this is what lets anything
	/// outside the simulation see that a shot happened at all.</para>
	/// </summary>
	public IReadOnlyList<WeaponShot> Beams => _beams;

	/// <summary>
	/// The live beam tracers — the original's <c>g_ProjectilePool</c> (<c>004a9746</c>) pool. Unlike <see cref="Beams"/>
	/// these outlive the tick that made them (by exactly one tick, see
	/// <see cref="BeamTracer.InitialLife"/>) and are what a renderer draws.
	/// </summary>
	public IReadOnlyList<BeamTracer> Tracers => _tracers;

	/// <summary>
	/// The travelling shots in flight — the same <c>g_ProjectilePool</c> (<c>004a9746</c>) pool <see cref="Tracers"/> comes
	/// from. Unlike a tracer these live for as long as their <c>BULLETS.DAT</c> lifetime or until
	/// they hit something, and they move and do damage while they do.
	/// </summary>
	public IReadOnlyList<Projectile> Projectiles => _projectiles;

	/// <summary>
	/// The launcher rounds in flight — the same pool again, and the same deal: a rocket that leaves
	/// the rail this tick does not move or hit anything until the next one. They are kept apart from
	/// <see cref="Projectiles"/> because they are a different class with a different tick, exactly as
	/// they are in the original.
	/// </summary>
	public IReadOnlyList<Rocket> RocketsInFlight => _rockets;

	/// <summary>
	/// <c>DAT_004d25aa</c> — an electro-optical missile is being flown. <see cref="Tick"/> clears it
	/// just before it walks the rounds, and a player-flown round raises it again every tick it is
	/// steered (<see cref="Rocket"/>); <see cref="WeaponMounts.FireTick"/> raises it on the tick that
	/// launches one. While it is up the controls go to the round rather than to the machine, and the
	/// weapon chain holds still. See docs/retail/simulation/rockets.md#the-missile-camera.
	/// </summary>
	public bool MissileFlown { get; internal set; }

	/// <summary>
	/// The two axes and the trigger <c>Rocket_PlayerSteer</c> reads out of the player input block —
	/// what the host built from the controls this frame. It is read, never consumed: the original
	/// rebuilds the block every tick, so zeroing it would cost a second tick in the same host frame its
	/// input. What the steer's own clearing of the trigger does is <see cref="PlayerTriggerCleared"/>.
	/// </summary>
	public MissileSteerInput MissileSteer { get; set; }

	/// <summary>
	/// The trigger byte <c>DAT_004d2357</c> cleared part-way through a tick — by the steer, and when a
	/// flown round ends — so the machine's own fire path, which reads it later in the same tick, sees it
	/// released. Reset at the top of every tick, as the original rebuilds the byte.
	/// </summary>
	internal bool PlayerTriggerCleared { get; set; }

	/// <summary>
	/// <c>DAT_0049c394</c> — the last round the locally piloted machine launched, whatever its
	/// subtype, which the MFD's missile camera rides. <see cref="FireRocket"/> sets it; the camera
	/// screen clears it the first time it paints and finds the round gone from
	/// <see cref="RocketsInFlight"/>.
	/// </summary>
	public Rocket? PlayerMissile { get; set; }

	/// <summary>
	/// <c>DAT_0049c398</c> — <see cref="PlayerMissile"/> ended before its lifetime ran out, which is to
	/// say it struck something. The camera screen flashes on it and clears it.
	/// </summary>
	public bool PlayerMissileStruck { get; set; }

	/// <summary>
	/// The <c>Input_LatchButton(1, 1)</c> a flown round makes as it ends: the input layer's press-once
	/// latch on the first button row, which is the host's to hold. Read and cleared by
	/// <see cref="TakeFireRowLatch"/>.
	/// </summary>
	private bool _fireRowLatchRequested;

	/// <summary>
	/// Whether a flown round asked for the first button row to be latched since the last call — see
	/// docs/retail/simulation/rockets.md#flight--rocket_tickupdate-0040a538.
	/// </summary>
	public bool TakeFireRowLatch() {
		bool requested = _fireRowLatchRequested;
		_fireRowLatchRequested = false;
		return requested;
	}

	/// <summary>A flown round has ended: the trigger byte is cleared and row 0 latched.</summary>
	internal void EndFlownRound() {
		PlayerTriggerCleared = true;
		_fireRowLatchRequested = true;
	}

	/// <summary>
	/// Every travelling shot that struck something during the tick just completed, as the shot record
	/// the raycast left behind. Cleared at the top of each <see cref="Tick"/>, exactly as
	/// <see cref="Beams"/> is, and not part of the original for the same reason.
	/// </summary>
	public IReadOnlyList<WeaponShot> Impacts => _impacts;

	/// <summary>
	/// The impact effects playing right now — the original's <c>g_ExplosionPool</c> (<c>004a96a2</c>), a pool
	/// of its own that <c>Sim_MainTick</c> walks in the same effect-pool pass as the one
	/// <see cref="Tracers"/> and <see cref="Projectiles"/> come from. An
	/// entry lives for exactly one pass of its shape's flipbook; see <see cref="ImpactEffect"/>.
	///
	/// <para>An effect that plays out during a tick stays in this list, holding its slot against every
	/// later spawn in the same tick, until the render-time flush at the end of the tick removes it —
	/// <c>ExplosionPool_FlushDeletes</c> (<c>00407b3c</c>), a phase-5 hook of the <c>Sim_RenderFrame</c> the original
	/// runs after each <c>Sim_MainTick</c>. So between ticks every entry here is live.</para>
	/// </summary>
	public IReadOnlyList<ImpactEffect> Effects => _effects;

	/// <summary>
	/// The dynamic lights impact effects are currently casting — the effect light manager
	/// <c>DAT_004a968c</c>. The renderer reads it to decide what each drawn object is lit by; see
	/// <see cref="EffectLightField"/> and docs/retail/formats/effect-lights.md.
	/// </summary>
	public EffectLightField EffectLights { get; } = new();

	/// <summary>
	/// <c>Explosion_Construct</c> (<c>00407f1c</c>) — puts one impact effect at <paramref name="position"/>. Called from the
	/// two places the original calls it from along this path: from inside an object's hit test, where
	/// the effect belongs to the object struck (and is spawned whether or not the sweep goes on to
	/// find something nearer), and from the tail of <see cref="Raycast"/> itself for a shot that ends
	/// on the ground.
	///
	/// <para>Every site allocates from the pool before it builds, and with <see cref="ImpactEffect.PoolSize"/>
	/// slots taken the spawn does nothing at all: no effect, no light, no sound.</para>
	///
	/// <para>Silently does nothing when the table did not load or the id is outside it. A retail
	/// <c>ImpactFX</c> array can hold an id no type row exists for, and the original bounds nothing
	/// here — reading past the table is not a behaviour worth reproducing.</para>
	/// </summary>
	/// <param name="typeId">The <c>EXPLOS.DAT</c> type, out of a <c>PROJ.DAT</c> <c>ImpactFX</c> array.</param>
	/// <param name="position">Where the shot landed, in world units.</param>
	/// <param name="owner">
	/// <c>Explosion_Construct</c>'s owner argument, the object the effect belongs to for drawing — see
	/// <see cref="ImpactEffect.Owner"/>.
	/// </param>
	/// <param name="playSound">
	/// The constructor's own last argument, which gates the row's <c>SoundId</c> and nothing else.
	/// Every object-hit spawn passes true; the ground hit at the tail of <see cref="Raycast"/> is the
	/// one site that passes false.
	/// </param>
	internal void SpawnImpactEffect(short typeId, Vec3i position, SimObject? owner, bool playSound = true) {
		if (ImpactEffectPoolFull || Explosions?.Type(typeId) is not { } record) {
			return;
		}

		_effects.Add(new ImpactEffect(
			typeId, Explosions, record, Explosions.FrameCount(record.ShapeIndex), position, owner, EffectLights,
			this));

		if (playSound && record.SoundId >= 0) {
			// Math_RandomBelow(0x32) on the presentation generator, whose result the constructor throws
			// away before playing the row's own sound (004080ca).
			PresentationRandom.NextBelow(0x32);
			PlayTableSound(record.SoundId, position);
		}
	}

	/// <summary>
	/// One of the four ids an <c>ImpactFX</c> array holds, drawn the way every spawn site draws it —
	/// <c>Math_RandomNext(...) &amp; 3</c>, so all four are equally likely and the same array gives a
	/// different effect shot to shot.
	/// </summary>
	internal short PickImpactEffect(short[]? effects) =>
		effects is { Length: > 0 } ? effects[Random.NextMasked(3) % effects.Length] : (short)0;

	/// <summary>
	/// <see cref="SpawnImpactEffect"/> with the id drawn from <paramref name="effects"/> by
	/// <see cref="PickImpactEffect"/> only once a pool slot is known to be free. This is the order of the
	/// ground hit and of the mech and structure hit tests, whose <c>Math_RandomNext</c> sits inside the
	/// allocation's null test, so a full pool leaves the generator where it was. The flyer's hit test
	/// draws before it allocates and goes through <see cref="PickImpactEffect"/> itself.
	/// </summary>
	internal void SpawnPickedImpactEffect(short[]? effects, Vec3i position, SimObject? owner,
			bool playSound = true) {
		if (!ImpactEffectPoolFull) {
			SpawnImpactEffect(PickImpactEffect(effects), position, owner, playSound);
		}
	}

	/// <summary><c>Pool_Alloc</c> (<c>00471a24</c>) on <c>g_ExplosionPool</c> would return nothing.</summary>
	private bool ImpactEffectPoolFull => _effects.Count >= ImpactEffect.PoolSize;

	/// <summary>
	/// The debris databases — <c>DEF_DEB</c> and whatever else has been asked for. Null when the
	/// install has no <c>DEF_DEB</c>, in which case nothing throws anything.
	/// </summary>
	public DebrisCatalog? Debris { get; }

	/// <summary>
	/// Wreckage in the air, from <see cref="SpawnDebris"/>. Each piece lives until it comes to rest
	/// or bursts; see <see cref="DebrisObject"/>.
	/// </summary>
	public IReadOnlyList<DebrisObject> DebrisInFlight => _debris;

	/// <summary>
	/// Fires currently burning, from <see cref="SpawnFire"/> — at most
	/// <see cref="FireEffect.PoolSize"/> of them, as in the original.
	/// </summary>
	public IReadOnlyList<FireEffect> Fires => _fires;

	/// <summary>
	/// <c>Debris_ThrowGroup</c> — throws one debris group at <paramref name="frame"/>.
	///
	/// <para><b>Two ways of reading a group</b>, on its own throw count: zero throws every piece it
	/// holds, once each; anything else throws exactly that many pieces drawn from it at random,
	/// weighted by each piece's share of the group's total — so a five-throw group of six pieces can
	/// throw the same one twice and skip two others.</para>
	///
	/// <para>Each piece is placed by <c>Debris_Throw</c>: a piece that states an orientation yaw has
	/// it composed onto <paramref name="frame"/> and keeps the result as its own attitude, and one
	/// that states a throw yaw is launched along that bearing relative to it. A piece that states
	/// neither is thrown on a wholly random bearing from the frame's translation, which is what every
	/// <c>DEF_DEB</c> group does.</para>
	///
	/// <para>The launch itself is <c>Debris_Launch</c>: the pitch is drawn between
	/// <paramref name="pitchMin"/> and <paramref name="pitchMax"/>, and the speed is
	/// <paramref name="speedScale"/> shifted up ten over the piece's own mass, plus
	/// <see cref="DebrisCarrierVelocity"/>.</para>
	/// </summary>
	/// <param name="groupIndex">The group, in the two-database index space — see <see cref="DebrisCatalog"/>.</param>
	/// <param name="frame">Where and how it is thrown from.</param>
	/// <param name="installed">
	/// The alternate database this site has installed, which is the original's global at the moment
	/// of the call: a machine's own chassis table, <c>BASE_DEB</c>, or the table a bursting piece was
	/// itself thrown from.
	/// </param>
	/// <param name="pitchMin">Lowest launch pitch, BAM. 6000 (about 33 degrees) for a first throw.</param>
	/// <param name="pitchMax">Highest launch pitch, BAM. 16000 (about 88 degrees) for a first throw.</param>
	/// <param name="speedScale">
	/// What the launch speed is <c>&lt;&lt; 10</c> and divided by the piece's mass from. 420 for a
	/// first throw and 320 for a burst, so second-generation debris scatters closer.
	/// </param>
	internal void SpawnDebris(short groupIndex, in Transform3 frame, DebrisDatabase? installed,
			short pitchMin = ThrowPitchMin, short pitchMax = ThrowPitchMax,
			int speedScale = ThrowSpeedScale) {
		if (Debris?.Resolve(groupIndex, installed) is not { } resolved) {
			return;
		}

		var (database, group) = resolved;

		if (group.ThrowCount == 0) {
			foreach (var piece in group.Pieces) {
				Throw(database, piece, frame, pitchMin, pitchMax, speedScale);
			}

			return;
		}

		for (int thrown = 0; thrown < group.ThrowCount; thrown++) {
			// The weighted walk: a draw under the group's total, then each piece's weight subtracted
			// off it in file order until one covers what is left. The original's loop is unguarded and
			// walks off the end if the total does not match; stopping at the last piece is this
			// engine's, and no retail group can reach it.
			int draw = Random.NextBelow((short)group.TotalWeight);
			int index = 0;
			while (index < group.Pieces.Count - 1 && draw > group.Pieces[index].Weight) {
				draw -= group.Pieces[index].Weight;
				index++;
			}

			Throw(database, group.Pieces[index], frame, pitchMin, pitchMax, speedScale);
		}
	}

	/// <inheritdoc cref="SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)" />
	/// <remarks>
	/// <c>Debris_ThrowGroupAt</c> — the same throw from a point rather than a frame, which is how every site
	/// but a machine's own component destruction spawns: it builds an identity rotation, drops the
	/// point in, and hands that over. So a piece thrown this way keeps whatever attitude its record
	/// states and nothing else.
	/// </remarks>
	internal void SpawnDebris(short groupIndex, Vec3i position, DebrisDatabase? installed,
			short pitchMin = ThrowPitchMin, short pitchMax = ThrowPitchMax,
			int speedScale = ThrowSpeedScale) {
		var frame = Transform3.Identity;
		frame.X = position.X;
		frame.Y = position.Y;
		frame.Z = position.Z;

		SpawnDebris(groupIndex, frame, installed, pitchMin, pitchMax, speedScale);
	}

	/// <summary>
	/// <c>Debris_Throw</c> and <c>Debris_Launch</c> — places and launches one piece. Silently does
	/// nothing once <see cref="DebrisObject.PoolSize"/> pieces are already in the air, which is what
	/// the original's allocator returning null amounts to.
	/// </summary>
	private void Throw(DebrisDatabase database, DebrisPiece piece, in Transform3 frame,
			short pitchMin, short pitchMax, int speedScale) {
		if (_debris.Count >= DebrisObject.PoolSize) {
			return;
		}

		(short X, short Y, short Z) euler = default;

		// A piece with either angle stated has its own yaw composed onto the spawn frame, and the
		// composed attitude read back out as Euler angles. The original tests the pair together for
		// the compose and each separately for what it does with the answer, which is why a piece
		// stating only a throw yaw still does the matrix work and then keeps none of the attitude.
		// The <b>position is the spawn frame's own</b> either way: the composed transform is a
		// temporary the original never places anything at.
		if (piece.OrientationYaw != DebrisDatabase.NoAngle || piece.ThrowYaw != DebrisDatabase.NoAngle) {
			var yaw = Transform3.FromEuler(0, 0, (short)piece.OrientationYaw);
			euler = Transform3.Concat(yaw, frame).ToEuler();
		}

		short bearing = piece.ThrowYaw == DebrisDatabase.NoAngle
			? Random.Next()
			: (short)(euler.Z + piece.ThrowYaw - piece.OrientationYaw);

		short pitch = (short)(Random.NextBelow((short)(pitchMax - pitchMin)) + pitchMin);
		var velocity = LaunchVelocity(bearing, pitch, piece.Mass, speedScale);

		string library = database.Name + ShapeLibrarySuffix;

		_debris.Add(new DebrisObject(
			library, piece.ShapeIndex, DebrisShapeRadius(library, piece.ShapeIndex),
			piece.ChildGroup, piece.DestroyEffect, database,
			new Vec3i(frame.X, frame.Y, frame.Z),
			piece.OrientationYaw == DebrisDatabase.NoAngle ? default : euler,
			velocity, DrawSpinRate(), DrawBurstDelay()));
	}

	/// <summary>
	/// Puts one already-built piece of wreckage into the world on the launch the throw uses —
	/// <c>Debris_Launch</c> reached directly rather than through a group. The one caller is
	/// <see cref="WeaponMount.Destroy"/>, which throws the gun's own model off the hardpoint rather
	/// than anything a debris table names.
	/// </summary>
	/// <param name="shapeLibrary">The shape file the gun's model is a root of.</param>
	/// <param name="shapeIndex">Which root.</param>
	/// <param name="shapeRadius">Its bounding radius.</param>
	/// <param name="position">The muzzle point it is thrown from.</param>
	/// <param name="euler">The attitude it keeps — the muzzle frame's own.</param>
	/// <param name="bearing">The bearing it is thrown along.</param>
	/// <param name="pitch">The pitch it is thrown at — a fixed figure here, not a draw.</param>
	/// <param name="mass">What divides the speed.</param>
	/// <param name="childGroup">The group it bursts into, or <c>-1</c>.</param>
	/// <param name="destroyEffect">The effect that goes off there, or <c>-1</c>.</param>
	/// <param name="childTable">The database <paramref name="childGroup"/> is read against.</param>
	internal void SpawnDebrisPiece(string shapeLibrary, int shapeIndex, int shapeRadius,
			Vec3i position, (short X, short Y, short Z) euler, short bearing, short pitch, short mass,
			short childGroup, short destroyEffect, DebrisDatabase? childTable) {
		if (_debris.Count >= DebrisObject.PoolSize) {
			return;
		}

		_debris.Add(new DebrisObject(shapeLibrary, shapeIndex, shapeRadius, childGroup, destroyEffect,
			childTable, position, euler, LaunchVelocity(bearing, pitch, mass, ThrowSpeedScale),
			DrawSpinRate(), DrawBurstDelay(), hercDetailBias: true));
	}

	/// <summary>
	/// <c>Debris_Launch</c>'s velocity — the speed is <paramref name="speedScale"/> shifted up ten and
	/// divided by the piece's mass, split into a horizontal part by the pitch and then onto the two
	/// horizontal axes by the bearing.
	///
	/// <para>The launch velocity has <see cref="DebrisCarrierVelocity"/> added to it, so a shot-down
	/// aircraft's wreckage keeps flying rather than dropping out of the sky where the aircraft
	/// was.</para>
	/// </summary>
	private (short X, short Y, short Z) LaunchVelocity(short bearing, short pitch, short mass,
			int speedScale) {
		int speed = mass != 0 ? (speedScale << 10) / mass : 0;
		int horizontal = SimMath.Q14Multiply(speed, SimTrig.Cos(pitch));
		var carrier = DebrisCarrierVelocity;

		return (
			(short)(SimMath.Q14Multiply(horizontal, SimTrig.Cos((short)(bearing + BinaryAngle.QuarterTurn))) + carrier.X),
			(short)(SimMath.Q14Multiply(horizontal, SimTrig.Cos(bearing)) + carrier.Y),
			(short)(SimMath.Q14Multiply(speed, SimTrig.Cos((short)(pitch - BinaryAngle.QuarterTurn))) + carrier.Z));
	}

	/// <summary>
	/// <c>DAT_004a96e4</c> — a velocity every piece of wreckage thrown while it is set inherits.
	/// <c>Flyer_ComponentDamageWrite</c> (<c>00421bb4</c>) is the only writer: it points the global at
	/// the aircraft's own world velocity for the length of the call and clears it again afterwards,
	/// so the debris the component cascade sheds keeps the speed the aircraft was doing. The hit
	/// test's own throw happens after the clear and gets nothing.
	/// </summary>
	internal Vec3i DebrisCarrierVelocity;

	/// <summary>The tumble: always the same direction, between 800 and 2500 BAM a second.</summary>
	private short DrawSpinRate() =>
		(short)(SimMath.Q10Multiply(SpinRateSpread, Random.NextMasked(0x3ff)) + SpinRateBase);

	/// <summary>The burst countdown, drawn whether or not the piece has anything to burst into.</summary>
	private short DrawBurstDelay() => (short)(Random.NextBelow(BurstDelaySpread) + BurstDelayBase);

	/// <summary>What a debris database's shape file is called, given its name.</summary>
	public const string ShapeLibrarySuffix = ".DTS";

	private readonly Dictionary<string, int[]> _debrisShapeRadii = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Tells the world how large each root of one debris shape file is, which is what a thrown piece
	/// clears the ground by: <c>Debris_TickUpdate</c> settles a piece at the terrain height plus its
	/// shape's own bounding radius (vtable <c>+0x10</c>, the root's <c>shape+8</c>, which is
	/// <see cref="Scene.SceneModel.ShapeRadius"/>) scaled by <see cref="DebrisObject.GroundClearanceScale"/>.
	///
	/// <para>Supplied rather than read here for the reason
	/// <see cref="ExplosionCatalog.BindFrameCounts"/> is: the radius is a property of the shape file,
	/// which <see cref="Scene.MissionScene"/> builds. A library nothing has bound answers zero, and a
	/// piece out of it rests on the terrain surface.</para>
	/// </summary>
	public void BindDebrisShapeRadii(string shapeLibrary, IReadOnlyList<int> radiiPerShape) {
		_debrisShapeRadii[shapeLibrary] = radiiPerShape.ToArray();
	}

	/// <summary>The bounding radius of one debris shape, or zero when it is unknown.</summary>
	public int DebrisShapeRadius(string shapeLibrary, int shapeIndex) =>
		_debrisShapeRadii.TryGetValue(shapeLibrary, out var radii)
			&& shapeIndex >= 0 && shapeIndex < radii.Length
			? radii[shapeIndex]
			: 0;

	/// <inheritdoc cref="SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)" />
	public const short ThrowPitchMin = 6000;

	/// <inheritdoc cref="SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)" />
	public const short ThrowPitchMax = 16000;

	/// <inheritdoc cref="SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)" />
	public const int ThrowSpeedScale = 0x1a4;

	/// <inheritdoc cref="DebrisObject.SpinRate" />
	private const int SpinRateSpread = -0x6a4;

	/// <inheritdoc cref="DebrisObject.SpinRate" />
	private const short SpinRateBase = -800;

	/// <summary>The burst countdown's own draw — 2000 to 32000, so 1 to 16 seconds.</summary>
	private const short BurstDelaySpread = 30000;

	/// <inheritdoc cref="BurstDelaySpread" />
	private const short BurstDelayBase = 2000;

	/// <summary>
	/// <c>FireEffect_Ctor</c> (<c>0046b388</c>) — sets something alight.
	///
	/// <para><b>The pool is small and full is not a refusal.</b> With all
	/// <see cref="FireEffect.PoolSize"/> slots busy the original takes the fire with the fewest
	/// passes left and rebuilds it, so the newest fire always gets a slot and the one nearest going
	/// out is what pays. That is <c>FireEffect_AcquireSlot</c>, and it is reproduced here.</para>
	///
	/// <para>The original also starts the shared burning sound on the first live fire and stops it on
	/// the last, which <see cref="ReleaseFires"/> holds the other end of.</para>
	///
	/// <para><b>On the Moon only shapes 1 and 3 burn.</b> The constructor puts any other shape
	/// straight back on the free list — <i>after</i> the slot was taken, so with the pool full a
	/// filtered fire still evicts the weakest one and lights nothing in its place. A machine's own
	/// destruction lights shapes 0 and 2 only, so on the Moon a machine never burns, though the
	/// whole-object branch still puts out what was burning on it. The fires that do light play
	/// <see cref="FireEffect.MoonLoopCount"/> passes.</para>
	/// </summary>
	/// <param name="owner">What is burning.</param>
	/// <param name="componentIndex">Which of its components, or <c>-1</c> — see <see cref="FireEffect"/>.</param>
	/// <param name="localPoint">Where on it, for a fire with no component.</param>
	/// <param name="shapeIndex">Which <c>FIRE.DTS</c> root to burn.</param>
	internal void SpawnFire(SimObject owner, short componentIndex, Vec3i localPoint, int shapeIndex) {
		if (shapeIndex < 0 || shapeIndex >= _fireShapeFrames.Length) {
			return;
		}

		if (_fires.Count >= FireEffect.PoolSize) {
			int weakest = 0;
			for (int i = 1; i < _fires.Count; i++) {
				if (_fires[i].LoopsRemaining < _fires[weakest].LoopsRemaining) {
					weakest = i;
				}
			}

			_fires.RemoveAt(weakest);
		}

		if (OnMoon && shapeIndex != 1 && shapeIndex != 3) {
			return;
		}

		bool first = _fires.Count == 0;
		if (first) {
			Sounds?.Play(SoundId.BurningObject);
		}

		var fire = new FireEffect(owner, componentIndex, localPoint, shapeIndex,
			_fireShapeFrames[shapeIndex], OnMoon ? FireEffect.MoonLoopCount : FireEffect.LoopCount);
		_fires.Add(fire);

		// Sound_Play is not positional, so the loop would sound centred at the row's own volume until
		// the tick below placed it. The original does not have that gap: FireEffect_Ctor ends by
		// calling FireEffect_TickUpdate, which places the sound on the fire it just built. With this
		// the only live fire it is trivially the nearest, so placing it directly is that call's
		// outcome without ticking the flipbook a frame early.
		if (first) {
			Sounds?.MoveTo(SoundId.BurningObject, fire.Position);
		}
	}

	/// <summary>
	/// <c>FireEffect_ReleaseForOwner</c> (<c>0046b528</c>) — puts out every fire burning on one
	/// object. The original calls it in exactly one place, the whole-object destruction branch, which
	/// clears a machine's per-component fires before lighting the one big one.
	/// </summary>
	internal void ReleaseFires(SimObject owner) {
		_fires.RemoveAll(fire => ReferenceEquals(fire.Owner, owner));

		if (_fires.Count == 0) {
			Sounds?.Stop(SoundId.BurningObject);
		}
	}

	/// <summary>
	/// Adds an object to the simulation — <c>ObjectList_Add</c> (<c>00411dd4</c>), which appends
	/// to the world's one live-object list and stamps the object with the slot it landed in.
	///
	/// <para>The slot matters: it is how every per-object table in the simulation is addressed, so
	/// each object's tables are grown to cover the new list length as it joins. The original
	/// allocates them to a fixed cap instead; growing is the only difference.</para>
	/// </summary>
	public void Add(SimObject simObject) {
		simObject.ListIndex = _objects.Count;
		_objects.Add(simObject);

		// Mech_Constructor takes its shadows out of the flat pool as it builds the machine; this
		// is the first point here at which a machine has a world to take them from.
		(simObject as MechObject)?.AllocateShadows(this);

		for (int i = 0; i < _objects.Count; i++) {
			_objects[i].EnsureTableSize(_objects.Count);
		}

		if (_sightingCalledIn.Length < _objects.Count) {
			Array.Resize(ref _sightingCalledIn, _objects.Count);
		}
	}

	/// <summary>
	/// <c>DAT_004a9b84[obj+0x4b]</c> — whether the player's group has already had its chance to call
	/// <paramref name="enemy"/> in, a per-object latch indexed by <see cref="SimObject.ListIndex"/>
	/// and never cleared during a mission. Written only by <see cref="MechObject.EnemySighted"/>.
	/// </summary>
	internal bool SightingCalledIn(SimObject enemy) =>
		enemy.ListIndex >= 0 && enemy.ListIndex < _sightingCalledIn.Length && _sightingCalledIn[enemy.ListIndex];

	/// <summary>Raises <see cref="SightingCalledIn"/> for <paramref name="enemy"/>.</summary>
	internal void LatchSighting(SimObject enemy) {
		if (enemy.ListIndex >= 0 && enemy.ListIndex < _sightingCalledIn.Length) {
			_sightingCalledIn[enemy.ListIndex] = true;
		}
	}

	private bool[] _sightingCalledIn = Array.Empty<bool>();

	/// <summary>
	/// The counter of the countdown record at <c>004a9be8</c> — the rate limit on the "enemy detected"
	/// callout. Stepped and re-armed by <see cref="MechObject.EnemySighted"/>, once per call from any
	/// machine but the player's, and by nothing else — whether retail steps it anywhere else is an
	/// Open item of docs/retail/simulation/ai-targeting.md.
	/// </summary>
	internal short SightingCalloutTimer;

	/// <summary>
	/// <c>Sim_RaycastObjectList</c> (<c>00426528</c>) — the shared ray-versus-live-object query, which
	/// is a hit test and a damage application at once: each candidate's own
	/// <see cref="SimObject.DirectFireHitTest"/> resolves the geometry and applies whatever got
	/// through in the same call.
	///
	/// <para>The sweep <b>shortens the ray as it goes</b> and does not stop at the first hit: a
	/// candidate found later, but nearer, overwrites the one before it, because every subsequent
	/// candidate is tested against the shortened <see cref="WeaponShot.Distance"/> and can only pass
	/// if it is nearer still. It ends early only for a hit inside
	/// <see cref="WeaponShot.MinimumScanDistance"/>, which nothing can beat.</para>
	///
	/// <para>The terrain goes first, and is the reason an object sweep on its own would not do:
	/// <see cref="RaycastTerrain"/> shortens the ray at the ground before a single object is tested,
	/// so a machine standing behind a ridge cannot be shot through it.</para>
	///
	/// <para>Both of the original's exclusions are here: the attacker at the shot record's
	/// <c>+0x0e</c> (<see cref="WeaponShot.Owner"/>) and the second one at <c>+0x14</c>
	/// (<see cref="WeaponShot.Excluded"/>), which only a flyer's airframe contact probe ever fills.</para>
	///
	/// <para>The AI "something just shot at me" notification on each candidate's <c>+0x50</c> slot is
	/// here, and so is the friendly-fire complaint the original raises beside it, and the engagement
	/// pair below.</para>
	/// </summary>
	/// <returns>The distance the shot travelled before it hit something, or zero if it hit nothing.</returns>
	public int Raycast(WeaponShot shot) {
		// The mission difficulty scales both damage figures before anything is tested, which is where
		// the original puts it too. A shot with no attacker has no side to scale by and is left alone
		// — the original's own gate, and what keeps a flyer's airframe contacts out of it.
		if (shot.Owner is { } attacker) {
			shot.ApplyDifficultyScale(DamageScaleFor(attacker.Side));
		}

		bool hit = RaycastTerrain(shot);

		for (int i = 0; i < _objects.Count; i++) {
			var candidate = _objects[i];

			// An object whose mission group is still waiting on its action is skipped before any
			// geometry is touched — the original's own `group[+0x14] != 0` test, the same one
			// MechObject.CollisionTest and the frame submit make. It matters more here than
			// anywhere: an undeployed group is placed by the ordinary rules, so retail missions
			// routinely leave several of them stacked on a shared waypoint — the first stock
			// mission parks seven objects in three overlapping pairs — where they are invisible and
			// unticked but, without this, perfectly solid. Shots stopped on nothing.
			if (candidate.Removed || candidate.AwaitingDeployment
					|| ReferenceEquals(candidate, shot.Owner)
					|| ReferenceEquals(candidate, shot.Excluded)) {
				continue;
			}

			int struckAt = candidate.DirectFireHitTest(this, shot);
			if (struckAt == 0) {
				continue;
			}

			// The engagement pair, and the two halves land on opposite objects: the shooter is marked
			// engaged, the struck object's action fires. See docs/retail/simulation/mission-deployment.md, "An
			// object's own two actions".
			if (shot.Owner is { } firer
					&& ReferenceEquals(candidate, SimObject.SelectedTargetOf(firer))) {
				firer.Engaged = true;
				candidate.ActivateEngagementAction(this);
			}

			// "Something just shot at me", on the candidate's own +0x50 slot. The original puts it
			// exactly here — past the hit test, so only what the ray actually reached hears about it —
			// and skips it for a destroyed candidate, or an enemy one that is out of action. It applies
			// no damage. See docs/retail/simulation/hit-detection.md.
			if (shot.Owner is { } owner && !candidate.Destroyed
					&& (candidate.Side == owner.Side || !candidate.OutOfAction)) {
				// The player hitting anything on his own side outside his own group, structures
				// included: the player's own nearest squadmate complains, if it is close enough to
				// him to have seen it (docs/retail/simulation/ai-targeting.md, "Radio callouts").
				if (owner.LocallyPiloted && candidate.Side == owner.Side
						&& !ReferenceEquals(candidate.Group, owner.Group)
						&& owner.Group?.NearestLiveMember(owner) is MechObject witness
						&& witness.Position.ApproxDistanceTo(owner.Position) < FriendlyFireWitnessRange) {
					witness.FriendlyFireComplaint(this);
				}

				candidate.OnTakingFire(this, owner, shot.DamageArmor);
			}

			shot.Distance = struckAt;
			shot.HitObject = candidate;
			hit = true;

			if (struckAt < WeaponShot.MinimumScanDistance) {
				break;
			}
		}

		// The ground impact, which is the sweep's own job and not the ground's: the original keeps two
		// flags — "something was struck" and "an object was struck" — and spawns an effect at the ray's
		// far end when the first is set and the second is not. So a shot that ends in the dirt puts one
		// down and a shot that ends on a machine does not, even though the ground clipped the ray
		// first in both cases.
		//
		// It comes out of the ImpactFxGroup.Ground array, and unlike every object hit it is spawned
		// with no owner and with the sound suppressed (the constructor's last argument is 0 here and 1
		// at every other site).
		if (hit && shot.HitObject == null) {
			SpawnPickedImpactEffect(
				shot.ImpactFx(WeaponShot.ImpactFxGroup.Ground),
				shot.Muzzle.TransformPoint(0, shot.Distance, 0),
				owner: null,
				playSound: false);
		}

		// "My line of fire is blocked", on the shooter's own +0x64 slot, and the only caller of it in
		// the image. A shot that stopped nearer than the target it was aimed at and within 45 degrees
		// of the same bearing is the shooter hitting something in the way — which is what sends a
		// machine into `skirting` to walk around it. Only a machine implements the slot.
		//
		// A shot that reached the target does not count, which is what keeps the state off a machine
		// that is shooting perfectly well: the original clears the "what was struck" pointer, and only
		// that, when the candidate it just resolved is the shooter's own target.
		if (hit && shot.Owner is MechObject { Target: { } aimedAt } shooter
				&& !ReferenceEquals(shot.HitObject, aimedAt)) {
			var stopped = shot.Muzzle.TransformPoint(0, shot.Distance, 0);

			// The original measures the two the two different ways: the ground plane to where the shot
			// stopped, three dimensions to the target.
			int toStop = SimMath.FastMagnitude2D(
				shooter.Position.X - stopped.X, shooter.Position.Y - stopped.Y);

			if (toStop < shooter.Position.ApproxDistanceTo(aimedAt.Position)) {
				short spread = (short)(Detection.HeadingToward(stopped, shooter.Position)
					- Detection.HeadingToward(aimedAt.Position, shooter.Position));

				if (System.Math.Abs((int)spread) < BlockedLineOfFireArc) {
					shooter.OnLineOfFireBlocked();
				}
			}
		}

		return hit ? shot.Distance + 1 : 0;
	}

	/// <summary>
	/// <c>Sim_RaycastShapes</c> (<c>00404ca0</c>) and the <c>Sim_RaycastShapeList</c>
	/// (<c>00404bc0</c>) it ends in — a ray between two world points against the structures' collision
	/// volumes, and nothing else: no terrain, no machines, no damage, no effects. It is the AI's shape
	/// probe, called by <see cref="MechObject"/>'s obstacle avoidance and its line-of-sight test (see
	/// docs/retail/simulation/hit-detection.md, "The shape probe").
	///
	/// <para>The ray is built as a shot's is — pointed from <paramref name="from"/> at
	/// <paramref name="to"/> by <see cref="SimTrig.EulerToward"/>, starting at
	/// <paramref name="from"/>, as long as the sqrt-free distance between them — and each candidate
	/// is tested by <see cref="BaseObject.VolumeRaycast"/>. Candidates are every structure
	/// <see cref="BaseObject.InShapeList"/> admits, oldest first as <c>Pool_Prev</c> walks the
	/// structure pool. The original gathers them with no deployment test, so a structure whose group
	/// has not arrived is one.</para>
	///
	/// <para>The sweep shortens the ray to each hit and stops at one inside
	/// <see cref="WeaponShot.MinimumScanDistance"/>, as <see cref="Raycast"/> does, so what it reports
	/// is the last structure struck: the nearest, unless an earlier one was inside that distance.</para>
	///
	/// <para>The original takes a clearance and a side filter as well. All three of its callers pass a
	/// clearance of 0 and a side of −1, "any", so neither is a parameter here.</para>
	/// </summary>
	/// <param name="from">Where the ray starts, in world units.</param>
	/// <param name="to">Where it ends.</param>
	/// <param name="distance">How far along the ray the struck structure's volume was entered.</param>
	/// <param name="struck">The structure struck, or null for a miss.</param>
	/// <returns>Whether any structure was struck.</returns>
	internal bool RaycastShapes(Vec3i from, Vec3i to, out int distance, out BaseObject? struck) {
		var (pitch, roll, yaw) = SimTrig.EulerToward(to, from);
		var muzzle = Transform3.FromEuler(pitch, roll, yaw);
		muzzle.X = from.X;
		muzzle.Y = from.Y;
		muzzle.Z = from.Z;

		var muzzleInverse = muzzle.Inverted();
		int length = to.ApproxDistanceTo(from);

		distance = 0;
		struck = null;

		for (int i = 0; i < _objects.Count; i++) {
			if (_objects[i] is not BaseObject { Removed: false, InShapeList: true } candidate
					|| !candidate.VolumeRaycast(muzzle, muzzleInverse, length, ShapeProbeClearance,
						out int struckAt)) {
				continue;
			}

			length = struckAt;
			distance = struckAt;
			struck = candidate;

			if (struckAt < WeaponShot.MinimumScanDistance) {
				break;
			}
		}

		return struck != null;
	}

	/// <summary>The clearance every caller of <see cref="RaycastShapes"/> passes.</summary>
	private const int ShapeProbeClearance = 0;

	/// <summary>
	/// <c>Damage_ExplosiveBlastSweep</c> (<c>00426a20</c>) — the area-of-effect counterpart of
	/// <see cref="Raycast"/>: instead of following a ray it walks the whole live-object list once and
	/// offers the blast to everything standing inside it.
	///
	/// <para>The range test is <b>surface to centre, not centre to centre</b>: an object's own
	/// <see cref="SimObject.HitRadius"/> is subtracted before the comparison, so a large machine is
	/// caught by a blast that a small one standing in the same place would be outside of.</para>
	///
	/// <para>What the blast then does is the object's own business — <see cref="SimObject.ExplosiveDamage"/>,
	/// which is implemented for a machine and nothing else. Unlike the raycast the sweep does not
	/// stop, shorten or care about order: everything in range is hit, and a wall between two of them
	/// does not shield either, which is the original's behaviour and not a simplification.</para>
	///
	/// <para>Exactly three call sites, all terminal events rather than routine fire, and all three
	/// reach it here: the drop pod touching down (<see cref="MeteorObject"/>), a plasma round going
	/// off (<see cref="Projectile"/>), and a machine ending its ramming charge on something — see
	/// MechObject.Ramming.cs.</para>
	/// </summary>
	/// <param name="hitPoint">Where the explosion went off, in world units.</param>
	/// <param name="blastRadius">How far it reaches, and the denominator of each victim's falloff.</param>
	/// <param name="damage">The blast's damage figure, before any victim's shields scale it.</param>
	/// <param name="attacker">Who set it off, for the kill credit.</param>
	/// <param name="excluded">
	/// One object the blast passes over — the sweep's own <c>param_5</c>, which the machine death
	/// throe uses to keep a wreck from blowing itself up a second time.
	/// </param>
	/// <returns>
	/// Whether the blast <i>caught</i> anything — set beside the damage call and so before that call
	/// can decide the target's shields swallowed it, which is the original's own arrangement. The
	/// drop pod's <c>+0x4c</c> latch is the one caller that reads it; see <see cref="MeteorObject"/>.
	/// </returns>
	public bool ExplosiveBlastSweep(Vec3i hitPoint, int blastRadius, short damage,
			SimObject? attacker, SimObject? excluded) {
		bool hit = false;

		for (int i = 0; i < _objects.Count; i++) {
			var candidate = _objects[i];

			// A group still waiting on its arrival action is not in the mission, so it is not in the
			// blast either — the same gate the raycast and the frame submit make, and the same reason:
			// undeployed groups sit stacked on shared waypoints where a single explosion would
			// otherwise catch all of them at once.
			if (candidate.Removed || candidate.AwaitingDeployment
					|| ReferenceEquals(candidate, excluded)) {
				continue;
			}

			if (candidate.Position.ApproxDistanceTo(hitPoint) - candidate.HitRadius >= blastRadius) {
				continue;
			}

			candidate.ExplosiveDamage(this, damage, hitPoint, blastRadius, attacker);
			hit = true;
		}

		return hit;
	}

	/// <summary>
	/// <c>Sim_RaycastTerrain</c> (<c>00428048</c>) — the ray-versus-ground query the shared raycast
	/// runs before it looks at any object. It rebuilds the ray's far end from the shot's own frame,
	/// walks the heightmap with <see cref="HeightGrid.RayWalk"/>, and measures the ground hit back
	/// to the muzzle; a hit nearer than the ray's current length clips it there.
	///
	/// <para>The measured distance uses the sim's sqrt-free magnitude, as the original does, so it
	/// carries that approximation's direction-dependent error, as every other 3D range in the
	/// simulation does.</para>
	///
	/// <para>The original also hands the sweep a pseudo-object standing in for the ground, so that
	/// the AI notification path has something to name. Nothing here consumes that yet, so a terrain
	/// clip leaves <see cref="WeaponShot.HitObject"/> null and records the point instead.</para>
	/// </summary>
	/// <returns>Whether the shot was clipped at the ground.</returns>
	private bool RaycastTerrain(WeaponShot shot) {
		// The ray is the muzzle transform's Y axis, so its far end is that frame's own
		// (0, distance, 0) — the same construction the shot itself was built from.
		var muzzle = new Vec3i(shot.Muzzle.X, shot.Muzzle.Y, shot.Muzzle.Z);
		var end = shot.Muzzle.TransformPoint(0, shot.Distance, 0);

		if (!Terrain.RayWalk(muzzle, end, out var ground)) {
			return false;
		}

		int distance = ground.ApproxDistanceTo(muzzle);
		if (distance >= shot.Distance) {
			return false;
		}

		shot.Distance = distance;
		shot.GroundHit = ground;
		return true;
	}

	/// <summary>
	/// Resolves one beam and records it. The <c>PROJ.DAT</c> lookup <c>Bullet_FireBurst</c> repeats
	/// on the way in is skipped — the mount already resolved the same record at loadout time and
	/// <see cref="WeaponShot"/> carries what it holds.
	/// </summary>
	internal void FireBeam(WeaponShot shot) {
		// The report goes first, before the ray is resolved, because that is where Bullet_FireBurst
		// puts it — a fixed catalog id rather than one off the weapon's own record, so every beam in
		// the game makes the same noise at the muzzle.
		Sounds?.PlayAt(SoundId.BeamFire, new Vec3i(shot.Muzzle.X, shot.Muzzle.Y, shot.Muzzle.Z));

		int travelled = Raycast(shot);

		// Bullet_FireBurst's own fallback: a sweep that struck nothing returns zero, and the tracer is
		// drawn out to the weapon's full range instead — a miss is still a visible shot.
		if (travelled == 0) {
			travelled = shot.Range;
		}

		// The far end is rebuilt from the shot's frame rather than measured, the same construction the
		// terrain clip uses: the ray is the muzzle transform's Y axis.
		_tracers.Add(new BeamTracer(
			new Vec3i(shot.Muzzle.X, shot.Muzzle.Y, shot.Muzzle.Z),
			shot.Muzzle.TransformPoint(0, travelled, 0),
			shot.SubtypeId,
			BeamTable?.HalfWidth(shot.SubtypeId) ?? 0,
			Random,
			shot.Muzzle.RotateVector(0, BeamTracer.SpanLength, 0),
			travelled));

		_beams.Add(shot);
	}

	/// <summary>
	/// <c>Bullet_Fire</c> (<c>0040b43c</c>) — spawns one travelling shot. The powered form <c>Bullet_FirePowered</c> (<c>0040b5a0</c>) is the
	/// same call with two fields written afterwards, so it is this one method: an energy gun passes
	/// the capacitor charge it spent, an ammunition mount passes zero.
	///
	/// <para>The homing target the powered form also attaches, for the plasma subtype alone, is the
	/// firing machine's <b>selected target</b> (<c>mech+0x1a4</c>), which
	/// <see cref="TargetSelection"/> now fills in. A round fired with nothing selected still flies
	/// straight, exactly as it does in the original. See <see cref="Projectile.Target"/>.</para>
	/// </summary>
	/// <param name="projectile">The firing <c>PROJ.DAT</c> record.</param>
	/// <param name="muzzle">The world muzzle point the fire prologue worked out.</param>
	/// <param name="aim">The shot transform's euler triple, before scatter.</param>
	/// <param name="ownerSpeed">The firing machine's travel speed, which the shot inherits.</param>
	/// <param name="power">The capacitor charge spent, or zero for a shot out of a magazine.</param>
	/// <param name="owner">The machine that fired.</param>
	/// <returns>The shot, or null when <see cref="Bullets"/> has no record for its subtype.</returns>
	internal Projectile? FireBullet(ProjectileData.Projectile projectile, Vec3i muzzle,
			(short X, short Y, short Z) aim, short ownerSpeed, short power, SimObject? owner) {
		if (Bullets?.Record(projectile.SubtypeId) is not { } record) {
			return null;
		}

		var shot = new Projectile(projectile, record, muzzle, aim, ownerSpeed, power, owner, Random);

		// Unlike the beam's fixed report this one is the weapon's own, out of the record: BULLETS.DAT
		// +0x08, played at the muzzle as the stored id plus the effects-half bias.
		PlayTableSound(record.FireSoundId, muzzle);

		// The powered form's second write, and the whole of what makes one subtype behave differently
		// from the other eight: the plasma round takes the firing machine's selected target and
		// chases it. Everything else flies where it was pointed.
		if (projectile.SubtypeId == Projectile.PlasmaSubtype && owner is MechObject firing) {
			shot.Target = firing.Target;
		}

		_projectiles.Add(shot);
		return shot;
	}

	/// <summary>
	/// <c>Rocket_Fire</c> (<c>0040a9c4</c>) — spawns one launcher round. There is no powered form: a
	/// rocket comes off a rack, never out of a capacitor, so the record's damage is what it does.
	///
	/// <para><b>A target is attached only when this class of launcher has lock</b> — the launcher's
	/// vtable <c>+0x6c</c> (<c>Mech_MissileLockState</c>, <c>004155ac</c>), which
	/// reads the per-subtype lock flags at <c>manager+0x0a</c> rather than any ammunition count. See
	/// <see cref="MechObject.MissileLockTick"/> for what builds them. A round fired without lock
	/// flies where it was pointed, which is exactly what the original does. <b>Every other class's
	/// slot is a <c>return 1</c> stub</b> (<c>SimObject_MissileLockState_Always</c>, <c>00411b04</c>),
	/// so a <see cref="FlyerObject"/>'s or a <see cref="BaseObject"/>'s rounds always take the
	/// launcher's selected target.</para>
	///
	/// <para>The one exception is the original's own: a machine that is <b>not</b> locally piloted
	/// firing <see cref="Rocket.PlayerFlownSubtype"/> skips the lock gate outright, because that
	/// subtype is the missile the player flies by hand and so never builds a lock — which is how an
	/// AI opponent's electro-optical missile still tracks.</para>
	///
	/// <para>A target, once attached, is asked which of its components the round locks on to
	/// (<see cref="Rocket.LockComponent"/>).</para>
	///
	/// <para>Every round the locally piloted machine launches becomes <see cref="PlayerMissile"/>, the
	/// one the MFD's missile camera rides.</para>
	/// </summary>
	/// <param name="projectile">The firing <c>PROJ.DAT</c> record.</param>
	/// <param name="muzzle">The world muzzle point the fire prologue worked out.</param>
	/// <param name="aim">The shot transform's euler triple. A rocket has no scatter to apply to it.</param>
	/// <param name="ownerSpeed">The launching machine's travel speed, which the round inherits.</param>
	/// <param name="owner">The machine that fired.</param>
	/// <returns>The round, or null when <see cref="Rockets"/> has no record for its subtype.</returns>
	internal Rocket? FireRocket(ProjectileData.Projectile projectile, Vec3i muzzle,
			(short X, short Y, short Z) aim, short ownerSpeed, SimObject? owner) {
		if (Rockets?.Record(projectile.SubtypeId) is not { } record) {
			return null;
		}

		var round = new Rocket(projectile, record, muzzle, aim, ownerSpeed, owner);

		// ROCKETS.DAT's layout is not BULLETS.DAT's: the launch sound is at +0x0c, not the guns' +0x08.
		PlayTableSound(record.FireSoundId, muzzle);

		if (owner is { LocallyPiloted: true }) {
			PlayerMissile = round;
		}

		if (owner is MechObject launching
				&& (launching.MissileLocked(projectile.SubtypeId)
					|| (!launching.LocallyPiloted && projectile.SubtypeId == Rocket.PlayerFlownSubtype))) {
			round.Lock(launching.Target);
		} else if (owner is not null and not MechObject) {
			// The lock gate is the launcher's own vtable +0x6c, and every class but the mech installs a
			// `return 1` stub (SimObject_MissileLockState_Always (00411b04)) -- so a Cybrid flyer's or a
			// missile tower's round is always given the launcher's selected target.
			round.Lock(owner.Target);
		}

		_rockets.Add(round);
		return round;
	}

	/// <summary>Records a travelling shot's impact for <see cref="Impacts"/>. Not part of the original.</summary>
	internal void RecordProjectileHit(WeaponShot shot) => _impacts.Add(shot);

	/// <summary>
	/// Advances the simulation by one tick of the engine's own fixed length. See
	/// <see cref="Tick(short, double)"/>.
	/// </summary>
	public void Tick() => Tick(TickDelta, 1000.0 / TicksPerSecond);

	/// <summary>
	/// One tick under the developer keys' <c>Alt+S</c> freeze, of the engine's own length. See
	/// <see cref="TickFrozen(short)"/>.
	/// </summary>
	public void TickFrozen() => TickFrozen(TickDelta);

	/// <summary>
	/// One tick under the <c>Alt+S</c> freeze: what <c>Sim_MainTick</c> still runs with
	/// <c>DAT_004d2576</c> up. That is the player's input poll (<see cref="MechObject.FrozenTick"/>)
	/// and the mission's verdict; every pool, object, group, timer and the sensor sweep wait. The
	/// modal panels are not this: they stop the tick outright.
	/// </summary>
	public void TickFrozen(short tickDelta) {
		SimMath.TickDelta = tickDelta;
		_beams.Clear();
		_impacts.Clear();
		PlayerTriggerCleared = false;

		if (PlayerMech is { Removed: false, AwaitingDeployment: false } player) {
			player.FrozenTick(this);
		}

		if (PlayerMech is { Removed: false, Destroyed: false } pilot) {
			PollMission(pilot);
		}

		// Sim_RenderFrame still runs under the freeze, and with it the phase-5 flushes and the charge-bar
		// exchange.
		FlushRenderFrameDeletes();
		PlayerMech?.Weapons.PushGaugeStates();
	}

	/// <summary>
	/// The pool flushes among the phase-5 subsystem hooks the top of <c>Sim_RenderFrame</c> runs
	/// (<c>Subsystem_RunPhase</c>, <c>00401d94</c>), in their registration order: the effect lights'
	/// (<c>EffectLightPool_FlushDeletes</c>, <c>004077e8</c>) before the impact effects'
	/// (<c>ExplosionPool_FlushDeletes</c>, <c>00407b3c</c>). The second is what queues an ended effect's light
	/// handle, so the first does not return it until the next frame — see
	/// docs/retail/formats/effect-lights.md#claiming-a-slot.
	/// </summary>
	private void FlushRenderFrameDeletes() {
		EffectLights.FlushReleases();

		for (int i = 0; i < _effects.Count; i++) {
			if (_effects[i].Finished) {
				_effects[i].Destruct();
			}
		}

		_effects.RemoveAll(effect => effect.Finished);
	}

	/// <summary>
	/// <c>Sim_MainTick</c>'s mission poll: <see cref="MissionObjectives.Poll"/>, and on
	/// <see cref="MissionStatus.PlayerImmobilised"/> the outnumbered damage on the player's group first,
	/// which turns the answer into <see cref="MissionStatus.ImmobilisedThenDestroyed"/> when it kills the
	/// player. See docs/retail/simulation/mission-objectives.md#the-poll--mission_pollstatus-004131ac.
	/// </summary>
	private void PollMission(MechObject pilot) {
		var alert = Objectives.Poll(this, pilot);
		if (alert == MissionStatus.None) {
			return;
		}

		if (alert == MissionStatus.PlayerImmobilised && pilot.Group is { } group) {
			MechObject.ApplyGroupOutnumberedDamage(this, group);
			if (pilot.Destroyed) {
				alert = MissionStatus.ImmobilisedThenDestroyed;
			}
		}

		PendingMissionAlert = alert;
	}

	/// <summary>
	/// Advances the simulation by one tick: publishes the timestep, then updates every live object.
	/// Objects flagged removed are skipped, matching how the original's tick walks its lists.
	///
	/// <para><paramref name="tickDelta"/> is the <c>SimTickDelta</c> the tick runs with and
	/// <paramref name="elapsedMilliseconds"/> the time it stands for. Anything but the engine's own
	/// pair comes from a replayed tape, whose frames each carry the delta the original measured for
	/// them.</para>
	/// </summary>
	public void Tick(short tickDelta, double elapsedMilliseconds) {
		SimMath.TickDelta = tickDelta;
		_beams.Clear();
		_impacts.Clear();
		PlayerTriggerCleared = false;

		// The effect pool goes first, as it does in Sim_MainTick, where it is walked ahead of the
		// machine list. That ordering is what gives a tracer a full tick on screen: one spawned while
		// a machine updates is not counted down until the tick after. A travelling shot gets the same
		// deal — the round that leaves the barrel this tick does not move or hit anything until the
		// next one.
		// Impact effects share that deal, and want it more: one is spawned from inside a hit test, so
		// it is created part-way through this same tick and must not be counted down until the next.
		// One that plays out keeps its slot until FlushRenderFrameDeletes, at the end of the tick.
		for (int i = _effects.Count - 1; i >= 0; i--) {
			_effects[i].Tick();
		}

		// Sim_MainTick drops the missile-flown flag immediately before it walks the pool these three
		// share, so it stays up only for as long as a round raises it again.
		MissileFlown = false;

		for (int i = _tracers.Count - 1; i >= 0; i--) {
			if (_tracers[i].Tick()) {
				_tracers.RemoveAt(i);
			}
		}

		for (int i = _projectiles.Count - 1; i >= 0; i--) {
			if (_projectiles[i].Tick(this)) {
				_projectiles.RemoveAt(i);
			}
		}

		for (int i = _rockets.Count - 1; i >= 0; i--) {
			if (_rockets[i].Tick(this)) {
				_rockets.RemoveAt(i);
			}
		}

		// The drop pods are a pool like the rest and Sim_MainTick walks them with the rest, before it
		// reaches the groups. That ordering is what lets a pod deliver its group and have the group go
		// live on the same tick rather than the next one.
		for (int i = _meteors.Count - 1; i >= 0; i--) {
			if (_meteors[i].Tick(this, _dropPodFrames)) {
				_meteors.RemoveAt(i);
			}
		}

		// Wreckage and fires are pool objects too, and walked with the rest of them. A piece that
		// bursts as it is ticked appends its children to the same list; iterating backwards means they
		// wait for the next tick rather than moving twice on this one, which is the deal every other
		// pool object gets.
		for (int i = _debris.Count - 1; i >= 0; i--) {
			if (_debris[i].Tick(this)) {
				_debris.RemoveAt(i);
			}
		}

		// One sound serves every fire in the mission, so it is placed on whichever of them is nearest
		// the camera: FireEffect_TickUpdate measures its own distance to ViewObjectPtr and calls
		// Sound_UpdatePosition(0x33) whenever it beats the running minimum at DAT_006b4fc0, which the
		// pool's phase-5 hook (FireEffect_PerFrameReset, 0046b084, run from Sim_RenderFrame) resets to 0x7fffffff
		// every frame. Taking the minimum across the walk and placing once is the same outcome.
		//
		// A burnt-out fire still counts towards the minimum on the tick it goes out, as it does in the
		// original: the placement happens above the loops-remaining test, not after it.
		long nearest = long.MaxValue;
		Vec3i nearestPosition = default;

		for (int i = _fires.Count - 1; i >= 0; i--) {
			bool done = _fires[i].Tick(this);

			var offset = _fires[i].Position - ListenerPosition;
			long distance = (long)offset.X * offset.X + (long)offset.Y * offset.Y
				+ (long)offset.Z * offset.Z;
			if (distance < nearest) {
				nearest = distance;
				nearestPosition = _fires[i].Position;
			}

			if (done) {
				_fires.RemoveAt(i);
				if (_fires.Count == 0) {
					Sounds?.Stop(SoundId.BurningObject);
				}
			}
		}

		if (_fires.Count > 0) {
			Sounds?.MoveTo(SoundId.BurningObject, nearestPosition);
		}


		// A group still waiting on its arrival action is not in the mission yet, so it does not tick:
		// Sim_MainTick sends such a group to Group_DeploymentCheck instead of its order tick, and
		// skips a base's own update outright. See SimObject.AwaitingDeployment.
		for (int i = 0; i < _objects.Count; i++) {
			var simObject = _objects[i];
			if (!simObject.Removed && !simObject.AwaitingDeployment) {
				simObject.Tick(this);
			}
		}

		// The AI, which is driven from the mission-group layer and not from the object list: a machine
		// that is not a live group member never thinks. It runs here, alongside the object updates and
		// ahead of the sensor sweep, so a machine reassesses on the contacts it had at the top of the
		// tick rather than on ones made during it.
		//
		// A group that has not entered the mission takes the other branch instead: Sim_MainTick sends
		// it to Group_DeploymentCheck and not to its order tick, and it is one or the other, never
		// both. See MissionGroup.AwaitingDeployment.
		for (int i = 0; i < _groups.Count; i++) {
			var group = _groups[i];

			if (group.AwaitingDeployment) {
				group.DeploymentCheck(this);
			} else {
				group.AiTick(this);
			}
		}

		// The mission's timers, then its triggers. Sim_MainTick runs ActionTimers_Tick (00426b48) and
		// Actions_EvaluateTriggers back to back and -- the part that is easy to get backwards --
		// *after* the group pass, not before it. So an action that fires this tick is not seen by the
		// group waiting on it until the next one, and a group arrives a tick after its trigger.
		for (int i = 0; i < _actionTimers.Count; i++) {
			_actionTimers[i].Tick(this);
		}

		MissionTriggers.Evaluate(this);

		// Who can see whom, worked out from where everything has just finished moving to.
		// Sim_MainTick puts it exactly here: after every pool's per-object update and after the
		// player's input poll, immediately ahead of the per-mech systems pass. So a contact made this
		// tick is not acted on until the next one.
		Detection.Tick(this);

		// And then the per-mech systems pass, which is where Sim_MainTick puts it — immediately after
		// the sensor sweep, because the lock gate reads the line-of-sight cache that sweep maintains.
		// Only the missile-lock half and the ECM's engagement gate run from here; the reactor and shield
		// half is inside MechObject.Tick, where its inputs are last tick's and its position is free.
		for (int i = 0; i < _objects.Count; i++) {
			if (_objects[i] is MechObject { Removed: false, AwaitingDeployment: false, Destroyed: false } mech) {
				mech.RaiseTargetEngagementGate();
				mech.AiTimersTick();
				mech.MissileLockTick(this);
			}
		}

		// And last, the mission's own verdict on how the player is doing -- Sim_MainTick's final act,
		// after the systems pass and gated on the player's machine still being alive. It is throttled
		// hard inside: see MissionObjectives.Poll.
		if (PlayerMech is { Removed: false, Destroyed: false } pilot) {
			PollMission(pilot);
		}

		FlushRenderFrameDeletes();

		// And that frame's Player_PerFrameCockpitUpdate exchanges the power levels with the charge bars, one
		// push to each tick; see WeaponMounts.PushGaugeStates.
		PlayerMech?.Weapons.PushGaugeStates();

		TickCount++;
		ElapsedMilliseconds += elapsedMilliseconds;
	}

	/// <summary>
	/// Terrain height under a world position, via the ported <c>Terrain_HeightQuery</c>. Provided
	/// here because it is the form simulation code wants — ground-impact checks and the flyer
	/// terrain-avoidance autopilot both ask "how high is the ground under this object".
	/// </summary>
	public int GroundHeightAt(Vec3i position) => Terrain.HeightAtWorld(position.X, position.Y);

	/// <summary>
	/// Half-arc, either side of the bearing to the target, inside which a shot that stopped short
	/// counts as the shooter's own line of fire being blocked. 45°; see
	/// docs/retail/simulation/ai-combat-states.md.
	/// </summary>
	private const int BlockedLineOfFireArc = 0x2000;

	/// <summary>
	/// How near the player the squadmate <see cref="Raycast"/> picks must be for it to complain about
	/// the player's shot at a friendly outside the group. See docs/retail/simulation/ai-targeting.md,
	/// "Radio callouts".
	/// </summary>
	private const int FriendlyFireWitnessRange = 30000;
}
