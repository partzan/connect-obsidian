// Connect Obsidian is released under the MIT License. See the LICENSE file.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ObsidianChatGpt
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (HasArg(args, "--selftest"))
            {
                return SelfTest();
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\ObsidianChatGPTConnect", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Connect Obsidian is already open. Look for its icon near the clock.",
                        "Obsidian",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return 0;
                }

                Application.EnableVisualStyles();
                try
                {
                    Application.SetCompatibleTextRenderingDefault(false);
                }
                catch (InvalidOperationException)
                {
                }

                Application.Run(new MainForm(HasArg(args, "--connect")));
                GC.KeepAlive(mutex);
            }

            return 0;
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int SelfTest()
        {
            string secret = "sample-secret-value";
            byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);
            string roundTrip = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
            if (roundTrip != secret)
            {
                return 1;
            }

            string yaml = "control_plane:\r\n  base_url: \"https://api.openai.com\"\r\n  tunnel_id: \"tunnel_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"\r\n  api_key: \"env:OLD\"\r\nmcp:\r\n  server_urls:\r\n    - channel: main\r\n      url: \"http://127.0.0.1:27123/mcp/\"\r\n";
            string updated = ProfileText.Update(yaml, "tunnel_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "http://127.0.0.1:27124/mcp/");
            if (updated.IndexOf("tunnel_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", StringComparison.Ordinal) < 0)
            {
                return 2;
            }

            if (updated.IndexOf("env:CONTROL_PLANE_API_KEY", StringComparison.Ordinal) < 0)
            {
                return 3;
            }

            if (updated.IndexOf("base_url: \"https://api.openai.com\"", StringComparison.Ordinal) < 0)
            {
                return 4;
            }

            if (updated.IndexOf("http://127.0.0.1:27124/mcp/", StringComparison.Ordinal) < 0)
            {
                return 12;
            }

            string redacted = LogText.Redact("Authorization: Bearer abcdef123456 and sk-proj-secretvalue");
            if (redacted.IndexOf("abcdef123456", StringComparison.Ordinal) >= 0 || redacted.IndexOf("sk-proj-secretvalue", StringComparison.Ordinal) >= 0)
            {
                return 5;
            }

            if (!SettingsStore.IsValidTunnelId("tunnel_0123456789abcdef0123456789abcdef"))
            {
                return 6;
            }

            if (SettingsStore.IsValidTunnelId("tunnel_your_id_here"))
            {
                return 7;
            }

            if (!SettingsStore.IsValidMcpUrl("http://127.0.0.1:27123/mcp/"))
            {
                return 8;
            }

            if (SettingsStore.IsValidMcpUrl("https://example.com/mcp/"))
            {
                return 9;
            }

            string auth = SettingsStore.FormatBearer("abc");
            if (auth != "Bearer abc")
            {
                return 10;
            }

            string arguments = TunnelRunner.RunArguments();
            if (arguments.IndexOf("abc", StringComparison.Ordinal) >= 0 || arguments.IndexOf("Bearer", StringComparison.Ordinal) >= 0)
            {
                return 11;
            }

            using (Stream packed = Assembly.GetExecutingAssembly().GetManifestResourceStream(RuntimePack.TunnelResource))
            {
                if (packed == null || packed.Length < 1000000)
                {
                    return 13;
                }
            }

            return 0;
        }
    }

    internal static class RuntimePack
    {
        public const string TunnelResource = "ObsidianChatGpt.tunnel-client.exe";

        public static string ClientPath()
        {
            return Path.Combine(RuntimeDirectory(), "tunnel-client.exe");
        }

        public static string Ensure()
        {
            try
            {
                Directory.CreateDirectory(RuntimeDirectory());
                Extract(TunnelResource, ClientPath());
                Extract("ObsidianChatGpt.cloudflared.exe", Path.Combine(RuntimeDirectory(), "cloudflared.exe"));
                Extract("ObsidianChatGpt.cloudflared-manifest.json", Path.Combine(RuntimeDirectory(), "cloudflared-manifest.json"));
                return null;
            }
            catch (Exception ex)
            {
                return "Could not prepare the built-in tunnel program. " + ex.Message;
            }
        }

        private static string RuntimeDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObsidianChatGPT", "runtime");
        }

        private static void Extract(string resourceName, string destination)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream source = assembly.GetManifestResourceStream(resourceName))
            {
                if (source == null)
                {
                    throw new InvalidOperationException("Missing built-in file " + resourceName);
                }

                if (File.Exists(destination) && new FileInfo(destination).Length == source.Length)
                {
                    return;
                }

                string temporary = destination + ".new";
                using (FileStream target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[262144];
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        target.Write(buffer, 0, read);
                    }
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(temporary, destination);
            }
        }
    }

    internal static class SettingsStore
    {
        public const string DefaultMcpUrl = "http://127.0.0.1:27123/mcp/";

        public static string Folder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ObsidianChatGPT");
        }

        public static string ProfilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tunnel-client", "obsidian.yaml");
        }

        public static string BundledClientPath()
        {
            return RuntimePack.ClientPath();
        }

        public static bool IsValidTunnelId(string value)
        {
            return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, "^tunnel_[0-9a-fA-F]{8,64}$");
        }

        public static bool IsValidMcpUrl(string value)
        {
            return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, "^https?://127\\.0\\.0\\.1:[0-9]{2,5}/mcp/?$");
        }

        public static string FormatBearer(string key)
        {
            string trimmed = (key ?? string.Empty).Trim();
            if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return "Bearer " + trimmed.Substring(7).Trim();
            }

            return "Bearer " + trimmed;
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            settings.McpUrl = DefaultMcpUrl;
            string path = Path.Combine(Folder(), "settings.txt");
            if (File.Exists(path))
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    int eq = lines[i].IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    string key = lines[i].Substring(0, eq);
                    string value = lines[i].Substring(eq + 1);
                    if (key == "tunnelId")
                    {
                        settings.TunnelId = value.Trim();
                    }
                    else if (key == "mcpUrl")
                    {
                        settings.McpUrl = value.Trim();
                    }
                    else if (key == "startWithWindows")
                    {
                        settings.StartWithWindows = value.Trim() == "true";
                    }
                }
            }

            settings.OpenAiKey = ReadSecret("openai.key");
            settings.ObsidianKey = ReadSecret("obsidian.key");
            if (string.IsNullOrEmpty(settings.TunnelId))
            {
                settings.TunnelId = ReadYamlValue("tunnel_id");
            }

            if (string.IsNullOrEmpty(settings.McpUrl))
            {
                settings.McpUrl = DefaultMcpUrl;
            }

            return settings;
        }

        public static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(Folder());
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("tunnelId=" + (settings.TunnelId ?? string.Empty).Trim());
            builder.AppendLine("mcpUrl=" + (settings.McpUrl ?? string.Empty).Trim());
            builder.AppendLine("startWithWindows=" + (settings.StartWithWindows ? "true" : "false"));
            File.WriteAllText(Path.Combine(Folder(), "settings.txt"), builder.ToString(), new UTF8Encoding(false));
            if (!string.IsNullOrEmpty(settings.OpenAiKey))
            {
                WriteSecret("openai.key", settings.OpenAiKey.Trim());
            }

            if (!string.IsNullOrEmpty(settings.ObsidianKey))
            {
                WriteSecret("obsidian.key", settings.ObsidianKey.Trim());
            }
        }

        public static void ForgetKeys()
        {
            DeleteFile(Path.Combine(Folder(), "openai.key"));
            DeleteFile(Path.Combine(Folder(), "obsidian.key"));
        }

        public static bool Complete(AppSettings settings)
        {
            return settings != null
                && IsValidTunnelId(settings.TunnelId)
                && IsValidMcpUrl(settings.McpUrl)
                && !string.IsNullOrEmpty(settings.OpenAiKey)
                && !string.IsNullOrEmpty(settings.ObsidianKey);
        }

        public static string StartupShortcutPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Connect Obsidian.lnk");
        }

        public static void ApplyStartup(bool enabled)
        {
            string path = StartupShortcutPath();
            if (!enabled)
            {
                DeleteFile(path);
                return;
            }

            CreateShortcut(path, "--connect");
        }

        public static void CreateShortcut(string shortcutPath, string arguments)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Application.ExecutablePath });
            shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments ?? string.Empty });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { AppDomain.CurrentDomain.BaseDirectory });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Connect Obsidian to ChatGPT" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        private static string ReadYamlValue(string key)
        {
            string path = ProfilePath();
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            string text = File.ReadAllText(path);
            Match match = Regex.Match(text, "(?m)^\\s*" + Regex.Escape(key) + ":\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ReadSecret(string fileName)
        {
            string path = Path.Combine(Folder(), fileName);
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                byte[] protectedBytes = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain).Trim();
            }
            catch (CryptographicException)
            {
                return string.Empty;
            }
        }

        private static void WriteSecret(string fileName, string value)
        {
            Directory.CreateDirectory(Folder());
            byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(Folder(), fileName), protectedBytes);
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    internal sealed class AppSettings
    {
        public string TunnelId;
        public string McpUrl;
        public string OpenAiKey;
        public string ObsidianKey;
        public bool StartWithWindows;
    }

    internal static class ProfileText
    {
        public static string Update(string text, string tunnelId, string mcpUrl)
        {
            text = Regex.Replace(text, "(?m)^(\\s*tunnel_id:\\s*\")[^\"]*(\".*)$", "${1}" + tunnelId + "${2}");
            text = Regex.Replace(text, "(?m)^(\\s*api_key:\\s*\")[^\"]*(\".*)$", "${1}env:CONTROL_PLANE_API_KEY${2}");
            text = Regex.Replace(text, "(?m)^(\\s*url:\\s*\")[^\"]*(\".*)$", "${1}" + mcpUrl + "${2}");
            return text;
        }

        public static string ListenAddress(string text)
        {
            Match match = Regex.Match(text ?? string.Empty, "listen_addr:\\s*\"([^\"]+)\"");
            if (match.Success && match.Groups[1].Value.IndexOf("0.0.0.0", StringComparison.Ordinal) < 0)
            {
                return match.Groups[1].Value;
            }

            return "127.0.0.1:8080";
        }
    }

    internal static class LogText
    {
        public static string Redact(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }

            line = Regex.Replace(line, "sk-[A-Za-z0-9_\\-]+", "sk-***");
            line = Regex.Replace(line, "(?i)bearer\\s+[A-Za-z0-9_\\-\\.=]+", "Bearer ***");
            return line;
        }

        public static string Format(string raw)
        {
            raw = Redact(raw);
            string level = Group(raw, "\"level\"\\s*:\\s*\"([^\"]+)\"");
            if (level == "debug" || level == "trace")
            {
                return null;
            }

            string message = Group(raw, "\"msg\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (message == null)
            {
                message = Group(raw, "\"message\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            }

            if (message != null)
            {
                message = message.Replace("\\\"", "\"").Replace("\\n", " ");
                return DateTime.Now.ToString("HH:mm:ss") + "  " + (level ?? "info") + "  " + message;
            }

            return DateTime.Now.ToString("HH:mm:ss") + "  " + raw;
        }

        private static string Group(string text, string pattern)
        {
            Match match = Regex.Match(text, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    internal static class Probe
    {
        public static bool Ok(string url)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 1200;
                request.AllowAutoRedirect = false;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    int code = (int)response.StatusCode;
                    return code >= 200 && code < 300;
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    int code = (int)response.StatusCode;
                    return code >= 200 && code < 300;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static string ObsidianRoot(string mcpUrl)
        {
            string root = (mcpUrl ?? string.Empty).TrimEnd('/');
            if (root.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                root = root.Substring(0, root.Length - 4);
            }

            return root + "/";
        }
    }

    internal static class TunnelRunner
    {
        public static string RunArguments()
        {
            return "run --profile obsidian --mcp.extra-headers \"Authorization: env:OBSIDIAN_AUTH\"";
        }

        public static string InitArguments(string tunnelId, string mcpUrl)
        {
            return "init --profile obsidian --tunnel-id " + tunnelId + " --mcp-server-url \"" + mcpUrl + "\"";
        }
    }

    internal sealed class StatusLight : Control
    {
        private Color lamp = Color.FromArgb(156, 163, 175);
        private bool glowing;

        public StatusLight()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.White;
            Size = new Size(28, 28);
            AccessibleName = "Connection light";
        }

        public void SetLamp(Color color, bool glow)
        {
            lamp = color;
            glowing = glow;
            AccessibleDescription = glow ? "Green, connected" : "Not green";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (glowing)
            {
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(70, lamp)))
                {
                    e.Graphics.FillEllipse(glow, 0, 0, Width - 1, Height - 1);
                }
            }

            Rectangle core = new Rectangle(6, 6, Width - 13, Height - 13);
            using (SolidBrush brush = new SolidBrush(lamp))
            {
                e.Graphics.FillEllipse(brush, core);
            }

            using (SolidBrush highlight = new SolidBrush(Color.FromArgb(glowing ? 180 : 70, Color.White)))
            {
                e.Graphics.FillEllipse(highlight, core.X + 3, core.Y + 3, 6, 5);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly bool autoConnect;
        private readonly StatusLight statusLight;
        private readonly Label statusLabel;
        private readonly Label detailLabel;
        private readonly Button connectButton;
        private readonly Button disconnectButton;
        private readonly TextBox logBox;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer;
        private Process process;
        private bool allowExit;
        private bool checking;
        private bool balloonShown;
        private int startTicket;
        private Icon appIcon;
        private AppSettings settings;

        public MainForm(bool autoConnect)
        {
            this.autoConnect = autoConnect;
            this.settings = SettingsStore.Load();
            Text = "Connect Obsidian to ChatGPT";
            Font = new Font("Segoe UI", 10F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(520, 640);
            ClientSize = new Size(560, 680);
            BackColor = Color.White;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 92;
            header.BackColor = Color.FromArgb(20, 61, 44);
            header.Padding = new Padding(20, 16, 20, 12);

            Label title = new Label();
            title.Text = "Obsidian  to  ChatGPT";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            title.Dock = DockStyle.Top;
            title.Height = 34;
            title.AutoSize = false;

            Label subtitle = new Label();
            subtitle.Text = "Press Connect once. Leave this running while ChatGPT uses the vault.";
            subtitle.ForeColor = Color.FromArgb(216, 243, 220);
            subtitle.Dock = DockStyle.Fill;
            subtitle.AutoSize = false;

            header.Controls.Add(subtitle);
            header.Controls.Add(title);

            Panel statusPanel = new Panel();
            statusPanel.Dock = DockStyle.Top;
            statusPanel.Height = 96;
            statusPanel.Padding = new Padding(20, 14, 20, 4);

            Panel statusRow = new Panel();
            statusRow.Dock = DockStyle.Top;
            statusRow.Height = 34;

            statusLight = new StatusLight();
            statusLight.Location = new Point(0, 2);

            statusLabel = new Label();
            statusLabel.Text = "Not connected";
            statusLabel.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            statusLabel.ForeColor = Color.FromArgb(75, 85, 99);
            statusLabel.Location = new Point(36, 4);
            statusLabel.Size = new Size(460, 28);
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            statusRow.Controls.Add(statusLight);
            statusRow.Controls.Add(statusLabel);

            detailLabel = new Label();
            detailLabel.Text = "Open Obsidian first. Then press Connect. In ChatGPT, type @ and choose Obsidian.";
            detailLabel.ForeColor = Color.FromArgb(55, 65, 81);
            detailLabel.Dock = DockStyle.Fill;
            detailLabel.AutoSize = false;

            statusPanel.Controls.Add(detailLabel);
            statusPanel.Controls.Add(statusRow);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Top;
            buttons.Height = 58;
            buttons.Padding = new Padding(18, 8, 12, 8);
            buttons.WrapContents = false;

            connectButton = MakeButton("Connect", Color.FromArgb(27, 67, 50), Color.White);
            connectButton.Click += delegate { BeginConnect(true); };
            disconnectButton = MakeButton("Disconnect", Color.FromArgb(243, 244, 246), Color.FromArgb(55, 65, 81));
            disconnectButton.Click += delegate { Disconnect(true); };
            Button settingsButton = MakeButton("Settings", Color.FromArgb(243, 244, 246), Color.FromArgb(55, 65, 81));
            settingsButton.Click += delegate { ShowSettings(); };
            buttons.Controls.Add(connectButton);
            buttons.Controls.Add(disconnectButton);
            buttons.Controls.Add(settingsButton);

            logBox = new TextBox();
            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.None;
            logBox.BackColor = Color.FromArgb(247, 247, 245);
            logBox.Font = new Font("Segoe UI", 9F);
            logBox.HideSelection = true;

            Panel logHost = new Panel();
            logHost.Dock = DockStyle.Fill;
            logHost.Padding = new Padding(20, 8, 20, 16);
            logHost.Controls.Add(logBox);

            Controls.Add(logHost);
            Controls.Add(buttons);
            Controls.Add(statusPanel);
            Controls.Add(header);

            tray = new NotifyIcon();
            tray.Text = "Obsidian to ChatGPT";
            tray.Visible = true;
            LoadAppIcon();
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, delegate { ShowFromTray(); });
            menu.Items.Add("Disconnect", null, delegate { Disconnect(true); });
            menu.Items.Add("Exit", null, delegate { ExitApp(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowFromTray(); };

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 2000;
            timer.Tick += delegate { Poll(); };
            timer.Start();

            Shown += delegate { OnFirstShown(); };
            FormClosing += OnFormClosing;
            Microsoft.Win32.SystemEvents.SessionEnding += delegate { StopProcesses(); };
        }

        private static Button MakeButton(string text, Color back, Color fore)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = false;
            button.Width = 130;
            button.Height = 38;
            button.Margin = new Padding(4, 0, 8, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            button.BackColor = back;
            button.ForeColor = fore;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void OnFirstShown()
        {
            if (!SettingsStore.Complete(settings))
            {
                if (ShowSettings() && autoConnect)
                {
                    BeginConnect(false);
                }
            }
            else if (autoConnect)
            {
                BeginConnect(false);
            }

            Poll();
        }

        private bool ShowSettings()
        {
            using (SettingsForm dialog = new SettingsForm(SettingsStore.Load()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                settings = SettingsStore.Load();
                return SettingsStore.Complete(settings);
            }
        }

        private void BeginConnect(bool askWhenObsidianIsClosed)
        {
            settings = SettingsStore.Load();
            if (!SettingsStore.Complete(settings))
            {
                if (!ShowSettings())
                {
                    return;
                }

                settings = SettingsStore.Load();
            }

            if (askWhenObsidianIsClosed && !Probe.Ok(Probe.ObsidianRoot(settings.McpUrl)))
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "Obsidian is not answering yet. Open Obsidian, turn on Local REST API, and enable the insecure HTTP server.\r\n\r\nConnect anyway?",
                    "Obsidian",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    SetState("Waiting for Obsidian", "Open Obsidian, then press Connect again.", Color.FromArgb(154, 107, 18));
                    return;
                }
            }

            connectButton.Enabled = false;
            SetState("Starting", "Starting the tunnel. Leave this window running.", Color.FromArgb(154, 107, 18));
            AppSettings snapshot = settings;
            int ticket = Interlocked.Increment(ref startTicket);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = StartWorker(snapshot, ticket);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        connectButton.Enabled = true;
                        if (!string.IsNullOrEmpty(error) && error != "already" && error != "cancelled")
                        {
                            AppendLog(error);
                            SetState("Could not start", error, Color.FromArgb(155, 58, 47));
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private string StartWorker(AppSettings snapshot, int ticket)
        {
            try
            {
                if (ticket != Thread.VolatileRead(ref startTicket))
                {
                    return "cancelled";
                }

                string prepareError = RuntimePack.Ensure();
                if (prepareError != null)
                {
                    return prepareError;
                }

                string yamlPath = SettingsStore.ProfilePath();
                string currentId = string.Empty;
                if (File.Exists(yamlPath))
                {
                    currentId = Regex.Match(File.ReadAllText(yamlPath), "tunnel_id:\\s*\"([^\"]+)\"").Groups[1].Value;
                }

                bool sameTunnel = currentId == snapshot.TunnelId;
                if (sameTunnel && Probe.Ok(ReadyUrl()))
                {
                    BeginInvoke(new Action(delegate
                    {
                        SetState("Connected", "Already connected. In ChatGPT, type @ and choose Obsidian.", Color.FromArgb(27, 127, 78));
                    }));
                    return "already";
                }

                if (Probe.Ok(ReadyUrl()))
                {
                    StopProcesses();
                }

                if (!File.Exists(yamlPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(yamlPath));
                    string initError = RunInit(snapshot);
                    if (initError != null)
                    {
                        return initError;
                    }
                }

                if (ticket != Thread.VolatileRead(ref startTicket))
                {
                    return "cancelled";
                }

                if (!File.Exists(yamlPath))
                {
                    return "The tunnel profile was not created.";
                }

                string text = File.ReadAllText(yamlPath);
                text = ProfileText.Update(text, snapshot.TunnelId, snapshot.McpUrl);
                if (!string.IsNullOrEmpty(snapshot.OpenAiKey))
                {
                    text = text.Replace(snapshot.OpenAiKey, "env:CONTROL_PLANE_API_KEY");
                }

                if (!string.IsNullOrEmpty(snapshot.ObsidianKey))
                {
                    text = text.Replace(snapshot.ObsidianKey, string.Empty);
                }

                File.WriteAllText(yamlPath, text, new UTF8Encoding(false));
                Process started = StartRun(snapshot);
                process = started;
                BeginInvoke(new Action(delegate
                {
                    AppendLog("Tunnel process started.");
                }));
                return null;
            }
            catch (Exception ex)
            {
                return LogText.Redact(ex.Message);
            }
        }

        private string RunInit(AppSettings snapshot)
        {
            Process init = CreateProcess(TunnelRunner.InitArguments(snapshot.TunnelId, snapshot.McpUrl), snapshot);
            init.Start();
            string stderr = string.Empty;
            Thread errorThread = new Thread(delegate()
            {
                stderr = init.StandardError.ReadToEnd();
            });
            errorThread.IsBackground = true;
            errorThread.Start();
            string stdout = init.StandardOutput.ReadToEnd();
            if (!init.WaitForExit(30000))
            {
                try
                {
                    init.Kill();
                }
                catch
                {
                }

                return "Creating the profile took too long.";
            }

            errorThread.Join(2000);
            string output = stdout + "\n" + stderr;
            if (init.ExitCode != 0 && output.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return "Could not create the profile. " + LogText.Redact(output).Trim();
            }

            return null;
        }

        private Process StartRun(AppSettings snapshot)
        {
            Process run = CreateProcess(TunnelRunner.RunArguments(), snapshot);
            run.EnableRaisingEvents = true;
            run.OutputDataReceived += OnProcessLine;
            run.ErrorDataReceived += OnProcessLine;
            run.Exited += delegate
            {
                try
                {
                    BeginInvoke(new Action(OnProcessExited));
                }
                catch (InvalidOperationException)
                {
                }
            };
            run.Start();
            run.BeginOutputReadLine();
            run.BeginErrorReadLine();
            return run;
        }

        private Process CreateProcess(string arguments, AppSettings snapshot)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            string clientPath = SettingsStore.BundledClientPath();
            info.FileName = clientPath;
            info.Arguments = arguments;
            info.WorkingDirectory = Path.GetDirectoryName(clientPath);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            info.EnvironmentVariables["CONTROL_PLANE_API_KEY"] = snapshot.OpenAiKey.Trim();
            info.EnvironmentVariables["OBSIDIAN_AUTH"] = SettingsStore.FormatBearer(snapshot.ObsidianKey);
            info.EnvironmentVariables["HARPOON_ALLOW_PLAINTEXT_HTTP"] = "true";
            Process created = new Process();
            created.StartInfo = info;
            return created;
        }

        private void OnProcessLine(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendLog(e.Data);
            }
        }

        private void OnProcessExited()
        {
            connectButton.Enabled = true;
            if (Probe.Ok(ReadyUrl()))
            {
                SetState("Connected", "In ChatGPT, type @ and choose Obsidian. You can close this window; it stays by the clock.", Color.FromArgb(27, 127, 78));
                return;
            }

            SetState("Disconnected", "The tunnel stopped. Press Connect to start it again.", Color.FromArgb(155, 58, 47));
        }

        private void Poll()
        {
            if (checking)
            {
                return;
            }

            checking = true;
            string readyUrl = ReadyUrl();
            string obsidianUrl = Probe.ObsidianRoot(settings == null ? SettingsStore.DefaultMcpUrl : settings.McpUrl);
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ready = Probe.Ok(readyUrl);
                bool obsidian = Probe.Ok(obsidianUrl);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        checking = false;
                        ApplyPoll(ready, obsidian);
                    }));
                }
                catch (InvalidOperationException)
                {
                    checking = false;
                }
            });
        }

        private void ApplyPoll(bool ready, bool obsidian)
        {
            if (!ready)
            {
                return;
            }

            connectButton.Enabled = true;
            if (!obsidian)
            {
                SetState("Tunnel is up", "Open Obsidian and turn on Local REST API, including the insecure HTTP server.", Color.FromArgb(154, 107, 18));
                return;
            }

            SetState("Connected", "In ChatGPT, type @ and choose Obsidian. You can close this window; it stays by the clock.", Color.FromArgb(27, 127, 78));
        }

        private string ReadyUrl()
        {
            string listen = "127.0.0.1:8080";
            string path = SettingsStore.ProfilePath();
            if (File.Exists(path))
            {
                listen = ProfileText.ListenAddress(File.ReadAllText(path));
            }

            return "http://" + listen + "/readyz";
        }

        private void Disconnect(bool confirm)
        {
            if (confirm)
            {
                DialogResult answer = MessageBox.Show(this, "Disconnect Obsidian from ChatGPT?", "Obsidian", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            Interlocked.Increment(ref startTicket);
            StopProcesses();
            connectButton.Enabled = true;
            SetState("Disconnected", "Press Connect when you want ChatGPT to use the vault again.", Color.FromArgb(75, 85, 99));
            AppendLog("Disconnected.");
        }

        private void StopProcesses()
        {
            Process[] running = Process.GetProcessesByName("tunnel-client");
            for (int i = 0; i < running.Length; i++)
            {
                try
                {
                    Process killer = new Process();
                    killer.StartInfo.FileName = "taskkill.exe";
                    killer.StartInfo.Arguments = "/PID " + running[i].Id + " /T /F";
                    killer.StartInfo.CreateNoWindow = true;
                    killer.StartInfo.UseShellExecute = false;
                    killer.Start();
                    killer.WaitForExit(5000);
                }
                catch
                {
                }
            }

            process = null;
        }

        private void SetState(string status, string detail, Color color)
        {
            bool connected = status == "Connected";
            Color lamp = connected ? Color.FromArgb(34, 197, 94) : color;
            statusLabel.Text = status;
            statusLabel.ForeColor = color;
            detailLabel.Text = detail;
            statusLight.SetLamp(lamp, connected);
            tray.Text = TrimTray("Obsidian: " + status);
        }

        private void LoadAppIcon()
        {
            try
            {
                appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                appIcon = null;
            }

            if (appIcon == null)
            {
                return;
            }

            Icon = (Icon)appIcon.Clone();
            tray.Icon = (Icon)appIcon.Clone();
        }

        private static string TrimTray(string value)
        {
            if (value.Length <= 63)
            {
                return value;
            }

            return value.Substring(0, 63);
        }

        private void AppendLog(string raw)
        {
            if (string.IsNullOrEmpty(raw) || logBox.IsDisposed)
            {
                return;
            }

            if (logBox.InvokeRequired)
            {
                try
                {
                    logBox.BeginInvoke(new Action<string>(AppendLog), raw);
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            string line = LogText.Format(raw);
            if (line == null)
            {
                return;
            }

            logBox.AppendText(line + Environment.NewLine);
            if (logBox.TextLength > 40000)
            {
                logBox.Text = logBox.Text.Substring(logBox.TextLength - 20000);
            }

            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            bool running = Probe.Ok(ReadyUrl());
            if (!allowExit && e.CloseReason == CloseReason.UserClosing && running)
            {
                e.Cancel = true;
                Hide();
                if (!balloonShown)
                {
                    balloonShown = true;
                    tray.ShowBalloonTip(3000, "Obsidian is still connected", "ChatGPT can use the vault. Right-click the tray icon to disconnect.", ToolTipIcon.Info);
                }

                return;
            }

            if (e.CloseReason == CloseReason.WindowsShutDown || allowExit)
            {
                StopProcesses();
            }

            if (!e.Cancel)
            {
                timer.Stop();
                tray.Visible = false;
                tray.Dispose();
                if (appIcon != null)
                {
                    appIcon.Dispose();
                    appIcon = null;
                }
            }
        }

        private void ExitApp()
        {
            if (Probe.Ok(ReadyUrl()))
            {
                DialogResult answer = MessageBox.Show(this, "Disconnect Obsidian from ChatGPT and quit?", "Obsidian", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            allowExit = true;
            StopProcesses();
            tray.Visible = false;
            Close();
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly TextBox tunnelBox;
        private readonly TextBox openAiBox;
        private readonly TextBox obsidianBox;
        private readonly TextBox mcpBox;
        private readonly CheckBox startupBox;
        private readonly Label errorLabel;
        private readonly bool hadOpenAi;
        private readonly bool hadObsidian;

        public SettingsForm(AppSettings settings)
        {
            Text = "Obsidian connection settings";
            Font = new Font("Segoe UI", 10F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 520);
            ShowInTaskbar = false;

            hadOpenAi = !string.IsNullOrEmpty(settings.OpenAiKey);
            hadObsidian = !string.IsNullOrEmpty(settings.ObsidianKey);

            int y = 16;
            tunnelBox = AddField(this, ref y, "Tunnel id", settings.TunnelId, "tunnel_ then letters and numbers");
            openAiBox = AddField(this, ref y, "OpenAI API key", string.Empty, hadOpenAi ? "Saved. Type a new key only to replace it." : "sk-... from platform.openai.com");
            openAiBox.UseSystemPasswordChar = true;
            obsidianBox = AddField(this, ref y, "Obsidian API key", string.Empty, hadObsidian ? "Saved. Type a new key only to replace it." : "From Obsidian, Settings, Local REST API");
            obsidianBox.UseSystemPasswordChar = true;
            mcpBox = AddField(this, ref y, "Obsidian address", string.IsNullOrEmpty(settings.McpUrl) ? SettingsStore.DefaultMcpUrl : settings.McpUrl, "http://127.0.0.1:27123/mcp/");

            startupBox = new CheckBox();
            startupBox.Text = "Connect when I sign in to Windows";
            startupBox.Checked = settings.StartWithWindows;
            startupBox.Location = new Point(20, y);
            startupBox.Size = new Size(500, 24);
            Controls.Add(startupBox);
            y += 34;

            Label help = new Label();
            help.Text = "The OpenAI key and the Obsidian key are different. They stay encrypted for this Windows account. Obsidian must be open, with Local REST API and the insecure HTTP server turned on.";
            help.Location = new Point(20, y);
            help.Size = new Size(500, 64);
            help.ForeColor = Color.FromArgb(75, 85, 99);
            Controls.Add(help);
            y += 68;

            errorLabel = new Label();
            errorLabel.Location = new Point(20, y);
            errorLabel.Size = new Size(500, 40);
            errorLabel.ForeColor = Color.FromArgb(155, 58, 47);
            Controls.Add(errorLabel);

            Button save = new Button();
            save.Text = "Save";
            save.Location = new Point(410, 470);
            save.Size = new Size(110, 34);
            save.Click += delegate { SaveSettings(); };
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Location = new Point(292, 470);
            cancel.Size = new Size(110, 34);
            cancel.DialogResult = DialogResult.Cancel;
            Button forget = new Button();
            forget.Text = "Forget keys";
            forget.Location = new Point(20, 470);
            forget.Size = new Size(120, 34);
            forget.Click += delegate { Forget(); };
            Controls.Add(save);
            Controls.Add(cancel);
            Controls.Add(forget);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static TextBox AddField(Control parent, ref int y, string caption, string value, string cue)
        {
            Label label = new Label();
            label.Text = caption;
            label.Location = new Point(20, y);
            label.Size = new Size(500, 20);
            parent.Controls.Add(label);
            y += 22;
            TextBox box = new TextBox();
            box.Location = new Point(20, y);
            box.Size = new Size(500, 26);
            box.Text = value ?? string.Empty;
            box.HandleCreated += delegate { SetCue(box, cue); };
            parent.Controls.Add(box);
            y += 38;
            return box;
        }

        private static void SetCue(TextBox box, string cue)
        {
            if (box.IsHandleCreated && !string.IsNullOrEmpty(cue))
            {
                SendMessage(box.Handle, 0x1501, (IntPtr)1, cue);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private void SaveSettings()
        {
            AppSettings current = SettingsStore.Load();
            AppSettings next = new AppSettings();
            next.TunnelId = tunnelBox.Text.Trim();
            next.McpUrl = mcpBox.Text.Trim();
            next.StartWithWindows = startupBox.Checked;
            next.OpenAiKey = openAiBox.Text.Trim();
            next.ObsidianKey = obsidianBox.Text.Trim();
            if (next.ObsidianKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                next.ObsidianKey = next.ObsidianKey.Substring(7).Trim();
            }

            if (string.IsNullOrEmpty(next.OpenAiKey))
            {
                next.OpenAiKey = current.OpenAiKey;
            }

            if (string.IsNullOrEmpty(next.ObsidianKey))
            {
                next.ObsidianKey = current.ObsidianKey;
            }

            if (!SettingsStore.IsValidTunnelId(next.TunnelId))
            {
                errorLabel.Text = "Enter the tunnel id from platform.openai.com. It looks like tunnel_ followed by letters and numbers.";
                return;
            }

            if (!SettingsStore.IsValidMcpUrl(next.McpUrl))
            {
                errorLabel.Text = "The Obsidian address should look like http://127.0.0.1:27123/mcp/";
                return;
            }

            if (string.IsNullOrEmpty(next.OpenAiKey) || string.IsNullOrEmpty(next.ObsidianKey))
            {
                errorLabel.Text = "Enter both keys. The OpenAI key starts with sk-. The other key is from Obsidian.";
                return;
            }

            try
            {
                SettingsStore.Save(next);
                SettingsStore.ApplyStartup(next.StartWithWindows);
            }
            catch (Exception ex)
            {
                errorLabel.Text = ex.Message;
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void Forget()
        {
            SettingsStore.ForgetKeys();
            openAiBox.Text = string.Empty;
            obsidianBox.Text = string.Empty;
            errorLabel.Text = "Saved keys removed from this Windows account.";
        }
    }
}
