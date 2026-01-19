namespace anqrwzui;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 应用程序启动日志
        Logger.Info("应用程序启动");
        
        try
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new Main());
            
            Logger.Info("应用程序正常退出");
        }
        catch (Exception ex)
        {
            Logger.Error("应用程序异常退出", ex);
            MessageBox.Show($"应用程序发生错误: {ex.Message}", "错误", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }    
}