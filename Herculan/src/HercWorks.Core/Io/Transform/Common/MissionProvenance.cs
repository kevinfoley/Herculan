namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Which <c>.MSN</c> and <c>.ENG</c> records supplied what a <see cref="MissionGenerator"/> writes: for
/// every exported <c>script.dat</c> record, the record that created its slot, the records merged into it
/// and the templates copied into them, down to the word; for every <c>mission.str</c> line, the
/// <c>.ENG</c> record whose text it holds. The generator reports to it as it adds, replaces, overlays and
/// copies, and it changes nothing the generator writes.
///
/// <para>A record is named by its row and its index in that row of <see cref="Data.File.Msn.MissionFile"/>,
/// which is the generator's read-loop counter, so a record that fails its condition still takes an
/// index. The merge rules are docs/retail/formats/msn-mission-file.md#repeated-guids, the random table rules
/// docs/retail/formats/msn-mission-file.md#random-tables, and the blocks
/// docs/retail/formats/script-dat.md#the-13-block-structure: blocks 1 to 12 from rows 6 to 17, block 13
/// from row 4.</para>
/// </summary>
public sealed class MissionProvenance {
	/// <summary>Every slot the generator keeps, per <c>.MSN</c> row, in its order, exported or not.</summary>
	private readonly Dictionary<int, List<SlotProvenance>> _slots = new();

	/// <summary>The exported slots per block, in <c>script.dat</c> order.</summary>
	private readonly Dictionary<int, List<SlotProvenance>> _blocks = new();

	private readonly Dictionary<MsnRecordId, SlotProvenance> _slotOf = new();
	private readonly List<TextLineProvenance> _lines = new();
	private readonly Dictionary<int, int> _lineOf = new();

	/// <summary>The template copies made into the record being read, waiting for its add or merge.</summary>
	private readonly List<(int Source, int First, int Count)> _pendingCopies = new();

	/// <summary>The slot a merge is writing into, and what each word of the incoming record carries.</summary>
	private SlotProvenance? _mergeTarget;
	private WordSource[] _mergeWords = [];

	private SlotProvenance? _textPackage;

	/// <summary>The row-2 records whose condition passed, in read order, and the one each header word holds the write of.</summary>
	private readonly List<MsnRecordId> _headerPatches = new();
	private readonly Dictionary<int, MsnRecordId> _headerWords = new();

	/// <summary>The row-2 records whose condition passed, which patched the header, in read order.</summary>
	public IReadOnlyList<MsnRecordId> HeaderPatches => _headerPatches;

	/// <summary>
	/// The row-2 record whose write a header word (<c>script.dat</c> offset <c>word * 2</c>) holds:
	/// the last of <see cref="HeaderPatches"/> that sets it, or null when none does and the word keeps its unset value. A
	/// launch that writes over the word after the load (<see cref="Data.File.Msn.MissionGeneration"/>) is not one.
	/// </summary>
	/// <param name="word">The header word's index.</param>
	public MsnRecordId? HeaderWord(int word) => _headerWords.TryGetValue(word, out var record) ? record : null;

	/// <summary>
	/// The exported record at an index of a <c>script.dat</c> block
	/// (1 to 13), or null past the block's end. Block 13 is one record, row 4's slot 0, at index 0.
	/// </summary>
	/// <param name="block">The <c>script.dat</c> block, 1 to 13.</param>
	/// <param name="index">The record's index in the block.</param>
	public SlotProvenance? Slot(int block, int index) {
		var slots = Slots(block);
		return index >= 0 && index < slots.Count ? slots[index] : null;
	}

	/// <summary>A block's exported records in <c>script.dat</c> order.</summary>
	/// <param name="block">The <c>script.dat</c> block.</param>
	public IReadOnlyList<SlotProvenance> Slots(int block) {
		if (block is < 1 or > 13) {
			throw new ArgumentOutOfRangeException(nameof(block), block, "script.dat has blocks 1 to 13.");
		}

		if (block == 13) {
			return _textPackage == null ? [] : [_textPackage];
		}

		return _blocks.TryGetValue(block, out var slots) ? slots : [];
	}

	/// <summary>
	/// The exported record a record created or was merged into, or null when it reaches no
	/// <c>script.dat</c> record: its condition failed, its GUID is <c>-1</c>, its row is not exported, or
	/// it is a row-4 record other than the one block 13 is written from. A template reaches the
	/// records that copy from it only through <see cref="SlotProvenance.Word"/>.
	/// </summary>
	/// <param name="record">The record.</param>
	public ScriptDatSlot? ExportedSlot(MsnRecordId record) {
		if (record.Row == 4) {
			return _textPackage?.Created == record ? new ScriptDatSlot(13, 0) : null;
		}

		return _slotOf.TryGetValue(record, out var slot) && slot.Index >= 0 ? new ScriptDatSlot(slot.Block, slot.Index) : null;
	}

	/// <summary>The <c>mission.str</c> lines in written order.</summary>
	public IReadOnlyList<TextLineProvenance> Lines => _lines;

	/// <summary>The <c>mission.str</c> line an <c>.ENG</c> record placed or replaced the text of, or null for none.</summary>
	/// <param name="engRecord">The <c>.ENG</c> record's index.</param>
	public int? LineOf(int engRecord) => _lineOf.TryGetValue(engRecord, out int line) ? line : null;

	// ---- What the generator reports ------------------------------------------------------------

	/// <summary>
	/// A template copy of some words from a slot of the row
	/// being read, into the record about to be added or merged.
	/// </summary>
	/// <param name="source">The slot of the row being read.</param>
	/// <param name="first">The first word copied.</param>
	/// <param name="count">How many words are copied.</param>
	internal void CopiedFromTemplate(int source, int first, int count) => _pendingCopies.Add((source, first, count));

	/// <summary>A record of a row added as a new slot.</summary>
	/// <param name="row">The row.</param>
	/// <param name="index">The record's index in the row.</param>
	/// <param name="words">The record's words.</param>
	internal void Added(int row, int index, short[] words) {
		var record = new MsnRecordId(row, index);
		var slots = SlotsOfRow(row);
		int block = BlockOf(row);
		int exported = -1;
		if (block != 0 && (row == 17 || words[Data.File.Msn.MapObject.GUIDWord] != -1)) {
			if (!_blocks.TryGetValue(block, out var blockSlots)) {
				_blocks[block] = blockSlots = new();
			}

			exported = blockSlots.Count;
		}

		var slot = new SlotProvenance(block, exported, record, Incoming(row, record, words.Length, out var copies));
		slot.AddCopies(copies);
		slots.Add(slot);
		if (exported >= 0) {
			_blocks[block].Add(slot);
		}

		_slotOf[record] = slot;
	}

	/// <summary>A record of a row about to be merged into a slot; <see cref="Wrote"/> follows for each word it writes.</summary>
	/// <param name="row">The row.</param>
	/// <param name="slot">The slot it is merged into.</param>
	/// <param name="index">The record's index in the row.</param>
	internal void Merging(int row, int slot, int index) {
		var record = new MsnRecordId(row, index);
		_mergeTarget = SlotsOfRow(row)[slot];
		_mergeWords = Incoming(row, record, _mergeTarget.WordCount, out var copies);
		_mergeTarget.AddMerge(record, copies);
		_slotOf[record] = _mergeTarget;
	}

	/// <summary>The merge under way wrote some words into its slot.</summary>
	/// <param name="first">The first word written.</param>
	/// <param name="count">How many words were written.</param>
	internal void Wrote(int first, int count) {
		for (int word = first; word < first + count; word++) {
			_mergeTarget!.SetWord(word, _mergeWords[word]);
		}
	}

	/// <summary>A row-4 record read into slot 0, the slot block 13 is written from.</summary>
	/// <param name="index">The record's index in row 4.</param>
	/// <param name="wordCount">The record's length in words.</param>
	internal void TextPackage(int index, int wordCount) {
		var record = new MsnRecordId(4, index);
		_textPackage = new SlotProvenance(13, 0, record, Own(record, wordCount));
	}

	/// <summary>A row-2 record passed its condition and patches the header.</summary>
	/// <param name="index">The record's index in row 2.</param>
	internal void HeaderPatched(int index) => _headerPatches.Add(new MsnRecordId(2, index));

	/// <summary>A row-2 record wrote a header word.</summary>
	/// <param name="word">The header word's index.</param>
	/// <param name="index">The record's index in row 2.</param>
	internal void HeaderWordWritten(int word, int index) => _headerWords[word] = new MsnRecordId(2, index);

	/// <summary>An <c>.ENG</c> record added as a new <c>mission.str</c> line.</summary>
	/// <param name="engRecord">The <c>.ENG</c> record's index.</param>
	internal void LineAdded(int engRecord) {
		_lineOf[engRecord] = _lines.Count;
		_lines.Add(new TextLineProvenance(_lines.Count, engRecord, engRecord));
	}

	/// <summary>An <c>.ENG</c> record replaced the text of a line.</summary>
	/// <param name="line">The <c>mission.str</c> line.</param>
	/// <param name="engRecord">The <c>.ENG</c> record's index.</param>
	internal void LineReplaced(int line, int engRecord) {
		_lineOf[engRecord] = line;
		_lines[line] = _lines[line] with { Supplier = engRecord };
	}

	/// <summary>
	/// What each word of an incoming record carries: the record itself, except where a pending template
	/// copy wrote it, which carries the record and the source's own origin for the word.
	/// </summary>
	private WordSource[] Incoming(int row, MsnRecordId record, int wordCount, out List<TemplateCopy> copies) {
		var words = Own(record, wordCount);
		copies = new List<TemplateCopy>();
		var slots = SlotsOfRow(row);
		foreach (var (source, first, count) in _pendingCopies) {
			var from = slots[source];
			var covered = new int[count];
			for (int i = 0; i < count; i++) {
				var origin = from.WordAt(first + i);
				words[first + i] = new WordSource(record, origin.CopiedFrom ?? origin.Record);
				covered[i] = first + i;
			}

			copies.Add(new TemplateCopy(record, from.Created, covered));
		}

		_pendingCopies.Clear();
		return words;
	}

	private static WordSource[] Own(MsnRecordId record, int wordCount) {
		var words = new WordSource[wordCount];
		Array.Fill(words, new WordSource(record, null));
		return words;
	}

	private List<SlotProvenance> SlotsOfRow(int row) {
		if (!_slots.TryGetValue(row, out var slots)) {
			_slots[row] = slots = new();
		}

		return slots;
	}

	/// <summary>The block a row's slots are written to, or 0 for a row <c>script.dat</c> does not carry.</summary>
	private static int BlockOf(int row) => row is >= 6 and <= 17 ? row - 5 : 0;
}

/// <summary>A record of an <c>.MSN</c>: its row (1 to 17) and its index in that row of <see cref="Data.File.Msn.MissionFile"/>.</summary>
public readonly record struct MsnRecordId(int Row, int Index);

/// <summary>A record of <c>script.dat</c>: its block (1 to 13) and its index among the block's records.</summary>
public readonly record struct ScriptDatSlot(int Block, int Index);

/// <summary>
/// Where one word of an exported record came from.
/// </summary>
/// <param name="Record">The record whose write the
/// slot holds: the slot's creator, or the merged record that last wrote the word.</param>
/// <param name="CopiedFrom">Set when that record took the word from a template, and names the source record the value was
/// authored in.</param>
public readonly record struct WordSource(MsnRecordId Record, MsnRecordId? CopiedFrom);

/// <summary>
/// A template copy made into a record before it was added or merged.
/// </summary>
/// <param name="Record">The record the copy was made into.</param>
/// <param name="Source">The record of the same row it copied from.</param>
/// <param name="Words">The <c>.MSN</c> words it covered.</param>
public sealed record TemplateCopy(MsnRecordId Record, MsnRecordId Source, IReadOnlyList<int> Words);

/// <summary>
/// A <c>mission.str</c> line.
/// </summary>
/// <param name="Line">The line's index.</param>
/// <param name="Created">The <c>.ENG</c> record (its index in the file)
/// that placed the line.</param>
/// <param name="Supplier">The <c>.ENG</c> record whose text the line holds — a later record of
/// the same id replaces the text and keeps the line.</param>
public sealed record TextLineProvenance(int Line, int Created, int Supplier);

/// <summary>
/// The provenance of one exported <c>script.dat</c> record. Words are the <c>.MSN</c> record's (the
/// model classes' <c>…Word</c> constants), not the <c>script.dat</c> layout's. A row-6 point whose sum
/// flag is set reports its coordinates as its own record's, though they are computed from the two points
/// it names.
/// </summary>
public sealed class SlotProvenance {
	/// <summary>Row 8's word 4, which stands for its waypoint count and the list after it.</summary>
	private const int RouteListWord = 4;

	private readonly WordSource[] _words;
	private readonly List<MsnRecordId> _merged = new();
	private readonly List<TemplateCopy> _copies = new();

	internal SlotProvenance(int block, int index, MsnRecordId created, WordSource[] words) {
		Block = block;
		Index = index;
		Created = created;
		_words = words;
	}

	/// <summary>The <c>script.dat</c> block.</summary>
	public int Block { get; }

	/// <summary>The index among the block's records.</summary>
	public int Index { get; }

	/// <summary>The record that created the slot, the first survivor of its GUID.</summary>
	public MsnRecordId Created { get; }

	/// <summary>
	/// The later survivors of the GUID merged into the slot, in read order: each replaced it whole in rows 3
	/// and 6 to 9, and overlaid the words it sets in rows 10 to 16.
	/// </summary>
	public IReadOnlyList<MsnRecordId> Merged => _merged;

	/// <summary>Every template copy made into the creator or a merged record, in read order; <see cref="Word"/> says which still hold.</summary>
	public IReadOnlyList<TemplateCopy> TemplateCopies => _copies;

	/// <summary>
	/// Where a <c>.MSN</c> word of the record came from. For row 8, word 4 and every
	/// word after it, its waypoint count and list, are one entry.
	/// </summary>
	/// <param name="word">The word's index.</param>
	public WordSource Word(int word) {
		if (Created.Row == 8) {
			word = Math.Min(word, RouteListWord);
		}

		if (word < 0 || word >= _words.Length) {
			throw new ArgumentOutOfRangeException(nameof(word), word, $"A row-{Created.Row} record has {_words.Length} words.");
		}

		return _words[word];
	}

	/// <summary>Every record whose write some word of the slot holds (<see cref="WordSource.Record"/>), in no particular order.</summary>
	public IEnumerable<MsnRecordId> Suppliers => _words.Select(word => word.Record).Distinct();

	internal int WordCount => _words.Length;

	internal WordSource WordAt(int word) => _words[word];

	internal void SetWord(int word, WordSource source) => _words[word] = source;

	internal void AddCopies(IEnumerable<TemplateCopy> copies) => _copies.AddRange(copies);

	internal void AddMerge(MsnRecordId record, IEnumerable<TemplateCopy> copies) {
		_merged.Add(record);
		_copies.AddRange(copies);
	}
}
