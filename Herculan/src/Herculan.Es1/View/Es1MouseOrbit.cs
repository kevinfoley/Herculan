using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;
using Herculan.Engine.View;
using Silk.NET.Input;

namespace Herculan.Es1.View;

/// <summary>
/// The outside view under <see cref="TweakSettingDefinitions.MouseOutsideView"/>: the controls stay on the machine
/// (<see cref="Es1ViewChain.MouseOrbit"/>), and dragging with the left mouse button turns retail's orbit
/// (<see cref="Es1Camera.TurnOrbit"/>) a <see cref="MouseOrbitDrag.RadiansPerPixel"/> a pixel, across the window turning
/// its heading and down it lowering the eye. The orbit keeps retail's distance, centre and floor
/// (docs/es1/simulation/external-views.md#cam_update), and the view keeps retail's rect and caption. This engine's own:
/// retail steers the orbit with the controls alone.
///
/// <para>While the drag can turn the orbit (<see cref="Up"/>), the left button is the host's
/// (<see cref="Input.Es1HostMouse.HostTakesLeftButton"/>): retail's cockpit takes clicks in its external view on the
/// widgets it no longer draws, which a drag would press.</para>
/// </summary>
/// <param name="held">Whether something over the mission holds the input, the host's pause menu or its free camera.</param>
public sealed class Es1MouseOrbit(Es1ViewChain chain, Func<bool> held) {
	private readonly MouseOrbitDrag _drag = new();

	/// <summary>
	/// Whether the drag turns the orbit: the tweak on, the chain in its outside view and the view manager's external view
	/// up, so not part way through a change, no retail panel up, the preferences panel's own orbit among them, and nothing
	/// of the host's holding the input.
	/// </summary>
	public bool Up => Es1ViewChain.MouseOrbit && !held() && chain.View == Es1ViewChain.OutsideView
		&& chain.Manager is { CurrentView: Es1CockpitViews.ExternalView } manager
		&& (manager.Cockpit.Panels?.OpenCount ?? 0) == 0;

	/// <summary>Once a host frame, before the mission's: the frame's drag turns the orbit, unless the menu bar has the pointer.</summary>
	public void Update(IMouse? mouse, bool imguiWantsMouse) {
		var delta = _drag.Read(mouse, Up && !imguiWantsMouse);
		short heading = Bam(delta.X), pitch = Bam(delta.Y);
		if (heading != 0 || pitch != 0) {
			chain.Camera.TurnOrbit(heading, pitch);
		}
	}

	private static short Bam(float pixels) => (short)BinaryAngle.FromRadians(pixels * MouseOrbitDrag.RadiansPerPixel);
}
