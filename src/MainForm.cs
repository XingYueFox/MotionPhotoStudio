using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MotionPhotoStudio
{
    internal sealed class MainForm : Form
    {
        private readonly WebView2 _webView;
        private bool _webViewReady;
        private bool _dark;
        internal Bridge HostBridge { get; private set; }

        internal WebView2 View { get { return _webView; } }
        internal bool IsDark { get { return _dark; } }

        internal event Action<string[]> FilesDropped;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cx, int cy, uint fuLoad);

        // ExtractAssociatedIcon 固定给 32px，高 DPI 任务栏会把小图拉伸得又小又糊；
        // 直接从 exe 里按大图标尺寸（含 48/256 档）取整份图标。
        private static Icon LoadBigAppIcon()
        {
            try
            {
                const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
                int size = 48;
                IntPtr h = LoadImage(IntPtr.Zero, Application.ExecutablePath, IMAGE_ICON, size, size, LR_LOADFROMFILE);
                if (h != IntPtr.Zero)
                {
                    Icon icon = Icon.FromHandle(h);
                    return (Icon)icon.Clone(); // 脱离句柄生命周期，避免窗口销毁后图标失效
                }
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            return null;
        }

        public MainForm()
        {
            Text = "动态照片工坊";
            Icon = LoadBigAppIcon();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(800, 560);
            Size = new Size(1320, 860);
            BackColor = Color.FromArgb(253, 247, 255);
            DoubleBuffered = true;
            AllowDrop = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(253, 247, 255),
                AllowExternalDrop = false
            };
            Controls.Add(_webView);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_CAPTION = 0x00C00000;
                const int WS_THICKFRAME = 0x00040000;
                const int WS_MINIMIZEBOX = 0x00020000;
                const int WS_MAXIMIZEBOX = 0x00010000;
                CreateParams cp = base.CreateParams;
                cp.Style |= WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDpiAwareDefaultSize();
            TryRoundCorners();
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        // 窗口尺寸在 150% 缩放下若原样传物理像素，页面拿到的 CSS 视口只有 880×573，
        // 首屏会被截断。这里按 DPI 把“设计尺寸”换算成物理像素，限制在工作区内，并居中。
        private void ApplyDpiAwareDefaultSize()
        {
            try
            {
                uint dpi = 96;
                try { dpi = GetDpiForWindow(Handle); } catch { dpi = 96; }
                if (dpi < 72) dpi = (uint)DeviceDpi;
                double scale = dpi / 96.0;
                if (scale <= 0 || double.IsNaN(scale)) scale = 1;
                Rectangle wa = Screen.FromHandle(Handle).WorkingArea;

                const double factor = 0.90;   // 默认比设计尺寸略收一点，避免一开就“顶满”屏幕
                int w = Math.Min((int)Math.Round(1320 * scale * factor), (int)(wa.Width * 0.88));
                int h = Math.Min((int)Math.Round(860 * scale * factor), (int)(wa.Height * 0.88));
                MinimumSize = new Size((int)Math.Round(880 * scale), (int)Math.Round(600 * scale));
                Size = new Size(w, h);

                // 先按当前尺寸居中（StartPosition 只在显示时生效，改过尺寸后会偏）
                Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2);
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private void TryRoundCorners()
        {
            try
            {
                int preference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
            }
            catch { }
        }

        internal void SetDarkFrame(bool dark)
        {
            _dark = dark;
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
                int border = dark ? 0x2A2A2E : 0xE6DFE9;
                DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int)); // DWMWA_BORDER_COLOR
            }
            catch { }
        }

        // 让窗体底色与页面主题表面色一致（缩放边条、启动瞬间都由窗体自绘）
        internal void SetSurfaceColor(string hex)
        {
            try
            {
                string h = (hex ?? "").TrimStart('#');
                if (h.Length != 6) return;
                Color c = Color.FromArgb(
                    Convert.ToInt32(h.Substring(0, 2), 16),
                    Convert.ToInt32(h.Substring(2, 2), 16),
                    Convert.ToInt32(h.Substring(4, 2), 16));
                BackColor = c;
                _webView.DefaultBackgroundColor = c;
            }
            catch { }
        }

        // ---------- 窗口边缘缩放 ----------
        // WebView2 的子窗口会独吞 WM_NCHITTEST（且 Chromium 拒绝被 SetWindowLongPtr 子类化），
        // 宿主收不到边缘命中测试。改为：页面指针贴近窗口边缘时上报边缘方向，
        // 宿主释放捕获后发送 WM_NCLBUTTONDOWN + 对应 HT 码，交给系统原生缩放循环——
        // 与拖标题栏（HT 码 HTCAPTION）完全同一套机制，视觉上窗口依旧铺满，没有额外边框。

        internal void StartWindowResize(int htCode)
        {
            if (WindowState == FormWindowState.Maximized) return;
            if (htCode < 10 || htCode > 17) return;
            ReleaseCapture();
            SendMessage(Handle, 0x00A1 /*WM_NCLBUTTONDOWN*/, htCode, 0);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RecenterWindow();
            InitializeWebView();
        }

        // 按最终尺寸把窗口摆到工作区正中（StartPosition=CenterScreen 用的是改尺寸前的大小）
        private void RecenterWindow()
        {
            try
            {
                if (WindowState == FormWindowState.Maximized) return;
                Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
                int x = wa.Left + (wa.Width - Width) / 2;
                int y = wa.Top + (wa.Height - Height) / 2;
                Location = new Point(Math.Max(wa.Left, x), Math.Max(wa.Top, y));
            }
            catch { }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (HostBridge != null) HostBridge.PushAccent();
        }

        private async void InitializeWebView()
        {
            try
            {
                var options = new CoreWebView2EnvironmentOptions
                {
                    // 注意：不要开启 msWebView2BrowserHitTransparent —— 那是官方文档标注的
                    // “仅测试用”旗标，会把鼠标滚轮/键盘事件整个穿透给宿主窗口，导致页面无法滚动。
                    AdditionalBrowserArguments = "--disable-features=msWebOOUI,msPdfOOUI"
                };
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    Paths.FixedRuntimeFolder(), Paths.WebViewDataDir, options);
                await _webView.EnsureCoreWebView2Async(environment);

                CoreWebView2 core = _webView.CoreWebView2;
                CoreWebView2Settings settings = core.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.AreDevToolsEnabled = true;
                settings.IsStatusBarEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsSwipeNavigationEnabled = false;
                settings.IsBuiltInErrorPageEnabled = false;

                core.SetVirtualHostNameToFolderMapping("app.local", Paths.WwwDir, CoreWebView2HostResourceAccessKind.Allow);
                HostBridge = new Bridge(this);
                core.WebMessageReceived += (s, a) => HostBridge.OnMessage(a.WebMessageAsJson);
                core.NavigationCompleted += async (s, a) =>
                {
                    _webViewReady = true;
                        HostBridge.PushAppInfo();
                    if (Program.ScreenshotMode)
                    {
                        try
                        {
                            await Task.Delay(2800);
                            string dir = Path.GetDirectoryName(Program.ScreenshotPath);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            using (var stream = File.Create(Program.ScreenshotPath))
                            {
                                await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                            }
                        }
                        catch (Exception shotEx)
                        {
                            try { File.AppendAllText(Path.Combine(Paths.DataDir, "crash.log"), "screenshot: " + shotEx + Environment.NewLine); } catch { }
                        }
                        Close();
                    }
                };
                core.Navigate("https://app.local/index.html");
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowRuntimeMissing();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "界面引擎初始化失败：" + ex.Message, "动态照片工坊",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private void ShowRuntimeMissing()
        {
            DialogResult r = MessageBox.Show(this,
                "未检测到 Microsoft Edge WebView2 运行时。\n\n" +
                "Windows 11 与更新过的 Windows 10 通常已自带；\n" +
                "也可以把固定版本运行时放进程序目录的 runtime 文件夹。\n\n" +
                "是否现在打开官方下载页面？",
                "动态照片工坊", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes)
            {
                try { System.Diagnostics.Process.Start("https://developer.microsoft.com/microsoft-edge/webview2/"); } catch { }
            }
            Close();
        }

        internal void PostJson(string json)
        {
            if (!_webViewReady) return;
            try
            {
                if (InvokeRequired) BeginInvoke(new Action(() => PostJson(json)));
                else _webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch { }
        }

        internal void PostEvent(string name, object data)
        {
            PostJson("{\"event\":" + Bridge.JsonString(name) + ",\"data\":" + Bridge.Serialize(data) + "}");
        }

        // ---------- 窗口边框 / 缩放 ----------

        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int HTCLIENT = 1;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                AdjustMaximizedBounds(ref m);
                return;
            }

            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    int lp = m.LParam.ToInt32();
                    var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                    m.Result = (IntPtr)HitTestBorder(PointToClient(screen));
                }
                return;
            }
            base.WndProc(ref m);
        }

        private int HitTestBorder(Point p)
        {
            const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
            int border = (int)Math.Round(7 * (DeviceDpi / 96.0));
            bool left = p.X <= border;
            bool right = p.X >= ClientSize.Width - border;
            bool top = p.Y <= border;
            bool bottom = p.Y >= ClientSize.Height - border;

            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            return HTCLIENT;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private void AdjustMaximizedBounds(ref Message m)
        {
            IntPtr monitor = MonitorFromWindow(Handle, 2 /*MONITOR_DEFAULTTONEAREST*/);
            if (monitor == IntPtr.Zero) return;
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(monitor, ref info)) return;

            var mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
            mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
            mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
            mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
            mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
            mmi.ptMinTrackSize.X = MinimumSize.Width;
            mmi.ptMinTrackSize.Y = MinimumSize.Height;
            Marshal.StructureToPtr(mmi, m.LParam, true);
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        internal void StartWindowDrag()
        {
            ReleaseCapture();
            SendMessage(Handle, 0x00A1 /*WM_NCLBUTTONDOWN*/, 2 /*HTCAPTION*/, 0);
        }

        internal void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            else WindowState = FormWindowState.Maximized;
        }

        internal bool IsMaximized { get { return WindowState == FormWindowState.Maximized; } }

        // ---------- 拖放 ----------

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0) return;
                var handler = FilesDropped;
                if (handler != null) handler(paths);
            }
            catch { }
        }
    }
}
