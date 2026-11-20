#:project ../../Herculan/src/Herculan.Es1/Herculan.Es1.csproj

// Compares the port's FM register writes for a retail song with a DOSBox-X capture of the same song
// (docs/herculan/plan-es1-sound.md, module D part 7 step 4).
//
//   dotnet run tools/scripts/es1_dro_compare.cs -- dump <capture.dro> [--from N] [--count N]
//   dotnet run tools/scripts/es1_dro_compare.cs -- compare <install> <capture.dro> <song> [options]
//
// dump prints a capture's writes, each with its index and time in milliseconds.
//
// A capture starts at the chip's first key-on after it is armed: DOSBox-X first writes its register cache, every
// register that is not 0 in register order with the key-on bit masked off 0xb0-0xb8, then the key-on itself. After
// that it holds only the writes that change a register's value.
//
// compare plays <song> (a file name such as menu.hmp, found in the shell's res32\ or the simulator's hmi\) through
// the shell's sound path: Es1SoundManager.LoadDrivers over the install's sos\ files, the shell's master volume, the
// song made and played as ShellMusic_Play makes it, then Es1MidiSystem.Tick once for each of the timer's 120
// interrupts a second, into a chip that records every write with its tick. From the port's first key-on, its writes
// are filtered as the capture's are, against the capture's cache, and the two lists walked side by side; a difference
// is reported with the writes around it and the walk resynchronizes on the next run of matching writes.
//   --from N      the capture index the comparison starts at: the capture's writes before the first key-on at or
//                 after it are the cache the port's writes are filtered against, default 0
//   --to N        the capture index the comparison stops before, default the capture's end
//   --search S    align at the first key-on in the song's first S seconds after which the port's next 64 writes are
//                 the capture's, for a capture that starts in the middle of a song; default 0, the song's first key-on
//   --prelude S   a song played, then stopped as ShellMusic_Stop stops one but with no fade, before <song> starts, for
//                 a capture whose song follows another: the stop's pitch bends and the voices' instruments carry over;
//                 a comma-separated list plays each in turn, for the session's songs since the driver opened, whose
//                 stops' bends all mark their channels
//   --prelude-ms M  how long each prelude plays, default 10000
//   --switch-at-ms T  plays <song>, one of the simulator's three calm songs, through the simulator's sound path
//                 instead: Sound_Init with the catalog, Music_Tick once a frame, and at T ms into the song what
//                 Music_Tick's check sets on seeing an enemy, so that the song fades and its battle song follows; no
//                 check is due otherwise. Not with --prelude
//   --frame-interrupts N  the timer's interrupts a frame with --switch-at-ms, default 3, the engine's tick
//   --master V    the sound manager's master volume, default the shell's 95, or the simulator's 90 with --switch-at-ms
//   --seconds S   how long to play the song, default the compared writes' length, 2 seconds and the search
//   --show N      matching writes printed before each difference, default 6
//   --diffs N     differences printed, default 10; the rest are counted
//   --window N    how far ahead either list is searched to resynchronize, default 400
using Herculan.Engine.Audio;
using Herculan.Engine.Audio.Opl;
using Herculan.Engine.Content;
using Herculan.Es1.Shell;
using Herculan.Es1.Sim;
using Herculan.Es1.Sound;

if (args.Length < 2) {
	Console.Error.WriteLine("usage: dump <capture.dro> [--from N] [--count N] | compare <install> <capture.dro> <song> [options]");
	return 2;
}

var options = new Dictionary<string, string>();
var positional = new List<string>();
for (int i = 1; i < args.Length; i++) {
	if (args[i].StartsWith("--") && i + 1 < args.Length) {
		options[args[i][2..]] = args[++i];
	} else {
		positional.Add(args[i]);
	}
}

int Option(string name, int fallback) => options.TryGetValue(name, out var v) ? int.Parse(v) : fallback;

if (args[0] == "dump") {
	var capture = Dro.Read(positional[0]);
	int from = Option("from", 0), count = Option("count", 200);
	Console.WriteLine($"{capture.Count} writes, {capture[^1].Ms} ms");
	for (int i = from; i < Math.Min(capture.Count, from + count); i++) {
		Console.WriteLine($"{i,6} {capture[i].Ms,7} ms  {Describe(capture[i])}");
	}

	return 0;
}

if (args[0] != "compare" || positional.Count < 3) {
	Console.Error.WriteLine("compare takes <install> <capture.dro> <song>");
	return 2;
}

string install = positional[0];
var captured = Dro.Read(positional[1]);
string songName = positional[2];
int show = Option("show", 6), maxDiffs = Option("diffs", 10), window = Option("window", 400);

// The capture's cache, and its live writes from the first key-on at or after --from, up to --to.
int fromIndex = Option("from", 0), to = Math.Min(Option("to", captured.Count), captured.Count);
int liveAt = captured.FindIndex(fromIndex, IsKeyOn);
if (liveAt < 0 || liveAt >= to) {
	Console.Error.WriteLine($"the capture has no key-on from {fromIndex} to {to}");
	return 1;
}

byte[] cache = [];
List<Write> live = [];

// The comparison started at the capture's key-on at index at: the writes before it are the cache.
void StartLive(int at) {
	liveAt = at;
	cache = new byte[0x200];
	foreach (var w in captured.Take(at)) {
		cache[w.Register] = w.Value;
	}

	live = captured.Take(to).Skip(at).Select(w => w with { Ms = w.Ms - captured[at].Ms }).ToList();
}

StartLive(liveAt);
int search = Option("search", 0);
int seconds = Option("seconds", (int)(live[^1].Ms / 1000) + 2 + search);

// The port.
var shell = Es1SingleMission.MountShell(install);
var sim = GameContent.MountInstall(install, disc: null, HercWorks.Vol.Voln.DbsimProgram, GameLanguage.English);
byte[]? Read(string folder, string name) => shell.Read(folder, name) ?? sim.Read(folder, name);
string? Folder(string name) => new[] { "res32", "hmi" }.FirstOrDefault(f => Read(f, name) != null);
string? folder = Folder(songName);
string? preludeName = options.GetValueOrDefault("prelude");
string[] preludeNames = preludeName?.Split(',') ?? [];
string?[] preludeFolders = [.. preludeNames.Select(Folder)];
if (folder == null) {
	Console.Error.WriteLine($"{songName} is in neither res32\\ nor hmi\\");
	return 1;
}

for (int i = 0; i < preludeNames.Length; i++) {
	if (preludeFolders[i] == null) {
		Console.Error.WriteLine($"{preludeNames[i]} is in neither res32\\ nor hmi\\");
		return 1;
	}
}

var chip = new RecordingChip();
int switchAt = Option("switch-at-ms", -1), frameInterrupts = Option("frame-interrupts", 3);
Es1SimGlobals? world = null;
Es1SoundManager manager;
if (switchAt < 0) {
	manager = new Es1SoundManager(Read, new Es1DigitalDriver(new SilentBackend()), chip) {
		Devices = Es1Sound.ReadDevices(install),
	};
	if (!manager.LoadDrivers()) {
		Console.Error.WriteLine("LoadDrivers failed");
		return 1;
	}

	manager.SetSampleNames([.. Es1ShellSound.Names, $@"{folder}\{songName}", .. preludeNames.Select((name, i) => $@"{preludeFolders[i]}\{name}")]);
} else {
	// The simulator's path: Sound_Init with the catalog, whose rows the songs are; a timer that never runs, since the
	// loop below ticks the songs itself.
	world = new Es1SimGlobals();
	world.Sound.Init(Es1Sound.ReadDevices(install), false, world.VoiceLines, Read, new SilentBackend(), new Es1SimClock(), chip);
	if (world.Sound.Manager is not { DriversLoaded: true } loaded) {
		Console.Error.WriteLine("Sound_Init made no manager or LoadDrivers failed");
		return 1;
	}

	manager = loaded;
	// The preferences' load with MUSIC on.
	world.Sound.SetMusicEnabled(1);
	if (preludeName != null) {
		Console.Error.WriteLine("--prelude plays through the shell's path, which --switch-at-ms replaces");
		return 2;
	}
}

manager.SetMasterVolume((sbyte)Option("master", world == null ? Es1ShellSound.MasterVolume : Es1SoundManager.DefaultMasterVolume));

// A song made and played as ShellMusic_Play does; null when it does not play.
Es1SoundObject? Start(short index) {
	var sound = manager.CreateObject(2, 0, index)!;
	sound.LoadSample();
	sound.SetRepeats(-1, -1, -1);
	return sound.Play() ? sound : null;
}

int tick = 0;
for (int i = 0; i < preludeNames.Length; i++) {
	var prelude = Start((short)(Es1ShellSound.Names.Length + 1 + i));
	if (prelude == null) {
		Console.Error.WriteLine($"{preludeNames[i]} does not play");
		return 1;
	}

	for (int end = tick + Option("prelude-ms", 10000) * 120 / 1000; tick < end; tick++) {
		chip.Tick = tick;
		manager.Midi.Tick();
	}

	prelude.Stop();
	prelude.Destroy();
}

int songTick = tick;
int songWrite = chip.Writes.Count;
Es1SoundObject? music;
if (world == null) {
	music = Start((short)Es1ShellSound.Names.Length);
} else {
	// The calm song as Music_Tick's first call leaves it, with no check due; the first frame's call starts it.
	short row = (short)Enumerable.Range(0, Es1Music.CalmSongs)
		.FirstOrDefault(r => manager.Name(r)?.EndsWith(songName, StringComparison.OrdinalIgnoreCase) == true, -1);
	if (row < 0) {
		Console.Error.WriteLine($"{songName} is none of the catalog's calm songs, rows 0 to {Es1Music.CalmSongs - 1}");
		return 1;
	}

	world.Music.Song = row;
	world.Music.SongObject = world.Sound.Catalog(row);
	world.Music.NextCheck = uint.MaxValue;
	chip.Tick = tick;
	world.Music.Tick(world, 1);
	music = world.Sound.IsPlaying(row) ? world.Music.SongObject : null;
}

if (music == null) {
	Console.Error.WriteLine($"{songName} does not play");
	return 1;
}

if (Option("tracks", 0) != 0 && manager.Midi.Songs.Song(music.Handle) is { } song) {
	for (int t = 0; t < song.TrackCount; t++) {
		Console.WriteLine($"track {t,2}: channel {song.Tracks[t].Channel,2}, devices " +
			$"{string.Join(" ", song.File.TrackDevices[t].TakeWhile(d => d != 0).Select(d => d.ToString("x4")))}, " +
			$"{(song.TrackMap[t] == 0xff ? "dropped" : $"slot {song.TrackMap[t]}")}");
	}
}

bool switched = false;
for (; tick < songTick + seconds * 120; tick++) {
	chip.Tick = tick;
	if (world != null && tick > songTick && (tick - songTick) % frameInterrupts == 0) {
		// A frame: Music_Tick, then at the switch's time what the check sets on seeing an enemy, so that the fade's
		// first step is the next frame's.
		world.Music.Tick(world, 1);
		if (!switched && (tick - songTick) * 1000L >= switchAt * 120L) {
			world.Music.Switching = true;
			world.Music.Battle = true;
			switched = true;
		}
	}

	manager.Midi.Tick();
}

// The port's writes from a key-on, filtered against a copy of the capture's cache.
List<Write> Filter(int start, int limit) {
	var seen = (byte[])cache.Clone();
	var list = new List<Write>();
	int tick0 = chip.Writes[start].Tick;
	for (int k = start; k < chip.Writes.Count && list.Count < limit; k++) {
		var w = chip.Writes[k];
		if (seen[w.Register] != w.Value) {
			seen[w.Register] = w.Value;
			list.Add(w with { Ms = (w.Tick - tick0) * 1000L / 120 });
		}
	}

	return list;
}

// The alignment: the port's first key-on, or with --search the first key-on in that many seconds of the song from
// which the port's next 64 filtered writes are the capture's.
var keyOns = Enumerable.Range(songWrite, chip.Writes.Count - songWrite).Where(k => IsKeyOn(chip.Writes[k])).ToList();
if (keyOns.Count == 0) {
	Console.Error.WriteLine($"{songName} keys no note on in {seconds} s");
	return 1;
}

int portKeyOn = keyOns[0];
if (search > 0) {
	int head = Math.Min(64, live.Count);
	int found = keyOns.TakeWhile(k => chip.Writes[k].Tick - songTick < search * 120)
		.FirstOrDefault(k => Filter(k, head) is var f && f.Count == head && f.Select((w, n) => w.Same(live[n])).All(x => x), -1);
	if (found < 0) {
		// The notes alone: the frequency word of each key-on, whichever voice it is on, for the capture's first 24 after
		// the key-on that started it, which may be a bent frequency whose note on is in the cache.
		var captureNotes = Notes(captured, liveAt + 1, to, 24);
		var portNotes = Notes(chip.Writes, songWrite, chip.Writes.Count, int.MaxValue);
		int n = captureNotes.Count;
		int at = Enumerable.Range(0, Math.Max(0, portNotes.Count - n + 1))
			.FirstOrDefault(p => chip.Writes[portNotes[p].Index].Tick - songTick < search * 120 &&
				Enumerable.Range(0, n).All(k => portNotes[p + k].Word == captureNotes[k].Word), -1);
		if (at < 0) {
			Console.WriteLine($"no key-on in the song's first {search} s starts the capture's first {head} writes or its first {n} notes; comparing from the first key-on");
			Console.WriteLine($"  the capture's notes: {string.Join(" ", captureNotes.Select(c => c.Word.ToString("x4")))}");
			Console.WriteLine($"  the song's first:    {string.Join(" ", portNotes.Take(n).Select(c => c.Word.ToString("x4")))}");
			var best = Enumerable.Range(0, Math.Max(0, portNotes.Count - n + 1))
				.Select(p => (p, Count: Enumerable.Range(0, n).Count(k => portNotes[p + k].Word == captureNotes[k].Word)))
				.MaxBy(x => x.Count);
			Console.WriteLine($"  the closest:         {string.Join(" ", portNotes.Skip(best.p).Take(n).Select(c => c.Word.ToString("x4")))}" +
				$", {best.Count} of {n} at {(chip.Writes[portNotes[best.p].Index].Tick - songTick) / 120.0:0.000} s");
		} else {
			portKeyOn = portNotes[at].Index;
			StartLive(captureNotes[0].Index);
			Console.WriteLine($"no key-on in the song's first {search} s starts the capture's first {head} writes; its {n} notes from {liveAt} start at the port's key-on {portKeyOn}");
		}
	} else {
		portKeyOn = found;
	}
}

Console.WriteLine($"{songName} from {folder}\\, {seconds} s; the capture's live writes from {liveAt} to {to}, {live.Count} of them, {live[^1].Ms} ms");
Console.WriteLine($"the port aligned at its key-on in tick {chip.Writes[portKeyOn].Tick - songTick}, {(chip.Writes[portKeyOn].Tick - songTick) / 120.0:0.000} s into the song" +
	(preludeName == null ? "" : $", after {preludeName} played {songTick / 120.0:0.000} s and stopped"));

// The port's registers at that key-on against the capture's cache, for the registers the port has written.
var state = new byte[0x200];
var written = new bool[0x200];
foreach (var w in chip.Writes.Take(portKeyOn)) {
	state[w.Register] = w.Value;
	written[w.Register] = true;
}

var stateDiffs = Enumerable.Range(0, 0x200).Where(r => written[r] && state[r] != cache[r]).ToList();
Console.WriteLine($"before it the port writes {written.Count(x => x)} registers, " +
	$"{stateDiffs.Count} of them with a value other than the capture's cache" +
	(stateDiffs.Count == 0 ? "" : ": " + string.Join(", ", stateDiffs.Select(r => $"{r:x2} port {state[r]:x2} capture {cache[r]:x2}"))));

var port = Filter(portKeyOn, int.MaxValue);

// The walk.
int ci = 0, j = 0, matched = 0, diffs = 0, extraCapture = 0, extraPort = 0;
long worstDrift = 0;
const int Run = 4;
bool RunAt(int a, int b) {
	for (int k = 0; k < Run; k++) {
		if (a + k >= live.Count || b + k >= port.Count || !live[a + k].Same(port[b + k])) {
			return false;
		}
	}

	return true;
}

while (ci < live.Count && j < port.Count) {
	if (live[ci].Same(port[j])) {
		long drift = port[j].Ms - live[ci].Ms;
		if (Math.Abs(drift) > Math.Abs(worstDrift)) {
			worstDrift = drift;
		}

		matched++;
		ci++;
		j++;
		continue;
	}

	// The nearest resynchronization: the smallest skip on both sides after which Run writes match.
	(int a, int b) skip = (-1, -1);
	for (int total = 1; total <= 2 * window && skip.a < 0; total++) {
		for (int a = Math.Max(0, total - window); a <= Math.Min(total, window); a++) {
			if (RunAt(ci + a, j + total - a)) {
				skip = (a, total - a);
				break;
			}
		}
	}

	diffs++;
	if (diffs <= maxDiffs) {
		Console.WriteLine();
		Console.WriteLine($"difference {diffs}: capture {ci}, port {j}" +
			(skip.a < 0 ? ", no match after it" : $"; {skip.a} capture writes against {skip.b} of the port's"));
		for (int k = Math.Max(0, ci - show); k < ci; k++) {
			Console.WriteLine($"  same     {live[k].Ms,7} | {port[j - ci + k].Ms,7} ms  {Describe(live[k])}");
		}

		int lines = skip.a < 0 ? show : Math.Max(skip.a, skip.b);
		for (int k = 0; k < lines; k++) {
			string left = skip.a < 0 && ci + k < live.Count || k < skip.a ? $"{live[ci + k].Ms,7} ms  {Describe(live[ci + k]),-44}" : new string(' ', 54);
			string right = skip.a < 0 && j + k < port.Count || k < skip.b ? $"{port[j + k].Ms,7} ms  {Describe(port[j + k])}" : "";
			Console.WriteLine($"  {left} | {right}");
		}
	}

	if (skip.a < 0) {
		break;
	}

	extraCapture += skip.a;
	extraPort += skip.b;
	ci += skip.a;
	j += skip.b;
}

Console.WriteLine();
Console.WriteLine($"matched {matched} of the capture's {live.Count} live writes and the port's {port.Count}; " +
	$"{diffs} differences, {extraCapture} capture and {extraPort} port writes in them; walk stopped at capture {ci}, port {j}");
Console.WriteLine($"the largest time difference of a matched write: {worstDrift} ms (port − capture)");
return diffs == 0 ? 0 : 1;

static bool IsKeyOn(Write w) => w.Register is >= 0xb0 and <= 0xb8 && (w.Value & 0x20) != 0;

// The key-ons from start up to end, each as its index and frequency word, block and F-number, from the 0xa0 byte its
// voice holds; the writes before start give the 0xa0 bytes. A key-on right after a write to its voice's 0xa0 that
// follows a key-on of the same voice is the note on's bent frequency, which depends on the channel's history, and is
// left out.
static List<(int Index, int Word)> Notes(List<Write> writes, int start, int end, int limit) {
	var low = new byte[9];
	var notes = new List<(int, int)>();
	for (int k = 0; k < end && notes.Count < limit; k++) {
		var w = writes[k];
		if (w.Register is >= 0xa0 and <= 0xa8) {
			low[w.Register - 0xa0] = w.Value;
		} else if (k >= start && IsKeyOn(w)) {
			bool bent = k >= 2 && writes[k - 1].Register == w.Register - 0x10 && writes[k - 2].Register == w.Register && IsKeyOn(writes[k - 2]);
			if (!bent) {
				notes.Add((k, (w.Value & 0x1f) << 8 | low[w.Register - 0xb0]));
			}
		}
	}

	return notes;
}

static string Describe(Write w) {
	int register = w.Register, value = w.Value;
	string what = register switch {
		0x01 => "test/wave enable",
		0x08 => "CSM/note select",
		0xbd => "depth/rhythm",
		>= 0x20 and < 0x40 => $"op {register - 0x20:x2} mult",
		>= 0x40 and < 0x60 => $"op {register - 0x40:x2} level",
		>= 0x60 and < 0x80 => $"op {register - 0x60:x2} attack/decay",
		>= 0x80 and < 0xa0 => $"op {register - 0x80:x2} sustain/release",
		>= 0xa0 and <= 0xa8 => $"voice {register - 0xa0} fnum low",
		>= 0xb0 and <= 0xb8 => $"voice {register - 0xb0} {((value & 0x20) != 0 ? "key on " : "key off")} block {(value >> 2) & 7}",
		>= 0xc0 and <= 0xc8 => $"voice {register - 0xc0} feedback/conn",
		>= 0xe0 and < 0x100 => $"op {register - 0xe0:x2} wave",
		_ => "",
	};
	return $"{register:x3} <- {value:x2}  {what}";
}

readonly record struct Write(int Register, byte Value, long Ms, int Tick) {
	public bool Same(Write other) => Register == other.Register && Value == other.Value;
}

static class Dro {
	/// <summary>A DOSBox DRO version 2 capture's register writes, each with the milliseconds of delay before it.</summary>
	public static List<Write> Read(string path) {
		byte[] d = File.ReadAllBytes(path);
		if (System.Text.Encoding.ASCII.GetString(d, 0, 8) != "DBRAWOPL" || BitConverter.ToUInt16(d, 8) != 2) {
			throw new InvalidDataException($"{path} is not a DRO version 2 capture");
		}

		int pairs = BitConverter.ToInt32(d, 12);
		byte shortDelay = d[23], longDelay = d[24], mapLength = d[25];
		byte[] map = d.AsSpan(26, mapLength).ToArray();
		int pos = 26 + mapLength;
		long ms = 0;
		var list = new List<Write>();
		for (int p = 0; p < pairs; p++, pos += 2) {
			byte index = d[pos], value = d[pos + 1];
			if (index == shortDelay) {
				ms += value + 1;
			} else if (index == longDelay) {
				ms += (value + 1) * 256;
			} else {
				list.Add(new Write(map[index & 0x7f] | ((index & 0x80) != 0 ? 0x100 : 0), value, ms, -1));
			}
		}

		return list;
	}
}

/// <summary>A chip that records every write with the tick it came in, −1 before the first.</summary>
sealed class RecordingChip : IOplChip {
	public readonly List<Write> Writes = new();
	public int Tick = -1;
	public int SampleRate => 49716;
	public void WriteRegister(int register, byte value) => Writes.Add(new Write(register, value, 0, Tick));
	public void Generate(Span<short> frames) => frames.Clear();
}

sealed class SilentBackend : IAudioBackend {
	public bool IsAvailable => false;
	public int CreateSample(WaveSample sample) => -1;
	public int Start(int sample, float gain, float pan, float pitch, bool looping) => -1;
	public void Stop(int play) { }
	public bool IsPlaying(int play) => false;
	public void SetGain(int play, float gain) { }
	public void SetPan(int play, float pan) { }
	public void SetPitch(int play, float ratio) { }
	public void SetMasterGain(float gain) { }
	public void StopAll() { }
	public IAudioStream? OpenStream(int sampleRate, int channels) => null;
	public void Update() { }
	public void Dispose() { }
}
