using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Engine.Platform;

/// <summary>
/// A game's menu bar over its front end and the windows the bar opens, which <see cref="FrontEndWindow"/> shows, draws
/// and hands the keys, the gamepad and the pointer to. Each game supplies its own; the frame asks nothing more of it than
/// these members. This engine's own; retail has no such bar.
/// </summary>
public interface IFrontEndMenus : IDisposable {
	/// <summary>
	/// Raised by a menu choice that ends the front end's turn, from inside the menus' ImGui frame; the frame closes the
	/// window once that frame is drawn (<see cref="FrontEndWindow.CloseAfterFrame"/>), since the close disposes ImGui.
	/// </summary>
	event Action? CloseRequested;

	/// <summary>Raised when the menus save new settings for the gamepad's pointer (<see cref="GamepadPointer.Settings"/>).</summary>
	event Action<GamepadMouseSettings>? PadPointerSettingsChanged;

	/// <summary>Whether the bar is up.</summary>
	bool Visible { get; }

	/// <summary>Whether one of the bar's windows, or a notice the menus raised without it, is up.</summary>
	bool PanelOpen { get; }

	/// <summary>
	/// Whether ImGui's gamepad navigation is given the pad as released (<see cref="ImGuiGamepad.Suspended"/>), as while a
	/// window is listening for a control to bind.
	/// </summary>
	bool PadNavigationSuspended { get; }

	/// <summary>Takes the frame's ImGui and the window's input context, once both exist.</summary>
	void Attach(ScaledImGui imgui, IInputContext input);

	/// <summary>Raises the bar.</summary>
	void Show();

	/// <summary>[Esc]'s step back: takes down whichever window is up, else hides the bar. False when neither was up.</summary>
	bool BackOut();

	/// <summary>A gamepad's Back (<see cref="GamepadHostButtons.BackPressed"/>), which backs out of the menus or raises the bar.</summary>
	void PadBack();

	/// <summary>What the menus raise at the front end's start; the frame skips it in a <c>--screenshot</c> capture.</summary>
	void OfferWelcome();

	/// <summary>
	/// Draws the bar and its windows inside the frame's ImGui frame; the frame skips it in a <c>--screenshot</c> capture.
	/// <paramref name="owner"/> is the native window a picker belongs to, <paramref name="fullScreen"/> whether that window
	/// is full screen, and <paramref name="revealOnHover"/> whether the pointer at the window's top may raise the bar this
	/// frame.
	/// </summary>
	void Draw(nint owner, bool fullScreen, bool revealOnHover);

	/// <summary>Tells the player that <paramref name="message"/>, with <paramref name="detail"/> saying why, over the window.</summary>
	void ShowError(string message, string detail);

	/// <summary>As <see cref="ShowError"/>, once the window's ImGui is gone, as it is after the window has closed.</summary>
	void ShowErrorOutsideWindow(string message, string detail);
}
