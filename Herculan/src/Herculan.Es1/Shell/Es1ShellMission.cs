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
/// mission's kind <c>Mission_Kind</c>, the mission's path and its text's path; and the menu handlers' steps over them,
/// <c>SingleMission_Start</c>, <c>Mission_WriteRestore</c> and <c>Shell_PrepareMission</c>
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
	}

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
	/// <c>Mission_OutcomeGrade</c> grades for kind 3's line; 0 in the image. Set by the results' read, S2's
	/// <c>Shell_ProcessMissionResults</c>, which no ported code runs.
	/// </summary>
	public short Outcome { get; set; }

	/// <summary>
	/// The word <c>000aad5e</c>, which <c>Mission_ReadRestore</c> reads from the word <c>data\restore.dat</c> holds from
	/// <see cref="MissionKind"/>: the kind <c>Mission_OutcomeGrade</c> picks its table by; 0 in the image. Set by the
	/// restore's read, which no ported code runs.
	/// </summary>
	public short RestoredMissionKind { get; set; }

	/// <summary>The mission's path <c>000aad14</c>, <c>msn\&lt;name&gt;</c>; empty in the image.</summary>
	public string Path { get; private set; } = "";

	/// <summary>The mission's text's path <c>000aad2a</c>, <see cref="Path"/> with <c>.eng</c> from its first <c>.</c>; empty in the image.</summary>
	public string TextPath { get; private set; } = "";

	/// <summary>
	/// What this engine keeps of <c>SingleMission_Record</c> (<c>0009b014</c>) and the hangar after a single mission's
	/// load: the <c>data\player.mec</c> it wrote, which holds the squad's entries from the player's on, each with its
	/// slot's HERC (docs/es1/formats/career-log-and-player-mec.md#a-single-missions-file); none before a load and after one
	/// that fails. The load builds the record and the hangar inside <see cref="Es1MissionLoad"/> and keeps neither; the war
	/// map's tests and its second write read this instead (<see cref="PlayerHasNoHerc"/>, <see cref="AnyPilotUnarmed"/>,
	/// <see cref="WritePlayerMec"/>), which holds while no ported screen changes the squad between the load and BEGIN
	/// MISSION >. PLACEHOLDER: the arming screens, S4's, change the hangar's HERCs; with them the record and the hangar
	/// are to be kept in its place.
	/// </summary>
	public Es1PlayerMec? Player { get; private set; }

	/// <summary>
	/// <c>SingleMission_Start</c> (<c>00036807</c>) (docs/es1/shell/single-mission.md#starting-the-mission): the path built
	/// from <see cref="MissionKind"/>; the mission loaded in the mode-0 load, which writes <c>data\script.dat</c>,
	/// <c>data\mission.var</c> and <c>data\player.mec</c> (<see cref="Es1SingleMission"/>); and the paths saved with
	/// <see cref="WriteRestore"/>. A load that fails is reported and the restore still written, as retail writes it after
	/// a mission that does not parse; a path that is not built (no lists, or a list of no names, where retail reads past
	/// its arrays) loads nothing, an engine guard.
	/// </summary>
	public void StartSingleMission() {
		// PLACEHOLDER: Inventory_LoadFile (00027e02)(the stock 000aad62, car\s_armory.dat), the weapon stock, which the arming
		// screen and the results read (S4's and S2's, docs/es1/formats/car-hercs-and-stock.md#the-weapon-stock); not loaded.
		if (BuildSingleMissionPath(MissionKind) is { } name && _data.Random is { } random) {
			var p = _data.MissionParams;
			var missionParams = new Es1MissionParams(p[0], p[1], p[2], p[3], p[4]);
			if (Es1SingleMission.Start(_installRoot, _content, name, missionParams, random, out string? error) is { } result) {
				// MsnGen_LoadMission stores header words 3 and 4 in 000aad5a and 000aad5c, and keeps the header, 000ab222 its word 4.
				MissionKind = result.Script.Header[3];
				HeaderWord4 = result.Script.Header[4];
				MessageBoxLine = HeaderWord4;
				Player = result.Player;
			} else {
				Player = null;
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

		Path = $"{Es1MissionFiles.MissionFolder}\\{name}";
		SetTextPath();
		return name;
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
	/// <c>CareerRecord_PlayerHasNoHerc</c> (<c>000161dc</c>)(<c>CareerRecord_Current</c>) in <see cref="Mode"/> 0
	/// (docs/es1/shell/war-map.md#begin-mission): the player's entry has no hangar slot. Read from <see cref="Player"/>, the
	/// player's entry, written first, carrying no HERC; that also holds for a slot that names an empty one, which retail
	/// answers 0 for, where the doc gives a chassis type of 0 to 7 a HERC of <c>car\sm_init.dat</c>. With no
	/// <see cref="Player"/>, after a load that
	/// failed, the answer is true, an engine guard that keeps the shell from leaving for a mission with no file.
	/// </summary>
	public bool PlayerHasNoHerc() => Player is not { Entries: [{ Herc: not null }, ..] };

	/// <summary>
	/// <c>CareerRecord_AnyPilotUnarmed</c> (<c>0001620d</c>)(<c>CareerRecord_Current</c>) in <see cref="Mode"/> 0
	/// (docs/es1/shell/war-map.md#begin-mission): any pilot with a hangar slot whose HERC carries weapon 17, the empty
	/// mount, on every mount (<c>Hangar_SlotIsUnarmed</c>, <c>0002a354</c>; <c>Herc_IsUnarmed</c>, <c>00022ebf</c>), so
	/// also one with no mounts; an empty slot answers false. Retail steps the current pilot through the entries with a
	/// slot, as many as <c>CareerRecord_CountPilots</c> counts, from the record's current pilot; <see cref="Player"/> holds
	/// those entries, so each is tested once. Retail's steps also move the current pilot and the hangar's selected slot,
	/// which this engine does not keep (<see cref="Player"/>).
	/// </summary>
	public bool AnyPilotUnarmed() =>
		Player?.Entries.Any(entry => entry.Herc is { } herc && herc.Weapons.All(weapon => weapon == UnarmedWeapon)) ?? false;

	/// <summary>The weapon an empty mount carries.</summary>
	private const short UnarmedWeapon = 17;

	/// <summary>
	/// <c>PlayerMec_Write</c> (<c>0002299e</c>) in <see cref="Mode"/> 0, BEGIN MISSION >'s
	/// (docs/es1/formats/career-log-and-player-mec.md#the-player-file): <c>data\player.mec</c> written again with the
	/// squad the load kept (<see cref="Player"/>), the player's first word drawn again with <c>Math_RandomBelow</c>(11)
	/// as <c>PlayerMec_WritePilotWords</c> (<c>0001548f</c>) draws it on each write; the HERCs' status and mount words are
	/// as the load's write left them, which a second write leaves unchanged. Nothing is written with no
	/// <see cref="Player"/>. PLACEHOLDER: <c>CareerRecord_SaveCurrent</c> (<c>00014f28</c>)'s <c>car\sm_play.gam</c>, the
	/// shell's after-mission file (plan-es1-first-mission.md's "The shell's after-mission files").
	/// </summary>
	public void WritePlayerMec() {
		if (Player is not { } player) {
			return;
		}

		if (player.Entries is [var first, ..] && _data.Random is { } random) {
			first.Word1 = (short)random.Below(11);
		}

		try {
			Es1SingleMission.WritePlayerMec(_installRoot, player);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"data\\{Es1MissionFiles.PlayerName} does not open for writing ({ex.Message}).");
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
