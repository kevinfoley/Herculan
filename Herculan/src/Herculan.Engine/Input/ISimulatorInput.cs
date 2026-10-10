using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>
/// Where the simulator's input comes from: the window's keyboard and mouse, or a replaying tape's, and whether
/// the debug UI has taken either. Every handler reads <see cref="Keyboard"/> and
/// <see cref="IPointerDevice.Pointer"/> rather than a device, so a replay drives exactly the code a player does.
/// </summary>
public interface ISimulatorInput : IPointerDevice {
	/// <summary>
	/// What every key handler reads: the window's own keyboard, or a replaying tape's keystrokes. <see cref="LiveKeys"/>
	/// stays the window's throughout, because a replay still listens to it for [Ctrl+E], for -D's any-key abort and
	/// for the menu bar.
	/// </summary>
	IKeyState? Keyboard { get; }

	IKeyState? LiveKeys { get; }

	IMouse? Mouse { get; }

	/// <summary>The cockpit's click queue, which both the live mouse and a tape's mouse events feed.</summary>
	CockpitInput Cockpit { get; }

	bool ImGuiHasKeyboard { get; }

	bool ImGuiWantsMouse { get; }

	/// <summary>
	/// Whether the debug UI is typing and the game's keys should go dead. Never during a replay: the tape's
	/// keystrokes are not the player's, and the player typing into the debug panel must not lose them.
	/// </summary>
	bool KeyboardCapturedByImGui { get; }

	/// <summary>Hands the keyboard back to the player once a tape has stopped — -p's hand-over.</summary>
	void TakeLiveKeys();
}
