using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// A flyer or ground vehicle — the <c>SKIMMER</c>/<c>HOVTANK</c>/<c>DROPSHIP</c> class listed in
/// <c>nam\FLYERS.NAM</c>. In DBSIM this is the class constructed by <c>Flyer_Constructor</c> (<c>004215f4</c>) from a
/// <c>script.dat</c> block-8 record and attached to its group by <c>Flyer_AttachToGroup</c> (<c>00421ee8</c>).
///
/// <para>The one behavioural detail carried over from that attach function is the hover height: a
/// flyer does <b>not</b> get the terrain query mechs and structures get — it takes its Z straight
/// from its spawn coordinate, and if that leaves it at zero the original substitutes 5000 world
/// units (30 m). So a flyer starts at an absolute altitude rather than following the ground.</para>
///
/// <para><b>It flies, and it fights.</b> The class has its own seven-state behaviour table and its
/// own three descriptor dispatchers, so the same <c>Mech_AiTick</c> that drives a HERC drives an
/// aircraft — see <see cref="FlyerObject.AiTick"/> — and the flight model underneath it is the one
/// the player's RAZOR uses. See <see cref="FlyerObject.MovementTick"/>.</para>
///
/// <para><b>It is shootable</b>, and by the simplest of the three hit paths: no shields, no volume,
/// just the type's <c>col\&lt;NAME&gt;.COL</c> sphere model and a one-component health record. See
/// <see cref="DirectFireHitTest"/>.</para>
/// </summary>
public sealed partial class FlyerObject : SimObject {
	/// <summary>
	/// Altitude the original substitutes when a flyer's spawn coordinate carries no Z, in world
	/// units. Straight from <c>Flyer_AttachToGroup</c> (<c>00421ee8</c>)'s trailing <c>if (z == 0) z = 5000;</c>.
	/// </summary>
	public const int DefaultHoverHeight = 5000;

	/// <summary>
	/// The effect id a hit on an already-wrecked flyer spawns instead of the shot's own — the
	/// original's literal 10 in <c>Flyer_DirectFireHitTest</c>, the same id the component-destruction
	/// path uses for its ordinary blast.
	/// </summary>
	private const short WreckHitEffect = 10;

	/// <summary>
	/// The debris group an ordinary hit on an aircraft sheds — the original's literal 1, out of
	/// <c>DEF_DEB</c>.
	/// </summary>
	private const short HitDebrisGroup = 1;

	/// <summary>
	/// And the one the hit that brings it down sheds instead — the literal 3, the same group a
	/// crashing RAZOR throws.
	/// </summary>
	private const short WreckDebrisGroup = 3;

	private readonly int _shapeRadius;
	private readonly ColliderNode[] _collision;
	private readonly ComponentDamage? _damage;

	/// <param name="collision">
	/// The type's <c>col\&lt;NAME&gt;.COL</c> hit-sphere model. Empty for two of the three retail
	/// flyer names — only <c>SKIMMER</c> ships one — and a flyer with no model cannot be struck at
	/// all, which is the original's outcome too.
	/// </param>
	/// <param name="shapeRadius">The drawn model's own bound — see <see cref="ShapeRadius"/>.</param>
	/// <param name="damage">
	/// The type's <c>dmg\&lt;NAME&gt;.DMG</c> health record, sized to a flyer's one component and one
	/// dependent. Null alongside a missing <c>.COL</c>, for the same reason.
	/// </param>
	/// <param name="flight">
	/// The type's <c>fm\&lt;NAME&gt;.FM</c>, which the flyer type loader reads into the type record at
	/// <c>+0x3a</c>. Null leaves the aircraft unable to fly at all; only <c>SKIMMER</c> ships one.
	/// </param>
	public FlyerObject(string name, FlyerSimData? simData, int shapeRadius,
			ColliderNode[]? collision = null, ComponentDamage? damage = null,
			FlightModelRecord? flight = null) {
		Name = name;
		SimData = simData;
		_shapeRadius = shapeRadius;
		_collision = collision ?? Array.Empty<ColliderNode>();
		_damage = damage;
		Flight = flight;

		// Flyer_Constructor's own seeding of the flight block: the airspeed and the throttle, in the
		// 0x4e bytes it has just zeroed.
		_flight.BodyVelocity = new Vec3i(0, InitialAirSpeed, 0);
		_flight.Throttle = InitialThrottle;

		InstallInitialBehaviour();
	}

	/// <summary>Base name of the flyer's data files, e.g. <c>SKIMMER</c>.</summary>
	public string Name { get; }

	/// <summary>
	/// The type's stats from <c>dat\&lt;name&gt;.DAT</c>, or null when the install has no such file —
	/// only <c>SKIMMER</c> ships one, so the other two types legitimately have none.
	/// </summary>
	public FlyerSimData? SimData { get; }

	/// <inheritdoc />
	/// <remarks>
	/// <b>Zero</b> — an aircraft has no body radius, so a blast measures it from its origin. Not an
	/// omission here: the original's slot is a <c>return 0</c> stub.
	/// </remarks>
	public override int HitRadius => 0;

	/// <inheritdoc />
	/// <remarks>
	/// The drawn model's own bound, which is what <see cref="DirectFireHitTest"/> rejects against —
	/// a flyer has no radius of its own anywhere else.
	/// </remarks>
	public override int ShapeRadius => _shapeRadius;

	/// <inheritdoc />
	/// <remarks><c>Flyer_Constructor</c> (<c>004215f4</c>) writes 2 at <c>0x004216d8</c>.</remarks>
	public override TargetClass TargetClass => TargetClass.Flyer;

	/// <inheritdoc />
	/// <remarks>A flyer has no legs to lose, so only the destroyed half of the pair applies.</remarks>
	public override bool Neutralised => Destroyed;

	/// <inheritdoc />
	/// <remarks>
	/// An aircraft can answer the third byte of the triple: <see cref="Disarmed"/> is what
	/// <c>Flyer_AiSelectBehaviour</c> (<c>00422d00</c>) latches when the group's order is <c>sleep</c> or <c>travel</c>.
	/// </remarks>
	public override bool OutOfAction => Neutralised || Disarmed;

	/// <summary>
	/// <c>flyer+0xa5</c> — this aircraft is not contesting anything. Latched by the reassess on the
	/// two orders that send a flight somewhere rather than send it to fight; no write in the original
	/// that clears it is known (docs/retail/simulation/ai-flyers.md, Open), and nothing here clears it. Read
	/// by <c>Group_IsWipedOut</c> through <see cref="OutOfAction"/>.
	/// </summary>
	public bool Disarmed { get; set; }

	/// <summary>
	/// <c>obj+0x99</c> — whether the flyer's one component has been destroyed, which for an aircraft
	/// is the whole of it. A wreck still stops shots, for as long as it is anywhere a shot can reach:
	/// see <see cref="ApplyDamage"/> for where the original puts it.
	/// </summary>
	public override bool Destroyed => _destroyed;

	private bool _destroyed;

	/// <summary>
	/// Who landed the shot that finished it — the shooter <see cref="ApplyComponentDamage"/> credits
	/// with the kill.
	/// </summary>
	public SimObject? LastAttacker { get; private set; }

	/// <summary>Per-component health, or null for a type the install ships no <c>.DMG</c> for.</summary>
	public ComponentDamage? Damage => _damage;

	/// <inheritdoc />
	public override int OverallDamage => _damage?.OverallDamage ?? 0;

	/// <inheritdoc />
	public override ShapeCellFrames? CellFrames => _damage?.CellFrames;

	/// <summary>
	/// The flyer's shape-to-world transform — its full attitude, bank and pitch included, with its
	/// world position in the translation. See <see cref="WorldFrame"/>.
	/// </summary>
	public Transform3 WorldTransform => WorldFrame;

	/// <summary>
	/// <c>Flyer_DirectFireHitTest</c> (<c>00421c8c</c>), the flyer's vtable <c>+0x20</c> — the
	/// shortest of the three hit tests in the simulation, and the only one with no second piece of
	/// geometry behind it.
	///
	/// <list type="number">
	/// <item><b>The same coarse reject</b> every hit test opens with: muzzle to aircraft, against the
	/// ray's length plus the shape's radius plus the shot's clearance.</item>
	/// <item><b>The sphere model</b>, straight away — <see cref="CollisionModel.Test"/> against the
	/// type's <c>.COL</c>, in the flyer's own frame. There are no shields to absorb anything first
	/// and no collision volume to fall back on.</item>
	/// <item><b>Damage</b> to whichever component the geometry named, which for a flyer is always the
	/// only one it has.</item>
	/// </list>
	///
	/// <para>The effect a hit spawns is the shot's <see cref="WeaponShot.ImpactFxGroup.Armor"/> array
	/// — the same array a structure hit uses — <b>unless the flyer is already a wreck</b>, in which
	/// case the original substitutes a fixed effect id of its own instead of anything the shot
	/// carries.</para>
	///
	/// <para>Wreckage follows every hit, and the hit that brings the aircraft down throws a different
	/// group — see docs/retail/simulation/hit-detection.md, "<c>Flyer_DirectFireHitTest</c>".</para>
	/// </summary>
	public override int DirectFireHitTest(SimWorld world, WeaponShot shot) {
		var muzzle = new Vec3i(shot.Muzzle.X, shot.Muzzle.Y, shot.Muzzle.Z);
		if (_shapeRadius + shot.Clearance + shot.Distance < Position.ApproxDistanceTo(muzzle)) {
			return 0;
		}

		var hit = CollisionModel.Test(
			_collision, Transform3.Concat(WorldTransform, shot.MuzzleInverse),
			shot.Distance, shot.Clearance, ComponentAlive);

		if (hit is not { } struck) {
			return 0;
		}

		// The effect is picked before the damage lands, which matters only for the generator: the
		// original draws here whether or not the wreck substitution below throws the result away.
		short effect = world.Effects.PickImpactEffect(shot.ImpactFx(WeaponShot.ImpactFxGroup.Armor));

		int struckAt = struck.Distance + 1;
		ApplyDamage(struck.ComponentIndex, shot.DamageArmor, shot.Owner, world);

		var point = shot.Muzzle.TransformPoint(0, struckAt, 0);
		// With no owner, unlike the mech and structure hit tests: an effect on a flyer is drawn whatever
		// the camera is attached to.
		world.Effects.SpawnImpactEffect(Destroyed ? WreckHitEffect : effect, point, owner: null);

		// And wreckage off the same point, with the same substitution: an ordinary hit sheds
		// HitDebrisGroup, and the hit that brings the aircraft down sheds WreckDebrisGroup instead.
		// Both are DEF_DEB's, a flyer having no debris table of its own.
		world.Effects.SpawnDebris(Destroyed ? WreckDebrisGroup : HitDebrisGroup, point, installed: null);

		return struckAt;
	}

	/// <summary>
	/// What a blast does to an aircraft — and it is the shared base implementation rather than
	/// anything of the flyer class's own, which is why it is so much simpler than a machine's or a
	/// structure's. No roll, no component selection, no shields: the whole aircraft is one point and
	/// the damage lands on component 0, the only one it has.
	///
	/// <para>The falloff is measured from the object's <b>surface</b> — <see cref="HitRadius"/> is
	/// subtracted, exactly as the sweep subtracts it before deciding the aircraft was in range at
	/// all. For an aircraft that is a no-op, a flyer's being zero, but it is written out because the
	/// implementation is shared: a class with a real body radius takes more than the blast's face
	/// value at point-blank range.</para>
	///
	/// <para>There is no range test. The sweep has already made it, so the numerator cannot come out
	/// negative. See docs/retail/simulation/damage-system.md, "A flyer".</para>
	/// </summary>
	public override void ExplosiveDamage(SimWorld world, short damage, Vec3i hitPoint, int blastRadius,
			SimObject? attacker) {
		if (blastRadius <= 0) {
			return;
		}

		int distance = Position.ApproxDistanceTo(hitPoint) - HitRadius;
		ApplyDamage(0, (short)((blastRadius - distance) * damage / blastRadius), attacker, world);
	}

	/// <summary>
	/// Whether one of the type's components is still standing. A flyer with no <c>.DMG</c> has none,
	/// so nothing of it is shootable — which is what leaves <c>HOVTANK</c> and <c>DROPSHIP</c>
	/// untouchable on retail data.
	/// </summary>
	private bool ComponentAlive(int index) => _damage?.IsActive(index) ?? false;

	/// <summary>
	/// <c>Flyer_ComponentDamageWrite</c> (<c>00421bb4</c>), the flyer's vtable <c>+0x74</c> — a thin wrapper over
	/// <see cref="ComponentDamage.ApplyDamage"/> that turns the loss of component 0 into the loss of
	/// the aircraft.
	///
	/// <para>Unlike a mech, which weighs limbs and cockpit sections against each other before it
	/// decides it is dead, a flyer's death test is that one component index. What follows the flag is
	/// the aircraft leaving: it goes into its behaviour table's <c>dead</c> state, which has neither
	/// a think nor a move, and <b>its Z is set to -100000 in the same breath</b> — so the wreck drops
	/// straight out of the world rather than hanging where it was shot. Nothing moves it afterwards,
	/// because the state it is now in has no move slot to clamp it back to the ground.</para>
	///
	/// <para>The wreckage the cascade sheds inherits the aircraft's world velocity, through the
	/// global <see cref="EffectPools.DebrisCarrierVelocity"/> the original points at
	/// <c>flyer+0x24f</c> for the length of this call. The loss also plays the alert for the player's
	/// own selected target going down, on the guard all three damage endpoints share, credits the
	/// shooter through its <c>+0x60</c> slot (<see cref="MechObject.CreditNeutralised"/>), and fires
	/// the flyer's mission action — see <see cref="SimObject.DefeatAction"/>.</para>
	/// </summary>
	public override void ApplyComponentDamage(SimWorld world, int componentIndex, short damage,
			SimObject? attacker) =>
		ApplyDamage(componentIndex, damage, attacker, world);

	/// <inheritdoc cref="ApplyComponentDamage"/>
	private void ApplyDamage(int componentIndex, short damage, SimObject? attacker,
			SimWorld? world = null) {
		if (_damage == null) {
			return;
		}

		if (world != null) {
			world.Effects.DebrisCarrierVelocity = FlightWorldVelocity;
		}

		bool lost = _damage.ApplyDamage(componentIndex, damage, world, this);

		if (world != null) {
			world.Effects.DebrisCarrierVelocity = default;
		}

		if (!lost || componentIndex != 0) {
			return;
		}

		bool wasDestroyed = _destroyed;

		// Flyer_ComponentDamageWrite's own order: the announcement and the shooter's kill credit go
		// out before the destroyed flag is set, the mission action after the drop, and the dead state
		// and the flyby stop last.
		if (!wasDestroyed && world != null) {
			AnnounceNeutralised(world, attacker, this, SystemMessages.EnemyTargetDestroyed);
			(attacker as MechObject)?.CreditNeutralised(world, this, wasImmobilised: false);
		}

		_destroyed = true;
		LastAttacker = attacker;
		Position = new Vec3i(Position.X, Position.Y, WreckDropHeight);

		if (!wasDestroyed && world != null) {
			ReportOutOfAction(world);
			ActivateDefeatAction(world);
		}

		SetBehaviourState(Ai.FlyerBehaviourState.Dead);
		StopFlybySound(world);
	}

	/// <summary>
	/// Where a shot-down aircraft is put — the original's literal <c>-100000</c> into
	/// <c>flyer+0x2e</c>, which is the object's world Z and not a rate.
	/// </summary>
	private const int WreckDropHeight = -100000;

	/// <summary>
	/// The object pass' tick, which for an aircraft is its behaviour descriptor's <c>+0x24</c> move
	/// slot — <see cref="MovementTick"/> for the four states that carry one, and nothing at all for
	/// <c>deciding</c>, <c>sleeping</c> and <c>dead</c>, whose triples are null. The think runs from
	/// the group pass that follows, in <see cref="AiTick"/>, so the aircraft is integrated on the
	/// previous think's decisions — the original's own ordering.
	/// </summary>
	public override void Tick(SimWorld world) {
		if (Behaviour.State is { Moves: true } && Flight != null) {
			MovementTick(world);
		}
	}
}
