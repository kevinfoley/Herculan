using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Read;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Audio;
using Herculan.Engine.Numerics;
using Herculan.Es1.View;

namespace Herculan.Es1.Sound;

/// <summary>
/// The simulator's sound file, <c>0004db14</c>–<c>0004e70f</c> (docs/es1/formats/sound-samples.md): the sound manager
/// <c>Sound_Init</c> makes, the catalog of <c>str\SOUNDS.STR</c>'s rows <c>SoundCatalog_Load</c> builds, the switches
/// preferences' MUSIC and SOUNDS set, and the calls the simulator plays, stops and tests a sound through by its row.
/// Retail's globals: the manager <c>000959ac</c>, the catalog's objects <c>000bd63c</c> and row count <c>000bd72c</c>,
/// <c>Sound_MusicEnabled</c> (<c>000959b8</c>), <c>Sound_EffectsEnabled</c> (<c>000959bc</c>), the listener
/// <c>Sound_Listener</c> (<c>000bd730</c>) with its countdown <c>000959b4</c>, and the points offered for the rows
/// (<c>000b1a84</c>, <c>SoundCatalog_Positions</c> <c>000b1b74</c>). The simulation owns one
/// (<see cref="Sim.Es1SimGlobals.Sound"/>), which the preferences'
/// handlers of options 0 and 1 write from their load on.
/// </summary>
public sealed class Es1Sound {
	/// <summary>The catalog's object array, <c>000bd63c</c>: as many as the manager's slots.</summary>
	public const int CatalogCapacity = Es1SoundManager.ObjectSlots;

	/// <summary>The row of the click, <c>gm_69</c>, which <c>Widget_ClickSound</c> (<c>000314a0</c>) plays.</summary>
	public const int ClickRow = 0x11;

	/// <summary>The row <c>Sim_StartMission</c>'s step 15 plays last, <c>start3</c>.</summary>
	public const int StartRow = 0x13;

	/// <summary>The frames between the listener's updates, <c>000959b4</c>'s reset.</summary>
	public const int ListenerFrames = 10;

	/// <summary>The memory limit <c>Sound_Init</c> sets in the low-memory mode, and the one it sets otherwise.</summary>
	public const uint LowMemoryLimit = 200_000, MemoryLimit = 0xffffffff;

	/// <summary>The hearing distance <c>Sound_Init</c> sets.</summary>
	public const uint HearingRange = 100_000;

	private readonly Es1SoundObject?[] _catalog = new Es1SoundObject?[CatalogCapacity];

	// 000b1a84: per row, the distance of the point offered this frame, 0 for none.
	private readonly int[] _offered = new int[CatalogCapacity];

	// SoundCatalog_Positions (000b1b74): per row, the point offered this frame.
	private readonly Vec3i[] _offeredPoints = new Vec3i[CatalogCapacity];

	/// <summary><c>000959ac</c>: the sound manager, none until <see cref="Init"/> makes one, and with a digital device 0.</summary>
	public Es1SoundManager? Manager { get; private set; }

	/// <summary><c>000bd72c</c>: the catalog's rows, those up to <c>SOUNDS.STR</c>'s first empty name.</summary>
	public int CatalogCount { get; private set; }

	/// <summary><c>Sound_MusicEnabled</c> (<c>000959b8</c>), 0 in the image: the songs play while it is not 0.</summary>
	public byte MusicEnabled { get; private set; }

	/// <summary><c>Sound_EffectsEnabled</c> (<c>000959bc</c>), 0 in the image: the digital sounds play while it is not 0.</summary>
	public byte EffectsEnabled { get; private set; }

	/// <summary><c>000959b4</c>, 0 in the image: the frames until <c>Sound_UpdateListener</c> next places the sounds.</summary>
	public int ListenerCountdown { get; private set; }

	/// <summary>
	/// Whether <see cref="OfferPosition"/> keeps the nearest point offered in a frame, comparing the new distance where
	/// retail compares the kept one with itself, so that the flyers' shared sound plays from the nearest flyer; off, the
	/// first point offered is kept, as retail's is.
	/// The code path of a tweak that is not built (docs/herculan/plan-es1-first-mission.md#open).
	/// </summary>
	public static bool NearestOfferWins { get; set; }

	/// <summary><c>Sound_Listener</c> (<c>000bd730</c>): the camera <see cref="UpdateListener"/> last kept, none until then.</summary>
	public Es1Camera? Listener { get; private set; }

	/// <summary><c>000bd63c</c>[<paramref name="row"/>]: the row's sound object, none past the catalog or with no manager.</summary>
	public Es1SoundObject? Catalog(int row) => (uint)row < CatalogCapacity ? _catalog[row] : null;

	/// <summary>
	/// <c>Sim_StartMission</c>'s step 3 (docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc):
	/// with <paramref name="soundOn"/> (<c>000b5b68</c>, which <c>-s</c> toggles from 1) the six values from the install's
	/// <c>resource.cfg</c>, read as <c>Profile_GetString</c> and <c>Profile_GetInt</c> read it (<see cref="Es1Profile"/>):
	/// <c>[SoundFX]</c>'s <c>Driver#</c>, an empty one 0 and any other through <c>sscanf</c>'s <c>%X</c>, <c>IRQ</c> and
	/// <c>DMA</c> through <c>atol</c> with a default of 0, and its <c>PortAddress</c> and <c>[Music]</c>'s <c>Driver#</c> and
	/// <c>PortAddress</c> through <c>%X</c>; without it, digital device 0.
	///
	/// <para>PLACEHOLDER: a key the file lacks, where <c>Profile_GetString</c> copies its default argument 0 as a string,
	/// reads as empty, and a value <c>%X</c> assigns nothing from, where retail passes on what the stack held, reads 0
	/// (main-loop.md's Open). The setup writes every key.</para>
	/// </summary>
	public static Es1SoundDevices MissionStartDevices(string installRoot, bool soundOn) {
		if (!soundOn) {
			return default;
		}

		string path = CaseInsensitivePath.Combine(installRoot, Es1ResourceCfg.FileName);
		byte[]? bytes = null;
		try {
			bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			// The file does not open, as Profile_Load allows.
		}

		var profile = Es1Profile.Parse(bytes);
		string digital = profile.GetString(Es1ResourceCfg.SoundFx, Es1ResourceCfg.Driver) ?? "";
		return new Es1SoundDevices(
			MidiPort: Hex(Es1ResourceCfg.Music, Es1ResourceCfg.Port),
			Port: Hex(Es1ResourceCfg.SoundFx, Es1ResourceCfg.Port),
			Dma: (byte)profile.GetInt(Es1ResourceCfg.SoundFx, Es1ResourceCfg.Dma, 0),
			Irq: (byte)profile.GetInt(Es1ResourceCfg.SoundFx, Es1ResourceCfg.Irq, 0),
			MidiDevice: Hex(Es1ResourceCfg.Music, Es1ResourceCfg.Driver),
			DigitalDevice: digital.Length == 0 ? (ushort)0 : Hex(Es1ResourceCfg.SoundFx, Es1ResourceCfg.Driver));

		ushort Hex(string section, string key) =>
			Es1Profile.TryScanHex(profile.GetString(section, key) ?? "", out uint value) ? (ushort)value : (ushort)0;
	}

	/// <summary>
	/// <c>Sound_Init</c> (<c>0004db14</c>)(<paramref name="devices"/>) (docs/es1/formats/sound-samples.md#the-driver):
	/// the voice lines' slots (<see cref="Es1VoiceLines.InitSlots"/>); then, with a digital device not 0, the manager over
	/// <paramref name="backend"/>, the six values stored, <see cref="Es1SoundManager.LoadDrivers"/>, whose answer is not
	/// read, the memory limit with loading kept from unloading other sounds (<see cref="LowMemoryLimit"/> with it allowed
	/// in the low-memory mode), the hearing distance, and <see cref="LoadCatalog"/>; and either way the listener's
	/// countdown <see cref="ListenerFrames"/>.
	/// </summary>
	/// <param name="read">The volumes' reader, (folder, file name) to a file's bytes, which the manager and the catalog read through.</param>
	public void Init(Es1SoundDevices devices, bool lowMemory, Es1VoiceLines voiceLines, Func<string, string, byte[]?> read,
			IAudioBackend backend) {
		voiceLines.InitSlots(lowMemory);
		if (devices.DigitalDevice != 0) {
			var manager = new Es1SoundManager(read, new Es1DigitalDriver(backend)) { Devices = devices };
			Manager = manager;
			manager.LoadDrivers();
			manager.SetMemoryLimit(lowMemory ? LowMemoryLimit : MemoryLimit);
			manager.MayUnloadToFit = lowMemory;
			manager.SetHearingRange(HearingRange);
			CatalogCount = 0;
			LoadCatalog(read, voiceLines);
		}

		ListenerCountdown = ListenerFrames;
	}

	/// <summary>
	/// <c>SoundCatalog_Load</c> (<c>0004dc70</c>) (docs/es1/formats/sound-samples.md#the-simulators-catalog):
	/// <c>str\SOUNDS.STR</c>'s rows, every name to <see cref="Es1SoundManager.SetSampleNames"/>, then for each row up to the
	/// first empty name the object <see cref="Es1SoundManager.CreateObject"/>(2, attribute byte 3, row), its repeats
	/// (−1, −1, attribute byte 0), its volume attribute byte 1, and its sample loaded with attribute byte 2 set; then for
	/// each voice-line slot the next index past the rows and a digital sound on it (<see cref="Es1SpeechSlot.SoundIndex"/>,
	/// <see cref="Es1SpeechSlot.Sound"/>). Each row's offered distance (<see cref="OfferPosition"/>) is cleared with it.
	///
	/// <para>Engine guards: a table that does not read gives no rows, where retail reads a name through a null pointer; the
	/// rows stop at the table's end or at <see cref="CatalogCapacity"/>, where retail reads on past the names it stored; and
	/// a row no object is made for is passed over, where retail calls through a null object.</para>
	/// </summary>
	private void LoadCatalog(Func<string, string, byte[]?> read, Es1VoiceLines voiceLines) {
		var manager = Manager!;
		var rows = read("str", "SOUNDS.STR") is { } bytes ? new StringFileTransformer().Parse(bytes)?.Group(0) : null;
		rows ??= [];
		manager.SetSampleNames(rows.Select(row => row.Text).ToArray());
		CatalogCount = 0;
		while (CatalogCount < rows.Count && CatalogCount < CatalogCapacity && rows[CatalogCount].Text.Length != 0) {
			byte[] attributes = rows[CatalogCount].Attributes;
			byte Attribute(int i) => i < attributes.Length ? attributes[i] : (byte)0;
			var sound = manager.CreateObject(2, Attribute(3), (short)CatalogCount);
			_catalog[CatalogCount] = sound;
			if (sound != null) {
				sound.SetRepeats(-1, -1, Attribute(0));
				sound.SetVolume((sbyte)Attribute(1));
				if (Attribute(2) != 0) {
					sound.LoadSample();
				}
			}

			_offered[CatalogCount] = 0;
			CatalogCount++;
		}

		short index = (short)CatalogCount;
		foreach (var slot in voiceLines.Slots) {
			slot.SoundIndex = index;
			slot.Sound = manager.CreateObject(0, 0, index);
			index++;
		}
	}

	/// <summary>
	/// <c>Sound_Play</c> (<c>0004de84</c>)(<paramref name="row"/>): with a manager, the row's object played through its
	/// slot <c>+0x10</c> while its kind's switch is on (<see cref="Es1SoundObject.IsCategoryEnabled"/>). A row with no
	/// object does nothing, an engine guard.
	/// </summary>
	public void Play(int row) {
		if (Manager != null && Catalog(row) is { } sound && sound.IsCategoryEnabled(MusicEnabled != 0, EffectsEnabled != 0)) {
			sound.Play();
		}
	}

	/// <summary>
	/// <c>Sound_PlayAt</c> (<c>0004deb8</c>)(<paramref name="point"/>, <paramref name="row"/>)
	/// (docs/es1/formats/sound-samples.md#placed-sounds): with a manager, the row's object placed at the point
	/// (<see cref="Es1SoundObject.SetPosition"/>), then <see cref="Play"/>. A row with no object does nothing, an engine
	/// guard.
	/// </summary>
	public void PlayAt(Vec3i point, int row) {
		if (Manager != null && Catalog(row) is { } sound) {
			sound.SetPosition(point);
			Play(row);
		}
	}

	/// <summary>
	/// <c>Sound_OfferPosition</c> (<c>0004dee4</c>)(<paramref name="point"/>, <paramref name="row"/>)
	/// (docs/es1/simulation/main-loop.md#the-frames-sounds--sim_framesoundtick-00058574): with a manager, the point's
	/// distance from <see cref="Listener"/> (<c>Math_FastMagnitude3D</c>) and the point kept for the row while its kept
	/// distance is 0, which <see cref="UpdateListener"/> places the row's object at. Retail's test of a kept distance
	/// compares the dword with itself (<c>0004df4d</c>), where the new distance belongs, so the first point offered in a
	/// frame at a distance other than 0 is the one used; with <see cref="NearestOfferWins"/> a nearer point replaces it.
	///
	/// <para>Engine guards: a row past the catalog's object array is not kept, and with no listener yet, where retail
	/// reads the position through a null pointer, the distance is taken from (0, 0, 0).</para>
	/// </summary>
	public void OfferPosition(Vec3i point, int row) {
		if (Manager == null || (uint)row >= CatalogCapacity) {
			return;
		}

		var listener = Listener?.Position ?? default;
		int distance = SimMath.FastMagnitude3D(unchecked(listener.X - point.X), unchecked(listener.Y - point.Y),
			unchecked(listener.Z - point.Z));
		if (_offered[row] == 0 || (NearestOfferWins && distance < _offered[row])) {
			_offered[row] = distance;
			_offeredPoints[row] = point;
		}
	}

	/// <summary>
	/// <c>Sound_UpdateListener</c> (<c>0004df98</c>)(<paramref name="camera"/>)
	/// (docs/es1/simulation/main-loop.md#the-frames-sounds--sim_framesoundtick-00058574): the camera kept as
	/// <see cref="Listener"/>; then, with a manager, each catalog row with a point offered placed there and its distance
	/// cleared, and on every <see cref="ListenerFrames"/>th call the manager's listener given the rotation of (0, 0, 0)
	/// (<c>BuildEulerRotationMatrixQ14</c>) at the camera's position (<see cref="Es1SoundManager.UpdateListener"/>). A row
	/// with no object is passed over with its distance cleared, an engine guard.
	/// </summary>
	public void UpdateListener(Es1Camera camera) {
		Listener = camera;
		if (Manager == null) {
			return;
		}

		for (int row = 0; row < CatalogCount; row++) {
			if (_offered[row] != 0) {
				_catalog[row]?.SetPosition(_offeredPoints[row]);
				_offered[row] = 0;
			}
		}

		if (--ListenerCountdown == 0) {
			ListenerCountdown = ListenerFrames;
			var frame = Transform3.FromEuler(0, 0, 0);
			frame.X = camera.Position.X;
			frame.Y = camera.Position.Y;
			frame.Z = camera.Position.Z;
			Manager.UpdateListener(frame);
		}
	}

	/// <summary>
	/// <c>Sound_Stop</c> (<c>0004df70</c>)(<paramref name="row"/>): with a manager, the row's object stopped through its
	/// slot <c>+0x14</c>. A row with no object does nothing, an engine guard.
	/// </summary>
	public void Stop(int row) {
		if (Manager != null) {
			Catalog(row)?.Stop();
		}
	}

	/// <summary>
	/// <c>Sound_IsPlaying</c> (<c>0004e05c</c>)(<paramref name="row"/>): false with no manager, else the row's object's
	/// slot <c>+0x30</c>. A row with no object answers false, an engine guard.
	/// </summary>
	public bool IsPlaying(int row) => Manager != null && Catalog(row)?.IsPlaying() == true;

	/// <summary>
	/// <c>Sound_Preload</c> (<c>0004dde0</c>)(<paramref name="row"/>): false with no manager, else
	/// <see cref="Es1SoundObject.LoadSample"/> on the row's object. A row with no object answers false, an engine guard.
	/// </summary>
	public bool Preload(int row) => Manager != null && Catalog(row)?.LoadSample() == true;

	/// <summary>
	/// <c>Sound_Shutdown</c> (<c>0004de08</c>): with a manager, each of the catalog's objects destroyed through its slot
	/// <c>+8</c> with 3, then <see cref="Es1SoundManager.UnloadDrivers"/>. Retail keeps the freed objects' pointers and the
	/// manager; here the catalog lets go of them, an engine guard, so a play after it does nothing.
	/// </summary>
	public void Shutdown() {
		if (Manager == null) {
			return;
		}

		for (int row = 0; row < CatalogCount; row++) {
			_catalog[row]?.Destroy();
			_catalog[row] = null;
		}

		Manager.UnloadDrivers();
	}

	/// <summary>
	/// <c>Sound_SetMusicEnabled</c> (<c>0004e294</c>)(<paramref name="on"/>)
	/// (docs/es1/formats/sound-samples.md#turning-songs-and-sounds-off): when <paramref name="on"/> is 0 and differs from
	/// <see cref="MusicEnabled"/>, with a manager, <see cref="StopSongs"/>; then stored.
	/// </summary>
	public void SetMusicEnabled(byte on) {
		if (on != MusicEnabled && on == 0 && Manager != null) {
			StopSongs();
		}

		MusicEnabled = on;
	}

	/// <summary>
	/// <c>Sound_SetEffectsEnabled</c> (<c>0004e2c8</c>)(<paramref name="on"/>): <see cref="SetMusicEnabled"/> over
	/// <see cref="EffectsEnabled"/> with <see cref="StopDigitalSounds"/>.
	/// </summary>
	public void SetEffectsEnabled(byte on) {
		if (on != EffectsEnabled && on == 0 && Manager != null) {
			StopDigitalSounds();
		}

		EffectsEnabled = on;
	}

	/// <summary>
	/// <c>Sound_StopSongs</c> (<c>0004e2f9</c>): with a manager, each song among the manager's object slots below
	/// <see cref="CatalogCount"/> that plays stopped through its slot <c>+0x14</c>.
	/// </summary>
	public void StopSongs() => StopKind(1);

	/// <summary>
	/// <c>Sound_StopDigitalSounds</c> (<c>0004e34c</c>): <see cref="StopSongs"/> for the digital sounds; a song plays on.
	/// </summary>
	public void StopDigitalSounds() => StopKind(0);

	/// <summary>
	/// The digital driver's service of its voices (<see cref="Es1DigitalDriver.Update"/>), which retail's runs from the
	/// timer: called once a host frame, an engine choice. Nothing with no manager.
	/// </summary>
	public void UpdateDriver() => Manager?.Driver.Update();

	private void StopKind(byte kind) {
		if (Manager == null) {
			return;
		}

		for (int slot = 0; slot < CatalogCount; slot++) {
			if (Manager.GetObject(slot) is { } sound && sound.Kind == kind && sound.IsPlaying()) {
				sound.Stop();
			}
		}
	}
}
