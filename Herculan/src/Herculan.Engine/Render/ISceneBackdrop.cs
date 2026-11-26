namespace Herculan.Engine.Render;

/// <summary>
/// What a game paints behind everything in a pass, its sky and whatever else stands behind the scene — see
/// <see cref="SceneRenderer.Backdrop"/>. Each game's simulator paints its backdrop before any shape, so the scene
/// draws straight over it.
/// </summary>
public interface ISceneBackdrop {
	/// <summary>
	/// Paints the backdrop into the viewport, origin bottom-left as <c>GL.Viewport</c>
	/// takes it, which the renderer has already set. It leaves the depth test enabled and depth writes on when it
	/// returns.
	/// </summary>
	/// <param name="camera">The camera.</param>
	/// <param name="viewportX">The viewport's left edge.</param>
	/// <param name="viewportY">The viewport's bottom edge.</param>
	/// <param name="viewportWidth">The viewport's width.</param>
	/// <param name="viewportHeight">The viewport's height.</param>
	void Draw(Camera camera, int viewportX, int viewportY, int viewportWidth, int viewportHeight);
}
