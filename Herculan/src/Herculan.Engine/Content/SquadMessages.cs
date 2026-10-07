using HercWorks.Core.Data.File;
namespace Herculan.Engine.Content;

/// <summary>
/// <c>str\PILOT&lt;bank&gt;.STR</c> — what a squadmate can say. One of these per recorded voice, and
/// a comm box takes the one its pilot's voice bank names. See docs/retail/formats/cockpit-messages.md, "The
/// pilot and squad channel".
///
/// <para>Layout is an ordinary <c>.STR</c> table of one group. Each entry's attribute bytes are the
/// same eight <c>MessagePort_Enqueue</c> (<c>00434e8c</c>) reads for the cockpit computer, minus the
/// last: byte 0 is the message id, byte 1 the variant digit its <c>.WAV</c> and <c>.SNC</c> filenames
/// carry, byte 2 the queue priority and bytes 3-6 the four timings. There is no clip number, because
/// the pilot channel builds its filename from the id and the variant instead.</para>
///
/// <para><b>The id is the key, not the position.</b> <c>SystemMessages_Index</c> (<c>00435970</c>)
/// scatters the strings into a table at <c>base + attr[0] * 9</c> and counts how many land in each
/// slot, so entries sharing an id become variants of one message and the post rolls between them.
/// Unlike <c>SYSTEM.STR</c>, the pilot files really do use this: ids 2, 3, 21, 30 and 31 have two to
/// four recordings each, which is why the same squadmate does not answer a repeated order in the same
/// words.</para>
/// </summary>
public sealed class SquadMessages {
	/// <summary>Highest message id the files use, and the largest the portrait scripts are numbered to.</summary>
	public const int MaxMessageId = 42;

	private readonly Dictionary<int, List<Entry>> _byId = new();
	private readonly List<Entry> _ordered;
	private readonly Dictionary<int, int> _firstIndex;

	private SquadMessages(Dictionary<int, List<Entry>> byId, List<Entry> ordered, Dictionary<int, int> firstIndex) {
		_byId = byId;
		_ordered = ordered;
		_firstIndex = firstIndex;
	}

	/// <summary>One recording of one message.</summary>
	/// <param name="Id">Attribute byte 0 — what a poster names.</param>
	/// <param name="Variant">Attribute byte 1 — the last digit of the <c>.WAV</c> and <c>.SNC</c> names.</param>
	/// <param name="Text">The line, for the pilot channel's own text box.</param>
	/// <param name="Attributes">The raw bytes, read through the <see cref="SystemMessages"/> indices.</param>
	public readonly record struct Entry(int Id, int Variant, string Text, byte[] Attributes);

	/// <summary>How many distinct message ids the file carried.</summary>
	public int Count => _byId.Count;

	/// <summary>Every recording of <paramref name="id"/>, or an empty list when it has none.</summary>
	public IReadOnlyList<Entry> Variants(int id) =>
		_byId.TryGetValue(id, out var entries) ? entries : Array.Empty<Entry>();

	/// <summary>
	/// One recording of <paramref name="id"/>, rolled among its variants the way
	/// <c>PilotMessagePort_Post</c> (<c>00435c48</c>) and <c>MessagePort_PickVariant</c>
	/// (<c>00436a3c</c>) do, or null when the file has no such id. Both take the full sixteen bits
	/// unsigned, <c>(next &amp; 0xffff) % count</c>, rather than <see cref="Numerics.SimRandom.NextBelow"/>'s
	/// fifteen, and draw only when there is more than one variant.
	/// </summary>
	public Entry? Pick(int id, Numerics.SimRandom? random) {
		var entries = Variants(id);
		if (entries.Count == 0) {
			return null;
		}

		return entries.Count == 1 || random == null
			? entries[0]
			: entries[random.NextMasked(0xffff) % entries.Count];
	}

	/// <summary>
	/// A training instruction: the first entry carrying <paramref name="id"/> and, after it, as many
	/// more entries <b>in file order</b> as the id has — <c>PilotMessagePort_WrapText</c>
	/// (<c>00436318</c>) takes the count from the id's table slot and steps with
	/// <c>StrTable_NextString</c> (<c>004539cc</c>), which returns the string after the one it is given in the loaded file,
	/// whatever that string's id. Retail files keep an id's entries together, so the two readings
	/// agree. Empty when the file has no such id.
	/// </summary>
	public IReadOnlyList<Entry> Instruction(int id) {
		if (!_firstIndex.TryGetValue(id, out int first)) {
			return Array.Empty<Entry>();
		}

		int count = Math.Min(Variants(id).Count, _ordered.Count - first);
		return _ordered.GetRange(first, count);
	}

	/// <summary>The resource name for one voice bank — <c>pilot1</c>, <c>pilot2</c>, <c>pilot4</c>.</summary>
	public static string ResourceName(int voiceBank) => $"PILOT{voiceBank}.STR";

	/// <summary>
	/// The resource name for the channel's speakerless set — <c>commandX</c> with the training mission
	/// number as the digit, so <c>COMMAND0.STR</c> for any mission that is not a training one.
	/// </summary>
	public static string CommandResourceName(int trainingMission) => $"COMMAND{trainingMission}.STR";

	/// <summary>
	/// Reads one voice bank's message set out of the mounted archives, or null when the file is absent
	/// or does not parse.
	/// </summary>
	public static SquadMessages? Load(GameContent content, int voiceBank) =>
		SimStrings.Load(content, ResourceName(voiceBank)) is { } table ? FromTable(table) : null;

	/// <summary>
	/// Reads the speakerless set — what <c>Gau_BuildCockpitWidgets</c> (<c>00431bf8</c>) has
	/// <c>SystemMessages_Index</c> scatter into the table at <c>004d0971</c>, right after building the
	/// port. The shape is a pilot bank's, with an eighth attribute byte.
	/// </summary>
	public static SquadMessages? LoadCommand(GameContent content, int trainingMission) =>
		SimStrings.Load(content, CommandResourceName(trainingMission)) is { } table ? FromTable(table) : null;

	/// <summary>Scatters an already-parsed <c>.STR</c> by attribute byte 0.</summary>
	public static SquadMessages FromTable(StringFile table) {
		var byId = new Dictionary<int, List<Entry>>();
		var ordered = new List<Entry>();
		var firstIndex = new Dictionary<int, int>();

		for (int group = 0; group < table.GroupCount; group++) {
			foreach (var entry in table.Group(group)) {
				var attributes = entry.Attributes;
				if (attributes.Length <= SystemMessages.IdAttribute) {
					continue;
				}

				int id = attributes[SystemMessages.IdAttribute];
				int variant = attributes.Length > VariantAttribute ? attributes[VariantAttribute] : 0;

				if (!byId.TryGetValue(id, out var entries)) {
					byId[id] = entries = new List<Entry>();
					firstIndex[id] = ordered.Count;
				}

				var parsed = new Entry(id, variant, entry.Text, attributes);
				entries.Add(parsed);
				ordered.Add(parsed);
			}
		}

		return new SquadMessages(byId, ordered, firstIndex);
	}

	/// <summary>
	/// Attribute byte 1 — the digit the pilot channel patches into its <c>P&lt;bank&gt;_nnnnn.WAV</c>
	/// and <c>P&lt;letter&gt;_nnnnn.SNC</c> filenames. Zero throughout <c>SYSTEM.STR</c>, which is why
	/// the computer's channel never uses it.
	/// </summary>
	public const int VariantAttribute = 1;
}
