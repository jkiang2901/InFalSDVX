using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InFalSDVX
{
    public class MainForm : Form
    {
        // Win32 API Imports
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

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

        private const uint INPUT_MOUSE = 0;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        // Settings
        private Keys keyLeft  = Keys.Q;
        private Keys keyLeft2 = Keys.A;
        private Keys keyRight  = Keys.E;
        private Keys keyRight2 = Keys.D;
        private Keys keyToggle = Keys.F8;

        private bool isEnabled = true;
        private bool isLeftPressed  = false;
        private bool isLeft2Pressed  = false;
        private bool isRightPressed = false;
        private bool isRight2Pressed = false;
        private volatile bool isRunning = true;

        private int moveSpeed = 15;
        private System.Threading.Thread moveThread;
        private NotifyIcon trayIcon;

        private static readonly string SettingsPath =
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.ExecutablePath),
                "settings.ini");

        // Minimalist UI Elements
        private Label lblStatus;
        private Button btnToggle;
        private Button btnBindLeft;
        private Button btnBindLeft2;
        private Button btnBindRight;
        private Button btnBindRight2;
        private TrackBar tbSpeed;
        private Label lblSpeed;
        private TextBox txtLog;

        private Button bindingTarget = null;

        public MainForm()
        {
            LoadSettings();
            InitializeComponent();
            _proc = HookCallback;
            _hookID = SetHook(_proc);

            moveThread = new System.Threading.Thread(MoveLoop);
            moveThread.IsBackground = true;
            moveThread.Priority = System.Threading.ThreadPriority.Highest;
            moveThread.Start();

            Log("Ready. Remapper: ACTIVE");
        }

        private void SaveSettings()
        {
            try
            {
                var lines = new System.Text.StringBuilder();
                lines.AppendLine("[Keys]");
                lines.AppendLine("Left="  + (int)keyLeft);
                lines.AppendLine("Left2=" + (int)keyLeft2);
                lines.AppendLine("Right="  + (int)keyRight);
                lines.AppendLine("Right2=" + (int)keyRight2);
                lines.AppendLine("[Speed]");
                lines.AppendLine("Value=" + moveSpeed);
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
                        case "Value":  moveSpeed = Math.Max(1, Math.Min(60, num)); break;
                    }
                }
            }
            catch { }
        }

        private static void InjectMouseMove(int dx, int dy)
        {
            // Modern SendInput API for Raw Input / DirectInput game engine compatibility
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dx = dx;
            inputs[0].mi.dy = dy;
            inputs[0].mi.mouseData = 0;
            inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE;
            inputs[0].mi.time = 0;
            inputs[0].mi.dwExtraInfo = IntPtr.Zero;

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));

            // Legacy mouse_event fallback
            mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, UIntPtr.Zero);
        }

        private void InitializeComponent()
        {
            this.Text = "InFalSDVX";
            this.Size = new Size(320, 295);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(28, 28, 28);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // Status & Toggle
            lblStatus = new Label
            {
                Text = "Status: ACTIVE",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(14, 14),
                AutoSize = true
            };

            btnToggle = new Button
            {
                Text = "Toggle (F8)",
                Location = new Point(200, 10),
                Size = new Size(90, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            btnToggle.FlatAppearance.BorderSize = 1;
            btnToggle.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            btnToggle.Click += (s, e) => ToggleRemapper(!isEnabled);

            // Keybindings Section — Row 1
            Label lblLeft = new Label { Text = "Left:", Location = new Point(14, 50), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
            btnBindLeft = new Button
            {
                Text = keyLeft.ToString(),
                Location = new Point(54, 46),
                Size = new Size(70, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnBindLeft.FlatAppearance.BorderSize = 1;
            btnBindLeft.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            btnBindLeft.Click += (s, e) => StartBinding(btnBindLeft);

            Label lblRight = new Label { Text = "Right:", Location = new Point(155, 50), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
            btnBindRight = new Button
            {
                Text = keyRight.ToString(),
                Location = new Point(200, 46),
                Size = new Size(90, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnBindRight.FlatAppearance.BorderSize = 1;
            btnBindRight.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            btnBindRight.Click += (s, e) => StartBinding(btnBindRight);

            // Keybindings Section — Row 2 (alternate keys)
            Label lblLeft2 = new Label { Text = "Left2:", Location = new Point(14, 82), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
            btnBindLeft2 = new Button
            {
                Text = keyLeft2.ToString(),
                Location = new Point(60, 78),
                Size = new Size(64, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnBindLeft2.FlatAppearance.BorderSize = 1;
            btnBindLeft2.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            btnBindLeft2.Click += (s, e) => StartBinding(btnBindLeft2);

            Label lblRight2 = new Label { Text = "Right2:", Location = new Point(148, 82), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
            btnBindRight2 = new Button
            {
                Text = keyRight2.ToString(),
                Location = new Point(200, 78),
                Size = new Size(90, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnBindRight2.FlatAppearance.BorderSize = 1;
            btnBindRight2.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            btnBindRight2.Click += (s, e) => StartBinding(btnBindRight2);

            // Speed Section
            lblSpeed = new Label
            {
                Text = "Speed: 15 px/tick",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 180, 180),
                Location = new Point(14, 114),
                AutoSize = true
            };

            tbSpeed = new TrackBar
            {
                Minimum = 1,
                Maximum = 60,
                Value = moveSpeed,
                Location = new Point(10, 132),
                Size = new Size(284, 30),
                TickStyle = TickStyle.None
            };
            tbSpeed.ValueChanged += (s, e) =>
            {
                moveSpeed = tbSpeed.Value;
                lblSpeed.Text = "Speed: " + moveSpeed + " px/tick";
            };

            // Log Section
            txtLog = new TextBox
            {
                Location = new Point(14, 168),
                Size = new Size(276, 70),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = Color.FromArgb(160, 160, 160),
                Font = new Font("Consolas", 8F, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle,
                TabStop = false
            };

            // Add controls
            this.Controls.Add(lblStatus);
            this.Controls.Add(btnToggle);
            this.Controls.Add(lblLeft);
            this.Controls.Add(btnBindLeft);
            this.Controls.Add(lblRight);
            this.Controls.Add(btnBindRight);
            this.Controls.Add(lblLeft2);
            this.Controls.Add(btnBindLeft2);
            this.Controls.Add(lblRight2);
            this.Controls.Add(btnBindRight2);
            this.Controls.Add(lblSpeed);
            this.Controls.Add(tbSpeed);
            this.Controls.Add(txtLog);

            // Tray icon & Form icon
            Icon appIcon = null;
            if (System.IO.File.Exists("app.ico"))
            {
                try { appIcon = new Icon("app.ico"); } catch {}
            }
            if (appIcon == null)
            {
                try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch {}
            }
            if (appIcon != null)
            {
                this.Icon = appIcon;
            }

            trayIcon = new NotifyIcon
            {
                Icon = (appIcon != null) ? appIcon : SystemIcons.Application,
                Text = "InFalSDVX",
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };

            this.FormClosing += (s, e) => { SaveSettings(); isRunning = false; UnhookWindowsHookEx(_hookID); trayIcon.Dispose(); };
            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;
        }

        private void Log(string msg)
        {
            string line = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), msg);
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => Log(msg)));
                return;
            }
            txtLog.AppendText(line + Environment.NewLine);
        }

        private void StartBinding(Button btn)
        {
            bindingTarget = btn;
            btn.Text = "[Press]";
            btn.BackColor = Color.FromArgb(70, 70, 70);
            btn.ForeColor = Color.White;
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (bindingTarget != null)
            {
                Keys newKey = e.KeyCode;
                if (bindingTarget == btnBindLeft)
                {
                    keyLeft = newKey;
                    btnBindLeft.Text = keyLeft.ToString();
                    Log("Left bound: " + keyLeft);
                }
                else if (bindingTarget == btnBindLeft2)
                {
                    keyLeft2 = newKey;
                    btnBindLeft2.Text = keyLeft2.ToString();
                    Log("Left2 bound: " + keyLeft2);
                }
                else if (bindingTarget == btnBindRight)
                {
                    keyRight = newKey;
                    btnBindRight.Text = keyRight.ToString();
                    Log("Right bound: " + keyRight);
                }
                else if (bindingTarget == btnBindRight2)
                {
                    keyRight2 = newKey;
                    btnBindRight2.Text = keyRight2.ToString();
                    Log("Right2 bound: " + keyRight2);
                }

                bindingTarget.BackColor = Color.FromArgb(45, 45, 45);
                bindingTarget.ForeColor = Color.White;
                bindingTarget = null;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else
            {
                e.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (bindingTarget != null)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }
            return false;
        }

        private void ToggleRemapper(bool active)
        {
            isEnabled = active;
            if (isEnabled)
            {
                lblStatus.Text = "Status: ACTIVE";
                lblStatus.ForeColor = Color.White;
                Log("Remapper ACTIVE");
            }
            else
            {
                lblStatus.Text = "Status: DISABLED";
                lblStatus.ForeColor = Color.FromArgb(140, 140, 140);
                isLeftPressed  = false;
                isLeft2Pressed  = false;
                isRightPressed = false;
                isRight2Pressed = false;
                Log("Remapper DISABLED");
            }
        }

        private void MoveLoop()
        {
            while (isRunning)
            {
                if (isEnabled)
                {
                    int deltaX = 0;
                    if (isLeftPressed || isLeft2Pressed) deltaX -= moveSpeed;
                    if (isRightPressed || isRight2Pressed) deltaX += moveSpeed;

                    if (deltaX != 0)
                    {
                        InjectMouseMove(deltaX, 0);
                    }
                }
                System.Threading.Thread.Sleep(5); // ~200Hz high precision background loop
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                if (key == keyToggle)
                {
                    if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                    {
                        ToggleRemapper(!isEnabled);
                    }
                    return (IntPtr)1;
                }

                if (isEnabled)
                {
                    if (key == keyLeft)
                    {
                        if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                        {
                            if (!isLeftPressed)
                            {
                                isLeftPressed = true;
                                InjectMouseMove(-moveSpeed * 2, 0);
                            }
                        }
                        else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                        {
                            isLeftPressed = false;
                        }
                        return (IntPtr)1;
                    }
                    else if (key == keyLeft2)
                    {
                        if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                        {
                            if (!isLeft2Pressed)
                            {
                                isLeft2Pressed = true;
                                InjectMouseMove(-moveSpeed * 2, 0);
                            }
                        }
                        else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                        {
                            isLeft2Pressed = false;
                        }
                        return (IntPtr)1;
                    }
                    else if (key == keyRight)
                    {
                        if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                        {
                            if (!isRightPressed)
                            {
                                isRightPressed = true;
                                InjectMouseMove(moveSpeed * 2, 0);
                            }
                        }
                        else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                        {
                            isRightPressed = false;
                        }
                        return (IntPtr)1;
                    }
                    else if (key == keyRight2)
                    {
                        if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                        {
                            if (!isRight2Pressed)
                            {
                                isRight2Pressed = true;
                                InjectMouseMove(moveSpeed * 2, 0);
                            }
                        }
                        else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                        {
                            isRight2Pressed = false;
                        }
                        return (IntPtr)1;
                    }
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
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
