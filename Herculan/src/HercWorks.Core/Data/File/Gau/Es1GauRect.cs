namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// One 16-byte rect of ES1's cockpit layout (<see cref="Es1GauFile"/>): four <c>int32</c>, the top-left corner then the
/// bottom-right, as the file holds them.
/// </summary>
public readonly record struct Es1GauRect(int X0, int Y0, int X1, int Y1);
