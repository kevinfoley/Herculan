using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The compass's power-up wind-up: on taking a walking machine the heading tape starts at north and
/// winds round to the real heading instead of reading it from the first frame. The gate
/// <c>Gunsight_UpdateAndPaint</c> (<c>0043d6dc</c>) puts in front of the angle it hands the tape,
/// armed by <c>Widget_BeginPowerUpAnimation</c> (<c>00438ddc</c>) from <c>Cockpit_PowerUpTick</c>
/// (<c>00432924</c>).
///
/// <para>It does not run every mission: a flyer's cockpit is marked finished before it starts, and a
/// heading past <see cref="HalfTurn"/> finishes on its first frame. Both of those, and the ramp's
/// derivation, are docs/retail/formats/cockpit-gunsight-hud.md's power-up wind-up section. The second is reproduced
/// here rather than coded around — <see cref="Angle"/> arms and evaluates in one call, as the
/// original's tick and paint do in one pass, so it falls out of the arithmetic. The original reads
/// the clock once to arm and again after the world draw to evaluate; this reads it once, so the
/// arming frame's ramp is always zero, where retail's is zero unless a coarse tick falls during that
/// draw.</para>
/// </summary>
public sealed class HeadingTapeSweep {
	/// <summary>
	/// How far the ramp moves per coarse tick, the original's own <c>0x32</c>. A coarse tick is 16 ms
	/// (see <see cref="Herculan.Engine.Audio.GameAudio.CoarseTickSeconds"/>), so the compass winds at
	/// about 17 degrees a second and a half turn takes some ten seconds.
	/// </summary>
	public const int RampPerTick = 0x32;

	/// <summary>The half-turn boundary: at or below this the ramp climbs, above it the ramp descends.</summary>
	public const int HalfTurn = 0x8000;

	private readonly long _poweredAt;
	private bool _armed;
	private bool _latched;
	private long _armedAt;

	private HeadingTapeSweep(long poweredAt, bool latched) {
		_poweredAt = poweredAt;
		_latched = latched;
	}

	/// <summary>
	/// The sweep for a cockpit powering up at <paramref name="coarseTicks"/>. A flyer's comes back
	/// already latched and never moves the tape — <c>Gau_BuildCockpitWidgets</c>' own branch, on the
	/// same <c>FlyerFlag</c> that gates the engine hum in
	/// <see cref="Herculan.Engine.Audio.GameAudio.PowerUp"/>.
	/// </summary>
	public static HeadingTapeSweep ForPowerUp(MechObject pilot, long coarseTicks) {
		ArgumentNullException.ThrowIfNull(pilot);
		return new HeadingTapeSweep(coarseTicks, latched: pilot.Type.IsFlyer);
	}

	/// <summary>Whether the tape is still winding — false before it starts and once it has latched.</summary>
	public bool Sweeping => _armed && !_latched;

	/// <summary>
	/// What the gunsight hands the tape this frame: the ramp while the wind-up is running, the
	/// machine's own heading otherwise. Call it once a frame — it is the original's paint-time state
	/// machine and latches as it goes.
	/// </summary>
	public short Angle(short heading, long coarseTicks) {
		if (_latched) {
			return heading;
		}

		// Cockpit_PowerUpTick arms the widget on the first tick after the power-up began, not on the
		// power-up's own tick: its gate is a plain elapsed != 0.
		if (!_armed) {
			if (coarseTicks == _poweredAt) {
				return heading;
			}

			_armed = true;
			_armedAt = coarseTicks;
		}

		ushort target = (ushort)heading;
		ushort ramp = (ushort)((coarseTicks - _armedAt) * RampPerTick);

		if (target <= HalfTurn) {
			if (target <= ramp) {
				_latched = true;
				return heading;
			}

			return (short)ramp;
		}

		ushort descending = (ushort)-ramp;
		if (descending <= target) {
			_latched = true;
			return heading;
		}

		return (short)descending;
	}
}
