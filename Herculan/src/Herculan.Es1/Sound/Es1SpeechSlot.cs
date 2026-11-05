namespace Herculan.Es1.Sound;

/// <summary>
/// One of the simulator's voice-line records, <c>0x3a</c> bytes, the lip-sync player's <c>0x2b</c> first
/// (docs/es1/formats/snc-lip-sync.md#the-speech-slot): the face's frame and the script it steps through, and the key
/// <see cref="Es1VoiceLines.LoadLine"/> finds the line by. <c>VoiceSlot_ElementCtor</c> (<c>ES1DBSIM 0004dc40</c>) and
/// <c>Snc_Ctor</c> (<c>0004e444</c>) leave the script and the sound object none and the taken byte and the last use 0.
/// </summary>
public sealed class Es1SpeechSlot {
	internal Es1SpeechSlot(int index) => Index = index;

	/// <summary>The slot's index among <see cref="Es1VoiceLines.Slots"/>, which picks its 100 bytes of script.</summary>
	public int Index { get; }

	/// <summary>
	/// <c>+0x00</c>: the line's sound object, which <c>Snc_Load</c> stores from <see cref="Sound"/> and
	/// <see cref="Es1VoiceLines.Start"/> plays; none until a script is loaded.
	/// </summary>
	public Es1SoundObject? LineSound { get; internal set; }

	/// <summary><c>+0x04</c>, the count of <see cref="Es1VoiceLines.Clock"/> at which the frame on show ends.</summary>
	public uint DueCount { get; internal set; }

	/// <summary><c>+0x08</c>, <c>int16</c>, the frame on show; −1 once the script has ended.</summary>
	public short Frame { get; internal set; }

	/// <summary><c>+0x0a</c>, the cursor into <see cref="Script"/>, as an index.</summary>
	public int Cursor { get; internal set; }

	/// <summary>
	/// <c>+0x0e</c>: the voice's path, <c>simvoice\&lt;voice name&gt;.sfx</c>, which <see cref="Es1VoiceLines.LoadLine"/>
	/// builds with a sound manager and speech on; none before.
	/// </summary>
	public string? VoicePath { get; internal set; }

	/// <summary><c>+0x27</c>, the script's buffer, the slot's 100 bytes, which <c>Voice_EvictLru</c> points it at; none before.</summary>
	public byte[]? Script { get; internal set; }

	/// <summary>
	/// <c>+0x2b</c>, the key of the voice's name (<see cref="Es1VoiceLines.VoiceKey"/>). The constructors leave it as the
	/// allocation left it; here it starts at 0, which no voice name's key is.
	/// </summary>
	public int Key { get; internal set; }

	/// <summary><c>+0x2f</c>, the clock <c>000981c4</c> at the slot's last use.</summary>
	public uint LastUse { get; internal set; }

	/// <summary><c>+0x33</c>, set once the slot is taken for a voice; none of the functions ported here reads it.</summary>
	public bool Taken { get; internal set; }

	/// <summary>
	/// <c>+0x34</c>, <c>int16</c>: the sound manager's name index the slot's sample is named as, the next past the
	/// catalog's rows, which <c>SoundCatalog_Load</c> stores (<see cref="Es1Sound.LoadCatalog"/>).
	/// </summary>
	public short SoundIndex { get; internal set; }

	/// <summary>
	/// <c>+0x36</c>: the digital sound <c>SoundCatalog_Load</c> makes on <see cref="SoundIndex"/>; none with no sound
	/// manager, where retail's is what the allocation left (docs/es1/formats/sound-samples.md#open).
	/// </summary>
	public Es1SoundObject? Sound { get; internal set; }
}
