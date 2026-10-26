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
/// the index <c>000bd0ec</c>, <c>CommandStringTable</c> (<c>000b0e44</c>) with <c>000bd0ac</c>, or one of the three
/// <c>PilotStringTables</c> (<c>000b0e14</c>) with its index at <c>000bcc22</c> + slot × <c>0x183</c>.
///
/// <para>The index has a slot for every byte id here. Retail's are packed one after another: the system index holds
/// 34, ids 0 to <c>0x21</c>, with the slot <c>MessagePort_StepToVariant</c> builds at <c>000bd21e</c> right after it;
/// each pilot index 43, ids 0 to <c>0x2a</c>, the next slot's index after it; and the command index the 7 from
/// <c>000bd0ac</c> to the system index. An id past a retail index reads or writes the one beyond. The retail
/// <c>system.str</c>'s ids are 0 to <c>0x21</c>.</para>
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
/// The message boxes' string tables, which <c>MessagePort_StaticInit</c> (<c>0002fe00</c>) builds empty and
/// <c>SystemMessages_Index</c> fills (docs/es1/simulation/cockpit-messages.md#the-string-files): the system table when
/// the first port is built, the pilot and command tables as the squadmates' pilots are set. In retail they are
/// globals; here the cockpit owns one set.
/// </summary>
/// <param name="strings">
/// <c>str\&lt;name&gt;.str</c> by its name, the file <c>SystemMessages_Index</c> loads, read through Core; null when it is
/// missing or does not parse. None loads nothing.
/// </param>
public sealed class Es1MessageTables(Func<string, StringFile?>? strings = null) {
	/// <summary>The pilot tables, one for each squadmate slot.</summary>
	public const int PilotSlots = 3;

	/// <summary><c>SystemStringTable</c> (<c>000b0e54</c>), <c>str\system.str</c>, and its index <c>000bd0ec</c>.</summary>
	public Es1MessageTable System { get; } = new();

	/// <summary><c>CommandStringTable</c> (<c>000b0e44</c>), <c>str\commandr.str</c>, and its index <c>000bd0ac</c>.</summary>
	public Es1MessageTable Command { get; } = new();

	/// <summary>
	/// <c>PilotStringTables</c> (<c>000b0e14</c>), <c>0x10</c> bytes apart, each squadmate slot's <c>str\pilot&lt;v&gt;.str</c>,
	/// and their indexes from <c>PilotMessageIndex</c> (<c>000bcc22</c>), <c>0x183</c> bytes apart.
	/// </summary>
	public IReadOnlyList<Es1MessageTable> Pilots { get; } = [new(), new(), new()];

	/// <summary><c>ResourcePath_BuildFolderName</c>(<c>str</c>, <paramref name="name"/>) and <c>StringTable_LoadFromFile</c>: the file, or null.</summary>
	public StringFile? Load(string name) => strings?.Invoke(name);
}
