using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Bump these together when cutting a release; release.ps1 names the zip after the informational version.
[assembly: AssemblyTitle("D2R Terror Zone Clock Setter")]
[assembly: AssemblyProduct("D2R Terror Zone Clock")]
[assembly: AssemblyDescription("Sets the Windows clock to a Diablo II: Resurrected terror zone session.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

namespace CalendarTimeTool
{
    internal static class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SystemTime
        {
            public ushort Year;
            public ushort Month;
            public ushort DayOfWeek;
            public ushort Day;
            public ushort Hour;
            public ushort Minute;
            public ushort Second;
            public ushort Milliseconds;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetLocalTime(ref SystemTime systemTime);

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folderId, uint flags, IntPtr token, out IntPtr path);

        private const string DataUrl = "https://d2emu.com/data/tz-2023-localized.json";
        private static readonly Guid SavedGamesFolderId = new Guid("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4");

        // Zone order as listed on d2emu.com/tz-sp
        private static readonly string[] ZoneNames =
        {
            "Blood Moor, Den of Evil",
            "Burial Grounds, Crypt, Mausoleum",
            "Stony Field, Tristram",
            "Cold Plains, Cave",
            "Dark Wood, Underground Passage",
            "Black Marsh, The Hole, Forgotten Tower",
            "Tamoe Highland, Outer Cloister, Pit, Monastery Gate",
            "Barracks, Jail",
            "Cathedral, Catacombs, Inner Cloister",
            "The Secret Cow Level",
            "Lut Gholein Sewers",
            "Rocky Waste, Stony Tomb",
            "Dry Hills, Halls of the Dead",
            "Far Oasis, Maggot Lair",
            "Lost City, Valley of Snakes, Claw Viper Temple, Ancient Tunnels",
            "Arcane Sanctuary, Harem, Palace Cellar",
            "Tal Rasha's Tombs, Tal Rasha's Chamber, Canyon of the Magi",
            "Spider Forest, Arachnid Lair, Spider Cavern",
            "Great Marsh",
            "Flayer Jungle, Flayer Dungeon, Swampy Pit",
            "Kurast Bazaar, Lower Kurast, Upper Kurast, Kurast Causeway, Kurast Sewers, Ruined Temple, Disused Fane, Forgotten Reliquary, Forgotten Temple, Ruined Fane, Disused Reliquary",
            "Travincal",
            "Durance of Hate",
            "Outer Steppes, Plains of Despair",
            "City of the Damned, River of Flame",
            "Chaos Sanctuary",
            "Bloody Foothills, Frigid Highlands, Abaddon",
            "Frozen Tundra, Infernal Pit",
            "Arreat Plateau, Pit of Acheron",
            "Crystalline Passage, Frozen River",
            "Nihlathak's Temple, Temple Halls",
            "Glacial Trail, Drifter Cavern",
            "Ancient's Way, Icy Cellar",
            "Worldstone Keep, Throne of Destruction, Worldstone Chamber"
        };

        private sealed class Session
        {
            public DateTime StartUtc;
            public string Zone;
        }

        private sealed class ZoneItem
        {
            public string Name;
            public bool IsFavourite;

            public override string ToString()
            {
                return (IsFavourite ? "★ " : "") + Name;
            }
        }

        private static DateTime GetActualUtcTime()
        {
            using (var client = new UdpClient())
            {
                client.Client.ReceiveTimeout = 3000;
                client.Connect("time.windows.com", 123);
                var request = new byte[48];
                request[0] = 0x1B;
                client.Send(request, request.Length);
                var endpoint = new IPEndPoint(IPAddress.Any, 0);
                var response = client.Receive(ref endpoint);
                ulong seconds = ((ulong)response[40] << 24) | ((ulong)response[41] << 16) |
                                 ((ulong)response[42] << 8) | response[43];
                ulong fraction = ((ulong)response[44] << 24) | ((ulong)response[45] << 16) |
                                 ((ulong)response[46] << 8) | response[47];
                return new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(seconds)
                    .AddTicks((long)(fraction * TimeSpan.TicksPerSecond / 0x100000000L));
            }
        }

        // Returns 0 on success, otherwise the Win32 error code.
        private static int SetClock(DateTime localTime)
        {
            var systemTime = new SystemTime
            {
                Year = (ushort)localTime.Year,
                Month = (ushort)localTime.Month,
                Day = (ushort)localTime.Day,
                Hour = (ushort)localTime.Hour,
                Minute = (ushort)localTime.Minute,
                Second = (ushort)localTime.Second,
                Milliseconds = (ushort)localTime.Millisecond
            };
            return SetLocalTime(ref systemTime) ? 0 : Marshal.GetLastWin32Error();
        }

        private static List<Session> LoadSessions(string cachePath, out bool fromCache)
        {
            string json;
            fromCache = false;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.UserAgent] = "D2RTerrorZoneClock";
                    json = client.DownloadString(DataUrl);
                }
                try { File.WriteAllText(cachePath, json, Encoding.UTF8); }
                catch (IOException) { }
            }
            catch (Exception)
            {
                if (!File.Exists(cachePath))
                    throw;
                json = File.ReadAllText(cachePath, Encoding.UTF8);
                fromCache = true;
            }

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var raw = serializer.Deserialize<List<Dictionary<string, object>>>(json);
            var sessions = new List<Session>(raw.Count);
            foreach (var entry in raw)
            {
                object datetime, zone;
                if (!entry.TryGetValue("datetime", out datetime) || !entry.TryGetValue("zone", out zone))
                    continue;
                var names = zone as Dictionary<string, object>;
                object name;
                if (names == null || !names.TryGetValue("enUS", out name))
                    continue;
                sessions.Add(new Session
                {
                    StartUtc = DateTimeOffset.Parse((string)datetime, CultureInfo.InvariantCulture).UtcDateTime,
                    Zone = (string)name
                });
            }
            sessions.Sort(delegate(Session a, Session b) { return a.StartUtc.CompareTo(b.StartUtc); });
            return sessions;
        }

        // D2R keeps single-player characters in Saved Games\Diablo II Resurrected.
        private static string SavedGamesFolder()
        {
            var path = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(SavedGamesFolderId, 0, IntPtr.Zero, out path) == 0)
                    return Marshal.PtrToStringUni(path);
            }
            finally
            {
                Marshal.FreeCoTaskMem(path);
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
        }

        // Zips the D2R save folder into "Diablo II Resurrected Backups" beside it and returns the zip's path.
        private static string BackupSaves(DateTime stamp, out int fileCount)
        {
            var savedGames = SavedGamesFolder();
            var saveFolder = Path.Combine(savedGames, "Diablo II Resurrected");
            if (!Directory.Exists(saveFolder))
                throw new DirectoryNotFoundException("No D2R save folder found at " + saveFolder + ".");

            var backupFolder = Path.Combine(savedGames, "Diablo II Resurrected Backups");
            Directory.CreateDirectory(backupFolder);
            var zipPath = Path.Combine(backupFolder, "D2R saves " + stamp.ToString("yyyy-MM-dd HH-mm-ss") + ".zip");

            fileCount = 0;
            var zipStream = new FileStream(zipPath, FileMode.CreateNew);
            try
            {
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    foreach (var file in Directory.GetFiles(saveFolder, "*", SearchOption.AllDirectories))
                    {
                        var entry = archive.CreateEntry(file.Substring(saveFolder.Length + 1).Replace('\\', '/'));
                        entry.LastWriteTime = File.GetLastWriteTime(file);
                        // The game may have a save open, so read without blocking it.
                        using (var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var target = entry.Open())
                            source.CopyTo(target);
                        fileCount++;
                    }
                }
            }
            catch (Exception)
            {
                zipStream.Dispose();
                try { File.Delete(zipPath); }
                catch (IOException) { }
                throw;
            }
            return zipPath;
        }

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private sealed class MainForm : Form
        {
            private static readonly Color TextColor = Color.FromArgb(235, 220, 190);
            private static readonly Color MutedColor = Color.FromArgb(170, 150, 120);
            private static readonly Color ActiveColor = Color.FromArgb(255, 170, 90);
            private static readonly Color GoldColor = Color.FromArgb(247, 205, 120);

            // art.png and frame.png are 1280x720; the window shows them at 80%.
            private static readonly Size WindowSize = new Size(1024, 576);
            private static readonly Rectangle PanelBounds = new Rectangle(522, 122, 440, 400);
            private const int GoblinArtX = 680;     // the goblin's centre in art.png
            private const int GoblinWindowX = 290;  // where that centre lands in the window

            private readonly ComboBox zoneSelector = new ComboBox();
            private readonly Button favouriteButton = new Button();
            private readonly Label sessionCaption = new Label();
            private readonly Label sessionTime = new Label();
            private readonly Label sessionRelative = new Label();
            private readonly Button setButton = new Button();
            private readonly Button revertButton = new Button();
            private readonly CheckBox autoRevertCheck = new CheckBox();
            private readonly Button backupButton = new Button();
            private readonly Label result = new Label();
            private readonly ToolTip toolTip = new ToolTip();
            private readonly Timer refreshTimer = new Timer();

            private readonly string dataFolder;
            private readonly string settingsPath;
            private readonly HashSet<string> favourites = new HashSet<string>();
            private List<Session> sessions = new List<Session>();
            private TimeSpan sessionLength = TimeSpan.FromMinutes(30);
            private Session shownSession;
            private bool savesBackedUp;

            // Real time minus system time. Non-zero while this app has moved the clock.
            private TimeSpan clockOffset = TimeSpan.Zero;
            private bool rebuildingZones;
            private bool loadingSettings;

            public MainForm()
            {
                dataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "D2RTerrorZoneClock");
                Directory.CreateDirectory(dataFolder);
                settingsPath = Path.Combine(dataFolder, "settings.txt");

                Text = "D2R Terror Zone Clock";
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = false;
                ClientSize = WindowSize;
                StartPosition = FormStartPosition.CenterScreen;
                BackColor = Color.FromArgb(20, 8, 10);
                ForeColor = TextColor;
                Font = new Font("Segoe UI", 9.75F, FontStyle.Regular);
                DoubleBuffered = true;
                BackgroundImage = ComposeBackground();
                BackgroundImageLayout = ImageLayout.None;

                // The goblin icon is compiled into the exe (see build.ps1); reuse it for the window.
                try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch (ArgumentException) { }

                var left = PanelBounds.Left + 20;
                var width = PanelBounds.Width - 40;
                var captionFont = new Font("Segoe UI", 8F, FontStyle.Bold);

                // Zone picker and favourite toggle
                PlaceLabel(new Label { Text = "TERROR ZONE" }, left, 140, width, 18, captionFont, MutedColor);
                zoneSelector.DropDownStyle = ComboBoxStyle.DropDownList;
                zoneSelector.FlatStyle = FlatStyle.Flat;
                zoneSelector.SetBounds(left, 160, width - 40, 26);
                zoneSelector.BackColor = Color.FromArgb(50, 39, 37);
                zoneSelector.ForeColor = Color.FromArgb(245, 226, 190);
                zoneSelector.DropDownWidth = 560;
                zoneSelector.MaxDropDownItems = 20;
                zoneSelector.DrawMode = DrawMode.OwnerDrawFixed;
                zoneSelector.ItemHeight = 20;
                zoneSelector.DrawItem += ZoneSelector_DrawItem;
                zoneSelector.SelectedIndexChanged += ZoneSelector_SelectedIndexChanged;
                Controls.Add(zoneSelector);

                favouriteButton.SetBounds(left + width - 32, 159, 32, 28);
                StyleButton(favouriteButton, Color.FromArgb(63, 55, 48));
                favouriteButton.Padding = Padding.Empty;
                favouriteButton.Font = new Font("Segoe UI", 11F);
                favouriteButton.Click += FavouriteButton_Click;
                Controls.Add(favouriteButton);

                // The one session the clock will be set to
                PlaceLabel(sessionCaption, left, 206, width, 18, captionFont, MutedColor);
                PlaceLabel(sessionTime, left, 224, width, 34, new Font("Georgia", 17F, FontStyle.Bold), Color.FromArgb(247, 224, 175));
                PlaceLabel(sessionRelative, left, 260, width, 22, new Font("Segoe UI", 10F), TextColor);
                sessionTime.Text = "Loading...";

                // Clock actions
                setButton.Text = "Set clock to this session";
                setButton.AutoSize = true;
                setButton.Location = new Point(left, 298);
                setButton.Enabled = false;
                StyleButton(setButton, Color.FromArgb(121, 39, 31));
                setButton.Click += SetButton_Click;
                Controls.Add(setButton);

                revertButton.Text = "Revert to real time";
                revertButton.AutoSize = true;
                StyleButton(revertButton, Color.FromArgb(63, 55, 48));
                revertButton.Click += RevertButton_Click;
                Controls.Add(revertButton);
                revertButton.Location = new Point(left + setButton.PreferredSize.Width + 10, 298);

                autoRevertCheck.Text = "Restore real time when this app closes";
                autoRevertCheck.AutoSize = true;
                autoRevertCheck.Location = new Point(left + 2, 340);
                autoRevertCheck.BackColor = Color.Transparent;
                autoRevertCheck.Checked = true;
                autoRevertCheck.CheckedChanged += delegate { SaveSettings(); };
                Controls.Add(autoRevertCheck);

                // Save backup
                backupButton.Text = "Back up saves";
                backupButton.AutoSize = true;
                backupButton.Location = new Point(left, 378);
                StyleButton(backupButton, Color.FromArgb(63, 55, 48));
                backupButton.Click += BackupButton_Click;
                Controls.Add(backupButton);
                var backupLeft = left + backupButton.PreferredSize.Width + 12;
                PlaceLabel(new Label { Text = "Zips your D2R save folder, just in case." },
                    backupLeft, 385, left + width - backupLeft, 20, new Font("Segoe UI", 8.5F), MutedColor);
                toolTip.SetToolTip(backupButton, "Saves a zip of Saved Games\\Diablo II Resurrected\nto Saved Games\\Diablo II Resurrected Backups");

                PlaceLabel(result, left, 426, width, 86, Font, Color.FromArgb(214, 192, 152));
                result.Text = "Loading terror zone schedule...";

                refreshTimer.Interval = 15000;
                refreshTimer.Tick += delegate { RenderSession(); };

                LoadSettings();
                RebuildZoneList(ReadSetting("zone", "Cathedral, Catacombs, Inner Cloister"));

                Load += MainForm_Load;
                FormClosing += MainForm_FormClosing;
                FormClosed += MainForm_FormClosed;
            }

            private DateTime RealUtcNow()
            {
                return DateTime.UtcNow + clockOffset;
            }

            private bool ClockIsShifted
            {
                get { return Math.Abs(clockOffset.TotalSeconds) > 5; }
            }

            // ---------- Look ----------

            private void PlaceLabel(Label label, int left, int top, int width, int height, Font font, Color color)
            {
                label.AutoSize = false;
                label.SetBounds(left, top, width, height);
                label.BackColor = Color.Transparent;
                label.Font = font;
                label.ForeColor = color;
                Controls.Add(label);
            }

            private static void StyleButton(Button button, Color color)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(135, 55, 42);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(145, 48, 35);
                button.BackColor = color;
                button.ForeColor = Color.FromArgb(250, 231, 196);
                button.Padding = new Padding(8, 3, 8, 3);
            }

            // Paints the art, the control panel and the frame once; the form shows the result as its background.
            private static Bitmap ComposeBackground()
            {
                var size = WindowSize;
                var background = new Bitmap(size.Width, size.Height);
                using (var art = LoadImage("art.png"))
                using (var frame = LoadImage("frame.png"))
                using (var wrap = new ImageAttributes())
                using (var g = Graphics.FromImage(background))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    wrap.SetWrapMode(WrapMode.TileFlipXY);
                    g.Clear(Color.FromArgb(20, 8, 10));

                    // Scale the art to the window height and shift it left so the goblin sits beside the panel.
                    // A mirrored copy carries the brick wall on under the panel.
                    var artWidth = art.Width * size.Height / art.Height;
                    var artLeft = GoblinWindowX - GoblinArtX * size.Height / art.Height;
                    DrawImage(g, art, new Rectangle(artLeft, 0, artWidth, size.Height), wrap);
                    art.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    DrawImage(g, art, new Rectangle(artLeft + artWidth, 0, artWidth, size.Height), wrap);

                    using (var panel = RoundedRectangle(PanelBounds, 8))
                    using (var fill = new SolidBrush(Color.FromArgb(215, 22, 13, 13)))
                    using (var edge = new Pen(Color.FromArgb(190, 176, 128, 52), 1.5F))
                    {
                        g.FillPath(fill, panel);
                        g.DrawPath(edge, panel);
                    }

                    DrawImage(g, frame, new Rectangle(Point.Empty, size), wrap);

                    // The title plate's inner outline in frame.png runs from (470, 8) to (810, 80).
                    var scale = (float)size.Height / frame.Height;
                    var plate = new RectangleF(470 * scale, 8 * scale, 340 * scale, 72 * scale);
                    using (var font = new Font("Georgia", 14F, FontStyle.Bold))
                    using (var brush = new SolidBrush(GoldColor))
                    using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g.DrawString("TERROR ZONE CLOCK", font, brush, plate, format);
                    }
                }
                return background;
            }

            private static void DrawImage(Graphics g, Image image, Rectangle target, ImageAttributes attributes)
            {
                g.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }

            private static Bitmap LoadImage(string resourceName)
            {
                using (var stream = typeof(Program).Assembly.GetManifestResourceStream(resourceName))
                using (var image = Image.FromStream(stream))
                    return new Bitmap(image);
            }

            private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
            {
                var diameter = radius * 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                return path;
            }

            // ---------- Startup / shutdown ----------

            private async void MainForm_Load(object sender, EventArgs e)
            {
                // If the clock was left shifted (e.g. after a crash), measure the real offset first.
                try
                {
                    var actual = await Task.Run(delegate { return GetActualUtcTime(); });
                    clockOffset = actual - DateTime.UtcNow;
                    if (!ClockIsShifted)
                        clockOffset = TimeSpan.Zero;
                }
                catch (Exception)
                {
                    clockOffset = TimeSpan.Zero;
                }

                await ReloadSessions();
                refreshTimer.Start();
            }

            private async Task ReloadSessions()
            {
                try
                {
                    var cachePath = Path.Combine(dataFolder, "tz-schedule.json");
                    bool fromCache = false;
                    sessions = await Task.Run(delegate { return LoadSessions(cachePath, out fromCache); });
                    if (sessions.Count >= 2)
                        sessionLength = sessions[1].StartUtc - sessions[0].StartUtc;

                    // Add any zones the site introduced that aren't in the built-in list.
                    RebuildZoneList(SelectedZone());
                    RenderSession();

                    var message = fromCache
                        ? "Offline: using the schedule saved on " + File.GetLastWriteTime(cachePath).ToString("d MMM yyyy") + "."
                        : "Schedule loaded (" + sessions.Count + " sessions).";
                    if (ClockIsShifted)
                        message += " Your clock is off by " + FormatSpan(clockOffset.Duration()) + "; times shown use the real time.";
                    else
                        message += " Pick a zone, back up your saves, then set the clock.";
                    result.Text = message;
                }
                catch (Exception ex)
                {
                    result.Text = "Could not load the terror zone schedule: " + ex.Message;
                }
            }

            private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
            {
                if (!autoRevertCheck.Checked || !ClockIsShifted)
                    return;
                DateTime actualUtc;
                try { actualUtc = GetActualUtcTime(); }
                catch (Exception) { actualUtc = RealUtcNow(); }
                var error = SetClock(actualUtc.ToLocalTime());
                if (error != 0)
                {
                    MessageBox.Show(
                        "Windows could not restore the clock (error " + error + ").\nPlease fix the time in Windows settings.",
                        "Clock not restored", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
            {
                refreshTimer.Stop();
                SaveSettings();
            }

            // ---------- Zones and favourites ----------

            private string SelectedZone()
            {
                var item = zoneSelector.SelectedItem as ZoneItem;
                return item == null ? null : item.Name;
            }

            private void RebuildZoneList(string selectName)
            {
                var names = new List<string>(ZoneNames);
                foreach (var session in sessions)
                {
                    if (!names.Contains(session.Zone))
                        names.Add(session.Zone);
                }

                var ordered = names.Where(n => favourites.Contains(n))
                    .Concat(names.Where(n => !favourites.Contains(n)));

                rebuildingZones = true;
                zoneSelector.BeginUpdate();
                zoneSelector.Items.Clear();
                foreach (var name in ordered)
                {
                    var item = new ZoneItem { Name = name, IsFavourite = favourites.Contains(name) };
                    zoneSelector.Items.Add(item);
                    if (name == selectName)
                        zoneSelector.SelectedItem = item;
                }
                if (zoneSelector.SelectedIndex < 0 && zoneSelector.Items.Count > 0)
                    zoneSelector.SelectedIndex = 0;
                zoneSelector.EndUpdate();
                rebuildingZones = false;
                UpdateFavouriteButton();
            }

            // Draws the picker in the panel's colours; the system ones show a bright highlight on the dark panel.
            private void ZoneSelector_DrawItem(object sender, DrawItemEventArgs e)
            {
                var highlighted = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (var back = new SolidBrush(highlighted ? Color.FromArgb(121, 39, 31) : zoneSelector.BackColor))
                    e.Graphics.FillRectangle(back, e.Bounds);
                if (e.Index < 0)
                    return;
                var textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, zoneSelector.Items[e.Index].ToString(), zoneSelector.Font, textBounds,
                    zoneSelector.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            private void ZoneSelector_SelectedIndexChanged(object sender, EventArgs e)
            {
                if (rebuildingZones)
                    return;
                UpdateFavouriteButton();
                RenderSession();
                SaveSettings();
            }

            private void UpdateFavouriteButton()
            {
                var zone = SelectedZone();
                var isFavourite = zone != null && favourites.Contains(zone);
                favouriteButton.Text = isFavourite ? "★" : "☆";
                toolTip.SetToolTip(favouriteButton, isFavourite ? "Remove from favourites" : "Add to favourites");
            }

            private void FavouriteButton_Click(object sender, EventArgs e)
            {
                var zone = SelectedZone();
                if (zone == null)
                    return;
                if (!favourites.Remove(zone))
                    favourites.Add(zone);
                RebuildZoneList(zone);
                SaveSettings();
            }

            // ---------- Session ----------

            // Shows the zone's most recent session, or its next one when the schedule has no earlier session.
            private void RenderSession()
            {
                var zone = SelectedZone();
                var now = RealUtcNow();
                var zoneSessions = sessions.Where(s => s.Zone == zone).ToList();
                var session = zoneSessions.LastOrDefault(s => s.StartUtc <= now)
                              ?? zoneSessions.FirstOrDefault(s => s.StartUtc > now);
                shownSession = session;
                setButton.Enabled = session != null;
                sessionCaption.ForeColor = MutedColor;

                if (session == null)
                {
                    sessionCaption.Text = "NO SESSION";
                    sessionTime.Text = "-";
                    sessionRelative.Text = sessions.Count == 0 ? "No schedule loaded." : "This zone isn't in the current schedule.";
                    return;
                }

                var end = session.StartUtc + sessionLength;
                sessionTime.Text = session.StartUtc.ToLocalTime().ToString("ddd d MMM yyyy, h:mm tt");
                if (session.StartUtc > now)
                {
                    sessionCaption.Text = "NEXT SESSION";
                    sessionRelative.Text = "Starts in " + FormatSpan(session.StartUtc - now);
                }
                else if (now < end)
                {
                    sessionCaption.Text = "ACTIVE NOW";
                    sessionCaption.ForeColor = ActiveColor;
                    sessionRelative.Text = "Started " + FormatSpan(now - session.StartUtc) + " ago, ends in " + FormatSpan(end - now);
                }
                else
                {
                    sessionCaption.Text = "LAST SESSION";
                    sessionRelative.Text = "Started " + FormatSpan(now - session.StartUtc) + " ago";
                }
            }

            private static string FormatSpan(TimeSpan span)
            {
                if (span.TotalMinutes < 1)
                    return "under a minute";
                if (span.TotalHours < 1)
                    return (int)span.TotalMinutes + "m";
                if (span.TotalDays < 1)
                    return (int)span.TotalHours + "h " + span.Minutes + "m";
                return (int)span.TotalDays + "d " + span.Hours + "h";
            }

            // ---------- Clock actions ----------

            private void SetButton_Click(object sender, EventArgs e)
            {
                var session = shownSession;
                if (session == null)
                    return;
                var target = session.StartUtc.ToLocalTime();

                var prompt = "Set the Windows clock to " + target.ToString("yyyy-MM-dd HH:mm:ss") + "?\n\nZone: " + session.Zone;
                if (!savesBackedUp)
                    prompt += "\n\nYou haven't backed up your saves yet. Choose No, then \"Back up saves\", if you want a copy first.";
                var confirmation = MessageBox.Show(prompt, "Confirm clock change", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirmation != DialogResult.Yes)
                    return;

                var realUtc = RealUtcNow();
                var error = SetClock(target);
                if (error != 0)
                {
                    result.Text = "Windows could not set the clock. Error " + error + ".";
                    return;
                }
                clockOffset = realUtc - DateTime.UtcNow;
                result.Text = "Clock set to " + target.ToString("yyyy-MM-dd HH:mm:ss") + " for " + session.Zone + "." +
                              (autoRevertCheck.Checked ? " Real time will be restored when you close this app." : "");
                RenderSession();
            }

            private async void RevertButton_Click(object sender, EventArgs e)
            {
                try
                {
                    revertButton.Enabled = false;
                    result.Text = "Getting actual time from time.windows.com...";
                    DateTime actualUtc;
                    var source = "time.windows.com";
                    try
                    {
                        actualUtc = await Task.Run(delegate { return GetActualUtcTime(); });
                    }
                    catch (Exception)
                    {
                        actualUtc = RealUtcNow();
                        source = "the time saved before the clock was changed";
                    }
                    var actualLocal = actualUtc.ToLocalTime();
                    var offsetAtCheck = actualUtc - DateTime.UtcNow;

                    var confirmation = MessageBox.Show(
                        "Restore the Windows clock to " + actualLocal.ToString("yyyy-MM-dd HH:mm:ss") + "?\n(from " + source + ")",
                        "Confirm time restore",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);
                    if (confirmation != DialogResult.Yes)
                    {
                        result.Text = "Clock restore cancelled.";
                        return;
                    }

                    // Account for the time the dialog stayed open.
                    actualLocal = (DateTime.UtcNow + offsetAtCheck).ToLocalTime();
                    var error = SetClock(actualLocal);
                    if (error != 0)
                    {
                        result.Text = "Windows could not restore the clock. Error " + error + ".";
                        return;
                    }
                    clockOffset = TimeSpan.Zero;
                    result.Text = "Clock restored to actual time: " + actualLocal.ToString("yyyy-MM-dd HH:mm:ss") + ".";
                    RenderSession();
                }
                finally
                {
                    revertButton.Enabled = true;
                }
            }

            private async void BackupButton_Click(object sender, EventArgs e)
            {
                backupButton.Enabled = false;
                result.Text = "Backing up your D2R saves...";
                try
                {
                    // Name the zip after the real time, not a shifted clock.
                    var stamp = RealUtcNow().ToLocalTime();
                    var fileCount = 0;
                    var zipPath = await Task.Run(delegate { return BackupSaves(stamp, out fileCount); });
                    savesBackedUp = true;
                    result.Text = "Backed up " + fileCount + " save files to " + zipPath;
                }
                catch (Exception ex)
                {
                    result.Text = "Backup failed: " + ex.Message;
                }
                finally
                {
                    backupButton.Enabled = true;
                }
            }

            // ---------- Settings ----------

            private readonly Dictionary<string, string> settings = new Dictionary<string, string>();

            private string ReadSetting(string key, string fallback)
            {
                string value;
                return settings.TryGetValue(key, out value) ? value : fallback;
            }

            private void LoadSettings()
            {
                try
                {
                    if (File.Exists(settingsPath))
                    {
                        foreach (var line in File.ReadAllLines(settingsPath, Encoding.UTF8))
                        {
                            var split = line.IndexOf('=');
                            if (split > 0)
                                settings[line.Substring(0, split)] = line.Substring(split + 1);
                        }
                    }
                }
                catch (IOException) { }

                foreach (var name in ReadSetting("favourites", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    favourites.Add(name);
                loadingSettings = true;
                autoRevertCheck.Checked = ReadSetting("autoRevert", "1") == "1";
                loadingSettings = false;
            }

            private void SaveSettings()
            {
                if (rebuildingZones || loadingSettings)
                    return;
                var zone = SelectedZone();
                if (zone != null)
                    settings["zone"] = zone;
                settings["favourites"] = string.Join("|", favourites);
                settings["autoRevert"] = autoRevertCheck.Checked ? "1" : "0";
                try
                {
                    File.WriteAllLines(settingsPath, settings.Select(kv => kv.Key + "=" + kv.Value), Encoding.UTF8);
                }
                catch (IOException) { }
            }
        }
    }
}
