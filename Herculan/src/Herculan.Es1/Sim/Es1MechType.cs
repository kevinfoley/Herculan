using System.Text;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Part;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Anim;
using HercWorks.Core.Data.File.Dts.Bsp;
using Herculan.Engine.Content;

namespace Herculan.Es1.Sim;

/// <summary>
/// One chassis' type record, <c>0x1c4</c> bytes, as <c>MechType_InitOne</c> (<c>ES1DBSIM 00019cf0</c>) fills it
/// (docs/es1/formats/chassis-dat.md#the-load): the chassis file, the shape's roots and each root's hardpoint slots, the
/// gun layout and the texture bank.
///
/// <para>Not loaded: the paper doll (<c>PaperDoll_Load</c>), which the cockpit draws; this is PLACEHOLDER: step 6 loads
/// it. The low-memory skip of the first roots and texture banks is not taken: the engine runs as a machine with the
/// memory <c>g_LowMemory</c> asks for (docs/es1/formats/chassis-dat.md#low-memory).</para>
/// </summary>
public sealed class Es1MechType {
	/// <summary><c>Mech_LoadResources</c>' four texture banks by the chassis file's texture group (offset 148).</summary>
	public static IReadOnlyList<string> TextureBanks { get; } = ["LIGHT", "MEDIUM", "HEAVY", "ENEMY"];

	/// <summary><c>g_ShapeDetailThresholds</c> (<c>00091dac</c>), stored in every type record's <c>+0xca</c>.</summary>
	public static IReadOnlyList<int> DetailThresholds { get; } = [75, 60, 45, 25, 18, 12, 6];

	/// <summary>How many blank <c>CONFIG_PART</c>s the record holds at <c>+0xd4</c>, which empty hardpoints take.</summary>
	public const int BlankParts = 10;

	/// <summary>The invisible mounting code, whose hardpoint <c>GunLayout_CollectHardpointBones</c> gives as -1.</summary>
	public const byte InvisibleMounting = 4;

	private Es1MechType(short type, short slot, string name, Es1HercSimDat chassis, IReadOnlyList<TSObject> roots,
			GunLayout? gunLayout) {
		Type = type;
		Slot = slot;
		Name = name;
		Chassis = chassis;
		Roots = roots;
		GunLayout = gunLayout;
		Animation = roots.Count > 0 ? ShapeAnimation.FromRoot(roots[0]) : null;
		HardpointSlots = roots.Select(BindHardpointSlots).ToArray();
	}

	/// <summary>The type number, <c>MECHS.NAM</c>'s index, which the load stores at <c>+0x6c</c>.</summary>
	public short Type { get; }

	/// <summary>The record's slot, <c>+0</c>.</summary>
	public short Slot { get; }

	/// <summary><c>g_MechTypeNames</c>' entry, which names the chassis' <c>dat\</c>, <c>dts\</c>, <c>gl\</c>, <c>dmg\</c> and <c>col\</c> files.</summary>
	public string Name { get; }

	/// <summary>The 192 bytes of <c>dat\&lt;name&gt;</c> at <c>+2</c>.</summary>
	public Es1HercSimDat Chassis { get; }

	/// <summary><c>+0xc6</c>, the shape's roots: complete alternate models, finest first (docs/retail/rendering/mech-shape-drawing.md).</summary>
	public IReadOnlyList<TSObject> Roots { get; }

	/// <summary>Root 0's animation, the one the instance and every root are posed through.</summary>
	public ShapeAnimation? Animation { get; }

	/// <summary><c>gl\&lt;name&gt;</c>, the hardpoint list; null when the chassis has none.</summary>
	public GunLayout? GunLayout { get; }

	/// <summary>
	/// <c>+0xce</c>, per root its <c>CONNECT_INFO</c> list: per hardpoint, the id of the part its slot holds, or null for
	/// the invisible mounting or a root without the part (<c>MechType_BindHardpointSlots</c>, <c>000025c4</c>).
	/// </summary>
	public IReadOnlyList<short?[]> HardpointSlots { get; }

	/// <summary>The texture bank every root's <c>+0x26</c> takes, <c>dba\</c>&lt;bank&gt;; null when the install lacks it.</summary>
	public DynamixBitmapArray? TextureBank { get; private init; }

	/// <summary>The chassis' cockpit name, the 8 characters at the record's <c>+0x58</c> (file offset 86), upper-cased.</summary>
	public string CockpitName {
		get {
			var bytes = Chassis.NameBytes.Take(8).TakeWhile(b => b != 0).ToArray();
			return Encoding.ASCII.GetString(bytes).ToUpperInvariant();
		}
	}

	/// <summary>Root 0's bounding radius, <c>shape+8</c>, which the LOD choice reads whichever root was drawn last.</summary>
	public int ShapeRadius => Roots.Count > 0 && Roots[0] is TSBasePart root ? root.Radius : 0;

	/// <summary>
	/// <c>MechType_InitOne</c> for type <paramref name="type"/>, given its slot. Null when <c>dat\&lt;name&gt;</c> or
	/// <c>dts\&lt;name&gt;</c> is missing, where the original asserts (<c>util\ovl.cpp</c>).
	/// </summary>
	public static Es1MechType? Load(GameContent content, NameList names, short type, short slot) {
		if (names[type] is not { } name
				|| content.Read("dat", name + ".DAT") is not { } datBytes
				|| new Es1HercSimDataTransformer().Parse(datBytes) is not { } chassis
				|| content.Read("dts", name + ".DTS") is not { } dtsBytes
				|| new DTSModelTransformer().Parse(dtsBytes) is not DynamixThreeSpaceModel { Roots: { Count: > 0 } roots }) {
			return null;
		}

		RemapDetailRootTransforms(roots, chassis.ModelLoDBoneIds);
		var gunLayout = content.Read("gl", name + ".GL") is { } glBytes ? new GunLayoutTransformer().Parse(glBytes) : null;
		int group = chassis.TextureGroup;
		var bank = group >= 0 && group < TextureBanks.Count && content.Read("dba", TextureBanks[group] + ".DBA") is { } bankBytes
			? new DynamixBitmapArrayTransformer().Parse(bankBytes)
			: null;

		return new Es1MechType(type, slot, name, chassis, roots, gunLayout) { TextureBank = bank };
	}

	/// <summary>
	/// <c>GunLayout_CollectHardpointBones</c> (<c>0000befc</c>) and <c>MechType_BindHardpointSlots</c> for one root: each
	/// hardpoint's bone id, -1 for the invisible mounting, resolved to the part slot holding the part of that id through
	/// the shape's slot <c>+0x28</c>, null where the root has none (docs/retail/rendering/mech-shape-drawing.md).
	/// </summary>
	private short?[] BindHardpointSlots(TSObject root) {
		var hardpoints = GunLayout?.Hardpoints ?? [];
		return hardpoints.Select(h => h.MountingCode == InvisibleMounting || FindPart(root, h.BoneId) == null
			? (short?)null
			: h.BoneId).ToArray();
	}

	/// <summary>The length of one root's map, and the most part ids the list holds.</summary>
	private const int RemapLength = 50;

	/// <summary>
	/// Step 4, <c>MechType_RemapDetailRootTransforms</c> (<c>00019ba0</c>, docs/es1/formats/chassis-dat.md#the-load): every
	/// root after root 0 renumbered onto root 0's nodes, in place, through the part ids at <c>+0x30</c> (file offset 46),
	/// signed bytes ended by a negative one. A part root 0 lacks is skipped and a transform id past the 50-byte map is
	/// unmapped, where retail reads through a null part or past its map; the core's port of ES2's walk sits in ES2's own
	/// code, which this assembly does not reference.
	/// </summary>
	private static void RemapDetailRootTransforms(IReadOnlyList<TSObject> roots, IReadOnlyList<byte> partIds) {
		var maps = new sbyte[roots.Count][];
		for (int level = 1; level < roots.Count; level++) {
			maps[level] = Enumerable.Repeat((sbyte)-1, RemapLength).ToArray();
		}

		for (int i = 0; i < partIds.Count && i < RemapLength && (sbyte)partIds[i] >= 0; i++) {
			if (FindPart(roots[0], (sbyte)partIds[i]) is not { } reference) {
				continue;
			}

			for (int level = 1; level < roots.Count; level++) {
				if (FindPart(roots[level], (sbyte)partIds[i]) is { Transform: >= 0 and < RemapLength } part) {
					maps[level][part.Transform] = (sbyte)reference.Transform;
				}
			}
		}

		// Root 0's keyframe count, the int16 at +0x10 of its +0x2a.
		int nodeCount = ShapeAnimation.FirstAnimList(roots.Count > 0 ? roots[0] : null)?.Transforms?.Length ?? 0;
		for (int level = 1; level < roots.Count; level++) {
			RemapTransformIds(roots[level], maps[level], nodeCount);
		}
	}

	/// <summary>
	/// <c>Shape_RemapTransformIds</c> (<c>00019ac4</c>) by the class: a <c>TSGroup</c>'s own id and no further; each
	/// per-node entry of a <c>TSBSPPart</c> above 0, below the node count and mapped to a byte that is not negative, then
	/// its children; a <c>TSCellAnimPart</c>'s own id, then its children; the children of an <c>ANShape</c>,
	/// <c>TSShape</c>, <c>TSPartList</c> or <c>TSDetailPart</c>; nothing for any other class, a <c>TSBSPGroup</c> among them.
	/// </summary>
	private static void RemapTransformIds(TSObject? node, sbyte[] map, int nodeCount) {
		switch (node) {
			case TSGroup group when node.GetType() == typeof(TSGroup):
				group.Transform = RemapTransformId(group.Transform, map);
				return;
			case TSBSPPart bsp:
				var transforms = bsp.Transforms ?? [];
				for (int i = 0; i < transforms.Length; i++) {
					short id = transforms[i];
					if (id > 0 && id < nodeCount && id < map.Length && map[id] >= 0) {
						transforms[i] = map[id];
					}
				}

				break;
			case TSCellAnimPart cell:
				cell.Transform = RemapTransformId(cell.Transform, map);
				break;
			case TSPartList:
				break;
			default:
				return;
		}

		foreach (var child in ((TSPartList)node).Parts ?? []) {
			RemapTransformIds(child, map, nodeCount);
		}
	}

	/// <summary><c>Shape_RemapTransformId</c> (<c>00019aa0</c>): a negative id stays; any other becomes its signed map byte, so an unset one becomes -1.</summary>
	private static short RemapTransformId(short id, sbyte[] map) => id < 0 ? id : id < map.Length ? map[id] : (short)-1;

	/// <summary>The part of <paramref name="partId"/> under <paramref name="node"/>, depth first, as the part lists' find slots search.</summary>
	public static TSBasePart? FindPart(TSObject? node, int partId) {
		if (node is not TSBasePart part) {
			return null;
		}

		if (part.IdNumber == partId) {
			return part;
		}

		if (part is TSPartList { Parts: { } children }) {
			foreach (var child in children) {
				if (FindPart(child, partId) is { } found) {
					return found;
				}
			}
		}

		return null;
	}
}
