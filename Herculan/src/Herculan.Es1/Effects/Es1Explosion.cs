using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Effects;

/// <summary>
/// An impact effect, the <c>0x51</c>-byte <c>TS_OBJ</c> of <c>g_ExplosionPool</c> (docs/es1/simulation/impact-effects.md#the-effect):
/// a root of <c>dts\explos</c> standing still where a shot lands or a part comes down, playing its flipbook once, with
/// a ground shape and a light for some types. <c>ExplosionVtable</c> (<c>ES1DBSIM 00090822</c>)'s.
/// </summary>
public sealed class Es1Explosion {
	/// <summary>The type tag of a row whose object class <c>+0x2a</c> is 0, and of one whose class is not.</summary>
	public const byte TypeTag = 2, ClassTypeTag = 8;

	/// <summary>The row's offset of the intensity ramp, an <c>int16</c> a frame.</summary>
	public const int RampOffset = 0x0c;

	private readonly Es1ExplosionPool _pool;
	private short _countdown;

	/// <summary>
	/// <c>Explosion_Construct</c> (<c>00005af0</c>)'s fields (docs/es1/simulation/impact-effects.md#construction--explosion_construct-00005af0):
	/// the root <paramref name="shape"/>, the type byte, the type tag, the frame countdown from the row's <c>+0x06</c>,
	/// the point, the ground shape when the row's <c>+0x08</c> is not 0 and the light when its <c>+0x0a</c> is not 0.
	/// A new instance's cell frames are 0, so the row's sequence starts on 0. Retail takes the ground shape from
	/// <c>g_FlatObjPool</c>'s 150 places, which the HERCs' shadows share; this engine does not count them, so a ground
	/// shape is always built.
	/// </summary>
	internal Es1Explosion(Es1ExplosionPool pool, sbyte typeId, TSObject? shape, Vec3i point) {
		_pool = pool;
		TypeId = typeId;
		Shape = shape;
		CellFrames = new short[(shape as TSShape)?.SequenceList?.Length ?? 0];
		var row = Row;
		Tag = row.ObjectClass == 0 ? TypeTag : ClassTypeTag;
		_countdown = row.FrameInterval;
		Position = point;
		if (row.GroundShape != 0) {
			GroundShape = new Es1GroundShape(point);
		}

		if (row.LightMode != 0) {
			LightHandle = pool.Lights.Construct(row.FrameIntensity[0], point);
		}

		// PLACEHOLDER: Sound_PlayAt (0004deb8)(point, the row's +0x28 + 10); no plan step ports ES1's sound yet.
	}

	/// <summary><c>+0x00</c>, the type tag, which the distance bands are keyed by.</summary>
	public byte Tag { get; }

	/// <summary><c>+0x20</c>, the position.</summary>
	public Vec3i Position { get; }

	/// <summary>The shape instance <c>+0x2e</c>'s shape, the row's root; null for none.</summary>
	public TSObject? Shape { get; }

	/// <summary>The shape instance's cell frames, its <c>+8</c>, one per sequence of the shape.</summary>
	public short[] CellFrames { get; }

	/// <summary><c>+0x3b</c>, the type id, which <c>Explosion_GetTypeRecord</c> (<c>00005734</c>) reads signed.</summary>
	public sbyte TypeId { get; }

	/// <summary><c>+0x49</c>, the ground shape, or null.</summary>
	public Es1GroundShape? GroundShape { get; }

	/// <summary><c>+0x4d</c>, the light's slot in <see cref="Es1EffectLights"/>, or −1 for none.</summary>
	public int LightHandle { get; } = -1;

	/// <summary><c>Explosion_GetTypeRecord</c>: the row of <see cref="TypeId"/>.</summary>
	public Es1ExplosionTypeEntry Row => _pool.Types[TypeId];

	/// <summary>The cell frame the draw shows: the row's sequence's, 0 for a negative sequence.</summary>
	public int DrawnCellFrame => Row.AnimSequence is var sequence && sequence >= 0 && sequence < CellFrames.Length
		? CellFrames[sequence]
		: 0;

	/// <summary>
	/// <c>Explosion_TickUpdate</c> (<c>00005d74</c>) (docs/es1/simulation/impact-effects.md#the-tick--explosion_tickupdate-00005d74):
	/// 0 while <c>Math_CountdownTimerTick</c> of the countdown has not reached 0; otherwise 1 for a negative sequence;
	/// else the sequence's cell frame one on, modulo the shape's frame count for it, 1 when that is 0; else the light set
	/// to the low byte of the row's <c>int16</c> at <see cref="RampOffset"/> + 2 × frame
	/// (<see cref="Es1ExplosionPool.RowWord"/>, which reads on past the row as retail does), the ground shape stepped,
	/// the countdown reloaded from the row's <c>+0x06</c>, and 0. A sequence the shape lacks or that has no frames,
	/// where retail reads past the shape's table or divides by 0, ends the effect, an engine guard.
	/// </summary>
	internal short Tick(Es1SimClock clock) {
		if (clock.CountdownTimerTick(ref _countdown) != 0) {
			return 0;
		}

		var row = Row;
		int sequence = row.AnimSequence;
		if (sequence < 0) {
			return 1;
		}

		var counts = (Shape as TSShape)?.SequenceList;
		if (counts == null || sequence >= counts.Length || sequence >= CellFrames.Length || (ushort)counts[sequence] == 0) {
			return 1;
		}

		int frame = ((ushort)CellFrames[sequence] + 1) % (ushort)counts[sequence];
		CellFrames[sequence] = (short)frame;
		if (frame == 0) {
			return 1;
		}

		if (LightHandle >= 0) {
			_pool.Lights.SetIntensity(LightHandle, (byte)_pool.RowWord(TypeId, RampOffset + 2 * frame));
		}

		GroundShape?.Step(_pool.GroundShapeFrameCount);
		_countdown = row.FrameInterval;
		return 0;
	}

	/// <summary>
	/// <c>Explosion_Destruct</c> (<c>00005940</c>) with 3 (docs/es1/simulation/impact-effects.md#the-end--explosion_destruct-00005940):
	/// the ground shape destroyed, then the light (<see cref="Es1EffectLights.Destruct"/>); the pool queues the effect.
	/// </summary>
	internal void Destroy() {
		GroundShape?.Destroy();
		_pool.Lights.Destruct(LightHandle);
	}

	/// <summary>
	/// <c>Explosion_ProximityTest</c> (<c>00005d2c</c>)(effect, <paramref name="point"/>)
	/// (docs/es1/simulation/impact-effects.md#the-proximity-test--explosion_proximitytest-00005d2c): whether the row's
	/// light mode is 2 and <c>Math_DistanceBetweenPoints</c> from the point to the position is under the row's
	/// <c>int32</c> <c>+0x24</c>. No caller is known (docs/es1/simulation/impact-effects.md#open).
	/// </summary>
	public bool ProximityTest(Vec3i point) => Row.LightMode == 2 && point.ApproxDistanceTo(Position) < Row.ProximityRadius;
}

/// <summary>
/// An impact effect's ground shape, a <c>FlatObj</c> of <c>g_FlatObjPool</c> on root
/// <see cref="Es1ExplosionPool.GroundShapeRoot"/> of the flat shape set, built at the effect's point with no angles
/// (docs/es1/simulation/impact-effects.md#construction--explosion_construct-00005af0). <c>FlatObjVtableExploCopy</c>
/// (<c>000908a2</c>)'s.
/// </summary>
public sealed class Es1GroundShape(Vec3i point) {
	/// <summary><c>+0x20</c>, the position; <see cref="PlaceForDraw"/> sets its Z to 0.</summary>
	public Vec3i Position { get; private set; } = point;

	/// <summary>The shape instance's cell frame of sequence 0.</summary>
	public short CellFrame { get; private set; }

	/// <summary>Set once destroyed, after which it is not drawn.</summary>
	public bool Destroyed { get; private set; }

	/// <summary>
	/// <c>FlatObj_DrawExploCopy</c> (<c>00005ca8</c>)'s first step: the Z set to 0; answers the frame it is then drawn
	/// at, at its position with no angles.
	/// </summary>
	public Transform3 PlaceForDraw() {
		Position = new Vec3i(Position.X, Position.Y, 0);
		var frame = Transform3.Identity;
		frame.X = Position.X;
		frame.Y = Position.Y;
		frame.Z = 0;
		return frame;
	}

	/// <summary>
	/// The tick's step: sequence 0 one on, modulo <paramref name="frameCount"/>; a count of 0, where retail divides by
	/// 0, leaves it standing, an engine guard.
	/// </summary>
	internal void Step(int frameCount) {
		if (frameCount > 0) {
			CellFrame = (short)(((ushort)CellFrame + 1) % frameCount);
		}
	}

	/// <summary><c>FlatObj_DtorExploCopy</c> (<c>00005cd8</c>) with 3: the instance destroyed and the shape queued on its pool.</summary>
	internal void Destroy() => Destroyed = true;
}
