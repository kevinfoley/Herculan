namespace Herculan.Engine.Host.Install;

/// <summary>
/// An <see cref="IProgress{T}"/> that runs <paramref name="report"/> on the reporting thread itself.
/// <see cref="Progress{T}"/> posts to the creating thread's synchronization context, which neither the console nor
/// the ImGui frame loop has.
/// </summary>
sealed class ActionProgress<T>(Action<T> report) : IProgress<T> {
	public void Report(T value) => report(value);
}
