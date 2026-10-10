namespace Herculan.Engine.Audio;

/// <summary>
/// DBSIM's <c>Music_*</c> layer over the CD player: which Red Book track the mission plays, whether the session
/// enabled it, and where the play head was when a mute or a suspend stopped it. <see cref="SoundDirector"/>'s
/// mute, suspend and resume take this arm when <see cref="Track"/> is set and the catalog's ten digital music
/// rows when it is not.
/// </summary>
public sealed class CdMusic : IDisposable {
	private ICdAudio _cd = new NullCdAudio();

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
	public int Track { get; private set; }

	/// <summary>
	/// <c>Music_CdEnabled</c> (<c>0049f918</c>) - raised beside <see cref="Track"/> when a mission
	/// session starts. <see cref="SoundDirector.SuspendAll"/> reads it, and so does retail's <c>sfxWndProc</c>
	/// (<c>00462294</c>) before restarting a finished track, which <see cref="ICdAudio.Update"/>
	/// stands in for.
	/// </summary>
	public bool Enabled { get; private set; }

	/// <summary>
	/// <c>Music_SavedPosition</c> (<c>0049f91c</c>) - where the play head was when the music was last
	/// stopped by a mute or a suspend, as an opaque TMSF word. 0 means "start the track again".
	/// </summary>
	public int SavedPosition { get; private set; }

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
	/// <paramref name="musicEnabled"/> (<see cref="SoundDirector.MusicEnabled"/>) is up. The original guards the whole arm on the mission being a
	/// <b>non-training</b> one - see <see cref="World.ScriptDatHeader.TrainingMissionNumber"/> - so a
	/// training mission runs in silence.
	///
	/// <para>The original's unconditional <c>Sound_SetMusicEnabled(1)</c> at the end of the session
	/// setup, which turns music back on behind a MUSIC-off preference, is not reproduced: the row's
	/// setting stands (docs/retail/simulation/audio.md, "The mission session overrides the MUSIC preference").</para>
	/// </summary>
	/// <param name="select">The <c>-R</c> value; see <see cref="MissionTrack"/>.</param>
	/// <param name="musicEnabled"><see cref="SoundDirector.MusicEnabled"/>.</param>
	public void StartMission(int select, bool musicEnabled) {
		Track = MissionTrack(select);
		Enabled = true;
		SavedPosition = 0;

		if (musicEnabled) {
			_cd.PlayTrack(Track);
		}
	}

	/// <summary>
	/// Clears the mission's music and stops the disc. <c>Sim_InitMissionSession</c> has no
	/// counterpart - retail leaves the process - but a host that loads a second mission needs one.
	/// </summary>
	public void StopMission() {
		Track = 0;
		Enabled = false;
		SavedPosition = 0;
		_cd.Stop();
	}

	/// <summary>
	/// <c>Sound_MuteMusic</c> (<c>00462c74</c>)'s CD arm: saves the play position and stops the disc.
	/// </summary>
	/// <returns>False with no track set, where the mute is the digital rows' instead.</returns>
	internal bool Mute() {
		SavedPosition = 0;

		if (Track == 0) {
			return false;
		}

		SavedPosition = _cd.GetPosition();
		_cd.Stop();
		return true;
	}

	/// <summary>
	/// <c>Sound_UnmuteMusic</c> (<c>00462d54</c>)'s CD arm: the disc resumes from the saved position, or
	/// restarts the track when there is none.
	/// </summary>
	/// <returns>False with no track set, where the unmute is the digital rows' instead.</returns>
	internal bool Unmute() {
		if (Track == 0) {
			return false;
		}

		if (SavedPosition == 0) {
			_cd.PlayTrack(Track);
		} else {
			_cd.ResumeAt(SavedPosition);
		}

		return true;
	}

	/// <summary>
	/// <c>Sound_SuspendAll</c> (<c>00463078</c>)'s CD arm, which comes first. The position is saved only when
	/// music is on; the disc stops either way.
	/// </summary>
	internal void Suspend(bool musicEnabled) {
		SavedPosition = 0;
		if (Track != 0 && Enabled) {
			if (musicEnabled) {
				SavedPosition = _cd.GetPosition();
			}

			_cd.Stop();
		}
	}

	/// <summary>
	/// <c>Sound_ResumeAll</c> (<c>00463134</c>)'s CD arm, which its caller reaches only with music on. Unlike
	/// the suspend it does not consult <see cref="Enabled"/>, and a mission that never set a track resumes
	/// nothing because <c>PlayTrack(0)</c> does nothing.
	/// </summary>
	internal void Resume() {
		if (SavedPosition == 0) {
			_cd.PlayTrack(Track);
		} else {
			_cd.ResumeAt(SavedPosition);
		}
	}

	/// <summary>
	/// Standing in for the <c>MM_MCINOTIFY</c> that re-issues the play in retail; see <see cref="ICdAudio.Update"/>.
	/// It runs across a suspend too, where it does nothing, because the music is stopped.
	/// </summary>
	public void Update() => _cd.Update();

	/// <inheritdoc />
	public void Dispose() => _cd.Dispose();
}
