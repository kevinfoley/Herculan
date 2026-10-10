using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Audio;

/// <summary>
/// The whole audio subsystem as one object for a host to hold: the device, the bank, the director,
/// the speaking channels the cockpit's <see cref="MessagePorts"/> post to, and the handful of per-frame
/// duties that belong to none of them individually.
///
/// <para>Everything here is optional. <see cref="Create"/> returns an instance even with no device
/// and no samples, so a host never has to branch on whether sound came up.</para>
/// </summary>
public sealed class GameAudio : ISoundSink, IDisposable {
	private readonly SoundDirector? _director;
	private readonly MessagePorts _ports;
	private MechObject? _engineLoopOwner;
	private SimWorld? _world;

	private GameAudio(SoundDirector? director, SoundBank? bank, ComputerVoice? voice,
			MessagePorts ports, string status, SquadVoice? squadVoice = null) {
		_director = director;
		_ports = ports;
		Bank = bank;
		Voice = voice;
		SquadSpeech = squadVoice;
		Status = status;
		var messages = ports.Computer;

		// The port drives both halves. Speech is the voice channel's; the alert tone is an ordinary
		// catalog effect, so it goes through the director like any other cockpit sound.
		messages.Speak += messageId => {
			if (SpeechEnabled) {
				Voice?.Speak(messageId);
			}
		};
		messages.AlertTone += id => _director?.Play(id);
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
	void ISoundSink.PlayAt(int id, Vec3i position, SoundReach? reach, object? source) =>
		_director?.PlayAt(id, position, reach, source);

	/// <inheritdoc />
	void ISoundSink.Stop(int id) => _director?.Stop(id);

	/// <inheritdoc />
	void ISoundSink.MoveTo(int id, Vec3i position, SoundReach? reach, object? source) =>
		_director?.UpdatePosition(id, position, reach, source);

	/// <inheritdoc />
	void ISoundSink.SetPitch(int id, int rate) => _director?.SetPitch(id, rate);

	/// <inheritdoc />
	void ISoundSink.Say(int messageId) => _ports.Computer.Post(messageId);

	/// <inheritdoc />
	void ISoundSink.Unsay(int messageId) => _ports.Computer.Withdraw(messageId);

	/// <inheritdoc />
	void ISoundSink.SquadSay(int messageId, object speaker) => _ports.Squad?.Post(messageId, speaker);

	/// <inheritdoc />
	void ISoundSink.CommandSay(int messageId) => _ports.Squad?.PostUnattributed(messageId);

	/// <inheritdoc />
	void ISoundSink.SquadUnsay(int messageId, object? speaker) => _ports.Squad?.Port.Withdraw(messageId, speaker);

	/// <summary>
	/// Reads a training mission's instructor clip, given the training mission and the message id — see
	/// <see cref="InstructorVoice.ReadClip"/>. Null leaves the instructor silent.
	/// </summary>
	public Func<int, int, byte[]?>? InstructorClip { get; set; }

	/// <summary>
	/// Connects the mission's comm boxes' two outputs: the recorded line goes to <see cref="SquadSpeech"/>
	/// and the static to the effect catalog, which is the same split the computer's port takes. The boxes
	/// themselves are <see cref="MessagePorts.Squad"/>.
	/// </summary>
	public void AttachSquad(SquadCommChannel squad) {
		ArgumentNullException.ThrowIfNull(squad);

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
						&& InstructorClip is { } readClip) {
					int training = squad.TrainingMission;
					SquadSpeech?.SpeakClip(InstructorVoice.ClipName(training, message.Id), () => readClip(training, message.Id));
				}
			};
		}

		// CommBox_OnMessageBegin tests whether the hiss is already running before starting it, so a
		// second box opening under the first does not layer a second copy — see the note on
		// Sound_Play in docs/retail/simulation/audio.md.
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
	/// <param name="ports">The cockpit's message ports, whose posts this speaks.</param>
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
	/// <param name="discImage">
	/// The install's disc, when it is an image, whose audio tracks are preferred to a CD drive's; see
	/// <see cref="ImageMusicSource"/>.
	/// </param>
	/// <param name="soundCfg">
	/// The install's <c>data\sound.cfg</c>, applied through <see cref="SoundCfgBackend"/>; null takes the shipped
	/// settings.
	/// </param>
	public static GameAudio Create(GameContent content, MessagePorts ports, SimRandom? random = null, bool lowMemory = false,
			bool silent = false, string? cdDrive = null, string? musicDirectory = null,
			HercWorks.Disc.DiscImage? discImage = null, SoundCfg? soundCfg = null) {
		// The message port's display half needs nothing but the text, so the ticker still runs on a
		// machine with no sound device and in an install with no SIMSOUND.VOL.
		var messages = ports.Computer.Messages;

		SoundBank? bank;
		try {
			bank = SoundBank.Load(content, lowMemory);
		} catch (Exception e) {
			return new GameAudio(null, null, null, ports, $"sound bank failed to load: {e.Message}");
		}

		if (bank == null) {
			return new GameAudio(null, null, null, ports,
				$"no {SoundCatalog.ResourceName} in the mounted archives — is {SoundBank.ArchiveName} mounted?");
		}

		string? deviceFailure = null;
		var backend = new SoundCfgBackend(
			(silent ? null : (IAudioBackend?)OpenAlBackend.TryCreate(out deviceFailure)) ?? new NullAudioBackend(),
			soundCfg ?? new SoundCfg());
		// Music is Red Book CD audio and never went through SOS, so it gets a stream of its own on the
		// device rather than a pool channel: failing to find a disc leaves the effects half exactly as
		// it was.
		var cd = silent
			? new NullCdAudio("silenced by request")
			: CdAudio.Open(backend, cdDrive, musicDirectory, discImage: discImage);
		var director = new SoundDirector(bank, backend, random);
		director.Music.Cd = cd;
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

		return new GameAudio(director, bank, voice, ports, status, squadVoice);
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

		_director?.Music.StartMission(trackSelect, _director.MusicEnabled);
	}

	/// <summary>
	/// <c>Cockpit_PowerUpSound</c> (<c>004328cc</c>)'s sound half — the cockpit's power-up, played when the player takes a machine. Plays
	/// the start-up sequence, and for a flyer also starts the engine hum and drops it to the pitch
	/// the original sets.
	///
	/// <para><b>The hum is the flyer's, not the walker's</b>, despite the sample being called
	/// <c>herceng1</c>. The original gates it on the type record's <c>+0x50</c> — file offset 78,
	/// <c>FlyerFlag</c>, set on the RAZOR alone — so a HERC powers up without one and its
	/// running noise is its footsteps. See docs/retail/simulation/mech-locomotion.md's type-record table.</para>
	///
	/// <para><b>A flyer gets the hum and nothing else.</b> <c>start3</c> and the announcement both
	/// sit behind <c>cockpit+0x245</c>, which <c>Gau_BuildCockpitWidgets</c> sets for a flyer before
	/// this runs; see docs/retail/simulation/audio.md, "The cockpit power-up". The announcement is
	/// <see cref="MessagePorts.PowerUp"/>'s.</para>
	/// </summary>
	public void PowerUp(MechObject pilot) {
		bool flyer = pilot.Type.IsFlyer;

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
		_director?.Stop(SoundId.EngineLoop);
	}

	/// <summary>
	/// Leaves the cockpit for good: the hum and the computer's voice stop. <c>Cockpit_PowerUpSound</c>'s
	/// counterpart at the end of a mission; the message half is <see cref="MessagePorts.LeaveCockpit"/>.
	/// </summary>
	public void LeaveCockpit() {
		PowerDown();
		Voice?.Stop();

		// Retail has no counterpart: DBSIM exits and Windows closes the device with the process. A host
		// that goes on running has to hand the disc back itself.
		_director?.Music.StopMission();
	}

	/// <summary>
	/// Per-frame service: keeps the engine hum on its machine, lets finite repeat counts run, and
	/// notices when the speaking channels finish. Call once a frame, after the listener has been set and
	/// after <see cref="MessagePorts.Update"/>.
	/// </summary>
	public void Update() {
		Voice?.Update();
		SquadSpeech?.Update();

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
	/// Silences everything without tearing the device down — for a lost window, which is where the
	/// original calls <c>Sound_SuspendAll</c> (<c>Sim_Suspend</c>, <c>0045f0b8</c>, alongside both ports' pause). Speech is
	/// cut rather than remembered: <see cref="Resume"/> can only restart a clip from its beginning,
	/// and half a sentence twice is worse than none. The message ports' half is
	/// <see cref="MessagePorts.Suspended"/>.
	/// </summary>
	public void Suspend() {
		Voice?.Stop();
		_director?.SuspendAll();
	}

	/// <summary>Puts back what <see cref="Suspend"/> stopped.</summary>
	public void Resume() {
		_director?.ResumeAll();
	}

	/// <inheritdoc />
	public void Dispose() => _director?.Dispose();
}
