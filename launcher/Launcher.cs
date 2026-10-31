using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QqBotLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 4 && args[0] == "--write-config")
            {
                try
                {
                    string qq = args[1];
                    string token = args[2];
                    string napcat = args[3];
                    int httpPort = args.Length > 4 ? int.Parse(args[4]) : 3000;
                    int wsPort = args.Length > 5 ? int.Parse(args[5]) : 3001;
                    string path = LauncherConfig.WriteOneBotConfig(napcat, qq, token, httpPort, wsPort);
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log"), path, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log"), ex.ToString(), Encoding.UTF8);
                }
                return;
            }

            if (args.Length >= 2 && args[0] == "--sync-config")
            {
                StringBuilder result = new StringBuilder();
                try
                {
                    LauncherConfig cfg = LauncherConfig.Load();
                    MainForm.EnsureAutoSettings(cfg);
                    cfg.GameDir = args[1];
                    cfg.Save();
                    MainForm.SyncModConfig(cfg, args[1], delegate(string message)
                    {
                        result.AppendLine(message);
                    });
                    result.AppendLine("token=" + cfg.Token);
                    result.AppendLine("http=" + cfg.HttpPort);
                    result.AppendLine("ws=" + cfg.WsPort);
                }
                catch (Exception ex)
                {
                    result.AppendLine(ex.ToString());
                }
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log"), result.ToString(), Encoding.UTF8);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString(), Encoding.UTF8);
                MessageBox.Show(ex.ToString(), "启动器崩溃");
            }
        }
    }

    /* ============================ 主题 ============================ */

    internal static class Theme
    {
        public static readonly Color BgTop = Color.FromArgb(232, 240, 255);
        public static readonly Color BgBottom = Color.FromArgb(246, 236, 255);
        public static readonly Color Bg = Color.FromArgb(233, 238, 247);
        public static readonly Color Surface = Color.FromArgb(255, 255, 255);
        public static readonly Color CardFill = Color.FromArgb(214, 255, 255, 255);
        public static readonly Color CardBorder = Color.FromArgb(255, 214, 224, 240);
        public static readonly Color TextPrimary = Color.FromArgb(28, 28, 30);
        public static readonly Color TextSecondary = Color.FromArgb(110, 110, 118);
        public static readonly Color Accent = Color.FromArgb(10, 132, 255);
        public static readonly Color AccentLight = Color.FromArgb(90, 200, 250);
        public static readonly Color Danger = Color.FromArgb(255, 59, 48);
        public static readonly Color Success = Color.FromArgb(52, 199, 89);
        public static readonly Color LogBack = Color.FromArgb(24, 24, 26);
        public static readonly Color LogText = Color.FromArgb(229, 229, 234);

        public static Font Ui(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static Font Mono(float size)
        {
            return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Max(1f, radius * 2f);
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, RectangleF rect, float radius, Brush brush)
        {
            using (GraphicsPath path = Rounded(rect, radius))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRounded(Graphics g, RectangleF rect, float radius, Pen pen)
        {
            using (GraphicsPath path = Rounded(rect, radius))
            {
                g.DrawPath(pen, path);
            }
        }

        public static void DrawShadow(Graphics g, Rectangle rect, float radius, int depth)
        {
            for (int i = depth; i >= 1; i--)
            {
                int alpha = 6 + (depth - i) * 3;
                Rectangle shadow = new Rectangle(rect.X - i, rect.Y - i + 3, rect.Width + i * 2, rect.Height + i * 2);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, 40, 60, 110)))
                {
                    FillRounded(g, shadow, radius + i, brush);
                }
            }
        }
    }

    /* ============================ 配置 ============================ */

    internal sealed class LauncherConfig
    {
        public string Qq = "";
        public string Token = "";
        public int HttpPort = 3000;
        public int WsPort = 3001;
        public string NapCatDir = "";
        public string QqExe = "";
        public string GameDir = "";
        public long GroupId = 0L;
        public bool Receive = true;
        public bool Broadcast = false;
        public string Prefix = ":";
        public int SendIntervalMs = 1200;
        public int MaxMessageLength = 900;
        public string IgnoreIds = "";
        public string GameFormat = "&7{time} &d{prefix}{role}{title}{level}{name}&8({qq})&7: {message}";
        public string QqFormat = "[{player}] {message}";
        public string GroupsRaw = "";
        public string RelayUrl = "";
        public string RelayToken = "";

        public static string IniPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.ini");
        }

        public static LauncherConfig Load()
        {
            LauncherConfig config = new LauncherConfig();
            config.NapCatDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "napcat");
            if (!File.Exists(IniPath()))
            {
                config.Token = RandomToken();
                return config;
            }
            foreach (string line in File.ReadAllLines(IniPath(), Encoding.UTF8))
            {
                int split = line.IndexOf('=');
                if (split <= 0) continue;
                string key = line.Substring(0, split).Trim();
                string value = line.Substring(split + 1).Trim();
                if (key == "qq") config.Qq = value;
                else if (key == "token") config.Token = value;
                else if (key == "httpPort") config.HttpPort = ParseInt(value, 3000);
                else if (key == "wsPort") config.WsPort = ParseInt(value, 3001);
                else if (key == "napcatDir" && value.Length > 0) config.NapCatDir = value;
                else if (key == "qqExe" && value.Length > 0) config.QqExe = value;
                else if (key == "gameDir" && value.Length > 0) config.GameDir = value;
                else if (key == "groupId") { long gid; if (long.TryParse(value, out gid)) config.GroupId = gid; }
                else if (key == "receive") config.Receive = value == "true";
                else if (key == "broadcast") config.Broadcast = value == "true";
                else if (key == "prefix") config.Prefix = value;
                else if (key == "sendIntervalMs") config.SendIntervalMs = ParseInt(value, 1200);
                else if (key == "maxMessageLength") config.MaxMessageLength = ParseInt(value, 900);
                else if (key == "ignoreIds") config.IgnoreIds = value;
                else if (key == "gameFormat") config.GameFormat = DecodeIni(value);
                else if (key == "qqFormat") config.QqFormat = DecodeIni(value);
                else if (key == "groups") config.GroupsRaw = DecodeIni(value);
                else if (key == "relayUrl") config.RelayUrl = value;
                else if (key == "relayToken") config.RelayToken = value;
            }
            if (config.Token.Length == 0) config.Token = RandomToken();
            return config;
        }

        private static string EncodeIni(string value)
        {
            if (value == null)
            {
                return "";
            }
            return value.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string DecodeIni(string value)
        {
            if (value == null)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    char next = value[i + 1];
                    if (next == 'n')
                    {
                        sb.Append('\n');
                        i++;
                        continue;
                    }
                    if (next == '\\')
                    {
                        sb.Append('\\');
                        i++;
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : fallback;
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("qq=" + Qq);
            sb.AppendLine("token=" + Token);
            sb.AppendLine("httpPort=" + HttpPort);
            sb.AppendLine("wsPort=" + WsPort);
            sb.AppendLine("napcatDir=" + NapCatDir);
            sb.AppendLine("qqExe=" + QqExe);
            sb.AppendLine("gameDir=" + GameDir);
            sb.AppendLine("groupId=" + GroupId);
            sb.AppendLine("receive=" + Receive.ToString().ToLowerInvariant());
            sb.AppendLine("broadcast=" + Broadcast.ToString().ToLowerInvariant());
            sb.AppendLine("prefix=" + Prefix);
            sb.AppendLine("sendIntervalMs=" + SendIntervalMs);
            sb.AppendLine("maxMessageLength=" + MaxMessageLength);
            sb.AppendLine("ignoreIds=" + IgnoreIds);
            sb.AppendLine("gameFormat=" + EncodeIni(GameFormat));
            sb.AppendLine("qqFormat=" + EncodeIni(QqFormat));
            sb.AppendLine("groups=" + EncodeIni(GroupsRaw));
            sb.AppendLine("relayUrl=" + RelayUrl);
            sb.AppendLine("relayToken=" + RelayToken);
            File.WriteAllText(IniPath(), sb.ToString(), Encoding.UTF8);
        }

        public static string RandomToken()
        {
            const string chars = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Random random = new Random();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 16; i++) sb.Append(chars[random.Next(chars.Length)]);
            return sb.ToString();
        }

        public static string WriteOneBotConfig(string napcatDir, string qq, string token, int httpPort, int wsPort)
        {
            string configDir = Path.Combine(napcatDir, "config");
            Directory.CreateDirectory(configDir);
            string path = Path.Combine(configDir, "onebot11_" + qq + ".json");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"network\": {");
            sb.AppendLine("    \"httpServers\": [");
            sb.AppendLine("      {");
            sb.AppendLine("        \"name\": \"http-server\",");
            sb.AppendLine("        \"enable\": true,");
            sb.AppendLine("        \"port\": " + httpPort + ",");
            sb.AppendLine("        \"host\": \"127.0.0.1\",");
            sb.AppendLine("        \"enableCors\": true,");
            sb.AppendLine("        \"enableWebsocket\": false,");
            sb.AppendLine("        \"messagePostFormat\": \"array\",");
            sb.AppendLine("        \"token\": \"" + Escape(token) + "\",");
            sb.AppendLine("        \"debug\": false");
            sb.AppendLine("      }");
            sb.AppendLine("    ],");
            sb.AppendLine("    \"httpSseServers\": [],");
            sb.AppendLine("    \"httpClients\": [],");
            sb.AppendLine("    \"websocketServers\": [");
            sb.AppendLine("      {");
            sb.AppendLine("        \"name\": \"websocket-server\",");
            sb.AppendLine("        \"enable\": true,");
            sb.AppendLine("        \"host\": \"127.0.0.1\",");
            sb.AppendLine("        \"port\": " + wsPort + ",");
            sb.AppendLine("        \"messagePostFormat\": \"array\",");
            sb.AppendLine("        \"reportSelfMessage\": true,");
            sb.AppendLine("        \"token\": \"" + Escape(token) + "\",");
            sb.AppendLine("        \"enableForcePushEvent\": true,");
            sb.AppendLine("        \"debug\": false,");
            sb.AppendLine("        \"heartInterval\": 30000");
            sb.AppendLine("      }");
            sb.AppendLine("    ],");
            sb.AppendLine("    \"websocketClients\": [],");
            sb.AppendLine("    \"plugins\": []");
            sb.AppendLine("  },");
            sb.AppendLine("  \"musicSignUrl\": \"\",");
            sb.AppendLine("  \"enableLocalFile2Url\": false,");
            sb.AppendLine("  \"parseMultMsg\": false,");
            sb.AppendLine("  \"imageDownloadProxy\": \"\",");
            sb.AppendLine("  \"timeout\": {");
            sb.AppendLine("    \"baseTimeout\": 10000,");
            sb.AppendLine("    \"uploadSpeedKBps\": 256,");
            sb.AppendLine("    \"downloadSpeedKBps\": 256,");
            sb.AppendLine("    \"maxTimeout\": 1800000");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            string content = sb.ToString();
            File.WriteAllText(path, content, new UTF8Encoding(false));
            // 再写一份通用配置：NapCat 按登录 QQ 找 onebot11_<uin>.json，
            // 找不到会回退到 onebot11.json，并把它复制成当前账号的配置。
            File.WriteAllText(Path.Combine(configDir, "onebot11.json"), content, new UTF8Encoding(false));
            // 已经存在的其它账号配置（onebot11_<uin>.json）也统一改成当前 Token，
            // 避免 NapCat 按登录号读到了旧 Token。
            foreach (string file in Directory.GetFiles(configDir, "onebot11_*.json"))
            {
                try
                {
                    string text = File.ReadAllText(file, Encoding.UTF8);
                    string updated = Regex.Replace(text, "\"token\"\\s*:\\s*\"[^\"]*\"", "\"token\": \"" + token + "\"");
                    if (updated != text)
                    {
                        File.WriteAllText(file, updated, new UTF8Encoding(false));
                    }
                }
                catch (Exception)
                {
                }
            }
            WriteWebUiConfig(configDir, token);
            return path;
        }

        /** WebUI 的 token 是另一份配置（webui.json）。这里把它也设成同一个值。 */
        public static void WriteWebUiConfig(string configDir, string token)
        {
            string path = Path.Combine(configDir, "webui.json");
            string json;
            if (File.Exists(path))
            {
                json = File.ReadAllText(path, Encoding.UTF8);
                json = Regex.Replace(json, "\"host\"\\s*:\\s*\"[^\"]*\"", "\"host\": \"127.0.0.1\"");
                if (Regex.IsMatch(json, "\"token\"\\s*:"))
                {
                    json = Regex.Replace(json, "\"token\"\\s*:\\s*\"[^\"]*\"", "\"token\": \"" + token + "\"");
                }
                else
                {
                    json = json.TrimEnd();
                    if (json.EndsWith("}"))
                    {
                        json = json.Substring(0, json.Length - 1).TrimEnd();
                    }
                    json += (json.EndsWith("{") ? "" : ",") + "\n  \"token\": \"" + token + "\"\n}\n";
                }
            }
            else
            {
                json = "{\n"
                        + "  \"host\": \"127.0.0.1\",\n"
                        + "  \"port\": 6099,\n"
                        + "  \"token\": \"" + token + "\",\n"
                        + "  \"loginRate\": 10,\n"
                        + "  \"autoLoginAccount\": \"\",\n"
                        + "  \"disableWebUI\": false,\n"
                        + "  \"accessControlMode\": \"none\",\n"
                        + "  \"ipWhitelist\": [],\n"
                        + "  \"ipBlacklist\": [],\n"
                        + "  \"enableXForwardedFor\": false,\n"
                        + "  \"enable2FA\": false,\n"
                        + "  \"totpSecret\": \"\"\n"
                        + "}\n";
            }
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    /* ============================ 控件 ============================ */

    internal sealed class GlassCard : Panel
    {
        public string Title = "";
        public Color TitleColor = Theme.TextPrimary;
        public Color FillColor = Theme.Surface;
        public Color BorderColor = Color.FromArgb(255, 214, 224, 240);
        public bool ShowHighlight = true;

        public GlassCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
            // 卡片圆角外的区域显示父容器底色，避免 Region 裁剪造成锯齿
            BackColor = Theme.Bg;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            RectangleF rect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
            using (SolidBrush fill = new SolidBrush(FillColor))
            {
                Theme.FillRounded(g, rect, 18f, fill);
            }

            if (ShowHighlight)
            {
                // 顶部做一层渐变高光，而不是一条硬边白带
                using (GraphicsPath path = Theme.Rounded(rect, 18f))
                {
                    Region old = g.Clip;
                    g.SetClip(path);
                    using (LinearGradientBrush top = new LinearGradientBrush(
                        new Rectangle(0, 0, Math.Max(1, Width), 44),
                        Color.FromArgb(70, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255), 90f))
                    {
                        g.FillRectangle(top, 0, 0, Width, 44);
                    }
                    g.Clip = old;
                }
            }

            using (Pen border = new Pen(BorderColor, 1f))
            {
                Theme.DrawRounded(g, rect, 18f, border);
            }

            if (Title.Length > 0)
            {
                using (Font font = Theme.Ui(10.5f, FontStyle.Bold))
                using (SolidBrush brush = new SolidBrush(TitleColor))
                {
                    g.DrawString(Title, font, brush, 20f, 14f);
                }
            }
            base.OnPaint(e);
        }
    }

    internal sealed class PillButton : Control
    {
        public enum Kind
        {
            Primary,
            Secondary,
            Danger,
            Ghost
        }

        public Kind Variant = Kind.Secondary;

        private float hover;
        private float press;
        private float hoverTarget;
        private float pressTarget;
        private readonly Timer animator;

        public PillButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Bg;
            Font = Theme.Ui(9.5f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            Height = 42;
            animator = new Timer();
            animator.Interval = 15;
            animator.Tick += delegate { Animate(); };
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hoverTarget = 1f;
            StartAnimator();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverTarget = 0f;
            pressTarget = 0f;
            StartAnimator();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressTarget = 1f;
            StartAnimator();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressTarget = 0f;
            StartAnimator();
            base.OnMouseUp(e);
        }

        private void StartAnimator()
        {
            if (!animator.Enabled)
            {
                animator.Start();
            }
        }

        private void Animate()
        {
            bool moving = false;
            hover = Step(hover, hoverTarget, 0.16f, ref moving);
            press = Step(press, pressTarget, 0.30f, ref moving);
            if (moving)
            {
                Invalidate();
            }
            else
            {
                animator.Stop();
            }
        }

        private static float Step(float value, float target, float speed, ref bool moving)
        {
            if (Math.Abs(value - target) <= speed)
            {
                return target;
            }
            moving = true;
            return value + Math.Sign(target - value) * speed;
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int)(from.A + (to.A - from.A) * amount),
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            bool enabled = Enabled;
            float alpha = enabled ? 1f : 0.42f;
            float scale = 1f - press * 0.035f;
            float w = Width * scale;
            float h = Height * scale;
            float x = (Width - w) / 2f;
            float y = (Height - h) / 2f + press * 0.8f;
            RectangleF rect = new RectangleF(x + 0.5f, y + 0.5f, Math.Max(1f, w - 1f), Math.Max(1f, h - 1f));
            float radius = Math.Max(1f, (h - 1f) / 2f);

            if (Variant == Kind.Primary)
            {
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb((int)((18 + 30 * hover) * alpha), 16, 82, 170)))
                {
                    Theme.FillRounded(g, new RectangleF(x, y + 3.5f, w, h), radius, shadow);
                }
                Color top = Blend(Theme.Accent, Color.White, 0.12f * hover);
                Color bottom = Blend(Theme.AccentLight, Color.White, 0.12f * hover);
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, (int)w), Math.Max(1, (int)h)),
                    Color.FromArgb((int)(255 * alpha), top),
                    Color.FromArgb((int)(255 * alpha), bottom), 0f))
                {
                    Theme.FillRounded(g, rect, radius, brush);
                }
                using (SolidBrush text = new SolidBrush(Color.FromArgb((int)(255 * alpha), Color.White)))
                {
                    DrawLabel(g, text, y);
                }
            }
            else if (Variant == Kind.Danger)
            {
                int fill = (int)((26 + 22 * hover) * alpha);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(fill, Theme.Danger)))
                {
                    Theme.FillRounded(g, rect, radius, brush);
                }
                using (Pen border = new Pen(Color.FromArgb((int)((110 + 60 * hover) * alpha), Theme.Danger), 1f))
                {
                    Theme.DrawRounded(g, rect, radius, border);
                }
                using (SolidBrush text = new SolidBrush(Color.FromArgb((int)(255 * alpha), Theme.Danger)))
                {
                    DrawLabel(g, text, y);
                }
            }
            else if (Variant == Kind.Ghost)
            {
                if (hover > 0.01f)
                {
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(26 * hover * alpha), 40, 50, 70)))
                    {
                        Theme.FillRounded(g, rect, radius, brush);
                    }
                }
                using (SolidBrush text = new SolidBrush(Color.FromArgb((int)(225 * alpha), Theme.TextSecondary)))
                {
                    DrawLabel(g, text, y);
                }
            }
            else
            {
                using (SolidBrush brush = new SolidBrush(
                    Color.FromArgb((int)((228 + 27 * hover) * alpha), Color.White)))
                {
                    Theme.FillRounded(g, rect, radius, brush);
                }
                using (Pen border = new Pen(
                    Color.FromArgb((int)((80 + 40 * hover) * alpha), 170, 182, 205), 1f))
                {
                    Theme.DrawRounded(g, rect, radius, border);
                }
                using (SolidBrush text = new SolidBrush(Color.FromArgb((int)(255 * alpha), Theme.TextPrimary)))
                {
                    DrawLabel(g, text, y);
                }
            }
        }

        private void DrawLabel(Graphics g, Brush brush, float offsetY)
        {
            StringFormat format = new StringFormat();
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            g.DrawString(Text, Font, brush, new RectangleF(0, offsetY, Width, Height), format);
            format.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && animator != null)
            {
                animator.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class CircleButton : Control
    {
        public bool Danger;
        private bool hovered;

        public CircleButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hovered)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(Danger ? 70 : 40, Danger ? 255 : 0, Danger ? 80 : 0, Danger ? 60 : 0)))
                {
                    g.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
                }
            }
            using (Pen pen = new Pen(Color.FromArgb(210, Theme.TextSecondary), 1.6f))
            {
                if (Text == "x")
                {
                    g.DrawLine(pen, Width * 0.32f, Height * 0.32f, Width * 0.68f, Height * 0.68f);
                    g.DrawLine(pen, Width * 0.68f, Height * 0.32f, Width * 0.32f, Height * 0.68f);
                }
                else
                {
                    g.DrawLine(pen, Width * 0.28f, Height * 0.55f, Width * 0.72f, Height * 0.55f);
                }
            }
            base.OnPaint(e);
        }
    }

    internal sealed class GlassInput : Control
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);

        public readonly TextBox Box;
        private string placeholder = "";
        private bool focused;

        public string Placeholder
        {
            get { return placeholder; }
            set
            {
                placeholder = value ?? "";
                ApplyPlaceholder();
            }
        }

        public GlassInput()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Surface;
            Height = 38;

            Box = new TextBox();
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Theme.Ui(10f, FontStyle.Regular);
            Box.ForeColor = Theme.TextPrimary;
            Box.BackColor = Color.FromArgb(252, 252, 255);
            Box.Enter += delegate { focused = true; Invalidate(); };
            Box.Leave += delegate { focused = false; Invalidate(); };
            Controls.Add(Box);
        }

        private void ApplyPlaceholder()
        {
            if (Box == null)
            {
                return;
            }
            IntPtr handle = Box.Handle;
            SendMessage(handle, 0x1501, 1, placeholder);
        }

        public bool ReadOnly
        {
            get { return Box.ReadOnly; }
            set
            {
                Box.ReadOnly = value;
                Box.BackColor = value ? Color.FromArgb(246, 248, 252) : Color.FromArgb(252, 252, 255);
                Invalidate();
            }
        }

        public string TextValue
        {
            get { return Box.Text; }
            set { Box.Text = value; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Box == null)
            {
                return;
            }
            Box.SetBounds(12, (Height - Box.PreferredHeight) / 2 + 1, Math.Max(10, Width - 24), Box.PreferredHeight);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
            {
                Theme.FillRounded(g, rect, 12f, fill);
            }
            using (Pen border = new Pen(focused ? Theme.Accent : Color.FromArgb(70, 150, 160, 180), focused ? 1.6f : 1f))
            {
                Theme.DrawRounded(g, rect, 12f, border);
            }
            if (Box.TextLength == 0 && !focused && Placeholder.Length > 0)
            {
                ApplyPlaceholder();
            }
            base.OnPaint(e);
        }
    }

    internal sealed class StatusChip : Control
    {
        public Color DotColor = Theme.TextSecondary;
        private string caption = "未启动";

        public StatusChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Bg;
            Height = 30;
            Font = Theme.Ui(9f, FontStyle.Bold);
        }

        public void SetStatus(string text, Color color)
        {
            caption = text;
            DotColor = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
            {
                Theme.FillRounded(g, rect, Height / 2f, fill);
            }
            using (SolidBrush dot = new SolidBrush(DotColor))
            {
                g.FillEllipse(dot, 12, (Height - 8) / 2f, 8, 8);
            }
            using (SolidBrush text = new SolidBrush(Theme.TextPrimary))
            {
                g.DrawString(caption, Font, text, 26, (Height - 18) / 2f + 1);
            }
            base.OnPaint(e);
        }
    }

    internal sealed class GlassLabel : Control
    {
        private Font textFont = Theme.Ui(8.5f, FontStyle.Regular);
        public Color TextColor = Theme.TextSecondary;

        public GlassLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Surface;
            TabStop = false;
        }

        public void Style(Font font, Color color)
        {
            textFont = font;
            TextColor = color;
            AutoFit();
        }

        public void AutoFit()
        {
            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                SizeF size = g.MeasureString(Text, textFont);
                Size = new Size((int)Math.Ceiling(size.Width) + 2, (int)Math.Ceiling(size.Height));
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AutoFit();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (SolidBrush brush = new SolidBrush(TextColor))
            {
                e.Graphics.DrawString(Text, textFont, brush, 0, 0);
            }
        }
    }

    internal sealed class GroupInfo
    {
        public readonly long Id;
        public readonly string Name;

        public GroupInfo(long id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Label
        {
            get { return Name + "（" + Id + "）"; }
        }
    }

    internal sealed class GroupPickerForm : Form
    {
        private readonly ListBox list = new ListBox();
        public GroupInfo Selected { get; private set; }

        public GroupPickerForm(List<GroupInfo> groups, long current)
        {
            Text = "选择要互通的 QQ 群";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(440, 380);
            BackColor = Theme.Surface;
            Font = Theme.Ui(9f, FontStyle.Regular);

            list.SetBounds(16, 16, 408, 300);
            list.Font = Theme.Ui(9.5f, FontStyle.Regular);
            list.IntegralHeight = false;
            list.DisplayMember = "Label";
            foreach (GroupInfo group in groups)
            {
                list.Items.Add(group);
            }
            for (int i = 0; i < list.Items.Count; i++)
            {
                GroupInfo info = (GroupInfo)list.Items[i];
                if (info.Id == current)
                {
                    list.SelectedIndex = i;
                }
            }
            Controls.Add(list);

            Button ok = new Button();
            ok.Text = "确定";
            ok.SetBounds(248, 328, 84, 30);
            ok.Click += delegate
            {
                Selected = list.SelectedItem as GroupInfo;
                DialogResult = DialogResult.OK;
            };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(340, 328, 84, 30);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            list.DoubleClick += delegate
            {
                Selected = list.SelectedItem as GroupInfo;
                if (Selected != null)
                {
                    DialogResult = DialogResult.OK;
                }
            };
        }
    }

    internal sealed class GlassToggle : Control
    {
        private bool value;
        private float anim;
        private readonly Timer timer;

        public event EventHandler ValueChanged;

        public GlassToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Surface;
            Size = new Size(48, 26);
            Cursor = Cursors.Hand;
            timer = new Timer();
            timer.Interval = 15;
            timer.Tick += delegate { Step(); };
        }

        public bool Value
        {
            get { return value; }
            set
            {
                if (this.value == value)
                {
                    return;
                }
                this.value = value;
                if (!timer.Enabled)
                {
                    timer.Start();
                }
                Invalidate();
                if (ValueChanged != null)
                {
                    ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        private void Step()
        {
            float target = value ? 1f : 0f;
            float diff = target - anim;
            if (Math.Abs(diff) < 0.12f)
            {
                anim = target;
                timer.Stop();
            }
            else
            {
                anim += Math.Sign(diff) * 0.12f;
            }
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            Value = !Value;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF track = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color color = Blend(Color.FromArgb(208, 215, 226), Color.FromArgb(52, 199, 89), anim);
            using (SolidBrush brush = new SolidBrush(color))
            {
                Theme.FillRounded(g, track, Height / 2f, brush);
            }
            float knob = Height - 6f;
            float x = 3f + anim * (Width - knob - 6f);
            using (SolidBrush white = new SolidBrush(Color.White))
            {
                g.FillEllipse(white, x, 3f, knob, knob);
            }
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int)(from.A + (to.A - from.A) * amount),
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && timer != null)
            {
                timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class AdminForm : Form
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly LauncherConfig cfg;
        private readonly Action saved;
        private readonly GlassInput prefixInput = new GlassInput();
        private readonly GlassInput intervalInput = new GlassInput();
        private readonly GlassInput lengthInput = new GlassInput();
        private readonly GlassInput ignoreInput = new GlassInput();
        private readonly TextBox gameFormatInput = new TextBox();
        private readonly TextBox qqFormatInput = new TextBox();
        private readonly TextBox groupsInput = new TextBox();
        private readonly GlassInput relayUrlInput = new GlassInput();
        private readonly GlassInput relayTokenInput = new GlassInput();
        private readonly GlassToggle receiveToggle = new GlassToggle();
        private readonly GlassToggle broadcastToggle = new GlassToggle();

        public AdminForm(LauncherConfig cfg, Action saved)
        {
            this.cfg = cfg;
            this.saved = saved;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 1000);
            MaximumSize = new Size(560, 1000);
            MinimumSize = new Size(560, 1000);
            BackColor = Theme.Bg;
            Font = Theme.Ui(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
            Text = "管理员设置";

            BuildTitle();
            BuildMessageCard();
            BuildFormatCard();
            BuildIgnoreCard();
            BuildGroupsCard();
            BuildRelayCard();
            BuildButtons();

            receiveToggle.Value = cfg.Receive;
            broadcastToggle.Value = cfg.Broadcast;
            prefixInput.TextValue = cfg.Prefix;
            intervalInput.TextValue = cfg.SendIntervalMs.ToString();
            lengthInput.TextValue = cfg.MaxMessageLength.ToString();
            ignoreInput.TextValue = cfg.IgnoreIds;
            gameFormatInput.Text = cfg.GameFormat;
            qqFormatInput.Text = cfg.QqFormat;
            groupsInput.Text = cfg.GroupsRaw;
            relayUrlInput.TextValue = cfg.RelayUrl;
            relayTokenInput.TextValue = cfg.RelayToken;
        }

        private void BuildTitle()
        {
            Panel bar = new Panel();
            bar.SetBounds(0, 0, ClientSize.Width, 64);
            bar.BackColor = Theme.Bg;
            bar.MouseDown += DragWindow;
            Controls.Add(bar);

            GlassLabel title = new GlassLabel();
            title.Text = "管理员设置";
            title.BackColor = Theme.Bg;
            title.Style(Theme.Ui(13f, FontStyle.Bold), Theme.TextPrimary);
            title.Location = new Point(24, 20);
            title.MouseDown += DragWindow;
            bar.Controls.Add(title);

            CircleButton close = new CircleButton();
            close.Text = "x";
            close.Danger = true;
            close.SetBounds(ClientSize.Width - 46, 18, 28, 28);
            close.Click += delegate { Close(); };
            bar.Controls.Add(close);
        }

        private void BuildMessageCard()
        {
            GlassCard card = new GlassCard();
            card.Title = "消息";
            card.SetBounds(20, 64, 520, 196);
            Controls.Add(card);

            AddToggleRow(card, "接收 QQ 消息", 20, 44, receiveToggle);
            AddToggleRow(card, "全服广播", 20, 82, broadcastToggle);
            GlassLabel broadcastHint = new GlassLabel();
            broadcastHint.Text = "开启后 QQ 消息会以服务器聊天形式发出";
            broadcastHint.BackColor = Theme.Surface;
            broadcastHint.Style(Theme.Ui(7.5f, FontStyle.Regular), Theme.TextSecondary);
            broadcastHint.Location = new Point(20, 104);
            card.Controls.Add(broadcastHint);

            AddField(card, "聊天前缀", 20, 134, prefixInput, 120);
            AddField(card, "发送间隔(ms)", 168, 134, intervalInput, 120);
            AddField(card, "单条最大长度", 316, 134, lengthInput, 184);
        }

        private void BuildIgnoreCard()
        {
            GlassCard card = new GlassCard();
            card.Title = "忽略的 QQ 号";
            card.SetBounds(20, 532, 520, 96);
            Controls.Add(card);

            ignoreInput.Placeholder = "多个用逗号分隔，例如 123456,789012";
            ignoreInput.SetBounds(20, 44, 480, 34);
            card.Controls.Add(ignoreInput);
        }
        private void BuildFormatCard()
        {
            GlassCard card = new GlassCard();
            card.Title = "格式模板";
            card.SetBounds(20, 272, 520, 248);
            Controls.Add(card);

            GlassLabel gameLabel = new GlassLabel();
            gameLabel.Text = "游戏内显示（QQ → 游戏）";
            gameLabel.BackColor = Theme.Surface;
            gameLabel.Style(Theme.Ui(8.5f, FontStyle.Regular), Theme.TextSecondary);
            gameLabel.Location = new Point(20, 38);
            card.Controls.Add(gameLabel);

            StyleMultiline(gameFormatInput);
            gameFormatInput.SetBounds(20, 58, 480, 56);
            card.Controls.Add(gameFormatInput);

            GlassLabel qqLabel = new GlassLabel();
            qqLabel.Text = "发到 QQ（游戏 → QQ）";
            qqLabel.BackColor = Theme.Surface;
            qqLabel.Style(Theme.Ui(8.5f, FontStyle.Regular), Theme.TextSecondary);
            qqLabel.Location = new Point(20, 110);
            card.Controls.Add(qqLabel);

            StyleMultiline(qqFormatInput);
            qqFormatInput.SetBounds(20, 142, 480, 40);
            card.Controls.Add(qqFormatInput);

            GlassLabel hint = new GlassLabel();
            hint.Text = "点右上角“字符表”查看全部占位符和颜色代码";
            hint.BackColor = Theme.Surface;
            hint.Style(Theme.Ui(7.5f, FontStyle.Regular), Theme.TextSecondary);
            hint.Location = new Point(20, 196);
            card.Controls.Add(hint);

            PillButton tokens = new PillButton();
            tokens.Text = "字符表";
            tokens.Variant = PillButton.Kind.Secondary;
            tokens.BackColor = Theme.Surface;
            tokens.SetBounds(396, 28, 104, 30);
            tokens.Click += delegate
            {
                using (TokenGuideForm guide = new TokenGuideForm())
                {
                    guide.ShowDialog(this);
                }
            };
            card.Controls.Add(tokens);
        }

        private void BuildGroupsCard()
        {
            GlassCard card = new GlassCard();
            card.Title = "多群同步";
            card.SetBounds(20, 640, 520, 120);
            Controls.Add(card);

            GlassLabel hint = new GlassLabel();
            hint.Text = "每行一个：群号=玩家1,玩家2（* 或留空 = 所有用户）；不在列表的群不转发";
            hint.BackColor = Theme.Surface;
            hint.Style(Theme.Ui(7.5f, FontStyle.Regular), Theme.TextSecondary);
            hint.Location = new Point(20, 38);
            card.Controls.Add(hint);

            StyleMultiline(groupsInput);
            groupsInput.SetBounds(20, 60, 480, 46);
            card.Controls.Add(groupsInput);
        }

        private static void StyleMultiline(TextBox box)
        {
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Color.White;
            box.ForeColor = Theme.TextPrimary;
            box.Font = Theme.Ui(8.5f, FontStyle.Regular);
        }
        private void BuildRelayCard()
        {
            GlassCard card = new GlassCard();
            card.Title = "服务器中转（可选）";
            card.SetBounds(20, 772, 520, 150);
            Controls.Add(card);

            GlassLabel hint = new GlassLabel();
            hint.Text = "填 主机:端口 后，模组通过中转连接；留空 = 使用本地 NapCat";
            hint.BackColor = Theme.Surface;
            hint.Style(Theme.Ui(7.5f, FontStyle.Regular), Theme.TextSecondary);
            hint.Location = new Point(20, 38);
            card.Controls.Add(hint);

            relayUrlInput.Placeholder = "例如 1.2.3.4:18574";
            relayUrlInput.SetBounds(20, 62, 480, 32);
            card.Controls.Add(relayUrlInput);

            relayTokenInput.Placeholder = "中转 Token（relay.ini 里的那个）";
            relayTokenInput.SetBounds(20, 104, 480, 32);
            card.Controls.Add(relayTokenInput);
        }
        private void BuildButtons()
        {
            PillButton save = new PillButton();
            save.Text = "保存";
            save.Variant = PillButton.Kind.Primary;
            save.SetBounds(20, 940, 140, 40);
            save.Click += delegate { Apply(); };
            Controls.Add(save);

            PillButton cancel = new PillButton();
            cancel.Text = "取消";
            cancel.Variant = PillButton.Kind.Secondary;
            cancel.SetBounds(172, 940, 100, 40);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
        }

        private void AddToggleRow(Control parent, string text, int x, int y, GlassToggle toggle)
        {
            GlassLabel label = new GlassLabel();
            label.Text = text;
            label.BackColor = Theme.Surface;
            label.Style(Theme.Ui(9f, FontStyle.Regular), Theme.TextSecondary);
            label.Location = new Point(x, y + 4);
            parent.Controls.Add(label);

            toggle.BackColor = Theme.Surface;
            toggle.Location = new Point(x + (x == 20 ? 170 : 170), y);
            if (x == 270)
            {
                toggle.Location = new Point(440, y);
            }
            else
            {
                toggle.Location = new Point(190, y);
            }
            parent.Controls.Add(toggle);
        }

        private void AddField(Control parent, string text, int x, int y, GlassInput input, int width)
        {
            GlassLabel label = new GlassLabel();
            label.Text = text;
            label.BackColor = Theme.Surface;
            label.Style(Theme.Ui(8f, FontStyle.Regular), Theme.TextSecondary);
            label.Location = new Point(x, y);
            parent.Controls.Add(label);

            input.SetBounds(x, y + 20, width, 34);
            parent.Controls.Add(input);
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        private void Apply()
        {
            int interval;
            int length;
            cfg.Receive = receiveToggle.Value;
            cfg.Broadcast = broadcastToggle.Value;
            cfg.Prefix = prefixInput.TextValue.Trim();
            cfg.SendIntervalMs = int.TryParse(intervalInput.TextValue.Trim(), out interval) ? Math.Max(300, interval) : 1200;
            cfg.MaxMessageLength = int.TryParse(lengthInput.TextValue.Trim(), out length) ? Math.Max(100, length) : 900;
            cfg.IgnoreIds = ignoreInput.TextValue.Trim();
            cfg.GameFormat = gameFormatInput.Text.Trim();
            cfg.QqFormat = qqFormatInput.Text.Trim();
            cfg.GroupsRaw = groupsInput.Text.Trim();
            cfg.RelayUrl = relayUrlInput.TextValue.Trim();
            cfg.RelayToken = relayTokenInput.TextValue.Trim();
            cfg.Save();
            if (saved != null)
            {
                saved();
            }
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(Color.FromArgb(45, 120, 145, 180), 1f))
            {
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    internal sealed class TokenGuideForm : Form
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly GlassLabel statusLabel = new GlassLabel();

        public TokenGuideForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 650);
            MaximumSize = new Size(520, 650);
            MinimumSize = new Size(520, 650);
            BackColor = Theme.Bg;
            Font = Theme.Ui(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
            Text = "格式字符表";

            Panel bar = new Panel();
            bar.SetBounds(0, 0, ClientSize.Width, 56);
            bar.BackColor = Theme.Bg;
            bar.MouseDown += DragWindow;
            Controls.Add(bar);

            GlassLabel title = new GlassLabel();
            title.Text = "格式字符表";
            title.BackColor = Theme.Bg;
            title.Style(Theme.Ui(12f, FontStyle.Bold), Theme.TextPrimary);
            title.Location = new Point(20, 18);
            title.MouseDown += DragWindow;
            bar.Controls.Add(title);

            PillButton copyAll = new PillButton();
            copyAll.Text = "复制全部";
            copyAll.Variant = PillButton.Kind.Secondary;
            copyAll.BackColor = Theme.Bg;
            copyAll.SetBounds(ClientSize.Width - 168, 14, 108, 30);
            copyAll.Click += delegate
            {
                try
                {
                    StringBuilder table = new StringBuilder();
                    table.AppendLine("游戏内：{time} {prefix} {role} {title} {level} {name} {qq} {group} {message}");
                    table.AppendLine("发到QQ：{player} {message} {time} {world} {group}");
                    table.AppendLine("颜色：&0-&f &l &o &n &m &r（支持 §）");
                    Clipboard.SetText(table.ToString());
                    statusLabel.Text = "已复制全部字符表";
                }
                catch (Exception)
                {
                    statusLabel.Text = "复制失败";
                }
            };
            bar.Controls.Add(copyAll);

            CircleButton close = new CircleButton();
            close.Text = "x";
            close.Danger = true;
            close.SetBounds(ClientSize.Width - 46, 16, 28, 28);
            close.Click += delegate { Close(); };
            bar.Controls.Add(close);

            GlassCard gameCard = new GlassCard();
            gameCard.Title = "游戏内显示（QQ → 游戏）";
            gameCard.SetBounds(20, 60, 480, 256);
            Controls.Add(gameCard);
            AddTokenRow(gameCard, "{time}", "时间（dateFormat，默认 HH:mm）", 40);
            AddTokenRow(gameCard, "{prefix}", "聊天前缀，默认 [QQ] ", 62);
            AddTokenRow(gameCard, "{role}", "群主/管理员徽章，如 [群主]", 84);
            AddTokenRow(gameCard, "{title}", "群头衔，如 [摸鱼王]", 106);
            AddTokenRow(gameCard, "{level}", "群等级，如 Lv.12", 128);
            AddTokenRow(gameCard, "{name}", "昵称/群名片（按 QQ 号取色，带悬停）", 150);
            AddTokenRow(gameCard, "{qq}", "QQ 号", 172);
            AddTokenRow(gameCard, "{group}", "多群同步时的群标签", 194);
            AddTokenRow(gameCard, "{message}", "正文（文字/表情/图片/语音）", 216);

            GlassCard qqCard = new GlassCard();
            qqCard.Title = "发到 QQ（游戏 → QQ）";
            qqCard.SetBounds(20, 328, 480, 168);
            Controls.Add(qqCard);
            AddTokenRow(qqCard, "{player}", "发送者游戏 ID", 40);
            AddTokenRow(qqCard, "{message}", "聊天内容", 62);
            AddTokenRow(qqCard, "{time}", "当前时间", 84);
            AddTokenRow(qqCard, "{world}", "当前维度", 106);
            AddTokenRow(qqCard, "{group}", "主群号", 128);

            GlassCard colorCard = new GlassCard();
            colorCard.Title = "颜色代码";
            colorCard.SetBounds(20, 508, 480, 86);
            Controls.Add(colorCard);

            GlassLabel colors = new GlassLabel();
            colors.Text = "&0-&f 颜色  &l 粗体  &o 斜体  &n 下划线  &m 删除线  &r 重置";
            colors.BackColor = Theme.Surface;
            colors.Style(Theme.Ui(8f, FontStyle.Regular), Theme.TextSecondary);
            colors.Location = new Point(20, 42);
            colorCard.Controls.Add(colors);

            GlassLabel colors2 = new GlassLabel();
            colors2.Text = "也支持 § 符号；不支持十六进制颜色。";
            colors2.BackColor = Theme.Surface;
            colors2.Style(Theme.Ui(8f, FontStyle.Regular), Theme.TextSecondary);
            colors2.Location = new Point(20, 62);
            colorCard.Controls.Add(colors2);

            statusLabel.Text = "点击任意字符即可复制";
            statusLabel.BackColor = Theme.Bg;
            statusLabel.Style(Theme.Ui(8.5f, FontStyle.Regular), Theme.TextSecondary);
            statusLabel.Location = new Point(20, 604);
            Controls.Add(statusLabel);
        }

        private void AddTokenRow(GlassCard card, string token, string desc, int y)
        {
            GlassLabel tokenLabel = new GlassLabel();
            tokenLabel.Text = token;
            tokenLabel.BackColor = Theme.Surface;
            tokenLabel.Style(Theme.Ui(9f, FontStyle.Bold), Theme.Accent);
            tokenLabel.Cursor = Cursors.Hand;
            tokenLabel.Location = new Point(20, y);
            tokenLabel.Click += delegate { CopyToken(token); };
            card.Controls.Add(tokenLabel);

            GlassLabel descLabel = new GlassLabel();
            descLabel.Text = desc;
            descLabel.BackColor = Theme.Surface;
            descLabel.Style(Theme.Ui(8.5f, FontStyle.Regular), Theme.TextSecondary);
            descLabel.Location = new Point(116, y);
            card.Controls.Add(descLabel);
        }

        private void CopyToken(string token)
        {
            try
            {
                Clipboard.SetText(token);
                statusLabel.Text = "已复制 " + token;
            }
            catch (Exception)
            {
                statusLabel.Text = "复制失败";
            }
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(Color.FromArgb(45, 120, 145, 180), 1f))
            {
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    /* ============================ 主窗体 ============================ */

    internal sealed class MainForm : Form
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly LauncherConfig config;

        private readonly GlassInput qqInput = new GlassInput();
        private readonly GlassInput tokenInput = new GlassInput();
        private readonly GlassInput groupInput = new GlassInput();

        private readonly GlassInput qqExeInput = new GlassInput();
        private readonly TextBox logBox = new TextBox();
        private readonly StatusChip statusChip = new StatusChip();
        private readonly PillButton startButton = new PillButton();
        private readonly PillButton stopButton = new PillButton();
        private Process napcatProcess;
        private Process relayProcess;
        private Process consoleProcess;
        private Timer groupCheckTimer;
        private int groupCheckTries;
        private bool autoPickShown;

        private readonly object logLock = new object();
        private StreamWriter logWriter;
        private string logPath;

        private readonly GlassCard cardAccount = new GlassCard();
        private readonly GlassCard cardPath = new GlassCard();
        private readonly GlassCard cardLog = new GlassCard();

        public MainForm()
        {
            config = LauncherConfig.Load();
            InitializeLogFile();

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(780, 880);
            MinimumSize = new Size(780, 880);
            MaximumSize = new Size(780, 880);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = Theme.Ui(9f, FontStyle.Regular);
            BackColor = Theme.Bg;
            Text = "QQ Chat Bridge 启动器";

            BuildTitleBar();
            BuildAccountCard();
            BuildPathCard();
            BuildActions();
            BuildLogCard();

            FormClosing += delegate
            {
                StopBot();
                StopConsole();
                SaveConfig();
                lock (logLock)
                {
                    if (logWriter != null)
                    {
                        try
                        {
                            logWriter.Flush();
                            logWriter.Dispose();
                        }
                        catch (Exception)
                        {
                        }
                        logWriter = null;
                    }
                }
            };
            Shown += delegate
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--open-console") >= 0)
                {
                    OpenConsole(null, EventArgs.Empty);
                }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--admin-settings") >= 0)
                {
                    OpenAdminSettings();
                }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--token-guide") >= 0)
                {
                    using (TokenGuideForm guide = new TokenGuideForm()) { guide.ShowDialog(this); }
                }
            };

            EnsureAutoSettings(config);
            if (config.GameDir.Length == 0 || !Directory.Exists(config.GameDir))
            {
                string detected = DetectGameDir();
                if (detected != null)
                {
                    config.GameDir = detected;
                    config.Save();
                }
            }
            LoadValues();
            CheckEnvironment();
            SyncAll(config, Log);
            Log("Token（与 NapCat / 模组同一份）：" + Mask(config.Token));
            Log("端口：HTTP " + config.HttpPort + " / WS " + config.WsPort);
        }

        /** 自动找常见的 .minecraft 目录，省得用户先装一次模组。 */
        private static string DetectGameDir()
        {
            List<string> candidates = new List<string>();
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft"));
            foreach (string dir in candidates)
            {
                if (Directory.Exists(dir)
                        && (Directory.Exists(Path.Combine(dir, "mods")) || Directory.Exists(Path.Combine(dir, "config"))))
                {
                    return dir;
                }
            }
            return null;
        }

        /** 把当前 Token / 端口同时写进 NapCat 与模组配置，保证三处一致。 */
        internal static void SyncAll(LauncherConfig cfg, Action<string> log)
        {
            string napcat = ResolveNapCatDir();
            if (napcat != null && cfg.Qq.Length > 0)
            {
                try
                {
                    string path = LauncherConfig.WriteOneBotConfig(napcat, cfg.Qq, cfg.Token, cfg.HttpPort, cfg.WsPort);
                    log("已写入 NapCat 配置：" + path + "（含 onebot11.json 兜底）");
                    log("WebUI：http://127.0.0.1:6099 ，Token 与上面相同");
                }
                catch (Exception ex)
                {
                    log("写 NapCat 配置失败：" + ex.Message);
                }
            }
            else
            {
                log("提示：QQ 号或 NapCat 未就绪，暂未写 NapCat 配置");
            }

            if (cfg.GameDir.Length > 0 && Directory.Exists(cfg.GameDir))
            {
                SyncModConfig(cfg, cfg.GameDir, log);
            }
            else
            {
                log("提示：还没记录 .minecraft 路径，模组配置未同步；");
                log("      请点一次“安装模组到 mods”。");
            }
        }

        /** Token 与端口全部自动处理，界面上不再让用户填。 */
        internal static void EnsureAutoSettings(LauncherConfig cfg)
        {
            bool changed = false;
            if (cfg.Token == null || cfg.Token.Length == 0)
            {
                cfg.Token = LauncherConfig.RandomToken();
                changed = true;
            }
            int http = FindFreePort(cfg.HttpPort >= 1024 ? cfg.HttpPort : 3000);
            if (http != cfg.HttpPort)
            {
                cfg.HttpPort = http;
                changed = true;
            }
            int wsStart = cfg.WsPort >= 1024 && cfg.WsPort != cfg.HttpPort ? cfg.WsPort : 3001;
            int ws = FindFreePort(wsStart);
            if (ws == cfg.HttpPort)
            {
                ws = FindFreePort(cfg.HttpPort + 1);
            }
            if (ws != cfg.WsPort)
            {
                cfg.WsPort = ws;
                changed = true;
            }
            if (changed)
            {
                cfg.Save();
            }
        }

        private static int FindFreePort(int start)
        {
            for (int port = Math.Max(1024, start); port < start + 50; port++)
            {
                if (PortFree(port))
                {
                    return port;
                }
            }
            return start;
        }

        private static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= 4)
            {
                return "****";
            }
            return value.Substring(0, 4) + "********";
        }

        /** 把 Token / 端口同步进 .minecraft/config/qqchatbridge.json，省得用户手改。 */
        internal static void SyncModConfig(LauncherConfig cfg, string gameDir, Action<string> log)
        {
            try
            {
                string configDir = Path.Combine(gameDir, "config");
                Directory.CreateDirectory(configDir);
                string path = Path.Combine(configDir, "qqchatbridge.json");
                // 整份重写：群号、Token、端口、广播/显示等全部以管理员设置为准
                File.WriteAllText(path, BuildModConfigJson(cfg), new UTF8Encoding(false));
                log("已写入模组配置（管理员设置）：" + path);
            }
            catch (Exception ex)
            {
                log("同步模组配置失败：" + ex.Message);
            }
            WriteRelayFiles(cfg, log);
        }

        /** relay 目录下的共享配置：选群/改设置后立即更新，不用等重启。 */
        internal static void WriteRelayFiles(LauncherConfig cfg, Action<string> log)
        {
            if (cfg.RelayUrl == null || cfg.RelayUrl.Trim().Length == 0)
            {
                return;
            }
            string relayDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "relay");
            if (!Directory.Exists(relayDir))
            {
                return;
            }
            try
            {
                File.WriteAllText(Path.Combine(relayDir, "client-config.json"),
                        BuildModConfigJson(cfg), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(relayDir, "client-bootstrap.json"),
                        BuildBootstrapJson(cfg), new UTF8Encoding(false));
                log("已更新下发配置：relay\\client-config.json（群号 " + cfg.GroupId + "）");
            }
            catch (Exception ex)
            {
                log("写下发配置失败：" + ex.Message);
            }
        }

        /** 玩家端引导配置：包含 remoteConfigUrl / remoteConfigToken，启动后自动拉取共享配置。 */
        private static string BuildBootstrapJson(LauncherConfig cfg)
        {
            string json = BuildModConfigJson(cfg);
            int end = json.LastIndexOf('}');
            if (end > 0)
            {
                json = json.Substring(0, end).TrimEnd();
                json += ",\n  \"remoteConfigUrl\": \"http://" + Escape(cfg.RelayUrl.Trim()) + "/client-config\",";
                json += "\n  \"remoteConfigToken\": \"" + Escape(cfg.RelayToken) + "\"\n}\n";
            }
            return json;
        }
        private static string BuildModConfigJson(LauncherConfig cfg)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            bool relay = cfg.RelayUrl != null && cfg.RelayUrl.Trim().Length > 0;
            string relayBase = relay ? cfg.RelayUrl.Trim().TrimEnd(new char[] { '/' }) : "";
            sb.AppendLine("  \"wsUrl\": \"" + (relay ? "ws://" + relayBase + "/ws" : "ws://127.0.0.1:" + cfg.WsPort) + "\",");
            sb.AppendLine("  \"httpUrl\": \"" + (relay ? "http://" + relayBase + "/api" : "http://127.0.0.1:" + cfg.HttpPort) + "\",");
            sb.AppendLine("  \"token\": \"" + Escape(relay ? cfg.RelayToken : cfg.Token) + "\",");
            sb.AppendLine("  \"groupId\": " + cfg.GroupId + ",");
            sb.AppendLine("  \"selfId\": 0,");
            sb.AppendLine("  \"autoConnect\": true,");
            sb.AppendLine("  \"receive\": " + Bool(cfg.Receive) + ",");
            sb.AppendLine("  \"prefix\": \"" + Escape(cfg.Prefix) + "\",");
            sb.AppendLine("  \"broadcast\": " + Bool(cfg.Broadcast) + ",");
            sb.AppendLine("  \"sendIntervalMs\": " + cfg.SendIntervalMs + ",");
            sb.AppendLine("  \"maxMessageLength\": " + cfg.MaxMessageLength + ",");
            sb.AppendLine("  \"dateFormat\": \"HH:mm\",");
            sb.AppendLine("  \"bridgePrefix\": \"[QQ] \",");
            sb.AppendLine("  \"gameFormat\": \"" + Escape(cfg.GameFormat) + "\",");
            sb.AppendLine("  \"qqFormat\": \"" + Escape(cfg.QqFormat) + "\",");
            sb.AppendLine("  \"groups\": [" + BuildGroupsJson(cfg.GroupsRaw) + "],");
            sb.AppendLine("  \"ignoreIds\": [" + BuildIgnoreIds(cfg.IgnoreIds) + "]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string BuildGroupsJson(string raw)
        {
            if (raw == null || raw.Trim().Length == 0)
            {
                return "";
            }
            List<string> items = new List<string>();
            foreach (string line in raw.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#"))
                {
                    continue;
                }
                string idPart = text;
                string viewersPart = "";
                int eq = text.IndexOf('=');
                if (eq > 0)
                {
                    idPart = text.Substring(0, eq).Trim();
                    viewersPart = text.Substring(eq + 1).Trim();
                }
                long id;
                if (!long.TryParse(idPart, out id) || id <= 0)
                {
                    continue;
                }
                List<string> viewers = new List<string>();
                if (viewersPart.Length > 0 && viewersPart != "*")
                {
                    foreach (string viewer in viewersPart.Split(new char[] { ',', '，', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        viewers.Add("\"" + viewer.Trim() + "\"");
                    }
                }
                items.Add("{\"id\": " + id + ", \"viewers\": [" + string.Join(", ", viewers) + "]}");
            }
            return string.Join(", ", items);
        }
        private static string BuildIgnoreIds(string raw)
        {
            if (raw == null || raw.Trim().Length == 0)
            {
                return "";
            }
            List<string> parts = new List<string>();
            foreach (string piece in raw.Split(new char[] { ',', '，', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string id = piece.Trim();
                if (id.Length > 0 && Regex.IsMatch(id, "^\\d+$"))
                {
                    parts.Add("\"" + id + "\"");
                }
            }
            return string.Join(", ", parts);
        }

        private static string Escape(string text)
        {
            if (text == null)
            {
                return "";
            }
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        private static readonly bool RoundedByDwm =
                Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!RoundedByDwm)
            {
                return;
            }
            try
            {
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, 4);
            }
            catch (Exception)
            {
            }
        }



        /* ---------- 标题栏 ---------- */

        private void BuildTitleBar()
        {
            Panel bar = new Panel();
            bar.SetBounds(0, 0, ClientSize.Width, 72);
            bar.BackColor = Theme.Bg;
            bar.MouseDown += DragWindow;
            Controls.Add(bar);

            Panel logo = new Panel();
            logo.SetBounds(24, 20, 34, 34);
            logo.BackColor = Theme.Bg;
            logo.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(0, 0, 34, 34), Theme.Accent, Theme.AccentLight, 45f))
                {
                    e.Graphics.FillEllipse(brush, 0, 0, 33, 33);
                }
                using (Font font = Theme.Ui(13f, FontStyle.Bold))
                using (SolidBrush text = new SolidBrush(Color.White))
                {
                    StringFormat format = new StringFormat();
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    e.Graphics.DrawString("Q", font, text, new RectangleF(0, -1, 33, 33), format);
                    format.Dispose();
                }
            };
            bar.Controls.Add(logo);

            GlassLabel title = new GlassLabel();
            title.Text = "QQ Chat Bridge";
            title.BackColor = Theme.Bg;
            title.Style(Theme.Ui(14f, FontStyle.Bold), Theme.TextPrimary);
            title.Location = new Point(68, 18);
            title.MouseDown += DragWindow;
            bar.Controls.Add(title);

            GlassLabel subtitle = new GlassLabel();
            subtitle.Text = "Minecraft ↔ QQ 群 · 一键启动";
            subtitle.BackColor = Theme.Bg;
            subtitle.Style(Theme.Ui(8.5f, FontStyle.Regular), Theme.TextSecondary);
            subtitle.Location = new Point(69, 45);
            subtitle.MouseDown += DragWindow;
            bar.Controls.Add(subtitle);

            CircleButton minimize = new CircleButton();
            minimize.Text = "-";
            minimize.SetBounds(ClientSize.Width - 84, 22, 30, 30);
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            bar.Controls.Add(minimize);

            CircleButton close = new CircleButton();
            close.Text = "x";
            close.Danger = true;
            close.SetBounds(ClientSize.Width - 48, 22, 30, 30);
            close.Click += delegate { Close(); };
            bar.Controls.Add(close);
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        /* ---------- 卡片 ---------- */

        private void BuildAccountCard()
        {
            cardAccount.Title = "账号";
            cardAccount.SetBounds(24, 84, 732, 262);
            Controls.Add(cardAccount);

            AddFieldLabel(cardAccount, "QQ 号", 20, 40);
            qqInput.Placeholder = "机器人小号 QQ（启动后扫码或账密登录）";
            qqInput.SetBounds(20, 62, 360, 38);
            cardAccount.Controls.Add(qqInput);

            GlassLabel hint = new GlassLabel();
            hint.Text = "Token 与端口已自动生成并写入模组配置";
            hint.BackColor = Theme.Surface;
            hint.Style(Theme.Ui(8.5f, FontStyle.Regular), Theme.TextSecondary);
            hint.Location = new Point(400, 73);
            cardAccount.Controls.Add(hint);

            AddFieldLabel(cardAccount, "QQ 群号（管理员填写，模组只读）", 20, 108);
            groupInput.ReadOnly = true;
            groupInput.Placeholder = "启动机器人后点右侧“选择群”";
            groupInput.SetBounds(20, 130, 360, 38);
            cardAccount.Controls.Add(groupInput);

            AddFieldLabel(cardAccount, "Token（只读，可复制）", 20, 176);
            tokenInput.SetBounds(20, 198, 360, 38);
            tokenInput.ReadOnly = true;
            cardAccount.Controls.Add(tokenInput);

            PillButton copyToken = new PillButton();
            copyToken.Text = "复制";
            copyToken.Variant = PillButton.Kind.Secondary;
            copyToken.BackColor = Theme.Surface;
            copyToken.SetBounds(390, 198, 72, 38);
            copyToken.Click += CopyToken;
            cardAccount.Controls.Add(copyToken);

            PillButton refreshToken = new PillButton();
            refreshToken.Text = "刷新";
            refreshToken.Variant = PillButton.Kind.Secondary;
            refreshToken.BackColor = Theme.Surface;
            refreshToken.SetBounds(470, 198, 72, 38);
            refreshToken.Click += RefreshToken;
            cardAccount.Controls.Add(refreshToken);

            PillButton checkGroup = new PillButton();
            checkGroup.Text = "选择群";
            checkGroup.Variant = PillButton.Kind.Secondary;
            checkGroup.BackColor = Theme.Surface;
            checkGroup.SetBounds(390, 130, 100, 38);
            checkGroup.Click += delegate { PickGroup(); };
            cardAccount.Controls.Add(checkGroup);
        }

        private void RefreshToken(object sender, EventArgs e)
        {
            bool wasRunning = napcatProcess != null && !napcatProcess.HasExited;
            config.Token = LauncherConfig.RandomToken();
            config.Save();
            tokenInput.TextValue = config.Token;
            SyncAll(config, Log);
            if (wasRunning && config.Qq.Length > 0)
            {
                Log("正在重启 NapCat 以应用新 Token…");
                StopBot();
                StartBot();
            }
            else
            {
                Log("Token 已刷新并写入 NapCat / 模组配置；启动机器人和游戏后生效");
            }
        }

        private void PickGroup()
        {
            SaveConfig();
            if (config.Qq.Length == 0)
            {
                MessageBox.Show("请先填写 QQ 号。", "提示");
                return;
            }
            List<GroupInfo> groups;
            Cursor = Cursors.WaitCursor;
            try
            {
                groups = FetchGroups();
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show("获取群列表失败：\n" + ex.Message
                        + "\n\n请先启动机器人并完成 QQ 登录，再点“选择群”。", "提示");
                return;
            }
            Cursor = Cursors.Default;
            if (groups.Count == 0)
            {
                MessageBox.Show("机器人当前不在任何群里。\n请先把机器人 QQ 号拉进目标群。", "提示");
                return;
            }
            using (GroupPickerForm picker = new GroupPickerForm(groups, config.GroupId))
            {
                if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected == null)
                {
                    return;
                }
                config.GroupId = picker.Selected.Id;
                config.Save();
                groupInput.TextValue = picker.Selected.Label;
                SyncAll(config, Log);
                Log("已选择群：" + picker.Selected.Label + "，已写入模组配置");
            }
        }

        private List<GroupInfo> FetchGroups()
        {
            string url = "http://127.0.0.1:" + config.HttpPort + "/get_group_list";
            string body = HttpPostJson(url, config.Token, "{}");
            List<GroupInfo> list = new List<GroupInfo>();
            foreach (Match match in Regex.Matches(body,
                    "\"group_id\"\\s*:\\s*(\\d+)[^}]*?\"group_name\"\\s*:\\s*\"([^\"]*)\""))
            {
                list.Add(new GroupInfo(long.Parse(match.Groups[1].Value), match.Groups[2].Value));
            }
            return list;
        }

        private void StartGroupCheck()
        {
            if (groupCheckTimer == null)
            {
                groupCheckTimer = new Timer();
                groupCheckTimer.Interval = 2500;
                groupCheckTimer.Tick += delegate
                {
                    groupCheckTries++;
                    if (groupCheckTries > 12)
                    {
                        groupCheckTimer.Stop();
                        Log("群列表查询超时：NapCat 可能还没登录，登录后再点“选择群”。");
                        return;
                    }
                    System.Threading.Thread thread = new System.Threading.Thread(delegate()
                    {
                        try
                        {
                            ListGroups(false);
                        }
                        catch (Exception)
                        {
                        }
                    });
                    thread.IsBackground = true;
                    thread.Start();
                };
            }
            groupCheckTries = 0;
            autoPickShown = false;
            groupCheckTimer.Start();
        }

        private static string HttpPostJson(string url, string token, string json)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = 4000;
            request.ReadWriteTimeout = 4000;
            if (token != null && token.Length > 0)
            {
                request.Headers["Authorization"] = "Bearer " + token;
            }
            byte[] data = Encoding.UTF8.GetBytes(json);
            request.ContentLength = data.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private void ListGroups(bool manual)
        {
            string url = "http://127.0.0.1:" + config.HttpPort + "/get_group_list";
            string body = HttpPostJson(url, config.Token, "{}");
            MatchCollection groups = Regex.Matches(body,
                "\"group_id\"\\s*:\\s*(\\d+)[^}]*?\"group_name\"\\s*:\\s*\"([^\"]*)\"");
            if (groups.Count == 0)
            {
                if (manual)
                {
                    Log("NapCat 返回里没有群：机器人还没加入任何群。");
                }
                return;
            }
            Log("机器人当前在 " + groups.Count + " 个群：");
            bool found = false;
            string foundName = "";
            foreach (Match match in groups)
            {
                long gid = long.Parse(match.Groups[1].Value);
                string name = match.Groups[2].Value;
                Log("  " + gid + "  " + name + (gid == config.GroupId ? "  ← 已绑定" : ""));
                if (gid == config.GroupId)
                {
                    found = true;
                    foundName = name;
                }
            }
            if (found)
            {
                Log("群号 " + config.GroupId + " 检查通过");
                string label = foundName + "（" + config.GroupId + "）";
                try { BeginInvoke(new Action(delegate { groupInput.TextValue = label; })); } catch (Exception) { }
            }
            else if (config.GroupId != 0L)
            {
                Log("群号 " + config.GroupId + " 不在列表：机器人没加这个群，或登录账号不是填的 QQ");
            }
            else
            {
                Log("还没有选择群：点“选择群”从上面的列表里挑一个");
                if (!autoPickShown)
                {
                    autoPickShown = true;
                    try { BeginInvoke(new Action(delegate { PickGroup(); })); } catch (Exception) { }
                }
            }
            if (groupCheckTimer != null)
            {
                try
                {
                    BeginInvoke(new Action(delegate { groupCheckTimer.Stop(); }));
                }
                catch (Exception)
                {
                }
            }
        }

        private void CopyToken(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(config.Token);
                Log("Token 已复制到剪贴板");
            }
            catch (Exception ex)
            {
                Log("复制 Token 失败：" + ex.Message);
            }
        }

        private void BuildPathCard()
        {
            cardPath.Title = "路径";
            cardPath.SetBounds(24, 358, 732, 112);
            Controls.Add(cardPath);

            AddFieldLabel(cardPath, "QQ.exe", 20, 40);
            qqExeInput.Placeholder = "自动检测，找不到时手动选择";
            qqExeInput.SetBounds(20, 62, 540, 38);
            cardPath.Controls.Add(qqExeInput);

            PillButton autoFind = new PillButton();
            autoFind.Text = "自动查找";
            autoFind.Variant = PillButton.Kind.Secondary;
            autoFind.BackColor = Theme.Surface;
            autoFind.SetBounds(572, 62, 92, 38);
            autoFind.Click += AutoFindQq;
            cardPath.Controls.Add(autoFind);
        }

        private void AddFieldLabel(Control parent, string text, int x, int y)
        {
            GlassLabel label = new GlassLabel();
            label.Text = text;
            label.BackColor = Theme.Surface;
            label.Style(Theme.Ui(8.5f, FontStyle.Regular), Theme.TextSecondary);
            label.Location = new Point(x, y);
            parent.Controls.Add(label);
        }

        /* ---------- 操作按钮 ---------- */

        private void BuildActions()
        {
            startButton.Text = "启动机器人";
            startButton.Variant = PillButton.Kind.Primary;
            startButton.SetBounds(24, 486, 170, 44);
            startButton.Click += delegate { StartBot(); };
            Controls.Add(startButton);

            stopButton.Text = "停止";
            stopButton.Variant = PillButton.Kind.Danger;
            stopButton.SetBounds(206, 486, 110, 44);
            stopButton.Enabled = false;
            stopButton.Click += delegate { StopBot(); };
            Controls.Add(stopButton);

            PillButton webui = new PillButton();
            webui.Text = "打开 WebUI";
            webui.SetBounds(328, 486, 130, 44);
            webui.Click += OpenWebUi;
            Controls.Add(webui);

            PillButton install = new PillButton();
            install.Text = "安装模组";
            install.SetBounds(470, 486, 130, 44);
            install.Click += InstallMod;
            Controls.Add(install);

            PillButton check = new PillButton();
            check.Text = "检查环境";
            check.SetBounds(612, 486, 120, 44);
            check.Click += delegate { CheckEnvironment(); };
            Controls.Add(check);

            PillButton save = new PillButton();
            save.Text = "保存配置";
            save.Variant = PillButton.Kind.Secondary;
            save.SetBounds(24, 542, 120, 38);
            save.Click += delegate { SaveConfig(); SyncAll(config, Log); Log("配置已保存并同步到 NapCat / 模组"); };

            PillButton admin = new PillButton();
            admin.Text = "管理员设置";
            admin.Variant = PillButton.Kind.Secondary;
            admin.SetBounds(276, 542, 130, 38);
            admin.Click += delegate { OpenAdminSettings(); };
            Controls.Add(admin);
            Controls.Add(save);

            PillButton clearLog = new PillButton();
            clearLog.Text = "清空日志";
            clearLog.Variant = PillButton.Kind.Secondary;
            clearLog.SetBounds(156, 542, 110, 38);
            clearLog.Click += delegate { logBox.Clear(); };
            Controls.Add(clearLog);

            statusChip.SetBounds(24, 592, 240, 30);
            Controls.Add(statusChip);
        }

        /* ---------- 日志 ---------- */

        private void BuildLogCard()
        {
            cardLog.Title = "运行日志";
            cardLog.BackColor = Theme.Bg;
            cardLog.FillColor = Theme.LogBack;
            cardLog.BorderColor = Color.FromArgb(70, 255, 255, 255);
            cardLog.TitleColor = Color.FromArgb(235, 235, 245);
            cardLog.ShowHighlight = false;
            cardLog.SetBounds(24, 634, 732, 222);
            Controls.Add(cardLog);

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.None;
            logBox.BackColor = Theme.LogBack;
            logBox.ForeColor = Theme.LogText;
            logBox.Font = Theme.Mono(9f);
            logBox.SetBounds(14, 40, cardLog.Width - 28, cardLog.Height - 54);
            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            cardLog.Controls.Add(logBox);

            PillButton consoleButton = new PillButton();
            consoleButton.Text = "CMD";
            consoleButton.Variant = PillButton.Kind.Secondary;
            consoleButton.BackColor = Theme.LogBack;
            consoleButton.Font = Theme.Ui(8.5f, FontStyle.Bold);
            consoleButton.SetBounds(cardLog.Width - 78, 9, 62, 26);
            consoleButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            consoleButton.Click += OpenConsole;
            cardLog.Controls.Add(consoleButton);

            ToolTip toolTip = new ToolTip();
            toolTip.SetToolTip(consoleButton, "打开完整的 CMD 实时日志窗口（同时写入 logs 文件）");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush brush = new SolidBrush(Theme.Bg))
            {
                g.FillRectangle(brush, 0, 0, Width, Height);
            }
            using (Pen border = new Pen(Color.FromArgb(45, 120, 145, 180), 1f))
            {
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }

            base.OnPaint(e);
        }

        /* ---------- 数据 ---------- */

        private void LoadValues()
        {
            qqInput.TextValue = config.Qq;
            tokenInput.TextValue = config.Token;
            groupInput.TextValue = config.GroupId == 0L
                    ? "未选择，点右侧“选择群”"
                    : ("已选群号 " + config.GroupId + "，登录后自动带出群名");
            qqExeInput.TextValue = config.QqExe;
        }

        private void SaveConfig()
        {
            config.Qq = qqInput.TextValue.Trim();
            config.QqExe = qqExeInput.TextValue.Trim();
            string napcat = ResolveNapCatDir();
            if (napcat != null)
            {
                config.NapCatDir = napcat;
            }
            config.Save();
        }

        /// <summary>自动在启动器目录（含一级子目录）里找 NapCat，不再让用户手选。</summary>
        private static string ResolveNapCatDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            List<string> candidates = new List<string>();
            candidates.Add(baseDir);
            candidates.Add(Path.Combine(baseDir, "napcat"));
            try
            {
                foreach (string dir in Directory.GetDirectories(baseDir))
                {
                    candidates.Add(dir);
                    candidates.Add(Path.Combine(dir, "napcat"));
                }
            }
            catch (Exception)
            {
            }
            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "napcat.mjs")) &&
                    File.Exists(Path.Combine(candidate, "NapCatWinBootMain.exe")))
                {
                    return candidate;
                }
            }
            return null;
        }

        private void AutoFindQq(object sender, EventArgs e)
        {
            string path = FindQqExe();
            if (path == null)
            {
                Log("没有自动找到 QQ.exe，请手动选择。");
                MessageBox.Show("没有找到 QQ，请确认已安装 QQ NT，并手动选择 QQ.exe。", "提示");
            }
            else
            {
                qqExeInput.TextValue = path;
                Log("找到 QQ：" + path);
            }
        }

        private static string FindQqExe()
        {
            string[] subKeys = new string[]
            {
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\QQ",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QQ"
            };
            RegistryKey[] roots = new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser };
            foreach (RegistryKey root in roots)
            {
                foreach (string sub in subKeys)
                {
                    try
                    {
                        using (RegistryKey key = root.OpenSubKey(sub))
                        {
                            if (key == null) continue;
                            string uninstall = key.GetValue("UninstallString") as string;
                            if (string.IsNullOrEmpty(uninstall)) continue;
                            string dir = Path.GetDirectoryName(uninstall.Trim('"'));
                            if (dir == null) continue;
                            string exe = Path.Combine(dir, "QQ.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            string local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Tencent\QQNT\QQ.exe");
            if (File.Exists(local)) return local;
            string programFiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Tencent\QQNT\QQ.exe");
            if (File.Exists(programFiles)) return programFiles;
            string programFilesX86 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"Tencent\QQNT\QQ.exe");
            if (File.Exists(programFilesX86)) return programFilesX86;
            return null;
        }

        /* ---------- 行为 ---------- */

        private void StartBot()
        {
            SaveConfig();
            if (config.Qq.Length == 0)
            {
                MessageBox.Show("请先填写 QQ 号。", "提示");
                return;
            }
            if (config.GroupId == 0L)
            {
                Log("还没选群：启动并登录成功后，点“选择群”从机器人所在群里挑一个即可。");
            }
            // 私用构建：启动器默认固定走内置中转，保证与 mod 里写死的地址/Token 一致
            if (config.RelayUrl.Trim().Length == 0)
            {
                config.RelayUrl = "";
            }
            if (config.RelayToken.Trim().Length == 0)
            {
                config.RelayToken = LauncherConfig.RandomToken() + LauncherConfig.RandomToken();
            }
            config.Save();
            SyncAll(config, Log);
            StartRelayIfNeeded();
            string napcat = ResolveNapCatDir();
            if (napcat == null)
            {
                MessageBox.Show("没有找到 NapCat。请确认启动器所在文件夹里有 napcat 子目录（包含 napcat.mjs）。", "提示");
                return;
            }
            config.NapCatDir = napcat;
            Log("NapCat 目录：" + napcat);
            string qqExe = config.QqExe;
            if (qqExe.Length == 0 || !File.Exists(qqExe))
            {
                qqExe = FindQqExe();
            }
            if (qqExe == null || !File.Exists(qqExe))
            {
                MessageBox.Show("没有找到 QQ.exe，请先安装 QQ NT，或手动选择。", "提示");
                return;
            }
            qqExeInput.TextValue = qqExe;

            try
            {
                string configPath = LauncherConfig.WriteOneBotConfig(
                    napcat, config.Qq, config.Token, config.HttpPort, config.WsPort);
                Log("已写入 OneBot 配置：" + configPath);
                Log("注意：NapCat 按登录 QQ 号读取配置，登录的账号必须与上面填的 QQ 号一致");
            }
            catch (Exception ex)
            {
                MessageBox.Show("写入 NapCat 配置失败：" + ex.Message, "错误");
                return;
            }

            string hook = Path.Combine(napcat, "NapCatWinBootHook.dll");
            string launcherExe = Path.Combine(napcat, "NapCatWinBootMain.exe");

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = launcherExe;
            info.Arguments = "\"" + qqExe + "\" \"" + hook + "\" -q " + config.Qq;
            info.WorkingDirectory = napcat;
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.CreateNoWindow = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            info.EnvironmentVariables["NAPCAT_PATCH_PACKAGE"] = Path.Combine(napcat, "qqnt.json");
            info.EnvironmentVariables["NAPCAT_LOAD_PATH"] = Path.Combine(napcat, "loadNapCat.js");
            info.EnvironmentVariables["NAPCAT_INJECT_PATH"] = hook;
            info.EnvironmentVariables["NAPCAT_LAUNCHER_PATH"] = launcherExe;
            info.EnvironmentVariables["NAPCAT_MAIN_PATH"] = Path.Combine(napcat, "napcat.mjs");
            info.EnvironmentVariables["NAPCAT_WORKDIR"] = napcat;
            info.EnvironmentVariables["NAPCAT_WEBUI_SECRET_KEY"] = config.Token;

            try
            {
                napcatProcess = new Process();
                napcatProcess.StartInfo = info;
                napcatProcess.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data)) Log(e.Data);
                };
                napcatProcess.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data)) Log("[ERR] " + e.Data);
                };
                napcatProcess.EnableRaisingEvents = true;
                napcatProcess.Exited += delegate { Log("[launcher] NapCat 进程已退出"); };
                napcatProcess.Start();
                napcatProcess.BeginOutputReadLine();
                napcatProcess.BeginErrorReadLine();

                startButton.Enabled = false;
                stopButton.Enabled = true;
                statusChip.SetStatus("运行中（QQ " + config.Qq + "）", Theme.Success);
                Log("已启动 NapCat，等待二维码/自动登录…");
                Log("提示：首次登录会输出二维码，也可以打开 http://127.0.0.1:6099 扫码。");
                if (config.GroupId == 0L)
                {
                    Log("登录成功后点“选择群”，从机器人所在群里挑一个即可。");
                }
                StartGroupCheck();
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败：" + ex.Message, "错误");
                Log("[错误] " + ex);
            }
        }

        private void StartRelayIfNeeded()
        {
            if (config.RelayUrl == null || config.RelayUrl.Trim().Length == 0)
            {
                return;
            }
            string relayDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "relay");
            string exe = Path.Combine(relayDir, "QQRelay.exe");
            if (!File.Exists(exe))
            {
                exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QQRelay.exe");
                relayDir = AppDomain.CurrentDomain.BaseDirectory;
            }
            if (!File.Exists(exe))
            {
                MessageBox.Show("启用了服务器中转，但找不到 relay\\QQRelay.exe。\n请把 relay 文件夹放到启动器目录。", "提示");
                return;
            }

            int externPort = ParseRelayPort();
            int port = 18574;
            StringBuilder ini = new StringBuilder();
            ini.AppendLine("listenHost=+");
            ini.AppendLine("listenPort=" + port);
            ini.AppendLine("napcatWs=ws://127.0.0.1:" + config.WsPort);
            ini.AppendLine("napcatHttp=http://127.0.0.1:" + config.HttpPort);
            ini.AppendLine("token=" + config.RelayToken);
            ini.AppendLine("napcatToken=" + config.Token);
            ini.AppendLine("allowIps=");
            ini.AppendLine("maxClients=64");
            ini.AppendLine("ratePerMinute=120");
            ini.AppendLine("logFile=relay.log");
            File.WriteAllText(Path.Combine(relayDir, "relay.ini"), ini.ToString(), new UTF8Encoding(false));

            // 下发给玩家端的共享配置
            File.WriteAllText(Path.Combine(relayDir, "client-config.json"),
                    BuildModConfigJson(config), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(relayDir, "client-bootstrap.json"),
                    BuildBootstrapJson(config), new UTF8Encoding(false));
            Log("已写入下发配置：client-config.json / client-bootstrap.json");

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(exe);
                info.WorkingDirectory = relayDir;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardErrorEncoding = Encoding.UTF8;
                relayProcess = new Process();
                relayProcess.StartInfo = info;
                relayProcess.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data)) Log("[relay] " + e.Data);
                };
                relayProcess.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data)) Log("[relay] " + e.Data);
                };
                relayProcess.Start();
                relayProcess.BeginOutputReadLine();
                relayProcess.BeginErrorReadLine();
                Log("中转已启动：监听 0.0.0.0:" + port + "，公网请映射 " + externPort + " → " + port);
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动中转失败：" + ex.Message, "错误");
            }
        }

        private int ParseRelayPort()
        {
            string url = config.RelayUrl.Trim();
            int colon = url.LastIndexOf(':');
            int port;
            if (colon > 0 && int.TryParse(url.Substring(colon + 1), out port) && port > 0 && port < 65536)
            {
                return port;
            }
            return 18574;
        }

        private void StopRelay()
        {
            try
            {
                if (relayProcess != null && !relayProcess.HasExited)
                {
                    Process.Start(new ProcessStartInfo("taskkill",
                        "/PID " + relayProcess.Id + " /T /F")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    relayProcess.WaitForExit(2000);
                }
            }
            catch (Exception)
            {
            }
            relayProcess = null;
        }
        private void StopBot()
        {
            try
            {
                if (napcatProcess != null && !napcatProcess.HasExited)
                {
                    Process.Start(new ProcessStartInfo("taskkill",
                        "/PID " + napcatProcess.Id + " /T /F")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    napcatProcess.WaitForExit(3000);
                }
            }
            catch (Exception)
            {
            }
            napcatProcess = null;
            StopRelay();
            if (groupCheckTimer != null) { groupCheckTimer.Stop(); }
            StopConsole();
            startButton.Enabled = true;
            stopButton.Enabled = false;
            statusChip.SetStatus("未启动", Theme.TextSecondary);
        }

        private void StopConsole()
        {
            try
            {
                if (consoleProcess != null && !consoleProcess.HasExited)
                {
                    Process.Start(new ProcessStartInfo("taskkill",
                        "/PID " + consoleProcess.Id + " /T /F")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    consoleProcess.WaitForExit(2000);
                }
            }
            catch (Exception)
            {
            }
            consoleProcess = null;
        }

        private void OpenWebUi(object sender, EventArgs e)
        {
            try
            {
                Process.Start("http://127.0.0.1:6099");
            }
            catch (Exception ex)
            {
                Log("打开 WebUI 失败：" + ex.Message);
            }
        }

        private void OpenAdminSettings()
        {
            using (AdminForm form = new AdminForm(config, delegate
            {
                SyncAll(config, Log);
                Log("管理员设置已保存并同步到模组配置");
            }))
            {
                form.ShowDialog(this);
            }
        }
        private void InstallMod(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "选择 Minecraft 的 mods 目录（例如 .minecraft/mods）";
            if (dialog.ShowDialog() != DialogResult.OK) return;

            string modsDir = dialog.SelectedPath;
            string packageMods = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mods");
            string[] jars = Directory.Exists(packageMods) ? Directory.GetFiles(packageMods, "*.jar") : new string[0];
            if (jars.Length == 0)
            {
                MessageBox.Show("启动器目录下没有找到 mods\\*.jar。", "提示");
                return;
            }
            foreach (string jar in jars)
            {
                string target = Path.Combine(modsDir, Path.GetFileName(jar));
                File.Copy(jar, target, true);
                Log("已安装模组：" + target);
            }

            string configSource = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            DirectoryInfo gameDir = Directory.GetParent(modsDir);
            if (gameDir != null)
            {
                if (Directory.Exists(configSource))
                {
                    string configTarget = Path.Combine(gameDir.FullName, "config");
                    CopyDirectory(configSource, configTarget);
                    Log("已写入配置文件：" + configTarget);
                }
                SyncModConfig(config, gameDir.FullName, Log);
                config.GameDir = gameDir.FullName;
                config.Save();
            }

            MessageBox.Show("已复制 " + jars.Length + " 个模组到：\n" + modsDir
                + "\n\n图片内嵌显示需要 converse + YACL（已一并复制），"
                + "首次启动后可在 ModMenu 里微调。", "完成");
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                string targetFile = Path.Combine(target, Path.GetFileName(file));
                if (!File.Exists(targetFile))
                {
                    File.Copy(file, targetFile);
                }
            }
            foreach (string dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        private void CheckEnvironment()
        {
            string qq = config.QqExe.Length > 0 && File.Exists(config.QqExe) ? config.QqExe : FindQqExe();
            Log("=== 环境检查 ===");
            string napcatCheck = ResolveNapCatDir();
            Log("NapCat：" + (napcatCheck != null ? "OK（" + napcatCheck + "）" : "缺失（应放在启动器目录的 napcat 文件夹）"));
            Log("QQ：" + (qq != null ? qq : "未找到（请安装 QQ NT）"));
            Log("HTTP 端口 " + config.HttpPort + "：" + (PortFree(config.HttpPort) ? "可用" : "被占用"));
            Log("WS 端口 " + config.WsPort + "：" + (PortFree(config.WsPort) ? "可用" : "被占用"));
        }

        private static bool PortFree(int port)
        {
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void InitializeLogFile()
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                logPath = Path.Combine(dir, "launcher-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                logWriter = new StreamWriter(logPath, true, new UTF8Encoding(true));
                logWriter.AutoFlush = true;
                logWriter.WriteLine("==== QQ Chat Bridge 启动器 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
            }
            catch (Exception)
            {
                logWriter = null;
            }
        }

        private void OpenConsole(object sender, EventArgs e)
        {
            try
            {
                if (logPath == null || !File.Exists(logPath))
                {
                    Log("日志文件尚未创建");
                    return;
                }
                string safePath = logPath.Replace("'", "''");
                ProcessStartInfo info = new ProcessStartInfo("cmd.exe");
                info.UseShellExecute = true;
                info.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                info.WindowStyle = ProcessWindowStyle.Normal;
                string ps = "$host.UI.RawUI.WindowTitle='QQ Chat Bridge 实时日志'; "
                        + "Get-Content -LiteralPath '" + safePath + "' -Wait -Tail 300 -Encoding UTF8";
                info.Arguments = "/k powershell -NoExit -NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"";
                if (consoleProcess != null && !consoleProcess.HasExited)
                {
                    try { SetForegroundWindow(consoleProcess.MainWindowHandle); } catch (Exception) { }
                    Log("CMD 实时日志窗口已在运行");
                    return;
                }
                consoleProcess = Process.Start(info);
                Log("已打开 CMD 实时日志窗口（文件：" + logPath + "）");
            }
            catch (Exception ex)
            {
                Log("打开 CMD 失败：" + ex.Message);
            }
        }

        private void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            if (logBox.InvokeRequired)
            {
                logBox.BeginInvoke(new Action<string>(Log), message);
                return;
            }
            logBox.AppendText(line + Environment.NewLine);
            lock (logLock)
            {
                if (logWriter != null)
                {
                    try
                    {
                        logWriter.WriteLine(line);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
    }
}
