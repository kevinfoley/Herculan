using System.Numerics;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The floating "Tweaks" window opened from the menu bar.
///
/// <para>Edits land on the live <see cref="TweakSettings"/> immediately, so a checkbox shows
/// its effect in the viewport while the panel is still open. Cancel undoes them by re-reading the
/// settings file — safe because nothing outside this panel writes to <see cref="TweakSettings"/>
/// at runtime, so the in-memory values never diverge from disk except through edits made here. Save
/// writes the current values to disk and closes, Cancel reloads from disk (discarding anything not
/// yet saved) and closes, and so does closing the window by its title-bar button or [Esc]; see
/// <see cref="HostMenuBar.BackOut"/> for the latter.</para>
/// </summary>
public sealed class TweaksMenu {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 400f;

	// The visible settings in the order they are drawn, grouped once rather than every frame.
	private static readonly (TweakCategory Category, TweakSettingDefinition<bool>[] Settings)[] VisibleByCategory =
		TweakSettingDefinitions.All.Where(d => !d.Hidden).GroupBy(d => d.Category).Select(g => (g.Key, g.ToArray())).ToArray();

	private readonly TweakSettings _settings;
	private readonly LocalizationTable _localization;

	// Built once: passing the method groups in Draw would allocate the array and a delegate for each, every frame.
	private readonly (string key, Action callback)[] _presetButtons;
	private readonly (string key, Action callback)[] _closeButtons;

	/// <summary>Whether the panel is currently open. Set by the menu bar; see <see cref="HostMenuBar"/>.</summary>
	public bool IsOpen { get; set; }

	public TweaksMenu(TweakSettings settings, LocalizationTable localization) {
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_localization = localization ?? throw new ArgumentNullException(nameof(localization));
		_presetButtons = [("tweaks.none", DisableAll), ("tweaks.defaults", ApplyDefaults), ("tweaks.recommended", ApplyRecommended), ("tweaks.all", EnableAll)];
		_closeButtons = [("general.save", Save), ("general.cancel", Cancel)];
	}

	/// <summary>Draws the panel, if it is open. Call once per frame, inside the ImGui frame.</summary>
	public void Draw() {
		if (!IsOpen) {
			return;
		}

		// A zero component means "fit the content", so the panel keeps a fixed width and grows to
		// whatever height its settings need.
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		// The title doubles as the window's ID, so a language change opens it afresh, re-centred; a
		// "###tweaks" suffix would keep the ID fixed at the cost of a string built every frame.
		if (ImGui.Begin(_localization.GetStringOrKey("tweaks.title"), ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			foreach (var (category, settings) in VisibleByCategory) {
				ImGui.SeparatorText(_localization.GetStringOrKey(CategoryKey(category)));
				foreach (var definition in settings) {
					bool value = _settings.GetSettingValue(definition);
					string displayName = _localization.GetString(definition.DisplayNameKey) ?? definition.ID;
					if (ImGui.Checkbox(displayName, ref value)) {
						_settings.SetSettingValue(definition, value);
					}

					if (ImGui.IsItemHovered() && _localization.GetString(definition.DescriptionKey) is { } description) {
						ImGui.SetTooltip(description);
					}
				}
			}

			ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(10)));
			LocalizedImGuiHelpers.DrawButtonRow(_localization, _presetButtons);

			ImGui.Separator();

			LocalizedImGuiHelpers.DrawButtonRow(_localization, _closeButtons);
		}

		ImGui.End();

		if (!stayOpen && IsOpen) {
			Cancel();
		}
	}

	private static string CategoryKey(TweakCategory category) => category switch {
		TweakCategory.Cosmetic => "tweaks.category.cosmetic",
		TweakCategory.AI => "tweaks.category.ai",
		TweakCategory.Functional => "tweaks.category.functional",
		TweakCategory.Input => "tweaks.category.input",
		_ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
	};

	private void DisableAll() {
		ApplyToAll(false);
	}

	private void EnableAll() {
		ApplyToAll(true);
	}

	private void ApplyToAll(bool value) {
		foreach (var definition in TweakSettingDefinitions.All.Where(d => !d.Hidden)) {
			_settings.SetSettingValue(definition, value);
		}
	}

	private void ApplyDefaults() {
		foreach (var setting in TweakSettingDefinitions.All) {
			_settings.SetSettingValue(setting, setting.DefaultValue);
		}
	}

	private void ApplyRecommended() {
		foreach (var setting in TweakSettingDefinitions.All) {
			_settings.SetSettingValue(setting, setting.RecommendedValue);
		}
	}

	private void Save() {
		_settings.SaveToDisk();
		IsOpen = false;
	}

	/// <summary>Discards any edit not yet saved by reloading <see cref="TweakSettings"/> from disk,
	/// and closes the panel. Also reachable from outside — [Esc] backs out of an open Tweaks the same
	/// way the title-bar close and the in-panel Cancel button do; see <see cref="HostMenuBar.BackOut"/>.</summary>
	public void Cancel() {
		_settings.LoadFromDisk();
		IsOpen = false;
	}
}
