using System.Numerics;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Host.Debugging;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Host.Shell;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The menu bar [Shift+Esc] raises over the shell and over a mission, and the pointer at the window's top left over the
/// shell (<see cref="ShowOnHover"/>), HERCULAN's own and not retail's, and the
/// panels it opens: Debug and Mission Scripting (a mission's only, and only under <c>--developer</c>), Settings, Controller, Keys, Tweaks, Accessibility and About; the
/// Editor item, which leaves the shell for the mission editor and is greyed over a mission; Custom Missions
/// (<see cref="CustomMissionsWindow"/>), the shell's alone and absent over a mission; and the
/// first start's <see cref="WelcomeDialog"/>, the <see cref="UpdateDialog"/> and the <see cref="ErrorDialog"/>, which the shell raises without the bar; and a
/// notice while the on-line manual opens (<see cref="OnlineManual.Opening"/>). Hidden until <see cref="Show"/>, and never
/// drawn into a <c>--screenshot</c> capture. Plain [Esc] is retail's in a mission, where it pans to and from the
/// Heads-Down Display, so which key reaches the bar is each host's to decide: over a mission
/// <see cref="SimulatorFrame"/>, over the shell <c>KeyboardRouting</c>.
/// </summary>
sealed class HostMenuBar : IEscapeMenu {
	// The height of the zone ShowOnHover watches, in pixels on a 100% display (ScaledImGui.Scaled). Its width is the
	// bar's items' (ItemsWidth).
	private const float HoverHeight = 4f;

	private readonly LocalizationTable _localization;
	private readonly DebugPanel? _debug;
	private readonly MissionScriptingWindow? _scripting;
	private readonly Action? _openEditor;
	private readonly CustomMissionsWindow? _customMissions;
	private readonly PanelCentring _centring = new();

	// Set when the next frame's bar is to take the focus with the highlight on its first item, and what the previous
	// frame left of the panels and the highlight, to see a panel closed from the pad.
	private bool _focusItems;
	private bool _panelWasOpen;
	private bool _highlightWasShowing;

	/// <param name="keys">The key map the Keys window lists.</param>
	/// <param name="openEditor">The Editor item's switch to the mission editor, or null to grey the item.</param>
	/// <param name="scripting">The Mission Scripting window, which the host draws as it does the Debug panel, or null for no item.</param>
	/// <param name="customMissions">The Custom Missions item's window, or null for a bar without the item.</param>
	public HostMenuBar(LocalizationTable localization, KeyMap keys, TweaksMenu tweaks, SettingsWindow settings,
			ControllerWindow controller, DebugPanel? debug = null, Action? openEditor = null, MissionScriptingWindow? scripting = null,
			CustomMissionsWindow? customMissions = null) {
		_localization = localization;
		_openEditor = openEditor;
		_customMissions = customMissions;
		Tweaks = tweaks;
		Settings = settings;
		Controller = controller;
		Keys = new KeysWindow(localization, keys);
		Accessibility = new AccessibilityMenu(A11ySettings.Current, localization);
		_debug = debug;
		_scripting = scripting;
		About = new AboutWindow(localization);
		Welcome = new WelcomeDialog(localization, () => Tweaks.IsOpen = true);
		Update = new UpdateDialog(localization);
		Error = new ErrorDialog(localization);
	}

	public TweaksMenu Tweaks { get; }

	public SettingsWindow Settings { get; }

	public ControllerWindow Controller { get; }

	public KeysWindow Keys { get; }

	public AccessibilityMenu Accessibility { get; }

	public AboutWindow About { get; }

	public WelcomeDialog Welcome { get; }

	public UpdateDialog Update { get; }

	public ErrorDialog Error { get; }

	/// <summary>Whether the bar is up.</summary>
	public bool Visible { get; private set; }

	/// <summary>Whether one of the bar's panels, or the welcome, the update notice or the error notice, is up.</summary>
	public bool PanelOpen => Tweaks.IsOpen || Settings.IsOpen || Controller.IsOpen || Keys.IsOpen || Accessibility.IsOpen || About.IsOpen
		|| Welcome.IsOpen || Update.IsOpen || Error.IsOpen || _debug?.IsOpen == true || _scripting?.IsOpen == true
		|| _customMissions?.IsOpen == true;

	/// <summary>Whether the bar or one of its panels is up.</summary>
	public bool Up => Visible || PanelOpen;

	/// <summary>Raises the bar.</summary>
	public void Show() => Visible = true;

	/// <summary>
	/// Raises the bar while the pointer is within <see cref="HoverHeight"/> of the window's top edge and over the width
	/// the bar's items come up in (<see cref="ItemsWidth"/>). A host that wants the zone calls this
	/// each frame inside the ImGui frame, before <see cref="Draw"/>; when to skip it is the host's to decide. The bar
	/// hides again as it does however it was raised, by a click outside it.
	/// </summary>
	public void ShowOnHover() {
		// ImGui's pointer is in its own frame's units, which ScaledImGui.Scaled converts to. One it has lost reads
		// -FLT_MAX, so the zone's lower bounds keep that out.
		var pointer = ImGui.GetIO().MousePos;
		if (pointer.X >= 0f && pointer.X < ItemsWidth()
				&& pointer.Y >= 0f && pointer.Y < ScaledImGui.Scaled(HoverHeight)) {
			Visible = true;
		}
	}

	/// <summary>
	/// How far from the window's left edge the bar's items reach, in ImGui's units, worked out from their labels since the
	/// bar is not drawn while it is hidden: ImGui lays a menu bar item out as its label's width plus two item spacings,
	/// after the bar's own frame padding. Follows the language and the display scale, which the labels' widths carry.
	/// </summary>
	private float ItemsWidth() {
		var style = ImGui.GetStyle();
		float width = style.FramePadding.X;
		foreach (var item in Items()) {
			width += ImGui.CalcTextSize(_localization.GetStringOrKey(item.Key)).X + style.ItemSpacing.X * 2f;
		}

		return width;
	}

	/// <summary>
	/// The bar's items in order, each its label's localization key, whether it can be chosen, and what choosing it does: the
	/// one list <see cref="Draw"/> draws and <see cref="ItemsWidth"/> measures. A bare item per panel, no checkmark, since each
	/// panel closes itself.
	/// </summary>
	private IEnumerable<(string Key, bool Enabled, Action Choose)> Items() {
		if (_debug != null) {
			yield return ("menu.debug", true, () => _debug.IsOpen = true);
		}

		if (_scripting != null) {
			yield return ("menu.mission_scripting", true, () => _scripting.IsOpen = true);
		}

		yield return ("menu.settings", true, () => Settings.IsOpen = true);
		yield return ("menu.controller", true, Controller.Open);
		yield return ("menu.keys", true, () => Keys.IsOpen = true);
		yield return ("menu.tweaks", true, () => Tweaks.IsOpen = true);
		yield return ("menu.accessibility", true, () => Accessibility.IsOpen = true);
		yield return ("menu.editor", _openEditor != null, () => _openEditor!());
		if (_customMissions != null) {
			yield return ("menu.custom_missions", true, _customMissions.Open);
		}

		yield return ("menu.about", true, () => About.IsOpen = true);
	}

	/// <summary>
	/// A gamepad's Back: <see cref="BackOut"/>, or <see cref="Show"/> when there was nothing to back out of; and while the
	/// bar is still up, the bar takes the focus with the highlight on its first item, so the pad can go on from there.
	/// </summary>
	public void PadBack() {
		if (!BackOut()) {
			Show();
		}

		_focusItems = Visible;
	}

	/// <summary>
	/// [Esc]'s step back: takes down whichever panel is up, else hides the bar. Returns false when neither
	/// was up, leaving the key to the host. Tweaks and Accessibility go through their Cancel, so an edit made but not saved is
	/// discarded rather than left applied but unpersisted. The welcome and the error notice go as their OK does, the update notice
	/// as its Close does. Custom Missions takes down the prompt it has up, if any, else closes.
	/// </summary>
	public bool BackOut() {
		if (PanelOpen) {
			if (_debug != null) {
				_debug.IsOpen = false;
			}
			if (_scripting != null) {
				_scripting.IsOpen = false;
			}
			if (Tweaks.IsOpen) {
				Tweaks.Cancel();
			}
			if (Controller.IsOpen) {
				Controller.Cancel();
			}
			if (Accessibility.IsOpen) {
				Accessibility.Cancel();
			}
			if (Welcome.IsOpen) {
				Welcome.Dismiss();
			}
			Update.Dismiss();
			Error.Dismiss();
			Settings.Close();
			_customMissions?.BackOut();
			Keys.IsOpen = false;
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
	/// Draws the bar, when it is up, and its panels, when they are; a host with a Debug panel or a Mission Scripting window
	/// draws those itself, since they need the host's state. <paramref name="owner"/> is the native window a folder
	/// picker belongs to, and <paramref name="fullScreen"/> whether that window is full screen, which
	/// <see cref="PanelCentring"/> watches. Call once per frame inside the ImGui frame.
	/// </summary>
	public void Draw(nint owner, bool fullScreen) {
		_centring.Update(fullScreen);

		// A panel the pad closed with its own button leaves the bar up with nothing focused, so the bar takes the
		// focus as it does after Back. The highlight showing is what says the pad, or the keyboard, was in use.
		bool panelOpen = PanelOpen;
		if (_panelWasOpen && !panelOpen && Visible && _highlightWasShowing) {
			_focusItems = true;
		}

		_panelWasOpen = panelOpen;

		if (Visible && ImGui.BeginMainMenuBar()) {
			if (_focusItems) {
				_focusItems = false;
				ImGuiNavigation.FocusMenuBar();
			}

			foreach (var (key, enabled, choose) in Items()) {
				if (ImGui.MenuItem(_localization.GetStringOrKey(key), null, false, enabled)) {
					choose();
				}
			}

			// A click outside the bar hides it, but only while no panel is up — with one open, that click
			// either lands on it or is DebugPanel's own outside-click close, and Tweaks and Settings close by
			// their own buttons.
			if (!PanelOpen && !ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
				Visible = false;
			}

			// So does a gamepad's B while no panel is up. ImGui's own B leaves the bar for the window under it, which is
			// none then, so it would only take the highlight off a bar still standing over the game.
			if (!PanelOpen && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceRight, false)) {
				Visible = false;
			}

			ImGui.EndMainMenuBar();
		}

		Tweaks.Draw(_centring.Condition);
		Settings.Draw(owner, _centring.Condition);
		Controller.Draw(_centring.Condition);
		Keys.Draw(_centring.Condition);
		Accessibility.Draw(_centring.Condition);
		About.Draw(_centring.Condition);
		_customMissions?.Draw(owner, _centring.Condition);
		Welcome.Draw(_centring.Condition);
		Update.Draw(_centring.Condition);
		// One modal at a time: an error raised under the welcome or the update notice waits for it to go.
		if (!Welcome.IsOpen && !Update.IsOpen) {
			Error.Draw(_centring.Condition);
		}

		DrawManualNotice();
		_highlightWasShowing = ImGui.GetIO().NavVisible;
	}

	// While the on-line manual is being opened (OnlineManual.Opening), a notice at the window's centre with no buttons, which
	// takes no input and goes by itself.
	private void DrawManualNotice() {
		if (!OnlineManual.Opening) {
			return;
		}

		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
		if (ImGui.Begin("###manual_loading", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs
				| ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav)) {
			ImGui.TextUnformatted(_localization.GetStringOrKey("manual.loading"));
		}

		ImGui.End();
	}
}
