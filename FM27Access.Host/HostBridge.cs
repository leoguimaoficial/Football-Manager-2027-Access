using System;
using FM27Access.Speech;

namespace FM27Access.Host;

/// <summary>
/// The concrete <see cref="IHostBridge"/> handed to core's <c>Mount</c>. Owns the two dev-driver
/// ring buffers (so they survive reloads) and routes a reload request onto the host main-thread
/// pump. A single instance lives for the whole process; <see cref="Instance"/> lets the injected
/// <see cref="HostBehaviour"/> reach the current core tick.
/// </summary>
internal sealed class HostBridge : IHostBridge
{
    public static HostBridge Instance { get; set; }

    public Action Tick { get; set; }

#if FM27_DEVELOPER_TOOLS
    public SpeechLog SpeechHistory { get; } = new();
    public SpeechLog LogHistory { get; } = new();
#else
    public SpeechLog SpeechHistory => null;
    public SpeechLog LogHistory => null;
#endif

    public string CompanionSessionToken { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public bool IsFirstMount => CoreHost.IsFirstMount;

    public void RequestReload() => HostDispatcher.Post(CoreHost.Reload);
}
