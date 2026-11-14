namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// The MIDI half of the HMI library (<c>HmiMidi_InitSystem</c>, <c>00070ed8</c>): the open drivers, the song slots and the
/// player over them. One a sound manager, since retail's is the program's one set of globals
/// (docs/es1/sound/song-player.md).
/// </summary>
public sealed class Es1MidiSystem {
	/// <summary>Makes the system with no driver open.</summary>
	public Es1MidiSystem() {
		Songs = new Es1HmiSongs(Drivers);
		Player = new Es1HmiPlayer(Drivers, Songs);
	}

	/// <summary>The open drivers.</summary>
	public Es1MidiDrivers Drivers { get; } = new();

	/// <summary>The song slots.</summary>
	public Es1HmiSongs Songs { get; }

	/// <summary>The player.</summary>
	public Es1HmiPlayer Player { get; }

	/// <summary>
	/// One interrupt of the game's timer for every song: <see cref="Es1HmiPlayer.Tick"/> on each song slot in turn.
	/// Retail's timer calls each playing song's own event in the order the library holds them; the slot order stands in
	/// for it.
	/// </summary>
	public void Tick() {
		for (int slot = 0; slot < Es1HmiSongs.Slots; slot++) {
			if (Songs.Song(slot) is { } song) {
				Player.Tick(song);
			}
		}
	}
}
