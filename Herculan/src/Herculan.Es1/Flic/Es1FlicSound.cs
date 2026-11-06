using Herculan.Engine.Audio;

namespace Herculan.Es1.Flic;

/// <summary>
/// <c>PLAYFLIC.EXE</c>'s digital sounds (docs/es1/formats/dfl-flic.md#the-sound): the list at <c>00015c2c</c> that
/// <c>Flic_DecodeSound</c> (<c>ES1PLAYFLIC 000023ec</c>) loads into, starts and stops, newest first, and each sound's
/// position, <c>DigiSample_Position</c> (<c>00008814</c>), which the sync on a sound waits on.
///
/// <para>A start plays the sound as unsigned 8-bit mono at <see cref="Rate"/>, the rate <c>SoundSys_Open</c>
/// (<c>00005f08</c>) gives the digital driver, through a stream of the host's mixer, whose frame being heard is the
/// position. PLACEHOLDER (decision 26 of docs/herculan/plan-es1-first-mission.md, as <see cref="Sound.Es1DigitalDriver"/>):
/// the volume <c>0x7fff</c> <c>Flic_DecodeSound</c> sets reaches the mixer as its share of <c>0x7fff</c>, full, and the
/// sound plays in the center.</para>
///
/// <para>With no stream, under the host's <c>--no-sound</c> or with no audio device, a started sound's position is
/// counted on the clock from its start at <see cref="Rate"/> bytes a second, as a sound card plays it, and nothing is
/// heard: an engine choice, so that the frames keep the timing a card gives them.</para>
/// </summary>
public sealed class Es1FlicSound : IDisposable {
	/// <summary>The digital driver's rate, <c>SoundSys_Open</c>'s init block (<c>00005c48</c>), and the bytes a second the sync counts.</summary>
	public const int Rate = 11025;

	/// <summary><c>Flic_DecodeSound</c>'s operations on a digital sound; 3 to 5 act on songs, which neither flic holds.</summary>
	public const int LoadOp = 0, StartOp = 1, StopOp = 2;

	/// <summary>The length of one block handed to a sound's stream, a quarter of a second.</summary>
	private const int BlockFrames = Rate / 4;

	private readonly IAudioBackend _backend;
	private readonly Func<string, byte[]?> _read;
	private readonly string _pathFlic;
	private readonly List<Sound> _list = [];
	private readonly short[] _block = new short[BlockFrames];
	private double _seconds;

	/// <param name="backend">The host's mixer; with <see cref="NullAudioBackend"/> no sound is heard.</param>
	/// <param name="pathFlic"><c>RESOURCE.CFG</c>'s <c>[FlicPlayer]</c> <c>pathFlic</c>, which a sound's name follows.</param>
	/// <param name="read">The bytes of the file a DOS path names, or null when it does not open.</param>
	public Es1FlicSound(IAudioBackend backend, string pathFlic, Func<string, byte[]?> read) {
		_backend = backend;
		_pathFlic = pathFlic;
		_read = read;
	}

	/// <summary>
	/// <c>Flic_DecodeSound</c> (<c>000023ec</c>) for a digital sound, its data a <c>uint32</c> operation, a <c>uint32</c>
	/// number and a NUL-ended name. 0: the file <c>pathFlic</c> + the name loaded whole and, when it holds a byte, put at
	/// the head of the list under the number; one that does not open prints <c>File_LoadWhole</c>'s message, which retail
	/// prints through <c>printf</c> while the flic's screen is up, and the host's error output carries here. 1: the first
	/// sound of the number started; 2: its playing stopped. Another operation, or a number not in the list, does nothing.
	/// </summary>
	public void Decode(byte[] data) {
		if (data.Length < 8) {
			return;
		}

		int op = BitConverter.ToInt32(data, 0), number = BitConverter.ToInt32(data, 4);
		switch (op) {
			case LoadOp:
				string path = _pathFlic + Name(data);
				byte[]? bytes = _read(path);
				if (bytes == null) {
					Console.Error.WriteLine($"FAIL: '{path}' no exist");
				} else if (bytes.Length != 0) {
					_list.Insert(0, new Sound(number, bytes));
				}

				break;
			case StartOp:
				if (Find(number) is { } start) {
					Start(start);
				}

				break;
			case StopOp:
				if (Find(number) is { Voice: { } voice }) {
					voice.Stop();
				}

				break;
		}
	}

	/// <summary>
	/// <c>DigiSample_Position</c> (<c>00008814</c>) of the first sound of <paramref name="number"/> in the list: the bytes of
	/// it heard since its last start; null with no sound of that number, where the sync's waits do not wait.
	///
	/// <para>Retail adds the DMA transfer's place in the driver's buffer to the driver's count, which is 0 once the voice is
	/// stopped; that place is not modelled, so a stopped sound answers 0. Engine guard: before any start retail reads the
	/// voice at the sample's <c>+0x74</c>, which nothing has written, and here the answer is 0. Engine guard: retail polls
	/// the position without pause and so meets each byte of the sound; here it is read once a host frame, and a sound heard
	/// to its end answers its length, so that the last frames' waits cannot pass it by between two reads.</para>
	/// </summary>
	public uint? Position(int number) => Find(number) is { } sound ? sound.Voice?.Position(_seconds, sound.Data.Length) ?? 0 : null;

	/// <summary>The clock moved on to <paramref name="seconds"/>, and every playing stream kept fed.</summary>
	public void Update(double seconds) {
		_seconds = seconds;
		foreach (var sound in _list) {
			foreach (var voice in sound.Voices) {
				voice.Feed(sound.Data, _block);
			}
		}
	}

	/// <summary><c>Machine_Close</c>'s close of the drivers (<c>00006110</c>), which ends every sound.</summary>
	public void Dispose() {
		foreach (var sound in _list) {
			foreach (var voice in sound.Voices) {
				voice.Stop();
			}
		}

		_list.Clear();
	}

	private Sound? Find(int number) => _list.Find(s => s.Number == number);

	/// <summary>
	/// <c>DigiSample_Start</c> (<c>000088cc</c>), <c>HmiDigi_StartSample</c> on a voice of its own: a sound started again
	/// plays on beside its earlier start, and the position follows the newest.
	/// </summary>
	private void Start(Sound sound) {
		var stream = _backend.IsAvailable ? _backend.OpenStream(Rate, 1) : null;
		stream?.SetGain(1f);
		var voice = new Voice(stream, _seconds);
		sound.Voices.Add(voice);
		voice.Feed(sound.Data, _block);
	}

	/// <summary>The name at the data's <c>+8</c>, up to its NUL or the chunk's end.</summary>
	private static string Name(byte[] data) {
		int end = Array.IndexOf(data, (byte)0, 8);
		return System.Text.Encoding.ASCII.GetString(data, 8, (end < 0 ? data.Length : end) - 8);
	}

	/// <summary>A list node: the number at <c>+0</c> and the sample object at <c>+4</c> with the loaded file.</summary>
	private sealed class Sound(int number, byte[] data) {
		public int Number { get; } = number;
		public byte[] Data { get; } = data;
		public List<Voice> Voices { get; } = [];
		public Voice? Voice => Voices.Count == 0 ? null : Voices[^1];
	}

	/// <summary>One start of a sound: its stream, or with none the clock time it started at.</summary>
	private sealed class Voice(IAudioStream? stream, double startSeconds) {
		private IAudioStream? _stream = stream;
		private int _queued;
		private bool _stopped;
		private uint _heard;

		public void Feed(byte[] data, short[] block) {
			while (_stream != null && _queued < data.Length && _stream.FreeBlocks > 0) {
				int count = Math.Min(block.Length, data.Length - _queued);
				for (int i = 0; i < count; i++) {
					block[i] = (short)((data[_queued + i] - 0x80) << 8);
				}

				_stream.Queue(block.AsSpan(0, count), _queued);
				_queued += count;
			}
		}

		public uint Position(double seconds, int length) {
			if (_stopped) {
				return 0;
			}

			if (_stream == null) {
				return (uint)Math.Clamp((long)((seconds - startSeconds) * Rate), 0, length);
			}

			long heard = _stream.Position;
			if (heard >= 0) {
				_heard = (uint)Math.Min(heard, length);
			} else if (_queued >= length) {
				_heard = (uint)length;
			}

			// Otherwise the stream has run dry before its end, and the position holds until it plays again.
			return _heard;
		}

		public void Stop() {
			_stopped = true;
			_stream?.Dispose();
			_stream = null;
		}
	}
}
