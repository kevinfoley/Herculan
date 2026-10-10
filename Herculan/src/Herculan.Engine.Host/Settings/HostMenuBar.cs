using Herculan.Engine.Cockpit;
using Herculan.Engine.Host.Debugging;
using Herculan.Engine.Host.Localization;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The menu bar [Esc] raises over the shell and over a mission, HERCULAN's own and not retail's, and the
/// panels it opens: Debug (a mission's only, and only under <c>--developer</c>), Tweaks and Settings. Hidden until <see cref="Show"/>, and never
/// drawn into a <c>--screenshot</c> capture. Which [Esc] press reaches it is each host's to decide, since
/// retail takes [Esc] first wherever it has a use for it; over a mission, <see cref="SimulatorFrame"/> decides.
/// </summary>
sealed class HostMenuBar : IEscapeMenu {
	private readonly LocalizationTable _localization;
	private readonly DebugPanel? _debug;
	private readonly PanelCentring _centring = new();

	public HostMenuBar(LocalizationTable localization, TweaksMenu tweaks, SettingsWindow settings, DebugPanel? debug = null) {
		_localization = localization;
		Tweaks = tweaks;
		Settings = settings;
		_debug = debug;
	}

	public TweaksMenu Tweaks { get; }

	public SettingsWindow Settings { get; }

	/// <summary>Whether the bar is up.</summary>
	public bool Visible { get; private set; }

	/// <summary>Whether one of the bar's panels is up.</summary>
	public bool PanelOpen => Tweaks.IsOpen || Settings.IsOpen || _debug?.IsOpen == true;

	/// <summary>Raises the bar.</summary>
	public void Show() => Visible = true;

	/// <summary>
	/// [Esc]'s step back: takes down whichever panel is up, else hides the bar. Returns false when neither
	/// was up, leaving the key to the host. Tweaks goes through its Cancel, so an edit made but not saved is
	/// discarded rather than left applied but unpersisted.
	/// </summary>
	public bool BackOut() {
		if (PanelOpen) {
			if (_debug != null) {
				_debug.IsOpen = false;
			}
			if (Tweaks.IsOpen) {
				Tweaks.Cancel();
			}
			Settings.Close();
			return true;
		}

		if (Visible) {
			Visible = false;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Draws the bar, when it is up, and Tweaks and Settings, when they are; a host with a Debug panel draws
	/// that itself, since it needs the host's state. <paramref name="owner"/> is the native window a folder
	/// picker belongs to, and <paramref name="fullScreen"/> whether that window is full screen, which
	/// <see cref="PanelCentring"/> watches. Call once per frame inside the ImGui frame.
	/// </summary>
	public void Draw(nint owner, bool fullScreen) {
		_centring.Update(fullScreen);

		// A bare item per panel, no checkmark, since each panel closes itself.
		if (Visible && ImGui.BeginMainMenuBar()) {
			if (_debug != null && ImGui.MenuItem(_localization.GetStringOrKey("menu.debug"))) {
				_debug.IsOpen = true;
			}

			if (ImGui.MenuItem(_localization.GetStringOrKey("menu.settings"))) {
				Settings.IsOpen = true;
			}

			if (ImGui.MenuItem(_localization.GetStringOrKey("menu.tweaks"))) {
				Tweaks.IsOpen = true;
			}

			// A click outside the bar hides it, but only while no panel is up — with one open, that click
			// either lands on it or is DebugPanel's own outside-click close, and Tweaks and Settings close by
			// their own buttons.
			if (!PanelOpen && !ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
				Visible = false;
			}

			ImGui.EndMainMenuBar();
		}

		Tweaks.Draw(_centring.Condition);
		Settings.Draw(owner, _centring.Condition);
	}
}
