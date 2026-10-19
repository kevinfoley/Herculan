using Herculan.Engine.Input;
using Herculan.Es1.Input;
using Xunit;

namespace Herculan.Engine.Tests;

public class Es1ControllerPortTests {
	/// <summary>
	/// The port takes each axis from its role, not its device index: with the rudder role on device axis 5, port axis 2
	/// reads that axis, and with the throttle role unmapped, port axis 3 does not answer. Catches the rudder and throttle
	/// roles swapped onto axes 2 and 3, an axis read by device index, and an unmapped role answering detection.
	/// </summary>
	[Fact]
	public void PortAxesTakeTheMapsRolesAndAnUnmappedRoleDoesNotAnswer() {
		var map = new JoystickDeviceMap { StickXAxis = 0, StickYAxis = 1, RudderAxis = 5, ThrottleAxis = JoystickDeviceMap.Unmapped };
		float[] device = [0.25f, -1f, 0f, 0f, 0f, 0.5f];
		var port = new Es1ControllerPort(() => map, index => device[index], _ => false);

		Assert.Equal([true, true, true, false], Enumerable.Range(0, 4).Select(port.AxisAnswers));
		Assert.Equal(32, port.ReadAxis(0));
		Assert.Equal(-128, port.ReadAxis(1));
		Assert.Equal(64, port.ReadAxis(2));
		Assert.Equal(0, port.ReadAxis(3));
	}
}
