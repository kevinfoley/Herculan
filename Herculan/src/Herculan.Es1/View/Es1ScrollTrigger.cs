namespace Herculan.Es1.View;

/// <summary>
/// A screen-edge strip, <c>CockpitView_BuildScrollTriggers</c>'s <c>0x24</c>-byte object
/// (docs/es1/simulation/cockpit-panel.md#the-screen-edges-and-the-cursors): a button on <c>ScrollTriggerVtable</c>
/// (<c>ES1DBSIM 00093046</c>), the button base's table with the click <c>ScrollTrigger_OnClick</c> (<c>0002db04</c>),
/// which hands the strip to its cockpit's <c>CockpitView_HandleEdgeTrigger</c>.
/// </summary>
public sealed class Es1ScrollTrigger : Es1ButtonWidget {
	/// <param name="cockpit"><c>+0x20</c>, the cockpit that owns it.</param>
	internal Es1ScrollTrigger(Es1Cockpit cockpit) {
		Cockpit = cockpit;
	}

	/// <summary><c>+0x20</c>.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary><c>ScrollTrigger_OnClick</c>(strip, value): <c>CockpitView_HandleEdgeTrigger</c>(the cockpit, the strip), whatever the buttons.</summary>
	public override void OnClick(short value) => Cockpit.HandleEdgeTrigger(this);
}
