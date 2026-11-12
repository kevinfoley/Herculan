using System.Numerics;
using Herculan.Engine.Input;
using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using ImGuiNET;

namespace Herculan.Engine.Menus;

/// <summary>
/// The menu bar over the shell and over a mission, and the panels and dialogs it owns. It is a HERCULAN addition, not a
/// retail feature. It is hidden until <see cref="Show"/>, and is never drawn into a <c>--screenshot</c> capture.
///
/// <para>[Shift+Esc] raises the bar over the shell and over a mission. Over the shell, so does the pointer at the
/// window's top left (<see cref="ShowOnHover"/>). Plain [Esc] belongs to retail in a mission, where it pans to and from
/// the Heads-Down Display, so each host decides which key reaches the bar. In an ES2 mission that is the cockpit's
/// keyboard handling (<c>SimulatorFrame</c>), and over ES2's shell it is the shell's key router
/// (<c>KeyboardRouting</c>).</para>
///
/// <para>The bar's items, in order (<see cref="Items"/>):</para>
/// <list type="bullet">
/// <item>"Game", a menu over the shell only (<see cref="GameMenu"/>).</item>
/// <item>Debug and Mission Scripting, over a mission only and only under <c>--developer</c>.</item>
/// <item>Settings, Controller, Keys and Tweaks. A host greys any of these by passing none. ES1's shell greys Keys, which
/// has no ES1 counterpart yet.</item>
/// <item>Accessibility.</item>
/// <item>Editor, which leaves the shell for the mission editor, and is greyed over a mission.</item>
/// <item>Custom Missions, over the shell only.</item>
/// <item>About.</item>
/// </list>
///
/// <para>The game's own panels (Debug, Mission Scripting, Settings, Controller, Custom Missions) come in as
/// <see cref="IMenuBarPanel"/>s. The bar also owns the first start's <see cref="WelcomeDialog"/>, the
/// <see cref="UpdateDialog"/> and the <see cref="ErrorDialog"/>, which the shell raises without the bar, and a notice
/// shown while the on-line manual opens.</para>
/// </summary>
public sealed class HostMenuBar : IEscapeMenu {
	// The height of the zone ShowOnHover watches, in pixels on a 100% display (ScaledImGui.Scaled). Its width is the
	// bar's items' (ItemsWidth).
	private const float HoverHeight = 4f;

	private readonly LocalizationTable _localization;
	private readonly IMenuBarPanel? _debug;
	private readonly IMenuBarPanel? _scripting;
	private readonly Action? _openEditor;
	private readonly IDrawnMenuBarPanel? _customMissions;
	private readonly Func<bool>? _manualOpening;
	private readonly GameMenu? _game;
	private readonly PanelCentring _centring = new();

	// Set when the next frame's bar is to take the focus with the highlight on its first item, and what the previous
	// frame left of the panels and the highlight, to see a panel closed from the pad.
	private bool _focusItems;
	private bool _panelWasOpen;
	private bool _highlightWasShowing;

	/// <param name="keys">The key map the Keys window lists, or null to grey the item.</param>
	/// <param name="tweaks">The Tweaks window, or null to grey the item.</param>
	/// <param name="settings">The Settings window, or null to grey the item.</param>
	/// <param name="controller">The Controller window, or null to grey the item.</param>
	/// <param name="openEditor">The Editor item's switch to the mission editor, or null to grey the item.</param>
	/// <param name="debug">The Debug panel, which the host draws, or null for no item.</param>
	/// <param name="scripting">The Mission Scripting window, which the host draws as it does the Debug panel, or null for no item.</param>
	/// <param name="customMissions">The Custom Missions item's window, or null for a bar without the item.</param>
	/// <param name="accessibilitySettings">The settings the Accessibility window offers (<see cref="AccessibilityMenu"/>), or null for all.</param>
	/// <param name="manualOpening">Whether the on-line manual is being opened, which the bar shows a notice for, or null for a bar without the notice.</param>
	/// <param name="game">The "Game" menu, or null for a bar without it.</param>
	public HostMenuBar(LocalizationTable localization, KeyMap? keys, TweaksMenu? tweaks, IDrawnMenuBarPanel? settings,
			IDrawnMenuBarPanel? controller, IMenuBarPanel? debug = null, Action? openEditor = null, IMenuBarPanel? scripting = null,
			IDrawnMenuBarPanel? customMissions = null, IReadOnlyCollection<A11ySettingDefinition<bool>>? accessibilitySettings = null,
			Func<bool>? manualOpening = null, GameMenu? game = null) {
		_localization = localization;
		_openEditor = openEditor;
		_customMissions = customMissions;
		_manualOpening = manualOpening;
		_game = game;
		Tweaks = tweaks;
		Settings = settings;
		Controller = controller;
		Keys = keys != null ? new KeysWindow(localization, keys) : null;
		Accessibility = new AccessibilityMenu(A11ySettings.Current, localization, accessibilitySettings);
		_debug = debug;
		_scripting = scripting;
		About = new AboutWindow(localization);
		Welcome = new WelcomeDialog(localization, () => {
			if (Tweaks != null) {
				Tweaks.IsOpen = true;
			}
		});
		Update = new UpdateDialog(localization);
		Error = new ErrorDialog(localization);
	}

	public TweaksMenu? Tweaks { get; }

	public IDrawnMenuBarPanel? Settings { get; }

	public IDrawnMenuBarPanel? Controller { get; }

	public KeysWindow? Keys { get; }

	public AccessibilityMenu Accessibility { get; }

	public AboutWindow About { get; }

	public WelcomeDialog Welcome { get; }

	public UpdateDialog Update { get; }

	public ErrorDialog Error { get; }

	/// <summary>Whether the bar is up.</summary>
	public bool Visible { get; private set; }

	/// <summary>Whether one of the bar's panels, or the welcome, the update notice or the error notice, is up.</summary>
	public bool PanelOpen => Tweaks?.IsOpen == true || Settings?.IsOpen == true || Controller?.IsOpen == true || Keys?.IsOpen == true
		|| Accessibility.IsOpen || About.IsOpen
		|| Welcome.IsOpen || Update.IsOpen || Error.IsOpen || _debug?.IsOpen == true || _scripting?.IsOpen == true
		|| _customMissions?.IsOpen == true;

	/// <summary>Whether the bar or one of its panels is up.</summary>
	public bool Up => Visible || PanelOpen;

	/// <summary>Raises the bar.</summary>
	public void Show() => Visible = true;

	/// <summary>
	/// Raises the bar while the pointer is within <see cref="HoverHeight"/> of the window's top edge and over the width
	/// the bar's items come up in (<see cref="ItemsWidth"/>). A host that wants the zone calls this each frame inside the
	/// ImGui frame, before <see cref="Draw"/>, and may skip the call when it wants. However the bar was raised, a click
	/// outside it hides the bar.
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
	/// The bar's items in order: the one list that <see cref="Draw"/> draws and <see cref="ItemsWidth"/> measures. ImGui
	/// lays out a menu in the bar the same way as an item, so both can share the list.
	/// <list type="bullet">
	/// <item><c>Key</c>: the label's localization key.</item>
	/// <item><c>Enabled</c>: whether the item can be chosen; a greyed item cannot.</item>
	/// <item><c>Choose</c>: what choosing the item does.</item>
	/// <item><c>MenuItems</c>: for a menu, draws its items while it is open; null for a plain item.</item>
	/// </list>
	/// Each panel's item is a plain item with no checkmark, since the panel has its own way to close.
	/// </summary>
	private IEnumerable<(string Key, bool Enabled, Action Choose, Action? MenuItems)> Items() {
		if (_game != null) {
			yield return ("menu.game", true, () => { }, _game.DrawItems);
		}

		if (_debug != null) {
			yield return ("menu.debug", true, _debug.Open, null);
		}

		if (_scripting != null) {
			yield return ("menu.mission_scripting", true, _scripting.Open, null);
		}

		yield return ("menu.settings", Settings != null, () => Settings!.Open(), null);
		yield return ("menu.controller", Controller != null, () => Controller!.Open(), null);
		yield return ("menu.keys", Keys != null, () => Keys!.IsOpen = true, null);
		yield return ("menu.tweaks", Tweaks != null, () => Tweaks!.IsOpen = true, null);
		yield return ("menu.accessibility", true, () => Accessibility.IsOpen = true, null);
		yield return ("menu.editor", _openEditor != null, () => _openEditor!(), null);
		if (_customMissions != null) {
			yield return ("menu.custom_missions", true, _customMissions.Open, null);
		}

		yield return ("menu.about", true, () => About.IsOpen = true, null);
	}

	/// <summary>
	/// Handles the menu bar input. It calls <see cref="BackOut"/>, or <see cref="Show"/> when there was nothing to back out
	/// of. If the bar is still up afterwards, the bar takes the focus with the highlight on its first item, so the pad can
	/// go on from there.
	/// <para>While the Controller window is listening for a control to bind (<see cref="ControllerWindow.Listening"/>), the
	/// press does none of that. Listening stops, and the window says that <paramref name="input"/> is reserved for the menu bar and cannot be bound
	/// to anything else (<see cref="ControllerWindow.RefuseMenuBarInput"/>).</para>
	/// </summary>
	/// <param name="input">The control pressed: a Custom profile's menu bar input, or by default a gamepad's Back.</param>
	public void RaiseOrBackOut(ControllerInput? input = null) {
		if (Controller is ControllerWindow { Listening: true } controller) {
			controller.RefuseMenuBarInput(input ?? ControllerInput.Button(ControllerDevice.GamepadBack));
			return;
		}

		if (!BackOut()) {
			Show();
		}

		_focusItems = Visible;
	}

	/// <summary>
	/// [Esc]'s step back: closes every open panel, or, when none is open, hides the bar. Returns false when neither was
	/// up, leaving the key to the host. Tweaks and Accessibility close through their Cancel, so an edit made but not saved
	/// is discarded rather than left applied but unsaved. The welcome and the error notice are dismissed the way their OK
	/// button does, and the update notice the way its Close button does. The game's panels close through their own
	/// <see cref="IMenuBarPanel.BackOut"/>.
	/// </summary>
	public bool BackOut() {
		if (PanelOpen) {
			_debug?.BackOut();
			_scripting?.BackOut();
			if (Tweaks?.IsOpen == true) {
				Tweaks.Cancel();
			}
			Controller?.BackOut();
			if (Accessibility.IsOpen) {
				Accessibility.Cancel();
			}
			if (Welcome.IsOpen) {
				Welcome.Dismiss();
			}
			Update.Dismiss();
			Error.Dismiss();
			Settings?.BackOut();
			_customMissions?.BackOut();
			if (Keys != null) {
				Keys.IsOpen = false;
			}
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
	/// Draws the bar, when it is up, and its open panels, except the Debug panel and the Mission Scripting window, which
	/// the host draws itself since they need the host's state. <paramref name="owner"/> is the native window a folder
	/// picker belongs to. <paramref name="fullScreen"/> is whether that window is full screen, and
	/// <see cref="PanelCentring"/> watches it. Call once per frame inside the ImGui frame.
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

			bool menuOpen = false;
			foreach (var (key, enabled, choose, menuItems) in Items()) {
				string label = _localization.GetStringOrKey(key);
				if (menuItems != null) {
					if (ImGui.BeginMenu(label, enabled)) {
						menuOpen = true;
						menuItems();
						ImGui.EndMenu();
					}
				} else if (ImGui.MenuItem(label, null, false, enabled)) {
					choose();
				}
			}

			// A click outside the bar hides it, but only while no panel is up — with one open, that click
			// either lands on it or is DebugPanel's own outside-click close, and Tweaks and Settings close by
			// their own buttons — and no menu is open, whose items lie outside the bar and which ImGui closes itself.
			if (!PanelOpen && !menuOpen && !ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
				Visible = false;
			}

			// So does a gamepad's B while no panel or menu is up. ImGui's own B leaves the bar for the window under it, which
			// is none then, so it would only take the highlight off a bar still standing over the game; with a menu open,
			// its B closes the menu.
			if (!PanelOpen && !menuOpen && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceRight, false)) {
				Visible = false;
			}

			ImGui.EndMainMenuBar();
		}

		Tweaks?.Draw(_centring.Condition);
		Settings?.Draw(owner, _centring.Condition);
		Controller?.Draw(owner, _centring.Condition);
		Keys?.Draw(_centring.Condition);
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

	// While the on-line manual is being opened (the constructor's manualOpening), a notice at the window's centre with no
	// buttons, which takes no input and goes by itself.
	private void DrawManualNotice() {
		if (_manualOpening?.Invoke() != true) {
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
