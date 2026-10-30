using System.Text;
using HercWorks.Core.Data.File;
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

	/// <summary>The mission's path <c>000aad14</c>, <c>msn\&lt;name&gt;</c>; empty in the image.</summary>
	public string Path { get; private set; } = "";

	/// <summary>The mission's text's path <c>000aad2a</c>, <see cref="Path"/> with <c>.eng</c> from its first <c>.</c>; empty in the image.</summary>
	public string TextPath { get; private set; } = "";

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
				// MsnGen_LoadMission stores header words 3 and 4 in 000aad5a and 000aad5c; no ported code reads 000aad5c.
				MissionKind = result.Script.Header[3];
			} else {
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
	/// the shell's menus make: the mission map deleted (<c>Shell_DeleteMissionMap</c>, <c>0002a4ec</c>), the objectives
	/// written (<see cref="WriteObjectives"/>), and the mission map made again.
	/// </summary>
	public void Prepare(Es1ShellScreens screens) {
		if (screens.MissionMap != null) {
			// PLACEHOLDER: maybe_MouseEventQueue_Stop (000106af), 0000a9c8(map) and 00009a73(map, 3), the mission map's, S5's.
			screens.MissionMap = null;
		}

		WriteObjectives();
		// PLACEHOLDER: maybe_MouseEventQueue_Start (00010559), the byte 0007e5df cleared and Shell_MissionMap (000809a4) made
		// with 000094d9(0, 000a02d4): the briefing's mission map, S5's; until it lands no mission map is made.
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
