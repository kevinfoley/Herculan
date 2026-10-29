using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

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

	/// <summary>The write to <paramref name="variable"/>, as a sentence names the variable, as a sentence.</summary>
	public string Sentence(string variable) => Kind switch {
		WriteKind.Add => $"Add {Value} to {variable}",
		WriteKind.Subtract => $"Subtract {Value} from {variable}",
		WriteKind.Set => $"Set {variable} to {Value}",
		_ => $"Unknown operation on {variable} (no effect in play)",
	};
}

/// <summary>A change a <see cref="CampaignVariableWrites"/> list asks for, which its view makes through its own editor.</summary>
internal abstract record WriteRequest;

/// <summary>Write <paramref name="Slot"/> to be campaign variable <paramref name="Variable"/> and the layer's code <paramref name="Op"/>.</summary>
internal sealed record SetWriteRequest(int Slot, short Variable, short Op) : WriteRequest;

/// <summary>A write after the last.</summary>
internal sealed record AddWriteRequest : WriteRequest;

/// <summary>Write <paramref name="Slot"/> to be removed.</summary>
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
	/// The codes <paramref name="layer"/> has a case for, each with its operation, in the order a choice offers them. The action
	/// and objective codes are those their edits write (<see cref="ActionEditing"/>, <see cref="ObjectiveEditing"/>).
	/// </summary>
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

	/// <summary>What <paramref name="layer"/>'s code <paramref name="code"/> does; <see cref="WriteOperation.Unknown"/> for one it has no case for.</summary>
	public static WriteOperation Operation(WriteLayer layer, short code) {
		foreach (var (offered, operation) in Offered(layer)) {
			if (offered == code) {
				return operation;
			}
		}

		return WriteOperation.Unknown;
	}

	/// <summary>A write of <paramref name="layer"/>'s code <paramref name="code"/> to <paramref name="variable"/>, as a sentence; <c>-1</c> for none chosen.</summary>
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
	/// The writes <paramref name="writes"/> of <paramref name="layer"/>, each (variable, code), under <paramref name="heading"/>:
	/// each a sentence, an operation choice and a variable choice with Remove, then Add, disabled with
	/// <paramref name="whyNoAdd"/> under it when one cannot be added; every control disabled when <paramref name="locked"/>.
	/// Answers the change asked for this frame, or null.
	/// </summary>
	/// <param name="source">The mission, whose uses of each variable the variable choice lists.</param>
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

	/// <summary>The writes <paramref name="writes"/> of <paramref name="layer"/> under <paramref name="heading"/>, read-only, one line each, or a note when there are none.</summary>
	public static void DrawReadOnly(string heading, WriteLayer layer, IReadOnlyList<(short Variable, short Op)> writes) {
		ImGui.Separator();
		ImGui.TextWrapped($"{heading}:");
		if (!DrawLines(layer, writes)) {
			EditorText.Note("It sets no campaign variable.");
		}
	}

	/// <summary>One line per write of <paramref name="writes"/> that names a variable; whether there was any.</summary>
	public static bool DrawLines(WriteLayer layer, IReadOnlyList<(short Variable, short Op)> writes) {
		bool any = false;
		foreach (var (variable, code) in writes.Where(write => write.Variable >= 0)) {
			ImGui.Bullet();
			ImGui.TextWrapped(Sentence(layer, variable, code));
			any = true;
		}

		return any;
	}

	/// <summary>The (variable, code) pairs of parallel <paramref name="variables"/> and <paramref name="codes"/>, as a record carries them.</summary>
	public static IReadOnlyList<(short Variable, short Op)> Pairs(IReadOnlyList<short> variables, IReadOnlyList<short> codes) =>
		Enumerable.Range(0, Math.Min(variables.Count, codes.Count)).Select(slot => (variables[slot], codes[slot])).ToList();

	/// <summary>The (variable, code) pairs of a record's interleaved out-of-action <paramref name="words"/>, as the <c>.MSN</c> holds them.</summary>
	public static IReadOnlyList<(short Variable, short Op)> Interleaved(IReadOnlyList<short> words) =>
		Enumerable.Range(0, words.Count / 2).Select(pair => (words[pair * 2], words[pair * 2 + 1])).ToList();

	// The code a variable change keeps: the write's own, or Add 1 for one the layer has no case for.
	private static short Kept(WriteLayer layer, short code) =>
		Operation(layer, code).Kind != WriteKind.Unknown
			? code
			: Offered(layer).First(offered => offered.Operation == WriteOperation.AddOne).Code;
}
