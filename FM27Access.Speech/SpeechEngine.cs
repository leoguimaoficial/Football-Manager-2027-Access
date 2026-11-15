using System.Runtime.InteropServices;

namespace FM27Access.Speech;

public static class SpeechEngine
{
    private static IntPtr _context;
    private static IntPtr _backend;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            throw new InvalidOperationException("SpeechEngine.Initialize() called twice.");

        var config = prism_config_init();

        _context = prism_init(ref config);
        if (_context == IntPtr.Zero)
        {
            Log.Fatal("Prism context creation failed (prism_init returned null).");
            throw new InvalidOperationException(
                "FM27Access: Prism context creation failed. The mod cannot function without speech.");
        }

        Log.Info("Prism context created.");

        _backend = prism_registry_acquire_best(_context);
        if (_backend == IntPtr.Zero)
        {
            Log.Fatal("No Prism backend available (prism_registry_acquire_best returned null).");
            prism_shutdown(_context);
            _context = IntPtr.Zero;
            throw new InvalidOperationException(
                "FM27Access: No speech backend available. Is a screen reader or SAPI voice installed?");
        }

        var namePtr = prism_backend_name(_backend);
        var name = namePtr != IntPtr.Zero
            ? Marshal.PtrToStringUTF8(namePtr)
            : "unknown";
        Log.Info($"Prism backend acquired: {name}");

        var err = prism_backend_initialize(_backend);
        if (err == PrismError.AlreadyInitialized)
        {
            Log.Info("Prism backend was already initialized (shared instance via acquire).");
        }
        else if (err != PrismError.Ok)
        {
            var errMsg = GetErrorString(err);
            Log.Fatal($"Prism backend initialization failed: {errMsg}");
            prism_backend_free(_backend);
            prism_shutdown(_context);
            _backend = IntPtr.Zero;
            _context = IntPtr.Zero;
            throw new InvalidOperationException(
                $"FM27Access: Speech backend failed to initialize: {errMsg}");
        }

        _initialized = true;
        Log.Info("SpeechEngine initialized successfully.");
    }

    public static void Speak(string text, bool interrupt)
    {
        if (!_initialized)
            throw new InvalidOperationException("SpeechEngine.Speak() called before Initialize().");

        var err = prism_backend_speak(_backend, text, interrupt);
        if (err != PrismError.Ok)
        {
            var errMsg = GetErrorString(err);
            Log.Fatal($"Prism speak failed: {errMsg} (text was: \"{text}\")");
            throw new InvalidOperationException(
                $"FM27Access: Speech output failed ({errMsg}). The mod cannot function without speech.");
        }
    }

    public static void Shutdown()
    {
        if (!_initialized) return;

        _initialized = false;

        if (_backend != IntPtr.Zero)
        {
            prism_backend_free(_backend);
            _backend = IntPtr.Zero;
        }

        if (_context != IntPtr.Zero)
        {
            prism_shutdown(_context);
            _context = IntPtr.Zero;
        }

        Log.Info("SpeechEngine shut down.");
    }

    private static string GetErrorString(PrismError err)
    {
        var ptr = prism_error_string(err);
        return ptr != IntPtr.Zero
            ? Marshal.PtrToStringUTF8(ptr) ?? err.ToString()
            : err.ToString();
    }

    // --- Native types ---

    [StructLayout(LayoutKind.Sequential)]
    private struct PrismConfig
    {
        public byte version;
        // Deprecated HWND field — must be present for correct struct layout on Windows x64.
        public IntPtr hwnd;
    }

    private enum PrismError : int
    {
        Ok = 0,
        NotInitialized = 1,
        InvalidParam = 2,
        NotImplemented = 3,
        NoVoices = 4,
        VoiceNotFound = 5,
        SpeakFailure = 6,
        MemoryFailure = 7,
        RangeOutOfBounds = 8,
        Internal = 9,
        NotSpeaking = 10,
        NotPaused = 11,
        AlreadyPaused = 12,
        InvalidUtf8 = 13,
        InvalidOperation = 14,
        AlreadyInitialized = 15,
        BackendNotAvailable = 16,
        Unknown = 17,
        InvalidAudioFormat = 18,
    }

    // --- P/Invoke ---

    [DllImport("prism", CallingConvention = CallingConvention.Cdecl)]
    private static extern PrismConfig prism_config_init();

    [DllImport("prism", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr prism_init(ref PrismConfig cfg);

    [DllImport("prism", CallingConvention = CallingConvention.Cdecl)]
    private static extern void prism_shutdown(IntPtr ctx);

    [DllImport("prism", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr prism_registry_acquire_best(IntPtr ctx);

    [DllImport("prism", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr prism_backend_name(IntPtr backend);

    [DllImport("prism", CallingConvention = CallingConvention.Cdecl)]
    private static extern PrismError prism_backend_initialize(IntPtr backend);

    [DllImport("prism", CallingConvention = CallingConvention.Cdecl)]
    private static extern PrismError prism_backend_speak(
        IntPtr backend,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        [MarshalAs(UnmanagedType.U1)] bool interrupt);

    [DllImport("prism", CallingConvention = CallingConvention.Cdecl)]
    private static extern void prism_backend_free(IntPtr backend);

    [DllImport("prism", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr prism_error_string(PrismError error);
}
