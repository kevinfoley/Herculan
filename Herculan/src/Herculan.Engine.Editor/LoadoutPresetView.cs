using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A HERC's "Loadout preset" choice under its chassis, and its "Save preset" and "Delete preset" buttons under its hardpoints,
/// with their prompts (docs/herculan/editor-design.md#new-units), drawn by <see cref="MachineFitView"/> over the
/// presets of <see cref="LoadoutPresets"/>. A preset applied writes through <see cref="MachineFitter.RequestLoadout"/>. One per
/// mission, shared by the squad's views and the views of the HERCs outside it, so a prompt is drawn by the view that put it up.
/// </summary>
internal sealed class LoadoutPresetView {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PromptWidth = 360f;

	// The longest name the name prompt takes.
	private const uint MaxName = 48;

	private enum Prompt {
		None,
		Name,
		Overwrite,
		Delete,
	}

	private readonly MachineFitter _fitter;
	private readonly LoadoutPresets _presets;

	private Prompt _prompt;
	private bool _raised;

	// The HERC whose view put the prompt up, and the option it edits.
	private (short Guid, MsnRecordId? Option) _owner;
	private string _typedName = "";
	private string _deleting = "";

	public LoadoutPresetView(MachineFitter fitter, LoadoutPresets presets) {
		_fitter = fitter;
		_presets = presets;
	}

	/// <summary>Whether a prompt is up, which takes the keyboard, Esc included.</summary>
	public bool IsOpen => _prompt != Prompt.None;

	/// <summary>
	/// The "Loadout preset" dropdown, <paramref name="width"/> wide, for the herc of row-12 GUID <paramref name="guid"/>, of
	/// <c>MECHS.NAM</c> type <paramref name="type"/> and fitted with <paramref name="weapons"/> and <paramref name="ammo"/>: the
	/// type's presets, by name, each the current fit does not take disabled with the reason; picking one applies it, in
	/// <paramref name="option"/>'s template where a template supplies a field (<see cref="MachineFitter.RequestLoadout"/>).
	/// </summary>
	public void DrawChoice(short guid, int type, short[] weapons, short[] ammo, float width, MsnRecordId? option) {
		var presets = _presets.For(type);
		var current = Matching(presets, weapons, ammo);
		ImGui.SetNextItemWidth(width);
		ImGui.BeginDisabled(presets.Count == 0);
		if (EditorText.BeginCombo("Loadout preset", presets.Count == 0 ? "(none saved)" : current?.Name ?? "(none)")) {
			for (int i = 0; i < presets.Count; i++) {
				var preset = presets[i];
				string? why = WhyNotApply(type, preset);
				ImGui.BeginDisabled(why != null);
				bool chosen = ReferenceEquals(preset, current);
				if (EditorText.WrappedSelectable(preset.Name, $"preset{i}", chosen) && !chosen) {
					_fitter.RequestLoadout(guid, preset.Slots.Select(slot => (slot.Slot, slot.Weapon, slot.Ammo)).ToList(), option);
				}

				ImGui.EndDisabled();
				EditorText.Tooltip(why);
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
	}

	/// <summary>
	/// "Save preset" and "Delete preset" for the herc <see cref="DrawChoice"/> draws, both disabled while
	/// <paramref name="disabled"/>, with the prompt either put up for it; <paramref name="chassisName"/> names its chassis as the
	/// view shows it. Save takes the weapon and ammunition type of each hardpoint the view edits.
	/// </summary>
	public void DrawButtons(short guid, int type, string chassisName, short[] weapons, short[] ammo, MsnRecordId? option,
			bool disabled) {
		var current = Matching(_presets.For(type), weapons, ammo);
		var fitted = Fitted(type, weapons, ammo);
		ImGui.PushID(guid);
		Span<float> widths = [ButtonRow.LabelWidth("Save preset"), ButtonRow.LabelWidth("Delete preset")];
		var row = new ButtonRow(widths);
		ImGui.BeginDisabled(disabled || fitted.Count == 0);
		if (row.Button("Save preset")) {
			_typedName = "";
			Show(Prompt.Name, guid, option);
		}

		ImGui.EndDisabled();
		ImGui.BeginDisabled(disabled || current == null);
		if (row.Button("Delete preset") && current != null) {
			_deleting = current.Name;
			Show(Prompt.Delete, guid, option);
		}

		ImGui.EndDisabled();
		if (_prompt != Prompt.None && _owner == (guid, option)) {
			DrawPrompt(type, chassisName, fitted);
		}

		ImGui.PopID();
	}

	// The first of <presets> whose every slot holds its weapon and ammunition type in <weapons> and <ammo>, or null.
	private static LoadoutPreset? Matching(IEnumerable<LoadoutPreset> presets, short[] weapons, short[] ammo) =>
		presets.FirstOrDefault(preset => preset.Slots.All(slot =>
			slot.Slot < weapons.Length && weapons[slot.Slot] == slot.Weapon && slot.Slot < ammo.Length && ammo[slot.Slot] == slot.Ammo));

	// What Save stores: each hardpoint of <type> the view edits, with its weapon and ammunition type.
	private List<LoadoutPresetSlot> Fitted(int type, short[] weapons, short[] ammo) =>
		_fitter.Mounts(type).Where(mount => _fitter.WhyNoWeapons(type, mount) == null)
			.Select(mount => new LoadoutPresetSlot(mount, weapons[mount], ammo[mount])).ToList();

	// Why <preset> cannot be applied to <type> as the hardpoints offer weapons now, or null when it can: a slot that is not one
	// of its hardpoints, or a weapon its hardpoint does not offer.
	private string? WhyNotApply(int type, LoadoutPreset preset) {
		var mounts = _fitter.Mounts(type);
		var unfit = preset.Slots
			.Where(slot => !mounts.Contains(slot.Slot) || !_fitter.WeaponsFor(type, slot.Slot).Contains(slot.Weapon))
			.ToList();
		if (unfit.Count == 0) {
			return null;
		}

		bool unrestrictedTakes = HercGroups.IsCybrid(type) && !_fitter.CybridUnrestricted
			&& unfit.All(slot => mounts.Contains(slot.Slot) && _fitter.UnrestrictedWeaponsFor(type).Contains(slot.Weapon));
		return unrestrictedTakes
			? "Some of its weapons are offered only under the \"Unrestricted\" Cybrid Loadouts setting, in Editor Settings."
			: "Some of its weapons are not offered on this chassis's hardpoints.";
	}

	private void DrawPrompt(int type, string chassisName, IReadOnlyList<LoadoutPresetSlot> fitted) {
		string title = (_prompt == Prompt.Delete ? "Delete preset" : "Save preset") + "###loadout-preset";
		if (!_raised) {
			ImGui.OpenPopup(title);
			_raised = true;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PromptWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(title, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			// Taken down by something other than a choice: raised again, since the prompt still waits on one.
			_raised = false;
			return;
		}

		var shown = _prompt;
		switch (shown) {
			case Prompt.Name:
				DrawName(type, fitted);
				break;
			case Prompt.Overwrite:
				EditorText.Wrapped($"A preset named \"{_typedName.Trim()}\" already exists for {chassisName}.");
				switch (Buttons("Overwrite")) {
					case true:
						_presets.Save(type, _typedName, fitted);
						Close();
						break;
					case false:
						// Back to the name, as typed.
						_prompt = Prompt.Name;
						break;
				}

				break;
			case Prompt.Delete:
				EditorText.Wrapped($"Delete the preset \"{_deleting}\" for {chassisName}?");
				switch (Buttons("Delete")) {
					case true:
						_presets.Delete(type, _deleting);
						Close();
						break;
					case false:
						Close();
						break;
				}

				break;
		}

		// A choice that took this prompt down, or put up the next, closes this one; the next is raised next frame.
		if (_prompt != shown || !_raised) {
			ImGui.CloseCurrentPopup();
			_raised = false;
		}

		ImGui.EndPopup();
	}

	// The name prompt: a name field with the keyboard, Save (Enter too) while the trimmed name is not empty, and Cancel. A name
	// the chassis already has asks before it is overwritten.
	private void DrawName(int type, IReadOnlyList<LoadoutPresetSlot> fitted) {
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("Name");
		ImGui.SameLine();
		if (ImGui.IsWindowAppearing()) {
			ImGui.SetKeyboardFocusHere();
		}

		ImGui.SetNextItemWidth(-1f);
		bool entered = ImGui.InputText("##name", ref _typedName, MaxName, ImGuiInputTextFlags.EnterReturnsTrue);
		bool named = _typedName.Trim().Length > 0;
		Span<float> widths = [ButtonRow.LabelWidth("Save"), ButtonRow.LabelWidth("Cancel")];
		var row = new ButtonRow(widths);
		ImGui.BeginDisabled(!named);
		bool save = row.Button("Save");
		ImGui.EndDisabled();
		bool cancel = row.Button("Cancel");
		if ((save || entered) && named) {
			if (_presets.Find(type, _typedName) != null) {
				_prompt = Prompt.Overwrite;
			} else {
				_presets.Save(type, _typedName, fitted);
				Close();
			}
		} else if (cancel || ImGui.IsKeyPressed(ImGuiKey.Escape)) {
			Close();
		}
	}

	// <label> and Cancel: true for <label>, false for Cancel or Esc, null for neither.
	private static bool? Buttons(string label) {
		ImGui.Dummy(new Vector2(0f, ScaledImGui.Scaled(6f)));
		Span<float> widths = [ButtonRow.LabelWidth(label), ButtonRow.LabelWidth("Cancel")];
		var row = new ButtonRow(widths);
		bool confirm = row.Button(label);
		bool cancel = row.Button("Cancel");
		return confirm ? true : cancel || ImGui.IsKeyPressed(ImGuiKey.Escape) ? false : null;
	}

	private void Show(Prompt prompt, short guid, MsnRecordId? option) {
		_prompt = prompt;
		_owner = (guid, option);
		_raised = false;
	}

	private void Close() {
		_prompt = Prompt.None;
		_raised = false;
	}
}
