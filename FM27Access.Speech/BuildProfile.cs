namespace FM27Access.Speech;

/// <summary>Capabilities fixed at compilation, never enabled by an HTTP request.</summary>
public static partial class BuildProfile
{
#if FM27_DEVELOPER_TOOLS
    public const bool DeveloperTools = true;
#else
    public const bool DeveloperTools = false;
#endif
}
