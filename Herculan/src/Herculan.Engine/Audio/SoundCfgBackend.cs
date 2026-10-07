using HercWorks.Core.Data.File.Cfg;

namespace Herculan.Engine.Audio;

/// <summary>
/// An <see cref="IAudioBackend"/> with <c>data\sound.cfg</c>'s output format put on it — what the original's SOS
/// mixer does with <c>Rate</c> and <c>Width</c> (docs/retail/formats/audio.md, "DATA\SOUND.CFG"). It owns the
/// backend it wraps.
///
/// <list type="bullet">
/// <item><c>Width = Mono</c> centres every playback.</item>
/// <item>A sample recorded faster than <c>Rate</c>'s mixing rate is resampled down to it as it is registered, so
/// at <c>Rate = 11</c> the 22,050 Hz <c>hmi</c> samples lose their top octave. <see cref="WaveSample.Downsample"/>
/// is the conversion, and it is this engine's.</item>
/// <item>Streams pass through untouched: CD music is MCI's and never reached the mixer.</item>
/// <item><c>Driver</c> and <c>Buffers</c> are not applied. They choose between the Windows MME and DirectSound
/// outputs and size MME's buffering, and an OpenAL device has neither to choose.</item>
/// </list>
///
/// <para>A movie's soundtrack is not the sound manager's in the original either, so movies are handed the
/// unwrapped backend.</para>
/// </summary>
public sealed class SoundCfgBackend : IAudioBackend {
	private readonly IAudioBackend _inner;
	private readonly SoundCfg _config;

	public SoundCfgBackend(IAudioBackend inner, SoundCfg config) {
		_inner = inner;
		_config = config;
	}

	/// <inheritdoc />
	public bool IsAvailable => _inner.IsAvailable;

	/// <inheritdoc />
	public int CreateSample(WaveSample sample) => _inner.CreateSample(sample.Downsample(_config.SampleRate));

	/// <inheritdoc />
	public int Start(int sample, float gain, float pan, float pitch, bool looping) =>
		_inner.Start(sample, gain, Pan(pan), pitch, looping);

	/// <inheritdoc />
	public void Stop(int play) => _inner.Stop(play);

	/// <inheritdoc />
	public bool IsPlaying(int play) => _inner.IsPlaying(play);

	/// <inheritdoc />
	public void SetGain(int play, float gain) => _inner.SetGain(play, gain);

	/// <inheritdoc />
	public void SetPan(int play, float pan) => _inner.SetPan(play, Pan(pan));

	/// <inheritdoc />
	public void SetPitch(int play, float ratio) => _inner.SetPitch(play, ratio);

	/// <inheritdoc />
	public void SetMasterGain(float gain) => _inner.SetMasterGain(gain);

	/// <inheritdoc />
	public void StopAll() => _inner.StopAll();

	/// <inheritdoc />
	public IAudioStream? OpenStream(int sampleRate, int channels) => _inner.OpenStream(sampleRate, channels);

	private float Pan(float pan) => _config.Mono ? 0f : pan;

	/// <inheritdoc />
	public void Dispose() => _inner.Dispose();
}
