using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Windows.Forms;

namespace FM27Access.CompanionApp;

/// <summary>
/// Standalone companion window: a WebView2 pointed at the mod's localhost accessibility
/// server. The screen reader navigates the WebView2 content in browse mode; the server's
/// HTML handles live updates (it polls a screen token and reloads on game navigation).
/// Retries until the mod's server is up (i.e. the game is running with the mod loaded).
///
/// When the mod auto-launches this window it passes "--watch-pid &lt;gamePid&gt;": the window
/// then exits when the game exits, so quitting FM26 leaves no orphaned companion behind.
/// </summary>
internal static class Program
{
    private const string ServerUrl = "http://127.0.0.1:8787/";

    [STAThread]
    private static void Main(string[] args)
    {
        // Optional positional arg: a URL or local file path to load instead of the game
        // server. Used for standalone spikes (e.g. the T1 key-passthrough test page).
        // The "--watch-pid <pid>" flag is consumed separately and is not a load target.
        string target = ServerUrl;
        bool isServer = true;
        int watchPid = -1;
        string startupError = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--startup-error" && i + 1 < args.Length)
            {
                startupError = args[++i];
                continue;
            }
            if (args[i] == "--watch-pid" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out watchPid);
                continue;
            }
            target = System.IO.File.Exists(args[i]) ? new Uri(System.IO.Path.GetFullPath(args[i])).AbsoluteUri : args[i];
            isServer = false;
        }

        ApplicationConfiguration.Initialize();
        if (startupError != null)
        {
            MessageBox.Show(startupError, "FM27 Access", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var form = new Form
        {
            Text = "FM27 Access Companion",
            Width = 960,
            Height = 760,
            StartPosition = FormStartPosition.CenterScreen,
        };

        var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);

        using var retry = new System.Windows.Forms.Timer { Interval = 2000 };
        retry.Tick += (_, _) => web.CoreWebView2?.Navigate(target);

        form.Load += async (_, _) =>
        {
            // Keep the WebView's renderer, timers, and network alive when the window is not the
            // foreground/occluded one, so the SSE connection and live updates are not throttled or
            // dropped while focus is on the game (e.g. the F9/F10 debug keys). See
            // docs/archive/plans/refresh-step-3-plan.md.
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-background-timer-throttling "
                                           + "--disable-renderer-backgrounding "
                                           + "--disable-backgrounding-occluded-windows "
                                           // Qualified on the actual Windows11/Edge157 loopback
                                           // workload: default policy repeatedly failed before
                                           // HTTP; both ordinary-port comparison rounds passed.
                                           // Keep this mitigation local to this WebView, without
                                           // changing Windows networking or retrying game commands.
                                           + "--disable-features=TcpPortRandomizationWin "
                                           // Let the match audio cues (Web Audio) start without a
                                           // user gesture, so ticking works while focus is on the game.
                                           + "--autoplay-policy=no-user-gesture-required",
            };
            CoreWebView2Environment env;
            try
            {
                var dataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FM27Access", "WebView2");
                env = await CoreWebView2Environment.CreateAsync(null, dataPath, options);
                await web.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                if (form.IsDisposed || form.Disposing) return; // Normal game-exit cancellation.
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FM27Access");
                try { FM27Access.Speech.BoundedLogFile.Append(System.IO.Path.Combine(logPath, "companion.log"), DateTime.UtcNow.ToString("o") + " " + ex); }
                catch (Exception logError) { System.Diagnostics.Trace.TraceError("Companion startup diagnostics: " + logError); }
                MessageBox.Show(form, "Não foi possível iniciar o WebView2. Consulte o arquivo companion.log em AppData/Local/FM27Access.", "FM27 Access", MessageBoxButtons.OK, MessageBoxIcon.Error);
                form.Close();
                return;
            }
#if FM27_DEVELOPER_TOOLS
            web.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
            web.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                // Only the game server needs retrying until it's up; a fixed file/URL doesn't.
                if (!e.IsSuccess && isServer) retry.Start();
                else retry.Stop();
#if FM27_DEVELOPER_TOOLS
                _ = WriteNavigationEvidence(web, e);
#endif
            };
            web.CoreWebView2.Navigate(target);
        };

        if (watchPid >= 0 && !WatchGame(watchPid, form)) return;

        Application.Run(form);
    }

#if FM27_DEVELOPER_TOOLS
    private static async Task WriteNavigationEvidence(WebView2 web, CoreWebView2NavigationCompletedEventArgs e)
    {
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FM27Access");
        try
        {
            System.IO.Directory.CreateDirectory(logPath);
            string evidence = e.IsSuccess
                ? await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({root:!!document.getElementById('fmroot'),client:!!(window.FM&&FM.core),buttons:document.querySelectorAll('#fmroot button').length})")
                : e.WebErrorStatus.ToString();
            FM27Access.Speech.BoundedLogFile.Append(System.IO.Path.Combine(logPath, "companion.log"), DateTime.UtcNow.ToString("o") + " navigationSuccess=" + e.IsSuccess + " webview=" + web.CoreWebView2.Environment.BrowserVersionString + " evidence=" + evidence + Environment.NewLine);
        }
        catch (Exception ex) { Trace.TraceError("Companion navigation diagnostics: " + ex); }
    }
#endif

    // Close this window when the watched game process exits, so quitting FM26 (cleanly or
    // by crash) does not leave an orphaned companion stuck on its "waiting for game" retry.
    // If the process is already gone — or vanishes before we can hook it — close at once.
    private static bool WatchGame(int pid, Form form)
    {
        Process game;
        try { game = Process.GetProcessById(pid); }
        catch (ArgumentException) { return false; }

        // Create the UI-thread handle before subscribing: a game that already
        // exited must not call BeginInvoke on a form with no handle/message pump.
        _ = form.Handle;
        void CloseAfterExit(object sender, EventArgs args)
        {
            try { if (!form.IsDisposed) form.BeginInvoke(form.Close); }
            catch (InvalidOperationException) when (form.IsDisposed || form.Disposing) { }
        }
        game.Exited += CloseAfterExit;
        form.FormClosed += (_, _) => game.Dispose();
        game.EnableRaisingEvents = true;
        if (!game.HasExited) return true;
        game.Dispose();
        return false;
    }
}
