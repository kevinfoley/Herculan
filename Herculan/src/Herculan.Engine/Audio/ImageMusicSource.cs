using System.Runtime.CompilerServices;
using HercWorks.Disc;

namespace Herculan.Engine.Audio;

/// <summary>
/// Music from the audio tracks of a disc image (<see cref="DiscImage"/>), for a player whose disc is an image
/// rather than a CD in a drive. <b>This engine's own</b>; retail plays only a disc, through MCI
/// (docs/retail/formats/audio.md, "CD audio").
///
/// <para>An image opened through a cue sheet has the disc's real tracks. A raw image without one keeps no
/// table of contents, and <see cref="DiscImage"/> hands back all its audio as one track whose boundaries are
/// unknown. <b>Such an image's tracks are an estimate</b>: past the first track's pregap
/// (<see cref="FirstAudioPregapSectors"/>), the audio is cut wherever it holds at least
/// <see cref="MinimumGapSectors"/> sectors of exact digital silence, each piece becomes a track that starts at
/// its first sector holding sound and runs to the next piece's start, and the pieces are numbered in image
/// order from the first track's number. How those pieces compare with a disc's real tracks, and what is
/// open about their order, is docs/retail/retail-builds.md, "The v1.10 disc image". The status line says when the
/// boundaries are estimated.</para>
///
/// <para>Finding the pieces reads the whole audio stretch once, about 171 MB for the v1.10 image, so
/// <see cref="BeginLayout"/> starts it in the background as soon as the image is opened and every mission's
/// source shares the result. The source never disposes the image: whoever opened it owns it.</para>
/// </summary>
public sealed class ImageMusicSource : MusicSourceBase {
	/// <summary>
	/// The shortest run of digital silence that separates two pieces: 4 s. The v1.10 image's gaps are 453 to
	/// 458 sectors; a quiet passage inside a piece is not exact zero for anything like this long.
	/// </summary>
	public const int MinimumGapSectors = 300;

	/// <summary>
	/// The longest track opened, 30 minutes. The retail tracks are under three minutes; this keeps a damaged or
	/// foreign image from asking for a buffer of gigabytes.
	/// </summary>
	public const int MaxTrackSectors = 30 * 60 * DiscImage.SectorsPerSecond;

	/// <summary>The 2 s pregap a CD carries between its data track and the first audio track.</summary>
	public const int FirstAudioPregapSectors = 2 * DiscImage.SectorsPerSecond;

	/// <summary>Sectors read per call while filling a track or scanning for silence.</summary>
	private const int BatchSectors = 256;

	private static readonly ConditionalWeakTable<DiscImage, Task<Layout>> Layouts = new();

	private readonly DiscImage _image;
	private readonly Layout _layout;

	private ImageMusicSource(DiscImage image, Layout layout) {
		_image = image;
		_layout = layout;
	}

	/// <summary>One audio track as this source plays it.</summary>
	public readonly record struct Extent(int Number, int StartLba, int SectorCount);

	/// <summary>The tracks of an image, and whether their boundaries are <see cref="ImageMusicSource"/>'s estimate.</summary>
	public sealed record Layout(IReadOnlyList<Extent> Tracks, bool Estimated);

	/// <summary>The tracks this source plays.</summary>
	public IReadOnlyList<Extent> Tracks => _layout.Tracks;

	/// <summary>Whether the track boundaries are estimated from silence rather than read from a cue sheet.</summary>
	public bool Estimated => _layout.Estimated;

	/// <inheritdoc />
	public override bool IsAvailable => _layout.Tracks.Count > 0;

	/// <inheritdoc />
	public override string Status =>
		$"reading {_image.Path}, audio tracks {string.Join(",", _layout.Tracks.Select(track => track.Number))}"
		+ (_layout.Estimated ? " (boundaries estimated from the silence between pieces)" : "");

	/// <summary>
	/// Starts working out <paramref name="image"/>'s tracks in the background, if that has not already begun, so
	/// that <see cref="TryCreate"/> later has them ready.
	/// </summary>
	public static Task<Layout> BeginLayout(DiscImage image) =>
		Layouts.GetValue(image, key => Task.Run(() => ReadLayout(key)));

	/// <summary>
	/// A source for <paramref name="image"/>'s audio, waiting for <see cref="BeginLayout"/>'s work if it is still
	/// running; null, with the reason, when the image has no audio or it could not be read. Never throws.
	/// </summary>
	public static ImageMusicSource? TryCreate(DiscImage image, out string failure) {
		Layout layout;
		try {
			layout = BeginLayout(image).GetAwaiter().GetResult();
		} catch (Exception e) when (e is DiscFormatException or IOException or ObjectDisposedException) {
			failure = $"the audio in {image.Path} could not be read: {e.Message}";
			return null;
		}

		if (layout.Tracks.Count == 0) {
			failure = $"the disc image {image.Path} has no audio tracks";
			return null;
		}

		failure = "";
		return new ImageMusicSource(image, layout);
	}

	/// <summary>
	/// <paramref name="image"/>'s audio tracks: a cue sheet's as they are, and a track of unknown boundaries cut
	/// into pieces at its silences, as <see cref="ImageMusicSource"/> describes.
	/// </summary>
	public static Layout ReadLayout(DiscImage image) {
		var tracks = new List<Extent>();
		bool estimated = false;
		foreach (var track in image.Tracks.Where(track => track.IsAudio)) {
			if (track.BoundaryUnknown) {
				estimated = true;
				tracks.AddRange(SplitAtSilence(image, track));
			} else {
				tracks.Add(new Extent(track.Number, track.StartLba, track.SectorCount));
			}
		}

		return new Layout(tracks, estimated);
	}

	// The stretch opens with the first audio track's pregap, which a disc puts between a data track and audio,
	// so no piece starts inside it: on the v1.10 image its first sector holds 24 stray bytes that are not
	// silence.
	private static List<Extent> SplitAtSilence(DiscImage image, DiscTrack track) {
		var starts = new List<int>();
		int end = track.StartLba + track.SectorCount;
		int pregapEnd = track.StartLba + FirstAudioPregapSectors;
		// The first sound starts a piece whatever came before it.
		long silentRun = MinimumGapSectors;
		var buffer = new byte[BatchSectors * DiscImage.RawSectorSize];
		for (int lba = track.StartLba; lba < end;) {
			int count = Math.Min(BatchSectors, end - lba);
			image.ReadAudioSectors(lba, count, buffer);
			for (int i = 0; i < count; i++) {
				if (lba + i < pregapEnd) {
					continue;
				}

				if (buffer.AsSpan(i * DiscImage.RawSectorSize, DiscImage.RawSectorSize).IndexOfAnyExcept((byte)0) < 0) {
					silentRun++;
					continue;
				}

				if (silentRun >= MinimumGapSectors) {
					starts.Add(lba + i);
				}

				silentRun = 0;
			}

			lba += count;
		}

		return starts.Select((start, i) =>
			new Extent(track.Number + i, start, (i + 1 < starts.Count ? starts[i + 1] : end) - start)).ToList();
	}

	/// <inheritdoc />
	protected override (MusicTrack Track, Action<CancellationToken> Produce)? Prepare(int track) {
		if (_layout.Tracks.FirstOrDefault(extent => extent.Number == track) is not { SectorCount: > 0 and <= MaxTrackSectors } extent) {
			return null;
		}

		var created = new MusicTrack(track, (long)extent.SectorCount * MusicTrack.FramesPerSector);
		return (created, token => {
			var buffer = new byte[BatchSectors * DiscImage.RawSectorSize];
			int end = extent.StartLba + extent.SectorCount;
			for (int lba = extent.StartLba; lba < end;) {
				token.ThrowIfCancellationRequested();
				int count = Math.Min(BatchSectors, end - lba);
				_image.ReadAudioSectors(lba, count, buffer);
				created.Append(buffer.AsSpan(0, count * DiscImage.RawSectorSize));
				lba += count;
			}
		});
	}
}
