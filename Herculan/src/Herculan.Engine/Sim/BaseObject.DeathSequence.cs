using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// A structure coming down — <c>Base_DeathSequenceTick</c>'s stages, from smoke through the collapse to
/// the fire, and the wreck or the sinking that ends them. See
/// docs/retail/simulation/destruction-effects.md ("A structure coming down").
/// </summary>
public sealed partial class BaseObject {
	/// <summary>
	/// Whether a part is still coming down — it has been destroyed but has stages of its death
	/// sequence left to run. A structure that has fallen but whose parts are still collapsing reads
	/// <see cref="Destroyed"/> and this at once.
	/// </summary>
	public bool Collapsing(int index) =>
		index >= 0 && index < _deathStage.Length && _deathStage[index] != 0;

	/// <summary>
	/// <c>Base_DeathSequenceTick</c> — steps every part that is coming down through the next stage of its
	/// <see cref="StructureDeathSequence"/>. This is the whole of how a building collapses, and it
	/// runs backwards: the stage counter starts at the sequence's own length and each expiry of the
	/// stage timer takes one off it.
	///
	/// <list type="bullet">
	/// <item><b>The early stages are smoke.</b> A secondary explosion is scattered at a random point
	/// inside the part's <see cref="BaseComponentType.SmokeSpread"/> box around its
	/// <see cref="BaseComponentType.Position"/>. Stage 4 exactly also cascades: every part hanging off
	/// this one is finished off, so a tower goes when the block under it does.</item>
	/// <item><b>Stage 1 is the collapse.</b> The sequence's own explosion goes off, the part throws
	/// its debris out of <c>BASE_DEB</c>, and then either the part is redrawn as its own rubble or —
	/// once every part is gone, for a type that leaves a wreck — the whole structure switches over to
	/// its hulk.</item>
	/// <item><b>Stage 0 is the fire</b>, and it is where the two scales meet. When the type and the
	/// part both state a fire, only the type's whole-structure fire is lit, once every part is gone;
	/// otherwise the part lights its own, if it states one. A structure with no wreck and exactly one
	/// part, which has no rubble cell, is dropped through the floor instead —
	/// <see cref="SunkDepth"/>, the original's own way of making a small object disappear.</item>
	/// </list>
	///
	/// <para>The part changes through <see cref="CellFrames"/>: its sequence steps to
	/// <see cref="CollapsedCell"/> and the renderer draws that cell of it instead, which for a
	/// structure is the part's rubble rather than nothing.</para>
	/// </summary>
	private void DeathSequenceTick(SimWorld world) {
		for (int i = 0; i < Type.Components.Length; i++) {
			if (_deathStage[i] == 0
					|| StructureDeathSequence.At(Type.Components[i].DestroyedEffect) is not { } sequence
					|| SimMath.CountdownTimerTick(ref _deathTimer[i]) != 0) {
				continue;
			}

			var component = Type.Components[i];
			_deathStage[i]--;

			switch (_deathStage[i]) {
				case 0:
					LightTheFire(world, component);
					break;

				case CollapseStage:
					Collapse(world, i, component, sequence);
					break;

				default:
					// EFFECTS DETAIL decides which smoke stages happen at all, and the stage hold goes
					// with the smoke: a stage that scatters none leaves its timer at zero, so the next
					// tick takes the stage after it. At the lowest setting a part runs straight from its
					// first hit to its collapse in as many ticks as it has stages.
					if (SmokesAtStage(world.EffectsDetail, _deathStage[i])) {
						ScatterSmoke(world, component, sequence);
						_deathTimer[i] = StructureDeathSequence.StageInterval;
					}

					if (_deathStage[i] == CascadeStage) {
						FinishDependents(world, i);
					}

					break;
			}
		}
	}

	/// <summary>
	/// The last stage. When the type and the part both state a fire, the type's whole-structure fire
	/// is lit in place of the part's, and only once every part is gone (<see cref="EveryPartGone"/>);
	/// otherwise the part burns alone. A structure with no wreck and one part, which has no rubble
	/// cell, is dropped out of the world.
	/// </summary>
	private void LightTheFire(SimWorld world, BaseComponentType component) {
		if (Type.HulkTypeIndex == -1 && component.DestroyedSubShape == -1
				&& Type.Components.Length == 1) {
			// Dropped through the floor rather than taken out of the list: the original writes the
			// depth onto the object's Z and leaves it where it is, so it is still shootable and still
			// blocks nothing, it is simply nowhere the camera looks. Its own Tick puts it back on the
			// terrain every frame, so the sunk depth has to survive that -- see Sunk.
			Sunk = true;
			return;
		}

		if (Type.FireShapeIndex < 0 || component.FireShapeIndex < 0) {
			if (component.FireShapeIndex >= 0) {
				world.SpawnFire(this, -1, component.EmitPoint, component.FireShapeIndex);
			}

			return;
		}

		if (EveryPartGone()) {
			world.SpawnFire(this, -1, Type.FirePoint, Type.FireShapeIndex);
		}
	}

	/// <summary>
	/// The collapse. The explosion is the type's own when every part is gone and the type and this
	/// part both state a fire, and the part's own otherwise; the sequence decides whether it goes off
	/// at the emission point or at the structure's origin. Then the debris, and then the shape change:
	/// the hulk once every part is gone, whatever the fires, for a type that leaves a wreck. See
	/// docs/retail/simulation/destruction-effects.md ("A structure coming down").
	/// </summary>
	private void Collapse(SimWorld world, int index, BaseComponentType component,
			StructureDeathSequence sequence) {
		bool gone = EveryPartGone();

		if (gone && Type.FireShapeIndex >= 0 && component.FireShapeIndex >= 0) {
			SpawnCollapseExplosion(world, Type.DestroyedEffect, Type.FirePoint, ref _deathTimer[index]);
		} else if (component.DestroyedEffect >= 0) {
			SpawnCollapseExplosion(world, component.DestroyedEffect, component.EmitPoint,
				ref _deathTimer[index]);
		}

		ThrowDebris(world, component);

		// A type that leaves a wreck switches to it once every part is gone; anything else loses the
		// part's own geometry and takes everything hanging off that part with it.
		if (gone && Type.HulkTypeIndex >= 0) {
			ShowingHulk = true;
			return;
		}

		if (component.DestroyedSubShape >= 0) {
			CellFrames[component.DestroyedSubShape] = CollapsedCell;
			FinishDependents(world, index);
		}
	}

	/// <summary>
	/// <c>Base_CollapseExplosion</c> — one collapse explosion, and the stage hold that goes with it. The
	/// sequence's <see cref="StructureDeathSequence.ExplodeAtOrigin"/> decides whether the point is
	/// the structure's own or the offset put through its frame.
	/// </summary>
	private void SpawnCollapseExplosion(SimWorld world, int sequenceIndex, Vec3i point,
			ref short timer) {
		if (StructureDeathSequence.At(sequenceIndex) is not { } sequence) {
			return;
		}

		world.SpawnImpactEffect(sequence.Explosion, sequence.ExplodeAtOrigin
			? Position
			: WorldTransform.TransformPoint(point.X, point.Y, point.Z), this);

		timer = sequence.CollapseHold;
	}

	/// <summary>
	/// <c>Base_ThrowDebris</c> — the debris a collapsing part throws, out of <c>BASE_DEB</c>. A part whose
	/// group is above <see cref="LargeDebrisGroup"/> throws two further fixed groups on top of its
	/// own, which is the difference between a shed falling over and a hangar coming apart.
	/// </summary>
	private void ThrowDebris(SimWorld world, BaseComponentType component) {
		var point = WorldTransform.TransformPoint(
			component.EmitPoint.X, component.EmitPoint.Y, component.EmitPoint.Z);
		var table = StructureDebris(world);

		world.SpawnDebris(component.DebrisGroup, point, table);

		if (component.DebrisGroup > LargeDebrisGroup) {
			world.SpawnDebris(LargeDebrisExtraA, point, table);
			world.SpawnDebris(LargeDebrisExtraB, point, table);
		}
	}

	/// <summary>
	/// <c>Base_FinishDependents</c> — finishes off every part that hangs off this one. Recursive in the
	/// original and here: a chain of dependent parts all comes down together, each through the
	/// ordinary damage path so each starts its own death sequence. Each is credited to whoever
	/// destroyed the part it hangs off, read from that part's own record.
	/// </summary>
	private void FinishDependents(SimWorld world, int index) {
		for (int i = 0; i < Type.Components.Length; i++) {
			if (Type.Components[i].ParentComponent != index
					|| _damage[i] >= Type.Components[i].MaxDamage) {
				continue;
			}

			ApplyDamage(world.Random, i, Type.Components[i].MaxDamage, _attackers[index], world);
			FinishDependents(world, i);
		}
	}

	/// <summary>
	/// Whether a smoke stage scatters its explosion — <c>Base_DeathSequenceTick</c>'s test of
	/// <c>Sound_DetailSetting</c> (<c>004d1fc7</c>), the EFFECTS DETAIL byte it reads once on entry:
	/// every stage at 2, the odd-numbered ones at 1, none at 0. Any other value scatters none, as the
	/// original's two equality tests leave it.
	/// </summary>
	public static bool SmokesAtStage(int effectsDetail, int stage) =>
		effectsDetail == 2 || (effectsDetail == 1 && (stage & 1) != 0);

	/// <summary>
	/// A smoke stage: one secondary explosion at a random point inside the part's own spread box
	/// around its position.
	/// </summary>
	private void ScatterSmoke(SimWorld world, BaseComponentType component,
			StructureDeathSequence sequence) {
		var spread = component.SmokeSpread;
		var local = new Vec3i(
			component.Position.X + world.Random.NextBelow((short)(spread.X * 2)) - spread.X,
			component.Position.Y + world.Random.NextBelow((short)(spread.Y * 2)) - spread.Y,
			component.Position.Z + world.Random.NextBelow((short)(spread.Z * 2)) - spread.Z);

		world.SpawnImpactEffect(sequence.SmokeExplosion,
			WorldTransform.TransformPoint(local.X, local.Y, local.Z), this);
	}

	/// <summary>
	/// <c>Base_EveryPartGone</c> — whether every part either has no fire of its own or is fully damaged,
	/// which is the original's test for "this structure, and not just this part, has gone".
	/// </summary>
	private bool EveryPartGone() {
		for (int i = 0; i < Type.Components.Length; i++) {
			if (Type.Components[i].FireShapeIndex >= 0 && _damage[i] < Type.Components[i].MaxDamage) {
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Whether this structure has been dropped out of sight — the last stage of a type that leaves no
	/// wreck, has one part, and nothing to hide. See <see cref="SunkDepth"/>.
	/// </summary>
	public bool Sunk { get; private set; }

	/// <summary>
	/// Whether this structure has switched over to its <see cref="BaseType.HulkTypeIndex"/> wreck — a
	/// root of <c>dgs\BHULKS.DGS</c> drawn in place of the building. The original writes the hulk
	/// shape straight onto the object's model instance; here it is a flag the scene reads and
	/// <see cref="CurrentVolume"/> and <see cref="ShapeRadius"/> follow.
	/// </summary>
	public bool ShowingHulk { get; private set; }

	/// <summary>
	/// The cell a collapsed part's sequence is stepped to — the original's own literal, written
	/// straight into the shape instance's array at
	/// <see cref="BaseComponentType.DestroyedSubShape"/>.
	///
	/// <para>It is <b>1</b>, not the machine's <see cref="ComponentDamage.DestroyedCell"/>, and it
	/// does not blank the part: a structure's parts are two-cell flipbooks whose <i>second cell is
	/// its own rubble</i> — different geometry, not none. A collapsed building is redrawn as its
	/// wreckage part by part, where a machine's destroyed component simply comes off.</para>
	/// </summary>
	public const short CollapsedCell = 1;

	/// <summary>
	/// The stage at which a part takes its dependents with it — the original's literal 4, tested for
	/// equality rather than as a threshold, so a sequence shorter than five stages never cascades this
	/// way at all.
	/// </summary>
	private const int CascadeStage = 4;

	/// <summary>
	/// The debris group above which a collapsing part throws extra wreckage — the original's literal
	/// 5, which is the last group in <c>DEF_DEB</c>. So the test really reads "does this part throw
	/// structure debris rather than the shared default".
	/// </summary>
	public const short LargeDebrisGroup = 5;

	/// <inheritdoc cref="LargeDebrisGroup" />
	private const short LargeDebrisExtraA = 10;

	/// <inheritdoc cref="LargeDebrisGroup" />
	private const short LargeDebrisExtraB = 12;

	/// <summary>
	/// Where the original drops a structure that leaves nothing behind — <c>-100000</c> written
	/// straight onto its Z, well under any terrain, so it is simply no longer anywhere the camera
	/// looks.
	/// </summary>
	public const int SunkDepth = -100000;
}
