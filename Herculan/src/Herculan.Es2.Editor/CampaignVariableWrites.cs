using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Es2.World;
using ImGuiNET;

namespace Herculan.Es2.Editor;

/// <summary>Which record writes a list of campaign variables, which decides what its operation codes mean (<see cref="CampaignVariableWrites"/>).</summary>
internal enum WriteLayer {
	/// <summary>An action, row 10: <c>Action_Activate</c>'s two codes (docs/retail/simulation/mission-deployment.md#the-four-ways-an-action-activates).</summary>
	Action,

	/// <summary>An objective, row 17: its four codes (docs/retail/simulation/mission-objectives.md#the-record).</summary>
	Objective,

	/// <summary>An object's or a group's out-of-action report (docs/retail/simulation/mission-deployment.md#the-out-of-action-report).</summary>
	OutOfAction,
}

/// <summary>What a write does to its variable: <see cref="Add"/> or <see cref="Subtract"/> a value, or <see cref="Set"/> it to one.</summary>
internal enum WriteKind {
	/// <summary>A code the layer has no case for: it writes nothing.</summary>
	Unknown,
	Add,
	Subtract,
	Set,
}

/// <summary>One write's operation, as <see cref="CampaignVariableWrites.Operation"/> reads a layer's code.</summary>
internal readonly record struct WriteOperation(WriteKind Kind, short Value) {
	public static readonly WriteOperation Unknown = new(WriteKind.Unknown, 0);
	public static readonly WriteOperation AddOne = new(WriteKind.Add, 1);
	public static readonly WriteOperation SubtractOne = new(WriteKind.Subtract, 1);

	public static WriteOperation SetTo(short value) => new(WriteKind.Set, value);

	/// <summary>The operation as a choice names it: "Add 1", "Set to 0".</summary>
	public string Choice => Kind switch {
		WriteKind.Add => $"Add {Value}",
		WriteKind.Subtract => $"Subtract {Value}",
		WriteKind.Set => $"Set to {Value}",
		_ => "Unknown operation (no effect in play)",
	};

	/// <summary>The write to a variable, as a sentence names the variable, as a sentence.</summary>
	/// <param name="variable">The variable's name as a sentence.</param>
	public string Sentence(string variable) => Kind switch {
		WriteKind.Add => $"Add {Value} to {variable}",
		WriteKind.Subtract => $"Subtract {Value} from {variable}",
		WriteKind.Set => $"Set {variable} to {Value}",
		_ => $"Unknown operation on {variable} (no effect in play)",
	};
}

/// <summary>A change a <see cref="CampaignVariableWrites"/> list asks for, which its view makes through its own editor.</summary>
internal abstract record WriteRequest;

/// <summary>A request to set a write to a campaign variable and the layer's code.</summary>
/// <param name="Slot">The write's slot.</param>
/// <param name="Variable">The campaign variable.</param>
/// <param name="Op">The layer's code.</param>
internal sealed record SetWriteRequest(int Slot, short Variable, short Op) : WriteRequest;

/// <summary>A write after the last.</summary>
internal sealed record AddWriteRequest : WriteRequest;

/// <summary>A request to remove a write.</summary>
/// <param name="Slot">The write's slot.</param>
internal sealed record RemoveWriteRequest(int Slot) : WriteRequest;

/// <summary>
/// A list of campaign variable writes, each read as a sentence ("Add 1 to campaign variable 30", "Set campaign variable 22 to
/// 1"), the variable's meaning after its number where the game gives it one (<see cref="CampaignVariableLabels.WritePhrase"/>):
/// editable, each write an operation the layer offers and a variable picked from <see cref="CampaignVariablePicker"/>, with
/// Remove, and Add after the last; or read-only, one line per write. The view that draws it makes the change it asks for.
/// Which code means which operation in each layer is decided here and nowhere else in the editor.
/// </summary>
internal sealed class CampaignVariableWrites {
	// In pixels on a 100% display (ScaledImGui.Scaled): the operation choice's width.
	private const float OperationWidth = 100f;

	private readonly CampaignVariablePicker _picker = new();

	/// <summary>
	/// The codes a layer has a case for, each with its operation, in the order a choice offers them. The action
	/// and objective codes are those their edits write (<see cref="ActionEditing"/>, <see cref="ObjectiveEditing"/>).
	/// </summary>
	/// <param name="layer">The layer.</param>
	public static IReadOnlyList<(short Code, WriteOperation Operation)> Offered(WriteLayer layer) => layer switch {
		WriteLayer.Action => [
			(ActionEditing.CounterIncrement, WriteOperation.AddOne),
			(ActionEditing.CounterClear, WriteOperation.SetTo(0)),
		],
		WriteLayer.Objective => [
			(ObjectiveEditing.CounterSet, WriteOperation.SetTo(1)),
			(ObjectiveEditing.CounterClear, WriteOperation.SetTo(0)),
			(ObjectiveEditing.CounterIncrement, WriteOperation.AddOne),
			(ObjectiveEditing.CounterDecrement, WriteOperation.SubtractOne),
		],
		_ => [
			(OutOfActionReport.OpClear, WriteOperation.SetTo(0)),
			(OutOfActionReport.OpIncrement, WriteOperation.AddOne),
			.. Enumerable.Range(OutOfActionReport.OpSetFirst, OutOfActionReport.OpSetLast - OutOfActionReport.OpSetFirst + 1)
				.Select(code => ((short)code, WriteOperation.SetTo((short)(code - OutOfActionReport.SetBias)))),
		],
	};

	/// <summary>What a layer's code does; <see cref="WriteOperation.Unknown"/> for one it has no case for.</summary>
	/// <param name="layer">The layer.</param>
	/// <param name="code">The code.</param>
	public static WriteOperation Operation(WriteLayer layer, short code) {
		foreach (var (offered, operation) in Offered(layer)) {
			if (offered == code) {
				return operation;
			}
		}

		return WriteOperation.Unknown;
	}

	/// <summary>A write of a layer's code to a variable, as a sentence; <c>-1</c> for none chosen.</summary>
	/// <param name="layer">The layer.</param>
	/// <param name="variable">The campaign variable.</param>
	/// <param name="code">The layer's code.</param>
	public static string Sentence(WriteLayer layer, short variable, short code) {
		if (variable < 0) {
			return "No campaign variable chosen (no effect in play)";
		}

		var operation = Operation(layer, code);
		return operation.Sentence(operation.Kind == WriteKind.Unknown
			? $"campaign variable {variable}"
			: CampaignVariableLabels.WritePhrase(variable));
	}

	/// <summary>
	/// Draws the writes of a layer, each (variable, code): each a sentence, an operation choice and a variable choice with
	/// Remove, then Add, disabled with a reason under it when one cannot be added.
	/// Answers the change asked for this frame, or null.
	/// </summary>
	/// <param name="heading">The heading the writes are drawn under.</param>
	/// <param name="layer">The layer.</param>
	/// <param name="writes">The writes, each (variable, code).</param>
	/// <param name="source">The mission, whose uses of each variable the variable choice lists.</param>
	/// <param name="locked">Whether every control is disabled.</param>
	/// <param name="whyNoAdd">Said under Add when one cannot be added, or null.</param>
	public WriteRequest? Draw(string heading, WriteLayer layer, IReadOnlyList<(short Variable, short Op)> writes,
			MissionDocument source, bool locked, string? whyNoAdd) {
		ImGui.Spacing();
		ImGui.TextWrapped($"{heading}:");
		WriteRequest? request = null;
		for (int slot = 0; slot < writes.Count; slot++) {
			var (variable, code) = writes[slot];
			ImGui.PushID($"write{slot}");
			ImGui.Bullet();
			ImGui.TextWrapped(Sentence(layer, variable, code));
			ImGui.BeginDisabled(locked);
			ImGui.SetNextItemWidth(ScaledImGui.Scaled(OperationWidth));
			if (EditorText.BeginCombo("##operation", Operation(layer, code).Choice)) {
				foreach (var (offered, operation) in Offered(layer)) {
					if (ImGui.Selectable(operation.Choice, offered == code) && offered != code) {
						request = new SetWriteRequest(slot, Math.Max(variable, (short)0), offered);
					}
				}

				ImGui.EndCombo();
			}

			ImGui.SameLine();
			float remove = EditorText.ButtonRoom("Remove");
			if (_picker.Draw("##variable", variable >= 0 ? variable : null, source, forWrite: true, width: -remove) is { } chosen
					&& chosen != variable) {
				request = new SetWriteRequest(slot, chosen, Kept(layer, code));
			}

			ImGui.SameLine();
			if (ImGui.SmallButton("Remove")) {
				request = new RemoveWriteRequest(slot);
			}

			ImGui.EndDisabled();
			ImGui.PopID();
		}

		if (writes.Count == 0) {
			EditorText.Note("It sets no campaign variable.");
		}

		ImGui.BeginDisabled(locked || whyNoAdd != null);
		if (ImGui.Button("Add")) {
			request = new AddWriteRequest();
		}

		ImGui.EndDisabled();
		EditorText.Reason(whyNoAdd);
		return request;
	}

	/// <summary>The writes of a layer under a heading, read-only, one line each, or a note when there are none.</summary>
	/// <param name="heading">The heading.</param>
	/// <param name="layer">The layer.</param>
	/// <param name="writes">The writes, each (variable, code).</param>
	public static void DrawReadOnly(string heading, WriteLayer layer, IReadOnlyList<(short Variable, short Op)> writes) {
		ImGui.Separator();
		ImGui.TextWrapped($"{heading}:");
		if (!DrawLines(layer, writes)) {
			EditorText.Note("It sets no campaign variable.");
		}
	}

	/// <summary>One line per write that names a variable; whether there was any.</summary>
	/// <param name="layer">The layer.</param>
	/// <param name="writes">The writes, each (variable, code).</param>
	public static bool DrawLines(WriteLayer layer, IReadOnlyList<(short Variable, short Op)> writes) {
		bool any = false;
		foreach (var (variable, code) in writes.Where(write => write.Variable >= 0)) {
			ImGui.Bullet();
			ImGui.TextWrapped(Sentence(layer, variable, code));
			any = true;
		}

		return any;
	}

	/// <summary>The (variable, code) pairs of parallel variable and code lists, as a record carries them.</summary>
	/// <param name="variables">The variables.</param>
	/// <param name="codes">The codes, parallel to the variables.</param>
	public static IReadOnlyList<(short Variable, short Op)> Pairs(IReadOnlyList<short> variables, IReadOnlyList<short> codes) =>
		Enumerable.Range(0, Math.Min(variables.Count, codes.Count)).Select(slot => (variables[slot], codes[slot])).ToList();

	/// <summary>The (variable, code) pairs of a record's interleaved out-of-action words, as the <c>.MSN</c> holds them.</summary>
	/// <param name="words">The interleaved words.</param>
	public static IReadOnlyList<(short Variable, short Op)> Interleaved(IReadOnlyList<short> words) =>
		Enumerable.Range(0, words.Count / 2).Select(pair => (words[pair * 2], words[pair * 2 + 1])).ToList();

	// The code a variable change keeps: the write's own, or Add 1 for one the layer has no case for.
	private static short Kept(WriteLayer layer, short code) =>
		Operation(layer, code).Kind != WriteKind.Unknown
			? code
			: Offered(layer).First(offered => offered.Operation == WriteOperation.AddOne).Code;
}
