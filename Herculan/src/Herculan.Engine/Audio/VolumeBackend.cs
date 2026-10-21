namespace Herculan.Engine.Audio;

/// <summary>
/// An <see cref="IAudioBackend"/> with the player's <see cref="AudioVolumes"/> laid over it: every playback's gain is
/// scaled by <see cref="AudioVolumes.Effects"/> and every stream's by <see cref="AudioVolumes.Music"/>, which is right
/// for a mission, whose music is the CD's stream and whose samples are all effects or speech. A change to either volume
/// reaches what is already sounding at the next <see cref="Update"/>. It owns the backend it wraps.
/// </summary>
public sealed class VolumeBackend : IAudioBackend {
	// Past this many remembered playbacks, a start forgets the ones that have finished.
	private const int PruneThreshold = 128;

	private readonly IAudioBackend _inner;
	private readonly AudioVolumes _volumes;

	// Each running playback's own gain, before the effects volume. A playback that ends by itself is never stopped
	// here, so finished ones are forgotten when a volume changes or the table grows past PruneThreshold.
	private readonly Dictionary<int, float> _gains = new();
	private readonly List<VolumeStream> _streams = new();
	private float _effects;
	private float _music;

	public VolumeBackend(IAudioBackend inner, AudioVolumes volumes) {
		_inner = inner;
		_volumes = volumes;
		_effects = volumes.Effects;
		_music = volumes.Music;
	}

	/// <inheritdoc />
	public bool IsAvailable => _inner.IsAvailable;

	/// <inheritdoc />
	public int CreateSample(WaveSample sample) => _inner.CreateSample(sample);

	/// <inheritdoc />
	public int Start(int sample, float gain, float pan, float pitch, bool looping) {
		if (_gains.Count >= PruneThreshold) {
			ForgetFinished();
		}

		int play = _inner.Start(sample, gain * _effects, pan, pitch, looping);
		if (play >= 0) {
			_gains[play] = gain;
		}

		return play;
	}

	/// <inheritdoc />
	public void Stop(int play) {
		_inner.Stop(play);
		_gains.Remove(play);
	}

	/// <inheritdoc />
	public bool IsPlaying(int play) => _inner.IsPlaying(play);

	/// <inheritdoc />
	public void SetGain(int play, float gain) {
		if (_gains.ContainsKey(play)) {
			_gains[play] = gain;
		}

		_inner.SetGain(play, gain * _effects);
	}

	/// <inheritdoc />
	public void SetPan(int play, float pan) => _inner.SetPan(play, pan);

	/// <inheritdoc />
	public void SetPitch(int play, float ratio) => _inner.SetPitch(play, ratio);

	/// <inheritdoc />
	public void SetMasterGain(float gain) => _inner.SetMasterGain(gain);

	/// <inheritdoc />
	public void StopAll() {
		_inner.StopAll();
		_gains.Clear();
	}

	/// <inheritdoc />
	public IAudioStream? OpenStream(int sampleRate, int channels) {
		if (_inner.OpenStream(sampleRate, channels) is not { } stream) {
			return null;
		}

		var wrapped = new VolumeStream(this, stream);
		_streams.Add(wrapped);
		return wrapped;
	}

	/// <summary>Services the device, then pushes a changed volume to every playback and stream still sounding.</summary>
	public void Update() {
		_inner.Update();

		if (_volumes.Effects != _effects) {
			_effects = _volumes.Effects;
			ForgetFinished();
			foreach (var (play, gain) in _gains) {
				_inner.SetGain(play, gain * _effects);
			}
		}

		if (_volumes.Music != _music) {
			_music = _volumes.Music;
			foreach (var stream in _streams) {
				stream.ApplyGain();
			}
		}
	}

	private void ForgetFinished() {
		foreach (int play in _gains.Keys.Where(play => !_inner.IsPlaying(play)).ToList()) {
			_gains.Remove(play);
		}
	}

	/// <inheritdoc />
	public void Dispose() => _inner.Dispose();

	// A stream whose gain is its own times the music volume.
	private sealed class VolumeStream : IAudioStream {
		private readonly VolumeBackend _owner;
		private readonly IAudioStream _inner;
		private float _gain = 1f;

		public VolumeStream(VolumeBackend owner, IAudioStream inner) {
			_owner = owner;
			_inner = inner;
			ApplyGain();
		}

		public int FreeBlocks => _inner.FreeBlocks;

		public long Position => _inner.Position;

		public void Queue(ReadOnlySpan<short> pcm, long startFrame) => _inner.Queue(pcm, startFrame);

		public void Stop() => _inner.Stop();

		public void SetGain(float gain) {
			_gain = gain;
			ApplyGain();
		}

		public void ApplyGain() => _inner.SetGain(_gain * _owner._music);

		public void Dispose() {
			_owner._streams.Remove(this);
			_inner.Dispose();
		}
	}
}
