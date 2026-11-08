namespace Herculan.Engine.Render;

/// <summary>
/// How a game files the object an item or billboard batch draws, which decides per pass whether it is drawn at all
/// (<see cref="SceneItem.Filing"/>, <see cref="SpriteBatch.Filing"/>). Every item of one object shares one filing.
/// </summary>
public interface IDrawFiling {
	/// <summary>Whether this pass draws the object.</summary>
	bool Drawn { get; }
}
