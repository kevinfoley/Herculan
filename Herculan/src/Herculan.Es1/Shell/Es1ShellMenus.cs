using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Silk.NET.Input;

namespace Herculan.Es1.Shell;

/// <summary>
/// The ES1 shell's menus over its <see cref="FrontEndWindow"/>: for now no bar and no windows, so the frame's ImGui draws
/// nothing over the shell. This engine's own, as the seam is; retail's shell has no such bar. ES2's game-agnostic panels
/// (About, Accessibility, the interface language) can be moved to the core and offered here later.
/// </summary>
public sealed class Es1ShellMenus : IFrontEndMenus {
	// Neither is raised: there is no menu choice to end the turn and no window that saves the pad's settings.
	public event Action? CloseRequested {
		add { }
		remove { }
	}

	public event Action<GamepadMouseSettings>? PadPointerSettingsChanged {
		add { }
		remove { }
	}

	public bool Visible => false;

	public bool PanelOpen => false;

	public bool PadNavigationSuspended => false;

	public void Attach(ScaledImGui imgui, IInputContext input) {
	}

	public void Show() {
	}

	public bool BackOut() => false;

	public void PadBack() {
	}

	public void OfferWelcome() {
	}

	public void Draw(nint owner, bool fullScreen, bool revealOnHover) {
	}

	/// <summary>On standard error, there being no window of the menus' to show it in.</summary>
	public void ShowError(string message, string detail) => Console.Error.WriteLine($"{message} {detail}");

	/// <inheritdoc cref="ShowError"/>
	public void ShowErrorOutsideWindow(string message, string detail) => ShowError(message, detail);

	public void Dispose() {
	}
}
