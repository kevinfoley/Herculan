using HercWorks.Core.Io.Transform;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Es2.Host.Settings;
using Herculan.Engine.Menus;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using Herculan.Es2.Settings;
using Silk.NET.Input;

namespace Herculan.Es2.Host.Shell;

/// <summary>
/// The shell's menus over its <see cref="FrontEndWindow"/>: HERCULAN's own <see cref="HostMenuBar"/> with its "Game" menu,
/// its Settings menu's restart, its Editor item and Custom Missions, the first start's <see cref="WelcomeDialog"/>, the automatic
/// update check and its <see cref="UpdateDialog"/>, and the <see cref="ErrorDialog"/>.
/// </summary>
sealed class ShellMenus : IFrontEndMenus {
	private readonly HostMenuBar _menuBar;
	private readonly TweaksMenu _tweaks;
	private readonly SettingsWindow _settings;
	private readonly ControllerWindow _controller;
	private readonly LocalizationTable _localization;

	/// <param name="dataDirectory">The folder the Controller window loads and saves the controller map in.</param>
	/// <param name="restartRequested">
	/// The Settings menu's restart, run as it is asked for; the window then closes once the frame is drawn.
	/// </param>
	/// <param name="editorRequested">
	/// The Editor item's switch to the mission editor, run as it is asked for; the window then closes once the frame
	/// is drawn.
	/// </param>
	/// <param name="customMissions">The menu bar's Custom Missions window, or null for a bar without it.</param>
	public ShellMenus(HostSession session, string dataDirectory, Action restartRequested, Action editorRequested,
			CustomMissionsWindow? customMissions = null) {
		_localization = session.Localization;
		_controller = new ControllerWindow(session.Localization, () => dataDirectory);
		_controller.Saved += file => PadPointerSettingsChanged?.Invoke(file.Mouse);
		_tweaks = new TweaksMenu(TweakSettings.Current, session.Localization);
		_settings = new SettingsWindow(session, () => RequestClose(restartRequested));
		var game = new GameMenu(session.Localization, RetailGame.Earthsiege2, other => RequestClose(() => GameSwitch.Request(other)));
		_menuBar = new HostMenuBar(session.Localization, session.Keys, _tweaks, _settings, _controller,
			openEditor: () => RequestClose(editorRequested), customMissions: customMissions, manualOpening: () => OnlineManual.Opening,
			game: game);
	}

	public event Action? CloseRequested;

	public event Action<GamepadMouseSettings>? PadPointerSettingsChanged;

	public bool Visible => _menuBar.Visible;

	public bool PanelOpen => _menuBar.PanelOpen;

	/// <summary>While the Controller window's Detect is listening (<see cref="ControllerWindow.Listening"/>).</summary>
	public bool PadNavigationSuspended => _controller.Listening;

	public void Attach(ScaledImGui imgui, IInputContext input) {
		_tweaks.Fonts = imgui;
		_controller.Attach(input);
	}

	public void Show() => _menuBar.Show();

	public bool BackOut() => _menuBar.BackOut();

	public void PadBack() => _menuBar.PadBack();

	/// <summary>
	/// Raises the first start's <see cref="WelcomeDialog"/>, unless it has been dismissed before, and starts the
	/// automatic update check (<see cref="UpdateChecks.StartAutomatic"/>).
	/// </summary>
	public void OfferWelcome() {
		_menuBar.Welcome.Offer();
		UpdateChecks.StartAutomatic();
	}

	/// <summary>
	/// The menu bar and its windows, and the update notice, once the automatic check has found a newer release and the
	/// welcome and any error notice are down. <paramref name="revealOnHover"/> is <see cref="HostMenuBar.ShowOnHover"/>.
	/// </summary>
	public void Draw(nint owner, bool fullScreen, bool revealOnHover) {
		if (!_menuBar.Welcome.IsOpen && !_menuBar.Error.IsOpen && UpdateChecks.TakeNews() is { } news) {
			_menuBar.Update.Offer(news);
		}

		if (revealOnHover) {
			_menuBar.ShowOnHover();
		}

		_menuBar.Draw(owner, fullScreen);
	}

	/// <summary>In the <see cref="ErrorDialog"/>.</summary>
	public void ShowError(string message, string detail) => _menuBar.Error.Show(message, detail);

	/// <summary>In the operating system's own box (<see cref="NativeAlert"/>).</summary>
	public void ShowErrorOutsideWindow(string message, string detail) =>
		NativeAlert.ShowError(_localization.GetStringOrKey("error.window_title"), $"{message}\n\n{detail}");

	public void Dispose() => _settings.Dispose();

	// Settings, the Editor item and "Switch to" ask from inside an ImGui window, and the close disposes the ImGui context, so the
	// close waits for the frame to finish (FrontEndWindow.CloseAfterFrame).
	private void RequestClose(Action requested) {
		requested();
		CloseRequested?.Invoke();
	}
}
