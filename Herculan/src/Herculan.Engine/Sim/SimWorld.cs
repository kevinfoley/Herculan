using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.Terrain;

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

	/// <param name="terrain">The loaded zone.</param>
	/// <param name="bullets">
	/// <c>dat\BULLETS.DAT</c>, which everything that fires a travelling shot needs — see
	/// <see cref="FireBullet"/>. Null leaves those weapons firing blanks, the same way an unported
	/// branch does.
	/// </param>
	/// <param name="explosions">
	/// <c>dat\EXPLOS.DAT</c>, which everything that lands needs — see
	/// <see cref="EffectPools.SpawnImpactEffect"/>. Null leaves impacts invisible.
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
	/// The debris databases, headed by <c>DEF_DEB</c> — see <see cref="EffectPools.SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)"/>. Null leaves
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
		Effects = new EffectPools(this);
		BehaviourBlock.ResetJitter();
	}

	/// <summary>The mission's actions, timers, objectives, counters and salvage.</summary>
	public MissionRuntime Mission { get; } = new();

	/// <summary>The impact effects, their lights, the wreckage in the air and the fires.</summary>
	public EffectPools Effects { get; }

	/// <summary>The player's flown missile and what the cockpit reads of it.</summary>
	public PlayerMissileState PlayerMissile { get; } = new();

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
	/// <see cref="Random"/>. <see cref="EffectPools.SpawnImpactEffect"/> makes <c>Explosion_Construct</c>'s
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
	/// <c>FlightModel_Step</c> (<c>00466a54</c>)'s throttle test, <see cref="Input.JoystickBindings.FlightThrottleIsLever"/>,
	/// as the player's controls stand this frame. The original reads it from globals for every airframe it
	/// steps, so a Cybrid flyer's throttle follows the player's stick and RAZOR THROTTLE binding — see
	/// docs/retail/simulation/ai-flyers.md, "A flyer cannot change speed". False when nothing sets it: a
	/// headless world has no player's controls.
	/// </summary>
	public bool FlightThrottleIsLever { get; set; }

	/// <inheritdoc cref="Content.GameContent.IsV110"/>
	/// <remarks>The simulation asks it only which release's ray walk to run — see <see cref="ThinRay"/>.</remarks>
	public bool IsV110 { get; set; }

	/// <summary>
	/// The rules every thin-ray ground test runs by, <see cref="HeightGrid.RayWalk"/> for weapon
	/// fire and line of sight: this install's release, and the
	/// <see cref="TweakSettingDefinitions.FixTerrainHitPoint"/> tweak as it stands now.
	/// </summary>
	public ThinRayRules ThinRay => new(IsV110, Tweaks.GetSettingValue(TweakSettingDefinitions.FixTerrainHitPoint));

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
	/// (<see cref="EffectPools.SpawnFire"/>). See docs/retail/simulation/destruction-effects.md.
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
	/// whoever loaded the shapes, for the reason <see cref="EffectPools.BindFireShapeFrames"/> is. Until it is,
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
	/// shapes, for the reason <see cref="EffectPools.BindDebrisShapeRadii"/> is. Until it is, a shape conforms at
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
	/// Every travelling shot that struck something during the tick just completed, as the shot record
	/// the raycast left behind. Cleared at the top of each <see cref="Tick"/>, exactly as
	/// <see cref="Beams"/> is, and not part of the original for the same reason.
	/// </summary>
	public IReadOnlyList<WeaponShot> Impacts => _impacts;

	/// <summary>
	/// The debris databases — <c>DEF_DEB</c> and whatever else has been asked for. Null when the
	/// install has no <c>DEF_DEB</c>, in which case nothing throws anything.
	/// </summary>
	public DebrisCatalog? Debris { get; }

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
	/// Resolves one beam and records it. The <c>PROJ.DAT</c> lookup <c>Bullet_FireBurst</c> repeats
	/// on the way in is skipped — the mount already resolved the same record at loadout time and
	/// <see cref="WeaponShot"/> carries what it holds.
	/// </summary>
	internal void FireBeam(WeaponShot shot) {
		// The report goes first, before the ray is resolved, because that is where Bullet_FireBurst
		// puts it — a fixed catalog id rather than one off the weapon's own record, so every beam in
		// the game makes the same noise at the muzzle.
		Sounds?.PlayAt(SoundId.BeamFire, new Vec3i(shot.Muzzle.X, shot.Muzzle.Y, shot.Muzzle.Z));

		int travelled = HitTests.Raycast(this, shot);

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
	/// <para>Every round the locally piloted machine launches becomes <see cref="PlayerMissileState.Round"/>, the
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
			PlayerMissile.Round = round;
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
		PlayerMissile.TriggerCleared = false;

		if (PlayerMech is { Removed: false, AwaitingDeployment: false } player) {
			player.FrozenTick(this);
		}

		if (PlayerMech is { Removed: false, Destroyed: false } pilot) {
			Mission.Poll(this, pilot);
		}

		// Sim_RenderFrame still runs under the freeze, and with it the phase-5 flushes and the charge-bar
		// exchange.
		Effects.FlushRenderFrameDeletes();
		PlayerMech?.Weapons.PushGaugeStates();
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
		PlayerMissile.TriggerCleared = false;

		// The effect pool goes first, as it does in Sim_MainTick, where it is walked ahead of the
		// machine list. That ordering is what gives a tracer a full tick on screen: one spawned while
		// a machine updates is not counted down until the tick after. A travelling shot gets the same
		// deal — the round that leaves the barrel this tick does not move or hit anything until the
		// next one.
		// Impact effects share that deal, and want it more: one is spawned from inside a hit test, so
		// it is created part-way through this same tick and must not be counted down until the next.
		// One that plays out keeps its slot until EffectPools.FlushRenderFrameDeletes, at the end of the tick.
		Effects.TickImpactEffects();

		// Sim_MainTick drops the missile-flown flag immediately before it walks the pool these three
		// share, so it stays up only for as long as a round raises it again.
		PlayerMissile.Flown = false;

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

		// Wreckage and fires are pool objects too, and walked with the rest of them.
		Effects.TickDebris();
		Effects.TickFires();

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
		for (int i = 0; i < Mission.ActionTimers.Count; i++) {
			Mission.ActionTimers[i].Tick(this);
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
			Mission.Poll(this, pilot);
		}

		Effects.FlushRenderFrameDeletes();

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
}
