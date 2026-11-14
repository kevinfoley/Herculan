using HercWorks.Core.Data.File;
using Herculan.Es1.Sound.Midi;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The song player (docs/es1/sound/song-player.md#playing) on constructed songs, into a driver that records what it is
/// sent: the songs' loop, the routing of a track and the volume.
/// </summary>
public class Es1HmiPlayerTests {
	/// <summary>
	/// Track 1's loop controller, its entry's count 2, branches once: track 1 and the later track 2 each move to their
	/// entry's landing, get its program, and play their first note again on the same tick. Catches the branch starting past
	/// track 1 or leaving the later tracks, the program change not sent, and the loop not counted down.
	/// </summary>
	[Fact]
	public void ALoopBranchesTheLaterTracks() {
		var (player, driver, song) = Play(loopCount: 2);

		player.Tick(song);
		player.Tick(song);

		Assert.Equal([
			[0x91, 0x3c, 0x40], [0x92, 0x40, 0x40],
			[0xc1, 5], [0xc2, 7], [0x91, 0x3c, 0x40], [0x92, 0x40, 0x40],
		], driver.Calls.Select(c => c.Bytes));
	}

	/// <summary>
	/// With its entry's loop count 0 the loop controller does not branch: the tracks play through to their ends and the song
	/// ends once, its end callback given its handle. Catches a count of 0 looping forever as <c>0xff</c> does, or wrapping
	/// round to 255.
	/// </summary>
	[Fact]
	public void ALoopCountOfZeroEndsTheSong() {
		var ended = new List<int>();
		var (player, driver, song) = Play(loopCount: 0, ended.Add);

		for (int tick = 0; tick < 40 && song.Playing; tick++) {
			player.Tick(song);
		}

		Assert.False(song.Playing);
		Assert.Equal([song.Slot], ended);
		Assert.DoesNotContain(driver.Calls, c => (c.Bytes[0] & 0xf0) == 0xc0);
	}

	/// <summary>
	/// A track goes to the first of its device ids that an open driver answers, not to the first slot answering any:
	/// with <c>0xa005</c> in slot 0 and <c>0xa002</c> in slot 1, a track listing <c>0xa002</c> first plays on slot 1 and one
	/// listing <c>0xa005</c> first on slot 0. Catches the loops nested the other way.
	/// </summary>
	[Fact]
	public void RoutingFollowsTheOrderOfATracksDeviceIds() {
		var drivers = new Es1MidiDrivers();
		Assert.True(drivers.TryAdd(0xa005, new Es1RecordingMidiDriver(), out _));
		Assert.True(drivers.TryAdd(0xa002, new Es1RecordingMidiDriver(), out _));
		var file = Song([
			Track(1, [0xa002, 0xa005, 0, 0, 0], [0x80, 0xff, 0x2f, 0]),
			Track(2, [0xa005, 0xa002, 0, 0, 0], [0x80, 0xff, 0x2f, 0]),
		], []);
		int[] map = [Es1HmiSongs.Unrouted, Es1HmiSongs.Unrouted];

		Assert.Equal(0, new Es1HmiSongs(drivers).Init(file, map, null, out _));

		Assert.Equal([1, 0], map);
	}

	/// <summary>
	/// A controller 7 goes out scaled by the song's and the master volume, but the player keeps the raw value, which a later
	/// master volume scales afresh. Catches the scaled value kept in its place.
	/// </summary>
	[Fact]
	public void Controller7KeepsTheRawValue() {
		var (player, driver, song) = Play(loopCount: 2);
		song.SongVolume = 64;

		player.SendEvent(song, [0xb3, 7, 100], 0);
		player.SetMasterVolume(64);

		// Sent: ((100 × 64) >> 7) × 127 >> 7 = 49. The master volume's burst, one controller 7 a channel from 0, then
		// gives channel 3 100 × 64 >> 7 = 50, where the scaled value kept would give 24.
		Assert.Equal(new byte[] { 0xb3, 7, 49 }, driver.Calls[0].Bytes);
		Assert.Equal(100, player.KeptVolume(0, 3));
		Assert.Equal(new byte[] { 0xb3, 7, 50 }, driver.Calls[1 + 3].Bytes);
	}

	/// <summary>
	/// A started song of three tracks on an <c>0xa002</c> driver in slot 0. Track 0 ends at once; track 1 (channel 1) plays
	/// a note, then a tick later the loop controller of id 1, then ends; track 2 (channel 2) plays a note and ends 16 ticks
	/// on. Tracks 1 and 2 hold an entry of id 1 landing on their note, with programs 5 and 7; track 1's has
	/// <paramref name="loopCount"/>.
	/// </summary>
	private static (Es1HmiPlayer, Es1RecordingMidiDriver, Es1HmiSong) Play(byte loopCount, Action<int>? end = null) {
		var drivers = new Es1MidiDrivers();
		var driver = new Es1RecordingMidiDriver();
		Assert.True(drivers.TryAdd(0xa002, driver, out int slot));
		Assert.Equal(0, slot);
		int[] fm = [0xa002, 0, 0, 0, 0];
		var file = Song([
			Track(0, fm, [0x80, 0xff, 0x2f, 0]),
			Track(1, fm, [0x80, 0x91, 0x3c, 0x40, 0x81, 0xb1, 0x6f, 1, 0x80, 0xff, 0x2f, 0]),
			Track(2, fm, [0x80, 0x92, 0x40, 0x40, 0x90, 0xff, 0x2f, 0]),
		], [(1, 1, 5, loopCount), (2, 1, 7, 0)]);
		var songs = new Es1HmiSongs(drivers);
		Assert.Equal(0, songs.Init(file, [Es1HmiSongs.Unrouted, Es1HmiSongs.Unrouted, Es1HmiSongs.Unrouted], end, out var song));
		Es1HmiPlayer.Start(song!);
		return (new Es1HmiPlayer(drivers, songs), driver, song!);
	}

	/// <summary>
	/// A song of <paramref name="tracks"/>, each with its device ids, and branch entries landing at offset 0 of their track,
	/// with no data: (track, id, program, loop count).
	/// </summary>
	private static Es1HmpSong Song((Es1HmpTrack Track, int[] Devices)[] tracks, (int Track, byte Id, byte Program, byte Count)[] branches) {
		const int branchTable = 0x1000;
		int dataStart = branchTable + tracks.Length + branches.Length * 24;
		return new Es1HmpSong {
			Signature = [.. "HMIMIDIP"u8.ToArray(), .. new byte[24]],
			BranchTableOffset = branchTable,
			TrackCount = tracks.Length,
			TrackDevices = tracks.Select(t => t.Devices).ToArray(),
			Tracks = tracks.Select(t => t.Track).ToList(),
			BranchCounts = Enumerable.Range(0, tracks.Length).Select(t => (byte)branches.Count(b => b.Track == t)).ToArray(),
			Branches = branches.OrderBy(b => b.Track)
				.Select(b => new Es1HmpBranch { Head = [0, 0, 0, 0, b.Id, b.Program, b.Count, 0], DataOffset = dataStart })
				.ToList(),
		};
	}

	/// <summary>A track on <paramref name="channel"/> listing <paramref name="devices"/>, with <paramref name="events"/>.</summary>
	private static (Es1HmpTrack, int[]) Track(int channel, int[] devices, byte[] events) =>
		(new Es1HmpTrack { Channel = channel, Events = events, Length = events.Length }, devices);
}
