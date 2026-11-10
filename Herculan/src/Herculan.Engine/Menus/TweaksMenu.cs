using System.Numerics;
using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using ImGuiNET;

namespace Herculan.Engine.Menus;

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
///
/// <para>It lists the settings the host offers, those the running game reads, less the hidden ones, and its preset
/// buttons set those alone.</para>
/// </summary>
public sealed class TweaksMenu {
	// In pixels on a 100% display (ScaledImGui.Scaled). The panel is never narrower than this, whatever its columns need.
	private const float MinimumPanelWidth = 440f;

	// In pixels on a 100% display: the space between the two columns.
	private const float ColumnGap = 24f;

	// The visible settings in the order they are drawn, grouped once rather than every frame.
	private readonly (TweakCategory Category, TweakSettingDefinition<bool>[] Settings)[] _visibleByCategory;

	// How many of _visibleByCategory's categories go in the left column; the rest go in the right one. Equal to its
	// length when the list is one column.
	private readonly int _leftCategories;

	// The offered settings, hidden ones included, which the preset buttons set.
	private readonly TweakSettingDefinition<bool>[] _offered;

	private readonly TweakSettings _settings;
	private readonly LocalizationTable _localization;

	// Built once: passing the method groups in Draw would allocate the array and a delegate for each, every frame.
	private readonly (string key, Action callback)[] _presetButtons;
	private readonly (string key, Action callback)[] _closeButtons;

	/// <summary>Whether the panel is currently open. Set by the menu bar; see <see cref="HostMenuBar"/>.</summary>
	public bool IsOpen { get; set; }

	/// <summary>The ImGui the panel draws in, for its italic note; the note is drawn upright without one.</summary>
	public ScaledImGui? Fonts { get; set; }

	/// <param name="offered">The settings to list and set, or null for every one <paramref name="settings"/> holds.</param>
	public TweaksMenu(TweakSettings settings, LocalizationTable localization,
			IReadOnlyCollection<TweakSettingDefinition<bool>>? offered = null) {
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_localization = localization ?? throw new ArgumentNullException(nameof(localization));
		_presetButtons = [("tweaks.none", DisableAll), ("tweaks.defaults", ApplyDefaults), ("tweaks.recommended", ApplyRecommended), ("tweaks.all", EnableAll)];
		_closeButtons = [("general.save", Save), ("general.cancel", Cancel)];
		_offered = settings.All.Where(d => offered == null || offered.Contains(d)).ToArray();
		_visibleByCategory = _offered.Where(d => !d.Hidden).GroupBy(d => d.Category).Select(g => (g.Key, g.ToArray())).ToArray();
		_leftCategories = BalancedSplit(_visibleByCategory.Select(c => c.Settings.Length + 1).ToArray());
	}

	/// <summary>
	/// Where to split the categories between the two columns: the number that go in the left column, chosen so the
	/// taller column is as short as it can be without splitting a category. <paramref name="rows"/> is each category's
	/// height in rows, its heading included. A single category is the whole list, in one column.
	/// </summary>
	internal static int BalancedSplit(IReadOnlyList<int> rows) {
		int total = rows.Sum();
		int best = rows.Count;
		int bestTallest = total;
		int left = 0;

		for (int split = 1; split < rows.Count; split++) {
			left += rows[split - 1];
			int tallest = Math.Max(left, total - left);
			if (tallest < bestTallest) {
				best = split;
				bestTallest = tallest;
			}
		}

		return best;
	}

	/// <summary>
	/// Draws the panel, if it is open. Call once per frame, inside the ImGui frame. <paramref name="centre"/> is when
	/// it is centred on the window (<see cref="PanelCentring.Condition"/>).
	/// </summary>
	public void Draw(ImGuiCond centre) {
		if (!IsOpen) {
			return;
		}

		// Each column is as wide as its widest row, so no label is cut off in any language. A zero height means "fit
		// the content", so the panel grows to whatever height its taller column needs.
		float leftWidth = ColumnWidth(0, _leftCategories);
		float rightWidth = ColumnWidth(_leftCategories, _visibleByCategory.Length);
		var style = ImGui.GetStyle();
		float columnsWidth = leftWidth + (rightWidth > 0 ? ScaledImGui.Scaled(ColumnGap) + rightWidth : 0f);
		float panelWidth = Math.Max(ScaledImGui.Scaled(MinimumPanelWidth), columnsWidth + style.WindowPadding.X * 2);
		ImGui.SetNextWindowSize(new Vector2(panelWidth, 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		// The title doubles as the window's ID, so a language change opens it afresh, re-centred; a
		// "###tweaks" suffix would keep the ID fixed at the cost of a string built every frame.
		if (ImGui.Begin(_localization.GetStringOrKey("tweaks.title"), ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			ImGui.TextWrapped(_localization.GetStringOrKey("tweaks.description"));
			ImGui.Spacing();
			if (Fonts?.ItalicFont is { } italic) {
				ImGui.PushFont(italic);
				ImGui.TextWrapped(_localization.GetStringOrKey("tweaks.note"));
				ImGui.PopFont();
			} else {
				ImGui.TextWrapped(_localization.GetStringOrKey("tweaks.note"));
			}

			if (rightWidth > 0) {
				ImGui.Columns(2, "tweak_columns", false);
				ImGui.SetColumnWidth(0, leftWidth + ScaledImGui.Scaled(ColumnGap));
				DrawCategories(0, _leftCategories);
				ImGui.NextColumn();
				DrawCategories(_leftCategories, _visibleByCategory.Length);
				ImGui.Columns(1);
			} else {
				DrawCategories(0, _visibleByCategory.Length);
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

	// Draws the categories from first up to (not including) end, each a heading and its checkboxes.
	private void DrawCategories(int first, int end) {
		for (int i = first; i < end; i++) {
			var (category, settings) = _visibleByCategory[i];
			ImGui.SeparatorText(_localization.GetStringOrKey(CategoryKey(category)));
			foreach (var definition in settings) {
				bool value = _settings.GetSettingValue(definition);
				if (ImGui.Checkbox(DisplayName(definition), ref value)) {
					_settings.SetSettingValue(definition, value);
				}

				if (ImGui.IsItemHovered() && _localization.GetString(definition.DescriptionKey) is { } description) {
					ScaledImGui.Tooltip(description);
				}
			}
		}
	}

	// The width the categories from first up to (not including) end need: their widest checkbox or heading. Zero for none.
	private float ColumnWidth(int first, int end) {
		var style = ImGui.GetStyle();
		float checkbox = ImGui.GetFrameHeight() + style.ItemInnerSpacing.X;
		float width = 0f;

		for (int i = first; i < end; i++) {
			var (category, settings) = _visibleByCategory[i];
			// SeparatorText draws a short rule either side of its text.
			width = Math.Max(width, ImGui.CalcTextSize(_localization.GetStringOrKey(CategoryKey(category))).X
				+ style.SeparatorTextPadding.X * 2);
			foreach (var definition in settings) {
				width = Math.Max(width, checkbox + ImGui.CalcTextSize(DisplayName(definition)).X);
			}
		}

		return width;
	}

	private string DisplayName(TweakSettingDefinition<bool> definition) =>
		_localization.GetString(definition.DisplayNameKey) ?? definition.ID;

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
		foreach (var definition in _offered.Where(d => !d.Hidden)) {
			_settings.SetSettingValue(definition, value);
		}
	}

	private void ApplyDefaults() {
		foreach (var setting in _offered) {
			_settings.SetSettingValue(setting, setting.DefaultValue);
		}
	}

	private void ApplyRecommended() {
		foreach (var setting in _offered) {
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
