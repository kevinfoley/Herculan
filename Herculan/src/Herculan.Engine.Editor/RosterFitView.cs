using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Platform;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// In the view of a herc, flyer, structure or ground vehicle outside the player's squad, its type and a herc's weapon fit
/// (docs/herculan/handoff-editor-roster.md#type-and-fit): a herc's chassis over every type of
/// <c>nam\MECHS.NAM</c> with its mounts (<see cref="MachineFitView"/>, <see cref="MachineFitter"/>); a flyer's type over
/// the flyers the Toolbox offers; a structure's or ground vehicle's within its kind (<see cref="UnitTypeCatalog.ChoicesFor"/>),
/// each under the name the Toolbox lists it by (<see cref="RosterTypeEditor"/>).
///
/// <para>Where a field the view edits comes from an option of a random choice the editor edits, "Changes apply to:" offers
/// that option, wherever it is picked, which an edit then writes, with how many records pick from a choice holding it
/// (<see cref="RandomChoices.Picking"/>), or only this herc (flyer, structure, ground vehicle), which writes the record's own
/// overlay instead and goes back to the option whenever another record is shown and not saved
/// (docs/herculan/handoff-editor-conditions.md#variants).</para>
/// </summary>
internal sealed class RosterFitView {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float TypeWidth = 200f;

	private readonly MachineFitter _fitter;
	private readonly MachineFitView _fit;
	private readonly RosterTypeEditor _types;
	private readonly VariantEditor _variants;
	private readonly RandomChoices _choices;
	private readonly UnitTypeCatalog _catalog;

	// The record "only this" is chosen for; showing any other record goes back to the option.
	private (int Row, short Guid)? _onlyThis;

	public RosterFitView(MachineFitter fitter, RosterTypeEditor types, VariantEditor variants, RandomChoices choices,
			UnitTypeCatalog catalog) {
		_fitter = fitter;
		_fit = new MachineFitView(fitter);
		_types = types;
		_variants = variants;
		_choices = choices;
		_catalog = catalog;
	}

	/// <summary>Whether <paramref name="placed"/> gets this view: an object outside the squad of a mission held as its <c>.MSN</c>.</summary>
	public bool Covers(SceneObject placed) => _variants.RecordOf(placed) != null;

	/// <summary>Draws the type, and a herc's fit, of <paramref name="placed"/>, when <see cref="Covers"/> it.</summary>
	public void Draw(SceneObject placed) {
		if (_variants.RecordOf(placed) is not var (row, guid)) {
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
		var drawn = row == 12 ? _fitter.DrawnAlternative(guid) : _types.DrawnAlternative(row, guid);
		bool toAlternative = drawn != null && !OnlyThis(row, guid, noun, drawn.Value);
		if (row == 12) {
			if (_fitter.PartlyDrawn(guid) && toAlternative) {
				EditorText.Note($"What was set for this {noun} alone stays as set.");
			}

			_fit.Draw(guid, choices, type => _catalog.NameOf(kind, type),
				"A new chassis comes with the fit a new herc of it gets.", toAlternative);
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

	// Where an edit goes when a field comes from option <alternative>: that option, or only this record. Answers whether only
	// this record is chosen.
	private bool OnlyThis(int row, short guid, string noun, MsnRecordId alternative) {
		int picking = _choices.Picking(alternative);
		ImGui.Text("Changes apply to:");
		if (ImGui.RadioButton($"that option, wherever it is picked ({picking} {noun}{(picking == 1 ? "" : "s")})", _onlyThis != (row, guid))) {
			_onlyThis = null;
		}

		if (ImGui.RadioButton($"only this {noun}", _onlyThis == (row, guid))) {
			_onlyThis = (row, guid);
		}

		return _onlyThis == (row, guid);
	}

	private static void Failure(string? failure) {
		if (failure != null) {
			ImGui.TextWrapped($"Not changed: {failure}");
		}
	}
}
