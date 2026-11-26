using System.Numerics;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Menus;

/// <summary>
/// What every game's floating "Settings" window, opened from the menu bar, shares: the window itself, the game's rows
/// (<see cref="DrawRows"/>), then Updates (<see cref="UpdateCheckRows"/>), an error line and a Close button, and a folder
/// prompt raised over it as a modal (<see cref="RaisePrompt"/>). Retail has no such screen in either game.
///
/// <para>The rows that change what a shell turn reads, the install and the disc, take effect by bringing the shell back
/// up; a mission has none to give, and there the game greys them (<see cref="InMission"/>, <see cref="InMissionTooltip"/>).</para>
/// </summary>
public abstract class SettingsWindowBase : IDrawnMenuBarPanel {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 520f;
	// The size of the start-up's own prompt.
	private static readonly Vector2 PromptSize = new(640f, 240f);

	private readonly string _id;
	private readonly string _promptId;
	private readonly UpdateCheckRows _updates;
	private PathPrompt? _prompt;
	private Action<string>? _apply;
	private object[] _promptArguments = [];
	private bool _promptRaised;
	private bool _open;

	/// <param name="id">The window's ImGui ID, and its prompt's stem.</param>
	/// <param name="inMission">Whether the window is over a mission (<see cref="InMission"/>).</param>
	protected SettingsWindowBase(LocalizationTable localization, string id, bool inMission) {
		Localization = localization;
		_id = id;
		_promptId = "##" + id + "_folder_prompt";
		InMission = inMission;
		InterfaceLanguage = new InterfaceLanguageCombo(localization);
		_updates = new UpdateCheckRows(localization);
	}

	protected LocalizationTable Localization { get; }

	/// <summary>The interface language combo, which a game draws among its rows.</summary>
	protected InterfaceLanguageCombo InterfaceLanguage { get; }

	/// <summary>Whether the window is over a mission, where the rows that bring the shell back up are greyed.</summary>
	protected bool InMission { get; }

	/// <summary>The error line under the rows, or null for none; raising a prompt or closing the window clears it.</summary>
	protected string? Error { get; set; }

	/// <summary>The prompt <see cref="RaisePrompt"/> raised, while it is up.</summary>
	public PathPrompt? Prompt => _prompt;

	/// <summary>Whether the window, or anything it raised, is up. Set by the menu bar.</summary>
	public virtual bool IsOpen {
		get => _open;
		set => _open = value;
	}

	void IMenuBarPanel.Open() => IsOpen = true;

	void IMenuBarPanel.BackOut() => Close();

	/// <summary>Takes the window down, and the prompt over it with nothing changed.</summary>
	public virtual void Close() => Hide();

	/// <summary>
	/// Draws the window, when it is up.
	/// </summary>
	/// <param name="owner">The native window a picker belongs to.</param>
	/// <param name="centre">When the window is centred (<see cref="PanelCentring.Condition"/>).</param>
	public virtual void Draw(nint owner, ImGuiCond centre) {
		if (!_open) {
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		if (ImGui.Begin(Localization.GetStringOrKey("settings.title") + "###" + _id, ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			DrawRows();

			ImGui.SeparatorText(Localization.GetStringOrKey("settings.updates"));
			_updates.Draw();

			if (Error != null) {
				ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.4f, 1f));
				ImGui.TextWrapped(Error);
				ImGui.PopStyleColor();
			}

			ImGui.Separator();
			if (ImGui.Button(Localization.GetStringOrKey("general.close"), new Vector2(ImGui.GetContentRegionAvail().X, 0f))) {
				stayOpen = false;
			}

			DrawPrompt(owner, centre);
		}

		ImGui.End();

		if (!stayOpen) {
			Hide();
		}
	}

	/// <summary>The game's rows, above Updates, drawn into the window.</summary>
	protected abstract void DrawRows();

	/// <summary>What the game forgets as the window goes down, to read again when it next opens.</summary>
	protected virtual void Forget() {
	}

	/// <summary>
	/// Raises a prompt over the window as a modal; accepting it passes the path chosen to the apply callback.
	/// </summary>
	/// <param name="prompt">The prompt.</param>
	/// <param name="apply">Receives the path chosen.</param>
	/// <param name="messageArguments">Fill the prompt's message.</param>
	protected void RaisePrompt(PathPrompt prompt, Action<string> apply, params object[] messageArguments) {
		_prompt = prompt;
		_apply = apply;
		_promptArguments = messageArguments;
		_promptRaised = false;
		Error = null;
	}

	/// <summary>In a mission, where the item just drawn is greyed, the tooltip that says where it can be changed.</summary>
	protected void InMissionTooltip() {
		if (InMission && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ScaledImGui.Tooltip(Localization.GetStringOrKey("settings.in_mission"));
		}
	}

	private void Hide() {
		_prompt = null;
		Error = null;
		_open = false;
		_updates.Forget();
		InterfaceLanguage.Forget();
		Forget();
	}

	// The prompt as a modal over the window. It is opened here rather than by the button that raised it so that its
	// popup ID is the window's.
	private void DrawPrompt(nint owner, ImGuiCond centre) {
		if (_prompt == null) {
			return;
		}

		if (!_promptRaised) {
			ImGui.OpenPopup(_promptId);
			_promptRaised = true;
		}

		ImGui.SetNextWindowSize(ScaledImGui.Scaled(PromptSize));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(_promptId, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove)) {
			return;
		}

		var outcome = _prompt.Draw(owner, _promptArguments);
		if (outcome != PathPrompt.Outcome.Open) {
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndPopup();

		if (outcome == PathPrompt.Outcome.Accepted && _prompt.Chosen is { } chosen) {
			_prompt = null;
			_apply?.Invoke(chosen);
		} else if (outcome == PathPrompt.Outcome.Dismissed) {
			_prompt = null;
		}
	}
}
