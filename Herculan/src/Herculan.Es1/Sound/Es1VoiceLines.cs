using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Sound;

/// <summary>
/// The simulator's voice lines and their lip-sync player (docs/es1/formats/snc-lip-sync.md#the-simulators-scripts): the
/// speech slots <c>Sound_Init</c> makes, <c>SpeechLine_Load</c> finding a line's slot and loading its script, and the
/// player that steps a face through its script on a clock of its own, which the PC timer advances on every second
/// interrupt. The globals are retail's: the slots <c>000bd734</c>, their count <c>000bd73c</c> and buffers
/// <c>000bd738</c>, <c>Snc_Clock</c> (<c>000959dc</c>), and the queue <c>SncQueue_Entries</c> (<c>000b1e54</c>),
/// <c>SncQueue_Count</c> (<c>000959e2</c>) and <c>SncQueue_Order</c> (<c>000b1ea4</c>). The simulation owns one
/// (<see cref="Es1SimGlobals.VoiceLines"/>).
///
/// <para>The timer's callback runs inside <see cref="Es1SimClock.Hold"/> on the frame's thread, as the other periodic
/// slots do, so nothing here is locked.</para>
/// </summary>
public sealed class Es1VoiceLines {
	/// <summary>The speech slots <c>Sound_Init</c> makes, and in the low-memory mode.</summary>
	public const int SlotCount = 10, LowMemorySlotCount = 1;

	/// <summary>The bytes of each slot's script buffer.</summary>
	public const int ScriptBytes = 100;

	/// <summary>The entries of the lip-sync queue.</summary>
	public const int QueueCapacity = 10;

	/// <summary>The PC timer's count <c>Snc_InstallTimer</c> registers <see cref="TimerTick"/> at: every second interrupt.</summary>
	public const int TimerCount = 2;

	/// <summary>The byte that ends a script, which <c>Snc_Load</c> appends; read as a frame or a delta it gives −1.</summary>
	public const byte Terminator = 0xff;

	/// <summary>
	/// <c>Snc_SkipTimerInstall</c> (<c>000959e0</c>), 0 in the image, for which <c>es2_xref</c> finds no writer
	/// (docs/es1/formats/snc-lip-sync.md#open).
	/// </summary>
	private const bool SkipTimerInstall = false;

	private Es1SpeechSlot[] _slots = [];
	private byte[][] _buffers = [];
	private Es1Sound? _sound;
	private readonly (uint Due, Es1SpeechSlot? Slot)[] _entries = new (uint, Es1SpeechSlot?)[QueueCapacity];

	/// <summary><c>SncQueue_Order</c>: indices into the entries, −1 for a pointer never written, which is 0 in the image.</summary>
	private readonly int[] _order = Enumerable.Repeat(-1, QueueCapacity).ToArray();

	/// <summary>The speech slots, <c>000bd734</c>, <c>000bd73c</c> of them; none before <see cref="InitSlots"/>.</summary>
	public IReadOnlyList<Es1SpeechSlot> Slots => _slots;

	/// <summary><c>Snc_Clock</c> (<c>000959dc</c>), 0 in the image: the lip-sync player's clock, which <see cref="TimerTick"/> advances.</summary>
	public uint Clock { get; private set; }

	/// <summary><c>SncQueue_Count</c> (<c>000959e2</c>), <c>int16</c>: the entries queued.</summary>
	public short QueueCount { get; private set; }

	/// <summary>
	/// <c>Sound_Init</c> (<c>0004db14</c>)'s voice lines (docs/es1/formats/sound-samples.md#the-driver), from the mission
	/// start's step 3: <see cref="SlotCount"/> slots, <see cref="LowMemorySlotCount"/> with <paramref name="lowMemory"/>
	/// set, each built by <c>VoiceSlot_ElementCtor</c>, and <see cref="ScriptBytes"/> bytes of script for each. Retail
	/// makes them whether or not it makes the sound manager.
	/// </summary>
	/// <param name="sound">The sound file whose manager and speech flag the lines read.</param>
	public void InitSlots(bool lowMemory, Es1Sound sound) {
		_sound = sound;
		int count = lowMemory ? LowMemorySlotCount : SlotCount;
		_slots = Enumerable.Range(0, count).Select(i => new Es1SpeechSlot(i)).ToArray();
		_buffers = Enumerable.Range(0, count).Select(_ => new byte[ScriptBytes]).ToArray();
	}

	/// <summary>
	/// <c>Snc_MissionInit</c> (<c>0004e498</c>), from the mission start's step 3 after <c>Sound_Init</c>
	/// (docs/es1/formats/snc-lip-sync.md#playing-a-line): unless <see cref="SkipTimerInstall"/>, <c>Snc_InstallTimer</c>
	/// (<c>0004e434</c>), which registers <see cref="TimerTick"/> on <paramref name="timer"/> at <see cref="TimerCount"/>
	/// and keeps no slot.
	/// </summary>
	public void MissionInit(Es1SimClock timer) {
		if (!SkipTimerInstall) {
			timer.AddCallback(TimerCount, TimerTick);
		}
	}

	/// <summary>
	/// <c>SpeechLine_Load</c> (<c>0004e0f0</c>)(<paramref name="scriptName"/>, <paramref name="voiceName"/>)
	/// (docs/es1/formats/sound-samples.md#voice-lines): the slot whose key is <see cref="VoiceKey"/> of the voice's name;
	/// with none, a slot from <see cref="EvictLru"/>, none when it gives none, which takes the key and is marked taken.
	/// On that slot, with a sound manager and <see cref="Es1Sound.SpeechEnabled"/> set, the voice's path
	/// <c>simvoice\&lt;voice name&gt;.sfx</c> is named as the slot's index (<see cref="Es1SoundManager.SetSampleName"/>,
	/// its answer not read) and the slot's sound loaded (<see cref="Es1SoundObject.LoadSample"/>), answering none when
	/// that fails, with the key and the taken mark kept and no script loaded; then the script is loaded with the slot's
	/// sound (<see cref="Load"/>). Either way the slot's last use becomes <paramref name="now"/>, the clock
	/// <c>000981c4</c>; answers the slot. A slot found by its key keeps the sample and script it has, so a second
	/// squadmate of the same voice bank saying the same line moves to the first one's script.
	/// </summary>
	/// <param name="read">The volumes' reader, (folder, file name) to a file's bytes; none reads no script.</param>
	public Es1SpeechSlot? LoadLine(string scriptName, string voiceName, uint now, Func<string, string, byte[]?>? read) {
		int key = VoiceKey(voiceName);
		var slot = Array.Find(_slots, s => s.Key == key);
		if (slot == null) {
			slot = EvictLru();
			if (slot == null) {
				return null;
			}

			slot.Key = key;
			slot.Taken = true;
			if (_sound is { Manager: { } manager, SpeechEnabled: true }) {
				// SoundManager_SetSampleName lower-cases the slot's own string in place.
				slot.VoicePath = ("simvoice\\" + voiceName + ".sfx").ToLowerInvariant();
				manager.SetSampleName(slot.SoundIndex, slot.VoicePath);
				// A slot with no sound, which retail loads through as a null object, fails here, an engine guard.
				if (slot.Sound?.LoadSample() != true) {
					return null;
				}
			}

			Load(slot, slot.Sound, scriptName, read);
		}

		slot.LastUse = now;
		return slot;
	}

	/// <summary>
	/// <c>Voice_KeyOfName</c> (<c>0004e208</c>)(<paramref name="name"/>), the key <see cref="LoadLine"/> finds a voice by: on an upper-cased copy,
	/// (character 1 − <c>'0'</c>) × <c>0x1000000</c> plus characters 3 to 7 read as five decimal digits, each
	/// character's value less <c>'0'</c>, so <c>P1_01000</c> keys to <c>0x1000000</c> + 1000. A character past the
	/// name's end reads 0 here, an engine guard; the names <see cref="Hdd.Es1HddCommBox.BeginMessage"/> builds are
	/// always long enough.
	/// </summary>
	public static int VoiceKey(string name) {
		string upper = name.ToUpperInvariant();
		int number = 0;
		short weight = 10000;
		for (int i = 0; i < 5; i++) {
			number += (Char(3 + i) - '0') * weight;
			weight /= 10;
		}

		return unchecked((Char(1) - '0') * 0x1000000 + number);

		int Char(int index) => index < upper.Length ? (sbyte)upper[index] : 0;
	}

	/// <summary>
	/// <c>Voice_EvictLru</c> (<c>0004e08c</c>): the first slot whose last use is the lowest, compared unsigned and below
	/// <c>0xffffffff</c>, its script pointed at its own <see cref="ScriptBytes"/> bytes. With none, which retail writes
	/// through a null slot for, none, an engine guard.
	/// </summary>
	private Es1SpeechSlot? EvictLru() {
		Es1SpeechSlot? found = null;
		uint lowest = uint.MaxValue;
		foreach (var slot in _slots) {
			if (slot.LastUse < lowest) {
				lowest = slot.LastUse;
				found = slot;
			}
		}

		if (found != null) {
			found.Script = _buffers[found.Index];
		}

		return found;
	}

	/// <summary>
	/// <c>Snc_Load</c> (<c>0004e4a8</c>)(<paramref name="slot"/>, <paramref name="sound"/>, <paramref name="name"/>)
	/// (docs/es1/formats/snc-lip-sync.md#the-simulators-scripts): <c>snc\&lt;name&gt;</c>'s <c>int32</c> length of bytes,
	/// read through <see cref="LipSyncScriptTransformer"/>, into the slot's script, then <see cref="Terminator"/>;
	/// <paramref name="sound"/> stored at <c>+0</c> (<see cref="Es1SpeechSlot.LineSound"/>), its byte <c>+0x16</c>
	/// cleared when there is one. A missing or unreadable file, which retail reads from unopened, leaves the terminator
	/// alone, and a script longer than the buffer is cut to leave room for it, engine guards; ES2's 556 scripts hold at
	/// most 56 bytes (docs/retail/formats/snc-lip-sync.md#layout).
	/// </summary>
	private static void Load(Es1SpeechSlot slot, Es1SoundObject? sound, string name, Func<string, string, byte[]?>? read) {
		slot.LineSound = sound;
		if (sound != null) {
			sound.Byte16 = 0;
		}

		if (slot.Script is not { } buffer) {
			return;
		}

		var bytes = new List<byte>();
		try {
			if (read?.Invoke("snc", name + ".SNC") is { } file && new LipSyncScriptTransformer().Parse(file) is { } script) {
				foreach (var e in script.Events) {
					bytes.Add(e.Frame);
					bytes.Add(e.Delta);
				}

				if (script.Length % 2 != 0 && script.Unread.Length > 0) {
					bytes.Add(script.Unread[0]);
				}
			}
		} catch (InvalidDataException) {
			bytes.Clear();
		}

		int length = Math.Min(bytes.Count, buffer.Length - 1);
		bytes.CopyTo(0, buffer, 0, length);
		buffer[length] = Terminator;
	}

	/// <summary>
	/// <c>Snc_Start</c> (<c>0004e518</c>)(<paramref name="slot"/>), as the face begins to talk
	/// (docs/es1/formats/snc-lip-sync.md#playing-a-line): the cursor at the script's start, the frame from
	/// <see cref="ReadFrame"/> and the due count from <see cref="ReadDueTime"/>, and the slot queued at that count
	/// (<see cref="Enqueue"/>). Then, with a sound object (<see cref="Es1SpeechSlot.LineSound"/>),
	/// <see cref="Es1Sound.SpeechEnabled"/> set and a frame other than 0, answers the object's play
	/// (<see cref="Es1SoundObject.Play"/>), which the effects' switch does not gate; otherwise true.
	/// </summary>
	public bool Start(Es1SpeechSlot slot) {
		slot.Cursor = 0;
		slot.Frame = ReadFrame(slot);
		slot.DueCount = ReadDueTime(slot);
		Enqueue(slot, slot.DueCount);
		return slot.LineSound is not { } sound || _sound?.SpeechEnabled != true || slot.Frame == 0 || sound.Play();
	}

	/// <summary>
	/// <c>Snc_TimerTick</c> (<c>0004e39c</c>), the PC timer's callback: <see cref="Clock"/> one on, then the queue's
	/// pointers from the first. At an entry due at the clock's count, 1 off the count, the entry's slot stepped
	/// (<see cref="Step"/>) and the tick over; at an entry due earlier, nothing more when it is the last counted, and
	/// otherwise each pointer after it moved down one place, 1 off the count for each, while the index stays below the
	/// count less 1, and the tick over; an entry due later is passed over. A pointer never written, which retail reads
	/// through, is passed over, an engine guard.
	/// </summary>
	internal void TimerTick() {
		Clock++;
		for (int i = 0; i < QueueCount; i++) {
			if (_order[i] < 0) {
				continue;
			}

			var (due, slot) = _entries[_order[i]];
			if (due == Clock) {
				QueueCount--;
				if (slot != null) {
					Step(slot);
				}

				return;
			}

			if (due < Clock) {
				if (QueueCount - 1 <= i) {
					return;
				}

				do {
					_order[i] = _order[i + 1];
					QueueCount--;
					i++;
				} while (i < QueueCount - 1);

				return;
			}
		}
	}

	/// <summary>
	/// <c>Snc_Step</c> (<c>0004e5f8</c>)(<paramref name="slot"/>): the frame from <see cref="ReadFrame"/>; unless that is
	/// −1, the due count from <see cref="ReadDueTime"/> and the slot queued at it. The terminator read as a frame ends
	/// the line with nothing queued.
	/// </summary>
	private void Step(Es1SpeechSlot slot) {
		slot.Frame = ReadFrame(slot);
		if (slot.Frame != -1) {
			slot.DueCount = ReadDueTime(slot);
			Enqueue(slot, slot.DueCount);
		}
	}

	/// <summary>
	/// <c>Snc_ReadFrame</c> (<c>0004e59c</c>)(<paramref name="slot"/>): the byte at the cursor, unsigned, the cursor one
	/// on; −1 for <see cref="Terminator"/>.
	/// </summary>
	private static short ReadFrame(Es1SpeechSlot slot) {
		byte value = Read(slot);
		return value == Terminator ? (short)-1 : value;
	}

	/// <summary>
	/// <c>Snc_ReadDueTime</c> (<c>0004e5c4</c>)(<paramref name="slot"/>): the byte at the cursor, unsigned, plus
	/// <see cref="Clock"/>, the cursor one on; −1 for <see cref="Terminator"/>.
	/// </summary>
	private uint ReadDueTime(Es1SpeechSlot slot) {
		byte value = Read(slot);
		return value == Terminator ? uint.MaxValue : value + Clock;
	}

	/// <summary>
	/// The byte at the slot's cursor, the cursor one on. Past the buffer's end, where retail reads on into the next
	/// slot's bytes, or with no buffer, it reads <see cref="Terminator"/>, an engine guard.
	/// </summary>
	private static byte Read(Es1SpeechSlot slot) {
		int at = slot.Cursor++;
		return slot.Script is { } script && at >= 0 && at < script.Length ? script[at] : Terminator;
	}

	/// <summary>
	/// <c>Snc_Enqueue</c> (<c>0004e624</c>)(<paramref name="slot"/>, <paramref name="due"/>): when the count is above 0
	/// and the first pointer's entry is due after <paramref name="due"/>, compared unsigned, 1 off the count and the
	/// pointers after the first moved down one place, as many as the new count; then {due, slot} stored at the entry the
	/// count indexes, the first pointer pointed at it and 1 on the count. With the count at <see cref="QueueCapacity"/>,
	/// where retail writes past the entries, nothing is queued, an engine guard.
	/// </summary>
	private void Enqueue(Es1SpeechSlot slot, uint due) {
		if (QueueCount > 0 && _order[0] >= 0 && _entries[_order[0]].Due > due) {
			QueueCount--;
			for (int i = 0; i < QueueCount; i++) {
				_order[i] = _order[i + 1];
			}
		}

		if (QueueCount >= QueueCapacity) {
			return;
		}

		_entries[QueueCount] = (due, slot);
		_order[0] = QueueCount;
		QueueCount++;
	}
}
