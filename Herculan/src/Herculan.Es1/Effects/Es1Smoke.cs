using HercWorks.Core.Data.File.Dts;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Effects;

/// <summary>
/// A smoke emitter, the <c>0x24</c>-byte object of <c>g_SmokePool</c>
/// (docs/es1/simulation/destruction-effects.md#the-emitter--smoke_construct-000069ec): it draws nothing, and puffs a
/// smoke ball every countdown from a point on a part of its owner, or its owner's position, until its puffs run out.
/// <c>SmokeVtable</c> (<c>ES1DBSIM 000909fa</c>)'s.
/// </summary>
public sealed class Es1Smoke {
	/// <summary><c>Smoke_PuffCountByKind</c> (<c>00090964</c>), the puffs by kind.</summary>
	public static readonly short[] PuffCountByKind = [200, 500, 1000];

	/// <summary>What <c>Smoke_Tick</c> reloads the countdown with after a puff.</summary>
	public const short PuffInterval = 200;

	/// <summary>The ball shapes a kind cycles through: kind × this + 0 to this − 1.</summary>
	public const int ShapesPerKind = 4;

	private readonly Es1SmokePool _pool;
	private short _countdown;

	/// <summary>
	/// <c>Smoke_Construct</c> (<c>000069ec</c>)(null, <paramref name="kind"/>, <paramref name="point"/>,
	/// <paramref name="partId"/>, <paramref name="owner"/>)'s fields: the kind, its puff count, the countdown 0, the owner,
	/// the part id, the first shape kind × <see cref="ShapesPerKind"/> + the generator's draw &amp; 3, and the point, or
	/// with none <see cref="NoPoint"/> set. A kind past the table, where retail reads past it, puffs none, an engine guard.
	/// </summary>
	internal Es1Smoke(Es1SmokePool pool, short kind, (short X, short Y, short Z)? point, short partId, Es1Mech owner,
			short draw) {
		_pool = pool;
		Kind = kind;
		Puffs = kind >= 0 && kind < PuffCountByKind.Length ? PuffCountByKind[kind] : (short)0;
		Owner = owner;
		PartId = partId;
		NextShape = (short)(kind * ShapesPerKind + (draw & 3));
		Point = point ?? default;
		NoPoint = point == null;
	}

	/// <summary><c>+0x22</c>, the kind: 0 a lost component's, 1 a collapsed structure's.</summary>
	public short Kind { get; }

	/// <summary><c>+0x1e</c>, the puffs left.</summary>
	public short Puffs { get; private set; }

	/// <summary><c>+0x0f</c>, the owner whose frame the point rides.</summary>
	public Es1Mech Owner { get; }

	/// <summary><c>+0x13</c>, the part the point is on, negative for the owner's own frame.</summary>
	public short PartId { get; }

	/// <summary><c>+0x20</c>, the shape the next ball is built on.</summary>
	public short NextShape { get; private set; }

	/// <summary><c>+0x15</c>, the point, three <c>int16</c>.</summary>
	public (short X, short Y, short Z) Point { get; }

	/// <summary><c>+0x06</c>, set for an emitter built with no point.</summary>
	public bool NoPoint { get; }

	/// <summary>
	/// <c>Smoke_Tick</c> (<c>00006b28</c>) (docs/es1/simulation/destruction-effects.md#the-tick--smoke_tick-00006b28):
	/// with puffs left, once <c>Math_CountdownTimerTick</c> of the countdown reaches 0, the countdown reloaded with
	/// <see cref="PuffInterval"/>, one puff taken, a ball built at <see cref="PuffPoint"/> on the next shape, and the
	/// next shape stepped through its kind's four; answers 1 once no puffs are left.
	/// </summary>
	internal short Tick(Es1SimClock clock) {
		if (Puffs != 0 && clock.CountdownTimerTick(ref _countdown) == 0) {
			_countdown = PuffInterval;
			Puffs--;
			_pool.ConstructBall(NextShape, PuffPoint());
			NextShape = (short)(Kind * ShapesPerKind + ((NextShape + 1) & 3));
		}

		return (short)(Puffs == 0 ? 1 : 0);
	}

	/// <summary>
	/// Where a ball is puffed: with a part id not negative or a point, the local point through the owner's frame, the
	/// local point being the point itself for a negative part id, and otherwise the point through the part's posed node
	/// (the node's translation with no point); with a negative part id and no point, the owner's position. Retail reads
	/// a part the shape lacks through a null node; here the node is the identity, an engine guard.
	/// </summary>
	private Vec3i PuffPoint() {
		if (PartId < 0 && NoPoint) {
			return Owner.Position;
		}

		Vec3i local;
		if (PartId < 0) {
			local = new Vec3i(Point.X, Point.Y, Point.Z);
		} else {
			var node = Owner.PartNodeTransform(PartId) ?? Transform3.Identity;
			local = NoPoint ? new Vec3i(node.X, node.Y, node.Z) : node.TransformPoint(Point.X, Point.Y, Point.Z);
		}

		return Owner.Frame.TransformPoint(local.X, local.Y, local.Z);
	}
}

/// <summary>
/// A smoke ball, the <c>0x46</c>-byte <c>TS_OBJ</c> of <c>g_SmokeBallPool</c>
/// (docs/es1/simulation/destruction-effects.md#the-ball--smokeball_construct-00006704): a root of <c>dts\smoke</c> that
/// plays its flipbook once where it was puffed. <c>SmokeBallVtable</c> (<c>0009097a</c>)'s.
/// </summary>
public sealed class Es1SmokeBall {
	/// <summary>The type tag <c>SmokeBall_Construct</c> stores at <c>+0</c>, which the distance bands are keyed by.</summary>
	public const byte TypeTag = 4;

	/// <summary>The countdown each cell of the flipbook stands for.</summary>
	public const int CellInterval = 200;

	private int _countdown = CellInterval;

	/// <summary><c>SmokeBall_Construct</c> (<c>00006704</c>)'s fields: the root <paramref name="shape"/>, the type tag, the point and the countdown.</summary>
	internal Es1SmokeBall(TSObject? shape, Vec3i point) {
		Shape = shape;
		Position = point;
	}

	/// <summary>The shape instance <c>+0x2e</c>'s shape; null for a root past the shapes, which retail reads past.</summary>
	public TSObject? Shape { get; }

	/// <summary><c>+0x20</c>, the position.</summary>
	public Vec3i Position { get; private set; }

	/// <summary>
	/// <c>+0x3b</c>, the drift, three <c>int16</c> added to the position each tick: (0, 0, 0), as <c>SmokeBall_Construct</c>
	/// writes none and the pool's block comes zeroed from the allocator; whether other code writes it is the doc's Open item
	/// (docs/es1/simulation/destruction-effects.md#open).
	/// </summary>
	public (short X, short Y, short Z) Drift { get; } = (0, 0, 0);

	/// <summary>The shape instance's cell frame of sequence 0.</summary>
	public short CellFrame { get; private set; }

	/// <summary>
	/// <c>Effect_DriftAndAnimate</c> (<c>0000693c</c>): the drift added to the position; on each expiry of
	/// <c>Timer_CountDown</c> the countdown reloaded with <see cref="CellInterval"/> and sequence 0 stepped on modulo the
	/// shape's frame count for it, answering 1 when it wraps to 0; otherwise 0. A shape with no frames, where retail
	/// reads through a missing shape or divides by 0, ends the ball, an engine guard.
	/// </summary>
	internal short Tick(Es1SimClock clock) {
		Position = new Vec3i(Position.X + Drift.X, Position.Y + Drift.Y, Position.Z + Drift.Z);
		if (clock.TimerCountDown(ref _countdown) != 0) {
			return 0;
		}

		_countdown = CellInterval;
		if (Shape is not TSShape { SequenceList: [var count, ..] } || (ushort)count == 0) {
			return 1;
		}

		CellFrame = (short)(((ushort)CellFrame + 1) % (ushort)count);
		return (short)(CellFrame == 0 ? 1 : 0);
	}
}
