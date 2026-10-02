namespace HercWorks.Help;

/// <summary>
/// The ceilings the parser and the picture decoder check before they allocate or loop.
///
/// <para>A help file is replaceable game data like any other, and the copies of the retail game in
/// circulation are not all the original pressing, so nothing here trusts a declared size or a stored
/// pointer. Each limit bounds a failure the format otherwise allows:</para>
///
/// <list type="bullet">
/// <item>B+ tree pages and topic links are chained by numbers stored in the file. A chain that loops
/// back on itself never ends; the page and link counts stop it, and the topic walk also refuses to
/// move backwards.</item>
/// <item>A picture's dimensions come from the file and are multiplied out to size its pixel buffer.
/// Unbounded, two compressed longs ask for an allocation of several exabytes.</item>
/// <item>Strings end at a NUL the file may never supply, so every string read stops at
/// <see cref="MaxStringBytes"/>.</item>
/// <item>The readme <see cref="WriteDocument"/> reads is held whole and its text embedded in the page,
/// so its size is capped too.</item>
/// </list>
///
/// <para>The defaults sit well clear of the retail corpus — 9 MB files, 103 topics, a 344x432
/// largest picture, a 37 KB readme — and are set to be uncontroversial for any plausible help file
/// rather than to be tight.</para>
/// </summary>
public sealed record HelpLimits {
	/// <summary>The limits used when a caller does not supply any.</summary>
	public static HelpLimits Default { get; } = new();

	/// <summary>Largest accepted file, in bytes. The whole file is held in memory.</summary>
	public int MaxFileBytes { get; init; } = 64 * 1024 * 1024;

	/// <summary>Most pages a B+ tree walk visits before the tree is rejected.</summary>
	public int MaxTreePages { get; init; } = 4096;

	/// <summary>Most topic links the topic walk visits before the file is rejected.</summary>
	public int MaxTopicLinks { get; init; } = 200_000;

	/// <summary>Largest accepted picture width or height, in pixels.</summary>
	public int MaxPictureDimension { get; init; } = 4096;

	/// <summary>Largest accepted pixel count per picture, checked as a <c>long</c> product.</summary>
	public long MaxPicturePixels { get; init; } = 4096L * 4096;

	/// <summary>Longest accepted NUL-terminated string, in bytes.</summary>
	public int MaxStringBytes { get; init; } = 64 * 1024;

	/// <summary>Largest accepted readme, a Write document, in bytes.</summary>
	public int MaxReadmeBytes { get; init; } = 1024 * 1024;
}
