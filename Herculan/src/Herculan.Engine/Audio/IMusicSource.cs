namespace Herculan.Engine.Audio;

/// <summary>
/// Where <see cref="StreamedCdAudio"/> gets a Red Book track's PCM from. Retail's music is a CD
/// player playing the disc; this engine reads the same audio digitally and plays it through its own
/// mixer, so the source of that audio is a seam of its own — see docs/retail/formats/audio.md's "CD audio".
///
/// <para>Four implementations: <see cref="CdRipMusicSource"/> reads the disc,
/// <see cref="ImageMusicSource"/> a disc image, <see cref="WaveFileMusicSource"/> a directory of
/// <c>TrackNN.wav</c> files for a machine with no drive, and <see cref="NullMusicSource"/> has nothing. <see cref="CdAudio.Open"/> chooses
/// between them.</para>
/// </summary>
public interface IMusicSource : IDisposable {
	/// <summary>Whether this source has any tracks at all.</summary>
	bool IsAvailable { get; }

	/// <summary>What the source is and what it holds, for the startup log.</summary>
	string Status { get; }

	/// <summary>
	/// Starts producing <paramref name="track"/> and returns at once, with the track filling on a
	/// worker thread. Asking again for the track most recently opened returns the same instance —
	/// already filled or still filling — so a loop, a resume or a second mission never reads it
	/// twice. Asking for a different one abandons the previous.
	/// </summary>
	/// <returns>The track, or null when this source does not have it.</returns>
	MusicTrack? Open(int track);
}

/// <summary>No music. What a machine with no disc and no track files gets.</summary>
public sealed class NullMusicSource : IMusicSource {
	/// <summary>Creates one with the given account of why there is no music.</summary>
	public NullMusicSource(string status) => Status = status;

	/// <inheritdoc />
	public bool IsAvailable => false;

	/// <inheritdoc />
	public string Status { get; }

	/// <inheritdoc />
	public MusicTrack? Open(int track) => null;

	/// <inheritdoc />
	public void Dispose() { }
}

/// <summary>
/// The bookkeeping <see cref="IMusicSource.Open"/>'s contract asks of every real source: one current
/// track, filled on a worker, cancelled when replaced or disposed.
/// </summary>
public abstract class MusicSourceBase : IMusicSource {
	private MusicTrack? _current;
	private CancellationTokenSource? _currentWork;
	private Task? _worker;
	private bool _disposed;

	/// <inheritdoc />
	public abstract bool IsAvailable { get; }

	/// <inheritdoc />
	public abstract string Status { get; }

	/// <inheritdoc />
	public MusicTrack? Open(int track) {
		if (_disposed) {
			return null;
		}

		if (_current is { } current && current.Number == track && current.Failure == null) {
			return current;
		}

		Abandon();

		if (Prepare(track) is not { } prepared) {
			return null;
		}

		var (created, produce) = prepared;
		var work = new CancellationTokenSource();
		_current = created;
		_currentWork = work;
		_worker = Task.Run(() => {
			try {
				produce(work.Token);
			} catch (OperationCanceledException) when (work.IsCancellationRequested) {
				// Abandoned: a different track was asked for, or the source is going away.
			} catch (Exception e) {
				created.Fail(e.Message);
			}
		});

		return created;
	}

	/// <summary>
	/// Works out whether this source has <paramref name="track"/> and how long it is, and returns the
	/// empty track with the work that fills it. The work runs on a worker thread and must honour its
	/// token; an exception it throws becomes the track's <see cref="MusicTrack.Failure"/>.
	/// </summary>
	protected abstract (MusicTrack Track, Action<CancellationToken> Produce)? Prepare(int track);

	/// <summary>Waits for the current track's producer to finish. For tests and the cache warm-up.</summary>
	internal bool WaitForCurrent(TimeSpan timeout) => _worker?.Wait(timeout) ?? true;

	private void Abandon() {
		if (_currentWork is { } work) {
			work.Cancel();

			// The worker notices at its next read; waiting for it keeps two producers from ever holding
			// the drive at once. A read is a few tens of milliseconds at most.
			try {
				_worker?.Wait(TimeSpan.FromSeconds(2));
			} catch (AggregateException) {
			}

			work.Dispose();
		}

		_current = null;
		_currentWork = null;
		_worker = null;
	}

	/// <inheritdoc />
	public void Dispose() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		Abandon();
		DisposeSource();
		GC.SuppressFinalize(this);
	}

	/// <summary>Releases whatever the derived source holds, after its worker has stopped.</summary>
	protected virtual void DisposeSource() { }
}
