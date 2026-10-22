using System.Numerics;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The floating "Keys" window opened from the menu bar: every fixed keyboard key of a <see cref="KeyMap"/>, read-only,
/// as a collapsible section per <see cref="KeySection"/> in the map's order, each a table of the keys and what they do
/// (<see cref="KeyRows"/>), with a filter over both columns at the top. Each key is shown as the player's keyboard and
/// interface language name it (<see cref="KeyLabels"/>), from a layout of the window's own that it takes up again every
/// frame it is open, so a layout switched with the window up shows on the next frame. The window is HERCULAN's own.
/// The title-bar close, Close and [Esc] (<see cref="HostMenuBar.BackOut"/>) close it.
/// </summary>
sealed class KeysWindow {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 680f;
	private const float PanelHeight = 560f;
	private const int FilterLength = 64;

	// A run's join; U+2026 is outside the glyphs the hosts' font is built with, which ImGui would draw as '?', so it
	// falls back to three dots until the font has it.
	private const string Ellipsis = " … ";
	private const string Dots = " ... ";

	private static readonly string[] WindowEntries = [
		"keys.title", "keys.filter_hint", "keys.column_key", "keys.column_action", "keys.no_match", "general.close",
	];

	private readonly LocalizationTable _localization;
	private readonly KeyMap _map;
	private readonly KeyboardLayout _layout = new(VkToScancode.KeyOf);
	private readonly KeyLabels _labels;
	private readonly KeySection[] _sections;

	// A section's rows that pass the filter, as drawn: reused every frame.
	private readonly List<(string Keys, string Description)> _matches = [];
	private string _filter = "";

	public KeysWindow(LocalizationTable localization, KeyMap map) {
		_localization = localization;
		_map = map;
		_labels = new KeyLabels(localization, _layout);
		_sections = SectionsOf(map).ToArray();
	}

	/// <summary>Whether the window is open. Set by the menu bar; see <see cref="HostMenuBar"/>.</summary>
	public bool IsOpen { get; set; }

	/// <summary>Every <c>.lang</c> entry the window shows for <paramref name="map"/>, the key labels' among them.</summary>
	public static IEnumerable<string> Entries(KeyMap map) =>
		WindowEntries
			.Concat(SectionsOf(map).Select(SectionEntry))
			.Concat(KeyRows.All.Select(row => row.Description))
			.Concat(KeyLabels.Entries(map, KeyRows.All))
			.Distinct();

	/// <summary>
	/// Draws the window, if it is open. Call once per frame, inside the ImGui frame. <paramref name="centre"/> is when it
	/// is centred on the window (<see cref="PanelCentring.Condition"/>).
	/// </summary>
	public void Draw(ImGuiCond centre) {
		if (!IsOpen) {
			return;
		}

		_layout.Refresh();

		// Never taller than the window it is over, so the Close button stays on screen at 640x480.
		var viewport = ImGui.GetMainViewport();
		ImGui.SetNextWindowSize(new Vector2(MathF.Min(ScaledImGui.Scaled(PanelWidth), viewport.Size.X),
			MathF.Min(ScaledImGui.Scaled(PanelHeight), viewport.Size.Y * 0.9f)));
		ImGui.SetNextWindowPos(viewport.GetCenter(), centre, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		if (ImGui.Begin(Text("keys.title") + "###keys", ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			ImGui.SetNextItemWidth(-1f);
			ImGui.InputTextWithHint("##filter", Text("keys.filter_hint"), ref _filter, FilterLength);

			// The rows scroll between the filter and the Close button, which stay put.
			var style = ImGui.GetStyle();
			float footer = ImGui.GetFrameHeight() + style.ItemSpacing.Y * 2f + 1f;
			if (ImGui.BeginChild("##rows", new Vector2(0f, -footer))) {
				Sections(_filter.Trim(), FontHas('…') ? Ellipsis : Dots);
			}
			ImGui.EndChild();

			ImGui.Separator();
			if (ImGui.Button(Text("general.close"), new Vector2(ImGui.GetContentRegionAvail().X, 0f))) {
				stayOpen = false;
			}
		}

		ImGui.End();

		if (!stayOpen) {
			IsOpen = false;
		}
	}

	// Each section with a row that passes the filter, open while there is a filter so its matches show.
	private void Sections(string filter, string runJoin) {
		bool any = false;
		foreach (var section in _sections) {
			_matches.Clear();
			foreach (var row in KeyRows.All) {
				if (row.Section != section) {
					continue;
				}

				string keys = _labels.Of(_map, row, runJoin);
				string description = Text(row.Description);
				if (filter.Length == 0 || keys.Contains(filter, StringComparison.OrdinalIgnoreCase)
						|| description.Contains(filter, StringComparison.OrdinalIgnoreCase)) {
					_matches.Add((keys, description));
				}
			}

			if (_matches.Count == 0) {
				continue;
			}

			any = true;
			if (filter.Length > 0) {
				ImGui.SetNextItemOpen(true);
			}

			if (!ImGui.CollapsingHeader(Text(SectionEntry(section)) + "###section" + (int)section, ImGuiTreeNodeFlags.DefaultOpen)) {
				continue;
			}

			if (!ImGui.BeginTable("##keys" + (int)section, 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp)) {
				continue;
			}

			ImGui.TableSetupColumn(Text("keys.column_key"), ImGuiTableColumnFlags.WidthStretch, 1f);
			ImGui.TableSetupColumn(Text("keys.column_action"), ImGuiTableColumnFlags.WidthStretch, 2.4f);
			ImGui.TableHeadersRow();
			foreach (var (keys, description) in _matches) {
				ImGui.TableNextRow();
				ImGui.TableNextColumn();
				ImGui.TextWrapped(keys);
				ImGui.TableNextColumn();
				ImGui.TextWrapped(description);
			}

			ImGui.EndTable();
		}

		if (!any) {
			ImGui.TextDisabled(Text("keys.no_match"));
		}
	}

	// The sections in the order the map first gives each.
	private static IEnumerable<KeySection> SectionsOf(KeyMap map) => map.Bindings.Select(binding => binding.Section).Distinct();

	private static string SectionEntry(KeySection section) => section switch {
		KeySection.Driving => "keys.section.driving",
		KeySection.Weapons => "keys.section.weapons",
		KeySection.TargetingAndShields => "keys.section.targeting",
		KeySection.DisplaysAndViews => "keys.section.displays",
		KeySection.FlashComm => "keys.section.flash_comm",
		KeySection.CommandDisplay => "keys.section.command_display",
		KeySection.DamageDetail => "keys.section.damage_detail",
		KeySection.OutsideView => "keys.section.outside_view",
		KeySection.PanelsAndGame => "keys.section.panels",
		KeySection.Herculan => "keys.section.herculan",
		_ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
	};

	// Whether the current font has a glyph for c.
	private static bool FontHas(char c) {
		var lookup = ImGui.GetFont().IndexLookup;
		return c < lookup.Size && lookup[c] != ushort.MaxValue;
	}

	private string Text(string key) => _localization.GetStringOrKey(key);
}
