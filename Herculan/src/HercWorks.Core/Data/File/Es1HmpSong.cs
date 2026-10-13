namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - one of ES1's <c>.HMP</c> songs, HMI's MIDI format, in the layout the song loader reads (docs/es1/formats/hmp-music.md).
/// Offsets are stored as the file holds them; the pointers the loader writes into its copy are not in the model. Read by
/// <see cref="Io.Transform.Common.Es1HmpSongTransformer"/>.
/// </summary>
public class Es1HmpSong {
	/// <summary>The size of the header, the offset of the first track.</summary>
	public const int HeaderSize = 0x308;

	/// <summary>The most tracks the loader keeps a song's per-track tables for.</summary>
	public const int MaxTracks = 32;

	/// <summary>Bytes <c>+0x00</c>–<c>+0x1f</c>: <c>HMIMIDIP</c>, the 8 bytes the loader compares, then 24 zero bytes in every retail file.</summary>
	public byte[] Signature { get; set; } = [];

	/// <summary><c>+0x20</c>: the offset from the file's start of the branch table, which follows the last track in every retail file.</summary>
	public int BranchTableOffset { get; set; }

	/// <summary>Bytes <c>+0x24</c>–<c>+0x2f</c>, which the loader does not read.</summary>
	public byte[] Unread24 { get; set; } = [];

	/// <summary><c>+0x30</c>: the number of tracks.</summary>
	public int TrackCount { get; set; }

	/// <summary>The dwords at <c>+0x34</c>, <c>+0x38</c> and <c>+0x3c</c>, which the loader does not read.</summary>
	public int[] Unread34 { get; set; } = [];

	/// <summary>The 16 dwords at <c>+0x40</c>–<c>+0x7f</c>, which the loader does not read.</summary>
	public int[] Unread40 { get; set; } = [];

	/// <summary>
	/// <c>+0x80</c>: for each of 32 tracks, the five device ids the loader matches against the drivers it has open, 0 ending the
	/// list.
	/// </summary>
	public int[][] TrackDevices { get; set; } = [];

	/// <summary>Bytes <c>+0x300</c>–<c>+0x307</c>, where the loader writes the caller's callback; zero in every retail file.</summary>
	public byte[] CallbackSlot { get; set; } = [];

	/// <summary>The <see cref="TrackCount"/> tracks, in file order from <see cref="HeaderSize"/>.</summary>
	public List<Es1HmpTrack> Tracks { get; set; } = [];

	/// <summary>Bytes between the last track's end and <see cref="BranchTableOffset"/>; empty in every retail file.</summary>
	public byte[] AfterTracks { get; set; } = [];

	/// <summary>The branch table's counts, one byte per track: how many branch entries the track has.</summary>
	public byte[] BranchCounts { get; set; } = [];

	/// <summary>The branch entries, each track's in turn, as many as <see cref="BranchCounts"/> sums to.</summary>
	public List<Es1HmpBranch> Branches { get; set; } = [];

	/// <summary>The bytes after the branch entries, to the end of the file, which the entries' offsets point into.</summary>
	public byte[] BranchData { get; set; } = [];
}

/// <summary>One track chunk of an <see cref="Es1HmpSong"/>.</summary>
public class Es1HmpTrack {
	/// <summary><c>+0</c>: the chunk's number, its index in every retail file.</summary>
	public int ChunkNumber { get; set; }

	/// <summary><c>+4</c>: the chunk's length, these 12 bytes of header included; the next chunk starts that far on.</summary>
	public int Length { get; set; }

	/// <summary><c>+8</c>, which the loader does not read.</summary>
	public int Unread08 { get; set; }

	/// <summary>From <c>+0xc</c>: the track's events, <see cref="Length"/> less 12 bytes.</summary>
	public byte[] Events { get; set; } = [];
}

/// <summary>One 24-byte branch entry of an <see cref="Es1HmpSong"/>.</summary>
public class Es1HmpBranch {
	/// <summary>Bytes <c>+0</c>–<c>+7</c>, which the loader does not read.</summary>
	public byte[] Head { get; set; } = [];

	/// <summary><c>+8</c>, where the loader writes the pointer <see cref="DataOffset"/> gives; the same value as it in every retail file.</summary>
	public int PointerSlot { get; set; }

	/// <summary>
	/// <c>+0xc</c>: an offset from the file's start, in every retail file into <see cref="Es1HmpSong.BranchData"/> or, in one
	/// entry of <c>SHELLC.HMP</c>, at the file's end.
	/// </summary>
	public int DataOffset { get; set; }

	/// <summary>Bytes <c>+0x10</c>–<c>+0x17</c>, which the loader does not read.</summary>
	public byte[] Tail { get; set; } = [];
}
