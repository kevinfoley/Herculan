using Herculan.Engine.Numerics;

namespace Herculan.Engine.View;

/// <summary>
/// The debug panel's "steady eye", this engine's own diagnostic and nothing of the original's: it pins the
/// cockpit eye's <i>height</i> to whatever it was the moment the option went on and leaves everything else —
/// the machine's own travel, its lean, the eye's fore/aft swing — alone. That isolates the vertical bob from
/// the ride without touching the animation that produces either, which is the A/B for "is it the eye or the
/// machine?".
/// </summary>
public sealed class SteadyEye {
	private bool _captured;
	private int _riseUnits;

	/// <summary>Whether the pin is on.</summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// Applies the pin to a cockpit eye position: with it off this returns <paramref name="eye"/> untouched, with
	/// it on the eye's height is held at the rise it had the moment the pin was switched on.
	/// </summary>
	public Vec3i PinEyeHeight(Vec3i eye, Vec3i mechPosition) {
		if (!Enabled) {
			_captured = false;
			return eye;
		}

		if (!_captured) {
			_riseUnits = eye.Z - mechPosition.Z;
			_captured = true;
		}

		return new Vec3i(eye.X, eye.Y, mechPosition.Z + _riseUnits);
	}
}
