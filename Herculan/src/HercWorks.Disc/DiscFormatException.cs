namespace HercWorks.Disc;

/// <summary>
/// A disc image, cue sheet or ISO 9660 structure is malformed, or uses a feature this reader does not
/// support. Every inconsistency found in the input surfaces as this (or, for a file that shrank while
/// open, an <see cref="EndOfStreamException"/>), never as an index or overflow fault.
/// </summary>
public sealed class DiscFormatException : IOException {
	/// <summary>Creates one with the given account of what is wrong.</summary>
	public DiscFormatException(string message) : base(message) { }

	/// <summary>Creates one wrapping the fault that exposed the problem.</summary>
	public DiscFormatException(string message, Exception inner) : base(message, inner) { }
}
