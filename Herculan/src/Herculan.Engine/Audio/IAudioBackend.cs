namespace Herculan.Engine.Audio;

/// <summary>
/// The mixer underneath <see cref="SoundDirector"/>: it holds the decoded samples, hands out a
/// playback channel when one is asked for, and does nothing but play what it is told at the gain
/// and pan it is given.
///
/// <para><b>A sample is not a channel.</b> <see cref="CreateSample"/> registers one decoded
/// recording; <see cref="Start"/> begins a playback of it and returns a handle to <i>that
/// playback</i>. Starting the same sample twice gives two handles and two concurrent copies, which
/// is what the original does — see docs/retail/formats/audio.md, "A repeated play layers; it does not
/// restart". Channels are finite, so <see cref="Start"/> can refuse.</para>
///
/// <para><b>Deliberately not a 3D audio API.</b> Distance rolloff, panning and the audible cutoff
/// are the original's own rules and are computed in <see cref="SoundDirector.Place"/> before
/// anything reaches here — see docs/retail/formats/audio.md.</para>
///
/// <para>The abstraction will enable building different audio backends for different platforms. It
/// is also what lets the simulation and its tests run with no audio device
/// present, through <see cref="NullAudioBackend"/>.</para>
/// </summary>
public interface IAudioBackend : IDisposable {
	/// <summary>Whether a device actually opened. False means every call here is a no-op.</summary>
	bool IsAvailable { get; }

	/// <summary>
	/// Registers one decoded recording. Called once per catalog row that has a sample, and once per
	/// computer-voice clip on first use.
	/// </summary>
	/// <returns>A sample id for <see cref="Start"/>, or -1 when it could not be registered.</returns>
	int CreateSample(WaveSample sample);

	/// <summary>
	/// Begins a playback of <paramref name="sample"/> on a free channel.
	///
	/// <para>Gain, pan and pitch are passed in rather than set afterwards; they are the caller's own
	/// per-sound state, which <see cref="SoundDirector"/> keeps exactly as the original keeps it on 
	/// the voice record.</para>
	/// </summary>
	/// <param name="looping">Endless repetition.</param>
	/// <returns>
	/// A handle naming this playback, or -1 when the sample is unknown or every channel is busy.
	/// A refusal is not an error: the original's own <c>sosDIGIStartSample</c> fails the same way
	/// and <c>Sfx_Play</c> returns 0 without complaint.
	/// </returns>
	int Start(int sample, float gain, float pan, float pitch, bool looping);

	/// <summary>Stops one playback and frees its channel. A stale handle is ignored.</summary>
	void Stop(int play);

	/// <summary>Whether <paramref name="play"/> is still sounding. A stale handle answers false.</summary>
	bool IsPlaying(int play);

	/// <summary>Sets a running playback's gain, 0 to 1.</summary>
	void SetGain(int play, float gain);

	/// <summary>Sets a running playback's stereo position: -1 hard left, 0 centre, +1 hard right.</summary>
	void SetPan(int play, float pan);

	/// <summary>Sets a running playback's rate as a ratio, 1 being the recorded rate.</summary>
	void SetPitch(int play, float ratio);

	/// <summary>Overall output gain, 0 to 1.</summary>
	void SetMasterGain(float gain);

	/// <summary>Stops every playback at once.</summary>
	void StopAll();

	/// <summary>
	/// Opens a streamed voice of its own, outside the channel pool and untouched by
	/// <see cref="StopAll"/> — CD music, which never went through the original's mixer either.
	/// </summary>
	/// <returns>The stream, or null when there is no device to play it on.</returns>
	IAudioStream? OpenStream(int sampleRate, int channels);
}

/// <summary>
/// A voice fed PCM a block at a time, for audio too long to hand over as one sample. The caller
/// keeps <see cref="FreeBlocks"/> blocks queued; the stream plays them in order and starts itself on
/// the first block queued after it has run dry.
/// </summary>
public interface IAudioStream : IDisposable {
	/// <summary>How many more blocks can be queued now. Asking reclaims the blocks already played.</summary>
	int FreeBlocks { get; }

	/// <summary>
	/// Queues interleaved 16-bit PCM, tagged with the frame the block starts at in the caller's own
	/// numbering, which is what <see cref="Position"/> answers in.
	/// </summary>
	void Queue(ReadOnlySpan<short> pcm, long startFrame);

	/// <summary>
	/// The frame being heard, in the numbering <see cref="Queue"/> was given: the playing block's tag
	/// plus how far into it playback is. -1 when nothing is queued.
	/// </summary>
	long Position { get; }

	/// <summary>Stops and discards everything queued.</summary>
	void Stop();

	/// <summary>The stream's gain, 0 to 1, under the master gain.</summary>
	void SetGain(float gain);
}
