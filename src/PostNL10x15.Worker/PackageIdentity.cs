using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PostNL10x15.Worker;

internal static class PackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    public static string GetLocalStatePath()
    {
        string? overridePath = Environment.GetEnvironmentVariable(
            "POSTNL10X15_LOCALSTATE");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        string familyName = GetCurrentPackageFamilyName();
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Packages",
            familyName,
            "LocalState");
    }

    private static string GetCurrentPackageFamilyName()
    {
        uint length = 0;
        int result = GetCurrentPackageFamilyName(ref length, null);
        if (result == AppModelErrorNoPackage)
        {
            throw new InvalidOperationException(
                "De inbox-worker moet vanuit het geinstalleerde "
                + "virtuele-printerpakket worden gestart.");
        }

        if (result != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(result);
        }

        var value = new StringBuilder(checked((int)length));
        result = GetCurrentPackageFamilyName(ref length, value);
        if (result != 0)
        {
            throw new Win32Exception(result);
        }

        return value.ToString();
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetCurrentPackageFamilyName",
        CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(
        ref uint packageFamilyNameLength,
        StringBuilder? packageFamilyName);
}
