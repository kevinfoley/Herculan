using ImGuiNET;

namespace Herculan.Engine.Host.Localization;

/// <summary>
/// The interface-language combo: every <c>.lang</c> file by the name it gives itself
/// (<see cref="LocalizationTable.GetLanguages"/>), a choice taking effect at once. The Settings window and the
/// startup's install prompt both show it.
/// </summary>
sealed class InterfaceLanguageCombo {
	private readonly LocalizationTable _localization;

	// Found on the first draw; finding them reads every .lang file.
	private IReadOnlyList<(string Locale, string Name)>? _languages;

	public InterfaceLanguageCombo(LocalizationTable localization) {
		_localization = localization;
	}

	/// <summary>Forgets the languages found, so the next <see cref="Draw"/> looks for them again.</summary>
	public void Forget() => _languages = null;

	/// <summary>Draws the combo at half the width left; true on the frame a choice changes the locale.</summary>
	public bool Draw() {
		_languages ??= _localization.GetLanguages();
		string current = _localization.SelectedLocale;
		string preview = _languages.FirstOrDefault(language => language.Locale == current).Name ?? current;
		string label = _localization.GetStringOrKey("settings.interface_language");

		bool changed = false;
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
		if (ImGui.BeginCombo(label + "###interface_language", preview)) {
			foreach (var (locale, name) in _languages) {
				ImGui.PushID(locale);
				if (ImGui.Selectable(name, locale == current) && locale != current) {
					_localization.SelectedLocale = locale;
					changed = _localization.SelectedLocale == locale;
				}
				ImGui.PopID();
			}
			ImGui.EndCombo();
		}

		return changed;
	}
}
