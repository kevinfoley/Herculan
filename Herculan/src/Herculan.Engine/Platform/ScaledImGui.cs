using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;

namespace Herculan.Engine.Platform;

/// <summary>
/// Silk's <see cref="ImGuiController"/> sized for the display its window is on: the font rasterised at the display's
/// scale (<see cref="EngineWindow.ContentScale"/>) and the style's paddings, spacings and roundings scaled to match,
/// so the hosts' menus and panels read the same at Windows' 250% as at 100%. Silk's controller does neither: it gives
/// ImGui the window's size in window coordinates and the framebuffer-to-window ratio as the framebuffer scale, and on
/// Windows, where window coordinates are pixels, that ratio is 1 however dense the display.
///
/// <para>A size the hosts give ImGui in pixels is a 100% display's, and goes through <see cref="Scaled(float)"/>.</para>
///
/// <para>When the window moves to a display of another scale, or the scale setting changes, the controller is built
/// again: Silk bakes the font atlas once, in the constructor, and keeps rebuilding its texture private. Building
/// again forgets ImGui's own state with the old context, such as where a panel was dragged to.</para>
/// </summary>
public sealed class ScaledImGui : IDisposable {
	/// <summary>The font's size in pixels on a 100% display.</summary>
	public const float FontSize = 16f;

	private readonly GL _gl;
	private readonly EngineWindow _window;
	private readonly IInputContext _input;
	private readonly string _fontPath;
	private ImGuiController _controller;
	private float _builtContentScale;
	private float _builtClientScale;

	/// <param name="fontPath">The TrueType font every ImGui window draws in.</param>
	public ScaledImGui(GL gl, EngineWindow window, IInputContext input, string fontPath) {
		_gl = gl;
		_window = window;
		_input = input;
		_fontPath = fontPath;
		_controller = Build();
	}

	/// <summary>
	/// A size in pixels on a 100% display, in the current ImGui frame's units. Read off the frame's font size, so
	/// only between <see cref="Update"/> and <see cref="Render"/>.
	/// </summary>
	public static float Scaled(float size) => size * ImGui.GetFontSize() / FontSize;

	/// <inheritdoc cref="Scaled(float)"/>
	public static Vector2 Scaled(Vector2 size) => size * (ImGui.GetFontSize() / FontSize);

	/// <summary>
	/// The widest a tooltip may be, in pixels on a 100% display. A longer text is wrapped onto further lines rather
	/// than drawn as one line across the window.
	/// </summary>
	public const float TooltipMaxWidth = 400f;

	/// <summary>
	/// <see cref="ImGui.SetTooltip(string)"/>, wrapped to <see cref="TooltipMaxWidth"/>; a shorter text keeps its own
	/// width. Inside the ImGui frame only, as <see cref="Scaled(float)"/>.
	/// </summary>
	public static void Tooltip(string text) {
		ImGui.BeginTooltip();
		// The wrap position is in the tooltip's own coordinates, so the window's padding comes off it to keep the
		// whole tooltip, not just its text, inside the width.
		ImGui.PushTextWrapPos(Scaled(TooltipMaxWidth) - ImGui.GetStyle().WindowPadding.X);
		ImGui.TextUnformatted(text);
		ImGui.PopTextWrapPos();
		ImGui.EndTooltip();
	}

	/// <summary>Starts the next ImGui frame, first building the controller again if the display's scale changed.</summary>
	public void Update(float deltaSeconds) {
		// Compared loosely: the client scale is a ratio of two sizes, which a resize need not update together.
		if (_window.ClientScale is { } clientScale
				&& (MathF.Abs(_window.ContentScale - _builtContentScale) > 0.01f
					|| MathF.Abs(clientScale - _builtClientScale) > 0.01f)) {
			_controller.Dispose();
			_controller = Build();
		}

		_controller.Update(deltaSeconds);
	}

	/// <summary>
	/// Draws the frame's ImGui windows over the whole framebuffer. Silk's controller leaves the GL viewport as it finds
	/// it, so a window whose host never sets one would draw ImGui into the viewport the context was created with, at
	/// the window's size before <see cref="EngineWindow"/> scaled it or the player resized it, while the pointer still
	/// hits the layout across the whole window.
	/// </summary>
	public void Render() {
		var framebuffer = _window.FramebufferSize;
		_gl.Viewport(0, 0, (uint)Math.Max(framebuffer.X, 1), (uint)Math.Max(framebuffer.Y, 1));
		_controller.Render();
	}

	public void Dispose() => _controller.Dispose();

	// The font is baked at the display's pixel size, and drawn at the window-coordinate size: on Windows the two are
	// the same; where window coordinates are already scaled (macOS), FontGlobalScale takes the pixel-sized atlas back
	// down so the text stays sharp. The style is a 100% display's until scaled here; the constructor's frame, which
	// has already begun with it, holds nothing and is never drawn.
	private ImGuiController Build() {
		_builtContentScale = _window.ContentScale;
		_builtClientScale = _window.ClientScale ?? _builtContentScale;
		var controller = new ImGuiController(_gl, _window.View, _input,
			new ImGuiFontConfig(_fontPath, (int)MathF.Round(FontSize * _builtContentScale)));
		ImGui.GetIO().FontGlobalScale = _builtClientScale / _builtContentScale;
		ImGui.GetStyle().ScaleAllSizes(_builtClientScale);
		ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg].W = .975f;
		return controller;
	}
}
