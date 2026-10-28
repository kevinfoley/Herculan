using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Anim;
using Herculan.Engine.Content;

namespace Herculan.Es1.Sim;

/// <summary>
/// One flyer type's <c>0x2e</c>-byte record at <c>ES1DBSIM 000bc46c</c>, as <c>FlyerType_LoadResources</c>
/// (<c>0001c6c8</c>) fills it (docs/es1/formats/bullets-skimmer-dat.md#the-load-1): <c>dat\&lt;name&gt;</c>'s 34 bytes,
/// the damage table, the collision model and the shape.
/// </summary>
public sealed class Es1FlyerType {
	private Es1FlyerType(short type, int recordIndex, string name, Es1FlyerSimData data, TSObject shape) {
		Type = type;
		RecordIndex = recordIndex;
		Name = name;
		Data = data;
		Shape = shape;
		Animation = ShapeAnimation.FromRoot(shape);
	}

	/// <summary>The type number, <c>FLYERS.NAM</c>'s index.</summary>
	public short Type { get; }

	/// <summary>The record's place in the records, <c>FlyerType_RecordIndex</c>'s answer.</summary>
	public int RecordIndex { get; }

	/// <summary>The name <c>000bc468</c> holds for the type, which names its <c>dat\</c>, <c>dts\</c>, <c>dmg\</c> and <c>col\</c> files.</summary>
	public string Name { get; }

	/// <summary><c>+0x00</c>–<c>+0x21</c>, <c>dat\&lt;name&gt;</c> (docs/es1/formats/bullets-skimmer-dat.md#datskimmerdat).</summary>
	public Es1FlyerSimData Data { get; }

	/// <summary><c>+0x22</c>, <c>dmg\&lt;name&gt;</c>; null when the install lacks it, where retail asserts.</summary>
	public HercSimDamage? DamageTable { get; private init; }

	/// <summary><c>+0x26</c>, <c>col\&lt;name&gt;</c>'s sphere model; empty when the install lacks it.</summary>
	public ColliderNode[] CollisionModel { get; private init; } = [];

	/// <summary>
	/// <c>+0x2a</c>, the object <c>Shape_LoadDts</c> (<c>0006351c</c>) reads from <c>dts\&lt;name&gt;</c>, the file's
	/// first, which every flyer of the type is drawn from whole, with no LOD table.
	/// </summary>
	public TSObject Shape { get; }

	/// <summary>The shape's nodes at rest, which its static instance draws them at.</summary>
	public ShapeAnimation? Animation { get; }

	/// <summary>
	/// The shape's <c>+0x26</c>, the bank its textured faces take: <c>dba\enemy</c>, the fourth chassis texture bank
	/// (<c>000bc45c</c>, docs/es1/formats/chassis-dat.md#the-load), whatever the type; null when the install lacks it.
	/// </summary>
	public DynamixBitmapArray? TextureBank { get; private init; }

	/// <summary>The shape's bounding radius, <c>shape+8</c>, which slot <c>+0x14</c> and the gunsight's box read.</summary>
	public int ShapeRadius => Shape is TSBasePart part ? part.Radius : 0;

	/// <summary>
	/// <c>FlyerType_LoadResources</c>(<paramref name="type"/>): 34 bytes of <c>dat\&lt;name&gt;</c>, <c>dts\&lt;name&gt;</c>
	/// with the <c>dba\enemy</c> bank, <c>dmg\&lt;name&gt;</c> (<c>Damage_LoadMechDmgFile</c>) and <c>col\&lt;name&gt;</c>
	/// (<c>Collision_RegisterObject</c>). Null when <c>dat\&lt;name&gt;</c> or <c>dts\&lt;name&gt;</c> is missing, where
	/// <c>Ovl_ReadFile</c> asserts at severity 5; <c>HOVTANK</c> has no <c>dat\</c> file
	/// (docs/es1/formats/bullets-skimmer-dat.md#open).
	/// </summary>
	internal static Es1FlyerType? Load(GameContent content, string name, short type, int recordIndex,
			DynamixBitmapArray? bank) {
		if (content.Read("dat", name + ".DAT") is not { } datBytes
				|| new Es1FlyerSimDataTransformer().Parse(datBytes) is not { } data
				|| content.Read("dts", name + ".DTS") is not { } dtsBytes
				|| new DTSModelTransformer().Parse(dtsBytes) is not DynamixThreeSpaceModel { Roots: [var shape, ..] }) {
			return null;
		}

		return new Es1FlyerType(type, recordIndex, name, data, shape) {
			TextureBank = bank,
			DamageTable = content.Read("dmg", name + ".DMG") is { } dmgBytes ? new HercDamageFileTransformer().Parse(dmgBytes) : null,
			CollisionModel = Es1HitSpheres.ReadModel(content.Read("col", name + ".COL")),
		};
	}
}

/// <summary>
/// <c>flyersys.cpp</c>'s type tables (docs/es1/formats/bullets-skimmer-dat.md#the-load-1): the two-byte type-slot table
/// <c>000bc466</c>, the count <c>000bc464</c> and the records <c>000bc46c</c>, which the first pass's
/// <see cref="Register"/> and <see cref="LoadAll"/> fill.
/// </summary>
public sealed class Es1FlyerTypes {
	/// <summary>The type numbers the slot table has room for, which <c>FlyerTypes_LoadAll</c> walks.</summary>
	public const int TypeCount = 2;

	/// <summary>The slot table's byte for a type with no record.</summary>
	public const byte NoRecord = 0xff;

	/// <summary>The bank every flyer shape takes, the fourth of <see cref="Es1MechType.TextureBanks"/>.</summary>
	public const string BankName = "ENEMY";

	private readonly byte[] _recordIndex = [NoRecord, NoRecord];
	private readonly Dictionary<int, Es1FlyerType> _records = new();

	/// <summary><c>000bc464</c>, how many types have a record.</summary>
	public short Count { get; private set; }

	/// <summary>
	/// <c>FlyerType_RecordIndex</c> (<c>0001c678</c>)(<paramref name="type"/>): the type's byte in the slot table,
	/// <see cref="NoRecord"/> when it has none. Retail indexes the two bytes unchecked; a type outside them answers
	/// <see cref="NoRecord"/> here, an engine guard.
	/// </summary>
	public byte RecordIndex(short type) => type is >= 0 and < TypeCount ? _recordIndex[type] : NoRecord;

	/// <summary>
	/// <c>FlyerType_Register</c> (<c>0001c688</c>)(<paramref name="type"/>): a type with no record takes the count as its
	/// record and the count goes up by 1; the <c>.DMG</c> and collision slots it reserves are the type's own fields here.
	/// Retail writes a type outside the two bytes unchecked, over the name list's pointer; such a type registers nothing
	/// here, an engine guard.
	/// </summary>
	public void Register(short type) {
		if (type is >= 0 and < TypeCount && _recordIndex[type] == NoRecord) {
			_recordIndex[type] = (byte)Count++;
		}
	}

	/// <summary>The record of <paramref name="type"/>, or null with none or with its files missing.</summary>
	public Es1FlyerType? this[short type] =>
		RecordIndex(type) is var index and not NoRecord ? _records.GetValueOrDefault(index) : null;

	/// <summary>
	/// <c>FlyerTypes_LoadAll</c> (<c>0001c770</c>): with a type registered, <c>nam\flyers</c>' names, then
	/// <c>FlyerType_LoadResources</c> for each of types 0 and 1 that has a record.
	/// </summary>
	public void LoadAll(GameContent content) {
		if (Count == 0) {
			return;
		}

		var names = new NameListTransformer().Parse(content.ReadRequired("nam", "FLYERS.NAM"))
			?? throw new InvalidDataException("nam\\FLYERS.NAM does not read.");
		// Mech_LoadResources' fourth bank, 000bc45c, which each flyer shape's +0x26 takes.
		var bank = content.Read("dba", BankName + ".DBA") is { } bankBytes
			? new DynamixBitmapArrayTransformer().Parse(bankBytes)
			: null;
		for (short type = 0; type < TypeCount; type++) {
			byte index = RecordIndex(type);
			if (index != NoRecord && names[type] is { } name && Es1FlyerType.Load(content, name, type, index, bank) is { } record) {
				_records[index] = record;
			}
		}
	}

	/// <summary>The types loaded, in record order.</summary>
	public IEnumerable<Es1FlyerType> Loaded => _records.OrderBy(entry => entry.Key).Select(entry => entry.Value);
}
