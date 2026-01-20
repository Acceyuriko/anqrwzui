using System.Runtime.InteropServices;

namespace anqrwzui
{
  public class MouseController
  {
    private bool _deviceReady = false;

    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int opendevice(int index = 0);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int opendevicebyid(int vid, int pid);
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int opendevicebypath(string path);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int isconnected();
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int resetdevice();
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int closedevice();
    // 设备信息
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr getmodel();
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr getserialnumber();
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr getproductiondate();
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr getfirmwareversion();
    // 键盘操作
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int presskeybyname(string keyn);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int presskeybyvalue(int keyv);
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int releasekeybyname(string keyn);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int releasekeybyvalue(int keyv);
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int pressandreleasekeybyname(string keyn);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int pressandreleasekeybyvalue(int keyv);
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int iskeypressedbyname(string keyn);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int iskeypressedbyvalue(int keyv);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int releaseallkey();
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int inputstring(string str);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int getcapslock();
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int getnumlock();
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int setpresskeydelay(int mind, int maxd);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int setinputstringintervaltime(int mind, int maxd);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int setcasesensitive(int cs);
    // 鼠标操作
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int pressmousebutton(int mbtn);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int releasemousebutton(int mbtn);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int pressandreleasemousebutton(int mbtn);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int ismousebuttonpressed(int mbtn);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int releaseallmousebutton();
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int movemouseto(int x, int y);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int movemouserelative(int x, int y);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int movemousewheel(int z);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int getmousex();
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int getmousey();
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int setmouseposition(int x, int y);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int setpressmousebuttondelay(int mind, int maxd);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int setmousemovementdelay(int mind, int maxd);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int setmousemovementspeed(int speedvalue);
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int setmousemovementmode(int mode);
    // 加密狗操作
    [DllImport("gbild64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int initializedongle();
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int setreadpassword(string writepwd, string newpwd);
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int setwritepassword(string oldpwd, string newpwd);
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr readstring(string readpwd, int addr, int count);
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int writestring(string writepwd, string str, int addr);
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern int setcipher(string writepwd, string cipher);
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr encryptstring(string str);
    [DllImport("gbild64.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr decryptstring(string str);

    public MouseController()
    {
      Logger.Info("初始化鼠标控制器");

      try
      {
        _deviceReady = opendevice() != 0;
        if (!_deviceReady)
        {
          Logger.Warning("无法打开鼠标控制设备，将回退到 SendInput");
        }
        else
        {
          var modelPtr = getmodel();
          var versionPtr = getfirmwareversion();
          var snPtr = getserialnumber();
          var prodDatePtr = getproductiondate();

          string model = Marshal.PtrToStringAnsi(modelPtr) ?? "未知";
          string version = Marshal.PtrToStringAnsi(versionPtr) ?? "未知";
          string sn = Marshal.PtrToStringAnsi(snPtr) ?? "未知";
          string prodDate = Marshal.PtrToStringAnsi(prodDatePtr) ?? "未知";

          Logger.Info($"鼠标控制设备信息 - 型号: {model}, 版本: {version}, 序列号: {sn}, 生产日期: {prodDate}");

        }
      }
      catch (Exception ex)
      {
        _deviceReady = false;
        Logger.Error("初始化鼠标控制器失败，将回退到 SendInput", ex);
      }
    }

    public void MoveRelative(int dx, int dy)
    {
      if (_deviceReady)
      {
        var code = movemouserelative(dx, dy);
        if (code == 0)
        {
          Logger.Error("设备相对移动失败，回退 SendInput");
          SendInputMove(dx, dy);
        }
      }
      else
      {
        SendInputMove(dx, dy);
      }
    }

    private static void SendInputMove(int dx, int dy)
    {
      var input = new INPUT
      {
        type = INPUT_MOUSE,
        mi = new MOUSEINPUT
        {
          dx = dx,
          dy = dy,
          mouseData = 0,
          dwFlags = MOUSEEVENTF_MOVE,
          time = 0,
          dwExtraInfo = IntPtr.Zero
        }
      };

      uint sent = SendInput(1, new INPUT[] { input }, Marshal.SizeOf<INPUT>());
      if (sent == 0)
      {
        Logger.Error("SendInput 发送鼠标移动失败");
      }
    }

    private const int INPUT_MOUSE = 0;
    private const int MOUSEEVENTF_MOVE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
      public int type;
      public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
      public int dx;
      public int dy;
      public int mouseData;
      public int dwFlags;
      public int time;
      public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
  }
}