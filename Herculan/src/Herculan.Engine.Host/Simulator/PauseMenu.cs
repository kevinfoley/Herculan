using System.Numerics;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// The menu a gamepad's Start raises over a mission (docs/herculan/herculan-key-bindings.md, "The pause menu"). This
/// engine's own. It holds the mission through <see cref="SimulatorFrame.Pause"/>, and its last three items raise
/// retail's panels as [F11], [F12] and [Q] do, greyed where those keys would raise nothing.
///
/// <para>The pad works it directly rather than through ImGui's navigation, under which the highlight stays hidden, and
/// the first press of A does nothing, until the d-pad has moved it. It works it while the menu has ImGui's focus, or
/// nothing has: not while the menu bar or one of its windows has it, and B there hands it back.</para>
/// </summary>
sealed class PauseMenu {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 260f;

	// The scene behind the menu is dimmed by this, as ABGR.
	private const uint DimColour = 0x80000000;

	private static readonly string[] ItemKeys = ["pause.resume", "pause.objectives", "pause.preferences", "pause.quit"];
	private const int Resume = 0;
	private const int Objectives = 1;
	private const int Preferences = 2;
	private const int Quit = 3;

	private readonly LocalizationTable _localization;
	private readonly SimulatorFrame _frame;
	private readonly ModalPanels _panels;
	private readonly MissionOutcome _outcome;
	private readonly CockpitInput _cockpit;

	private int _selected;

	// Whether the pad worked the menu last frame. A press that brought the focus here was taken where the focus was.
	private bool _hadPad;

	public PauseMenu(LocalizationTable localization, SimulatorFrame frame, ModalPanels panels, MissionOutcome outcome,
			CockpitInput cockpit) {
		_localization = localization;
		_frame = frame;
		_panels = panels;
		_outcome = outcome;
		_cockpit = cockpit;
	}

	/// <summary>Whether the menu is up, including while a retail panel it raised is over it.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>Whether the menu is on screen: up, and no retail panel over it.</summary>
	public bool Showing => IsOpen && !_panels.AnyOpen;

	/// <summary>
	/// Start: raises the menu, unless the mission is over or a retail panel already holds the input, or with the menu
	/// showing resumes. While a panel the menu raised is up, Start leaves it to be closed its own way.
	/// </summary>
	public void Start() {
		if (!IsOpen) {
			if (!_panels.AnyOpen && !_outcome.Over) {
				IsOpen = true;
				_selected = Resume;
				_frame.Pause();
			}
		} else if (Showing) {
			Close();
		}
	}

	/// <summary>Draws the menu while it is <see cref="Showing"/>, and takes the pad's choice. Call once per frame inside the ImGui frame.</summary>
	public void Draw() {
		// The frame the menu comes back from a retail panel is the frame that panel took its closing press, which the
		// menu must not take too: hidden, it did not have the pad last frame.
		if (!Showing) {
			_hadPad = false;
			return;
		}

		var viewport = ImGui.GetMainViewport();
		ImGui.GetBackgroundDrawList().AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size, DimColour);

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
		int? chosen = null;
		bool resume = false;
		if (ImGui.Begin(Text("pause.title") + "###pause", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize
				| ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNavInputs)) {
			bool pad = ImGui.IsWindowFocused() || !ImGui.IsWindowFocused(ImGuiFocusedFlags.AnyWindow);
			if (pad && _hadPad) {
				if (Pressed(ImGuiKey.GamepadDpadUp) || Pressed(ImGuiKey.GamepadLStickUp)) {
					Step(-1);
				}

				if (Pressed(ImGuiKey.GamepadDpadDown) || Pressed(ImGuiKey.GamepadLStickDown)) {
					Step(1);
				}

				if (ImGui.IsKeyPressed(ImGuiKey.GamepadFaceDown, false)) {
					chosen = _selected;
				}

				resume = ImGui.IsKeyPressed(ImGuiKey.GamepadFaceRight, false);
			}

			_hadPad = pad;
			ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
			for (int i = 0; i < ItemKeys.Length; i++) {
				bool enabled = Enabled(i);
				if (ImGui.Selectable(Text(ItemKeys[i]), i == _selected,
						enabled ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled)) {
					chosen = i;
				}

				if (enabled && ImGui.IsItemHovered() && ImGui.GetIO().MouseDelta != Vector2.Zero) {
					_selected = i;
				}
			}

			ImGui.PopStyleVar();
		}

		ImGui.End();

		if (resume) {
			Close();
		} else if (chosen is { } item && Enabled(item)) {
			Choose(item);
		}
	}

	private void Choose(int item) {
		switch (item) {
			case Resume:
				Close();
				break;
			case Objectives:
				_panels.Objectives?.Open();
				break;
			case Preferences:
				_panels.Preferences?.Open();
				break;
			case Quit:
				_panels.RaiseStatusAlertForQuit();
				break;
		}
	}

	// Mouse events go on being queued for the cockpit while the frame is held, so a click on the menu would land on the
	// console behind it once the mission runs on; they are dropped as it resumes.
	private void Close() {
		IsOpen = false;
		_frame.Unpause();
		_cockpit.Reset();
	}

	private bool Enabled(int item) => item switch {
		Objectives => _panels.CanRaiseObjectives,
		Preferences => _panels.CanRaisePreferences,
		Quit => _panels.CanRaiseStatusAlert(_outcome.Over),
		_ => true,
	};

	// Moves the highlight past any greyed item, wrapping. Resume is never greyed, so the walk always ends.
	private void Step(int direction) {
		do {
			_selected = (_selected + direction + ItemKeys.Length) % ItemKeys.Length;
		} while (!Enabled(_selected));
	}

	private static bool Pressed(ImGuiKey key) => ImGui.IsKeyPressed(key, true);

	private string Text(string key) => _localization.GetStringOrKey(key);
}
