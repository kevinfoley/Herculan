namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - a simulator's <c>snc\*.SNC</c> portrait lip-sync script, ES2's layout, which ES1's simulator reads too
/// (docs/retail/formats/snc-lip-sync.md#layout, docs/es1/formats/snc-lip-sync.md#the-simulators-scripts). Read by
/// <see cref="Io.Transform.Dbsim.LipSyncScriptTransformer"/>.
/// </summary>
public class LipSyncScript {
	/// <summary>The <c>int32</c> length as read: twice the events' count in every retail file.</summary>
	public int Length { get; set; }

	/// <summary>The script's events in order, <see cref="Length"/> / 2 of them.</summary>
	public LipSyncEvent[] Events { get; set; } = [];

	/// <summary>
	/// Bytes after the events: an odd length's last byte and anything past the length. Empty in every retail file; kept so a
	/// written script gives back its bytes.
	/// </summary>
	public byte[] Unread { get; set; } = [];
}

/// <summary>One portrait frame and how long it shows.</summary>
public class LipSyncEvent {
	/// <summary>The frame of the speaker's portrait bank.</summary>
	public byte Frame { get; set; }

	/// <summary>Coarse ticks until the next event.</summary>
	public byte Delta { get; set; }
}
