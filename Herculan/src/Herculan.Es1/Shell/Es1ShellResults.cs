using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Shell;

/// <summary>
/// <c>Shell_ProcessMissionResults</c> (<c>00038eb5</c>), the way back from a mission in either mode
/// (docs/es1/shell/between-missions.md#after-a-mission), and the values it leaves for the debriefing's results panel
/// (docs/es1/shell/debriefing.md#the-results-panel): the salvage, the salvage pairs, the three totals and the lost pilots'
/// count. The outcome goes to <see cref="Es1ShellMission.Outcome"/>, the flags to <see cref="Es1ShellMission.Flags"/>,
/// a single mission's record and hangar to <see cref="Es1ShellMission.Record"/> and <see cref="Es1ShellMission.Hangar"/>,
/// a career's to the roster (<see cref="Es1ShellMission.Roster"/>); the player's three gains, the panel's "YOURS" column,
/// are the record's pilot entry 0's <see cref="Es1PilotEntry.Gains"/>. The class is this engine's bundle of those
/// globals; every member is retail's.
/// </summary>
public sealed class Es1ShellResults {
	/// <summary><c>data\results.dat</c>'s salvage, the dword <c>0007fcf4</c>; 0 in the image.</summary>
	public int Salvage { get; private set; }

	/// <summary>
	/// <c>data\results.dat</c>'s pairs of words (<i>a</i>, <i>b</i>), whose count is the word <c>000817cc</c>, the panel's
	/// "WEAPONS RECOVERED:"; a career pushes each <i>b</i> onto slot <i>a</i> of the weapon stock.
	/// </summary>
	public (short Slot, short Value)[] Pairs { get; private set; } = [];

	/// <summary>
	/// The words <c>0009ae5c</c>, <c>0009ae5e</c> and <c>0009ae60</c>, which the read zeroes and each pilot's gains add to
	/// (<see cref="Es1PilotEntry.ApplyGains"/>): the panel's "TOTAL" column of HERC, structure and flyer kills.
	/// </summary>
	public short[] Totals { get; } = new short[Es1PilotEntry.GainCount];

	/// <summary>
	/// The word <c>0007ebca</c>, 0 in the image, to which <c>CareerRecord_CountLostPilots</c> adds the single mission's
	/// pilots whose condition is 0: the panel's "SQUAD LOSSES:". <c>es2_xref</c> finds its address in the two functions that
	/// add to it and in two readers, and none that zeroes it; here it counts from the shell's start, as the image's 0 does.
	/// A career's <c>CareerRecord_ReplaceLostPilots</c> adds to it in place of the count
	/// (<see cref="Es1CareerAfterMission.ReplaceLostPilots"/>).
	/// </summary>
	public short LostPilots { get; internal set; }

	/// <summary>The career record's word <c>+4</c> after each career mission, the service bay after one (docs/es1/shell/between-missions.md#the-careers-screen).</summary>
	private const short AfterMissionScreen = 1710;

	/// <summary>
	/// <c>Shell_ProcessMissionResults</c> on <paramref name="mission"/>, after <see cref="Es1ShellMission.ReadRestore"/>:
	/// <list type="number">
	/// <item>in <c>Shell_Mode</c> 1 <c>Career_LoadCurrent</c> (<see cref="Es1CareerRoster.LoadCurrent"/>), otherwise
	/// <c>SingleMission_RestoreRecord</c> (<c>00014fd6</c>): <c>car\sm_play.gam</c> into the record and
	/// <c>car\sm_play.mec</c> into the hangar (<see cref="RestoreRecord"/>);</item>
	/// <item><c>data\results.dat</c>: the outcome, the salvage, the pair count and the pairs, each pair (<i>a</i>, <i>b</i>)
	/// in mode 1 pushing <i>b</i> onto slot <i>a</i> of the roster's stock 0 (<see cref="Es1WeaponStock.Add"/>), then
	/// <see cref="ReadPilotResults"/> on <c>CareerRecord_Current</c>'s record;</item>
	/// <item><c>data\mission.var</c> whole into the flags, and flag 0 set to the outcome;</item>
	/// <item>in mode 1 <see cref="FinishCareerMission"/>; otherwise <c>CareerRecord_CountLostPilots</c>
	/// (<see cref="Es1CareerRecord.CountLostPilots"/>) of <c>CareerRecord_Current</c>'s record added to
	/// <see cref="LostPilots"/>.</item>
	/// </list>
	/// Retail asserts at severity 5 when a file does not open (<c>shell.cpp</c> line 199 for <c>results.dat</c>,
	/// <c>0xd7</c> for <c>mission.var</c>) and reads what a stream gives past its end; here a file that does not open is
	/// reported and read as empty, and every field past a file's end reads 0, engine guards. A career whose load left no
	/// hangar is read over an empty one, an engine guard.
	/// </summary>
	public void Process(Es1ShellMission mission, string installRoot, Es1ShellData data) {
		if (mission.Mode == 1) {
			mission.Roster.LoadCurrent();
		} else {
			RestoreRecord(mission, installRoot);
		}

		var record = mission.CurrentRecord!;
		var hangar = mission.Hangar ??= Es1Hangar.Read(new Es1HercList());

		var results = new Cursor(ReadData(installRoot, Es1MissionResults.ResultsName, "shell.cpp line 199"));
		mission.Outcome = results.Int16();
		Salvage = results.Int32();
		short count = results.Int16();
		Pairs = new (short, short)[Math.Max((int)count, 0)];
		for (int i = 0; i < Pairs.Length; i++) {
			Pairs[i] = (results.Int16(), results.Int16());
			if (mission.Mode == 1) {
				mission.Roster.Stocks[0].Add(Pairs[i].Slot, Pairs[i].Value);
			}
		}

		ReadPilotResults(record, hangar, mission.Mode, results);

		var flags = new Cursor(ReadData(installRoot, Es1MissionFiles.MissionVarName, "shell.cpp line 0xd7"));
		for (int i = 0; i < mission.Flags.Length; i++) {
			mission.Flags[i] = flags.Int16();
		}

		mission.Flags[0] = mission.Outcome;
		if (mission.Mode == 1) {
			FinishCareerMission(mission, data, hangar);
		} else {
			LostPilots += record.CountLostPilots();
		}
	}

	/// <summary>
	/// <c>Shell_ProcessMissionResults</c>' last step in <c>Shell_Mode</c> 1 (docs/es1/shell/between-missions.md#after-a-mission),
	/// on the roster's record its word <c>+7</c> names: <c>Shell_StockItemsFromFlags</c>, <see cref="Salvage"/> added to
	/// <c>Career_Salvage</c>, <c>Inventory_ResupplyWeapons</c> of stock 0 and <c>CareerRecord_FinishMission</c>
	/// (<see cref="Es1CareerAfterMission"/>); <c>Tech_Unlock</c>(the record's equipment state, its missions played)
	/// (<see cref="Es1EquipmentState.Unlock"/>), whose grade test passes for missions played 0 and otherwise when
	/// <c>Mission_OutcomeGrade</c> (<c>00036837</c>)(the outcome, the word <c>000aad5e</c>) is below 2;
	/// <c>Hangar_AdvanceMarkedHerc</c>; and the record's word <c>+4</c> 1710.
	/// </summary>
	private void FinishCareerMission(Es1ShellMission mission, Es1ShellData data, Es1Hangar hangar) {
		var roster = mission.Roster;
		var after = new Es1CareerAfterMission(mission, data);
		after.StockItemsFromFlags();
		roster.Salvage += Salvage;
		after.ResupplyWeapons(roster.Stocks[0]);
		var record = roster.CurrentRecord;
		after.FinishMission(record, hangar);
		short missions = record.MissionsPlayed;
		bool gradePasses = missions == 0 || MessageBox.Es1MessageBox.OutcomeGrade(mission.Outcome, mission.RestoredMissionKind) < 2;
		record.Equipment.Unlock(missions, gradePasses);
		Es1CareerAfterMission.AdvanceMarkedHerc(hangar);
		record.Word4 = AfterMissionScreen;
	}

	/// <summary>
	/// <c>SingleMission_RestoreRecord</c> (<c>00014fd6</c>): <c>car\sm_play.gam</c> read into the record
	/// (<see cref="Es1CareerRecordTransformer"/>), then <c>car\sm_play.mec</c> loaded into the hangar with
	/// <c>Hangar_LoadFile</c> (<see cref="Es1Hangar.Read"/>). Retail asserts at <c>career.cpp</c> line <c>0x61</c> when the
	/// record's file does not open, and reads the hangar from whatever its stream gives; here a file that is missing or
	/// does not read is reported and leaves the record as it was, or a new one, and the hangar empty, engine guards.
	/// </summary>
	private static void RestoreRecord(Es1ShellMission mission, string installRoot) {
		string recordPath = Es1SingleMission.CarPath(installRoot, Es1SingleMission.SavedRecordName);
		try {
			mission.Record = new Es1CareerRecordTransformer().Parse(File.ReadAllBytes(recordPath)) ?? mission.Record;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
			Console.Error.WriteLine($"car\\{Es1SingleMission.SavedRecordName} does not read ({ex.Message}; career.cpp line 0x61 asserts here).");
		}

		mission.Record ??= new Es1CareerRecord();
		string hangarPath = Es1SingleMission.CarPath(installRoot, Es1SingleMission.SavedHangarName);
		Es1HercList? list = null;
		try {
			list = new Es1HercListTransformer().Parse(File.ReadAllBytes(hangarPath));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
			Console.Error.WriteLine($"car\\{Es1SingleMission.SavedHangarName} does not read ({ex.Message}); the hangar is left empty.");
		}

		mission.Hangar = Es1Hangar.Read(list ?? new Es1HercList());
	}

	/// <summary>
	/// <c>CareerRecord_ReadPilotResults</c> (<c>0001568d</c>): <see cref="Totals"/> zeroed; then for each entry 0 to 3 of
	/// <paramref name="record"/> on strength with a slot, in entry order, its condition and its gains:
	/// <list type="bullet">
	/// <item>for entry 0, or every entry outside <paramref name="mode"/> 0: <c>Hangar_ReadStatusBlock</c>
	/// (<c>00028e50</c>)(stream, slot), which, when the slot holds a HERC, reads the 66-byte block over its status block
	/// and makes its diagram's changes (<see cref="Es1HercRecord.BuildDiagramMounts"/>), and reads nothing otherwise; the
	/// condition is that HERC's internal word 9, or 0 with none;</item>
	/// <item>for the other entries in mode 0: a block of its own (<c>HercStatus_Ctor</c>) read whole from the stream, the
	/// condition its internal word 9, no HERC changed;</item>
	/// </list>
	/// then <c>PilotEntry_ReadGains</c> (<c>000153d8</c>): 6 bytes into the gains, and <see cref="Es1PilotEntry.ApplyGains"/>
	/// with <see cref="Totals"/>.
	/// </summary>
	private void ReadPilotResults(Es1CareerRecord record, Es1Hangar hangar, short mode, Cursor results) {
		Array.Clear(Totals);
		for (int i = 0; i < Es1CareerRecord.PilotCount; i++) {
			if (!record.OnStrengthWithSlot(i)) {
				continue;
			}

			var entry = record.Pilots[i];
			if (i == 0 || mode != 0) {
				if (hangar.HercIn(entry.Slot) is { } herc) {
					herc.Status = results.Int16s(Es1HercRecord.StatusLength);
					herc.BuildDiagramMounts();
					entry.Condition = herc.PilotCondition;
				} else {
					entry.Condition = 0;
				}
			} else {
				short[] block = results.Int16s(Es1HercRecord.StatusLength);
				entry.Condition = block[Es1HercRecord.PilotConditionWord];
			}

			entry.Gains = results.Int16s(Es1PilotEntry.GainCount);
			entry.ApplyGains(Totals);
		}
	}

	/// <summary>The install's <c>data\</c><paramref name="name"/>, or no bytes, reported, when it does not open.</summary>
	private static byte[] ReadData(string installRoot, string name, string assert) {
		try {
			return File.ReadAllBytes(Es1MissionFiles.DataPath(installRoot, name));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"data\\{name} does not open ({ex.Message}; {assert} asserts here).");
			return [];
		}
	}

	/// <summary>
	/// A little-endian read of a file's bytes from the start, as the shell's <c>FileRStream</c> reads it; a field past the
	/// end reads 0, an engine guard where retail reads what the stream gives.
	/// </summary>
	public sealed class Cursor(byte[] bytes) {
		private int _at;

		public byte Byte() => _at < bytes.Length ? bytes[_at++] : Skip(1);

		public short Int16() => (short)(Byte() | Byte() << 8);

		public int Int32() => (ushort)Int16() | Int16() << 16;

		public short[] Int16s(int count) => Enumerable.Range(0, count).Select(_ => Int16()).ToArray();

		public byte[] Bytes(int count) => Enumerable.Range(0, Math.Max(count, 0)).Select(_ => Byte()).ToArray();

		private byte Skip(int count) {
			_at += count;
			return 0;
		}
	}
}
