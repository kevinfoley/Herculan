namespace Herculan.Engine.View;

/// <summary>
/// The cockpit's step kick — the view bob a pilot sees when the machine walks:
/// <c>Cockpit_StartStepKick</c> (<c>00434144</c>) and <c>Cockpit_StepKickTick</c>
/// (<c>00434194</c>). It shifts the projection centre, not the camera. See
/// docs/retail/rendering/cockpit-canopy-palette.md#the-step-kick.
///
/// <para>The curve runs on the frame clock in seconds rather than on coarse ticks, and is restarted
/// from <see cref="Sim.MechObject.Footfalls"/> rather than called from the footfall itself.</para>
/// </summary>
public sealed class CockpitViewKick {
	/// <summary>The ten samples of <c>StepKickCurve</c> (<c>0049b046</c>), in device pixels for the 640x480 modes.</summary>
	private static readonly int[] Curve = { 1, 2, 3, 4, 5, 5, 4, 3, 2, 1 };

	/// <summary>
	/// How long one run of the curve lasts, in seconds — the original's <c>0x3c</c> coarse ticks at
	/// <see cref="CoarseTickSeconds"/> each.
	/// </summary>
	public const double DurationSeconds = 0x3c * CoarseTickSeconds;

	/// <summary><c>Time_GetCoarseTicks</c>' unit: <c>GetTickCount() >> 4</c>, so 16 ms.</summary>
	private const double CoarseTickSeconds = 0.016;

	private double _elapsed = DurationSeconds;
	private int _lastFootfalls;

	/// <summary>
	/// This kick's current offset, in the art's device pixels. Positive slides the image up the
	/// screen, which is what a machine dropping onto a planted foot does to what the pilot sees.
	/// </summary>
	public int OffsetPixels {
		get {
			if (_elapsed >= DurationSeconds) {
				return 0;
			}

			// The original's integer index, and its own truncation with it.
			int index = (int)(_elapsed / DurationSeconds * Curve.Length);
			return Curve[index < 0 ? 0 : index >= Curve.Length ? Curve.Length - 1 : index];
		}
	}

	/// <summary>
	/// Advances the curve and restarts it on each new footfall. <paramref name="footfalls"/> is
	/// <see cref="Sim.MechObject.Footfalls"/>; passing a machine's running count rather than a flag
	/// is what lets this be driven from the render loop without missing a step the simulation took
	/// between two frames.
	/// </summary>
	public void Update(double deltaSeconds, int footfalls) {
		if (footfalls != _lastFootfalls) {
			_lastFootfalls = footfalls;
			_elapsed = 0;
			return;
		}

		if (_elapsed < DurationSeconds) {
			_elapsed += deltaSeconds;
		}
	}

	/// <summary>Drops any kick in progress — for leaving the cockpit view.</summary>
	public void Reset() {
		_elapsed = DurationSeconds;
	}
}
