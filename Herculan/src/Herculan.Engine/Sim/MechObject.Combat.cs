using Herculan.Engine.Content;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.World;
using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;

namespace Herculan.Engine.Sim;

/// <summary>
/// Taking fire and giving it: the trigger path (<c>Mech_PlayerFireTick</c> (<c>00415608</c>) → <c>WeaponMounts_FireTrigger</c> (<c>00410dbc</c>)), the
/// mech's own direct-fire hit test (<c>Mech_DirectFireHitTest</c>, <c>00418ba8</c>) and the two
/// functions below it that turn a struck component into damage
/// (<c>Mech_ApplyDirectFireDamage</c> <c>004188c8</c>, <c>Mech_ComponentDamageWrite</c>
/// <c>00417de4</c>).
/// </summary>
public sealed partial class MechObject {
	/// <summary>
	/// The two cockpit component slots. <c>Mech_ComponentDamageWrite</c> checks them by literal index
	/// as the machine's death gate — losing either one, with everything inside it, kills the pilot
	/// outright regardless of what else is still standing.
	/// </summary>
	private const int CockpitFrontComponent = 0;

	/// <inheritdoc cref="CockpitFrontComponent"/>
	private const int CockpitRearComponent = 1;

	/// <summary>
	/// Dependent sub-piece slots the damage endpoint reads by literal offset. The leg servos are the
	/// front pair, joined by <see cref="RearLegServoDependents"/> on a four-legged chassis; life
	/// support and the pilot are the machine's other two death gates; the reactor drives the two
	/// output-damage flags.
	/// </summary>
	private static readonly int[] FrontLegServoDependents = { 0, 1 };

	/// <inheritdoc cref="FrontLegServoDependents"/>
	private static readonly int[] RearLegServoDependents = { 10, 11 };

	/// <inheritdoc cref="FrontLegServoDependents"/>
	private const int ShieldGeneratorDependent = 4;

	/// <inheritdoc cref="FrontLegServoDependents"/>
	private const int ReactorDependent = 5;

	/// <inheritdoc cref="FrontLegServoDependents"/>
	private const int LifeSupportDependent = 8;

	/// <inheritdoc cref="FrontLegServoDependents"/>
	private const int PilotDependent = 9;

	/// <summary>
	/// Where the legs count as crippled — <c>Mech_ComponentDamageWrite</c>'s own <c>0x8d</c>, Q8 over
	/// 256 with 0 pristine. Crossing it latches <see cref="LegsCrippled"/>, which costs the machine
	/// most of its speed and, on the player's own, announces <c>STRUCTURAL FAILURE IMMINENT</c>.
	/// </summary>
	private const int LegsCrippledDamage = 0x8d;

	/// <summary>
	/// And the lower of the two leg thresholds, <see cref="LegsDamaged"/>'s — reached at a little
	/// under a third of a side gone, for the milder speed penalty and
	/// <c>INTERNAL DAMAGE: LEG SERVOS</c>.
	/// </summary>
	private const int LegsDamagedAlert = 0x50;

	/// <summary>Reactor-damage thresholds, from the same function: <c>0xc0</c> and <c>0x80</c>.</summary>
	private const int ReactorCriticalDamage = 0xc0;

	/// <inheritdoc cref="ReactorCriticalDamage"/>
	private const int ReactorDegradedDamage = 0x80;

	/// <summary>
	/// The Q8 reading that means a part is gone. Both <see cref="ComponentDamage.DamagePercent"/> and
	/// <see cref="ComponentDamage.DependentPercent"/> saturate here.
	/// </summary>
	public const int FullyDamaged = 0x100;

	/// <summary>
	/// How many times this machine's cockpit has been jolted — a hit on one of its two cockpit
	/// components, or a slide landing. The host watches it for the edge rather than a flag, the same
	/// way it watches <see cref="Footfalls"/>, so a jolt taken between two frames is not missed. See
	/// <c>CockpitHitShake</c>, and docs/formats/cockpit-canopy-palette.md, "The damage shake".
	/// </summary>
	public int CockpitHits { get; private set; }

	/// <summary>
	/// A cockpit component past this reading no longer shakes the view — the same function's literal
	/// <c>0x64</c>, on <see cref="ComponentDamage.DamagePercent"/>'s 0-<see cref="FullyDamaged"/>
	/// scale. So the jolt stops once the cockpit is about 40% gone, and a machine being finished off
	/// takes its last hits in a steady view.
	/// </summary>
	private const int CockpitShakeDamageLimit = 100;

	/// <summary>
	/// <c>Mech_PlayerFireTick</c> (<c>00415608</c>), the player's own fire path, called once a frame from
	/// <c>Sim_PollPlayerInput</c> with the input device struct.
	///
	/// <para><b>The trigger is a held state, not a keypress.</b> The mount's own vtable <c>+0x30</c>
	/// (<c>WeaponMount_TriggerHeld</c>, <c>0040f8ad</c>) does nothing but read the device struct's byte at <c>+0x0d</c> — the
	/// fire button — so holding it fires again the moment the refire timer runs out and the capacitor
	/// is back over the threshold. Nothing edge-detects it anywhere along the path.</para>
	///
	/// <para>Only a machine with a pilot ever reaches this: the original calls it from the input poll
	/// for <c>LocalPlayerMech</c> alone, and AI machines fire through their own think function, which
	/// is unported. Here that falls out of <see cref="Controls"/>, which is
	/// <see cref="MechControls.Neutral"/> for everything the player is not flying.</para>
	///
	/// <para>The rest of <c>Mech_PlayerFireTick</c> is the player's <b>line of fire</b>: on a shot it stamps
	/// up to 40 points along the turret bearing at <see cref="FiringLineSpacing"/> spacing, cut to the
	/// range of the selected target. Nothing draws them — their only reader is
	/// <see cref="ObstacleAvoidance"/>, which steers the player's own squadmates out of the way. See
	/// docs/simulation/ai-navigation.md.</para>
	/// </summary>
	private void FireTick(SimWorld world) {
		// A round the player is flying clears the device's trigger byte before this reads it.
		bool trigger = Controls.Fire && !(LocallyPiloted && world.PlayerTriggerCleared);
		bool fired = Weapons.FireTick(this, world, trigger);

		if (!IsPlayer) {
			return;
		}

		if (!fired) {
			world.ClearPlayerFiringLine();
			return;
		}

		int count = FiringLineDefaultPoints;

		if (Target is { } target) {
			count = Position.ApproxDistanceTo(target.Position) >> FiringLineRangeShift;
			count = count > FiringLineMaxPoints ? FiringLineMaxPoints : count < 1 ? 1 : count;
		}

		short cos = BinaryAngle.Cos((short)(Heading - TorsoTwistAngle));
		short sin = BinaryAngle.Sin((short)(Heading - TorsoTwistAngle));
		int stepX = (int)((-(long)FiringLineSpacing * sin + 0x2000) >> 14);
		int stepY = (int)(((long)FiringLineSpacing * cos + 0x2000) >> 14);

		world.SetPlayerFiringLine(Position, stepX, stepY, count);
	}

	/// <summary>How far apart the player's line-of-fire points are laid.</summary>
	private const int FiringLineSpacing = 0x1000;

	/// <summary>How many points the line runs to when the player has nothing selected.</summary>
	private const int FiringLineDefaultPoints = 20;

	/// <summary>The ceiling on the point count, and the size of the original's own vector.</summary>
	private const int FiringLineMaxPoints = 40;

	/// <summary>Target range is shifted by this to give the point count.</summary>
	private const int FiringLineRangeShift = 12;

	/// <summary>
	/// <inheritdoc cref="SimObject.Target"/>
	/// <summary>
	/// <c>mech+0x1a4</c> — the machine's selected target, and the field the whole of homing hangs
	/// off: <c>Bullet_FirePowered</c> reads it to give a plasma round something to chase and
	/// <c>Rocket_Fire</c> reads it to give a missile a lock, so before anything wrote it every guided
	/// weapon in the game flew straight.
	///
	/// <para><b>Nothing in the simulation writes it for the player's machine.</b> The selection is
	/// made in the cockpit and copied here once a frame — see <see cref="TargetSelection"/>, which is
	/// where the RE for that lives. An AI machine writes it from its own think and from the combat
	/// reassess.</para>
	///
	/// <para>The field and its refcount bookkeeping are <see cref="SimObject.Target"/>'s, because the
	/// original keeps them on the shared base; what a HERC adds on top of them is here.</para>
	/// </summary>
	private protected override void OnTargetChanged(SimObject? previous) {
		if (Target == null && Weapons.AutoTrack) {
			// Player_PerFrameCockpitUpdate arms mech+0x31c here, on the change that leaves ATT
			// with nothing to track. See MechObject.TorsoTick, which runs it down.
			_autoTrackIdle = AutoTrackIdleDelay;
		}

		// And the Targeting Pod's lock restarts on the same change. Both gates are
		// Player_PerFrameCockpitUpdate's own: it runs for the piloted machine alone, and it calls the
		// reset only when the new selection is something — clearing the target leaves the lock where
		// it was. See TargetingPodLock.ResetComponentLock.
		if (LocallyPiloted && Target != null) {
			Pods.TargetingMount?.ComponentLock?.ResetComponentLock(Target);
		}

		TargetChanged = true;
	}

	/// <summary>
	/// <c>[Tab]</c> — <c>CockpitWidgets_HandleCommand</c>'s <c>0x0f</c> case, which steps the
	/// Targeting Pod's component lock on the selected machine. A machine with no pod, or with nothing
	/// selected, does nothing.
	///
	/// <para>The command only reaches the pod while the heads-down display is <i>not</i> down: in view
	/// mode 1 the same scancode goes to the display's own command slot instead, which is the manual's
	/// <c>Zoom Map In/Out</c>. That split is the host's to make — see
	/// docs/formats/cockpit-input.md.</para>
	/// </summary>
	public void CycleTargetComponent() => Pods.TargetingMount?.ComponentLock?.CycleComponent(Target);

	/// <summary>
	/// <c>Player_ResolveTargetAimPoint</c> (<c>0041b728</c>) — the point the HUD aims at on the
	/// selected target, and which component of it that is. With a Targeting Pod fitted and the target
	/// inside <see cref="ComponentAimRange"/> the pod answers both; otherwise it is the target's own
	/// <see cref="SimObject.AimPoint"/> and no component.
	///
	/// <para>Both halves reach the cockpit through <c>CockpitView_SetTargetBlock</c>: the flag lands
	/// at <c>+0x27c</c> and drops the target box to its bare pip, and the component id lands at
	/// <c>+0x27e</c> and highlights that region of the MFD's paper doll. See
	/// docs/formats/hud-target-indicator.md and docs/formats/mfd.md.</para>
	/// </summary>
	/// <returns>Where to aim, whether a component was singled out, and which.</returns>
	public (Vec3i Point, bool ComponentTargeted, short Component) ResolveTargetAimPoint() {
		if (Target is not { } target) {
			return (Position, false, 0);
		}

		if (Pods.TargetingMount?.ComponentLock is { } pod
			&& Position.ApproxDistanceTo(target.Position) < ComponentAimRange) {
			bool targeted = pod.ResolveAimPoint(target, out var point, out short component);
			return (point, targeted, component);
		}

		return (target.AimPoint, false, 0);
	}

	/// <summary>
	/// How close the selected machine has to be before the Targeting Pod is asked for a component aim
	/// point at all — the original's literal 30000, 180 m, the manual's "close range". Outside it the
	/// pod is skipped entirely and the box goes back to whole.
	/// </summary>
	public const int ComponentAimRange = 30000;

	/// <summary>
	/// <c>mech+0x9d</c> — raised whenever <see cref="Target"/> changes and never cleared by the write
	/// itself. In the original it gates the AI's per-tick weapon arbitration (a machine that has just
	/// switched target does not shoot on that tick) and it is what tells the cockpit to reset the
	/// gunsight's lock state. Nothing consumes it yet; it is set because the setter is the only place
	/// that can, and leaving it out would mean revisiting the setter later.
	/// </summary>
	public bool TargetChanged { get; set; }

	/// <summary>
	/// <c>mech+0x31c</c> — how long Automatic Turret Tracking waits, with the latch on and nothing
	/// selected, before it gives up and brings the turret home. <c>Player_PerFrameCockpitUpdate</c>
	/// (<c>0041b130</c>) arms it from the selection change that cleared the target and runs it down
	/// every frame the pair still holds; the engine runs it down in the turret block instead, which
	/// is the only thing that reads the result. See <see cref="AutoTrackIdleDelay"/>.
	/// </summary>
	public short AutoTrackIdleTimer => _autoTrackIdle;

	/// <summary>
	/// What that timer is armed with — the original's own <c>0x1194</c>, about 55 ticks.
	/// </summary>
	public const short AutoTrackIdleDelay = 0x1194;

	private short _autoTrackIdle;

	/// <summary>
	/// <c>mech+0x288</c> — the damage shields and armour have absorbed inside the current
	/// <see cref="DamageWindowTimer"/> window, about two seconds; <see cref="AiTimersTick"/> zeroes it
	/// at each expiry. See docs/simulation/ai-combat-states.md ("The circling step").
	/// </summary>
	public int DamageTaken { get; private set; }

	/// <summary>
	/// <c>mech+0x285</c> — the countdown <see cref="DamageTaken"/> accumulates over, reloaded with
	/// <see cref="DamageWindowReload"/> at each expiry.
	/// </summary>
	public short DamageWindowTimer { get; private set; }

	/// <summary>The damage window, in <see cref="SimMath.CountdownTimerTick"/>'s unit — about two seconds.</summary>
	public const short DamageWindowReload = 4000;

	/// <summary>
	/// How many shots have got past this machine's shields. Not part of the original — a plain
	/// counter kept alongside the real per-component health in <see cref="Damage"/>, which is what
	/// those shots actually go into.
	/// </summary>
	public int PenetratingHits { get; private set; }

	/// <summary>
	/// <c>mech+0x99</c> — whether the machine is dead. Set by <see cref="ComponentDamageWrite"/> when
	/// either cockpit section is gone or life support or the pilot has been destroyed; see there for
	/// the whole of the test.
	/// </summary>
	public override bool Destroyed => _destroyed;

	private bool _destroyed;

	/// <summary>
	/// <c>mech+0xa4</c> — whether the machine can no longer move under its own power. Latched, never
	/// cleared, and in the original it also clears the machine's target and fires its mission action.
	///
	/// <para>What sets it depends on the chassis. A walker loses it with its legs, in
	/// <see cref="GradeLegs"/>. A flyer has no legs to lose and takes it instead from the airframe
	/// contact that destroys its nose or its belly — and for a flyer it is the harder stop of the
	/// two, because the flight path also refuses to integrate position while it is set. The aircraft
	/// is down where it fell. See <see cref="FlyerMovementTick"/>.</para>
	/// </summary>
	public bool Immobilised { get; private set; }

	/// <summary>
	/// <c>mech+0xa9</c> — the harder of the two graded leg states: a side reads
	/// <see cref="LegsCrippledDamage"/> or worse, but not enough legs are actually destroyed to
	/// immobilise the machine. Costs it most of its speed, and keeps it out of <c>flanking</c>.
	/// </summary>
	public bool LegsCrippled { get; private set; }

	/// <summary>
	/// <c>mech+0xa8</c> — the softer one: neither side is crippled, but one is past
	/// <see cref="LegsDamagedAlert"/>. In the original it exists mainly to raise the pilot's alert
	/// once, and to take the milder speed penalty. Latched, like its partner. The original's
	/// <c>flanking</c> gate reads it too; the port's does not (KNOWN_ISSUES.md).
	/// </summary>
	public bool LegsDamaged { get; private set; }

	/// <summary>
	/// The reactor's condition as the two latching flags <c>mech+0xaa</c> and <c>mech+0xab</c>
	/// describe it. It feeds <see cref="ReactorRate"/>, which the original computes <b>once, at
	/// spawn</b> — so a reactor wrecked mid-mission latches the flag without changing the rate. That
	/// is the original's own behaviour and is reproduced: nothing recomputes the rate from here.
	/// </summary>
	public ReactorCondition Reactor { get; private set; } = ReactorCondition.Intact;

	/// <summary>Who landed the shot that killed it, for the kill credit the original hands back.</summary>
	public SimObject? LastAttacker { get; private set; }

	/// <summary>
	/// How far under the map a chassis that leaves no wreck is put — the original's own literal,
	/// written straight into the object's Z. See <see cref="MechTypeRecord.VanishesOnDeath"/>.
	///
	/// <para>The original also sets a byte at <c>obj+0x38</c> on the way. Its role is not
	/// established and nothing ported reads it, so it is left out rather than guessed at; the sink
	/// and the dropped parts are what take the machine off the screen either way.</para>
	/// </summary>
	private const int VanishedDepth = -100000;

	/// <summary>
	/// Which of this machine's legs have come off — <c>mech+0x238</c>'s dropped entries. The original
	/// deletes the leg's shadow when its servos read fully destroyed, and
	/// <c>Mech_PlaceLegsOnGround</c> skips an entry with no shadow, footfall and all; this is that
	/// skip, kept apart from <see cref="Shadows"/> so a shadow the pool could not supply does not
	/// read as a lost leg. See <see cref="GradeLegs"/> and <see cref="PlaceLegsOnGround"/>.
	/// </summary>
	private bool[] _legsLost = System.Array.Empty<bool>();

	/// <summary>Whether leg <paramref name="leg"/> has been shot off.</summary>
	public bool LegLost(int leg) => leg >= 0 && leg < _legsLost.Length && _legsLost[leg];

	/// <summary>
	/// <c>Mech_CreditNeutralisedTarget</c> (<c>00415710</c>), the mech's vtable <c>+0x60</c> — told to the machine that just put
	/// <paramref name="victim"/> out of the fight, from both of
	/// <see cref="ComponentDamageWrite"/>'s branches. The base class' slot
	/// (<c>SimObject_CreditNeutralisedTargetNoOp</c> (<c>00411b2c</c>)) is an empty stub, so only a HERC credits anything.
	///
	/// <para><paramref name="wasImmobilised"/> is the victim's reading from <i>before</i> this
	/// change, and it is what stops a machine being counted twice: a HERC whose legs went first was
	/// already credited then, so finishing it off scores nothing more. Only a first, cross-team
	/// neutralisation adds to the tally — <see cref="KillsOf"/>, one counter per target class.</para>
	///
	/// <para>Two callouts go out from here, and only the first is under
	/// <paramref name="wasImmobilised"/>: a squadmate that scored says so, and a squadmate that has
	/// just been stopped cries out whether or not this was the blow that counted. The second is the
	/// one place in the original that forces a post past a destroyed machine's own silence — see
	/// <see cref="PostSquadMessage"/>.</para>
	///
	/// <para>A squadmate the player itself stopped also adds one to mission counter 10
	/// (<c>DAT_004a9f08</c>), again whether or not this was the blow that counted.</para>
	/// </summary>
	internal void CreditNeutralised(SimWorld world, MechObject victim, bool wasImmobilised) {
		MechObject? player = world.PlayerMech;

		if (!wasImmobilised && Group != null && victim.Group != null
				&& Group.Side != victim.Group.Side) {
			if (player != null && !ReferenceEquals(player, this)
					&& ReferenceEquals(player.Group, Group)) {
				PostSquadMessage(world, SquadMessageScoredAKill);
			}

			if ((uint)victim.TargetClass < (uint)_killsByClass.Length) {
				_killsByClass[(int)victim.TargetClass]++;
			}

			ScoredAKill = true;
		}

		if (player != null && !ReferenceEquals(player, victim)
				&& ReferenceEquals(player.Group, victim.Group)) {
			victim.PostSquadMessage(
				world,
				victim.Destroyed ? SquadMessageDestroyed : SquadMessageWentDown,
				force: true);

			if (ReferenceEquals(player, this)) {
				world.BumpMissionCounter(World.MissionLoader.SquadmatesDownedCounter, 1);
			}
		}
	}

	/// <summary>
	/// <c>mech+0x2a4</c> — how many of <paramref name="targetClass"/> this machine has put out of the
	/// fight this mission, counted once per victim: <c>INC word [mech + class*2 + 0x2a4]</c> at
	/// <c>0041576d</c>, the class being the victim's <c>+0x1a8</c>. The first three are the Herc, Base and
	/// Flyer kills the mission's results carry to the pilot (<see cref="MissionResults"/>).
	///
	/// <para>The original indexes the array unchecked. The four classes an object can hold are all it is
	/// kept for here; how far the original's array runs past the three the results read is not
	/// established.</para>
	/// </summary>
	public short KillsOf(TargetClass targetClass) =>
		(uint)targetClass < (uint)_killsByClass.Length ? _killsByClass[(int)targetClass] : (short)0;

	private readonly short[] _killsByClass = new short[(int)TargetClass.GroundVehicle + 1];

	/// <summary>
	/// <c>mech+0xb3</c> — the mission placed this machine already broken, so its wreck is worth nothing.
	/// <see cref="ApplyStartingCondition"/> raises it on its two worst grades, so a mission cannot be farmed
	/// by authoring derelicts into it.
	/// </summary>
	public bool WorthNoSalvage { get; private set; }

	/// <summary>
	/// <c>Mech_SalvageValue</c> (<c>00418e60</c>) — what this machine's wreck is worth to the player's side, in
	/// kilograms before the results' own scale (<see cref="MissionResults"/>). Zero when
	/// <see cref="WorthNoSalvage"/> is set. Otherwise every mount, in hardpoint order, whose component reads
	/// under half damaged goes onto the salvage list with its condition; and the chassis is worth
	/// <see cref="MechTypeRecord.SalvageScale"/> of its <see cref="ComponentDamage.WeightedArmorRemaining"/>, the
	/// scale halved when component 0 is at full damage. See
	/// docs/simulation/component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60.
	/// </summary>
	internal int SalvageValue(SimWorld world) {
		if (WorthNoSalvage || _damage == null) {
			return 0;
		}

		foreach (var mount in Weapons.Slots) {
			if (mount == null) {
				continue;
			}

			int reading = _damage.DamagePercent(WeaponMounts.FirstMountComponent + mount.LoadoutSlot);
			if ((short)reading < SalvageableMountReading) {
				world.QueueSalvage((short)mount.WeaponId, SalvageCondition(reading));
			}
		}

		int scale = Type.SalvageScale;
		if ((short)_damage.DamagePercent(0) == FullyDamaged) {
			scale >>= 1;
		}

		return SimMath.Q10Multiply(_damage.WeightedArmorRemaining(), scale);
	}

	/// <summary>The reading a mount's component must be under for the mount to be salvaged — the literal <c>0x80</c>.</summary>
	private const short SalvageableMountReading = 0x80;

	/// <summary>
	/// <c>Group_ApplyOutnumberedDamage</c> (<c>00423f08</c>): <see cref="ApplyOutnumberedDamage"/> on
	/// every member of <paramref name="group"/>. <see cref="SimWorld"/> runs it on the player's group when
	/// the mission poll answers <see cref="MissionStatus.PlayerImmobilised"/>. See
	/// docs/simulation/mission-objectives.md#the-poll--mission_pollstatus-004131ac.
	/// </summary>
	internal static void ApplyGroupOutnumberedDamage(SimWorld world, MissionGroup group) {
		for (int i = 0; i < group.Members.Count; i++) {
			if (group.Members[i] is MechObject member) {
				member.ApplyOutnumberedDamage(world);
			}
		}
	}

	/// <summary>
	/// <c>Mech_ApplyOutnumberedDamage</c> (<c>0041b804</c>): unless this machine is
	/// <see cref="MissionObjectives.IsClearOfThreats">clear of threats</see>, weighs the hostile strength
	/// around it against the friendly and spreads one <see cref="OutnumberedImpactDamage"/> impact over it
	/// per <see cref="OutnumberedStrengthPerImpact"/> of difference. The original walks its HERC, flyer and
	/// structure pools in turn; one walk over every object sums the same.
	/// </summary>
	private void ApplyOutnumberedDamage(SimWorld world) {
		if (MissionObjectives.IsClearOfThreats(world, this) || Group is not { } ownGroup) {
			return;
		}

		int friendly = 0;
		int hostile = 0;
		var objects = world.Objects;

		for (int i = 0; i < objects.Count; i++) {
			var other = objects[i];
			if (other.Removed || other.OutOfAction || other.AwaitingDeployment || other.Group is not { } otherGroup) {
				continue;
			}

			(int range, int strength) = other switch {
				MechObject mech => (OutnumberedHercRange, (int)mech.Type.SalvageScale),
				FlyerObject => (OutnumberedFlyerRange, OutnumberedFlyerStrength),
				BaseObject { Type.Armament: not BaseArmament.None } => (OutnumberedStructureRange, OutnumberedStructureStrength),
				_ => (0, 0)
			};

			if (range == 0 || Position.ApproxDistanceTo(other.Position) >= range) {
				continue;
			}

			if (otherGroup.Side == ownGroup.Side) {
				friendly += strength;
			} else {
				hostile += strength;
			}
		}

		friendly = Math.Max(friendly - OutnumberedFriendlyAllowance, 0);
		for (; friendly < hostile; hostile -= OutnumberedStrengthPerImpact) {
			SpreadImpactDamage(world, OutnumberedImpactDamage, OutnumberedImpactDamage);
		}
	}

	/// <summary><c>Mech_ApplyOutnumberedDamage</c>'s reach for a HERC, which counts its <see cref="MechTypeRecord.SalvageScale"/>.</summary>
	private const int OutnumberedHercRange = 45000;

	/// <summary>Its reach for an aircraft, which counts <see cref="OutnumberedFlyerStrength"/>.</summary>
	private const int OutnumberedFlyerRange = 50000;

	/// <summary>Its reach for an armed structure, which counts <see cref="OutnumberedStructureStrength"/>.</summary>
	private const int OutnumberedStructureRange = 35000;

	private const int OutnumberedFlyerStrength = 500;

	private const int OutnumberedStructureStrength = 500;

	/// <summary>Taken off the friendly sum, floored at zero, before the two are compared.</summary>
	private const int OutnumberedFriendlyAllowance = 600;

	/// <summary>How much hostile excess each impact accounts for.</summary>
	private const int OutnumberedStrengthPerImpact = 400;

	/// <summary>Both the most damage and the per-component odds of each impact, <c>0x40</c>.</summary>
	private const short OutnumberedImpactDamage = 0x40;

	/// <summary><c>mech+0xa6</c> — raised by the first kill this machine scores. Latched.</summary>
	public bool ScoredAKill { get; private set; }

	/// <summary>
	/// <c>Mech_DirectFireHitTest</c> (<c>00418ba8</c>), the mech's vtable <c>+0x20</c> — the hit test
	/// and the damage application in one call, exactly as the original has it.
	///
	/// <list type="number">
	/// <item><b>Reject by distance.</b> Muzzle to machine, against the ray's remaining length plus
	/// this machine's <see cref="MechTypeRecord.HitRadius"/> plus the shot's own
	/// <see cref="WeaponShot.Clearance"/>. A coarse first pass that keeps the transform work off
	/// everything nowhere near the shot.</item>
	/// <item><b>Geometry, in the shot's own frame.</b> The machine's hit centre is brought into
	/// muzzle space, where the ray is the Y axis: the hit needs the centre in front and within range,
	/// and its distance off the axis under this machine's radius. That is a ray-versus-vertical-
	/// cylinder test written as two comparisons.</item>
	/// <item><b>Shields.</b> The facing is picked by which side of the machine the muzzle is on, and
	/// that facing absorbs up to what it holds — see <see cref="ShieldCharge.AbsorbDirectFire"/>. A
	/// shot it absorbs entirely stops here: it still counts as a hit and still stops the ray, and it
	/// spawns only a shield flash.</item>
	/// <item><b>Component selection.</b> Anything that got through goes to the machine's real hit
	/// geometry — the <c>col\&lt;NAME&gt;.COL</c> sphere model, every cluster of which rides one of
	/// the shape's animated nodes, so which part is struck depends on where the legs and torso are
	/// right now. <b>Missing every sphere is a clean miss</b>: the cylinder is only a gate, and a
	/// shot through the gap under a HERC's torso passes on to whatever stands behind it.</item>
	/// </list>
	/// </summary>
	public override int DirectFireHitTest(SimWorld world, WeaponShot shot) {
		var muzzle = new Vec3i(shot.Muzzle.X, shot.Muzzle.Y, shot.Muzzle.Z);
		if (shot.Clearance + shot.Distance + Type.HitRadius < Position.ApproxDistanceTo(muzzle)) {
			return 0;
		}

		// The radar reaction sits here in the original, past the range reject and ahead of the shield
		// absorb, so a round that reaches the machine at all provokes it whether or not it penetrates.
		RadarReactionToHit(shot.WeaponClass);

		// Machine space to muzzle space, the two hops the original composes: this machine's own
		// world transform, then the world-to-muzzle one the raycast cached.
		var toMuzzleSpace = Transform3.Concat(WorldTransform, shot.MuzzleInverse);

		short shieldDamage = shot.DamageShield;
		int struckAt = ShieldAbsorbDirectFire(toMuzzleSpace, shot.Distance, ref shieldDamage);
		if (struckAt == 0) {
			return 0;
		}

		DamageTaken += shot.DamageShield - shieldDamage;

		// The shields-down latch, set exactly where the original sets it: on the locally piloted
		// machine, when a shot lands with less than 500 points of charge left across both facings.
		// It plays alert 0x15 here and is released again in PowerTick once the array rebuilds past
		// 1500 - and it is what the MFD status screen reads for its SHIELDS DN condition, which is
		// why a HERC target never shows that state.
		if (LocallyPiloted && !ShieldsDownAlert && Shields.Total < ShieldsDownAlertCharge) {
			ShieldsDownAlert = true;
			world.Sounds?.Say(SystemMessages.ShieldsCritical);
		}

		if (shieldDamage == 0) {
			world.SpawnImpactEffect(
				world.PickImpactEffect(shot.ImpactFx(WeaponShot.ImpactFxGroup.Shield)),
				shot.Muzzle.TransformPoint(0, struckAt, 0));
			return struckAt;
		}

		var hit = CollisionModel.Test(
			_collision, toMuzzleSpace, shot.Distance, shot.Clearance, ComponentAlive, NodeFrame);

		if (hit is not { } struck) {
			return 0;
		}

		DamageTaken += shot.DamageArmor;
		PenetratingHits++;
		ApplyDirectFireDamage(world, struck.ComponentIndex, shot,
			shot.Muzzle.TransformPoint(0, struck.Distance, 0));

		return struck.Distance;
	}

	/// <summary>
	/// Whether one of this machine's components is still standing. A machine with no
	/// <c>dmg\&lt;NAME&gt;.DMG</c> has no components at all and nothing can hit it, which is what the
	/// original ends up with for a type whose files are missing.
	/// </summary>
	private bool ComponentAlive(int index) => _damage?.IsActive(index) ?? false;

	/// <summary>
	/// Where one of the shape's nodes stands right now, relative to this machine's own frame — the
	/// resolver <see cref="CollisionModel.Test"/> places node-mounted sphere clusters with, and the
	/// reason a HERC's hit volume walks with it.
	///
	/// <para>The <c>.COL</c> names a shape <i>part</i> id, which the original resolves through the
	/// shape to that part's transform slot (<c>Mech_ComponentGeometryTest_Candidate</c>, which falls
	/// back on an identity transform for a part the shape does not have) —
	/// <see cref="Anim.ShapeAnimation.TransformIdOfPart"/> is that lookup.</para>
	/// </summary>
	private Transform3? NodeFrame(short partId) {
		int transformId = Animation?.TransformIdOfPart(partId) ?? -1;
		return transformId < 0 || Shape == null ? null : Shape.NodeTransform(transformId);
	}

	/// <summary>
	/// <c>Mech_ShieldAbsorb_DirectFire</c> (<c>00413cc4</c>) — the geometry and the facing choice, with
	/// <see cref="ShieldCharge.AbsorbDirectFire"/> doing the absorption itself.
	///
	/// <para>The returned distance is the original's own linearisation of where the ray enters the
	/// hit cylinder: <c>alongAxis - (radius - offAxis)</c>, floored at 1 so that a hit is never
	/// mistaken for a miss. It is what the raycast shortens the ray to.</para>
	/// </summary>
	/// <param name="toMuzzleSpace">This machine's frame expressed in the shot's.</param>
	/// <param name="range">The ray's remaining length.</param>
	/// <param name="shieldDamage">The shot's shield damage, reduced by what the struck facing took.</param>
	/// <returns>How far along the ray this machine was struck, or zero for a miss.</returns>
	private int ShieldAbsorbDirectFire(in Transform3 toMuzzleSpace, int range, ref short shieldDamage) {
		// The machine is tested by its hit centre, not its origin: a beam passing over a HERC's feet
		// is a miss, and one through its torso is a hit, and the origin is at the feet.
		var center = toMuzzleSpace.TransformPoint(0, 0, Type.HitCenterHeight);

		// Y is distance down the ray. The original's comparison is unsigned, which is what rejects
		// anything behind the muzzle without a second test.
		if ((uint)center.Y >= (uint)range) {
			return 0;
		}

		int offAxis = SimMath.FastMagnitude2D(center.X, center.Z);
		if (offAxis >= Type.HitRadius) {
			return 0;
		}

		// Front or rear is decided by where the muzzle sits in the machine's frame, not by where the
		// machine sits in the shot's — so it is the shooter's bearing that picks the facing, which is
		// what makes turning your back on someone expose the rear array.
		bool front = toMuzzleSpace.Inverted().Y >= 1;
		Shields.AbsorbDirectFire(front, ref shieldDamage);

		int entry = center.Y - (Type.HitRadius - offAxis);
		return entry < 1 ? 1 : entry + 1;
	}

	/// <summary>
	/// <c>Mech_ApplyDirectFireDamage</c> (<c>004188c8</c>) — what a named component does with the
	/// damage that reached it.
	///
	/// <para><b>The shot's splash fraction is taken off the top.</b>
	/// <see cref="WeaponShot.SplashFactor"/> is a Q10 multiplier, and the share it names is
	/// <i>diverted</i> away from the struck component into a small explosion of its own — the mech's
	/// own vtable <c>+0x70</c>, <see cref="ExplosiveDamage"/>, at
	/// <see cref="SecondaryBlastRadius"/> — and the struck component gets only the remainder. Every
	/// retail beam states zero, so on a beam the whole shot still lands on the one component; the
	/// rocket records and the plasma round are what state a share.</para>
	///
	/// <para><b>The share is absorbed a second time on its way in.</b> The explosion path opens with
	/// its own shield absorption, and the original does not exempt a blast that came out of a shot
	/// which has already been through the other one — so a machine with charge left keeps far more of
	/// a splashing weapon off its structure than the two damage figures alone suggest.</para>
	///
	/// <para>The effect a hit spawns depends on whether the component's damage reading crossed one of
	/// its eight bands: it did not, and the shot draws from
	/// <see cref="WeaponShot.ImpactFxGroup.Ground"/>; it did, and the shot draws from
	/// <see cref="WeaponShot.ImpactFxGroup.Armor"/> instead. This is the branch that made the two
	/// arrays distinct, and it is now reachable — though on retail data all 27 projectile records
	/// carry byte-identical arrays for the two, so the same effect is drawn either way.</para>
	///
	/// <para><b>A band change on a mount component rolls to knock that mount out</b> — see
	/// <see cref="RollWeaponMountDestruction"/>, which is the other half of this function.</para>
	///
	/// <para><b><c>0x12</c> <c>DAMAGE LEVEL CRITICAL</c> is never said.</b> Its call site sits
	/// between the two readings and needs the later one to have <i>fallen</i> below the earlier; no
	/// retail <c>PROJ.DAT</c> record can make the write negative, so the line is unreachable — see
	/// docs/formats/cockpit-messages.md, "Posters". The cockpit jolt that shares its gate is a
	/// separate effect and is raised, through <see cref="CockpitHits"/>. What the test reads as
	/// having been meant is implemented beside that jolt under the
	/// <see cref="TweakSettingDefinitions.CriticalDamageMessage"/> tweak.</para>
	/// </summary>
	private void ApplyDirectFireDamage(SimWorld world, short componentIndex, WeaponShot shot, Vec3i hitPoint) {
		if (_damage == null) {
			return;
		}

		short armorDamage = shot.DamageArmor;
		short splash = (short)SimMath.Q10Multiply(shot.SplashFactor, armorDamage);
		int before = _damage.DamagePercent(componentIndex);
		int band = before >> 5;

		ComponentDamageWrite(world, componentIndex, (short)(armorDamage - splash), shot.Owner);

		// The diverted share, as its own explosion inside this machine. It is not a world sweep: the
		// original calls this machine's own +0x70 slot directly, so the blast is confined to the
		// components around the point of impact and cannot reach anything standing nearby.
		if (splash != 0) {
			ExplosiveDamage(world, splash, hitPoint, SecondaryBlastRadius, shot.Owner);
		}

		int after = _damage.DamagePercent(componentIndex);
		var group = after >> 5 == band
			? WeaponShot.ImpactFxGroup.Ground
			: WeaponShot.ImpactFxGroup.Armor;

		if (group == WeaponShot.ImpactFxGroup.Armor) {
			// The cockpit jolt, which is inside the band-change branch and not on every hit: a shot
			// that only scuffs the cockpit's armour is not felt, and one that moves it into a new
			// band is. See CockpitHitShake.
			if (LocallyPiloted && after < CockpitShakeDamageLimit
					&& (componentIndex == CockpitFrontComponent
						|| componentIndex == CockpitRearComponent)) {
				CockpitHits++;
			}

			// The announcement the jolt above was meant to carry, reconstructed. Retail's own call
			// site is inside that block and tests the *pre*-write reading against the same limit,
			// which asks for a cockpit that came out of the hit less damaged than it went in; taking
			// each test from the other reading turns it into the upward crossing it reads as
			// intended to be. This is a non-vanilla tweak. See SystemMessages.DamageLevelCritical
			// and KNOWN_ISSUES.md.
			if (TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.CriticalDamageMessage) && LocallyPiloted
					&& before < CockpitShakeDamageLimit && CockpitShakeDamageLimit < after
					&& (componentIndex == CockpitFrontComponent
						|| componentIndex == CockpitRearComponent)) {
				world.Sounds?.Say(SystemMessages.DamageLevelCritical);
			}

			RollWeaponMountDestruction(world, componentIndex, after);
		}

		world.SpawnImpactEffect(world.PickImpactEffect(shot.ImpactFx(group)), hitPoint);

		// And a spray of wreckage off the impact point, but only for a hit that moved the component
		// into a new band and did not finish it: a shot that merely scuffs the armour throws nothing,
		// and one that destroys the part has the cascade's own, much larger throw instead. The group
		// is the literal 2, which is DEF_DEB's, so it is the same three shapes off every machine
		// however exotic its own wreckage table is.
		if (group == WeaponShot.ImpactFxGroup.Armor && after != FullyDamaged) {
			world.SpawnDebris(HitDebrisGroup, hitPoint, DebrisTable(world));
		}
	}

	/// <summary>
	/// The debris group a band-changing hit throws — <c>Mech_ApplyDirectFireDamage</c>'s own literal
	/// 2, which lands in <c>DEF_DEB</c> whatever this machine has installed.
	/// </summary>
	private const short HitDebrisGroup = 2;

	/// <summary>
	/// This chassis' own debris table, <c>dat\&lt;DebrisFile&gt;_DEB.DAT</c> — what the original
	/// keeps on the type record at <c>+0x212</c> and installs as the alternate database before every
	/// throw a machine makes. Null for a chassis whose <c>.DAT</c> names none, in which case only the
	/// <c>DEF_DEB</c> half of the index space resolves.
	/// </summary>
	private DebrisDatabase? DebrisTable(SimWorld world) =>
		Type.DebrisTableName is { Length: > 0 } name ? world.Debris?.Database(name) : null;

	/// <summary>
	/// <c>Mech_ApplyExplosiveDamage</c> (<c>004187d0</c>), the mech's vtable <c>+0x70</c> — <b>the other
	/// damage model</b>, and not a setting of the direct-fire one. Where a beam picks exactly one
	/// component out of the hit geometry and hands it the whole shot, a blast rolls every component
	/// the machine has and scales what each takes by how far it stood from the bang.
	///
	/// <list type="number">
	/// <item><b>The facing</b> is the bearing from the machine to the blast against its own heading,
	/// front inside a quarter turn either way — the same ±90° window
	/// <c>Mech_GetShieldByHeading</c> uses, and unlike the direct-fire path it is the machine's
	/// facing that decides, not the geometry of a ray.</item>
	/// <item><b>Shields</b>, through <see cref="ShieldCharge.AbsorbExplosion"/>, which is the
	/// original's second implementation of the hard cap and scales its input differently from the
	/// first. A facing that swallows the blast whole ends it here.</item>
	/// <item><b>A roll per component.</b> Each live slot draws once at
	/// <see cref="ComponentBlastOdds"/> in 4096 — a little over half — to be considered at all, so
	/// two identical blasts on an identical machine do not wreck the same parts.</item>
	/// <item><b>Distance and falloff.</b> A component that passed its roll is placed in the world and
	/// measured against the blast point; inside <paramref name="blastRadius"/> it takes the shield
	/// overflow scaled linearly to zero at the radius, through the same
	/// <see cref="ComponentDamageWrite"/> endpoint a beam's damage ends in.</item>
	/// </list>
	///
	/// <para>Reached from two places, as in the original: the world sweep
	/// (<see cref="SimWorld.ExplosiveBlastSweep"/>) for a shot that goes off in the open, and this
	/// machine's own direct-fire path for the splash share of a shot that hit it — see
	/// <see cref="ApplyDirectFireDamage"/>.</para>
	///
	/// <para>The computer's damage warnings are posted from the shared endpoint, so a blast raises
	/// them exactly as a beam does. The debris a component lost this way throws goes through
	/// <see cref="ComponentDamage.ApplyDamage"/> like any other loss.</para>
	/// </summary>
	public override void ExplosiveDamage(SimWorld world, short damage, Vec3i hitPoint, int blastRadius,
			SimObject? attacker) {
		if (_damage == null || blastRadius <= 0) {
			return;
		}

		short bearing = Detection.HeadingToward(hitPoint, Position);
		bool front = (ushort)(bearing - Heading + BinaryAngle.QuarterTurn) < BinaryAngle.HalfTurn;

		short overflow = Shields.AbsorbExplosion(front, damage);

		// Not ported: no retail write of DamageTaken (mech+0x288) on the blast path has been found yet.
		// Mech_ApplyExplosiveDamage and Mech_ShieldAbsorb_Explosive were searched; the only writes
		// found are Mech_DirectFireHitTest's. Counting blasts would let them arm the circling
		// break-off, so the line stays out until something in retail is found that does this.
		// DamageTaken += damage - overflow;

		if (overflow <= 0) {
			return;
		}

		for (short i = 0; i < _damage.Count; i++) {
			if (!_damage.IsActive(i) || world.Random.NextMasked(0xfff) >= ComponentBlastOdds) {
				continue;
			}

			int distance = ComponentPosition(i).ApproxDistanceTo(hitPoint);
			if (distance >= blastRadius) {
				continue;
			}

			ComponentDamageWrite(
				world, i, (short)((long)overflow * (blastRadius - distance) / blastRadius),
				attacker);
		}
	}

	/// <summary>
	/// The odds a component is considered for a blast at all, out of the low twelve bits of a draw —
	/// <c>0x802</c> in 4096, a shade over half. It is rolled per component per explosion, which is
	/// what makes splash damage spread rather than flatten.
	/// </summary>
	private const int ComponentBlastOdds = 0x802;

	/// <summary>
	/// The radius of the secondary explosion <c>Mech_ApplyDirectFireDamage</c> touches off with the
	/// shot's splash share — small enough that it reaches the components around the point of impact
	/// and no further.
	/// </summary>
	private const int SecondaryBlastRadius = 500;

	/// <summary>
	/// One component's blast anchor: the shape node it rides and the point it sits at on that node.
	/// Null for a component the <c>.COL</c> never names, which is most of the internals — see
	/// <see cref="ComponentPosition"/>.
	/// </summary>
	private readonly record struct ComponentAnchor(short NodeIndex, short X, short Y, short Z);

	/// <summary>
	/// The per-component anchors, built once from the <c>.COL</c> — see
	/// <see cref="BuildComponentAnchors"/>. Built on demand rather than in the constructor because
	/// the original builds it at loadout time, after both the model and the piece table are in place.
	/// </summary>
	private ComponentAnchor?[]? _componentAnchors;

	/// <summary>
	/// Where a component learns where it is: the machine's <c>.COL</c> is walked cluster by cluster
	/// and each cluster's node and centre are filed under the component index it names. The original
	/// does this once at loadout, writing into two runtime-only fields of the <c>.DMG</c> record —
	/// see docs/simulation/damage-system.md, "Where a component stands".
	///
	/// <para><b>The write is unguarded, so the last cluster naming a component wins.</b> It makes no
	/// difference on retail data — every mech <c>.COL</c> names each component exactly once — but it
	/// is the original's order and the cheap thing to be faithful to.</para>
	///
	/// <para>The centre is the <i>bound</i> of the cluster, not any one sphere: the same figure
	/// <see cref="CollisionModelReader"/> derives at load and the hit test tries before the spheres
	/// under it.</para>
	/// </summary>
	private ComponentAnchor?[] BuildComponentAnchors() {
		var anchors = new ComponentAnchor?[_damage?.Count ?? 0];

		foreach (var node in _collision) {
			foreach (var cluster in node.Clusters) {
				if (cluster.ComponentIndex >= 0 && cluster.ComponentIndex < anchors.Length) {
					anchors[cluster.ComponentIndex] = new ComponentAnchor(
						node.NodeIndex, cluster.Bound.X, cluster.Bound.Y, cluster.Bound.Z);
				}
			}
		}

		return anchors;
	}

	/// <summary>
	/// Where one of this machine's components stands in the world. The blast measures its falloff
	/// from it, and a missile takes its lock node's position through the same accessor.
	///
	/// <para>It reads the anchor <see cref="BuildComponentAnchors"/> filed for the component and puts
	/// the point through the machine's frame, composing the node's posed transform first when the
	/// anchor names one — so a leg's blast point walks with the leg and the torso's swings with the
	/// turret. The node gate is the original's <c>&gt; 0</c> rather than "not the object frame", so
	/// node 0 would be left in the object frame; no retail <c>.COL</c> uses node 0.</para>
	///
	/// <para><b>A component with no anchor answers the machine's own origin</b>, which is at its
	/// feet. That is the original's first branch, not a fallback invented here, and it is what most
	/// of a HERC gets: a mech <c>.COL</c> names between six and sixteen of the 29 slots, so every
	/// internal and — on most chassis — the shoulders are measured from the ground under the machine
	/// rather than from anywhere near where they are. A blast at head height reaches those parts less
	/// readily than the geometry would suggest.</para>
	/// </summary>
	public override Vec3i ComponentWorldPosition(short componentIndex) => ComponentPosition(componentIndex);

	/// <summary>
	/// <c>Mech_NextTargetableComponent</c> (<c>00415558</c>), vtable <c>+0x80</c> — the Targeting
	/// Pod's rotation over <see cref="TargetingPodLock.ComponentRotation"/>, skipping any slot the
	/// machine has lost and wrapping at seven.
	///
	/// <para><b>The slot the cursor starts on is never tested.</b> The walk steps before it looks, and
	/// stops when it comes back to where it began — so a machine with only its current slot left
	/// answers "nothing", and a cursor of <see cref="TargetingPodLock.NoComponent"/> starts from
	/// position 0 and so gives rotation entry 1, component 4, rather than component 0. Both are the
	/// original's.</para>
	/// </summary>
	public override int NextTargetableComponent(int cursor, out int componentId) {
		int start = cursor < 0 ? 0 : cursor;
		int at = start;

		do {
			at++;
			if (at == TargetingPodLock.ComponentRotation.Length) {
				at = 0;
			}
		} while (at != start && !ComponentPresent(TargetingPodLock.ComponentRotation[at]));

		if (at == start) {
			componentId = TargetingPodLock.NoComponent;
			return TargetingPodLock.NoComponent;
		}

		componentId = TargetingPodLock.ComponentRotation[at];
		return at;
	}

	/// <summary>
	/// <c>Mech_ComponentPresent</c> (<c>00415540</c>), vtable <c>+0x84</c> — one entry of the
	/// occupancy array at <c>mech+0x20e</c>, which is <see cref="ComponentDamage.IsActive"/>.
	/// </summary>
	public override bool ComponentPresent(int componentId) => _damage?.IsActive(componentId) ?? false;

	/// <inheritdoc cref="ComponentWorldPosition" />
	private Vec3i ComponentPosition(short componentIndex) {
		var (frame, point) = ComponentFrame(componentIndex);
		return frame.TransformPoint(point.X, point.Y, point.Z);
	}

	/// <summary>
	/// The frame one component is placed by and its anchor point within it. The destruction path
	/// wants both: it throws the component's wreckage from the composed frame, with the component's
	/// own world position dropped into the translation, so the pieces come off pointing the way the
	/// part was pointing rather than the way the machine is.
	/// </summary>
	/// <returns>The node-composed frame, and the anchor point in it.</returns>
	internal (Transform3 Frame, Vec3i Point) ComponentFrame(short componentIndex) {
		_componentAnchors ??= BuildComponentAnchors();

		if (componentIndex < 0 || componentIndex >= _componentAnchors.Length
				|| _componentAnchors[componentIndex] is not { } anchor) {
			return (WorldTransform, default);
		}

		var frame = WorldTransform;
		if (anchor.NodeIndex > 0 && NodeFrame(anchor.NodeIndex) is { } posed) {
			frame = Transform3.Concat(posed, frame);
		}

		return (frame, new Vec3i(anchor.X, anchor.Y, anchor.Z));
	}

	/// <summary>
	/// The frame a destroyed component throws its wreckage from — <see cref="ComponentFrame"/>'s
	/// rotation with the component's world position in the translation, which is exactly the
	/// transform <c>Component_DestroyAndCascade</c> builds on its stack and hands to the throw.
	/// </summary>
	public Transform3 ComponentThrowFrame(short componentIndex) {
		var (frame, point) = ComponentFrame(componentIndex);
		var world = frame.TransformPoint(point.X, point.Y, point.Z);

		frame.X = world.X;
		frame.Y = world.Y;
		frame.Z = world.Z;
		return frame;
	}

	/// <summary>
	/// The weapon-mount half of <c>Mech_ApplyDirectFireDamage</c>: a hit that moved one of the
	/// machine's mount components (<see cref="WeaponMounts.FirstMountComponent"/> and up) into a new
	/// damage band rolls once to take that mount out for good.
	///
	/// <para><b>The odds depend on whose machine it is.</b> The roll is a draw of the low twelve bits
	/// against the side's own figure times <see cref="MountDestructionOddsScale"/> — 3 in
	/// 4096-per-41, about 3%, for the player's side, and 10 for the Cybrids, about 10%. So a Cybrid
	/// machine sheds its guns more than three times as readily as one of ours does.</para>
	///
	/// <para><b>The chassis has to allow it at all</b> — see
	/// <see cref="MechTypeRecord.WeaponMountsDestructible"/>, which the PITBULL alone states zero
	/// for. Its mounts can still be lost the certain way, through
	/// <see cref="WeaponMount.ConditionChanged"/>.</para>
	///
	/// <para>The roll does not run on a component that is <i>already</i> at
	/// <see cref="FullyDamaged"/>: there is nothing left to knock out, and it is that test, not the
	/// mount's own destroyed byte, that keeps a wreck from rolling on every subsequent hit.</para>
	///
	/// <para><b>The order of the three writes matters.</b> The mount is destroyed, then the
	/// component's active flag is cleared, and only then is the component finished off with a flat
	/// 10000 — with the flag already down, that write lands on the damage array but cannot cascade,
	/// so losing a gun does not take the shoulder it hangs off with it. See
	/// <see cref="ComponentDamage.Deactivate"/>.</para>
	///
	/// <para><b>A Cybrid's lost gun is salvage.</b> On a machine of side 1 the mount goes onto the
	/// mission's salvage list (<see cref="SimWorld.QueueSalvage"/>) between the flag write and the
	/// finish-off, with its condition taken from <see cref="MountSalvageScale"/> of the reading: a gun
	/// knocked off a half-wrecked mount comes home in better shape than the mount reads.</para>
	/// </summary>
	/// <param name="damagePercent">The component's reading <i>after</i> the write, 0 pristine and 256 gone.</param>
	private void RollWeaponMountDestruction(SimWorld world, short componentIndex, int damagePercent) {
		if (_damage == null || damagePercent == FullyDamaged
				|| componentIndex < WeaponMounts.FirstMountComponent
				|| !Type.WeaponMountsDestructible) {
			return;
		}

		int odds = Side == MissionSide.Human ? MountDestructionOddsHuman : MountDestructionOddsCybrid;
		if (world.Random.NextMasked(0xfff) >= odds * MountDestructionOddsScale) {
			return;
		}

		var mount = Weapons.ByComponent(componentIndex);
		mount?.Destroy(world, this, rolled: true, DebrisTable(world));
		_damage.Deactivate(componentIndex);
		if (Side == MissionSide.Cybrid && mount != null) {
			world.QueueSalvage((short)mount.WeaponId,
				SalvageCondition(SimMath.Q10Multiply(MountSalvageScale, damagePercent)));
		}

		_damage.ApplyDamage(componentIndex, MountDestructionFinishOff, world, this, DebrisTable(world));
	}

	/// <summary>
	/// The Q10 share of a mount's reading its salvage condition is taken from — the literal 500 at
	/// <c>00418a9b</c>, just under a half.
	/// </summary>
	private const int MountSalvageScale = 500;

	/// <summary>
	/// A Q8 damage reading as the percentage condition the salvage list and the results' status blocks
	/// carry: <c>(0x100 - reading) * 100</c> shifted right 8, arithmetically (<c>SAR</c> at <c>00418ace</c>
	/// and <c>00423dbc</c>, where the decompiler shows an unsigned shift).
	/// </summary>
	internal static short SalvageCondition(int reading) => (short)((short)(0x100 - reading) * 100 >> 8);

	/// <inheritdoc cref="RollWeaponMountDestruction"/>
	private const int MountDestructionOddsHuman = 3;

	/// <inheritdoc cref="RollWeaponMountDestruction"/>
	private const int MountDestructionOddsCybrid = 10;

	/// <inheritdoc cref="RollWeaponMountDestruction"/>
	private const int MountDestructionOddsScale = 0x29;

	/// <summary>
	/// What the mount's component is written off with once the roll succeeds — more than any mount
	/// component's armour, so the slot reads destroyed however healthy it was a moment earlier.
	/// </summary>
	private const short MountDestructionFinishOff = 10000;

	/// <summary>
	/// <c>Mech_ComponentDamageWrite</c> (<c>00417de4</c>), the mech's vtable <c>+0x74</c> — the shared
	/// endpoint both damage pathways converge on, and where the consequences of losing a part are
	/// worked out.
	///
	/// <list type="number">
	/// <item><b>The write itself</b>, through <see cref="ComponentDamage.ApplyDamage"/>, which is what
	/// carries overflow into the component's internals and cascades the parts mounted on it.</item>
	/// <item><b>Shield capacity is recomputed</b>, because it is a function of the shield generator's
	/// own damage — shooting a machine's generator shrinks the array it can hold. The original calls
	/// <c>Mech_ComputeShieldCapacity</c> from here as well as from the spawn path.</item>
	/// <item><b>The legs are graded</b>, from the servo dependents rather than from the leg components
	/// — the front pair on a biped, averaged with the rear pair on the PITBULL. Lose half of them and
	/// the machine is <see cref="Immobilised"/>; short of that it is <see cref="LegsCrippled"/> or
	/// merely hurt. The RAZOR is skipped outright, as a chassis that does not walk.</item>
	/// <item><b>The death test.</b> Either cockpit section fully gone, or life support or the pilot
	/// destroyed, and the machine is dead — at which point the original re-enters this function with a
	/// flat 30000 on component 0 to finish everything else off, which is why a kill leaves a machine
	/// comprehensively wrecked rather than merely stopped.</item>
	/// <item><b>The reactor flags latch</b> off its own dependent. They never clear, and the check is
	/// gated on both being down, so once the first sets the second is only reachable by a single hit
	/// crossing both thresholds at once.</item>
	/// <item><b>Every mount is told what its own component now reads</b>, with the figure from before
	/// the write and the figure from after — see <see cref="WeaponMount.ConditionChanged"/>. It is
	/// this, not the hit, that decides whether a hardpoint survives: the write is made against a
	/// component, and the mount hanging off it finds out here.</item>
	/// </list>
	///
	/// <para><b>The snapshot has to be taken before the write</b> and over <i>all</i> the mounts, not
	/// just the one whose component was struck — the write cascades, so a hit on a shoulder can move
	/// the reading of a mount several components away. The original allocates the same array of
	/// per-mount readings on its own stack for exactly that reason.</para>
	///
	/// <para>The computer's warnings this posts along the way — the shield generator's two, the
	/// weapon-mount one, the leg grade's pair, the reactor's and the kill announcement — are all
	/// gated on <see cref="SimObject.LocallyPiloted"/>, so only the machine the player is flying
	/// says anything. See docs/simulation/component-damage.md.</para>
	/// </summary>
	public override void ApplyComponentDamage(SimWorld world, int componentIndex, short damage,
			SimObject? attacker) =>
		ComponentDamageWrite(world, (short)componentIndex, damage, attacker);

	/// <inheritdoc cref="ApplyComponentDamage"/>
	private void ComponentDamageWrite(SimWorld world, short componentIndex, short damage,
			SimObject? attacker) {
		// Sim_DamageToPlayerDisabled (004240f4), asked at the very top and of nothing else in the
		// simulation. It takes out the whole write, so an invulnerable machine loses no component and
		// none of the consequences below — the death test included — ever run for it. Its shields are
		// not covered: they are spent before the damage reaches here.
		if (LocallyPiloted && world.PlayerInvulnerable) {
			return;
		}

		if (_damage == null || !_damage.IsActive(componentIndex)) {
			return;
		}

		var mounts = Weapons.Mounts.ToList();
		var before = mounts
			.Select(m => _damage.DamagePercent(m.LoadoutSlot + WeaponMounts.FirstMountComponent))
			.ToList();

		// The generator's own reading, for the two warnings below that are differences across the
		// write rather than states after it.
		int generatorBefore = _damage.DependentPercent(ShieldGeneratorDependent);

		// The chassis' own debris table goes in as the installed alternate immediately before the
		// write, exactly where Mech_ComponentDamageWrite installs it: everything the cascade throws
		// reads its high indices against this machine's own wreckage.
		_damage.ApplyDamage(componentIndex, damage, world, this, DebrisTable(world));

		bool mountLost = false;
		for (int i = 0; i < mounts.Count; i++) {
			int after = _damage.DamagePercent(mounts[i].LoadoutSlot + WeaponMounts.FirstMountComponent);
			mountLost |= before[i] < FullyDamaged && after == FullyDamaged;
			mounts[i].ConditionChanged(world.Random, before[i], after, world, this, DebrisTable(world));
		}

		// Once for the write, not once per mount: the original raises its flag inside the walk and
		// posts after it, so a cascade that strips several hardpoints at once says this one line.
		if (mountLost && LocallyPiloted) {
			world.Sounds?.Say(SystemMessages.WeaponDestroyed);
		}

		int generatorAfter = _damage.DependentPercent(ShieldGeneratorDependent);

		// The first-damage line is the else arm of the destroyed test, so the hit that takes an
		// untouched generator out says only that it is destroyed.
		if (LocallyPiloted) {
			if (generatorBefore < FullyDamaged && generatorAfter == FullyDamaged) {
				world.Sounds?.Say(SystemMessages.ShieldGeneratorDestroyed);
			} else if (generatorBefore == 0 && generatorAfter != 0) {
				world.Sounds?.Say(SystemMessages.InternalDamageShieldGenerator);
			}
		}

		// The pod's own term is read live, after the write, for the same reason the generator's is:
		// this write may have been the one that took the pod's hardpoint.
		Shields.SetMax(ShieldCapacity(Type.ShieldCapacity, (short)generatorAfter,
			Pods.ShieldPod, MechPods.DamageOf(Pods.ShieldPodMount, _damage)));

		if (!Type.IsFlyer) {
			GradeLegs(world, attacker);
		}

		if (!Destroyed && (_damage.FullyDestroyed(CockpitFrontComponent)
				|| _damage.FullyDestroyed(CockpitRearComponent)
				|| _damage.DependentPercent(PilotDependent) == FullyDamaged
				|| _damage.DependentPercent(LifeSupportDependent) == FullyDamaged)) {
			_destroyed = true;
			LastAttacker ??= attacker;

			// Before the finish-off, so the announcement is made against the machine as the killing
			// shot left it rather than against the wreck the recursion below makes of it.
			AnnounceNeutralised(world, attacker, this, SystemMessages.EnemyTargetDestroyed);

			// Sampled BEFORE the finish-off, because the finish-off is what makes it stale: the
			// recursion below runs the whole of this function again, GradeLegs included, on a
			// machine whose components have just been written off wholesale. The original keeps the
			// same local across the same call for the same reason, and everything after the
			// recursion reads the answer from before the kill rather than after it.
			bool wasImmobilised = Immobilised;

			// The original's own recursive finish-off, with no attacker so the kill is not credited
			// twice. Destroyed is already set, so this pass cannot re-enter the death branch -- nor
			// can the disabled branch inside GradeLegs, which is guarded on the same flag.
			ComponentDamageWrite(world, CockpitFrontComponent, 30000, null);

			(attacker as MechObject)?.CreditNeutralised(world, this, wasImmobilised);

			// The machine's own mission action -- fired at the one moment it goes out of the fight,
			// which for a machine whose legs went first was already the disabled branch in
			// GradeLegs. This
			// is why it is guarded on the reading from before the kill rather than on Destroyed: the
			// action goes off once per machine, not once per way of stopping it. See
			// SimObject.DefeatAction; this is what brings a retail mission's next wave in. The
			// counter report shares the guard, so it too is made once.
			if (!wasImmobilised) {
				ReportOutOfAction(world);
				ActivateDefeatAction(world);
			}

			// A wreck holds nothing and paints nothing. The setter takes the target's own
			// TargetedBy count back down with it.
			Target = null;
			Scanner = false;

			if (Type.VanishesOnDeath) {
				// The SPIDER, and only the SPIDER: it leaves no wreck. The original sinks it a
				// hundred thousand units under the map and deletes every shadow it owns
				// (00418600-00418650). It is never removed from the object list, and a machine put
				// that far under the terrain is not drawn. The SPIDER's part list is empty, so on
				// retail data the deletion has nothing to do.
				Position = new Vec3i(Position.X, Position.Y, VanishedDepth);
				for (int i = 0; i < Shadows.Count; i++) {
					ReleaseShadow(world, i);
				}

				_shadows = System.Array.Empty<GroundShape?>();
				SetBehaviourState(BehaviourState.InLimbo);
			} else if (!Type.IsFlyer) {
				SetBehaviourState(BehaviourState.Dead);
			}

			// A flyer takes neither branch: nothing installs a state on it, and it keeps whatever
			// it was in. That is the original's own reading of typeRecord+0x4c and +0x50, not an
			// omission here -- see FlyerMovementTick for what actually stops a downed aircraft.
		}

		if (Reactor == ReactorCondition.Intact) {
			int reactor = _damage.DependentPercent(ReactorDependent);
			Reactor = reactor > ReactorCriticalDamage ? ReactorCondition.Critical
				: reactor > ReactorDegradedDamage ? ReactorCondition.Degraded
				: ReactorCondition.Intact;

			// The original holds a latch per band and posts this same line from both. Reaching
			// either one is what announces the reactor, and because the grade is only read while
			// both latches are clear a machine says it once however far it goes on degrading.
			if (Reactor != ReactorCondition.Intact && LocallyPiloted) {
				world.Sounds?.Say(SystemMessages.InternalDamageEngine);
			}
		}
	}

	/// <summary>
	/// The leg half of <c>Mech_ComponentDamageWrite</c>. The readings are the servo dependents' own,
	/// not the leg components': a HERC's legs are graded by what is inside them.
	///
	/// <para>Two things happen here, and only the second is guarded on the machine still being able
	/// to walk. <b>A leg that reads fully destroyed is dropped</b> — the original deletes that leg's
	/// shadow, so <c>Mech_PlaceLegsOnGround</c> skips it and it stops planting; that runs
	/// whatever else is already true of the machine. <b>Half the legs gone
	/// immobilises it</b>, which is the disabled branch: the machine goes out of the fight there and
	/// then, on the same terms a kill does.</para>
	/// </summary>
	private void GradeLegs(SimWorld world, SimObject? attacker) {
		int legCount = Type.LegCount;
		if (_damage == null || legCount == 0) {
			return;
		}

		var slots = legCount > 2
			? FrontLegServoDependents.Concat(RearLegServoDependents).ToArray()
			: FrontLegServoDependents;

		if (_legsLost.Length < legCount) {
			_legsLost = new bool[legCount];
		}

		int destroyed = 0;
		int leg = 0;
		foreach (int slot in slots.Take(legCount)) {
			if (_damage.DependentPercent(slot) == FullyDamaged) {
				destroyed++;
				_legsLost[leg] = true;
				ReleaseShadow(world, leg);
			}

			leg++;
		}

		if (Immobilised) {
			return;
		}

		if (destroyed >= legCount / 2) {
			// The original's order, and it matters: everything that reacts to the machine going out
			// of the fight runs while Immobilised is still clear, so the kill credit and the mission
			// action see the transition rather than the state after it.
			if (!Destroyed) {
				(attacker as MechObject)?.CreditNeutralised(world, this, wasImmobilised: false);
				ReportOutOfAction(world);
				ActivateDefeatAction(world);
				AnnounceNeutralised(world, attacker, this, SystemMessages.EnemyTargetDisabled);
				SetBehaviourState(BehaviourState.Disabled);
			}

			Immobilised = true;
			LastAttacker ??= attacker;
			Target = null;
			return;
		}

		// A four-legged chassis is graded on the average of each side's pair rather than on the front
		// pair alone — the original's own `(front + rear) >> 1`, per side.
		int left = _damage.DependentPercent(FrontLegServoDependents[0]);
		int right = _damage.DependentPercent(FrontLegServoDependents[1]);
		if (legCount == 4) {
			left = (left + _damage.DependentPercent(RearLegServoDependents[0])) >> 1;
			right = (right + _damage.DependentPercent(RearLegServoDependents[1])) >> 1;
		}

		// Each latch is also the warning's own one-shot: it is why a machine that keeps taking hits
		// in the same band does not keep repeating itself, and it is separate from the message
		// port's 4.8 s repeat swallow, which would not be enough on its own.
		if (left >= LegsCrippledDamage || right >= LegsCrippledDamage) {
			if (!LegsCrippled && LocallyPiloted) {
				world.Sounds?.Say(SystemMessages.StructuralFailureImminent);
			}

			LegsCrippled = true;
		} else if (left > LegsDamagedAlert || right > LegsDamagedAlert) {
			if (!LegsDamaged && LocallyPiloted) {
				world.Sounds?.Say(SystemMessages.InternalDamageLegServos);
			}

			LegsDamaged = true;
		}
	}
}
