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
using Herculan.Engine.Content;

namespace Herculan.Es1.Sim;

/// <summary>
/// One chassis' type record, <c>0x1c4</c> bytes, as <c>MechType_InitOne</c> (<c>ES1DBSIM 00019cf0</c>) fills it
/// (docs/es1/formats/chassis-dat.md#the-load): the chassis file, the shape's roots and each root's hardpoint slots, the
/// gun layout and the texture bank.
///
/// <para>The low-memory skip of the first roots and texture banks is not taken: the engine runs as a machine with the
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

	/// <summary>A record on <paramref name="chassis"/> with no shape roots, posed through <paramref name="animation"/>; for tests.</summary>
	internal Es1MechType(Es1HercSimDat chassis, ShapeAnimation animation) {
		Name = "";
		Chassis = chassis;
		Roots = [];
		Animation = animation;
		HardpointSlots = [];
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

	/// <summary>
	/// <c>+0xc2</c>, the paper doll <c>PaperDoll_Load</c> (<c>00030388</c>) reads from <c>pdg\&lt;name&gt;</c>
	/// (docs/es1/simulation/paper-doll.md#the-records): its views' origins and sizes and their regions, as the file
	/// states them; null when the install lacks the file.
	/// </summary>
	public PaperDollGraphic? PaperDoll { get; private init; }

	/// <summary>
	/// The frames of <c>dba\&lt;name&gt;</c>, of which <c>PaperDoll_Load</c> gives view <i>v</i> frame <i>v</i> as its
	/// picture; null when the install lacks the file.
	/// </summary>
	public IReadOnlyList<DynamixBitmap>? PaperDollArt { get; private init; }

	/// <summary>View <paramref name="view"/>'s picture, frame <paramref name="view"/> of <see cref="PaperDollArt"/>; null when either is missing.</summary>
	public DynamixBitmap? PaperDollPicture(int view) =>
		PaperDollArt is { } art && view >= 0 && view < art.Count ? art[view] : null;

	/// <summary>The chassis' cockpit name, the 8 characters at the record's <c>+0x58</c> (file offset 86), upper-cased.</summary>
	public string CockpitName {
		get {
			var bytes = Chassis.NameBytes.Take(8).TakeWhile(b => b != 0).ToArray();
			return Encoding.ASCII.GetString(bytes).ToUpperInvariant();
		}
	}

	/// <summary>
	/// <c>dmg\&lt;name&gt;</c> (docs/es1/formats/dmg-damage-file.md), which <c>Mech_Constructor</c> loads into each machine's
	/// <c>+0x190</c>; null when the install lacks it.
	/// </summary>
	public HercSimDamage? DamageTable { get; private init; }

	/// <summary>
	/// <c>col\&lt;name&gt;</c>'s sphere model with its clusters' bounds (<see cref="Es1HitSpheres.ReadModel"/>), which <c>Mech_Constructor</c> loads into each machine's <c>+0x18c</c>; empty when the
	/// install lacks it. A machine's loadout replaces clusters in a copy, so the type's is shared unchanged.
	/// </summary>
	public ColliderNode[] CollisionModel { get; private init; } = [];

	/// <summary>Root 0's bounding radius, <c>shape+8</c>, which the LOD choice reads whichever root was drawn last.</summary>
	public int ShapeRadius => Roots.Count > 0 && Roots[0] is TSBasePart root ? root.Radius : 0;

	/// <summary>
	/// <c>MechType_InitOne</c> for type <paramref name="type"/>, given its slot, with its last step, <c>PaperDoll_Load</c>.
	/// Null when <c>dat\&lt;name&gt;</c> or
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

		MechDetailRootRemap.Apply(roots, chassis.ModelLoDBoneIds);
		var gunLayout = content.Read("gl", name + ".GL") is { } glBytes ? new GunLayoutTransformer().Parse(glBytes) : null;
		int group = chassis.TextureGroup;
		var bank = group >= 0 && group < TextureBanks.Count && content.Read("dba", TextureBanks[group] + ".DBA") is { } bankBytes
			? new DynamixBitmapArrayTransformer().Parse(bankBytes)
			: null;

		return new Es1MechType(type, slot, name, chassis, roots, gunLayout) {
			TextureBank = bank,
			PaperDollArt = content.Read("dba", name + ".DBA") is { } artBytes ? new DynamixBitmapArrayTransformer().Parse(artBytes)?.Images : null,
			PaperDoll = content.Read("pdg", name + ".PDG") is { } dollBytes ? new PaperDiagramGraphTransformer().Parse(dollBytes) : null,
			DamageTable = content.Read("dmg", name + ".DMG") is { } dmgBytes ? new HercDamageFileTransformer().Parse(dmgBytes) : null,
			CollisionModel = Es1HitSpheres.ReadModel(content.Read("col", name + ".COL")),
		};
	}

	/// <summary>
	/// <c>GunLayout_CollectHardpointBones</c> (<c>0000befc</c>) and <c>MechType_BindHardpointSlots</c> for one root: each
	/// hardpoint's bone id, -1 for the invisible mounting, resolved to the part slot holding the part of that id through
	/// the shape's slot <c>+0x28</c>, null where the root has none (docs/retail/rendering/mech-shape-drawing.md).
	/// </summary>
	private short?[] BindHardpointSlots(TSObject root) {
		var hardpoints = GunLayout?.Hardpoints ?? [];
		return hardpoints.Select(h => h.MountingCode == InvisibleMounting || MechDetailRootRemap.FindPart(root, h.BoneId) == null
			? (short?)null
			: h.BoneId).ToArray();
	}
}
