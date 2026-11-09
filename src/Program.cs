// SoftBright — Samsung Odyssey OLED G8 (G85SB) için yazılımsal parlaklık kontrolü.
// Monitör DDC/CI komutlarına cevap vermediği için Windows Magnification API'nin
// tam ekran renk matrisi kullanılır (Windows'un kendi Renk Filtreleri ile aynı mekanizma).
//
// Tepsi simgesi Windows ses simgesi gibi davranır:
//   - simge üzerinde fare tekerleği -> parlaklık artar / azalır, küçük bir flyout belirir
//   - sol tık -> kaydırıcılı flyout açılır
//   - Windows 11'de simge gizli alana değil, doğrudan sistem simgelerinin yanına yerleştirilir
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SoftBright
{
    static class Native
    {
        // --- Magnification ---
        [DllImport("Magnification.dll")] public static extern bool MagInitialize();
        [DllImport("Magnification.dll")] public static extern bool MagUninitialize();
        [StructLayout(LayoutKind.Sequential)]
        public struct MAGCOLOREFFECT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)] public float[] transform;
        }
        [DllImport("Magnification.dll")] public static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT effect);

        // --- Kısayol / genel ---
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4;
        public const uint VK_END = 0x23, VK_HOME = 0x24, VK_UP = 0x26, VK_DOWN = 0x28;

        // --- Fare kancası (tepsi simgesi üzerinde tekerlek) ---
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string name);
        public const int WH_MOUSE_LL = 14;
        public const int WM_MOUSEWHEEL = 0x020A;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        // --- Tepsi simgesinin ekran konumu ---
        [StructLayout(LayoutKind.Sequential)]
        public struct NOTIFYICONIDENTIFIER { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
        [DllImport("shell32.dll")] public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        // --- Windows 11 yuvarlak köşe ---
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;
    }

    /// <summary>Ekranı kısan motor. Yüzde değeri algılanan parlaklık (luminans) olarak yorumlanır.</summary>
    class Dimmer : IDisposable
    {
        bool _init;
        int _level = 100;
        public int Level { get { return _level; } }

        public bool Init()
        {
            _init = Native.MagInitialize();
            return _init;
        }

        public bool Apply(int level)
        {
            if (!_init) return false;
            level = Math.Max(Settings.MinLevel, Math.Min(100, level));
            _level = level;
            return SetScale((float)Math.Pow(level / 100.0, 1.0 / 2.2));
        }

        public void Reapply() { Apply(_level); }

        bool SetScale(float s)
        {
            var e = new Native.MAGCOLOREFFECT();
            e.transform = new float[25];
            e.transform[0] = s;    // R
            e.transform[6] = s;    // G
            e.transform[12] = s;   // B
            e.transform[18] = 1f;  // A
            e.transform[24] = 1f;
            return Native.MagSetFullscreenColorEffect(ref e);
        }

        public void Dispose()
        {
            if (!_init) return;
            try { SetScale(1f); } catch { }
            try { Native.MagUninitialize(); } catch { }
            _init = false;
        }
    }

    /// <summary>Arayüz metinleri: sistem dili Türkçe ise Türkçe, değilse İngilizce.</summary>
    static class L
    {
        static readonly bool Tr = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";
        public const string AppName = "SoftBright";
        public static string Percent(int v) { return Tr ? "%" + v : v + "%"; }
        public static string HintWheel { get { return Tr ? "Simge üzerinde tekerlek : artır / azalt" : "Mouse wheel over the icon : up / down"; } }
        public static string HintKeys { get { return Tr ? "Ctrl+Alt+↑ / ↓  : artır / azalt" : "Ctrl+Alt+↑ / ↓  : up / down"; } }
        public static string HintHomeEnd { get { return Tr ? "Ctrl+Alt+Home / End : %100 / en düşük" : "Ctrl+Alt+Home / End : 100% / minimum"; } }
        public static string AutoStart { get { return Tr ? "Windows ile başlat" : "Start with Windows"; } }
        public static string Donate { get { return Tr ? "☕ Bağış yap (Buy Me a Coffee)" : "☕ Donate (Buy Me a Coffee)"; } }
        public static string Project { get { return Tr ? "Proje sayfası (GitHub)" : "Project page (GitHub)"; } }
        public static string Exit { get { return Tr ? "Çıkış" : "Exit"; } }
        public static string ApplyFailed { get { return Tr ? "Parlaklık uygulanamadı" : "Could not apply brightness"; } }
        public static string Tooltip(int v) { return Tr ? "Parlaklık: %" + v + "  (tekerlek / Ctrl+Alt+↑↓)" : "Brightness: " + v + "%  (wheel / Ctrl+Alt+↑↓)"; }
        public static string AlreadyRunning { get { return Tr ? "SoftBright zaten çalışıyor. Görev çubuğundaki tepsi simgesine bakın." : "SoftBright is already running. Look for its icon in the system tray."; } }
        public static string MagFailed { get { return Tr ? "Ekran kısma motoru (Magnification API) başlatılamadı." : "Could not initialize the dimming engine (Magnification API)."; } }
    }

    /// <summary>SOFTBRIGHT_DEBUG=1 ortam değişkeni varsa %APPDATA%\SoftBright\debug.log dosyasına yazar.</summary>
    static class Log
    {
        static readonly bool Enabled = Environment.GetEnvironmentVariable("SOFTBRIGHT_DEBUG") == "1";
        static readonly string Path_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoftBright", "debug.log");
        public static void Write(string s)
        {
            if (!Enabled) return;
            try { File.AppendAllText(Path_, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine); } catch { }
        }
    }

    static class Settings
    {
        public const string DonateUrl = "https://buymeacoffee.com/harunsariaslan";
        public const string ProjectUrl = "https://github.com/harunsariaslan7/SoftBright";
        public const int MinLevel = 5;
        public static int Level = 100;
        public static int Step = 5;       // kısayol adımı
        public static int WheelStep = 5;  // tekerlek adımı (bir kademe)

        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoftBright"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.txt"); } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var kv = line.Split(new[] { '=' }, 2);
                    if (kv.Length != 2) continue;
                    int v;
                    if (!int.TryParse(kv[1].Trim(), out v)) continue;
                    switch (kv[0].Trim())
                    {
                        case "level": Level = v; break;
                        case "step": if (v > 0) Step = v; break;
                        case "wheelstep": if (v > 0) WheelStep = v; break;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(FilePath, new[] { "level=" + Level, "step=" + Step, "wheelstep=" + WheelStep });
            }
            catch { }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "SoftBright";

        public static bool AutoStart
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                        return k != null && k.GetValue(RunName) != null;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                        else k.DeleteValue(RunName, false);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Windows 11 tepsi simgesini "gizli simgeler" yerine doğrudan görev çubuğuna (ses/ağ simgelerinin
        /// yanına) taşır. Explorer, simge ilk göründüğünde HKCU\Control Panel\NotifyIconSettings altında
        /// bir kayıt oluşturur; IsPromoted=1 yapıldığında simge sabitlenir.
        /// </summary>
        /// <returns>Kayıt bulunduysa true; changed: değer bu çağrıda değiştirildiyse true.</returns>
        public static bool PromoteTrayIcon(out bool changed)
        {
            changed = false;
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true))
                {
                    if (root == null) return false;
                    string exe = Application.ExecutablePath;
                    bool found = false;
                    foreach (var name in root.GetSubKeyNames())
                    {
                        using (var k = root.OpenSubKey(name, true))
                        {
                            if (k == null) continue;
                            var p = k.GetValue("ExecutablePath") as string;
                            if (p == null || !string.Equals(p, exe, StringComparison.OrdinalIgnoreCase)) continue;
                            found = true;
                            var v = k.GetValue("IsPromoted");
                            if (!(v is int) || (int)v != 1)
                            {
                                k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                                changed = true;
                            }
                        }
                    }
                    return found;
                }
            }
            catch { return false; }
        }
    }

    /// <summary>Global kısayolları alan görünmez pencere.</summary>
    class HotkeyWindow : NativeWindow, IDisposable
    {
        readonly TrayApp _app;
        const int ID_UP = 1, ID_DOWN = 2, ID_MAX = 3, ID_MIN = 4;

        public HotkeyWindow(TrayApp app)
        {
            _app = app;
            CreateHandle(new CreateParams());
            Native.RegisterHotKey(Handle, ID_UP, Native.MOD_CONTROL | Native.MOD_ALT, Native.VK_UP);
            Native.RegisterHotKey(Handle, ID_DOWN, Native.MOD_CONTROL | Native.MOD_ALT, Native.VK_DOWN);
            Native.RegisterHotKey(Handle, ID_MAX, Native.MOD_CONTROL | Native.MOD_ALT, Native.VK_HOME);
            Native.RegisterHotKey(Handle, ID_MIN, Native.MOD_CONTROL | Native.MOD_ALT, Native.VK_END);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                switch (m.WParam.ToInt32())
                {
                    case ID_UP: _app.Adjust(+Settings.Step); break;
                    case ID_DOWN: _app.Adjust(-Settings.Step); break;
                    case ID_MAX: _app.SetLevel(100, true, true); break;
                    case ID_MIN: _app.SetLevel(Settings.MinLevel, true, true); break;
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            for (int i = 1; i <= 4; i++) Native.UnregisterHotKey(Handle, i);
            DestroyHandle();
        }
    }

    /// <summary>
    /// Düşük seviyeli fare kancası: tekerlek tepsi simgesinin üzerinde çevrildiğinde
    /// olayı yakalar (Windows ses simgesindeki davranışın aynısı).
    /// </summary>
    class TrayWheelHook : IDisposable
    {
        readonly TrayApp _app;
        readonly Native.LowLevelMouseProc _proc; // GC tarafından toplanmasın diye alanda tutulur
        IntPtr _hook;

        public TrayWheelHook(TrayApp app)
        {
            _app = app;
            _proc = Callback;
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
            Log.Write("kanca=" + _hook + " err=" + Marshal.GetLastWin32Error());
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == 0 && wParam.ToInt64() == Native.WM_MOUSEWHEEL)
            {
                var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                // Kanca 300 ms içinde dönmek zorunda; Explorer'a sormak yerine önbellekteki dikdörtgen kullanılır.
                Rectangle r = _app.CachedIconRect;
                Log.Write("tekerlek pt=" + info.pt.x + "," + info.pt.y + " rect=" + r);
                if (!r.IsEmpty && HitTest(r, info.pt.x, info.pt.y))
                {
                    int delta = (short)((info.mouseData >> 16) & 0xFFFF);
                    _app.OnTrayWheel(delta);
                    return new IntPtr(1); // olayı yut, görev çubuğuna gitmesin
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>
        /// Otomatik gizlenen görev çubuğu açılırken simge kayar ve önbellek bir an eski kalabilir.
        /// Yatay çubukta aynı x aralığında ±64 px dikey, dikey çubukta aynı y aralığında ±64 px yatay tolerans tanınır.
        /// </summary>
        static bool HitTest(Rectangle r, int x, int y)
        {
            if (r.Contains(x, y)) return true;
            const int tol = 64;
            bool horizontalBar = true;
            IntPtr tb = Native.FindWindow("Shell_TrayWnd", null);
            Native.RECT tr;
            if (tb != IntPtr.Zero && Native.GetWindowRect(tb, out tr)) horizontalBar = (tr.right - tr.left) >= (tr.bottom - tr.top);
            if (horizontalBar) return x >= r.Left && x < r.Right && y >= r.Top - tol && y < r.Bottom + tol;
            return y >= r.Top && y < r.Bottom && x >= r.Left - tol && x < r.Right + tol;
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }
    }

    /// <summary>Windows 11 ses kaydırıcısına benzeyen, kendi çizilen yatay kaydırıcı.</summary>
    class Slider : Control
    {
        int _value = 100;
        bool _dragging;
        public int Minimum = Settings.MinLevel;
        public int Maximum = 100;
        public int WheelStep = 5;
        public Color Accent = Color.FromArgb(255, 185, 0);
        public Color Track = Color.FromArgb(158, 158, 158);
        public delegate void WheelHandler(object sender, int delta);
        public event EventHandler ValueChanged;
        public event WheelHandler Wheel;

        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(Minimum, Math.Min(Maximum, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        int Pad { get { return Height / 2; } }

        int ValueFromX(int x)
        {
            double usable = Math.Max(1, Width - 2 * Pad);
            double ratio = Math.Max(0, Math.Min(1, (x - Pad) / usable));
            return Minimum + (int)Math.Round(ratio * (Maximum - Minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _dragging = true; Focus();
            Value = ValueFromX(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) Value = ValueFromX(e.X);
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
            if (Wheel != null) Wheel(this, Math.Sign(e.Delta) * WheelStep);
            else Value += Math.Sign(e.Delta) * WheelStep;
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value -= 1; e.Handled = true; break;
                case Keys.Right: case Keys.Up: Value += 1; e.Handled = true; break;
                case Keys.Home: Value = Minimum; e.Handled = true; break;
                case Keys.End: Value = Maximum; e.Handled = true; break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int cy = Height / 2;
            int th = Math.Max(3, Height / 10);          // ray kalınlığı
            int tr = Math.Max(8, (int)(Height * 0.36)); // baş parmak yarıçapı
            int x0 = Pad, x1 = Width - Pad;
            double ratio = (_value - Minimum) / (double)Math.Max(1, Maximum - Minimum);
            int xv = x0 + (int)Math.Round(ratio * (x1 - x0));

            using (var p = new Pen(Track, th) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(p, x0, cy, x1, cy);
            using (var p = new Pen(Accent, th) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                if (xv > x0) g.DrawLine(p, x0, cy, xv, cy);

            // Win11 tarzı baş parmak: koyu halka + vurgu renkli iç daire
            using (var ring = new SolidBrush(Color.FromArgb(69, 69, 69)))
                g.FillEllipse(ring, xv - tr, cy - tr, tr * 2, tr * 2);
            int ir = _dragging || Focused ? tr - 4 : tr - 5;
            using (var dot = new SolidBrush(Accent))
                g.FillEllipse(dot, xv - ir, cy - ir, ir * 2, ir * 2);
        }
    }

    /// <summary>Ses flyout'u gibi: simge + kaydırıcı + yüzde. Tekerlek/kısayolda kısa süre görünür.</summary>
    class PopupForm : Form
    {
        readonly TrayApp _app;
        readonly Slider _slider;
        readonly Label _value;
        readonly Label _glyph;
        readonly Timer _hide;
        readonly float _s; // DPI ölçeği
        bool _updating;

        public PopupForm(TrayApp app)
        {
            _app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(44, 44, 44);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10f);

            using (var g = CreateGraphics()) _s = g.DpiX / 96f;
            Size = new Size(S(340), S(66));

            _glyph = new Label
            {
                Text = "", // Segoe MDL2 / Fluent "Brightness"
                Font = new Font("Segoe MDL2 Assets", 14f),
                ForeColor = Color.White,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(S(12), 0, S(36), Height)
            };
            _value = new Label
            {
                Text = "100",
                Font = new Font("Segoe UI", 12.5f),
                ForeColor = Color.White,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(Width - S(60), 0, S(50), Height)
            };
            _slider = new Slider
            {
                WheelStep = Settings.WheelStep,
                BackColor = BackColor,
                Bounds = new Rectangle(S(50), (Height - S(28)) / 2, Width - S(50) - S(62), S(28)),
                Value = 100
            };
            _slider.ValueChanged += delegate { if (!_updating) _app.SetLevel(_slider.Value, true, false); };
            _slider.Wheel += delegate(object s, int d) { _app.SetLevel(_slider.Value + d, true, false); };

            Controls.Add(_glyph);
            Controls.Add(_value);
            Controls.Add(_slider);

            _hide = new Timer { Interval = 1800 };
            _hide.Tick += delegate { _hide.Stop(); if (!Bounds.Contains(Cursor.Position)) Hide(); else _hide.Start(); };

            Deactivate += delegate { _hide.Stop(); Hide(); };
            MouseWheel += delegate(object s, MouseEventArgs e) { _app.SetLevel(_slider.Value + Math.Sign(e.Delta) * Settings.WheelStep, true, false); };

            var h = Handle; // BeginInvoke için tutamağı hemen oluştur
        }

        int S(int px) { return (int)Math.Round(px * _s); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int pref = Native.DWMWCP_ROUND;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch { }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW: Alt+Tab'da görünmesin
                return cp;
            }
        }

        public void SetValue(int v)
        {
            _updating = true;
            _slider.Value = v;
            _value.Text = v.ToString();
            _updating = false;
        }

        /// <summary>Flyout'u tepsi simgesinin hemen üstünde göster. autoHide: kısa süre sonra kendiliğinden kapanır.</summary>
        public void ShowAt(Rectangle anchor, bool autoHide)
        {
            var wa = Screen.FromRectangle(anchor).WorkingArea;
            int x = anchor.Left + anchor.Width / 2 - Width / 2;
            int y = anchor.Top - Height - S(12);
            if (y < wa.Top) y = Math.Min(anchor.Bottom + S(12), wa.Bottom - Height);
            x = Math.Max(wa.Left + S(8), Math.Min(x, wa.Right - Width - S(8)));
            Location = new Point(x, y);
            if (!Visible) Show();
            if (autoHide) { _hide.Stop(); _hide.Start(); }
            else { _hide.Stop(); Activate(); _slider.Focus(); }
        }
    }

    class TrayApp : ApplicationContext
    {
        readonly Dimmer _dim;
        readonly NotifyIcon _icon;
        readonly HotkeyWindow _hotkeys;
        readonly TrayWheelHook _wheel;
        readonly Timer _keepAlive;
        readonly PopupForm _popup;
        ToolStripMenuItem _autoStartItem;
        Icon _currentIcon;
        bool _cleaned;
        int _promoteTries;

        // NotifyIcon'un gizli pencere/kimlik bilgisi (Shell_NotifyIconGetRect için)
        IntPtr _iconHwnd;
        uint _iconId;
        Rectangle _cachedRect;
        public Rectangle CachedIconRect { get { return _cachedRect; } }

        void RefreshIconRect()
        {
            Rectangle r;
            _cachedRect = TryGetIconRect(out r) ? r : Rectangle.Empty;
        }

        public TrayApp(Dimmer dim)
        {
            _dim = dim;
            _popup = new PopupForm(this);

            _icon = new NotifyIcon();
            _icon.ContextMenuStrip = BuildMenu();
            _icon.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (_popup.Visible) _popup.Hide(); else _popup.ShowAt(IconRectOrCursor(), false);
                }
            };
            _icon.Visible = true;
            ResolveIconIdentity();
            RefreshIconRect();

            _hotkeys = new HotkeyWindow(this);
            _wheel = new TrayWheelHook(this);

            // Bazı olaylar (ekran değişimi, uyku, oyun) efekti sıfırlayabilir; periyodik olarak yeniden uygula.
            // İlk saniyelerde ayrıca simgeyi görev çubuğuna sabitlemeyi dener (Explorer kaydı oluşturduktan sonra).
            _keepAlive = new Timer { Interval = 500 };
            int tick = 0;
            _keepAlive.Tick += delegate
            {
                RefreshIconRect();
                if (++tick % 3 != 0) return; // aşağısı 1,5 saniyede bir
                _dim.Reapply();
                if (_promoteTries < 10)
                {
                    bool changed;
                    if (Settings.PromoteTrayIcon(out changed))
                    {
                        _promoteTries = 10;
                        // Explorer IsPromoted değerini simge eklenirken okur; simgeyi kaldırıp yeniden ekle.
                        if (changed) { _icon.Visible = false; _icon.Visible = true; ResolveIconIdentity(); RefreshIconRect(); }
                    }
                    else _promoteTries++;
                }
            };
            _keepAlive.Start();
            SystemEvents.DisplaySettingsChanged += delegate { _dim.Reapply(); };
            SystemEvents.SessionSwitch += delegate { _dim.Reapply(); };
            SystemEvents.PowerModeChanged += delegate { _dim.Reapply(); };

            Application.ApplicationExit += delegate { Cleanup(); };

            SetLevel(Settings.Level, false, false);
        }

        void ResolveIconIdentity()
        {
            try
            {
                var t = typeof(NotifyIcon);
                var idF = t.GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
                var winF = t.GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
                if (idF == null || winF == null) return;
                _iconId = (uint)(int)idF.GetValue(_icon);
                var win = winF.GetValue(_icon) as NativeWindow;
                if (win != null) _iconHwnd = win.Handle;
                Log.Write("simge hwnd=" + _iconHwnd + " id=" + _iconId);
            }
            catch (Exception ex) { Log.Write("simge kimligi alinamadi: " + ex.Message); }
        }

        public bool TryGetIconRect(out Rectangle rect)
        {
            rect = Rectangle.Empty;
            if (_iconHwnd == IntPtr.Zero) return false;
            var id = new Native.NOTIFYICONIDENTIFIER();
            id.cbSize = (uint)Marshal.SizeOf(typeof(Native.NOTIFYICONIDENTIFIER));
            id.hWnd = _iconHwnd;
            id.uID = _iconId;
            Native.RECT r;
            if (Native.Shell_NotifyIconGetRect(ref id, out r) != 0) return false;
            rect = Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
            return rect.Width > 0 && rect.Height > 0;
        }

        Rectangle IconRectOrCursor()
        {
            Rectangle r;
            if (TryGetIconRect(out r)) return r;
            var p = Cursor.Position;
            return new Rectangle(p.X - 12, p.Y - 12, 24, 24);
        }

        /// <summary>Fare kancasından gelir; işi UI kuyruğuna atar ki kanca hızlı dönsün.</summary>
        public void OnTrayWheel(int delta)
        {
            int d = Math.Sign(delta) * Settings.WheelStep;
            _popup.BeginInvoke(new Action(delegate { SetLevel(_dim.Level + d, true, true); }));
        }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add(new ToolStripMenuItem(L.AppName) { Enabled = false });
            m.Items.Add(new ToolStripSeparator());
            foreach (int lvl in new[] { 100, 80, 60, 40, 25, 10 })
            {
                int captured = lvl;
                var it = new ToolStripMenuItem(L.Percent(lvl));
                it.Click += delegate { SetLevel(captured, true, false); };
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(L.HintWheel) { Enabled = false });
            m.Items.Add(new ToolStripMenuItem(L.HintKeys) { Enabled = false });
            m.Items.Add(new ToolStripMenuItem(L.HintHomeEnd) { Enabled = false });
            m.Items.Add(new ToolStripSeparator());
            _autoStartItem = new ToolStripMenuItem(L.AutoStart) { CheckOnClick = true, Checked = Settings.AutoStart };
            _autoStartItem.CheckedChanged += delegate { Settings.AutoStart = _autoStartItem.Checked; };
            m.Items.Add(_autoStartItem);
            m.Items.Add(new ToolStripSeparator());
            var donate = new ToolStripMenuItem(L.Donate);
            donate.Click += delegate { OpenUrl(Settings.DonateUrl); };
            m.Items.Add(donate);
            var site = new ToolStripMenuItem(L.Project);
            site.Click += delegate { OpenUrl(Settings.ProjectUrl); };
            m.Items.Add(site);
            m.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem(L.Exit);
            exit.Click += delegate { ExitThread(); };
            m.Items.Add(exit);
            return m;
        }

        static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); } catch { }
        }

        public void Adjust(int delta) { SetLevel(_dim.Level + delta, true, true); }

        public void SetLevel(int level, bool save, bool showOsd)
        {
            level = Math.Max(Settings.MinLevel, Math.Min(100, level));
            if (!_dim.Apply(level))
            {
                _icon.Text = L.ApplyFailed;
                return;
            }
            Settings.Level = level;
            if (save) Settings.Save();

            _icon.Text = L.Tooltip(level);
            UpdateIcon(level);
            _popup.SetValue(level);
            if (showOsd) _popup.ShowAt(IconRectOrCursor(), true);
        }

        void UpdateIcon(int level)
        {
            var old = _currentIcon;
            _currentIcon = MakeIcon(level);
            _icon.Icon = _currentIcon;
            if (old != null) old.Dispose();
        }

        static Icon MakeIcon(int level)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                // Arka plan: düşük seviyede koyu gri, yüksek seviyede kehribar.
                double t = (level - Settings.MinLevel) / (double)(100 - Settings.MinLevel);
                Color lo = Color.FromArgb(70, 70, 70), hi = Color.FromArgb(255, 170, 30);
                var bg = Color.FromArgb(
                    (int)(lo.R + (hi.R - lo.R) * t),
                    (int)(lo.G + (hi.G - lo.G) * t),
                    (int)(lo.B + (hi.B - lo.B) * t));
                using (var path = RoundedRect(new Rectangle(1, 1, 30, 30), 8))
                using (var b = new SolidBrush(bg))
                    g.FillPath(b, path);

                string text = level.ToString();
                float size = level >= 100 ? 12f : 16f;
                using (var f = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var sz = g.MeasureString(text, f);
                    g.DrawString(text, f, Brushes.White, (32 - sz.Width) / 2f, (32 - sz.Height) / 2f);
                }

                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { Native.DestroyIcon(h); }
            }
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Cleanup()
        {
            if (_cleaned) return;
            _cleaned = true;
            try { _keepAlive.Stop(); } catch { }
            try { _wheel.Dispose(); } catch { }
            try { _hotkeys.Dispose(); } catch { }
            try { _icon.Visible = false; _icon.Dispose(); } catch { }
            try { _dim.Dispose(); } catch { }
        }

        protected override void ExitThreadCore()
        {
            Cleanup();
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new System.Threading.Mutex(true, "SoftBright_TekOrnek", out created))
            {
                if (!created)
                {
                    MessageBox.Show(L.AlreadyRunning, L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Settings.Load();

                var dim = new Dimmer();
                if (!dim.Init())
                {
                    MessageBox.Show(L.MagFailed, L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Application.ThreadException += delegate { dim.Dispose(); };
                AppDomain.CurrentDomain.UnhandledException += delegate { dim.Dispose(); };

                try { Application.Run(new TrayApp(dim)); }
                finally { dim.Dispose(); }
            }
        }
    }
}
