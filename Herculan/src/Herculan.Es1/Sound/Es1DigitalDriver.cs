using Herculan.Engine.Audio;

namespace Herculan.Es1.Sound;

/// <summary>
/// What <c>DigiSound_Play</c> (<c>000509c4</c>) hands <c>HmiDigi_StartSample</c> (<c>0006dad9</c>): the start block
/// (docs/es1/formats/sound-samples.md#playing), its fields by their offsets there.
/// </summary>
/// <param name="Data"><c>+0x00</c>, the loaded file.</param>
/// <param name="Name">The file's name, which the block does not carry: this engine's key for the sample it registers with
/// the backend.</param>
/// <param name="Length"><c>+0x08</c>, the file's size.</param>
/// <param name="Repeats"><c>+0x0c</c>, the object's repeat count.</param>
/// <param name="Word10"><c>+0x10</c>, the object's <c>+0x3f</c>, 2.</param>
/// <param name="Volume"><c>+0x14</c>, 0–<c>0x7fff</c>.</param>
/// <param name="Id"><c>+0x18</c>, <c>0x1000</c> + the object slot.</param>
/// <param name="Done"><c>+0x1c</c>, the callback, (event, voice).</param>
/// <param name="Flags"><c>+0x28</c>.</param>
/// <param name="LoopEnd"><c>+0x30</c>, with flag <see cref="Es1DigitalDriver.LoopRangeFlag"/>.</param>
/// <param name="LoopStart"><c>+0x34</c>, with flag <see cref="Es1DigitalDriver.LoopRangeFlag"/>.</param>
/// <param name="Pan"><c>+0x40</c>, with flag <see cref="Es1DigitalDriver.PanFlag"/>.</param>
public readonly record struct Es1SampleStart(
	byte[] Data, string Name, int Length, ushort Repeats, int Word10, ushort Volume, int Id, Action<int, int>? Done, int Flags,
	int LoopEnd, int LoopStart, ushort Pan);

/// <summary>
/// <b>This engine's stand-in for HMI's digital driver</b> (docs/es1/formats/sound-samples.md#the-driver), whose mixing is
/// item 7's module E: its 32 voices, the calls the sound objects make on them, and their end, over the host's
/// <see cref="IAudioBackend"/>. A voice is busy from its start to its stop or its end, as retail's bit <c>0x8000</c> of the
/// voice record's word <c>+0x30</c> is; the five calls below read and write the voice as their retail counterparts read
/// and write the record.
///
/// <para>PLACEHOLDER (decision 26 of docs/herculan/plan-es1-first-mission.md, module E): the volume reaches the mixer as
/// its share of <c>0x7fff</c>, the pan as its distance from <c>0x8000</c>, the data as unsigned 8-bit mono at
/// <see cref="SampleRate"/>, and a repeat count <i>c</i> as <i>c</i> + 1 plays; the start block's <c>+0x10</c>, its loop
/// range and the voice word <c>+0x30</c>'s bit <c>0x1000</c> are not read. The driver's own curves, format and repeats
/// replace them in module E.</para>
/// </summary>
public sealed class Es1DigitalDriver {
	/// <summary>The driver's voices.</summary>
	public const int Voices = 32;

	/// <summary>The rate the manager's init block gives the driver, <c>000959f0</c>.</summary>
	public const int SampleRate = 11025;

	/// <summary>The start block's flag for a placed sound, whose pan is the block's <c>+0x40</c>.</summary>
	public const int PanFlag = 0x200;

	/// <summary>The start block's flag for a loop range at <c>+0x30</c> and <c>+0x34</c>.</summary>
	public const int LoopRangeFlag = 0x40;

	/// <summary><c>HmiDigi_IsVoiceDone</c>'s answer for a voice number past the last.</summary>
	public const int BadVoice = 10;

	/// <summary>The event the stand-in passes the start block's callback when a voice's last play ends.</summary>
	public const int DoneEvent = 2;

	private readonly IAudioBackend _backend;
	private readonly Dictionary<string, int> _samples = new(StringComparer.Ordinal);
	private readonly Voice[] _voices = new Voice[Voices];

	/// <param name="backend">The host's mixer; <see cref="NullAudioBackend"/> plays nothing.</param>
	public Es1DigitalDriver(IAudioBackend backend) {
		_backend = backend;
		for (int i = 0; i < Voices; i++) {
			_voices[i] = new Voice();
		}
	}

	/// <summary>
	/// <c>HmiDigi_StartSample</c> (<c>0006dad9</c>)(driver, <paramref name="block"/>): the block copied into the first voice
	/// not busy, and that voice's number; −1 with all <see cref="Voices"/> busy. Retail's 0 for a device id above
	/// <c>0xe105</c> is not taken, the stand-in having no device id.
	/// </summary>
	public int StartSample(in Es1SampleStart block) {
		int voice = Array.FindIndex(_voices, v => !v.Busy);
		if (voice < 0) {
			return -1;
		}

		var v = _voices[voice];
		v.Busy = true;
		v.Block = block;
		v.Volume = block.Volume;
		v.Repeats = (short)block.Repeats;
		v.Sample = Sample(block);
		Begin(v);
		return voice;
	}

	/// <summary>
	/// <c>HmiDigi_StopSample</c> (<c>0006dd7f</c>)(driver, <paramref name="voice"/>): a busy voice made free, its playback
	/// stopped. A number past the last does nothing.
	/// </summary>
	public void StopSample(int voice) {
		if ((uint)voice >= Voices || !_voices[voice].Busy) {
			return;
		}

		var v = _voices[voice];
		v.Busy = false;
		_backend.Stop(v.Play);
		v.Play = -1;
	}

	/// <summary>
	/// <c>HmiDigi_IsVoiceDone</c> (<c>0006df9d</c>)(driver, <paramref name="voice"/>): 0 while the voice is busy, 1 once it
	/// is free, <see cref="BadVoice"/> for a number past the last.
	/// </summary>
	public int IsVoiceDone(int voice) => (uint)voice >= Voices ? BadVoice : _voices[voice].Busy ? 0 : 1;

	/// <summary><c>HmiDigi_CountBusyVoices</c> (<c>0006e055</c>)(driver): how many of the <see cref="Voices"/> are busy.</summary>
	public int CountBusyVoices() => _voices.Count(v => v.Busy);

	/// <summary>
	/// <c>HmiDigi_SetSampleVolume</c> (<c>0006e0b3</c>)(driver, <paramref name="voice"/>, <paramref name="volume"/>): a busy
	/// voice's volume, answering the one it had; 0 for a voice not busy, and for a number past the last, which retail
	/// does not test, an engine guard.
	/// </summary>
	public int SetSampleVolume(int voice, ushort volume) {
		if ((uint)voice >= Voices || !_voices[voice].Busy) {
			return 0;
		}

		var v = _voices[voice];

		int old = (short)v.Volume;
		v.Volume = volume;
		_backend.SetGain(v.Play, Gain(volume));
		return old;
	}

	/// <summary>
	/// The voice record's word <c>+0x38</c>, the playing sample's repeats left, which <c>DigiSound_SetRepeats</c>
	/// (<c>00050cbc</c>) writes through the record's far pointer, busy or not. A number past the last does nothing, an
	/// engine guard.
	/// </summary>
	public void SetVoiceRepeats(int voice, short count) {
		if ((uint)voice < Voices) {
			_voices[voice].Repeats = count;
		}
	}

	/// <summary>
	/// This engine's stand-in for the driver's own service of its voices, which retail's runs from the timer: each busy
	/// voice whose playback has ended plays again while it has repeats left, one off them, and is otherwise made free and
	/// its block's callback given (<see cref="DoneEvent"/>, the voice). Call once a frame.
	/// </summary>
	public void Update() {
		for (int i = 0; i < Voices; i++) {
			var v = _voices[i];
			if (!v.Busy || _backend.IsPlaying(v.Play)) {
				continue;
			}

			if (v.Repeats > 0) {
				v.Repeats--;
				Begin(v);
				continue;
			}

			v.Busy = false;
			v.Play = -1;
			v.Block.Done?.Invoke(DoneEvent, i);
		}
	}

	/// <summary>A voice's playback started from its block, at its volume and the block's pan.</summary>
	private void Begin(Voice v) {
		float pan = (v.Block.Flags & PanFlag) != 0 ? (v.Block.Pan - 0x8000) / (float)0x8000 : 0;
		v.Play = v.Sample < 0 ? -1 : _backend.Start(v.Sample, Gain(v.Volume), pan, 1, false);
	}

	/// <summary>The backend's sample for the block's name, decoded and registered the first time a name plays.</summary>
	private int Sample(in Es1SampleStart block) {
		if (_samples.TryGetValue(block.Name, out int id)) {
			return id;
		}

		int length = Math.Min(block.Length, block.Data.Length);
		var pcm = new short[length];
		for (int i = 0; i < length; i++) {
			pcm[i] = (short)((block.Data[i] - 0x80) << 8);
		}

		id = length > 0 ? _backend.CreateSample(WaveSample.FromSamples(pcm, SampleRate)) : -1;
		_samples[block.Name] = id;
		return id;
	}

	private static float Gain(ushort volume) => Math.Min(volume, (ushort)0x7fff) / (float)0x7fff;

	/// <summary>One voice: retail's voice record as the stand-in keeps it.</summary>
	private sealed class Voice {
		public bool Busy;
		public Es1SampleStart Block;
		public ushort Volume;
		public short Repeats;
		public int Sample = -1;
		public int Play = -1;
	}
}
