using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;
using Herculan.Es1.Input;

namespace Herculan.Es1.Shell;

/// <summary>
/// What the shell loads once at its start and keeps for every screen (docs/es1/shell/startup.md#the-shared-data), as far
/// as the main menu needs it: the file-name table, the <c>prefs.cfg</c> options' bytes and words, the 13 fonts, the
/// generator, the shell's text and the careers log. The HERC diagrams and the battle set are later parts'.
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

	/// <summary>The mission parameters' five words, options 12 to 16 (docs/es1/formats/msn-mission-file.md).</summary>
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

	/// <summary><c>eng\watext.bin</c>, the shell's text at <c>0009afa8</c>; none when it does not read.</summary>
	public string[]? Text { get; private set; }

	/// <summary>The careers log at <c>0009b20c</c>; none when its path is unknown or the file does not read.</summary>
	public Es1CareerLog? CareerLog { get; private set; }

	/// <summary>The word <c>0007fcea</c>, the current career, 0 when the log's is negative.</summary>
	public short CurrentCareer { get; private set; }

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
	/// % 128 draws; <c>eng\watext.bin</c>; and the careers log, with <see cref="CurrentCareer"/> from it.
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

		// PLACEHOLDER: the rect 0009b000–0009b00c, (0, 0) and the display's attributes 2 and 3 less 1, which only screen
		// 9999 reads (P2 and P3's); the HERC diagrams' tables and art (HercLayout_Load, HercDamageMarks_Load,
		// LoadDamageDat_Shell, HercHotspots_Load, HercArt_LoadAllBanks), S3's; BattleSet_Load, S4's; and 00027c4c, unread.

		LoadCareersLog(installRoot);
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
	/// and the current entry −1 (docs/es1/formats/career-log-and-player-mec.md#loading). A file that does not read whole is
	/// reported and leaves no log, which <see cref="WriteCareersLog"/> then does not write, an engine guard: retail reads
	/// whatever the bytes give and writes it back.
	/// </summary>
	private void LoadCareersLog(string installRoot) {
		if (FileNames is not { Length: > CareersLogEntry } names) {
			return;
		}

		string path = CaseInsensitivePath.Combine(installRoot, names[CareersLogEntry].Split('\\', StringSplitOptions.RemoveEmptyEntries));
		CareersLogPath = path;
		try {
			CareerLog = File.Exists(path) ? new Es1CareerLogTransformer().Parse(File.ReadAllBytes(path)) ?? new Es1CareerLog()
				: new Es1CareerLog();
		} catch (InvalidDataException ex) {
			Console.Error.WriteLine($"{path} does not read ({ex.Message}): the careers log is left alone.");
			return;
		}

		CurrentCareer = Math.Max((short)0, CareerLog.Current);
	}

	/// <summary>
	/// <c>CareerLog_Write</c> (<c>00017f5f</c>)(log, −1), the shutdown's step 1: the file deleted and written again with
	/// every entry (docs/es1/formats/career-log-and-player-mec.md#writing). Retail asserts at severity 5 when it does not
	/// open; here that is reported and the shutdown goes on, an engine guard.
	/// </summary>
	public void WriteCareersLog() {
		if (CareerLog is not { } log || CareersLogPath is not { } path) {
			return;
		}

		try {
			File.Delete(path);
			File.WriteAllBytes(path, new Es1CareerLogTransformer().Write(log)!);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Unable to open careers log file {path} ({ex.Message}).");
		}
	}
}
