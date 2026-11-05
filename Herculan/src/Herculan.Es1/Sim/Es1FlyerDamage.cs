using Herculan.Engine.Numerics;
using Herculan.Es1.Messages;

namespace Herculan.Es1.Sim;

/// <summary>
/// A flyer's hits and death (docs/es1/simulation/flyers.md#hits-and-death): the hit test, its slot <c>+0x24</c>, and the
/// damage write it ends in. A flyer has no shields, no splash and no blasts.
/// </summary>
public static class Es1FlyerDamage {
	/// <summary>The first entry of the shot's effect array a hit draws from, the armor's four.</summary>
	public const int ArmorEffects = 8;

	/// <summary>The debris group a hit throws, and the effect and the group of the hit that kills.</summary>
	public const int HitDebrisGroup = 1, KillDebrisGroup = 3;

	/// <inheritdoc cref="HitDebrisGroup"/>
	public const short KillEffect = 10;

	/// <summary>The computer message the player's machine posts when it kills the flyer it has targeted.</summary>
	public const short KillMessage = Es1MechOutOfAction.DestroyedMessage;

	/// <summary>Where a dead flyer goes, out of the world, so the frame's draw leaves it out.</summary>
	public const int DeathDepth = Es1MechOutOfAction.LimboDepth;

	/// <summary>
	/// <c>Flyer_DirectFireHitTest</c> (<c>ES1DBSIM 0001b370</c>), slot <c>+0x24</c>, (<paramref name="flyer"/>,
	/// <paramref name="shot"/>):
	/// <list type="number">
	/// <item>0 when the ray's start is farther from the flyer (<c>Math_DistanceBetweenPoints</c>) than its radius (slot
	/// <c>+0x14</c>) plus the ray's length and clearance;</item>
	/// <item>the shot's armor figure <c>+4</c> through <see cref="Es1MechDamage.ScaleByDifficulty"/> when the shooter's group's
	/// side is 0;</item>
	/// <item>the struck component (<see cref="Es1HitSpheres.SelectStruckComponent"/>) on the type's collision model and the
	/// occupancy flags, in the flyer's frame through the ray's; a miss is 0;</item>
	/// <item>a hit draws the generator for the effect, entry <see cref="ArmorEffects"/> + the draw &amp; 3 of the shot's
	/// effects, and debris group <see cref="HitDebrisGroup"/>; then <see cref="ComponentDamageWrite"/>(flyer, shooter, the
	/// armor figure, the component), whose 1 makes them <see cref="KillEffect"/> and <see cref="KillDebrisGroup"/>; then the
	/// effect and the debris at the ray's frame applied to (0, the hit distance + 1, 0).</item>
	/// </list>
	/// Answers the hit distance + 1, or 0. Retail also copies and scales the shot's shield figure <c>+6</c>, which it does
	/// not read again; that is not kept here.
	/// </summary>
	public static int HitTest(Es1Flyer flyer, Es1Shot shot, Es1SimGlobals globals) {
		var ray = shot.Ray;
		if (flyer.Position.ApproxDistanceTo(ray.Start) > ray.Clearance + ray.Length + flyer.ShapeRadius) {
			return 0;
		}

		short armorFigure = shot.Armor;
		// Retail reads the shooter's group unchecked; a shooter with none is not scaled here.
		if (shot.Owner.Group?.Side == 0) {
			armorFigure = Es1MechDamage.ScaleByDifficulty(armorFigure, globals);
		}

		// A type without its .DMG, where retail asserts at the load, has no header and is never struck here.
		if (flyer.Damage is not { } header) {
			return 0;
		}

		// SKIMMER.COL's one node has part id −1, so no node is read. Retail would ask a node of the flyer's static
		// instance for a part id that is not negative; no retail flyer has one, and the frame alone is taken here.
		var toMuzzle = Transform3.Concat(flyer.Frame, ray.WorldToRay);
		if (Es1HitSpheres.SelectStruckComponent(header.Model, header.Occupied, toMuzzle, ray, static _ => null) is not { } hit) {
			return 0;
		}

		var (hitDistance, component) = hit;
		int k = globals.Random.Next() & 3;
		short? effect = shot.Effects?.ImpactFXArmor[k];
		int debrisGroup = HitDebrisGroup;
		int distance = hitDistance + 1;
		if (ComponentDamageWrite(flyer, shot.Owner, armorFigure, component, globals)) {
			effect = KillEffect;
			debrisGroup = KillDebrisGroup;
		}

		var point = ray.Frame.TransformPoint(0, distance, 0);
		// Explosion_Construct (00005af0)(null, point, effect); a shot with no effects makes none on a hit that does not kill.
		if (effect is { } type) {
			globals.Explosions.Construct(point, type, globals.Random, globals.Sound);
		}

		// Debris_ThrowGroup (00005fb8)(group of 000bc1c0, point, 000bc1c6).
		globals.Debris.ThrowGroup(debrisGroup, point, globals.Random);
		return distance;
	}

	/// <summary>
	/// <c>Flyer_ComponentDamageWrite</c> (<c>0001b290</c>)(<paramref name="flyer"/>, <paramref name="attacker"/>,
	/// <paramref name="damage"/>, <paramref name="component"/>): <see cref="Es1DamageHeader.ApplyDamageAndCascade"/>, and
	/// when that answers 1 for component 0:
	/// <list type="number">
	/// <item><see cref="KillMessage"/> when the attacker is <c>LocalPlayerMech</c> and the flyer that machine's target;</item>
	/// <item>the attacker's slot <c>+0x64</c> (<see cref="IEs1SimObject.CreditNeutralisedTarget"/>), not already
	/// immobilized;</item>
	/// <item>destroyed, the radar off, <see cref="Es1Flyer.Byte32Set"/>, and the Z <see cref="DeathDepth"/>;</item>
	/// <item>the report (<see cref="Es1Flyer.ReportOutOfAction"/>) and the <c>+0x170</c> action activated when there is
	/// one;</item>
	/// <item><c>dead</c> installed.</item>
	/// </list>
	/// Answers whether it did. Retail calls the attacker's slot with no null test; its one caller,
	/// <see cref="HitTest"/>, passes the shot's owner, which is never null here.
	/// </summary>
	public static bool ComponentDamageWrite(Es1Flyer flyer, IEs1SimObject attacker, short damage, short component,
			Es1SimGlobals globals) {
		if (flyer.Damage is not { } header || !header.ApplyDamageAndCascade(damage, component, globals) || component != 0) {
			return false;
		}

		if (globals.LocalPlayerMech is { } local && ReferenceEquals(attacker, local) && ReferenceEquals(local.Target, flyer)) {
			Es1MessageTicker.ComputerPostMessage(globals, KillMessage);
		}

		attacker.CreditNeutralisedTarget(false, flyer, globals);
		flyer.Destroyed = true;
		flyer.RadarOn = false;
		flyer.Byte32Set = true;
		flyer.Position = new Vec3i(flyer.Position.X, flyer.Position.Y, DeathDepth);
		flyer.ReportOutOfAction(globals);
		flyer.Actions.Second?.Activate(globals.MissionVars);
		flyer.Behaviour.SetState(Es1FlyerBehaviour.States[Es1FlyerBehaviour.Dead], globals.DwellJitter);
		return true;
	}
}
