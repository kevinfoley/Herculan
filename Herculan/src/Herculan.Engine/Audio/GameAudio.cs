using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Audio;

/// <summary>
/// The whole audio subsystem as one object for a host to hold: the device, the bank, the director,
/// and the handful of per-frame duties that belong to none of them individually.
///
/// <para>Everything here is optional. <see cref="Create"/> returns an instance even with no device
/// and no samples, so a host never has to branch on whether sound came up.</para>
/// </summary>
public sealed class GameAudio : ISoundSink, IDisposable {
	/// <summary>
	/// How long after the power-up begins the computer announces the result — the original's own
	/// <c>200 &lt; elapsed</c> against <c>Time_GetCoarseTicks</c>, whose unit is 16 ms. It lands
	/// inside <c>start3</c>'s five seconds rather than after them.
	/// </summary>
	public static readonly TimeSpan PowerUpAnnounceDelay = TimeSpan.FromMilliseconds(200 * 16);

	/// <summary>
	/// <c>Time_GetCoarseTicks</c>' unit, which is what <see cref="MessagePort"/> counts in:
	/// <c>GetTickCount() &gt;&gt; 4</c>, so 16 ms of wall time.
	/// </summary>
	public const double CoarseTickSeconds = 0.016;

	/// <summary>
	/// <c>Time_GetCoarseTicks</c> as this session has counted it — the same clock the message port
	/// runs on, and the one the cockpit's power-up animations are timed against. Exposed because the
	/// compass's wind-up is stamped and ramped in it; see <see cref="Content.HeadingTapeSweep"/>.
	/// </summary>
	public long CoarseTicks => (long)_messageTicks;

	private readonly SoundDirector? _director;
	private MechObject? _engineLoopOwner;
	private MechObject? _pilot;
	private SimWorld? _world;
	private TimeSpan _powerUpAnnounceIn = TimeSpan.MinValue;
	private double _messageTicks;
	private bool _suspended;

	private GameAudio(SoundDirector? director, SoundBank? bank, ComputerVoice? voice,
			SystemMessages? messages, string status, SquadVoice? squadVoice = null) {
		_director = director;
		Bank = bank;
		Voice = voice;
		SquadSpeech = squadVoice;
		Status = status;
		Messages = new MessagePort(messages);

		// The port drives both halves. Speech is the voice channel's; the alert tone is an ordinary
		// catalog effect, so it goes through the director like any other cockpit sound.
		Messages.Speak += messageId => {
			if (SpeechEnabled) {
				Voice?.Speak(messageId);
			}
		};
		Messages.AlertTone += id => _director?.Play(id);
	}

	/// <summary>
	/// <c>Sound_SpeechEnabled</c> (<c>0049f97e</c>) — the one gate on every recorded line, the
	/// computer's, the squad's and the instructor's alike: <c>Voice_Acquire</c> opens no clip and
	/// <c>Snc_Start</c> plays none while it is down. Its only writer is PILOT MESSAGE's handler, so
	/// that row's TEXT ONLY silences the computer too, whatever COMPUTER MESSAGE says. A comm box's
	/// portrait still talks, because its script runs either way.
	/// </summary>
	public bool SpeechEnabled { get; set; } = true;

	/// <summary>
	/// The cockpit computer's speaking channel, or null when there is no device. Separate from
	/// <see cref="Director"/> because speech is a separate channel in the original — see
	/// <see cref="ComputerVoice"/>.
	/// </summary>
	public ComputerVoice? Voice { get; }

	/// <summary>
	/// The squadmates' speaking channel, or null when there is no device — the other half of the
	/// original's five-slot speech pool. See <see cref="SquadVoice"/>.
	/// </summary>
	public SquadVoice? SquadSpeech { get; }

	/// <summary>
	/// The cockpit's message port. Not an audio object — it owns the on-screen ticker as much as the
	/// speech — but it lives here because this is where a posted message arrives: the simulation
	/// reaches it through <see cref="ISoundSink.Say"/>, which knows nothing about either half. A
	/// renderer reads <see cref="MessagePort.Ticker"/> from it; see
	/// <see cref="Content.MessageTickerLayout"/>.
	/// </summary>
	public MessagePort Messages { get; }

	/// <summary>The loaded catalog and samples, or null when <c>SOUNDS.STR</c> would not load.</summary>
	public SoundBank? Bank { get; }

	/// <summary>Why audio is in the state it is, for the debug panel and the startup log.</summary>
	public string Status { get; }

	/// <summary>Whether sound will actually be heard.</summary>
	public bool IsAvailable => _director is { IsAvailable: true };

	/// <summary>
	/// The sink to hand <see cref="SimWorld.Sounds"/>. This type is it: the two channels the
	/// simulation can reach — the effect catalog and the computer's voice — are separate objects
	/// underneath, and which one a call belongs to is not the simulation's business.
	/// </summary>
	public ISoundSink Sink => this;

	/// <inheritdoc />
	void ISoundSink.Play(int id) => _director?.Play(id);

	/// <inheritdoc />
	void ISoundSink.PlayAt(int id, Vec3i position) => _director?.PlayAt(id, position);

	/// <inheritdoc />
	void ISoundSink.Stop(int id) => _director?.Stop(id);

	/// <inheritdoc />
	void ISoundSink.MoveTo(int id, Vec3i position) => _director?.UpdatePosition(id, position);

	/// <inheritdoc />
	void ISoundSink.SetPitch(int id, int rate) => _director?.SetPitch(id, rate);

	/// <inheritdoc />
	void ISoundSink.Say(int messageId) => Messages.Post(messageId);

	/// <inheritdoc />
	void ISoundSink.Unsay(int messageId) => Messages.Withdraw(messageId);

	/// <inheritdoc />
	void ISoundSink.SquadSay(int messageId, object speaker) => Squad?.Post(messageId, speaker);

	/// <inheritdoc />
	void ISoundSink.CommandSay(int messageId) => Squad?.PostUnattributed(messageId);

	/// <inheritdoc />
	void ISoundSink.SquadUnsay(int messageId, object? speaker) => Squad?.Port.Withdraw(messageId, speaker);

	/// <summary>
	/// The pilot and squad channel — the three comm boxes, their queue and the portraits they play.
	/// Null until <see cref="AttachSquad"/> is called, because which pilots are in the boxes is a
	/// per-mission fact; a post to it before then is simply dropped.
	/// </summary>
	public SquadCommChannel? Squad { get; private set; }

	/// <summary>
	/// Where a training mission's instructor clips are read from — see <see cref="InstructorVoice"/>.
	/// Null leaves the instructor silent.
	/// </summary>
	public string? InstructorVoiceDirectory { get; set; }

	/// <summary>
	/// Hands this the mission's comm boxes and connects their two outputs: the recorded line goes to
	/// <see cref="SquadSpeech"/> and the static to the effect catalog, which is the same split the
	/// computer's port takes. From here on <see cref="Update"/> runs the channel on the port's own
	/// clock, so it stops with everything else across a suspend.
	/// </summary>
	public void AttachSquad(SquadCommChannel squad) {
		ArgumentNullException.ThrowIfNull(squad);

		Squad = squad;
		squad.Speak += (voiceBank, messageId, variant) => {
			if (SpeechEnabled) {
				SquadSpeech?.Speak(voiceBank, messageId, variant);
			}
		};

		// The training port speaks at the end of its own paint rather than through a comm box, and
		// only when PILOT MESSAGE is not TEXT ONLY — the paint's SimOptions[2] test.
		if (squad.Port.Training) {
			squad.Port.Shown += message => {
				if (squad.Port.Mode != MessageChannelMode.TextOnly && SpeechEnabled
						&& InstructorVoiceDirectory is { } directory) {
					SquadSpeech?.SpeakFile(Path.Combine(directory,
						InstructorVoice.ClipName(squad.TrainingMission, message.Id)));
				}
			};
		}

		// CommBox_OnMessageBegin tests whether the hiss is already running before starting it, so a
		// second box opening under the first does not layer a second copy — see the note on
		// Sound_Play in docs/formats/audio.md.
		squad.Hiss += id => {
			if (_director is { } director && !director.IsPlaying(id)) {
				director.Play(id);
			}
		};
	}

	/// <summary>The rule layer, for a caller that wants to play something directly.</summary>
	public SoundDirector? Director => _director;

	/// <summary>Overall output gain, 0 to 1.</summary>
	public float MasterVolume {
		get => _director?.MasterVolume ?? 0f;
		set {
			if (_director != null) {
				_director.MasterVolume = value;
			}
		}
	}

	/// <summary>
	/// Brings audio up against the mounted archives. Never throws and never returns null: a missing
	/// <c>SIMSOUND.VOL</c>, a missing <c>SOUNDS.STR</c> or a machine with no output device all give
	/// an instance that silently does nothing, with <see cref="Status"/> saying which.
	/// </summary>
	/// <param name="content">The mounted archives. Must include <c>SIMSOUND.VOL</c> for the samples.</param>
	/// <param name="random">
	/// The generator the variation roll draws on — pass the world's
	/// <see cref="Sim.SimWorld.PresentationRandom"/>, as the original does.
	/// </param>
	/// <param name="lowMemory">Select the low-memory <c>hmx</c> sample bank.</param>
	/// <param name="silent">
	/// Skip the output device and run on <see cref="NullAudioBackend"/> even where one would open.
	/// Everything above the device behaves as it does on a machine without one. It silences the CD
	/// too, which the digital backend has nothing to do with.
	/// </param>
	/// <param name="cdDrive">
	/// Which CD drive the music comes off, as a drive letter. Null takes the first one holding audio
	/// — see <see cref="CdAudio.Open"/>.
	/// </param>
	/// <param name="musicDirectory">
	/// A directory of <c>TrackNN.wav</c> files to play instead of the disc; see
	/// <see cref="WaveFileMusicSource"/>.
	/// </param>
	public static GameAudio Create(GameContent content, SimRandom? random = null, bool lowMemory = false,
			bool silent = false, string? cdDrive = null, string? musicDirectory = null) {
		// Read first and unconditionally: the message port's display half needs nothing but the text,
		// so the ticker still runs on a machine with no sound device and in an install with no
		// SIMSOUND.VOL.
		var messages = SystemMessages.Load(content);

		SoundBank? bank;
		try {
			bank = SoundBank.Load(content, lowMemory);
		} catch (Exception e) {
			return new GameAudio(null, null, null, messages, $"sound bank failed to load: {e.Message}");
		}

		if (bank == null) {
			return new GameAudio(null, null, null, messages,
				$"no {SoundCatalog.ResourceName} in the mounted archives — is {SoundBank.ArchiveName} mounted?");
		}

		string? deviceFailure = null;
		var backend = (silent ? null : (IAudioBackend?)OpenAlBackend.TryCreate(out deviceFailure))
			?? new NullAudioBackend();
		// Music is Red Book CD audio and never went through SOS, so it gets a stream of its own on the
		// device rather than a pool channel: failing to find a disc leaves the effects half exactly as
		// it was.
		var cd = silent
			? new NullCdAudio("silenced by request")
			: CdAudio.Open(backend, cdDrive, musicDirectory);
		var director = new SoundDirector(bank, backend, random) { Cd = cd };
		var voice = new ComputerVoice(content, messages, backend);
		var squadVoice = new SquadVoice(content, backend);

		// The device's own account of itself goes in the status line either way. A launch that opened
		// only on a retry sounds normal but is worth seeing, and one that gave up needs to say what it
		// gave up on: OpenAlBackend.OpenAttempts explains why "no audio device" is not the whole story
		// on Windows.
		string status = backend.IsAvailable
			? $"OpenAL, {bank.Catalog.Count} catalog entries"
			: silent
				? $"silenced by request; {bank.Catalog.Count} catalog entries loaded but not played"
				: $"no sound; {bank.Catalog.Count} catalog entries loaded but silent";

		if (deviceFailure != null) {
			status += $" ({deviceFailure})";
		}

		status += $", music: {cd.Status}";

		status += messages != null
			? $", {messages.Count} computer messages"
			: $", no {SystemMessages.ResourceName} (computer voice silent)";

		if (bank.Missing.Count > 0) {
			// battle1.wav is expected: the ten music rows all name it and it ships nowhere, because
			// music is CD audio. Anything else is worth seeing.
			status += $" (no sample for: {string.Join(", ", bank.Missing)})";
		}

		return new GameAudio(director, bank, voice, messages, status, squadVoice);
	}

	/// <summary>
	/// Attaches this as <paramref name="world"/>'s sink, so everything the simulation does is heard,
	/// and remembers the world so <see cref="SetListener"/> can keep
	/// <see cref="SimWorld.ListenerPosition"/> in step with the director's.
	/// </summary>
	public void Attach(SimWorld world) {
		_world = world;
		world.Sounds = Sink;
	}

	/// <summary>
	/// Where the player's ears are. The original's listener is the camera, so this takes the camera's
	/// own position and facing rather than the machine's.
	///
	/// <para>The world gets the position too: its own range gates — the footfall's and the
	/// missile-inbound warning's — measure against the camera, and having one setter for both is what
	/// stops them drifting apart.</para>
	/// </summary>
	/// <param name="position">Camera position, in world units.</param>
	/// <param name="heading">
	/// Camera facing as a binary angle in the <b>simulation's</b> convention, 0 being <c>+Y</c>. A
	/// host holding a <c>Camera</c> must negate its <c>Yaw</c>, which runs the other way.
	/// </param>
	public void SetListener(Vec3i position, int heading) {
		if (_world != null) {
			_world.ListenerPosition = position;
		}

		if (_director == null) {
			return;
		}

		_director.ListenerPosition = position;
		_director.ListenerHeading = heading;
	}

	/// <summary>
	/// <c>Sim_InitMissionSession</c>'s music arm: starts the mission's Red Book track, unless the
	/// mission is a training one — the original never sets a track for one of those, so it runs
	/// without music.
	/// </summary>
	/// <param name="header">The mission's own header; only its training number is read.</param>
	/// <param name="trackSelect">
	/// DBSIM's <c>-R</c> value, which is the only thing that picks between the five tracks.
	/// </param>
	public void StartMissionMusic(World.ScriptDatHeader header, int trackSelect = 0) {
		if (header.TrainingMissionNumber != 0) {
			return;
		}

		_director?.StartMissionMusic(trackSelect);
	}

	/// <summary>
	/// <c>Cockpit_PowerUpSound</c> (<c>004328cc</c>) — the cockpit's power-up, played when the player takes a machine. Plays
	/// the start-up sequence, and for a flyer also starts the engine hum and drops it to the pitch
	/// the original sets.
	///
	/// <para><b>The hum is the flyer's, not the walker's</b>, despite the sample being called
	/// <c>herceng1</c>. The original gates it on the type record's <c>+0x50</c> — file offset 78,
	/// <c>FlyerFlag</c>, set on the RAZOR alone — so a HERC powers up without one and its
	/// running noise is its footsteps. See docs/simulation/mech-locomotion.md's type-record table.</para>
	///
	/// <para><b>A flyer gets the hum and nothing else.</b> <c>start3</c> and the announcement both
	/// sit behind <c>cockpit+0x245</c>, which <c>Gau_BuildCockpitWidgets</c> sets for a flyer before
	/// this runs; see docs/formats/audio.md, "The cockpit power-up".</para>
	/// </summary>
	public void PowerUp(MechObject pilot) {
		_pilot = pilot;
		bool flyer = pilot.Type.IsFlyer;
		_powerUpAnnounceIn = flyer ? TimeSpan.MinValue : PowerUpAnnounceDelay;

		if (_director == null) {
			return;
		}

		if (!flyer) {
			_director.Play(SoundId.PowerUp);
			_engineLoopOwner = null;
			return;
		}

		_engineLoopOwner = pilot;
		_director.PlayAt(SoundId.EngineLoop, pilot.Position);
		_director.SetPitch(SoundId.EngineLoop, SoundId.EngineLoopPitch);
	}

	/// <summary>Stops the engine hum — leaving the cockpit, or the machine dying.</summary>
	public void PowerDown() {
		_engineLoopOwner = null;
		_powerUpAnnounceIn = TimeSpan.MinValue;
		_director?.Stop(SoundId.EngineLoop);
	}

	/// <summary>
	/// Leaves the cockpit for good: the hum stops and the message port forgets everything it was
	/// holding. <c>Cockpit_PowerUpSound</c>'s counterpart at the end of a mission.
	/// </summary>
	public void LeaveCockpit() {
		PowerDown();
		_pilot = null;
		Voice?.Stop();
		Messages.Clear();

		// Retail has no counterpart: DBSIM exits and Windows closes the device with the process. A host
		// that goes on running has to hand the disc back itself.
		_director?.StopMissionMusic();
	}

	/// <summary>
	/// Per-frame service: keeps the engine hum on its machine, lets finite repeat counts run, and
	/// runs the speech channel. Call once a frame, after the listener has been set.
	/// </summary>
	/// <param name="elapsed">Wall time since the last call, for the power-up announcement's delay.</param>
	public void Update(TimeSpan elapsed = default) {
		AnnouncePowerUp(elapsed);
		Voice?.Update();

		// The port's clock is Time_GetCoarseTicks' wall time, not the simulation's, and it stops while
		// suspended or paused — which is what the original's own pause pair (MessagePort_Pause,
		// 00435b58 / MessagePort_Resume, 00435b80) achieves by shifting every deadline forward by
		// however long the pause lasted.
		if (!_suspended && !MessagesPaused) {
			_messageTicks += elapsed.TotalSeconds / CoarseTickSeconds;
		}

		Messages.PilotDisabled = _pilot is { Destroyed: true };
		Messages.Update((long)_messageTicks);

		// The squad channel runs on the same clock: it is the second instance of the same port, and
		// its comm boxes count their static in the same coarse ticks.
		SquadSpeech?.Update();
		if (Squad is { } squad) {
			squad.Port.PilotDisabled = Messages.PilotDisabled;
			squad.Update((long)_messageTicks);
		}

		if (_director == null) {
			return;
		}

		// The hum is positional and its machine moves, so it is re-placed rather than left where it
		// started — Sound_UpdatePosition is exactly what the original uses it for.
		if (_engineLoopOwner is { Removed: false, Destroyed: false } owner) {
			_director.UpdatePosition(SoundId.EngineLoop, owner.Position);
		} else if (_engineLoopOwner != null) {
			PowerDown();
		}

		_director.Update();
	}

	/// <summary>
	/// <c>Cockpit_PowerUpTick</c> (<c>00432924</c>)'s tail — the cockpit's power-up sequence announcing itself once
	/// <see cref="PowerUpAnnounceDelay"/> has passed since the sequence began.
	///
	/// <para>Which line is <see cref="PowerUpFindsDamage"/>'s.</para>
	/// </summary>
	private void AnnouncePowerUp(TimeSpan elapsed) {
		if (_powerUpAnnounceIn == TimeSpan.MinValue) {
			return;
		}

		_powerUpAnnounceIn -= elapsed;
		if (_powerUpAnnounceIn > TimeSpan.Zero) {
			return;
		}

		_powerUpAnnounceIn = TimeSpan.MinValue;
		Messages.Post(PowerUpFindsDamage(_pilot)
			? SystemMessages.PowerUpDamaged
			: SystemMessages.PowerUpNominal);
	}

	/// <summary>
	/// Whether the power-up announces <see cref="SystemMessages.PowerUpDamaged"/>: true when any of
	/// the first <see cref="PowerUpCheckedInternals"/> internals reads any damage at all, which is
	/// <c>Cockpit_PowerUpTick</c>'s test as docs/formats/cockpit-messages.md, "Posters", derives it.
	///
	/// <para>The original's reading (<c>Mech_ReadEntryDamage</c>, <c>0041b514</c>) takes a zero
	/// maximum as fully damaged where <see cref="ComponentDamage.DependentPercent"/> reads it as 0;
	/// no chassis that announces has one among these slots.</para>
	/// </summary>
	private static bool PowerUpFindsDamage(MechObject? pilot) {
		if (pilot?.Damage is not { } damage) {
			return false;
		}

		for (int slot = 0; slot < PowerUpCheckedInternals; slot++) {
			if (damage.DependentPercent(slot) != 0) {
				return true;
			}
		}

		return false;
	}

	/// <summary>How many internals the power-up checks, from slot 0 — the original's literal 10.</summary>
	private const int PowerUpCheckedInternals = 10;

	/// <summary>
	/// Stops both message ports' clock and nothing else — what a modal panel does. The original's
	/// <c>AlertPanel_Enter</c> (<c>00454630</c>) and <c>AlertPanel_Leave</c> (<c>004548ac</c>) pause
	/// and resume the two ports and leave the sound alone, so an effect already playing plays out
	/// and a line already on screen keeps the rest of its display time for after the panel.
	/// <see cref="Suspend"/> is the lost window's fuller stop.
	/// </summary>
	public bool MessagesPaused { get; set; }

	/// <summary>
	/// Silences everything without tearing the device down — for a lost window, which is where the
	/// original calls <c>Sound_SuspendAll</c> (<c>Sim_Suspend</c>, <c>0045f0b8</c>, alongside both ports' pause). Speech is
	/// cut rather than remembered: <see cref="Resume"/> can only restart a clip from its beginning,
	/// and half a sentence twice is worse than none. The message port's clock stops, so a line already
	/// on screen keeps the rest of its display time for after the pause.
	/// </summary>
	public void Suspend() {
		_suspended = true;
		Voice?.Stop();
		_director?.SuspendAll();
	}

	/// <summary>Puts back what <see cref="Suspend"/> stopped.</summary>
	public void Resume() {
		_suspended = false;
		_director?.ResumeAll();
	}

	/// <inheritdoc />
	public void Dispose() => _director?.Dispose();
}
