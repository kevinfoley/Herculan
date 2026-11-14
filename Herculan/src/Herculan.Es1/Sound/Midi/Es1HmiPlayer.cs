namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// The song player (docs/es1/sound/song-player.md#playing): the tick <c>HmiMidi_SongTick</c> (<c>0007ef95</c>) and what
/// it calls, <c>HmiMidi_SendEvent</c> (<c>000715c5</c>) and <c>HmiMidi_SilenceSong</c> (<c>00071f84</c>), over the open
/// drivers and the loaded songs. Both programs turn the channel-stealing mode off, so only its off path is here.
///
/// <para>The library's timer is not ported: nothing here calls <see cref="Tick"/>, the owner calls it once for each
/// interrupt of the game's timer, as the timer's dispatcher does for a song at 120 ticks a second
/// (docs/es1/sound/song-player.md#the-librarys-timer). The byte <c>000a77bc</c>, which a callback clears by branching
/// through <c>HmiMidi_BranchTrack</c> or <c>HmiMidi_BranchSong</c>, is not ported either: <c>es2_xref.py</c> finds no
/// call of either function.</para>
///
/// <para>Where the tick walks past what a retail file holds, the engine ends the track or skips the controller instead of
/// reading on: an event run off the end of its track, an event of length 0, a branch controller whose id its track has no
/// entry for, and a trigger number of 127 or more. A branch entry's odd last data byte is not sent, where retail sends it
/// with the byte after the data. None of these occurs in the 19 retail songs. An event for a driver slot with no driver is
/// dropped, where retail calls the slot's entry whatever it holds; with no driver open, every event of track 0 is.</para>
/// </summary>
public sealed class Es1HmiPlayer(Es1MidiDrivers drivers, Es1HmiSongs songs) {
	/// <summary>The length of an event by its status byte's high nibble, below <c>0xf0</c> (<c>000a22cc</c>).</summary>
	private static readonly byte[] LengthByKind = [0, 0, 0, 0, 0, 0, 0, 0, 3, 3, 3, 3, 2, 2, 3, 0];

	/// <summary>The length of an event by its status byte's low nibble, from <c>0xf0</c> (<c>000a22dc</c>).</summary>
	private static readonly byte[] LengthBySystem = [0, 1, 2, 1, 0, 0, 0, 0, 2, 2, 2, 2, 2, 2, 2, 2];

	private readonly byte[] _keptVolume = Enumerable.Repeat((byte)0x7f, Es1MidiDrivers.Slots * 16).ToArray();

	/// <summary>The master volume out of 127 (<c>000a56d8</c>), 127 in the image.</summary>
	public int MasterVolume { get; private set; } = 127;

	/// <summary>
	/// The controller 7 value last sent on a channel of a driver slot, before the song and master volumes scaled it
	/// (<c>000a56fc</c>); 127 in the image, for every channel, until one is sent.
	/// </summary>
	public byte KeptVolume(int driverSlot, int channel) => _keptVolume[driverSlot * 16 + channel];

	/// <summary>
	/// <c>HmiMidi_SetMasterVolume</c> (<c>0006edb3</c>) (docs/es1/sound/song-player.md#volume): the master volume set, and
	/// for each open driver from slot 0 up to the first free slot, on each channel its device reaches and on channel 9, a
	/// controller 7 of the kept value × the master volume >> 7 sent straight to the driver. The song volume is not applied,
	/// as in retail. The walk stops after the fifth slot, where retail's reads on past the table when all five are open.
	/// The channel's pause flag that retail's version also reads (<c>000a785c</c>), which sends 0 instead, is not ported:
	/// <c>HmiMidi_PauseSong</c> sets it, and the scan finds no call through the song's vtable slot that reaches that
	/// function (docs/es1/formats/sound-samples.md#open).
	/// </summary>
	public void SetMasterVolume(byte volume) {
		MasterVolume = volume;
		for (int slot = 0; slot < Es1MidiDrivers.Slots && drivers.DeviceId(slot) != 0; slot++) {
			if (drivers.Driver(slot) is not { } driver) {
				continue;
			}

			for (int channel = 0; channel < 16; channel++) {
				if (drivers.ReachesChannel(slot, channel) || channel == 9) {
					driver.SendEvent([(byte)(0xb0 | channel), 7, (byte)(KeptVolume(slot, channel) * MasterVolume >> 7)], slot);
				}
			}
		}
	}

	/// <summary>
	/// <c>HmiMidi_StartSong</c> (<c>0006ea6b</c>): the song marked playing. The retail function also registers the song's
	/// tick with the library's timer, which the owner of <see cref="Tick"/> stands in for.
	/// </summary>
	public static void Start(Es1HmiSong song) => song.Playing = true;

	/// <summary>
	/// <c>HmiMidi_StopSong</c> (<c>0006ead9</c>): for a song playing, silenced, not playing, and its tracks back at their
	/// starts; no end callback. The timer event it also removes is not ported.
	/// </summary>
	public void Stop(Es1HmiSong song) {
		if (song.Playing) {
			Silence(song);
			song.Playing = false;
			songs.Reset(song);
		}
	}

	/// <summary>
	/// <c>HmiMidi_SendEvent</c> (docs/es1/sound/song-player.md#the-tick): <paramref name="midiEvent"/> to the driver in
	/// <paramref name="driverSlot"/> as the file holds it, except a controller 7, whose value is kept per slot and channel and
	/// sent as ((value × the song volume) >> 7) × the master volume >> 7, or 0 while the song is muted.
	/// </summary>
	public void SendEvent(Es1HmiSong song, ReadOnlySpan<byte> midiEvent, int driverSlot) {
		if (drivers.Driver(driverSlot) is not { } driver) {
			return;
		}

		if (midiEvent.Length >= 3 && (midiEvent[0] & 0xf0) == 0xb0 && midiEvent[1] == 7) {
			_keptVolume[driverSlot * 16 + (midiEvent[0] & 0xf)] = midiEvent[2];
			byte scaled = song.Muted ? (byte)0 : (byte)(((midiEvent[2] * song.SongVolume >> 7) * MasterVolume) >> 7);
			driver.SendEvent([midiEvent[0], 7, scaled], driverSlot);
		} else {
			driver.SendEvent(midiEvent, driverSlot);
		}
	}

	/// <summary>
	/// <c>HmiMidi_SilenceSong</c> (docs/es1/sound/song-player.md#the-end): for each track routed to a driver, on the chunk's
	/// channel, controller <c>0x7b</c> 0, controller <c>0x79</c> 0, the pitch bend <c>En 40 40</c> and controller 7 with 0,
	/// sent straight to the driver.
	/// </summary>
	public void Silence(Es1HmiSong song) {
		for (int t = 0; t < song.TrackCount; t++) {
			int slot = song.TrackMap[t];
			if (slot is -1 or Es1HmiSongs.Unrouted || drivers.Driver(slot) is not { } driver) {
				continue;
			}

			byte channel = (byte)song.Tracks[t].Channel;
			driver.SendEvent([(byte)(channel | 0xb0), 0x7b, 0], slot);
			driver.SendEvent([(byte)(channel | 0xb0), 0x79, 0], slot);
			driver.SendEvent([(byte)(channel | 0xe0), 0x40, 0x40], slot);
			driver.SendEvent([(byte)(channel | 0xb0), 7, 0], slot);
		}
	}

	/// <summary>
	/// <c>HmiMidi_SongTick</c> (docs/es1/sound/song-player.md#the-tick), for one interrupt: nothing for a song not playing
	/// or paused; otherwise each track in turn gets one more tick, and while it has events and the ticks have reached its
	/// delta, its event is handled and the next delta read, until a delta above 0. The song's fade, which runs first in
	/// retail, is not ported: <c>es2_xref.py</c> finds no call of <c>HmiMidi_FadeSong</c>.
	/// </summary>
	public void Tick(Es1HmiSong song) {
		if (!song.Playing || song.Paused) {
			return;
		}

		for (int t = 0; t < song.TrackCount; t++) {
			var track = song.Tracks[t];
			track.TickCount++;
			if (!track.HasEvents || track.Delta > track.TickCount) {
				continue;
			}

			do {
				bool advance = true;
				int length = HandleEvent(song, t, ref advance);
				if (length < 0) {
					return;
				}

				track.TickCount = 0;
				if (!track.HasEvents) {
					break;
				}

				track.Pos += length;
				if (advance) {
					track.Pos = Es1HmiTrack.ReadDelta(track.Events, track.Pos, out int delta);
					track.Delta = delta;
				}
			} while (track.Delta == 0);
		}
	}

	/// <summary>
	/// Handles the event at the track's place: answers the bytes to move on by, or −1 when the song ended.
	/// <paramref name="advance"/> is cleared when a branch has read the next delta already.
	/// </summary>
	private int HandleEvent(Es1HmiSong song, int t, ref bool advance) {
		var track = song.Tracks[t];
		var events = track.Events;
		int at = track.Pos;
		byte status = at < events.Length ? events[at] : (byte)0xff;
		byte next = at + 1 < events.Length ? events[at + 1] : (byte)0x2f;
		int length = status < 0xf0 ? LengthByKind[status >> 4] : LengthBySystem[status & 0xf];
		if (at >= events.Length || length == 0 && status != 0xff) {
			status = 0xff;
			next = 0x2f;
		}

		if (status == 0xff) {
			if (next == 0x2f) {
				track.Pos = Es1HmiTrack.NoEvents;
				if (--song.TracksPlaying == 0) {
					End(song);
					return -1;
				}

				return 3;
			}

			return next == 0x51 ? 5 : length;
		}

		if ((status & 0xf0) != 0xb0) {
			SendEvent(song, events.AsSpan(at, Math.Min(length, events.Length - at)), song.TrackMap[t]);
			return length;
		}

		byte value = at + 2 < events.Length ? events[at + 2] : (byte)0;
		switch (next) {
			case 0x6d:
			case 0x73:
				if (Find(track, value) is { } counted && at + 6 < events.Length) {
					counted.LoopCount = events[at + 6];
				}

				return length;
			case 0x6f:
			case 0x70:
				if (Find(track, value) is { } songLoop && Loop(song, t, songLoop)) {
					BranchSong(song, value, ref advance);
					return 0;
				}

				return length;
			case 0x75:
			case 0x76:
				if (Find(track, value) is { } trackLoop && Loop(song, t, trackLoop)) {
					BranchTrack(song, t, trackLoop);
					return 0;
				}

				return length;
			case 0x72:
				if (Find(track, (byte)(value | 0x80)) != null && (song.BranchCallback?.Invoke(song.Slot, t, value | 0x80) ?? true)) {
					BranchSong(song, (byte)(value | 0x80), ref advance);
					return 0;
				}

				return length;
			case 0x79:
				if (Find(track, value) is { } single && (song.BranchCallback?.Invoke(song.Slot, t, value) ?? true)) {
					BranchTrack(song, t, single);
					return 0;
				}

				return length;
			case 0x77:
				if (value < song.TriggerCallbacks.Length) {
					song.TriggerCallbacks[value]?.Invoke(song.Slot, t, value);
				}

				return length;
			case 0x6c:
			case 0x6e:
			case 0x71:
			case 0x74:
			case 0x78:
				return length;
			default:
				SendEvent(song, events.AsSpan(at, Math.Min(length, events.Length - at)), song.TrackMap[t]);
				return length;
		}
	}

	/// <summary>The entry of the track's list with <paramref name="id"/>, the first of them; null for none.</summary>
	private static Es1SongBranch? Find(Es1HmiTrack track, byte id) => track.Branches.FirstOrDefault(b => b.Id == id);

	/// <summary>
	/// A loop end: takes 1 off the entry's loop count unless it is 0 or <c>0xff</c>, asks the loop callback (false makes the
	/// count 0), and answers whether a count is left to branch on.
	/// </summary>
	private static bool Loop(Es1HmiSong song, int t, Es1SongBranch entry) {
		int count = entry.LoopCount;
		if (count is not (0xff or 0)) {
			entry.LoopCount--;
			count--;
		}

		if (song.LoopCallback is { } callback && !callback(song.Slot, t, entry.Id, count)) {
			count = 0;
		}

		return count != 0;
	}

	/// <summary>
	/// The branch of a whole song: each track from 1 that has events moves to its entry of <paramref name="id"/> (one
	/// without that entry stays), reads the delta there, zeroes its ticks, sends a program change with the entry's program
	/// on the channel of the event it lands on, then each pair of the entry's data as a controller on that channel.
	/// <paramref name="advance"/> is cleared once a track has been moved.
	/// </summary>
	private void BranchSong(Es1HmiSong song, byte id, ref bool advance) {
		for (int t = 1; t < song.TrackCount; t++) {
			var track = song.Tracks[t];
			if (!track.HasEvents || Find(track, id) is not { } entry) {
				continue;
			}

			track.Pos = Es1HmiTrack.ReadDelta(track.Events, entry.LandingOffset, out int delta);
			track.Delta = delta;
			track.TickCount = 0;
			advance = false;
			byte channel = (byte)(track.Pos < track.Events.Length ? track.Events[track.Pos] & 0xf : 0);
			SendBranchSetup(song, t, entry, channel);
		}
	}

	/// <summary>
	/// The branch of one track: its events pointer moved to the entry's landing, the delta there not yet read, and the
	/// program change and controllers sent on the channel of the track's chunk.
	/// </summary>
	private void BranchTrack(Es1HmiSong song, int t, Es1SongBranch entry) {
		var track = song.Tracks[t];
		track.Pos = entry.LandingOffset;
		SendBranchSetup(song, t, entry, (byte)track.Channel);
	}

	private void SendBranchSetup(Es1HmiSong song, int t, Es1SongBranch entry, byte channel) {
		int slot = song.TrackMap[t];
		SendEvent(song, [(byte)(channel | 0xc0), entry.Program], slot);
		for (int i = 0; i + 1 < entry.Data.Length; i += 2) {
			SendEvent(song, [(byte)(channel | 0xb0), entry.Data[i], entry.Data[i + 1]], slot);
		}
	}

	/// <summary>
	/// The song's end (docs/es1/sound/song-player.md#the-end), when its last track ends: not playing, silenced, its tracks
	/// back at their starts, then the end callback called with the song's handle.
	/// </summary>
	private void End(Es1HmiSong song) {
		song.Playing = false;
		Silence(song);
		songs.Reset(song);
		song.EndCallback?.Invoke(song.Slot);
	}
}
