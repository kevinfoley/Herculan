using System.Numerics;
using Silk.NET.Input;

namespace Herculan.Engine.View;

/// <summary>
/// The left-button drag that turns the outside view under <see cref="Settings.TweakSettingDefinitions.MouseOutsideView"/>:
/// each frame, how far the pointer moved while the button was held, which a game turns into its orbit's yaw and pitch at
/// <see cref="RadiansPerPixel"/>. This engine's own; neither game's retail reads the mouse for its outside view.
/// </summary>
public sealed class MouseOrbitDrag {
	/// <summary>How far the orbit turns per pixel of pointer movement, in radians, across the window and down it alike.</summary>
	public const float RadiansPerPixel = 0.0025f;

	private bool _dragging;
	private Vector2 _lastPosition;

	/// <summary>
	/// The pointer's movement since the last call, in window pixels, while the left button stays held and
	/// <paramref name="active"/> holds; zero otherwise, and on the frame a drag starts. Any frame that is not
	/// <paramref name="active"/> ends the drag, so one started before the view came up does not carry into it.
	/// </summary>
	public Vector2 Read(IMouse? mouse, bool active) {
		if (!active || mouse == null) {
			_dragging = false;
			return Vector2.Zero;
		}

		bool held = mouse.IsButtonPressed(MouseButton.Left);
		var position = mouse.Position;
		var delta = held && _dragging ? position - _lastPosition : Vector2.Zero;
		_dragging = held;
		_lastPosition = position;
		return delta;
	}
}
