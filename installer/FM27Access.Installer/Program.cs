namespace FM27Access.Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.FirstOrDefault() == "--apply-update")
        {
            try { InstallerUpdateService.Apply(args); }
            catch (Exception ex)
            {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FM27Access/Installer");
                var text = new LocalizationService(cache);
                new LoggingService(cache).Write(ex.ToString());
                MessageBox.Show(text["failed"], text["error"], MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }
        InstallerUpdateService.CleanupCompletedHelper();
        Application.Run(new InstallerForm());
    }
}
