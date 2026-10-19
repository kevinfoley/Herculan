using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Platform;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// In the view of a herc, flyer, structure or ground vehicle outside the player's squad, its type and a herc's weapon fit
/// (docs/herculan/handoff-editor-mission-editing.md#in-place, "Variants"): a herc's chassis over every type of
/// <c>nam\MECHS.NAM</c> with its mounts (<see cref="MachineFitView"/>, <see cref="MachineFitter"/>); a flyer's type over
/// the flyers the Toolbox offers; a structure's or ground vehicle's within its kind (<see cref="UnitTypeCatalog.ChoicesFor"/>),
/// each under the name the Toolbox lists it by (<see cref="RosterTypeEditor"/>).
///
/// <para>Where a field the view edits comes from a variant alternative of a table the editor edits, an edit changes the
/// alternative, and the view says how many records draw it; "Only this herc" (flyer, structure, ground vehicle), unticked
/// whenever another record is shown and not saved, writes the record's own overlay instead.</para>
/// </summary>
internal sealed class RosterFitView {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float TypeWidth = 200f;

	private readonly MachineFitter _fitter;
	private readonly MachineFitView _fit;
	private readonly RosterTypeEditor _types;
	private readonly VariantEditor _variants;
	private readonly UnitTypeCatalog _catalog;

	// The record "Only this" is ticked for; showing any other record unticks it.
	private (int Row, short Guid)? _onlyThis;

	public RosterFitView(MachineFitter fitter, RosterTypeEditor types, VariantEditor variants, UnitTypeCatalog catalog) {
		_fitter = fitter;
		_fit = new MachineFitView(fitter);
		_types = types;
		_variants = variants;
		_catalog = catalog;
	}

	/// <summary>Whether <paramref name="placed"/> gets this view: an object outside the squad of a mission held as its <c>.MSN</c>.</summary>
	public bool Covers(SceneObject placed) => _variants.RecordOf(placed) != null;

	/// <summary>Draws the type, and a herc's fit, of <paramref name="placed"/>, when <see cref="Covers"/> it.</summary>
	public void Draw(SceneObject placed) {
		if (_variants.Source is not { } source || _variants.RecordOf(placed) is not var (row, guid)) {
			return;
		}

		if (_onlyThis != (row, guid)) {
			_onlyThis = null;
		}

		var placement = placed.Placement;
		bool isVehicle = placed.Object is Sim.BaseObject { Type.IsVehicle: true };
		var kind = UnitTypeCatalog.KindOf(placement.Kind, isVehicle);
		var choices = _catalog.ChoicesFor(placement.Kind, isVehicle);
		string noun = VariantView.Noun(placed);
		ImGui.Separator();
		ImGui.PushID("fit");
		var drawn = row == 12 ? _fitter.DrawnAlternative(placement.SlotIndex) : _types.DrawnAlternative(row, guid);
		bool toAlternative = drawn != null && !OnlyThis(row, guid, noun, source, drawn.Value);
		if (row == 12) {
			if (_fitter.PartlyDrawn(placement.SlotIndex) && toAlternative) {
				EditorText.Note($"A field set for this {noun} alone stays its own.");
			}

			_fit.Draw(placement.SlotIndex, choices, type => _catalog.NameOf(kind, type),
				"A new chassis comes with the fit a new herc of it gets.", toAlternative, showFixed: true);
			Failure(_fitter.Failure);
		} else {
			DrawType(row, guid, kind, choices, placement.TypeIndex, toAlternative);
		}

		ImGui.PopID();
	}

	// A flyer's, structure's or ground vehicle's type, a choice within its kind.
	private void DrawType(int row, short guid, ToolboxKind kind, IEnumerable<int> choices, int current, bool toAlternative) {
		string? why = _types.WhyFixed(row, guid);
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Type:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(TypeWidth));
		ImGui.BeginDisabled(why != null);
		if (ImGui.BeginCombo("##type", _catalog.NameOf(kind, current))) {
			foreach (int type in choices) {
				if (ImGui.Selectable(_catalog.NameOf(kind, type), type == current) && type != current) {
					_types.RequestType(row, guid, type, toAlternative);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		Failure(_types.Failure);
	}

	// Where an edit goes when a field comes from <alternative>: the checkbox, and what an edit then changes. Answers whether
	// it is ticked.
	private bool OnlyThis(int row, short guid, string noun, MissionDocument source, MsnRecordId alternative) {
		bool ticked = _onlyThis == (row, guid);
		if (ImGui.Checkbox($"Only this {noun}", ref ticked)) {
			_onlyThis = ticked ? (row, guid) : null;
		}

		int drawers = VariantEditing.DrawingNow(source, alternative).Count;
		EditorText.Note(ticked
			? $"Changes this {noun} alone."
			: $"Changes the alternative: {drawers} {(drawers == 1 ? "record draws" : "records draw")} it.");
		return ticked;
	}

	private static void Failure(string? failure) {
		if (failure != null) {
			ImGui.TextWrapped($"Not changed: {failure}");
		}
	}
}
