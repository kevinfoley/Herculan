using Herculan.Engine.Numerics;

namespace Herculan.Engine.Anim;

/// <summary>
/// How <see cref="AnimationThread.SeekToPosition"/> scales its Q14 position across a sequence's
/// ticks, total − 1 of them. The game that seeks a thread names one.
/// </summary>
public enum SeekRounding {
	/// <summary>
	/// DBSIM's <c>Math_Q14Multiply</c> (<c>0047dfb4</c>): <c>(position × (total − 1)) &gt;&gt; 14</c>,
	/// truncated, with the remainder discarded.
	/// </summary>
	Truncate,

	/// <summary>
	/// Not retail: the smoother turret movement tweak (<see cref="Settings.TweakSettingDefinitions.SmootherTurretMovement"/>),
	/// which both games' turret seeks read in place of their own rounding. The
	/// product is <see cref="Truncate"/>'s, but the sub-tick remainder the Q14 scale-down truncates
	/// away is kept, and the thread's frame fraction spends it, so a seeked pose is not quantized
	/// to a whole animation tick (docs/retail/simulation/torso-aim.md, "Angle to pose"). On ES2's OUTLAW that
	/// is 1747 drawn twist poses from centre to limit where retail has 170, and the drawn view moves
	/// every tick from half stick up rather than from three quarters. Playback is untouched: only a
	/// seek produces a remainder, and the locomotion thread is never seeked.
	///
	/// <para>What stepping is left comes from the cosine table, not the animation.
	/// <see cref="SimTrig.Cos"/> quantises a rotation to its 16-BAM step, so the drawn view still moves
	/// in 0.101° increments and a twist under about 2°/s still steps. The thread's Q10 frame fraction
	/// is fine enough to carry the remainder past that point: one Q10 unit is 8 binary angle across a
	/// torso sweep's 45° keyframe interval.</para>
	/// </summary>
	KeepRemainder,

	/// <summary>
	/// ES1's <c>Math_Q14Multiply</c> (<c>ES1DBSIM 0005e838</c>), which <c>AnimThread_SeekToPosition</c>
	/// (<c>ES1DBSIM 000620c4</c>) calls as <c>Math_Q14Multiply(total − 1, position)</c>:
	/// <c>((total − 1) × position + 0x2000) &gt;&gt; 14</c>, rounded to nearest, with no remainder
	/// kept (<see cref="SimMath.Q14MultiplyRounded"/>). See docs/es1/rendering/dts-node-posing.md,
	/// "Differences from ES2".
	/// </summary>
	RoundToNearest,
}
