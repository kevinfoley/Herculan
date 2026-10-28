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
/// <param name="pool">The pool whose place it holds until destroyed and flushed; none counts no place.</param>
public sealed class Es1Shadow(int root, int partId, Effects.Es1FlatObjPool? pool = null) {
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

	/// <summary>Set once the shadow is destroyed, after which it is not drawn.</summary>
	public bool Destroyed { get; private set; }

	/// <summary>
	/// Slot <c>+0xc</c> with 3, <c>FlatObj_DtorMechCopy</c> (<c>000119b8</c>): the shadow queued for delete on its pool, so
	/// drawn no more (docs/es1/simulation/mech.md#the-shadows).
	/// </summary>
	public void Destroy() {
		if (!Destroyed) {
			Destroyed = true;
			pool?.QueueForDelete();
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
	/// chassis' type record; <paramref name="jitter"/> is <c>Behaviour_DwellJitter</c>, which step 9 steps;
	/// <paramref name="flatObjects"/> is <c>g_FlatObjPool</c>, which step 8's shadows take their places from, none counting
	/// none.
	/// </summary>
	public Es1Mech(Es1MechType type, Es1DwellJitter jitter, Effects.Es1FlatObjPool? flatObjects = null) {
		Type = type;

		// Step 1: SimObjectBase_ConstructWithDetailTable on the record's roots, an animated instance on root 0.
		Shape = type.Animation != null ? new ShapeInstance(type.Animation) : null;
		CellFrames = new short[(type.Roots.Count > 0 ? type.Roots[0] as TSShape : null)?.SequenceList?.Length ?? 0];

		// Step 3, Component_AllocDamageArrays, is the header's construction at step 10, where its table and model load.
		// Step 4: the five lock countdowns at +0x1ee (Countdowns.Lock), 0 as the slot's zero fill leaves them.
		// Step 5: the throttle, speed, turn and turret rates and angles start at 0.
		Energy = InitialEnergy;

		// Step 6: Shield_Init on the record's +0xc0 (file offset 190), the shields' capacity.
		Shields = new Es1Shields(type.Chassis.ShieldMaxTotal);

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

		// Step 8: one shadow per kind byte at +0x72 (file offset 112) up to the first negative one, its part five on. With
		// the pool full retail writes the shadow's depth through the null it gets; here that shadow is null, an engine guard.
		var bytes = type.Chassis.ShadowBytes;
		for (int i = 0; i < 5 && (sbyte)bytes[i] >= 0; i++) {
			Shadows.Add(flatObjects?.Alloc() == false ? null : new Es1Shadow((sbyte)bytes[i], (sbyte)bytes[i + 5], flatObjects));
		}

		// Step 9: deciding, or player when +0x9d is set. The second pass sets +0x9d after the constructor, so every machine
		// is built deciding, whose reassess installs player for the player's machine (Es1MechBehaviour).
		Behaviour.SetState(Es1MechBehaviour.States[IsPlayer ? Es1MechBehaviour.Player : Es1MechBehaviour.Deciding], jitter);
		// Step 10: dmg\<name> and col\<name> bound to the damage header (Damage_LoadMechDmgFile, Collision_RegisterObject,
		// Component_LinkMaxRefData). Retail asserts on a missing file; a type without its table has no header here.
		if (type.DamageTable is { } table) {
			Damage = new Es1DamageHeader(this, table, type.CollisionModel);
		}

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

	/// <inheritdoc/>
	public Es1BehaviourDescriptor? BehaviourDescriptor => Behaviour.Descriptor;

	/// <summary>
	/// <c>+0x234</c>, the combat rating, which the systems tick recomputes while <see cref="SkipCombatRating"/> is clear
	/// (<see cref="Ai.Es1CombatRating.Compute"/>).
	/// </summary>
	public short CombatRating { get; set; }

	/// <summary><c>+0x240</c>, the fear, which the flee check writes (<see cref="Ai.Es1CombatReassess.FleeCheck"/>) and the weapon choice reads as its floor.</summary>
	public short Fear { get; set; }

	/// <summary>
	/// <c>+0x94</c>, do not take the player's selection (docs/es1/simulation/mech-ai.md#the-fields), which
	/// <see cref="Ai.Es1Targets.IsTargetable"/> reads: set by the squad order's verb 1, cleared by its verbs 0, 5 and
	/// <c>0xb</c> (<see cref="Ai.Es1SquadOrders"/>) and by taking fire (<see cref="Ai.Es1TakingFire.OnTakingFire"/>).
	/// </summary>
	public bool IgnorePlayerSelection { get; set; }

	/// <summary><c>+0x1e4</c>, the squad order's abandon threshold, written with <see cref="SquadTarget"/> (<see cref="Ai.Es1SquadOrders"/>).</summary>
	public short SquadThreshold { get; set; }

	/// <summary><c>+0xa9</c>: <c>Mech_AiTick</c> skips the think once, clearing it.</summary>
	public bool ThinkSuppressed { get; set; }

	/// <summary>
	/// <c>+0xab</c>, something ran into it: set by <see cref="RunInto"/>, read by <c>ramming</c>'s move
	/// (<see cref="Ai.Es1Ramming.RamTick"/>), never cleared.
	/// </summary>
	public bool RanInto { get; set; }

	/// <summary>
	/// <c>+0xa7</c>, the line of fire is blocked (docs/es1/simulation/mech-ai.md#skirting): set by
	/// <see cref="AiOnLineOfFireBlocked"/>, cleared by <c>skirting</c> once the line is clear
	/// (<see cref="Ai.Es1Skirting.SkirtThink"/>).
	/// </summary>
	public bool LineOfFireBlocked { get; set; }

	/// <summary>
	/// <c>+0x246</c>, the object that blocked the line of fire, which <c>skirting</c> walks round; retail stores the
	/// object's <c>+6</c>, so the think reads its position at <c>+0x1a</c> from it.
	/// </summary>
	public IEs1Placed? LineOfFireBlocker { get; set; }

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

	/// <summary>
	/// <c>+0x1cc</c> and <c>+0x1d0</c>, the shadows and their count; a slot the damage write has destroyed holds null
	/// (docs/es1/simulation/mech.md#the-damage-write--mech_componentdamagewrite-00013250).
	/// </summary>
	public List<Es1Shadow?> Shadows { get; } = new();

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
	/// radar verbs write (<see cref="ReceiveSquadOrder"/>) and <see cref="Ai.Es1Navigation.UpdateWeaponsFree"/> reads.
	/// </summary>
	public bool RadarKeptOn { get; set; }

	/// <summary><c>+0xa6</c>, a target was just handed over (docs/es1/simulation/mech-ai.md#the-fields).</summary>
	public bool TargetHandedOver { get; set; }

	/// <summary>
	/// <c>+0x238</c>, the aim slot: the component the AI's shot is aimed at, −1 for none
	/// (docs/es1/simulation/mech-ai.md#the-fields).
	/// </summary>
	public short AimSlot { get; set; }

	/// <summary>Slot <c>+0x2c</c>, the squad order's receiver, <see cref="Ai.Es1SquadOrders.Receive"/>.</summary>
	public short ReceiveSquadOrder(short reply, Es1SquadOrder order, Es1SimGlobals globals) =>
		Ai.Es1SquadOrders.Receive(this, reply, order, globals);

	/// <summary>Slot <c>+0x54</c>, taking fire, <see cref="Ai.Es1TakingFire.OnTakingFire"/>.</summary>
	public void AiOnTakingFire(short damage, IEs1SimObject attacker, Es1SimGlobals globals) =>
		Ai.Es1TakingFire.OnTakingFire(this, damage, attacker, globals);

	/// <summary>Slot <c>+0x68</c>, the line of fire blocked, <see cref="Ai.Es1Skirting.OnLineOfFireBlocked"/>.</summary>
	public void AiOnLineOfFireBlocked(IEs1Placed blocker) => Ai.Es1Skirting.OnLineOfFireBlocked(this, blocker);

	/// <summary><c>+0xa</c>, the heading, the last of <see cref="Rotation"/>.</summary>
	public short Heading => Rotation.Z;

	/// <inheritdoc/>
	public short ListSlot { get; set; }

	/// <inheritdoc/>
	public bool RadarVisible { get; set; }

	/// <summary><c>+0x93</c>, destroyed, which the damage write's death step sets (<see cref="Es1MechOutOfAction.Death"/>).</summary>
	public bool Destroyed { get; set; }

	/// <summary>
	/// <c>+0x32</c>, which the death step sets for a type whose record's <c>+0x4c</c> is set, as it sinks the machine out
	/// of the world; nothing this engine ports reads it.
	/// </summary>
	public bool Byte32Set { get; set; }

	/// <summary>
	/// <c>+0x23a</c>, the kill tallies, an <c>int16</c> per target class (<c>+0x162</c>) up to the footfall latches, which
	/// <see cref="CreditNeutralisedTarget"/> counts; nothing this engine ports reads them.
	/// </summary>
	public short[] KillTallies { get; } = new short[8];

	/// <summary>Slot <c>+0x64</c>, <see cref="Es1MechOutOfAction.CreditNeutralisedTarget"/>.</summary>
	public void CreditNeutralisedTarget(bool alreadyImmobilized, Es1Mech victim, Es1SimGlobals globals) =>
		Es1MechOutOfAction.CreditNeutralisedTarget(this, alreadyImmobilized, victim, globals);

	/// <summary>
	/// <c>Mech_ReportOutOfAction</c> (<c>0000d77c</c>)(machine)
	/// (docs/es1/simulation/sim-object-base.md#the-out-of-action-report--mech_reportoutofaction-0000d77c): with a group,
	/// its report (<see cref="Es1MissionGroup.ReportIfAllOutOfAction"/>); then, with the flag number of
	/// <see cref="Word174"/> not negative, that flag written by its operation (<see cref="Es1MissionGroup.ApplyOutOfAction"/>).
	/// </summary>
	public void ReportOutOfAction(Es1SimGlobals globals) {
		Group?.ReportIfAllOutOfAction(this, globals);
		if (Word174.First >= 0) {
			Es1MissionGroup.ApplyOutOfAction(Word174.First, Word174.Second, globals.MissionVars);
		}
	}

	/// <summary><c>+0x19c</c>, the damage header; null for a type without its <c>.DMG</c>, which retail asserts on.</summary>
	public Es1DamageHeader? Damage { get; }

	/// <summary>
	/// The array at <c>+8</c> of the shape instance <c>+0x2e</c>, the cell each <c>TSCellAnimPart</c> sequence shows, one per
	/// sequence of root 0's shape, each at 0; the damage writes it
	/// (docs/es1/simulation/component-damage.md#write-and-cascade--component_applydamageandcascade-00009f7c).
	/// </summary>
	public short[] CellFrames { get; }

	/// <summary>The cell <paramref name="sequence"/> shows; 0 past the array.</summary>
	public short CellFrame(int sequence) => sequence >= 0 && sequence < CellFrames.Length ? CellFrames[sequence] : (short)0;

	/// <summary>Sequence <paramref name="sequence"/> put on <paramref name="cell"/>; retail writes past the array unchecked, here nothing, an engine guard.</summary>
	public void SetCellFrame(int sequence, short cell) {
		if (sequence >= 0 && sequence < CellFrames.Length) {
			CellFrames[sequence] = cell;
		}
	}

	/// <summary><c>+0x8e</c>, skip the combat-rating update; a hit that reaches the shields clears it.</summary>
	public bool SkipCombatRating { get; set; }

	/// <summary><c>+0x15e</c>, its own target, or null.</summary>
	public IEs1SimObject? Target { get; set; }

	/// <summary><c>+0x96</c>, the ECM spoof, which the systems tick's roll against a jamming target sets (<see cref="Es1MechSystems"/>).</summary>
	public bool EcmSpoofed { get; set; }

	/// <summary><c>+0x9b</c>, jamming, which the systems tick derives each tick from the machine's ECM (<see cref="Es1MechSystems"/>).</summary>
	public bool Jamming { get; set; }

	/// <summary><c>+0x1b8</c>, the shields.</summary>
	public Es1Shields Shields { get; }

	/// <summary>The countdowns the systems tick runs.</summary>
	public Es1MechCountdowns Countdowns { get; } = new();

	/// <summary>
	/// <c>+0xa4</c> and <c>+0xa5</c>, the two reactor grades, which cut the reactor's rate (<see cref="Es1MechSystems"/>);
	/// the damage write sets them (<see cref="Es1MechDamage.ComponentDamageWrite"/>).
	/// </summary>
	public bool ReactorDamaged { get; set; }

	/// <inheritdoc cref="ReactorDamaged"/>
	public bool ReactorCritical { get; set; }

	/// <summary>
	/// <c>+0xaa</c>, the shields message has been posted; the MFD's status screen reads it as shields down. The systems
	/// tick clears it on the player's machine, and the hit test sets it (<see cref="Es1MechDamage.HitTest"/>).
	/// </summary>
	public bool ShieldsMessagePosted { get; set; }

	/// <summary>
	/// <c>+0x217</c>, the damage a machine of <c>PlayerMech</c>'s group has taken from the other side, which taking fire
	/// adds up (<see cref="Ai.Es1TakingFire.OnTakingFire"/>); zeroed when the under-fire window runs out and by the squad
	/// order's verbs 0, 2, 3, 8 and <c>0xb</c> (<see cref="Ai.Es1SquadOrders"/>).
	/// </summary>
	public int DamageInPlayerSquad { get; set; }

	/// <summary><c>+0x21e</c>, the damage taken, which the hit test raises; zeroed when the damage window runs out.</summary>
	public int DamageTaken { get; set; }

	/// <summary><c>+0x9f</c>, disarmed, which the AI's weapon choice sets when every mount is absent or spent (<see cref="Ai.Es1AiWeapons.ChooseWeapon"/>).</summary>
	public bool Disarmed { get; set; }

	/// <inheritdoc/>
	public bool SkipWeaponChoice { get; set; }

	/// <summary>
	/// <c>+0x242</c>, the latched mount (docs/es1/simulation/mech-ai.md#the-fields): the <c>ELF</c> the AI fired last,
	/// which it fires again while ready and in range without choosing (<see cref="Ai.Es1AiWeapons.FireAtPoint"/>); null for none.
	/// </summary>
	public Es1WeaponMount? LatchedMount { get; set; }

	/// <summary>
	/// Slot <c>+0x38</c>, <c>Mech_GetShieldByHeading</c> (<c>00010e6c</c>, docs/es1/simulation/mech.md#small-functions): the
	/// front charge for a <paramref name="bearing"/> within ±<c>0x4000</c>, the rear otherwise.
	/// </summary>
	public short GetShieldByHeading(short bearing) => (uint)(bearing + 0x4000) < 0x8000 ? Shields.Front : Shields.Rear;

	/// <summary>Slot <c>+0x3c</c>, <c>Mech_GetSpeed</c> (<c>00010e48</c>, docs/es1/simulation/mech.md#small-functions): Q10(2000, <see cref="Speed"/>).</summary>
	public short GetSpeed() => (short)SimMath.Q10MultiplyRounded(2000, Speed);

	/// <summary>Slot <c>+0x24</c>, <see cref="Es1MechDamage.HitTest"/>.</summary>
	public int HitTest(Es1Shot shot, Es1SimGlobals globals) => Es1MechDamage.HitTest(this, shot, globals);

	/// <summary>
	/// Slot <c>+0x44</c>, <c>Mech_GetOverallDamage</c> (<c>00010eb0</c>): <see cref="Es1DamageHeader.ReadOverallDamage"/>;
	/// 0 with no header.
	/// </summary>
	public short OverallDamage => Damage?.ReadOverallDamage() ?? 0;

	/// <summary>
	/// Slot <c>+0x28</c>, <c>Mech_GetAimNodeTransform</c> (<c>00013204</c>): the posed node of the type record's camera part
	/// (<c>+0xc</c>), or null when the shape has no such part.
	/// </summary>
	public Transform3? AimNodeTransform() => PartNodeTransform(Type.Chassis.CameraPartId);

	/// <summary>
	/// The posed node of part <paramref name="partId"/>, found through slot <c>+0x24</c> of the shape of the instance
	/// <c>+0x2e</c>, as <see cref="AimNodeTransform"/> and <c>WeaponMount_PrepareShot</c> find theirs; null when the shape
	/// has no such part.
	/// </summary>
	public Transform3? PartNodeTransform(int partId) {
		int transformId = Type.Animation?.TransformIdOfPart(partId) ?? -1;
		return transformId >= 0 && Shape != null ? Shape.NodeTransform(transformId) : null;
	}

	/// <summary>
	/// Slot <c>+0x58</c>, <c>Mech_ComponentNearestAim</c> (<c>00015884</c>, docs/es1/simulation/mech.md#small-functions):
	/// of the components whose occupancy flag is set, each placed by <see cref="ComponentPosition"/>, the first whose
	/// <c>Math_EulerToward</c> from <paramref name="from"/> differs least from <paramref name="angles"/>, or −1. The
	/// difference is the sum of two absolutes, −32768 counted as 32767: element 0's against the pitch and element 1's,
	/// which <c>Math_EulerToward</c> always answers 0, against the roll, so the bearing's heading is not weighed. No header
	/// answers −1.
	/// </summary>
	public short ComponentNearestAim(Vec3i from, (short Pitch, short Roll, short Heading) angles) {
		static int Abs(short angle) => angle == short.MinValue ? short.MaxValue : Math.Abs(angle);

		if (Damage is not { } header) {
			return -1;
		}

		int best = NoAimDifference;
		short chosen = -1;
		for (short component = 0; component < Es1DamageHeader.ComponentCount; component++) {
			if (!header.Occupied[component]) {
				continue;
			}

			var (pitch, roll, _) = SimTrig.EulerToward(ComponentPosition(component), from);
			int difference = Abs((short)(pitch - angles.Pitch)) + Abs((short)(roll - angles.Roll));
			if (difference < best) {
				best = difference;
				chosen = component;
			}
		}

		return chosen;
	}

	/// <summary>The difference <see cref="ComponentNearestAim"/> starts from, above any it measures.</summary>
	public const int NoAimDifference = 0x1000000;

	/// <summary>
	/// Slot <c>+0x5c</c>, <c>Mech_ComponentPosition</c> (<c>00015990</c>, docs/es1/simulation/mech.md#small-functions): the
	/// piece's anchor point through its part's node when the part id is above 0 and the shape has the part, then through
	/// the machine's frame; otherwise through the frame alone. Retail reads an unbound point through a null pointer; (0, 0,
	/// 0) here, an engine guard, and with no header the position.
	/// </summary>
	public Vec3i ComponentPosition(short component) {
		if (Damage?.Piece(component) is not { } piece) {
			return Position;
		}

		var (x, y, z) = piece.AnchorPoint ?? default;
		var point = new Vec3i(x, y, z);
		if (piece.AnchorPart > 0 && PartNodeTransform(piece.AnchorPart) is { } node) {
			point = node.TransformPoint(x, y, z);
		}

		return Frame.TransformPoint(point.X, point.Y, point.Z);
	}

	/// <summary>Slot <c>+0x70</c>, <c>SimObject_MissileLockState_AlwaysMechCopy</c>: every missile a HERC fires has lock (docs/es1/simulation/rockets.md#launching--rocket_fire-00007748).</summary>
	public bool MissileLockState(short subtype) => true;

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
	/// <c>+0x1d2</c>, the standing squad order's verb (docs/es1/simulation/mech-ai.md#the-fields): 1 move, 2 patrol, 4
	/// engage, 6 guard, 0 none. The squad order's receiver writes it (<see cref="Ai.Es1SquadOrders"/>), taking fire sets 4
	/// (<see cref="Ai.Es1TakingFire.OnTakingFire"/>), and <see cref="Ai.Es1Targets.ClearSquadEngageOrder"/> clears 4.
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

	/// <summary>The readings <see cref="ReadDamageReadouts"/> fills, the 84-byte buffer the comm boxes' condition and the MFD's status screen pass.</summary>
	public const int DamageReadingCount = 42;

	/// <summary>
	/// <c>Mech_ReadDamageReadouts</c> (<c>0001583c</c>)(machine, buffer): the type record's <c>int16</c> <c>+0x6c</c>, the
	/// type number, in entry 0 and <see cref="Es1DamageHeader.FillDamageReadouts"/>' readings after it
	/// (docs/es1/simulation/damage-readouts.md). With no header every reading is 0.
	/// </summary>
	public short[] ReadDamageReadouts() {
		var buffer = new short[DamageReadingCount];
		buffer[0] = Type.Type;
		Damage?.FillDamageReadouts(buffer);
		return buffer;
	}

	/// <summary>
	/// <c>Mech_ReadEntryDamage</c> (<c>00015868</c>)(machine, <paramref name="index"/>, <paramref name="kind"/>):
	/// <see cref="Es1DamageHeader.ReadEntryDamage"/>; 0 with no header.
	/// </summary>
	public short ReadEntryDamage(int index, int kind) => Damage?.ReadEntryDamage(index, kind) ?? 0;

	/// <summary>
	/// <c>+0x1d4</c> and <c>+0x1d8</c>, the squad order's point, which the receiver's verbs <c>0xc</c> to <c>0xe</c> write
	/// (<see cref="Ai.Es1SquadOrders"/>). Retail's point is its X and Y; the dword after them is <see cref="SquadTarget"/>,
	/// so the Z here, which every reader ignores, is not retail's and stays 0.
	/// </summary>
	public Vec3i SquadPoint { get; set; }

	/// <summary><c>+0x1dc</c>, the squad order's target, or null, written by the receiver's verbs 0, 2, 5 and <c>0xb</c> (<see cref="Ai.Es1SquadOrders"/>) and by taking fire.</summary>
	public IEs1SimObject? SquadTarget { get; set; }

	/// <summary>
	/// <c>+0x1e0</c>, the squad order's second object, or null. PLACEHOLDER: no writer is known; <c>Mech_ReceiveSquadOrder</c>
	/// does not write it (ES2's verb <c>0xc</c> stores its subject there) and a text search of the decompile finds none, so
	/// it holds null (docs/es1/simulation/mech-ai.md#the-fields).
	/// </summary>
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

	/// <summary>
	/// <paramref name="target"/> installed as the AI's thinks install one: when it is not <see cref="Target"/>, the old
	/// target's holders' count down by 1 and the new one's up by 1, and <see cref="TargetChanged"/> set; null installs none.
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

	/// <summary>Out of action: <see cref="Disarmed"/> (<c>+0x9f</c>), <see cref="Immobilized"/> (<c>+0x9e</c>) or <see cref="Destroyed"/> (<c>+0x93</c>).</summary>
	public bool OutOfAction => Disarmed || Immobilized || Destroyed;

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

	/// <summary>
	/// <c>+0x91</c>, the mission file's radar setting (docs/es1/simulation/mech-ai.md#the-fields), set when the record's
	/// <c>.MSN</c> <c>0x08</c> is not 0 (docs/es1/formats/script-dat.md#the-second-pass).
	/// </summary>
	public bool RadarSetting { get; set; }

	/// <summary>
	/// <c>+0x1e6</c>, the cruise speed, 0 for <see cref="Ai.Es1Navigation.DefaultCruiseSpeed"/>
	/// (docs/es1/simulation/mech-ai.md#the-fields): the record's <c>.MSN</c> <c>0x0a</c> when it is not negative
	/// (docs/es1/formats/script-dat.md#the-second-pass).
	/// </summary>
	public short CruiseSpeed { get; set; }

	/// <summary>
	/// <c>+0x174</c> and <c>+0x176</c>, the record's <c>.MSN</c> <c>0x4a</c> and <c>0x4c</c>; <c>Mech_Constructor</c> stores
	/// <c>0xffff</c> in the first (<c>0001142a</c>) and leaves the second, which is read only with the first not negative.
	/// </summary>
	public (short First, short Second) Word174 { get; set; } = (-1, 0);

	/// <summary><c>+0x16c</c> and <c>+0x170</c>, the two actions; the second fires when the machine goes out of the fight.</summary>
	public (Es1Action? First, Es1Action? Second) Actions { get; set; }

	/// <summary><c>+0x232</c>, a squad machine's <c>data\player.mec</c> pilot word 1.</summary>
	public short PilotWord { get; set; }

	/// <summary><c>+0xad</c>, worth no salvage: the record's salvage scale is 0, or the starting condition says so.</summary>
	public bool WorthNoSalvage { get; set; }

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
	/// with time 0; each fitted mount's model stamped with its hardpoint's node and its template joined to the damage
	/// header (<see cref="Es1DamageHeader.FitWeapon"/>, moved by <see cref="Es1WeaponMount.MountPointOffset"/>,
	/// <c>WeaponMount_MuzzleOffset</c>); then the anchors bound and the unoccupied components' flags cleared
	/// (<see cref="Es1DamageHeader.BindAnchorsAndClearUnoccupied"/>).
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

			Damage?.FitWeapon((sbyte)mount.Hardpoint.LoadoutSlot, mount.Template, mount.MountPointOffset);
		}

		Damage?.BindAnchorsAndClearUnoccupied();
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
