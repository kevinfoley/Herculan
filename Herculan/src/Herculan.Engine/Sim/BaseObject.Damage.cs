using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// Taking fire: the direct-fire hit test every structure class shares (<c>Base_DirectFireHitTest</c>,
/// <c>00405038</c>) and its volume raycast, the damage write (<c>Base_ApplyDamage</c>, <c>00404d70</c>),
/// the blast path, and where each component stands for a blast or a missile's lock.
/// </summary>
public sealed partial class BaseObject {
	/// <summary>Whether one of the type's components is still standing.</summary>
	public bool ComponentAlive(int index) =>
		index >= 0 && index < _alive.Length && _alive[index];

	/// <summary>Damage taken by one component, against its <see cref="BaseComponentType.MaxDamage"/>.</summary>
	public int ComponentDamage(int index) =>
		index >= 0 && index < _damage.Length ? _damage[index] : 0;

	/// <summary>
	/// <c>Base_DamageFraction</c> (<c>004052b4</c>), the type's vtable <c>+0x40</c> — how far gone the structure is, as a Q8
	/// fraction: the sum of every component's damage over the sum of every component's maximum. A
	/// full 256 is what <see cref="ApplyDamage"/> tests for to decide the structure has fallen.
	///
	/// <para>Because it is a <i>ratio of sums</i> rather than a count of destroyed components, a
	/// structure with one 30000-point core and six small parts is effectively destroyed by killing
	/// the core alone — which is exactly how the two seven-component retail types are authored.</para>
	/// </summary>
	public int DamageFraction {
		get {
			int damage = 0;
			int maximum = 0;
			for (int i = 0; i < Type.Components.Length; i++) {
				damage += _damage[i];
				maximum += Type.Components[i].MaxDamage;
			}

			return maximum == 0 ? 0 : (damage << 8) / maximum;
		}
	}

	/// <inheritdoc />
	public override int OverallDamage => DamageFraction;

	/// <summary>The Q8 value <see cref="DamageFraction"/> reaches when nothing is left standing.</summary>
	public const int FullyDestroyed = 0x100;

	/// <summary>
	/// <c>Base_DirectFireHitTest</c> (<c>00405038</c>) — the vtable <c>+0x20</c> every structure
	/// class shares (all five of the type-switched vtables <c>Base_Construct</c> (<c>00405314</c>) installs point at it),
	/// and, as everywhere else along this path, the hit test and the damage application in one call.
	///
	/// <para><b>There are two completely different pieces of hit geometry</b>, and which one runs is
	/// the type's <see cref="BaseType.HasCollisionModel"/> flag:</para>
	/// <list type="bullet">
	/// <item><b>The sphere model</b> — <c>dat\BASECOL.DAT</c>'s hand-authored clusters, one per
	/// destructible component. This is the path that can say <i>which part</i> of a building was
	/// struck, so it is the one that makes a structure come apart section by section. 25 of the 65
	/// retail types use it.</item>
	/// <item><b>The shape's collision volume</b> — the coarse height field in the <c>.DGS</c> record
	/// (see <see cref="ShapeVolume"/>), which knows only "solid here". Everything it hits is
	/// component 0.</item>
	/// </list>
	///
	/// <para>A <see cref="Wrecked"/> structure is switched over to the volume path whichever it was
	/// using — the wreck is a different shape with different geometry, and its spheres would be the
	/// standing building's. Before component 0 reaches its collapse stage a fallen sphere-model
	/// structure is still on the sphere path, where every cluster belongs to a destroyed component and
	/// is skipped.</para>
	///
	/// <para>It also rolls <see cref="HitDebrisOdds"/> in 4096 on every hit — a shade over a quarter —
	/// to shed <see cref="HitDebrisGroup"/> off the impact point.</para>
	/// </summary>
	public override int DirectFireHitTest(SimWorld world, WeaponShot shot) {
		short component = -1;
		short damage = shot.DamageArmor;
		int struckAt;

		if (Wrecked || !Type.HasCollisionModel) {
			struckAt = VolumeStruck(shot);

			// The one consumer of the plasma round's stash: a shot that arrives with both damage
			// figures at zero has been emptied by WeaponShot.StashDamage, and this branch — the volume
			// path only, never the sphere path — puts the armour figure back. See there.
			if (struckAt != 0 && shot.DamageArmor == 0 && shot.DamageShield == 0) {
				damage = shot.StashedDamageArmor;
			}
		} else if (!WithinReach(shot.Muzzle, shot.Distance, shot.Clearance)) {
			return 0;
		} else {
			// A node-placed cluster is read in its node's posed frame, so the turret and dish clusters
			// of the six animated types that carry them follow the animation.
			var hit = CollisionModel.Test(
				_collision, Transform3.Concat(WorldTransform, shot.MuzzleInverse),
				shot.Distance, shot.Clearance, ComponentAlive, NodeFrame);

			struckAt = hit is { } found ? found.Distance + 1 : 0;
			component = hit?.ComponentIndex ?? -1;
		}

		if (struckAt == 0) {
			return 0;
		}

		// The damage goes in before the effect, and only while the structure is standing: a wreck is
		// still solid and still stops shots, it just has nothing left to lose.
		if (!Destroyed) {
			ApplyDamage(world.Random, component, damage, shot.Owner, world);
		}

		// Always the armour array. Unlike a mech, a structure has no shields to flash and no
		// component health band to fall through, so this is the one branch that exists.
		var point = shot.Muzzle.TransformPoint(0, struckAt, 0);
		world.SpawnPickedImpactEffect(shot.ImpactFx(WeaponShot.ImpactFxGroup.Armor), point, this);

		if (world.Random.NextMasked(0xfff) < HitDebrisOdds) {
			world.SpawnDebris(HitDebrisGroup, point, StructureDebris(world));
		}

		return struckAt;
	}

	/// <inheritdoc cref="DirectFireHitTest" />
	private const int HitDebrisOdds = 0x401;

	/// <inheritdoc cref="DirectFireHitTest" />
	private const short HitDebrisGroup = 1;

	/// <summary>
	/// <c>BASE_DEB</c> — the debris table the structure paths install as the alternate database
	/// before every throw they make, exactly as <c>Base_ThrowDebris</c> (<c>0040379c</c>) installs
	/// <c>g_DebrisStructure</c>. Null when the install has no such table.
	/// </summary>
	private static DebrisDatabase? StructureDebris(SimWorld world) =>
		world.Debris?.Database(DebrisDatabase.StructureName);

	/// <summary>
	/// <c>Base_DirectFireHitTest</c>'s volume path: <see cref="VolumeRaycast"/> against the shot's ray,
	/// with the hit test's own <c>+ 1</c> on the distance.
	/// </summary>
	/// <returns>How far along the ray the volume was entered, plus one, or zero for a miss.</returns>
	private int VolumeStruck(WeaponShot shot) =>
		VolumeRaycast(shot.Muzzle, shot.MuzzleInverse, shot.Distance, shot.Clearance, out int struckAt)
			? struckAt + 1
			: 0;

	/// <summary>
	/// The per-object body of <c>Sim_RaycastShapeVolume</c> (<c>00427da8</c>): one ray against this
	/// structure's collision volume. <c>Base_DirectFireHitTest</c> runs it on the one structure it is
	/// called on (<see cref="VolumeStruck"/>), and <see cref="SimWorld.RaycastShapes"/> over the
	/// structure list.
	///
	/// <para>Two rejects before any grid work: <see cref="WithinReach"/>, then the structure's centre
	/// brought into the ray's frame and tested against a box — <b>X and Y only</b>, with Z left out
	/// entirely, which is the original's own test and not an omission here. Only then is the ray
	/// brought into shape space and marched (docs/retail/simulation/hit-detection.md, "The collision
	/// volume").</para>
	///
	/// <para>The original also lowers a global minimum distance here (<c>004aab54</c>). Its only other
	/// writer zeroes it and every distance is non-negative, so it never holds anything but zero, and
	/// it is not carried.</para>
	/// </summary>
	/// <param name="muzzle">The ray's frame: its start in the translation, running down its Y axis.</param>
	/// <param name="muzzleInverse">World to ray space — <paramref name="muzzle"/> inverted.</param>
	/// <param name="distance">The ray's length.</param>
	/// <param name="clearance">The ray record's <c>+0x08</c> — see <see cref="WeaponShot.Clearance"/>.</param>
	/// <param name="struckAt">
	/// How far from the start the volume was entered, measured in shape space; zero on a miss, and
	/// zero on a hit whose start is already inside the volume.
	/// </param>
	internal bool VolumeRaycast(in Transform3 muzzle, in Transform3 muzzleInverse, int distance,
			int clearance, out int struckAt) {
		struckAt = 0;

		if (CurrentVolume is not { IsSolid: true } volume
				|| !WithinReach(muzzle, distance, clearance)) {
			return false;
		}

		int reach = ShapeRadius + clearance;
		int limit = distance + reach;

		var center = muzzleInverse.TransformPoint(Position.X, Position.Y, Position.Z);
		if (center.X >= reach || center.X <= -reach || center.Y <= -reach || center.Y >= limit) {
			return false;
		}

		var toShapeSpace = WorldTransform.Inverted();
		var start = toShapeSpace.TransformPoint(muzzle.X, muzzle.Y, muzzle.Z);
		var far = muzzle.TransformPoint(0, distance, 0);
		var end = toShapeSpace.TransformPoint(far.X, far.Y, far.Z);

		if (!volume.Raycast(start, end, clearance, out var hit)) {
			return false;
		}

		struckAt = hit.ApproxDistanceTo(start);
		return true;
	}

	/// <summary>
	/// The coarse reject both hit paths open with, and the same one every hit test in the simulation
	/// starts from: ray start to structure, against the ray's remaining length plus the shape's
	/// radius plus the ray's clearance. It keeps the transform work off everything nowhere near the
	/// ray.
	/// </summary>
	private bool WithinReach(in Transform3 muzzle, int distance, int clearance) =>
		Position.ApproxDistanceTo(new Vec3i(muzzle.X, muzzle.Y, muzzle.Z))
			<= ShapeRadius + clearance + distance;

	/// <summary>
	/// Whether <c>Sim_RaycastShapeList</c> (<c>00404bc0</c>) gathers this structure for
	/// <see cref="SimWorld.RaycastShapes"/>: <b>a static type always, an animated type only once it is
	/// <see cref="Wrecked"/></b>. It is <see cref="CollisionRadius"/>'s test read the other way round,
	/// so a structure is seen either by the AI's shape probes or by the machine sweep that reads a
	/// collision radius, never both (docs/retail/simulation/ai-navigation.md, "The two probes").
	/// </summary>
	internal bool InShapeList => Type.Source != BaseShapeSource.AnimatedLibrary || Wrecked;

	/// <summary>
	/// <c>Base_ApplyDamage</c> (<c>00404d70</c>), the vtable <c>+0x74</c> — writes one
	/// component's health and, if that finished it, checks whether the structure has fallen.
	///
	/// <para><b>A component can die early, at random.</b> Past half its maximum, the original rolls
	/// once per tenth of the component's health the shot moved it through, each roll a 10% chance of
	/// finishing it outright (<c>rand &amp; 0xfff &lt;= 0x199</c>). So a big hit on a half-wrecked
	/// section is likely to bring it down before its stated hit points run out, and the same hit
	/// twice does not do the same thing.</para>
	///
	/// <para><b>Nothing here tests <see cref="Destroyed"/></b>, so the fallen-structure branch runs
	/// whenever the fraction comes out full. A part that is alive at full damage is the only way to
	/// reach it twice, and only <see cref="ApplyStartingCondition"/> leaves one.</para>
	/// </summary>
	/// <param name="random">The simulation's shared generator, for the early-destruction roll.</param>
	/// <param name="componentIndex">
	/// Which component was struck, or <c>-1</c> for "the hit geometry could not say", which the
	/// original resolves to component 0 rather than dropping the damage.
	/// </param>
	/// <param name="damage">The shot's armour damage.</param>
	/// <param name="attacker">Who fired, recorded on the component that falls and credited with the kill if the structure falls.</param>
	/// <param name="world">
	/// The running world, when the caller has one — needed only so the structure can fire its own
	/// mission action the moment it is destroyed. See <see cref="SimObject.DefeatAction"/>.
	/// </param>
	public void ApplyDamage(SimRandom random, int componentIndex, int damage, SimObject? attacker,
			SimWorld? world = null) {
		if (Type.Invulnerable) {
			return;
		}

		int index = componentIndex == -1 ? 0 : componentIndex;
		if (index < 0 || index >= _alive.Length || !_alive[index]) {
			return;
		}

		var component = Type.Components[index];
		int taken = _damage[index] + damage;
		bool destroyed = component.MaxDamage <= taken;

		if (!destroyed && component.MaxDamage / 2 < taken) {
			int tenth = SimMath.Q16Divide(10, component.MaxDamage);
			int from = SimMath.Q16Multiply(_damage[index], tenth);
			int to = SimMath.Q16Multiply(taken, tenth);
			while (to > from) {
				bool finished = random.NextMasked(0xfff) <= 0x199;
				from++;
				if (finished) {
					destroyed = true;
					break;
				}
			}
		}

		if (!destroyed) {
			_damage[index] = taken;
			return;
		}

		_damage[index] = component.MaxDamage;
		_alive[index] = false;
		_attackers[index] = attacker;

		if (DamageFraction == FullyDestroyed) {
			// Base_ApplyDamage's own order: the announcement that what the player was shooting at has
			// come down and the shooter's kill credit go out before the destroyed flag is set, and the
			// structure's mission action last. See SimObject.AnnounceNeutralised,
			// MechObject.CreditNeutralised and SimObject.DefeatAction.
			if (world != null) {
				AnnounceNeutralised(world, attacker, this, SystemMessages.EnemyTargetDestroyed);
				(attacker as MechObject)?.CreditNeutralised(world, this, wasImmobilised: false);
			}

			_destroyed = true;
			_scannerActive = false;

			if (world != null) {
				ReportOutOfAction(world);
				ActivateDefeatAction(world);
			}
		}

		// And the part starts to fall. A component that names no sequence is simply gone the instant
		// its health runs out; one that names a sequence is given that many stages and the first
		// interval, and DeathSequenceTick takes it from there.
		if (StructureDeathSequence.At(component.DestroyedEffect) is { } sequence) {
			_deathStage[index] = sequence.StageCount;
			_deathTimer[index] = StructureDeathSequence.StageInterval;
		}
	}

	/// <inheritdoc />
	/// <remarks>The structure's slot is <see cref="ApplyDamage"/>.</remarks>
	public override void ApplyComponentDamage(SimWorld world, int componentIndex, short damage,
			SimObject? attacker) =>
		ApplyDamage(world.Random, componentIndex, damage, attacker, world);

	/// <summary>
	/// What a blast does to a building. It shares nothing with a machine's but the shape of the
	/// falloff: a structure has no shields to absorb anything, no facing to be caught from behind,
	/// and its parts stand where the type record says rather than where an animation has put them.
	///
	/// <para><b>Every live component is measured, not a random subset.</b> The original's
	/// per-component draw is compared against a ceiling one above the largest value its mask can
	/// produce, so the test never fails. The draw is kept because it advances the shared generator,
	/// which is what everything downstream of it sees.</para>
	///
	/// <para>Two gates come first, the same two the hit test opens with: an
	/// <see cref="BaseType.Invulnerable"/> type takes nothing, and a structure is blast-damageable
	/// only through its <c>BASECOL.DAT</c> model — <b>a type without one stands in a blast untouched
	/// however close it is</b>, where direct fire would still hurt it through the shape's collision
	/// volume. A <see cref="Wrecked"/> structure is skipped for the reason the hit test switches it to
	/// the volume path: the spheres belong to the building that used to be there.</para>
	/// </summary>
	public override void ExplosiveDamage(SimWorld world, short damage, Vec3i hitPoint, int blastRadius,
			SimObject? attacker) {
		if (Type.Invulnerable || Wrecked || !Type.HasCollisionModel || blastRadius <= 0) {
			return;
		}

		for (int i = 0; i < Type.Components.Length; i++) {
			if (!_alive[i] || world.Random.NextMasked(0xfff) >= BlastConsiderationCeiling) {
				continue;
			}

			int distance = ComponentPosition(i).ApproxDistanceTo(hitPoint);
			if (distance >= blastRadius) {
				continue;
			}

			ApplyDamage(world.Random, i, (blastRadius - distance) * damage / blastRadius, attacker, world);
		}
	}

	/// <summary>
	/// The ceiling the blast's per-component draw is compared against — <c>0x1004</c>, against a draw
	/// masked to <c>0xfff</c>. Written out rather than folded away so the one place the original
	/// could have meant otherwise stays visible; see <see cref="ExplosiveDamage"/>.
	/// </summary>
	private const int BlastConsiderationCeiling = 0x1004;

	/// <summary>
	/// Where one of this building's parts stands in the world, which is what a blast measures its
	/// falloff from. The component's own <see cref="BaseComponentType.Position"/> put through
	/// <see cref="WorldTransform"/>, and nothing else: unlike a machine's there is no node to resolve
	/// and no animation to consult, because a structure's parts do not move.
	///
	/// <para>The type record is the only place the point comes from — the <c>BASECOL.DAT</c> spheres
	/// a shot is tested against are <i>not</i> consulted here, and several types put a component's
	/// blast point above the geometry that stops bullets.</para>
	/// </summary>
	public Vec3i ComponentPosition(int index) {
		if (index < 0 || index >= Type.Components.Length) {
			return Position;
		}

		var local = Type.Components[index].Position;
		return WorldTransform.TransformPoint(local.X, local.Y, local.Z);
	}

	/// <summary><c>Base_ComponentPosition</c> (<c>00406808</c>), vtable <c>+0x58</c> — <see cref="ComponentPosition"/>.</summary>
	public override Vec3i ComponentWorldPosition(short componentIndex) => ComponentPosition(componentIndex);

	/// <summary>
	/// <c>Base_FirstLiveComponent</c> (<c>00406868</c>), vtable <c>+0x54</c>, with a null second
	/// argument: the first component still standing, or −1 when none is. The AI's fire path shoots a
	/// structure there (docs/retail/simulation/ai-weapons.md).
	/// </summary>
	public int FirstLiveComponent() {
		for (int i = 0; i < Type.Components.Length; i++) {
			if (_alive[i]) {
				return i;
			}
		}

		return -1;
	}

	/// <summary>
	/// <c>Base_FirstLiveComponent</c> (<c>00406868</c>) handed a pose — the same walk over the
	/// type's components as <see cref="FirstLiveComponent"/>, but taking the live one with the least
	/// <see cref="SimObject.AimOffset"/>, the first of equals, rather than the first live one. See
	/// docs/retail/simulation/rockets.md, "Spawning".
	/// </summary>
	public override short ComponentNearestAim(Vec3i from, (short X, short Y, short Z) attitude) {
		int best = NoAimOffset;
		short chosen = -1;

		for (short i = 0; i < Type.Components.Length; i++) {
			if (!_alive[i]) {
				continue;
			}

			int offset = AimOffset(ComponentPosition(i), from, attitude);
			if (offset < best) {
				best = offset;
				chosen = i;
			}
		}

		return chosen;
	}
}
