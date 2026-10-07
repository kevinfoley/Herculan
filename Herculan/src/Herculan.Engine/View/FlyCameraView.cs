using Herculan.Engine.Render;
using Herculan.Engine.Sim;

namespace Herculan.Engine.View;

/// <summary>Puts the render camera where a <see cref="FlyCameraObject"/> is.</summary>
public static class FlyCameraView {
	/// <summary>Copies this tick's pose onto a camera for rendering.</summary>
	public static void ApplyTo(this FlyCameraObject observer, Camera camera) {
		ArgumentNullException.ThrowIfNull(observer);
		ArgumentNullException.ThrowIfNull(camera);
		camera.Position = observer.Position;
		camera.Yaw = observer.Heading;
		camera.Pitch = observer.Pitch;
		camera.Roll = 0;
	}
}
