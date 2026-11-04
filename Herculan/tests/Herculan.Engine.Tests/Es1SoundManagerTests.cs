using Herculan.Engine.Audio;
using Herculan.Es1.Sound;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The sound manager's loading and voices (docs/es1/formats/sound-samples.md#naming-and-loading,
/// docs/es1/formats/sound-samples.md#playing), over names whose files are only their sizes.
/// </summary>
public class Es1SoundManagerTests {
	/// <summary>
	/// Loading past the limit unloads the stopped sound of the highest priority at least the loader's, the larger on a tie.
	/// Catches the priority comparison inverted, the tie going to the smaller or the first, and the loop unloading more
	/// than it needs.
	/// </summary>
	[Fact]
	public void LoadingPastTheLimitUnloadsTheHighestPriorityLargerSound() {
		var manager = Manager(("a", 10), ("b", 10), ("c", 20), ("d", 10));
		manager.SetMemoryLimit(40);
		var a = manager.CreateObject(2, 3, 0)!;
		var b = manager.CreateObject(2, 0, 1)!;
		var c = manager.CreateObject(2, 3, 2)!;
		var d = manager.CreateObject(2, 1, 3)!;
		Assert.True(a.LoadSample() && b.LoadSample() && c.LoadSample());

		Assert.True(d.LoadSample());

		Assert.Equal([true, true, false, true], new[] { a, b, c, d }.Select(s => (s.Flags & Es1SoundObject.Loaded) != 0));
		Assert.Equal(30u, manager.BytesLoaded);
	}

	/// <summary>
	/// Two objects on one name hold one copy of its data: the first unload keeps it and its bytes, the last frees both.
	/// Catches the reference count skipped, the size counted per object, and the data freed on the first unload.
	/// </summary>
	[Fact]
	public void ASharedNamesDataIsFreedByItsLastUnload() {
		var manager = Manager(("a", 10));
		var first = manager.CreateObject(2, 1, 0)!;
		var second = manager.CreateObject(2, 1, 0)!;
		Assert.True(first.LoadSample() && second.LoadSample());
		Assert.Equal(10u, manager.BytesLoaded);

		first.Destroy();
		Assert.Equal(10u, manager.BytesLoaded);
		Assert.NotNull(manager.Data(0));

		second.Destroy();
		Assert.Equal(0u, manager.BytesLoaded);
		Assert.Null(manager.Data(0));
		Assert.Null(manager.GetObject(first.Slot));
	}

	/// <summary>
	/// With more than 30 voices busy, a play first stops the quietest digital sound. Catches the comparison picking the
	/// loudest, the stop skipped, and the threshold taken at 30 rather than past it.
	/// </summary>
	[Fact]
	public void PastThirtyBusyVoicesAPlayStopsTheQuietestSound() {
		var manager = Manager(("a", 10));
		var sounds = Enumerable.Range(0, 32).Select(_ => manager.CreateObject(2, 1, 0)!).ToArray();
		foreach (var sound in sounds) {
			sound.SetVolume(50);
		}

		sounds[5].SetVolume(10);
		Assert.All(sounds.Take(31), sound => Assert.True(sound.Play()));
		Assert.Equal(31, manager.Driver.CountBusyVoices());

		Assert.True(sounds[31].Play());

		Assert.False(sounds[5].IsPlaying());
		Assert.True(sounds[31].IsPlaying());
		Assert.Equal(31, manager.Driver.CountBusyVoices());
	}

	/// <summary>A manager over <c>hmi\&lt;name&gt;.sfx</c> files of the given sizes, on a backend whose plays never end.</summary>
	private static Es1SoundManager Manager(params (string Name, int Size)[] files) {
		var sizes = files.ToDictionary(f => f.Name + ".sfx", f => f.Size);
		var manager = new Es1SoundManager(
			(folder, name) => folder == "hmi" && sizes.TryGetValue(name, out int size) ? new byte[size] : null,
			new Es1DigitalDriver(new EndlessBackend()));
		manager.SetSampleNames(files.Select(f => $"hmi\\{f.Name}.sfx").ToArray());
		return manager;
	}

	/// <summary>A backend whose every play lasts until it is stopped.</summary>
	private sealed class EndlessBackend : IAudioBackend {
		private readonly HashSet<int> _playing = [];
		private int _samples, _plays;

		public bool IsAvailable => true;
		public int CreateSample(WaveSample sample) => _samples++;

		public int Start(int sample, float gain, float pan, float pitch, bool looping) {
			_playing.Add(_plays);
			return _plays++;
		}

		public void Stop(int play) => _playing.Remove(play);
		public bool IsPlaying(int play) => _playing.Contains(play);

		public void SetGain(int play, float gain) {
		}

		public void SetPan(int play, float pan) {
		}

		public void SetPitch(int play, float ratio) {
		}

		public void SetMasterGain(float gain) {
		}

		public void StopAll() => _playing.Clear();
		public IAudioStream? OpenStream(int sampleRate, int channels) => null;

		public void Update() {
		}

		public void Dispose() {
		}
	}
}
