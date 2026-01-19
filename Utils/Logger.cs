using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace anqrwzui
{
  public static class Logger
  {
    private static readonly object _lockObject = new object();
    private static string? _logFilePath;
    private static bool _isInitialized = false;

    static Logger()
    {
      Initialize();
    }

    private static void Initialize()
    {
      try
      {
        var exeDirectory = Directory.GetCurrentDirectory();

        var logsFolder = Path.Combine(exeDirectory, "Logs");
        Directory.CreateDirectory(logsFolder);

        _logFilePath = Path.Combine(logsFolder, $"anqrwzui_{DateTime.Now:yyyyMMdd}.log");
        _isInitialized = true;

        Info("日志系统初始化完成");
        Info($"日志文件位置: {_logFilePath}");
      }
      catch (Exception ex)
      {
        // 如果文件日志初始化失败，至少确保控制台日志可用
        Console.WriteLine($"日志系统初始化失败: {ex.Message}");
      }
    }

    public static void Debug(string message)
    {
      Log("DEBUG", message);
    }

    public static void Info(string message)
    {
      Log("INFO", message);
    }

    public static void Warning(string message)
    {
      Log("WARNING", message);
    }

    public static void Error(string message)
    {
      Log("ERROR", message);
    }

    public static void Error(string message, Exception ex)
    {
      Log("ERROR", $"{message} - {ex.GetType().Name}: {ex.Message}");
    }

    private static void Log(string level, string message)
    {
      var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
      var logMessage = $"[{timestamp}] [{level}] {message}";

      WriteToFile(logMessage);
    }

    private static void WriteToFile(string logMessage)
    {
      if (!_isInitialized) return;

      lock (_lockObject)
      {
        try
        {
          using (var writer = new StreamWriter(_logFilePath!, true, Encoding.UTF8))
          {
            writer.WriteLine(logMessage);
          }
        }
        catch (Exception ex)
        {
          // 如果文件写入失败，回退到控制台输出
          Console.WriteLine($"文件日志写入失败: {ex.Message}");
          Console.WriteLine(logMessage);
        }
      }
    }

    public static string GetLogFilePath()
    {
      return _logFilePath!;
    }
  }
}