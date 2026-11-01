using System.Text;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Shell;

/// <summary>
/// The mission the shell loads for the simulator: <c>mission.cpp</c>'s globals, the mode <c>Shell_Mode</c>, the
/// mission's kind <c>Mission_Kind</c>, the mission's path and its text's path, the single mission's record, the career's
/// roster and the hangar; and the shell's steps over them, <c>SingleMission_Start</c>, a career's <c>MsnGen_LoadMission</c>,
/// <c>Mission_WriteRestore</c>, <c>Mission_ReadRestore</c>, <c>Shell_PrepareMission</c> and the war map's tests and player file
/// (docs/es1/shell/main-menu.md#the-buttons, docs/es1/shell/single-mission.md#starting-the-mission,
/// docs/es1/formats/eng-mission-text.md#which-file). The class is this engine's bundle of those globals, handed to the
/// menus through <see cref="Menus.Es1MenuContext"/>; every member is retail's.
/// </summary>
public sealed class Es1ShellMission {
	/// <summary><c>data\restore.dat</c>, entry 8 of <c>cfg\filename.bin</c>.</summary>
	public const string RestoreName = "restore.dat";

	private readonly string _installRoot;
	private readonly GameContent _content;
	private readonly Es1ShellData _data;

	/// <param name="installRoot">The install whose <c>data\</c> the load writes into.</param>
	/// <param name="content">The shell's archives, which the mission and its text are read from.</param>
	/// <param name="data">The shared data: the generator, the single missions' lists and the MISSION PARAMS words.</param>
	public Es1ShellMission(string installRoot, GameContent content, Es1ShellData data) {
		_installRoot = installRoot;
		_content = content;
		_data = data;
		Roster = new Es1CareerRoster(installRoot, content, data, this);
	}

	/// <summary>The install whose <c>data\</c> and <c>car\</c> the shell's files are written into.</summary>
	public string InstallRoot => _installRoot;

	/// <summary>The roster <c>Career_Roster</c> (<c>0009b218</c>), the loaded career and its backups (<see cref="Es1CareerRoster"/>).</summary>
	public Es1CareerRoster Roster { get; }

	/// <summary>
	/// <c>CareerRecord_Current</c> (<c>00014ef4</c>) (docs/es1/formats/bst-battle-sets.md#the-career-record): in
	/// <see cref="Mode"/> 0 <see cref="Record"/>, otherwise the roster's record its word <c>+7</c> names
	/// (<see cref="Es1CareerRoster.CurrentRecord"/>).
	/// </summary>
	public Es1CareerRecord? CurrentRecord => Mode == 0 ? Record : Roster.CurrentRecord;

	/// <summary>
	/// <c>Shell_Mode</c> (<c>000aad56</c>), 0 in the image: 0 a single mission, 1 a career, which decides how a mission
	/// loads (docs/es1/formats/msn-mission-file.md#the-mode-and-the-mission-parameters).
	/// </summary>
	public short Mode { get; set; }

	/// <summary>
	/// <c>Mission_Kind</c> (<c>000aad5a</c>), an <c>int16</c>, 0 in the image: the single mission's list, the SINGLE
	/// MISSION menu's line, −1 for one drawn at random, which a load that parses replaces with the mission's header word 3
	/// (docs/es1/shell/single-mission.md#the-list).
	/// </summary>
	public short MissionKind { get; set; }

	/// <summary>
	/// The loaded mission's header word 4, <c>000ab222</c>, as its header patches leave it: the line of <c>C0_text.bin</c> a
	/// single mission's opening message shows (docs/es1/shell/message-box.md#the-kinds); 0 in the image.
	/// </summary>
	public short HeaderWord4 { get; private set; }

	/// <summary>
	/// <c>MessageBox_Line</c> (<c>000aad5c</c>), the line of its text file a message shows and of its lip-sync list the talking
	/// head speaks: <c>MsnGen_LoadMission</c> copies header word 4 into it, and <c>DigitalDialog_ShowMessage</c> sets it by
	/// the message's kind (docs/es1/shell/message-box.md#showing-a-message).
	/// </summary>
	public short MessageBoxLine { get; set; }

	/// <summary>
	/// The word <c>000aad58</c>, the first word of <c>data\results.dat</c>, the debriefing's ordinal, which
	/// <c>Mission_OutcomeGrade</c> grades for kind 3's line; 0 in the image. Set by the results' read
	/// (<see cref="Es1ShellResults.Process"/>), zeroed by <c>CareerRecord_Init</c>, the load's
	/// (<see cref="StartSingleMission"/>) and a new career's (<see cref="Es1CareerRoster.New"/>), and set to flag 0 by
	/// <c>Roster_ActivateRecord</c> (<see cref="Es1CareerRoster.ActivateRecord"/>).
	/// </summary>
	public short Outcome { get; set; }

	/// <summary>
	/// The word <c>000aad5e</c>, which <c>Mission_ReadRestore</c> (<see cref="ReadRestore"/>) reads from the word
	/// <c>data\restore.dat</c> holds from <see cref="MissionKind"/>: the kind <c>Mission_OutcomeGrade</c> picks its table
	/// by, and <c>Career_OutcomeChoice</c> its threshold; 0 in the image. <c>Roster_ActivateRecord</c> sets it to the
	/// career record's <c>+0xc</c> (<see cref="Es1CareerRoster.ActivateRecord"/>).
	/// </summary>
	public short RestoredMissionKind { get; set; }

	/// <summary>
	/// The 100 <c>int16</c> flags at <c>000ab244</c>, which the results' read loads from <c>data\mission.var</c> with flag 0
	/// the outcome (docs/es1/shell/between-missions.md#after-a-mission), a career's record sets and saves
	/// (<see cref="Es1CareerRoster"/>), and a mission's load seeds (docs/es1/formats/msn-mission-file.md#the-flags); 0 in
	/// the image. The load (<see cref="Es1MissionLoad"/>) seeds a copy, which is copied back here after it.
	/// </summary>
	public short[] Flags { get; } = new short[Es1MissionLoad.FlagCount];

	/// <summary>
	/// <c>Squad_MemberCount</c> (<c>0007ebc8</c>), the loaded mission's first group's members, which a mission's load
	/// counts and <c>CareerRecord_MarkTopPilots</c> reads (docs/es1/shell/career.md#a-careers-next-mission); 0 in the image.
	/// </summary>
	public int SquadMemberCount { get; private set; }

	/// <summary>
	/// What the results' read keeps for the debriefing (<see cref="Es1ShellResults"/>): the salvage, the salvage pairs, the
	/// three totals and the lost pilots' count.
	/// </summary>
	public Es1ShellResults Results { get; } = new();

	/// <summary>The mission's path <c>000aad14</c>, <c>msn\&lt;name&gt;</c>; empty in the image.</summary>
	public string Path { get; private set; } = "";

	/// <summary>The mission's text's path <c>000aad2a</c>, <see cref="Path"/> with <c>.eng</c> from its first <c>.</c>; empty in the image.</summary>
	public string TextPath { get; private set; } = "";

	/// <summary>
	/// <c>SingleMission_Record</c> (<c>0009b014</c>), <c>CareerRecord_Current</c> (<c>00014ef4</c>) in <see cref="Mode"/> 0
	/// (docs/es1/formats/bst-battle-sets.md#the-career-record): set up by a single mission's load
	/// (<see cref="StartSingleMission"/>) and read back from <c>car\sm_play.gam</c> after the mission
	/// (<see cref="Es1ShellResults.Process"/>). Retail's is static data, zero in the image; here none until the first.
	/// </summary>
	public Es1CareerRecord? Record { get; set; }

	/// <summary>
	/// The hangar (<see cref="Es1Hangar"/>): the one a single mission's load leaves, after the mission the one read back
	/// from <c>car\sm_play.mec</c>, and a career's, which the roster reads and writes (<see cref="Es1CareerRoster"/>). None
	/// before any, and after a load that fails, which the war map's tests read as an
	/// engine guard (<see cref="PlayerHasNoHerc"/>).
	/// </summary>
	public Es1Hangar? Hangar { get; set; }

	/// <summary>
	/// <c>SingleMission_Start</c> (<c>00036807</c>) (docs/es1/shell/single-mission.md#starting-the-mission): the weapon
	/// stock loaded from <c>car\s_armory.dat</c> (<see cref="Es1CareerRoster.LoadStock"/>); the path built
	/// from <see cref="MissionKind"/>; the mission loaded in the mode-0 load, which writes <c>data\script.dat</c>,
	/// <c>data\mission.var</c> and <c>data\player.mec</c> (<see cref="Es1SingleMission"/>); and the paths saved with
	/// <see cref="WriteRestore"/>. A load that fails is reported and the restore still written, as retail writes it after
	/// a mission that does not parse; a path that is not built (no lists, or a list of no names, where retail reads past
	/// its arrays) loads nothing, an engine guard.
	/// </summary>
	public void StartSingleMission() {
		Roster.LoadStock("S_ARMORY.DAT");
		if (BuildSingleMissionPath(MissionKind) is { } name && _data.Random is { } random) {
			var p = _data.MissionParams;
			var missionParams = new Es1MissionParams(p[0], p[1], p[2], p[3], p[4]);
			if (Es1SingleMission.Start(_installRoot, _content, name, missionParams, random, Record, out string? error) is { } result) {
				// MsnGen_LoadMission stores header words 3 and 4 in 000aad5a and 000aad5c, and keeps the header, 000ab222 its word 4.
				KeepLoad(result);
				// CareerRecord_Init zeroes the outcome word 000aad58 with the record.
				Outcome = 0;
				Record = result.Record;
				Hangar = result.Hangar;
			} else {
				Hangar = null;
				Console.Error.WriteLine(error);
			}
		}

		WriteRestore();
	}

	/// <summary>
	/// <c>SingleMission_BuildPath</c> (<c>00036649</c>)(<paramref name="index"/>): the list <paramref name="index"/> of
	/// <see cref="Es1ShellData.SingleMissions"/>, or for −1 the list <c>Math_RandomBelow</c>(the count) draws; a name drawn
	/// from it (<see cref="Es1ShellRandom.PickRandom"/>) as <c>msn\%s</c> into <see cref="Path"/>; and
	/// <see cref="TextPath"/> set. Returns the name, or null when there is no list or name to take, where retail reads past
	/// its arrays, an engine guard that leaves both paths as they were.
	/// </summary>
	public string? BuildSingleMissionPath(short index) {
		if (_data.Random is not { } random) {
			return null;
		}

		var lists = _data.SingleMissions;
		short list = index == -1 ? (short)random.Below((short)(lists?.Count ?? 0)) : index;
		if (lists == null || list < 0 || list >= lists.Count) {
			Console.Error.WriteLine($"No single-mission list {list}: msn\\sing_msn.dat holds {lists?.Count ?? 0}; no mission is loaded.");
			return null;
		}

		if (random.PickRandom(lists[list].Names) is not { } name) {
			Console.Error.WriteLine($"Single-mission list {list} names no mission; no mission is loaded.");
			return null;
		}

		SetPath(name);
		return name;
	}

	/// <summary>
	/// The path both modes' builds make: <paramref name="name"/> formatted as <c>msn\%s</c> into <see cref="Path"/>, and
	/// <see cref="TextPath"/> set with <c>Mission_SetTextPath</c>.
	/// </summary>
	public void SetPath(string name) {
		Path = $"{Es1MissionFiles.MissionFolder}\\{name}";
		SetTextPath();
	}

	/// <summary>
	/// What <c>MsnGen_LoadMission</c> leaves in the shell's globals in either mode: header words 3 and 4 in
	/// <see cref="MissionKind"/> (<c>000aad5a</c>) and <see cref="MessageBoxLine"/> (<c>000aad5c</c>), the header kept, its
	/// word 4 <see cref="HeaderWord4"/> (<c>000ab222</c>); the flags it seeded in <see cref="Flags"/>; and
	/// <see cref="SquadMemberCount"/>.
	/// </summary>
	private void KeepLoad(Es1MissionLoadResult result) {
		MissionKind = result.Script.Header[3];
		HeaderWord4 = result.Script.Header[4];
		MessageBoxLine = HeaderWord4;
		Array.Copy(result.Flags, Flags, Math.Min(result.Flags.Length, Flags.Length));
		SquadMemberCount = result.SquadMemberCount;
	}

	/// <summary>
	/// <c>MsnGen_LoadMission</c> (<c>00035873</c>)(<see cref="Path"/>) in <see cref="Mode"/> 1, a career's
	/// (docs/es1/formats/msn-mission-file.md#loading-a-mission): the mission <paramref name="name"/> read from <c>msn\</c>
	/// and loaded on <paramref name="record"/>, the roster's current record, and <see cref="Hangar"/>, the career's
	/// (<see cref="Es1MissionLoad.LoadCareer"/>), with <see cref="Flags"/> as they stand, TIME OF DAY from
	/// <see cref="Es1ShellData.MissionParams"/> and the roster's careers-log entry's word <c>+0x40</c>;
	/// <c>data\mission.var</c> and <c>data\script.dat</c> written (<see cref="Es1SingleMission.WriteDataFiles"/>); the load's
	/// globals kept (<see cref="KeepLoad"/>); and <c>PlayerMec_Write</c>'s files, <c>data\player.mec</c> and the career's
	/// <c>Career_Save</c> (<see cref="WriteCareerPlayerFiles"/>). No <c>car\sm_play.*</c> file is written. Retail reads
	/// through a null careers-log entry, asserts when <c>data\script.dat</c> does not open and reads what a missing mission
	/// gives; here a missing entry's word reads 0, and a mission that is missing or does not load, no generator or no
	/// hangar is reported and loads nothing, engine guards.
	/// </summary>
	public void LoadCareerMission(string name, Es1CareerRecord record) {
		if (_data.Random is not { } random || Hangar is not { } hangar) {
			Console.Error.WriteLine($"No generator or no career hangar: {name} is not loaded.");
			return;
		}

		if (Es1SingleMission.ReadMission(_installRoot, _content, name, out string? error) is not { } file) {
			Console.Error.WriteLine(error);
			return;
		}

		Es1MissionLoadResult result;
		try {
			result = Es1MissionLoad.LoadCareer(file, Flags, _data.MissionParams[4], record, hangar, Roster.LogEntry?.Word ?? 0, random.Below);
		} catch (NotSupportedException ex) {
			Console.Error.WriteLine($"Cannot load {name}: {ex.Message}");
			return;
		}

		try {
			Es1SingleMission.WriteDataFiles(_installRoot, result);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"data\\{Es1MissionFiles.ScriptName} or data\\{Es1MissionFiles.MissionVarName} does not open for writing ({ex.Message}; mg.cpp line 0x4c5 or 0x505 asserts here).");
		}

		KeepLoad(result);
		WriteCareerPlayerFiles(result.Player);
	}

	/// <summary>
	/// <c>PlayerMec_Write</c> (<c>0002299e</c>)'s files in <see cref="Mode"/> 1: <c>data\player.mec</c>
	/// (<see cref="Es1SingleMission.WritePlayerFile"/>), then <c>CareerRecord_SaveCurrent</c> (<c>00014f28</c>)'s
	/// <c>Career_Save</c>(roster, its word <c>+7</c>) (<see cref="Es1CareerRoster.Save"/>). A player file that does not
	/// open is reported and the save still made, an engine guard.
	/// </summary>
	private void WriteCareerPlayerFiles(Es1PlayerMec player) {
		try {
			Es1SingleMission.WritePlayerFile(_installRoot, player);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"data\\{Es1MissionFiles.PlayerName} does not open for writing ({ex.Message}).");
		}

		Roster.Save(Roster.Current);
	}

	/// <summary>
	/// <c>CareerRecord_SaveCurrent</c> (<c>00014f28</c>): in <see cref="Mode"/> 0 <c>SingleMission_SaveRecord</c>
	/// (<see cref="Es1SingleMission.SaveRecord"/>) of <see cref="Record"/>, otherwise <c>Career_Save</c>(roster, its word
	/// <c>+7</c>) (<see cref="Es1CareerRoster.Save"/>). With no single-mission record nothing is written, and a record file
	/// that does not open is reported, engine guards.
	/// </summary>
	public void SaveCurrentRecord() {
		if (Mode != 0) {
			Roster.Save(Roster.Current);
			return;
		}

		if (Record is not { } record) {
			return;
		}

		try {
			Es1SingleMission.SaveRecord(_installRoot, record);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"car\\{Es1SingleMission.SavedRecordName} does not open for writing ({ex.Message}; career.cpp line 0x58 asserts here).");
		}
	}

	/// <summary>
	/// <c>Mission_SetTextPath</c> (<c>000364be</c>) (docs/es1/formats/eng-mission-text.md#which-file): <see cref="Path"/>
	/// cut at its first <c>.</c> with <c>.eng</c> after it. Retail writes through a null pointer for a path with no
	/// <c>.</c>; here <c>.eng</c> goes after the whole path, an engine guard.
	/// </summary>
	private void SetTextPath() {
		int dot = Path.IndexOf('.');
		TextPath = (dot < 0 ? Path : Path[..dot]) + ".eng";
	}

	/// <summary>
	/// <c>Mission_WriteRestore</c> (<c>0003634b</c>) (docs/es1/formats/eng-mission-text.md#which-file): <c>data\restore.dat</c>
	/// written with <see cref="Mode"/>, <see cref="MissionKind"/> and the two paths, each an <c>int16</c> length
	/// counting the NUL and the path with its NUL, through the <c>FileRWStream</c> that does not truncate, so a shorter file
	/// leaves a longer one's tail. Retail asserts at severity 5 at <c>mission.cpp</c> line <c>0x55</c> when the file does
	/// not open; here that is reported, an engine guard.
	/// </summary>
	public void WriteRestore() {
		using var bytes = new MemoryStream();
		using (var writer = new BinaryWriter(bytes, Encoding.Latin1, leaveOpen: true)) {
			writer.Write(Mode);
			writer.Write(MissionKind);
			foreach (string path in new[] { Path, TextPath }) {
				byte[] text = Encoding.Latin1.GetBytes(path);
				writer.Write((short)(text.Length + 1));
				writer.Write(text);
				writer.Write((byte)0);
			}
		}

		string file = Es1MissionFiles.DataPath(_installRoot, RestoreName);
		try {
			Directory.CreateDirectory(CaseInsensitivePath.Combine(_installRoot, Es1MissionFiles.DataFolder));
			using var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write);
			stream.Write(bytes.ToArray());
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"data\\{RestoreName} does not open for writing ({ex.Message}; mission.cpp line 0x55 asserts here).");
		}
	}

	/// <summary>
	/// <c>Mission_ReadRestore</c> (<c>00036512</c>) (docs/es1/formats/eng-mission-text.md#which-file): <c>data\restore.dat</c>
	/// read as <see cref="WriteRestore"/> writes it: a word into <see cref="Mode"/>, a word into
	/// <see cref="RestoredMissionKind"/>, then each path as an <c>int16</c> length and that many bytes into
	/// <see cref="Path"/> and <see cref="TextPath"/>, each taken up to its NUL. Retail asserts at severity 5 at
	/// <c>mission.cpp</c> line <c>0x75</c> when the file does not open, and reads what the stream gives past its end; here a
	/// file that does not open is reported and changes nothing, and a field past its end reads as 0 or empty, engine guards.
	/// </summary>
	public void ReadRestore() {
		byte[] bytes;
		try {
			bytes = File.ReadAllBytes(Es1MissionFiles.DataPath(_installRoot, RestoreName));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"data\\{RestoreName} does not open ({ex.Message}; mission.cpp line 0x75 asserts here).");
			return;
		}

		var reader = new Es1ShellResults.Cursor(bytes);
		Mode = reader.Int16();
		RestoredMissionKind = reader.Int16();
		Path = ReadPath(reader);
		TextPath = ReadPath(reader);
	}

	/// <summary>
	/// <c>Shell_ProcessMissionResults</c> (<c>00038eb5</c>) over the install's files, in either <see cref="Mode"/>
	/// (<see cref="Es1ShellResults.Process"/>).
	/// </summary>
	public void ProcessResults() => Results.Process(this, _installRoot, _data);

	/// <summary>One of <see cref="ReadRestore"/>'s paths: its length, then that many bytes, the text up to the first NUL.</summary>
	private static string ReadPath(Es1ShellResults.Cursor reader) {
		byte[] text = reader.Bytes(reader.Int16());
		int nul = Array.IndexOf(text, (byte)0);
		return Encoding.Latin1.GetString(text, 0, nul < 0 ? text.Length : nul);
	}

	/// <summary>
	/// <c>Shell_PrepareMission</c> (<c>0002a523</c>) (docs/es1/shell/main-menu.md#the-buttons), after every mission load
	/// the shell's menus make: the mission map deleted (<see cref="Es1ShellScreens.DeleteMissionMap"/>), the objectives
	/// written (<see cref="WriteObjectives"/>), and the mission map made again.
	/// </summary>
	public void Prepare(Es1ShellScreens screens) {
		screens.DeleteMissionMap();
		WriteObjectives();
		// PLACEHOLDER: maybe_MouseEventQueue_Start (00010559), the byte 0007e5df cleared and Shell_MissionMap (000809a4) made
		// with 000094d9(0, 000a02d4): the briefing's mission map, S5's. Until it lands an object with no behaviour stands for
		// it, so that what tests the map set (a back to the war map, Es1ShellScreens.HandleMessage's step 3) sees it set.
		screens.MissionMap = new object();
	}

	/// <summary>
	/// <c>MissionVar_ReadFlag</c> (<c>0003628d</c>)(<paramref name="index"/>) (docs/es1/formats/eng-mission-text.md#reading-a-block):
	/// the <c>int16</c> at twice the index in <c>data\mission.var</c>, which it opens for each read. Retail asserts at
	/// <c>mission.cpp</c> line <c>0x4b</c> when the file does not open and reads past its end as the stream gives; here
	/// either reads 0, reported for a file that does not open, an engine guard.
	/// </summary>
	public short ReadFlag(short index) {
		byte[] bytes;
		try {
			bytes = File.ReadAllBytes(Es1MissionFiles.DataPath(_installRoot, Es1MissionFiles.MissionVarName));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"data\\{Es1MissionFiles.MissionVarName} does not open ({ex.Message}; mission.cpp line 0x4b asserts here).");
			return 0;
		}

		int at = index * 2;
		return at >= 0 && at + 1 < bytes.Length ? (short)(bytes[at] | bytes[at + 1] << 8) : (short)0;
	}

	/// <summary>
	/// <c>CareerRecord_PlayerHasNoHerc</c> (<c>000161dc</c>)(<c>CareerRecord_Current</c>)
	/// (docs/es1/shell/war-map.md#begin-mission), on <see cref="CurrentRecord"/>. With no <see cref="Hangar"/>, after a load
	/// that failed, the answer is true, an engine guard that keeps the shell from leaving for a mission with no file.
	/// </summary>
	public bool PlayerHasNoHerc() => CurrentRecord is not { } record || Hangar == null || record.PlayerHasNoHerc();

	/// <summary>
	/// <c>CareerRecord_AnyPilotUnarmed</c> (<c>0001620d</c>)(<c>CareerRecord_Current</c>)
	/// (docs/es1/shell/war-map.md#begin-mission), on <see cref="CurrentRecord"/> and <see cref="Hangar"/>; false with
	/// neither, an engine guard.
	/// </summary>
	public bool AnyPilotUnarmed() => CurrentRecord is { } record && Hangar is { } hangar && record.AnyPilotUnarmed(hangar);

	/// <summary>
	/// <c>CareerRecord_PlayerHercDisabled</c> (<c>0001626d</c>)(<c>CareerRecord_Current</c>)
	/// (docs/es1/shell/between-missions.md#next-mission), on <see cref="CurrentRecord"/> and <see cref="Hangar"/>; false with
	/// neither, an engine guard.
	/// </summary>
	public bool PlayerHercDisabled() => CurrentRecord is { } record && Hangar is { } hangar && record.PlayerHercDisabled(hangar);

	/// <summary>
	/// <c>CareerRecord_AnyHercDisabled</c> (<c>00016294</c>)(<c>CareerRecord_Current</c>)
	/// (docs/es1/shell/between-missions.md#next-mission), on <see cref="CurrentRecord"/> and <see cref="Hangar"/>; false with
	/// neither, an engine guard.
	/// </summary>
	public bool AnyHercDisabled() => CurrentRecord is { } record && Hangar is { } hangar && record.AnyHercDisabled(hangar);

	/// <summary>
	/// <c>PlayerMec_Write</c> (<c>0002299e</c>), BEGIN MISSION >'s
	/// (docs/es1/formats/career-log-and-player-mec.md#the-player-file), on <see cref="CurrentRecord"/> and
	/// <see cref="Hangar"/>. In <see cref="Mode"/> 0 <c>data\player.mec</c> is written again with the load's writer
	/// (<see cref="Es1MissionLoad.WritePlayerMec"/>), the player's first word drawn again on <c>Shell_RandomState</c>, and
	/// <c>car\sm_play.gam</c> after it (<see cref="Es1SingleMission.WritePlayerMec"/>); in mode 1 with the career's writer
	/// (<see cref="Es1MissionLoad.WriteCareerPlayerMec"/>), then <c>Career_Save</c> (<see cref="WriteCareerPlayerFiles"/>).
	/// Nothing is written with no <see cref="Hangar"/>, a draw with no generator is 0, and a missing careers-log entry's
	/// word reads 0, engine guards.
	/// </summary>
	public void WritePlayerMec() {
		if (CurrentRecord is not { } record || Hangar is not { } hangar) {
			return;
		}

		if (Mode != 0) {
			WriteCareerPlayerFiles(Es1MissionLoad.WriteCareerPlayerMec(record, hangar, Roster.LogEntry?.Word ?? 0));
			return;
		}

		var random = _data.Random;
		var player = Es1MissionLoad.WritePlayerMec(record, hangar, bound => random?.Below(bound) ?? 0);
		try {
			Es1SingleMission.WritePlayerMec(_installRoot, player, record);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"data\\{Es1MissionFiles.PlayerName} or car\\{Es1SingleMission.SavedRecordName} does not open for writing ({ex.Message}).");
		}
	}

	/// <summary>
	/// <c>Mission_WriteObjectives</c> (<c>00018824</c>)(<see cref="TextPath"/>)
	/// (docs/es1/formats/eng-mission-text.md#the-objectives): the text's first <c>OBJ:</c> block written to
	/// <c>data\object.str</c> as a string table of one group, one entry per piece with no attributes; with no such block
	/// nothing is written and the old file stays. Retail reads every piece as a literal, and the retail blocks hold only
	/// literals; a choice is passed over here. A text that is missing or does not read is reported and writes nothing,
	/// and a file that does not open, where retail asserts at <c>dialog.cpp</c> line <c>0x56</c>, is reported; both engine
	/// guards.
	/// </summary>
	public void WriteObjectives() {
		int slash = TextPath.LastIndexOf('\\');
		byte[]? bytes = slash < 0 ? null : _content.Read(TextPath[..slash], TextPath[(slash + 1)..]);
		if (bytes == null) {
			Console.Error.WriteLine($"{TextPath} is missing: data\\{Es1ObjectivesPanel.ObjectivesName} is not written.");
			return;
		}

		Es1MissionText? text;
		try {
			text = new Es1MissionTextTransformer().Parse(bytes);
		} catch (InvalidDataException ex) {
			Console.Error.WriteLine($"{TextPath} does not read ({ex.Message}): data\\{Es1ObjectivesPanel.ObjectivesName} is not written.");
			return;
		}

		if (text?.Find(Es1MissionText.ObjectivesTag, 0) is not { } block) {
			return;
		}

		var entries = block.Pieces.OfType<Es1TextLiteral>().Select(literal => new StringFile.Entry(literal.Text, [])).ToArray();
		string file = Es1MissionFiles.DataPath(_installRoot, Es1ObjectivesPanel.ObjectivesName);
		try {
			File.Delete(file);
			File.WriteAllBytes(file, new StringFileTransformer().Write(new StringFile { Groups = [entries] })!);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"data\\{Es1ObjectivesPanel.ObjectivesName} does not open for writing ({ex.Message}; dialog.cpp line 0x56 asserts here).");
		}
	}
}
