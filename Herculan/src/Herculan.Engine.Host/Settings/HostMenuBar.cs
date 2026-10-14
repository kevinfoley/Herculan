using Herculan.Engine.Cockpit;
using Herculan.Engine.Host.Debugging;
using Herculan.Engine.Host.Localization;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The menu bar [Shift+Esc] raises over the shell and over a mission, HERCULAN's own and not retail's, and the
/// panels it opens: Debug (a mission's only, and only under <c>--developer</c>), Settings, Controller, Tweaks and About; the
/// Editor item, which leaves the shell for the mission editor and is greyed over a mission; and the
/// first start's <see cref="WelcomeDialog"/>, which the shell raises without the bar. Hidden until <see cref="Show"/>, and never
/// drawn into a <c>--screenshot</c> capture. Plain [Esc] is retail's in a mission, where it pans to and from the
/// Heads-Down Display, so which key reaches the bar is each host's to decide: over a mission
/// <see cref="SimulatorFrame"/>, over the shell <c>KeyboardRouting</c>.
/// </summary>
sealed class HostMenuBar : IEscapeMenu {
	private readonly LocalizationTable _localization;
	private readonly DebugPanel? _debug;
	private readonly Action? _openEditor;
	private readonly PanelCentring _centring = new();

	/// <param name="openEditor">The Editor item's switch to the mission editor, or null to grey the item.</param>
	public HostMenuBar(LocalizationTable localization, TweaksMenu tweaks, SettingsWindow settings, ControllerWindow controller,
			DebugPanel? debug = null, Action? openEditor = null) {
		_localization = localization;
		_openEditor = openEditor;
		Tweaks = tweaks;
		Settings = settings;
		Controller = controller;
		_debug = debug;
		About = new AboutWindow(localization);
		Welcome = new WelcomeDialog(localization, () => Tweaks.IsOpen = true);
	}

	public TweaksMenu Tweaks { get; }

	public SettingsWindow Settings { get; }

	public ControllerWindow Controller { get; }

	public AboutWindow About { get; }

	public WelcomeDialog Welcome { get; }

	/// <summary>Whether the bar is up.</summary>
	public bool Visible { get; private set; }

	/// <summary>Whether one of the bar's panels, or the welcome, is up.</summary>
	public bool PanelOpen => Tweaks.IsOpen || Settings.IsOpen || Controller.IsOpen || About.IsOpen || Welcome.IsOpen || _debug?.IsOpen == true;

	/// <summary>Whether the bar or one of its panels is up.</summary>
	public bool Up => Visible || PanelOpen;

	/// <summary>Raises the bar.</summary>
	public void Show() => Visible = true;

	/// <summary>
	/// [Esc]'s step back: takes down whichever panel is up, else hides the bar. Returns false when neither
	/// was up, leaving the key to the host. Tweaks goes through its Cancel, so an edit made but not saved is
	/// discarded rather than left applied but unpersisted. The welcome goes as its OK does.
	/// </summary>
	public bool BackOut() {
		if (PanelOpen) {
			if (_debug != null) {
				_debug.IsOpen = false;
			}
			if (Tweaks.IsOpen) {
				Tweaks.Cancel();
			}
			if (Controller.IsOpen) {
				Controller.Cancel();
			}
			if (Welcome.IsOpen) {
				Welcome.Dismiss();
			}
			Settings.Close();
			About.IsOpen = false;
			return true;
		}

		if (Visible) {
			Visible = false;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Draws the bar, when it is up, and its panels, when they are; a host with a Debug panel draws
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

			if (ImGui.MenuItem(_localization.GetStringOrKey("menu.controller"))) {
				Controller.Open();
			}

			if (ImGui.MenuItem(_localization.GetStringOrKey("menu.tweaks"))) {
				Tweaks.IsOpen = true;
			}

			if (ImGui.MenuItem(_localization.GetStringOrKey("menu.editor"), null, false, _openEditor != null)) {
				_openEditor!();
			}

			if (ImGui.MenuItem(_localization.GetStringOrKey("menu.about"))) {
				About.IsOpen = true;
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
		Controller.Draw(_centring.Condition);
		About.Draw(_centring.Condition);
		Welcome.Draw(_centring.Condition);
	}
}
