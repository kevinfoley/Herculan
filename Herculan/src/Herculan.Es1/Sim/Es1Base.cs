using HercWorks.Core.Data.File.Msn.Es1;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// The table <c>Base_Construct</c> installs by the type (docs/es1/simulation/structures.md#the-classes): the plain
/// buildings' <c>StructureVtable</c> (<c>00090522</c>), the radar masts' <c>StructureRadarVtable</c> (<c>0009059e</c>) and
/// the gun tower's <c>StructureArmedVtable</c> (<c>0009061a</c>).
/// </summary>
public enum Es1BaseClass {
	Plain,
	Radar,
	Armed,
}

/// <summary>
/// A structure, the <c>0x1ba</c>-byte object of <c>g_BasePool</c>, as <c>Base_Construct</c> (<c>ES1DBSIM 00003cbc</c>)
/// builds it (docs/es1/simulation/structures.md#construction--base_construct-00003cbc) and the second pass places it
/// (docs/es1/formats/script-dat.md#the-second-pass). Its slots are <see cref="Class"/>'s table's; a slot the tables leave
/// to the object base answers as the base does (<see cref="IEs1SimObject"/>'s defaults). The fields are
/// docs/es1/simulation/structures.md#the-fields.
/// </summary>
public sealed class Es1Base : IEs1SimObject {
	/// <summary>The type tag the constructor stores at <c>+0</c>, which the distance bands' shades list is keyed by.</summary>
	public const short TypeTag = 5;

	/// <summary>The target class the constructor stores at <c>+0x162</c>, after the base's −1.</summary>
	public const short BaseClass = 1;

	/// <summary>The animation threads the constructor's loop can build, <c>+0x195</c>.</summary>
	public const int MaxThreads = 2;

	/// <summary>The thread the constructor seeks, and the Q14 position it seeks it to.</summary>
	public const int SeekedThread = 1;

	/// <inheritdoc cref="SeekedThread"/>
	public const short SeekedPosition = 6000;

	/// <summary>The time <c>Base_ThinkTick</c> hands <c>SimObject_ApplyRootMotionIfEnabled</c>, <c>PUSH 0x64</c> at <c>00003043</c>.</summary>
	public const short RootMotionTime = 100;

	/// <summary>The type numbers of the radar masts' and the gun tower's arms of the constructor's switch (<c>00090694</c>).</summary>
	public const short FirstRadarType = 5, LastRadarType = 6, ArmedType = 8;

	private readonly AnimationThread?[] _threads = new AnimationThread?[MaxThreads];

	/// <summary>
	/// <c>Base_Construct</c>'s steps on a structure the pool has given (<see cref="Es1BasePool.Construct"/>), in its order:
	/// <c>SimObjectBase_ConstructStatic</c> on <paramref name="shape"/> for the plain class, <c>SimObjectBase_ConstructAnimated</c>
	/// for the other two, so only theirs is an animated instance; the base's fields, the actions none and the flag number
	/// −1 (<see cref="Word174"/>), and <c>ObjectList_Add</c>; the class's table, with the radar <c>+0x90</c> set for a radar
	/// mast; the threads; the starting condition <paramref name="condition"/>; then the cell countdown, the type record and
	/// the record's <c>0x06</c> and <c>0x08</c>. The base's <c>+0x92</c> and <c>+0xa1</c> set to 1 are not held: nothing ported
	/// reads <c>+0x92</c>, and the scanner takes <c>+0xa1</c> as set (<see cref="Mfd.Es1MfdScannerScreen"/>). The behaviour
	/// block <c>+0x47</c> is not built (<see cref="BehaviourDescriptor"/>).
	/// </summary>
	internal Es1Base(Es1BaseType type, Es1BaseClass baseClass, Es1BaseShape shape, Es1BaseShape? hulk, Es1ScriptBase record,
			Es1SimGlobals globals) {
		Type = type;
		Class = baseClass;
		StandingShape = shape;
		Shape = shape;
		Hulk = hulk;
		CellFrames = new short[shape.Sequences?.Length ?? 0];
		if (baseClass != Es1BaseClass.Plain && shape.Animation != null) {
			Instance = new ShapeInstance(shape.Animation);
		}

		globals.AddLiveObject(this);
		RadarOn = baseClass == Es1BaseClass.Radar;

		// The threads: for i of 0 and 1 below +0x06, ShapeInst_AddThread(structure, i), the rate +0x26 + 2i, and thread 1
		// seeked to 6000, with Es1Turret.Rounding. Retail tests neither the instance nor the sequence; a sequence the shape lacks, or a static
		// instance, builds no thread here, an engine guard.
		for (int i = 0; i < MaxThreads; i++) {
			if (i >= type.ThreadCount || Instance == null || !Instance.Animation.HasSequence(i)) {
				continue;
			}

			var thread = Instance.AddThread(i);
			thread.Rate = type.ThreadRate(i);
			if (i == SeekedThread) {
				thread.SeekToPosition(i, SeekedPosition, Es1Turret.Rounding);
			}

			_threads[i] = thread;
		}

		// The starting condition, the record's 0x0e: negative undamaged; 0 the damage at the hit points, the wreck in the
		// instance and destroyed, the radar left as the class set it; otherwise (100 − condition) of the hit points.
		short condition = record.Word16;
		if (condition < 0) {
			Damage = 0;
		} else if (condition == 0) {
			Damage = type.HitPoints;
			Shape = hulk;
			Destroyed = true;
		} else {
			Damage = (short)((100 - condition) * type.HitPoints / 100);
		}

		CellCountdown = type.CellInterval;
		OutOfActionCopy = (record.Word0E, record.Word10);
	}

	/// <summary><c>+0x188</c>, the type record.</summary>
	public Es1BaseType Type { get; }

	/// <summary>The table the constructor installed.</summary>
	public Es1BaseClass Class { get; }

	/// <summary>The shape the constructor built the instance on, from the table its side picks.</summary>
	public Es1BaseShape StandingShape { get; }

	/// <summary>
	/// The instance's (<c>+0x2e</c>) shape <c>+4</c>: <see cref="StandingShape"/>, or the type's wreck once the starting
	/// condition or the collapse has put it in; null for a wreck <c>dgs\bhulks</c> lacks, which draws nothing.
	/// </summary>
	public Es1BaseShape? Shape { get; internal set; }

	/// <summary>The type's wreck, <c>BaseHulkShapes</c>[the type's <c>+0x04</c>], which the collapse puts in; null for one <c>dgs\bhulks</c> lacks.</summary>
	public Es1BaseShape? Hulk { get; }

	/// <summary>The animated instance's threads and node poses, which the radar masts and the gun tower have; null for a static one.</summary>
	public ShapeInstance? Instance { get; }

	/// <summary>The instance's cell array <c>+8</c>, a cell per sequence of <see cref="StandingShape"/>.</summary>
	public short[] CellFrames { get; }

	/// <summary>The cell of <paramref name="sequence"/>, 0 past the array.</summary>
	public short CellFrame(int sequence) => sequence >= 0 && sequence < CellFrames.Length ? CellFrames[sequence] : (short)0;

	/// <summary><c>+0x195</c> and <c>+0x199</c>, the two animation threads, null where none was built.</summary>
	public IReadOnlyList<AnimationThread?> Threads => _threads;

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

	/// <summary>The behaviour block <c>+0x47</c>, which the constructor does not build, so no descriptor.</summary>
	public Es1BehaviourDescriptor? BehaviourDescriptor => null;

	/// <inheritdoc/>
	public bool RadarVisible { get; set; }

	/// <summary><c>+0x90</c>, the radar, which the constructor sets for a radar mast.</summary>
	public bool RadarOn { get; set; }

	/// <summary><c>+0x93</c>, destroyed.</summary>
	public bool Destroyed { get; set; }

	/// <inheritdoc/>
	public bool Engaged { get; set; }

	/// <inheritdoc/>
	public bool EngagementGate { get; set; }

	/// <summary><c>+0x9d</c>: no structure is the player's machine.</summary>
	public bool IsPlayer => false;

	/// <summary><c>+0x9e</c>, which no structure function writes.</summary>
	public bool Immobilized => false;

	/// <summary><c>+0x9f</c>, which ES1 sets on no structure (docs/es1/simulation/structures.md#construction--base_construct-00003cbc).</summary>
	public bool Disarmed => false;

	/// <summary><c>+0x96</c>, which no structure function writes.</summary>
	public bool EcmSpoofed => false;

	/// <summary><c>+0x9b</c>, which no structure function writes.</summary>
	public bool Jamming => false;

	/// <summary><c>+0xae</c>, which no structure function writes.</summary>
	public bool Collapsed => false;

	/// <summary><c>+0xab</c>, something ran into it: set by <see cref="RunInto"/>.</summary>
	public bool RanInto { get; set; }

	/// <inheritdoc/>
	public bool SkipWeaponChoice { get; set; }

	/// <inheritdoc/>
	public Es1SensorTables Sensors { get; } = new();

	/// <inheritdoc/>
	public short TargetHolders { get; set; }

	/// <summary><c>+0x15e</c>, which no structure function writes: the gun tower keeps its target apart.</summary>
	public IEs1SimObject? Target => null;

	/// <summary><c>+0x162</c>, the target class, <see cref="BaseClass"/>.</summary>
	public short TargetClass => BaseClass;

	/// <summary><c>+0x16c</c> and <c>+0x170</c>, the two actions; the second fires when the structure is destroyed.</summary>
	public (Es1Action? First, Es1Action? Second) Actions { get; set; }

	/// <summary><c>+0x16c</c>, the engagement action, the first of <see cref="Actions"/>.</summary>
	public Es1Action? EngagementAction => Actions.First;

	/// <summary><c>+0x174</c> and <c>+0x176</c>, the out-of-action (flag, operation) pair; the constructor's flag number −1.</summary>
	public (short First, short Second) Word174 { get; set; } = (-1, 0);

	/// <summary><c>+0x18c</c>, the damage, one total.</summary>
	public short Damage { get; set; }

	/// <summary>
	/// <c>+0x18e</c> and <c>+0x190</c>, the record's <c>0x06</c> and <c>0x08</c>, the out-of-action pair again, which no
	/// ported code reads (docs/es1/simulation/structures.md#open).
	/// </summary>
	public (short First, short Second) OutOfActionCopy { get; }

	/// <summary><c>+0x193</c>, the counter of the cell sequence's countdown record <c>+0x192</c>.</summary>
	public short CellCountdown;

	/// <summary><c>+0x19e</c>, the counter of the collapse's countdown record <c>+0x19d</c>.</summary>
	public short CollapseCountdown;

	/// <summary><c>+0x1a0</c>, the collapse count, which the constructor zeroes and the damage sets.</summary>
	public short CollapseCount { get; set; }

	/// <summary><c>+0x1a2</c> and <c>+0x1a4</c>, the turret's elevation and traverse rates, which only the gun tower's turret moves.</summary>
	public short[] TurretRate { get; } = new short[2];

	/// <summary><c>+0x1a6</c> and <c>+0x1a8</c>, the turret's elevation and traverse, which only the gun tower's turret moves.</summary>
	public short[] TurretAngle { get; } = new short[2];

	/// <summary>The turret's elevation and traverse, <see cref="TurretAngle"/>.</summary>
	public (short Elevation, short Traverse) TurretAngles => (TurretAngle[0], TurretAngle[1]);

	/// <summary><c>+0x1ab</c>, the counter of the gun tower's refire countdown record <c>+0x1aa</c>.</summary>
	public short RefireCountdown;

	/// <summary><c>+0x1ad</c>, the gun tower's target, which it keeps apart from <see cref="Target"/>.</summary>
	public IEs1SimObject? TowerTarget { get; set; }

	/// <summary><c>+0x1b2</c>, the counter of the gun tower's firing window's countdown record <c>+0x1b1</c>.</summary>
	public short FiringWindowCountdown;

	/// <summary><c>+0x1b4</c>, the gun tower's firing window's flag: set while it may fire.</summary>
	public bool FiringWindowOpen { get; set; }

	/// <summary><c>+0x1b7</c>, the counter of the gun tower's retarget countdown record <c>+0x1b6</c>.</summary>
	public short RetargetCountdown;

	/// <inheritdoc/>
	/// <remarks><c>TSObj_GetRadiusBaseCopy</c>'s read, the radius of the shape the instance holds.</remarks>
	public short ShapeRadius => Shape?.Radius ?? 0;

	/// <summary>
	/// Slot <c>+0x60</c>, <c>Base_GetBodyRadius</c> (<c>00002dd4</c>): the type's <c>+0x30</c> when it has a collision model,
	/// otherwise 0.
	/// </summary>
	public short BodyRadius => Type.HasCollisionModel ? Type.BodyRadius : (short)0;

	/// <summary>
	/// Slot <c>+0x34</c>, <c>Base_GetAimPoint</c> (<c>00002d3c</c>): the base's triples cleared, then the offset's Z the
	/// type's <c>+0x32</c>.
	/// </summary>
	public Vec3i AimOffset => new(0, 0, Type.AimHeight);

	/// <summary>
	/// Slot <c>+0x40</c>: the gun tower's <c>Base_GetTurretAngle</c> (<c>00002dc0</c>), the traverse <c>+0x1a8</c>; the
	/// base's zero stub in the other two tables.
	/// </summary>
	public short TorsoTwistAngle => Class == Es1BaseClass.Armed ? TurretAngles.Traverse : (short)0;

	/// <summary>
	/// Slot <c>+0x44</c>, <c>Base_DamageFraction</c> (<c>00003c84</c>): (<c>+0x18c</c> &lt;&lt; 8) / the hit points, a signed
	/// divide. A type with 0 hit points, which retail divides by, answers 0 here, an engine guard.
	/// </summary>
	public short OverallDamage => Type.HitPoints != 0 ? (short)((Damage << 8) / Type.HitPoints) : (short)0;

	/// <summary>Slot <c>+0x48</c>, <c>Base_BearingInSensorArc_Always</c> (<c>00003cb4</c>): every bearing.</summary>
	public bool BearingInSensorArc(short bearing) => true;

	/// <summary>Slot <c>+0x24</c>, <c>Base_DirectFireHitTest</c> (<c>00003a58</c>): <see cref="Es1BaseDamage.HitTest"/>.</summary>
	public int HitTest(Es1Shot shot, Es1SimGlobals globals) => Es1BaseDamage.HitTest(this, shot, globals);

	/// <summary>
	/// The posed node of part <paramref name="partId"/>, found through slot <c>+0x24</c> of the shape the instance
	/// <c>+0x2e</c> holds and the instance's node array <c>+0x16</c>; null for a static instance or a shape with no such
	/// part. Its callers ask it only of a standing structure; once the wreck is in it answers null.
	/// </summary>
	public Transform3? PartNodeTransform(int partId) {
		if (Instance == null || !ReferenceEquals(Shape, StandingShape)) {
			return null;
		}

		int transformId = Instance.Animation.TransformIdOfPart(partId);
		return transformId >= 0 ? Instance.NodeTransform(transformId) : null;
	}

	/// <summary>
	/// <c>Mech_ReportOutOfAction</c> (<c>0000d77c</c>)(structure)
	/// (docs/es1/simulation/sim-object-base.md#the-out-of-action-report--mech_reportoutofaction-0000d77c): with a group,
	/// its report; then, with the flag number of <see cref="Word174"/> not negative, that flag written by its operation.
	/// </summary>
	public void ReportOutOfAction(Es1SimGlobals globals) {
		Group?.ReportIfAllOutOfAction(this, globals);
		if (Word174.First >= 0) {
			Es1MissionGroup.ApplyOutOfAction(Word174.First, Word174.Second, globals.MissionVars);
		}
	}

	/// <summary>
	/// Slot <c>+0x28</c>, <c>Base_GetAimNodeTransform</c> (<c>00002d68</c>): a zero transform with its diagonal <c>0x4000</c>
	/// and its translation's Z the type's <c>+0x32</c>.
	/// </summary>
	public Transform3? AimNodeTransform() {
		var transform = Transform3.Identity;
		transform.Z = Type.AimHeight;
		return transform;
	}

	/// <summary>Slot <c>+0x58</c>, the base's <c>SimObject_ComponentNearestAim_None</c>: −1.</summary>
	public short ComponentNearestAim(Vec3i from, (short Pitch, short Roll, short Heading) angles) => -1;

	/// <summary>
	/// Slot <c>+0x5c</c>, the base's <c>SimObject_ComponentPositionNoOp</c>, which writes nothing into its caller's point.
	/// Its callers ask only for a component slot <c>+0x58</c> or the aim slot gave, which a structure never does; the
	/// position here.
	/// </summary>
	public Vec3i ComponentPosition(short component) => Position;

	/// <summary>Slot <c>+0x6c</c>, <c>SimObject_SetRunIntoBaseCopy</c>: <see cref="RanInto"/> set.</summary>
	public void RunInto() => RanInto = true;

	/// <summary>Slot <c>+0x70</c>, <c>SimObject_MissileLockState_AlwaysBaseCopy</c>.</summary>
	public bool MissileLockState(short subtype) => true;

	/// <summary>
	/// The structure as the shape tests read it (docs/es1/simulation/terrain.md#the-structures-tests): its frame and the
	/// radius and volume of the shape its instance holds.
	/// </summary>
	public Es1ShapeObject ShapeObject => new(Frame, ShapeRadius, Shape?.Volume);

	/// <summary>A wreck to the structures' ray gather and the hit test: destroyed, with the collapse count at 0.</summary>
	public bool IsWreck => Destroyed && CollapseCount == 0;

	/// <summary>
	/// Slot <c>+0x1c</c>: <c>Base_ThinkTick</c> for the plain and radar tables, and the gun tower's
	/// <c>Base_ArmedThinkTick</c> (<c>00003364</c>, <see cref="Es1BaseTower.ArmedThinkTick"/>), which hands a destroyed
	/// tower's whole tick to <c>Base_ThinkTick</c>.
	/// </summary>
	public void Tick(Es1SimGlobals globals) {
		if (Class == Es1BaseClass.Armed && !Destroyed) {
			Es1BaseTower.ArmedThinkTick(this, globals);
		} else {
			ThinkTick(globals);
		}
	}

	/// <summary>
	/// <c>Base_ThinkTick</c> (<c>00002df8</c>) (docs/es1/simulation/structures.md#the-plain-tick--base_thinktick-00002df8):
	/// the collapse (<see cref="Es1BaseDamage.CollapseStep"/>) while <see cref="CollapseCount"/> is not 0; then, while
	/// standing, the cell sequence <c>+0x2a</c> when it
	/// is not negative, on each expiry of the countdown reloaded from <c>+0x2c</c> and its cell stepped to (cell + 1)
	/// modulo the sequence's frame count; and the threads when <c>+0x06</c> is not 0
	/// (<see cref="ApplyRootMotionIfEnabled"/> with <see cref="RootMotionTime"/>). A sequence the shape gives no frames,
	/// which retail divides by, leaves the cell, an engine guard.
	/// </summary>
	private void ThinkTick(Es1SimGlobals globals) {
		if (CollapseCount != 0) {
			Es1BaseDamage.CollapseStep(this, globals);
		}

		if (Destroyed) {
			return;
		}

		short sequence = Type.CellSequence;
		if (sequence >= 0 && globals.Clock.CountdownTimerTick(ref CellCountdown) == 0) {
			CellCountdown = Type.CellInterval;
			int frames = StandingShape.FrameCount(sequence);
			if (sequence < CellFrames.Length && frames != 0) {
				CellFrames[sequence] = (short)(((ushort)CellFrames[sequence] + 1) % frames);
			}
		}

		if (Type.ThreadCount != 0) {
			ApplyRootMotionIfEnabled(RootMotionTime);
		}
	}

	/// <summary>
	/// <c>SimObject_ApplyRootMotionIfEnabled</c> (<c>0000215c</c>) with the instance animated: the root seeded with the
	/// identity, the animation stepped by <paramref name="time"/>, the root's translation put through the rotation and added
	/// to the position and its angles to the object's, the rotation word cleared
	/// (docs/es1/simulation/object-classes.md#animation-threads-and-root-motion). The root is the first thread's.
	/// </summary>
	internal void ApplyRootMotionIfEnabled(short time) {
		if (Instance == null) {
			return;
		}

		var root = _threads[0];
		root?.WriteRoot(Transform3.Identity);
		Instance.StepAnimation(time);
		if (root == null) {
			return;
		}

		var motion = root.ReadRoot();
		Position += Frame.RotateVector(motion.X, motion.Y, motion.Z);
		var (x, y, z) = motion.ToEuler();
		Rotation = ((short)(Rotation.X + x), (short)(Rotation.Y + y), (short)(Rotation.Z + z));
		RotationWord = 0;
		root.WriteRoot(Transform3.Identity);
	}
}

/// <summary>
/// <c>g_BasePool</c> (<c>ES1DBSIM 000aed70</c>), which <c>Structure_InitPool</c> (<c>00004574</c>) makes to the live base
/// count, and its delete queue <c>g_BaseDeleteQueue</c> (<c>000aed74</c>)
/// (docs/es1/simulation/structures.md#loading-the-shapes--basetype_loadshape-000045a4). <see cref="Live"/> is in
/// allocation order, the order a <c>Pool_Prev</c> walk from the end visits it.
/// </summary>
public sealed class Es1BasePool {
	private readonly List<Es1Base> _live = new();
	private readonly List<Es1Base> _deleteQueue = new();

	/// <summary>The structures the pool holds, <c>Structure_InitPool</c>'s count.</summary>
	public int Capacity { get; set; }

	/// <summary>The structures built, oldest first.</summary>
	public IReadOnlyList<Es1Base> Live => _live;

	/// <summary>The tables the structures are built from; none until a world is spawned.</summary>
	public Es1BaseTypes? Types { get; set; }

	/// <summary>
	/// <c>Base_Construct</c> (<c>00003cbc</c>)(<paramref name="side"/>, <paramref name="record"/>): the type record of the
	/// record's type; the side's tables, the animated and static ones of side 0 or of the enemy; by the type, plain (0–4
	/// and 7) from the static table, radar mast (5, 6) or armed (8) from the animated one, its entry <c>+0x02</c>; then
	/// the structure (<see cref="Es1Base"/>). Null when the pool is full, where retail's radar arm writes its byte through
	/// the null; for a type above 8, which retail builds through a pointer it never sets; and for a type whose shape did
	/// not load, which retail asserts on: engine guards.
	/// </summary>
	public Es1Base? Construct(short side, Es1ScriptBase record, Es1SimGlobals globals) {
		if (_live.Count >= Capacity || Types?[record.Type] is not { } type || record.Type > Es1Base.ArmedType) {
			return null;
		}

		var baseClass = record.Type switch {
			>= Es1Base.FirstRadarType and <= Es1Base.LastRadarType => Es1BaseClass.Radar,
			Es1Base.ArmedType => Es1BaseClass.Armed,
			_ => Es1BaseClass.Plain,
		};
		if (Types.ShapeFor(type, side) is not { } shape) {
			return null;
		}

		var structure = new Es1Base(type, baseClass, shape, Types.Hulk(type.HulkIndex), record, globals);
		_live.Add(structure);
		return structure;
	}

	/// <summary>
	/// <c>Pool_Nth</c> (<c>00063a78</c>) on the pool: the <i>n</i>-th structure built, or null when the walk ends first.
	/// </summary>
	public Es1Base? Nth(int n) => n >= 0 && n < _live.Count ? _live[n] : null;

	/// <summary>
	/// Slot <c>+0xc</c> with flag bit 0, <c>Base_Dtor</c>, <c>Base_RadarDtor</c> or <c>Base_ArmedDtor</c>: the structure
	/// queued on <c>g_BaseDeleteQueue</c> (<c>ObjectPool_QueueForDelete</c>). It stays in <c>LiveObjectList</c>: ES1's
	/// destructors do not take it out, where ES2's do. No ported code runs a structure's destructor.
	/// </summary>
	public void Destroy(Es1Base structure) {
		if (_live.Remove(structure)) {
			_deleteQueue.Add(structure);
		}
	}

	/// <summary><c>Structure_FlushDeletes</c> (<c>00004590</c>), phase 5: each queued structure given back to the pool.</summary>
	public void FlushDeletes() => _deleteQueue.Clear();
}

/// <summary>
/// <c>Base_AttachToGroup</c> (<c>ES1DBSIM 000044a4</c>) and <c>Base_ApplyFormationOffset</c> (<c>00004474</c>): a structure
/// put on its group's heading and point and spread by its <c>BFORMS.DAT</c> slot
/// (docs/es1/simulation/structures.md#joining-its-group--base_attachtogroup-000044a4,
/// docs/es1/formats/formations-dat.md#where-a-member-stands).
/// </summary>
public static class Es1BaseGroups {
	/// <summary>
	/// <c>Base_AttachToGroup</c>(<paramref name="structure"/>, <paramref name="slot"/>, <paramref name="group"/>): the group
	/// and slot stored; a rotation whose first angle holds the sentinel becomes (0, 0, the group's heading) with
	/// <c>+0x2c</c> cleared; a position whose Z is negative becomes the group's point, or the route's waypoint at the
	/// cursor, spread by the formation, with no height added. A group with neither point nor waypoint leaves the position,
	/// where the original reads the waypoint of no route, an engine guard.
	/// </summary>
	public static void Attach(Es1Base structure, int slot, Es1MissionGroup group, HercWorks.Core.Data.File.Dat.Sim.Es1BaseFormations formations) {
		structure.Group = group;
		structure.MemberSlot = slot;
		if (structure.Rotation.X == Es1Mech.RotationSentinel) {
			structure.Rotation = (0, 0, group.Heading);
			structure.RotationWord = 0;
		}

		if (structure.Position.Z < 0 && (group.Point ?? group.WaypointAtCursor) is { } point) {
			structure.Position = ApplyOffset(structure, point, formations);
		}
	}

	/// <summary>
	/// <c>Base_ApplyFormationOffset</c>(<paramref name="structure"/>, <paramref name="position"/>): nothing for member slot
	/// 0; otherwise <c>BaseFormation_GetSlotOffset</c> (<c>0000441c</c>)(structure, slot, the group's formation), the two
	/// <c>int32</c> at <c>000bc190</c> + formation × <c>0x38</c> + slot × 8 − 8 with a Z of 0, through
	/// <see cref="Es1Formation.RotateAndAddOffset"/>. Neither number is checked, so a slot counts on through the table; an
	/// index outside it, which retail reads past the array, takes no offset here, an engine guard.
	/// </summary>
	public static Vec3i ApplyOffset(Es1Base structure, Vec3i position, HercWorks.Core.Data.File.Dat.Sim.Es1BaseFormations formations) {
		if (structure.MemberSlot == 0 || structure.Group is not { } group) {
			return position;
		}

		const int slots = HercWorks.Core.Data.File.Dat.Sim.Es1BaseFormation.SlotCount;
		int index = group.Formation * slots + structure.MemberSlot - 1;
		int formation = index / slots;
		if (index < 0 || formation >= formations.Formations.Length || index % slots >= formations.Formations[formation].Slots.Length) {
			return position;
		}

		var offset = formations.Formations[formation].Slots[index % slots];
		return Es1Formation.RotateAndAddOffset(structure, group, position, new Vec3i(offset.X, offset.Y, 0));
	}
}
