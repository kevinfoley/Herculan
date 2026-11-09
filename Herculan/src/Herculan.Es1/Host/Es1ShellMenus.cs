using Herculan.Engine.Localization;
using Herculan.Engine.Menus;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using Herculan.Es1.Settings;
using Silk.NET.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// Metaltech: Earthsiege's shell's menus over its <see cref="FrontEndWindow"/>: HERCULAN's own <see cref="HostMenuBar"/>
/// with the windows that work the same in either game, Accessibility and About, its own Tweaks, and the
/// <see cref="ErrorDialog"/>. Settings, Controller, Keys and Editor are greyed, since each is built on Earthsiege 2's
/// install, controls or missions; Custom Missions is left out. There is no welcome and no automatic update check.
/// </summary>
public sealed class Es1ShellMenus : IFrontEndMenus {
	// The accessibility settings ES1 honors: the larger font, which ScaledImGui reads for the bar's own windows. The damage
	// flash and shake settings are read by Earthsiege 2's cockpit alone.
	private static readonly A11ySettingDefinition<bool>[] Accessibility = [A11ySettingDefinitions.LargerFont];

	private readonly HostMenuBar _menuBar;
	private readonly TweaksMenu _tweaks;
	private readonly LocalizationTable _localization;

	/// <summary>The Tweaks window edits <see cref="TweakSettings.Current"/>, which the host has loaded with ES1's catalog.</summary>
	public Es1ShellMenus(LocalizationTable localization) {
		_localization = localization;
		_tweaks = new TweaksMenu(TweakSettings.Current, localization, offered: Es1TweakSettingDefinitions.Offered);
		_menuBar = new HostMenuBar(localization, keys: null, tweaks: _tweaks, settings: null, controller: null,
			accessibilitySettings: Accessibility);
	}

	// Neither is raised: no item ends the shell's turn, and there is no Controller window to save the pad's settings.
	public event Action? CloseRequested {
		add { }
		remove { }
	}

	public event Action<GamepadMouseSettings>? PadPointerSettingsChanged {
		add { }
		remove { }
	}

	public bool Visible => _menuBar.Visible;

	public bool PanelOpen => _menuBar.PanelOpen;

	public bool PadNavigationSuspended => false;

	public void Attach(ScaledImGui imgui, IInputContext input) {
		_tweaks.Fonts = imgui;
	}

	public void Show() => _menuBar.Show();

	public bool BackOut() => _menuBar.BackOut();

	public void PadBack() => _menuBar.PadBack();

	public void OfferWelcome() {
	}

	/// <summary>The menu bar and its windows. <paramref name="revealOnHover"/> is <see cref="HostMenuBar.ShowOnHover"/>.</summary>
	public void Draw(nint owner, bool fullScreen, bool revealOnHover) {
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

	public void Dispose() {
	}
}
