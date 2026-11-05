using Herculan.Engine.Numerics;
using Herculan.Es1.Messages;

namespace Herculan.Es1.Sim;

/// <summary>
/// A structure's hits, damage and collapse (docs/es1/simulation/structures.md#hits--base_directfirehittest-00003a58,
/// docs/es1/simulation/structures.md#damage--base_applydamage-00003978, docs/es1/simulation/structures.md#the-collapse):
/// one damage total, a weak spot that triples a hit, and, once destroyed, eight steps from the kill to a smoking wreck.
/// </summary>
public static class Es1BaseDamage {
	/// <summary><c>BaseHit_Occupancy</c> (<c>000904a0</c>), the hit test's occupancy flags: component 0, the one every retail cluster names.</summary>
	private static readonly bool[] Occupancy = [true];

	/// <summary>What a hit inside the weak spot multiplies the armor figure by.</summary>
	public const short WeakSpotFactor = 3;

	/// <summary>The debris group a hit throws, and the one the kill throws.</summary>
	public const int HitDebrisGroup = 1, KillDebrisGroup = 3;

	/// <summary>The computer message the player's machine posts when it destroys the structure it has targeted.</summary>
	public const short KillMessage = Es1MechOutOfAction.DestroyedMessage;

	/// <summary>The collapse's steps, which the kill sets the count to.</summary>
	public const short CollapseSteps = 8;

	/// <summary>The <c>EXPLOS.DAT</c> types of the collapse's small explosions and of its large one.</summary>
	public const short SmallExplosion = 0xf, LargeExplosion = 0x10;

	/// <summary>What the collapse's countdown reloads with after a small explosion, about 0.12 s, and after the large one, about 0.78 s.</summary>
	public const short SmallInterval = 300, LargeInterval = 2000;

	/// <summary>The smoke kind the wreck's emitter is built with, and its part, none.</summary>
	public const short WreckSmokeKind = 1, WreckSmokePart = -1;

	/// <summary>
	/// <c>Base_DirectFireHitTest</c> (<c>ES1DBSIM 00003a58</c>)(<paramref name="structure"/>, <paramref name="shot"/>), slot
	/// <c>+0x24</c>:
	/// <list type="number">
	/// <item>the shot's armor figure <c>+4</c> through <see cref="Es1MechDamage.ScaleByDifficulty"/> when the side of the
	/// shot owner's group is 0;</item>
	/// <item>a wreck (<see cref="Es1Base.IsWreck"/>) or a type with no collision model against the shape its instance
	/// holds (<see cref="Es1ShapeTests.RaycastShapeVolume"/>), a hit giving its distance + 1; any other 0 when the ray's start
	/// is farther from it (<c>Math_DistanceBetweenPoints</c>) than the ray's length and clearance plus its radius (slot
	/// <c>+0x14</c>), and otherwise the struck component (<see cref="Es1HitSpheres.SelectStruckComponent"/>) on the type's
	/// model and <c>BaseHit_Occupancy</c> in its frame through the ray's, a hit giving its distance + 1;</item>
	/// <item>on a hit, the point (0, the distance, 0) through the ray's frame; while the structure stands, the armor figure
	/// times <see cref="WeakSpotFactor"/> (<c>int16</c>) when the point is nearer the weak spot (the type's <c>+0x1e</c>
	/// through the structure's frame) than its radius <c>+0x24</c>, and <see cref="ApplyDamage"/>(structure, the shot's
	/// owner, the figure); then, standing or not, the effect of entry 8 + (<c>Math_RandomNext</c> &amp; 3) of the shot's
	/// effects at the point and debris group <see cref="HitDebrisGroup"/> there.</item>
	/// </list>
	/// Answers the distance, or 0. Retail also copies and scales the shield figure <c>+6</c>, which it does not read again;
	/// that is not kept here. It reads the owner's group unchecked; an owner with no group is not scaled here, an engine
	/// guard. A shot with no effects makes no effect.
	/// </summary>
	public static int HitTest(Es1Base structure, Es1Shot shot, Es1SimGlobals globals) {
		short armorFigure = shot.Armor;
		if (shot.Owner.Group?.Side == 0) {
			armorFigure = Es1MechDamage.ScaleByDifficulty(armorFigure, globals);
		}

		var ray = shot.Ray;
		var type = structure.Type;
		int distance = 0;
		if (structure.IsWreck || type.CollisionModel is not { } model) {
			if (Es1ShapeTests.RaycastShapeVolume([structure.ShapeObject], ray, out var hit)) {
				distance = hit.Distance + 1;
			}
		} else {
			if (structure.Position.ApproxDistanceTo(ray.Start) > ray.Length + ray.Clearance + structure.ShapeRadius) {
				return 0;
			}

			var toRay = Transform3.Concat(structure.Frame, ray.WorldToRay);
			if (Es1HitSpheres.SelectStruckComponent(model, Occupancy, toRay, ray, structure.PartNodeTransform) is { } hit) {
				distance = hit.Distance + 1;
			}
		}

		if (distance == 0) {
			return 0;
		}

		var point = ray.Frame.TransformPoint(0, distance, 0);
		if (!structure.Destroyed) {
			var weakSpot = type.WeakSpot;
			if (structure.Frame.TransformPoint(weakSpot.X, weakSpot.Y, weakSpot.Z).ApproxDistanceTo(point) < type.WeakSpotRadius) {
				armorFigure = (short)(armorFigure * WeakSpotFactor);
			}

			ApplyDamage(structure, shot.Owner, armorFigure, globals);
		}

		// Explosion_Construct (00005af0)(null, point, effect entry 8 + draw & 3).
		int k = globals.Random.Next() & 3;
		if (shot.Effects?.ImpactFXArmor[k] is { } effect) {
			globals.Explosions.Construct(point, effect, globals.Random, globals.Sound);
		}

		// Debris_ThrowGroup (00005fb8)(group of 000bc1c0, point, 000bc1c6).
		globals.Debris.ThrowGroup(HitDebrisGroup, point, globals.Random);
		return distance;
	}

	/// <summary>
	/// <c>Base_ApplyDamage</c> (<c>00003978</c>)(<paramref name="structure"/>, <paramref name="attacker"/>,
	/// <paramref name="damage"/>): the damage added to <see cref="Es1Base.Damage"/> (<c>int16</c>), and when that passes the
	/// type's hit points <c>+0x08</c>:
	/// <list type="number">
	/// <item><see cref="KillMessage"/> when the attacker is <c>LocalPlayerMech</c> and the structure that machine's target;</item>
	/// <item>the damage set to the hit points, and the attacker's slot <c>+0x64</c>
	/// (<see cref="IEs1SimObject.CreditNeutralisedTarget"/>)(not already immobilized, the structure);</item>
	/// <item>destroyed, and the radar off;</item>
	/// <item>debris group <see cref="KillDebrisGroup"/> thrown at its position;</item>
	/// <item>the report (<see cref="Es1Base.ReportOutOfAction"/>) and the <c>+0x170</c> action activated when there is one;</item>
	/// <item>the collapse count set to <see cref="CollapseSteps"/>.</item>
	/// </list>
	/// Retail calls the attacker's slot with no null test; its one caller, <see cref="HitTest"/>, passes the shot's owner,
	/// which is never null here.
	/// </summary>
	public static void ApplyDamage(Es1Base structure, IEs1SimObject attacker, short damage, Es1SimGlobals globals) {
		structure.Damage = (short)(structure.Damage + damage);
		short hitPoints = structure.Type.HitPoints;
		if (structure.Damage <= hitPoints) {
			return;
		}

		if (globals.LocalPlayerMech is { } local && ReferenceEquals(attacker, local) && ReferenceEquals(local.Target, structure)) {
			Es1MessageTicker.ComputerPostMessage(globals, KillMessage);
		}

		structure.Damage = hitPoints;
		attacker.CreditNeutralisedTarget(false, structure, globals);
		structure.Destroyed = true;
		structure.RadarOn = false;
		globals.Debris.ThrowGroup(KillDebrisGroup, structure.Position, globals.Random);
		structure.ReportOutOfAction(globals);
		structure.Actions.Second?.Activate(globals.MissionVars);
		structure.CollapseCount = CollapseSteps;
	}

	/// <summary>
	/// <c>Base_ThinkTick</c>'s first step, the collapse (docs/es1/simulation/structures.md#the-collapse), while the count is
	/// not 0: on each expiry of its countdown the count goes down by 1, and then
	/// <list type="bullet">
	/// <item>at 7 to 2, an explosion of <see cref="SmallExplosion"/> at a random point of the scatter box through the
	/// structure's frame, each axis <c>Math_RandomBelow</c>(2<i>e</i>) − <i>e</i> + <i>c</i> with <i>e</i> the type's
	/// half-extent and <i>c</i> its center, X, then Y, then Z; the countdown reloaded with <see cref="SmallInterval"/>;</item>
	/// <item>at 1, an explosion of <see cref="LargeExplosion"/> at the type's collapse point <c>+0x0c</c> through the frame;
	/// the countdown reloaded with <see cref="LargeInterval"/>;</item>
	/// <item>at 0, the wreck (<see cref="Es1Base.Hulk"/>) in the instance, and <c>Smoke_Construct</c>(null,
	/// <see cref="WreckSmokeKind"/>, the collapse point, <see cref="WreckSmokePart"/>, the structure).</item>
	/// </list>
	/// The countdown, never loaded before the kill, is at 0, so the first step runs on the tick after it.
	/// </summary>
	public static void CollapseStep(Es1Base structure, Es1SimGlobals globals) {
		if (globals.Clock.CountdownTimerTick(ref structure.CollapseCountdown) != 0) {
			return;
		}

		var type = structure.Type;
		structure.CollapseCount--;
		if (structure.CollapseCount == 0) {
			structure.Shape = structure.Hulk;
			// Smoke_Construct (000069ec), its first shape drawn on the generator 000b5c48.
			globals.Smoke.Construct(WreckSmokeKind, type.CollapsePoint, WreckSmokePart, structure, globals.PresentationRandom);
		} else if (structure.CollapseCount == 1) {
			var at = type.CollapsePoint;
			globals.Explosions.Construct(structure.Frame.TransformPoint(at.X, at.Y, at.Z), LargeExplosion, globals.Random, globals.Sound);
			structure.CollapseCountdown = LargeInterval;
		} else {
			var random = globals.Random;
			var (centerX, centerY, centerZ) = type.ScatterCenter;
			var (extentX, extentY, extentZ) = type.ScatterHalfExtents;
			int x = random.NextBelow((short)(extentX << 1)) - extentX + centerX;
			int y = random.NextBelow((short)(extentY << 1)) - extentY + centerY;
			int z = random.NextBelow((short)(extentZ << 1)) - extentZ + centerZ;
			globals.Explosions.Construct(structure.Frame.TransformPoint(x, y, z), SmallExplosion, random, globals.Sound);
			structure.CollapseCountdown = SmallInterval;
		}
	}
}
