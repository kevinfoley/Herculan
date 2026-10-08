using System.Reflection;
using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;
using HercWorks.Vol.Io;

namespace HercWorks.Query;

/// <summary>What the census found for one archive entry.</summary>
internal enum CensusOutcome {
	/// <summary>No HercWorks reader is bound to the entry.</summary>
	NoReader,

	/// <summary>The reader threw, returned null, or ran past <see cref="Census.ParseTimeout"/>.</summary>
	Fails,

	/// <summary>The reader returned a model, but writing it back does not give the entry's bytes.</summary>
	Parses,

	/// <summary>The reader returned a model and writing it back gives the entry's bytes exactly.</summary>
	RoundTrips,
}

/// <param name="Archive">The archive's file name, <c>SIMVOL0.VOL</c>.</param>
/// <param name="Type">The entry's extension, upper case, or its whole name when it has none.</param>
/// <param name="Header">The entry's first four bytes in hex: the class tag and version of a persistent-object file.</param>
/// <param name="Reader">The registry label of the reader tried, null when none is bound.</param>
/// <param name="Offset">
/// For <see cref="CensusOutcome.Fails"/>, the reader's cursor when it stopped; for <see cref="CensusOutcome.Parses"/>, the first
/// byte the written copy gets wrong, or its length when it is a prefix of the entry.
/// </param>
/// <param name="Consumed">The reader's cursor after a parse, which falls short of <paramref name="Size"/> when it stops early.</param>
/// <param name="Detail">The exception, or how the written copy differs.</param>
/// <param name="IdenticalToOther">Whether the <c>--against</c> install has an entry of the same folder and name with the same bytes.</param>
internal sealed record CensusEntry(string Archive, string Folder, string Name, string Type, int Size, string Header,
	string? Reader, CensusOutcome Outcome, int? Offset, int? Consumed, string? Detail, bool? IdenticalToOther);

/// <summary>
/// Runs every HercWorks.Core reader over every entry of every archive in an install's <c>VOL</c> folder, writes back what
/// parses, and compares the result with the entry's bytes. The reader for an entry is the one
/// <see cref="TransformerRegistry"/> binds to it, else one the census binds itself (<see cref="ReaderFor"/>).
/// </summary>
internal static class Census {
	/// <summary>How long one parse or write may run before it counts as a failure.</summary>
	public static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(10);

	private static readonly FieldInfo CursorField =
		typeof(ThreeSpaceByteTransformer).GetField("Index", BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new MissingFieldException(nameof(ThreeSpaceByteTransformer), "Index");

	private static readonly FieldInfo BufferField =
		typeof(ThreeSpaceByteTransformer).GetField("Bytes", BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new MissingFieldException(nameof(ThreeSpaceByteTransformer), "Bytes");

	/// <summary>Every <c>.VOL</c> in <paramref name="installRoot"/>'s <c>VOL</c> folder, in name order.</summary>
	/// <exception cref="DirectoryNotFoundException">The install has no <c>VOL</c> folder.</exception>
	public static IReadOnlyList<string> Archives(string installRoot) {
		string folder = Path.Combine(installRoot, RetailData.ArchiveFolder);
		if (!Directory.Exists(folder)) {
			throw new DirectoryNotFoundException($"No {RetailData.ArchiveFolder} folder in {installRoot}.");
		}

		return Directory.GetFiles(folder, "*.VOL").OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
	}

	public static List<CensusEntry> Run(string installRoot, string? againstRoot) {
		var against = againstRoot == null ? null : Index(againstRoot);
		var result = new List<CensusEntry>();
		foreach (string path in Archives(installRoot)) {
			var vol = VolFileReader.ParseVolFile(path);
			string archive = Path.GetFileName(path).ToUpperInvariant();
			var chassis = ChassisNames(vol);
			foreach (var entry in vol.FilesSet) {
				result.Add(Examine(archive, vol, entry, chassis, against));
			}
		}

		return result;
	}

	/// <summary>Every entry of <paramref name="installRoot"/>'s archives by folder and name, for <c>--against</c>.</summary>
	private static Dictionary<(string Folder, string Name), byte[]> Index(string installRoot) {
		var index = new Dictionary<(string, string), byte[]>();
		foreach (string path in Archives(installRoot)) {
			var vol = VolFileReader.ParseVolFile(path);
			foreach (var entry in vol.FilesSet) {
				if (entry.RawBytes != null) {
					index.TryAdd(Key(vol, entry), entry.RawBytes);
				}
			}
		}

		return index;
	}

	private static (string Folder, string Name) Key(Voln vol, VolEntry entry) =>
		(FolderOf(vol, entry).ToUpperInvariant(), entry.FileName!.Trim().ToUpperInvariant());

	private static string FolderOf(Voln vol, VolEntry entry) =>
		vol.Folders.TryGetValue(entry.DirIdx, out var dir) ? dir.Label : "";

	/// <summary>
	/// The names <paramref name="vol"/>'s <c>nam\MECHS.NAM</c> lists, empty when it has none. Each names its chassis's
	/// <c>dat\</c> stats file (<see cref="HercWorks.Core.Data.File.NameList"/>); the registry finds ES2's by their source
	/// files' timestamps, which another game's archives do not share.
	/// </summary>
	private static HashSet<string> ChassisNames(Voln vol) {
		var names = vol.FilesSet.FirstOrDefault(e => string.Equals(e.FileName?.Trim(), "MECHS.NAM", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(FolderOf(vol, e), "nam", StringComparison.OrdinalIgnoreCase)) is { RawBytes: { } bytes }
			? new NameListTransformer().Parse(bytes)?.Names
			: null;
		return new HashSet<string>(names ?? [], StringComparer.OrdinalIgnoreCase);
	}

	private static CensusEntry Examine(string archive, Voln vol, VolEntry entry, HashSet<string> chassis,
			Dictionary<(string, string), byte[]>? against) {
		string name = entry.FileName!.Trim();
		byte[] bytes = entry.RawBytes ?? [];
		int dot = name.LastIndexOf('.');
		string type = (dot >= 0 ? name[(dot + 1)..] : name).ToUpperInvariant();
		string header = Convert.ToHexString(bytes, 0, Math.Min(4, bytes.Length));
		bool? identical = against == null ? null : against.TryGetValue(Key(vol, entry), out var other) && other.AsSpan().SequenceEqual(bytes);

		var (label, transformer) = ReaderFor(vol, entry, type, chassis);
		CensusEntry Make(CensusOutcome outcome, int? offset = null, int? consumed = null, string? detail = null) =>
			new(archive, FolderOf(vol, entry), name, type, bytes.Length, header, label, outcome, offset, consumed, detail, identical);

		if (transformer == null) {
			return Make(CensusOutcome.NoReader);
		}

		var (model, parseError) = Timed(() => transformer.ParseToObject(bytes));
		int? cursor = BufferField.GetValue(transformer) == null ? null : (int)CursorField.GetValue(transformer)!;
		if (parseError != null || model == null) {
			return Make(CensusOutcome.Fails, cursor, detail: parseError ?? "returned null");
		}

		var write = transformer.GetType().GetMethod("Write", BindingFlags.Instance | BindingFlags.Public, [model.GetType()]);
		if (write == null) {
			return Make(CensusOutcome.Parses, consumed: cursor, detail: "no Write for " + model.GetType().Name);
		}

		var (written, writeError) = Timed(() => write.Invoke(transformer, [model]));
		if (writeError != null || written is not byte[] copy) {
			return Make(CensusOutcome.Parses, consumed: cursor, detail: "write: " + (writeError ?? "returned null"));
		}

		int length = Math.Min(copy.Length, bytes.Length);
		int differs = bytes.AsSpan(0, length).CommonPrefixLength(copy.AsSpan(0, length));
		if (differs == length && copy.Length == bytes.Length) {
			return Make(CensusOutcome.RoundTrips, consumed: cursor);
		}

		return Make(CensusOutcome.Parses, differs, cursor, $"written copy is {copy.Length} bytes, differs from byte {differs}");
	}

	/// <summary>
	/// The registry's reader, else one of three the registry binds more narrowly than the format: a <c>.NAM</c>'s, a
	/// chassis stats file's by <see cref="ChassisNames"/>, and the structure shape library's for every <c>.DGS</c>,
	/// all of which open with the same persistent-object header.
	/// </summary>
	private static (string? Label, ThreeSpaceByteTransformer? Transformer) ReaderFor(Voln vol, VolEntry entry, string type, HashSet<string> chassis) {
		if (TransformerRegistry.FindTransformer(entry) is { } registered) {
			return (TransformerRegistry.FindLabel(entry), registered);
		}

		string stem = Path.GetFileNameWithoutExtension(entry.FileName!.Trim());
		return type switch {
			"NAM" => ("Name List", new NameListTransformer()),
			"DAT" when chassis.Contains(stem) && string.Equals(FolderOf(vol, entry), "dat", StringComparison.OrdinalIgnoreCase) =>
				("Herc Sim Data (by MECHS.NAM)", new HercWorks.Core.Io.Transform.Dbsim.HercSimDataTransformer()),
			"DGS" => ("Structure Shape Library (by extension)", new HercWorks.Core.Io.Transform.Dbsim.BasesDgsTransformer()),
			_ => (null, null),
		};
	}

	/// <summary>
	/// Runs <paramref name="work"/> on a thread of its own, so a reader that loops on bytes it does not understand is
	/// abandoned after <see cref="ParseTimeout"/> rather than stopping the census.
	/// </summary>
	private static (object? Result, string? Error) Timed(Func<object?> work) {
		object? result = null;
		string? error = null;
		var thread = new Thread(() => {
			try {
				result = work();
			} catch (Exception ex) {
				var inner = ex is TargetInvocationException { InnerException: { } wrapped } ? wrapped : ex;
				error = $"{inner.GetType().Name}: {inner.Message}{Where(inner)}";
			}
		}) { IsBackground = true };
		thread.Start();
		if (!thread.Join(ParseTimeout)) {
			return (null, $"still running after {ParseTimeout.TotalSeconds:0} s");
		}

		return (result, error);
	}

	/// <summary>The HercWorks method the exception was thrown from, " in ReadArray", or nothing.</summary>
	private static string Where(Exception ex) {
		var frame = new System.Diagnostics.StackTrace(ex).GetFrames()
			.FirstOrDefault(f => f.GetMethod()?.DeclaringType?.Namespace?.StartsWith("HercWorks.") == true);
		return frame?.GetMethod() is { } method ? $" in {method.DeclaringType!.Name}.{method.Name}" : "";
	}
}
