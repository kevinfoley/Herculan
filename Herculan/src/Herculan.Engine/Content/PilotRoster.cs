using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Cockpit;

namespace Herculan.Engine.Content;

/// <summary>
/// Who a squadmate is: the name across their comm box, the portrait bank that talks, and the
/// recorded voice that speaks. All three come off one number — the pilot's index into
/// <c>str\PILOTS.STR</c>, which the machine carries at <c>mech+0x29c</c> and
/// <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) stamps from the player squad's own records.
///
/// <para><c>HddGauge_LoadPilotFrames</c> (<c>0044a7c0</c>) is where the three meet: it walks
/// <c>PILOTS.STR</c> to that index for the name, divides the index by three
/// (<c>Pilot_PortraitOf</c>, <c>00434240</c>) for the portrait bank <c>dba\PILOT&lt;n&gt;.DBA</c> and its
/// <c>ofs\PILOT&lt;n&gt;.OFS</c> offsets, and takes <c>(n &gt;&gt; 2) + 1</c> with 3 remapped to 4
/// (<c>Pilot_VoiceBankOf</c>, <c>00434260</c>) for the voice bank. So 36 pilots share 12 portraits and 12 portraits share
/// three recorded voices.</para>
/// </summary>
public sealed class PilotRoster {
	/// <summary>The resource holding the 36 names.</summary>
	public const string ResourceName = "PILOTS.STR";

	/// <summary>Folder the per-portrait offset tables live in.</summary>
	public const string OffsetFolder = "ofs";

	/// <summary>How many roster entries share one portrait — <c>Pilot_PortraitOf</c>'s divisor.</summary>
	public const int PilotsPerPortrait = 3;

	/// <summary>How many portrait banks there are, and how many speakers the <c>.SNC</c> names cover.</summary>
	public const int PortraitCount = 12;

	/// <summary>
	/// Entries the offset table is read for — <c>HddGauge_LoadPilotFrames</c>' hardcoded <c>0x1b</c>,
	/// not a count in the file. The banks ship 28 frames, so the last — the death scream's,
	/// <see cref="SquadCommChannel.ScreamFrame"/> — is never placed and draws at (0, 0).
	/// </summary>
	public const int OffsetEntryCount = 27;

	/// <summary>
	/// Talking-head frames at the head of a portrait bank — the range a <c>.SNC</c> script's frame
	/// bytes cover. The three frames after them are placed by the offset table but drawn by nothing:
	/// <c>HddGauge_PaintPilotFrame</c>'s live branch rolls one of them and reads its offset pair into
	/// locals it never uses.
	/// </summary>
	public const int TalkingFrameCount = 24;

	private readonly string[] _names;
	private readonly Dictionary<int, (int X, int Y)[]> _offsets = new();
	private readonly GameContent _content;

	private PilotRoster(GameContent content, string[] names) {
		_content = content;
		_names = names;
	}

	/// <summary>Every name, in file order. The index into this is the pilot index.</summary>
	public IReadOnlyList<string> Names => _names;

	/// <summary>Pilot <paramref name="pilot"/>'s name, or null when the index is off the roster.</summary>
	public string? Name(int pilot) => pilot >= 0 && pilot < _names.Length ? _names[pilot] : null;

	/// <summary>Which portrait bank a pilot talks with — <c>Pilot_PortraitOf</c>.</summary>
	public static int PortraitOf(int pilot) => pilot / PilotsPerPortrait;

	/// <summary>
	/// Which recorded voice a portrait speaks with — <c>Pilot_VoiceBankOf</c>. Banks 1, 2 and 4 exist;
	/// the third group is remapped onto 4 rather than getting one of its own.
	/// </summary>
	public static int VoiceBankOf(int portrait) {
		int bank = (portrait >> 2) + 1;
		return bank == 3 ? 4 : bank;
	}

	/// <summary>The sprite bank name for a portrait — the loader's <c>"pilot" + itoa(n)</c>.</summary>
	public static string BankName(int portrait) => "PILOT" + portrait;

	/// <summary>
	/// Where each frame of a portrait bank is drawn relative to its box origin, from
	/// <c>ofs\PILOT&lt;n&gt;.OFS</c>. Read once per portrait and kept. Empty when the file is missing,
	/// which places every frame at the origin rather than dropping the portrait.
	///
	/// <para>These are the bank's own 320-wide pixels and are <b>not</b> doubled for the 640-wide
	/// cockpit: both the comm box's paint and the MFD's add them raw while blitting the frame itself
	/// doubled.</para>
	/// </summary>
	public (int X, int Y)[] Offsets(int portrait) {
		if (_offsets.TryGetValue(portrait, out var cached)) {
			return cached;
		}

		var offsets = Array.Empty<(int, int)>();
		if (_content.Read(OffsetFolder, BankName(portrait) + ".OFS") is { } bytes
			&& new PilotOffsetFileTransformer().Parse(bytes) is { Entries: { } entries }) {
			offsets = new (int, int)[OffsetEntryCount];
			foreach (var entry in entries.Take(OffsetEntryCount)) {
				if (entry.Index >= 0 && entry.Index < OffsetEntryCount) {
					offsets[entry.Index] = (entry.X, entry.Y);
				}
			}
		}

		_offsets[portrait] = offsets;
		return offsets;
	}

	/// <summary>Frame <paramref name="frame"/>'s offset, or (0,0) when the table does not cover it.</summary>
	public (int X, int Y) Offset(int portrait, int frame) {
		var offsets = Offsets(portrait);
		return frame >= 0 && frame < offsets.Length ? offsets[frame] : (0, 0);
	}

	/// <summary>
	/// Reads the roster out of the mounted archives, or null when <c>PILOTS.STR</c> is absent or does
	/// not parse. It is <c>str\PILOTS.STR</c> in every language: <c>HddDisplay_Ctor</c> (<c>00448cc8</c>) builds the path
	/// with <c>ResourcePath_BuildFolderName</c>, not the language's folder, so v1.10's <c>stf\</c> and <c>stg\</c>
	/// copies are never read.
	/// </summary>
	public static PilotRoster? Load(GameContent content) {
		ArgumentNullException.ThrowIfNull(content);

		if (SimStrings.LoadUntranslated(content, ResourceName) is not { } table) {
			return null;
		}

		var names = new List<string>();
		for (int group = 0; group < table.GroupCount; group++) {
			names.AddRange(table.Group(group).Select(entry => entry.Text));
		}

		return new PilotRoster(content, names.ToArray());
	}
}
