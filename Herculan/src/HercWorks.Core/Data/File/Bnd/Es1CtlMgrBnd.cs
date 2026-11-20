namespace HercWorks.Core.Data.File.Bnd;

/// <summary>
/// ES1's shell <c>BND\CTL_MGR.BND</c>, which <c>CtlMgr_LoadBnd</c> (<c>ES1GO 00010530</c>) reads into <c>0009adec</c>.
/// Layout and values: docs/es1/formats/bnd-notes.md#the-shells-files.
/// </summary>
public sealed class Es1CtlMgrBnd : Es1TuningView {
	/// <summary>The global's length, the retail file's.</summary>
	public const int Length = 6;

	public Es1CtlMgrBnd(byte[]? bytes) : base(bytes, Length) {
	}

	/// <summary>Offset 4, <c>CtlMgr_QueueSize</c> (<c>0009adf0</c>), the size of the shell's mouse queues.</summary>
	public short QueueSize => Int16(4);
}
