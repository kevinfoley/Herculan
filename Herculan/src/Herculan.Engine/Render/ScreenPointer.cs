using Silk.NET.Maths;

namespace Herculan.Engine.Render;

/// <summary>
/// The host's pointer over a window that shows a low-resolution screen at <see cref="Fit"/>'s placement: a position in
/// the window's client coordinates, which the pointer reports in, as a whole pixel of the screen, held to the screen's
/// own range as a mouse driver's limits hold its pointer; and a pixel of the screen back as a client position. The client
/// size and the framebuffer size differ on a scaled display, and <see cref="Fit"/> is in the framebuffer's pixels.
/// </summary>
/// <param name="Fit">The screen's frame placed in the window's framebuffer.</param>
/// <param name="Client">The window's client size, in the coordinates the pointer reports.</param>
/// <param name="Screen">The screen's size in its own pixels, which the frame shows whole.</param>
public readonly record struct ScreenPointer(FrameFit Fit, Vector2D<int> Client, Vector2D<int> Screen) {
	/// <summary>
	/// The screen pixel under the client position (<paramref name="clientX"/>, <paramref name="clientY"/>), each axis held
	/// to 0 – the screen's size less 1, so a pointer over a bar or outside the window gives the nearest edge pixel.
	/// </summary>
	public (int X, int Y) ToScreen(float clientX, float clientY) {
		var window = Fit.Window;
		var (frameX, frameY) = Fit.ToFrame(clientX * window.X / Math.Max(Client.X, 1), clientY * window.Y / Math.Max(Client.Y, 1));
		var frame = Fit.Frame;
		int x = (int)MathF.Floor(frameX * Screen.X / Math.Max(frame.X, 1));
		int y = (int)MathF.Floor(frameY * Screen.Y / Math.Max(frame.Y, 1));
		return (Math.Clamp(x, 0, Math.Max(Screen.X - 1, 0)), Math.Clamp(y, 0, Math.Max(Screen.Y - 1, 0)));
	}

	/// <summary>Whether the client position (<paramref name="clientX"/>, <paramref name="clientY"/>) is over the screen's frame, not over a bar beside it or outside the window.</summary>
	public bool Over(float clientX, float clientY) {
		var window = Fit.Window;
		var (frameX, frameY) = Fit.ToFrame(clientX * window.X / Math.Max(Client.X, 1), clientY * window.Y / Math.Max(Client.Y, 1));
		return frameX >= 0f && frameY >= 0f && frameX < Fit.Frame.X && frameY < Fit.Frame.Y;
	}

	/// <summary>Whether the client position (<paramref name="clientX"/>, <paramref name="clientY"/>) is inside the window's client area.</summary>
	public bool InWindow(float clientX, float clientY) =>
		clientX >= 0f && clientY >= 0f && clientX < Client.X && clientY < Client.Y;

	/// <summary>The client position of the middle of screen pixel (<paramref name="x"/>, <paramref name="y"/>), which <see cref="ToScreen"/> maps back to it.</summary>
	public (float X, float Y) ToClient(int x, int y) {
		var frame = Fit.Frame;
		var (windowX, windowY) = Fit.ToWindow((x + 0.5f) * frame.X / Math.Max(Screen.X, 1), (y + 0.5f) * frame.Y / Math.Max(Screen.Y, 1));
		var window = Fit.Window;
		return (windowX * Client.X / Math.Max(window.X, 1), windowY * Client.Y / Math.Max(window.Y, 1));
	}
}
