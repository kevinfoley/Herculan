namespace HercWorks.Core.Io.Transform;

/// <summary>
/// Transformer base for a single, statically-known file model — the successor to the untyped
/// <see cref="ThreeSpaceByteTransformer.BytesToObject"/> / <see cref="ThreeSpaceByteTransformer.ObjectToBytes"/>
/// pair.
///
/// <para>Why: the untyped pair is declared in terms of <see cref="HercWorks.Vol.DataFile"/>, which
/// forced every parsed model to derive from it. Nothing consumes that base polymorphically —
/// every call site casts straight back to the concrete type — while the inherited members
/// (RawBytes, Ext, FileName, Header, FileSize) are written by the transformers and then never
/// read off a parsed model, have to be name-blocklisted out of the VOL browser's content tree,
/// and collide with genuine per-format header fields on three classes. Parsing through
/// <see cref="Parse"/> instead returns the real type, so the cast at the call site disappears and
/// the model class is free of plumbing it never wanted.</para>
///
/// <para>Migration is per-transformer and non-breaking: this derives from
/// <see cref="ThreeSpaceByteTransformer"/> and leaves the legacy pair at its default, so
/// TransformerRegistry keeps holding a heterogeneous list of both old and new transformers and
/// reaches either through <see cref="ThreeSpaceByteTransformer.ParseToObject"/>. Converted so far:
/// <see cref="Dbsim.GauFileTransformer"/>.</para>
///
/// <para>Round-trip note: formats that must write back bytes they have not decoded keep those
/// bytes as an explicit field on their own model (e.g. <c>GAUFile.Remainder</c>) rather than
/// leaning on an inherited <c>RawBytes</c> — the undecoded span is part of that format, so it
/// belongs to that format's model.</para>
/// </summary>
/// <typeparam name="T">The parsed model this transformer reads and writes.</typeparam>
public abstract class ByteTransformer<T> : ThreeSpaceByteTransformer where T : class {
	/// <summary>Parses bytes into the model, or null if they cannot be read.</summary>
	/// <param name="inputArray">The bytes to parse, or null for none.</param>
	public abstract T? Parse(byte[]? inputArray);

	/// <summary>Serializes the model back to bytes, or null if it cannot be written.</summary>
	public abstract byte[]? Write(T source);

	public sealed override object? ParseToObject(byte[]? inputArray) => Parse(inputArray);
}
