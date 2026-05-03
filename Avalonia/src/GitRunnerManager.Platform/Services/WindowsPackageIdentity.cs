using System.Runtime.InteropServices;

namespace GitRunnerManager.Platform.Services;

internal static class WindowsPackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;

    internal static Func<bool>? DetectorOverride { get; set; }

    public static bool IsPackagedApp
    {
        get
        {
            if (DetectorOverride != null)
                return DetectorOverride();

            if (!OperatingSystem.IsWindows())
                return false;

            uint packageFullNameLength = 0;
            var result = GetCurrentPackageFullName(ref packageFullNameLength, null);
            return result == ErrorInsufficientBuffer;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, string? packageFullName);
}
