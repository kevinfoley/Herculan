using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Projectiles;

/// <summary>
/// The projectile base the four classes share, a <c>TS_OBJ</c> of <c>g_ProjectilePool</c> with ES2's fields 6 lower
/// (docs/es1/simulation/projectiles.md#the-projectile-classes): the type tag 3, the angles and the frame built from them,
/// the position, the shape instance, the subtype byte, the owner and the projectile record.
/// </summary>
public abstract class Es1Projectile {
	/// <summary>The type tag every class stores at <c>+0</c>, which the distance bands' shades list is keyed by.</summary>
	public const byte TypeTag = 3;

	/// <summary>
	/// <c>TSObj_Ctor</c> on <paramref name="shape"/>, none for a beam tracer; the subtype byte <c>+0x3b</c> and the
	/// projectile record <c>+0x48</c>.
	/// </summary>
	protected Es1Projectile(TSObject? shape, byte subtype, ProjectileData.Projectile? record) {
		Shape = shape;
		CellFrames = new short[(shape as TSShape)?.SequenceList?.Length ?? 0];
		Subtype = subtype;
		Record = record;
	}

	/// <summary>The shape instance's shape, <c>+0x2e</c>'s; null for none.</summary>
	public TSObject? Shape { get; }

	/// <summary>The shape instance's cell frames, its <c>+8</c>, one per sequence of the shape; each starts at 0.</summary>
	public short[] CellFrames { get; }

	/// <summary><c>+0x3b</c>, the subtype, which picks the type record.</summary>
	public byte Subtype { get; protected set; }

	/// <summary><c>+0x44</c>, the machine that fired, which the sweep skips.</summary>
	public IEs1SimObject? Owner { get; protected set; }

	/// <summary><c>+0x48</c>, the projectile record (docs/es1/formats/weapons-dat-sim.md#the-layout), or null for none.</summary>
	public ProjectileData.Projectile? Record { get; }

	/// <summary><c>+0x20</c>–<c>+0x28</c>, the position, the frame's translation.</summary>
	public Vec3i Position { get; set; }

	/// <summary><c>+6</c>, <c>+8</c> and <c>+0xa</c>: pitch, roll and heading.</summary>
	public (short Pitch, short Roll, short Heading) Angles { get; set; }

	/// <summary><c>+0x2c</c>, the frame-valid word: 0 rebuilds the frame from the angles.</summary>
	public short RotationWord { get; set; }

	/// <summary><c>Context_GetTransform</c> of <c>+6</c>: the rotation from the angles, at the position.</summary>
	public Transform3 Frame {
		get {
			var frame = Transform3.FromEuler(Angles.Pitch, Angles.Roll, Angles.Heading);
			frame.X = Position.X;
			frame.Y = Position.Y;
			frame.Z = Position.Z;
			return frame;
		}
	}

	/// <summary>The cell frame the draw shows: the first sequence's, which a gun round steps; 0 for a shape with none.</summary>
	public virtual int DrawnCellFrame => CellFrames.Length > 0 ? CellFrames[0] : 0;

	/// <summary>Slot <c>+0x18</c>, the tick: 1 when the projectile ends.</summary>
	public abstract short Tick(Es1ProjectileTick tick);

	/// <summary>Slot <c>+0xc</c> with 3, the class destructor, which queues it for the pool; a class's own part first.</summary>
	public virtual void Destroy() {
	}

	/// <summary>
	/// The cell step the missile's and the gun round's ticks share: cell frame <paramref name="sequence"/> one on, modulo
	/// the shape's frame count for it (its <c>+0x20</c> table). Retail divides by a count of 0, which no retail shape
	/// holds; here such a sequence stands.
	/// </summary>
	protected void StepCellFrame(int sequence) {
		var counts = (Shape as TSShape)?.SequenceList;
		if (counts == null || sequence < 0 || sequence >= counts.Length || sequence >= CellFrames.Length
				|| (ushort)counts[sequence] == 0) {
			return;
		}

		CellFrames[sequence] = (short)(((ushort)CellFrames[sequence] + 1) % (ushort)counts[sequence]);
	}

	/// <summary>
	/// The sweep a missile's and a gun round's ticks run over the step <paramref name="step"/> along the frame's Y axis
	/// (<c>Transform_ApplyToShortPoint</c>), with the type record's <paramref name="slack"/> as the ray's clearance:
	/// <see cref="Es1ShotSweep.Raycast"/> of the shot <paramref name="shot"/> builds on the ray; a strike answers true,
	/// otherwise the position takes the step's end.
	/// </summary>
	protected bool SweepStep(Es1ProjectileTick tick, short step, short slack, Func<Es1Ray, Es1Shot> shot) {
		var frame = Frame;
		var stepEnd = frame.TransformPoint(0, step, 0);
		if (Es1ShotSweep.Raycast(shot(new Es1Ray(frame, step, slack)), tick.Globals) != 0) {
			return true;
		}

		Position = stepEnd;
		return false;
	}
}
