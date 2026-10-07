using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;

namespace Herculan.Engine.View;

/// <summary>
/// The chase view's memory of where the player has been: <c>Cam_Update</c> (<c>004011a0</c>) records the player's
/// position, euler triple and the frame's length into a 50-entry ring every frame
/// (<c>ChaseTrail_Positions</c> (<c>004a7c80</c>), <c>ChaseTrail_Rotations</c> (<c>004a7ed8</c>), <c>ChaseTrail_Lengths</c> (<c>004a8004</c>), cursor <c>ChaseTrail_Cursor</c> (<c>004a80cc</c>)), and the
/// chase camera sits wherever that ring puts the player <see cref="Delay"/> ago. The walk back and its
/// interpolation are docs/retail/simulation/external-views.md's "Mode 3: the chase".
/// </summary>
public sealed class PlayerTrail {
	/// <summary>How many frames the ring holds.</summary>
	public const int Length = 50;

	/// <summary>How far behind the player the chase view runs, in the units <see cref="FrameRate"/> measures a frame in.</summary>
	public const int Delay = 8000;

	/// <summary>
	/// The rate a frame's length is measured at: <c>Math_IntegrateRateOverTick(0x400)</c>, so a
	/// vanilla 40 ms tick is 324 and <see cref="Delay"/> is about a second.
	/// </summary>
	public const short FrameRate = 0x400;

	private readonly Vec3i[] _positions = new Vec3i[Length];
	private readonly (short X, short Y, short Z)[] _rotations = new (short, short, short)[Length];
	private readonly int[] _lengths = new int[Length];
	private int _cursor;

	/// <summary>Records one frame of <paramref name="player"/>, at the current tick length.</summary>
	public void Record(SimObject player) {
		_positions[_cursor] = player.Position;
		_rotations[_cursor] = (player.Pitch, player.Roll, (short)player.Heading);
		_lengths[_cursor] = SimMath.IntegrateRateOverTick(FrameRate);
		_cursor = (_cursor + 1) % Length;
	}

	/// <summary>
	/// Where the chase camera goes. Walks back from the newest frame until <see cref="Delay"/> is used
	/// up, round the ring as many times as that takes, and interpolates by the remainder over that
	/// frame's length — between the frame it stopped on and the one after it, which is one frame later
	/// than the delay it measured.
	///
	/// <para>Null when every recorded length is zero, where the original's walk never ends. Its frame
	/// gate (<see cref="ExternalViewChain.ChaseFrameGate"/>) keeps a fresh ring from reaching here, and
	/// this engine's tick always has a length, so the null is this engine's guard rather than a case
	/// retail meets.</para>
	/// </summary>
	public (Vec3i Position, (short X, short Y, short Z) Rotation)? Chase() {
		if (Array.TrueForAll(_lengths, length => length <= 0)) {
			return null;
		}

		int remaining = Delay;
		int older = _cursor;
		while (true) {
			older = older == 0 ? Length - 1 : older - 1;
			if (remaining <= _lengths[older]) {
				break;
			}

			remaining -= _lengths[older];
		}

		int fraction = SimMath.Q16Divide(remaining, _lengths[older]);
		int newer = older + 1 > Length - 1 ? 0 : older + 1;

		var from = _positions[newer];
		var to = _positions[older];
		var position = new Vec3i(
			from.X + SimMath.Q16Multiply(to.X - from.X, fraction),
			from.Y + SimMath.Q16Multiply(to.Y - from.Y, fraction),
			from.Z + SimMath.Q16Multiply(to.Z - from.Z, fraction));

		var a = _rotations[newer];
		var b = _rotations[older];
		var rotation = (
			(short)(a.X + SimMath.Q16Multiply((short)(b.X - a.X), fraction)),
			(short)(a.Y + SimMath.Q16Multiply((short)(b.Y - a.Y), fraction)),
			(short)(a.Z + SimMath.Q16Multiply((short)(b.Z - a.Z), fraction)));

		return (position, rotation);
	}
}
