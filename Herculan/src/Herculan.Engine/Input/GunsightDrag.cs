using Herculan.Engine.Sim;

namespace Herculan.Engine.Input;

/// <summary>
/// The pointer as <c>Sim_PollPlayerInput</c> reads it for the gunsight drag: the right-button byte <c>+0x0b</c> of
/// the frame's device block, and the position at <c>+0x02</c>/<c>+0x06</c> in the forward view's art pixels.
/// </summary>
public readonly record struct GunsightPointer(bool Right, int X, int Y) {
	/// <summary>No right button: what a replay, a modal panel's loop or a missing pointer leaves in the block.</summary>
	public static GunsightPointer Released => default;
}

/// <summary>
/// The gunsight drag (docs/retail/simulation/gunsight-drag.md): with the right button held from a
/// press inside the gunsight complex's rect, the pointer's distance from that press aims the turret, squared, added to
/// the twist and pitch <c>Sim_PollPlayerInput</c> (<c>00460764</c>) hands the machine after either of its branches.
/// Retail's mouse record drops the held right button at the first move, so it never acts there; which button state
/// feeds it is <see cref="Settings.TweakSettingDefinitions.FixGunsightDrag"/>'s.
/// </summary>
public sealed class GunsightDrag {
	// 0049f702, and the press point at 004d2afa / 004d2afe.
	private bool _armed;
	private int _anchorX;
	private int _anchorY;

	// 0049f6f8 / 0049f6fc: the two terms, kept between frames — the frame that arms leaves them as they were.
	private int _twist;
	private int _pitch;

	/// <summary>The twist term, positive with the pointer right of the press.</summary>
	public int Twist => _twist;

	/// <summary>The pitch term, positive with the pointer above the press.</summary>
	public int Pitch => _pitch;

	/// <summary>
	/// One frame of the drag. <paramref name="armX0"/> to <paramref name="armY1"/> is the complex's rect, inclusive,
	/// in the same space as the pointer.
	/// </summary>
	public void Update(GunsightPointer pointer, int armX0, int armY0, int armX1, int armY1) {
		if (!pointer.Right) {
			_armed = false;
			_twist = 0;
			_pitch = 0;
			return;
		}

		if (_armed) {
			int dx = _anchorX - pointer.X;
			_twist = (dx * dx) >> 6;
			if (dx > 0) {
				_twist = -_twist;
			}

			int dy = _anchorY - pointer.Y;
			_pitch = (dy * dy) >> 5;
			if (dy < 0) {
				_pitch = -_pitch;
			}

			return;
		}

		if (pointer.X >= armX0 && pointer.X <= armX1 && pointer.Y >= armY0 && pointer.Y <= armY1) {
			_armed = true;
			_anchorX = pointer.X;
			_anchorY = pointer.Y;
		}
	}

	/// <summary>A stick's full deflection, which <see cref="AddTo"/> caps a dragged axis at.</summary>
	public static readonly int StickFull = JoystickReading.ApplyResponse(JoystickReading.RawFull);

	/// <summary>
	/// The machine's controls with the terms added, each axis a term moves held within <see cref="StickFull"/>, so the
	/// drag turns the turret no faster than a stick pushed all the way. The cap is this engine's: the original adds the
	/// terms 16-bit and unclamped, so its turret would turn as fast as the pointer is far. With the tweak off the terms
	/// are as good as always zero, so the cap does not apply to them in practice.
	/// </summary>
	public MechControls AddTo(MechControls controls) =>
		controls with {
			TorsoTwist = Add(controls.TorsoTwist, _twist),
			TorsoPitch = Add(controls.TorsoPitch, _pitch),
		};

	private static short Add(short axis, int term) =>
		term != 0 ? (short)Math.Clamp(axis + term, -StickFull, StickFull) : axis;
}
