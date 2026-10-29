using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Platform;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// In the view of a herc, flyer, structure or ground vehicle outside the player's squad, its type and a herc's weapon fit
/// (docs/herculan/editor-design.md#new-units), drawn under its Loadout or Type section (<see cref="LoadoutView"/>):
/// a herc's chassis over every type of <c>nam\MECHS.NAM</c> with its mounts (<see cref="MachineFitView"/>,
/// <see cref="MachineFitter"/>); a flyer's type over the flyers the Toolbox offers; a structure's or ground vehicle's within its
/// kind (<see cref="UnitTypeCatalog.ChoicesFor"/>), each under the name the Toolbox lists it by (<see cref="RosterTypeEditor"/>).
/// Given the option being edited, it shows and edits that option (docs/herculan/editor-design.md#random-tables).
/// </summary>
internal sealed class RosterFitView {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float TypeWidth = 200f;

	private readonly MachineFitter _fitter;
	private readonly MachineFitView _fit;
	private readonly RosterTypeEditor _types;
	private readonly RandomTableEditor _tableEditor;
	private readonly UnitTypeCatalog _catalog;

	public RosterFitView(MachineFitter fitter, RosterTypeEditor types, RandomTableEditor tableEditor, UnitTypeCatalog catalog,
			LoadoutPresetView presets) {
		_fitter = fitter;
		_fit = new MachineFitView(fitter, presets);
		_types = types;
		_tableEditor = tableEditor;
		_catalog = catalog;
	}

	/// <summary>
	/// Draws the type, and a herc's fit, of <paramref name="placed"/>, an object outside the squad: given
	/// <paramref name="option"/>, the template of the option being edited of the table it picks from, as a load picking that
	/// option makes it, each edit of a field a template supplies written there; else as the layout shows it.
	/// </summary>
	public void Draw(SceneObject placed, MsnRecordId? option) {
		if (_tableEditor.RecordOf(placed) is not var (row, guid)) {
			return;
		}

		var placement = placed.Placement;
		bool isVehicle = placed.Object is Sim.BaseObject { Type.IsVehicle: true };
		var kind = UnitTypeCatalog.KindOf(placement.Kind, isVehicle);
		var choices = _catalog.ChoicesFor(placement.Kind, isVehicle);
		ImGui.PushID("fit");
		if (row == 12) {
			// A field an overlay of the herc sets is its own in every option, and an edit of it writes that overlay
			// (RosterFit.Write).
			if (option != null && _fitter.PartlyFromTemplate(guid)) {
				EditorText.Note("Part of its loadout is set for this HERC alone: that part stays as set whichever option a load "
					+ "picks, and changing it changes this HERC only.");
			}

			_fit.Draw(guid, choices, type => _catalog.NameOf(kind, type),
				"Changing the chassis also gives this HERC that chassis's standard weapons.", option);
			Failure(_fitter.Failure);
		} else {
			DrawType(row, guid, kind, choices, _types.Type(row, guid, option), option);
		}

		ImGui.PopID();
	}

	// A flyer's, structure's or ground vehicle's type, a choice within its kind, under its section's "Type:" label.
	private void DrawType(int row, short guid, ToolboxKind kind, IEnumerable<int> choices, int current, MsnRecordId? option) {
		string? why = _types.WhyFixed(row, guid);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(TypeWidth));
		ImGui.BeginDisabled(why != null);
		if (EditorText.BeginCombo("##type", _catalog.NameOf(kind, current))) {
			foreach (int type in choices) {
				if (ImGui.Selectable(_catalog.NameOf(kind, type), type == current) && type != current) {
					_types.RequestType(row, guid, type, option);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		Failure(_types.Failure);
	}

	private static void Failure(string? failure) {
		if (failure != null) {
			ImGui.TextWrapped($"Not changed: {failure}");
		}
	}
}
