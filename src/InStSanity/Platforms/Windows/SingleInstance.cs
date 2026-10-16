namespace InStSanity.Platforms.Windows;

/// <summary>
/// Keeps one instance of the app per Windows session.
/// </summary>
/// <remarks>
/// Two instances would fight over the loopback port and the WebView2 user-data folder, so the
/// second one exits instead. Bringing the existing window to the front (instead of exiting) is
/// planned for a later phase — see <c>docs/hybrid-port-plan.md</c> Phase 4.
/// </remarks>
internal static class SingleInstance
{
	const string MutexName = @"Local\InStSanity.SingleInstance";

	// Held for the process lifetime; Windows releases it when the app exits.
	internal static Mutex? InstanceMutex;

	public static bool IsPrimaryInstance { get; private set; }

	public static void Acquire()
	{
		var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

		if (createdNew)
		{
			InstanceMutex = mutex;
			IsPrimaryInstance = true;
			return;
		}

		mutex.Dispose();
		IsPrimaryInstance = false;
	}
}
