using System.Numerics;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The floating "Accessibility" window opened from the menu bar. It works as <see cref="TweaksMenu"/> does: edits land
/// on the live <see cref="A11ySettings"/> at once, Save writes them and closes, and Cancel, the title-bar close and
/// [Esc] (<see cref="HostMenuBar.BackOut"/>) reload the file and close. A category with no visible setting is left out.
/// </summary>
public sealed class AccessibilityMenu {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 400f;

	// The visible settings in the order they are drawn, grouped once rather than every frame.
	private static readonly (A11yCategory Category, A11ySettingDefinition<bool>[] Settings)[] VisibleByCategory =
		A11ySettingDefinitions.All.Where(d => !d.Hidden).GroupBy(d => d.Category).OrderBy(g => g.Key).Select(g => (g.Key, g.ToArray())).ToArray();

	private readonly A11ySettings _settings;
	private readonly LocalizationTable _localization;

	// Built once: passing the method groups in Draw would allocate the array and a delegate for each, every frame.
	private readonly (string key, Action callback)[] _closeButtons;

	/// <summary>Whether the panel is currently open. Set by the menu bar; see <see cref="HostMenuBar"/>.</summary>
	public bool IsOpen { get; set; }

	public AccessibilityMenu(A11ySettings settings, LocalizationTable localization) {
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_localization = localization ?? throw new ArgumentNullException(nameof(localization));
		_closeButtons = [("general.save", Save), ("general.cancel", Cancel)];
	}

	/// <summary>
	/// Draws the panel, if it is open. Call once per frame, inside the ImGui frame. <paramref name="centre"/> is when
	/// it is centred on the window (<see cref="PanelCentring.Condition"/>).
	/// </summary>
	public void Draw(ImGuiCond centre) {
		if (!IsOpen) {
			return;
		}

		// A zero component means "fit the content", so the panel keeps a fixed width and grows to
		// whatever height its settings need.
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		if (ImGui.Begin(_localization.GetStringOrKey("a11y.title"), ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			foreach (var (category, settings) in VisibleByCategory) {
				ImGui.SeparatorText(_localization.GetStringOrKey(CategoryKey(category)));
				foreach (var definition in settings) {
					bool value = _settings.GetSettingValue(definition);
					string displayName = _localization.GetString(definition.DisplayNameKey) ?? definition.ID;
					if (ImGui.Checkbox(displayName, ref value)) {
						_settings.SetSettingValue(definition, value);
					}

					if (ImGui.IsItemHovered() && _localization.GetString(definition.DescriptionKey) is { } description) {
						ScaledImGui.Tooltip(description);
					}
				}
			}

			ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(10)));
			ImGui.Separator();

			LocalizedImGuiHelpers.DrawButtonRow(_localization, _closeButtons);
		}

		ImGui.End();

		if (!stayOpen && IsOpen) {
			Cancel();
		}
	}

	private static string CategoryKey(A11yCategory category) => category switch {
		A11yCategory.Motor => "a11y.category.motor",
		A11yCategory.Cognitive => "a11y.category.cognitive",
		A11yCategory.Vision => "a11y.category.vision",
		A11yCategory.Hearing => "a11y.category.hearing",
		_ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
	};

	private void Save() {
		_settings.SaveToDisk();
		IsOpen = false;
	}

	/// <summary>Discards any edit not yet saved by reloading <see cref="A11ySettings"/> from disk, and closes the panel.</summary>
	public void Cancel() {
		_settings.LoadFromDisk();
		IsOpen = false;
	}
}
