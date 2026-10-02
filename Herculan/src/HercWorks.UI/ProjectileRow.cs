using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct;

namespace HercWorks.UI;

/// <summary>
/// Editable grid-row shape for one PROJ.DAT record (<see cref="ProjectileData.Projectile"/>), with
/// the type as its raw id so the grid's combo column can bind to it and each impact-effect array
/// flattened into four columns, in file order.
/// </summary>
public class ProjectileRow {
	/// <summary>The record's position in the file — what a weapon template's PROJ.DAT index names.</summary>
	public int Index { get; set; }

	/// <inheritdoc cref="ProjectileData.Projectile.Type"/>
	public short Type { get; set; }

	/// <inheritdoc cref="ProjectileData.Projectile.SubtypeId"/>
	public short SubtypeId { get; set; }

	public short DamageShield { get; set; }
	public short DamageArmor { get; set; }

	/// <inheritdoc cref="ProjectileData.Projectile.SplashFactor"/>
	public short SplashFactor { get; set; }

	public short Speed { get; set; }

	/// <inheritdoc cref="ProjectileData.Projectile.ImpactFXShield"/>
	public short ShieldFx0 { get; set; }
	public short ShieldFx1 { get; set; }
	public short ShieldFx2 { get; set; }
	public short ShieldFx3 { get; set; }

	/// <inheritdoc cref="ProjectileData.Projectile.ImpactFXGround"/>
	public short GroundFx0 { get; set; }
	public short GroundFx1 { get; set; }
	public short GroundFx2 { get; set; }
	public short GroundFx3 { get; set; }

	/// <inheritdoc cref="ProjectileData.Projectile.ImpactFXArmor"/>
	public short ArmorFx0 { get; set; }
	public short ArmorFx1 { get; set; }
	public short ArmorFx2 { get; set; }
	public short ArmorFx3 { get; set; }

	public static ProjectileRow FromRecord(int index, ProjectileData.Projectile record) => new() {
		Index = index,
		Type = record.Type?.Val
			?? throw new InvalidDataException($"Record {index} has a Type other than 0, 2, 3 or 4."),
		SubtypeId = record.SubtypeId,
		DamageShield = record.DamageShield,
		DamageArmor = record.DamageArmor,
		SplashFactor = record.SplashFactor,
		Speed = record.Speed,
		ShieldFx0 = record.ImpactFXShield[0],
		ShieldFx1 = record.ImpactFXShield[1],
		ShieldFx2 = record.ImpactFXShield[2],
		ShieldFx3 = record.ImpactFXShield[3],
		GroundFx0 = record.ImpactFXGround[0],
		GroundFx1 = record.ImpactFXGround[1],
		GroundFx2 = record.ImpactFXGround[2],
		GroundFx3 = record.ImpactFXGround[3],
		ArmorFx0 = record.ImpactFXArmor[0],
		ArmorFx1 = record.ImpactFXArmor[1],
		ArmorFx2 = record.ImpactFXArmor[2],
		ArmorFx3 = record.ImpactFXArmor[3]
	};

	public ProjectileData.Projectile ToRecord() => new() {
		Type = ProjectileType.ForId(Type),
		SubtypeId = SubtypeId,
		DamageShield = DamageShield,
		DamageArmor = DamageArmor,
		SplashFactor = SplashFactor,
		Speed = Speed,
		ImpactFXShield = new[] { ShieldFx0, ShieldFx1, ShieldFx2, ShieldFx3 },
		ImpactFXGround = new[] { GroundFx0, GroundFx1, GroundFx2, GroundFx3 },
		ImpactFXArmor = new[] { ArmorFx0, ArmorFx1, ArmorFx2, ArmorFx3 }
	};
}
