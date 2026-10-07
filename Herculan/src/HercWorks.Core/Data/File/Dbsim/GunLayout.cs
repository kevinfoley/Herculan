namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - /SIMVOL0/GL/{herc}.GL — 'Gun Layout', the chassis' hardpoint list: an <c>int16</c> count,
/// then that many 26-byte <see cref="HardpointEntry"/> records. Layout and field roles:
/// docs/retail/formats/gun-layout-gl.md.
/// </summary>
public class GunLayout {
	public short TotalGuns { get; set; }
	public HardpointEntry[]? Hardpoints { get; set; }

	public GunLayout() { }

	public GunLayout(short total) {
		TotalGuns = total;
		Hardpoints = new HardpointEntry[total];
	}

	public HardpointEntry NewEntry() => new();

	public class HardpointEntry {
		/// <summary><c>+0x00</c> — the model bone the gun rides and the shot leaves from.</summary>
		public short BoneId { get; set; }

		/// <summary>
		/// <c>+0x02</c> — convergence pitch node. Negative on every retail chassis, which is what lets
		/// gun convergence apply. See docs/retail/formats/gun-layout-gl.md#record.
		/// </summary>
		public short ConvergencePitchNode { get; set; }

		/// <summary><c>+0x04</c> — convergence yaw node, as <see cref="ConvergencePitchNode"/>.</summary>
		public short ConvergenceYawNode { get; set; }

		/// <summary>
		/// <c>+0x06</c> — mounting code: 0 on top, 1 underneath, 2 left side, 3 right side,
		/// 4 invisible. Picks the weapon's model shape and the side of the muzzle offset.
		/// </summary>
		public byte MountingCode { get; set; }

		/// <summary><c>+0x07</c> — the cockpit weapon row this mount owns; the panel prints it as <c>n+1</c>.</summary>
		public byte FireChainNumber { get; set; }

		/// <summary><c>+0x08</c>-<c>+0x0f</c> — no assigned role; see docs/retail/formats/gun-layout-gl.md#open.</summary>
		public short Unk3_0or_Neg5000 { get; set; }

		/// <inheritdoc cref="Unk3_0or_Neg5000"/>
		public short Unk4_0or_5000 { get; set; }

		/// <inheritdoc cref="Unk3_0or_Neg5000"/>
		public short Unk5_Neg8000 { get; set; }

		/// <inheritdoc cref="Unk3_0or_Neg5000"/>
		public short Unk6_16000 { get; set; }

		/// <summary><c>+0x10</c> — mount-point offset X, Y, Z in the bone's space.</summary>
		public short[] Offset { get; set; } = new short[3];

		/// <summary>
		/// <c>+0x16</c> — signed: how far away in the mount array this hardpoint's LINK partner sits.
		/// Retail pairs mirrored left/right hardpoints with ±1.
		/// </summary>
		public byte LinkPartnerOffset { get; set; }

		/// <summary>
		/// <c>+0x17</c> — fit slot: the index into the mission's weapon-id and ammunition-type arrays,
		/// which also names the mount's damage component (slot + 19).
		/// </summary>
		public byte LoadoutSlot { get; set; }

		/// <summary>
		/// <c>+0x18</c> — the pitch the gun's model is thrown at when the mount is destroyed. See
		/// docs/retail/simulation/destruction-effects.md.
		/// </summary>
		public short DebrisPitch { get; set; }
	}
}
