namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// The caller's block <c>HmiMidi_InitDriver</c> (<c>00071076</c>) takes, the sound manager's <c>+0xa0</c>
/// (docs/es1/sound/song-player.md#the-drivers): where an open keeps the image it loaded, so that the next open of a device
/// whose code is an image uses that image again instead of loading one.
/// </summary>
public sealed class Es1MidiDriverBlock {
	/// <summary>
	/// <c>+0x04</c>–<c>+0x14</c> and <c>+0x28</c>: the image an open loaded, standing for its data and code far pointers, its
	/// address and its allocation; null while the data far pointer is 0, which has the next open load one.
	/// </summary>
	public byte[]? Image { get; set; }

	/// <summary>
	/// <c>+0x18</c>: the far pointer to the sound manager, whose first bytes are the digital driver block device
	/// <c>0xa005</c>'s open reads (docs/es1/sound/drum-device.md#opening); zero until the manager fills it.
	/// </summary>
	public Es1DigitalDriverBlock Digital { get; set; }
}
