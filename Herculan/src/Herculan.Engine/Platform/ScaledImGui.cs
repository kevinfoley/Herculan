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

	/// <summary>How many times <see cref="FontSize"/> <see cref="LargeFont"/> is.</summary>
	public const float LargeFontScale = 2f;

	private readonly GL _gl;
	private readonly EngineWindow _window;
	private readonly IInputContext _input;
	private readonly string _fontPath;
	private readonly string? _italicFontPath;
	private readonly string? _boldFontPath;
	private ImGuiController _controller;
	private float _builtContentScale;
	private float _builtClientScale;

	/// <param name="fontPath">The TrueType font every ImGui window draws in.</param>
	/// <param name="italicFontPath">Its italic face, for <see cref="ItalicFont"/>; null for none.</param>
	/// <param name="boldFontPath">Its bold face, for <see cref="BoldFont"/>; null for none.</param>
	public ScaledImGui(GL gl, EngineWindow window, IInputContext input, string fontPath, string? italicFontPath = null,
			string? boldFontPath = null) {
		_gl = gl;
		_window = window;
		_input = input;
		_fontPath = fontPath;
		_italicFontPath = italicFontPath;
		_boldFontPath = boldFontPath;
		_controller = Build();
	}

	/// <summary>
	/// A size in pixels on a 100% display, in the current ImGui frame's units. Read off the frame's font size, so
	/// only between <see cref="Update"/> and <see cref="Render"/>.
	/// </summary>
	public static float Scaled(float size) => size * ImGui.GetFontSize() / FontSize;

	/// <summary>
	/// The same font rasterised at <see cref="LargeFontScale"/> times the size, for text drawn that large, which the
	/// ordinary font would draw blurred. Drawn at <see cref="LargeFontSize"/>.
	/// </summary>
	public ImFontPtr LargeFont { get; private set; }

	/// <summary>
	/// The font's italic face at the ordinary size, or null when the constructor was given none. Rebuilt with the
	/// controller, so it is read each time it is drawn with rather than kept.
	/// </summary>
	public ImFontPtr? ItalicFont { get; private set; }

	/// <summary>The font's bold face at the ordinary size, or null when the constructor was given none; rebuilt as <see cref="ItalicFont"/> is.</summary>
	public ImFontPtr? BoldFont { get; private set; }

	/// <summary>The bold face at <see cref="LargeFontScale"/> times the size, drawn at <see cref="LargeFontSize"/>; null without a bold face.</summary>
	public ImFontPtr? LargeBoldFont { get; private set; }

	/// <summary>The size <see cref="LargeFont"/> is drawn at in the current ImGui frame, as <see cref="Scaled(float)"/>.</summary>
	public static float LargeFontSize => ImGui.GetFontSize() * LargeFontScale;

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

	/// <summary>
	/// Queues a gamepad's state as ImGui's gamepad keys, once a frame before the frame starts, or null for none. Silk's
	/// controller feeds the keyboard and the mouse only. While one is set ImGui navigates by gamepad.
	/// </summary>
	public Action<ImGuiIOPtr>? GamepadFeed { get; set; }

	/// <summary>
	/// Other controls held as the left and right mouse buttons, such as a <see cref="GamepadPointer"/>'s triggers, or null
	/// for none. Read once a frame before the frame starts, alongside the mouse's own buttons.
	/// </summary>
	public Func<(bool Left, bool Right)>? HeldAsMouseButtons { get; set; }

	/// <summary>
	/// Where the pointer is, in window coordinates, when it is not the mouse's cursor, such as a
	/// <see cref="GamepadPointer.Override"/>; null for the mouse's own. Read once a frame before the frame starts. While it
	/// is set ImGui draws its own arrow there, since the window's cursor is hidden.
	/// </summary>
	public Func<Vector2?>? PointerOverride { get; set; }

	/// <summary>Starts the next ImGui frame, first building the controller again if the display's scale changed.</summary>
	public void Update(float deltaSeconds) {
		// Compared loosely: the client scale is a ratio of two sizes, which a resize need not update together.
		if (_window.ClientScale is { } clientScale
				&& (MathF.Abs(_window.ContentScale - _builtContentScale) > 0.01f
					|| MathF.Abs(clientScale - _builtClientScale) > 0.01f)) {
			_controller.Dispose();
			_controller = Build();
		}

		// Before the controller's update, which ends in ImGui's NewFrame, where queued events are taken.
		if (GamepadFeed is { } feed) {
			var io = ImGui.GetIO();
			io.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad;
			io.BackendFlags |= ImGuiBackendFlags.HasGamepad;
			feed(io);
		}

		FeedPointer(PointerOverride?.Invoke());
		if (HeldAsMouseButtons is { } held) {
			var (left, right) = held();
			FeedMouseButton(ImGuiMouseButton.Left, MouseButton.Left, left);
			FeedMouseButton(ImGuiMouseButton.Right, MouseButton.Right, right);
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

	// Silk's controller writes io.MouseDown from the mouse alone just before NewFrame, and NewFrame then applies the queued
	// events over it, so the button's state with the other control's goes in as an event. ImGui drops an event that
	// repeats the button's last state, which it reads from io.MouseDown, so that is set to the opposite first; the
	// controller overwrites it before NewFrame either way.
	private void FeedMouseButton(ImGuiMouseButton button, MouseButton mouseButton, bool alsoHeld) {
		bool down = alsoHeld || (_input.Mice.Count > 0 && _input.Mice[0].IsButtonPressed(mouseButton));
		var io = ImGui.GetIO();
		io.MouseDown[(int)button] = !down;
		io.AddMouseButtonEvent((int)button, down);
	}

	// The controller writes io.MousePos from the mouse just before NewFrame, so a pointer elsewhere goes in as an event, as
	// FeedMouseButton's buttons do, with io.MousePos first set to ImGui's "no pointer" so the event is not dropped as a
	// repeat of the last frame's position. The event has to be taken in the frame it is queued, or that frame keeps the
	// controller's position: ImGui's trickling holds a move queued after a key's or a button's change over to the next
	// frame, and the gamepad's navigation keys change on every frame the stick moves, so the pointer would flick between
	// the two. So trickling is off while the pointer is elsewhere; a press and release within one frame are then missed,
	// which no stick or trigger is quick enough to make.
	private static void FeedPointer(Vector2? pointer) {
		var io = ImGui.GetIO();
		io.MouseDrawCursor = pointer != null;
		io.ConfigInputTrickleEventQueue = pointer == null;
		if (pointer is { } at) {
			io.MousePos = new Vector2(-float.MaxValue, -float.MaxValue);
			io.AddMousePosEvent(at.X, at.Y);
		}
	}

	// The font is baked at the display's pixel size, and drawn at the window-coordinate size: on Windows the two are
	// the same; where window coordinates are already scaled (macOS), FontGlobalScale takes the pixel-sized atlas back
	// down so the text stays sharp. The style is a 100% display's until scaled here; the constructor's frame, which
	// has already begun with it, holds nothing and is never drawn.
	private ImGuiController Build() {
		_builtContentScale = _window.ContentScale;
		_builtClientScale = _window.ClientScale ?? _builtContentScale;
		float pixels = MathF.Round(FontSize * _builtContentScale);
		var controller = new ImGuiController(_gl, _window.View, _input, new ImGuiFontConfig(_fontPath, (int)pixels), () => {
			var fonts = ImGui.GetIO().Fonts;
			LargeFont = fonts.AddFontFromFileTTF(_fontPath, pixels * LargeFontScale);
			ItalicFont = _italicFontPath != null ? fonts.AddFontFromFileTTF(_italicFontPath, pixels) : null;
			BoldFont = _boldFontPath != null ? fonts.AddFontFromFileTTF(_boldFontPath, pixels) : null;
			LargeBoldFont = _boldFontPath != null ? fonts.AddFontFromFileTTF(_boldFontPath, pixels * LargeFontScale) : null;
		});
		ImGui.GetIO().FontGlobalScale = _builtClientScale / _builtContentScale;
		ImGui.GetStyle().ScaleAllSizes(_builtClientScale);
		ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg].W = .975f;
		return controller;
	}
}
