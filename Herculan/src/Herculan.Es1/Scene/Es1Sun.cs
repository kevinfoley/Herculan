using System.Numerics;
using Herculan.Engine.Render;

namespace Herculan.Es1.Scene;

/// <summary>
/// The sun <c>Light_CreateMissionSun</c> (<c>ES1DBSIM 0005a888</c>) builds once per mission from constants, the only light a
/// zone shape takes besides effect lights (docs/es1/rendering/polygon-shading.md#the-sun). Its direction is shorter
/// than ES2's, so a face's light rises half as steeply with its facing
/// (docs/es1/rendering/polygon-shading.md#a-faces-light).
/// </summary>
public static class Es1Sun {
	/// <summary>The sun's intensity, the <c>0x100</c> its setter is called with.</summary>
	public const int Intensity = 0x100;

	/// <summary>The length of the vector <c>(0, 0x800, 0)</c> the sun's direction is rotated from.</summary>
	public const int DirectionLength = 0x800;

	/// <summary>The direction the light travels, in render space: <c>(0, 0x800, 0)</c> turned by the angles (−8000, 0, 24000).</summary>
	public static Vector3 Direction { get; } = MissionSun.DirectionFor(-8000, 24000);

	/// <summary>A face's light is <c>128 + ShadeSlope · facing</c>, 128 here.</summary>
	public static int ShadeSlope { get; } = MissionSun.FaceShadeSlopeFor(Intensity, DirectionLength);
}
