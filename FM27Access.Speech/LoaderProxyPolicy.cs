#nullable enable
namespace FM27Access.Speech;

/// <summary>One supported Doorstop proxy per installation; pinned bytes are identical.</summary>
public static class LoaderProxyPolicy
{
    public const string ByStore = "by-store";
    public static string ForStore(string store) => store == "Xbox" ? "winhttp.dll" : "version.dll";
    public static string InstalledPath(string packagePath, string store, string? policy) =>
        policy == ByStore && (packagePath is "version.dll" or "winhttp.dll") ? ForStore(store) : packagePath;
}
