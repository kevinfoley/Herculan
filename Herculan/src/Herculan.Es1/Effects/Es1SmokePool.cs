using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Effects;

/// <summary>
/// The smoke file's pools and shapes (docs/es1/simulation/destruction-effects.md#smoke): the emitters' pool
/// <c>g_SmokePool</c> (<c>ES1DBSIM 000aefd4</c>), <see cref="EmitterCapacity"/> emitters, and the balls' pool
/// <c>g_SmokeBallPool</c> (<c>000aef68</c>), <see cref="BallCapacity"/> balls, each with its delete queue
/// (<c>g_SmokeDeleteQueue</c>, <c>000aefd8</c>; <c>g_SmokeBallDeleteQueue</c>, <c>000aef6c</c>), and the roots of
/// <c>dts\smoke</c> that <c>Smoke_LoadResources</c> (<c>000066ec</c>) loads at phase 2 into <c>g_SmokeShapes</c>
/// (<c>000bc1d0</c>), every one bound to the bank <c>dba\smoke</c>, which is the draw's.
///
/// <para><see cref="Emitters"/> and <see cref="Balls"/> are in allocation order, the order a <c>Pool_Prev</c> walk from
/// the end visits them.</para>
/// </summary>
public sealed class Es1SmokePool {
	/// <summary><c>Smoke_LoadResources</c>' <c>Pool_Init</c>(0, <c>0x24</c>, <c>0x14</c>): 20 emitters.</summary>
	public const int EmitterCapacity = 20;

	/// <summary><c>SmokeBall_LoadResources</c>' <c>Pool_Init</c>(0, <c>0x46</c>, 200): 200 balls.</summary>
	public const int BallCapacity = 200;

	private readonly List<Es1Smoke> _emitters = new();
	private readonly List<Es1Smoke> _emitterQueue = new();
	private readonly List<Es1SmokeBall> _balls = new();
	private readonly List<Es1SmokeBall> _ballQueue = new();

	/// <summary>The live emitters, oldest first.</summary>
	public IReadOnlyList<Es1Smoke> Emitters => _emitters;

	/// <summary>The live balls, oldest first.</summary>
	public IReadOnlyList<Es1SmokeBall> Balls => _balls;

	/// <summary><c>g_SmokeShapes</c>, the roots of <c>dts\smoke</c>.</summary>
	public IReadOnlyList<TSObject> Shapes { get; private init; } = [];

	/// <summary>The phase-2 load of <c>dts\smoke</c> through <c>HercWorks.Core</c>; a missing file leaves no shapes.</summary>
	public static Es1SmokePool Load(GameContent content) => new() {
		Shapes = content.Read("dts", "SMOKE.DTS") is { } bytes
			&& new DTSModelTransformer().Parse(bytes) is DynamixThreeSpaceModel { Roots: { } roots }
				? roots
				: [],
	};

	/// <summary>
	/// <c>Smoke_Construct</c> (<c>000069ec</c>)(null, <paramref name="kind"/>, <paramref name="point"/>,
	/// <paramref name="partId"/>, <paramref name="owner"/>): an emitter from the pool, or null with the pool full, the
	/// emitters queued for delete still holding their places. Its first shape draws <c>Math_RandomNext</c> on
	/// <paramref name="random"/>, the generator <c>000b5c48</c>.
	/// </summary>
	public Es1Smoke? Construct(short kind, (short X, short Y, short Z)? point, short partId, IEs1SimObject owner, SimRandom random) {
		if (_emitters.Count + _emitterQueue.Count >= EmitterCapacity) {
			return null;
		}

		var smoke = new Es1Smoke(this, kind, point, partId, owner, random.Next());
		_emitters.Add(smoke);
		return smoke;
	}

	/// <summary>
	/// <c>SmokeBall_Construct</c> (<c>00006704</c>)(null, <paramref name="shape"/>, <paramref name="point"/>): a ball from
	/// the pool on root <paramref name="shape"/> of <see cref="Shapes"/>, or none with the pool full. A root past the
	/// shapes, which retail reads past, builds a ball with no shape, an engine guard.
	/// </summary>
	internal void ConstructBall(short shape, Vec3i point) {
		if (_balls.Count + _ballQueue.Count >= BallCapacity) {
			return;
		}

		_balls.Add(new Es1SmokeBall(shape >= 0 && shape < Shapes.Count ? Shapes[shape] : null, point));
	}

	/// <summary>
	/// <c>Sim_MainTick</c>'s walk of the balls (docs/es1/simulation/main-loop.md#the-tick--sim_maintick-00059700): each,
	/// oldest first, through <c>Effect_DriftAndAnimate</c>, and one that answers non-zero destroyed at once through slot
	/// <c>+0xc</c> with 3 (<c>SmokeBall_Dtor</c>, <c>000067f4</c>), which queues it.
	/// </summary>
	public void TickBalls(Es1SimClock clock) {
		for (int i = 0; i < _balls.Count;) {
			if (_balls[i].Tick(clock) != 0) {
				_ballQueue.Add(_balls[i]);
				_balls.RemoveAt(i);
			} else {
				i++;
			}
		}
	}

	/// <summary>
	/// <c>Sim_MainTick</c>'s walk of the emitters: each, oldest first, through <c>Smoke_Tick</c>, and one that answers
	/// non-zero destroyed at once through slot <c>+0xc</c> with 3 (<c>Smoke_Dtor</c>, <c>00006af8</c>), which queues it.
	/// </summary>
	public void TickEmitters(Es1SimClock clock) {
		for (int i = 0; i < _emitters.Count;) {
			if (_emitters[i].Tick(clock) != 0) {
				_emitterQueue.Add(_emitters[i]);
				_emitters.RemoveAt(i);
			} else {
				i++;
			}
		}
	}

	/// <summary>
	/// <c>Smoke_FlushDeletes</c> (<c>0000664c</c>), phase 5: each queued emitter given back to its pool, then
	/// <c>SmokeBall_FlushDeletes</c> (<c>00006638</c>), each queued ball.
	/// </summary>
	public void FlushDeletes() {
		_emitterQueue.Clear();
		_ballQueue.Clear();
	}
}
