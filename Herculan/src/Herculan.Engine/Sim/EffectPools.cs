using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// The pools of what a hit or a destruction leaves behind: impact effects and the lights they cast, wreckage, and
/// fires. Each is a DBSIM pool of its own; <see cref="SimWorld.Effects"/> holds them, and
/// <see cref="SimWorld.Tick(short, double)"/> walks them where <c>Sim_MainTick</c> does.
/// </summary>
public sealed class EffectPools {
	private readonly SimWorld _world;
	private readonly List<ImpactEffect> _effects = new();
	private readonly List<DebrisObject> _debris = new();
	private readonly List<FireEffect> _fires = new();

	internal EffectPools(SimWorld world) {
		_world = world;
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

	/// <summary>
	/// The impact effects playing right now — the original's <c>g_ExplosionPool</c> (<c>004a96a2</c>), a pool
	/// of its own that <c>Sim_MainTick</c> walks in the same effect-pool pass as the one
	/// <see cref="SimWorld.Tracers"/> and <see cref="SimWorld.Projectiles"/> come from. An
	/// entry lives for exactly one pass of its shape's flipbook; see <see cref="ImpactEffect"/>.
	///
	/// <para>An effect that plays out during a tick stays in this list, holding its slot against every
	/// later spawn in the same tick, until the render-time flush at the end of the tick removes it —
	/// <c>ExplosionPool_FlushDeletes</c> (<c>00407b3c</c>), a phase-5 hook of the <c>Sim_RenderFrame</c> the original
	/// runs after each <c>Sim_MainTick</c>. So between ticks every entry here is live.</para>
	/// </summary>
	public IReadOnlyList<ImpactEffect> ImpactEffects => _effects;

	/// <summary>
	/// The dynamic lights impact effects are currently casting — the effect light manager
	/// <c>DAT_004a968c</c>. The renderer reads it to decide what each drawn object is lit by; see
	/// <see cref="EffectLightField"/> and docs/retail/rendering/effect-lights.md.
	/// </summary>
	public EffectLightField Lights { get; } = new();

	/// <summary>
	/// <c>Explosion_Construct</c> (<c>00407f1c</c>) — puts one impact effect at <paramref name="position"/>. Called from the
	/// two places the original calls it from along this path: from inside an object's hit test, where
	/// the effect belongs to the object struck (and is spawned whether or not the sweep goes on to
	/// find something nearer), and from the tail of <see cref="HitTests.Raycast"/> itself for a shot that ends
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
	/// Every object-hit spawn passes true; the ground hit at the tail of <see cref="HitTests.Raycast"/> is the
	/// one site that passes false.
	/// </param>
	internal void SpawnImpactEffect(short typeId, Vec3i position, SimObject? owner, bool playSound = true) {
		if (ImpactEffectPoolFull || _world.Explosions?.Type(typeId) is not { } record) {
			return;
		}

		_effects.Add(new ImpactEffect(
			typeId, _world.Explosions, record, _world.Explosions.FrameCount(record.ShapeIndex), position, owner, Lights,
			_world));

		if (playSound && record.SoundId >= 0) {
			// Math_RandomBelow(0x32) on the presentation generator, whose result the constructor throws
			// away before playing the row's own sound (004080ca).
			_world.PresentationRandom.NextBelow(0x32);
			_world.PlayTableSound(record.SoundId, position);
		}
	}

	/// <summary>
	/// One of the four ids an <c>ImpactFX</c> array holds, drawn the way every spawn site draws it —
	/// <c>Math_RandomNext(...) &amp; 3</c>, so all four are equally likely and the same array gives a
	/// different effect shot to shot.
	/// </summary>
	internal short PickImpactEffect(short[]? effects) =>
		effects is { Length: > 0 } ? effects[_world.Random.NextMasked(3) % effects.Length] : (short)0;

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
	/// Wreckage in the air, from <see cref="SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)"/>. Each piece lives until it comes to rest
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
		if (_world.Debris?.Resolve(groupIndex, installed) is not { } resolved) {
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
			int draw = _world.Random.NextBelow((short)group.TotalWeight);
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
			? _world.Random.Next()
			: (short)(euler.Z + piece.ThrowYaw - piece.OrientationYaw);

		short pitch = (short)(_world.Random.NextBelow((short)(pitchMax - pitchMin)) + pitchMin);
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
		(short)(SimMath.Q10Multiply(SpinRateSpread, _world.Random.NextMasked(0x3ff)) + SpinRateBase);

	/// <summary>The burst countdown, drawn whether or not the piece has anything to burst into.</summary>
	private short DrawBurstDelay() => (short)(_world.Random.NextBelow(BurstDelaySpread) + BurstDelayBase);

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

		if (_world.OnMoon && shapeIndex != 1 && shapeIndex != 3) {
			return;
		}

		bool first = _fires.Count == 0;
		if (first) {
			_world.Sounds?.Play(SoundId.BurningObject);
		}

		var fire = new FireEffect(owner, componentIndex, localPoint, shapeIndex,
			_fireShapeFrames[shapeIndex], _world.OnMoon ? FireEffect.MoonLoopCount : FireEffect.LoopCount);
		_fires.Add(fire);

		// Sound_Play is not positional, so the loop would sound centred at the row's own volume until
		// the tick below placed it. The original does not have that gap: FireEffect_Ctor ends by
		// calling FireEffect_TickUpdate, which places the sound on the fire it just built. With this
		// the only live fire it is trivially the nearest, so placing it directly is that call's
		// outcome without ticking the flipbook a frame early.
		if (first) {
			_world.Sounds?.MoveTo(SoundId.BurningObject, fire.Position);
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
			_world.Sounds?.Stop(SoundId.BurningObject);
		}
	}

	/// <summary>
	/// The pool flushes among the phase-5 subsystem hooks the top of <c>Sim_RenderFrame</c> runs
	/// (<c>Subsystem_RunPhase</c>, <c>00401d94</c>), in their registration order: the effect lights'
	/// (<c>EffectLightPool_FlushDeletes</c>, <c>004077e8</c>) before the impact effects'
	/// (<c>ExplosionPool_FlushDeletes</c>, <c>00407b3c</c>). The second is what queues an ended effect's light
	/// handle, so the first does not return it until the next frame — see
	/// docs/retail/rendering/effect-lights.md#claiming-a-slot.
	/// </summary>
	internal void FlushRenderFrameDeletes() {
		Lights.FlushReleases();

		for (int i = 0; i < _effects.Count; i++) {
			if (_effects[i].Finished) {
				_effects[i].Destruct();
			}
		}

		_effects.RemoveAll(effect => effect.Finished);
	}

	/// <summary>The impact effects' walk, which <c>Sim_MainTick</c> makes ahead of every other pool.</summary>
	internal void TickImpactEffects() {
		for (int i = _effects.Count - 1; i >= 0; i--) {
			_effects[i].Tick();
		}
	}

	/// <summary>The wreckage's walk.</summary>
	internal void TickDebris() {
		// A piece that bursts as it is ticked appends its children to the same list; iterating backwards means
		// they wait for the next tick rather than moving twice on this one, which is the deal every other pool
		// object gets.
		for (int i = _debris.Count - 1; i >= 0; i--) {
			if (_debris[i].Tick(_world)) {
				_debris.RemoveAt(i);
			}
		}
	}

	/// <summary>The fires' walk, which places the burning sound on the nearest fire.</summary>
	internal void TickFires() {
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
			bool done = _fires[i].Tick(_world);

			var offset = _fires[i].Position - _world.ListenerPosition;
			long distance = (long)offset.X * offset.X + (long)offset.Y * offset.Y
				+ (long)offset.Z * offset.Z;
			if (distance < nearest) {
				nearest = distance;
				nearestPosition = _fires[i].Position;
			}

			if (done) {
				_fires.RemoveAt(i);
				if (_fires.Count == 0) {
					_world.Sounds?.Stop(SoundId.BurningObject);
				}
			}
		}

		if (_fires.Count > 0) {
			_world.Sounds?.MoveTo(SoundId.BurningObject, nearestPosition);
		}
	}
}
