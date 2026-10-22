using HercWorks.Core.Data.File;

namespace Herculan.Es1.Messages;

/// <summary>A string of a loaded <c>.STR</c>, by its group and its place in the group: the text pointer a slot holds.</summary>
public readonly record struct Es1StringRef(int Group, int Entry);

/// <summary>
/// An index slot, the 9 bytes <c>SystemMessages_Index</c> fills for one message id: {text, count at <c>+4</c>,
/// attributes at <c>+5</c>} (docs/retail/simulation/cockpit-message-port.md#posting).
/// </summary>
/// <param name="Text">The first string with the id, or none.</param>
/// <param name="Count">How many strings carry the id, the variants a post rolls between.</param>
/// <param name="Attributes">The text's attribute bytes, or none.</param>
public readonly record struct Es1MessageSlot(Es1StringRef? Text, byte Count, byte[]? Attributes);

/// <summary>
/// One of the message boxes' string tables and the index <c>SystemMessages_Index</c> (<c>ES1DBSIM 0002f1ec</c>) fills
/// from it (docs/es1/simulation/cockpit-messages.md#the-string-files): <c>SystemStringTable</c> (<c>000b0e54</c>) with
/// the index <c>000bd0ec</c>, and in plan step 6c the command and pilot tables.
///
/// <para>The index has a slot for every byte id here. Retail's system index holds 34, ids 0 to <c>0x21</c>, the
/// slot <c>MessagePort_StepToVariant</c> builds at <c>000bd21e</c> right after it; an id past them reads the
/// globals beyond. The retail <c>system.str</c>'s ids are 0 to <c>0x21</c>.</para>
/// </summary>
public sealed class Es1MessageTable {
	private readonly Es1MessageSlot[] _index = new Es1MessageSlot[256];
	private StringFile? _file;

	/// <summary><c>StrTable_FirstString</c> (<c>00045114</c>) answering nonzero: a file is loaded.</summary>
	public bool Loaded => _file != null;

	/// <summary>The index slot of message <paramref name="id"/>.</summary>
	public Es1MessageSlot this[byte id] => _index[id];

	/// <summary>The text of <paramref name="text"/>, or none.</summary>
	public string? Text(Es1StringRef? text) => text is { } at ? _file?.Group(at.Group)[at.Entry].Text : null;

	/// <summary>
	/// <c>SystemMessages_Index</c>'s load and walk for this table: <paramref name="file"/>, read through Core, held as
	/// <c>StringTable_LoadFromFile</c> loads it; then each string <c>StringTable_ReadNext</c> (<c>00045004</c>) gives,
	/// which stops at the first group's end, so only group 0 is indexed: the slot of its attribute byte 0 takes the text
	/// and the attributes while its count is 0, and its count goes up by 1. A file that is missing or does not parse
	/// leaves the table as it was.
	///
	/// <para>An entry with no attribute bytes is indexed under id 0 here; retail reads the byte after its count, the next
	/// entry's. No retail entry has none.</para>
	/// </summary>
	public void Index(StringFile? file) {
		if (file == null) {
			return;
		}

		_file = file;
		var group = file.Group(0);
		for (int entry = 0; entry < group.Count; entry++) {
			var attributes = group[entry].Attributes;
			byte id = attributes.Length > 0 ? attributes[0] : (byte)0;
			var slot = _index[id];
			if (slot.Count == 0) {
				slot = slot with { Text = new Es1StringRef(0, entry), Attributes = attributes };
			}

			_index[id] = slot with { Count = (byte)(slot.Count + 1) };
		}
	}

	/// <summary>
	/// <c>MessagePort_StepToVariant</c> (<c>0002f31c</c>)(port, <paramref name="variant"/>, <paramref name="slot"/>,
	/// table): for variant 0 the slot; otherwise <c>StrTable_NextString</c> (<c>0004513c</c>) of the slot's own text,
	/// called once a step but always on that text, so every variant from 1 on is the string after the slot's first; and
	/// the slot <c>000bd21e</c> built from it, a count of 0 and its attributes, none when it has no attribute bytes
	/// (<c>StrTable_GetAttributes</c>, <c>00045194</c>).
	///
	/// <para>Past its group's last string this gives none; retail's walk reads the next group's header as a string, and
	/// past the file's end answers 0.</para>
	/// </summary>
	public Es1MessageSlot StepToVariant(Es1MessageSlot slot, byte variant) {
		if (variant == 0) {
			return slot;
		}

		Es1StringRef? next = slot.Text is { } at && _file != null && at.Entry + 1 < _file.Group(at.Group).Count
			? at with { Entry = at.Entry + 1 }
			: null;
		var attributes = next is { } n ? _file!.Group(n.Group)[n.Entry].Attributes : null;
		return new Es1MessageSlot(next, 0, attributes is { Length: > 0 } ? attributes : null);
	}
}

/// <summary>
/// The message boxes' string tables, which <c>MessagePort_StaticInit</c> (<c>0002fe00</c>) builds empty and the first
/// port built fills (docs/es1/simulation/cockpit-messages.md#building-the-two-ports). In retail they are globals; here
/// the cockpit owns one set.
/// </summary>
public sealed class Es1MessageTables {
	/// <summary><c>SystemStringTable</c> (<c>000b0e54</c>), <c>str\system.str</c>, and its index <c>000bd0ec</c>.</summary>
	public Es1MessageTable System { get; } = new();

	// PLACEHOLDER: CommandStringTable (000b0e44) with its index 000bd0ac and the three PilotStringTables (000b0e14) with
	// theirs (000bcc22), which the pilot channel's posts read and the squadmates' pilot setter 0003dab4 fills: plan
	// step 6c.
}
