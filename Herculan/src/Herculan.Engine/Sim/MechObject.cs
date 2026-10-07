using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.Sim.Anim;

namespace Herculan.Engine.Sim;

/// <summary>
/// A HERC in the simulation. In DBSIM this is the class with the 34-slot vtable — by a wide margin
/// the most elaborate <see cref="SimObject"/> subtype, carrying shields, a 29-slot component health
/// array with its own dependency graph, a weapon-mount manager object, reactor energy bookkeeping
/// and an AI/input controller.
///
/// <para>A HERC has <b>no velocity vector</b>. Every metre of translation and every degree of
/// turn-in-place rotation comes out of the walk / run / turn animations' root-node motion; the
/// control law only picks a speed scalar, a turn rate and an animation playback rate, and the
/// animation does the moving (<see cref="AnimationThread"/>). That is why <see cref="Speed"/> below
/// is a scalar with no direction attached, and why a HERC with no <see cref="ShapeAnimation"/>
/// simply stands still. See docs/retail/simulation/mech-locomotion.md.</para>
///
/// <para>The per-type data is real: <see cref="HercSimDat"/> comes from the game's own
/// <c>dat\&lt;name&gt;.dat</c> and <see cref="Type"/> applies the load-time rescale on top, so the
/// speeds and turn rates are the machine's actual stats. <see cref="Loadout"/> is the fit the
/// mission author gave this machine.</para>
/// </summary>
public sealed partial class MechObject : SimObject {
	private readonly int _shapeRadius;
	private readonly GunLayout? _hardpoints;
	private readonly WeaponCatalog? _weapons;
	private ColliderNode[] _collision;
	private readonly ComponentDamage? _damage;
	private readonly Func<int, int>? _weaponModelCellCount;

	/// <param name="hardpoints">
	/// The chassis' own <c>gl\&lt;HERC&gt;.GL</c> hardpoint list — where each weapon physically sits,
	/// which cockpit row it owns, and which slot of <paramref name="loadout"/> it draws from. Without
	/// it the machine is fitted with nothing, because the fit alone does not say where anything goes.
	/// </param>
	/// <param name="weapons">The simulator's weapon tables — see <see cref="WeaponCatalog"/>.</param>
	/// <param name="collision">
	/// The chassis' <c>col\&lt;HERC&gt;.COL</c> hit-sphere model — every cluster of it mounted on one
	/// of the shape's animated nodes, which is what makes the hit geometry follow the walk cycle. A
	/// machine without one cannot be struck at all; see <see cref="DirectFireHitTest"/>.
	/// </param>
	/// <param name="damage">
	/// The chassis' <c>dmg\&lt;HERC&gt;.DMG</c> component health, sized to a mech's 29 components and
	/// 22 dependents. Without it a struck component has nowhere to record the hit and the machine is
	/// likewise untouchable.
	/// </param>
	/// <param name="weaponModelCellCount">
	/// How long a fitted weapon's muzzle-flash flipbook is, by its <c>dts\MECHWPNS.DTS</c> shape
	/// index — the one thing the mounts need from the model library, and what sets the length of
	/// both the flash and the ELF's spin-up. See <see cref="WeaponMount.FlashCell"/>.
	/// </param>
	/// <param name="flightModel">
	/// The chassis' <c>fm\&lt;NAME&gt;.FM</c>, which only a flyer has and which
	/// <c>MechType_InitOne</c> loads only for a type whose record sets
	/// <see cref="MechTypeRecord.IsFlyer"/>. Non-null is what puts the machine on the flight path —
	/// see <see cref="Flight"/>.
	/// </param>
	public MechObject(string name, HercSimDat simData, int shapeRadius, MechLoadout loadout,
			ShapeAnimation? animation = null, GunLayout? hardpoints = null,
			WeaponCatalog? weapons = null, ColliderNode[]? collision = null,
			ComponentDamage? damage = null, Func<int, int>? weaponModelCellCount = null,
			FlightModel? flightModel = null) {
		Name = name;
		SimData = simData;
		Type = new MechTypeRecord(simData);
		_shapeRadius = shapeRadius;
		Loadout = loadout;
		_hardpoints = hardpoints;
		_weapons = weapons;
		_collision = collision ?? Array.Empty<ColliderNode>();
		_damage = damage;
		_weaponModelCellCount = weaponModelCellCount;

		if (Type.IsFlyer && flightModel != null) {
			// Mech_Constructor's own flyer seed: airborne at 1000, with the throttle field the
			// flight model owns starting where the machine's own throttle is.
			Flight = new FlightModelRecord(flightModel);
			FlightVelocity = new Vec3i(0, InitialAirSpeed, 0);
			FlightThrottle = Throttle;
		}

		// A HERC powers up in its stop / step-off sequence, not its walk cycle — the mech constructor
		// builds this thread with typeRec+0x12 and a rate of zero. It matters: the gait state
		// machine only enters the turn-in-place cycle from a stop sequence, so a machine started on
		// the walk cycle at zero speed can never begin turning.
		if (animation != null && animation.HasSequence(Type.StopForwardSequence)) {
			Animation = animation;
			Shape = new ShapeInstance(animation);
			Thread = Shape.AddThread(Type.StopForwardSequence);

			// The torso's two threads, in the constructor's own order, which breaks priority ties
			// between threads (ShapeInstance.LocalOf). Neither ever plays:
			// their rate stays zero and the torso tick seeks them by angle instead. A type record
			// with a negative sequence id gets no thread, exactly as the original skips one.
			TorsoTwistThread = AddTorsoThread(animation, Type.TorsoTwistSequence);
			TorsoPitchThread = AddTorsoThread(animation, Type.TorsoPitchSequence);
		}

		// The original splits this in two: the constructor sizes the shield array and fills the pool,
		// and the spawn path calls Mech_ConfigureLoadout straight afterwards to fit the pods and
		// resize the array around them. Both run before the machine ever ticks, so they are one call
		// here — see MechObject.Power.cs.
		ConfigureLoadout();
	}

	/// <summary>Base name of the mech's data files, e.g. <c>SAMSON</c> for <c>dat\SAMSON.DAT</c>.</summary>
	public string Name { get; }

	/// <summary>The mech type's stats, straight out of the game's own per-mech <c>.DAT</c>.</summary>
	public HercSimDat SimData { get; }

	/// <summary>The same stats with the load-time rescale applied and the fields correctly named.</summary>
	public MechTypeRecord Type { get; }

	/// <summary>The weapon fit the mission gave this machine — see the type's summary.</summary>
	public MechLoadout Loadout { get; }

	/// <summary>
	/// This machine's locomotion thread, the first of the three and the only one that ever plays.
	/// Null when it has no animation.
	/// </summary>
	public AnimationThread? Thread { get; }

	/// <summary>Whether this is the machine the player pilots. Only it slides on steep ground.</summary>
	public bool IsPlayer { get; set; }

	/// <inheritdoc />
	public override bool LocallyPiloted => IsPlayer;

	/// <inheritdoc />
	/// <remarks><c>Mech_Constructor</c> (<c>00415bb0</c>) writes 0 at <c>0x00415d35</c>.</remarks>
	public override TargetClass TargetClass => TargetClass.Herc;

	/// <inheritdoc />
	/// <remarks>
	/// <c>Mech_GetAimNodeTransform</c> (<c>00417b98</c>), the mech vtable <c>+0x24</c>: it pushes the <b>type record's <c>+0x0c</c></b>
	/// as a shape part id — <c>.DAT</c> file offset 10, <see cref="MechTypeRecord.CameraPartId"/>, the
	/// same node the cockpit eye rides — resolves it to that node's transform, and the callers put its
	/// translation through the machine's own rotation. So a HERC is aimed at from its cockpit, which
	/// walks and leans with it, and not from the ground point its model origin sits on.
	///
	/// <para>A machine with no shape falls back to the origin, which is the original's own branch for
	/// a part the shape does not have.</para>
	/// </remarks>
	public override Vec3i AimPoint {
		get {
			var node = CameraNodeTransform;
			return new Vec3i(node.X, node.Y, node.Z);
		}
	}

	/// <inheritdoc />
	/// <remarks>The mech vtable's <c>+0x30</c> (<c>004155c4</c>): <c>(0, +0x64, +0x66)</c> and
	/// <c>(0, +0x68, +0x6a)</c> of the type record.</remarks>
	public override (Vec3i Eye, Vec3i OrbitCentre) ViewMounts =>
		(new Vec3i(0, Type.EyeOffsetY, Type.EyeOffsetZ), new Vec3i(0, Type.OrbitCentreY, Type.OrbitCentreZ));

	/// <inheritdoc />
	public override Transform3? ViewNodeFrame => CameraNodeTransform;

	/// <inheritdoc />
	/// <remarks>The same node's <i>model-space</i> Z, which is the <c>+0x1c</c> the sweep reads.</remarks>
	public override int SightHeight =>
		Shape is { } shape && Animation?.TransformIdOfPart(Type.CameraPartId) is { } node && node >= 0
			? shape.NodeTransform(node).Z
			: Detection.DefaultSightHeight;

	/// <inheritdoc />
	/// <remarks>
	/// Both halves of the original's pair, which for a HERC are two different things: destroyed, or
	/// merely unable to walk. Losing the legs takes a machine off the target list.
	/// </remarks>
	public override bool Neutralised => Destroyed || Immobilised;

	/// <inheritdoc />
	public override bool OutOfAction => Neutralised || Disarmed;

	/// <inheritdoc />
	/// <remarks>Mech vtable <c>+0x3c</c> (<c>Mech_GetTorsoTwistAngle</c>, <c>00415488</c>), which returns <c>mech+0x298</c>.</remarks>
	public override short AimTwist => TorsoTwistAngle;

	/// <summary>
	/// <c>mech+0x96</c> — the radar mode: false is PASSIVE, true is ACTIVE. <b>A HERC powers up
	/// passive</b>; nothing in the original writes this field at construction and only the toggle
	/// below ever sets it for a player.
	///
	/// <para>It does two things at once, which is the trade the mode is: an active scanner lets this
	/// machine paint contacts out to <see cref="Detection.RadarTargetingRange"/> instead of relying
	/// on line of sight inside visual range, and it makes this machine visible to everything else at
	/// the same range — and it is one of the two emissions an anti-radiation missile homes on.</para>
	/// </summary>
	public bool Scanner { get; private set; }

	/// <summary>
	/// <c>Mech_ToggleRadarMode</c> (<c>0041b468</c>) — the manual's [R]. The F4 scanner screen's
	/// PASS/ACTIVE button pair is a different path, <see cref="SetScanner"/>; see
	/// docs/retail/simulation/target-selection.md, "Radar mode". <b>Only the machine the player is flying toggles</b>: the
	/// original gates the flip on <c>mech+0xa3</c> and then repaints the console lights for whatever
	/// the mode now is, so calling it on an AI machine only refreshes the display.
	///
	/// <para>The tone the flip plays is the cockpit's own confirmation, one sound for each mode —
	/// <c>gnract</c> going active and <c>gnrdact</c> going passive. It is not positional: the
	/// original plays it through <c>Sound_Play</c>, because it is a noise the cockpit makes rather
	/// than one the world does.</para>
	///
	/// <para>The tone is only half of it. The flip also posts the computer message that says the new
	/// mode aloud, and withdraws <b>both</b> of them first — so flipping twice in quick succession
	/// leaves the machine announcing where it ended up rather than reading out the whole sequence.
	/// </para>
	///
	/// <para>The two console lights are not ported.</para>
	/// </summary>
	/// <param name="world">
	/// Optional, and only so the tone has somewhere to go. A call with none still flips the mode.
	/// </param>
	public void ToggleScanner(SimWorld? world = null) {
		if (!LocallyPiloted) {
			return;
		}

		Scanner = !Scanner;

		if (world?.Sounds is not { } sounds) {
			return;
		}

		sounds.Play(Scanner ? SoundId.ScannerActive : SoundId.ScannerPassive);

		sounds.Unsay(Content.SystemMessages.ActiveRadarMode);
		sounds.Unsay(Content.SystemMessages.PassiveRadarMode);
		sounds.Say(Scanner
			? Content.SystemMessages.ActiveRadarMode
			: Content.SystemMessages.PassiveRadarMode);
	}

	/// <summary>
	/// The scanner screen's PASS and ACTIVE buttons, which <b>set</b> rather than toggle:
	/// <c>MfdButton_OnClick</c> writes <c>mech+0x96</c> directly with 0 or 1 rather than going through
	/// the [R] path, so pressing PASS twice leaves the machine passive.
	/// </summary>
	public void SetScanner(bool active) {
		if (LocallyPiloted) {
			Scanner = active;
		}
	}

	/// <inheritdoc />
	public override bool ScannerActive => Scanner;

	/// <summary>
	/// <c>mech+0xa1</c> — this machine's jammer, derived once per tick by
	/// <see cref="JammerTick"/> and not settable from outside.
	/// </summary>
	public bool Jammer { get; private set; }

	/// <summary>
	/// The ECM pod row's on/off button (<c>pod+0x7d</c>), which is what the <i>player's</i> jammer
	/// follows. <see cref="PodTick"/> copies it out of the row's own button every frame, and pressing
	/// that row — by click or by its number key — is the only thing that moves it. An AI machine's
	/// jammer does not go through here at all. See <see cref="JammerTick"/>.
	/// </summary>
	public bool EcmEnabled { get; set; }

	/// <inheritdoc />
	public override bool JammerActive => Jammer;

	/// <summary>This tick's pilot input. The host writes it before the world ticks.</summary>
	public MechControls Controls { get; set; } = MechControls.Neutral;

	/// <summary>
	/// Where the pilot's eye is, in world units — the machine's own position with the pose of the
	/// node its type record names in <see cref="MechTypeRecord.CameraPartId"/> applied.
	///
	/// <para>There is no cockpit-bob code anywhere in DBSIM, and none is needed: the eye rides a
	/// model node, the walk cycle animates that node's parent, and the bob falls out.
	/// <c>Cockpit_TargetAnglesFromCameraBone</c> (<c>0041ef14</c>) reads the same node the same way to work out where a target sits relative
	/// to the pilot.</para>
	///
	/// <para>Falls back to the machine's own origin when its model names no such node, which is any
	/// shape with no animation data.</para>
	/// </summary>
	public Vec3i EyePosition {
		get {
			var eye = EyeTransform;
			return new Vec3i(eye.X, eye.Y, eye.Z);
		}
	}

	/// <summary>
	/// The node the eye rides, in world space — the camera node's own posed transform composed with
	/// the machine's, and nothing else. This is the frame <see cref="EyeTransform"/> is measured in.
	/// </summary>
	public Transform3 CameraNodeTransform => PartTransform(Type.CameraPartId);

	/// <summary>
	/// The pilot's whole frame in world space, orientation included: the camera node's frame with the
	/// type's own eye offset (<see cref="MechTypeRecord.EyeOffsetY"/>) put through it.
	/// <see cref="EyePosition"/> is its translation.
	///
	/// <para>The offset is the cockpit branch of <c>Cam_Update</c> (<c>004011a0</c>)'s own step — it takes the node's
	/// world matrix from the mech vtable's <c>+0x24</c> accessor (<c>00417b98</c>) and calls
	/// <c>Transform_ApplyToShortPoint</c> with the offset point the <c>+0x30</c> accessor
	/// (<c>004155c4</c>) built out of the type record. Without it the eye sits at the node's own
	/// origin, which on a HERC is around its waist.</para>
	///
	/// <para>The orientation is not decorative either: <c>Cockpit_TargetAnglesFromCameraBone</c>
	/// (<c>0041ef14</c>) brings a target into exactly this frame to work out where the HUD should
	/// draw it, and the view takes its whole euler triple — roll included — from it. It is also why
	/// torso twist and pitch turn the view without anything having to add them to it: the camera node
	/// hangs off the two nodes those sequences drive (see
	/// docs/retail/simulation/mech-locomotion.md's chain table).</para>
	/// </summary>
	public Transform3 EyeTransform {
		get {
			var node = CameraNodeTransform;
			var eye = node.TransformPoint(0, Type.EyeOffsetY, Type.EyeOffsetZ);
			node.X = eye.X;
			node.Y = eye.Y;
			node.Z = eye.Z;
			return node;
		}
	}

	/// <summary>
	/// Where one part of this machine's model has ended up in the world, orientation included: the
	/// part's posed node transform composed with the machine's own. The camera bone and every weapon
	/// hardpoint are both resolved this way — <c>WeaponMount_PrepareShot</c> (<c>0040e788</c>) reads
	/// the firing hardpoint's bone exactly as <c>Cockpit_TargetAnglesFromCameraBone</c> reads the pilot's.
	///
	/// <para>Falls back to the machine's own frame for a part the model does not have, which is what
	/// the original's own fallback transform amounts to.</para>
	/// </summary>
	/// <param name="partId">The model part, in the <c>.DTS</c> part id space the type record and the <c>.GL</c> hardpoint list both use.</param>
	public Transform3 PartTransform(int partId) {
		if (Shape is not { } shape || Animation is not { } animation) {
			return Rotation();
		}

		int node = animation.TransformIdOfPart(partId);
		return node < 0 ? Rotation() : Transform3.Concat(shape.NodeTransform(node), Rotation());
	}

	/// <summary>
	/// The machine's own shape-to-world transform: its lean and heading with its world position in
	/// the translation. Anything turning a point of the machine's shape into a world point goes
	/// through this, as <see cref="EyePosition"/> does.
	/// </summary>
	public Transform3 WorldTransform => Rotation();

	/// <inheritdoc />
	/// <remarks>A machine's frame is its full attitude, lean included — see <see cref="WorldTransform"/>.</remarks>
	public override Transform3 WorldFrame => WorldTransform;

	/// <summary>
	/// The machine's body radius — <see cref="MechTypeRecord.BodyRadius"/>, the flat 750 every retail
	/// HERC states. Not <see cref="MechTypeRecord.HitRadius"/>, which is the generous <i>shot</i>
	/// radius <see cref="DirectFireHitTest"/> rejects against, and not the drawn model's bound
	/// either, which is <see cref="ShapeRadius"/>.
	/// </summary>
	public override int HitRadius => Type.BodyRadius;

	/// <inheritdoc />
	/// <remarks>The same figure — the original hands both radii out of the same field.</remarks>
	public override int CollisionRadius => Type.BodyRadius;

	/// <inheritdoc />
	/// <remarks>Root 0's whichever LOD root is drawn, as <see cref="Scene.SceneModelLibrary.MechShapeRadius"/> explains. The HUD target box and the effect-light selection read it.</remarks>
	public override int ShapeRadius => _shapeRadius;

	/// <summary>
	/// This machine's per-component health, or null for a type whose <c>.DMG</c> the install is
	/// missing. Every hit past shields lands in here.
	/// </summary>
	public ComponentDamage? Damage => _damage;

	/// <inheritdoc />
	public override ShapeCellFrames? CellFrames => _damage?.CellFrames;

	// The object's rotation matrix (mech+0x12) and its dirty flag (mech+0x32). Rebuilt from the
	// euler angles on demand, and invalidated at the end of every locomotion tick.
	private Transform3 _rotation;
	private bool _rotationValid;

	/// <summary>
	/// One simulation step: the control law, then the move it implies.
	///
	/// <para>The original reaches these through two different paths — the input poll calls the
	/// control law for the player and the AI think function calls it for everyone else, while the
	/// move runs from the object list's own per-tick dispatch. Their order within a frame is the
	/// same either way, and running them back to back here is what makes a headless tick
	/// reproducible.</para>
	/// </summary>
	public override void Tick(SimWorld world) {
		// Firing comes before the power tick, which is the original's order within a frame and not an
		// arbitrary choice: Sim_PollPlayerInput runs the trigger path, and the mech list's own
		// Mech_PerTickSystemsUpdate pass follows it. That pass is what counts the refire timer down,
		// so a shot fired this tick has already lost a tick's worth of its delay by the end of it.
		FireTick(world);

		// Reactor and pool first. In the original this is a separate dispatch entirely —
		// Sim_MainTick walks the global mech list calling Mech_PerTickSystemsUpdate, while the
		// control law comes in from the input poll or the AI think — but it runs once per mech per
		// tick either way, and its inputs are last tick's, so its position within the tick is free.
		PowerTick(world);

		// The jammer, from the same original function. Before the flight branch because a turretless
		// chassis carries hardpoints and can fit the pod like anything else.
		JammerTick();

		if (Flight is { } flight) {
			// A flyer takes a different behaviour class entirely: no throttle law, no turret, and a
			// move of its own. See MechObject.Flight.cs.
			FlightTick(world, flight);
			return;
		}

		if (UnderAiControl) {
			// Mech_ApplyThrottleInput and the turret block are Sim_PollPlayerInput's, and it runs for
			// LocalPlayerMech alone. A machine in a state whose think drives the control law itself
			// reaches it from there instead — see MechObject.Navigation.cs — so running the input path
			// for it here would bleed the throttle back to zero underneath every decision the think
			// just made. Everything else keeps the pilot path, which is what Controls is for.
			if (Behaviour.State == BehaviourState.Ramming) {
				// The one state that installs a move slot of its own. See MechObject.Ramming.cs.
				RamTick(world);
			} else {
				MovementTick(world);
			}

			return;
		}

		PilotInputTick(world);
		MovementTick(world);
	}

	/// <summary>
	/// What a tick frozen by the developer keys' <c>Alt+S</c> does to the player's machine:
	/// <c>Sim_MainTick</c> (<c>0045f464</c>) still runs <c>Sim_PollPlayerInput</c> under the freeze, so
	/// the trigger, the throttle law and the turret go on, while the move, the power tick and a flyer's
	/// flight input sit behind it. So a frozen machine turns if it has speed, slews its turret and fires,
	/// and walks nowhere. See docs/retail/command-line.md's developer keys.
	/// </summary>
	internal void FrozenTick(SimWorld world) {
		FireTick(world);

		if (Flight == null && !UnderAiControl) {
			PilotInputTick(world);
		}
	}

	/// <summary><c>Sim_PollPlayerInput</c>'s throttle and turret branch, ahead of the move.</summary>
	private void PilotInputTick(SimWorld world) {
		LatchCenterBody();

		if (_centeringBody) {
			// Center Body replaces the pilot's steering and his twist axis both, and is the reason
			// the original runs the throttle and the turret from the same branch: the two commands
			// it substitutes have to be worked out together, from the same pair of errors.
			CenterBodyTick(world);
		} else {
			ApplyThrottleInput(world, Controls.Turn);
			TorsoTick(world);
		}
	}

	/// <summary>
	/// <c>Mech_GetShieldByHeading</c> (<c>004154d0</c>), the mech vtable's <c>+0x34</c>: the facing
	/// within ±90° of <paramref name="heading"/>. The damage path asks it with a real bearing; the
	/// AI's weapon choice asks it with a boolean, which is a bug in the original — see
	/// docs/retail/simulation/ai-weapons.md.
	/// </summary>
	public override short ShieldByHeading(short heading) =>
		(ushort)(heading + BinaryAngle.QuarterTurn) < BinaryAngle.HalfTurn
			? Shields.Front
			: Shields.Rear;

	/// <summary>
	/// The object's world transform, rebuilt from the euler angles when they have moved.
	///
	/// <para>In DBSIM the rotation matrix at <c>mech+0x12</c> and the world position at
	/// <c>mech+0x26</c> are one contiguous 0x20-byte transform — the position <i>is</i> the matrix's
	/// translation — which is why root motion applied through it lands in world space and why
	/// restoring the matrix after a blocked step restores the position with it.</para>
	/// </summary>
	private Transform3 Rotation() {
		if (!_rotationValid) {
			_rotation = Transform3.FromEuler(Pitch, Roll, (short)Heading);
			_rotationValid = true;
		}

		var position = Position;
		_rotation.X = position.X;
		_rotation.Y = position.Y;
		_rotation.Z = position.Z;
		return _rotation;
	}
}

/// <summary>
/// A mech's weapon fit, as the mission states it. AI machines get theirs from <c>script.dat</c>
/// block 7's own weapon array; the player's lance gets theirs from <c>player.mec</c>. Both feed the
/// same <c>Mech_ConfigureLoadout</c> in the original.
/// </summary>
/// <param name="WeaponIds">
/// The mount ids the mission assigned, <b>in slot order and with the file's holes left in</b> —
/// <c>0</c> or <c>-1</c> for an unfitted slot. The order and the holes both matter: a chassis'
/// <c>.GL</c> hardpoint list addresses this array by slot index, so closing a hole or sorting the
/// list would fit the wrong weapon to the wrong hardpoint. See <see cref="WeaponMounts.Build"/>.
/// </param>
/// <param name="SecondaryKeys">
/// The parallel second value per slot, the array DBSIM's loadout call takes alongside the first.
/// It is the ammunition type a missile launcher is loaded with — the value a launcher's mount takes
/// through <c>Proj_LookupRecord(Rocket, key)</c> and then prints as its name. Retail data puts
/// <see cref="WeaponCatalog.DefaultSecondaryKey"/> in every slot that is not a launcher. May be
/// shorter than <see cref="WeaponIds"/>, or empty, in which case the default is assumed.
/// </param>
public readonly record struct MechLoadout(
	IReadOnlyList<int> WeaponIds,
	IReadOnlyList<short> SecondaryKeys) {

	/// <summary>An empty fit, for a machine spawned outside a mission.</summary>
	public static MechLoadout None => new(Array.Empty<int>(), Array.Empty<short>());

	/// <summary>The weapon id in fit slot <paramref name="slot"/>, or 0 when the fit has no such slot.</summary>
	public int WeaponAt(int slot) => slot >= 0 && slot < WeaponIds.Count ? WeaponIds[slot] : 0;

	/// <summary>The ammunition type in fit slot <paramref name="slot"/>, defaulting to retail's own filler.</summary>
	public short SecondaryAt(int slot) =>
		slot >= 0 && slot < SecondaryKeys.Count ? SecondaryKeys[slot] : WeaponCatalog.DefaultSecondaryKey;
}
