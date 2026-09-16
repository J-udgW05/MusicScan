using System.Runtime.ExceptionServices;

namespace MusicScanIntegrity.App.Tests;

/// <summary>Runs test code on an STA thread.</summary>
/// <remarks>
/// WPF elements live only on STA threads while xUnit runs tests on MTA ones.
/// The mechanism is a few lines, not worth an extra package.
/// </remarks>
internal static class Sta
{
    /// <summary>Runs the action on an STA thread and waits for it.</summary>
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;

        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Otherwise an exception from the other thread would be lost and the test
                // would pass on broken code.
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        failure?.Throw();
    }
}
