using HercWorks.Core.Data.File.Msn;
using ImGuiNET;

namespace Herculan.Es2.Editor;

/// <summary>
/// How a random table's chances are shown and typed, in the Random table view (<see cref="RandomTableView"/>) and a record's
/// Loadout or Type section (<see cref="LoadoutView"/>): each option's chance a percent to one decimal in a typed field, held as
/// a draft until the field lets go; the note giving the chances exactly where the percents round them
/// (<see cref="RandomTableEditing.Exact"/>); and the share no option takes, with what a load landing there does
/// (docs/herculan/editor-design.md#random-tables).
/// </summary>
internal sealed class RandomTableChances {
	/// <summary>The chance field's width, in pixels on a 100% display (Engine.Platform.ScaledImGui.Scaled).</summary>
	public const float FieldWidth = 56f;

	// A chance being typed, and the field it is for, until the field lets go.
	private string? _draftFor;
	private float _draft;

	/// <summary>
	/// A typed percent to one decimal, 0 to 100; answers the value when the field lets go
	/// after a change.
	/// </summary>
	/// <param name="id">The field's ImGui ID, unique among the chances shown.</param>
	/// <param name="current">The percent shown.</param>
	public decimal? Field(string id, decimal current) {
		if (_draftFor != null && _draftFor != id && !ImGui.IsAnyItemActive()) {
			_draftFor = null;
		}

		float shown = _draftFor == id ? _draft : (float)current;
		if (ImGui.InputFloat($"##{id}", ref shown, 0f, 0f, "%.1f")) {
			_draftFor = id;
			_draft = shown;
		}

		decimal? typed = null;
		if (ImGui.IsItemDeactivatedAfterEdit() && _draftFor == id) {
			_draftFor = null;
			decimal value = float.IsFinite(_draft) ? decimal.Round((decimal)Math.Clamp(_draft, 0f, 100f), 1) : current;
			if (value != current) {
				typed = value;
			}
		}

		return typed;
	}

	/// <summary>
	/// Where a table's percents round its chances, a note giving them exactly, and when
	/// the table is editable that a change writes every chance as shown.
	/// </summary>
	/// <param name="table">The table.</param>
	/// <param name="editable">Whether the table can be edited.</param>
	public static void DrawRounding(RandomTable table, bool editable) {
		if (RandomTableEditing.Exact(table)) {
			return;
		}

		string exact = string.Join(", ", table.Options.Select(option => $"{table.Percent(option.Draws):0.###}%"));
		EditorText.Note(editable ? $"Rounded from {exact}: a change writes every chance as shown." : $"Rounded from {exact}.");
	}

	/// <summary>
	/// The share of a table's rolls no option takes, from the percents shown, when there is
	/// one; for a table of hercs, flyers, structures or ground vehicles with what a load landing there does to the record
	/// picking.
	/// </summary>
	/// <param name="table">The table.</param>
	/// <param name="percents">The percents shown.</param>
	/// <param name="noun">What the table's records are called.</param>
	public static void DrawNoOption(RandomTable table, IReadOnlyList<decimal> percents, string noun) {
		if (table.Leftover <= 0) {
			return;
		}

		EditorText.Wrapped($"No option: {Math.Max(100m - percents.Sum(), 0m):0.#}%");
		if (table.Row is >= 12 and <= 14) {
			// Its references stay the .MSN's own numbers, which the load reads as list positions (MissionGenerator.ReadHercs and
			// its siblings, docs/herculan/editor-design.md#random-tables).
			EditorText.Note($"A load that lands here picks no option: the {noun} keeps its own details, but its "
				+ "position, heading and triggers can come out wrong in play.");
		}
	}
}
