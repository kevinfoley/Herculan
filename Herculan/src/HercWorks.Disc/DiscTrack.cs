namespace HercWorks.Disc;

/// <summary>
/// One track of a <see cref="DiscImage"/>, with its extent given the way a drive's table of contents
/// reports it: from its <c>INDEX 01</c> to the next track's <c>INDEX 01</c>, so a track's length
/// includes the pregap of the track after it, which is also what a CD player plays.
/// </summary>
public sealed class DiscTrack {
	internal DiscTrack(int number, TrackFormat format, int startLba, int sectorCount, int pregapSectors,
			bool boundaryUnknown) {
		Number = number;
		Format = format;
		StartLba = startLba;
		SectorCount = sectorCount;
		PregapSectors = pregapSectors;
		BoundaryUnknown = boundaryUnknown;
	}

	/// <summary>The track number, 1 to 99.</summary>
	public int Number { get; }

	/// <summary>How the track's sectors are stored.</summary>
	public TrackFormat Format { get; }

	/// <summary>Whether this is a Red Book audio track.</summary>
	public bool IsAudio => Format == TrackFormat.Audio;

	/// <summary>The disc address of the track's <c>INDEX 01</c>. Track 1's is 0.</summary>
	public int StartLba { get; }

	/// <summary>Sectors from <see cref="StartLba"/> to the next track's start, or to the end of the image for the last.</summary>
	public int SectorCount { get; }

	/// <summary>Sectors of pregap before <see cref="StartLba"/>: the cue sheet's <c>INDEX 00</c> and <c>PREGAP</c> together.</summary>
	public int PregapSectors { get; }

	/// <summary>
	/// The track is everything after the data track of a raw image opened without a cue sheet. Such an
	/// image keeps no table of contents, so this one "track" may run across several of the disc's real
	/// audio tracks, numbered from 2 as the first of them would be. Open the image through a cue sheet to
	/// get the real boundaries.
	/// </summary>
	public bool BoundaryUnknown { get; }

	/// <summary>The track's playing time, at 75 sectors a second.</summary>
	public TimeSpan Duration => TimeSpan.FromSeconds(SectorCount / (double)DiscImage.SectorsPerSecond);

	/// <inheritdoc />
	public override string ToString() =>
		$"Track {Number:00} {Format} LBA {StartLba} +{SectorCount}{(BoundaryUnknown ? " (boundary unknown)" : "")}";
}
