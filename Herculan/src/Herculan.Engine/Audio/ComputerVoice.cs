using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;

namespace Herculan.Engine.Audio;

/// <summary>
/// The cockpit computer's speaking voice — <c>SYSTEM.STR</c>'s messages, read aloud from the
/// <c>CVM_nnnn.WAV</c> clips in the voice archive. See docs/retail/simulation/cockpit-messages.md, "The
/// computer's messages".
///
/// <para><b>It is not part of the sound catalog and does not go through
/// <see cref="SoundDirector"/>.</b> Speech has its own channel pool in the original, opened at
/// priority <c>0xff</c> so that no catalog effect can ever evict a line mid-sentence, and it is
/// never positional — the computer is in the player's ears, not in the world. That separation is
/// kept here: this type holds its own backend voices.</para>
///
/// <para><b>This type does not decide when to speak.</b> It has no queue of its own: it is handed a
/// message id and plays that message's clip, exactly as the original's own dispatch does. Which
/// message is next, how long it holds the screen, whether a repeat is swallowed and whether the voice
/// half of the channel is enabled at all are all
/// <see cref="MessagePort"/>'s — one queue, feeding both halves.</para>
/// </summary>
public sealed class ComputerVoice {
	/// <summary>
	/// The voice archive's name for <paramref name="language"/>, without its extension, and the folder its clips live
	/// in: <c>Voice_ArchiveName</c> (<c>0045ef68</c>) patches the language letter over the last letter of
	/// <c>simvoice</c>, and <c>Voice_FilePath</c> (<c>0045ef80</c>) puts that name in front of every clip
	/// (docs/retail/simulation/audio.md, "File naming"). There is no English fallback: a v1.10 install copies its language's
	/// archive at every size.
	/// </summary>
	public static string VoiceFolder(GameLanguage language) => language switch {
		GameLanguage.French => "SIMVOICF",
		GameLanguage.German => "SIMVOICG",
		_ => "SIMVOICE",
	};

	/// <summary>
	/// How a clip number becomes a resource name. The original builds it from the literal
	/// <c>CVM_0000</c> template it keeps beside the pilot and base-command ones.
	/// </summary>
	public const string ClipNameFormat = "CVM_{0:0000}.WAV";

	private readonly GameContent _content;
	private readonly IAudioBackend _backend;
	private readonly Dictionary<int, int> _samples = new();

	private int _speaking = -1;

	/// <param name="content">The mounted archives. Must include a voice archive for anything to sound.</param>
	/// <param name="messages">The parsed <c>SYSTEM.STR</c>, or null to run mute.</param>
	/// <param name="backend">The mixer the clips are opened on.</param>
	public ComputerVoice(GameContent content, SystemMessages? messages, IAudioBackend backend) {
		_content = content;
		_backend = backend;
		Messages = messages;
	}

	/// <summary>The message set, or null when <c>SYSTEM.STR</c> would not load.</summary>
	public SystemMessages? Messages { get; }

	/// <summary>Speech gain, 0 to 1. Separate from the effects volume, as the original's channel is.</summary>
	public float Volume {
		get => _volume;
		set {
			_volume = Math.Clamp(value, 0f, 1f);
			if (_speaking >= 0) {
				_backend.SetGain(_speaking, _volume);
			}
		}
	}

	private float _volume = 1f;

	/// <summary>The message currently being spoken, or null when the channel is silent.</summary>
	public int? Speaking { get; private set; }

	/// <summary>Whether a clip for <paramref name="messageId"/> could be found and opened.</summary>
	public bool CanSpeak(int messageId) => Sample(messageId) >= 0;

	/// <summary>
	/// Reads message <paramref name="messageId"/> aloud — the flat <c>SYSTEM.STR</c> id its call sites
	/// pass, resolved here to the <c>CVM</c> clip its attribute byte names. The tail of
	/// <c>MessagePort_Show</c> (<c>00436abc</c>), which is a bare "build the filename and play it": there is no queue and
	/// nothing is checked, because the port has already decided this line is the one to say.
	///
	/// <para>A line still running is cut off. The original opens each clip into its own channel slot
	/// and would let two overlap, but its port never asks: a message speaks only as it goes up, and one
	/// goes up only once the last has come down.</para>
	/// </summary>
	public void Speak(int messageId) {
		int sample = Sample(messageId);
		if (sample < 0) {
			return;
		}

		// Cutting the running line first also frees its channel, so speech only ever needs one of the
		// backend's and cannot be starved by a line of its own.
		Stop();

		_speaking = _backend.Start(sample, _volume, 0f, 1f, looping: false);
		Speaking = _speaking >= 0 ? messageId : null;
	}

	/// <summary>
	/// Notices when the running clip has finished, so <see cref="Speaking"/> stops naming it. Call
	/// once a frame.
	/// </summary>
	public void Update() {
		if (Speaking != null && (_speaking < 0 || !_backend.IsPlaying(_speaking))) {
			Speaking = null;
			_speaking = -1;
		}
	}

	/// <summary>Silences the channel.</summary>
	public void Stop() {
		if (_speaking >= 0) {
			_backend.Stop(_speaking);
		}

		_speaking = -1;
		Speaking = null;
	}

	/// <summary>
	/// The backend sample for a message's clip, decoded on first use and kept.
	///
	/// <para>The original keeps five slots and evicts the least recently used, because 66 clips of
	/// 8-bit PCM did not fit its cache budget. Holding each one that is actually asked for costs a
	/// few hundred kilobytes over a mission and removes the eviction path entirely, which is the same
	/// trade <see cref="SoundBank"/> makes for the effect samples.</para>
	/// </summary>
	/// <returns>The sample id, or -1 when the message, its clip or a device is missing.</returns>
	private int Sample(int messageId) {
		if (_samples.TryGetValue(messageId, out int cached)) {
			return cached;
		}

		int id = -1;

		if (Messages?[messageId] is { VoiceClip: > 0 } message) {
			string name = string.Format(ClipNameFormat, message.VoiceClip);
			if (_content.Read(VoiceFolder(_content.Language), name) is { } bytes
					&& WaveSample.Decode(bytes) is { } sample) {
				id = _backend.CreateSample(sample);
			}
		}

		_samples[messageId] = id;
		return id;
	}
}
