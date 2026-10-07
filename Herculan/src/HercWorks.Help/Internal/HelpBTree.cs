namespace HercWorks.Help.Internal;

/// <summary>
/// Walks a B+ tree's leaf chain in key order (docs/retail/formats/winhelp.md#b-trees). The tree is only
/// ever read whole, so the pages above the leaves are used for nothing but finding the first leaf.
/// </summary>
internal static class HelpBTree {
	public delegate void EntryReader(ref ByteCursor page);

	private const ushort Magic = 0x293B;
	private const int MaxLevels = 16;

	/// <summary>
	/// Calls <paramref name="readEntry"/> once per entry, with the cursor on the entry and limited to
	/// its page. Checks the tree's structure string against <paramref name="structure"/>, so a tree
	/// whose entries are laid out differently is rejected rather than misread.
	/// </summary>
	public static void Read(ByteCursor tree, string structure, HelpLimits limits, EntryReader readEntry) {
		if (tree.U16() != Magic) {
			throw new MalformedHelpException("B+ tree magic");
		}

		tree.Skip(2);
		int pageSize = tree.U16();
		var structureBytes = tree.Bytes(16);
		int length = structureBytes.IndexOf((byte)0);
		string actual = Windows1252.Decode(structureBytes[..(length < 0 ? 16 : length)]);
		if (actual != structure) {
			throw new MalformedHelpException($"B+ tree structure '{actual}', expected '{structure}'");
		}

		tree.Skip(4);
		int root = tree.I16();
		tree.Skip(2);
		int pageCount = tree.I16();
		int levels = tree.I16();
		tree.Skip(4);

		if (pageSize < 8 || pageCount <= 0 || pageCount > limits.MaxTreePages || levels < 1 || levels > MaxLevels
			|| (long)pageSize * pageCount > tree.Remaining) {
			throw new MalformedHelpException("B+ tree header");
		}

		var pages = tree.Slice(pageSize * pageCount);
		int pagesStart = pages.Position;

		int page = root;
		for (int level = levels; level > 1; level--) {
			var index = PageAt(pages, pagesStart, page, pageSize, pageCount);
			index.Skip(4);
			page = index.I16();
		}

		var visited = new bool[pageCount];
		while (page != -1) {
			var leaf = PageAt(pages, pagesStart, page, pageSize, pageCount);
			if (visited[page]) {
				throw new MalformedHelpException("B+ tree leaf chain loops");
			}

			visited[page] = true;
			leaf.Skip(2);
			int count = leaf.U16();
			leaf.Skip(2);
			int next = leaf.I16();
			for (int i = 0; i < count; i++) {
				readEntry(ref leaf);
			}

			page = next;
		}
	}

	private static ByteCursor PageAt(ByteCursor pages, int pagesStart, int page, int pageSize, int pageCount) {
		if (page < 0 || page >= pageCount) {
			throw new MalformedHelpException($"B+ tree page {page} of {pageCount}");
		}

		var cursor = pages;
		cursor.Skip(pagesStart + page * pageSize - cursor.Position);
		return cursor.Slice(pageSize);
	}
}
