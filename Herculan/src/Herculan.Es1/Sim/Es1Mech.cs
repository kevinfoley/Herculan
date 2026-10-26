using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// A HERC's shadow, a <c>FlatObj</c> of the flat shape set drawn flat at height 0 (docs/es1/simulation/mech.md#the-shadows).
/// </summary>
/// <param name="Root">The flat set's root the chassis list's byte names: 0 under a foot, 2 under the body.</param>
/// <param name="PartId">The part it follows, the byte five on in the list (file offset 117 on).</param>
public sealed class Es1Shadow(int root, int partId) {
	/// <summary>The depth it is built at, out of the draw's range until it is first placed.</summary>
	public const int BuildDepth = -100000;

	public int Root { get; } = root;

	public int PartId { get; } = partId;

	/// <summary><c>+0x20</c>, the position; its Z is set to 0 at every draw (<c>FlatObj_DrawMechCopy</c>, <c>00011988</c>).</summary>
	public Vec3i Position { get; set; } = new(0, 0, BuildDepth);

	/// <summary><c>+0x0a</c>, the heading it takes from the machine.</summary>
	public short Heading { get; set; }

	/// <summary>The frame <c>FlatObj_DrawMechCopy</c> draws it at: its heading, at its position with Z 0.</summary>
	public Transform3 DrawFrame {
		get {
			var frame = Transform3.FromEuler(0, 0, Heading);
			frame.X = Position.X;
			frame.Y = Position.Y;
			frame.Z = 0;
			return frame;
		}
	}
}

/// <summary>
/// A HERC, the <c>0x24f</c>-byte machine of <c>g_MechPool</c>, as <c>Mech_Constructor</c> (<c>ES1DBSIM 00011330</c>) builds
/// it (docs/es1/simulation/mech.md#construction) and the second pass places it (docs/es1/formats/script-dat.md#the-second-pass).
/// The fields are the ones this step reads and writes; the rest of the object comes with the steps that use it.
/// </summary>
public sealed class Es1Mech : IEs1SimObject {
	/// <summary>The rotation sentinel's first angle, which <c>Mech_AttachToGroup</c> tests at <c>+6</c>.</summary>
	public const short RotationSentinel = -32768;

	/// <summary>The type tag <c>Mech_Constructor</c> stores at <c>+0</c>, which the distance bands' shades list is keyed by.</summary>
	public const short TypeTag = 7;

	/// <summary>The energy pool the constructor fills, <c>+0x228</c>.</summary>
	public const short InitialEnergy = 10000;

	/// <summary>The footfall latches <c>+0x24a</c>–<c>+0x24e</c>.</summary>
	public const int FootfallLatchCount = 5;

	/// <summary>
	/// <c>Mech_Constructor</c>: steps 1, 5, 7, 8, 9 and 11, with the others' call sites kept. <paramref name="type"/> is the
	/// chassis' type record; <paramref name="jitter"/> is <c>Behaviour_DwellJitter</c>, which step 9 steps.
	/// </summary>
	public Es1Mech(Es1MechType type, Es1DwellJitter jitter) {
		Type = type;

		// Step 1: SimObjectBase_ConstructWithDetailTable on the record's roots, an animated instance on root 0.
		Shape = type.Animation != null ? new ShapeInstance(type.Animation) : null;

		// Step 3. PLACEHOLDER: Component_AllocDamageArrays (00009b8c) with 21 internals and 28 components; step 7 ports
		// the damage header.
		// Step 4. PLACEHOLDER: the five lock countdowns at +0x1ee; step 7 ports missile lock.
		// Step 5: the throttle, speed, turn and turret rates and angles start at 0.
		Energy = InitialEnergy;

		// Step 6. PLACEHOLDER: Shield_Init (0000fabc) on the record's shield capacity; step 7 ports the shields.

		// Step 7: the locomotion thread on the record's +0x12 (file offset 16, the forward stop sequence) with no test of
		// the sequence; the priority (word +6) of the two stop sequences, +0x12 and +0x14, zeroed in the shape's own
		// sequence table, so for every machine of the shape; the twist and pitch threads for a sequence that is not
		// negative.
		if (Shape != null) {
			LocomotionThread = Shape.AddThread(type.Chassis.AnimId_StopMove);
			Shape.Animation.Sequences[type.Chassis.AnimId_StopMove].ClearPriority();
			Shape.Animation.Sequences[type.Chassis.AnimId_StopReverse].ClearPriority();
			TwistThread = type.Chassis.AnimId_TorsoTwist >= 0 ? Shape.AddThread(type.Chassis.AnimId_TorsoTwist) : null;
			PitchThread = type.Chassis.AnimId_TorsoPitch >= 0 ? Shape.AddThread(type.Chassis.AnimId_TorsoPitch) : null;
		}

		WorthNoSalvage = type.Chassis.SalvageScale == 0;

		// Step 8: one shadow per kind byte at +0x72 (file offset 112) up to the first negative one, its part five on.
		var bytes = type.Chassis.ShadowBytes;
		for (int i = 0; i < 5 && (sbyte)bytes[i] >= 0; i++) {
			Shadows.Add(new Es1Shadow((sbyte)bytes[i], (sbyte)bytes[i + 5]));
		}

		// Step 9: deciding, or player when +0x9d is set. The second pass sets +0x9d after the constructor, so every machine
		// is built deciding, whose reassess installs player for the player's machine (Es1MechBehaviour).
		Behaviour.SetState(Es1MechBehaviour.States[IsPlayer ? Es1MechBehaviour.Player : Es1MechBehaviour.Deciding], jitter);
		// Step 10. PLACEHOLDER: dmg\<name> and col\<name> loaded and bound (Damage_LoadMechDmgFile, Collision_RegisterObject,
		// Component_LinkMaxRefData); step 4 (collision) and step 7 (damage) port them.
		// Step 11: the latches +0x24a to +0x24d clear and the death sequence's +0x24e set.
		FootfallLatches[FootfallLatchCount - 1] = true;

		Locomotion = new Es1Locomotion(this);
		Turret = new Es1Turret(this);
	}

	/// <summary>The machine's throttle, control law and root-motion step.</summary>
	public Es1Locomotion Locomotion { get; }

	/// <summary>The machine's turret ticks and centring.</summary>
	public Es1Turret Turret { get; }

	/// <summary><c>+0x22a</c>, the twist rate.</summary>
	public short TwistRate { get; set; }

	/// <summary><c>+0x22c</c>, the pitch rate.</summary>
	public short PitchRate { get; set; }

	/// <summary><c>+0x22e</c>, the twist, which selects the position in the twist thread's sequence (docs/retail/simulation/torso-aim.md#angle-to-pose).</summary>
	public short Twist { get; set; }

	/// <summary><c>+0x230</c>, the pitch, likewise for the pitch thread.</summary>
	public short Pitch { get; set; }

	/// <summary><c>+0x226</c>, the throttle, ±<c>0x400</c> (docs/es1/simulation/mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc).</summary>
	public short Throttle { get; set; }

	/// <summary><c>+0x8d</c>, the throttle changed, so the cockpit's gauge follows.</summary>
	public bool ThrottleChanged { get; set; }

	/// <summary><c>+0x224</c>, the speed, on the chassis file's scale; the playback rate's in a steady gait.</summary>
	public short Speed { get; set; }

	/// <summary><c>+0x222</c>, the turn rate, added to the heading every tick.</summary>
	public short TurnRate { get; set; }

	/// <summary><c>+0x236</c>, the locomotion thread's playback rate, Q8.</summary>
	public short PlaybackRate { get; set; }

	/// <summary><c>+0x9e</c>, immobilized: the control law takes its inputs away and the machine falls.</summary>
	public bool Immobilized { get; set; }

	/// <summary><c>+0xa2</c>, the milder leg grade, which cuts the desired speed to Q10 750.</summary>
	public bool LegsDamaged { get; set; }

	/// <summary><c>+0xa3</c>, the severe leg grade, which cuts the desired speed to Q10 400.</summary>
	public bool LegsCrippled { get; set; }

	/// <summary>
	/// The unstick countdown, the <c>int32</c> at <c>+0x204</c> of the long countdown at <c>+0x203</c> that
	/// <c>Timer_CountDown</c> steps; while it runs the machine walks at a top speed.
	/// </summary>
	public int UnstickCountdown { get; set; }

	/// <summary><c>+0xa8</c>, the unstick direction: set walks it out backwards.</summary>
	public bool UnstickReverse { get; set; }

	/// <summary><c>+0x1e8</c>, the unstick side, ±5000.</summary>
	public short UnstickSide { get; set; }

	/// <summary><c>+0x47</c>, the behaviour block.</summary>
	public Es1BehaviourBlock Behaviour { get; } = new();

	/// <summary><c>+0xa9</c>: <c>Mech_AiTick</c> skips the think once, clearing it.</summary>
	public bool ThinkSuppressed { get; set; }

	/// <summary><c>+0xab</c>, something ran into it.</summary>
	public bool RanInto { get; set; }

	/// <summary><c>+0x24a</c>–<c>+0x24e</c>, the footfall latches, the last the death sequence's (<see cref="Es1Footfalls"/>).</summary>
	public bool[] FootfallLatches { get; } = new bool[FootfallLatchCount];

	/// <summary><c>+0x162</c>, the target class: the constructor's step 5 stores 0, a HERC.</summary>
	public short TargetClass => 0;

	/// <summary>Slot <c>+0x60</c>, <c>Mech_GetBodyRadius</c> (<c>00010ec8</c>): the record's <c>+0x70</c> (file offset 110).</summary>
	public short BodyRadius => Type.Chassis.BodyRadius;

	/// <summary>Slot <c>+0x6c</c>: <see cref="RanInto"/> set.</summary>
	public void RunInto() => RanInto = true;

	/// <summary><c>+0xae</c>, collapsed: the death sequence has played out.</summary>
	public bool Collapsed { get; set; }

	/// <summary>
	/// <c>+0x2c</c>, the word that says the rotation is built from the angles
	/// (docs/es1/simulation/object-classes.md#the-fields); the control law clears it as it turns the heading.
	/// </summary>
	public short RotationWord { get; set; }

	/// <summary><c>+0x188</c>, the type record.</summary>
	public Es1MechType Type { get; }

	/// <summary><c>+0x2e</c>, the animated shape instance; null for a shape with no animation.</summary>
	public ShapeInstance? Shape { get; }

	/// <summary>
	/// <c>+0x33</c>, set when the instance is an animated one (docs/es1/simulation/object-classes.md#the-fields): here,
	/// when <see cref="Shape"/> is built.
	/// </summary>
	public bool IsAnimated => Shape != null;

	/// <summary><c>+0x1c0</c>, <c>+0x1c4</c> and <c>+0x1c8</c>: the locomotion, twist and pitch threads.</summary>
	public AnimationThread? LocomotionThread { get; }

	/// <inheritdoc cref="LocomotionThread"/>
	public AnimationThread? TwistThread { get; }

	/// <inheritdoc cref="LocomotionThread"/>
	public AnimationThread? PitchThread { get; }

	/// <summary><c>+0x1cc</c> and <c>+0x1d0</c>, the shadows.</summary>
	public List<Es1Shadow> Shadows { get; } = new();

	/// <summary><c>+0x20</c>, the position.</summary>
	public Vec3i Position { get; set; }

	/// <summary><c>+6</c>, <c>+8</c> and <c>+0xa</c>: the Euler angles, the heading last.</summary>
	public (short X, short Y, short Z) Rotation { get; set; }

	/// <summary><c>+0x3f</c>, the group.</summary>
	public Es1MissionGroup? Group { get; set; }

	/// <summary><c>+0x43</c>, its ref's place among the group's eight.</summary>
	public int MemberSlot { get; set; }

	/// <summary><c>+0x9d</c>, the player's machine.</summary>
	public bool IsPlayer { get; set; }

	/// <summary><c>+0x90</c>, the radar, on or off; no constructor step writes it (docs/es1/simulation/mech.md#construction).</summary>
	public bool RadarOn { get; set; }

	/// <summary>
	/// <c>+0xac</c>, a squadmate's radar is kept on (docs/es1/simulation/mech-ai.md#the-fields), which the squad order's
	/// radar verbs write (<see cref="ReceiveSquadOrder"/>). PLACEHOLDER: its reader, <c>Ai_UpdateWeaponsFree</c>
	/// (<c>000161dc</c>), is plan step 8's, so nothing reads it yet.
	/// </summary>
	public bool RadarKeptOn { get; set; }

	/// <summary>Slot <c>+0x2c</c>, the squad order's receiver, <see cref="Es1SquadOrders.Receive"/>.</summary>
	public short ReceiveSquadOrder(short reply, Es1SquadOrder order, Es1SimGlobals globals) =>
		Es1SquadOrders.Receive(this, reply, order, globals);

	/// <summary><c>+0xa</c>, the heading, the last of <see cref="Rotation"/>.</summary>
	public short Heading => Rotation.Z;

	/// <inheritdoc/>
	public short ListSlot { get; set; }

	/// <inheritdoc/>
	public bool RadarVisible { get; set; }

	/// <summary><c>+0x93</c>, destroyed. PLACEHOLDER: nothing sets it until plan step 7's damage write.</summary>
	public bool Destroyed { get; set; }

	/// <summary><c>+0x15e</c>, its own target, or null.</summary>
	public IEs1SimObject? Target { get; set; }

	/// <inheritdoc/>
	public short TargetHolders { get; set; }

	/// <summary>
	/// <c>+0x99</c>, the player's objective latch, which the player's think sets under selectors 0 and 5 and the selector-0
	/// status tests (docs/es1/simulation/mission-status.md#the-fields).
	/// </summary>
	public bool ObjectiveReached { get; set; }

	/// <summary>
	/// <c>+0x9a</c>, the data link's latch, which the player's think sets at the link's fourth step and the selector-3
	/// status tests (docs/es1/simulation/mission-status.md#the-fields).
	/// </summary>
	public bool DataLinkComplete { get; set; }

	/// <summary>
	/// <c>+0x1d2</c>, the standing squad order's verb (docs/es1/simulation/mech-ai.md#the-fields). PLACEHOLDER: its writer,
	/// the squad order's receiver <c>Mech_ReceiveSquadOrder</c> (<c>0001a1cc</c>), is plan step 8's in the verbs that
	/// write it, so it holds 0 in the slice.
	/// </summary>
	public short SquadVerb { get; set; }

	/// <summary>
	/// <c>Mech_SquadOrderLineIndex</c> (<c>00015d70</c>)(machine), a comm box's order line, an entry of
	/// <c>STRINGS0.STR</c> group 34 (docs/es1/simulation/mech.md#small-functions): the behaviour descriptor's
	/// <see cref="Es1BehaviourDescriptor.LineIndex"/>; while the machine is neither immobilized nor destroyed, that line is
	/// not 5 (<c>FLEE</c>) and the descriptor's flag 1 (<c>+9</c>) is clear, the standing order overrides it:
	/// <see cref="SquadVerb"/> 1 gives 1, 2 gives 2, 3 or 6 gives 4. With no descriptor, which the constructor always
	/// installs, line 0, an engine guard.
	/// </summary>
	public short SquadOrderLineIndex() {
		var descriptor = Behaviour.Descriptor;
		short line = descriptor?.LineIndex ?? 0;
		if (!Immobilized && !Destroyed && line != 5 && descriptor?.Flags[1] != true) {
			line = SquadVerb switch {
				1 => 1,
				2 => 2,
				3 or 6 => 4,
				_ => line,
			};
		}

		return line;
	}

	/// <summary>The readings <see cref="ReadDamageReadouts"/> fills.</summary>
	public const int DamageReadingCount = 32;

	/// <summary>
	/// <c>Mech_ReadDamageReadouts</c> (<c>0001583c</c>)(machine, buffer): the type record's <c>int16</c> <c>+0x6c</c> in
	/// entry 0 and <c>Component_FillDamageReadouts</c>' readings after it (docs/es1/simulation/damage-readouts.md).
	/// PLACEHOLDER: the damage readings are plan step 7's; until then every reading is 0, intact.
	/// </summary>
	public short[] ReadDamageReadouts() => new short[DamageReadingCount];

	/// <summary><c>+0x1d4</c>, the squad order's point. PLACEHOLDER: written with <see cref="SquadVerb"/>, plan step 8.</summary>
	public Vec3i SquadPoint { get; set; }

	/// <summary><c>+0x1dc</c>, the squad order's target, or null. PLACEHOLDER: written with <see cref="SquadVerb"/>, plan step 8.</summary>
	public IEs1SimObject? SquadTarget { get; set; }

	/// <summary><c>+0x1e0</c>, the squad order's second object, or null. PLACEHOLDER: written with <see cref="SquadVerb"/>, plan step 8.</summary>
	public IEs1SimObject? SquadSecondObject { get; set; }

	/// <summary>
	/// <c>Mech_AiGoalPosition</c> (<c>000179a0</c>): the position the machine holds, by <see cref="SquadVerb"/>: 6, the
	/// order's point; 3, the second object's position; 5, the target's; otherwise its group's
	/// <see cref="Es1MissionGroup.OrderTargetPosition"/> (docs/es1/simulation/mech-ai.md#navigation). Retail tests no
	/// pointer; where it would read through a null one, null here: an engine guard.
	/// </summary>
	public Vec3i? AiGoalPosition() => SquadVerb switch {
		6 => SquadPoint,
		3 => SquadSecondObject?.Position,
		5 => SquadTarget?.Position,
		_ => Group?.OrderTargetPosition(),
	};

	/// <summary><c>+0x95</c>, the lock lamp.</summary>
	public bool LockLamp { get; set; }

	/// <summary><c>+0x97</c>, the target just changed.</summary>
	public bool TargetChanged { get; set; }

	/// <summary>
	/// <see cref="Target"/> released, as <c>Mech_AiSelectBehaviour</c> (<c>0001892c</c>) does: with a target, its holders'
	/// count down by 1, the target null and <see cref="TargetChanged"/> set; with none, nothing.
	/// </summary>
	public void ReleaseTarget() {
		if (Target == null) {
			return;
		}

		Target.TargetHolders--;
		Target = null;
		TargetChanged = true;
	}

	/// <inheritdoc/>
	/// <remarks>Root 0's, <see cref="Es1MechType.ShapeRadius"/>, the radius the LOD choice reads.</remarks>
	public short ShapeRadius => (short)Type.ShapeRadius;

	/// <summary>
	/// Slot <c>+0x34</c>, <c>Mech_GetAimPoint</c> (<c>00010ee0</c>)(machine, orbit, eye): its first triple, the orbit's
	/// centre (0, the record's <c>+0x68</c>, <c>+0x6a</c>) (docs/es1/simulation/mech.md#small-functions).
	/// </summary>
	public Vec3i AimOffset => new(0, Type.Chassis.OrbitCentreY, Type.Chassis.OrbitCentreZ);

	/// <inheritdoc/>
	public bool Engaged { get; set; }

	/// <inheritdoc/>
	public bool EngagementGate { get; set; }

	/// <summary><c>+0x16c</c>, the engagement action, the first of <see cref="Actions"/>.</summary>
	public Es1Action? EngagementAction => Actions.First;

	/// <inheritdoc/>
	public Es1SensorTables Sensors { get; } = new();

	/// <summary>Slot <c>+0x40</c>, <c>Mech_GetTorsoTwistAngle</c> (<c>00010e34</c>): <see cref="Twist"/> (docs/es1/simulation/mech.md#small-functions).</summary>
	public short TorsoTwistAngle => Twist;

	/// <summary>Slot <c>+0x4c</c>, <see cref="Es1SensorModel.MechAiEnemySighted"/>.</summary>
	public void AiEnemySighted(IEs1SimObject sighted, Es1SimGlobals globals, Es1Mech playerMech) =>
		Es1SensorModel.MechAiEnemySighted(this, sighted, globals, playerMech);

	/// <summary>
	/// <c>Mech_ToggleRadarMode</c> (<c>000157f4</c>): for the player's machine (<c>+0x9d</c>) the radar flipped
	/// (docs/es1/simulation/mech.md#the-cockpit). PLACEHOLDER: the sound it then plays, <c>0x1b</c> with the radar off and
	/// <c>0x1a</c> with it on, for any machine; no plan step ports ES1's sound yet.
	/// </summary>
	public void ToggleRadarMode() {
		if (IsPlayer) {
			RadarOn = !RadarOn;
		}
	}

	/// <summary><c>+0x91</c>, set when the record's <c>.MSN</c> <c>0x08</c> is not 0 (docs/es1/formats/script-dat.md#open).</summary>
	public bool Word08Set { get; set; }

	/// <summary><c>+0x1e6</c>, the record's <c>.MSN</c> <c>0x0a</c> when it is not negative (docs/es1/formats/script-dat.md#open).</summary>
	public short Word1E6 { get; set; }

	/// <summary><c>+0x174</c> and <c>+0x176</c>, the record's <c>.MSN</c> <c>0x4a</c> and <c>0x4c</c>; <c>0xffff</c> for the second until set.</summary>
	public (short First, short Second) Word174 { get; set; } = (0, -1);

	/// <summary><c>+0x16c</c> and <c>+0x170</c>, the two actions; the second fires when the machine goes out of the fight.</summary>
	public (Es1Action? First, Es1Action? Second) Actions { get; set; }

	/// <summary><c>+0x232</c>, a squad machine's <c>data\player.mec</c> pilot word 1.</summary>
	public short PilotWord { get; set; }

	/// <summary><c>+0xad</c>, worth no salvage: the record's salvage scale is 0.</summary>
	public bool WorthNoSalvage { get; }

	/// <summary><c>+0x228</c>, the energy pool.</summary>
	public short Energy { get; set; }

	/// <summary><c>+0x198</c>, the mount manager.</summary>
	public Es1WeaponMounts? Mounts { get; private set; }

	/// <summary>
	/// The frame <c>Context_GetTransform</c> of <c>+6</c> builds: the rotation from the angles, at the position.
	/// </summary>
	public Transform3 Frame {
		get {
			var frame = Transform3.FromEuler(Rotation.X, Rotation.Y, Rotation.Z);
			frame.X = Position.X;
			frame.Y = Position.Y;
			frame.Z = Position.Z;
			return frame;
		}
	}

	/// <summary>
	/// <c>Mech_ConfigureLoadout</c> (<c>00012c00</c>): the manager, the local one for the player's machine; root motion
	/// with time 0; and each fitted mount's model stamped with its hardpoint's node.
	/// </summary>
	public void ConfigureLoadout(Func<int, short> fit, Func<int, short> ammunition, Es1Weapons weapons,
			Func<Weapons.WeaponMountTemplate, int, TSObject?> modelFor) {
		Mounts = Es1WeaponMounts.Build(IsPlayer, Type.GunLayout, fit, ammunition, weapons, modelFor,
			sequence => Shape!.AddThread(sequence));
		ApplyRootMotionIfEnabled(0);

		foreach (var mount in Mounts.Mounts) {
			if (mount == null) {
				continue;
			}

			// The hardpoint's node, found through the shape's slot +0x24, stamped onto the model (Shape_StampTransformId).
			if (Type.Roots.Count > 0 && MechDetailRootRemap.FindPart(Type.Roots[0], mount.Hardpoint.BoneId) is { } part) {
				mount.ModelTransformId = part.Transform;
			}

			// PLACEHOLDER: the template's spheres, piece and internal maximum put into the machine's model and table
			// (Collision_ReplaceClusters, HercPiece_ReplacePieces, HercPiece_SetInternalMaxima), the anchors bound and the
			// occupancy flags cleared; step 7 ports them with the damage tables.
		}
	}

	/// <summary>
	/// <c>SimObject_ApplyRootMotionIfEnabled</c> (<c>0000215c</c>) with the instance animated: the root seeded with the
	/// identity, the animation stepped by <paramref name="time"/>, the root's translation put through the rotation and added
	/// to the position and its angles to the object's (docs/es1/simulation/object-classes.md#animation-threads-and-root-motion).
	/// The root is the locomotion thread's, the first registered.
	/// </summary>
	public void ApplyRootMotionIfEnabled(short time) {
		if (Shape == null || LocomotionThread == null) {
			return;
		}

		LocomotionThread.WriteRoot(Transform3.Identity);
		Shape.StepAnimation(time);
		var root = LocomotionThread.ReadRoot();
		var moved = Frame.RotateVector(root.X, root.Y, root.Z);
		Position += moved;
		var (x, y, z) = root.ToEuler();
		Rotation = ((short)(Rotation.X + x), (short)(Rotation.Y + y), (short)(Rotation.Z + z));
		LocomotionThread.WriteRoot(Transform3.Identity);
	}

	/// <summary>A node's pose in the machine's own frame, by its part id; the identity for a part the shape lacks.</summary>
	public Transform3 PartTransform(int partId) {
		if (Shape == null || Type.Animation is not { } animation) {
			return Transform3.Identity;
		}

		int transformId = animation.TransformIdOfPart(partId);
		return transformId >= 0 ? Shape.NodeTransform(transformId) : Transform3.Identity;
	}
}
