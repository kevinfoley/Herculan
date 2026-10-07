namespace Herculan.Engine.Sim;

/// <summary>
/// A volume and rolloff that a positional play uses in place of its catalog row's attribute bytes 1,
/// 4 and 5. Not the original's: DBSIM always places a sound by its row. It exists for a tweak that
/// moves a sound somewhere its row's range was never chosen for — see
/// <see cref="MeteorObject.TweakSoundReach"/>.
/// </summary>
/// <param name="Volume">0-100, as attribute byte 1; it goes through the same headroom trim.</param>
/// <param name="MinRange">Where rolloff starts, in world units rather than byte 4's 1024s; null keeps the row's.</param>
/// <param name="MaxRange">The cutoff, in world units rather than byte 5's 1024s.</param>
public readonly record struct SoundReach(int Volume, int? MinRange, int MaxRange);
