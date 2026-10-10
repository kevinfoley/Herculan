namespace Herculan.Engine.Audio;

/// <summary>
/// The backend used when there is no audio device: a headless test, a mission editor, or a machine
/// where OpenAL failed to open.
///
/// <para>It hands out real sample ids and play handles and remembers nothing else, so
/// <see cref="SoundDirector"/> runs its whole rule set — variation rolls, distance cutoff, the
/// newest-handle bookkeeping — exactly as it would with a device attached. That is what makes those
/// rules testable without one.</para>
///
/// <para><see cref="IsPlaying"/> always answers false, which is the one behavioural difference:
/// a finite repeat count completes immediately instead of after the sample's length. Channels are
/// unlimited here, so <see cref="Start"/> never refuses.</para>
/// </summary>
public sealed class NullAudioBackend : IAudioBackend {
	private int _nextSample;
	private int _nextPlay;

	/// <inheritdoc />
	public bool IsAvailable => false;

	/// <inheritdoc />
	public int CreateSample(WaveSample sample) => _nextSample++;

	/// <inheritdoc />
	public int Start(int sample, float gain, float pan, float pitch, bool looping) =>
		sample < 0 ? -1 : _nextPlay++;

	/// <inheritdoc />
	public void Stop(int play) { }

	/// <inheritdoc />
	public bool IsPlaying(int play) => false;

	/// <inheritdoc />
	public void SetGain(int play, float gain) { }

	/// <inheritdoc />
	public void SetPan(int play, float pan) { }

	/// <inheritdoc />
	public void SetPitch(int play, float ratio) { }

	/// <inheritdoc />
	public void SetMasterGain(float gain) { }

	/// <inheritdoc />
	public void StopAll() { }

	/// <inheritdoc />
	public IAudioStream? OpenStream(int sampleRate, int channels) => null;

	/// <inheritdoc />
	public void Update() { }

	/// <inheritdoc />
	public void Dispose() { }
}
