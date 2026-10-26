using HercWorks.Core.Data.File.Dts;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Effects;

/// <summary>
/// A piece of wreckage, the <c>0x4e</c>-byte <c>TS_OBJ</c> of <c>g_DebrisPool</c>
/// (docs/es1/simulation/destruction-effects.md#the-piece--debris_construct-00006168): a root of <c>dts\debris</c>, or a
/// thrown gun's root of <c>dts\mechwpn2</c>, that leaves at one speed tumbling about one axis, falls, bounces on the flat
/// ground and vanishes on the bounce that brings it to rest. <c>DebrisVtable</c> (<c>ES1DBSIM 000908e6</c>)'s.
/// </summary>
public sealed class Es1Debris {
	/// <summary>The type tag <c>Debris_Construct</c> stores at <c>+0</c>, which the distance bands are keyed by.</summary>
	public const byte TypeTag = 1;

	/// <summary><c>Debris_Launch</c>'s speed.</summary>
	public const short LaunchSpeed = 400;

	/// <summary>What <c>Debris_TickUpdate</c> turns the first angle by, takes off the vertical speed, and its drag, Q10.</summary>
	public const short TurnStep = 1500, Gravity = 50, Drag = 50;

	/// <summary>The bounce's rebound, Q10 of the vertical speed, and the rebound under which the piece comes to rest.</summary>
	public const short Rebound = 450, RestSpeed = 100;

	/// <summary>
	/// <c>Debris_Construct</c> (<c>00006168</c>)'s fields: the root <paramref name="shape"/>, the type tag, the point as
	/// the position and the detail bias 0. It writes no velocity: every caller launches the piece.
	/// </summary>
	internal Es1Debris(TSObject? shape, Vec3i point) {
		Shape = shape;
		Position = point;
	}

	/// <summary>The shape instance <c>+0x2e</c>'s shape; null for a root past the shapes, which retail reads past.</summary>
	public TSObject? Shape { get; }

	/// <summary><c>+0x20</c>, the position.</summary>
	public Vec3i Position { get; private set; }

	/// <summary><c>+0x06</c>, <c>+0x08</c> and <c>+0x0a</c>, the <c>TS_OBJ</c>'s angles; the tick turns the first.</summary>
	public (short X, short Y, short Z) Rotation { get; internal set; }

	/// <summary><c>+0x44</c>, the velocity, three <c>int16</c>, per tick.</summary>
	public (short X, short Y, short Z) Velocity { get; private set; }

	/// <summary><c>+0x4a</c>, the detail bias <c>Debris_Draw</c> (<c>000065c4</c>) hands <c>TSDetailPart_SetBias</c> around the draw.</summary>
	public int DetailBias { get; internal set; }

	/// <summary><c>Context_GetTransform</c> of <c>+6</c>: the rotation from its angles, at its position.</summary>
	public Transform3 Frame {
		get {
			var frame = Transform3.FromEuler(Rotation.X, Rotation.Y, Rotation.Z);
			frame.X = Position.X;
			frame.Y = Position.Y;
			frame.Z = Position.Z;
			return frame;
		}
	}

	/// <summary>
	/// <c>Debris_Launch</c> (<c>00006378</c>)(piece, <paramref name="heading"/>, <paramref name="pitch"/>)
	/// (docs/es1/simulation/destruction-effects.md#the-launch--debris_launch-00006378): the angles (0, 0, heading) and
	/// <see cref="LaunchSpeed"/> split along the heading and the pitch, each product <c>Math_Q14Multiply</c>'s, as an
	/// <c>int16</c>.
	/// </summary>
	public void Launch(short heading, short pitch) {
		short horizontal = (short)SimMath.Q14MultiplyRounded(LaunchSpeed, SimTrig.Cos(pitch));
		Rotation = (0, 0, heading);
		Velocity = (
			(short)SimMath.Q14MultiplyRounded(horizontal, SimTrig.Cos(unchecked((short)(heading + 0x4000)))),
			(short)SimMath.Q14MultiplyRounded(horizontal, SimTrig.Cos(heading)),
			(short)SimMath.Q14MultiplyRounded(LaunchSpeed, SimTrig.Cos(unchecked((short)(pitch - 0x4000)))));
	}

	/// <summary>
	/// <c>Debris_TickUpdate</c> (<c>000064b8</c>) (docs/es1/simulation/destruction-effects.md#the-tick--debris_tickupdate-000064b8),
	/// in fixed steps whatever the tick's length: the first angle turned by <see cref="TurnStep"/>; <see cref="Gravity"/>
	/// off the vertical speed; <see cref="Drag"/> of each horizontal speed, Q10, off it; the position moved by the new
	/// velocity; a Z below 0 set to 0 with the vertical speed −Q10 of <see cref="Rebound"/>, answering 1 when that is
	/// under <see cref="RestSpeed"/>; otherwise 0.
	/// </summary>
	internal short Tick() {
		var (vx, vy, vz) = Velocity;
		vz = unchecked((short)(vz - Gravity));
		vx = unchecked((short)(vx - SimMath.Q10MultiplyRounded(Drag, vx)));
		vy = unchecked((short)(vy - SimMath.Q10MultiplyRounded(Drag, vy)));
		int z = Position.Z + vz;
		short answer = 0;
		if (z < 0) {
			z = 0;
			vz = unchecked((short)-SimMath.Q10MultiplyRounded(Rebound, vz));
			if (vz < RestSpeed) {
				answer = 1;
			}
		}

		Velocity = (vx, vy, vz);
		Position = new Vec3i(Position.X + vx, Position.Y + vy, z);
		Rotation = (unchecked((short)(Rotation.X + TurnStep)), Rotation.Y, Rotation.Z);
		return answer;
	}
}
