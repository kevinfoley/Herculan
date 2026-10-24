namespace Herculan.Engine.Sim;

/// <summary>
/// One of the four ways a structure's part comes down — the parallel short tables in DBSIM's statics
/// at <c>004973fc</c>, <c>00497404</c>, <c>0049740c</c>, <c>00497414</c> and <c>0049741c</c>, each
/// four entries long and all indexed by the same
/// <see cref="World.BaseComponentType.DestroyedEffect"/>.
///
/// <para>A part is not simply deleted when its health runs out: it is given a countdown of
/// <see cref="StageCount"/> stages, and <see cref="BaseObject.DeathSequenceTick"/> steps it through
/// them one at a time. The early stages throw up smoke and secondary explosions inside the part's
/// own <see cref="World.BaseComponentType.SmokeSpread"/>; the last two do the collapse itself and
/// then set it alight. So a building takes several seconds to come down, and its parts come down in
/// whatever order they were shot. Retail behavior:
/// docs/retail/simulation/destruction-effects.md#a-structure-coming-down.</para>
/// </summary>
/// <param name="Explosion">
/// <c>004973fc</c> — the <c>EXPLOS.DAT</c> type the collapse stage sets off.
/// </param>
/// <param name="CollapseHold">
/// <c>00497404</c> — how long the last stage is held for after the collapse. A sequence stating 0
/// drops straight through, so its fire arrives on the next tick.
/// </param>
/// <param name="ExplodeAtOrigin">
/// <c>0049740c</c> — whether the collapse explosion goes off at the structure's own origin rather
/// than at the part's emission point.
/// </param>
/// <param name="SmokeExplosion">
/// <c>00497414</c> — the <c>EXPLOS.DAT</c> type the smoke stages scatter.
/// </param>
/// <param name="StageCount">
/// <c>0049741c</c> — how many stages the sequence runs, which is the whole of how long a part takes
/// to fall.
/// </param>
public readonly record struct StructureDeathSequence(
	short Explosion, short CollapseHold, bool ExplodeAtOrigin, short SmokeExplosion, short StageCount) {

	/// <summary>The four sequences, in the order the tables state them.</summary>
	public static readonly StructureDeathSequence[] All = {
		new(16, 2000, true, 15, 8),
		new(21, 0, false, 15, 6),
		new(15, 0, false, 15, 6),
		new(15, 0, false, 15, 2),
	};

	/// <summary>One sequence by index, or null when the index names none.</summary>
	public static StructureDeathSequence? At(int index) =>
		index >= 0 && index < All.Length ? All[index] : null;

	/// <summary>
	/// How long one stage is held for — <c>Base_ApplyDamage</c>'s own literal when it starts the
	/// sequence, and the smoke stage's own reload. Against the tick's delta that is a few ticks a
	/// stage, so a six-stage part falls over a second or so.
	/// </summary>
	public const short StageInterval = 300;
}
