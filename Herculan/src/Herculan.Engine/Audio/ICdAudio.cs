namespace Herculan.Engine.Audio;

/// <summary>
/// The <c>Music_*</c> layer: Red Book CD audio, which in the original is driven straight through
/// Win32 <c>mciSendCommand</c> on device <c>cdaudio</c> and does not touch HMI SOS at all. See
/// docs/retail/formats/audio.md's "CD audio".
///
/// <para>It is an interface because the transport is not the original's decision to keep: this
/// engine plays the disc's audio through its own mixer (<see cref="StreamedCdAudio"/>), keeps
/// retail's MCI (<see cref="MciCdAudio"/>) for a drive that will not be read digitally, and runs
/// silent (<see cref="NullCdAudio"/>) with neither. Everything above it — the track choice, the
/// enable flag, the saved position across a suspend — is <see cref="SoundDirector"/>'s and is the
/// same against all three. <see cref="CdAudio.Open"/> chooses.</para>
/// </summary>
public interface ICdAudio : IDisposable {
	/// <summary>Whether a CD device opened and a disc with audio tracks is in it.</summary>
	bool IsAvailable { get; }

	/// <summary>
	/// <c>Music_IsIdle</c> (<c>00473b2c</c>) — whether the device is closed, which is the original's own idle test:
	/// every one of the four entry points leaves <c>Music_MciDeviceId</c> (<c>004a0e44</c>) at -1
	/// when it is not holding the device open.
	/// </summary>
	bool IsIdle { get; }

	/// <summary>Why the device is in the state it is, for the startup log.</summary>
	string Status { get; }

	/// <summary>
	/// <c>Music_PlayTrack</c> (<c>00473b3c</c>) — opens <c>cdaudio</c>, sets the TMSF time format and
	/// plays <paramref name="track"/> from its start to its own length.
	/// </summary>
	/// <returns>Whether the track started.</returns>
	bool PlayTrack(int track);

	/// <summary>
	/// <c>Music_ResumeAt</c> (<c>00473cc0</c>) — the same open/set/play, but starting from a position
	/// <see cref="GetPosition"/> saved rather than from a track boundary.
	/// </summary>
	bool ResumeAt(int packedTmsf);

	/// <summary>
	/// <c>Music_GetPosition</c> (<c>00473c38</c>) — the play head as a packed TMSF word, or 0 when
	/// the device is closed or the query fails. The value is opaque; it only ever goes back into
	/// <see cref="ResumeAt"/>.
	/// </summary>
	int GetPosition();

	/// <summary><c>Music_Stop</c> (<c>00473af4</c>) — <c>MCI_STOP</c> then <c>MCI_CLOSE</c>.</summary>
	void Stop();

	/// <summary>
	/// Keeps the track playing and looping; call once a frame. The original has no such call: it
	/// asks for <c>MCI_NOTIFY</c> and <c>sfxWndProc</c> (<c>00462294</c>) re-issues the play on
	/// <c>MM_MCINOTIFY</c>. <see cref="StreamedCdAudio.Update"/> feeds its stream here, and
	/// <see cref="MciCdAudio.Update"/> polls the drive for the end of the track.
	/// </summary>
	void Update();
}

/// <summary>
/// No music, and nothing goes wrong. What a host gets when <see cref="CdAudio.Open"/> finds no
/// disc, no track files and no rip cache, or under <c>--no-sound</c>.
/// </summary>
public sealed class NullCdAudio : ICdAudio {
	/// <summary>Creates one with the given account of why there is no CD.</summary>
	public NullCdAudio(string status = "no CD device") => Status = status;

	/// <inheritdoc />
	public bool IsAvailable => false;

	/// <inheritdoc />
	public bool IsIdle => true;

	/// <inheritdoc />
	public string Status { get; }

	/// <inheritdoc />
	public bool PlayTrack(int track) => false;

	/// <inheritdoc />
	public bool ResumeAt(int packedTmsf) => false;

	/// <inheritdoc />
	public int GetPosition() => 0;

	/// <inheritdoc />
	public void Stop() { }

	/// <inheritdoc />
	public void Update() { }

	/// <inheritdoc />
	public void Dispose() { }
}
