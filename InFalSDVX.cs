using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InFalSDVX
{
    public class MainForm : Form
    {
        // ── Win32 API Imports ──────────────────────────────────────────────────
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(4)] public MOUSEINPUT mi;
            [FieldOffset(4)] public KEYBDINPUT ki;
            [FieldOffset(4)] public HARDWAREINPUT hi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // ── Constants ──────────────────────────────────────────────────────────
        private const uint INPUT_MOUSE       = 0;
        private const uint INPUT_KEYBOARD    = 1;
        private const int  WH_KEYBOARD_LL    = 13;
        private const int  WH_MOUSE_LL       = 14;

        private const int  WM_KEYDOWN        = 0x0100;
        private const int  WM_KEYUP          = 0x0101;
        private const int  WM_SYSKEYDOWN     = 0x0104;
        private const int  WM_SYSKEYUP       = 0x0105;

        private const int  WM_LBUTTONDOWN    = 0x0201;
        private const int  WM_LBUTTONUP      = 0x0202;
        private const int  WM_RBUTTONDOWN    = 0x0204;
        private const int  WM_RBUTTONUP      = 0x0205;

        private const uint MOUSEEVENTF_MOVE    = 0x0001;
        private const uint KEYEVENTF_KEYUP     = 0x0002;
        private const uint KEYEVENTF_SCANCODE  = 0x0008;
        private const uint MAPVK_VK_TO_VSC     = 0;

        private const int  WM_HOTKEY         = 0x0312;
        private const int  HOTKEY_F8_ID      = 9000;

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        private LowLevelProc _kbProc;
        private LowLevelProc _mouseProc;
        private IntPtr _kbHook    = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;

        // ── Settings ───────────────────────────────────────────────────────────
        // Knob movement bindings (Keyboard Key -> Mouse X move)
        private Keys keyLeft   = Keys.Q;
        private Keys keyLeft2  = Keys.A;
        private Keys keyRight  = Keys.E;
        private Keys keyRight2 = Keys.D;

        // Mouse click bindings (Mouse Click -> Keyboard Key output)
        private Keys keyLClick = Keys.F;      // Left Mouse Click  -> sends key 'F'
        private Keys keyRClick = Keys.G;      // Right Mouse Click -> sends key 'G'

        private Keys keyToggle = Keys.F8;     // Global Toggle Hotkey

        private bool isEnabled      = true;
        private bool isLeftPressed  = false;
        private bool isLeft2Pressed = false;
        private bool isRightPressed = false;
        private bool isRight2Pressed= false;

        private bool isRealLMBDown  = false;
        private bool isRealRMBDown  = false;

        private volatile bool isRunning = true;
        private int moveSpeed = 15;
        private System.Threading.Thread moveThread;
        private NotifyIcon trayIcon;

        private static readonly string SettingsPath =
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.ExecutablePath),
                "settings.ini");

        // ── Minimalist UI Controls ─────────────────────────────────────────────
        private Label  lblStatus;
        private Button btnToggle;
        private Button btnBindLeft,  btnBindLeft2;
        private Button btnBindRight, btnBindRight2;
        private Button btnBindLClick, btnBindRClick;
        private TrackBar tbSpeed;
        private Label  lblSpeed;
        private TextBox txtLog;

        private Button bindingTarget = null;

        public MainForm()
        {
            LoadSettings();
            InitializeComponent();

            _kbProc    = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;
            _kbHook    = SetHook(WH_KEYBOARD_LL, _kbProc);
            _mouseHook = SetHook(WH_MOUSE_LL,    _mouseProc);

            moveThread = new System.Threading.Thread(MoveLoop);
            moveThread.IsBackground = true;
            moveThread.Priority     = System.Threading.ThreadPriority.Highest;
            moveThread.Start();

            Log("Ready. Remapper: ACTIVE");
            if (!IsAdministrator())
            {
                Log("NOTE: If game ignores inputs, run InFalSDVX as Administrator!");
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Register F8 as system-wide Global Hotkey so it works background/tabbed out
            RegisterHotKey(this.Handle, HOTKEY_F8_ID, 0, (uint)keyToggle);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnregisterHotKey(this.Handle, HOTKEY_F8_ID);
            SaveSettings();
            isRunning = false;
            UnhookWindowsHookEx(_kbHook);
            UnhookWindowsHookEx(_mouseHook);
            if (trayIcon != null) trayIcon.Dispose();
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_F8_ID)
            {
                ToggleRemapper(!isEnabled);
            }
            base.WndProc(ref m);
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        private void SaveSettings()
        {
            try
            {
                var lines = new System.Text.StringBuilder();
                lines.AppendLine("[Keys]");
                lines.AppendLine("Left="   + (int)keyLeft);
                lines.AppendLine("Left2="  + (int)keyLeft2);
                lines.AppendLine("Right="  + (int)keyRight);
                lines.AppendLine("Right2=" + (int)keyRight2);
                lines.AppendLine("LClick=" + (int)keyLClick);
                lines.AppendLine("RClick=" + (int)keyRClick);
                lines.AppendLine("[Speed]");
                lines.AppendLine("Value="  + moveSpeed);
                System.IO.File.WriteAllText(SettingsPath, lines.ToString());
            }
            catch { }
        }

        private void LoadSettings()
        {
            if (!System.IO.File.Exists(SettingsPath)) return;
            try
            {
                foreach (var raw in System.IO.File.ReadAllLines(SettingsPath))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("[") || line.Length == 0) continue;
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    var key = parts[0].Trim();
                    var val = parts[1].Trim();
                    int num;
                    if (!int.TryParse(val, out num)) continue;
                    switch (key)
                    {
                        case "Left":   keyLeft   = (Keys)num; break;
                        case "Left2":  keyLeft2  = (Keys)num; break;
                        case "Right":  keyRight  = (Keys)num; break;
                        case "Right2": keyRight2 = (Keys)num; break;
                        case "LClick": keyLClick = (Keys)num; break;
                        case "RClick": keyRClick = (Keys)num; break;
                        case "Value":  moveSpeed = Math.Max(1, Math.Min(60, num)); break;
                    }
                }
            }
            catch { }
        }

        // ── Input Injections ───────────────────────────────────────────────────
        private static void InjectMouseMove(int dx, int dy)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dx = dx;
            inputs[0].mi.dy = dy;
            inputs[0].mi.mouseData = 0;
            inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE;
            inputs[0].mi.time = 0;
            inputs[0].mi.dwExtraInfo = IntPtr.Zero;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, UIntPtr.Zero);
        }

        private static void InjectKey(Keys key, bool down)
        {
            if (key == Keys.None) return;
            ushort vk = (ushort)key;
            ushort scanCode = (ushort)MapVirtualKey((uint)key, MAPVK_VK_TO_VSC);

            // 1. SendInput with Hardware Scan Code (DirectInput / RawInput / DirectX Game Engine compatible)
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].ki.wVk = vk;
            inputs[0].ki.wScan = scanCode;
            inputs[0].ki.dwFlags = (down ? 0 : KEYEVENTF_KEYUP) | KEYEVENTF_SCANCODE;
            inputs[0].ki.time = 0;
            inputs[0].ki.dwExtraInfo = IntPtr.Zero;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));

            // 2. Standard VirtualKey SendInput
            INPUT[] inputs2 = new INPUT[1];
            inputs2[0].type = INPUT_KEYBOARD;
            inputs2[0].ki.wVk = vk;
            inputs2[0].ki.wScan = scanCode;
            inputs2[0].ki.dwFlags = down ? 0 : KEYEVENTF_KEYUP;
            inputs2[0].ki.time = 0;
            inputs2[0].ki.dwExtraInfo = IntPtr.Zero;
            SendInput(1, inputs2, Marshal.SizeOf(typeof(INPUT)));

            // 3. Fallback keybd_event with scan code
            keybd_event((byte)vk, (byte)scanCode, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // ── UI Initialization ──────────────────────────────────────────────────
        private void InitializeComponent()
        {
            this.Text            = "InFalSDVX";
            this.Size            = new Size(320, 335);
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox     = false;
            this.BackColor       = Color.FromArgb(28, 28, 28);
            this.ForeColor       = Color.White;
            this.Font            = new Font("Segoe UI", 9F, FontStyle.Regular);

            // Status & Toggle
            lblStatus = new Label
            {
                Text      = "Status: ACTIVE",
                Font      = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                Location  = new Point(14, 14),
                AutoSize  = true
            };

            btnToggle = new Button
            {
                Text      = "Toggle (F8)",
                Location  = new Point(200, 10),
                Size      = new Size(90, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Cursor    = Cursors.Hand
            };
            btnToggle.FlatAppearance.BorderSize  = 1;
            btnToggle.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            btnToggle.Click += (s, e) => ToggleRemapper(!isEnabled);

            // Row 1: Left / Right knob keys
            Label lblLeft = new Label { Text = "Left:", Location = new Point(14, 48), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
            btnBindLeft = MakeBindButton(keyLeft.ToString(), new Point(54, 44), Color.FromArgb(45, 45, 45), Color.White);
            btnBindLeft.Click += (s, e) => StartBinding(btnBindLeft);

            Label lblRight = new Label { Text = "Right:", Location = new Point(155, 48), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
            btnBindRight = MakeBindButton(keyRight.ToString(), new Point(200, 44), Color.FromArgb(45, 45, 45), Color.White);
            btnBindRight.Click += (s, e) => StartBinding(btnBindRight);

            // Row 2: Left2 / Right2 alt knob keys
            Label lblLeft2 = new Label { Text = "Left2:", Location = new Point(14, 78), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
            btnBindLeft2 = MakeBindButton(keyLeft2.ToString(), new Point(60, 74), Color.FromArgb(45, 45, 45), Color.White);
            btnBindLeft2.Click += (s, e) => StartBinding(btnBindLeft2);

            Label lblRight2 = new Label { Text = "Right2:", Location = new Point(148, 78), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
            btnBindRight2 = MakeBindButton(keyRight2.ToString(), new Point(200, 74), Color.FromArgb(45, 45, 45), Color.White);
            btnBindRight2.Click += (s, e) => StartBinding(btnBindRight2);

            // Row 3: Mouse Click -> Keyboard Key Bindings
            Label lblLClick = new Label { Text = "L-Click ->", Location = new Point(14, 108), AutoSize = true, ForeColor = Color.FromArgb(100, 200, 255) };
            btnBindLClick = MakeBindButton(keyLClick == Keys.None ? "(none)" : keyLClick.ToString(), new Point(80, 104), Color.FromArgb(30, 55, 75), Color.FromArgb(100, 200, 255));
            btnBindLClick.Width = 60;
            btnBindLClick.Click += (s, e) => StartBinding(btnBindLClick);

            Label lblRClick = new Label { Text = "R-Click ->", Location = new Point(148, 108), AutoSize = true, ForeColor = Color.FromArgb(255, 160, 100) };
            btnBindRClick = MakeBindButton(keyRClick == Keys.None ? "(none)" : keyRClick.ToString(), new Point(215, 104), Color.FromArgb(75, 40, 20), Color.FromArgb(255, 160, 100));
            btnBindRClick.Width = 75;
            btnBindRClick.Click += (s, e) => StartBinding(btnBindRClick);

            // Speed Section
            lblSpeed = new Label
            {
                Text = "Knob Speed: " + moveSpeed + " px/tick",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 180, 180),
                Location = new Point(14, 138),
                AutoSize = true
            };

            tbSpeed = new TrackBar
            {
                Minimum = 1,
                Maximum = 60,
                Value = moveSpeed,
                Location = new Point(10, 154),
                Size = new Size(284, 30),
                TickStyle = TickStyle.None
            };
            tbSpeed.ValueChanged += (s, e) =>
            {
                moveSpeed = tbSpeed.Value;
                lblSpeed.Text = "Knob Speed: " + moveSpeed + " px/tick";
                SaveSettings();
            };

            // Log Section
            txtLog = new TextBox
            {
                Location = new Point(14, 192),
                Size = new Size(276, 88),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = Color.FromArgb(160, 160, 160),
                Font = new Font("Consolas", 8F, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle,
                TabStop = false
            };

            // Add Controls
            this.Controls.AddRange(new Control[] {
                lblStatus, btnToggle,
                lblLeft, btnBindLeft, lblRight, btnBindRight,
                lblLeft2, btnBindLeft2, lblRight2, btnBindRight2,
                lblLClick, btnBindLClick, lblRClick, btnBindRClick,
                lblSpeed, tbSpeed, txtLog
            });

            // Tray & App Icon
            Icon appIcon = null;
            if (System.IO.File.Exists("app.ico")) try { appIcon = new Icon("app.ico"); } catch { }
            if (appIcon == null) try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (appIcon != null) this.Icon = appIcon;

            trayIcon = new NotifyIcon
            {
                Icon = (appIcon != null) ? appIcon : SystemIcons.Application,
                Text = "InFalSDVX",
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };

            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;
        }

        private Button MakeBindButton(string text, Point loc, Color back, Color fore)
        {
            var btn = new Button
            {
                Text = text, Location = loc, Size = new Size(75, 24),
                FlatStyle = FlatStyle.Flat, BackColor = back,
                ForeColor = fore, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            return btn;
        }

        private void Log(string msg)
        {
            string line = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), msg);
            if (txtLog.InvokeRequired) { txtLog.Invoke(new Action(() => Log(msg))); return; }
            txtLog.AppendText(line + Environment.NewLine);
        }

        private void StartBinding(Button btn)
        {
            bindingTarget = btn;
            btn.Text = "[Key?]";
            btn.BackColor = Color.FromArgb(70, 70, 70);
            btn.ForeColor = Color.Yellow;
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (bindingTarget != null)
            {
                Keys newKey = e.KeyCode;
                if (newKey == Keys.Escape) newKey = Keys.None;

                if (bindingTarget == btnBindLeft)
                {
                    keyLeft = newKey;
                    btnBindLeft.Text = keyLeft.ToString();
                    Log("Knob Left -> " + keyLeft);
                    btnBindLeft.BackColor = Color.FromArgb(45, 45, 45); btnBindLeft.ForeColor = Color.White;
                }
                else if (bindingTarget == btnBindLeft2)
                {
                    keyLeft2 = newKey;
                    btnBindLeft2.Text = keyLeft2.ToString();
                    Log("Knob Left2 -> " + keyLeft2);
                    btnBindLeft2.BackColor = Color.FromArgb(45, 45, 45); btnBindLeft2.ForeColor = Color.White;
                }
                else if (bindingTarget == btnBindRight)
                {
                    keyRight = newKey;
                    btnBindRight.Text = keyRight.ToString();
                    Log("Knob Right -> " + keyRight);
                    btnBindRight.BackColor = Color.FromArgb(45, 45, 45); btnBindRight.ForeColor = Color.White;
                }
                else if (bindingTarget == btnBindRight2)
                {
                    keyRight2 = newKey;
                    btnBindRight2.Text = keyRight2.ToString();
                    Log("Knob Right2 -> " + keyRight2);
                    btnBindRight2.BackColor = Color.FromArgb(45, 45, 45); btnBindRight2.ForeColor = Color.White;
                }
                else if (bindingTarget == btnBindLClick)
                {
                    keyLClick = newKey;
                    btnBindLClick.Text = keyLClick == Keys.None ? "(none)" : keyLClick.ToString();
                    Log("L-Click -> Key [" + keyLClick + "]");
                    btnBindLClick.BackColor = Color.FromArgb(30, 55, 75); btnBindLClick.ForeColor = Color.FromArgb(100, 200, 255);
                }
                else if (bindingTarget == btnBindRClick)
                {
                    keyRClick = newKey;
                    btnBindRClick.Text = keyRClick == Keys.None ? "(none)" : keyRClick.ToString();
                    Log("R-Click -> Key [" + keyRClick + "]");
                    btnBindRClick.BackColor = Color.FromArgb(75, 40, 20); btnBindRClick.ForeColor = Color.FromArgb(255, 160, 100);
                }

                SaveSettings();
                bindingTarget = null;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (bindingTarget != null) return base.ProcessCmdKey(ref msg, keyData);
            return false;
        }

        private void ToggleRemapper(bool active)
        {
            isEnabled = active;
            if (isEnabled)
            {
                lblStatus.Text = "Status: ACTIVE";
                lblStatus.ForeColor = Color.White;
                Log("Remapper ACTIVE (F8)");
            }
            else
            {
                lblStatus.Text = "Status: DISABLED";
                lblStatus.ForeColor = Color.FromArgb(140, 140, 140);
                isLeftPressed   = false;
                isLeft2Pressed  = false;
                isRightPressed  = false;
                isRight2Pressed = false;
                isRealLMBDown   = false;
                isRealRMBDown   = false;
                Log("Remapper DISABLED (F8)");
            }
        }

        private void MoveLoop()
        {
            while (isRunning)
            {
                if (isEnabled)
                {
                    int deltaX = 0;
                    if (isLeftPressed || isLeft2Pressed)   deltaX -= moveSpeed;
                    if (isRightPressed || isRight2Pressed) deltaX += moveSpeed;
                    if (deltaX != 0) InjectMouseMove(deltaX, 0);
                }
                System.Threading.Thread.Sleep(5);
            }
        }

        private IntPtr SetHook(int hookType, LowLevelProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(hookType, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        // ── Keyboard Hook (Handles Knob Keys & F8) ─────────────────────────────
        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Keys key = (Keys)Marshal.ReadInt32(lParam);

                if (key == keyToggle)
                {
                    if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                    {
                        ToggleRemapper(!isEnabled);
                    }
                    return (IntPtr)1;
                }

                if (isEnabled && bindingTarget == null)
                {
                    bool down = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                    bool up   = (wParam == (IntPtr)WM_KEYUP   || wParam == (IntPtr)WM_SYSKEYUP);

                    if (key == keyLeft)   { if (down && !isLeftPressed)   { isLeftPressed   = true; InjectMouseMove(-moveSpeed*2, 0); } if (up) isLeftPressed   = false; return (IntPtr)1; }
                    if (key == keyLeft2)  { if (down && !isLeft2Pressed)  { isLeft2Pressed  = true; InjectMouseMove(-moveSpeed*2, 0); } if (up) isLeft2Pressed  = false; return (IntPtr)1; }
                    if (key == keyRight)  { if (down && !isRightPressed)  { isRightPressed  = true; InjectMouseMove( moveSpeed*2, 0); } if (up) isRightPressed  = false; return (IntPtr)1; }
                    if (key == keyRight2) { if (down && !isRight2Pressed) { isRight2Pressed = true; InjectMouseMove( moveSpeed*2, 0); } if (up) isRight2Pressed = false; return (IntPtr)1; }
                }
            }
            return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        // ── Mouse Hook (Captures Mouse Left / Right Click -> Injects Hardware ScanCode Keyboard Key) ──
        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && isEnabled && bindingTarget == null)
            {
                int msg = wParam.ToInt32();

                if (msg == WM_LBUTTONDOWN)
                {
                    if (keyLClick != Keys.None)
                    {
                        if (!isRealLMBDown)
                        {
                            isRealLMBDown = true;
                            Log("L-Click Down -> Key [" + keyLClick + "]");
                            InjectKey(keyLClick, true);
                        }
                        return (IntPtr)1;
                    }
                }
                else if (msg == WM_LBUTTONUP)
                {
                    if (keyLClick != Keys.None)
                    {
                        isRealLMBDown = false;
                        InjectKey(keyLClick, false);
                        return (IntPtr)1;
                    }
                }
                else if (msg == WM_RBUTTONDOWN)
                {
                    if (keyRClick != Keys.None)
                    {
                        if (!isRealRMBDown)
                        {
                            isRealRMBDown = true;
                            Log("R-Click Down -> Key [" + keyRClick + "]");
                            InjectKey(keyRClick, true);
                        }
                        return (IntPtr)1;
                    }
                }
                else if (msg == WM_RBUTTONUP)
                {
                    if (keyRClick != Keys.None)
                    {
                        isRealRMBDown = false;
                        InjectKey(keyRClick, false);
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
