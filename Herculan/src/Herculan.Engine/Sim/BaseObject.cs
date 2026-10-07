using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Sim.Anim;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// A structure — a base building, a turret, a bunker. In DBSIM this is the class built by
/// <c>Base_Construct</c> (<c>00405314</c>) from a <c>script.dat</c> block-9 record and attached to its group by
/// <c>Base_AttachToGroup</c> (<c>00405c3c</c>); its type comes from <c>dat\BASES.DAT</c> (see
/// <see cref="BaseType"/>), which is also what names its model, its texture bank and its
/// destructible parts.
///
/// <para>Structures are the bulk of a mission's object count and none of its motion: they sit where
/// the mission puts them. The one thing the original does that this does not is flatten the terrain
/// underneath a structure as it places it (<c>Terrain_MarkStructureFootprint</c> (<c>00470dc8</c>), called with the object's radius just
/// before the height query) — that writes to the loaded heightmap, so it belongs with terrain
/// deformation rather than here, and leaving it out means a structure on a slope stands on the
/// interpolated surface instead of a levelled pad.</para>
///
/// <para><b>They are shootable</b>, which is what <see cref="DirectFireHitTest"/> and
/// <see cref="ApplyDamage"/> are; see those for the two very different pieces of geometry that
/// answer "did this shot hit this building".</para>
/// </summary>
public sealed partial class BaseObject : SimObject {
	private readonly ShapeVolume? _volume;
	private readonly ColliderNode[] _collision;
	private readonly int _shapeRadius;
	private readonly (int BoundingRadius, ShapeVolume? Volume)? _hulk;

	// Damage taken per component, against BaseComponentType.MaxDamage -- the original's own
	// direction, counting up to the maximum rather than down from it (obj+0x205, stride 11).
	private readonly int[] _damage;

	// obj+0x201: whether each component is still standing. A destroyed one is skipped by both the
	// hit test and the damage path, so it stops absorbing fire entirely.
	private readonly bool[] _alive;

	// obj+0x205 +5 and +3: how many stages of its death sequence each component has left, and the
	// countdown to the next one. Both zero for a component that is not falling -- a live one, or one
	// that has already finished.
	private readonly short[] _deathStage;
	private readonly short[] _deathTimer;

	// obj+0x205 +7: who landed the hit that destroyed each component, which is what that component
	// hands on to the parts hanging off it (FinishDependents).
	private readonly SimObject?[] _attackers;

	/// <param name="type">The <c>BASES.DAT</c> entry this structure is an instance of.</param>
	/// <param name="volume">
	/// The shape's collision volume, or null for a type whose shape has none — every
	/// <see cref="BaseShapeSource.AnimatedLibrary"/> type, since the volume is a field of the
	/// <c>.DGS</c> record and those shapes are ordinary DTS. That costs nothing on retail data:
	/// all eight animated types set <see cref="BaseType.HasCollisionModel"/>, so none of them would
	/// reach the volume path while standing. A wreck is tested against <paramref name="hulk"/>'s
	/// volume instead.
	/// </param>
	/// <param name="collision">The type's <c>BASECOL.DAT</c> sphere model — see <see cref="CollisionModel"/>.</param>
	/// <param name="shapeRadius">
	/// The shape's own bounding radius — the original's vtable <c>+0x10</c> (<c>SimObject_GetShapeRadius</c>, <c>0046b80c</c>),
	/// which is simply <c>shape+8</c>. Both hit paths open with a coarse reject against it.
	///
	/// <para>Distinct from <see cref="HitRadius"/>, which is the <i>type</i>'s stated figure and is
	/// what the blast sweep asks for; the two disagree by up to a fifth on retail data, and four
	/// types state a type radius of zero while their shape has a real one.</para>
	///
	/// <para>Zero for an <see cref="BaseShapeSource.AnimatedLibrary"/> type, whose shape is a DTS
	/// root rather than a <c>.DGS</c> record and whose head fields this engine does not read; the
	/// type's own radius stands in there, which is close enough for a reject that only has to be
	/// generous.</para>
	/// </param>
	/// <param name="animCellCount">
	/// How many frames <see cref="BaseType.AnimCellSequence"/> holds — the modulus
	/// <see cref="ThinkTick"/> steps it round. One, the default, makes the step a no-op, which is the
	/// right answer for every type that does not animate.
	/// </param>
	/// <param name="animation">
	/// The type's animation data, or null for a type whose shape carries none. Shared per shape, as a
	/// mech type's is.
	/// </param>
	/// <param name="startingCondition">
	/// The block-9 record's starting condition, per cent — see <see cref="ApplyStartingCondition"/>.
	/// </param>
	/// <param name="hulk">
	/// The <see cref="BaseType.HulkTypeIndex"/> record of <c>dgs\BHULKS.DGS</c>: its stated bounding
	/// radius and its collision volume, which replace the building's once <see cref="ShowingHulk"/>
	/// is set — see <see cref="CurrentVolume"/>. Null for a type that leaves no wreck, and when the
	/// install has no such record.
	/// </param>
	/// <param name="keepSeekFraction">
	/// Whether the constructor's seed of <see cref="SeededThread"/> keeps its sub-tick remainder — see
	/// <see cref="AnimationThread.SeekToPosition"/>.
	/// </param>
	public BaseObject(BaseType type, ShapeVolume? volume, ColliderNode[]? collision, int shapeRadius,
			int animCellCount = 1, ShapeAnimation? animation = null, short startingCondition = 100,
			(int BoundingRadius, ShapeVolume? Volume)? hulk = null, bool keepSeekFraction = false) {
		Type = type;
		_animCellCount = animCellCount < 1 ? 1 : animCellCount;
		_animCellTimer = type.AnimCellInterval;
		_scannerActive = ScannerTypes.Contains(type.Index);

		// Base_Construct's tail, which runs for every class: one thread per sequence the type asks
		// for, from sequence 0 up, each started at its own rate out of BaseType.AnimThreadRates. A
		// count of zero -- every static-library type -- leaves the structure with no shape instance at
		// all, which is the original's arrangement too.
		if (animation != null && type.AnimThreadCount > 0) {
			Animation = animation;
			Shape = new ShapeInstance(animation);

			for (int i = 0; i < _threads.Length && i < type.AnimThreadCount; i++) {
				if (!animation.HasSequence(i)) {
					continue;
				}

				_threads[i] = Shape.AddThread(i);
				_threads[i]!.Rate = type.AnimThreadRates[i];

				// The constructor's own seed for the second thread: parked a little over a third of
				// the way through its sequence rather than at its start.
				if (i == SeededThread) {
					_threads[i]!.SeekToPosition(i, SeededThreadPosition, keepSeekFraction);
				}
			}
		}

		_volume = volume;
		_collision = collision ?? Array.Empty<ColliderNode>();
		_shapeRadius = shapeRadius != 0 ? shapeRadius : type.HitRadius;
		_hulk = hulk;
		_damage = new int[type.Components.Length];
		_deathStage = new short[type.Components.Length];
		_deathTimer = new short[type.Components.Length];
		_attackers = new SimObject?[type.Components.Length];
		_alive = new bool[type.Components.Length];
		Array.Fill(_alive, true);

		ApplyStartingCondition(startingCondition);
	}

	/// <summary>
	/// The last step of <c>Base_Construct</c> (<c>00405314</c>): the block-9 record's starting
	/// condition, a percentage applied to every component
	/// (docs/retail/simulation/structure-behaviour.md, "Starting condition"). Negative leaves the
	/// components undamaged; anything but 0 starts each at <c>(100 - percent) * maxDamage / 100</c>,
	/// kept to the original's 16-bit store.
	///
	/// <para><b>0 places the structure already fallen</b>: every component at its maximum with its
	/// sequence on <see cref="CollapsedCell"/>, <see cref="Destroyed"/> and <see cref="ShowingHulk"/>
	/// set and the scanner off, and nothing else — no mission action, no out-of-action report, no
	/// death sequence. The alive flags stay set, as the original leaves them, which is what lets a
	/// blast kill such a structure's parts a second time; see <see cref="ApplyDamage"/>.</para>
	/// </summary>
	private void ApplyStartingCondition(short percent) {
		if (percent < 0) {
			return;
		}

		for (int i = 0; i < Type.Components.Length; i++) {
			var component = Type.Components[i];
			if (percent != WreckedCondition) {
				_damage[i] = (short)((100 - percent) * component.MaxDamage / 100);
				continue;
			}

			_damage[i] = component.MaxDamage;
			if (component.DestroyedSubShape >= 0) {
				CellFrames[component.DestroyedSubShape] = CollapsedCell;
			}
		}

		if (percent == WreckedCondition) {
			_destroyed = true;
			_scannerActive = false;
			ShowingHulk = Type.HulkTypeIndex >= 0;
		}
	}

	/// <summary>The starting condition that places a structure already fallen — the original's literal 0.</summary>
	private const short WreckedCondition = 0;

	/// <summary>The <c>BASES.DAT</c> entry this structure is an instance of.</summary>
	public BaseType Type { get; }

	/// <summary>
	/// <c>obj+0x99</c> — whether every component has been destroyed and the structure has fallen. It
	/// is set by <see cref="ApplyDamage"/> the moment <see cref="DamageFraction"/> reaches full, or at
	/// spawn by a starting condition of 0 (<see cref="ApplyStartingCondition"/>). Once set, direct
	/// fire stops damaging the structure; a blast is gated by <see cref="ExplosiveDamage"/>'s own
	/// tests instead.
	/// </summary>
	public override bool Destroyed => _destroyed;

	private bool _destroyed;

	/// <summary>
	/// The wreck test that <see cref="DirectFireHitTest"/>, <see cref="ExplosiveDamage"/>,
	/// <see cref="CollisionRadius"/> and <see cref="BlocksWalker"/> all open with, which the original
	/// inlines in each (docs/retail/simulation/hit-detection.md, "Base_DirectFireHitTest"): the structure has
	/// fallen, its type leaves a wreck, and component 0 is down to its collapse stage or past it. Until
	/// then a fallen structure keeps its standing geometry.
	/// </summary>
	private bool Wrecked =>
		Destroyed && Type.HulkTypeIndex != -1
			// A type with no components has no stage to read, so this engine treats it as past its
			// collapse; the original reads component 0's record unconditionally.
			&& (_deathStage.Length == 0 || _deathStage[0] <= CollapseStage);

	/// <summary>The death-sequence stage at which a part collapses — the original's literal 1.</summary>
	private const int CollapseStage = 1;

	/// <inheritdoc />
	public override int HitRadius => Type.HitRadius;

	/// <inheritdoc />
	/// <remarks>The wreck's own radius once <see cref="ShowingHulk"/> is set — see <see cref="CurrentVolume"/>.</remarks>
	public override int ShapeRadius =>
		ShowingHulk && _hulk is { } hulk ? hulk.BoundingRadius : _shapeRadius;

	/// <summary>
	/// The collision volume the structure is tested against now: the building's, or the wreck's once
	/// <see cref="ShowingHulk"/> is set. The original's hulk swap replaces the shape pointer on the
	/// object's model instance, and every structure geometry read goes through that pointer —
	/// <c>Sim_RaycastShapeVolume</c> (<c>00427da8</c>), <c>Structure_WalkCollisionTest</c>
	/// (<c>00427c68</c>) and <c>SimObject_GetShapeRadius</c> (<c>0046b80c</c>) — so the volume and
	/// <see cref="ShapeRadius"/> change together (docs/retail/simulation/hit-detection.md, "The collision
	/// volume"). An install with no hulk record keeps the building's geometry; a retail install
	/// always has one.
	/// </summary>
	private ShapeVolume? CurrentVolume =>
		ShowingHulk && _hulk is { } hulk ? hulk.Volume : _volume;

	/// <inheritdoc />
	/// <remarks>
	/// <b>Only a standing <see cref="BaseShapeSource.AnimatedLibrary"/> type blocks by radius</b> —
	/// the original's slot tests <c>BASES.DAT +0x06</c>, the animation-thread count, which is non-zero
	/// on exactly those types. Every static type, and
	/// an animated one that is <see cref="Wrecked"/>, blocks by <see cref="BlocksWalker"/> instead. The
	/// value is the type's <see cref="BaseType.HitRadius"/>, so a structure that blocks by radius
	/// blocks at the radius it is shot at. <c>Base_GetCollisionRadius</c> (<c>004035b8</c>).
	/// </remarks>
	public override int CollisionRadius =>
		Type.Source == BaseShapeSource.AnimatedLibrary && !Wrecked
			? Type.HitRadius
			: 0;

	/// <summary>
	/// The <c>BASES.DAT</c> type indices <c>Base_Construct</c> (<c>00405314</c>) sends down its last
	/// branch, which derives a further class and writes <see cref="Sim.TargetClass.GroundVehicle"/>
	/// (<c>0x00405848</c>) where every other branch writes <see cref="Sim.TargetClass.Structure"/>.
	/// The list is the switch's own case labels.
	/// </summary>
	private static readonly HashSet<int> GroundVehicleTypes = new() {
		0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34,
		0x37, 0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d
	};

	/// <summary>
	/// The four type indices <c>Base_Construct</c> latches <c>obj+0x96</c> on for — structures that
	/// are radar masts, running an active scanner for as long as they stand. They are the only
	/// objects in a retail mission with a scanner on at spawn.
	/// </summary>
	private static readonly HashSet<int> ScannerTypes = new() { 5, 6, 0x1d, 0x1e };

	/// <summary>
	/// Which of the five structure classes <c>Base_Construct</c>'s switch gives a type index, and so
	/// which function fills its <c>+0x18</c> tick slot. The case labels are the original's own; the
	/// six indices that match no case (<c>0x0a</c>, <c>0x35</c>, <c>0x36</c>, <c>0x3e</c>-<c>0x40</c>)
	/// fall through to <see cref="StructureClass.Unclassified"/>.
	/// </summary>
	public enum StructureClass {
		/// <summary>No case matches — the original leaves its object pointer uninitialised.</summary>
		Unclassified,

		/// <summary><c>StructureVtable</c> (<c>00497940</c>), tick <c>Base_ThinkTick</c> (<c>00403ca8</c>).</summary>
		Plain,

		/// <summary><c>StructureRadarVtable</c> (<c>004979d4</c>), which keeps <see cref="Plain"/>'s tick.</summary>
		Radar,

		/// <summary><c>StructureArmedVtable</c> (<c>004978ac</c>), tick <c>00404100</c>.</summary>
		Armed,

		/// <summary>
		/// <c>StructureTransportVtable</c> (<c>00497784</c>), tick <c>Base_TransportThinkTick</c> (<c>004045c8</c>).
		/// The game's name for type <c>0x22</c>, <c>TRANSPORT</c>, is what its MFD target readout prints; the
		/// model is a landed drop pod carrying three weapon stations.
		/// </summary>
		Transport,

		/// <summary><c>StructureGroundVehicleVtable</c> (<c>00497818</c>), tick <c>0046a5d0</c>.</summary>
		GroundVehicle
	}

	/// <inheritdoc cref="StructureClass"/>
	public StructureClass Class => Classify(Type.Index);

	private static StructureClass Classify(int typeIndex) =>
		ScannerTypes.Contains(typeIndex) ? StructureClass.Radar
		: typeIndex is 8 or 0xb or 0x20 or 0x23 ? StructureClass.Armed
		: typeIndex == 0x22 ? StructureClass.Transport
		: GroundVehicleTypes.Contains(typeIndex) ? StructureClass.GroundVehicle
		: PlainTypes.Contains(typeIndex) ? StructureClass.Plain
		: StructureClass.Unclassified;

	/// <summary>The first arm of <c>Base_Construct</c>'s switch, as its own case labels.</summary>
	private static readonly HashSet<int> PlainTypes = new() {
		0, 1, 2, 3, 4, 7, 9, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16,
		0x17, 0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1f, 0x21, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29,
		0x2a, 0x2b, 0x2c
	};

	/// <inheritdoc />
	/// <remarks>
	/// Six of the 65 type indices (<c>0x0a</c>, <c>0x35</c>, <c>0x36</c> and <c>0x3e</c>-<c>0x40</c>)
	/// match no case in the original's switch, which leaves its object pointer uninitialised rather
	/// than classifying them; they are taken as ordinary structures here.
	/// </remarks>
	public override TargetClass TargetClass =>
		GroundVehicleTypes.Contains(Type.Index) ? TargetClass.GroundVehicle : TargetClass.Structure;

	/// <inheritdoc />
	/// <remarks>
	/// The structure's vtable <c>+0x30</c> (<c>0040351c</c>): the base accessor zeroes the offset
	/// triple and this one writes <see cref="BaseType.AimPointHeight"/> into its Z, which the tower
	/// ticks add to the position unrotated. So a building is aimed at a stated height up its side
	/// rather than at the ground point its model origin sits on. Homing, the HUD indicator and line
	/// of sight read the <c>+0x24</c> slot instead; this one is read by the tower ticks, the camera
	/// attach and <c>Ai_AimAndFire</c>'s fallback
	/// (docs/retail/simulation/structure-behaviour.md, "What a structure is aimed at").
	/// </remarks>
	public override Vec3i AimPoint =>
		new(Position.X, Position.Y, Position.Z + Type.AimPointHeight);

	/// <inheritdoc />
	/// <remarks>
	/// The structure's vtable <c>+0x24</c> (<c>00403548</c>) returns a node transform whose
	/// translation is <c>(0, 0, </c><see cref="BaseType.AimPointHeight"/><c>)</c>, so the sweep sights
	/// a structure from the same height it is aimed at — 1000 to 2000 units, not the 500 fallback. A
	/// turret on a rise stays in line of sight over the rise's edge because of it.
	/// </remarks>
	public override int SightHeight => Type.AimPointHeight;

	/// <inheritdoc />
	/// <remarks>The same <c>+0x30</c> accessor: its height goes into the second triple's Z.</remarks>
	public override (Vec3i Eye, Vec3i OrbitCentre) ViewMounts =>
		(Vec3i.Zero, new Vec3i(0, 0, Type.AimPointHeight));

	/// <inheritdoc />
	/// <remarks>
	/// The same <c>+0x24</c> node <see cref="SightHeight"/> reads, composed with the structure's frame
	/// as <c>Transform_Concat</c> does it. So the [Ctrl+F] attached view sits at the aim-point height
	/// looking along the structure's heading, not at its ground origin.
	/// </remarks>
	public override Transform3? ViewNodeFrame {
		get {
			var node = Transform3.Identity;
			node.Z = Type.AimPointHeight;
			return Transform3.Concat(node, WorldFrame);
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// <c>obj+0x96</c>: latched at spawn for the <see cref="ScannerTypes"/> and cleared by both writers
	/// of <see cref="Destroyed"/>, so a fallen radar mast stops transmitting.
	/// </remarks>
	public override bool ScannerActive => _scannerActive;

	private bool _scannerActive;

	/// <inheritdoc />
	public override bool Neutralised => Destroyed;

	/// <inheritdoc />
	public override bool OutOfAction => Neutralised || Disarmed;

	/// <summary>
	/// <c>obj+0xa5</c> — this structure has nothing to fight with. <c>Base_Construct</c> sets it at
	/// spawn for the <see cref="StructureClass.Plain"/> and <see cref="StructureClass.Radar"/> classes
	/// and no other, so an unarmed building is out of the AI's fight from birth
	/// (docs/retail/simulation/structure-behaviour.md, "Five classes, one switch"). The transport latches
	/// it from its own tick once it has nothing left to fire with.
	/// </summary>
	public bool Disarmed => Class is StructureClass.Plain or StructureClass.Radar || _transportDisarmed;

	/// <inheritdoc />
	public override bool Invulnerable => Type.Invulnerable;

	/// <summary>
	/// The structure's shape-to-world transform — the euler triple with its world position in the
	/// translation, which is what every draw installs (<c>SimObject_InstallModelTransform</c>,
	/// <c>00401fe4</c>). A standing structure has no lean, so for all but one class this is a Z
	/// rotation; a <see cref="StructureClass.GroundVehicle"/> carries the pitch and roll its terrain
	/// conform writes.
	/// </summary>
	public Transform3 WorldTransform => WorldFrame;

	/// <summary>
	/// Whether a walking machine is stopped by this structure's <b>collision volume</b> — the second
	/// of the two ways a building blocks movement, and the one that covers everything
	/// <see cref="CollisionRadius"/> does not. The two are exact complements: a standing animated
	/// type blocks by its radius, and every static type plus every <see cref="Wrecked"/> animated
	/// one blocks by its volume, so no structure is walked through and none is tested twice.
	///
	/// <para>The exception is a structure that is <i>gone</i>: destroyed, leaving no wreck, and built
	/// from a single component. That one stops blocking entirely.</para>
	///
	/// <para>The point is tested against the same footprint a shot meets: <see cref="ShapeVolume.HeightAt"/>
	/// applies the grid origin as the ray march does. <c>Structure_GatherWalkCandidates</c>
	/// (<c>00404ae4</c>) and <c>Structure_WalkCollisionTest</c> (<c>00427c68</c>); see
	/// docs/retail/simulation/hit-detection.md, "The collision volume".</para>
	/// </summary>
	/// <param name="point">Where the machine is trying to stand, in world units.</param>
	public bool BlocksWalker(Vec3i point) {
		if (Type.Source == BaseShapeSource.AnimatedLibrary && !Wrecked) {
			return false;
		}

		if (Type.HulkTypeIndex == -1 && Destroyed && Type.Components.Length <= 1) {
			return false;
		}

		if (CurrentVolume is not { IsSolid: true } volume
				|| SimMath.FastMagnitude2D(point.X - Position.X, point.Y - Position.Y) >= ShapeRadius) {
			return false;
		}

		// Only the heading matters, and only in 2D: the original transposes the transform's XY block
		// and rotates the offset by it rather than inverting the whole thing.
		var local = WorldTransform.Inverted().RotateVector(point.X - Position.X, point.Y - Position.Y, 0);
		return volume.HeightAt(local.X, local.Y, 0) != 0;
	}

	/// <summary>
	/// Sits the structure on the ground and runs whatever is still falling off it. Same ground
	/// treatment mechs get, and for the same reason: the mission states X and Y, and the terrain
	/// states Z.
	///
	/// <para>A ground vehicle that actually drove this tick is the exception: its own
	/// <c>ConformToTerrain</c> has already set Z, from four ground samples rather than one, and
	/// clamping it here again would undo the lean.</para>
	/// </summary>
	public override void Tick(SimWorld world) {
		if (Class == StructureClass.GroundVehicle) {
			if (GroundVehicleThinkTick(world)) {
				return;
			}
		} else if (Class == StructureClass.Armed) {
			ArmedThinkTick(world);
		} else if (Class == StructureClass.Transport) {
			TransportThinkTick(world);
		} else {
			ThinkTick(world);
		}

		Position = new Vec3i(Position.X, Position.Y,
			Sunk ? SunkDepth : world.GroundHeightAt(Position));
	}

	/// <summary>
	/// <c>Base_ThinkTick</c> (<c>00403ca8</c>) — <c>StructureVtable</c>'s <c>+0x18</c> slot, the plain
	/// building's whole per-tick behaviour, and the one of the family's four that the ordinary
	/// building and the radar mast both install.
	///
	/// <para>The death sequence first, and then — only while the structure is still standing — one
	/// step of its idle flipbook: when the countdown at <c>structure+0x1f6</c> expires it reloads from
	/// <see cref="BaseType.AnimCellInterval"/> and advances
	/// <see cref="BaseType.AnimCellSequence"/>'s cell by one, wrapping on that sequence's own frame
	/// count. This is what turns a radar dish. A type whose record states a negative sequence has no
	/// flipbook and the whole branch is skipped.</para>
	///
	/// <para>The tick's other arm is <see cref="StepAnimation"/>, run for a type that states any
	/// animation threads at all. That is what turns a radar mast's dish: the flipbook and the thread
	/// are two unrelated mechanisms, and the four radar types use the thread and state no flipbook at
	/// all.</para>
	/// </summary>
	private void ThinkTick(SimWorld world) {
		DeathSequenceTick(world);

		if (!Destroyed && Type.AnimThreadCount != 0) {
			StepAnimation();
		}

		// Only the two classes that install Base_ThinkTick free-run the flipbook. The armed class steps
		// the same cell array from its own tick, once per shot, as a muzzle flash, and the transport's
		// tick never steps it. Both hand their tick here once fallen, when the flipbook has stopped
		// anyway.
		if (Class is not (StructureClass.Plain or StructureClass.Radar)
				|| Destroyed || Type.AnimCellSequence < 0) {
			return;
		}

		if (SimMath.CountdownTimerTick(ref _animCellTimer) != 0) {
			return;
		}

		_animCellTimer = Type.AnimCellInterval;
		CellFrames[Type.AnimCellSequence] =
			(short)((CellFrames[Type.AnimCellSequence] + 1) % _animCellCount);
	}

	// structure+0x1f6 -- the idle flipbook's frame countdown, and the frame count its step wraps on.
	// The count is the shape's, so it is handed in rather than read from the type record. The armed
	// tick drives the same pair as a one-shot muzzle flash; see BaseObject.Armed.cs.
	private short _animCellTimer;
	private readonly int _animCellCount;

	/// <summary><c>base+0x1f9</c> — the structure's animation threads, at most two.</summary>
	private readonly AnimationThread?[] _threads = new AnimationThread?[MaxAnimThreads];

	/// <summary>How many threads <c>Base_Construct</c>'s loop can build — its own literal 2.</summary>
	private const int MaxAnimThreads = 2;

	/// <summary>Which thread the constructor seeds to a position rather than leaving at frame 0.</summary>
	private const int SeededThread = 1;

	/// <summary>The Q14 sequence position it is seeded to — the constructor's literal 6000.</summary>
	private const short SeededThreadPosition = 6000;

	/// <summary>
	/// <c>SimObject_ApplyRootMotionIfEnabled(this, 100)</c> (<c>00402604</c>) — step every thread on
	/// the shape by one tick's worth of animation time and apply whatever ground movement came out of
	/// the first one, exactly as <see cref="MechObject.IntegrateMotion"/> does for a HERC.
	///
	/// <para>The <i>stepping</i> is the point for a structure: it is what plays a radar dish's sweep,
	/// and it is what re-poses the turret nodes a seek has moved. <b>The root motion is inert on
	/// retail data</b> — no sequence in <c>BASES_AN.DTS</c> sets the ground-movement flag, so the
	/// delta read back is always identity (docs/retail/simulation/structure-behaviour.md, "The root motion
	/// is inert on retail data"). It is applied anyway because the original applies it, and because
	/// a hand-authored shape could carry one.</para>
	///
	/// <para>Only the translation and the heading are taken, where the original adds the delta's whole
	/// euler triple to the shared pitch/roll/heading fields. That costs nothing while the delta stays
	/// identity, which on retail data it always does — and the one class that has a pitch and a roll
	/// to add to, the ground vehicle, writes both from its terrain conform on a tick that never
	/// reaches here.</para>
	/// </summary>
	private void StepAnimation() {
		if (Shape is not { } shape) {
			return;
		}

		var root = _threads[0];
		root?.WriteRoot(Transform3.Identity);

		// Q8(SimTickDelta, 100), the same animation-time-per-sim-time constant the mech passes.
		shape.StepAnimation((short)SimMath.IntegrateRateOverTick(AnimationTimeRate));

		if (root == null) {
			return;
		}

		var motion = root.ReadRoot();
		Position = WorldFrame.RotateVector(motion.X, motion.Y, motion.Z) + Position;
		Heading = (Heading + motion.ToEuler().Z) & 0xffff;
	}

	/// <summary>Animation time per unit of simulation time — the tick's own literal 100.</summary>
	private const short AnimationTimeRate = 100;

	/// <summary>
	/// The structure's shape instance's per-sequence cell-frame array, which is what a collapsed
	/// part's geometry disappears through — see <see cref="ShapeCellFrames"/>.
	/// </summary>
	public override ShapeCellFrames CellFrames { get; } = new();
}
