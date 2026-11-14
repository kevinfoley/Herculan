using HercWorks.Core.Data.File;

namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// The library's eight song slots and the song loader (docs/es1/sound/song-player.md#loading): <c>HmiMidi_InitSong</c>
/// (<c>0006e4c1</c>) and <c>HmiMidi_ResetSong</c> (<c>0006eb98</c>), routing each track to one of the drivers in
/// <see cref="Es1MidiDrivers"/>.
/// </summary>
public sealed class Es1HmiSongs(Es1MidiDrivers drivers) {
	/// <summary>The songs the library holds at once.</summary>
	public const int Slots = 8;

	/// <summary>A track map entry for a track not yet routed, or that no open driver answers.</summary>
	public const int Unrouted = 0xff;

	/// <summary>Retail's error for a file that does not open <c>HMIMIDIP</c>.</summary>
	public const int NotASong = 0xe;

	/// <summary>Retail's error when all <see cref="Slots"/> song slots are taken.</summary>
	public const int SongSlotsTaken = 0xb;

	private static readonly byte[] Signature = "HMIMIDIP"u8.ToArray();

	private readonly Es1HmiSong?[] _songs = new Es1HmiSong?[Slots];

	/// <summary>The song in <paramref name="slot"/>, null when the slot is free.</summary>
	public Es1HmiSong? Song(int slot) => _songs[slot];

	/// <summary>
	/// <c>HmiMidi_InitSong</c> (docs/es1/sound/song-player.md#loading): 0 with the song in <paramref name="song"/>;
	/// <see cref="NotASong"/> when the file does not open <c>HMIMIDIP</c> and <see cref="SongSlotsTaken"/> when no slot is
	/// free. The tracks start (<see cref="Es1HmiTrack"/>), each branch entry gets its data, and each track whose entry in
	/// <paramref name="trackMap"/> is <see cref="Unrouted"/> is routed by <see cref="Es1MidiDrivers.Route"/>, the map
	/// written to; a track no driver answers loses its events pointer and one playing track. A track with another entry
	/// keeps it. An entry's data is cut at the file's end, an engine guard: retail points at the file and reads on, and no
	/// retail song's data runs past its end.
	/// </summary>
	public int Init(Es1HmpSong file, int[] trackMap, Action<int>? endCallback, out Es1HmiSong? song) {
		song = null;
		if (file.Signature.Length < Signature.Length || !file.Signature.AsSpan(0, Signature.Length).SequenceEqual(Signature)) {
			return NotASong;
		}

		int slot = Array.IndexOf(_songs, null);
		if (slot < 0) {
			return SongSlotsTaken;
		}

		song = new Es1HmiSong(slot, file, trackMap, endCallback);
		int entry = 0;
		int dataStart = file.BranchTableOffset + file.TrackCount + file.Branches.Count * 24;
		for (int t = 0; t < song.TrackCount; t++) {
			var branches = new Es1SongBranch[file.BranchCounts[t]];
			for (int i = 0; i < branches.Length; i++, entry++) {
				var raw = file.Branches[entry];
				int from = Math.Clamp(raw.DataOffset - dataStart, 0, file.BranchData.Length);
				int length = Math.Min(raw.Head[7], file.BranchData.Length - from);
				branches[i] = new Es1SongBranch(raw, file.BranchData[from..(from + length)]);
			}

			song.Tracks[t] = new Es1HmiTrack(file.Tracks[t], branches);
			song.Tracks[t].Start();
		}

		for (int t = 0; t < song.TrackCount; t++) {
			if (trackMap[t] != Unrouted) {
				continue;
			}

			int driver = drivers.Route(file.TrackDevices[t]);
			if (driver >= 0) {
				trackMap[t] = driver;
			} else {
				song.Tracks[t].Pos = Es1HmiTrack.NoEvents;
				song.TracksPlaying--;
			}
		}

		_songs[slot] = song;
		return 0;
	}

	/// <summary>
	/// <c>HmiMidi_ResetSong</c> (<c>0006eb98</c>): every track back at its start, then each track whose map entry is
	/// <see cref="Unrouted"/> without events and off the count of tracks playing. The routing is not done again.
	/// </summary>
	public void Reset(Es1HmiSong song) {
		song.TracksPlaying = song.TrackCount;
		foreach (var track in song.Tracks) {
			track.Start();
		}

		for (int t = 0; t < song.TrackCount; t++) {
			if (song.TrackMap[t] == Unrouted) {
				song.Tracks[t].Pos = Es1HmiTrack.NoEvents;
				song.TracksPlaying--;
			}
		}
	}

	/// <summary><c>HmiMidi_FreeSong</c> (<c>0006ea3d</c>): the slot's song forgotten, the slot free.</summary>
	public void Free(Es1HmiSong song) => _songs[song.Slot] = null;
}
