namespace Herculan.Es2.Shell;

/// <summary>
/// Where the 640x480 shell canvas lands in the window, and how to get back from a window pixel to the
/// canvas pixel under it.
///
/// <para><b>Why this exists.</b> VSHELL blits its screens at a fixed origin in a 640x480 video mode,
/// so an authored widget rect <i>is</i> a screen rect and its hit test compares authored coordinates
/// directly. Herculan runs in a resizable window, so screen-to-canvas is a real transform, and it has
/// to be the same transform the art was drawn with or click regions drift off their buttons. Both the
/// draw path (<see cref="ShellRenderer"/>) and the hit-test path (<see cref="ShellScreen"/>) take
/// their geometry from here so there is only one definition to be right — the same arrangement, and
/// the same reason, as <see cref="Render.CockpitScreenLayout"/>.</para>
///
/// <para><b>Scaled by window height</b>, uniformly on both axes, and centred. The one departure is
/// the <see cref="Scale"/> clamp: a window narrower than 4:3 would otherwise push the tab strip's
/// outer tabs off both edges, so the fit falls back to the window's width there and the leftover
/// margin moves to the top and bottom. On any window at least 4:3 — which is every real one — the
/// clamp does nothing, <see cref="OriginY"/> is zero, and the scale is purely the height ratio.</para>
///
/// <para>Both spaces put their origin at the top left with +Y down: the canvas because that is how
/// VSHELL authors it, the window because that is what the pointer reports. Unlike the cockpit's
/// layout there is no GL-viewport flip to cross, because the shell draws into one viewport covering
/// the whole window.</para>
/// </summary>
/// <param name="WindowWidth">Framebuffer width in pixels this placement was computed for.</param>
/// <param name="WindowHeight">Framebuffer height in pixels.</param>
/// <param name="Scale">Canvas pixels to window pixels, uniform on both axes.</param>
/// <param name="OriginX">Window x of the canvas's top-left corner.</param>
/// <param name="OriginY">Window y of the same corner.</param>
public readonly record struct ShellScreenLayout(int WindowWidth, int WindowHeight,
		float Scale, float OriginX, float OriginY) {

	/// <summary>Places the canvas in a window of the given framebuffer size.</summary>
	public static ShellScreenLayout Create(int windowWidth, int windowHeight) {
		windowWidth = Math.Max(windowWidth, 1);
		windowHeight = Math.Max(windowHeight, 1);

		float scale = Math.Min(
			windowHeight / (float)ShellLayout.CanvasHeight,
			windowWidth / (float)ShellLayout.CanvasWidth);

		return new ShellScreenLayout(windowWidth, windowHeight, scale,
			OriginX: (windowWidth - ShellLayout.CanvasWidth * scale) / 2f,
			OriginY: (windowHeight - ShellLayout.CanvasHeight * scale) / 2f);
	}

	/// <summary>The window pixel a canvas pixel lands on — the exact inverse of <see cref="WindowToCanvas"/>.</summary>
	public (float X, float Y) CanvasToWindow(float canvasX, float canvasY) =>
		(OriginX + canvasX * Scale, OriginY + canvasY * Scale);

	/// <summary>
	/// The canvas pixel under a window pixel. Not clamped — a point outside the canvas comes back as
	/// an out-of-range canvas coordinate, which <see cref="ContainsCanvas"/> is for.
	/// </summary>
	public (float X, float Y) WindowToCanvas(float windowX, float windowY) => Scale <= 0f
		? (float.NaN, float.NaN)
		: ((windowX - OriginX) / Scale, (windowY - OriginY) / Scale);

	/// <summary>Whether a canvas coordinate falls on the canvas at all, rather than in the letterbox.</summary>
	public bool ContainsCanvas(float canvasX, float canvasY) =>
		canvasX >= 0f && canvasY >= 0f
		&& canvasX < ShellLayout.CanvasWidth && canvasY < ShellLayout.CanvasHeight;
}
