using Herculan.Engine.Audio;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="VolumeBackend"/>: the player's volumes scale what reaches the device, and a change reaches what is already
/// sounding, without an audio device.
/// </summary>
public class VolumeBackendTests {
	[Fact]
	public void EffectsVolumeScalesPlaybacksAndFollowsAChange() {
		var device = new FakeBackend();
		var volumes = new AudioVolumes { Effects = 0.5f };
		var backend = new VolumeBackend(device, volumes);

		int play = backend.Start(0, 0.8f, 0f, 1f, looping: true);
		Assert.Equal(0.4f, device.Gains[play], 3);

		// The playback's own gain moves, then the volume: the change is laid over the newest gain.
		backend.SetGain(play, 0.6f);
		volumes.Effects = 0.25f;
		backend.Update();
		Assert.Equal(0.15f, device.Gains[play], 3);
	}

	[Fact]
	public void MusicVolumeScalesStreamsNotPlaybacks() {
		var device = new FakeBackend();
		var volumes = new AudioVolumes { Music = 0.5f };
		var backend = new VolumeBackend(device, volumes);

		var stream = backend.OpenStream(44100, 2)!;
		Assert.Equal(0.5f, device.Stream.Gain, 3);
		int play = backend.Start(0, 1f, 0f, 1f, looping: false);
		Assert.Equal(1f, device.Gains[play], 3);

		stream.SetGain(0.8f);
		volumes.Music = 0.25f;
		backend.Update();
		Assert.Equal(0.2f, device.Stream.Gain, 3);
		Assert.Equal(1f, device.Gains[play], 3);
	}

	private sealed class FakeBackend : IAudioBackend {
		private int _next;

		public Dictionary<int, float> Gains { get; } = new();

		public FakeStream Stream { get; } = new();

		public bool IsAvailable => true;

		public int CreateSample(WaveSample sample) => 0;

		public int Start(int sample, float gain, float pan, float pitch, bool looping) {
			Gains[_next] = gain;
			return _next++;
		}

		public void Stop(int play) => Gains.Remove(play);

		public bool IsPlaying(int play) => Gains.ContainsKey(play);

		public void SetGain(int play, float gain) {
			if (Gains.ContainsKey(play)) {
				Gains[play] = gain;
			}
		}

		public void SetPan(int play, float pan) { }

		public void SetPitch(int play, float ratio) { }

		public void SetMasterGain(float gain) { }

		public void StopAll() => Gains.Clear();

		public IAudioStream? OpenStream(int sampleRate, int channels) => Stream;

		public void Update() { }

		public void Dispose() { }
	}

	private sealed class FakeStream : IAudioStream {
		public float Gain { get; private set; } = 1f;

		public int FreeBlocks => 0;

		public long Position => -1;

		public void Queue(ReadOnlySpan<short> pcm, long startFrame) { }

		public void Stop() { }

		public void SetGain(float gain) => Gain = gain;

		public void Dispose() { }
	}
}
