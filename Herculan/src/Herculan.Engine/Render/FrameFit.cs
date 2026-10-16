using Silk.NET.Maths;

namespace Herculan.Engine.Render;

/// <summary>
/// Where a frame drawn at its own size sits in a window: scaled by one factor to the largest size that fits inside the
/// window, and centred, so a window of another shape gets black bars at its sides or at its top and bottom. Both sizes
/// are in framebuffer pixels. A frame the size of its window fills it unscaled (<see cref="Whole"/>).
/// </summary>
/// <param name="Frame">The size the frame is drawn at.</param>
/// <param name="Window">The window's framebuffer size.</param>
public readonly record struct FrameFit(Vector2D<int> Frame, Vector2D<int> Window) {
	/// <summary>A frame drawn at the window's own size, filling it.</summary>
	public static FrameFit Whole(Vector2D<int> window) => new(window, window);

	/// <summary>Whether the frame is drawn at a size other than the window's, and so has to be scaled into it.</summary>
	public bool Scaled => Frame != Window;

	/// <summary>The frame's place in the window, from the window's top-left corner, in framebuffer pixels.</summary>
	public (int Left, int Top, int Width, int Height) Placement {
		get {
			if (Frame.X <= 0 || Frame.Y <= 0) {
				return (0, 0, 0, 0);
			}

			float scale = MathF.Min(Window.X / (float)Frame.X, Window.Y / (float)Frame.Y);
			int width = (int)MathF.Round(Frame.X * scale);
			int height = (int)MathF.Round(Frame.Y * scale);
			return ((Window.X - width) / 2, (Window.Y - height) / 2, width, height);
		}
	}

	/// <summary>
	/// A window pixel, from its top-left corner, as a pixel of the frame, from the frame's: outside the frame's own
	/// range where it is over a bar. Unchanged while the window has no area for the frame, as when it is minimized.
	/// </summary>
	public (float X, float Y) ToFrame(float x, float y) {
		var (left, top, width, height) = Placement;
		return width > 0 && height > 0
			? ((x - left) * Frame.X / width, (y - top) * Frame.Y / height)
			: (x, y);
	}

	/// <summary>A pixel of the frame as a window pixel: <see cref="ToFrame"/> backwards.</summary>
	public (float X, float Y) ToWindow(float x, float y) {
		var (left, top, width, height) = Placement;
		return width > 0 && height > 0 && Frame.X > 0 && Frame.Y > 0
			? (left + x * width / Frame.X, top + y * height / Frame.Y)
			: (x, y);
	}
}
