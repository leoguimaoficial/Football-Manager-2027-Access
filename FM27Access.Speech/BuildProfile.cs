namespace FM27Access.Speech;

/// <summary>Capabilities fixed at compilation, never enabled by an HTTP request.</summary>
public static class BuildProfile
{
    public const string Version = "0.1.0";
#if FM27_DEVELOPER_TOOLS
    public const bool DeveloperTools = true;
#else
    public const bool DeveloperTools = false;
#endif
}
