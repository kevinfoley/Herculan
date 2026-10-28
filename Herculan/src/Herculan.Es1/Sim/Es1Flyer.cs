using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// A flyer, the <c>0x1d7</c>-byte object of <c>g_FlyerPool</c>, as <c>Flyer_Constructor</c> (<c>ES1DBSIM 0001ac58</c>)
/// builds it (docs/es1/simulation/flyers.md#construction--flyer_constructor-0001ac58) and the second pass places it
/// (docs/es1/formats/script-dat.md#the-second-pass). Its vtable <c>00091fea</c> is docs/es1/simulation/flyers.md#the-flyers-table;
/// a slot the table leaves to the object base answers as the base does (<see cref="IEs1SimObject"/>'s defaults). The
/// fields are docs/es1/simulation/flyers.md#the-fields.
/// </summary>
public sealed class Es1Flyer : IEs1SimObject {
	/// <summary>The type tag the constructor stores at <c>+0</c>, which the distance bands' shades list is keyed by.</summary>
	public const short TypeTag = 6;

	/// <summary>The target class the constructor stores at <c>+0x162</c>, after the base's −1.</summary>
	public const short FlyerClass = 2;

	/// <summary>The Z <see cref="Es1FlyerGroups.Attach"/> gives a flyer whose Z is 0: 5000 (30 m).</summary>
	public const int DefaultAltitude = 5000;

	/// <summary>
	/// <c>Flyer_Constructor</c>'s steps 2 to 5 on a flyer the pool has given (<see cref="Es1FlyerPool.Construct"/>):
	/// <c>SimObjectBase_ConstructStatic</c> on the type's shape, a static instance with no LOD table; the base's fields,
	/// the actions none and the flag number −1 (<see cref="Word174"/>), and <c>ObjectList_Add</c>;
	/// <c>Component_AllocDamageArrays</c>(<c>+0x196</c>, 1, 1); the class 2, the type record, the velocity (0, the record's
	/// <c>+4</c>, 0), <c>Component_LinkMaxRefData</c> with the record's collision model and damage table; and
	/// <c>Behaviour_SetState</c> on <c>deciding</c>, which steps <paramref name="jitter"/>. The base's <c>+0x92</c> and
	/// <c>+0xa1</c> set to 1 are not held: nothing ported reads <c>+0x92</c>, and the scanner takes <c>+0xa1</c> as set
	/// (<see cref="Mfd.Es1MfdScannerScreen"/>). A type without its table has no damage header, where retail asserts on the
	/// missing file.
	/// </summary>
	internal Es1Flyer(Es1FlyerType type, Es1DwellJitter jitter, Es1SimGlobals globals) {
		Type = type;
		globals.AddLiveObject(this);
		Velocity = (0, type.Data.SpeedForward, 0);
		if (type.DamageTable is { } table) {
			Damage = new Es1DamageHeader(this, table, type.CollisionModel, internals: 1, components: 1);
		}

		Behaviour.SetState(Es1FlyerBehaviour.States[Es1FlyerBehaviour.Deciding], jitter);
	}

	/// <summary><c>+0x190</c>, the type record.</summary>
	public Es1FlyerType Type { get; }

	/// <summary><c>+0x20</c>, the position.</summary>
	public Vec3i Position { get; set; }

	/// <summary><c>+6</c>, <c>+8</c> and <c>+0xa</c>: the pitch, the roll and the heading.</summary>
	public (short X, short Y, short Z) Rotation { get; set; }

	/// <summary><c>+0x2c</c>, the word that says the rotation is built from the angles (docs/es1/simulation/object-classes.md#the-fields).</summary>
	public short RotationWord { get; set; }

	/// <summary><c>+0xa</c>, the heading, the last of <see cref="Rotation"/>.</summary>
	public short Heading => Rotation.Z;

	/// <summary><c>Context_GetTransform</c> of <c>+6</c>: the rotation from the angles, at the position.</summary>
	public Transform3 Frame {
		get {
			var frame = Transform3.FromEuler(Rotation.X, Rotation.Y, Rotation.Z);
			frame.X = Position.X;
			frame.Y = Position.Y;
			frame.Z = Position.Z;
			return frame;
		}
	}

	/// <summary><c>+0x3f</c>, the group.</summary>
	public Es1MissionGroup? Group { get; set; }

	/// <summary><c>+0x43</c>, its ref's place among the group's eight.</summary>
	public int MemberSlot { get; set; }

	/// <inheritdoc/>
	public short ListSlot { get; set; }

	/// <summary>
	/// <c>+0x47</c>, the behaviour block; in it the word <c>+0x54</c> is the attack run's missile flag and the countdown
	/// record <c>+0x54</c> (its counter <c>+0x55</c>) the target sweep's.
	/// </summary>
	public Es1BehaviourBlock Behaviour { get; } = new();

	/// <inheritdoc/>
	public Es1BehaviourDescriptor? BehaviourDescriptor => Behaviour.Descriptor;

	/// <inheritdoc/>
	public bool RadarVisible { get; set; }

	/// <summary>
	/// <c>+0x90</c>, the radar, cleared every tick the flyer follows its route or keeps station, and on its death; no
	/// constructor step writes it.
	/// </summary>
	public bool RadarOn { get; set; }

	/// <summary><c>+0x93</c>, destroyed, which the damage write sets.</summary>
	public bool Destroyed { get; set; }

	/// <summary><c>+0x97</c>, the target just changed, which the patrol's sweep sets.</summary>
	public bool TargetChanged { get; set; }

	/// <inheritdoc/>
	public bool Engaged { get; set; }

	/// <inheritdoc/>
	public bool EngagementGate { get; set; }

	/// <summary><c>+0x9d</c>: no flyer is the player's machine.</summary>
	public bool IsPlayer => false;

	/// <summary><c>+0x9e</c>, which no flyer function writes, so 0 as the pool's slot leaves it.</summary>
	public bool Immobilized => false;

	/// <summary><c>+0x9f</c>, which no flyer function writes.</summary>
	public bool Disarmed => false;

	/// <summary><c>+0x96</c>, which no flyer function writes.</summary>
	public bool EcmSpoofed => false;

	/// <summary><c>+0x9b</c>, which no flyer function writes.</summary>
	public bool Jamming => false;

	/// <summary><c>+0xae</c>, which no flyer function writes.</summary>
	public bool Collapsed => false;

	/// <summary><c>+0xa8</c>, the ground warning, which the move sets and clears.</summary>
	public bool GroundWarning { get; set; }

	/// <summary><c>+0xa9</c>, skip the next think: <see cref="Es1FlyerBehaviour.AiTick"/> skips it once, clearing it.</summary>
	public bool ThinkSuppressed { get; set; }

	/// <summary><c>+0xab</c>, something ran into it: set by <see cref="RunInto"/>.</summary>
	public bool RanInto { get; set; }

	/// <inheritdoc/>
	public bool SkipWeaponChoice { get; set; }

	/// <inheritdoc/>
	public Es1SensorTables Sensors { get; } = new();

	/// <inheritdoc/>
	public short TargetHolders { get; set; }

	/// <summary><c>+0x15e</c>, its target, counted in the target's <c>+0x15c</c>.</summary>
	public IEs1SimObject? Target { get; private set; }

	/// <summary>
	/// <paramref name="target"/> installed as the sweeps and <c>Flyer_EngageWithFlight</c> install one
	/// (docs/es1/simulation/flyers.md#the-thinks): when it is not <see cref="Target"/>, the old target's holders' count
	/// down by 1 and the new one's up by 1, and <see cref="TargetChanged"/> set; null installs none.
	/// </summary>
	public void InstallTarget(IEs1SimObject? target) {
		if (ReferenceEquals(target, Target)) {
			return;
		}

		if (Target != null) {
			Target.TargetHolders--;
		}

		Target = target;
		if (target != null) {
			target.TargetHolders++;
		}

		TargetChanged = true;
	}

	/// <summary><c>+0x162</c>, the target class, <see cref="FlyerClass"/>.</summary>
	public short TargetClass => FlyerClass;

	/// <summary><c>+0x16c</c> and <c>+0x170</c>, the two actions; the second fires when the flyer goes out of the fight.</summary>
	public (Es1Action? First, Es1Action? Second) Actions { get; set; }

	/// <summary><c>+0x16c</c>, the engagement action, the first of <see cref="Actions"/>.</summary>
	public Es1Action? EngagementAction => Actions.First;

	/// <summary><c>+0x174</c> and <c>+0x176</c>, the out-of-action (flag, operation) pair; the constructor's flag number −1.</summary>
	public (short First, short Second) Word174 { get; set; } = (-1, 0);

	/// <summary><c>+0x18a</c>, <c>+0x18c</c> and <c>+0x18e</c>: the pitch, roll and heading rates, added to the angles each tick.</summary>
	public (short Pitch, short Roll, short Heading) Rates { get; set; }

	/// <summary><c>+0x194</c>, the attack run's phase.</summary>
	public short AttackPhase { get; set; }

	/// <summary><c>+0x196</c>, the damage arrays, one component and one internal; null for a type without its <c>.DMG</c>.</summary>
	public Es1DamageHeader? Damage { get; }

	/// <summary><c>+0x1b2</c>, the formation throttle.</summary>
	public short FormationThrottle { get; set; }

	/// <summary><c>+0x1b5</c>, the counter of the refire countdown record <c>+0x1b4</c>.</summary>
	public short RefireCountdown { get; set; }

	/// <summary><c>+0x1b7</c>, the run-in latch.</summary>
	public bool RunInLatch { get; set; }

	/// <summary>
	/// <c>+0x1c7</c>, <c>+0x1c9</c> and <c>+0x1cb</c>: the velocity in the flyer's own frame, (0, the type's speed, 0) from
	/// the constructor; <c>+0x1c9</c> is the forward speed.
	/// </summary>
	public (short X, short Y, short Z) Velocity { get; set; }

	/// <summary><c>+0x1ce</c>, the <c>int32</c> counter of the timer record <c>+0x1cd</c>, the attack run's third phase's.</summary>
	public int PhaseTimer { get; set; }

	/// <summary><c>+0x1d2</c>, the ground warning's height figure.</summary>
	public short GroundWarningFigure { get; set; }

	/// <summary><c>+0x1d5</c>, the counter of the ground warning's countdown record <c>+0x1d4</c>.</summary>
	public short GroundWarningCountdown { get; set; }

	/// <inheritdoc/>
	/// <remarks><c>TSObj_GetRadiusFlyerCopy</c>'s read, the type's shape's radius.</remarks>
	public short ShapeRadius => (short)Type.ShapeRadius;

	/// <summary>Slot <c>+0x60</c>, the base's <c>SimObject_GetBodyRadiusZero</c>: 0, so a flyer blocks no move.</summary>
	public short BodyRadius => 0;

	/// <summary>Slot <c>+0x34</c>, the base's <c>SimObject_GetAimPointZero</c>: its triples cleared.</summary>
	public Vec3i AimOffset => Vec3i.Zero;

	/// <summary>
	/// Slot <c>+0x3c</c>, <c>Flyer_GetSpeed</c> (<c>0001abf0</c>): <c>Math_Q10Multiply</c>(1000, the forward speed
	/// <c>+0x1c9</c>), ES1's rounding multiply.
	/// </summary>
	public short GetSpeed() => (short)SimMath.Q10MultiplyRounded(1000, Velocity.Y);

	/// <summary>
	/// Slot <c>+0x30</c>, <c>Flyer_HandleCommandNoOp</c> (<c>0001ac28</c>): reads the command's word and answers 0. No ported
	/// caller sends a flyer a command: the view's object is a HERC here.
	/// </summary>
	public short HandleCommand(ushort command) => 0;

	/// <summary>
	/// Slot <c>+0x44</c>, <c>Flyer_GetOverallDamage</c> (<c>0001b50c</c>): <c>Component_ReadOverallDamage</c> of
	/// <c>+0x196</c> (<see cref="Es1DamageHeader.ReadOverallDamage"/>); 0 with no header.
	/// </summary>
	public short OverallDamage => Damage?.ReadOverallDamage() ?? 0;

	/// <summary>
	/// Slot <c>+0x24</c>, <c>Flyer_DirectFireHitTest</c> (<c>0001b370</c>). Unported: part F3 ports it with the damage
	/// write; until then every shot misses a flyer (0).
	/// </summary>
	public int HitTest(Es1Shot shot, Es1SimGlobals globals) => 0;

	/// <summary>Slot <c>+0x28</c>, the base's <c>SimObject_GetAimNodeTransform_None</c>: none, so a missile steers at the position.</summary>
	public Transform3? AimNodeTransform() => null;

	/// <summary>Slot <c>+0x58</c>, the base's <c>SimObject_ComponentNearestAim_None</c>: −1.</summary>
	public short ComponentNearestAim(Vec3i from, (short Pitch, short Roll, short Heading) angles) => -1;

	/// <summary>
	/// Slot <c>+0x5c</c>, the base's <c>SimObject_ComponentPositionNoOp</c>, which writes nothing into its caller's point.
	/// Its callers ask only for a component slot <c>+0x58</c> or the aim slot gave, which a flyer never does; the position
	/// here.
	/// </summary>
	public Vec3i ComponentPosition(short component) => Position;

	/// <summary>Slot <c>+0x6c</c>, <c>SimObject_SetRunIntoFlyerCopy</c>: <see cref="RanInto"/> set.</summary>
	public void RunInto() => RanInto = true;

	/// <summary>Slot <c>+0x70</c>, <c>SimObject_MissileLockState_AlwaysFlyerCopy</c>: every missile a flyer fires has lock.</summary>
	public bool MissileLockState(short subtype) => true;

	/// <summary>
	/// <c>Mech_ReportOutOfAction</c> (<c>0000d77c</c>)(flyer)
	/// (docs/es1/simulation/sim-object-base.md#the-out-of-action-report--mech_reportoutofaction-0000d77c): with a group,
	/// its report; then, with the flag number of <see cref="Word174"/> not negative, that flag written by its operation.
	/// </summary>
	public void ReportOutOfAction(Es1SimGlobals globals) {
		Group?.ReportIfAllOutOfAction(this, globals);
		if (Word174.First >= 0) {
			Es1MissionGroup.ApplyOutOfAction(Word174.First, Word174.Second, globals.MissionVars);
		}
	}
}

/// <summary>
/// <c>g_FlyerPool</c> (<c>ES1DBSIM 000af72c</c>), which the first pass makes to the live flyer count
/// (docs/es1/formats/script-dat.md#reading), and its delete queue <c>g_FlyerDeleteQueue</c> (<c>000af730</c>).
/// <see cref="Live"/> is in allocation order, the order a <c>Pool_Prev</c> walk from the end visits it.
/// </summary>
public sealed class Es1FlyerPool {
	private readonly List<Es1Flyer> _live = new();
	private readonly List<Es1Flyer> _deleteQueue = new();

	/// <summary>The flyers the pool holds, <c>Pool_Init</c>'s count.</summary>
	public int Capacity { get; set; }

	/// <summary>The flyers built, oldest first.</summary>
	public IReadOnlyList<Es1Flyer> Live => _live;

	/// <summary>
	/// <c>Flyer_Constructor</c> (<c>0001ac58</c>)(null, type): a flyer from the pool, or null when it is full
	/// (<c>Pool_Alloc</c> answering 0), then its construction (<see cref="Es1Flyer"/>).
	/// </summary>
	public Es1Flyer? Construct(Es1FlyerType type, Es1SimGlobals globals) {
		if (_live.Count >= Capacity) {
			return null;
		}

		var flyer = new Es1Flyer(type, globals.DwellJitter, globals);
		_live.Add(flyer);
		return flyer;
	}

	/// <summary>
	/// <c>Pool_Nth</c> (<c>00063a78</c>) on the pool: the <i>n</i>-th flyer built, or null when the walk ends first.
	/// </summary>
	public Es1Flyer? Nth(int n) => n >= 0 && n < _live.Count ? _live[n] : null;

	/// <summary>
	/// Slot <c>+0xc</c> with flag bit 0, <c>Flyer_Dtor</c> (<c>0001af3c</c>): the flyer queued on
	/// <c>g_FlyerDeleteQueue</c> (<c>ObjectPool_QueueForDelete</c>), as the effects' pools queue theirs. It stays in
	/// <c>LiveObjectList</c>: ES1's destructor does not take it out, where ES2's does. No ported code destroys a flyer.
	/// </summary>
	public void Destroy(Es1Flyer flyer) {
		if (_live.Remove(flyer)) {
			_deleteQueue.Add(flyer);
		}
	}

	/// <summary><c>Flyer_FlushDeletes</c> (<c>0001ac14</c>), phase 5: each queued flyer given back to the pool.</summary>
	public void FlushDeletes() => _deleteQueue.Clear();
}
