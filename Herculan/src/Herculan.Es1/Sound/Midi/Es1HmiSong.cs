using HercWorks.Core.Data.File;

namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// A song the library holds in one of its eight song slots (docs/es1/sound/song-player.md#loading): the file, the track
/// map that routes each track to a driver slot, and each track's place in its events. Built by
/// <see cref="Es1HmiSongs.Init"/>, whose state it keeps in the library's per-song tables.
/// </summary>
public sealed class Es1HmiSong {
	internal Es1HmiSong(int slot, Es1HmpSong file, int[] trackMap, Action<int>? endCallback) {
		Slot = slot;
		File = file;
		TrackMap = trackMap;
		EndCallback = endCallback;
		TrackCount = file.TrackCount;
		TracksPlaying = file.TrackCount;
		Tracks = new Es1HmiTrack[file.TrackCount];
	}

	/// <summary>The song slot, 0–7: the song's handle.</summary>
	public int Slot { get; }

	/// <summary>The file the song plays.</summary>
	public Es1HmpSong File { get; }

	/// <summary>
	/// The caller's track map: for each track the driver slot it plays on, <see cref="Es1HmiSongs.Unrouted"/> for a track
	/// no driver answers. The loader writes into the caller's array.
	/// </summary>
	public int[] TrackMap { get; }

	/// <summary>
	/// The callback the file's <c>+0x300</c> holds, called with the song's handle when the last track ends
	/// (docs/es1/sound/song-player.md#the-end).
	/// </summary>
	public Action<int>? EndCallback { get; }

	/// <summary>The file's track count, from <c>+0x30</c>: how many tracks the player walks.</summary>
	public int TrackCount { get; }

	/// <summary>The tracks still playing: <see cref="TrackCount"/> less each dropped or ended one.</summary>
	public int TracksPlaying { get; internal set; }

	/// <summary>The tracks, in file order.</summary>
	public Es1HmiTrack[] Tracks { get; }

	/// <summary>Whether the song is started (<c>000a22ac</c>): <see cref="Es1HmiPlayer.Tick"/> does nothing for one that is not.</summary>
	public bool Playing { get; set; }

	/// <summary>Whether the song is paused (<c>000a226c</c>): the tick does nothing for it.</summary>
	public bool Paused { get; set; }

	/// <summary>
	/// The song volume out of 127 (<c>000a56dc</c>), 127 until <c>HmiMidi_SetSongVolume</c> sets it
	/// (docs/es1/sound/song-player.md#volume).
	/// </summary>
	public int SongVolume { get; set; } = 127;

	/// <summary>Whether the song is muted (<c>000a228c</c>): its controller 7 values go out as 0.</summary>
	public bool Muted { get; set; }

	/// <summary>
	/// The loop callback (<c>000a5fbc</c>), called with (song, track, id, count left); false stops the branch
	/// (docs/es1/sound/song-player.md#the-branch-controllers).
	/// </summary>
	public Func<int, int, int, int, bool>? LoopCallback { get; set; }

	/// <summary>The branch callback (<c>000a5f8c</c>), called with (song, track, id); false stops the branch.</summary>
	public Func<int, int, int, bool>? BranchCallback { get; set; }

	/// <summary>The trigger callbacks (<c>000a5fec</c>), 127 a song, called with (song, track, number).</summary>
	public Action<int, int, int>?[] TriggerCallbacks { get; } = new Action<int, int, int>?[127];
}

/// <summary>One track of an <see cref="Es1HmiSong"/>: where its next event is and when.</summary>
public sealed class Es1HmiTrack {
	/// <summary><see cref="Pos"/> when the track has no events pointer: dropped or ended.</summary>
	public const int NoEvents = -1;

	internal Es1HmiTrack(Es1HmpTrack chunk, Es1SongBranch[] branches) {
		Events = chunk.Events;
		Channel = chunk.Channel;
		Branches = branches;
	}

	/// <summary>The chunk's events, delta first.</summary>
	public byte[] Events { get; }

	/// <inheritdoc cref="Es1HmpTrack.Channel"/>
	public int Channel { get; }

	/// <summary>The offset in <see cref="Events"/> of the next event, past its delta, or <see cref="NoEvents"/>.</summary>
	public int Pos { get; set; }

	/// <summary>The ticks from the last event to the one at <see cref="Pos"/>.</summary>
	public int Delta { get; set; }

	/// <summary>The ticks since the last event.</summary>
	public int TickCount { get; set; }

	/// <summary>Whether the track has an events pointer.</summary>
	public bool HasEvents => Pos != NoEvents;

	/// <summary>The track's branch entries, in the file's order.</summary>
	public Es1SongBranch[] Branches { get; }

	/// <summary>
	/// The track's start (<c>HmiMidi_InitSong</c>'s step 4): the tick count 0 and <see cref="Pos"/> past the first delta,
	/// which becomes <see cref="Delta"/>.
	/// </summary>
	internal void Start() {
		TickCount = 0;
		Pos = ReadDelta(Events, 0, out int delta);
		Delta = delta;
	}

	/// <summary>
	/// <c>HmiMidi_ReadDelta</c> (<c>0006ed60</c>): seven bits a byte, the first byte the lowest seven, ending at the byte
	/// whose top bit is set; answers the offset past the delta. Retail reads on past the end of the track; this stops at it.
	/// </summary>
	internal static int ReadDelta(byte[] events, int at, out int delta) {
		delta = 0;
		int shift = 0;
		while (at < events.Length) {
			byte b = events[at++];
			delta |= (b & 0x7f) << shift;
			shift += 7;
			if ((b & 0x80) != 0) {
				break;
			}
		}

		return at;
	}
}

/// <summary>One branch entry of a track (docs/es1/formats/hmp-music.md#layout), its data resolved from the file.</summary>
public sealed class Es1SongBranch {
	internal Es1SongBranch(Es1HmpBranch entry, byte[] data) {
		LandingOffset = BitConverter.ToInt32(entry.Head, 0);
		Id = entry.Head[4];
		Program = entry.Head[5];
		LoopCount = entry.Head[6];
		Data = data;
	}

	/// <summary>
	/// <c>+0x00</c>: the offset into the track's events of the event the branch lands on, as the file holds it; the
	/// song player's branches (docs/es1/sound/song-player.md#the-branch-controllers) read it.
	/// </summary>
	public int LandingOffset { get; }

	/// <summary><c>+0x04</c>: the branch's id.</summary>
	public byte Id { get; }

	/// <summary><c>+0x05</c>: the program a branch to it sets.</summary>
	public byte Program { get; }

	/// <summary>
	/// <c>+0x06</c>: the loop count, written while the song plays. It starts from the file's value at each
	/// <see cref="Es1HmiSongs.Init"/>, where retail writes it into the loaded file: there it carries over to the next play
	/// and to every song object playing the same file. Each retail song's track 1 writes its count with <c>0x6d</c> before
	/// its <c>0x6f</c> reads it, so the two play alike.
	/// </summary>
	public byte LoopCount { get; set; }

	/// <summary><c>+0x08</c>'s data: controller and value pairs, the length <c>+0x07</c> gives.</summary>
	public byte[] Data { get; }
}
