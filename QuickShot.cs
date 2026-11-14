// QuickShot（快截）—— 轻量的 Windows 快速截图小工具
// 按快捷键 -> 框选区域 -> 回车/双击 -> 图片直接进剪贴板，不在本地留任何文件。
// 用 .NET Framework 自带的 csc.exe 编译，见 build.cmd。
//
// Copyright (C) 2026 sunjerry2-arch  <https://github.com/sunjerry2-arch/QuickShot>
// 本程序是自由软件：你可以依据自由软件基金会发布的 GNU 通用公共许可证第 3 版（GPL-3.0）
// 或（由你选择）任何更新的版本，重新发布和/或修改本程序。
// 本程序免费提供，不附带任何担保。完整许可证见 LICENSE 文件或 <https://www.gnu.org/licenses/>。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("QuickShot 快截")]
[assembly: System.Reflection.AssemblyProduct("QuickShot")]
[assembly: System.Reflection.AssemblyVersion("1.0.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.2.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.2")]   // 文件属性里的"产品版本"

namespace QuickShot
{
    static class AppInfo
    {
        public const string Version = "1.0.2";
        // 官方下载地址
        public const string RepoUrl = "https://github.com/sunjerry2-arch/QuickShot";
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "/writeicon") { IconMaker.WriteIco(args[1]); return; }

            // 每显示器 DPI 感知：多屏不同缩放时坐标才准
            try { if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4))) throw new Exception(); }
            catch { try { Native.SetProcessDpiAwareness(2); } catch { try { Native.SetProcessDPIAware(); } catch { } } }

            bool created;
            using (var mutex = new Mutex(true, "QuickShot.SingleInstance.7f3c1a", out created))
            {
                if (!created)
                {
                    MessageBox.Show("快截 QuickShot 已经在运行了（在任务栏右下角托盘里）。", "QuickShot");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write(e.Exception.ToString()); };
                bool quiet = Array.IndexOf(args, "/autostart") >= 0;
                Application.Run(new TrayApp(quiet));
            }
        }
    }

    static class Log
    {
        public static void Write(string msg)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "QuickShot.log"), DateTime.Now.ToString("s") + "  " + msg + Environment.NewLine); }
            catch { }
        }
    }

    // ---------------------------------------------------------------- 托盘 & 快捷键

    class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action Pressed;
        public HotkeyWindow()
        {
            var cp = new CreateParams();
            cp.Parent = new IntPtr(-3); // HWND_MESSAGE
            CreateHandle(cp);
        }
        public bool Register(uint mods, uint vk)
        {
            Native.UnregisterHotKey(Handle, 1);
            return Native.RegisterHotKey(Handle, 1, mods | 0x4000 /*MOD_NOREPEAT*/, vk);
        }
        public void Unregister() { Native.UnregisterHotKey(Handle, 1); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && Pressed != null) Pressed();
            base.WndProc(ref m);
        }
        public void Dispose() { Unregister(); DestroyHandle(); }
    }

    static class Settings
    {
        const string Key = @"Software\QuickShot";
        public static int GetInt(string name, int def)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                {
                    if (k == null) return def;
                    object v = k.GetValue(name);
                    return v is int ? (int)v : def;
                }
            }
            catch { return def; }
        }
        public static void SetInt(string name, int value)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, value, RegistryValueKind.DWord); }
            catch { }
        }
    }

    class TrayApp : ApplicationContext
    {
        const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4;
        static readonly uint[] HkMods = { MOD_ALT, MOD_CONTROL | MOD_ALT, MOD_CONTROL | MOD_SHIFT };
        static readonly string[] HkNames = { "Alt+A", "Ctrl+Alt+A", "Ctrl+Shift+A" };
        const int NoHotkey = 3;   // "不使用快捷键"：只从托盘菜单截图，不占任何按键
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        NotifyIcon tray;
        HotkeyWindow hk;
        OverlayForm overlay;
        ToolStripMenuItem captureItem, autoItem;
        List<ToolStripMenuItem> hkItems = new List<ToolStripMenuItem>();
        int hkIndex = -1;

        public TrayApp(bool quiet)
        {
            hk = new HotkeyWindow();
            hk.Pressed += StartCapture;

            var menu = new ContextMenuStrip();
            captureItem = new ToolStripMenuItem("截图", null, delegate { StartCaptureDelayed(); });
            captureItem.Font = new Font(captureItem.Font, FontStyle.Bold);
            menu.Items.Add(captureItem);
            menu.Items.Add(new ToolStripSeparator());
            var hkMenu = new ToolStripMenuItem("快捷键");
            for (int i = 0; i < HkNames.Length; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem(HkNames[i], null, delegate { ChangeHotkey(idx); });
                hkItems.Add(it);
                hkMenu.DropDownItems.Add(it);
            }
            hkMenu.DropDownItems.Add(new ToolStripSeparator());
            var noneItem = new ToolStripMenuItem("不使用快捷键（从这个菜单截图）", null, delegate { ChangeHotkey(NoHotkey); });
            hkItems.Add(noneItem);   // 下标正好是 NoHotkey
            hkMenu.DropDownItems.Add(noneItem);
            menu.Items.Add(hkMenu);
            autoItem = new ToolStripMenuItem("开机自动启动", null, delegate { ToggleAutoStart(); });
            autoItem.Checked = IsAutoStart();
            menu.Items.Add(autoItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("关于快截（免费软件）", null, delegate { ShowAbout(); }));
            menu.Items.Add(new ToolStripMenuItem("退出", null, delegate { ExitApp(); }));

            tray = new NotifyIcon();
            UpdateTrayIcon();
            tray.ContextMenuStrip = menu;   // 只有右键出菜单；左键单击不做任何事，防止误触截图
            tray.Text = "快截 QuickShot";
            tray.Visible = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;   // 任务栏深浅色切换时换图标

            int pref = Settings.GetInt("Hotkey", 0);
            if (pref < 0 || pref > NoHotkey) pref = 0;
            int got = -1;
            if (pref == NoHotkey) got = NoHotkey;
            else if (hk.Register(HkMods[pref], 0x41)) got = pref;
            else
                // 想要的键被占了（比如微信先开着）：让给对方，自己换一个空着的
                for (int i = 0; i < HkNames.Length && got < 0; i++)
                    if (i != pref && hk.Register(HkMods[i], 0x41)) got = i;
            hkIndex = got;
            UpdateHotkeyUi();

            if (got < 0)
                tray.ShowBalloonTip(8000, "快截：快捷键都被占用了",
                    "可以右键托盘图标，选「截图」。", ToolTipIcon.Warning);
            else if (got != pref)
                tray.ShowBalloonTip(10000, "快截：" + HkNames[pref] + " 已被其他程序占用（可能是微信）",
                    "已让给对方使用，快截改用 " + HkNames[got] + " 截图。\n" +
                    "也可以右键托盘图标 → 快捷键，换成别的按键或选「不使用快捷键」。", ToolTipIcon.Info);
            else if (!quiet)
                tray.ShowBalloonTip(4000, "快截 QuickShot 已在后台运行",
                    got == NoHotkey ? "右键托盘图标，选「截图」。本软件免费开源。"
                                    : "按 " + HkNames[got] + " 截图，截完直接 Ctrl+V 粘贴。本软件免费开源，右键托盘图标可设置。",
                    ToolTipIcon.Info);
        }

        void ShowAbout()
        {
            string msg =
                "快截 QuickShot  v" + AppInfo.Version + "\n" +
                "免费开源软件（GPL-3.0 许可证）\n\n" +
                "官方下载地址：\n" + AppInfo.RepoUrl + "\n\n" +
                "本软件完全免费。如果你是花钱买到的，说明你被骗了，\n" +
                "请向卖家申请退款，并在购买平台举报。\n\n" +
                "要打开项目主页吗？";
            if (MessageBox.Show(msg, "关于快截", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { System.Diagnostics.Process.Start(AppInfo.RepoUrl); } catch { }
            }
        }

        void UpdateHotkeyUi()
        {
            for (int i = 0; i < hkItems.Count; i++) hkItems[i].Checked = i == hkIndex;
            bool hasKey = hkIndex >= 0 && hkIndex < HkNames.Length;
            captureItem.Text = hasKey ? "截图\t" + HkNames[hkIndex] : "截图";
            tray.Text = hasKey ? "快截 QuickShot（" + HkNames[hkIndex] + " 截图）" : "快截 QuickShot（右键菜单截图）";
        }

        void ChangeHotkey(int idx)
        {
            if (idx == hkIndex) return;
            if (idx == NoHotkey)
            {
                hk.Unregister();
                hkIndex = NoHotkey;
                Settings.SetInt("Hotkey", idx);
            }
            else if (hk.Register(HkMods[idx], 0x41))
            {
                hkIndex = idx;
                Settings.SetInt("Hotkey", idx);
            }
            else
            {
                if (hkIndex >= 0 && hkIndex < HkNames.Length) hk.Register(HkMods[hkIndex], 0x41);
                tray.ShowBalloonTip(5000, "快截：" + HkNames[idx] + " 已被其他程序占用",
                    "可能是微信等软件在用，换一个按键试试。", ToolTipIcon.Warning);
            }
            UpdateHotkeyUi();
        }

        bool IsAutoStart()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("QuickShot") != null; }
            catch { return false; }
        }

        void ToggleAutoStart()
        {
            try
            {
                bool on = IsAutoStart();
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.DeleteValue("QuickShot", false);
                    else k.SetValue("QuickShot", "\"" + Application.ExecutablePath + "\" /autostart");
                }
            }
            catch (Exception ex) { MessageBox.Show("设置失败：" + ex.Message, "QuickShot"); }
            autoItem.Checked = IsAutoStart();
        }

        void StartCaptureDelayed()
        {
            // 从菜单触发时，等菜单收起再截，免得把菜单截进去
            var t = new System.Windows.Forms.Timer();
            t.Interval = 200;
            t.Tick += delegate { t.Stop(); t.Dispose(); StartCapture(); };
            t.Start();
        }

        void StartCapture()
        {
            if (overlay != null)
            {
                try { overlay.Activate(); } catch { }
                return;
            }
            try { overlay = new OverlayForm(); }
            catch (Exception ex)
            {
                overlay = null;
                Log.Write(ex.ToString());
                tray.ShowBalloonTip(4000, "截图失败", ex.Message, ToolTipIcon.Error);
                return;
            }
            overlay.FormClosed += delegate
            {
                overlay = null;
                GC.Collect();
            };
            try { overlay.Show(); }
            catch (Exception ex) { Log.Write("Show failed: " + ex); }
        }

        Icon trayIcon;

        void UpdateTrayIcon()
        {
            Icon old = trayIcon;
            trayIcon = IconMaker.TrayIcon(SystemInformation.SmallIconSize.Width, IconMaker.TaskbarIsLight());
            tray.Icon = trayIcon;
            if (old != null) old.Dispose();
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle)
                UpdateTrayIcon();
        }

        void ExitApp()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (overlay != null) overlay.Close();
            tray.Visible = false;
            tray.Dispose();
            hk.Dispose();
            ExitThread();
        }
    }

    // ---------------------------------------------------------------- 图标

    static class IconMaker
    {
        public static Bitmap Draw(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float s = size;
                // 白底 + 淡灰描边（浅色背景上也看得出轮廓）+ 黑色直角裁剪线
                float bw = Math.Max(1f, s * 0.0125f);
                using (var path = Ui.RoundRect(new RectangleF(bw / 2, bw / 2, s - bw, s - bw), s * 0.233f))
                {
                    g.FillPath(Brushes.White, path);
                    using (var bp = new Pen(Color.FromArgb(207, 207, 207), bw)) g.DrawPath(bp, path);
                }
                using (var p = new Pen(Ui.Ink, Math.Max(1.4f, s * 0.092f)))
                {
                    p.StartCap = LineCap.Square; p.EndCap = LineCap.Square; p.LineJoin = LineJoin.Miter;
                    g.DrawLines(p, new[] { new PointF(s * 0.33f, s * 0.17f), new PointF(s * 0.33f, s * 0.67f), new PointF(s * 0.83f, s * 0.67f) });
                    g.DrawLines(p, new[] { new PointF(s * 0.17f, s * 0.33f), new PointF(s * 0.67f, s * 0.33f), new PointF(s * 0.67f, s * 0.83f) });
                }
            }
            return bmp;
        }

        // 托盘图标：透明底，只画裁剪符号；按像素对齐填矩形，小尺寸也清晰无毛边
        public static Bitmap DrawTray(int size, Color ink)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var b = new SolidBrush(ink))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.None;
                float s = size;
                int w = Math.Max(2, (int)Math.Round(s * 0.12f));            // 线宽
                int lo = (int)Math.Round(s * 0.06f), hi = (int)Math.Round(s * 0.94f);
                int a = (int)Math.Round(s * 0.28f - w / 2f);                  // 靠左/靠上那根
                int c = (int)Math.Round(s * 0.72f - w / 2f);                  // 靠右/靠下那根
                // 第一个 L：竖线 x=a，从顶部到 c；横线 y=c，从 a 到右边
                g.FillRectangle(b, a, lo, w, c + w - lo);
                g.FillRectangle(b, a, c, hi - a, w);
                // 第二个 L：横线 y=a，从左边到 c；竖线 x=c，从 a 到底部
                g.FillRectangle(b, lo, a, c + w - lo, w);
                g.FillRectangle(b, c, a, w, hi - a);
            }
            return bmp;
        }

        public static Icon TrayIcon(int size, bool lightTaskbar)
        {
            Color ink = lightTaskbar ? Color.FromArgb(28, 28, 28) : Color.White;
            using (var bmp = DrawTray(size, ink))
            {
                byte[] dib = Dib(bmp);
                var ms = new MemoryStream();
                var bw = new BinaryWriter(ms);
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)1);
                bw.Write((byte)size); bw.Write((byte)size); bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32); bw.Write(dib.Length); bw.Write(22);
                bw.Write(dib);
                bw.Flush();
                ms.Position = 0;
                return new Icon(ms, size, size);   // 直接用带 alpha 的 ICO 数据，避免 GetHicon 的黑边
            }
        }

        public static bool TaskbarIsLight()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        static byte[] Png(Bitmap bmp)
        {
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }

        static byte[] Dib(Bitmap bmp)
        {
            int s = bmp.Width;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(40); bw.Write(s); bw.Write(s * 2); bw.Write((short)1); bw.Write((short)32);
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                for (int y = s - 1; y >= 0; y--)
                    for (int x = 0; x < s; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                    }
                bw.Write(new byte[((s + 31) / 32) * 4 * s]);   // AND 遮罩全 0，透明度由 alpha 决定
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var pngs = new List<byte[]>();
            foreach (int sz in sizes)
                using (var bmp = Draw(sz))
                    pngs.Add(sz >= 64 ? Png(bmp) : Dib(bmp));   // 小尺寸用传统 BMP 兼容性最好，大尺寸用 PNG 省空间
            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    bw.Write(d); bw.Write(d); bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((short)1); bw.Write((short)32);
                    bw.Write(pngs[i].Length); bw.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) bw.Write(p);
            }
        }
    }

    static class Ui
    {
        public static readonly Color Ink = Color.FromArgb(17, 17, 17);          // 主色：黑
        public static readonly Color ActiveBg = Color.FromArgb(30, 30, 30);     // 选中工具的底色
        public static readonly Color HoverBg = Color.FromArgb(236, 236, 236);

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var path = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // ---------------------------------------------------------------- 标注图形

    enum Tool { None, Rect, Ellipse, Line, Arrow, Pen, Mosaic, Text }

    class Shape
    {
        public Tool Kind;
        public Color Color;
        public float Width;
        public List<Point> Pts = new List<Point>();
        public string Text;
        public Font Font;

        const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
            TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;

        // 文字占据的区域，用于点中拖动
        public Rectangle TextBounds()
        {
            Size sz = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            return new Rectangle(Pts[0], sz);
        }

        public bool IsValid()
        {
            if (Pts.Count == 0) return false;
            if (Kind == Tool.Rect || Kind == Tool.Ellipse || Kind == Tool.Line || Kind == Tool.Arrow)
            {
                int dx = Pts[1].X - Pts[0].X, dy = Pts[1].Y - Pts[0].Y;
                return dx * dx + dy * dy >= 9;
            }
            return true;
        }

        static Rectangle Norm(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        PointF[] PtsF()
        {
            var r = new PointF[Pts.Count];
            for (int i = 0; i < r.Length; i++) r[i] = Pts[i];
            return r;
        }

        public void Draw(Graphics g, Bitmap mosaic)
        {
            switch (Kind)
            {
                case Tool.Rect:
                case Tool.Ellipse:
                    {
                        Rectangle r = Norm(Pts[0], Pts[1]);
                        using (var p = new Pen(Color, Width))
                        {
                            p.LineJoin = LineJoin.Round;
                            if (Kind == Tool.Rect) g.DrawRectangle(p, r); else g.DrawEllipse(p, r);
                        }
                        break;
                    }
                case Tool.Line:
                    using (var p = new Pen(Color, Width))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawLine(p, Pts[0], Pts[1]);
                    }
                    break;
                case Tool.Arrow:
                    DrawArrow(g, Pts[0], Pts[1]);
                    break;
                case Tool.Pen:
                    using (var p = new Pen(Color, Width))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                        if (Pts.Count == 1)
                            using (var b = new SolidBrush(Color))
                                g.FillEllipse(b, Pts[0].X - Width / 2, Pts[0].Y - Width / 2, Width, Width);
                        else g.DrawLines(p, PtsF());
                    }
                    break;
                case Tool.Mosaic:
                    DrawMosaic(g, mosaic);
                    break;
                case Tool.Text:
                    TextRenderer.DrawText(g, Text, Font, Pts[0], Color, TextFlags);
                    break;
            }
        }

        void DrawArrow(Graphics g, Point a, Point b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            float ux = dx / len, uy = dy / len;
            float head = Math.Max(Width * 4f, 12f);
            if (head > len * 0.9f) head = len * 0.9f;
            float hw = head * 0.42f;
            var basePt = new PointF(b.X - ux * head, b.Y - uy * head);
            var l = new PointF(basePt.X - uy * hw, basePt.Y + ux * hw);
            var r = new PointF(basePt.X + uy * hw, basePt.Y - ux * hw);
            using (var p = new Pen(Color, Width))
            {
                p.StartCap = LineCap.Round;
                g.DrawLine(p, a, new PointF(basePt.X + ux, basePt.Y + uy));
            }
            using (var br = new SolidBrush(Color))
                g.FillPolygon(br, new[] { (PointF)b, l, r });
        }

        void DrawMosaic(Graphics g, Bitmap src)
        {
            if (src == null || Pts.Count == 0) return;
            using (var path = new GraphicsPath())
            {
                if (Pts.Count == 1)
                    path.AddEllipse(Pts[0].X - Width / 2, Pts[0].Y - Width / 2, Width, Width);
                else
                {
                    path.AddLines(PtsF());
                    using (var p = new Pen(Color.Black, Width))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                        path.Widen(p);
                    }
                }
                Rectangle bb = Rectangle.Round(path.GetBounds());
                bb.Inflate(2, 2);
                bb.Intersect(new Rectangle(0, 0, src.Width, src.Height));
                if (bb.IsEmpty) return;
                Region old = g.Clip;
                var im = g.InterpolationMode;
                var sm = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.None;
                g.SetClip(path, CombineMode.Intersect);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(src, bb, bb, GraphicsUnit.Pixel);
                g.InterpolationMode = im;
                g.SmoothingMode = sm;
                g.Clip = old;
                old.Dispose();
            }
        }
    }

    // ---------------------------------------------------------------- 窗口识别

    static class WindowFinder
    {
        public static List<Rectangle> Collect(Rectangle vs)
        {
            var list = new List<Rectangle>();
            Native.EnumWindowsProc cb = delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                int ex = Native.GetWindowLong(h, -20);
                if ((ex & 0x20) != 0) return true; // WS_EX_TRANSPARENT
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                Native.RECT r;
                if (Native.DwmGetWindowAttribute(h, 9, out r, 16) != 0 && !Native.GetWindowRect(h, out r)) return true;
                var rc = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (rc.Width < 10 || rc.Height < 10) return true;
                rc.Intersect(vs);
                if (rc.IsEmpty) return true;
                rc.Offset(-vs.X, -vs.Y);
                list.Add(rc);
                return true;
            };
            Native.EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle b = s.Bounds;
                b.Offset(-vs.X, -vs.Y);
                list.Add(b);
            }
            return list;
        }
    }

    // ---------------------------------------------------------------- 截图遮罩层

    class OverlayForm : Form
    {
        enum Phase { Choose, Selected }
        enum DragMode { None, Creating, Moving, Resizing, Drawing, MovingText, ResizingText }

        class Btn
        {
            public Rectangle R; public string Id; public string Tip; public bool Sub;
            public Btn(Rectangle r, string id, string tip, bool sub) { R = r; Id = id; Tip = tip; Sub = sub; }
        }

        static readonly Color[] Palette = {
            Color.FromArgb(245, 34, 45), Color.FromArgb(250, 140, 22), Color.FromArgb(250, 219, 20),
            Color.FromArgb(82, 196, 26), Color.FromArgb(22, 119, 255), Color.FromArgb(114, 46, 209),
            Color.FromArgb(0, 0, 0), Color.FromArgb(255, 255, 255) };
        static readonly float[] LineWidths = { 2f, 4f, 7f };
        static readonly float[] MosaicWidths = { 12f, 22f, 36f };
        static readonly float[] FontSizes = { 16f, 22f, 32f };
        static readonly string[] BarIds = { "rect", "ellipse", "line", "arrow", "pen", "mosaic", "text", "|", "undo", "save", "|", "cancel", "done" };
        static readonly string[] BarTips = { "矩形（按住 Shift 画正方形）", "椭圆（按住 Shift 画正圆）", "直线（按住 Shift 保持水平/垂直/45°）", "箭头（按住 Shift 保持水平/垂直/45°）", "画笔", "马赛克", "文字", null,
            "撤销 Ctrl+Z", "保存到文件 Ctrl+S", null, "退出截图 Esc", "完成，复制到剪贴板（Enter / 双击）" };

        // 跨次截图记住颜色和粗细
        static Color curColor = Palette[0];
        static int sizeIdx = 1;
        static string lastSaveDir;

        Bitmap shot, dim, mosaic;
        Rectangle vs;
        int W, H;
        float S;
        List<Rectangle> wins;
        Font uiFont;

        Phase phase = Phase.Choose;
        DragMode drag = DragMode.None;
        Rectangle sel = Rectangle.Empty, hover = Rectangle.Empty;
        Point downPt, mouse;
        Rectangle selStart;
        int handle = -1;
        bool moved;
        Tool tool = Tool.None;
        List<Shape> shapes = new List<Shape>();
        Shape cur;
        TextBox tb;
        Font tbFont;
        Shape editOrig;          // 正在重新编辑的那段文字
        int editIdx;
        Shape selText;           // 当前选中的文字（显示虚线框和四角控制点）
        int textHandle;          // 正在拖的角：0 左上 1 右上 2 右下 3 左下
        Font rsStartFont;        // 开始缩放时的字体
        Rectangle rsStartBounds;
        Point rsAnchor;          // 缩放时固定不动的对角
        Shape dragShape;         // 正在拖动的文字
        Point dragShapeStart;

        List<Btn> btns = new List<Btn>();
        Rectangle barRect = Rectangle.Empty, subRect = Rectangle.Empty;
        List<int> seps = new List<int>();
        int subSep = -1;
        bool barAbove;

        public OverlayForm()
        {
            vs = SystemInformation.VirtualScreen;
            W = vs.Width; H = vs.Height;
            using (var g0 = Graphics.FromHwnd(IntPtr.Zero)) S = Math.Max(1f, g0.DpiX / 96f);

            // 先截全屏，再显示遮罩
            using (var raw = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(raw))
                {
                    IntPtr screenDc = Native.GetDC(IntPtr.Zero);
                    IntPtr dc = g.GetHdc();
                    try { Native.BitBlt(dc, 0, 0, W, H, screenDc, vs.X, vs.Y, 0x00CC0020 | 0x40000000); } // SRCCOPY | CAPTUREBLT
                    finally { g.ReleaseHdc(dc); Native.ReleaseDC(IntPtr.Zero, screenDc); }
                }
                shot = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(shot))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(raw, new Rectangle(0, 0, W, H), 0, 0, W, H, GraphicsUnit.Pixel);
                }
            }
            dim = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dim))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(shot, new Rectangle(0, 0, W, H), 0, 0, W, H, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                using (var b = new SolidBrush(Color.FromArgb(110, 0, 0, 0))) g.FillRectangle(b, 0, 0, W, H);
            }
            wins = WindowFinder.Collect(vs);
            uiFont = new Font("Microsoft YaHei UI", 12f * S, GraphicsUnit.Pixel);

            Text = "QuickShot";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = vs;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            DoubleBuffered = true;
            Cursor = Cursors.Cross;

            Point cp = Cursor.Position;
            mouse = new Point(cp.X - vs.X, cp.Y - vs.Y);
            hover = WindowAt(mouse);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW：不出现在 Alt+Tab
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.SetWindowPos(Handle, new IntPtr(-1), vs.X, vs.Y, W, H, 0x0040);
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) { m.Result = IntPtr.Zero; return; } // WM_DPICHANGED：保持物理像素尺寸
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (shot != null) shot.Dispose();
            if (dim != null) dim.Dispose();
            if (mosaic != null) mosaic.Dispose();
            if (uiFont != null) uiFont.Dispose();
            foreach (var s in shapes) if (s.Font != null) s.Font.Dispose();
            shot = dim = mosaic = null;
        }

        int Sc(float v) { return (int)Math.Round(v * S); }

        Rectangle ClientBounds() { return new Rectangle(0, 0, W, H); }

        Rectangle ScreenBoundsFor(Rectangle clientRect)
        {
            Rectangle r = clientRect;
            r.Offset(vs.X, vs.Y);
            Rectangle b = Screen.FromRectangle(r).Bounds;
            b.Offset(-vs.X, -vs.Y);
            return b;
        }

        Rectangle WindowAt(Point p)
        {
            foreach (Rectangle r in wins) if (r.Contains(p)) return r;
            return Rectangle.Empty;
        }

        // ------------------------------------------------ 绘制

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (shot == null) return;
            Graphics g = e.Graphics;
            Rectangle full = ClientBounds();
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(dim, full, full, GraphicsUnit.Pixel);

            Rectangle hi = HighlightRect();
            if (!hi.IsEmpty) g.DrawImage(shot, hi, hi, GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;

            if (!sel.IsEmpty && (shapes.Count > 0 || cur != null))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.SetClip(sel);
                foreach (var s in shapes) s.Draw(g, mosaic);
                if (cur != null) cur.Draw(g, mosaic);
                g.ResetClip();
                g.SmoothingMode = SmoothingMode.None;
            }

            if (!hi.IsEmpty)
            {
                bool isHover = sel.IsEmpty;
                // 选框：外黑 - 中白 - 内黑，深色浅色画面上都看得清
                int lw = isHover ? Sc(3) : Math.Max(1, Sc(1.5f));
                using (var outer = new Pen(Ui.Ink, lw + 2))
                using (var inner = new Pen(Color.White, lw))
                {
                    outer.Alignment = PenAlignment.Inset;
                    inner.Alignment = PenAlignment.Inset;
                    g.DrawRectangle(outer, hi);
                    Rectangle ir = hi;
                    ir.Inflate(-1, -1);
                    if (ir.Width > 0 && ir.Height > 0) g.DrawRectangle(inner, ir);
                }
                if (phase == Phase.Selected && tool == Tool.None && drag != DragMode.Creating)
                    DrawHandles(g);
                DrawSizeLabel(g, hi);
            }

            if (tb != null)
            {
                Rectangle r = tb.Bounds;
                r.Inflate(Sc(3), Sc(3));
                using (var p = new Pen(Color.FromArgb(200, 255, 255, 255))) { p.DashStyle = DashStyle.Dash; g.DrawRectangle(p, r); }
            }

            DrawSelText(g);

            if (ToolbarVisible()) DrawToolbar(g);
            if (ShowMagnifier()) DrawMagnifier(g);
        }

        Rectangle HighlightRect()
        {
            if (!sel.IsEmpty) return sel;
            if (phase == Phase.Choose && (drag == DragMode.None || (drag == DragMode.Creating && !moved))) return hover;
            return Rectangle.Empty;
        }

        bool ToolbarVisible()
        {
            return phase == Phase.Selected && (drag == DragMode.None || drag == DragMode.Drawing) && !barRect.IsEmpty;
        }

        bool ShowMagnifier()
        {
            if (tb != null || OverBar(mouse)) return false;
            return phase == Phase.Choose || drag == DragMode.Resizing;
        }

        Point[] HandlePoints()
        {
            int l = sel.Left, t = sel.Top, r = sel.Right - 1, b = sel.Bottom - 1;
            int mx = (l + r) / 2, my = (t + b) / 2;
            return new[] { new Point(l, t), new Point(mx, t), new Point(r, t), new Point(r, my),
                           new Point(r, b), new Point(mx, b), new Point(l, b), new Point(l, my) };
        }

        void DrawHandles(Graphics g)
        {
            int hs = Math.Max(6, Sc(7));
            using (var pen = new Pen(Ui.Ink))
                foreach (Point p in HandlePoints())
                {
                    var r = new Rectangle(p.X - hs / 2, p.Y - hs / 2, hs, hs);
                    g.FillRectangle(Brushes.White, r);
                    g.DrawRectangle(pen, r);
                }
        }

        void DrawSizeLabel(Graphics g, Rectangle r)
        {
            string t = r.Width + " × " + r.Height;
            Size ts = TextRenderer.MeasureText(t, uiFont);
            int lw = ts.Width + Sc(10), lh = ts.Height + Sc(4);
            Rectangle sb = ScreenBoundsFor(r);
            int x = r.X, y = r.Y - lh - Sc(4);
            if (y < sb.Top) { x = r.X + Sc(4); y = r.Y + Sc(4); }
            var lr = new Rectangle(x, y, lw, lh);
            using (var b = new SolidBrush(Color.FromArgb(200, 20, 20, 20))) g.FillRectangle(b, lr);
            TextRenderer.DrawText(g, t, uiFont, lr, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        void DrawMagnifier(Graphics g)
        {
            const int zoom = 4;
            int src = ((int)(28 * S)) | 1;
            int box = src * zoom;
            int infoH = Sc(40);
            Rectangle sb = ScreenBoundsFor(new Rectangle(mouse, new Size(1, 1)));
            int x = mouse.X + Sc(18), y = mouse.Y + Sc(22);
            if (x + box > sb.Right) x = mouse.X - Sc(18) - box;
            if (y + box + infoH > sb.Bottom) y = mouse.Y - Sc(22) - box - infoH;

            var dest = new Rectangle(x, y, box, box);
            g.FillRectangle(Brushes.Black, dest);
            var po = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(shot, dest, new Rectangle(mouse.X - src / 2, mouse.Y - src / 2, src, src), GraphicsUnit.Pixel);
            g.PixelOffsetMode = po;
            int c = (src / 2) * zoom;
            using (var b = new SolidBrush(Color.FromArgb(90, 128, 128, 128)))
            {
                g.FillRectangle(b, x, y + c, c, zoom);
                g.FillRectangle(b, x + c + zoom, y + c, box - c - zoom, zoom);
                g.FillRectangle(b, x + c, y, zoom, c);
                g.FillRectangle(b, x + c, y + c + zoom, zoom, box - c - zoom);
            }
            using (var p = new Pen(Color.FromArgb(220, 255, 255, 255)))
            {
                g.DrawRectangle(p, x + c - 1, y + c - 1, zoom + 1, zoom + 1);
                g.DrawRectangle(p, x, y, box - 1, box - 1);
            }

            var info = new Rectangle(x, y + box, box, infoH);
            using (var b = new SolidBrush(Color.FromArgb(225, 30, 30, 30))) g.FillRectangle(b, info);
            int px = Math.Max(0, Math.Min(W - 1, mouse.X)), py = Math.Max(0, Math.Min(H - 1, mouse.Y));
            Color col = shot.GetPixel(px, py);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, "(" + (px + vs.X) + ", " + (py + vs.Y) + ")", uiFont,
                new Rectangle(info.X, info.Y, info.Width, infoH / 2), Color.White, flags);
            TextRenderer.DrawText(g, "RGB(" + col.R + "," + col.G + "," + col.B + ")", uiFont,
                new Rectangle(info.X, info.Y + infoH / 2, info.Width, infoH / 2), Color.FromArgb(210, 210, 210), flags);
        }

        // ------------------------------------------------ 工具栏

        void LayoutToolbar()
        {
            btns.Clear(); seps.Clear(); subSep = -1;
            barRect = subRect = Rectangle.Empty;
            if (phase != Phase.Selected || sel.IsEmpty) return;

            int bs = Sc(30), pad = Sc(4), sepW = Sc(9), gap = Sc(6), subGap = Sc(4);
            int tw = pad * 2;
            foreach (string id in BarIds) tw += id == "|" ? sepW : bs;
            int th = bs + pad * 2;
            int subH = Sc(32);
            int need = th + (tool != Tool.None ? subGap + subH : 0);

            Rectangle sb = ScreenBoundsFor(sel);
            int x = Math.Max(sb.Left + 2, Math.Min(sel.Right - tw, sb.Right - tw - 2));
            int y;
            if (sel.Bottom + gap + need <= sb.Bottom) { y = sel.Bottom + gap; barAbove = false; }
            else if (sel.Top - gap - need >= sb.Top) { y = sel.Top - gap - th; barAbove = true; }
            else { y = sel.Bottom - gap - th; barAbove = true; }
            barRect = new Rectangle(x, y, tw, th);

            int cx = x + pad;
            for (int i = 0; i < BarIds.Length; i++)
            {
                if (BarIds[i] == "|") { seps.Add(cx + sepW / 2); cx += sepW; continue; }
                btns.Add(new Btn(new Rectangle(cx, y + pad, bs, bs), BarIds[i], BarTips[i], false));
                cx += bs;
            }

            if (tool != Tool.None)
            {
                int sw = Sc(24);
                int subW = pad * 2 + 3 * sw + sepW + Palette.Length * sw;
                int sy = barAbove ? y - subGap - subH : y + th + subGap;
                int sx = Math.Max(sb.Left + 2, Math.Min(x, sb.Right - subW - 2));
                subRect = new Rectangle(sx, sy, subW, subH);
                int c = sx + pad, by = sy + (subH - sw) / 2;
                for (int i = 0; i < 3; i++) { btns.Add(new Btn(new Rectangle(c, by, sw, sw), "size" + i, null, true)); c += sw; }
                subSep = c + sepW / 2; c += sepW;
                for (int i = 0; i < Palette.Length; i++) { btns.Add(new Btn(new Rectangle(c, by, sw, sw), "color" + i, null, true)); c += sw; }
            }
        }

        bool OverBar(Point p)
        {
            if (!ToolbarVisible()) return false;
            return barRect.Contains(p) || subRect.Contains(p);
        }

        Btn HitBtn(Point p)
        {
            foreach (Btn b in btns) if (b.R.Contains(p)) return b;
            return null;
        }

        static Tool ToolOf(string id)
        {
            switch (id)
            {
                case "rect": return Tool.Rect;
                case "ellipse": return Tool.Ellipse;
                case "line": return Tool.Line;
                case "arrow": return Tool.Arrow;
                case "pen": return Tool.Pen;
                case "mosaic": return Tool.Mosaic;
                case "text": return Tool.Text;
            }
            return Tool.None;
        }

        void DrawPanel(Graphics g, Rectangle r)
        {
            float rad = Sc(5);
            var sh = new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height + 2);
            using (var path = Ui.RoundRect(sh, rad + 1))
            using (var b = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillPath(b, path);
            using (var path = Ui.RoundRect(r, rad))
            {
                g.FillPath(Brushes.White, path);
                using (var p = new Pen(Color.FromArgb(215, 215, 215))) g.DrawPath(p, path);
            }
        }

        void DrawToolbar(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawPanel(g, barRect);
            using (var sp = new Pen(Color.FromArgb(225, 225, 225)))
                foreach (int sx in seps) g.DrawLine(sp, sx, barRect.Top + Sc(10), sx, barRect.Bottom - Sc(10));

            Btn tipBtn = null;
            foreach (Btn b in btns)
            {
                if (b.Sub) continue;
                Tool t = ToolOf(b.Id);
                bool active = t != Tool.None && t == tool;
                bool hot = b.R.Contains(mouse) && drag == DragMode.None;
                if (hot && b.Tip != null) tipBtn = b;
                if (active || hot)
                {
                    var rr = new RectangleF(b.R.X + 1, b.R.Y + 1, b.R.Width - 2, b.R.Height - 2);
                    using (var path = Ui.RoundRect(rr, Sc(4)))
                    using (var br = new SolidBrush(active ? Ui.ActiveBg : Ui.HoverBg))
                        g.FillPath(br, path);
                }
                Color ic = active ? Color.White : Color.FromArgb(60, 60, 60);
                if (b.Id == "done") ic = Ui.Ink;
                else if (b.Id == "cancel") ic = Color.FromArgb(225, 70, 70);
                else if (b.Id == "undo" && shapes.Count == 0) ic = Color.FromArgb(190, 190, 190);
                DrawIcon(g, b.Id, b.R, ic);
            }

            if (!subRect.IsEmpty)
            {
                DrawPanel(g, subRect);
                using (var sp = new Pen(Color.FromArgb(225, 225, 225)))
                    g.DrawLine(sp, subSep, subRect.Top + Sc(8), subSep, subRect.Bottom - Sc(8));
                float[] dots = { 2.5f, 4f, 6f };
                foreach (Btn b in btns)
                {
                    if (!b.Sub) continue;
                    if (b.Id.StartsWith("size"))
                    {
                        int i = b.Id[4] - '0';
                        bool on = i == sizeIdx;
                        if (on || b.R.Contains(mouse))
                        {
                            var rr = new RectangleF(b.R.X + 1, b.R.Y + 1, b.R.Width - 2, b.R.Height - 2);
                            using (var path = Ui.RoundRect(rr, Sc(4)))
                            using (var br = new SolidBrush(on ? Ui.ActiveBg : Ui.HoverBg))
                                g.FillPath(br, path);
                        }
                        float rad = dots[i] * S;
                        float cx = b.R.X + b.R.Width / 2f, cy = b.R.Y + b.R.Height / 2f;
                        using (var br = new SolidBrush(on ? Color.White : Color.FromArgb(90, 90, 90)))
                            g.FillEllipse(br, cx - rad, cy - rad, rad * 2, rad * 2);
                    }
                    else
                    {
                        int i = b.Id[5] - '0';
                        Rectangle cr = b.R;
                        cr.Inflate(-Sc(4), -Sc(4));
                        using (var br = new SolidBrush(Palette[i])) g.FillRectangle(br, cr);
                        using (var p = new Pen(Color.FromArgb(180, 180, 180))) g.DrawRectangle(p, cr);
                        if (Palette[i].ToArgb() == curColor.ToArgb())
                        {
                            Rectangle o = b.R;
                            o.Inflate(-Sc(1.5f), -Sc(1.5f));
                            using (var p = new Pen(Ui.Ink, Math.Max(1.5f, 2 * S))) g.DrawRectangle(p, o);
                        }
                    }
                }
            }

            if (tipBtn != null) DrawTip(g, tipBtn);
            g.SmoothingMode = SmoothingMode.None;
        }

        void DrawTip(Graphics g, Btn b)
        {
            Size ts = TextRenderer.MeasureText(b.Tip, uiFont);
            int w = ts.Width + Sc(12), h = ts.Height + Sc(6);
            int x = b.R.X + b.R.Width / 2 - w / 2;
            Rectangle sb = ScreenBoundsFor(barRect);
            x = Math.Max(sb.Left + 2, Math.Min(x, sb.Right - w - 2));
            int y = barAbove ? barRect.Bottom + Sc(6) : barRect.Top - h - Sc(6);
            if (!barAbove && !subRect.IsEmpty && y < sel.Top) y = barRect.Top - h - Sc(6);
            var r = new Rectangle(x, y, w, h);
            using (var path = Ui.RoundRect(r, Sc(4)))
            using (var br = new SolidBrush(Color.FromArgb(235, 40, 40, 40))) g.FillPath(br, path);
            TextRenderer.DrawText(g, b.Tip, uiFont, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        static PointF P(RectangleF b, float x, float y) { return new PointF(b.X + x * b.Width, b.Y + y * b.Height); }

        void DrawIcon(Graphics g, string id, Rectangle r, Color c)
        {
            float s = 16 * S;
            var b = new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s);
            using (var p = new Pen(c, Math.Max(1.3f, 1.6f * S)))
            using (var br = new SolidBrush(c))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                switch (id)
                {
                    case "rect":
                        g.DrawRectangle(p, b.X + 0.1f * s, b.Y + 0.2f * s, 0.8f * s, 0.6f * s);
                        break;
                    case "ellipse":
                        g.DrawEllipse(p, b.X + 0.08f * s, b.Y + 0.16f * s, 0.84f * s, 0.68f * s);
                        break;
                    case "line":
                        g.DrawLine(p, P(b, 0.16f, 0.84f), P(b, 0.84f, 0.16f));
                        break;
                    case "arrow":
                        g.DrawLine(p, P(b, 0.18f, 0.82f), P(b, 0.8f, 0.2f));
                        g.DrawLines(p, new[] { P(b, 0.42f, 0.18f), P(b, 0.82f, 0.18f), P(b, 0.82f, 0.58f) });
                        break;
                    case "pen":
                        g.DrawPolygon(p, new[] { P(b, 0.16f, 0.84f), P(b, 0.22f, 0.6f), P(b, 0.66f, 0.16f), P(b, 0.84f, 0.34f), P(b, 0.4f, 0.78f) });
                        g.DrawLine(p, P(b, 0.56f, 0.26f), P(b, 0.74f, 0.44f));
                        break;
                    case "mosaic":
                        {
                            float cell = 0.8f * s / 3f;
                            for (int i = 0; i < 3; i++)
                                for (int j = 0; j < 3; j++)
                                    if ((i + j) % 2 == 0)
                                        g.FillRectangle(br, b.X + 0.1f * s + i * cell, b.Y + 0.1f * s + j * cell, cell, cell);
                            g.DrawRectangle(p, b.X + 0.1f * s, b.Y + 0.1f * s, 0.8f * s, 0.8f * s);
                            break;
                        }
                    case "text":
                        g.DrawLines(p, new[] { P(b, 0.18f, 0.3f), P(b, 0.18f, 0.16f), P(b, 0.82f, 0.16f), P(b, 0.82f, 0.3f) });
                        g.DrawLine(p, P(b, 0.5f, 0.16f), P(b, 0.5f, 0.86f));
                        g.DrawLine(p, P(b, 0.36f, 0.86f), P(b, 0.64f, 0.86f));
                        break;
                    case "undo":
                        g.DrawLine(p, P(b, 0.16f, 0.36f), P(b, 0.6f, 0.36f));
                        g.DrawArc(p, b.X + 0.38f * s, b.Y + 0.36f * s, 0.44f * s, 0.46f * s, -90, 180);
                        g.DrawLine(p, P(b, 0.6f, 0.82f), P(b, 0.34f, 0.82f));
                        g.DrawLines(p, new[] { P(b, 0.32f, 0.2f), P(b, 0.16f, 0.36f), P(b, 0.32f, 0.52f) });
                        break;
                    case "save":
                        g.DrawLine(p, P(b, 0.5f, 0.12f), P(b, 0.5f, 0.64f));
                        g.DrawLines(p, new[] { P(b, 0.28f, 0.44f), P(b, 0.5f, 0.66f), P(b, 0.72f, 0.44f) });
                        g.DrawLines(p, new[] { P(b, 0.14f, 0.64f), P(b, 0.14f, 0.86f), P(b, 0.86f, 0.86f), P(b, 0.86f, 0.64f) });
                        break;
                    case "cancel":
                        g.DrawLine(p, P(b, 0.22f, 0.22f), P(b, 0.78f, 0.78f));
                        g.DrawLine(p, P(b, 0.78f, 0.22f), P(b, 0.22f, 0.78f));
                        break;
                    case "done":
                        p.Width = Math.Max(1.6f, 2f * S);
                        g.DrawLines(p, new[] { P(b, 0.12f, 0.52f), P(b, 0.4f, 0.78f), P(b, 0.88f, 0.24f) });
                        break;
                }
            }
        }

        void OnButton(string id)
        {
            CommitText();
            Tool t = ToolOf(id);
            if (t != Tool.None)
            {
                tool = tool == t ? Tool.None : t;
                if (tool == Tool.Mosaic) EnsureMosaic();
            }
            else if (id.StartsWith("size")) sizeIdx = id[4] - '0';
            else if (id.StartsWith("color")) curColor = Palette[id[5] - '0'];
            else if (id == "undo") Undo();
            else if (id == "save") { Save(); return; }
            else if (id == "cancel") { Close(); return; }
            else if (id == "done") { Finish(); return; }
            LayoutToolbar();
            Invalidate();
        }

        void EnsureMosaic()
        {
            if (mosaic != null) return;
            int bsz = Math.Max(8, Sc(10));
            int sw = (W + bsz - 1) / bsz, sh = (H + bsz - 1) / bsz;
            using (var attr = new ImageAttributes())
            using (var small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb))
            {
                attr.SetWrapMode(WrapMode.TileFlipXY);
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(shot, new Rectangle(0, 0, sw, sh), 0, 0, W, H, GraphicsUnit.Pixel, attr);
                }
                mosaic = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(mosaic))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(small, new Rectangle(0, 0, sw * bsz, sh * bsz), 0, 0, sw, sh, GraphicsUnit.Pixel, attr);
                }
            }
        }

        // ------------------------------------------------ 鼠标

        int HitHandle(Point p)
        {
            if (phase != Phase.Selected || tool != Tool.None || sel.IsEmpty) return -1;
            Point[] hp = HandlePoints();
            int tol = Math.Max(5, Sc(6));
            for (int i = 0; i < hp.Length; i++)
                if (Math.Abs(p.X - hp[i].X) <= tol && Math.Abs(p.Y - hp[i].Y) <= tol) return i;
            return -1;
        }

        static Cursor HandleCursor(int h)
        {
            switch (h)
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 2: case 6: return Cursors.SizeNESW;
                case 1: case 5: return Cursors.SizeNS;
                default: return Cursors.SizeWE;
            }
        }

        void UpdateCursor(Point p)
        {
            Cursor c;
            if (OverBar(p)) c = Cursors.Hand;
            else if (phase == Phase.Choose) c = Cursors.Cross;
            else if (HitTextHandle(p) >= 0) c = HitTextHandle(p) % 2 == 0 ? Cursors.SizeNWSE : Cursors.SizeNESW;
            else if (CanGrabText() && HitHandle(p) < 0 && TextAt(p) != null) c = Cursors.SizeAll;
            else if (tool == Tool.None)
            {
                int h = HitHandle(p);
                if (h >= 0) c = HandleCursor(h);
                else if (sel.Contains(p)) c = Cursors.SizeAll;
                else c = shapes.Count == 0 ? Cursors.Cross : Cursors.Default;
            }
            else if (sel.Contains(p)) c = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            else c = Cursors.Default;
            if (Cursor != c) Cursor = c;
        }

        Rectangle Clamp(Rectangle r)
        {
            return Rectangle.Intersect(r, ClientBounds());
        }

        void ResetSelection()
        {
            RemoveTb();
            foreach (var s in shapes) if (s.Font != null) s.Font.Dispose();
            shapes.Clear();
            cur = null;
            selText = null;
            sel = Rectangle.Empty;
            phase = Phase.Choose;
            tool = Tool.None;
            drag = DragMode.None;
            hover = WindowAt(mouse);
            LayoutToolbar();
            UpdateCursor(mouse);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point p = e.Location;
            mouse = p;
            if (tb != null)
            {
                CommitText();
                if (!OverBar(p)) return;
            }
            if (e.Button == MouseButtons.Right)
            {
                if (phase == Phase.Selected || !sel.IsEmpty) ResetSelection();
                else Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (OverBar(p))
            {
                Btn b = HitBtn(p);
                if (b != null) OnButton(b.Id);
                return;
            }

            downPt = p;
            moved = false;
            if (phase == Phase.Choose) { drag = DragMode.Creating; return; }

            // 点中选中文字的四角：等比缩放
            int th = HitTextHandle(p);
            if (th >= 0)
            {
                textHandle = th;
                rsStartFont = selText.Font;
                rsStartBounds = selText.TextBounds();
                Rectangle b = rsStartBounds;
                rsAnchor = th == 0 ? new Point(b.Right, b.Bottom) : th == 1 ? new Point(b.Left, b.Bottom)
                         : th == 2 ? new Point(b.Left, b.Top) : new Point(b.Right, b.Top);
                drag = DragMode.ResizingText;
                return;
            }

            // 点中已写好的文字：选中并可拖动
            if (CanGrabText() && HitHandle(p) < 0)
            {
                Shape ts = TextAt(p);
                if (ts != null)
                {
                    selText = ts;
                    dragShape = ts;
                    dragShapeStart = ts.Pts[0];
                    drag = DragMode.MovingText;
                    Invalidate();
                    return;
                }
            }
            selText = null;   // 点在别处：取消文字选中

            if (tool == Tool.None)
            {
                int h = HitHandle(p);
                if (h >= 0) { drag = DragMode.Resizing; handle = h; selStart = sel; }
                else if (sel.Contains(p)) { drag = DragMode.Moving; selStart = sel; }
                else if (shapes.Count == 0)
                {
                    // 在选区外按下：重新框选
                    sel = Rectangle.Empty;
                    phase = Phase.Choose;
                    hover = WindowAt(p);
                    LayoutToolbar();
                    drag = DragMode.Creating;
                }
                Invalidate();
                return;
            }

            if (!sel.Contains(p)) return;
            if (tool == Tool.Text) { BeginText(p); return; }

            cur = new Shape();
            cur.Kind = tool;
            cur.Color = curColor;
            cur.Width = (tool == Tool.Mosaic ? MosaicWidths[sizeIdx] : LineWidths[sizeIdx]) * S;
            cur.Pts.Add(p);
            if (tool == Tool.Rect || tool == Tool.Ellipse || tool == Tool.Line || tool == Tool.Arrow) cur.Pts.Add(p);
            drag = DragMode.Drawing;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = e.Location;
            mouse = p;
            switch (drag)
            {
                case DragMode.Creating:
                    if (!moved && (Math.Abs(p.X - downPt.X) > 3 || Math.Abs(p.Y - downPt.Y) > 3)) moved = true;
                    if (moved)
                        sel = Clamp(Rectangle.FromLTRB(Math.Min(downPt.X, p.X), Math.Min(downPt.Y, p.Y),
                                                       Math.Max(downPt.X, p.X) + 1, Math.Max(downPt.Y, p.Y) + 1));
                    break;
                case DragMode.Moving:
                    {
                        Rectangle r = selStart;
                        r.Offset(p.X - downPt.X, p.Y - downPt.Y);
                        r.X = Math.Max(0, Math.Min(r.X, W - r.Width));
                        r.Y = Math.Max(0, Math.Min(r.Y, H - r.Height));
                        sel = r;
                        break;
                    }
                case DragMode.Resizing:
                    {
                        int dx = p.X - downPt.X, dy = p.Y - downPt.Y;
                        int l = selStart.Left, t = selStart.Top, r = selStart.Right, b = selStart.Bottom;
                        if (handle == 0 || handle == 6 || handle == 7) l += dx;
                        if (handle == 2 || handle == 3 || handle == 4) r += dx;
                        if (handle == 0 || handle == 1 || handle == 2) t += dy;
                        if (handle == 4 || handle == 5 || handle == 6) b += dy;
                        Rectangle nr = Clamp(Rectangle.FromLTRB(Math.Min(l, r), Math.Min(t, b), Math.Max(l, r), Math.Max(t, b)));
                        if (nr.Width < 1) nr.Width = 1;
                        if (nr.Height < 1) nr.Height = 1;
                        sel = nr;
                        break;
                    }
                case DragMode.Drawing:
                    if (cur != null)
                    {
                        if (cur.Kind == Tool.Pen || cur.Kind == Tool.Mosaic)
                        {
                            Point last = cur.Pts[cur.Pts.Count - 1];
                            int dx = p.X - last.X, dy = p.Y - last.Y;
                            if (dx * dx + dy * dy >= 4) cur.Pts.Add(p);
                        }
                        else
                        {
                            Point q = p, p0 = cur.Pts[0];
                            if ((ModifierKeys & Keys.Shift) != 0 && (cur.Kind == Tool.Rect || cur.Kind == Tool.Ellipse))
                            {
                                int dx = q.X - p0.X, dy = q.Y - p0.Y;
                                int m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                                q = new Point(p0.X + (dx < 0 ? -m : m), p0.Y + (dy < 0 ? -m : m));
                            }
                            else if ((ModifierKeys & Keys.Shift) != 0 && (cur.Kind == Tool.Line || cur.Kind == Tool.Arrow))
                            {
                                // 按住 Shift：吸附到水平 / 垂直 / 45°
                                double dx = q.X - p0.X, dy = q.Y - p0.Y;
                                double len = Math.Sqrt(dx * dx + dy * dy);
                                double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                                q = new Point(p0.X + (int)Math.Round(len * Math.Cos(ang)), p0.Y + (int)Math.Round(len * Math.Sin(ang)));
                            }
                            cur.Pts[1] = q;
                        }
                    }
                    break;
                case DragMode.MovingText:
                    if (!moved && (Math.Abs(p.X - downPt.X) > 2 || Math.Abs(p.Y - downPt.Y) > 2)) moved = true;
                    if (moved && dragShape != null)
                        dragShape.Pts[0] = new Point(dragShapeStart.X + p.X - downPt.X, dragShapeStart.Y + p.Y - downPt.Y);
                    break;
                case DragMode.ResizingText:
                    if (selText != null && rsStartFont != null)
                    {
                        // 按鼠标到固定对角的距离等比缩放，取横竖两个方向里放大更多的那个
                        float w0 = Math.Max(1, rsStartBounds.Width), h0 = Math.Max(1, rsStartBounds.Height);
                        float ratio = Math.Max(Math.Abs(p.X - rsAnchor.X) / w0, Math.Abs(p.Y - rsAnchor.Y) / h0);
                        float px = Math.Max(8f * S, Math.Min(400f * S, rsStartFont.Size * ratio));
                        Font nf = new Font(rsStartFont.FontFamily, px, rsStartFont.Style, GraphicsUnit.Pixel);
                        if (selText.Font != rsStartFont) selText.Font.Dispose();
                        selText.Font = nf;
                        Size ns = selText.TextBounds().Size;
                        int nx = (textHandle == 0 || textHandle == 3) ? rsAnchor.X - ns.Width : rsAnchor.X;
                        int ny = (textHandle == 0 || textHandle == 1) ? rsAnchor.Y - ns.Height : rsAnchor.Y;
                        selText.Pts[0] = new Point(nx, ny);
                    }
                    break;
                default:
                    if (phase == Phase.Choose) hover = WindowAt(p);
                    UpdateCursor(p);
                    break;
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            switch (drag)
            {
                case DragMode.Creating:
                    if (moved)
                    {
                        if (sel.Width >= 3 && sel.Height >= 3) phase = Phase.Selected;
                        else sel = Rectangle.Empty;
                    }
                    else
                    {
                        // 单击不拖动：选中鼠标下的窗口
                        sel = hover;
                        if (!sel.IsEmpty) phase = Phase.Selected;
                    }
                    break;
                case DragMode.Drawing:
                    if (cur != null && cur.IsValid()) shapes.Add(cur);
                    cur = null;
                    break;
                case DragMode.MovingText:
                    {
                        // 单击只选中（显示缩放框），双击才进入编辑
                        dragShape = null;
                        break;
                    }
                case DragMode.ResizingText:
                    if (selText != null && rsStartFont != null && selText.Font != rsStartFont) rsStartFont.Dispose();
                    rsStartFont = null;
                    break;
            }
            drag = DragMode.None;
            LayoutToolbar();
            UpdateCursor(e.Location);
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && tb == null && CanGrabText())
            {
                Shape ts = TextAt(e.Location);
                if (ts != null) { EditText(ts); return; }   // 双击文字：重新编辑
            }
            if (e.Button == MouseButtons.Left && phase == Phase.Selected && tool == Tool.None
                && sel.Contains(e.Location) && !OverBar(e.Location))
                Finish();
        }

        // ------------------------------------------------ 文字

        bool CanGrabText()
        {
            return phase == Phase.Selected && (tool == Tool.None || tool == Tool.Text);
        }

        // 选中文字的外框（比文字本身稍大一圈）；没有选中时返回空
        Rectangle SelTextFrame()
        {
            if (selText == null || tb != null || !CanGrabText() || !shapes.Contains(selText)) return Rectangle.Empty;
            Rectangle r = selText.TextBounds();
            r.Inflate(Sc(4), Sc(4));
            return r;
        }

        Point[] SelTextCorners()
        {
            Rectangle r = SelTextFrame();
            return new[] { new Point(r.Left, r.Top), new Point(r.Right, r.Top), new Point(r.Right, r.Bottom), new Point(r.Left, r.Bottom) };
        }

        int HitTextHandle(Point p)
        {
            if (SelTextFrame().IsEmpty) return -1;
            Point[] cs = SelTextCorners();
            int tol = Math.Max(5, Sc(6));
            for (int i = 0; i < 4; i++)
                if (Math.Abs(p.X - cs[i].X) <= tol && Math.Abs(p.Y - cs[i].Y) <= tol) return i;
            return -1;
        }

        void DrawSelText(Graphics g)
        {
            Rectangle r = SelTextFrame();
            if (r.IsEmpty) return;
            using (var white = new Pen(Color.White))
            using (var dash = new Pen(Ui.Ink))
            {
                dash.DashStyle = DashStyle.Dash;
                g.DrawRectangle(white, r);
                g.DrawRectangle(dash, r);
            }
            int hs = Math.Max(6, Sc(7));
            using (var pen = new Pen(Ui.Ink))
                foreach (Point c in SelTextCorners())
                {
                    var hr = new Rectangle(c.X - hs / 2, c.Y - hs / 2, hs, hs);
                    g.FillRectangle(Brushes.White, hr);
                    g.DrawRectangle(pen, hr);
                }
        }

        Shape TextAt(Point p)
        {
            for (int i = shapes.Count - 1; i >= 0; i--)
            {
                Shape s = shapes[i];
                if (s.Kind != Tool.Text) continue;
                Rectangle r = s.TextBounds();
                r.Inflate(Sc(3), Sc(3));
                if (r.Contains(p)) return s;
            }
            return null;
        }

        // 把已写好的文字重新变回输入框；Esc 取消时原样放回，清空内容再确认就是删除
        void EditText(Shape s)
        {
            editIdx = shapes.IndexOf(s);
            if (editIdx < 0) return;
            selText = null;
            shapes.RemoveAt(editIdx);
            editOrig = s;
            BeginText(s.Pts[0], s.Font, s.Color, s.Text);
            Point off = tb.GetPositionFromCharIndex(0);
            tb.Location = new Point(s.Pts[0].X - off.X, s.Pts[0].Y - off.Y);
            tb.SelectionStart = tb.Text.Length;
            Invalidate();
        }

        void CancelText()
        {
            if (tb == null) return;
            if (editOrig != null)
            {
                shapes.Insert(Math.Min(editIdx, shapes.Count), editOrig);
                tbFont = null;   // 字体还归原来的文字所有，别释放
                editOrig = null;
            }
            RemoveTb();
        }

        void BeginText(Point p)
        {
            BeginText(p, null, curColor, null);
        }

        void BeginText(Point p, Font font, Color color, string text)
        {
            tbFont = font ?? new Font("Microsoft YaHei UI", FontSizes[sizeIdx] * S, GraphicsUnit.Pixel);
            tb = new TextBox();
            tb.Multiline = true;
            tb.WordWrap = false;
            tb.BorderStyle = BorderStyle.None;
            tb.Font = tbFont;
            tb.ForeColor = color;
            tb.BackColor = color.GetBrightness() > 0.75f ? Color.FromArgb(70, 70, 70) : Color.White;
            if (text != null) tb.Text = text;
            tb.Location = p;
            ResizeTb();
            tb.TextChanged += delegate { ResizeTb(); Invalidate(); };
            Controls.Add(tb);
            tb.Focus();
            UpdateCursor(p);
            Invalidate();
        }

        void ResizeTb()
        {
            string t = tb.Text;
            if (t.Length == 0 || t.EndsWith("\n")) t += "X";
            Size sz = TextRenderer.MeasureText(t + "W", tbFont);
            tb.Size = new Size(sz.Width + Sc(4), sz.Height + Sc(2));
        }

        void CommitText()
        {
            if (tb == null) return;
            string t = tb.Text;
            if (t.Trim().Length > 0)
            {
                var s = new Shape();
                s.Kind = Tool.Text;
                s.Color = tb.ForeColor;
                s.Text = t;
                s.Font = tbFont;
                Point off = tb.GetPositionFromCharIndex(0);
                s.Pts.Add(new Point(tb.Left + off.X, tb.Top + off.Y));
                shapes.Add(s);
                tbFont = null; // 字体归 shape 所有
            }
            editOrig = null;   // 编辑已确认（内容清空就等于删掉了这段文字）
            RemoveTb();
        }

        void RemoveTb()
        {
            if (tb == null) return;
            Controls.Remove(tb);
            tb.Dispose();
            tb = null;
            if (tbFont != null) { tbFont.Dispose(); tbFont = null; }
            Focus();
            Invalidate();
        }

        // ------------------------------------------------ 键盘

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (tb != null)
            {
                if (keyData == Keys.Escape) { CancelText(); return true; }
                if (keyData == Keys.Enter) { CommitText(); return true; }   // Shift+Enter 换行
                return base.ProcessCmdKey(ref msg, keyData);
            }
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Enter:
                case Keys.Control | Keys.C:
                    Finish(); return true;
                case Keys.Control | Keys.S: Save(); return true;
                case Keys.Control | Keys.Z: Undo(); LayoutToolbar(); Invalidate(); return true;
            }
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down)
            {
                int dx = k == Keys.Left ? -1 : k == Keys.Right ? 1 : 0;
                int dy = k == Keys.Up ? -1 : k == Keys.Down ? 1 : 0;
                if (phase == Phase.Choose && drag == DragMode.None)
                {
                    Point cp = Cursor.Position;
                    Cursor.Position = new Point(cp.X + dx, cp.Y + dy);   // 方向键微调鼠标
                }
                else if (phase == Phase.Selected && drag == DragMode.None)
                {
                    Rectangle r = sel;
                    if ((keyData & Keys.Shift) != 0) { r.Width = Math.Max(1, r.Width + dx); r.Height = Math.Max(1, r.Height + dy); }
                    else r.Offset(dx, dy);
                    if (r.Left >= 0 && r.Top >= 0 && r.Right <= W && r.Bottom <= H) sel = r;
                    LayoutToolbar();
                    Invalidate();
                }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Undo()
        {
            if (shapes.Count > 0)
            {
                var s = shapes[shapes.Count - 1];
                shapes.RemoveAt(shapes.Count - 1);
                if (s.Font != null) s.Font.Dispose();
            }
            else if (phase == Phase.Selected) ResetSelection();
        }

        // ------------------------------------------------ 输出

        Bitmap Render()
        {
            Rectangle r = sel;
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(shot, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(-r.X, -r.Y);
                foreach (var s in shapes) s.Draw(g, mosaic);
            }
            return bmp;
        }

        void Finish()
        {
            if (phase != Phase.Selected || sel.IsEmpty) return;
            CommitText();
            try
            {
                using (var bmp = Render())
                using (var ms = new MemoryStream())
                {
                    var data = new DataObject();
                    data.SetData(DataFormats.Bitmap, false, bmp);
                    bmp.Save(ms, ImageFormat.Png);
                    data.SetData("PNG", false, ms);
                    Clipboard.SetDataObject(data, true, 10, 50);   // copy=true：程序退出后剪贴板里的图依然在
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制到剪贴板失败：" + ex.Message, "QuickShot");
                return;
            }
            Close();
        }

        void Save()
        {
            if (phase != Phase.Selected || sel.IsEmpty) return;
            CommitText();
            using (var bmp = Render())
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp";
                dlg.FileName = "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                dlg.InitialDirectory = lastSaveDir ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (dlg.ShowDialog(this) != DialogResult.OK) { Activate(); return; }
                string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                ImageFormat fmt = ext == ".jpg" || ext == ".jpeg" ? ImageFormat.Jpeg : ext == ".bmp" ? ImageFormat.Bmp : ImageFormat.Png;
                try
                {
                    bmp.Save(dlg.FileName, fmt);
                    lastSaveDir = Path.GetDirectoryName(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：" + ex.Message, "QuickShot");
                    return;
                }
            }
            Close();
        }
    }
}
