using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// A fire burning on something — DBSIM's <c>fire.cpp</c> class, built by <c>FireEffect_Ctor</c>
/// (<c>0046b388</c>) and advanced by <c>FireEffect_TickUpdate</c>, out of the ten-entry pool at
/// <c>g_FirePool</c>.
///
/// <para>It is <b>not</b> the muzzle flash and not an impact effect: it is the flame that sits on a
/// wrecked machine or a collapsing building and keeps burning. Where an
/// <see cref="ImpactEffect"/> plays its flipbook once and ends, a fire <i>loops</i> — it counts
/// <see cref="LoopsRemaining"/> passes of <c>dts\FIRE.DTS</c>'s billboards down to zero and only
/// then goes out. Thirty passes, or five on the Moon.</para>
///
/// <para><b>It rides its owner.</b> Every tick it re-places itself from where the owner is now, so
/// a burning HERC's fires walk with it and the fire on a component walks with that component. The
/// owner's death puts every fire it carries out at once —
/// <see cref="EffectPools.ReleaseFires"/>.</para>
///
/// <para>Like every other effect class it lives in the effect pool rather than the object list, so
/// nothing can shoot a fire and nothing collides with one. The pool is small and the acquire is
/// an <i>evict</i>: with all ten busy, the original takes the one with the fewest passes left
/// rather than refusing — see <see cref="EffectPools.SpawnFire"/>.</para>
/// </summary>
public sealed class FireEffect {
	private readonly int _frameCount;
	private short _timer;

	/// <param name="owner">What is burning.</param>
	/// <param name="componentIndex">
	/// Which of the owner's components the fire sits on, or <c>-1</c> to use
	/// <paramref name="localPoint"/> in the owner's own frame. The original passes the collision
	/// cluster id here and resolves the node transform behind it every tick; a component index is the
	/// same thing said in this engine's terms, because
	/// <see cref="MechObject.ComponentWorldPosition"/> resolves exactly that cluster's node and
	/// centre.
	/// </param>
	/// <param name="localPoint">
	/// Where the fire sits in the owner's frame, used when <paramref name="componentIndex"/> names no
	/// component. This is the path a structure takes: its type record and each of its parts state a
	/// plain offset rather than a cluster.
	/// </param>
	/// <param name="shapeIndex">Which root of <c>dts\FIRE.DTS</c> it draws — see <see cref="ShapeIndex"/>.</param>
	/// <param name="frameCount">How many frames that root's flipbook has.</param>
	/// <param name="loops">How many passes of it to play — <see cref="LoopCount"/>.</param>
	internal FireEffect(SimObject owner, short componentIndex, Vec3i localPoint, int shapeIndex,
			int frameCount, short loops) {
		Owner = owner;
		ComponentIndex = componentIndex;
		LocalPoint = localPoint;
		ShapeIndex = shapeIndex;
		_frameCount = frameCount;
		LoopsRemaining = loops;
		_timer = FrameInterval;
		Position = owner.Position;
	}

	/// <summary>What is burning — <c>obj+0x4a</c>, and what <see cref="EffectPools.ReleaseFires"/> matches on.</summary>
	public SimObject Owner { get; }

	/// <summary>
	/// Which of the owner's components the fire rides, or <c>-1</c> for one placed by
	/// <see cref="LocalPoint"/> alone.
	/// </summary>
	public short ComponentIndex { get; }

	/// <summary>Where it sits in the owner's frame, for a fire with no component.</summary>
	public Vec3i LocalPoint { get; }

	/// <summary>
	/// Which root of <c>dts\FIRE.DTS</c> it draws. Retail ships four, of 24, 24, 24 and 27 frames;
	/// the destruction paths use <see cref="WholeObjectShape"/> and <see cref="ComponentShape"/>, and
	/// a structure's own records name whichever they like.
	/// </summary>
	public int ShapeIndex { get; }

	/// <summary>Where it is in the world, recomputed every tick from the owner.</summary>
	public Vec3i Position { get; private set; }

	/// <summary>Its flipbook frame — the shape's own cell-animation counter in the original.</summary>
	public int Frame { get; private set; }

	/// <summary>
	/// How many passes of the flipbook are left. Counted down by one each time the frame wraps back
	/// to zero, and the fire is out when it reaches zero.
	/// </summary>
	public short LoopsRemaining { get; private set; }

	/// <summary>
	/// <c>FireEffect_TickUpdate</c>, vtable <c>+0x14</c>. Steps the flipbook on its own timer, then re-places
	/// the fire from wherever its owner is now. Returns whether it has burnt out.
	///
	/// <para>The shared burning-object sound (<c>0x33</c>) is placed on whichever live fire is
	/// nearest the camera, which is why the original's tick measures its own distance to the view.
	/// The nearest-of-all choice is taken across the whole pool in <see cref="SimWorld.Tick"/>
	/// instead of by each fire against a running minimum; <see cref="EffectPools.SpawnFire"/> and
	/// <see cref="EffectPools.ReleaseFires"/> hold the one-sound-for-all-of-them rule the original
	/// counts with <c>DAT_006b4fbc</c>.</para>
	/// </summary>
	internal bool Tick(SimWorld world) {
		if (SimMath.CountdownTimerTick(ref _timer) == 0) {
			_timer = FrameInterval;

			if (_frameCount > 0) {
				Frame = (Frame + 1) % _frameCount;
				if (Frame == 0) {
					LoopsRemaining--;
				}
			} else {
				// A shape with no frames can never wrap, so it would never burn out. Ending it here is
				// this engine's; the original would loop it forever on data no retail file supplies.
				LoopsRemaining = 0;
			}
		}

		Position = ComponentIndex >= 0 && Owner is MechObject mech
			? mech.ComponentWorldPosition(ComponentIndex)
			: Owner.WorldFrame.TransformPoint(LocalPoint.X, LocalPoint.Y, LocalPoint.Z);

		return LoopsRemaining <= 0;
	}

	/// <summary>
	/// How long one frame is held — <c>0x40</c>, reloaded on every expiry. The tick's own delta is
	/// larger than that, so the countdown expires every tick and the flipbook runs at the simulation
	/// rate: a 24-frame root is about a second a pass, and thirty passes are about half a minute.
	/// </summary>
	public const short FrameInterval = 0x40;

	/// <summary>
	/// How many passes a fire plays — 30, except on the Moon (<see cref="SimWorld.OnMoon"/>), where
	/// it is <see cref="MoonLoopCount"/>.
	///
	/// <para>The original also reads this field as the eviction priority when the pool is full, which
	/// makes the fire with the least left to burn the one that gets taken.</para>
	/// </summary>
	public const short LoopCount = 30;

	/// <inheritdoc cref="LoopCount" />
	public const short MoonLoopCount = 5;

	/// <summary>
	/// The shape a whole-object fire uses — root 0, what
	/// <see cref="HercWorks.Core.Data.File.Dbsim.HercSimDamage.HercPiece"/>'s
	/// <see cref="ComponentDamage.DestructionFlagWholeObjectFire"/> branch builds after clearing the
	/// machine's other fires.
	/// </summary>
	public const int WholeObjectShape = 0;

	/// <summary>
	/// And the shape a per-component fire uses — root 2, what the
	/// <see cref="ComponentDamage.DestructionFlagComponentFire"/> branch builds without clearing
	/// anything.
	/// </summary>
	public const int ComponentShape = 2;

	/// <summary>
	/// The shape file, opened by <c>FireEffect_LoadResources</c>' literal <c>fire</c> — or
	/// <c>fire2</c> under the low-memory art setting, which this engine does not have.
	/// </summary>
	public const string ShapeLibraryName = "FIRE.DTS";

	/// <summary>
	/// The bank name prefix its four shapes are textured from, <c>fire0</c> and <c>fire1</c>. Which
	/// of the two each shape takes is <c>dat\FIRE.DAT</c>: a four-byte count header and then one byte
	/// per shape, <c>[0, 0, 0, 1]</c> in retail.
	/// </summary>
	public const string TextureBankPrefix = "FIRE";

	/// <inheritdoc cref="TextureBankPrefix" />
	public const string BankTableResource = "FIRE.DAT";

	/// <inheritdoc cref="TextureBankPrefix" />
	public const int BankTableHeaderLength = 4;

	/// <summary>
	/// How many fires can burn at once — the pool's own size. An eleventh evicts the one with the
	/// fewest passes left rather than being refused.
	/// </summary>
	public const int PoolSize = 10;
}
