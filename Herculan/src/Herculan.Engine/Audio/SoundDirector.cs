using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Audio;

/// <summary>
/// DBSIM's own <c>Sound_*</c> layer: the rules that sit between a catalog id and the mixer —
/// variation rolls, the category split, the distance cutoff and the stereo pan. See
/// docs/retail/formats/audio.md for where each of them comes from.
///
/// <para><b>One record per catalog id; copies of it overlap.</b> The original allocates exactly one
/// <c>SFX</c> voice per row of <c>SOUNDS.STR</c> and keeps it for the mission, but that record is
/// bookkeeping rather than a channel: <c>Sfx_Play</c> (<c>00463f34</c>) never tests the voice's own
/// <c>0x100</c> playing flag and issues a fresh <c>sosDIGIStartSample</c> every call, so two
/// machines firing the same weapon in the same tick are heard twice. The record holds this id's
/// current volume, pan and pitch and the handle of the <b>newest</b> playback only — see
/// docs/retail/formats/audio.md, "A repeated play layers; it does not restart".</para>
///
/// <para>One consequence is worth knowing before it looks like a bug: because the settings are the
/// id's and not the copy's, placing a new copy retunes the previous one. <see cref="Place"/> writes
/// the volume and pan for the sound it is about to start, and <see cref="SetVolume"/> pushes them at
/// whatever handle the record currently names — which until <see cref="Start"/> runs is the copy
/// already sounding. The original does exactly this: <c>Sfx_SetVolume</c> (<c>00464514</c>) writes
/// the record and then applies it to the backend handle at <c>+0x24</c> whenever the record is
/// marked playing.</para>
/// </summary>
public sealed class SoundDirector : IDisposable {
	/// <summary>The pan value that is dead centre — HMI SOS's own, and the voice default.</summary>
	public const int PanCentre = 0x8000;

	/// <summary>A pitch of 1.0, as the 16.16 ratio the original stores.</summary>
	public const int PitchOne = 0x10000;

	private readonly SoundBank _bank;
	private readonly IAudioBackend _backend;
	private readonly SimRandom _random;
	private ICdAudio _cd = new NullCdAudio();

	// One record per catalog id, which is what the original keeps: the sample the row names, the
	// settings that row is currently carrying, and the handle of the LAST playback started from it.
	// Older copies of the same id go on sounding but are no longer addressable -- see Start.
	private readonly int[] _samples;
	private readonly int[] _current;
	private readonly float[] _gain;
	private readonly float[] _pan;
	private readonly float[] _pitch;
	private readonly int[] _repeatsLeft;

	// Who started each id's newest copy, when they said; see PlayAt's source. Not the original's.
	private readonly object?[] _source;
	private bool _suspended;
	private bool _disposed;

	/// <summary>
	/// Wires a bank to a backend and creates the per-id voices.
	/// </summary>
	/// <param name="bank">The catalog and its samples.</param>
	/// <param name="backend">Where sound goes. Pass a <see cref="NullAudioBackend"/> to run silent.</param>
	/// <param name="random">
	/// The generator the variation roll draws on. The original draws it from its second state block,
	/// which the comm boxes and message variants share and the simulation does not, so pass
	/// <see cref="Sim.SimWorld.PresentationRandom"/> rather than a private generator when there is a world.
	/// </param>
	public SoundDirector(SoundBank bank, IAudioBackend backend, SimRandom? random = null) {
		_bank = bank;
		_backend = backend;
		_random = random ?? new SimRandom(0);

		int count = bank.Catalog.Count;
		_samples = new int[count];
		_current = new int[count];
		_gain = new float[count];
		_pan = new float[count];
		_pitch = new float[count];
		_repeatsLeft = new int[count];
		_source = new object?[count];

		for (int id = 0; id < count; id++) {
			_samples[id] = bank.Sample(id) is { } sample ? backend.CreateSample(sample) : -1;
			_current[id] = -1;
			_pitch[id] = 1f;
		}
	}

	/// <summary>The catalog behind this director.</summary>
	public SoundCatalog Catalog => _bank.Catalog;

	/// <summary>Whether a device actually opened.</summary>
	public bool IsAvailable => _backend.IsAvailable;

	/// <summary>
	/// <c>Sound_MusicEnabled</c> (<c>0049f90c</c>) - the enable flag for catalog ids below
	/// <see cref="SoundId.FirstEffect"/>, and, once <see cref="CdTrack"/> is set, for the CD.
	/// </summary>
	public bool MusicEnabled { get; set; } = true;

	/// <summary>
	/// The CD player, or a <see cref="NullCdAudio"/> where there is none. The original reaches MCI
	/// through four <c>SFX</c>-level thunks (<c>Sfx_PlayMusicTrack</c>, <c>00464754</c>, and its three
	/// neighbours) rather than through the effect channels, which is why music sits behind
	/// <see cref="ICdAudio"/> here rather than in the catalog's voices.
	/// </summary>
	public ICdAudio Cd {
		get => _cd;
		set => _cd = value ?? new NullCdAudio();
	}

	/// <summary>
	/// <c>Music_CdTrack</c> (<c>0049f914</c>) - which Red Book track this mission plays, or 0 for
	/// none. <b>It is the switch between the two music paths</b>: every place that touches music
	/// tests it, taking the CD when it is set and the catalog's ten digital music rows when it is
	/// not. Those rows all name <c>battle1.wav</c>, which ships in no archive, so the second path is
	/// dead in retail and silent here.
	/// </summary>
	public int CdTrack { get; private set; }

	/// <summary>
	/// <c>Music_CdEnabled</c> (<c>0049f918</c>) - raised beside <see cref="CdTrack"/> when a mission
	/// session starts. <see cref="SuspendAll"/> reads it, and so does retail's <c>sfxWndProc</c>
	/// (<c>00462294</c>) before restarting a finished track, which <see cref="ICdAudio.Update"/>
	/// stands in for.
	/// </summary>
	public bool CdEnabled { get; private set; }

	/// <summary>
	/// <c>Music_SavedPosition</c> (<c>0049f91c</c>) - where the play head was when the music was last
	/// stopped by a mute or a suspend, as an opaque TMSF word. 0 means "start the track again".
	/// </summary>
	public int SavedMusicPosition { get; private set; }

	/// <summary>How many music tracks the mission music cycles over.</summary>
	public const int MissionTrackCount = 5;

	/// <summary>The lowest track number mission music uses; track 1 is the data track.</summary>
	public const int FirstMusicTrack = 2;

	/// <summary>
	/// The track a mission plays, from <c>Sim_InitMissionSession</c> (<c>004614fc</c>):
	/// <c>select % 5 + 2</c>, so tracks 2 to 6. <paramref name="select"/> is the value of DBSIM's own
	/// <c>-R</c> command-line switch (<c>Music_TrackSelect</c> (<c>004d25f7</c>), parsed by <c>atol</c> at <c>0045e824</c>),
	/// which is the only thing in DBSIM that chooses between the five. Retail's launcher passes a count of
	/// the missions flown so far, so the track rotates; see <c>docs/retail/command-line.md</c>.
	///
	/// <para>The original's remainder is a signed <c>IDIV</c>, so a negative <c>-R</c> would give it
	/// a track below 2 and <c>MCI_PLAY</c> an invalid one; the magnitude is taken here instead.</para>
	/// </summary>
	public static int MissionTrack(int select) =>
		System.Math.Abs(select % MissionTrackCount) + FirstMusicTrack;

	/// <summary>
	/// <c>Sim_InitMissionSession</c>'s music arm (<c>00461c86</c>-<c>00461cbc</c>): sets the track and
	/// the enable byte, then starts the track through <c>Sound_StartMissionMusic</c> (<c>00463038</c>), which plays only when
	/// <see cref="MusicEnabled"/> is up. The original guards the whole arm on the mission being a
	/// <b>non-training</b> one - see <see cref="World.ScriptDatHeader.TrainingMissionNumber"/> - so a
	/// training mission runs in silence.
	///
	/// <para>The original's unconditional <c>Sound_SetMusicEnabled(1)</c> at the end of the session
	/// setup, which turns music back on behind a MUSIC-off preference, is not reproduced: the row's
	/// setting stands (docs/retail/formats/audio.md, "The mission session overrides the MUSIC preference").</para>
	/// </summary>
	/// <param name="select">The <c>-R</c> value; see <see cref="MissionTrack"/>.</param>
	public void StartMissionMusic(int select = 0) {
		CdTrack = MissionTrack(select);
		CdEnabled = true;
		SavedMusicPosition = 0;

		if (MusicEnabled) {
			_cd.PlayTrack(CdTrack);
		}
	}

	/// <summary>
	/// Clears the mission's music and stops the disc. <c>Sim_InitMissionSession</c> has no
	/// counterpart - retail leaves the process - but a host that loads a second mission needs one.
	/// </summary>
	public void StopMissionMusic() {
		CdTrack = 0;
		CdEnabled = false;
		SavedMusicPosition = 0;
		_cd.Stop();
	}

	/// <summary>
	/// <c>Prefs_ApplyMusicOption</c> (<c>00459c98</c>), the MUSIC row's handler out of the option
	/// table <c>Prefs_OptionHandlers</c> (<c>004d2060</c>): stores the flag and then unmutes or mutes — unless
	/// <paramref name="initialising"/>, which is <c>PrefsInitInProgress</c> and skips the second half
	/// so that reading the file cannot drive the mixer.
	/// </summary>
	public void ApplyMusicOption(bool enabled, bool initialising = false) {
		MusicEnabled = enabled;
		if (initialising) {
			return;
		}

		if (enabled) {
			UnmuteMusic();
		} else {
			MuteMusic();
		}
	}

	/// <summary>
	/// <c>Prefs_ApplySoundsOption</c> (<c>00459c6c</c>), the SOUNDS row's handler — the MUSIC one
	/// with the effects half of the catalog in place of the music half.
	/// </summary>
	public void ApplySoundsOption(bool enabled, bool initialising = false) {
		EffectsEnabled = enabled;
		if (initialising) {
			return;
		}

		if (enabled) {
			UnmuteEffects();
		} else {
			MuteEffects();
		}
	}

	/// <summary>
	/// <c>Sound_MuteEffects</c> (<c>00462cd8</c>) — zeroes the volume of every id from
	/// <see cref="SoundId.FirstEffect"/> up and clears the enable flag, so what is playing falls
	/// silent and what starts later starts at zero.
	/// </summary>
	public void MuteEffects() {
		for (int id = SoundId.FirstEffect; id < _gain.Length; id++) {
			SetVolume(id, 0);
		}

		EffectsEnabled = false;
	}

	/// <summary>
	/// <c>Sound_UnmuteEffects</c> (<c>00462df8</c>) — the counterpart, restoring each id's own
	/// attribute volume. A positional sound still playing comes back at that volume rather than at its
	/// distance's, until it is next placed; the original does the same.
	/// </summary>
	public void UnmuteEffects() {
		for (int id = SoundId.FirstEffect; id < _gain.Length; id++) {
			if (Entry(id) is { } entry) {
				SetVolume(id, AttributeVolume(entry));
			}
		}

		EffectsEnabled = true;
	}

	/// <summary>
	/// <c>Sound_MuteMusic</c> (<c>00462c74</c>). With a CD track set it saves the play position and
	/// stops the disc; without one it zeroes the ten digital music rows' volumes instead.
	/// </summary>
	public void MuteMusic() {
		SavedMusicPosition = 0;

		if (CdTrack == 0) {
			for (int id = 0; id < SoundId.FirstEffect && id < _gain.Length; id++) {
				SetVolume(id, 0);
			}
		} else {
			SavedMusicPosition = _cd.GetPosition();
			_cd.Stop();
		}

		MusicEnabled = false;
	}

	/// <summary>
	/// <c>Sound_UnmuteMusic</c> (<c>00462d54</c>) - the exact counterpart: the disc resumes from the
	/// saved position, or restarts the track when there is none.
	/// </summary>
	public void UnmuteMusic() {
		if (CdTrack == 0) {
			for (int id = 0; id < SoundId.FirstEffect && id < _gain.Length; id++) {
				if (Entry(id) is { } entry) {
					SetVolume(id, AttributeVolume(entry));
				}
			}
		} else if (SavedMusicPosition == 0) {
			_cd.PlayTrack(CdTrack);
		} else {
			_cd.ResumeAt(SavedMusicPosition);
		}

		MusicEnabled = true;
	}

	/// <summary>
	/// <c>Sound_EffectsEnabled</c> (<c>0049f910</c>) — the enable flag for ids from
	/// <see cref="SoundId.FirstEffect"/> up.
	/// </summary>
	public bool EffectsEnabled { get; set; } = true;

	/// <summary>
	/// The options-screen 0-2 detail value (<c>004d1fc7</c>) that
	/// <see cref="SoundCatalog.Entry.RequestsPerPlay"/> scales against. 2 lets every request through;
	/// 0 halves the rate the row already sets. Clamped on the way in
	/// because it arrives from <c>prefs.cfg</c>, which is a byte array a player can edit and the
	/// original reads back without validating — and a value above 2 would make the interval negative.
	/// </summary>
	public int DetailSetting {
		get => _detailSetting;
		set => _detailSetting = Math.Clamp(value, 0, 2);
	}

	private int _detailSetting = 2;

	/// <summary>Where the listener is — the camera, as it is in the original.</summary>
	public Vec3i ListenerPosition { get; set; }

	/// <summary>Which way the listener faces, as a binary angle, 0 being <c>+Y</c>.</summary>
	public int ListenerHeading { get; set; }

	/// <summary>Overall output gain, 0 to 1. Not the original's; a host-level volume control.</summary>
	public float MasterVolume {
		get => _masterVolume;
		set {
			_masterVolume = Math.Clamp(value, 0f, 1f);
			_backend.SetMasterGain(_masterVolume);
		}
	}

	private float _masterVolume = 1f;

	/// <summary>
	/// <c>Sound_Play</c> (<c>0046272c</c>) — the non-positional play. Rolls the variation count,
	/// sets the volume the row's own attributes give it, and starts the voice.
	/// </summary>
	public void Play(int id) {
		id = RollVariation(id);
		if (Entry(id) is not { } entry) {
			return;
		}

		SetVolume(id, CategoryEnabled(id) ? AttributeVolume(entry) : 0);
		Start(id, entry);
		_source[id] = null;
	}

	/// <summary>
	/// <c>Sound_PlayAt</c> (<c>004627dc</c>) — the positional play. Rolls the variation count, places
	/// the sound, and starts it only if <see cref="Place"/> found it audible.
	/// </summary>
	/// <param name="id">The catalog id.</param>
	/// <param name="position">Where the sound is, in world units.</param>
	/// <param name="reach">A volume and range to use instead of the row's; null is the original's play.</param>
	/// <param name="source">
	/// Who is playing it, so that only they can move this copy later through
	/// <see cref="UpdatePosition"/>. Not the original's; null leaves the copy movable by anyone, as
	/// the original's record is.
	/// </param>
	/// <returns>Whether the sound was close enough to be played at all.</returns>
	public bool PlayAt(int id, Vec3i position, SoundReach? reach = null, object? source = null) {
		id = RollVariation(id);
		if (Entry(id) is not { } entry) {
			return false;
		}

		if (!Place(id, position, reach)) {
			return false;
		}

		Start(id, entry);
		_source[id] = source;
		return true;
	}

	/// <summary>
	/// <c>Sound_Place</c> (<c>00462898</c>) — works out one sound's volume and pan from where it is
	/// relative to the listener, and reports whether it is audible at all.
	///
	/// <para>The rolloff is the original's, including its oddity: the falloff divides by the cutoff
	/// distance rather than by the width of the band between the two ranges, so a source sitting
	/// exactly at <see cref="SoundCatalog.Entry.MinRange"/> is already attenuated rather than at full
	/// volume.</para>
	/// </summary>
	/// <param name="id">The catalog id.</param>
	/// <param name="position">Where the sound is, in world units.</param>
	/// <param name="reach">A volume and range to use instead of the row's; null is the original's.</param>
	/// <returns>False when the source is past the row's cutoff, in which case nothing is played.</returns>
	public bool Place(int id, Vec3i position, SoundReach? reach = null) {
		if (Entry(id) is not { } entry) {
			return false;
		}

		var offset = position - ListenerPosition;
		int distance = SimMath.FastMagnitude3D(offset.X, offset.Y, offset.Z);

		int minRange = reach?.MinRange ?? entry.MinRange * SoundCatalog.RangeUnit;
		int maxRange = reach?.MaxRange ?? entry.MaxRange * SoundCatalog.RangeUnit;

		int volume;
		if (!CategoryEnabled(id)) {
			volume = 0;
		} else if (distance > maxRange) {
			volume = 0;
		} else {
			volume = SimMath.Q16Multiply(reach?.Volume ?? entry.Volume, SoundCatalog.VolumeTrim);
			if (distance >= minRange && maxRange > 0) {
				volume = (maxRange - distance) * volume / maxRange;
			}
		}

		SetVolume(id, volume * entry.CategoryVolume / 100);

		if (volume == 0) {
			return false;
		}

		// The listener's own frame: +Y is where it faces, +X is to its right. The original takes the
		// camera-transformed point's first two components and hands them to Math_Atan2Bam, which
		// takes its arguments as (x, y) — so those components are x = right, y = forward, the
		// ordinary view-space assignment. SimTrig.Atan2 is the same function with the conventional
		// atan2(y, x) parameter order, hence the swap here.
		int forward = SimMath.Q14Multiply(offset.X, BinaryAngle.Sin(-ListenerHeading))
			+ SimMath.Q14Multiply(offset.Y, BinaryAngle.Cos(-ListenerHeading));
		int right = SimMath.Q14Multiply(offset.X, BinaryAngle.Cos(-ListenerHeading))
			- SimMath.Q14Multiply(offset.Y, BinaryAngle.Sin(-ListenerHeading));

		// Doubling the bearing sweeps the whole pan range over half a turn, which is what makes the
		// image mirror front to back — a stereo field cannot tell the two apart anyway.
		//
		// Two deliberate deviations, both where the original's bearing is zero, which its 16-bit
		// front-half formula `(ushort)(bearing * -2)` turns into 0, the hard-left end.
		//
		// A source precisely abeam on the right: every neighbouring bearing on both sides lands at
		// the hard-right end, and the continuous value there is 0x10000, so it is only the truncation
		// that turns it into its opposite. Reproducing it would put an audible snap to the far channel
		// on any sound passing dead abeam, and this engine's placement reaches the exact zero far more
		// often than the original's does: DBSIM's forward component comes out of a full camera matrix
		// carrying pitch and roll, where an exact zero is a coincidence, and this one comes out of a
		// plain horizontal rotation, where it is simply what abeam means. It is given hard right.
		//
		// A source with no horizontal offset at all, which has no bearing: Math_Atan2Bam answers 0
		// for (0, 0), so the original pans it hard left. The drop pod's two sounds, played at the
		// camera itself, reach it every time; see docs/retail/formats/audio.md, "A sound played at
		// the camera". It is centred instead.
		int pan;
		if (forward == 0 && right == 0) {
			pan = PanCentre;
		} else {
			int bearing = SimTrig.Atan2(forward, right);
			pan = bearing < 0x8000
				? Math.Min(0x10000 - 2 * bearing, 0xffff)
				: (2 * bearing) & 0xffff;
		}

		SetPan(id, pan);
		return true;
	}

	/// <summary>
	/// <c>Sound_UpdatePosition</c> (<c>00462878</c>) — re-places a sound that is already running,
	/// without starting it. The looping engine hum and the flamer are what the original uses it for.
	/// </summary>
	/// <param name="id">The catalog id.</param>
	/// <param name="position">Where the sound now is, in world units.</param>
	/// <param name="reach">The volume and range it was started with, if not the row's.</param>
	/// <param name="source">
	/// Who is moving it. When given, the move happens only if the same caller started the id's newest
	/// copy — see <see cref="PlayAt"/> — so an older copy's owner cannot drag a newer one away.
	/// </param>
	public void UpdatePosition(int id, Vec3i position, SoundReach? reach = null, object? source = null) {
		if (source != null && (id < 0 || id >= _source.Length || !ReferenceEquals(_source[id], source))) {
			return;
		}

		Place(id, position, reach);
	}

	/// <summary><c>Sound_Stop</c> (<c>004629c0</c>).</summary>
	/// <remarks>
	/// Stops the newest copy only. An id sounding more than once has older copies the record no
	/// longer names, and they play out — the original loses them the same way, because its voice
	/// record holds one backend handle (<c>+0x24</c>) and a fresh start overwrites it. Nothing in
	/// the game stops a one-shot, so the loss is unreachable in practice; the loops that <i>are</i>
	/// stopped (the engine hum, the flamer) are started once and never overlap themselves.
	/// </remarks>
	public void Stop(int id) {
		if (id < 0 || id >= _current.Length) {
			return;
		}

		_repeatsLeft[id] = 0;
		_backend.Stop(_current[id]);
		_current[id] = -1;
	}

	/// <summary>
	/// <c>Sound_IsPlaying</c> (<c>004629ec</c>). Used by the original to avoid restarting a loop that
	/// is already running — the torso servo does exactly that.
	/// </summary>
	public bool IsPlaying(int id) =>
		id >= 0 && id < _current.Length
			&& (_backend.IsPlaying(_current[id]) || _repeatsLeft[id] > 0);

	/// <summary>
	/// <c>Sound_SetPitch</c> (<c>00463010</c>) — the playback rate as the original's 16.16 ratio.
	/// <c>Cockpit_PowerUpSound</c> (<c>004328cc</c>) uses it to drop the engine loop to <see cref="SoundId.EngineLoopPitch"/>.
	/// </summary>
	public void SetPitch(int id, int ratioQ16) {
		if (id < 0 || id >= _pitch.Length) {
			return;
		}

		_pitch[id] = ratioQ16 / (float)PitchOne;
		_backend.SetPitch(_current[id], _pitch[id]);
	}

	/// <summary>
	/// <c>Sound_ConsumeRequest</c> (<c>004626c4</c>) — spends one play request against
	/// <see cref="SoundCatalog.Entry.RequestsPerPlay"/> and says whether this one is the request that
	/// sounds, so that a sound fired by many objects in the same tick is heard once rather than once
	/// per object.
	///
	/// <para>The count is per catalog row and wraps at 0x0f, exactly as the original's runtime
	/// attribute byte does. It advances on every call, so two calls for the same id need not answer
	/// the same, and a call made speculatively spends a request.</para>
	/// </summary>
	/// <remarks>UNUSED. Claude could not find any callers for the equivalent feature in DBSIM.
	/// Implemented for fidelity and in case we discover callers later.</remarks>
	/// <returns>Whether this request is the one that plays.</returns>
	public bool ConsumeRequest(int id) {
		if (Entry(id) is not { } entry) {
			return false;
		}

		if (entry.RequestCount == 0x0f) {
			entry.RequestCount = 0;
		}

		int interval = (2 - DetailSetting) * entry.RequestsPerPlay;
		if (interval == 0) {
			return true;
		}

		entry.RequestCount++;
		return entry.RequestCount % interval == 0;
	}

	/// <summary>
	/// <c>Sound_SetCategoryVolume</c> (<c>00462f5c</c>) — the per-row scale in percent that every
	/// volume this row computes is multiplied through.
	/// </summary>
	public void SetCategoryVolume(int id, byte percent) {
		if (Entry(id) is not { } entry) {
			return;
		}

		entry.CategoryVolume = percent;
		SetVolume(id, SimMath.Q16Multiply(entry.Volume, SoundCatalog.VolumeTrim) * percent / 100);
	}

	/// <summary>
	/// <c>Sound_SuspendAll</c> (<c>00463078</c>) — stops everything, marking for the matching
	/// <see cref="ResumeAll"/> only the ids that loop forever and are playing. A one-shot or a
	/// finite repeat cut off by the suspend is not restarted, and its outstanding repeats are
	/// dropped. The original does this when the window loses focus. See docs/retail/formats/audio.md,
	/// "Mute, suspend and resume".
	/// </summary>
	public void SuspendAll() {
		_suspended = true;

		// The CD arm comes first, as it does in the original. The position is saved only when music is
		// on; the disc stops either way.
		SavedMusicPosition = 0;
		if (CdTrack != 0 && CdEnabled) {
			if (MusicEnabled) {
				SavedMusicPosition = _cd.GetPosition();
			}

			_cd.Stop();
		}

		for (int id = 0; id < _samples.Length; id++) {
			var entry = _bank.Catalog.Entries[id];
			entry.WasPlaying = entry.LoopCount == 0 && _backend.IsPlaying(_current[id]);
			_repeatsLeft[id] = 0;
			_backend.Stop(_current[id]);
			_current[id] = -1;
		}
	}

	/// <summary><c>Sound_ResumeAll</c> (<c>00463134</c>) — replays what <see cref="SuspendAll"/> stopped.</summary>
	public void ResumeAll() {
		_suspended = false;

		for (int id = 0; id < _samples.Length; id++) {
			var entry = _bank.Catalog.Entries[id];
			if (entry.WasPlaying) {
				Start(id, entry);
			}

			entry.WasPlaying = false;
		}

		// Unlike the suspend, the resume does not consult Music_CdEnabled: it tests the enable flag
		// alone, and a mission that never set a track resumes nothing because PlayTrack(0) does
		// nothing.
		if (!MusicEnabled) {
			return;
		}

		if (SavedMusicPosition == 0) {
			_cd.PlayTrack(CdTrack);
		} else {
			_cd.ResumeAt(SavedMusicPosition);
		}
	}

	/// <summary>Stops everything at once — <c>Sfx_StopAll</c> (<c>004647dc</c>).</summary>
	public void StopAll() {
		Array.Clear(_repeatsLeft);
		Array.Fill(_current, -1);
		_backend.StopAll();
	}

	/// <summary>
	/// Services the finite repeat counts. Call once a frame.
	///
	/// <para>Attribute byte 0 can ask for a sound to play a fixed number of times — the three cockpit
	/// alerts all ask for five — and no backend this targets expresses that, so the repeats are
	/// re-triggered here as each pass finishes. A count of 0 is endless and is the backend's own
	/// looping flag instead, so it never reaches this.</para>
	///
	/// <para>It does nothing between a <see cref="SuspendAll"/> and its <see cref="ResumeAll"/>.
	/// The suspend drops every outstanding repeat, as the original's <c>Sfx_StopAll</c> ends a
	/// finite loop for good; the gate keeps a repeat requested during the pause from being
	/// serviced until the resume.</para>
	/// </summary>
	public void Update() {
		// Standing in for the MM_MCINOTIFY that re-issues the play in retail; see ICdAudio.Update.
		// It runs across a suspend too, where it does nothing, because the music is stopped.
		_cd.Update();

		if (_suspended) {
			return;
		}

		for (int id = 0; id < _samples.Length; id++) {
			if (_repeatsLeft[id] <= 0 || _samples[id] < 0) {
				continue;
			}

			if (_backend.IsPlaying(_current[id])) {
				continue;
			}

			_repeatsLeft[id]--;
			if (_repeatsLeft[id] > 0) {
				_current[id] = _backend.Start(_samples[id], _gain[id], _pan[id], _pitch[id], false);
			}
		}
	}

	/// <summary>
	/// The variation roll every play does first — <c>id + rand(count)</c> when the row stands for
	/// more than one consecutive id, which is how a single impact id picks between
	/// <c>impacts2</c>, <c>3</c> and <c>5</c>.
	/// </summary>
	private int RollVariation(int id) {
		if (Entry(id) is not { VariationCount: > 1 } entry) {
			return id;
		}

		return id + _random.NextBelow(entry.VariationCount);
	}

	/// <summary>
	/// Begins one playback and records its handle as this id's current one.
	///
	/// <para>Nothing checks whether the id is already sounding, which is the whole of the original's
	/// behaviour here: <c>Sfx_Play</c> (<c>00463f34</c>) never tests its voice's <c>0x100</c> playing
	/// flag and issues a fresh <c>sosDIGIStartSample</c> every call, so the copies overlap. See
	/// docs/retail/formats/audio.md, "A repeated play layers; it does not restart".</para>
	/// </summary>
	private void Start(int id, SoundCatalog.Entry entry) {
		if (_samples[id] < 0) {
			return;
		}

		// Attribute byte 0 is a repeat count, so only 0 -- play forever -- is the backend's own
		// looping flag. A finite count is tracked here and re-triggered by Update.
		_repeatsLeft[id] = entry.LoopCount == 0 ? 0 : entry.LoopCount;
		_current[id] = _backend.Start(_samples[id], _gain[id], _pan[id], _pitch[id], entry.LoopCount == 0);
	}

	/// <summary>
	/// Writes this id's volume and pushes it to the copy that is running, if one is. Both halves
	/// matter: a play sets them before <see cref="Start"/> claims a channel, and
	/// <see cref="UpdatePosition"/> sets them on a channel already going.
	/// </summary>
	private void SetVolume(int id, int volume0To100) {
		if (id < 0 || id >= _gain.Length) {
			return;
		}

		_gain[id] = Math.Clamp(volume0To100 / 100f, 0f, 1f);
		_backend.SetGain(_current[id], _gain[id]);
	}

	private void SetPan(int id, int pan0To65535) {
		if (id < 0 || id >= _pan.Length) {
			return;
		}

		_pan[id] = (pan0To65535 - PanCentre) / (float)PanCentre;
		_backend.SetPan(_current[id], _pan[id]);
	}

	/// <summary>The row's authored volume after the loader's headroom trim and its category scale.</summary>
	private static int AttributeVolume(SoundCatalog.Entry entry) =>
		SimMath.Q16Multiply(entry.Volume, SoundCatalog.VolumeTrim) * entry.CategoryVolume / 100;

	private bool CategoryEnabled(int id) =>
		SoundCatalog.IsMusic(id) ? MusicEnabled : EffectsEnabled;

	private SoundCatalog.Entry? Entry(int id) =>
		_bank.Catalog[id] is { HasAttributes: true } entry ? entry : null;

	/// <inheritdoc />
	public void Dispose() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		_cd.Dispose();
		_backend.Dispose();
	}
}
