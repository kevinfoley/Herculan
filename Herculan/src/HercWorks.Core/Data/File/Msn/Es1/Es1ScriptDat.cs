namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// FILE - ES1's <c>data\script.dat</c>: the mission as the shell's load leaves it for the simulator, ten header words and
/// nine blocks, each an <c>int16</c> count and that many records of the fields the simulator reads, refs resolved
/// (docs/es1/formats/script-dat.md#writing). Written by <see cref="Io.Transform.Common.Es1MissionLoad"/>, read and written
/// byte for byte by <see cref="Io.Transform.Common.Es1ScriptDatTransformer"/>.
///
/// <para>A block record's fields are the <c>.MSN</c> record's (<see cref="Es1MissionFile"/>), in their order, without the
/// leading words the load keeps to itself.</para>
/// </summary>
public class Es1ScriptDat {
	/// <summary>How many header words lead the file.</summary>
	public const int HeaderLength = 10;

	/// <summary>
	/// The ten header words, the load's <c>000ab21a</c>–<c>000ab22c</c>, with the load's mode at index 2
	/// (docs/es1/formats/script-dat.md#writing).
	/// </summary>
	public short[] Header { get; set; } = new short[HeaderLength];

	/// <summary>Block 1, the points.</summary>
	public Es1ScriptPoint[] Points { get; set; } = [];

	/// <summary>Block 2, the headings: each <see cref="Es1Heading.Value"/>.</summary>
	public short[] Headings { get; set; } = [];

	/// <summary>Block 3, the waypoint groups: each one's <see cref="Es1WaypointGroup.Waypoints"/>, block-1 refs.</summary>
	public short[][] WaypointGroups { get; set; } = [];

	/// <summary>Block 4, the trigger areas.</summary>
	public Es1ScriptTriggerArea[] TriggerAreas { get; set; } = [];

	/// <summary>Block 5, the actions.</summary>
	public Es1ScriptAction[] Actions { get; set; } = [];

	/// <summary>Block 6, the mechs.</summary>
	public Es1ScriptMech[] Mechs { get; set; } = [];

	/// <summary>Block 7, the flyers.</summary>
	public Es1ScriptFlyer[] Flyers { get; set; } = [];

	/// <summary>Block 8, the bases.</summary>
	public Es1ScriptBase[] Bases { get; set; } = [];

	/// <summary>Block 9, the groups.</summary>
	public Es1ScriptGroup[] Groups { get; set; } = [];

	/// <summary>
	/// The bytes after block 9. The shell's writer opens the file without truncating it, so a mission shorter than the
	/// one written before leaves that one's tail here (docs/es1/formats/script-dat.md#writing); the simulator reads none
	/// of it. Kept so a file read gives back its bytes.
	/// </summary>
	public byte[] Tail { get; set; } = [];
}

/// <summary>A block-1 point, 12 bytes.</summary>
public class Es1ScriptPoint {
	/// <inheritdoc cref="Es1Point.X"/>
	public int X { get; set; }

	/// <inheritdoc cref="Es1Point.Y"/>
	public int Y { get; set; }

	/// <inheritdoc cref="Es1Point.Z"/>
	public int Z { get; set; }
}

/// <summary>A block-4 trigger area, 6 bytes.</summary>
public class Es1ScriptTriggerArea {
	/// <inheritdoc cref="Es1TriggerArea.Shape"/>
	public short Shape { get; set; }

	/// <inheritdoc cref="Es1TriggerArea.PointRef"/>
	public short PointRef { get; set; }

	/// <inheritdoc cref="Es1TriggerArea.Word0A"/>
	public short Word0A { get; set; }
}

/// <summary>A block-5 action, 24 bytes.</summary>
public class Es1ScriptAction {
	/// <inheritdoc cref="Es1Action.Type"/>
	public short Type { get; set; }

	/// <inheritdoc cref="Es1Action.Verb"/>
	public short Verb { get; set; }

	/// <inheritdoc cref="Es1Action.AreaRefs"/>
	public short[] AreaRefs { get; set; } = new short[8];

	/// <inheritdoc cref="Es1Action.Word1A"/>
	public short Word1A { get; set; }

	/// <inheritdoc cref="Es1Action.Word1C"/>
	public short Word1C { get; set; }
}

/// <summary>A block-6 mech, 98 bytes: the <c>.MSN</c> record's <c>0x08</c>–<c>0x69</c>.</summary>
public class Es1ScriptMech {
	/// <inheritdoc cref="Es1Mech.Words08"/>
	public short[] Words08 { get; set; } = new short[20];

	/// <inheritdoc cref="Es1Mech.Type"/>
	public short Type { get; set; }

	/// <inheritdoc cref="Es1Mech.Words32"/>
	public short[] Words32 { get; set; } = new short[10];

	/// <inheritdoc cref="Es1Mech.PointRef"/>
	public short PointRef { get; set; }

	/// <inheritdoc cref="Es1Mech.HeadingRef"/>
	public short HeadingRef { get; set; }

	/// <inheritdoc cref="Es1Mech.Word4A"/>
	public short Word4A { get; set; }

	/// <inheritdoc cref="Es1Mech.Word4C"/>
	public short Word4C { get; set; }

	/// <inheritdoc cref="Es1Mech.Words4E"/>
	public short[] Words4E { get; set; } = new short[10];

	/// <inheritdoc cref="Es1Mech.Word62"/>
	public short Word62 { get; set; }

	/// <inheritdoc cref="Es1Mech.ActionRef64"/>
	public short ActionRef64 { get; set; }

	/// <inheritdoc cref="Es1Mech.ActionRef66"/>
	public short ActionRef66 { get; set; }

	/// <inheritdoc cref="Es1Mech.Word68"/>
	public short Word68 { get; set; }
}

/// <summary>A block-7 flyer, 56 bytes: the <c>.MSN</c> record's <c>0x08</c>–<c>0x3f</c>.</summary>
public class Es1ScriptFlyer {
	/// <inheritdoc cref="Es1Flyer.Words08"/>
	public short[] Words08 { get; set; } = new short[20];

	/// <inheritdoc cref="Es1Flyer.PointRef"/>
	public short PointRef { get; set; }

	/// <inheritdoc cref="Es1Flyer.HeadingRef"/>
	public short HeadingRef { get; set; }

	/// <inheritdoc cref="Es1Flyer.Type"/>
	public short Type { get; set; }

	/// <inheritdoc cref="Es1Flyer.Word36"/>
	public short Word36 { get; set; }

	/// <inheritdoc cref="Es1Flyer.Word38"/>
	public short Word38 { get; set; }

	/// <inheritdoc cref="Es1Flyer.ActionRef3A"/>
	public short ActionRef3A { get; set; }

	/// <inheritdoc cref="Es1Flyer.ActionRef3C"/>
	public short ActionRef3C { get; set; }

	/// <inheritdoc cref="Es1Flyer.Word3E"/>
	public short Word3E { get; set; }
}

/// <summary>A block-8 base, 16 bytes: the <c>.MSN</c> record's <c>0x08</c>–<c>0x17</c>.</summary>
public class Es1ScriptBase {
	/// <inheritdoc cref="Es1Base.Type"/>
	public short Type { get; set; }

	/// <inheritdoc cref="Es1Base.PointRef"/>
	public short PointRef { get; set; }

	/// <inheritdoc cref="Es1Base.HeadingRef"/>
	public short HeadingRef { get; set; }

	/// <inheritdoc cref="Es1Base.Word0E"/>
	public short Word0E { get; set; }

	/// <inheritdoc cref="Es1Base.Word10"/>
	public short Word10 { get; set; }

	/// <inheritdoc cref="Es1Base.ActionRef12"/>
	public short ActionRef12 { get; set; }

	/// <inheritdoc cref="Es1Base.ActionRef14"/>
	public short ActionRef14 { get; set; }

	/// <inheritdoc cref="Es1Base.Word16"/>
	public short Word16 { get; set; }
}

/// <summary>A block-9 group, 82 bytes: the <c>.MSN</c> record's <c>0x06</c>–<c>0x57</c>.</summary>
public class Es1ScriptGroup {
	/// <inheritdoc cref="Es1Group.Words06"/>
	public short[] Words06 { get; set; } = new short[20];

	/// <inheritdoc cref="Es1Group.MemberKind"/>
	public short MemberKind { get; set; }

	/// <inheritdoc cref="Es1Group.Word30"/>
	public short Word30 { get; set; }

	/// <inheritdoc cref="Es1Group.Word32"/>
	public short Word32 { get; set; }

	/// <inheritdoc cref="Es1Group.PointRef"/>
	public short PointRef { get; set; }

	/// <inheritdoc cref="Es1Group.HeadingRef"/>
	public short HeadingRef { get; set; }

	/// <inheritdoc cref="Es1Group.WaypointGroupRef"/>
	public short WaypointGroupRef { get; set; }

	/// <inheritdoc cref="Es1Group.MemberRefs"/>
	public short[] MemberRefs { get; set; } = new short[8];

	/// <inheritdoc cref="Es1Group.Word4A"/>
	public short Word4A { get; set; }

	/// <inheritdoc cref="Es1Group.Ref4ERow"/>
	public short Ref4ERow { get; set; }

	/// <inheritdoc cref="Es1Group.Ref4E"/>
	public short Ref4E { get; set; }

	/// <inheritdoc cref="Es1Group.ActionRef"/>
	public short ActionRef { get; set; }

	/// <inheritdoc cref="Es1Group.Word52"/>
	public short Word52 { get; set; }

	/// <inheritdoc cref="Es1Group.Word54"/>
	public short Word54 { get; set; }

	/// <inheritdoc cref="Es1Group.Word56"/>
	public short Word56 { get; set; }
}
