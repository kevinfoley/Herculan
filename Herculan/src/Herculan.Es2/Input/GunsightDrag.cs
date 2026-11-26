using Herculan.Engine.Input;
using Herculan.Es2.Sim;

namespace Herculan.Es2.Input;

/// <summary>
/// The pointer as <c>Sim_PollPlayerInput</c> reads it for the gunsight drag: the right-button byte <c>+0x0b</c> of
/// the frame's device block, and the position at <c>+0x02</c>/<c>+0x06</c> in the forward view's art pixels.
/// The left button is this engine's: the held left button, which fires while the drag is armed (see
/// <see cref="GunsightDrag.Firing"/>), and which only the tweak's caller sets.
/// </summary>
/// <param name="Right">Whether the right button is held.</param>
/// <param name="X">The X in the forward view's art pixels.</param>
/// <param name="Y">The Y in the forward view's art pixels.</param>
/// <param name="Left">Whether the left button is held; this engine's own.</param>
public readonly record struct GunsightPointer(bool Right, int X, int Y, bool Left = false) {
	/// <summary>No right button: what a replay, a modal panel's loop or a missing pointer leaves in the block.</summary>
	public static GunsightPointer Released => default;
}

/// <summary>
/// The gunsight drag (docs/retail/simulation/gunsight-drag.md): with the right button held from a
/// press inside the gunsight complex's rect, the pointer's distance from that press aims the turret, squared, added to
/// the twist and pitch <c>Sim_PollPlayerInput</c> (<c>00460764</c>) hands the machine after either of its branches.
/// Retail's mouse record drops the held right button at the first move, so it never acts there; which button state
/// feeds it is <see cref="Settings.Es2TweakSettingDefinitions.FixGunsightDrag"/>'s.
/// </summary>
public sealed class GunsightDrag {
	// 0049f702, and the press point at 004d2afa / 004d2afe.
	private bool _armed;
	private int _anchorX;
	private int _anchorY;
	private bool _left;

	// 0049f6f8 / 0049f6fc: the two terms, kept between frames — the frame that arms leaves them as they were.
	private int _twist;
	private int _pitch;

	/// <summary>The twist term, positive with the pointer right of the press.</summary>
	public int Twist => _twist;

	/// <summary>The pitch term, positive with the pointer above the press.</summary>
	public int Pitch => _pitch;

	/// <summary>Whether the drag is armed: the right button held since it went down, or came, inside the rect.</summary>
	public bool Armed => _armed;

	/// <summary>
	/// Whether the left button is held with the drag armed, which fires as [Space] does, so a pilot aiming with the
	/// mouse need not reach for the keyboard. This engine's: retail has no mouse trigger.
	/// </summary>
	public bool Firing => _armed && _left;

	/// <summary>
	/// One frame of the drag. The arm rect is the complex's rect, inclusive,
	/// in the same space as the pointer.
	/// </summary>
	/// <param name="pointer">The pointer.</param>
	/// <param name="armX0">The arm rect's left edge.</param>
	/// <param name="armY0">The arm rect's top edge.</param>
	/// <param name="armX1">The arm rect's right edge.</param>
	/// <param name="armY1">The arm rect's bottom edge.</param>
	public void Update(GunsightPointer pointer, int armX0, int armY0, int armX1, int armY1) {
		_left = pointer.Left;
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
