using System.Globalization;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;

namespace Herculan.Es1.Shell;

/// <summary>
/// The roster <c>Career_Roster</c> (<c>0009b218</c>), the loaded career: its header and 41 career records, record 0 the
/// career and records 1 to 40 its backups (docs/es1/shell/career.md#the-roster), with the 41 weapon stocks the dwords
/// from <c>000aad62</c> point to and the globals of <c>career.cpp</c> the roster's functions keep, <c>Career_Salvage</c>,
/// the word <c>0007fcf8</c>, <c>Career_Loaded</c> and <c>Career_BackupLine</c>; and those functions, each citing its retail
/// symbol, over the career's two files in the install's <c>car\</c> (docs/es1/shell/career.md#the-files). The class is
/// this engine's bundle of those globals; every member is retail's.
/// </summary>
public sealed class Es1CareerRoster {
	/// <summary>The records a roster holds.</summary>
	public const int RecordCount = Es1CareerFile.RecordCount;

	/// <summary>The backups' records, 1 to 40.</summary>
	private const int LastBackup = RecordCount - 1;

	/// <summary><c>Career_Salvage</c>'s image value and a new career's, 60000 kilograms.</summary>
	public const int StartingSalvage = 60000;

	/// <summary>The file-name table's entries of <c>data\mission.var</c> and <c>car\temp.mec</c>.</summary>
	private const int MissionVarEntry = 5, TempHangarEntry = 12;

	/// <summary>
	/// <c>Career_OutcomeThresholds</c> (<c>0007ec80</c>), indexed by the word <c>000aad5e</c>
	/// (docs/es1/formats/bst-battle-sets.md#a-careers-missions).
	/// </summary>
	private static readonly short[] OutcomeThresholds = [4, 4, 4, 3, 3, 3, 4, 3, 3, 3];

	private readonly string _installRoot;
	private readonly GameContent _content;
	private readonly Es1ShellData _data;
	private readonly Es1ShellMission _mission;

	/// <param name="installRoot">The install whose <c>car\</c> holds the career's files.</param>
	/// <param name="content">The shell's archives, which a new career's starting files are read from.</param>
	/// <param name="data">The shared data: the careers log, the file-name table, the generator and the battle set.</param>
	/// <param name="mission">The mission globals the roster's functions set: the mode, the flags, the outcome, the word <c>000aad5e</c> and the hangar.</param>
	public Es1CareerRoster(string installRoot, GameContent content, Es1ShellData data, Es1ShellMission mission) {
		_installRoot = installRoot;
		_content = content;
		_data = data;
		_mission = mission;
	}

	/// <summary><c>+0</c>: <c>Career_New</c> sets it to 1 and <see cref="AdvanceCareer"/> clears it (career.md's Open).</summary>
	public byte Byte0 { get; set; }

	/// <summary><c>+1</c>, the career's careers-log entry, whose paths name its two files; none in the image.</summary>
	public Es1CareerLogEntry? LogEntry { get; private set; }

	/// <summary><c>+5</c>, the battle set (docs/es1/formats/bst-battle-sets.md#which-battle-set).</summary>
	public short BattleSet { get; set; }

	/// <summary><c>+7</c>, the record the shell works on: 0, except within the functions that set it and put it back.</summary>
	public short Current { get; set; }

	/// <summary><c>+9</c>, the current record's word <c>+2</c> while it is loaded.</summary>
	public short Word9 { get; set; }

	/// <summary><c>+0xb</c>, 12 name groups of <c>car\names.dat</c>, row <i>r</i> for squadmate <i>r</i> + 1.</summary>
	public short[] NameGroups { get; } = new short[Es1CareerFile.NameGroupCount];

	/// <summary><c>+0x23</c>, the 41 records, zero in the image.</summary>
	public Es1CareerRecord[] Records { get; } = Enumerable.Range(0, RecordCount).Select(_ => new Es1CareerRecord()).ToArray();

	/// <summary>The 41 weapon stocks the dwords from <c>000aad62</c> point to, one per record, which <c>00027c4c</c> makes empty at the start-up.</summary>
	public Es1WeaponStock[] Stocks { get; } = Enumerable.Range(0, RecordCount).Select(_ => new Es1WeaponStock()).ToArray();

	/// <summary><c>Career_Salvage</c> (<c>0007fcf0</c>), the career's salvage in kilograms.</summary>
	public int Salvage { get; set; } = StartingSalvage;

	/// <summary>
	/// <c>Career_AutoRepairMode</c> (<c>0007fcf8</c>), 1 in the image, which PREFERENCES sets and the career's saves store
	/// at a record's <c>+0x1f5</c> (<see cref="Es1CareerRecord.AutoRepairMode"/>).
	/// </summary>
	public short AutoRepairMode { get; set; } = 1;

	/// <summary><c>Career_Loaded</c> (<c>000809aa</c>): the current career is loaded this session.</summary>
	public bool Loaded { get; set; }

	/// <summary><c>Career_BackupLine</c> (<c>000809b4</c>), the backup list's selected line (docs/es1/shell/career.md#the-backup-list).</summary>
	public short BackupLine { get; set; }

	/// <summary>
	/// <c>CareerRecord_Current</c> (<c>00014ef4</c>) in <c>Shell_Mode</c> 1: the record <see cref="Current"/> names. Retail
	/// reads past the roster for a word outside the 41; here that gives record 0, an engine guard.
	/// </summary>
	public Es1CareerRecord CurrentRecord => Current is >= 0 and < RecordCount ? Records[Current] : Records[0];

	/// <summary>
	/// <c>PilotEntry_Condition</c> (<c>0001618d</c>)(<paramref name="record"/>, its <c>+0xe</c>): the player's pilot's
	/// condition. Retail reads the entry with no bounds test; an entry past the four reads 0 here, an engine guard.
	/// </summary>
	public static short PlayerCondition(Es1CareerRecord record) =>
		record.PlayerEntry is >= 0 and < Es1CareerRecord.PilotCount ? record.Pilots[record.PlayerEntry].Condition : (short)0;

	/// <summary>
	/// <c>Roster_NthUsedRecord</c> (<c>00016845</c>)(roster, <paramref name="n"/>): 0 for an <paramref name="n"/> of 0;
	/// otherwise the index of the <paramref name="n"/>-th of records 1 to 40 in use, or 0 when there are fewer.
	/// </summary>
	public short NthUsedRecord(short n) {
		if (n == 0) {
			return 0;
		}

		short count = 0;
		for (short i = 1; i <= LastBackup; i++) {
			if (Records[i].InUse != 0) {
				count++;
			}

			if (count == n) {
				return i;
			}
		}

		return 0;
	}

	/// <summary><c>Roster_CountUsedRecords</c> (<c>00017026</c>): the records 1 to 40 in use, the backups.</summary>
	public short CountUsedRecords() => (short)Records.Skip(1).Count(record => record.InUse != 0);

	/// <summary><c>Career_CampaignStarts</c> (<c>0007ebf0</c>): the missions played before each campaign.</summary>
	private static readonly short[] CampaignStarts = [0, 7, 7, 15, 15, 24, 24, 32];

	/// <summary><c>Career_CampaignNumbers</c> (<c>0007ebe0</c>): each campaign's number as the player counts them.</summary>
	private static readonly short[] CampaignNumbers = [1, 2, 2, 3, 3, 4, 4, 5];

	/// <summary>
	/// <c>Roster_BackupLines</c> (<c>000aa7d4</c>), 41 lines of 25 bytes, line <i>n</i> record <i>n</i>'s, empty in the image:
	/// what <see cref="BackupListLines"/> last wrote.
	/// </summary>
	private readonly string[] _backupLines = Enumerable.Repeat("", RecordCount).ToArray();

	/// <summary>
	/// <c>Roster_BackupListLines</c> (<c>000183bd</c>) (docs/es1/shell/career.md#the-backup-list): for each of records 1 to
	/// 40 in use, in order, its line written into <c>Roster_BackupLines</c> at the record's number with
	/// <c>CAMPAIGN %1d: MISSION %1d</c> (<c>0007ede1</c>): <i>k</i> the record's campaign, replaced by the last of 0 to 7
	/// whose <see cref="CampaignStarts"/> word is its missions played; the number <see cref="CampaignNumbers"/>[<i>k</i>]
	/// and the mission the missions played − <see cref="CampaignStarts"/>[<i>k</i>] + 1. The lines end with a null. Retail
	/// reads past the tables for a <i>k</i> outside 0 to 7; here such a <i>k</i> reads 0 from both, an engine guard.
	/// </summary>
	public string?[] BackupListLines() {
		var lines = new List<string?>();
		for (int n = 1; n <= LastBackup; n++) {
			var record = Records[n];
			if (record.InUse == 0) {
				continue;
			}

			int k = record.Campaign;
			for (int c = 0; c < CampaignStarts.Length; c++) {
				if (record.MissionsPlayed == CampaignStarts[c]) {
					k = c;
				}
			}

			bool known = k >= 0 && k < CampaignStarts.Length;
			int number = known ? CampaignNumbers[k] : 0, start = known ? CampaignStarts[k] : 0;
			_backupLines[n] = string.Create(CultureInfo.InvariantCulture, $"CAMPAIGN {number}: MISSION {record.MissionsPlayed - start + 1}");
			lines.Add(_backupLines[n]);
		}

		lines.Add(null);
		return [.. lines];
	}

	/// <summary>
	/// <c>Roster_SaveSlotUsed</c> (<c>000167a1</c>) (docs/es1/shell/career.md#save-and-the-overwrite-warning): whether the
	/// record numbered record 0's missions played + 1, the backup <see cref="SaveBackup"/> would write, is in use. Retail
	/// reads past the roster for a number past 40; here such a record is not in use, an engine guard.
	/// </summary>
	public bool SaveSlotUsed() {
		int slot = Records[0].MissionsPlayed + 1;
		return slot is >= 0 and < RecordCount && Records[slot].InUse != 0;
	}

	/// <summary>
	/// <c>Roster_SaveBackup</c> (<c>000167cd</c>): <see cref="Current"/> set to the missions played of the record it names,
	/// plus 1, <see cref="Save"/> to that record, and <see cref="Current"/> 0 again.
	/// </summary>
	public void SaveBackup() {
		Current = (short)(CurrentRecord.MissionsPlayed + 1);
		Save(Current);
		Current = 0;
	}

	/// <summary>
	/// <c>Roster_RestoreBackup</c> (<c>00016817</c>)(roster, <paramref name="n"/>) (docs/es1/shell/career.md#restore):
	/// <see cref="Current"/> set to <see cref="NthUsedRecord"/>(<paramref name="n"/>), that record made the career with
	/// <see cref="ActivateRecord"/>, and <see cref="Current"/> 0 again.
	/// </summary>
	public void RestoreBackup(short n) {
		Current = NthUsedRecord(n);
		ActivateRecord();
		Current = 0;
	}

	/// <summary>
	/// <c>Roster_SaveSlotText</c> (<c>0001838d</c>): <c>Roster_BackupLines</c>' line at the missions played of the record
	/// <see cref="Current"/> names, plus 1, the line the backup list last wrote for the backup SAVE would replace. Retail
	/// reads past the lines for a number past 40; here such a number gives an empty line, an engine guard.
	/// </summary>
	public string SaveSlotText() {
		int line = CurrentRecord.MissionsPlayed + 1;
		return line is >= 0 and < RecordCount ? _backupLines[line] : "";
	}

	/// <summary>
	/// <c>Roster_ForEachPilotNoOp</c> (<c>00016fdd</c>): <c>BarGraph_NoOpCareerCopy</c> (<c>0001600d</c>)(the record
	/// <see cref="Current"/> names, <i>i</i>) for <i>i</i> from 0 to 3, a call that does nothing.
	/// </summary>
	public static void ForEachPilotNoOp() {
	}

	/// <summary>
	/// <c>Words_RandomPermutation</c> (<c>000162f4</c>)(<paramref name="n"/>, words): the words set to −1, then for <i>i</i>
	/// from 0 to <paramref name="n"/> − 1, <i>i</i> put in the (<i>k</i> + 1)-th word still −1, <i>k</i> a draw below
	/// <paramref name="n"/> − <i>i</i> on <paramref name="randomBelow"/>: a random order of 0 to <paramref name="n"/> − 1.
	/// </summary>
	public static short[] RandomPermutation(short n, Func<short, int> randomBelow) {
		var words = Enumerable.Repeat((short)-1, n).ToArray();
		for (short i = 0; i < n; i++) {
			int k = randomBelow((short)(n - i));
			int at = 0;
			for (int seen = 0; seen < k; at++) {
				if (words[at] == -1) {
					seen++;
				}
			}

			while (words[at] != -1) {
				at++;
			}

			words[at] = i;
		}

		return words;
	}

	/// <summary>
	/// <c>Roster_ShuffleNameGroups</c> (<c>00016677</c>): for each squadmate row <i>r</i> from 0 to 2,
	/// <see cref="RandomPermutation"/>(4) and then <see cref="NameGroups"/>' word 4<i>r</i> + <i>i</i> set to 4<i>r</i> +
	/// word <i>i</i>, so each row holds its four groups in a random order.
	/// </summary>
	private void ShuffleNameGroups(Func<short, int> randomBelow) {
		for (int row = 0; row < 3; row++) {
			short[] order = RandomPermutation(4, randomBelow);
			for (int i = 0; i < 4; i++) {
				NameGroups[row * 4 + i] = (short)(row * 4 + order[i]);
			}
		}
	}

	/// <summary>
	/// <c>Roster_InitSquadPilots</c> (<c>000166ed</c>): on the record <see cref="Current"/> names, pilot entry 0's slot set to
	/// 0 (and <c>BarGraph_NoOpCareerCopy</c>, which does nothing); then on record 0, whatever <see cref="Current"/> holds,
	/// each squadmate's entry <i>i</i> from 1 to 3: the name group the first of row <i>i</i> − 1 of
	/// <see cref="NameGroups"/>, the name 0, <c>+4</c> 1, <c>+6</c> 0, the slot <i>i</i> and on strength.
	/// </summary>
	private void InitSquadPilots() {
		CurrentRecord.Pilots[0].Slot = 0;
		for (int i = 1; i < Es1CareerRecord.PilotCount; i++) {
			var pilot = Records[0].Pilots[i];
			pilot.NameGroup = NameGroups[(i - 1) * 4];
			pilot.Name = 0;
			pilot.Word4 = 1;
			pilot.Word6 = 0;
			pilot.Slot = (short)i;
			pilot.OnStrength = 1;
		}
	}

	/// <summary>
	/// <c>Career_New</c> (<c>000163ac</c>)(roster, <paramref name="word"/>, <paramref name="name"/>)
	/// (docs/es1/shell/career.md#career_new): the careers-log entry <see cref="Es1ShellData.FindOrAdd"/> gives; the battle
	/// set the word <c>0007fcec</c> (<see cref="Es1ShellData.NewCareerBattleSet"/>), <see cref="Current"/> and
	/// <see cref="Word9"/> 0, <see cref="Byte0"/> 1 and <see cref="Salvage"/> 60000; the name groups shuffled; stock 0 loaded
	/// from <c>car\c_armory.dat</c> and the hangar from <c>car\car_init.dat</c>; then for each of the 41 records,
	/// <c>CareerRecord_Init</c> in the mode (each zeroing <see cref="Es1ShellMission.Outcome"/>), the record
	/// <see cref="Current"/> names made the career (in use, <see cref="InitSquadPilots"/>, the salvage, its flags into
	/// <see cref="Es1ShellMission.Flags"/>, <see cref="AutoRepairMode"/> into it), the record and stock 0 to the career file
	/// and the hangar (<c>Hangar_Write</c>) to the hangar file; then that record's player entry made its current pilot.
	/// Both files are written through a <c>FileRWStream</c>, which does not truncate. Retail asserts at severity 5
	/// (<c>career.cpp</c> lines <c>0x2d1</c> and <c>0x2d8</c>) when a file does not open, and reads what a missing starting
	/// file's stream gives; here either is reported and the career goes on, a missing stock or hangar left empty, engine
	/// guards.
	/// </summary>
	public void New(short word, string name) {
		var entry = _data.FindOrAdd(word, name);
		LogEntry = entry;
		BattleSet = _data.NewCareerBattleSet;
		Current = 0;
		Word9 = 0;
		Byte0 = 1;
		Salvage = StartingSalvage;
		Func<short, int> below = bound => _data.Random?.Below(bound) ?? 0;
		ShuffleNameGroups(below);

		LoadStock("C_ARMORY.DAT");
		var hangar = Es1Hangar.Read(ReadShellFile(Es1SingleMission.CarFolder, "CAR_INIT.DAT", new Es1HercListTransformer()) ?? new Es1HercList());
		_mission.Hangar = hangar;
		var tech = ReadShellFile("bst", _mission.Mode == 1 ? "TECH_0.DAT" : "TECH_SNG.DAT", new Es1TechFileTransformer()) ?? new Es1TechFile();

		var careerFile = new Es1CareerFile { Byte0 = Byte0, BattleSet = BattleSet, NameGroups = (short[])NameGroups.Clone() };
		var records = new Es1CareerFileRecord[RecordCount];
		var lists = new Es1HercList[RecordCount];
		var recordTransformer = new Es1CareerRecordTransformer();
		for (int i = 0; i < RecordCount; i++) {
			var record = Records[i];
			if (_mission.Mode == 0) {
				record.InitSingleMission(tech);
			} else {
				record.InitCareer(tech, below);
			}

			_mission.Outcome = 0;
			if (i == Current) {
				record.InUse = 1;
				InitSquadPilots();
				record.Dword129 = Salvage;
				Array.Copy(record.Flags, _mission.Flags, _mission.Flags.Length);
				record.AutoRepairMode = AutoRepairMode;
			}

			// The record's bytes as they stand now, before the next record's InitSquadPilots or the selection below.
			records[i] = new Es1CareerFileRecord { Record = recordTransformer.Parse(recordTransformer.Write(record))!, Stock = Stocks[0].ToFile() };
			lists[i] = HangarWrite(hangar);
		}

		careerFile.Records = records;
		WriteFile(CareerFilePath(entry), new Es1CareerFileTransformer().Write(careerFile)!, truncate: false, "career.cpp line 0x2d1");
		WriteFile(HangarFilePath(entry), new Es1CareerHangarFileTransformer().Write(new Es1CareerHangarFile { Lists = lists })!,
			truncate: false, "career.cpp line 0x2d8");

		CurrentRecord.SelectPilot(CurrentRecord.PlayerEntry, hangar);
		ForEachPilotNoOp();
	}

	/// <summary>
	/// <c>Career_LoadCurrent</c> (<c>00016c1e</c>) (docs/es1/shell/career.md#the-files): <see cref="Current"/> 0, the
	/// careers log's current entry kept as <see cref="LogEntry"/>, the career file read whole into the header, the 41
	/// records and their stocks, and record 0 made current with <see cref="ActivateRecord"/>. Retail asserts at severity 5
	/// (<c>career.cpp</c> line <c>0x38a</c>) when the file does not open and reads past a log's entries for a current entry
	/// outside them; here a file that does not read, or no such entry, is reported and loads nothing, engine guards.
	/// </summary>
	public void LoadCurrent() {
		Current = 0;
		if (_data.CareerLog is not { } log || log.Current < 0 || log.Current >= log.Entries.Length) {
			Console.Error.WriteLine("The careers log has no current entry: no career is loaded.");
			return;
		}

		var entry = log.Entries[log.Current];
		LogEntry = entry;
		string path = CareerFilePath(entry);
		Es1CareerFile? file;
		try {
			file = new Es1CareerFileTransformer().Parse(File.ReadAllBytes(path));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
			Console.Error.WriteLine($"{path} does not read ({ex.Message}; career.cpp line 0x38a asserts here): no career is loaded.");
			return;
		}

		if (file == null) {
			Console.Error.WriteLine($"{path} is empty: no career is loaded.");
			return;
		}

		Byte0 = file.Byte0;
		BattleSet = file.BattleSet;
		Array.Copy(file.NameGroups, NameGroups, NameGroups.Length);
		for (int i = 0; i < RecordCount; i++) {
			Records[i] = file.Records[i].Record;
			Stocks[i].Read(file.Records[i].Stock);
		}

		ActivateRecord();
	}

	/// <summary>
	/// <c>Roster_ActivateRecord</c> (<c>00016d45</c>)
	/// (docs/es1/shell/career.md#making-a-record-current--roster_activaterecord-00016d45): on the record <i>r</i>
	/// <see cref="Current"/> names, <see cref="Salvage"/> from its <c>+0x129</c>, the word <c>000aad5e</c>
	/// (<see cref="Es1ShellMission.RestoredMissionKind"/>) from its <c>+0xc</c>, <see cref="Word9"/> from its <c>+2</c>, the
	/// flags from its 100 words and the outcome <c>000aad58</c> from flag 0; with <see cref="Current"/> not 0, <i>r</i> copied
	/// over record 0 and its stock over stock 0 (<see cref="CopyStock"/>); <see cref="AutoRepairMode"/> from its
	/// <c>+0x1f5</c>; hangar list <see cref="Current"/> of the hangar file read into the hangar (<see cref="ReadHangar"/>);
	/// and <i>r</i>'s player entry made its current pilot.
	/// </summary>
	public void ActivateRecord() {
		var record = CurrentRecord;
		Salvage = record.Dword129;
		_mission.RestoredMissionKind = record.LastMissionKind;
		Word9 = record.Word2;
		Array.Copy(record.Flags, _mission.Flags, _mission.Flags.Length);
		_mission.Outcome = _mission.Flags[0];
		if (Current != 0) {
			Records[0] = CopyRecord(record);
			CopyStock(Current, 0);
		}

		AutoRepairMode = record.AutoRepairMode;
		var hangar = ReadHangar(Current);
		_mission.Hangar = hangar;
		record.SelectPilot(record.PlayerEntry, hangar);
		ForEachPilotNoOp();
	}

	/// <summary>
	/// <c>Career_Save</c> (<c>000168b0</c>)(roster, <paramref name="slot"/>) (docs/es1/shell/career.md#saving--career_save-000168b0):
	/// with no careers-log entry, nothing. Otherwise the flags to <c>data\mission.var</c>; the career file deleted and
	/// written with the header; record 0 updated (in use, <c>+2</c> from <see cref="Word9"/>, the salvage, the flags,
	/// <see cref="AutoRepairMode"/>) and, for a slot other than 0, copied over record <paramref name="slot"/> with its stock;
	/// the 41 records and their stocks written; then the hangar file written again through <c>car\temp.mec</c>, list
	/// <paramref name="slot"/> the hangar as it stands and every other list copied from the old file
	/// (<c>HercList_Copy</c>, <see cref="CopyHercList"/>), and <c>temp.mec</c> renamed over it. Retail asserts at severity 5
	/// when a file does not open (lines <c>0x346</c>, <c>0x350</c>) or the rename fails (line <c>0x382</c>), and copies
	/// whatever an old hangar file that does not open gives; here each is reported and the save goes on, a list the old file
	/// lacks written as a list of zeros, and a slot outside the records copies nothing, engine guards.
	/// </summary>
	public void Save(short slot) {
		if (LogEntry is not { } entry) {
			return;
		}

		if (DataFile(MissionVarEntry) is { } missionVar) {
			byte[] flags = new byte[_mission.Flags.Length * 2];
			Buffer.BlockCopy(_mission.Flags, 0, flags, 0, flags.Length);
			WriteFile(missionVar, flags, truncate: true, "career.cpp line 0x346");
		}

		var record = Records[0];
		record.InUse = 1;
		record.Word2 = Word9;
		record.Dword129 = Salvage;
		Array.Copy(_mission.Flags, record.Flags, record.Flags.Length);
		record.AutoRepairMode = AutoRepairMode;
		if (slot is > 0 and < RecordCount) {
			Records[slot] = CopyRecord(record);
			CopyStock(0, slot);
		}

		var careerFile = new Es1CareerFile {
			Byte0 = Byte0, BattleSet = BattleSet, NameGroups = (short[])NameGroups.Clone(),
			Records = Enumerable.Range(0, RecordCount)
				.Select(i => new Es1CareerFileRecord { Record = Records[i], Stock = Stocks[i].ToFile() }).ToArray(),
		};
		string careerPath = CareerFilePath(entry);
		TryDelete(careerPath);
		WriteFile(careerPath, new Es1CareerFileTransformer().Write(careerFile)!, truncate: false, "career.cpp line 0x350");

		string hangarPath = HangarFilePath(entry);
		var old = ReadHangarFile(hangarPath, report: false);
		var lists = new Es1HercList[RecordCount];
		for (int i = 0; i < RecordCount; i++) {
			var oldList = old != null ? old.Lists[i] : new Es1HercList();
			lists[i] = i == slot ? HangarWrite(_mission.Hangar ?? Es1Hangar.Read(new Es1HercList())) : CopyHercList(oldList);
		}

		if (DataFile(TempHangarEntry) is not { } tempPath) {
			return;
		}

		WriteFile(tempPath, new Es1CareerHangarFileTransformer().Write(new Es1CareerHangarFile { Lists = lists })!, truncate: false,
			"FileRWStream_Open");
		TryDelete(hangarPath);
		try {
			File.Move(tempPath, hangarPath);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Unable to rename file {tempPath} to {hangarPath} ({ex.Message}; career.cpp line 0x382 asserts here).");
		}
	}

	/// <summary>
	/// <c>Roster_AdvanceCareer</c> (<c>00016f22</c>)
	/// (docs/es1/shell/career.md#moving-a-career-on--roster_advancecareer-00016f22): <see cref="Byte0"/> and the byte
	/// <c>+1</c> of the record <see cref="Current"/> names cleared, the record moved on (<see cref="AdvanceCampaign"/>), and
	/// when its campaign changed its word <c>+2</c> and <see cref="Word9"/> 0 and its 100 words cleared.
	/// </summary>
	public void AdvanceCareer() {
		Byte0 = 0;
		var record = CurrentRecord;
		record.Byte1 = 0;
		if (AdvanceCampaign(record)) {
			record.Word2 = 0;
			Word9 = 0;
			record.ClearWords();
		}
	}

	/// <summary>
	/// <c>Career_OutcomeChoice</c> (<c>000159e9</c>)(<see cref="Es1ShellMission.Outcome"/>): 1 when the outcome exceeds
	/// the threshold the word <c>000aad5e</c> indexes, else 0. Retail indexes with no bounds test; a word outside the ten
	/// gives 0 here, an engine guard.
	/// </summary>
	private int OutcomeChoice() {
		short kind = _mission.RestoredMissionKind;
		return kind >= 0 && kind < OutcomeThresholds.Length && _mission.Outcome > OutcomeThresholds[kind] ? 1 : 0;
	}

	/// <summary>
	/// <c>Career_PeekNextCampaign</c> (<c>00015aa5</c>)(<paramref name="record"/>) (docs/es1/shell/between-missions.md#moving-on):
	/// <see cref="Es1BattleSet.PeekNextCampaign"/> of the loaded battle set for <see cref="OutcomeChoice"/>, the record's
	/// position and campaign. With no battle set, as when its file did not read, the answer is 0, an engine guard.
	/// </summary>
	public short PeekNextCampaign(Es1CareerRecord record) =>
		_data.BattleSet?.PeekNextCampaign(OutcomeChoice(), record.Position, record.Campaign) ?? 0;

	/// <summary>
	/// <c>Career_AdvanceCampaign</c> (<c>00015a5a</c>)(<paramref name="record"/>): <see cref="Es1BattleSet.Advance"/> on the
	/// record's position and campaign; true when the campaign changed. With no battle set the record stays, an engine guard.
	/// </summary>
	public bool AdvanceCampaign(Es1CareerRecord record) {
		if (_data.BattleSet is not { } set) {
			return false;
		}

		short position = record.Position, campaign = record.Campaign;
		short before = campaign;
		set.Advance(OutcomeChoice(), ref position, ref campaign);
		record.Position = position;
		record.Campaign = campaign;
		return campaign != before;
	}

	/// <summary>
	/// <c>Career_StartMission</c> (<c>00016e93</c>)(roster, <paramref name="b"/>) (docs/es1/shell/career.md#a-careers-next-mission):
	/// <see cref="Current"/> set to <see cref="NthUsedRecord"/>(<paramref name="b"/>); the battle set <see cref="BattleSet"/>
	/// loaded again (<see cref="Es1ShellData.LoadBattleSet"/>); the mission's path built from the record <see cref="Current"/>
	/// names (<see cref="BuildMissionPath"/>); the mission loaded in mode 1 (<see cref="Es1ShellMission.LoadCareerMission"/>);
	/// the record's pilots marked by <c>Squad_MemberCount</c> (<see cref="Es1CareerRecord.MarkTopPilots"/>); the record's
	/// <c>+0xc</c> set to <c>Mission_Kind</c>; and the paths saved (<see cref="Es1ShellMission.WriteRestore"/>). A path that
	/// is not built loads nothing, an engine guard (<see cref="GetMissionName"/>).
	/// </summary>
	public void StartMission(short b) {
		Current = NthUsedRecord(b);
		_data.LoadBattleSet(_content, BattleSet);
		var record = CurrentRecord;
		if (BuildMissionPath(record) is { } name) {
			_mission.LoadCareerMission(name, record);
		}

		record.MarkTopPilots(_mission.SquadMemberCount);
		CurrentRecord.LastMissionKind = _mission.MissionKind;
		_mission.WriteRestore();
	}

	/// <summary>
	/// <c>Career_BuildMissionPath</c> (<c>00015a13</c>)(<paramref name="record"/>)
	/// (docs/es1/formats/bst-battle-sets.md#a-careers-missions): <see cref="GetMissionName"/> for <see cref="OutcomeChoice"/>,
	/// the record's position <c>+8</c> and campaign <c>+6</c>, formatted as <c>msn\%s</c> into <see cref="Es1ShellMission.Path"/>
	/// with the text's path set (<see cref="Es1ShellMission.SetPath"/>). Returns the name, or null when none is found,
	/// which leaves both paths as they were.
	/// </summary>
	private string? BuildMissionPath(Es1CareerRecord record) {
		if (GetMissionName(OutcomeChoice(), record.Position, record.Campaign) is not { } name) {
			return null;
		}

		_mission.SetPath(name);
		return name;
	}

	/// <summary>
	/// <c>BattleSet_GetMissionName</c> (<c>000140a5</c>)(<c>g_BattleSet</c>, <paramref name="outcome"/>,
	/// <paramref name="position"/>, <paramref name="campaign"/>): the position's name for the outcome,
	/// <c>Campaign_GetPositionName</c> (<c>000142f6</c>) through <c>StrList_Get</c> (<c>00020254</c>), which takes the
	/// list's only name when it holds one and name <paramref name="outcome"/> otherwise. A name holding <c>.MSN</c>
	/// (<c>Str_FindBoyerMoore</c>, case kept) is returned as it is; any other names a list, <c>bst\&lt;name&gt;</c>, read as
	/// one name list (<c>StrList_Read</c>), and the name <c>StrList_PickRandom</c> (<see cref="Es1ShellRandom.PickRandom"/>)
	/// draws from it is copied over the position's name and returned. Retail reads past its arrays for a campaign, position
	/// or name that is not there and reads what a missing list's stream gives; here each is reported and gives null, engine
	/// guards.
	/// </summary>
	private string? GetMissionName(int outcome, short position, short campaign) {
		if (_data.BattleSet is not { } set || campaign < 0 || campaign >= set.Campaigns.Length
				|| position < 0 || position >= set.Campaigns[campaign].Positions.Length) {
			Console.Error.WriteLine($"The battle set has no campaign {campaign} position {position}: no mission is loaded.");
			return null;
		}

		var names = set.Campaigns[campaign].Positions[position].Names;
		int index = names.Length == 1 ? 0 : outcome;
		if (index < 0 || index >= names.Length) {
			Console.Error.WriteLine($"Campaign {campaign} position {position} has no name {index}: no mission is loaded.");
			return null;
		}

		string name = names[index];
		if (name.Contains(".MSN", StringComparison.Ordinal)) {
			return name;
		}

		Es1NameList? list;
		try {
			list = _content.Read("bst", name) is { } bytes ? new Es1NameListTransformer().Parse(bytes) : null;
		} catch (InvalidDataException ex) {
			Console.Error.WriteLine($"bst\\{name} does not read ({ex.Message}): no mission is loaded.");
			return null;
		}

		if (list == null || _data.Random?.PickRandom(list.Names) is not { } drawn) {
			Console.Error.WriteLine($"bst\\{name} is missing or names no mission: no mission is loaded.");
			return null;
		}

		names[index] = drawn;
		return drawn;
	}

	/// <summary>
	/// <c>Inventory_CopyThroughScratch</c> (<c>00027cb3</c>)(<paramref name="from"/>, <paramref name="to"/>): the stock
	/// <paramref name="from"/> written as <c>car\scratch.arm</c> and read into the stock <paramref name="to"/>, which reverses
	/// each slot's words; the file is deleted after. Here the write and the read are made in memory, to the same result, and
	/// no file is written.
	/// </summary>
	private void CopyStock(int from, int to) => Stocks[to].Read(Stocks[from].ToFile());

	/// <summary>A record's <c>memmove</c>: a copy of its <c>0x1f7</c> bytes.</summary>
	private static Es1CareerRecord CopyRecord(Es1CareerRecord record) {
		var transformer = new Es1CareerRecordTransformer();
		return transformer.Parse(transformer.Write(record))!;
	}

	/// <summary><c>Hangar_Write</c> (<c>00029107</c>) of <paramref name="hangar"/> in the mode: a single mission's form in mode 0, a career's otherwise.</summary>
	private Es1HercList HangarWrite(Es1Hangar hangar) => _mission.Mode == 0 ? hangar.SingleMissionSave() : hangar.CareerSave();

	/// <summary>
	/// <c>HercList_Copy</c> (<c>000292da</c>)(dest, src): the header, the count and each HERC's slot as read, each HERC read
	/// and written again with <c>Herc_Write</c>, which changes its mount words first (<see cref="Es1HercRecord.PrepareWrite"/>).
	/// </summary>
	private static Es1HercList CopyHercList(Es1HercList list) => new() {
		SlotCount = list.SlotCount, Word1 = list.Word1, MarkedSlot = list.MarkedSlot, MarkedSlotWord = list.MarkedSlotWord, Dword = list.Dword,
		Hercs = list.Hercs.Select(entry => {
			var herc = entry.Herc.Clone();
			herc.PrepareWrite();
			return new Es1HercListEntry { Slot = entry.Slot, Herc = herc };
		}).ToArray(),
	};

	/// <summary>
	/// <c>Hangar_ReadFromSave</c> (<c>00028bd6</c>)(<paramref name="list"/>, the hangar file): the file's lists before
	/// <paramref name="list"/> passed over (<c>HercList_Skip</c>) and the next read into a hangar (<see cref="Es1Hangar.Read"/>).
	/// Retail reads whatever a file that does not open gives; here such a file, or a list outside the 41, gives an empty
	/// hangar, reported, an engine guard.
	/// </summary>
	private Es1Hangar ReadHangar(short list) {
		if (LogEntry is { } entry && ReadHangarFile(HangarFilePath(entry), report: true) is { } file && list is >= 0 and < RecordCount) {
			return Es1Hangar.Read(file.Lists[list]);
		}

		return Es1Hangar.Read(new Es1HercList());
	}

	private static Es1CareerHangarFile? ReadHangarFile(string path, bool report) {
		try {
			return new Es1CareerHangarFileTransformer().Parse(File.ReadAllBytes(path));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
			if (report) {
				Console.Error.WriteLine($"{path} does not read ({ex.Message}): the hangar is left empty.");
			}

			return null;
		}
	}

	/// <summary>The career file's path in the install, the log entry's <c>+0x42</c>.</summary>
	private string CareerFilePath(Es1CareerLogEntry entry) => Es1ShellData.InstallPath(_installRoot, Es1CareerLogEntry.Text(entry.CareerFile));

	/// <summary>The hangar file's path in the install, the log entry's <c>+0x53</c>.</summary>
	private string HangarFilePath(Es1CareerLogEntry entry) => Es1ShellData.InstallPath(_installRoot, Es1CareerLogEntry.Text(entry.HangarFile));

	/// <summary>The file-name table's entry <paramref name="entry"/> in the install; none, reported, without the table.</summary>
	private string? DataFile(int entry) {
		if (_data.FileNames is { } names && entry < names.Length) {
			return Es1ShellData.InstallPath(_installRoot, names[entry]);
		}

		Console.Error.WriteLine($"cfg\\filename.bin has no entry {entry}: its file is not written.");
		return null;
	}

	/// <summary>
	/// <c>Inventory_LoadFile</c> (<c>00027e02</c>)(the stock the dword <c>000aad62</c> points to, <c>car\</c><paramref name="name"/>)
	/// (docs/es1/formats/car-hercs-and-stock.md#the-weapon-stock): the file read into stock 0 (<see cref="Es1WeaponStock.Read"/>).
	/// A file that is missing or does not read is reported and read as no records, so the stock is left empty, an engine guard.
	/// </summary>
	public void LoadStock(string name) =>
		Stocks[0].Read(ReadShellFile(Es1SingleMission.CarFolder, name, new Es1InventoryTransformer()) ?? new Es1Inventory());

	/// <summary>A starting file from the shell's archives through <paramref name="transformer"/>; none, reported, when it is missing or does not read.</summary>
	private T? ReadShellFile<T>(string folder, string name, HercWorks.Core.Io.Transform.ByteTransformer<T> transformer) where T : class {
		try {
			if (_content.Read(folder, name) is { } bytes && transformer.Parse(bytes) is { } file) {
				return file;
			}
		} catch (InvalidDataException ex) {
			Console.Error.WriteLine($"{folder}\\{name} does not read ({ex.Message}).");
			return null;
		}

		Console.Error.WriteLine($"{folder}\\{name} is missing.");
		return null;
	}

	/// <summary>
	/// <paramref name="bytes"/> written to <paramref name="path"/>: truncated as a <c>FileWStream</c> writes, or over the
	/// file from its start with its tail kept as a <c>FileRWStream</c> writes. A file that does not open is reported with
	/// the assert retail makes there, an engine guard.
	/// </summary>
	private static void WriteFile(string path, byte[] bytes, bool truncate, string assert) {
		try {
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			using var stream = new FileStream(path, truncate ? FileMode.Create : FileMode.OpenOrCreate, FileAccess.Write);
			stream.Write(bytes);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"{path} does not open for writing ({ex.Message}; {assert} asserts here).");
		}
	}

	/// <summary><c>remove</c>: a file that is not there, or will not go, is left, as <c>remove</c>'s failure is ignored.</summary>
	private static void TryDelete(string path) {
		try {
			File.Delete(path);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"{path} is not deleted ({ex.Message}).");
		}
	}
}
