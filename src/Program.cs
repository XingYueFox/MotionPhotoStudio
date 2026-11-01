using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MotionPhotoStudio
{
    internal static class Program
    {
        internal static string Version = "27.0.0";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);
        internal static bool ScreenshotMode;
        internal static string ScreenshotPath;
        internal static string ScreenshotPage;
        internal static string ScreenshotTheme;
        internal static string ScreenshotDemoDir;

        [STAThread]
        private static int Main(string[] args)
        {
            // 给任务栏一个独立的应用标识，图标才会按本程序的 exe 资源绘制，而不是沿用缓存里的旧标识
            try { SetCurrentProcessExplicitAppUserModelID("MotionPhotoStudio.Desktop.v1"); } catch { }

            if (args != null && args.Length > 0)
            {
                if (args[0] == "--mux" && args.Length >= 4)
                {
                    // 命令行模式，便于脚本化与自检
                    var options = new Core.MuxOptions();
                    Core.MuxOutcome outcome = Core.MotionPhoto.Merge(args[1], args[2], args[3], options);
                    return outcome.Ok ? 0 : 2;
                }
                if (args[0] == "--version") return 0;
                if (args[0] == "--screenshot" && args.Length >= 2)
                {
                    ScreenshotPath = args[1];
                    ScreenshotPage = args.Length > 2 ? args[2] : null;
                    ScreenshotTheme = args.Length > 3 ? args[3] : null;
                    ScreenshotDemoDir = args.Length > 4 && args[4].StartsWith("demo=") ? args[4].Substring(5) : null;
                    ScreenshotMode = true;
                }
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (ScreenshotMode)
            {
                // 截图模式：允许并行多开，便于自动化测试
                try { Bootstrap(); Application.Run(new MainForm()); } catch (Exception ex) { Crash(ex); }
                return 0;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, "Global\\MotionPhotoStudio.SingleInstance.6C1F", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("动态照片工坊已经在运行中了。", "动态照片工坊", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                try
                {
                    Bootstrap();
                    Application.Run(new MainForm());
                }
                catch (Exception ex)
                {
                    Crash(ex);
                }
                finally
                {
                    GC.KeepAlive(mutex);
                }
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Bootstrap()
        {
            Paths.EnsureDirectories();
            NativeBoot.ExtractLoader();
            WebAssets.ExtractAll();
        }

        /// <summary>从 exe 内嵌资源中解析 WebView2 托管程序集，实现真正单文件。</summary>
        private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            string simpleName = new AssemblyName(args.Name).Name;
            string resource = "lib." + simpleName + ".dll";
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream == null) return null;
            try
            {
                using (stream)
                {
                    var buffer = new byte[stream.Length];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = stream.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return Assembly.Load(buffer);
                }
            }
            catch
            {
                return null;
            }
        }

        internal static void Crash(Exception ex)
        {
            string message = ex == null ? "未知错误" : ex.GetType().Name + "：" + ex.Message;
            try
            {
                string logPath = Path.Combine(Paths.DataDir, "crash.log");
                File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine + (ex == null ? "" : ex.StackTrace) + Environment.NewLine);
            }
            catch { }
            MessageBox.Show(message + "\n\n详细信息已写入 data\\crash.log", "动态照片工坊遇到问题", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal static class NativeBoot
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        /// <summary>把内嵌的 WebView2Loader.dll 释放到 bin 目录，并加入 DLL 搜索路径。</summary>
        internal static void ExtractLoader()
        {
            string target = Path.Combine(Paths.BinDir, "WebView2Loader.dll");
            try
            {
                byte[] payload = ReadResource("lib.WebView2Loader.dll");
                if (payload != null && NeedsWrite(target, payload.Length))
                {
                    File.WriteAllBytes(target, payload);
                }
                SetDllDirectory(Paths.BinDir);
            }
            catch
            {
                // 目录不可写时退回系统临时目录（例如只读 U 盘）
                string fallback = Path.Combine(Path.GetTempPath(), "MotionPhotoStudio", "bin");
                Directory.CreateDirectory(fallback);
                string alt = Path.Combine(fallback, "WebView2Loader.dll");
                byte[] payload = ReadResource("lib.WebView2Loader.dll");
                if (payload != null && NeedsWrite(alt, payload.Length)) File.WriteAllBytes(alt, payload);
                SetDllDirectory(fallback);
            }
        }

        private static bool NeedsWrite(string path, int expectedLength)
        {
            var info = new FileInfo(path);
            return !info.Exists || info.Length != expectedLength;
        }

        internal static byte[] ReadResource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var buffer = new byte[s.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = s.Read(buffer, read, buffer.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return buffer;
            }
        }
    }

    internal static class Paths
    {
        internal static string ExeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        internal static string DataDir = Path.Combine(ExeDir, "data");
        internal static string BinDir = Path.Combine(ExeDir, "bin");
        internal static string WwwDir = Path.Combine(DataDir, "www");
        internal static string SettingsFile = Path.Combine(DataDir, "settings.json");
        internal static string WebViewDataDir = Path.Combine(DataDir, "webview");

        internal static void EnsureDirectories()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                Directory.CreateDirectory(BinDir);
            }
            catch
            {
                // 只读位置：数据目录退回用户配置目录，保证程序仍能启动
                DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MotionPhotoStudio");
                BinDir = Path.Combine(DataDir, "bin");
                WwwDir = Path.Combine(DataDir, "www");
                SettingsFile = Path.Combine(DataDir, "settings.json");
                WebViewDataDir = Path.Combine(DataDir, "webview");
                Directory.CreateDirectory(DataDir);
                Directory.CreateDirectory(BinDir);
            }
        }

        /// <summary>可选：若存在 runtime\msedgewebview2.exe，则使用随包分发的固定版本运行时。</summary>
        internal static string FixedRuntimeFolder()
        {
            string candidate = Path.Combine(ExeDir, "runtime");
            return File.Exists(Path.Combine(candidate, "msedgewebview2.exe")) ? candidate : null;
        }
    }

    internal static class WebAssets
    {
        private const string AssetPrefix = "www.";

        /// <summary>把内嵌的界面资源释放到 data\www，版本变化时才重写。</summary>
        internal static void ExtractAll()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string stampPath = Path.Combine(Paths.WwwDir, ".version");
                string stamp = Program.Version + "|" + asm.ManifestModule.ModuleVersionId;
                bool upToDate = File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp;

                if (!upToDate)
                {
                    Directory.CreateDirectory(Paths.WwwDir);
                    foreach (string name in asm.GetManifestResourceNames())
                    {
                        if (!name.StartsWith(AssetPrefix, StringComparison.Ordinal)) continue;
                        string relative = name.Substring(AssetPrefix.Length);
                        string full = Path.Combine(Paths.WwwDir, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(full));
                        using (Stream s = asm.GetManifestResourceStream(name))
                        using (FileStream fs = File.Create(full))
                        {
                            s.CopyTo(fs);
                        }
                    }
                    File.WriteAllText(stampPath, stamp);
                }
            }
            catch
            {
                // 释放失败时退到临时目录，界面仍可用
                try
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "MotionPhotoStudio", "www");
                    Directory.CreateDirectory(tmp);
                    Assembly asm = Assembly.GetExecutingAssembly();
                    foreach (string name in asm.GetManifestResourceNames())
                    {
                        if (!name.StartsWith(AssetPrefix, StringComparison.Ordinal)) continue;
                        string relative = name.Substring(AssetPrefix.Length);
                        string full = Path.Combine(tmp, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(full));
                        using (Stream s = asm.GetManifestResourceStream(name))
                        using (FileStream fs = File.Create(full)) s.CopyTo(fs);
                    }
                    Paths.WwwDir = tmp;
                }
                catch { }
            }
        }
    }
}
