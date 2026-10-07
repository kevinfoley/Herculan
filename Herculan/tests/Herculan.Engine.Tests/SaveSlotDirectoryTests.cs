using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <c>sav\GAMEFILE.STR</c> — the save screen's slot list.
///
/// <para>The synthetic case builds the bytes by hand from the layout in
/// <c>docs/retail/formats/save-games.md</c> rather than from the transformer's own writer, so the two
/// disagreeing shows up as a failure instead of cancelling out. What it pins is the shape that is
/// easy to get wrong: two independently counted blocks, a stored length that counts each string's own
/// NUL, and a trailing byte per entry that is padding in the first block and the in-use flag in the
/// second.</para>
/// </summary>
public class SaveSlotDirectoryTests {
	[Fact]
	public void RoundTripsASyntheticDirectoryByteForByte() {
		byte[] original = BuildDirectory();
		var transformer = new SaveSlotDirectoryTransform();

		SaveSlotDirectory parsed = Assert.IsType<SaveSlotDirectory>(transformer.Parse(original));

		Assert.Equal(original, transformer.Write(parsed));
	}

	/// <summary>
	/// The in-use flag is the label block's trailing byte, not the filename block's — which is why the
	/// filenames of unused slots are present and complete while their labels are stubs.
	/// </summary>
	[Fact]
	public void ReadsFilenamesLabelsAndTheInUseFlag() {
		SaveSlotDirectory parsed =
			Assert.IsType<SaveSlotDirectory>(new SaveSlotDirectoryTransform().Parse(BuildDirectory()));

		Assert.Equal(SaveSlotDirectory.SlotCount, parsed.Slots.Count);
		Assert.Equal("GAME_0.SAV", parsed.Slots[0].FileName);
		Assert.Equal(" 1. KEVIN", parsed.Slots[0].Label);
		Assert.True(parsed.Slots[0].InUse);

		// Slot 1 is the never-written case: its label is the stored "N. " prefix and nothing else.
		Assert.Equal("GAME_1.SAV", parsed.Slots[1].FileName);
		Assert.Equal(" 2. ", parsed.Slots[1].Label);
		Assert.False(parsed.Slots[1].InUse);

		Assert.Equal("GAME_R.SAV", parsed.Slots[SaveSlotDirectory.ResumeSlot].FileName);
		Assert.Equal("GAME_T.SAV", parsed.Slots[SaveSlotDirectory.TrainingSlot].FileName);
	}

	/// <summary>
	/// The reader completes an empty slot's label from the string table, testing the fifth character —
	/// so a stored <c>" 2. "</c> gains the word and a label that already has one is left alone.
	/// </summary>
	[Fact]
	public void CompletesAnEmptySlotsLabel() {
		Assert.Equal(" 2. EMPTY", ShellSaveSlots.Complete(" 2. ", "EMPTY"));
		Assert.Equal(" 1. KEVIN", ShellSaveSlots.Complete(" 1. KEVIN", "EMPTY"));
	}

	/// <summary>
	/// A real install's directory file, parsed and written back.
	///
	/// <para>The round trip is byte-equal from the count block on and deliberately not equal in the
	/// leading length: that field is the physical file less four, taken by seeking to the end
	/// <i>after</i> writing, so on the retail file it counts a stale tail this writer does not
	/// reproduce. Everything the reader consumes does come back unchanged, which is the invariant worth
	/// holding.</para>
	/// </summary>
	[Fact]
	public void ParsesTheRetailDirectory() {
		if (GameInstall.Locate(null) is not { } root) {
			return;
		}

		string path = Path.Combine(ShellSaveSlots.Directory(root), ShellSaveSlots.DirectoryFileName);
		if (!File.Exists(path)) {
			return;
		}

		byte[] original = File.ReadAllBytes(path);
		var transformer = new SaveSlotDirectoryTransform();
		SaveSlotDirectory? parsed = transformer.Parse(original);

		Assert.NotNull(parsed);
		Assert.Equal(SaveSlotDirectory.SlotCount, parsed.Slots.Count);

		byte[] written = transformer.Write(parsed)!;
		string diag = $"payload={written.Length} physical={original.Length} "
			+ $"stored={parsed.StoredLength} inUse={parsed.Slots.Count(s => s.InUse)}";

		// Everything past the length field is a prefix of the file; what follows it is the stale tail.
		const int LengthFieldSize = 4;
		Assert.True(written.Length <= original.Length, diag);
		Assert.Equal(written[LengthFieldSize..], original[LengthFieldSize..written.Length]);

		// The stored length measures the physical file rather than the payload, which is exactly the
		// difference the round trip cannot reproduce.
		Assert.Equal(original.Length - LengthFieldSize, parsed.StoredLength);
		Assert.True(parsed.StoredLength > written.Length - LengthFieldSize, diag);

		// Every slot names a file, and the last two are the autosaves rather than player slots.
		Assert.All(parsed.Slots, slot => Assert.False(string.IsNullOrEmpty(slot.FileName), diag));
		Assert.Equal("GAME_R.SAV", parsed.Slots[SaveSlotDirectory.ResumeSlot].FileName);
		Assert.Equal("GAME_T.SAV", parsed.Slots[SaveSlotDirectory.TrainingSlot].FileName);
	}

	/// <summary>
	/// Twelve filenames then twelve labels, each string counted with its own NUL inside the count, each
	/// entry followed by one byte — zero in the filename block, the in-use flag in the label block.
	/// </summary>
	private static byte[] BuildDirectory() {
		var names = new string[SaveSlotDirectory.SlotCount];
		var labels = new string[SaveSlotDirectory.SlotCount];
		for (int i = 0; i < SaveSlotDirectory.PlayerSlotCount; i++) {
			names[i] = $"GAME_{i}.SAV";
			labels[i] = $"{i + 1,2}. ";
		}

		names[SaveSlotDirectory.ResumeSlot] = "GAME_R.SAV";
		names[SaveSlotDirectory.TrainingSlot] = "GAME_T.SAV";
		labels[SaveSlotDirectory.ResumeSlot] = "RESUME";
		labels[SaveSlotDirectory.TrainingSlot] = "TRAINING";

		// Only slot 0 holds a save, so it is the one with a completed label and the flag set.
		labels[0] = " 1. KEVIN";

		using var body = new MemoryStream();
		void Int16(int value) {
			body.WriteByte((byte)(value & 0xff));
			body.WriteByte((byte)((value >> 8) & 0xff));
		}

		void Counted(string value, byte trailing) {
			Int16(value.Length + 1);
			foreach (char c in value) {
				body.WriteByte((byte)c);
			}

			body.WriteByte(0);
			body.WriteByte(trailing);
		}

		Int16(SaveSlotDirectory.SlotCount);
		foreach (string name in names) {
			Counted(name, 0);
		}

		Int16(SaveSlotDirectory.SlotCount);
		for (int i = 0; i < labels.Length; i++) {
			Counted(labels[i], i == 0 ? (byte)1 : (byte)0);
		}

		using var file = new MemoryStream();
		int length = (int)body.Length;
		file.WriteByte((byte)(length & 0xff));
		file.WriteByte((byte)((length >> 8) & 0xff));
		file.WriteByte((byte)((length >> 16) & 0xff));
		file.WriteByte((byte)((length >> 24) & 0xff));
		body.Position = 0;
		body.CopyTo(file);
		return file.ToArray();
	}
}
