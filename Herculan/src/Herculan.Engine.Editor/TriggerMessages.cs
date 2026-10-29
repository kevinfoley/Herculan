using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Content;

namespace Herculan.Engine.Editor;

/// <summary>
/// The lines a trigger's message picks from: the <c>str\COMMAND&lt;n&gt;.STR</c> the mission speaks from, <c>n</c> the training
/// mission number its header's row-2 patches set and no launch writes over
/// (docs/retail/formats/script-dat.md#the-training-mission-number), loaded once per number. Who shows a line, and how a
/// training mission's sentences join into one instruction, is docs/retail/simulation/cockpit-messages.md#its-speakerless-set
/// and #the-training-port.
/// </summary>
internal sealed class TriggerMessages {
	/// <summary>The header word holding the training mission number (<c>script.dat</c> offset 8).</summary>
	private const int TrainingWord = 4;

	private readonly GameContent _content;
	private readonly Dictionary<int, SquadMessages?> _loaded = new();

	public TriggerMessages(GameContent content) => _content = content;

	/// <summary>The training mission number <paramref name="layout"/>'s header sets, 0 for an ordinary mission.</summary>
	public static int TrainingNumber(MissionLayout layout) => layout.Header?.Words[TrainingWord] ?? 0;

	/// <summary>The file the mission of <paramref name="training"/> speaks from: <c>COMMAND0.STR</c> for an ordinary mission.</summary>
	public static string FileName(int training) => SquadMessages.CommandResourceName(training);

	/// <summary>The lines of training mission <paramref name="training"/>'s file, or null when the install has none.</summary>
	public SquadMessages? For(int training) {
		if (!_loaded.TryGetValue(training, out var messages)) {
			_loaded[training] = messages = SquadMessages.LoadCommand(_content, training);
		}

		return messages;
	}

	/// <summary>Message <paramref name="id"/>'s text, an instruction's sentences joined; null when the file has no such line.</summary>
	public string? Text(int training, int id) {
		var entries = For(training)?.Instruction(id);
		return entries is { Count: > 0 } ? string.Join(" ", entries.Select(entry => entry.Text)) : null;
	}
}
