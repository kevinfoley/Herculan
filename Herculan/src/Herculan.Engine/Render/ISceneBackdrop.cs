namespace Herculan.Engine.Render;

/// <summary>
/// What a game paints behind everything in a pass, in place of <see cref="SceneRenderer"/>'s own sky — see
/// <see cref="SceneRenderer.Backdrop"/>. Each game's simulator paints its backdrop before any shape, so the scene
/// draws straight over it.
/// </summary>
public interface ISceneBackdrop {
	/// <summary>
	/// Paints the backdrop into the viewport (<paramref name="viewportX"/>, <paramref name="viewportY"/>,
	/// <paramref name="viewportWidth"/>, <paramref name="viewportHeight"/>), origin bottom-left as <c>GL.Viewport</c>
	/// takes it, which the renderer has already set. It leaves the depth test enabled and depth writes on when it
	/// returns.
	/// </summary>
	void Draw(Camera camera, int viewportX, int viewportY, int viewportWidth, int viewportHeight);
}
