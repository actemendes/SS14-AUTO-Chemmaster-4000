using System.Diagnostics;

// Compatibility facade for the existing CLI. All selection and DAC validation
// is centralized in ClientDiscovery.
internal static class Ss14ClientConnection
{
    public static Process Open(int? requestedPid) => ClientDiscovery.Open(requestedPid);

    public static string FindDac(Process process) => ClientDiscovery.FindDac(process);
}
