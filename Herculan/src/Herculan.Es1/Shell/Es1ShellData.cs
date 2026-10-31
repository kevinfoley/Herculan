using System.Text;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.Shell.Hangar;

namespace Herculan.Es1.Shell;

/// <summary>
/// What the shell loads once at its start and keeps for every screen (docs/es1/shell/startup.md#the-shared-data): the
/// file-name table, the <c>prefs.cfg</c> options' bytes and words, the 13 fonts, the generator, the shell's text, the
/// careers log and its functions, the HERC diagrams' tables and art, and the battle set with the single missions' lists
/// and the pilot names.
/// </summary>
public sealed class Es1ShellData {
	/// <summary>The file-name table's entry of the careers log, <c>car\names.log</c> (docs/es1/formats/career-log-and-player-mec.md#the-careers-log).</summary>
	public const int CareersLogEntry = 13;

	/// <summary>
	/// The 14 font slots <c>000aacc6</c>–<c>000aacfa</c>: each <c>dfn\</c> font in <c>Shell_LoadData</c>'s order, the
	/// <c>nm_green</c> handle stored twice, in the slots 5 and 7.
	/// </summary>
	private static readonly string[] FontNames = [
		"TITLE", "GRAY", "ACTIVE", "PUSHED", "INACTIVE", "NM_GREEN", "NM_RED1", "NM_GREEN", "CSTATS_1", "CSTATS_2",
		"NM_WHITE", "NM_BLUE", "NM_YLW", "BIG_RED",
	];

	/// <summary>The words <c>0007fd10</c> and <c>0007fd18</c>, the two bases of <see cref="DetailWord"/>.</summary>
	private const short DetailBase = 0x3f, DetailBaseOption3 = 0xbf;

	private Es1ShellData() {
	}

	/// <summary><c>cfg\filename.bin</c>, the table <c>FileNames_Load</c> (<c>0002094d</c>) loads into <c>0009afb8</c>; none when it does not read.</summary>
	public string[]? FileNames { get; private init; }

	/// <summary>
	/// The mission parameters' five <c>int16</c>, <c>MissionParams_Ammo</c> (<c>000aacbc</c>), <c>MissionParams_Damage</c>,
	/// <c>MissionParams_Difficulty</c>, <c>MissionParams_Visibility</c> and <c>MissionParams_TimeOfDay</c>
	/// (<c>000aacc4</c>): options 12 to 16, which MISSION PARAMS steps (docs/es1/shell/single-mission.md#the-mission-params-menu,
	/// docs/es1/formats/msn-mission-file.md#the-mode-and-the-mission-parameters).
	/// </summary>
	public short[] MissionParams { get; } = new short[5];

	/// <summary>The byte <c>0007fcb8</c>, option 0, which lets <c>ShellMusic_Play</c> start a track.</summary>
	public byte MusicByte { get; private set; }

	/// <summary>The byte <c>0007fcb9</c>, option 1, which lets a text button click.</summary>
	public byte ClickByte { get; private set; }

	/// <summary>The word <c>0007fcbc</c>, option 2.</summary>
	public short Option2Word { get; private set; }

	/// <summary>The word <c>0007fcba</c>: (<see cref="DetailBase"/>, or <see cref="DetailBaseOption3"/> with option 3 set, plus options 4, 5 and 6) / 256.</summary>
	public short DetailWord { get; private set; }

	/// <summary>The 14 font slots; a font whose file is missing is null.</summary>
	public HudFont?[] Fonts { get; } = new HudFont?[FontNames.Length];

	/// <summary><c>Shell_RandomState</c>, seeded.</summary>
	public Es1ShellRandom? Random { get; private set; }

	/// <summary>
	/// The HERC diagrams' tables and art (<see cref="Es1HercArt"/>) and the HERCs' diagrams built from them, none in the
	/// image until <see cref="Load"/>.
	/// </summary>
	public Es1HercDiagrams HercDiagrams { get; private set; } = new(Es1HercArt.Empty());

	/// <summary>The value tables <c>LoadDamageDat_Shell</c> expands from <c>cfg\damage.dat</c>, zero in the image until <see cref="Load"/>.</summary>
	public Es1RepairCosts RepairCosts { get; private set; } = new();

	/// <summary><c>eng\watext.bin</c>, the shell's text at <c>0009afa8</c>; none when it does not read.</summary>
	public string[]? Text { get; private set; }

	/// <summary>The careers log at <c>0009b20c</c>; none when its path is unknown or the file does not read.</summary>
	public Es1CareerLog? CareerLog { get; private set; }

	/// <summary>
	/// <c>Career_SelectedEntry</c> (<c>0007fcea</c>), the career menu's selected line, 0 in the image: the log's current
	/// entry at the load, 0 when that is negative; the career menu's handler stores its list's selection in it after every
	/// message, and <c>CareerLog_FindOrAdd</c> and DELETE's warning set it (docs/es1/shell/career.md#the-list).
	/// </summary>
	public short SelectedEntry { get; set; }

	/// <summary>
	/// The word <c>0007fcec</c>, 0 in the image, the battle set <c>Career_New</c> gives a new career; screen 1330's
	/// <c>BattleSetDialog_OnMessage</c> sets it (docs/es1/formats/bst-battle-sets.md#which-battle-set).
	/// </summary>
	public short NewCareerBattleSet { get; set; }

	/// <summary>The byte <c>+8</c> of the careers log <c>0009b20c</c>: an entry has been added since the log was loaded.</summary>
	private bool _careerAdded;

	/// <summary>The careers log's path in the install, <see cref="CareersLogEntry"/> through <c>Path_ApplyAltDrive</c>; none without the table.</summary>
	public string? CareersLogPath { get; private set; }

	/// <summary>
	/// <c>FileNames_Load</c> (<c>0002094d</c>), the start-up's step 7: <c>cfg\filename.bin</c> from <paramref name="content"/>.
	/// </summary>
	public static Es1ShellData LoadFileNames(GameContent content) {
		var names = content.Read("cfg", "FILENAME.BIN") is { } bytes ? new BinStringFileTransformer().Parse(bytes)?.Values : null;
		if (names == null) {
			Console.Error.WriteLine("cfg\\filename.bin is missing or unreadable: the careers log is not loaded.");
		}

		return new Es1ShellData { FileNames = names };
	}

	/// <summary>
	/// <c>Shell_LoadData</c> (<c>00020630</c>), the start-up's step 11, on <paramref name="preferences"/>, the options as the
	/// devices left them: options 12–16 into <see cref="MissionParams"/>, 0 and 1 into the two bytes, 2 into
	/// <see cref="Option2Word"/>, <see cref="DetailWord"/>; the 14 font slots; the generator seeded with <c>time()</c>
	/// % 128 draws; <c>eng\watext.bin</c>; the value tables (<see cref="Es1RepairCosts.Load"/>); the careers log, with <see cref="SelectedEntry"/> from it; and
	/// <c>BattleSet_Load</c> of the roster's battle set (<see cref="LoadBattleSet"/>), the roster's word <c>+5</c>, which is
	/// 0 in the image and set by nothing before this load. <c>00027c4c</c>, which makes the 41 weapon stocks, follows; they
	/// are the roster's here (<see cref="Es1CareerRoster.Stocks"/>).
	/// </summary>
	public void Load(GameContent content, Es1Preferences preferences, string installRoot, long secondsSince1970) {
		for (int i = 0; i < MissionParams.Length; i++) {
			MissionParams[i] = preferences[12 + i];
		}

		MusicByte = preferences[0];
		ClickByte = preferences[1];
		Option2Word = preferences[2];
		short detailBase = preferences[3] != 0 ? DetailBaseOption3 : DetailBase;
		DetailWord = (short)((short)(detailBase + preferences[4] + preferences[5] + preferences[6]) / 0x100);

		for (int i = 0; i < FontNames.Length; i++) {
			Fonts[i] = HudFont.Load(content, FontNames[i], "dfn");
		}

		Random = Es1ShellRandom.AtStartup(secondsSince1970);
		Text = content.Read("eng", "WATEXT.BIN") is { } text ? new BinStringFileTransformer().Parse(text)?.Values : null;

		// PLACEHOLDER: the rect 0009b000–0009b00c, (0, 0) and the display's attributes 2 and 3 less 1, which screen 9999
		// reads (S2's); Es1ScreenBlinds takes its bottom as a constant. Retail runs LoadDamageDat_Shell between the HERC
		// diagrams' second and third loads; here it follows all four, and neither reads what the other loads.
		var art = Es1HercArt.Load(content);
		RepairCosts = Es1RepairCosts.Load(content);
		HercDiagrams = new Es1HercDiagrams(art);

		CareersLogPath = FileNames is { Length: > CareersLogEntry } names ? InstallPath(installRoot, names[CareersLogEntry]) : null;
		LoadCareersLog();
		SelectedEntry = Math.Max((short)0, CareerLog?.Current ?? -1);
		LoadBattleSet(content, 0);
	}

	/// <summary>
	/// <paramref name="retailPath"/>, a path the shell opens relative to its directory, in <paramref name="installRoot"/>:
	/// where <c>Path_ApplyAltDrive</c> (<c>00020989</c>) leaves it with no alternative drive.
	/// </summary>
	public static string InstallPath(string installRoot, string retailPath) =>
		CaseInsensitivePath.Combine(installRoot, retailPath.Split('\\', StringSplitOptions.RemoveEmptyEntries));

	/// <summary>
	/// <c>g_BattleSet</c> (<c>000a02cc</c>), the battle set <see cref="LoadBattleSet"/> last loaded
	/// (docs/es1/formats/bst-battle-sets.md#battle-sets); none before, or when its file does not read.
	/// </summary>
	public Es1BattleSet? BattleSet { get; private set; }

	/// <summary>The pilot names <c>PilotNames_Load</c> (<c>00015070</c>) loads into <c>0007ebc4</c>, <c>car\names.dat</c>; none when it does not read.</summary>
	public Es1PilotNames? PilotNames { get; private set; }

	/// <summary>
	/// <c>BattleSet_Load</c> (<c>00013f01</c>)(<c>g_BattleSet</c>, <paramref name="index"/>)
	/// (docs/es1/formats/bst-battle-sets.md#battle-sets): <c>bst\batlsets.bin</c>'s entry <paramref name="index"/> names the
	/// file <c>BattleSet_ReadFile</c> (<c>00013fad</c>) reads from <c>bst\</c> into <see cref="BattleSet"/>; then
	/// <c>SingleMissions_Load</c> (<see cref="SingleMissions"/>) and <c>PilotNames_Load</c> (<see cref="PilotNames"/>).
	/// Retail reads whatever a missing name or file gives; here either is reported and leaves no battle set, and a names
	/// file that does not read leaves the names as they were, engine guards.
	/// </summary>
	public void LoadBattleSet(GameContent content, short index) {
		string[]? sets = content.Read("bst", "BATLSETS.BIN") is { } table ? new BinStringFileTransformer().Parse(table)?.Values : null;
		if (sets == null || index < 0 || index >= sets.Length) {
			Console.Error.WriteLine($"bst\\batlsets.bin is missing or names no battle set {index}: no battle set is loaded.");
			BattleSet = null;
		} else {
			try {
				BattleSet = content.Read("bst", sets[index]) is { } bytes ? new Es1BattleSetTransformer().Parse(bytes) : null;
			} catch (InvalidDataException ex) {
				Console.Error.WriteLine($"bst\\{sets[index]} does not read ({ex.Message}).");
				BattleSet = null;
			}

			if (BattleSet == null) {
				Console.Error.WriteLine($"bst\\{sets[index]} is missing or unreadable: no battle set is loaded.");
			}
		}

		LoadSingleMissions(content);
		try {
			if (content.Read(Es1SingleMission.CarFolder, "NAMES.DAT") is { } names) {
				PilotNames = new Es1PilotNamesTransformer().Parse(names) ?? PilotNames;
			}
		} catch (InvalidDataException ex) {
			Console.Error.WriteLine($"car\\names.dat does not read ({ex.Message}): the pilot names are not loaded.");
		}
	}

	/// <summary>
	/// The single missions' lists, the array <c>00081080</c> of the count word <c>00081084</c>'s low byte; none in the image
	/// (docs/es1/formats/bst-battle-sets.md#single-missions).
	/// </summary>
	public IReadOnlyList<Es1NameList>? SingleMissions { get; private set; }

	/// <summary>
	/// <c>SingleMissions_Load</c> (<c>000361ac</c>): <c>msn\sing_msn.dat</c>'s lists, which replace the array only when the
	/// count is not 0. Retail reads whatever a missing or short file gives; here such a file is reported and changes
	/// nothing, an engine guard.
	/// </summary>
	private void LoadSingleMissions(GameContent content) {
		Es1SingleMissions? file;
		try {
			file = content.Read("msn", "SING_MSN.DAT") is { } bytes ? new Es1SingleMissionsTransformer().Parse(bytes) : null;
		} catch (InvalidDataException ex) {
			Console.Error.WriteLine($"msn\\sing_msn.dat does not read ({ex.Message}): the single missions' lists are not loaded.");
			return;
		}

		if (file == null) {
			Console.Error.WriteLine("msn\\sing_msn.dat is missing: the single missions' lists are not loaded.");
			return;
		}

		if (file.Lists.Length != 0) {
			SingleMissions = file.Lists;
		}
	}

	/// <summary>The style table the shell's controls draw with, <c>CFG\styles.dat</c>; none when it does not read.</summary>
	public Es1WidgetStyles? Styles { get; private set; }

	/// <summary>The frames at <c>000ab4ee</c>, <c>res32\corners.dba</c>; none when it does not read.</summary>
	public IReadOnlyList<DynamixBitmap>? Corners { get; private set; }

	/// <summary>
	/// The start-up's step 14 (docs/es1/formats/styles-dat.md#the-loads): <c>Widget_LoadStylesAndFonts</c>
	/// (<c>00002818</c>), which loads <c>dat\styles</c>, puts <c>dfn\title</c>, <c>cpylw</c>, <c>active</c>,
	/// <c>pushed</c>, <c>inactive</c>, <c>nm_green</c> and <c>nm_red1</c> into the font slots 0 to 6 over what
	/// <see cref="Load"/> put there, and loads <c>dba\corners</c>; then <c>Shell_Main</c>'s own loads of
	/// <c>CFG\styles.dat</c> and <c>res32\corners.dba</c> over those two.
	/// </summary>
	public void LoadStylesAndFonts(GameContent content) {
		string[] names = ["TITLE", "CPYLW", "ACTIVE", "PUSHED", "INACTIVE", "NM_GREEN", "NM_RED1"];
		for (int i = 0; i < names.Length; i++) {
			Fonts[i] = HudFont.Load(content, names[i], "dfn");
		}

		Styles = ReadStyles(content, "cfg") ?? ReadStyles(content, "dat");
		Corners = ReadFrames(content, "res32") ?? ReadFrames(content, "dba");
	}

	private static Es1WidgetStyles? ReadStyles(GameContent content, string folder) =>
		content.Read(folder, "STYLES.DAT") is { } bytes ? new Es1WidgetStylesTransformer().Parse(bytes) : null;

	private static IReadOnlyList<DynamixBitmap>? ReadFrames(GameContent content, string folder) =>
		content.Read(folder, "CORNERS.DBA") is { } bytes ? new DynamixBitmapArrayTransformer().Parse(bytes)?.Images : null;

	/// <summary>
	/// <c>CareerLog_Load</c> (<c>00017e7f</c>) of <see cref="CareersLogPath"/>: a file that does not open gives no careers
	/// and the current entry −1 (docs/es1/formats/career-log-and-player-mec.md#loading), and the byte <c>+8</c> is cleared.
	/// A file that does not read whole is reported and leaves no log, which <see cref="WriteCareersLog"/> then does not
	/// write, an engine guard: retail reads whatever the bytes give and writes it back.
	/// </summary>
	private void LoadCareersLog() {
		_careerAdded = false;
		if (CareersLogPath is not { } path) {
			return;
		}

		try {
			CareerLog = File.Exists(path) ? new Es1CareerLogTransformer().Parse(File.ReadAllBytes(path)) ?? new Es1CareerLog()
				: new Es1CareerLog();
		} catch (InvalidDataException ex) {
			Console.Error.WriteLine($"{path} does not read ({ex.Message}): the careers log is left alone.");
			CareerLog = null;
		}
	}

	/// <summary>
	/// <c>CareerLog_Write</c> (<c>00017f5f</c>)(log, −1), the shutdown's step 1: the file deleted and written again with
	/// every entry (docs/es1/formats/career-log-and-player-mec.md#writing).
	/// </summary>
	public void WriteCareersLog() => WriteCareersLog(-1);

	/// <summary>
	/// <c>CareerLog_Write</c> (<c>00017f5f</c>)(log, <paramref name="skip"/>): the file deleted and written with the count,
	/// one less when an entry is left out, and when that is not 0 the current entry and every entry but
	/// <paramref name="skip"/>. Retail asserts at severity 5 (<c>career.cpp</c> line <c>0x51b</c>) when it does not open;
	/// here that is reported and the caller goes on, an engine guard.
	/// </summary>
	private void WriteCareersLog(short skip) {
		if (CareerLog is not { } log || CareersLogPath is not { } path) {
			return;
		}

		var written = new Es1CareerLog { Current = log.Current, Entries = log.Entries.Where((_, i) => i != skip).ToArray() };
		try {
			File.Delete(path);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllBytes(path, new Es1CareerLogTransformer().Write(written)!);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Unable to open careers log file {path} ({ex.Message}; career.cpp line 0x51b asserts here).");
		}
	}

	/// <summary>The text table's line <c>UNTITLED</c>, entry <c>0x16</c> of <c>0009afa8</c>, an empty name's.</summary>
	private const int UntitledLine = 0x16;

	/// <summary>
	/// <c>CareerLog_FindOrAdd</c> (<c>00018099</c>)(log, <paramref name="word"/>, <paramref name="name"/>)
	/// (docs/es1/formats/career-log-and-player-mec.md#adding-a-career): an empty name taken as <c>UNTITLED</c>; with no
	/// entries, entry 0 filled with the name, the word and <c>CAR\CAR_0.CAR</c> and <c>CAR\CAR_0.MEC</c>; otherwise the first
	/// entry whose name equals the name, ignoring case, and whose word equals the word; with none and no entry added since
	/// the load, a new last entry filled the same way with the paths of its index; with none and one added, the name and
	/// the word copied over the last entry, its paths kept. The entry returned is made the log's current one and
	/// <see cref="SelectedEntry"/>. The name is copied with its NUL over the entry's buffer, whose bytes past it stay. With
	/// no log, as after a file that did not read, a new one is started here, an engine guard.
	/// </summary>
	public Es1CareerLogEntry FindOrAdd(short word, string name) {
		if (name.Length == 0) {
			name = Text is { Length: > UntitledLine } text ? text[UntitledLine] : "UNTITLED";
		}

		var log = CareerLog ??= new Es1CareerLog();
		var entries = log.Entries;
		short index;
		if (entries.Length == 0) {
			index = 0;
			log.Entries = [NewEntry(0)];
			_careerAdded = true;
		} else {
			index = (short)Array.FindIndex(entries, entry =>
				string.Equals(Es1CareerLogEntry.Text(entry.Name), name, StringComparison.OrdinalIgnoreCase) && entry.Word == word);
			if (index < 0 && !_careerAdded) {
				index = (short)entries.Length;
				log.Entries = [.. entries, NewEntry(index)];
				_careerAdded = true;
			} else if (index < 0) {
				index = (short)(entries.Length - 1);
				CopyName(entries[index]);
			}
		}

		SelectedEntry = index;
		log.Current = index;
		return log.Entries[index];

		Es1CareerLogEntry NewEntry(int number) {
			var entry = new Es1CareerLogEntry();
			CopyName(entry);
			CopyText(entry.CareerFile, $"CAR\\CAR_{number}.CAR");
			CopyText(entry.HangarFile, $"CAR\\CAR_{number}.MEC");
			return entry;
		}

		void CopyName(Es1CareerLogEntry entry) {
			CopyText(entry.Name, name);
			entry.Word = word;
		}
	}

	/// <summary>
	/// <paramref name="text"/> and its NUL copied over the start of <paramref name="buffer"/>, cut to the buffer where
	/// retail's copy runs past it, an engine guard.
	/// </summary>
	private static void CopyText(byte[] buffer, string text) {
		byte[] bytes = Encoding.Latin1.GetBytes(text);
		int length = Math.Min(bytes.Length, buffer.Length - 1);
		Array.Copy(bytes, buffer, length);
		buffer[length] = 0;
	}

	/// <summary>
	/// <c>CareerLog_Delete</c> (<c>0001828e</c>)(log, <paramref name="index"/>)
	/// (docs/es1/formats/career-log-and-player-mec.md#writing): for an index below the count, the entry's two files deleted
	/// by their paths as the entry holds them, the current entry less 1 when it is not 0, the log written without the entry
	/// (<see cref="WriteCareersLog(short)"/>) and loaded again. Retail's test is signed, so a negative index reads before the
	/// entries; here it does nothing, an engine guard.
	/// </summary>
	public void DeleteCareer(string installRoot, short index) {
		if (CareerLog is not { } log || index < 0 || index >= log.Entries.Length) {
			return;
		}

		var entry = log.Entries[index];
		foreach (byte[] file in new[] { entry.CareerFile, entry.HangarFile }) {
			try {
				File.Delete(InstallPath(installRoot, Es1CareerLogEntry.Text(file)));
			} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Console.Error.WriteLine($"{Es1CareerLogEntry.Text(file)} is not deleted ({ex.Message}).");
			}
		}

		if (log.Current != 0) {
			log.Current--;
		}

		WriteCareersLog(index);
		LoadCareersLog();
	}

	/// <summary>
	/// <c>CareerLog_NameLines</c> (<c>000184ff</c>)(log) (docs/es1/shell/career.md#the-list): each entry's name, in log
	/// order, and the end. The buffers retail keeps at <c>CareerLog_NameBuffers</c> (<c>0007edfc</c>) until the next call are
	/// the strings here. With no log, as after a file that did not read, there are no names.
	/// </summary>
	public string?[] CareerNameLines() =>
		[.. (CareerLog?.Entries ?? []).Select(entry => Es1CareerLogEntry.Text(entry.Name)), null];
}
