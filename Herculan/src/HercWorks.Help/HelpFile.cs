using HercWorks.Help.Internal;

namespace HercWorks.Help;

/// <summary>
/// A window definition from <c>|SYSTEM</c> (docs/retail/formats/winhelp.md#system). Colours are
/// <c>0xRRGGBB</c>, null when the definition leaves them unset.
/// </summary>
public sealed record HelpWindow(
	string Type, string Name, string Caption, int X, int Y, int Width, int Height,
	int? Background, int? NonScrollingBackground);

/// <summary>A font descriptor from <c>|FONT</c> (docs/retail/formats/winhelp.md#font). <paramref name="Colour"/> is <c>0xRRGGBB</c>.</summary>
public sealed record HelpFont(byte Attributes, int HalfPoints, byte Family, string Face, int Colour) {
	public bool Bold => (Attributes & 0x01) != 0;

	public bool Italic => (Attributes & 0x02) != 0;

	public bool Underline => (Attributes & 0x04) != 0;

	public bool SmallCaps => (Attributes & 0x20) != 0;
}

/// <summary>An index keyword and the topic offsets it names, in <c>|KWDATA</c> order.</summary>
public sealed record HelpKeyword(string Keyword, IReadOnlyList<uint> Targets);

/// <summary>
/// A parsed <c>ES2GUIDE.HLP</c>: everything the on-line manual needs except the pictures, which
/// <see cref="TryGetPicture"/> decodes on request.
///
/// <para>This reads the subset of WinHelp the three retail files use, described in
/// docs/retail/formats/winhelp.md. A file that uses anything outside it — LZ77 or phrase compression, a
/// command or picture type not in that doc, a 4-byte compressed signed long — is rejected with a
/// reason rather than read on a guess. The format notes list what the rest of WinHelp would add
/// (docs/retail/formats/winhelp.md#not-in-the-corpus).</para>
///
/// <para>Parsing is total: a malformed file yields null and a reason, never an exception. Nothing in
/// this assembly opens a file, resolves a path or starts a process; it is handed bytes. The topic
/// walk follows links forward only, the tree walks refuse to revisit a page, and every count and
/// size is checked against <see cref="HelpLimits"/> and against the bytes actually present.</para>
/// </summary>
public sealed class HelpFile {
	private const uint FileMagic = 0x00035F3F;
	private const ushort SystemMagic = 0x036C;
	private const int TopicBlockSize = 4096;
	private const int TopicBlockHeader = 12;
	private const int TopicLinkHeader = 21;
	private const int WindowRecordSize = 90;

	private readonly byte[] _bytes;
	private readonly Dictionary<string, (int Start, int End)> _files;
	private readonly HelpLimits _limits;

	private HelpFile(byte[] bytes, Dictionary<string, (int Start, int End)> files, HelpLimits limits) {
		_bytes = bytes;
		_files = files;
		_limits = limits;
	}

	/// <summary><c>|SYSTEM</c> type 1.</summary>
	public string Title { get; private set; } = "";

	/// <summary><c>|SYSTEM</c> type 2.</summary>
	public string Copyright { get; private set; } = "";

	/// <summary><c>|SYSTEM</c> type 3: the topic offset of the contents topic, which <c>HELP_CONTENTS</c> opens.</summary>
	public uint ContentsOffset { get; private set; }

	/// <summary><c>|SYSTEM</c> type 4 records, in order.</summary>
	public IReadOnlyList<string> StartupMacros { get; private set; } = [];

	/// <summary><c>|SYSTEM</c> type 6 records, in order: a jump's window number indexes this.</summary>
	public IReadOnlyList<HelpWindow> Windows { get; private set; } = [];

	/// <summary><c>|CF</c><i>n</i>: the macros run when window <i>n</i> opens, by window index.</summary>
	public IReadOnlyDictionary<int, IReadOnlyList<string>> WindowMacros { get; private set; } =
		new Dictionary<int, IReadOnlyList<string>>();

	/// <summary>The <c>|FONT</c> descriptors a font change indexes.</summary>
	public IReadOnlyList<HelpFont> Fonts { get; private set; } = [];

	/// <summary>Every topic, in chain order; <see cref="HelpTopic.Number"/> indexes this.</summary>
	public IReadOnlyList<HelpTopic> Topics { get; private set; } = [];

	/// <summary><c>|CONTEXT</c>: context hash to topic offset.</summary>
	public IReadOnlyDictionary<uint, uint> Contexts { get; private set; } = new Dictionary<uint, uint>();

	/// <summary><c>|KWBTREE</c> with its <c>|KWDATA</c> targets, in keyword order.</summary>
	public IReadOnlyList<HelpKeyword> Keywords { get; private set; } = [];

	/// <summary>
	/// Parses <paramref name="bytes"/>. Returns null, with <paramref name="error"/> saying why, when the
	/// file is malformed or uses something outside the supported subset.
	/// </summary>
	public static HelpFile? Parse(byte[] bytes, out string? error, HelpLimits? limits = null) {
		ArgumentNullException.ThrowIfNull(bytes);
		limits ??= HelpLimits.Default;
		error = null;
		if (bytes.Length > limits.MaxFileBytes) {
			error = $"{bytes.Length} bytes is over the {limits.MaxFileBytes}-byte limit";
			return null;
		}

		try {
			var help = new HelpFile(bytes, ReadDirectory(bytes, limits), limits);
			help.ReadSystem();
			help.RejectUnsupportedFiles();
			help.ReadFonts();
			help.ReadContexts();
			help.ReadKeywords();
			help.ReadWindowMacros();
			help.Topics = new TopicReader(help).Read();
			return help;
		} catch (MalformedHelpException e) {
			error = e.Message;
			return null;
		}
	}

	/// <summary>
	/// The topic containing <paramref name="offset"/>, and the index of the block holding that
	/// character; null when the offset lies before the first topic. Keyword targets land inside
	/// topics, often mid-record, so the block is the last one starting at or before the offset.
	/// </summary>
	public (HelpTopic Topic, int Block)? Locate(uint offset) {
		HelpTopic? found = null;
		foreach (var topic in Topics) {
			if (topic.Offset > offset) {
				break;
			}

			found = topic;
		}

		if (found == null) {
			return null;
		}

		int block = 0;
		for (int i = 0; i < found.Blocks.Count && found.Blocks[i].Offset <= offset; i++) {
			block = i;
		}

		return (found, block);
	}

	/// <summary>The topic and block a context string's hash names; null when <c>|CONTEXT</c> has no such hash.</summary>
	public (HelpTopic Topic, int Block)? LocateContext(uint hash) =>
		Contexts.TryGetValue(hash, out uint offset) ? Locate(offset) : null;

	/// <summary>
	/// Decodes <c>|bm</c><paramref name="number"/>. Returns null, with <paramref name="error"/> saying
	/// why, when there is no such picture or it is malformed or of a kind the corpus does not use.
	/// </summary>
	public HelpPicture? TryGetPicture(int number, out string? error) {
		error = null;
		if (number < 0 || !_files.TryGetValue("|bm" + number, out var range)) {
			error = $"no |bm{number}";
			return null;
		}

		try {
			return PictureReader.Read(_bytes, range.Start, range.End, _limits);
		} catch (MalformedHelpException e) {
			error = $"|bm{number}: {e.Message}";
			return null;
		}
	}

	internal byte[] Bytes => _bytes;

	internal HelpLimits Limits => _limits;

	internal ByteCursor File(string name) {
		if (!_files.TryGetValue(name, out var range)) {
			throw new MalformedHelpException($"no {name}");
		}

		return new ByteCursor(_bytes, range.Start, range.End);
	}

	// The container header and the directory tree (docs/retail/formats/winhelp.md#container).
	private static Dictionary<string, (int, int)> ReadDirectory(byte[] bytes, HelpLimits limits) {
		var header = new ByteCursor(bytes, 0, bytes.Length);
		if (header.U32() != FileMagic) {
			throw new MalformedHelpException("not a help file");
		}

		int directory = header.I32();
		var files = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
		HelpBTree.Read(InternalFile(bytes, directory), "z4", limits, (ref ByteCursor page) => {
			string name = page.String(limits.MaxStringBytes);
			int at = page.I32();
			var file = InternalFile(bytes, at);
			if (!files.TryAdd(name, (file.Position, file.End))) {
				throw new MalformedHelpException($"{name} listed twice");
			}
		});

		return files;
	}

	// An internal file: a 9-byte header whose used-space field bounds the contents.
	private static ByteCursor InternalFile(byte[] bytes, int at) {
		var cursor = new ByteCursor(bytes, 0, bytes.Length);
		if (at < 0) {
			throw new MalformedHelpException($"internal file at {at}");
		}

		cursor.Skip(at);
		cursor.Skip(4);
		uint used = cursor.U32();
		cursor.Skip(1);
		if (used > (uint)cursor.Remaining) {
			throw new MalformedHelpException($"internal file at {at} claims {used} bytes");
		}

		return cursor.Slice((int)used);
	}

	private void RejectUnsupportedFiles() {
		foreach (string name in new[] { "|Phrases", "|PhrIndex", "|PhrImage" }) {
			if (_files.ContainsKey(name)) {
				throw new MalformedHelpException($"{name}: phrase compression is not supported");
			}
		}
	}

	private void ReadSystem() {
		var system = File("|SYSTEM");
		if (system.U16() != SystemMagic) {
			throw new MalformedHelpException("|SYSTEM magic");
		}

		int minor = system.U16();
		int major = system.U16();
		system.Skip(4);
		int flags = system.U16();
		if (major != 1 || minor <= 16) {
			throw new MalformedHelpException($"help format {major}.{minor} is not supported");
		}

		if (flags != 0) {
			throw new MalformedHelpException($"|SYSTEM flags {flags}: compressed topics are not supported");
		}

		var macros = new List<string>();
		var windows = new List<HelpWindow>();
		while (system.Remaining >= 4) {
			int type = system.U16();
			var record = system.Slice(system.U16());
			switch (type) {
				case 1:
					Title = record.String(_limits.MaxStringBytes);
					break;
				case 2:
					Copyright = record.String(_limits.MaxStringBytes);
					break;
				case 3:
					ContentsOffset = record.U32();
					break;
				case 4:
					macros.Add(record.String(_limits.MaxStringBytes));
					break;
				case 6:
					windows.Add(ReadWindow(record));
					break;
			}
		}

		StartupMacros = macros;
		Windows = windows;
	}

	private HelpWindow ReadWindow(ByteCursor record) {
		if (record.Remaining != WindowRecordSize) {
			throw new MalformedHelpException($"window definition of {record.Remaining} bytes");
		}

		int flags = record.U16();
		string type = FixedString(ref record, 10);
		string name = FixedString(ref record, 9);
		string caption = FixedString(ref record, 51);
		int x = record.I16(), y = record.I16(), width = record.I16(), height = record.I16();
		record.Skip(2);
		int background = Rgb(ref record);
		int nonScrolling = Rgb(ref record);
		return new HelpWindow(type, name, caption, x, y, width, height,
			(flags & 0x100) != 0 ? background : null, (flags & 0x200) != 0 ? nonScrolling : null);

		static int Rgb(ref ByteCursor c) {
			var b = c.Bytes(4);
			return (b[0] << 16) | (b[1] << 8) | b[2];
		}
	}

	private static string FixedString(ref ByteCursor cursor, int length) {
		var bytes = cursor.Bytes(length);
		int nul = bytes.IndexOf((byte)0);
		return Windows1252.Decode(nul < 0 ? bytes : bytes[..nul]);
	}

	private void ReadFonts() {
		var font = File("|FONT");
		int faceCount = font.U16();
		int descriptorCount = font.U16();
		int faceOffset = font.U16();
		int descriptorOffset = font.U16();

		var whole = File("|FONT");
		var faces = new string[faceCount];
		var faceTable = whole;
		faceTable.Skip(faceOffset);
		for (int i = 0; i < faceCount; i++) {
			faces[i] = FixedString(ref faceTable, 32);
		}

		var descriptors = new HelpFont[descriptorCount];
		var table = whole;
		table.Skip(descriptorOffset);
		for (int i = 0; i < descriptorCount; i++) {
			byte attributes = table.U8();
			int halfPoints = table.U8();
			byte family = table.U8();
			int face = table.U16();
			var colour = table.Bytes(3);
			table.Skip(3);
			if (face >= faceCount) {
				throw new MalformedHelpException($"font {i} names face {face} of {faceCount}");
			}

			descriptors[i] = new HelpFont(attributes, halfPoints, family, faces[face],
				(colour[0] << 16) | (colour[1] << 8) | colour[2]);
		}

		Fonts = descriptors;
	}

	private void ReadContexts() {
		var contexts = new Dictionary<uint, uint>();
		HelpBTree.Read(File("|CONTEXT"), "L4", _limits, (ref ByteCursor page) => {
			uint hash = page.U32();
			contexts[hash] = page.U32();
		});

		Contexts = contexts;
	}

	private void ReadKeywords() {
		var data = File("|KWDATA");
		var keywords = new List<HelpKeyword>();
		HelpBTree.Read(File("|KWBTREE"), "F24", _limits, (ref ByteCursor page) => {
			string keyword = page.String(_limits.MaxStringBytes);
			int count = page.U16();
			int at = page.I32();
			if (at < 0 || at % 4 != 0 || (long)at + 4L * count > data.Remaining) {
				throw new MalformedHelpException($"keyword '{keyword}' points outside |KWDATA");
			}

			var targets = data;
			targets.Skip(at);
			var offsets = new uint[count];
			for (int i = 0; i < count; i++) {
				offsets[i] = targets.U32();
			}

			keywords.Add(new HelpKeyword(keyword, offsets));
		});

		Keywords = keywords;
	}

	private void ReadWindowMacros() {
		var byWindow = new Dictionary<int, IReadOnlyList<string>>();
		for (int window = 0; window < Windows.Count; window++) {
			if (!_files.ContainsKey("|CF" + window)) {
				continue;
			}

			var file = File("|CF" + window);
			var macros = new List<string>();
			while (!file.AtEnd) {
				macros.Add(file.String(_limits.MaxStringBytes));
			}

			byWindow[window] = macros;
		}

		WindowMacros = byWindow;
	}

	/// <summary>
	/// The topic chain (docs/retail/formats/winhelp.md#topic): joins the blocks into one stream, follows the
	/// links, and turns each record into a <see cref="HelpTopic"/> header or a block of one.
	/// </summary>
	private sealed class TopicReader(HelpFile help) {
		private const int BlockPayload = TopicBlockSize - TopicBlockHeader;

		private readonly HelpLimits _limits = help._limits;
		private byte[] _stream = [];
		private int _blocks;

		public IReadOnlyList<HelpTopic> Read() {
			var file = help.File("|TOPIC");
			int length = file.Remaining;
			_blocks = (length + TopicBlockSize - 1) / TopicBlockSize;
			using var joined = new MemoryStream(length);
			var reader = file;
			uint firstLink = 0;
			for (int block = 0; block < _blocks; block++) {
				int size = Math.Min(TopicBlockSize, reader.Remaining);
				var slice = reader.Slice(size);
				if (size < TopicBlockHeader) {
					throw new MalformedHelpException("|TOPIC block shorter than its header");
				}

				slice.Skip(4);
				uint first = slice.U32();
				if (block == 0) {
					firstLink = first;
				}

				slice.Skip(4);
				joined.Write(slice.Bytes(slice.Remaining));
			}

			_stream = joined.ToArray();

			var topics = new List<TopicBuilder>();
			int at = StreamOffset(firstLink);
			int blockOfCount = -1;
			int characters = 0;
			for (int links = 0; ; links++) {
				if (links >= _limits.MaxTopicLinks) {
					throw new MalformedHelpException("too many topic links");
				}

				var link = new ByteCursor(_stream, at, _stream.Length);
				int size = link.I32();
				int secondSize = link.I32();
				link.Skip(4);
				uint next = link.U32();
				int firstEnd = link.I32();
				byte type = link.U8();
				if (size < TopicLinkHeader || size > _stream.Length - at || firstEnd < TopicLinkHeader || firstEnd > size
					|| secondSize != size - firstEnd) {
					throw new MalformedHelpException($"topic link at {at}");
				}

				var first = new ByteCursor(_stream, at + TopicLinkHeader, at + firstEnd);
				var second = new ByteCursor(_stream, at + firstEnd, at + size);

				int block = at / BlockPayload;
				if (block != blockOfCount) {
					blockOfCount = block;
					characters = 0;
				}

				uint offset = (uint)(block << 15) + (uint)characters;
				switch (type) {
					case 2:
						topics.Add(ReadHeader(first, second, offset, topics.Count));
						break;
					case 0x20:
					case 0x23:
						if (topics.Count == 0) {
							throw new MalformedHelpException("text before the first topic header");
						}

						var record = new RecordReader(_limits, first, second);
						HelpBlock blockRead = type == 0x20 ? record.ReadParagraphRun(offset) : record.ReadTableRow(offset);
						characters += record.TextLength;
						if (characters >= 0x8000) {
							throw new MalformedHelpException("topic offset character count overflows its block");
						}

						topics[^1].Blocks.Add((at, blockRead));
						break;
					default:
						throw new MalformedHelpException($"topic record type {type} is not supported");
				}

				if (next == uint.MaxValue) {
					break;
				}

				int nextAt = StreamOffset(next);
				if (nextAt <= at) {
					throw new MalformedHelpException("topic chain runs backwards");
				}

				at = nextAt;
			}

			return topics.Select(t => t.Build()).ToList();
		}

		// A topic position to an offset in the joined stream (docs/retail/formats/winhelp.md#positions).
		private int StreamOffset(uint position) {
			int block = (int)(position >> 14);
			int within = (int)(position & 0x3FFF);
			if (block >= _blocks || within < TopicBlockHeader || within >= TopicBlockSize) {
				throw new MalformedHelpException($"topic position {position:x}");
			}

			int offset = block * BlockPayload + within - TopicBlockHeader;
			if (offset >= _stream.Length) {
				throw new MalformedHelpException($"topic position {position:x} past the end");
			}

			return offset;
		}

		private TopicBuilder ReadHeader(ByteCursor first, ByteCursor second, uint offset, int number) {
			first.Skip(4);
			uint back = first.U32();
			uint forward = first.U32();
			int stored = first.I32();
			uint nonScrolling = first.U32();
			uint scrolling = first.U32();
			if (stored != number) {
				throw new MalformedHelpException($"topic {number} numbered {stored}");
			}

			// NUL-separated rather than NUL-terminated: the last string runs to the end of the link.
			var strings = new List<string>();
			var rest = second.Bytes(second.Remaining);
			while (true) {
				int nul = rest.IndexOf((byte)0);
				strings.Add(Windows1252.Decode(nul < 0 ? rest : rest[..nul]));
				if (nul < 0) {
					break;
				}

				rest = rest[(nul + 1)..];
			}

			string title = strings[0];
			var macros = strings.Skip(1).Where(s => s.Length > 0).ToList();

			// A non-scrolling region with no scrolling region after it is the whole topic.
			int? scrollingAt = nonScrolling == uint.MaxValue ? null
				: scrolling == uint.MaxValue ? int.MaxValue : StreamOffset(scrolling);
			return new TopicBuilder(number, title, offset, back == uint.MaxValue ? null : back,
				forward == uint.MaxValue ? null : forward, macros, scrollingAt);
		}
	}

	private sealed class TopicBuilder(int number, string title, uint offset, uint? back, uint? forward,
		IReadOnlyList<string> macros, int? scrollingAt) {
		public List<(int At, HelpBlock Block)> Blocks { get; } = [];

		public HelpTopic Build() {
			int scrollingStart = 0;
			if (scrollingAt is { } start) {
				while (scrollingStart < Blocks.Count && Blocks[scrollingStart].At < start) {
					scrollingStart++;
				}
			}

			return new HelpTopic(number, title, offset, back, forward, macros,
				Blocks.Select(b => b.Block).ToList(), scrollingStart);
		}
	}
}
