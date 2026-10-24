using System.Numerics;
using System.Runtime.InteropServices;
using Herculan.Engine.Settings;
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
/// again forgets ImGui's own state with the old context, such as where a panel was dragged to. So does turning the
/// <see cref="A11ySettingDefinitions.LargerFont"/> setting on or off, which bakes the font at
/// <see cref="LargerFontSize"/>; every size given through <see cref="Scaled(float)"/> grows with it.</para>
/// </summary>
public sealed class ScaledImGui : IDisposable {
	/// <summary>The font's size in pixels on a 100% display, and the size every other size in pixels is given against.</summary>
	public const float FontSize = 20f;

	/// <summary>The font's size in pixels on a 100% display under <see cref="A11ySettingDefinitions.LargerFont"/>.</summary>
	public const float LargerFontSize = 40f;

	/// <summary>Font size multiplier for large font.</summary>
	public const float RadialFontScale = 1.5f;

	// The characters every font is baked with: ImGui's default Basic Latin and Latin-1 Supplement, and General
	// Punctuation for the .lang files' dashes, ellipsis and German quotes, which would otherwise draw as '?'. Pinned for
	// the life of the process, since ImGui keeps the pointer for every rebuild of the atlas.
	private static readonly ushort[] GlyphRanges = [0x0020, 0x00ff, 0x2000, 0x206f, 0];
	private static readonly nint GlyphRangesPointer = GCHandle.Alloc(GlyphRanges, GCHandleType.Pinned).AddrOfPinnedObject();

	private readonly GL _gl;
	private readonly EngineWindow _window;
	private readonly IInputContext _input;
	private readonly string _fontPath;
	private readonly string? _italicFontPath;
	private readonly string? _boldFontPath;
	private ImGuiController _controller;
	private float _builtContentScale;
	private float _builtClientScale;
	private float _builtFontSize;

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
	/// The same font rasterised at <see cref="RadialFontScale"/> times the size, for text drawn that large, which the
	/// ordinary font would draw blurred. Drawn at <see cref="RadialFontSize"/>.
	/// </summary>
	public ImFontPtr RadialFont { get; private set; }

	/// <summary>
	/// The font's italic face at the ordinary size, or null when the constructor was given none. Rebuilt with the
	/// controller, so it is read each time it is drawn with rather than kept.
	/// </summary>
	public ImFontPtr? ItalicFont { get; private set; }

	/// <summary>The font's bold face at the ordinary size, or null when the constructor was given none; rebuilt as <see cref="ItalicFont"/> is.</summary>
	public ImFontPtr? BoldFont { get; private set; }

	/// <summary>The size <see cref="RadialFont"/> is drawn at in the current ImGui frame, as <see cref="Scaled(float)"/>.</summary>
	public static float RadialFontSize => ImGui.GetFontSize() * RadialFontScale;

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

	/// <summary>
	/// Starts the next ImGui frame, first building the controller again if the display's scale or the font's size
	/// changed.
	/// </summary>
	public void Update(float deltaSeconds) {
		// Compared loosely: the client scale is a ratio of two sizes, which a resize need not update together.
		if ((_window.ClientScale is { } clientScale
				&& (MathF.Abs(_window.ContentScale - _builtContentScale) > 0.01f
					|| MathF.Abs(clientScale - _builtClientScale) > 0.01f))
				|| ChosenFontSize != _builtFontSize) {
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

	private static float ChosenFontSize =>
		A11ySettings.Current.GetSettingValue(A11ySettingDefinitions.LargerFont) ? LargerFontSize : FontSize;

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

	// The style is a 100% display's until scaled here; the constructor's frame, which has already begun with it, holds
	// nothing and is never drawn. Silk's controller adds no font of its own when given no font config.
	private ImGuiController Build() {
		_builtContentScale = _window.ContentScale;
		_builtClientScale = _window.ClientScale ?? _builtContentScale;
		_builtFontSize = ChosenFontSize;
		var controller = new ImGuiController(_gl, _window.View, _input, null, () => {
			(RadialFont, ItalicFont, BoldFont) = AddFonts(ImGui.GetIO().Fonts, _fontPath, _italicFontPath, _builtFontSize,
				_builtContentScale, _boldFontPath);
		});
		ApplyScale(_builtContentScale, _builtClientScale, _builtFontSize);
		return controller;
	}

	/// <summary>
	/// Adds the fonts every ImGui window is drawn in to <paramref name="fonts"/>, before the atlas is built:
	/// <paramref name="fontPath"/> at <paramref name="fontSize"/> on a display of <paramref name="contentScale"/>, first
	/// so that it is the default, then the <see cref="RadialFont"/>, <see cref="ItalicFont"/> and <see cref="BoldFont"/>
	/// faces, which it returns. The hosts' windows add them through Silk's controller, and a test that lays the panels
	/// out without a window adds them to its own context.
	/// </summary>
	public static (ImFontPtr Radial, ImFontPtr? Italic, ImFontPtr? Bold) AddFonts(ImFontAtlasPtr fonts, string fontPath,
			string? italicFontPath, float fontSize, float contentScale, string? boldFontPath = null) {
		float pixels = MathF.Round(fontSize * contentScale);
		fonts.AddFontFromFileTTF(fontPath, pixels, default, GlyphRangesPointer);
		var radial = fonts.AddFontFromFileTTF(fontPath, pixels * RadialFontScale, default, GlyphRangesPointer);
		ImFontPtr? italic = italicFontPath != null ? fonts.AddFontFromFileTTF(italicFontPath, pixels, default, GlyphRangesPointer) : null;
		ImFontPtr? bold = boldFontPath != null ? fonts.AddFontFromFileTTF(boldFontPath, pixels, default, GlyphRangesPointer) : null;
		return (radial, italic, bold);
	}

	/// <summary>
	/// Scales the current context's style, which is a 100% display's until then, to the display, once the fonts are
	/// added (<see cref="AddFonts"/>) at <paramref name="fontSize"/>. The font is baked at the display's pixel size, and
	/// drawn at the window-coordinate size: on Windows the two are the same; where window coordinates are already scaled
	/// (macOS), FontGlobalScale takes the pixel-sized atlas back down so the text stays sharp. The scroll bars grow with
	/// the font, so <see cref="LargerFontSize"/> doubles their width.
	/// </summary>
	public static void ApplyScale(float contentScale, float clientScale, float fontSize) {
		var style = ImGui.GetStyle();
		ImGui.GetIO().FontGlobalScale = clientScale / contentScale;
		style.ScaleAllSizes(clientScale);
		style.ScrollbarSize *= fontSize / FontSize;
		style.Colors[(int)ImGuiCol.WindowBg].W = .975f;
	}
}
