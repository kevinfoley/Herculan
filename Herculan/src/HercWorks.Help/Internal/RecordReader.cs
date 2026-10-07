namespace HercWorks.Help.Internal;

/// <summary>
/// Reads one paragraph run (<c>0x20</c>) or table row (<c>0x23</c>) — docs/retail/formats/winhelp.md#paragraph-runs
/// and #tables. The first data part holds the format and the commands; the second holds the text, one
/// NUL-terminated string before each command.
///
/// <para>Only the commands, format bits and picture types the retail files use are accepted. Anything
/// else ends the parse with the byte named, so a file that needs more is turned away rather than drawn
/// half-understood.</para>
/// </summary>
internal sealed class RecordReader(HelpLimits limits, ByteCursor format, ByteCursor text) {
	private const int SupportedBits = 0x0002 | 0x0004 | 0x0008 | 0x0010 | 0x0020 | 0x0040 | 0x0200 | 0x0400 | 0x0800;
	private const int MaxTabStops = 64;
	private const int MaxColumns = 32;

	private ByteCursor _format = format;
	private ByteCursor _text = text;

	/// <summary>The record's text length field, which advances the topic offset.</summary>
	public int TextLength { get; private set; }

	public HelpParagraphRun ReadParagraphRun(uint offset) {
		ReadSizes();
		var paragraphFormat = ReadFormat();
		var inlines = ReadCommands();
		RequireConsumed();
		return new HelpParagraphRun(offset, paragraphFormat, inlines);
	}

	public HelpTableRow ReadTableRow(uint offset) {
		ReadSizes();
		int columnCount = _format.U8();
		int tableType = _format.U8();
		if (columnCount < 1 || columnCount > MaxColumns) {
			throw new MalformedHelpException($"table of {columnCount} columns");
		}

		if (tableType != 1) {
			throw new MalformedHelpException($"table type {tableType} is not supported");
		}

		var columns = new HelpColumn[columnCount];
		for (int i = 0; i < columnCount; i++) {
			int width = _format.I16();
			columns[i] = new HelpColumn(width, _format.I16());
		}

		var cells = new List<HelpCell>();
		while (true) {
			int column = _format.I16();
			if (column == -1) {
				break;
			}

			if (column < 0 || column >= columnCount) {
				throw new MalformedHelpException($"cell in column {column} of {columnCount}");
			}

			_format.Skip(3);
			var cellFormat = ReadFormat();
			cells.Add(new HelpCell(column, cellFormat, ReadCommands()));
		}

		RequireConsumed();
		return new HelpTableRow(offset, columns, cells);
	}

	// Format size, checked against what is actually left, then the text length.
	private void ReadSizes() {
		int formatSize = _format.CompressedSignedLong();
		TextLength = _format.CompressedUnsignedShort();
		if (formatSize != _format.Remaining) {
			throw new MalformedHelpException($"format size {formatSize} where {_format.Remaining} follow");
		}
	}

	private HelpParagraphFormat ReadFormat() {
		_format.CompressedSignedLong();
		_format.Skip(2);
		int bits = _format.U16();
		if ((bits & ~SupportedBits) != 0) {
			throw new MalformedHelpException($"paragraph format bits {bits:x4} are not supported");
		}

		int above = (bits & 0x0002) != 0 ? _format.CompressedSignedShort() : 0;
		int below = (bits & 0x0004) != 0 ? _format.CompressedSignedShort() : 0;
		int lines = (bits & 0x0008) != 0 ? _format.CompressedSignedShort() : 0;
		int left = (bits & 0x0010) != 0 ? _format.CompressedSignedShort() : 0;
		int right = (bits & 0x0020) != 0 ? _format.CompressedSignedShort() : 0;
		int first = (bits & 0x0040) != 0 ? _format.CompressedSignedShort() : 0;
		var tabs = new List<int>();
		if ((bits & 0x0200) != 0) {
			int count = _format.CompressedSignedShort();
			if (count < 0 || count > MaxTabStops) {
				throw new MalformedHelpException($"{count} tab stops");
			}

			for (int i = 0; i < count; i++) {
				int stop = _format.CompressedUnsignedShort();
				if ((stop & 0x4000) != 0) {
					throw new MalformedHelpException("typed tab stops are not supported");
				}

				tabs.Add(stop);
			}
		}

		var alignment = (bits & 0x0800) != 0 ? HelpAlignment.Centre
			: (bits & 0x0400) != 0 ? HelpAlignment.Right : HelpAlignment.Left;
		return new HelpParagraphFormat(above, below, lines, left, right, first, tabs, alignment);
	}

	// One string before each command, the closing 0xFF included; empty strings are dropped.
	private List<HelpInline> ReadCommands() {
		var inlines = new List<HelpInline>();
		while (true) {
			string text = _text.String(limits.MaxStringBytes);
			if (text.Length > 0) {
				inlines.Add(new HelpText(text));
			}

			byte command = _format.U8();
			switch (command) {
				case 0xFF:
					return inlines;
				case 0x80:
					inlines.Add(new HelpFontChange(_format.U16()));
					break;
				case 0x81:
					inlines.Add(new HelpLineBreak());
					break;
				case 0x82:
					inlines.Add(new HelpParagraphEnd());
					break;
				case 0x83:
					inlines.Add(new HelpTab());
					break;
				case 0x86:
					inlines.Add(ReadPicture(HelpPicturePlacement.Inline));
					break;
				case 0x88:
					inlines.Add(ReadPicture(HelpPicturePlacement.Right));
					break;
				case 0x89:
					inlines.Add(new HelpHotspotEnd());
					break;
				case 0xCC: {
					var body = _format.Slice(_format.I16());
					inlines.Add(new HelpHotspotStart(new HelpMacroLink(body.String(limits.MaxStringBytes))));
					break;
				}
				case 0xE7:
					inlines.Add(new HelpHotspotStart(new HelpJump(_format.U32(), null)));
					break;
				case 0xEF: {
					var body = _format.Slice(_format.I16());
					if (body.Remaining != 6 || body.U8() != 1) {
						throw new MalformedHelpException("0xEF jump other than type 1");
					}

					uint hash = body.U32();
					inlines.Add(new HelpHotspotStart(new HelpJump(hash, body.U8())));
					break;
				}
				default:
					throw new MalformedHelpException($"command {command:x2} is not supported");
			}
		}
	}

	private HelpInline ReadPicture(HelpPicturePlacement placement) {
		int type = _format.U8();
		int size = _format.CompressedSignedLong();
		if (type == 0x22) {
			_format.CompressedUnsignedShort();
		}

		if (size < 0) {
			throw new MalformedHelpException($"picture data of {size} bytes");
		}

		var data = _format.Slice(size);
		switch (type) {
			case 0x03:
			case 0x22:
				if (data.Remaining != 4 || data.U16() != 0) {
					throw new MalformedHelpException("picture reference other than a |bm number");
				}

				return new HelpPictureRef(placement, data.U16());
			case 0x05: {
				data.Skip(6);
				string body = data.String(limits.MaxStringBytes);
				int comma = body.IndexOf(',');
				if (!body.StartsWith('!') || comma < 0) {
					throw new MalformedHelpException("embedded window other than a button");
				}

				return new HelpButton(body[1..comma], body[(comma + 1)..]);
			}
			default:
				throw new MalformedHelpException($"picture type {type:x2} is not supported");
		}
	}

	private void RequireConsumed() {
		if (!_format.AtEnd) {
			throw new MalformedHelpException($"{_format.Remaining} bytes after the commands");
		}
	}
}
