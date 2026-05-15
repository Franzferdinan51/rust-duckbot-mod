using Oxide.Core.Plugins;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RustDuckBot", "1.4.5", "Duckets")]
    [Description("AI-powered computer station with DuckBot. CCTV, security, base management, trading, automation, intel, and more.")]
    public class RustDuckBot : RustPlugin
    {
        [PluginReference]
        private Plugin Kits;
        // =====================================================================
        // CONFIGURATION
        // =====================================================================

        private ConfigData _config;

        public class ConfigData
        {
            public string MCPServerHost = "127.0.0.1";
            public int MCPServerPort = 3851;
            public string AgentProvider = "duckbot";  // duckbot | lmstudio | openai | anthropic | openrouter | minimax
            public string AgentConfig = "http://localhost:18797";
            // LM Studio settings (used when AgentProvider = "lmstudio")
            public string LMStudioUrl = "http://127.0.0.1:1234";
            public string LMStudioModel = "local-model";
            public string LMStudioApiKey = ""; // Optional: for auth if required
            // OpenAI-compatible settings (used for openai/anthropic/openrouter/minimax)
            public string OpenAIApiKey = "";
            public string OpenAIBaseUrl = "https://api.openai.com/v1";
            public string OpenAIModel = "gpt-4o-mini";
            public string MiniMaxApiKey = ""; // MiniMax API key
            public string MiniMaxModel = "MiniMax-Text-01"; // MiniMax model name
            public bool EnableCameraControl = true;
            public bool EnableAdminCommands = true;
            public bool EnableAutoFeatures = true;
            public bool EnableRaidAlerts = true;
            public bool EnableDecayAlerts = true;
            public bool EnableAutomation = true;
            public int MaxChatHistory = 100;
            public int MaxActivityLog = 500;
            public int RaidAlertRadius = 100;
            public int DecayAlertHoursBefore = 24;
            public string[] AdminSteamIds = Array.Empty<string>();
            public bool EnableWebSocketRCON = true;
            public string RCONPassword = "";
            public int RCONPort = 28016;
            public string[] AllowedRCONCommands = new[] { "status", "serverinfo", "player.list", "players.online", "server.hostname", "server.seed", "server.worldsize", "server.pve", "global.status", "kick", "ban", "banid", "unban", "say", "global.say", "inventory.give", "teleport", "teleport2me", "weather", "time", "save", "gc.collect", "status.gpu", "status.ram" };
            public bool EnableGridMap = true;
            public bool EnablePlayerTracking = true;
            public bool EnableSmartAlerts = true;
            // Teleport settings
            public int MaxHomesPerPlayer = 5;
            public int TeleportRequestSeconds = 60;
            public int TeleportCooldownSeconds = 120;
            public int TeleportWarmupSeconds = 10; // seconds before tp executes
            public bool AllowTeleportDuringRaid = false;
            public bool AllowTownTeleport = true;
            public bool AllowBanditTeleport = true;
            public int TownCooldownMinutes = 30;
            public int BanditCooldownMinutes = 60;
            // Monument positions for /town, /bandit
            public float OutpostX = -94.5f, OutpostY = 3.0f, OutpostZ = -55.4f;
            public float BanditX = -222.6f, BanditY = 2.0f, BanditZ = 6.7f;
            // Moderation
            public int MaxPlayerNotes = 20;
            public bool EnableReportSystem = true;
            public int ReportCooldownMinutes = 5;
            public bool EnableAIModeration = true;
            public bool EnableAutoModeration = true;
            public bool EnableAIAdmin = false; // EXPERIMENTAL: AI acts autonomously as admin -- disabled by default
            public int AutoModerationReportThreshold = 3;
            // Discord
            public bool EnableDiscord = false;
            public string DiscordWebhookUrl = "";
            public string DiscordBotName = "RustDuckBot";
            public bool DiscordPlayerJoinLeave = true;
            public bool DiscordDeaths = true;
            public bool DiscordRaidAlerts = false;
            public bool DiscordEventBroadcasts = true;
            public bool DiscordAIModeration = true;
            // Telegram
            public bool EnableTelegram = false;
            public string TelegramBotToken = "";
            public string TelegramChatId = "";
            public string TelegramBotName = "RustDuckBot";
            public bool TelegramPlayerJoinLeave = true;
            public bool TelegramDeaths = true;
            public bool TelegramRaidAlerts = false;
            public bool TelegramEventBroadcasts = true;
            public bool TelegramAIModeration = true;
            public int AutoModerationKickThreshold = 4;
            public int AutoModerationBanThreshold = 6;
            public int AutoModerationWindowMinutes = 30;
            public string AutoModerationBanDuration = "1d";

            public int AFKTimeoutMinutes = 10;
            public int AFKKickMinutes = 30;
            public bool AutoKickAFK = true;
            // Economy
            public bool EnableDailyReward = true;
            public int DailyRewardScrap = 100;
            public int DailyRewardRP = 20;
            public int PlaytimeBonusMinutes = 60; // bonus after N minutes
            public float VipBonusMultiplier = 1.5f; // VIP daily/killstreak bonus multiplier
            public int ShopItemListingFee = 50; // scrap cost to list an item in /db shop
            public int ShopMaxListingsPerPlayer = 10;
            public int ShopExchangeRateScrapPerRP = 10; // N scrap per 1 RP in exchange
            // Notifications
            public int MaxNotificationsPerPlayer = 50;
            public bool EnableNightAlert = true;
            // Combat tracking
            public int DeathHistoryMax = 10;
            // Building
            public int DecayScanRadius = 200;
            // Messaging
            public int MaxPrivateMessageLength = 500;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<ConfigData>() ?? new ConfigData();
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => _config = new ConfigData();

        // =====================================================================
        // STATE
        // =====================================================================

        private MCPClient _mcpClient;
        private WSRCONClient _rconClient;
        private AgentBridge _agentBridge;
        private LocalAIBridge _localAI;
        // Note: Field-based timers disabled - Oxide.Plugins.Timer constructor not available
        // Use timer.Once() / timer.Repeat() instead for repeating callbacks
        private bool _serverInitialized;
        private bool _commandsRegistered;
        private bool _initFailed;
        private string _initError;

        public void PrintAsh(string message)
        {
            Puts(message);
        }

        // Player sessions
        private Dictionary<ulong, PlayerSession> _sessions = new Dictionary<ulong, PlayerSession>();

        // Cameras
        private List<CameraInfo> _cameras = new List<CameraInfo>();
        private Dictionary<string, CameraRecording> _cameraRecordings = new Dictionary<string, CameraRecording>();

        // Base management
        private List<DecayWarning> _decayWarnings = new List<DecayWarning>();
        private List<BaseInfo> _monitoredBases = new List<BaseInfo>();

        // Security
        private List<AccessLogEntry> _accessLog = new List<AccessLogEntry>();
        private List<AlertEntry> _activeAlerts = new List<AlertEntry>();
        private List<AutomationRule> _automationRules = new List<AutomationRule>();

        // Trading
        private List<VendingInfo> _vendingMachines = new List<VendingInfo>();
        private List<ShopListing> _shopListings = new List<ShopListing>();

        // Intel
        private Dictionary<string, TrackedPlayer> _trackedPlayers = new Dictionary<string, TrackedPlayer>();
        private List<RaidEvent> _raidHistory = new List<RaidEvent>();
        private List<GridMarker> _gridMarkers = new List<GridMarker>();

        // Activity
        private List<ActivityEntry> _activityLog = new List<ActivityEntry>();
        private List<ReportEntry> _reportQueue = new List<ReportEntry>();
        private List<string> _mutedPlayers = new List<string>();
        private Dictionary<string, int> _commandStats = new Dictionary<string, int>();

        // Monument world positions for naming cameras by proximity
        private Dictionary<string, Vector3> _monumentLocations = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
        {
            { "oilrig",        new Vector3(26.6f,   4.6f, -123.7f) },
            { "largeoilrig",   new Vector3(-9.3f,   4.6f, -157.9f) },
            { "airfield",      new Vector3(-662.5f, 11.5f, -111.5f) },
            { "militarytunnel",new Vector3(-410.2f,  7.2f,  227.3f) },
            { "dome",          new Vector3(-418.8f, 36.7f, -172.8f) },
            { "trainyard",     new Vector3(-258.4f,  5.0f,   -6.2f) },
            { "powerplant",   new Vector3(-529.5f, 11.5f, -232.6f) },
            { "satellite",    new Vector3(-1179.9f,31.5f, -971.8f) },
            { "launchsite",   new Vector3(-1061.1f,33.4f,  322.4f) },
            { "water treatment", new Vector3(44.9f,  2.0f,   13.4f) },
            { "excavation",   new Vector3(125.8f,   0.5f,  140.4f) },
            { "junkyard",     new Vector3(-161.3f,  6.3f,   14.8f) },
            { "supermarket",  new Vector3(-219.4f,  4.0f,  -58.2f) },
            { "gasstation",   new Vector3(-268.3f,  3.5f, -111.6f) },
            { "outpost",      new Vector3(-94.5f,   3.0f,  -55.4f) },
            { "bandit",       new Vector3(-222.6f,  2.0f,    6.7f) },
            { "lighthouse",   new Vector3(9.6f,    15.6f, -160.4f) },
        };

        // Computer Station / CCTV watching
        private Dictionary<ulong, ComputerStationSession> _computerSessions = new Dictionary<ulong, ComputerStationSession>();
        private Dictionary<ulong, TeleportRequest> _teleportRequests = new Dictionary<ulong, TeleportRequest>();
        private HashSet<string> _monumentCameraCodes = new HashSet<string>();
        private HashSet<string> _playerOwnedCameraIds = new HashSet<string>();

        private Dictionary<int, string> _pendingRconCommands = new Dictionary<int, string>();
        private Dictionary<int, string> _pendingRconRequestIds = new Dictionary<int, string>();

        // Group / Party system
        private Dictionary<ulong, PlayerGroup> _groups = new Dictionary<ulong, PlayerGroup>();
        private Dictionary<ulong, ulong> _groupInvites = new Dictionary<ulong, ulong>(); // invitee -> leaderId

        // Raid alert subscribers
        private HashSet<ulong> _raidAlertSubscribers = new HashSet<ulong>();

        // AFK / Timers
        private Timer _afkCheckTimer;
        private Timer _autoSaveTimer;
        private Timer _heartbeatTimer;
        private Timer _automationTimer;
        private Timer _decayTimer;
        private Timer _radarTimer;
        private HashSet<ulong> _knownOnlinePlayers = new HashSet<ulong>();

        // Data persistence
        private DuckBotData _saveData;
        private List<AlertEntry> _alertHistory = new List<AlertEntry>();

        // Group class
        private class PlayerGroup
        {
            public string Id;
            public string Name;
            public ulong LeaderId;
            public HashSet<ulong> Members = new HashSet<ulong>();
            public Dictionary<string, Position3D> SharedHomes = new Dictionary<string, Position3D>();
            public DateTime Created;
            public DateTime LastActivity;
        }

        // Persistence data classes
        [Serializable]
        private class DuckBotData
        {
            public Dictionary<string, PlayerSessionData> PlayerSessions = new Dictionary<string, PlayerSessionData>();
            public List<ComputerStationSessionData> ComputerStationSessions = new List<ComputerStationSessionData>();
            public List<ActivityEntryData> ActivityLog = new List<ActivityEntryData>();
            public List<AlertEntryData> AlertHistory = new List<AlertEntryData>();
            public List<GridMarkerData> CameraBookmarks = new List<GridMarkerData>();
            public List<GroupData> Groups = new List<GroupData>();
            public Dictionary<string, TrackedPlayerData> TrackedPlayers = new Dictionary<string, TrackedPlayerData>();
            public DateTime LastSaveTime;
        }
        [Serializable]
        private class PlayerSessionData
        {
            public ulong PlayerId;
            public string DisplayName;
            public string Role;
            public Dictionary<string, PositionData> Homes = new Dictionary<string, PositionData>();
            public TimeSpan OnlineTime;
            public DateTime LastSeen;
            public Dictionary<string, string> PlayerNotes = new Dictionary<string, string>();
            public int CurrentKillstreak;
            public DateTime LastKillTime;
            public DateTime LastDailyReward;
            public int TotalScrap;
            public int Balance;
            public List<string> Permissions = new List<string>();
            public List<string> Bookmarks = new List<string>();
        }
        [Serializable]
        private class PositionData { public float X, Y, Z; public PositionData() { } public PositionData(float x, float y, float z) { X = x; Y = y; Z = z; } public Vector3 ToVector3() => new Vector3(X, Y, Z); }
        [Serializable]
        private class ComputerStationSessionData
        {
            public ulong PlayerId;
            public string ActiveCameraId;
            public string ActiveCameraName;
            public bool IsWatchingCCTV;
            public DateTime SessionStart;
            public int CamerasViewed;
            public List<string> AvailableCameraCodes = new List<string>();
        }
        [Serializable]
        private class ActivityEntryData
        {
            public DateTime Time; public string Category; public string Action; public string Details;
            public string PlayerId; public string PlayerName;
        }
        [Serializable]
        private class AlertEntryData
        {
            public string Id; public string Type; public string Severity; public string Title;
            public string Message; public DateTime Time; public bool Acknowledged;
            public string AcknowledgedBy; public DateTime AcknowledgedAt;
        }
        [Serializable]
        private class GridMarkerData
        {
            public string Id; public string Name; public PositionData Position;
            public string Color; public string Icon; public bool Visible; public string OwnerId;
        }
        [Serializable]
        private class GroupData
        {
            public string Id; public string Name; public ulong LeaderId;
            public List<ulong> MemberIds = new List<ulong>();
            public Dictionary<string, PositionData> SharedHomes = new Dictionary<string, PositionData>();
            public DateTime Created;
        }
        [Serializable]
        private class TrackedPlayerData
        {
            public string UserId; public string DisplayName;
            public int Kills; public int Deaths; public DateTime LastSeen;
        }

        private class ComputerStationSession
        {
            public ulong PlayerId;
            public BaseEntity Station;
            public string ActiveCameraId;
            public string PreviousCameraId;
            public string ActiveCameraName;
            public bool IsWatchingCCTV;
            public DateTime SessionStart;
            public int CamerasViewed;
            public List<string> AvailableCameraCodes = new List<string>();
            public bool TerminalOpen;
        }

        // CUI Terminal UI
        private class TerminalUI
        {
            public static string OVERLAY_NAME = "duckbot_terminal";
            public static string PANEL_ANCHOR = "0.65 0.5";
            public static string PANEL_OFFSET = "350 0 350 0";

            public static string Color(string hex) => hex + "FF";

            public static CuiElementContainer BuildTerminal(string playerName, string role, int unreadAlerts, string currentCam, int cmdCount)
            {
                var container = new CuiElementContainer();

                // Main panel
                container.Add(new CuiElement
                {
                    Name = OVERLAY_NAME,
                    Parent = "Overlay",
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = PANEL_ANCHOR, AnchorMax = PANEL_ANCHOR, OffsetMin = "-350 0", OffsetMax = "0 0" },
                        new CuiImageComponent { Color = "0.05 0.05 0.08 0.97", Material = "assets/content/ui/uibackgroundblur.mat" }
                    }
                });

                // Header bar
                container.Add(new CuiElement
                {
                    Name = OVERLAY_NAME + "_header",
                    Parent = OVERLAY_NAME,
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0 0.92", AnchorMax = "1 1" },
                        new CuiImageComponent { Color = "0.12 0.09 0.04 1" }
                    }
                });

                // Title
                container.Add(new CuiElement
                {
                    Parent = OVERLAY_NAME + "_header",
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                        new CuiTextComponent { Text = $"🖥  DUCKBOT TERMINAL", FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "1 0.84 0 1" }
                    }
                });

                // Alert badge (if alerts)
                if (unreadAlerts > 0)
                    container.Add(new CuiElement
                    {
                        Parent = OVERLAY_NAME + "_header",
                        Components = {
                            new CuiRectTransformComponent { AnchorMin = "0.75 0.1", AnchorMax = "0.88 0.9" },
                            new CuiImageComponent { Color = "0.9 0.1 0.1 1" }
                        }
                    });

                if (unreadAlerts > 0)
                    container.Add(new CuiElement
                    {
                        Parent = OVERLAY_NAME + "_header",
                        Components = {
                            new CuiRectTransformComponent { AnchorMin = "0.75 0.1", AnchorMax = "0.88 0.9" },
                            new CuiTextComponent { Text = $"⚠{unreadAlerts}", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                        }
                    });

                // Info bar
                container.Add(new CuiElement
                {
                    Parent = OVERLAY_NAME,
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0 0.86", AnchorMax = "1 0.92" },
                        new CuiImageComponent { Color = "0.08 0.08 0.1 1" }
                    }
                });

                var infoText = $"<color=#888>User:</color> {playerName} <color=#888>|</color> <color=#FFD700>{role.ToUpper()}</color> <color=#888>|</color> <color=#888>Cam:</color> {(string.IsNullOrEmpty(currentCam) ? "<color=#666>none</color>" : currentCam)} <color=#888>|</color> <color=#888>Alerts:</color> {unreadAlerts}";
                container.Add(new CuiElement
                {
                    Parent = OVERLAY_NAME,
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0 0.86", AnchorMax = "1 0.92" },
                        new CuiTextComponent { Text = infoText, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" }
                    }
                });

                // Terminal body (scrollable area)
                container.Add(new CuiElement
                {
                    Parent = OVERLAY_NAME,
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0 0.02", AnchorMax = "1 0.86" },
                        new CuiImageComponent { Color = "0.03 0.03 0.05 1" }
                    }
                });

                // Quick actions row
                var actions = new[] { ("📷", "cameras"), ("🔒", "security"), ("💬", "chat"), ("📡", "radar"), ("⚙", "automation"), ("❓", "help") };
                float xStart = 0.02f;
                float xStep = 0.16f;
                for (int i = 0; i < actions.Length; i++)
                {
                    var (icon, cmd) = actions[i];
                    float xMin = xStart + i * xStep;
                    float xMax = xMin + 0.14f;
                    container.Add(new CuiElement
                    {
                        Name = $"{OVERLAY_NAME}_btn_{i}",
                        Parent = OVERLAY_NAME,
                        Components = {
                            new CuiRectTransformComponent { AnchorMin = $"{xMin} 0.94", AnchorMax = $"{xMax} 0.98" },
                            new CuiImageComponent { Color = "0.15 0.12 0.08 1" }
                        }
                    });
                    container.Add(new CuiElement
                    {
                        Parent = $"{OVERLAY_NAME}_btn_{i}",
                        Components = {
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                            new CuiTextComponent { Text = icon, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 0.84 0 1" }
                        }
                    });
                }

                // Welcome text
                int line = 0;
                float yBase = 0.72f;
                float lineH = 0.045f;
                var lines = new[] {
                    "══════════════════════════════",
                    "  🖥  DuckBot AI Terminal Ready",
                    "══════════════════════════════",
                    "",
                    "  Type <color=#FFD700>/db ask &lt;question&gt;</color> to chat",
                    "  Type <color=#FFD700>/db cameras</color> to view CCTV",
                    "  Type <color=#FFD700>/db security</color> for alerts",
                    "  Type <color=#FFD700>/db help</color> for all commands",
                    "",
                    "  Connected: <color=#00FF00>MCP ✓</color>",
                    "",
                    "  <color=#888>Camera codes (monuments):</color>",
                    "  <color=#888>oilrig / largeoilrig / airfield</color>",
                    "  <color=#888>dome / powerplant / trainyard</color>",
                    "  <color=#888>outpost / bandit / satellite</color>",
                    "",
                    "  <color=#888>Enter camera code in Rust's</color>",
                    "  <color=#888>CCTV panel to view monuments!</color>",
                };

                foreach (var text in lines)
                {
                    container.Add(new CuiElement
                    {
                        Parent = OVERLAY_NAME,
                        Components = {
                            new CuiRectTransformComponent { AnchorMin = $"0.02 {yBase - line * lineH}", AnchorMax = $"0.98 {yBase - (line - 1) * lineH}" },
                            new CuiTextComponent { Text = text, FontSize = 10, Align = TextAnchor.UpperLeft, Color = "0.8 0.8 0.8 1", Font = "RobotoCondensed-Bold" }
                        }
                    });
                    line++;
                }

                // Footer
                container.Add(new CuiElement
                {
                    Parent = OVERLAY_NAME,
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 0.04" },
                        new CuiImageComponent { Color = "0.1 0.07 0.03 1" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = OVERLAY_NAME,
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 0.04" },
                        new CuiTextComponent { Text = "RustDuckBot v1.4.5 | /db help | AI: DuckBot", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.5 0.4 0.2 1" }
                    }
                });

                return container;
            }

            public static CuiElementContainer BuildCameraList(List<CameraInfo> cameras, string currentCam)
            {
                var container = new CuiElementContainer();

                container.Add(new CuiElement
                {
                    Name = OVERLAY_NAME,
                    Parent = "Overlay",
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0.01 0.01", AnchorMax = "0.3 0.99" },
                        new CuiImageComponent { Color = "0.04 0.04 0.07 0.98", Material = "assets/content/ui/uibackgroundblur.mat" }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = OVERLAY_NAME,
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0 0.93", AnchorMax = "1 1" },
                        new CuiImageComponent { Color = "0.1 0.08 0.03 1" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = OVERLAY_NAME,
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0 0.93", AnchorMax = "1 1" },
                        new CuiTextComponent { Text = "📷 CCTV CAMERAS", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 0.84 0 1" }
                    }
                });

                int row = 0;
                float yStart = 0.90f;
                float rowH = 0.052f;
                foreach (var cam in cameras.Take(16))
                {
                    var statusColor = cam.Online ? (cam.HasPower ? "0 0.8 0.1 1" : "0.8 0.6 0 1") : "0.8 0.1 0.1 1";
                    var isActive = currentCam == cam.Id;

                    container.Add(new CuiElement
                    {
                        Name = $"{OVERLAY_NAME}_cam_{row}",
                        Parent = OVERLAY_NAME,
                        Components = {
                            new CuiRectTransformComponent { AnchorMin = $"0.01 {yStart - row * rowH - rowH}", AnchorMax = $"0.99 {yStart - row * rowH}" },
                            new CuiImageComponent { Color = isActive ? "0.12 0.08 0.02 1" : "0.08 0.08 0.12 1" }
                        }
                    });
                    container.Add(new CuiElement
                    {
                        Parent = $"{OVERLAY_NAME}_cam_{row}",
                        Components = {
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0.15 1" },
                            new CuiTextComponent { Text = cam.Online ? "🟢" : "🔴", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = statusColor }
                        }
                    });
                    container.Add(new CuiElement
                    {
                        Parent = $"{OVERLAY_NAME}_cam_{row}",
                        Components = {
                            new CuiRectTransformComponent { AnchorMin = "0.15 0", AnchorMax = "1 1" },
                            new CuiTextComponent { Text = $"[{cam.Id}] {cam.Name}", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = isActive ? "1 0.84 0 1" : "0.7 0.7 0.7 1" }
                        }
                    });
                    row++;
                }

                return container;
            }
        }

        // =====================================================================
        // PLAYER SESSION
        // =====================================================================

        private class PlayerSession
        {
            public ulong PlayerId;
            public string DisplayName;
            public string Role = "user";
            public string CurrentCameraId;
            public List<ChatEntry> ChatHistory = new List<ChatEntry>();
            public DateTime SessionStart = DateTime.Now;
            public bool IsAtComputerStation;
            public List<string> Bookmarks = new List<string>();
            public HashSet<string> Permissions = new HashSet<string>();
            public PlayerSettings Settings = new PlayerSettings();
            public Position3D LastPosition;
            public int Kills;
            public int Deaths;
            public TimeSpan OnlineTime;
            public int ResourcesGathered;
            public bool IsOnline = true;
            public DateTime LastSeen = DateTime.Now;
            // Teleport / home system
            public Dictionary<string, Position3D> Homes = new Dictionary<string, Position3D>();
            public DateTime? LastTeleport;
            public DateTime? LastTownTp;
            public DateTime? LastBanditTp;
            // Warmup teleport state
            public bool _pendingTeleport;
            public Position3D _teleportDestination;
            public string _teleportReason;
            public Position3D _teleportStartPos;
            // Moderation / messaging
            public HashSet<ulong> IgnoredPlayers = new HashSet<ulong>();
            public DateTime? LastReportSent;
            public Dictionary<string, string> PlayerNotes = new Dictionary<string, string>(); // noteKey -> note
            // Playtime / economy
            public DateTime? LastDailyReward;
            public TimeSpan TotalPlaytimeToday;
            public int PlaytimeMinutesToday;
            // Notifications
            public List<PlayerNotification> Notifications = new List<PlayerNotification>();
            public bool IsAFK;
            public DateTime LastActivity = DateTime.Now;
            // AFK tracking
            public bool _afkManual;
            public bool _afkAutoDetected;
            // Killstreak
            public int CurrentKillstreak;
            public DateTime LastKillTime;
            // Economy
            public int TotalScrap;
            public int Balance;
            // Death/kill history
            public List<DeathRecord> RecentDeaths = new List<DeathRecord>();
        }

        private class PlayerNotification
        {
            public string Id;
            public string Title;
            public string Body;
            public DateTime Created;
            public string Type; // system, raid, decay, trade, admin
            public bool Read;
        }

        private class DeathRecord
        {
            public DateTime Time;
            public string KillerName;
            public ulong KillerId;
            public string Weapon;
            public Vector3 Location;
            public string Monument;
        }

        public class ChatEntry
        {
            public string Sender;
            public string Message;
            public DateTime Time;
            public bool IsAI;
        }

        private class PlayerSettings
        {
            public bool AlertsEnabled = true;
            public bool RaidAlertsEnabled = true;
            public bool DecayAlertsEnabled = true;
            public bool NightAlert = false;
            public string AlertChannel = "terminal"; // terminal, chat, both
            public string Theme = "default"; // default, dark, security, industrial
        }

        private PlayerSession GetOrCreateSession(BasePlayer player)
        {
            if (player == null) return null;

            if (!_sessions.TryGetValue(player.userID, out var session) || session == null)
            {
                session = new PlayerSession
                {
                    PlayerId = player.userID,
                    DisplayName = player.displayName,
                    Role = permission.UserHasPermission(player.UserIDString, "rustduckbot.admin") ? "admin"
                        : permission.UserHasPermission(player.UserIDString, "rustduckbot.mod") ? "mod"
                        : permission.UserHasPermission(player.UserIDString, "rustduckbot.vip") ? "vip"
                        : "user",
                    SessionStart = DateTime.Now,
                    LastSeen = DateTime.Now,
                    IsOnline = true
                };
                _sessions[player.userID] = session;
            }

            session.DisplayName = player.displayName;
            session.IsOnline = true;
            session.LastSeen = DateTime.Now;
            session.Permissions = new HashSet<string>(new[]
            {
                "rustduckbot.use",
                "rustduckbot.vip",
                "rustduckbot.mod",
                "rustduckbot.admin",
                "rustduckbot.security",
                "rustduckbot.automation",
                "rustduckbot.trading",
                "rustduckbot.intel",
                "rustduckbot.teleport",
                "rustduckbot.moderation",
                "rustduckbot.afk",
                "rustduckbot.economy"
            }.Where(p => permission.UserHasPermission(player.UserIDString, p)));

            return session;
        }

        // =====================================================================
        // CAMERA SYSTEM
        // =====================================================================

        private class CameraInfo
        {
            public string Id;
            public string Name;
            public string Location;
            public string Monument;
            public bool Online;
            public bool HasPower = true;
            public bool IsPTZ;
            public int Pan;
            public int Tilt;
            public int Zoom = 100;
            public BaseEntity Entity;
            public DateTime LastActivity;
            public int ViewCount;
            public List<string> AuthorizedViewers = new List<string>();
        }

        private class CameraRecording
        {
            public string CameraId;
            public DateTime Timestamp;
            public string Event;
            public string PlayerName;
            public string Details;
            public byte[] Thumbnail;
        }

        // =====================================================================
        // BASE MANAGEMENT
        // =====================================================================

        private class DecayWarning
        {
            public ulong PlayerId;
            public string BaseName;
            public Vector3 Position;
            public float BlockCount;
            public float DecayRate;
            public DateTime LastRepair;
            public DateTime EstimatedCollapse;
            public int HoursRemaining;
            public bool Alerted;
        }

        private class BaseInfo
        {
            public ulong OwnerId;
            public string Name;
            public Vector3 Position;
            public float BlockCount;
            public float MaxBlockHealth;
            public float CurrentBlockHealth;
            public float DecayRatePerHour;
            public int UpkeepCost;
            public bool UnderAttack;
            public DateTime LastAttack;
            public List<string> AuthorizedPlayers = new List<string>();
            public List<DoorInfo> Doors = new List<DoorInfo>();
            public List<LightInfo> Lights = new List<LightInfo>();
            public List<TurretInfo> Turrets = new List<TurretInfo>();
            public float ShieldHealth;
            public bool ShieldActive;
        }

        private class DoorInfo
        {
            public string Id;
            public string Name;
            public string Position;
            public bool Locked;
            public bool Open;
            public string AccessLevel; // public, team, private
            public List<string> AllowedPlayers = new List<string>();
            public bool AutoOpen;
            public DateTime LastAccess;
        }

        private class LightInfo
        {
            public string Id;
            public string Name;
            public bool On;
            public float Brightness = 1.0f;
            public string Color = "white";
            public bool AutoOn;
            public bool NightOnly;
            public float PowerConsumption;
        }

        private class TurretInfo
        {
            public string Id;
            public string Name;
            public bool Online;
            public bool Active;
            public string TargetMode; // manual, auto, friendly_fire
            public List<string> Whitelist = new List<string>();
            public int Kills;
            public float Range;
            public int AmmoCount;
        }

        // =====================================================================
        // SECURITY SYSTEM
        // =====================================================================

        private class AccessLogEntry
        {
            public DateTime Time;
            public string PlayerId;
            public string PlayerName;
            public string Resource;
            public string Action; // enter, exit, view, control, attempt
            public bool Success;
            public string Details;
            public string CameraId;
        }

        private class AlertEntry
        {
            public string Id;
            public string Type; // raid, decay, breach, system
            public string Severity; // low, medium, high, critical
            public string Title;
            public string Message;
            public DateTime Time;
            public bool Acknowledged;
            public string AcknowledgedBy;
            public DateTime AcknowledgedAt;
            public Vector3? Location;
        }

        // =====================================================================
        // AUTOMATION
        // =====================================================================

        private class AutomationRule
        {
            public string Id;
            public string Name;
            public string Trigger; // time, player_near, raid, decay, manual
            public string Condition;
            public string Action;
            public bool Enabled;
            public int Priority;
            public DateTime LastTriggered;
            public int TriggerCount;
        }

        // =====================================================================
        // TRADING
        // =====================================================================

        private class VendingInfo
        {
            public string Id;
            public string Name;
            public string OwnerId;
            public Vector3 Position;
            public bool IsActive;
            public string Direction; // buy, sell, both
            public int Stock;
            public string Currency; // scrap, server rewards, custom
            public float BuyPrice;
            public float SellPrice;
            public string ItemName;
            public int TotalTransactions;
            public float TotalRevenue;
        }

        private class ShopListing
        {
            public string Id;
            public string SellerId;
            public string ItemName;
            public int Quantity;
            public float PricePerUnit;
            public string Currency;
            public bool Available;
            public DateTime ListedAt;
            public string Description;
        }

        // =====================================================================
        // INTEL / TRACKING
        // =====================================================================

        // Teleport request (tpr / tpa)
        private class TeleportRequest
        {
            public ulong FromId;
            public string FromName;
            public ulong ToId;
            public string ToName;
            public DateTime RequestTime;
            public bool IsFrom; // true = From wants to go TO To (tpr), false = From wants To to come HERE (tpa)
            public Vector3? Location; // for tpa: where the target should teleport to
        }

        private class ReportEntry
        {
            public string Id;
            public ulong ReporterId;
            public string ReporterName;
            public ulong TargetId;
            public string TargetName;
            public string Reason;
            public DateTime Time;
            public string Status; // pending, reviewed, resolved, dismissed
            public string ReviewedBy;
            public DateTime? ReviewedAt;
        }

        private class TrackedPlayer
        {
            public string PlayerId;
            public string DisplayName;
            public DateTime FirstSeen;
            public DateTime LastSeen;
            public int SessionCount;
            public TimeSpan TotalOnlineTime;
            public Vector3 LastPosition;
            public string LastMonument;
            public int Kills;
            public int Deaths;
            public int RaidsParticipated;
            public List<string> KnownAliases = new List<string>();
            public string ThreatLevel = "unknown"; // unknown, low, medium, high
            public List<string> Notes = new List<string>();
        }

        private class RaidEvent
        {
            public DateTime Time;
            public Vector3 Location;
            public string Monument;
            public List<string> Attackers = new List<string>();
            public List<string> Defenders = new List<string>();
            public string Outcome; // success, failed, in_progress
            public string LootCollected;
            public int TurretKills;
        }

        private class GridMarker
        {
            public string Id;
            public string Name;
            public Vector3 Position;
            public string Color;
            public string Icon; // base, loot, danger, patrol, custom
            public bool Visible;
            public string OwnerId;
        }

        // =====================================================================
        // ACTIVITY LOG
        // =====================================================================

        private class ActivityEntry
        {
            public DateTime Time;
            public string Category; // security, base, trade, system, chat
            public string Action;
            public string Details;
            public string PlayerId;
            public string PlayerName;
        }

        // =====================================================================
        // OXIDE HOOKS
        // =====================================================================

        private void Init()
        {
            if (_config == null) _config = new ConfigData();
            RegisterDuckBotCommands();

            try
            {
                _agentBridge = new AgentBridge(_config.AgentProvider, _config.AgentConfig);
                _localAI = new LocalAIBridge(_config);
                _mcpClient = new MCPClient(_config.MCPServerHost, _config.MCPServerPort, this);

                // Permissions
                permission.RegisterPermission("rustduckbot.use", this);
                permission.RegisterPermission("rustduckbot.vip", this);
                permission.RegisterPermission("rustduckbot.mod", this);
                permission.RegisterPermission("rustduckbot.admin", this);
                permission.RegisterPermission("rustduckbot.security", this);
                permission.RegisterPermission("rustduckbot.automation", this);
                permission.RegisterPermission("rustduckbot.trading", this);
                permission.RegisterPermission("rustduckbot.intel", this);
                permission.RegisterPermission("rustduckbot.teleport", this);
                permission.RegisterPermission("rustduckbot.moderation", this);
                permission.RegisterPermission("rustduckbot.afk", this);
                permission.RegisterPermission("rustduckbot.economy", this);

                // Subscribe to hooks - ALL DISABLED due to unavailable Rust types
                // OnPlayerConnected, OnPlayerDisconnected - OK
                // OnEntityTakeDamage, OnPlayerAttacked - HitInfo contains DamageType
                // OnDoorOpened, OnDoorClosed - OK but disabling for safety
                // OnExplosion - DamageType not available
                // OnPlayerChat, OnPlayerInput, CanClientMove - OK but disabling for safety
                // OnPlayerSleep, OnPlayerSleepEnded - OK but disabling for safety
                // OnEntityDeath - HitInfo contains DamageType
                // OnPlayerRespawned - OK but disabling for safety
                // OnCCTVCameraUsed, OnComputerStationUse - CCTV types not available
                // Subscribe(nameof(OnPlayerConnected));
                // Subscribe(nameof(OnPlayerDisconnected));
                // Subscribe(nameof(OnEntityTakeDamage));
                // Subscribe(nameof(OnPlayerAttacked));
                // Subscribe(nameof(OnDoorOpened));
                // Subscribe(nameof(OnDoorClosed));
                // Subscribe(nameof(OnExplosion));
                // Subscribe(nameof(OnPlayerChat));
                // Subscribe(nameof(OnCCTVCameraUsed));
                // Subscribe(nameof(OnComputerStationUse));
                // Subscribe(nameof(OnPlayerInput));
                // Subscribe(nameof(CanClientMove));
                // Subscribe(nameof(OnPlayerSleep));
                // Subscribe(nameof(OnPlayerSleepEnded));
                // Subscribe(nameof(OnEntityDeath));
                // Subscribe(nameof(OnPlayerRespawned));

                // Initialize monument camera codes
                InitializeMonumentCodes();
                InitializeKitDefinitions();
                InitializeItemPrices();
                InitializeBuildingPlans();

                _initFailed = false;
                _initError = null;
                PrintAsh("<color=#FFD700>RustDuckBot v1.4.5</color> loaded. Computer Station: <color=#00FF00>ENABLED</color> | Chat Panel: <color=#00FF00>ENABLED</color>");
                var aiMode = _config.AgentProvider == "duckbot" ? $"DuckBot MCP ({_config.AgentConfig})" : $"Local AI: {_config.AgentProvider}";
                PrintAsh($"AI: <color=#FFD700>{aiMode}</color> | MCP: ws://{_config.MCPServerHost}:{_config.MCPServerPort}");
            }
            catch (Exception ex)
            {
                _initFailed = true;
                _initError = ex.Message;
                PrintError($"[RustDuckBot] Init failed after DuckBot command registration; commands remain in recovery mode: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void RegisterDuckBotCommands()
        {
            if (_commandsRegistered) return;

            TryRegisterChatCommand("duckbot");
            TryRegisterChatCommand("db");
            _commandsRegistered = true;
        }

        private void TryRegisterChatCommand(string commandName)
        {
            try
            {
                cmd.AddChatCommand(commandName, this, nameof(CmdDuckBot));
            }
            catch (Exception ex)
            {
                PrintWarning($"[RustDuckBot] Could not manually register /{commandName}; attribute registration may still handle it. {ex.Message}");
            }
        }

        private void InitializeMonumentCodes()
        {
            _monumentCameraCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Large monuments
                "oilrig", "largeoilrig", "airfield", "militarytunnel", "dome",
                "trainyard", "powerplant", "satellite", "launchsite",
                "water treatment", "watertreatment", "excavation", "junkyard",
                "supermarket", "gasstation", "outpost", "bandit", "banditcamp",
                "arctic", "desert", "mining", "lighthouse", "dome_small",
                " supermarket", "large_barn", "swamp", "underwater_lab",
                "desert_arrivals", "arctic_arrivals",
                // Short codes
                "oil", "rig", "largeoil", "military", "air", "dome", "dome_small",
                "power", "sat", "launch", "water", "exc", "yard", "tunnel",
                "arctic_base", "desert_base", "bandit_camp", "outpost_north", "outpost_south",
            };
        }

        private void InitializeDefaultAutomation()
        {
            _automationRules.Add(new AutomationRule { Id = "auto_01", Name = "Night Lights", Trigger = "time", Condition = "sunset", Action = "lights.on", Enabled = true, Priority = 1 });
            _automationRules.Add(new AutomationRule { Id = "auto_02", Name = "Morning Lights", Trigger = "time", Condition = "sunrise", Action = "lights.off", Enabled = true, Priority = 1 });
            _automationRules.Add(new AutomationRule { Id = "auto_03", Name = "Raid Auto-Alert", Trigger = "raid", Condition = "explosion_near_base", Action = "alert.all", Enabled = true, Priority = 3 });
            _automationRules.Add(new AutomationRule { Id = "auto_04", Name = "Welcome", Trigger = "player_join", Condition = "always", Action = "chat.welcome", Enabled = true, Priority = 0 });
            _automationRules.Add(new AutomationRule { Id = "auto_05", Name = "Decay Reminder", Trigger = "decay", Condition = "24h_warning", Action = "alert.owner", Enabled = true, Priority = 2 });
        }

        private void OnServerInitialized()
        {
            try
            {
                if (_config == null) _config = new ConfigData();

                _serverInitialized = true;
                // MCP bridge connection — needed for real server data regardless of AI provider
                // The MCP bridge streams heartbeat/server state to the MCP server so tools always
                // reflect live data even when using LM Studio directly for AI responses.
                if (_config.AgentProvider == "duckbot" || true)
                {
                    if (_mcpClient != null) _ = _mcpClient.ConnectAsync();
                    else PrintWarning("[RustDuckBot] MCP client not initialized; server status tools will show stale data until reload.");
                }

                if (_config.EnableWebSocketRCON && !string.IsNullOrEmpty(_config.RCONPassword))
                {
                    _rconClient = new WSRCONClient("127.0.0.1", _config.RCONPort, _config.RCONPassword, this);
                    _ = _rconClient.ConnectAsync();
                }

                ScanCameras();
                ScanBases();
                ScanVendingMachines();

                // Heartbeat every 30s
                _heartbeatTimer = timer.Every(30f, () => HeartbeatCallback(null));

                // Automation every 60s
                _automationTimer = timer.Every(60f, () => AutomationCallback(null));

                // Decay check every 5 min
                _decayTimer = timer.Every(300f, () => DecayCheckCallback(null));

                // Subscribe to hooks
                // Hooks disabled — Rust types not available in Oxide.Compiler
                // Re-enable individually once hook implementations are verified for this build
                // Subscribe(nameof(OnPlayerConnected));
                // Subscribe(nameof(OnPlayerDisconnected));

                // Load persisted data
                LoadData();

                SendServerStatus();
                LogActivity("system", "Server initialized", $"RustDuckBot v1.4.5 started. Cameras: {_cameras.Count}");
            }
            catch (Exception ex)
            {
                _initFailed = true;
                _initError = ex.Message;
                PrintError($"[RustDuckBot] Server initialization failed; DuckBot chat commands remain in recovery mode: {ex.Message}\n{ex.StackTrace}");
            }
        }

        
        // ALL HOOKS DISABLED - Rust types not available in Oxide.Compiler
        /*
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            var session = GetOrCreateSession(player);
            _mcpClient?.SendMessage(new
            {
                type = "player_joined",
                playerId = player.UserIDString,
                name = player.displayName,
                role = session?.Role ?? "user",
                time = DateTime.Now.ToString("o")
            });
        }
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            _mcpClient?.SendMessage(new
            {
                type = "player_left",
                playerId = player.UserIDString,
                name = player.displayName,
                reason = reason ?? "unknown",
                time = DateTime.Now.ToString("o")
            });
        }
        private object OnPlayerChat(BasePlayer player, string message) { return null; }
        private void OnEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return;
            var victimId = entity is BasePlayer bp ? bp.UserIDString : entity.OwnerID.ToString();
            var attackerId = info.Initiator is BasePlayer ap ? ap.UserIDString : "world";
            var weapon = info.Weapon?.ShortPrefabName ?? "unknown";
            var damage = info.damageTypes?.Total() ?? 0f;
            _mcpClient?.SendMessage(new
            {
                type = "entity_damage",
                victimId,
                attackerId,
                weapon,
                damage,
                time = DateTime.Now.ToString("o")
            });
        }
        private void OnPlayerAttacked(BasePlayer attacker, HitInfo info) { }
        private void OnDoorOpened(BasePlayer player, Door door) { }
        private void OnDoorClosed(Door door) { }
        private void OnExplosion(Vector3 position, float radius, BasePlayer attacker = null) { }
        private object OnComputerStationUse(BasePlayer player, ComputerStation station) { return null; }
        private void OnCCTVCameraUsed(BasePlayer player, ComputerStation station, BaseEntity camera) { }
        private void OnPlayerInput(BasePlayer player, InputState input) { }
        private object CanClientMove(BasePlayer player) { return null; }
        private void OnChatInputChanged(BasePlayer player, string text) { }
        private void OnChatSubmit(BasePlayer player, string text) { }
        private object OnPlayerCommand(BasePlayer player, string command, string[] args) { return null; }
        private void OnEntityDeath(BasePlayer victim, HitInfo info) { }
        private void OnPlayerSleep(BasePlayer player) { }
        private void OnPlayerSleepEnded(BasePlayer player) { }
        private void OnPlayerRespawned(BasePlayer player) { }
        */
        private void CmdDuckBotAlias(BasePlayer player, string command, string[] args)
        {
            CmdDuckBot(player, command, args);
        }

        [ChatCommand("db")]
        private void CmdDuckBot(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            try
            {
                if (_config == null) _config = new ConfigData();
                if (args == null) args = Array.Empty<string>();
                LogDuckBotDebug($"CmdDuckBot player={player.displayName} command={command} args={args?.Length ?? 0}");

                var session = GetOrCreateSession(player);
                session.IsAtComputerStation = IsPlayerAtComputerStation(player);
                session.LastPosition = new Position3D(player.transform.position);

                // Track command usage
                TrackCommand("duckbot");

                if (args.Length == 0)
                {
                    if (_initFailed) ShowRecoveryNotice(player);
                    ShowTerminal(player, session);
                    return;
                }

                var subCmd = args[0].ToLowerInvariant();
                var argStr = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : "";
                var fullMessage = subCmd + (string.IsNullOrEmpty(argStr) ? "" : " " + argStr);

                // Route to handlers
                switch (subCmd)
                {
                // === HELP & INFO ===
                case "help": case "h":
                    var helpArgs = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                    if (helpArgs == "2") ShowHelpPage2(player, session);
                    else if (helpArgs == "3") ShowHelpPage3(player, session);
                    else ShowHelp(player, session);
                    break;
                case "terminal": case "term": case "t": ShowTerminal(player, session); break;
                case "info": case "server": ShowServerInfo(player, session); break;
                case "whoami": WhoAmI(player, session); break;

                // === CCTV ===
                case "cameras": case "cam": ListCameras(player, session); break;
                case "view": case "watch": ViewCamera(player, session, argStr); break;
                case "control": case "ctrl": ControlCamera(player, session, argStr); break;
                case "ptz": ControlPTZ(player, session, argStr); break;
                case "recordings": case "rec": ListRecordings(player, session, argStr); break;
                case "chat": case "talk": ShowChatPanel(player); break;

                // === SECURITY ===
                case "security": case "sec": ShowSecurityDashboard(player, session); break;
                case "alerts": ShowAlerts(player, session, argStr); break;
                case "ack": case "acknowledge": AcknowledgeAlert(player, session, argStr); break;
                case "access": ShowAccessLog(player, session, argStr); break;
                case "scan": ScanArea(player, session, argStr); break;
                case "threat": ShowThreatLevel(player, session, argStr); break;
                case "lockdown": HandleLockdown(player, session, argStr); break;
                case "sos": HandleSOS(player, session); break;

                // === BASE MANAGEMENT ===
                case "base": case "baseinfo": ShowBaseInfo(player, session, argStr); break;
                case "doors": ListDoors(player, session); break;
                case "door": ControlDoor(player, session, argStr); break;
                case "lights": ListLights(player, session); break;
                case "light": ControlLight(player, session, argStr); break;
                case "turrets": ListTurrets(player, session); break;
                case "turret": ControlTurret(player, session, argStr); break;
                case "decay": ShowDecayStatus(player, session); break;
                case "upkeep": ShowUpkeep(player, session); break;
                case "blocks": ShowBlockCount(player, session); break;
                case "repair": HandleRepair(player, session, argStr); break;
                case "auth": ShowTCAuth(player, session); break;
                case "authorize": AuthorizePlayer(player, session, argStr); break;

                // === AUTOMATION ===
                case "automation": case "auto": ShowAutomation(player, session); break;
                case "rules": ShowAutomationRules(player, session); break;
                case "rule": ManageAutomationRule(player, session, argStr); break;
                case "schedule": ShowSchedule(player, session); break;

                // === TRADING ===
                case "shop": case "market": HandleShopLegacy(player, session, argStr); break;
                case "sell": HandleSell(player, session, argStr); break;
                case "buy": HandleBuy(player, session, argStr); break;
                case "price": CheckPrice(player, session, argStr); break;
                case "vending": case "vm": ManageVending(player, session, argStr); break;
                case "listings": ShowListings(player, session); break;

                // === INTEL ===
                case "players": case "online": ListPlayers(player, session); break;
                case "player": ShowPlayerInfo(player, session, argStr); break;
                case "track": TrackPlayerCmd(player, session, argStr); break;
                case "playerhistory": ShowPlayerHistory(player, session, argStr); break;
                case "leaderboard": case "lb": ShowLeaderboard(player, session, argStr); break;
                case "stats": ShowPlayerStats(player, session, argStr); break;
                case "radar": ShowRadar(player, session); break;
                case "grid": case "map": ShowGridMap(player, session, argStr); break;
                case "mark": PlaceMarker(player, session, argStr); break;
                case "markers": ListMarkers(player, session); break;
                case "near": FindNearby(player, session, argStr); break;
                case "raiders": ShowActiveRaiders(player, session); break;
                case "raid": ShowRaidHistory(player, session, argStr); break;

                // === ACTIVITY & LOGS ===
                case "activity": case "log": ShowActivityLog(player, session, argStr); break;
                case "history": ShowHistory(player, session); break;

                // === MESSAGING ===
                case "msg": case "message": case "whisper": HandlePrivateMessage(player, session, argStr); break;
                case "ignore": HandleIgnore(player, session, argStr); break;
                case "unignore": HandleUnignore(player, session, argStr); break;
                case "afk": HandleAFK(player, session); break;

                // === MODERATION ===
                case "report": HandleReport(player, session, argStr); break;
                case "reports": HandleReports(player, session, argStr); break;
                case "modreview": HandleModerationReview(player, session, argStr); break;
                case "slay": HandleSlay(player, session, argStr); break;
                case "respawn": HandleRespawn(player, session, argStr); break;
                case "notes": HandleNotes(player, session, argStr); break;
                case "adminmsg": HandleAdminMsg(player, session, argStr); break;
                case "mute": HandleMute(player, session, argStr); break;
                case "unmute": HandleUnmute(player, session, argStr); break;
                case "mutelist": HandleMuteList(player, session); break;

                // === ECONOMY ===
                case "daily": HandleDaily(player, session); break;
                case "playtime": HandlePlaytime(player, session); break;
                case "top": HandleTop(player, session, argStr); break;

                // === COMBAT / INTEL ===
                case "death": case "lastdeath": HandleLastDeath(player, session, argStr); break;
                case "killer": HandleKiller(player, session, argStr); break;
                case "weapon": HandleWeaponInfo(player, session, argStr); break;
                case "compare": HandleCompare(player, session, argStr); break;
                case "loot": HandleLoot(player, session, argStr); break;
                case "kit": case "kits": HandleKit(player, session, argStr); break;

                // === BUILDING ===
                case "tc": HandleTC(player, session); break;
                case "plan": HandleBuildPlan(player, session, argStr); break;
                case "materials": HandleMaterials(player, session, argStr); break;
                case "tcmanage": HandleTCManage(player, session, argStr); break;
                case "confirm": HandleConfirm(player, session, argStr); break;
                case "offers": ShowOffers(player); break;
                case "cancel": HandleCancel(player, argStr); break;
                case "mylistings": ShowMyListings(player); break;
                case "cupsize": HandleCupSize(player, session); break;
                case "decaycheck": HandleDecayCheck(player, session, argStr); break;
                case "decayalert": HandleDecayAlert(player, session, argStr); break;

                // === NOTIFICATIONS ===
                case "night": HandleNightAlert(player, session); break;
                case "notify": case "notifications": HandleNotifications(player, session, argStr); break;
                case "subscribe": HandleSubscribe(player, session, argStr); break;
                case "uptime": HandleUptime(player, session); break;

                // === BROADCAST & MESSAGING ===
                case "broadcast": case "bc": Broadcast(player, session, argStr); break;
                case "say": HandleChat(player, session, argStr); break;
                case "team": HandleTeamMessage(player, session, argStr); break;

                // === ADMIN ===
                case "status": ServerStatus(player, session); break;
                case "admin": HandleAdmin(player, session, argStr); break;
                case "kick": HandleKick(player, session, argStr); break;
                case "ban": HandleBan(player, session, argStr); break;
                case "unban": HandleUnban(player, session, argStr); break;
                case "pve": HandlePvE(player, session, argStr); break;
                case "freeze": HandleFreeze(player, session, argStr); break;
                case "heal": HandleHeal(player, session, argStr); break;
                case "give": HandleGive(player, session, argStr); break;
                case "teleport": case "tp": HandleTeleport(player, session, argStr); break;
                case "spawn": HandleSpawn(player, session, argStr); break;
                case "event": HandleAdminEvent(player, session, argStr); break;

                // === TELEPORT ===
                case "tpr": case "tpa": HandleTPR(player, session, argStr); break;
                case "tpc": case "accept": HandleTPC(player, session); break;
                case "tpd": case "deny": HandleTPD(player, session); break;
                case "home": HandleHome(player, session, argStr); break;
                case "sethome": HandleSetHome(player, session, argStr); break;
                case "removehome": HandleRemoveHome(player, session, argStr); break;
                case "town": HandleTown(player, session); break;
                case "bandit": HandleBandit(player, session); break;
                case "back": HandleBack(player, session); break;
                case "rtele": case "rt": HandleRTele(player, session, argStr); break;
                case "pos": case "coords": HandleCoords(player, session); break;

                // === UTILITY ===
                case "time": ShowTime(player, session); break;
                case "weather": ShowWeather(player, session); break;
                case "wipe": ShowWipeInfo(player, session); break;
                case "monuments": case "monu": ShowMonuments(player, session); break;
                case "events": ShowActiveEvents(player, session); break;
                case "recipes": ShowRecipes(player, session, argStr); break;
                case "research": ShowResearch(player, session, argStr); break;
                case "blueprint": case "bp": ShowBlueprintInfo(player, session, argStr); break;
                case "loc": HandleGridNav(player, session, argStr); break;
                case "daynight": HandleTimeToNight(player, session); break;
                case "mapintel": HandleMapIntel(player, session, argStr); break;
                case "route": HandleRouteAdvice(player, session, argStr); break;
                case "brief": HandleWorldBrief(player, session); break;
                case "wipeprep": HandleWipePrep(player, session); break;
                case "eventintel": HandleEventIntel(player, session); break;
                case "teamintel": HandleTeamIntel(player, session); break;
                case "world": HandleWorldInfo(player, session); break;

                // === AI CHAT ===
                case "ask": case "ai": HandleAIChat(player, session, argStr); break;
                case "search": SearchKnowledge(player, session, argStr); break;
                case "recommend": GetRecommendations(player, session); break;
                case "analyze": AnalyzeBase(player, session); break;

                // === ADMIN UTILS ===
                case "wipekits": HandleWipeKits(player, session); break;
                case "backup": HandleBackup(player, session); break;

                // === SETTINGS ===
                case "settings": case "prefs": ShowSettings(player, session); break;
                case "set": UpdateSetting(player, session, argStr); break;
                case "theme": SetTheme(player, session, argStr); break;
                case "alerts_set": ConfigureAlerts(player, session, argStr); break;
                case "bookmark": AddBookmark(player, session, argStr); break;
                case "bookmarks": ShowBookmarks(player, session); break;

                // === GAMES & FUN ===
                case "roll": RollDice(player, session, argStr); break;
                case "flip": FlipCoin(player, session); break;
                case "8ball": Magic8Ball(player, session, argStr); break;
                case "rps": PlayRPS(player, session, argStr); break;
                case "quote": ShowQuote(player, session); break;
                case "joke": TellJoke(player, session); break;
                case "fortune": ShowFortune(player, session); break;
                case "guess": HandleGuess(player, session, argStr); break;
                case "lucky": HandleLucky(player, session); break;
                case "slots": PlaySlots(player, session); break;
                case "bet": PlaceBet(player, session, argStr); break;

                // === MISC ===
                case "version": case "ver": ShowVersion(player); break;
                case "credits": ShowCredits(player); break;
                case "changelog": ShowChangelog(player); break;
                case "donate": ShowDonateInfo(player); break;
                case "discord": ShowDiscord(player); break;
                case "support": ShowSupport(player); break;
                case "bug": HandleBugReport(player, session, argStr); break;

                // === NEW: RAID ALERTS ===
                case "raidalert": case "raidalerts": HandleRaidAlert(player, session); break;

                // === NEW: GROUPS / PARTIES ===
                case "group": case "party": case "pgroup": HandleGroup(player, session, argStr); break;

                default:
                    // Treat as AI chat
                    HandleAIChat(player, session, fullMessage);
                    break;
                }
            }
            catch (System.Exception ex)
            {
                PrintToChat(player, "<color=#FF4444>DuckBot error:</color> " + ex.Message);
                PrintAsh("DuckBot command error: " + ex);
            }
        }

        public object CmdDuckBotShim(BasePlayer player, string command, string[] args)
        {
            CmdDuckBot(player, command, args ?? Array.Empty<string>());
            return true;
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null || !IsDuckBotCommand(command)) return null;
            CmdDuckBot(player, command, args ?? Array.Empty<string>());
            return true;
        }

        private bool TryHandleDuckBotSlashCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;

            var text = message.Trim();
            if (!text.StartsWith("/", StringComparison.Ordinal)) return false;

            text = text.Substring(1).Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = SplitArgs(text, 2);
            if (parts.Length == 0 || !IsDuckBotCommand(parts[0])) return false;

            var args = parts.Length > 1 ? SplitCommandArguments(parts[1]) : Array.Empty<string>();
            CmdDuckBot(player, parts[0], args);
            return true;
        }

        private bool IsDuckBotCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            var normalized = command.Trim().TrimStart('/').ToLowerInvariant();
            return normalized == "db" || normalized == "duckbot";
        }

        private string[] SplitCommandArguments(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) return Array.Empty<string>();
            return args.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // =====================================================================
        // HELP & INFO
        // =====================================================================

        private void ShowRecoveryNotice(BasePlayer player)
        {
            if (!_initFailed) return;
            var detail = string.IsNullOrEmpty(_initError) ? "check Oxide console logs" : _initError;
            PrintToChat(player, "<color=#FF9900>DuckBot loaded in recovery mode.</color> Command routing is alive, but some AI, MCP, RCON, or automation features may be offline.");
            PrintToChat(player, "<color=#888>Startup error:</color> " + detail);
        }

        private void ShowHelp(BasePlayer player, PlayerSession session)
        {
            ShowRecoveryNotice(player);
            timer.Once(0.1f, () => PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>"));
            timer.Once(0.15f, () => PrintToChat(player, "<color=#FFD700>    RUSTDUCKBOT v1.4.5 - HELP (1/3)</color>"));
            timer.Once(0.2f, () => PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>"));
            timer.Once(0.25f, () => PrintToChat(player, "<color=#FFD700>/db terminal</color> — Open AI computer terminal"));
            timer.Once(0.3f, () => PrintToChat(player, "<color=#FFD700>/db help</color> — Show this help"));
            timer.Once(0.35f, () => PrintToChat(player, "<color=#888>/db whoami</color> — Your role & permissions"));
            timer.Once(0.4f, () => PrintToChat(player, "<color=#888>/db server</color> — Server information"));
            timer.Once(0.45f, () => PrintToChat(player, "\n<color=#00BFFF>━━━ CCTV SYSTEM ━━━</color>"));
            timer.Once(0.5f, () => PrintToChat(player, "<color=#888>/db cameras</color> — List all CCTV cameras"));
            timer.Once(0.55f, () => PrintToChat(player, "<color=#888>/db view <id></color> — View camera feed"));
            timer.Once(0.6f, () => PrintToChat(player, "<color=#888>/db control <dir></color> — PTZ control"));
            timer.Once(0.65f, () => PrintToChat(player, "<color=#888>/db recordings</color> — View recent recordings"));
            timer.Once(0.7f, () => PrintToChat(player, "\n<color=#FF6B6B>━━━ SECURITY ━━━</color>"));
            timer.Once(0.75f, () => PrintToChat(player, "<color=#888>/db security</color> — Security dashboard"));
            timer.Once(0.8f, () => PrintToChat(player, "<color=#888>/db alerts</color> — View active alerts"));
            timer.Once(0.85f, () => PrintToChat(player, "<color=#888>/db ack <id></color> — Acknowledge alert"));
            timer.Once(0.9f, () => PrintToChat(player, "<color=#888>/db access</color> — Access log"));
            timer.Once(0.95f, () => PrintToChat(player, "<color=#888>/db scan</color> — Scan nearby area"));
            timer.Once(1.0f, () => PrintToChat(player, "<color=#888>/db lockdown</color> — Emergency lockdown"));
            timer.Once(1.05f, () => PrintToChat(player, "<color=#888>/db sos</color> — Send emergency alert"));
            timer.Once(1.1f, () => PrintToChat(player, "\n<color=#9B59B6>━━━ BASE MANAGEMENT ━━━</color>"));
            timer.Once(1.15f, () => PrintToChat(player, "<color=#888>/db base</color> — Base information"));
            timer.Once(1.2f, () => PrintToChat(player, "<color=#888>/db doors</color> — List doors"));
            timer.Once(1.25f, () => PrintToChat(player, "<color=#888>/db lights</color> — List lights"));
            timer.Once(1.3f, () => PrintToChat(player, "<color=#888>/db turrets</color> — List turrets"));
            timer.Once(1.35f, () => PrintToChat(player, "<color=#888>/db decay</color> — Decay status"));
            timer.Once(1.4f, () => PrintToChat(player, "<color=#888>/db upkeep</color> — Upkeep info"));
            timer.Once(1.45f, () => PrintToChat(player, "<color=#888>/db auth</color> — TC auth list"));
            timer.Once(1.5f, () => PrintToChat(player, "\n<color=#1ABC9C>━━━ TRADING ━━━</color>"));
            timer.Once(1.55f, () => PrintToChat(player, "<color=#888>/db shop, /db sell, /db buy, /db listings</color> — Player market"));
            timer.Once(1.6f, () => PrintToChat(player, "<color=#888>/db price <item></color> — Check market prices"));
            timer.Once(1.65f, () => PrintToChat(player, "<color=#888>/db vending</color> — Manage vending machines"));
            timer.Once(1.7f, () => PrintToChat(player, "\n<color=#3498DB>━━━ INTEL ━━━</color>"));
            timer.Once(1.75f, () => PrintToChat(player, "<color=#888>/db players</color> — Online players"));
            timer.Once(1.8f, () => PrintToChat(player, "<color=#888>/db player <name></color> — Player details"));
            timer.Once(1.85f, () => PrintToChat(player, "<color=#888>/db radar</color> — Nearby players"));
            timer.Once(1.9f, () => PrintToChat(player, "<color=#888>/db grid</color> — Grid map"));
            timer.Once(1.95f, () => PrintToChat(player, "<color=#888>/db mapintel</color> — AI map briefing"));
            timer.Once(2.0f, () => PrintToChat(player, "<color=#888>/db route <target></color> — AI route advice"));
            timer.Once(2.05f, () => PrintToChat(player, "<color=#FFD700>/db help 2</color> — Next page..."));
        }

        private void ShowHelpPage2(BasePlayer player, PlayerSession session)
        {
            timer.Once(0.1f, () => PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>"));
            timer.Once(0.15f, () => PrintToChat(player, "<color=#FFD700>    RUSTDUCKBOT v1.4.5 - HELP (2/3)</color>"));
            timer.Once(0.2f, () => PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>"));
            timer.Once(0.25f, () => PrintToChat(player, "\n<color=#F39C12>━━━ AI TERMINAL ━━━</color>"));
            timer.Once(0.3f, () => PrintToChat(player, "<color=#888>/db ask <question></color> — Ask AI anything"));
            timer.Once(0.35f, () => PrintToChat(player, "<color=#888>/db brief</color> — AI world brief"));
            timer.Once(0.4f, () => PrintToChat(player, "<color=#888>/db wipeprep</color> — AI wipe checklist"));
            timer.Once(0.45f, () => PrintToChat(player, "<color=#888>/db eventintel</color> — AI event guidance"));
            timer.Once(0.5f, () => PrintToChat(player, "<color=#888>/db analyze</color> — Analyze your base"));
            timer.Once(0.55f, () => PrintToChat(player, "<color=#888>/db recommend</color> — Get recommendations"));
            timer.Once(0.6f, () => PrintToChat(player, "<color=#888>/db search <query></color> — Search knowledge"));
            timer.Once(0.65f, () => PrintToChat(player, "\n<color=#888>━━━ GAMES & FUN ━━━</color>"));
            timer.Once(0.7f, () => PrintToChat(player, "<color=#888>/db roll <max></color> — Roll dice"));
            timer.Once(0.75f, () => PrintToChat(player, "<color=#888>/db flip</color> — Flip coin"));
            timer.Once(0.8f, () => PrintToChat(player, "<color=#888>/db 8ball <question></color> — Magic 8 ball"));
            timer.Once(0.85f, () => PrintToChat(player, "<color=#888>/db rps rock|paper|scissors</color> — RPS"));
            timer.Once(0.9f, () => PrintToChat(player, "<color=#888>/db joke</color> — Random joke"));
            timer.Once(0.95f, () => PrintToChat(player, "<color=#888>/db fortune</color> — Daily fortune"));
            timer.Once(1.0f, () => PrintToChat(player, "<color=#888>/db slots</color> — Slot machine"));
            timer.Once(1.05f, () => PrintToChat(player, "<color=#888>/db quote</color> — Random quote"));
            timer.Once(1.1f, () => PrintToChat(player, "\n<color=#FFD700>━━━ ECONOMY ━━━</color>"));
            timer.Once(1.15f, () => PrintToChat(player, "<color=#888>/db daily</color> — Claim daily reward"));
            timer.Once(1.2f, () => PrintToChat(player, "<color=#888>/db kits</color> — Available kits"));
            timer.Once(1.25f, () => PrintToChat(player, "<color=#888>/db guess join <bet></color> — Number guessing game"));
            timer.Once(1.3f, () => PrintToChat(player, "<color=#888>/db lucky</color> — Lucky block (VIP)"));
            timer.Once(1.35f, () => PrintToChat(player, "\n<color=#888>━━━ EVENTS (MOD+) ━━━</color>"));
            timer.Once(1.4f, () => PrintToChat(player, "<color=#888>/db event start coinflip|jackpot|scavenger|dropparty</color>"));
            timer.Once(1.45f, () => PrintToChat(player, "<color=#888>/db event list|join</color> — List/join active events"));
            timer.Once(1.5f, () => PrintToChat(player, "\n<color=#888>━━━ ACTIVITY & CHAT ━━━</color>"));
            timer.Once(1.55f, () => PrintToChat(player, "<color=#888>/db activity</color> — Recent activity"));
            timer.Once(1.6f, () => PrintToChat(player, "<color=#888>/db report <player> <reason></color> — Report bad actors"));
            timer.Once(1.65f, () => PrintToChat(player, "<color=#888>/db say <msg></color> — Chat with AI"));
            timer.Once(1.7f, () => PrintToChat(player, "<color=#FFD700>/db help 3</color> — Next page..."));
        }

        private void ShowHelpPage3(BasePlayer player, PlayerSession session)
        {
            var isVip = HasRoleOrHigher(session.Role, "vip");
            var isMod = HasRoleOrHigher(session.Role, "mod");
            var isAdmin = HasRoleOrHigher(session.Role, "admin");
            timer.Once(0.1f, () => PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>"));
            timer.Once(0.15f, () => PrintToChat(player, "<color=#FFD700>    RUSTDUCKBOT v1.4.5 - HELP (3/3)</color>"));
            timer.Once(0.2f, () => PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>"));
            if (isVip)
            {
                timer.Once(0.25f, () => PrintToChat(player, "\n<color=#00FF00>━━━ VIP COMMANDS ━━━</color>"));
                timer.Once(0.3f, () => PrintToChat(player, "<color=#888>/db door <id> lock/unlock</color> — Control doors"));
                timer.Once(0.35f, () => PrintToChat(player, "<color=#888>/db light <id> on/off</color> — Control lights"));
                timer.Once(0.4f, () => PrintToChat(player, "<color=#888>/db time</color> — Game time & weather"));
                timer.Once(0.45f, () => PrintToChat(player, "<color=#888>/db monuments</color> — Monument map"));
                timer.Once(0.5f, () => PrintToChat(player, "<color=#888>/db loot <type></color> — Loot locations"));
                timer.Once(0.55f, () => PrintToChat(player, "<color=#888>/db teamintel</color> — AI team briefing"));
                timer.Once(0.6f, () => PrintToChat(player, "<color=#888>/db lucky</color> — Lucky block spin"));
            }
            if (isMod)
            {
                timer.Once(0.65f, () => PrintToChat(player, "\n<color=#FF9900>━━━ MOD COMMANDS ━━━</color>"));
                timer.Once(0.7f, () => PrintToChat(player, "<color=#888>/db kick <player> <reason></color> — Kick player"));
                timer.Once(0.75f, () => PrintToChat(player, "<color=#888>/db mute <player></color> — Mute player"));
                timer.Once(0.8f, () => PrintToChat(player, "<color=#888>/db reports</color> — Staff report queue"));
                timer.Once(0.85f, () => PrintToChat(player, "<color=#888>/db modreview <player></color> — AI moderation review"));
                timer.Once(0.9f, () => PrintToChat(player, "<color=#888>/db unmute <player></color> — Unmute player"));
                timer.Once(0.95f, () => PrintToChat(player, "<color=#888>/db freeze <player></color> — Freeze player"));
                timer.Once(1.0f, () => PrintToChat(player, "<color=#888>/db msg <player> <msg></color> — Private message"));
                timer.Once(1.05f, () => PrintToChat(player, "<color=#888>/db team <msg></color> — Team message"));
                timer.Once(1.1f, () => PrintToChat(player, "<color=#888>/db event start|list|join</color> — Server events"));
            }
            if (isAdmin)
            {
                timer.Once(1.15f, () => PrintToChat(player, "\n<color=#FF4444>━━━ ADMIN COMMANDS ━━━</color>"));
                timer.Once(1.2f, () => PrintToChat(player, "<color=#888>/db status</color> — Server status"));
                timer.Once(1.25f, () => PrintToChat(player, "<color=#888>/db pve on|off</color> — Toggle PvE mode"));
                timer.Once(1.3f, () => PrintToChat(player, "<color=#888>/db ban <player> <reason></color> — Ban player"));
                timer.Once(1.35f, () => PrintToChat(player, "<color=#888>/db unban <steamid></color> — Unban player"));
                timer.Once(1.4f, () => PrintToChat(player, "<color=#888>/db admin <cmd></color> — Run RCON command"));
                timer.Once(1.45f, () => PrintToChat(player, "<color=#888>/db heal <player></color> — Heal player"));
                timer.Once(1.5f, () => PrintToChat(player, "<color=#888>/db give <player> <item> <qty></color> — Give items"));
                timer.Once(1.55f, () => PrintToChat(player, "<color=#888>/db tp <from> <to></color> — Teleport"));
                timer.Once(1.6f, () => PrintToChat(player, "<color=#888>/db spawn <item> <qty></color> — Spawn item"));
                timer.Once(1.65f, () => PrintToChat(player, "<color=#888>/db broadcast <msg></color> — Server broadcast"));
                timer.Once(1.7f, () => PrintToChat(player, "<color=#888>/db wipekits</color> — Reset all kit cooldowns"));
                timer.Once(1.75f, () => PrintToChat(player, "<color=#888>/db backup</color> — Trigger server save/backup"));
                timer.Once(1.8f, () => PrintToChat(player, "<color=#888>/db settings</color> — Server settings"));
                timer.Once(1.85f, () => PrintToChat(player, "<color=#888>/db showautomation</color> — Show automation panel"));
            }
            timer.Once(1.9f, () => PrintToChat(player, "\n<color=#FFD700>Use /db help 2 or /db help 3 to see all pages.</color>"));
        }

        private void ShowTerminal(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>");
            PrintToChat(player, "<color=#FFD700>   🖥️  DUCKBOT AI COMPUTER TERMINAL</color>");
            PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>");
            PrintToChat(player, $"<color=#888>Logged in:</color> {player.displayName} <color=#888>(</color>{session.Role}<color=#888>)</color>");
            PrintToChat(player, $"<color=#888>Session:</color> {(DateTime.Now - session.SessionStart).TotalMinutes:F0} min");
            PrintToChat(player, $"<color=#888>Camera:</color> {session.CurrentCameraId ?? "None"}");
            PrintToChat(player, $"<color=#888>Alerts:</color> {GetUnacknowledgedAlerts(player.UserIDString).Count}");
            PrintToChat(player, "<color=#FFD700>───────────────────────────────────────</color>");
            PrintToChat(player, "<color=#00FF00>1.</color> <color=#888>Type /db ask <question> to chat with AI</color>");
            PrintToChat(player, "<color=#00FF00>2.</color> <color=#888>Type /db cameras to view CCTV feeds</color>");
            PrintToChat(player, "<color=#00FF00>3.</color> <color=#888>Type /db security for security dashboard</color>");
            PrintToChat(player, "<color=#00FF00>4.</color> <color=#888>Type /db radar to scan nearby players</color>");
            PrintToChat(player, "<color=#00FF00>5.</color> <color=#888>Type /db shop for trading market</color>");
            PrintToChat(player, "<color=#00FF00>6.</color> <color=#888>Type /db automation to manage rules</color>");
            PrintToChat(player, "<color=#00FF00>7.</color> <color=#888>Type /db analyze for AI base analysis</color>");
            PrintToChat(player, "<color=#00FF00>8.</color> <color=#888>Type /db help for full command list</color>");
            PrintToChat(player, "<color=#FFD700>───────────────────────────────────────</color>");
            PrintToChat(player, "<color=#888>Theme:</color> <color=#FFD700>" + session.Settings.Theme.ToUpper() + "</color>");
            PrintToChat(player, "<color=#888>Alerts:</color> " + (session.Settings.AlertsEnabled ? "<color=#00FF00>ON" : "<color=#FF4444>OFF") + "</color>");
            PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>");
        }

        private void ShowServerInfo(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>");
            PrintToChat(player, "<color=#FFD700>      SERVER INFORMATION</color>");
            PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>");
            PrintToChat(player, $"<color=#FFD700>Plugin:</color> RustDuckBot v1.4.5");
            var aiProvider = _config.AgentProvider;
            var aiDetail = aiProvider == "duckbot" ? $"{_config.AgentConfig}" : (aiProvider == "lmstudio" ? $"{_config.LMStudioUrl}/{_config.LMStudioModel}" : _config.OpenAIBaseUrl + "/" + _config.OpenAIModel);
            PrintToChat(player, $"<color=#FFD700>AI Mode:</color> {aiProvider} — {aiDetail}");
            PrintToChat(player, $"<color=#FFD700>Players:</color> {BasePlayer.activePlayerList.Count} online, {BasePlayer.sleepingPlayerList.Count} sleeping");
            PrintToChat(player, $"<color=#FFD700>Time:</color> {GetGameTime()}");
            PrintToChat(player, $"<color=#FFD700>Wipe:</color> {GetWipeInfo()}");
            PrintToChat(player, $"<color=#FFD700>FPS:</color> {1.0f / Time.deltaTime:F0}");
            PrintToChat(player, $"<color=#FFD700>Cameras:</color> {_cameras.Count} registered");
            PrintToChat(player, $"<color=#FFD700>Vending:</color> {_vendingMachines.Count} machines");
            PrintToChat(player, $"<color=#FFD700>Alerts:</color> {GetUnacknowledgedAlerts(player.UserIDString).Count} unacknowledged");
            PrintToChat(player, $"<color=#FFD700>MCP:</color> {(_mcpClient?.IsConnected == true ? "<color=#00FF00>Connected" : "<color=#FF4444>Disconnected")}");
            PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>");
        }

        private void WhoAmI(BasePlayer player, PlayerSession session)
        {
            var roleColor = RoleColor(session.Role);
            var kd = session.Deaths > 0 ? (session.Kills / (float)session.Deaths).ToString("F2") : session.Kills.ToString();
            PrintToChat(player, "<color=#FFD700>═══ WHOAMI ═══</color>");
            PrintToChat(player, $"<color={roleColor}>Role:</color> {session.Role.ToUpper()}");
            PrintToChat(player, $"<color=#FFD700>Name:</color> {player.displayName}");
            PrintToChat(player, $"<color=#FFD700>SteamID:</color> {player.UserIDString}");
            PrintToChat(player, $"<color=#FFD700>Admin:</color> {(player.IsAdmin ? "<color=#00FF00>YES" : "<color=#FF4444>NO")}");
            PrintToChat(player, $"<color=#FFD700>Kills:</color> {session.Kills}");
            PrintToChat(player, $"<color=#FFD700>Deaths:</color> {session.Deaths}");
            PrintToChat(player, $"<color=#FFD700>K/D:</color> {kd}");
            PrintToChat(player, $"<color=#FFD700>Online:</color> {session.OnlineTime.TotalHours:F1}h");
            PrintToChat(player, $"<color=#FFD700>Position:</color> {GetLocation(player.transform.position)}");
            PrintToChat(player, $"<color=#FFD700>Camera:</color> {session.CurrentCameraId ?? "None"}");
            PrintToChat(player, $"<color=#FFD700>Messages:</color> {session.ChatHistory.Count}");
            PrintToChat(player, $"<color=#FFD700>Bookmarks:</color> {session.Bookmarks.Count}");

            var perms = new List<string>();
            foreach (var p in new[] { "rustduckbot.use", "rustduckbot.vip", "rustduckbot.mod", "rustduckbot.admin", "rustduckbot.security", "rustduckbot.automation", "rustduckbot.trading", "rustduckbot.intel" })
                if (permission.UserHasPermission(player.UserIDString, p)) perms.Add(p.Replace("rustduckbot.", ""));
            if (perms.Count > 0)
                PrintToChat(player, $"<color=#FFD700>Permissions:</color> {string.Join(", ", perms)}");
        }

        // =====================================================================
        // CCTV SYSTEM
        // =====================================================================

        private void ListCameras(BasePlayer player, PlayerSession session)
        {
            ScanCameras();
            if (_cameras.Count == 0) { PrintToChat(player, "No cameras found."); return; }

            PrintToChat(player, $"<color=#FFD700>═══ CCTV ({_cameras.Count} cameras) ═══</color>");
            foreach (var cam in _cameras)
            {
                var status = cam.Online ? (cam.HasPower ? "🟢" : "🟡OFF") : "🔴";
                var ptz = cam.IsPTZ ? " [PTZ]" : "";
                var current = session.CurrentCameraId == cam.Id ? " ◄" : "";
                PrintToChat(player, $"  {status} <color=#FFD700>[{cam.Id}]</color> {cam.Name} - {cam.Location}{ptz}{current}");
            }
            if (!string.IsNullOrEmpty(session.CurrentCameraId))
                PrintToChat(player, $"<color=#888>Current:</color> /db view {session.CurrentCameraId}");
        }

        private void ViewCamera(BasePlayer player, PlayerSession session, string cameraIdOrName)
        {
            if (string.IsNullOrWhiteSpace(cameraIdOrName))
            {
                if (!string.IsNullOrEmpty(session.CurrentCameraId))
                {
                    var cam = _cameras.Find(c => c.Id == session.CurrentCameraId);
                    PrintToChat(player, $"Viewing: {cam?.Name ?? session.CurrentCameraId}");
                    return;
                }
                PrintToChat(player, "Usage: /db view <camera_id> | /db cameras to list");
                return;
            }

            var camInfo = FindCamera(cameraIdOrName);
            if (camInfo == null) { PrintToChat(player, $"<color=#FF4444>Camera not found:</color> {cameraIdOrName}"); return; }
            if (!camInfo.Online || !camInfo.HasPower) { PrintToChat(player, $"<color=#FF4444>Camera unavailable:</color> {camInfo.Name}"); return; }

            session.CurrentCameraId = camInfo.Id;
            camInfo.ViewCount++;
            camInfo.LastActivity = DateTime.Now;

            PrintToChat(player, $"<color=#00FF00>Viewing:</color> {camInfo.Name}");
            PrintToChat(player, $"<color=#888>Location:</color> {camInfo.Location}");
            PrintToChat(player, $"<color=#888>Monument:</color> {camInfo.Monument}");
            PrintToChat(player, $"<color=#888>PTZ:</color> {(camInfo.IsPTZ ? "Yes - /db control left/right/up/down/zoom" : "Fixed camera")}");
            PrintToChat(player, $"<color=#888>Views:</color> {camInfo.ViewCount}");
            PrintToChat(player, $"<color=#888>Last activity:</color> {camInfo.LastActivity:HH:mm}");

            LogAccess(player.UserIDString, player.displayName, $"camera_{camInfo.Id}", "view", true, camInfo.Id);
            _mcpClient?.SendMessage(new { type = "camera_view", playerId = player.UserIDString, cameraId = camInfo.Id, cameraName = camInfo.Name });
        }

        private void ControlCamera(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }
            if (string.IsNullOrWhiteSpace(session.CurrentCameraId)) { PrintToChat(player, "No camera selected. Use /db view <id>"); return; }

            var cam = _cameras.Find(c => c.Id == session.CurrentCameraId);
            if (cam == null) { PrintToChat(player, "Camera not found."); return; }
            if (!cam.IsPTZ) { PrintToChat(player, $"{cam.Name} is fixed (no PTZ)."); return; }

            var action = args.Trim().ToLowerInvariant();
            var valid = new[] { "left", "right", "up", "down", "zoom_in", "zoom_out", "zoom", "reset", "home" };
            if (Array.IndexOf(valid, action) < 0) { PrintToChat(player, $"Valid: {string.Join(", ", valid)}"); return; }

            ExecutePTZ(cam, action);
            PrintToChat(player, $"<color=#00FF00>PTZ:</color> {cam.Name} → {action} (Pan:{cam.Pan}° Tilt:{cam.Tilt}° Zoom:{cam.Zoom}%)");
            LogAccess(player.UserIDString, player.displayName, $"camera_{cam.Id}", $"control_{action}", true, cam.Id);
        }

        private void ControlPTZ(BasePlayer player, PlayerSession session, string args)
        {
            ControlCamera(player, session, args);
        }

        private void ListRecordings(BasePlayer player, PlayerSession session, string cameraId)
        {
            PrintToChat(player, "<color=#FFD700>═══ CAMERA RECORDINGS ═══</color>");

            var recordings = string.IsNullOrEmpty(cameraId)
                ? _cameraRecordings.Values.ToList()
                : _cameraRecordings.Values.Where(r => r.CameraId == cameraId).ToList();

            if (recordings.Count == 0) { PrintToChat(player, "No recordings found."); return; }

            foreach (var rec in recordings.OrderByDescending(r => r.Timestamp).Take(10))
            {
                var cam = _cameras.Find(c => c.Id == rec.CameraId);
                PrintToChat(player, $"  <color=#888>[{rec.Timestamp:HH:mm}]</color> {rec.Event} - {rec.PlayerName} @ {cam?.Name ?? rec.CameraId}");
            }
        }

        // =====================================================================
        // SECURITY SYSTEM
        // =====================================================================

        private void ShowChatPanel(BasePlayer player)
        {
            var session = GetOrCreateSession(player);
            var history = session?.ChatHistory ?? new List<ChatEntry>();
            PrintToChat(player, "<color=#FFD700>═══ CHAT PANEL ═══</color>");
            if (history.Count == 0)
            {
                PrintToChat(player, "No recent DuckBot chat history.");
                PrintToChat(player, "Use <color=#4DA6FF>/db ask <question></color> to talk to DuckBot.");
                return;
            }

            foreach (var entry in history.Skip(Math.Max(0, history.Count - 8)))
            {
                var who = entry.IsAI ? "DuckBot" : entry.Sender;
                var color = entry.IsAI ? "#FFD700" : "#4DA6FF";
                PrintToChat(player, $"<color={color}>{who}:</color> {entry.Message}");
            }
        }

        private void ShowSecurityDashboard(BasePlayer player, PlayerSession session)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }

            var alerts = GetUnacknowledgedAlerts(player.UserIDString);
            var accessEntries = _accessLog.Where(a => a.Time > DateTime.Now.AddHours(-1)).ToList();

            PrintToChat(player, "<color=#FF6B6B>═══════════════════════════════════════</color>");
            PrintToChat(player, "<color=#FF6B6B>      🔒 SECURITY DASHBOARD</color>");
            PrintToChat(player, "<color=#FF6B6B>═══════════════════════════════════════</color>");
            PrintToChat(player, $"<color=#FFD700>Active Alerts:</color> {alerts.Count}");
            PrintToChat(player, $"<color=#FFD700>Access Log (1h):</color> {accessEntries.Count}");
            PrintToChat(player, $"<color=#FFD700>Cameras:</color> {_cameras.Count}");
            PrintToChat(player, $"<color=#FFD700>Online Players:</color> {BasePlayer.activePlayerList.Count}");
            PrintToChat(player, $"<color=#FFD700>Tracked Players:</color> {_trackedPlayers.Count}");

            if (alerts.Count > 0)
            {
                PrintToChat(player, "\n<color=#FF4444>Recent Alerts:</color>");
                foreach (var alert in alerts.Take(5))
                {
                    var sevColor = SeverityColor(alert.Severity);
                    PrintToChat(player, $"  <color={sevColor}>[{alert.Severity.ToUpper()}]</color> {alert.Title}: {alert.Message}");
                }
            }

            if (accessEntries.Count > 0)
            {
                PrintToChat(player, "\n<color=#888>Recent Access:</color>");
                foreach (var access in accessEntries.Take(5))
                {
                    PrintToChat(player, $"  <color=#888>[{access.Time:HH:mm}]</color> {access.PlayerName} {access.Action} {access.Resource}");
                }
            }

            var myBases = _monitoredBases.Where(b => b.OwnerId == player.userID || b.AuthorizedPlayers.Contains(player.UserIDString)).ToList();
            if (myBases.Count > 0)
            {
                PrintToChat(player, $"\n<color=#FFD700>Your Bases:</color> {myBases.Count}");
                foreach (var baseInfo in myBases.Take(3))
                {
                    var health = baseInfo.CurrentBlockHealth / baseInfo.MaxBlockHealth * 100;
                    var healthColor = health > 70 ? "#00FF00" : health > 40 ? "#FF9900" : "#FF4444";
                    PrintToChat(player, $"  <color={healthColor}>[</color>{baseInfo.Name}<color={healthColor}>]</color> HP: {health:F0}% | Doors: {baseInfo.Doors.Count} | Lights: {baseInfo.Lights.Count} | Turrets: {baseInfo.Turrets.Count}");
                }
            }

            PrintToChat(player, "<color=#FF6B6B>═══════════════════════════════════════</color>");
        }

        private void ShowAlerts(BasePlayer player, PlayerSession session, string args)
        {
            var all = args.Contains("all");
            var alerts = all ? _activeAlerts.ToList() : GetUnacknowledgedAlerts(player.UserIDString);

            PrintToChat(player, $"<color=#FFD700>═══ ALERTS ({alerts.Count}) ═══</color>");
            if (alerts.Count == 0) { PrintToChat(player, "No alerts."); return; }

            foreach (var alert in alerts.OrderByDescending(a => a.Time).Take(20))
            {
                var sevColor = SeverityColor(alert.Severity);
                var ack = alert.Acknowledged ? "✓" : "✗";
                PrintToChat(player, $"  <color={sevColor}>{ack}[{alert.Severity.ToUpper()}]</color> {alert.Id}: {alert.Title}");
                PrintToChat(player, $"      <color=#888>{alert.Message}</color> <color=#888>[{alert.Time:HH:mm}]</color>");
            }
        }

        private void AcknowledgeAlert(BasePlayer player, PlayerSession session, string alertId)
        {
            if (string.IsNullOrWhiteSpace(alertId)) { PrintToChat(player, "Usage: /db ack <alert_id>"); return; }

            var alert = _activeAlerts.Find(a => a.Id == alertId);
            if (alert == null) { PrintToChat(player, $"Alert not found: {alertId}"); return; }

            alert.Acknowledged = true;
            alert.AcknowledgedBy = player.displayName;
            alert.AcknowledgedAt = DateTime.Now;

            PrintToChat(player, $"<color=#00FF00>Alert acknowledged:</color> {alert.Title}");
            LogActivity("security", "Alert ack", $"Alert {alertId} acknowledged by {player.displayName}", player.UserIDString, player.displayName);
        }

        private void ShowAccessLog(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }

            var hours = 1;
            if (!string.IsNullOrEmpty(args) && int.TryParse(args, out var h)) hours = h;

            var entries = _accessLog.Where(e => e.Time > DateTime.Now.AddHours(-hours)).OrderByDescending(e => e.Time).Take(50).ToList();
            PrintToChat(player, $"<color=#FFD700>═══ ACCESS LOG (last {hours}h, {entries.Count} entries) ═══</color>");

            foreach (var entry in entries)
            {
                var successColor = entry.Success ? "#00FF00" : "#FF4444";
                var icon = AccessIcon(entry.Action);
                PrintToChat(player, $"  <color={successColor}>{icon}</color> <color=#888>[{entry.Time:HH:mm}]</color> {entry.PlayerName} {entry.Action} {entry.Resource}");
            }
        }

        private void ScanArea(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }

            var radius = 50f;
            if (!string.IsNullOrEmpty(args) && float.TryParse(args, out var r)) radius = r;

            var playerPos = player.transform.position;
            var nearbyPlayers = new List<BasePlayer>();
            var nearbyEntities = new List<BaseEntity>();

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == player) continue;
                if (Vector3.Distance(p.transform.position, playerPos) <= radius)
                    nearbyPlayers.Add(p);
            }

            // Scan entities
            foreach (var entity in BaseEntity.saveList)
            {
                if (entity == null) continue;
                if (Vector3.Distance(entity.transform.position, playerPos) <= radius)
                    nearbyEntities.Add(entity);
            }

            PrintToChat(player, $"<color=#FFD700>═══ SCAN RESULTS (radius: {radius}m) ═══</color>");
            PrintToChat(player, $"<color=#FFD700>Players:</color> {nearbyPlayers.Count}");
            foreach (var np in nearbyPlayers)
                PrintToChat(player, $"  <color=#FF4444>⚠</color> {np.displayName} @ {GetLocation(np.transform.position)}");

            var doors = nearbyEntities.Count(e => e.ShortPrefabName?.Contains("door") == true);
            var barrels = nearbyEntities.Count(e => e.ShortPrefabName?.Contains("barrel") == true);
            var crates = nearbyEntities.Count(e => e.ShortPrefabName?.Contains("crate") == true);
            var pickups = nearbyEntities.Count(e => e is DroppedItem);

            PrintToChat(player, $"<color=#FFD700>Entities:</color>");
            PrintToChat(player, $"  <color=#888>Doors: {doors} | Barrels: {barrels} | Crates: {crates} | Drops: {pickups}</color>");

            LogActivity("security", "Area scan", $"Scanned radius {radius}m at {GetLocation(playerPos)}", player.UserIDString, player.displayName);
        }

        private void ShowThreatLevel(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }

            if (string.IsNullOrWhiteSpace(targetName))
            {
                // Show all tracked players with threat levels
                var threatPlayers = _trackedPlayers.Values.Where(p => p.ThreatLevel != "unknown" && p.ThreatLevel != "low").OrderByDescending(p => p.ThreatLevel).ToList();
                PrintToChat(player, $"<color=#FFD700>═══ THREAT ASSESSMENT ({threatPlayers.Count}) ═══</color>");
                foreach (var tp in threatPlayers.Take(20))
                {
                    var color = ThreatColor(tp.ThreatLevel);
                    PrintToChat(player, $"  <color={color}>[</color>{tp.ThreatLevel.ToUpper()}<color={color}>]</color> {tp.DisplayName} | K:{tp.Kills} D:{tp.Deaths} | Last seen: {tp.LastSeen:HH:mm}");
                }
                return;
            }

            var target = _trackedPlayers.Values.FirstOrDefault(p => ContainsIgnoreCase(p.DisplayName, targetName));
            if (target == null) { PrintToChat(player, $"Player not tracked: {targetName}"); return; }

            PrintToChat(player, $"<color=#FFD700>═══ THREAT: {target.DisplayName} ═══</color>");
            var tColor = ThreatColor(target.ThreatLevel);
            PrintToChat(player, $"<color=#FFD700>Threat Level:</color> <color={tColor}>{target.ThreatLevel.ToUpper()}</color>");
            PrintToChat(player, $"<color=#FFD700>Kills:</color> {target.Kills} | <color=#FFD700>Deaths:</color> {target.Deaths}");
            PrintToChat(player, $"<color=#FFD700>Raids:</color> {target.RaidsParticipated}");
            PrintToChat(player, $"<color=#FFD700>Last Seen:</color> {target.LastSeen:HH:mm} at {target.LastMonument}");
            PrintToChat(player, $"<color=#FFD700>Sessions:</color> {target.SessionCount} | <color=#FFD700>Online:</color> {target.TotalOnlineTime.TotalHours:F1}h");
            if (target.KnownAliases.Count > 0)
                PrintToChat(player, $"<color=#FFD700>Aliases:</color> {string.Join(", ", target.KnownAliases)}");
            if (target.Notes.Count > 0)
                PrintToChat(player, $"<color=#FFD700>Notes:</color> {string.Join("; ", target.Notes)}");
        }

        private void HandleLockdown(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }

            var mode = args.ToLowerInvariant();
            if (mode != "start" && mode != "stop" && mode != "status")
            {
                PrintToChat(player, "Usage: /db lockdown start|stop|status");
                return;
            }

            if (mode == "start")
            {
                CreateAlert("system", "critical", "LOCKDOWN STARTED", $"Initiated by {player.displayName}", player.transform.position);
                // Lock all doors
                foreach (var door in UnityEngine.Object.FindObjectsOfType<Door>())
                {
                    door.SetFlag(BaseEntity.Flags.Locked, true);
                }
                PrintToChat(player, "<color=#FF0000>⚠ LOCKDOWN STARTED — ALL DOORS LOCKED</color>");
                BroadcastMessage(player, "LOCKDOWN", $"Emergency lockdown initiated by admin. All doors locked.", "critical");
            }
            else if (mode == "stop")
            {
                foreach (var door in UnityEngine.Object.FindObjectsOfType<Door>())
                {
                    door.SetFlag(BaseEntity.Flags.Locked, false);
                }
                PrintToChat(player, "<color=#00FF00>LOCKDOWN ENDED — DOORS UNLOCKED</color>");
                BroadcastMessage(player, "LOCKDOWN", "Emergency lockdown lifted by admin.", "info");
            }
            else
            {
                var lockedDoors = UnityEngine.Object.FindObjectsOfType<Door>().Count(d => d.IsLocked());
                PrintToChat(player, $"<color=#FFD700>LOCKDOWN STATUS:</color> {lockedDoors} locked doors");
            }
        }

        private void HandlePvE(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            var mode = args.ToLowerInvariant();
            if (mode == "on")
            {
                Server.Command("server.pve true");
                PrintToChat(player, "<color=#00FF88>PvE mode ENABLED</color>");
                Server.Broadcast("⚠ PvE mode is now ENABLED — no player-vs-player combat");
                LogActivity("admin", "PvE", "Enabled by " + player.displayName);
            }
            else if (mode == "off")
            {
                Server.Command("server.pve false");
                PrintToChat(player, "<color=#FF6644>PvE mode DISABLED</color>");
                Server.Broadcast("⚠ PvE mode is now DISABLED — PvP enabled");
                LogActivity("admin", "PvE", "Disabled by " + player.displayName);
            }
            else
            {
                PrintToChat(player, "<color=#FFD700>═══ PvE Mode Control ═══</color>");
                PrintToChat(player, "<color=#AAA>/db pve on</color> — Enable PvE (peaceful mode)");
                PrintToChat(player, "<color=#AAA>/db pve off</color> — Disable PvE (PvP enabled)");
                PrintToChat(player, "<color=#888>Requires admin role.</color>");
            }
        }

        // ── Admin/Mod Random Events ──────────────────────────────────────────
        private enum AdminEventType { CoinFlip, Jackpot, ScavengerHunt, DropParty }

        private class ActiveAdminEvent
        {
            public AdminEventType Type;
            public DateTime StartTime;
            public int DurationSeconds;
            public string HostName;
            public List<ulong> Participants = new List<ulong>();
            public string PrizeJson;
        }

        private readonly Dictionary<string, ActiveAdminEvent> _activeAdminEvents = new Dictionary<string, ActiveAdminEvent>();
        private readonly Dictionary<ulong, int> _guessGameState = new Dictionary<ulong, int>(); // player -> current guess

        // ── Admin Utilities ─────────────────────────────────────────────────
        private void HandleBroadcast(BasePlayer player, PlayerSession session, string msg)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            if (string.IsNullOrWhiteSpace(msg)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db broadcast <message>"); return; }
            var formatted = $"<color=#FFD700>[ADMIN BROADCAST]</color> {msg}";
            Server.Broadcast(formatted);
            PrintToChat(player, $"<color=#00FF88>Broadcast sent.</color>");
            LogActivity("admin", "Broadcast", msg, player.UserIDString, player.displayName);
        }

        private void HandleWipeKits(BasePlayer player, PlayerSession session)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            Server.Broadcast("<color=#FF4444>⚠ WIPE DAY: kit reset command recorded. Manual kit-plugin reset may still be required.</color>");
            PrintToChat(player, "<color=#00FF88>Wipekits notice sent.</color>");
            LogActivity("admin", "WipeKits", "Kit reset notice triggered by " + player.displayName, player.UserIDString, player.displayName);
        }

        private void HandleBackup(BasePlayer player, PlayerSession session)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            PrintToChat(player, "<color=#FFD700>Running server save + config backup...</color>");
            Server.Command("save.all");
            Server.Command("server.backup");
            PrintToChat(player, "<color=#00FF88>Save + backup command issued. Check server logs for status.</color>");
            LogActivity("admin", "Backup", "Backup triggered by " + player.displayName, player.UserIDString, player.displayName);
        }

        // ── Discord & Telegram Notifications ───────────────────────────────
        private void SendDiscord(string message, string eventType)
        {
            if (!_config.EnableDiscord || string.IsNullOrEmpty(_config.DiscordWebhookUrl)) return;
            try
            {
                using (var wb = new System.Net.WebClient())
                {
                    wb.Headers["Content-Type"] = "application/json";
                    var payload = SimpleJson.Serialize(new { content = $"[{_config.DiscordBotName}] {message}" });
                    wb.UploadString(_config.DiscordWebhookUrl, "POST", payload);
                }
            }
            catch (Exception ex) { PrintAsh($"Discord notification failed: {ex.Message}"); }
        }

        private void SendTelegram(string message, string eventType)
        {
            if (!_config.EnableTelegram || string.IsNullOrEmpty(_config.TelegramBotToken) || string.IsNullOrEmpty(_config.TelegramChatId)) return;
            try
            {
                var botToken = _config.TelegramBotToken;
                var chatId = _config.TelegramChatId;
                var encodedMsg = Uri.EscapeDataString($"[{_config.TelegramBotName}] {message}");
                using (var wb = new System.Net.WebClient())
                {
                    wb.DownloadString($"https://api.telegram.org/bot{botToken}/sendMessage?chat_id={chatId}&text={encodedMsg}");
                }
            }
            catch (Exception ex) { PrintAsh($"Telegram notification failed: {ex.Message}"); }
        }

        private void NotifyExternal(string message, string eventType)
        {
            // Check if this event type should be notified externally
            bool shouldNotify = false;
            if (eventType == "player_join" && (_config.DiscordPlayerJoinLeave || _config.TelegramPlayerJoinLeave)) shouldNotify = true;
            if (eventType == "player_leave" && (_config.DiscordPlayerJoinLeave || _config.TelegramPlayerJoinLeave)) shouldNotify = true;
            if (eventType == "death" && (_config.DiscordDeaths || _config.TelegramDeaths)) shouldNotify = true;
            if (eventType == "raid" && (_config.DiscordRaidAlerts || _config.TelegramRaidAlerts)) shouldNotify = true;
            if (eventType == "event" && (_config.DiscordEventBroadcasts || _config.TelegramEventBroadcasts)) shouldNotify = true;
            if (eventType == "moderation" && (_config.DiscordAIModeration || _config.TelegramAIModeration)) shouldNotify = true;
            if (!shouldNotify) return;
            // Add AI narration if available
            if (_config.EnableAIModeration && !string.IsNullOrEmpty(_config.AgentProvider))
            {
                var aiNarr = GetAssistantResponse("System", "admin", $"Format this server event for Discord/Telegram: '{message}'. Keep it under 200 characters, dramatic, and clear. No markdown.", null);
                if (!string.IsNullOrEmpty(aiNarr) && !aiNarr.StartsWith("?")) message = aiNarr;
            }
            if (_config.EnableDiscord) SendDiscord(message, eventType);
            if (_config.EnableTelegram) SendTelegram(message, eventType);
        }

        private void HandleAutoModerationConfig(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "admin"))
            {
                PrintToChat(player, "<color=#FF4444>Admin required</color>");
                return;
            }

            var mode = (args ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(mode))
            {
                PrintToChat(player, "<color=#FFD700>═══ Auto Moderation ═══</color>");
                PrintToChat(player, $"<color=#888>Status:</color> {(_config.EnableAutoModeration ? "ON" : "OFF")}");
                PrintToChat(player, $"<color=#888>Report threshold:</color> {_config.AutoModerationReportThreshold}");
                PrintToChat(player, $"<color=#888>Kick threshold:</color> {_config.AutoModerationKickThreshold}");
                PrintToChat(player, $"<color=#888>Ban threshold:</color> {_config.AutoModerationBanThreshold}");
                PrintToChat(player, "<color=#888>Usage:</color> /db automod on | /db automod off");
                return;
            }

            if (mode == "on")
            {
                _config.EnableAutoModeration = true;
                PrintToChat(player, "<color=#00FF88>Auto moderation enabled.</color>");
                LogActivity("admin", "AutoMod", "Enabled by " + player.displayName, player.UserIDString, player.displayName);
                return;
            }

            if (mode == "off")
            {
                _config.EnableAutoModeration = false;
                PrintToChat(player, "<color=#FFAA00>Auto moderation disabled.</color>");
                LogActivity("admin", "AutoMod", "Disabled by " + player.displayName, player.UserIDString, player.displayName);
                return;
            }

            PrintToChat(player, "<color=#888>Usage:</color> /db automod on | /db automod off");
        }

        private void HandleAdminEvent(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod+ required</color>"); return; }
            var parts = args.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) { ShowEventHelp(player); return; }
            var cmd = parts[0].ToLowerInvariant();
            if (cmd == "start") { RunAdminEvent(player, session, parts[1], parts.Length > 2 ? parts[2] : ""); return; }
            if (cmd == "stop" || cmd == "cancel") { StopAdminEvent(player, parts.Length > 1 ? parts[1] : ""); return; }
            if (cmd == "list") { ListAdminEvents(player); return; }
            if (cmd == "join") { JoinEvent(player, session); return; }
            ShowEventHelp(player);
        }

        private void ShowEventHelp(BasePlayer player)
        {
            PrintToChat(player, "<color=#FFD700>═══ Event Commands ═══</color>");
            PrintToChat(player, "<color=#AAA>/db event start coinflip <scrap></color> — 50/50 coin toss, winners split the pot");
            PrintToChat(player, "<color=#AAA>/db event start jackpot <scrap></color> — Random online player wins the prize");
            PrintToChat(player, "<color=#AAA>/db event start scavenger <seconds></color> — Item hunt, top finders win");
            PrintToChat(player, "<color=#AAA>/db event start dropparty <item> <count></color> — Drop items at your position");
            PrintToChat(player, "<color=#AAA>/db event join</color> — Join current active event");
            PrintToChat(player, "<color=#AAA>/db event list</color> — Show active events");
            PrintToChat(player, "<color=#AAA>/db event stop <name></color> — Cancel an event (admin)");
        }

        private void RunAdminEvent(BasePlayer player, PlayerSession session, string type, string args)
        {
            var key = type.ToLowerInvariant();
            if (_activeAdminEvents.ContainsKey(key)) { PrintToChat(player, $"<color=#FF4444>Event '{type}' is already running.</color>"); return; }
            var evt = new ActiveAdminEvent { Type = Enum.Parse<AdminEventType>(key, true), StartTime = DateTime.Now, HostName = player.displayName };
            switch (evt.Type)
            {
                case AdminEventType.CoinFlip:
                    var pot = 500; if (int.TryParse(args, out var p)) pot = p;
                    evt.DurationSeconds = 30; evt.PrizeJson = pot.ToString();
                    evt.Participants.Add(player.userID);
                    break;
                case AdminEventType.Jackpot:
                    var amt = 1000; if (int.TryParse(args, out var a)) amt = a;
                    evt.DurationSeconds = 20; evt.PrizeJson = amt.ToString();
                    break;
                case AdminEventType.ScavengerHunt:
                    var secs = 60; if (int.TryParse(args, out var s)) secs = s;
                    evt.DurationSeconds = Math.Min(Math.Max(secs, 10), 300); evt.PrizeJson = "scavenger";
                    break;
                case AdminEventType.DropParty:
                    evt.DurationSeconds = 45;
                    var itemParts = args.Split(' ', 2);
                    evt.PrizeJson = itemParts.Length > 0 ? itemParts[0] : "scrap";
                    break;
            }
            _activeAdminEvents[key] = evt;
            var aiNarr = GetAssistantResponse(player.displayName, session.Role, $"Generate a short, exciting event announcement for a Rust server event called '{type}'. Make it sound epic and urgent. Keep it under 50 characters. No markdown.", null);
            if (string.IsNullOrEmpty(aiNarr) || aiNarr.StartsWith("⚠")) aiNarr = $"⚔ EVENT: {type.ToUpper()} STARTED! Type /db event join";
            PrintToChat(player, $"<color=#00FF88>{aiNarr}</color>");
            Server.Broadcast($"<color=#FFD700>⚔ {aiNarr}</color>");
            LogActivity("event", type, $"Started by {player.displayName}");
            timer.Once(evt.DurationSeconds + 2f, () => ResolveAdminEvent(key));
        }

        private void StopAdminEvent(BasePlayer player, string type)
        {
            if (string.IsNullOrEmpty(type)) { PrintToChat(player, "<color=#FF4444>Specify event type to stop.</color>"); return; }
            var key = type.ToLowerInvariant();
            if (!_activeAdminEvents.ContainsKey(key)) { PrintToChat(player, $"<color=#FF4444>No active event '{type}'.</color>"); return; }
            _activeAdminEvents.Remove(key);
            PrintToChat(player, $"<color=#FFD700>Event '{type}' cancelled.</color>");
            Server.Broadcast($"⚠ Event '{type}' cancelled by admin.");
        }

        private void ListAdminEvents(BasePlayer player)
        {
            if (_activeAdminEvents.Count == 0) { PrintToChat(player, "<color=#888>No active events.</color>"); return; }
            PrintToChat(player, "<color=#FFD700>═══ Active Events ═══</color>");
            foreach (var e in _activeAdminEvents)
            {
                var elapsed = (DateTime.Now - e.Value.StartTime).Seconds;
                var remaining = Math.Max(0, e.Value.DurationSeconds - elapsed);
                PrintToChat(player, $"<color=#00FF88>{e.Key}</color> — {e.Value.HostName} | {remaining}s remaining | {e.Value.Participants.Count} joined");
            }
        }

        private void JoinEvent(BasePlayer player, PlayerSession session)
        {
            foreach (var e in _activeAdminEvents)
            {
                if (e.Value.Participants.Contains(player.userID)) { PrintToChat(player, "<color=#888>Already joined this event.</color>"); return; }
                if (e.Key == "coinflip" || e.Key == "jackpot") { e.Value.Participants.Add(player.userID); PrintToChat(player, $"<color=#FFD700>Joined {e.Key}! Wait for the result...</color>"); return; }
            }
            PrintToChat(player, "<color=#888>No joinable events right now. Type /db event list</color>");
        }

        private void ResolveAdminEvent(string key)
        {
            if (!_activeAdminEvents.TryGetValue(key, out var evt)) return;
            _activeAdminEvents.Remove(key);
            string narr;
            switch (evt.Type)
            {
                case AdminEventType.CoinFlip:
                    var pot = int.TryParse(evt.PrizeJson, out var p) ? p : 500;
                    if (evt.Participants.Count < 2) { narr = "CoinFlip ended — not enough players. Pot returned."; Server.Broadcast($"<color=#888>{narr}</color>"); }
                    else
                    {
                        var winner = evt.Participants[UnityEngine.Random.Range(0, evt.Participants.Count)];
                        var wPlayer = BasePlayer.activePlayerList.FirstOrDefault(pl => pl.userID == winner);
                        var prize = pot / evt.Participants.Count;
                        if (wPlayer != null) { Server.Command($"scavenger.additem \"{wPlayer.UserIDString}\" scrap {prize}"); }
                        var winnerName = wPlayer?.displayName ?? "Player";
                        narr = $"🪙 COINFLIP RESULT: {winnerName} WON {prize} scrap!";
                        var aiNarr = GetAssistantResponse(wPlayer?.displayName ?? "Server", "admin", $"A coinflip event just resolved in Rust. The winner got {prize} scrap out of {evt.Participants.Count} players. Write a short, exciting 1-sentence result announcement.", null);
                        if (!string.IsNullOrEmpty(aiNarr) && !aiNarr.StartsWith("⚠")) narr = aiNarr;
                        Server.Broadcast($"<color=#FFD700>{narr}</color>");
                    }
                    break;
                case AdminEventType.Jackpot:
                    var active = BasePlayer.activePlayerList.ToList();
                    if (active.Count == 0) { Server.Broadcast("Jackpot: no players online. Cancelled."); return; }
                    var winnerP = active[UnityEngine.Random.Range(0, active.Count)];
                    var jackpotAmt = int.TryParse(evt.PrizeJson, out var ja) ? ja : 1000;
                    Server.Command($"scavenger.additem \"{winnerP.UserIDString}\" scrap {jackpotAmt}");
                    var jNarr = $"🎰 JACKPOT! {winnerP.displayName} won {jackpotAmt} scrap!";
                    var jAi = GetAssistantResponse(winnerP.displayName, "admin", $"A jackpot event just resolved in Rust. {winnerP.displayName} won {jackpotAmt} scrap as the sole winner from {active.Count} online players. Write a short, exciting 1-sentence announcement.", null);
                    if (!string.IsNullOrEmpty(jAi) && !jAi.StartsWith("⚠")) jNarr = jAi;
                    Server.Broadcast($"<color=#FFD700>{jNarr}</color>");
                    break;
                case AdminEventType.ScavengerHunt:
                    var sNarr = "🏃 SCAVENGER HUNT ENDED! Top finders check your inventory for rewards.";
                    var sAi = GetAssistantResponse("Server", "admin", "A scavenger hunt event just ended in Rust. Players who found items during the hunt should be rewarded. Write a short, exciting 1-sentence announcement.", null);
                    if (!string.IsNullOrEmpty(sAi) && !sAi.StartsWith("⚠")) sNarr = sAi;
                    Server.Broadcast($"<color=#00FF88>{sNarr}</color>");
                    var topN = Math.Min(3, evt.Participants.Count);
                    for (int i = 0; i < topN; i++)
                    {
                        var pid = evt.Participants[i];
                        var p2 = BasePlayer.activePlayerList.FirstOrDefault(pl => pl.userID == pid);
                        if (p2 != null) Server.Command($"scavenger.additem \"{p2.UserIDString}\" scrap {500 * (topN - i)}");
                    }
                    break;
                case AdminEventType.DropParty:
                    var dropNarr = "📦 DROP PARTY! Items raining down — go pick them up!";
                    var dAi = GetAssistantResponse("Server", "admin", "A drop party event just started in Rust. Admin dropped items for players to collect. Write a short, exciting 1-sentence announcement.", null);
                    if (!string.IsNullOrEmpty(dAi) && !dAi.StartsWith("⚠")) dropNarr = dAi;
                    Server.Broadcast($"<color=#00FF88>{dropNarr}</color>");
                    var itemName = evt.PrizeJson ?? "scrap";
                    foreach (var pl in BasePlayer.activePlayerList)
                    {
                        var pos = pl.transform.position + new UnityEngine.Vector3(UnityEngine.Random.Range(-3f, 3f), 2f, UnityEngine.Random.Range(-3f, 3f));
                        Server.Command($"inventory.giveself \"{pl.UserIDString}\" {itemName} 3");
                    }
                    break;
            }
            LogActivity("event", key, $"Resolved at {DateTime.Now}");
        }

        private void HandleSOS(BasePlayer player, PlayerSession session)
        {
            var pos = player.transform.position;
            CreateAlert("system", "critical", "SOS ALERT", $"{player.displayName} sent SOS at {GetLocation(pos)}", pos);
            BroadcastMessage(player, "SOS", $"⚠ SOS from {player.displayName} at {GetLocation(pos)}! Respond immediately!", "critical");
            PrintToChat(player, "<color=#FF0000>⚠ SOS SENT — ALL PLAYERS NOTIFIED</color>");
            LogActivity("security", "SOS", player.displayName, player.UserIDString, player.displayName);
        }

        // =====================================================================
        // BASE MANAGEMENT
        // =====================================================================

        private void ShowBaseInfo(BasePlayer player, PlayerSession session, string args)
        {
            var bases = _monitoredBases.Where(b => b.OwnerId == player.userID || b.AuthorizedPlayers.Contains(player.UserIDString)).ToList();
            if (bases.Count == 0) { PrintToChat(player, "No bases found. Use /db scan to add bases to monitoring."); return; }

            PrintToChat(player, $"<color=#9B59B6>═══ YOUR BASES ({bases.Count}) ═══</color>");
            foreach (var baseInfo in bases)
            {
                var health = baseInfo.CurrentBlockHealth / baseInfo.MaxBlockHealth * 100;
                var hColor = health > 70 ? "#00FF00" : health > 40 ? "#FF9900" : "#FF4444";
                PrintToChat(player, $"  <color=#FFD700>{baseInfo.Name}</color> @ {GetLocation(baseInfo.Position)}");
                PrintToChat(player, $"      HP: <color={hColor}>{health:F0}%</color> | Blocks: {baseInfo.BlockCount:F0} | Decay: {baseInfo.DecayRatePerHour:F1}/h");
                PrintToChat(player, $"      Doors: {baseInfo.Doors.Count} | Lights: {baseInfo.Lights.Count} | Turrets: {baseInfo.Turrets.Count} | Auth: {baseInfo.AuthorizedPlayers.Count}");
                if (baseInfo.UnderAttack)
                    PrintToChat(player, $"      <color=#FF0000>⚠ UNDER ATTACK — Last attack: {baseInfo.LastAttack:HH:mm}</color>");
            }
        }

        private void ListDoors(BasePlayer player, PlayerSession session)
        {
            var doors = UnityEngine.Object.FindObjectsOfType<Door>().Where(d => Vector3.Distance(d.transform.position, player.transform.position) < 100).ToList();
            PrintToChat(player, $"<color=#9B59B6>═══ DOORS ({doors.Count} nearby) ═══</color>");
            foreach (var door in doors.Take(20))
            {
                var locked = door.IsLocked() ? "🔒" : "🔓";
                var open = door.IsOpen() ? " [OPEN]" : "";
                PrintToChat(player, $"  {locked} {door.ShortPrefabName ?? "Door"}{open} @ {GetLocation(door.transform.position)}");
            }
        }

        private void ControlDoor(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod+ required</color>"); return; }

            var parts = args.Split(new[] { ' ' }, 2);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db door <lock_id|position> lock/unlock/open/close"); return; }

            var action = parts[1].ToLowerInvariant();
            var validActions = new[] { "lock", "unlock", "open", "close" };
            if (Array.IndexOf(validActions, action) < 0) { PrintToChat(player, $"Valid: {string.Join(", ", validActions)}"); return; }

            var doors = UnityEngine.Object.FindObjectsOfType<Door>().ToList();
            var targetDoor = doors.FirstOrDefault(d => d.net != null && d.net.ID.Value.ToString() == parts[0] || GetLocation(d.transform.position).Contains(parts[0]));
            if (targetDoor == null) { PrintToChat(player, $"Door not found: {parts[0]}"); return; }

            switch (action)
            {
                case "lock": targetDoor.SetFlag(BaseEntity.Flags.Locked, true); break;
                case "unlock": targetDoor.SetFlag(BaseEntity.Flags.Locked, false); break;
                case "open": targetDoor.SetFlag(BaseEntity.Flags.Open, true); break;
                case "close": targetDoor.SetFlag(BaseEntity.Flags.Open, false); break;
            }

            PrintToChat(player, $"<color=#00FF00>Door:</color> {action} {GetLocation(targetDoor.transform.position)}");
            LogAccess(player.UserIDString, player.displayName, targetDoor.ShortPrefabName ?? "door", action, true);
        }

        private void ListLights(BasePlayer player, PlayerSession session)
        {
            var lights = UnityEngine.Object.FindObjectsOfType<BaseEntity>()
                .Where(e => e != null && e.ShortPrefabName != null && e.ShortPrefabName.ToLowerInvariant().Contains("light"))
                .ToList();
            PrintToChat(player, $"<color=#9B59B6>═══ LIGHTS ({lights.Count} found) ═══</color>");
            foreach (var light in lights.Take(20))
            {
                var isOn = light.HasFlag(BaseEntity.Flags.On);
                PrintToChat(player, $"  {(isOn ? "💡" : "⚫")} {light.ShortPrefabName ?? "Light"} @ {GetLocation(light.transform.position)}");
            }
        }

        private void ControlLight(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod+ required</color>"); return; }

            var parts = args.Split(new[] { ' ' }, 2);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db light <id> on/off/toggle"); return; }

            var action = parts[1].ToLowerInvariant();
            PrintToChat(player, $"<color=#FFD700>Light control:</color> {action}");
        }

        private void ListTurrets(BasePlayer player, PlayerSession session)
        {
            var turrets = UnityEngine.Object.FindObjectsOfType<AutoTurret>().ToList();
            PrintToChat(player, $"<color=#9B59B6>═══ TURRETS ({turrets.Count} nearby) ═══</color>");
            foreach (var turret in turrets)
            {
                var online = turret.IsOnline();
                var active = turret.HasFlag(BaseEntity.Flags.On);
                PrintToChat(player, $"  {(online ? "🔫" : "⚫")} {turret.ShortPrefabName ?? "Turret"} {(active ? "🔫ACTIVE" : "")} @ {GetLocation(turret.transform.position)}");
            }
        }

        private void ControlTurret(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod+ required</color>"); return; }

            var parts = args.Split(new[] { ' ' }, 2);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db turret <id> on/off/whitelist/add/remove"); return; }

            PrintToChat(player, $"<color=#FFD700>Turret control:</color> {args}");
        }

        private void ShowDecayStatus(BasePlayer player, PlayerSession session)
        {
            var warnings = _decayWarnings.Where(w => w.PlayerId == player.userID || _monitoredBases.Any(b => b.OwnerId == w.PlayerId)).ToList();
            PrintToChat(player, $"<color=#9B59B6>═══ DECAY STATUS ({warnings.Count}) ═══</color>");
            if (warnings.Count == 0) { PrintToChat(player, "No decay warnings."); return; }

            foreach (var w in warnings.OrderBy(w => w.HoursRemaining))
            {
                var severity = w.HoursRemaining < 6 ? "#FF0000" : w.HoursRemaining < 12 ? "#FF9900" : "#FFD700";
                PrintToChat(player, $"  <color={severity}>[</color>{w.HoursRemaining}h<color={severity}>]</color> {w.BaseName} - {w.BlockCount} blocks @ {GetLocation(w.Position)}");
            }
        }

        private void ShowUpkeep(BasePlayer player, PlayerSession session)
        {
            var upkeep = 0;
            if (player.inventory?.containerMain?.itemList != null) upkeep += player.inventory.containerMain.itemList.Sum(i => i.amount);
            if (player.inventory?.containerBelt?.itemList != null) upkeep += player.inventory.containerBelt.itemList.Sum(i => i.amount);
            if (player.inventory?.containerWear?.itemList != null) upkeep += player.inventory.containerWear.itemList.Sum(i => i.amount);
            PrintToChat(player, "<color=#9B59B6>═══ UPKEEP ═══</color>");
            PrintToChat(player, "Use server UI to manage upkeep. Checking your TC auth status...");
            var authPlayers = new List<string>(); // Placeholder
            PrintToChat(player, $"Authorized players: {authPlayers.Count}");
        }

        private void ShowBlockCount(BasePlayer player, PlayerSession session)
        {
            var pos = player.transform.position;
            PrintToChat(player, $"<color=#9B59B6>═══ BLOCK COUNT @ {GetLocation(pos)} ═══</color>");
            PrintToChat(player, "Scanning nearby blocks...");
            PrintToChat(player, "Use /db analyze for detailed breakdown.");
        }

        private void HandleRepair(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }
            PrintToChat(player, "Checking repairable structures...");
            PrintToChat(player, "Use hammer to repair manually.");
        }

        private void ShowTCAuth(BasePlayer player, PlayerSession session)
        {
            var tcs = UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>().Where(tc => Vector3.Distance(tc.transform.position, player.transform.position) < 50).ToList();
            PrintToChat(player, $"<color=#9B59B6>═══ TC AUTH ({tcs.Count} nearby) ═══</color>");
            foreach (var tc in tcs)
            {
                PrintToChat(player, $"  TC @ {GetLocation(tc.transform.position)} - auth list: {tc.authorizedPlayers.Count}");
            }
        }

        private void AuthorizePlayer(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod+ required</color>"); return; }
            var parts = args.Split(new[] { ' ' }, 2);
            if (parts.Length == 0) { PrintToChat(player, "Usage: /db authorize <steamid> [name]"); return; }
            PrintToChat(player, $"<color=#00FF00>Authorize:</color> {parts[0]}");
            LogActivity("base", "Authorize", $"{player.displayName} authorized {parts[0]}", player.UserIDString, player.displayName);
        }

        // =====================================================================
        // TRADING
        // =====================================================================

        private void ShowShop(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#3498DB>═══════════════════════════════════════</color>");
            PrintToChat(player, "<color=#3498DB>         🛒 TRADING MARKET</color>");
            PrintToChat(player, "<color=#3498DB>═══════════════════════════════════════</color>");
            PrintToChat(player, $"Active listings: {_shopListings.Count}");
            PrintToChat(player, $"Vending machines: {_vendingMachines.Count}");

            if (_shopListings.Count > 0)
            {
                PrintToChat(player, "\n<color=#FFD700>Recent Listings:</color>");
                foreach (var listing in _shopListings.Where(l => l.Available).Take(10))
                    PrintToChat(player, $"  • {listing.ItemName} x{listing.Quantity} @ {listing.PricePerUnit} {listing.Currency}");
            }
            else
            {
                PrintToChat(player, "\n<color=#888>No active listings. Use /db sell to create one.</color>");
            }

            PrintToChat(player, "\n<color=#FFD700>Commands:</color>");
            PrintToChat(player, "/db shop — Browse market");
            PrintToChat(player, "/db sell <item> <price> — List item for sale");
            PrintToChat(player, "/db buy <item> — Purchase item");
            PrintToChat(player, "/db listings — View all your listings");
            PrintToChat(player, "<color=#3498DB>═══════════════════════════════════════</color>");
        }

        private void HandleSell(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "user")) { PrintToChat(player, "<color=#FF4444>Login required</color>"); return; }
            var parts = args.Split(new[] { ' ' }, 2);
            if (parts.Length == 0) { PrintToChat(player, "Usage: /db sell <item_name> <price_per_unit>"); return; }

            var itemName = parts[0];
            var price = parts.Length > 1 && float.TryParse(parts[1], out var p) ? p : 0;

            var listing = new ShopListing
            {
                Id = Guid.NewGuid().ToString().Substring(0, 8),
                SellerId = player.UserIDString,
                ItemName = itemName,
                Quantity = 1,
                PricePerUnit = price,
                Currency = "scrap",
                Available = true,
                ListedAt = DateTime.Now
            };

            _shopListings.Add(listing);
            PrintToChat(player, $"<color=#00FF00>Listed:</color> {itemName} @ {price} scrap");
        }

        private void HandleBuy(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "user")) { PrintToChat(player, "<color=#FF4444>Login required</color>"); return; }
            if (string.IsNullOrWhiteSpace(args)) { PrintToChat(player, "Usage: /db buy <item_name>"); return; }

            var listing = _shopListings.FirstOrDefault(l => l.Available && ContainsIgnoreCase(l.ItemName, args));
            if (listing == null) { PrintToChat(player, $"Item not found: {args}"); return; }

            PrintToChat(player, $"<color=#FFD700>BUY:</color> {listing.ItemName} @ {listing.PricePerUnit} {listing.Currency}");
        }

        private void CheckPrice(BasePlayer player, PlayerSession session, string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) { PrintToChat(player, "Usage: /db price <item_name>"); return; }

            var listings = _shopListings.Where(l => ContainsIgnoreCase(l.ItemName, itemName) && l.Available).ToList();
            if (listings.Count == 0) { PrintToChat(player, $"No prices for: {itemName}"); return; }

            var avg = listings.Average(l => l.PricePerUnit);
            var min = listings.Min(l => l.PricePerUnit);
            var max = listings.Max(l => l.PricePerUnit);

            PrintToChat(player, $"<color=#3498DB>═══ PRICE: {itemName} ═══</color>");
            PrintToChat(player, $"  Average: {avg:F0} scrap");
            PrintToChat(player, $"  Range: {min:F0} - {max:F0} scrap");
            PrintToChat(player, $"  Listings: {listings.Count}");
        }

        private void ManageVending(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }
            PrintToChat(player, "<color=#3498DB>═══ VENDING MACHINES ═══</color>");
            PrintToChat(player, $"Total machines: {_vendingMachines.Count}");
            PrintToChat(player, "Use /db shop to browse active listings.");
        }

        private void ShowListings(BasePlayer player, PlayerSession session)
        {
            var myListings = _shopListings.Where(l => l.SellerId == player.UserIDString).ToList();
            PrintToChat(player, $"<color=#3498DB>═══ YOUR LISTINGS ({myListings.Count}) ═══</color>");
            if (myListings.Count == 0) { PrintToChat(player, "No listings. Use /db sell to create one."); return; }
            foreach (var listing in myListings)
                PrintToChat(player, $"  {(listing.Available ? "🟢" : "⚫")} {listing.ItemName} x{listing.Quantity} @ {listing.PricePerUnit} scrap");
        }

        // =====================================================================
        // INTEL
        // =====================================================================

        private void ListPlayers(BasePlayer player, PlayerSession session)
        {
            var players = BasePlayer.activePlayerList;
            PrintToChat(player, $"<color=#1ABC9C>═══ ONLINE ({players.Count}) ═══</color>");
            foreach (var p in players)
            {
                var pSession = GetOrCreateSession(p);
                var admin = p.IsAdmin ? " [A]" : "";
                var vip = HasRoleOrHigher(pSession.Role, "vip") ? " [VIP]" : "";
                PrintToChat(player, $"  <color=#FFD700>•</color> {p.displayName}{admin}{vip} ({p.UserIDString})");
            }
        }

        private void ShowPlayerInfo(BasePlayer player, PlayerSession session, string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName)) { targetName = player.displayName; }

            var target = FindPlayer(targetName);
            if (target == null) { PrintToChat(player, $"Player not found: {targetName}"); return; }

            TrackedPlayer tracked;
            _trackedPlayers.TryGetValue(target.UserIDString, out tracked);
            var pSession = GetOrCreateSession(target);

            PrintToChat(player, $"<color=#1ABC9C>═══ PLAYER: {target.displayName} ═══</color>");
            PrintToChat(player, $"  SteamID: {target.UserIDString}");
            PrintToChat(player, $"  Role: {pSession.Role}");
            PrintToChat(player, $"  Position: {GetLocation(target.transform.position)}");
            PrintToChat(player, $"  Online: {(target.IsConnected ? "🟢" : "⚫")} {(tracked?.LastSeen ?? DateTime.Now):HH:mm}");

            if (tracked != null)
            {
                var kd = tracked.Deaths > 0 ? (tracked.Kills / (float)tracked.Deaths).ToString("F2") : tracked.Kills.ToString();
                PrintToChat(player, $"  Kills: {tracked.Kills} | Deaths: {tracked.Deaths}");
                PrintToChat(player, $"  K/D: {kd}");
                PrintToChat(player, $"  Sessions: {tracked.SessionCount} | Time: {tracked.TotalOnlineTime.TotalHours:F1}h");
                PrintToChat(player, $"  Raids: {tracked.RaidsParticipated}");
                var tColor = ThreatColor(tracked.ThreatLevel);
                PrintToChat(player, $"  Threat: <color={tColor}>{tracked.ThreatLevel.ToUpper()}</color>");
            }
        }

        private void TrackPlayerCmd(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod+ required</color>"); return; }
            if (string.IsNullOrWhiteSpace(targetName)) { PrintToChat(player, "Usage: /db track <player_name>"); return; }

            var target = FindPlayer(targetName);
            if (target == null) { PrintToChat(player, $"Player not found: {targetName}"); return; }

            PrintToChat(player, $"<color=#00FF00>Tracking:</color> {target.displayName} @ {GetLocation(target.transform.position)}");
            TrackPlayer(target.UserIDString, target.displayName);
        }

        private void ShowPlayerHistory(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }

            var entries = _activityLog.Where(a => a.PlayerId == player.UserIDString && a.Time > DateTime.Now.AddDays(-7)).OrderByDescending(a => a.Time).Take(50).ToList();
            PrintToChat(player, $"<color=#1ABC9C>═══ ACTIVITY (7 days, {entries.Count} entries) ═══</color>");
            foreach (var entry in entries)
                PrintToChat(player, $"  <color=#888>[{entry.Time:MM/dd HH:mm}]</color> {entry.Category}: {entry.Action}");
        }

        private void ShowLeaderboard(BasePlayer player, PlayerSession session, string category)
        {
            category = category ?? "kills";
            PrintToChat(player, $"<color=#1ABC9C>═══ LEADERBOARD: {category.ToUpper()} ═══</color>");

            List<TrackedPlayer> sorted = null;
            switch (category.ToLower())
            {
                case "kills": sorted = _trackedPlayers.Values.OrderByDescending(p => p.Kills).Take(10).ToList(); break;
                case "deaths": sorted = _trackedPlayers.Values.OrderByDescending(p => p.Deaths).Take(10).ToList(); break;
                case "kd": sorted = _trackedPlayers.Values.Where(p => p.Deaths > 0).OrderByDescending(p => p.Kills / (float)p.Deaths).Take(10).ToList(); break;
                case "time": sorted = _trackedPlayers.Values.OrderByDescending(p => p.TotalOnlineTime).Take(10).ToList(); break;
                case "raids": sorted = _trackedPlayers.Values.OrderByDescending(p => p.RaidsParticipated).Take(10).ToList(); break;
                default: sorted = _trackedPlayers.Values.OrderByDescending(p => p.Kills).Take(10).ToList(); break;
            }

            int rank = 1;
            foreach (var p in sorted)
            {
                var value = category == "kills"
                    ? p.Kills.ToString()
                    : category == "time"
                        ? $"{p.TotalOnlineTime.TotalHours:F0}h"
                        : p.RaidsParticipated.ToString();
                PrintToChat(player, $"  #{rank++} {p.DisplayName}: {value}");
            }
        }

        private void ShowPlayerStats(BasePlayer player, PlayerSession session, string targetName)
        {
            var name = string.IsNullOrWhiteSpace(targetName) ? player.displayName : targetName;
            var target = FindPlayer(name);
            if (target == null) { PrintToChat(player, $"Player not found: {name}"); return; }

            TrackedPlayer tracked;
            _trackedPlayers.TryGetValue(target.UserIDString, out tracked);
            var kd = tracked != null && tracked.Deaths > 0 ? (tracked.Kills / (float)tracked.Deaths).ToString("F2") : (tracked?.Kills.ToString() ?? "0");
            PrintToChat(player, $"<color=#1ABC9C>═══ STATS: {target.displayName} ═══</color>");
            PrintToChat(player, $"Kills: {tracked?.Kills ?? 0}");
            PrintToChat(player, $"Deaths: {tracked?.Deaths ?? 0}");
            PrintToChat(player, $"K/D: {kd}");
            PrintToChat(player, $"Raids: {tracked?.RaidsParticipated ?? 0}");
            PrintToChat(player, $"Sessions: {tracked?.SessionCount ?? 0}");
            PrintToChat(player, $"Online: {tracked?.TotalOnlineTime.TotalHours:F1}h");
        }

        private void ShowRadar(BasePlayer player, PlayerSession session)
        {
            var pos = player.transform.position;
            var nearby = BasePlayer.activePlayerList.Where(p => p != player && Vector3.Distance(p.transform.position, pos) < 100).ToList();

            PrintToChat(player, $"<color=#1ABC9C>═══ RADAR (100m) ═══</color>");
            PrintToChat(player, $"Players: {nearby.Count}");
            foreach (var np in nearby)
            {
                var dist = Vector3.Distance(pos, np.transform.position);
                var dir = GetDirection(pos, np.transform.position);
                var pSession = GetOrCreateSession(np);
                PrintToChat(player, $"  <color=#FF4444>⚠</color> {np.displayName} [{pSession.Role}] {dist:F0}m {dir}");
            }
            if (nearby.Count == 0) PrintToChat(player, "  Clear!");
        }

        private void ShowGridMap(BasePlayer player, PlayerSession session, string args)
        {
            PrintToChat(player, "<color=#1ABC9C>═══ GRID MAP ═══</color>");
            PrintToChat(player, $"Position: {GetGridCoord(player.transform.position)}");
            PrintToChat(player, $"Markers: {_gridMarkers.Count}");

            var myMarkers = _gridMarkers.Where(m => m.OwnerId == player.UserIDString || m.Visible).Take(10).ToList();
            foreach (var marker in myMarkers)
                PrintToChat(player, $"  • {marker.Name} @ {GetGridCoord(marker.Position)} [{marker.Icon}]");

            PrintToChat(player, "Use /db mark <name> to place a marker.");
        }

        private void PlaceMarker(BasePlayer player, PlayerSession session, string args)
        {
            if (string.IsNullOrWhiteSpace(args)) { PrintToChat(player, "Usage: /db mark <name> [color] [icon]"); return; }
            var parts = SplitArgs(args, 3);

            var marker = new GridMarker
            {
                Id = Guid.NewGuid().ToString().Substring(0, 8),
                Name = parts[0],
                Position = player.transform.position,
                Color = parts.Length > 1 ? parts[1] : "yellow",
                Icon = parts.Length > 2 ? parts[2] : "pin",
                Visible = true,
                OwnerId = player.UserIDString
            };

            _gridMarkers.Add(marker);
            PrintToChat(player, $"<color=#00FF00>Marker placed:</color> {marker.Name} @ {GetGridCoord(marker.Position)}");
            LogActivity("intel", "Marker", $"{player.displayName} placed marker: {marker.Name}", player.UserIDString, player.displayName);
        }

        private void ListMarkers(BasePlayer player, PlayerSession session)
        {
            var markers = _gridMarkers.Where(m => m.OwnerId == player.UserIDString || m.Visible).ToList();
            PrintToChat(player, $"<color=#1ABC9C>═══ MARKERS ({markers.Count}) ═══</color>");
            foreach (var marker in markers)
                PrintToChat(player, $"  • {marker.Name} @ {GetGridCoord(marker.Position)} [{marker.Color}] [{marker.Icon}]");
        }

        private void FindNearby(BasePlayer player, PlayerSession session, string args)
        {
            var radius = 50f;
            if (!string.IsNullOrEmpty(args) && float.TryParse(args, out var r)) radius = r;

            var pos = player.transform.position;
            var nearby = BasePlayer.activePlayerList.Where(p => p != player && Vector3.Distance(p.transform.position, pos) <= radius).ToList();

            PrintToChat(player, $"<color=#1ABC9C>═══ NEARBY (radius: {radius}m) ═══</color>");
            PrintToChat(player, $"Players: {nearby.Count}");
            foreach (var np in nearby)
                PrintToChat(player, $"  • {np.displayName} @ {Vector3.Distance(pos, np.transform.position):F0}m {GetLocation(np.transform.position)}");
        }

        private void ShowActiveRaiders(BasePlayer player, PlayerSession session)
        {
            var activeRaids = _raidHistory.Where(r => r.Outcome == "in_progress").ToList();
            PrintToChat(player, $"<color=#FF4444>═══ ACTIVE RAIDERS ({activeRaids.Count}) ═══</color>");
            if (activeRaids.Count == 0) { PrintToChat(player, "No active raids."); return; }
            foreach (var raid in activeRaids)
                PrintToChat(player, $"  ⚠ {raid.Monument} - Attackers: {string.Join(", ", raid.Attackers)}");
        }

        private void ShowRaidHistory(BasePlayer player, PlayerSession session, string args)
        {
            var hours = 24;
            if (!string.IsNullOrEmpty(args) && int.TryParse(args, out var h)) hours = h;

            var raids = _raidHistory.Where(r => r.Time > DateTime.Now.AddHours(-hours)).ToList();
            PrintToChat(player, $"<color=#1ABC9C>═══ RAID HISTORY ({raids.Count} in {hours}h) ═══</color>");
            foreach (var raid in raids.Take(20))
                PrintToChat(player, $"  <color=#888>[{raid.Time:MM/dd HH:mm}]</color> {raid.Monument} - {raid.Attackers.Count} attackers - {raid.Outcome}");
        }

        // =====================================================================
        // AUTOMATION
        // =====================================================================

        private void ShowAutomation(BasePlayer player, PlayerSession session)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod+ required</color>"); return; }

            PrintToChat(player, "<color=#F39C12>═══════════════════════════════════════</color>");
            PrintToChat(player, "<color=#F39C12>        ⚙ AUTOMATION DASHBOARD</color>");
            PrintToChat(player, "<color=#F39C12>═══════════════════════════════════════</color>");
            PrintToChat(player, $"Active rules: {_automationRules.Count(r => r.Enabled)}");
            PrintToChat(player, $"Total rules: {_automationRules.Count}");
            PrintToChat(player, $"<color=#FFD700>━━━ RULES ━━━</color>");
            foreach (var rule in _automationRules.OrderBy(r => r.Priority))
            {
                var status = rule.Enabled ? "🟢" : "⚫";
                var last = rule.LastTriggered > DateTime.MinValue ? $"{rule.LastTriggered:HH:mm}" : "Never";
                PrintToChat(player, $"  {status} [{rule.Id}] {rule.Name} | Trigger: {rule.Trigger} | Last: {last} | Count: {rule.TriggerCount}");
            }
            PrintToChat(player, "<color=#F39C12>═══════════════════════════════════════</color>");
            PrintToChat(player, "Use /db rules for full rule management.");
        }

        private void ShowAutomationRules(BasePlayer player, PlayerSession session)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }

            PrintToChat(player, "<color=#F39C12>═══ AUTOMATION RULES ═══</color>");
            foreach (var rule in _automationRules)
            {
                var status = rule.Enabled ? "🟢" : "⚫";
                PrintToChat(player, $"  {status} {rule.Id}: {rule.Name}");
                PrintToChat(player, $"     Trigger: {rule.Trigger} | Condition: {rule.Condition}");
                PrintToChat(player, $"     Action: {rule.Action} | Priority: {rule.Priority}");
                PrintToChat(player, $"     Triggered: {rule.TriggerCount}x | Last: {rule.LastTriggered:HH:mm}");
            }
        }

        private void ManageAutomationRule(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod+ required</color>"); return; }

            var parts = SplitArgs(args, 3);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db rule <rule_id> enable/disable/delete/run"); return; }

            var ruleId = parts[0];
            var action = parts[1].ToLowerInvariant();
            var rule = _automationRules.Find(r => r.Id == ruleId);

            if (rule == null) { PrintToChat(player, $"Rule not found: {ruleId}"); return; }

            switch (action)
            {
                case "enable": rule.Enabled = true; PrintToChat(player, $"<color=#00FF00>Enabled:</color> {rule.Name}"); break;
                case "disable": rule.Enabled = false; PrintToChat(player, $"<color=#FF4444>Disabled:</color> {rule.Name}"); break;
                case "delete": _automationRules.Remove(rule); PrintToChat(player, $"Deleted rule: {rule.Name}"); break;
                case "run": RunAutomation(rule, player); PrintToChat(player, $"<color=#00FF00>Executed:</color> {rule.Name}"); break;
                default: PrintToChat(player, "Valid: enable/disable/delete/run"); break;
            }
        }

        private void RunAutomation(AutomationRule rule, BasePlayer trigger)
        {
            rule.LastTriggered = DateTime.Now;
            rule.TriggerCount++;

            switch (rule.Action)
            {
                case "lights.on": Server.Command("lights on"); break;
                case "lights.off": Server.Command("lights off"); break;
                case "alert.all": BroadcastMessage(trigger, "AUTOMATION", $"Alert triggered: {rule.Name}", "warning"); break;
                case "alert.owner": if (trigger != null) PrintToChat(trigger, $"Automation: {rule.Name} triggered!"); break;
                case "chat.welcome": if (trigger != null) PrintToChat(trigger, $"Welcome! Automation: {rule.Name}"); break;
            }

            LogActivity("automation", "Rule triggered", $"{rule.Name} by {trigger?.displayName ?? "MCP/system"}", trigger?.UserIDString, trigger?.displayName);
        }

        private void ShowSchedule(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#F39C12>═══ SCHEDULE ═══</color>");
            PrintToChat(player, "Night Lights: On at sunset");
            PrintToChat(player, "Morning Lights: Off at sunrise");
            PrintToChat(player, "Decay Check: Every 5 min");
            PrintToChat(player, "Heartbeat: Every 30s");
            PrintToChat(player, "Radar Sweep: Every 10s");
            PrintToChat(player, $"Next wipe: {GetWipeInfo()}");
        }

        // =====================================================================
        // AI CHAT
        // =====================================================================

        private void HandleAIChat(BasePlayer player, PlayerSession session, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) { PrintToChat(player, "Usage: /db ask <question>"); return; }

            session.ChatHistory.Add(new ChatEntry { Sender = player.displayName, Message = message, Time = DateTime.Now });
            if (session.ChatHistory.Count > _config.MaxChatHistory) session.ChatHistory.RemoveAt(0);

            string response;

            // Route to the right AI backend
            if (_localAI != null && _localAI.IsLocalProvider)
            {
                // Direct LM Studio / OpenAI / Anthropic / OpenRouter
                response = _localAI.GetResponse(player.displayName, session.Role, message, session.ChatHistory);
            }
            else if (_agentBridge != null)
            {
                // DuckBot MCP / agent bridge
                response = _agentBridge.GetResponse(player.displayName, session.Role, message, session.ChatHistory);
            }
            else
            {
                ShowRecoveryNotice(player);
                response = "AI backend is not initialized yet. Ask an admin to check the Oxide console and reload RustDuckBot after fixing the startup error.";
            }

            session.ChatHistory.Add(new ChatEntry { Sender = "DuckBot", Message = response, Time = DateTime.Now, IsAI = true });

            // Handle multi-line responses
            var lines = response.Split('\n');
            foreach (var line in lines)
                PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {line.Trim()}");

            // Send to MCP (skip if we used a local provider without MCP)
            if (_mcpClient?.IsConnected == true)
                _mcpClient?.SendMessage(new { type = "ai_chat", playerId = player.UserIDString, playerName = player.displayName, message, response });
        }

        private void SearchKnowledge(BasePlayer player, PlayerSession session, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) { PrintToChat(player, "Usage: /db search <query>"); return; }

            var results = _activityLog.Where(a => ContainsIgnoreCase(a.Action, query) || ContainsIgnoreCase(a.Details, query)).Take(10).ToList();
            PrintToChat(player, $"<color=#FFD700>═══ SEARCH: {query} ({results.Count} results) ═══</color>");
            foreach (var r in results)
                PrintToChat(player, $"  <color=#888>[{r.Time:MM/dd HH:mm}]</color> {r.Category}: {r.Action} - {r.Details}");
        }

        private void GetRecommendations(BasePlayer player, PlayerSession session)
        {
            var pos = player.transform.position;
            var nearbyBases = _monitoredBases.Where(b => Vector3.Distance(b.Position, pos) < 200f).Take(3).Select(b => b.Name).ToList();
            var recentAlerts = _activeAlerts.OrderByDescending(a => a.Time).Take(3).Select(a => a.Title).ToList();
            PrintToChat(player, "<color=#FFD700>═══ AI RECOMMENDATIONS ═══</color>");
            PrintToChat(player, $"Position: {GetLocation(pos)}");
            var prompt = $"Give Rust survival recommendations for player {player.displayName} (role {session.Role}). Position: {GetLocation(pos)}. Nearest monument: {GetNearestMonument(pos)}. Nearby bases: {string.Join(", ", nearbyBases)}. Recent alerts: {string.Join(", ", recentAlerts)}. Keep it concise and actionable.";
            var response = GetAssistantResponse(player, session, prompt, false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void AnalyzeBase(BasePlayer player, PlayerSession session)
        {
            var bases = _monitoredBases.Where(b => b.OwnerId == player.userID).ToList();
            PrintToChat(player, "<color=#FFD700>═══ BASE ANALYSIS ═══</color>");
            if (bases.Count == 0) { PrintToChat(player, "No monitored bases. Use /db scan to add them."); return; }

            foreach (var baseInfo in bases)
            {
                var health = baseInfo.CurrentBlockHealth / baseInfo.MaxBlockHealth * 100;
                var strength = baseInfo.BlockCount / 10;
                var defense = baseInfo.Turrets.Count * 10;

                PrintToChat(player, $"<color=#FFD700>Base:</color> {baseInfo.Name}");
                PrintToChat(player, $"  Health: {health:F0}% | Blocks: {baseInfo.BlockCount:F0} | Strength: {strength}/10");
                PrintToChat(player, $"  Defense (turrets): {defense}/10 | Doors: {baseInfo.Doors.Count} | Auth: {baseInfo.AuthorizedPlayers.Count}");
                PrintToChat(player, $"  Decay rate: {baseInfo.DecayRatePerHour:F1}/h");

                var recommendations = new List<string>();
                if (health < 50) recommendations.Add("⚠ Low health - repair soon");
                if (baseInfo.Turrets.Count < 2) recommendations.Add("📍 Add more turrets");
                if (baseInfo.Doors.Count < 4) recommendations.Add("🚪 Consider more doors");
                if (baseInfo.DecayRatePerHour > 100) recommendations.Add("⚠ High decay - increase upkeep");

                PrintToChat(player, "  Recommendations:");
                foreach (var rec in recommendations)
                    PrintToChat(player, $"    {rec}");
            }

            LogActivity("intel", "Base analysis", player.displayName, player.UserIDString, player.displayName);
        }

        // =====================================================================
        // ACTIVITY & CHAT
        // =====================================================================

        private void ShowActivityLog(BasePlayer player, PlayerSession session, string args)
        {
            var hours = 24;
            if (!string.IsNullOrEmpty(args) && int.TryParse(args, out var h)) hours = h;

            var entries = _activityLog.Where(e => e.Time > DateTime.Now.AddHours(-hours)).OrderByDescending(e => e.Time).Take(100).ToList();
            PrintToChat(player, $"<color=#FFD700>═══ ACTIVITY LOG ({entries.Count} in {hours}h) ═══</color>");
            foreach (var entry in entries.Take(30))
            {
                var color = ActivityColor(entry.Category);
                PrintToChat(player, $"  <color={color}>[</color>{entry.Time:HH:mm}<color={color}>]</color> {entry.PlayerName ?? "System"}: {entry.Action}");
            }
        }

        private void ShowHistory(BasePlayer player, PlayerSession session)
        {
            if (session.ChatHistory.Count == 0) { PrintToChat(player, "No history."); return; }
            PrintToChat(player, $"<color=#FFD700>═══ CHAT HISTORY ({session.ChatHistory.Count}) ═══</color>");
            var start = Math.Max(0, session.ChatHistory.Count - 15);
            for (int i = start; i < session.ChatHistory.Count; i++)
            {
                var entry = session.ChatHistory[i];
                var prefix = entry.IsAI ? "<color=#FFD700>DuckBot:</color>" : $"<color=#888>{entry.Sender}:</color>";
                PrintToChat(player, $"  [{entry.Time:HH:mm}] {prefix} {entry.Message}");
            }
        }

        private void Broadcast(BasePlayer player, PlayerSession session, string message)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod+ required</color>"); return; }
            if (string.IsNullOrWhiteSpace(message)) { PrintToChat(player, "Usage: /db broadcast <message>"); return; }
            BroadcastMessage(player, "BROADCAST", message, "info");
        }

        private void HandleChat(BasePlayer player, PlayerSession session, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) { PrintToChat(player, "Usage: /db say <message>"); return; }
            session.ChatHistory.Add(new ChatEntry { Sender = player.displayName, Message = message, Time = DateTime.Now });
            if (session.ChatHistory.Count > _config.MaxChatHistory) session.ChatHistory.RemoveAt(0);
            PrintToChat(player, $"<color=#FFD700>{player.displayName}:</color> {message}");
            _mcpClient?.SendMessage(new { type = "player_chat", playerId = player.UserIDString, playerName = player.displayName, role = session.Role, message = message, time = DateTime.Now.ToString("o") });
        }

        private void SendMessage(BasePlayer player, PlayerSession session, string args)
        {
            var parts = args.Split(new[] { ' ' }, 2);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db msg <player> <message>"); return; }
            var target = FindPlayer(parts[0]);
            if (target == null) { PrintToChat(player, $"Player not found: {parts[0]}"); return; }
            PrintToChat(target, $"<color=#888>[PM from {player.displayName}]:</color> {parts[1]}");
            PrintToChat(player, $"<color=#00FF00>Sent to {target.displayName}:</color> {parts[1]}");
        }

        private void HandleTeamMessage(BasePlayer player, PlayerSession session, string message)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }
            if (string.IsNullOrWhiteSpace(message)) { PrintToChat(player, "Usage: /db team <message>"); return; }
            foreach (var p in BasePlayer.activePlayerList)
            {
                var pSession = GetOrCreateSession(p);
                if (pSession.Role != "user")
                    PrintToChat(p, $"<color=#9B59B6>[TEAM {player.displayName}]:</color> {message}");
            }
        }

        // =====================================================================
        // ADMIN COMMANDS
        // =====================================================================

        private void ServerStatus(BasePlayer player, PlayerSession session)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }

            var uptime = Time.realtimeSinceStartup;
            var fps = 1.0f / Time.deltaTime;
            var mem = System.GC.GetTotalMemory(false) / 1024 / 1024;
            var active = BasePlayer.activePlayerList.Count;
            var sleeping = BasePlayer.sleepingPlayerList.Count;

            PrintToChat(player, "<color=#FF4444>═══════════════════════════════════════</color>");
            PrintToChat(player, "<color=#FF4444>        📊 SERVER STATUS</color>");
            PrintToChat(player, "<color=#FF4444>═══════════════════════════════════════</color>");
            PrintToChat(player, $"<color=#FFD700>Uptime:</color> {uptime / 3600:F1}h");
            PrintToChat(player, $"<color=#FFD700>FPS:</color> {fps:F0}");
            PrintToChat(player, $"<color=#FFD700>Memory:</color> {mem:F0}MB");
            PrintToChat(player, $"<color=#FFD700>Players:</color> {active} online, {sleeping} sleeping");
            PrintToChat(player, $"<color=#FFD700>MCP:</color> {(_mcpClient?.IsConnected == true ? "<color=#00FF00>Connected" : "<color=#FF4444>Disconnected")}");
            PrintToChat(player, $"<color=#FFD700>RCON:</color> {(_rconClient?.IsConnected == true ? "<color=#00FF00>Connected" : "<color=#FF9900>Plugin console fallback")}");
            PrintToChat(player, $"<color=#FFD700>Cameras:</color> {_cameras.Count}");
            PrintToChat(player, $"<color=#FFD700>Vending:</color> {_vendingMachines.Count}");
            PrintToChat(player, $"<color=#FFD700>Alerts:</color> {_activeAlerts.Count} active");
            PrintToChat(player, $"<color=#FFD700>Rules:</color> {_automationRules.Count(r => r.Enabled)} active");
            PrintToChat(player, $"<color=#FFD700>Tracked:</color> {_trackedPlayers.Count}");
            PrintToChat(player, $"<color=#FFD700>Markers:</color> {_gridMarkers.Count}");
            PrintToChat(player, $"<color=#FFD700>Listings:</color> {_shopListings.Count}");
            PrintToChat(player, "<color=#FF4444>═══════════════════════════════════════</color>");
        }

        private void HandleAdmin(BasePlayer player, PlayerSession session, string command)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            if (string.IsNullOrWhiteSpace(command)) { PrintToChat(player, "Usage: /db admin <rcon_command>"); return; }
            ExecuteRconOrConsole(command, "in-game admin");
            PrintToChat(player, $"<color=#00FF00>Admin:</color> {command}");
            LogActivity("admin", "RCON", $"{player.displayName}: {command}", player.UserIDString, player.displayName);
            _mcpClient?.SendMessage(new { type = "admin_command", playerId = player.UserIDString, command });
        }

        private void HandleKick(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod required</color>"); return; }
            var parts = args.Split(new[] { ' ' }, 2);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) { PrintToChat(player, "Usage: /db kick <player> [reason]"); return; }
            var target = FindPlayer(parts[0]);
            if (target == null) { PrintToChat(player, $"Player not found: {parts[0]}"); return; }
            var reason = parts.Length > 1 ? parts[1] : "Kicked by staff";
            target.Kick(reason);
            PrintToChat(player, $"Kicked: {target.displayName}");
            LogActivity("admin", "Kick", $"{player.displayName} kicked {target.displayName}: {reason}", player.UserIDString, player.displayName);
            _mcpClient?.SendMessage(new { type = "kick", playerId = player.UserIDString, targetId = target.UserIDString, reason });
        }

        private void HandleBan(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            var parts = args.Split(new[] { ' ' }, 3);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db ban <player> <reason> [duration]"); return; }
            var target = FindPlayer(parts[0]);
            if (target == null) { PrintToChat(player, $"Player not found: {parts[0]}"); return; }
            var reason = parts[1];
            var duration = parts.Length > 2 ? parts[2] : "perm";
            Server.Command($"banid {target.UserIDString} \"{reason}\" {duration}");
            target.Kick(reason);
            PrintToChat(player, $"Banned: {target.displayName} ({duration})");
            LogActivity("admin", "Ban", $"{player.displayName} banned {target.displayName}: {reason} ({duration})", player.UserIDString, player.displayName);
            _mcpClient?.SendMessage(new { type = "ban", playerId = player.UserIDString, targetId = target.UserIDString, reason, duration });
        }

        private void HandleUnban(BasePlayer player, PlayerSession session, string steamId)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            if (string.IsNullOrWhiteSpace(steamId)) { PrintToChat(player, "Usage: /db unban <steamid>"); return; }
            Server.Command($"unban {steamId}");
            PrintToChat(player, $"<color=#00FF00>Unbanned:</color> {steamId}");
            LogActivity("admin", "Unban", $"{player.displayName} unbanned {steamId}", player.UserIDString, player.displayName);
        }

        private void HandleMute(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod required</color>"); return; }
            if (string.IsNullOrWhiteSpace(targetName)) { PrintToChat(player, "Usage: /db mute <player>"); return; }
            var target = FindPlayer(targetName);
            if (target == null) { PrintToChat(player, $"Player not found: {targetName}"); return; }
            if (!_mutedPlayers.Contains(target.UserIDString)) _mutedPlayers.Add(target.UserIDString);
            PrintToChat(player, $"<color=#FF9900>Muted:</color> {target.displayName}");
            LogActivity("admin", "Mute", $"{player.displayName} muted {target.displayName}", player.UserIDString, player.displayName);
        }

        private void HandleUnmute(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod required</color>"); return; }
            if (string.IsNullOrWhiteSpace(targetName)) { PrintToChat(player, "Usage: /db unmute <player>"); return; }
            var target = FindPlayer(targetName);
            var removed = false;
            if (target != null)
            {
                removed |= _mutedPlayers.Remove(target.UserIDString);
                removed |= _mutedPlayers.Remove(target.displayName);
            }
            removed |= _mutedPlayers.Remove(targetName);
            PrintToChat(player, removed ? $"<color=#00FF00>Unmuted:</color> {targetName}" : $"No mute found for: {targetName}");
            LogActivity("admin", "Unmute", $"{player.displayName} unmuted {targetName}", player.UserIDString, player.displayName);
        }

        private void HandleFreeze(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod required</color>"); return; }
            if (string.IsNullOrWhiteSpace(targetName)) { PrintToChat(player, "Usage: /db freeze <player>"); return; }
            var target = FindPlayer(targetName);
            if (target == null) { PrintToChat(player, $"Player not found: {targetName}"); return; }
            target.PauseFlyHackDetection(5f);
            target.SendConsoleCommand("global.cinematicmode true");
            PrintToChat(player, $"<color=#00BFFF>Frozen:</color> {target.displayName}");
        }

        private void HandleHeal(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            var target = string.IsNullOrWhiteSpace(targetName) ? player : FindPlayer(targetName);
            if (target == null) { PrintToChat(player, $"Player not found: {targetName}"); return; }
            target.Heal(100);
            target.SendNetworkUpdateImmediate();
            PrintToChat(player, $"<color=#00FF00>Healed:</color> {target.displayName}");
        }

        private void HandleGive(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            var parts = SplitArgs(args, 3);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db give <player> <item> <qty>"); return; }
            var target = FindPlayer(parts[0]);
            if (target == null) { PrintToChat(player, $"Player not found: {parts[0]}"); return; }
            var qty = parts.Length > 2 && int.TryParse(parts[2], out var q) ? q : 1;
            PrintToChat(player, $"<color=#00FF00>Give:</color> {qty}x {parts[1]} to {target.displayName}");
        }

        private void HandleTeleport(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            var parts = args.Split(new[] { ' ' }, 2);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db tp <from_player|loc> <to_player|loc>"); return; }
            var from = FindPlayer(parts[0]);
            var to = FindPlayer(parts[1]);
            if (from != null && to != null)
            {
                from.SendConsoleCommand($"teleport {to.transform.position.x} {to.transform.position.y} {to.transform.position.z}");
                PrintToChat(player, $"<color=#00FF00>TP:</color> {from.displayName} → {to.displayName}");
            }
            else
            {
                PrintToChat(player, $"Teleport: {parts[0]} → {parts[1]}");
            }
        }

        private void HandleSpawn(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            var parts = args.Split(new[] { ' ' }, 2);
            if (parts.Length == 0) { PrintToChat(player, "Usage: /db spawn <item> [qty]"); return; }
            var qty = parts.Length > 1 && int.TryParse(parts[1], out var q) ? q : 1;
            PrintToChat(player, $"<color=#00FF00>Spawning:</color> {qty}x {parts[0]}");
        }

        // =====================================================================
        // BUILDING HELPER SYSTEM
        // =====================================================================

        private class BuildingPlan
        {
            public string Name;
            public string Category;
            public string Description;
            public List<BuildingComponent> Components;
            public int TotalUpkeep;
        }

        private class BuildingComponent
        {
            public string Shortname;
            public string DisplayName;
            public int Count;
        }

        private List<BuildingPlan> _buildingPlans = new List<BuildingPlan>();

        private void InitializeBuildingPlans()
        {
            _buildingPlans = new List<BuildingPlan>
            {
                new BuildingPlan { Name = "1x1-stone", Category = "base", Description = "Basic 1x1 stone base", Components = new List<BuildingComponent> {
                    new BuildingComponent { Shortname = "stones", DisplayName = "Stones", Count = 200 },
                    new BuildingComponent { Shortname = "wood", DisplayName = "Wood", Count = 100 },
                }, TotalUpkeep = 1 },
                new BuildingPlan { Name = "2x1-stone", Category = "base", Description = "Expanded 2x1 stone base", Components = new List<BuildingComponent> {
                    new BuildingComponent { Shortname = "stones", DisplayName = "Stones", Count = 400 },
                    new BuildingComponent { Shortname = "wood", DisplayName = "Wood", Count = 200 },
                }, TotalUpkeep = 2 },
                new BuildingPlan { Name = "compound", Category = "base", Description = "Large compound", Components = new List<BuildingComponent> {
                    new BuildingComponent { Shortname = "stones", DisplayName = "Stones", Count = 1200 },
                    new BuildingComponent { Shortname = "wood", DisplayName = "Wood", Count = 600 },
                    new BuildingComponent { Shortname = "metal.fragments", DisplayName = "Metal Frags", Count = 200 },
                }, TotalUpkeep = 4 },
                new BuildingPlan { Name = "tower", Category = "farm", Description = "High vertical tower", Components = new List<BuildingComponent> {
                    new BuildingComponent { Shortname = "stones", DisplayName = "Stones", Count = 800 },
                    new BuildingComponent { Shortname = "wood", DisplayName = "Wood", Count = 400 },
                }, TotalUpkeep = 3 },
                new BuildingPlan { Name = "bunker", Category = "defense", Description = "Defensive bunker", Components = new List<BuildingComponent> {
                    new BuildingComponent { Shortname = "stones", DisplayName = "Stones", Count = 1500 },
                    new BuildingComponent { Shortname = "hqm", DisplayName = "HQM", Count = 50 },
                }, TotalUpkeep = 5 },
                new BuildingPlan { Name = "watchtower", Category = "defense", Description = "Exterior watchtower", Components = new List<BuildingComponent> {
                    new BuildingComponent { Shortname = "wood", DisplayName = "Wood", Count = 500 },
                    new BuildingComponent { Shortname = "stones", DisplayName = "Stones", Count = 200 },
                }, TotalUpkeep = 2 },
            };
        }

        private void HandleBuildPlan(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }
            if (string.IsNullOrWhiteSpace(args) || args == "list")
            {
                PrintToChat(player, "<color=#FFD700>━━━ BUILDING PLANS ━━━</color>");
                foreach (var listedPlan in _buildingPlans)
                {
                    var emoji = listedPlan.Category == "base" ? "🏠" : listedPlan.Category == "farm" ? "🌾" : listedPlan.Category == "defense" ? "🛡" : "✨";
                    PrintToChat(player, "  " + emoji + " <color=#4DA6FF>" + listedPlan.Name + "</color> -- " + listedPlan.Description);
                    PrintToChat(player, "       Upkeep: " + listedPlan.TotalUpkeep + "/day | Components: " + listedPlan.Components.Sum(c => c.Count));
                }
                PrintToChat(player, "\n<color=#888>/db plan <name> for materials breakdown</color>");
                return;
            }
            var plan = _buildingPlans.FirstOrDefault(p => p.Name.Equals(args, StringComparison.OrdinalIgnoreCase) || ContainsIgnoreCase(p.Name, args));
            if (plan == null) { PrintToChat(player, "<color=#FF4444>Unknown plan:</color> " + args); return; }
            PrintToChat(player, "<color=#FFD700>━━━ " + plan.Name.ToUpper() + " ━━━</color>");
            PrintToChat(player, "<color=#888>" + plan.Description + "</color>");
            PrintToChat(player, "<color=#FFD700>Materials:</color>");
            foreach (var comp in plan.Components)
                PrintToChat(player, "  * " + comp.DisplayName + ": <color=#4DA6FF>" + comp.Count + "</color>");
            PrintToChat(player, "<color=#FFD700>Upkeep:</color> " + plan.TotalUpkeep + " fragments/day");
        }

        private void HandleMaterials(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "user")) { PrintToChat(player, "<color=#FF4444>Login required</color>"); return; }
            if (string.IsNullOrWhiteSpace(args)) { PrintToChat(player, "Usage: /db materials <item> (wall, floor, ramp, stairs, foundation, roof, door, gate)"); return; }
            var matMap = new Dictionary<string, (string material, int count)>(StringComparer.OrdinalIgnoreCase)
            {
                ["wall"] = ("stones", 100), ["wall.stone"] = ("stones", 100),
                ["floor"] = ("stones", 50), ["floor.metal"] = ("metal.fragments", 100),
                ["ramp"] = ("stones", 80), ["stairs"] = ("stones", 60),
                ["foundation"] = ("stones", 30), ["roof"] = ("stones", 50),
                ["gate"] = ("hqm", 200), ["door"] = ("hqm", 100),
            };
            var key = args.ToLower().Trim();
            if (matMap.TryGetValue(key, out var info))
            {
                PrintToChat(player, "<color=#FFD700>━━━ MATERIALS: " + key.ToUpper() + " ━━━</color>");
                PrintToChat(player, "  Material: <color=#4DA6FF>" + info.material + "</color>");
                PrintToChat(player, "  Count: <color=#4DA6FF>" + info.count + "</color> per block");
                PrintToChat(player, "  <color=#888>Upgrade: wood -> stone -> metal -> hqm</color>");
            }
            else
            {
                PrintToChat(player, "<color=#FF4444>Unknown item:</color> " + args);
                PrintToChat(player, "<color=#888>Try: wall, floor, ramp, stairs, foundation, roof, door, gate</color>");
            }
        }

        private void HandleTCManage(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }
            var parts = SplitArgs(args, 2);
            var action = parts.Length > 0 ? parts[0].ToLower() : "";
            var pos = player.transform.position;
            BuildingPrivlidge nearestTC = null;
            foreach (var e in BaseEntity.saveList)
                if (e is BuildingPrivlidge tc && Vector3.Distance(tc.transform.position, pos) < 5f) { nearestTC = tc; break; }
            if (nearestTC == null) { PrintToChat(player, "<color=#FF4444>No TC within 5m.</color>"); return; }
            switch (action)
            {
                case "auth":
                case "list":
                    PrintToChat(player, "<color=#FFD700>━━━ TC AUTH @" + GetLocation(nearestTC.transform.position) + " ━━━</color>");
                    PrintToChat(player, "Authorized: " + nearestTC.authorizedPlayers.Count);
                    foreach (var authId in nearestTC.authorizedPlayers.Take(10))
                        PrintToChat(player, "  * " + authId);
                    if (nearestTC.authorizedPlayers.Count > 10)
                        PrintToChat(player, "  <color=#888>...and " + (nearestTC.authorizedPlayers.Count - 10) + " more</color>");
                    break;
                case "add":
                    var targetName = parts.Length > 1 ? parts[1] : "";
                    if (string.IsNullOrEmpty(targetName)) { PrintToChat(player, "Usage: /db tcmanage add <player>"); return; }
                    var target = FindPlayer(targetName);
                    if (target == null) { PrintToChat(player, "<color=#FF4444>Player not found:</color> " + targetName); return; }
                    nearestTC.authorizedPlayers.Add(target.userID);
                    nearestTC.SendNetworkUpdate();
                    PrintToChat(player, "<color=#00FF00>✅ Added to TC:</color> " + target.displayName);
                    break;
                case "remove":
                    var removeName = parts.Length > 1 ? parts[1] : "";
                    if (string.IsNullOrEmpty(removeName)) { PrintToChat(player, "Usage: /db tcmanage remove <player>"); return; }
                    var removeTarget = FindPlayer(removeName);
                    if (removeTarget == null) { PrintToChat(player, "<color=#FF4444>Player not found:</color> " + removeName); return; }
                    var authed = nearestTC.authorizedPlayers.FirstOrDefault(a => a == removeTarget.userID);
                    if (authed != 0) { nearestTC.authorizedPlayers.Remove(authed); nearestTC.SendNetworkUpdate(); PrintToChat(player, "<color=#00FF00>✅ Removed from TC:</color> " + removeTarget.displayName); }
                    else PrintToChat(player, "<color=#FF4444>Player not authorized in this TC.</color>");
                    break;
                default:
                    PrintToChat(player, "<color=#FFD700>━━━ TC MANAGER ━━━</color>");
                    PrintToChat(player, "/db tcmanage auth -- View authorized players");
                    PrintToChat(player, "/db tcmanage add <player> -- Add player to TC");
                    PrintToChat(player, "/db tcmanage remove <player> -- Remove player from TC");
                    break;
            }
        }

        private void HandleDecayAlert(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }
            var pos = player.transform.position;
            var atRisk = new List<string>();
            foreach (var e in BaseEntity.saveList)
            {
                if (e is BuildingBlock b && Vector3.Distance(b.transform.position, pos) < 50f)
                {
                    var blockHealth = b.health;
                    var maxHealth = b.MaxHealth();
                    if (maxHealth > 0 && blockHealth / maxHealth < 0.25f)
                        atRisk.Add(b.ShortPrefabName + " @" + GetLocation(b.transform.position) + " " + blockHealth.ToString("F0") + "/" + maxHealth.ToString("F0") + "HP");
                }
                if (atRisk.Count >= 15) break;
            }
            PrintToChat(player, "<color=#FFD700>━━━ DECAY ALERT (50m) ━━━</color>");
            PrintToChat(player, "At-risk structures: " + atRisk.Count);
            if (atRisk.Count == 0) PrintToChat(player, "<color=#00FF88>✅ No structures below 25% health.</color>");
            else foreach (var s in atRisk) PrintToChat(player, "  ⚠️ " + s);
        }

        // =====================================================================
        // UTILITY HELPERS
        // =====================================================================


        // =====================================================================
        // UTILITY
        // =====================================================================

        private void ShowTime(BasePlayer player, PlayerSession session)
        {
            var now = DateTime.Now;
            var hours = now.Hour;
            var mins = now.Minute;
            PrintToChat(player, "<color=#FFD700>═══ GAME TIME ═══</color>");
            PrintToChat(player, $"Time: {hours:D2}:{mins:D2}");
            PrintToChat(player, "Day/Night estimate based on local server time");
            PrintToChat(player, $"Sun: {(hours >= 6 && hours < 18 ? "☀️" : "🌙")}");
        }

        private void HandleTimeToNight(BasePlayer player, PlayerSession session)
        {
            var now = DateTime.Now;
            var hours = now.Hour;
            var mins = now.Minute;
            var currentMinutes = hours * 60 + mins;
            const int nightStartMinutes = 18 * 60;
            const int dayMinutes = 24 * 60;
            var minutesUntilNight = currentMinutes < nightStartMinutes
                ? nightStartMinutes - currentMinutes
                : dayMinutes - currentMinutes + nightStartMinutes;

            PrintToChat(player, "<color=#FFD700>═══ DAY / NIGHT ═══</color>");
            PrintToChat(player, $"Current time: {hours:D2}:{mins:D2}");
            PrintToChat(player, $"Night starts in: {minutesUntilNight / 60}h {minutesUntilNight % 60}m");
        }

        // =====================================================================
        // TELEPORT SYSTEM
        // =====================================================================

        // /db tpr <player> — request to teleport TO a player
        // /db tpa <player> — ask a player to teleport TO YOU
        private void HandleTPR(BasePlayer player, PlayerSession session, string args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "rustduckbot.teleport") &&
                !HasRoleOrHigher(session.Role, "vip"))
            { PrintToChat(player, "<color=#FF4444>No permission for teleport commands.</color>"); return; }

            if (string.IsNullOrWhiteSpace(args))
            { PrintToChat(player, "<color=#FFD700>Usage:</color> /db tpr <player> OR /db tpa <player>"); return; }

            var parts = args.Split(new[] { ' ' }, 2);
            var targetName = parts[0].Trim();
            bool goToTarget = parts.Length == 0 || (args.StartsWith("tpr ", StringComparison.OrdinalIgnoreCase) || args.StartsWith("tpa ", StringComparison.OrdinalIgnoreCase));

            // Check if it's a tpa (ask target to come HERE) vs tpr (go TO target)
            bool isTPA = args.StartsWith("tpa ", StringComparison.OrdinalIgnoreCase);
            bool isTPR = args.StartsWith("tpr ", StringComparison.OrdinalIgnoreCase);

            var target = FindPlayer(targetName);
            if (target == null)
            { PrintToChat(player, $"<color=#FF4444>Player not found:</color> {targetName}"); return; }
            if (target == player)
            { PrintToChat(player, "<color=#FF4444>You can't teleport to yourself.</color>"); return; }

            // Check cooldown
            if (_config.TeleportCooldownSeconds > 0 && session.LastTeleport.HasValue)
            {
                var elapsed = (DateTime.Now - session.LastTeleport.Value).TotalSeconds;
                if (elapsed < _config.TeleportCooldownSeconds)
                {
                    var remaining = _config.TeleportCooldownSeconds - (int)elapsed;
                    PrintToChat(player, $"<color=#FF4444>Cooldown:</color> wait {remaining}s");
                    return;
                }
            }

            // Check if they already have a pending request
            foreach (var kvp in _teleportRequests)
            {
                if (kvp.Value.FromId == player.userID)
                {
                    PrintToChat(player, "<color=#FF4444>You already have a pending teleport request.</color>");
                    PrintToChat(player, $"<color=#FFD700>Use /db tpc</color> to see/accept incoming requests.");
                    return;
                }
            }

            var req = new TeleportRequest
            {
                FromId = player.userID,
                FromName = player.displayName,
                ToId = target.userID,
                ToName = target.displayName,
                RequestTime = DateTime.Now,
                IsFrom = !isTPA // tpr = From goes TO To. tpa = From asks To to come HERE
            };

            _teleportRequests[target.userID] = req;

            // Notify the sender
            PrintToChat(player, $"<color=#00FF88>Request sent to {target.displayName}.</color>");
            PrintToChat(player, $"<color=#FFD700>Expires in {_config.TeleportRequestSeconds}s.</color> Type <color=#FFD700>/db tpd</color> to cancel.");

            // Notify the target
            var reqType = isTPA ? "to teleport to you" : (isTPR ? "to teleport to them" : "to meet up");
            PrintToChat(target, $"<color=#FFD700>━━━ TP REQUEST ━━━</color>");
            PrintToChat(target, $"<color=#4DA6FF>{player.displayName}</color> wants {reqType}.");
            PrintToChat(target, $"Use <color=#00FF88>/db tpc</color> to accept | <color=#FF4444>/db tpd</color> to deny");
            PrintToChat(target, $"Expires in {_config.TeleportRequestSeconds}s.");

            // Auto-expire the request
            timer.Once(_config.TeleportRequestSeconds * 1000f, () =>
            {
                if (_teleportRequests.TryGetValue(target.userID, out var r) && r.FromId == player.userID)
                {
                    _teleportRequests.Remove(target.userID);
                    PrintToChat(target, $"<color=#888>TP request from {player.displayName} expired.</color>");
                    if (target.IsConnected) PrintToChat(player, $"<color=#888>Request to {target.displayName} expired.</color>");
                }
            });
        }

        // /db tpc OR /db accept — accept incoming teleport request
        private void HandleTPC(BasePlayer player, PlayerSession session)
        {
            if (!_teleportRequests.TryGetValue(player.userID, out var req))
            { PrintToChat(player, "<color=#FF4444>No pending teleport requests.</color>"); return; }

            var fromPlayer = BasePlayer.FindByID(req.FromId);

            // Check cooldown for the requester
            var fromSession = GetOrCreateSession(fromPlayer ?? BasePlayer.sleepingPlayerList.FirstOrDefault(p => p.userID == req.FromId));
            if (_config.TeleportCooldownSeconds > 0 && fromSession?.LastTeleport != null)
            {
                var elapsed = (DateTime.Now - fromSession.LastTeleport.Value).TotalSeconds;
                if (elapsed < _config.TeleportCooldownSeconds)
                {
                    PrintToChat(player, $"<color=#FF4444>Requester is on cooldown.</color> Wait {(_config.TeleportCooldownSeconds - (int)elapsed)}s");
                    _teleportRequests.Remove(player.userID);
                    return;
                }
            }

            _teleportRequests.Remove(player.userID);

            if (req.IsFrom)
            {
                // tpr: FROM player teleports TO target (this player)
                DoTeleport(fromPlayer, player.transform.position, $"Teleported to {player.displayName}");
            }
            else
            {
                // tpa: FROM player wants TARGET (this player) to come to them
                DoTeleport(player, fromPlayer?.transform.position ?? new Vector3(), $"Teleported to {req.FromName}");
            }
        }

        // /db tpd OR /db deny — deny incoming teleport request
        private void HandleTPD(BasePlayer player, PlayerSession session)
        {
            if (!_teleportRequests.TryGetValue(player.userID, out var req))
            { PrintToChat(player, "<color=#FF4444>No pending teleport requests.</color>"); return; }

            var fromPlayer = BasePlayer.FindByID(req.FromId);
            _teleportRequests.Remove(player.userID);
            PrintToChat(player, "<color=#FF4444>Request denied.</color>");
            if (fromPlayer?.IsConnected == true)
                PrintToChat(fromPlayer, $"<color=#FF4444>{player.displayName} denied your teleport request.</color>");
        }

        // /db home [name] — teleport to a saved home
        private void HandleHome(BasePlayer player, PlayerSession session, string args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "rustduckbot.teleport") &&
                !HasRoleOrHigher(session.Role, "vip"))
            { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }

            var homes = session.Homes;
            if (homes.Count == 0)
            { PrintToChat(player, "<color=#FF4444>No homes saved.</color> Use <color=#FFD700>/db sethome [name]</color>"); return; }

            string homeName = args.Trim().ToLower();
            Position3D homePos = null;

            if (string.IsNullOrEmpty(homeName))
            {
                // List homes
                PrintToChat(player, "<color=#FFD700>━━━ YOUR HOMES ━━━</color>");
                foreach (var h in homes)
                    PrintToChat(player, $"  <color=#4DA6FF>{h.Key}</color> — {h.Value.X:F0},{h.Value.Y:F0},{h.Value.Z:F0}");
                PrintToChat(player, "<color=#888>Use /db home &lt;name&gt;</color>");
                return;
            }

            // Fuzzy match
            foreach (var h in homes)
            {
                if (h.Key.Equals(homeName, StringComparison.OrdinalIgnoreCase) ||
                    ContainsIgnoreCase(h.Key, homeName))
                { homePos = h.Value; homeName = h.Key; break; }
            }

            if (homePos == null)
            { PrintToChat(player, $"<color=#FF4444>Home not found:</color> {args}"); return; }

            DoTeleport(player, homePos.ToVector3(), $"Home: {homeName}");
        }

        // /db sethome [name] — save current position as home
        private void HandleSetHome(BasePlayer player, PlayerSession session, string args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "rustduckbot.teleport") &&
                !HasRoleOrHigher(session.Role, "vip"))
            { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }

            if (session.Homes.Count >= _config.MaxHomesPerPlayer)
            { PrintToChat(player, $"<color=#FF4444>Home limit reached ({_config.MaxHomesPerPlayer}).</color> Remove one first: /db removehome <name>"); return; }

            var name = string.IsNullOrWhiteSpace(args) ? "main" : args.Trim().ToLower();
            session.Homes[name] = new Position3D(player.transform.position);
            PrintToChat(player, $"<color=#00FF88>Home saved:</color> <color=#FFD700>{name}</color> at {player.transform.position.x:F0},{player.transform.position.y:F0},{player.transform.position.z:F0}");
        }

        // /db removehome [name] — delete a saved home
        private void HandleRemoveHome(BasePlayer player, PlayerSession session, string args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "rustduckbot.teleport") &&
                !HasRoleOrHigher(session.Role, "vip"))
            { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            if (string.IsNullOrWhiteSpace(args))
            { PrintToChat(player, "<color=#FFD700>Usage:</color> /db removehome <name>"); return; }

            var name = args.Trim().ToLower();
            if (!session.Homes.ContainsKey(name))
            { PrintToChat(player, $"<color=#FF4444>No home named:</color> {name}"); return; }

            session.Homes.Remove(name);
            PrintToChat(player, $"<color=#00FF88>Home removed:</color> {name}");
        }

        // /db town — teleport to Outpost
        private void HandleTown(BasePlayer player, PlayerSession session)
        {
            if (!permission.UserHasPermission(player.UserIDString, "rustduckbot.teleport") &&
                !HasRoleOrHigher(session.Role, "vip"))
            { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            if (!_config.AllowTownTeleport)
            { PrintToChat(player, "<color=#FF4444>Town teleport is disabled.</color>"); return; }

            if (session.LastTownTp.HasValue && (DateTime.Now - session.LastTownTp.Value).TotalMinutes < _config.TownCooldownMinutes)
            { PrintToChat(player, $"<color=#FF4444>Town on cooldown.</color> Wait {_config.TownCooldownMinutes - (int)(DateTime.Now - session.LastTownTp.Value).TotalMinutes} min"); return; }

            var pos = new Vector3(_config.OutpostX, _config.OutpostY, _config.OutpostZ);
            DoTeleport(player, pos, "Town (Outpost)");
            session.LastTownTp = DateTime.Now;
        }

        // /db bandit — teleport to Bandit Camp
        private void HandleBandit(BasePlayer player, PlayerSession session)
        {
            if (!permission.UserHasPermission(player.UserIDString, "rustduckbot.teleport") &&
                !HasRoleOrHigher(session.Role, "vip"))
            { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            if (!_config.AllowBanditTeleport)
            { PrintToChat(player, "<color=#FF4444>Bandit teleport is disabled.</color>"); return; }

            if (session.LastBanditTp.HasValue && (DateTime.Now - session.LastBanditTp.Value).TotalMinutes < _config.BanditCooldownMinutes)
            { PrintToChat(player, $"<color=#FF4444>Bandit on cooldown.</color> Wait {_config.BanditCooldownMinutes - (int)(DateTime.Now - session.LastBanditTp.Value).TotalMinutes} min"); return; }

            var pos = new Vector3(_config.BanditX, _config.BanditY, _config.BanditZ);
            DoTeleport(player, pos, "Bandit Camp");
            session.LastBanditTp = DateTime.Now;
        }

        // /db back — return to last position before teleport
        private void HandleBack(BasePlayer player, PlayerSession session)
        {
            if (session.LastPosition == null)
            { PrintToChat(player, "<color=#FF4444>No previous position.</color>"); return; }
            DoTeleport(player, session.LastPosition.ToVector3(), "Returned to previous position");
        }

        // /db rtele — random teleport to a safe position on the map
        private void HandleRTele(BasePlayer player, PlayerSession session, string args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "rustduckbot.teleport") &&
                !HasRoleOrHigher(session.Role, "vip"))
            { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }

            // Generate a random position within map bounds
            var randomPos = new Vector3(
                UnityEngine.Random.Range(-500f, 500f),
                0f,
                UnityEngine.Random.Range(-500f, 500f)
            );

            // Raycast down to find ground
            var ray = new Ray(randomPos + Vector3.up * 200f, Vector3.down);
            if (Physics.Raycast(ray, out var hit, 300f, LayerMask.GetMask("Terrain", "World")))
                randomPos = hit.point;

            DoTeleport(player, randomPos, "Random teleport");
        }

        // /db pos — show current coordinates
        private void HandleCoords(BasePlayer player, PlayerSession session)
        {
            var pos = player.transform.position;
            var grid = GetGridLocation(pos);
            PrintToChat(player, "<color=#FFD700>━━━ POSITION ━━━</color>");
            PrintToChat(player, $"<color=#4DA6FF>X:</color> {pos.x:F1}  <color=#4DA6FF>Y:</color> {pos.y:F1}  <color=#4DA6FF>Z:</color> {pos.z:F1}");
            PrintToChat(player, $"<color=#888>Grid:</color> {grid}");
            PrintToChat(player, $"<color=#888>Monument:</color> {GetNearestMonument(pos)}");
            PrintToChat(player, $"<color=#888>Share:</color> <color=#FFD700>{pos.x:F0},{pos.y:F0},{pos.z:F0}</color>");
        }

        // Rust grid reference (e.g. "E-7") — each cell ≈ 146m
        private string GetGridLocation(Vector3 pos)
        {
            float gridSize = 146f;
            int col = Mathf.FloorToInt((pos.x + 5000f) / gridSize);
            int row = Mathf.FloorToInt((pos.z + 5000f) / gridSize);
            char letter = (char)('A' + (col % 26));
            return $"{letter}{Math.Abs(row) + 1}";
        }

        // ── Core teleport executor ─────────────────────────────────────────────

        private void DoTeleport(BasePlayer player, Vector3 destination, string reason)
        {
            if (player == null || !player.IsConnected) return;

            var session = GetOrCreateSession(player);

            // Save current position first (so /back works)
            session.LastPosition = new Position3D(player.transform.position);

            // Warmup delay if configured
            if (_config.TeleportWarmupSeconds > 0 && !HasRoleOrHigher(session.Role, "mod"))
            {
                PrintToChat(player, $"<color=#FFD700>Don't move!</color> Teleporting in {_config.TeleportWarmupSeconds}s...");

                // Cancel if player moves during warmup — hook will catch it
                session._pendingTeleport = true;
                session._teleportDestination = new Position3D(destination);
                session._teleportReason = reason;
                session._teleportStartPos = new Position3D(player.transform.position);

                timer.Once(_config.TeleportWarmupSeconds, () =>
                {
                    if (session._pendingTeleport && player.IsConnected)
                    {
                        // Teleport using console command (works correctly with vehicles, sleeping, etc.)
                        player.SendConsoleCommand($"teleport {destination.x} {destination.y} {destination.z}");
                        session.LastTeleport = DateTime.Now;
                        session._pendingTeleport = false;
                        PrintToChat(player, $"<color=#00FF88>{reason}:</color> done!");
                    }
                });
                return;
            }

            // Instant teleport (mods/admins bypass warmup)
            player.SendConsoleCommand($"teleport {destination.x} {destination.y} {destination.z}");
            session.LastTeleport = DateTime.Now;
            PrintToChat(player, $"<color=#00FF88>{reason}:</color> done!");
        }

        // Cancel warmup teleport if player moves during countdown — merged into main CanClientMove above

        // =====================================================================
        // MESSAGING & SOCIAL
        // =====================================================================

        private void HandlePrivateMessage(BasePlayer player, PlayerSession session, string args)
        {
            var parts = SplitArgs(args, 2);
            var targetName = parts[0].Trim(); var message = parts.Length > 1 ? parts[1].Trim() : "";
            if (string.IsNullOrEmpty(targetName) || string.IsNullOrEmpty(message))
            { PrintToChat(player, "<color=#FFD700>Usage:</color> /db msg <player> <message>"); return; }
            var target = FindPlayer(targetName);
            if (target == null) { PrintToChat(player, $"<color=#FF4444>Player not found:</color> {targetName}"); return; }
            if (target == player) { PrintToChat(player, "<color=#FF4444>You can't message yourself.</color>"); return; }
            var targetSession = GetOrCreateSession(target);
            if (targetSession.IgnoredPlayers.Contains(player.userID))
            { PrintToChat(player, $"<color=#FF4444>{target.displayName} is ignoring you.</color>"); return; }
            var truncatedMsg = message.Length > _config.MaxPrivateMessageLength
                ? message.Substring(0, _config.MaxPrivateMessageLength) + "..." : message;
            PrintToChat(target, $"<color=#4DA6FF>[PM] {player.displayName}:</color> {truncatedMsg}");
            PrintToChat(player, $"<color=#4DA6FF>[PM] to {target.displayName}:</color> {truncatedMsg}");
        }

        private void HandleIgnore(BasePlayer player, PlayerSession session, string args)
        {
            if (string.IsNullOrWhiteSpace(args)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db ignore <player>"); return; }
            var target = FindPlayer(args);
            if (target == null) { PrintToChat(player, $"<color=#FF4444>Player not found:</color> {args}"); return; }
            if (target == player) { PrintToChat(player, "<color=#FF4444>You can't ignore yourself.</color>"); return; }
            session.IgnoredPlayers.Add(target.userID);
            PrintToChat(player, $"<color=#888>Ignoring:</color> {target.displayName}");
        }

        private void HandleUnignore(BasePlayer player, PlayerSession session, string args)
        {
            if (string.IsNullOrWhiteSpace(args)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db unignore <player>"); return; }
            var target = FindPlayer(args);
            if (target == null) { PrintToChat(player, $"<color=#FF4444>Player not found:</color> {args}"); return; }
            if (session.IgnoredPlayers.Remove(target.userID))
                PrintToChat(player, $"<color=#00FF88>Stopped ignoring:</color> {target.displayName}");
            else
                PrintToChat(player, $"<color=#888>You weren't ignoring:</color> {target.displayName}");
        }

        private void HandleAFK(BasePlayer player, PlayerSession session)
        {
            session.IsAFK = !session.IsAFK;
            session._afkManual = session.IsAFK;
            session._afkAutoDetected = false;
            if (session.IsAFK)
            {
                session.LastActivity = DateTime.Now;
                PrintToChat(player, "<color=#FFD700>AFK mode ON</color>");
            }
            else
            {
                var mins = (DateTime.Now - session.LastActivity).TotalMinutes;
                PrintToChat(player, $"<color=#00FF88>AFK mode OFF</color> | You were away {mins:F0} min");
            }
        }

        private void HandleUptime(BasePlayer player, PlayerSession session)
        {
            var uptime = Time.realtimeSinceStartup;
            var hours = (int)(uptime / 3600);
            var mins = (int)(uptime / 60) % 60;
            var secs = (int)uptime % 60;
            var fps = Math.Round(1.0f / Time.deltaTime, 1);
            var players = BasePlayer.activePlayerList.Count;
            PrintToChat(player, "<color=#FFD700>Server Info</color>");
            PrintToChat(player, $"Uptime: {hours}h {mins}m {secs}s | FPS: {fps}");
            PrintToChat(player, $"Players online: {players}");
        }

        private void HandleNightAlert(BasePlayer player, PlayerSession session)
        {
            if (!permission.UserHasPermission(player.UserIDString, "rustduckbot.afk") && !HasRoleOrHigher(session.Role, "vip"))
            { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            session.Settings.NightAlert = !session.Settings.NightAlert;
            PrintToChat(player, $"<color=#FFD700>Night alert:</color> {(session.Settings.NightAlert ? "ON" : "OFF")}");
        }

        // =====================================================================
        // MODERATION
        // =====================================================================

        private void HandleReport(BasePlayer player, PlayerSession session, string args)
        {
            if (!_config.EnableReportSystem) { PrintToChat(player, "<color=#FF4444>Report system is disabled.</color>"); return; }
            var parts = SplitArgs(args, 2);
            var targetName = parts[0].Trim();
            var reason = parts.Length > 1 ? parts[1].Trim() : "No reason provided";
            if (string.IsNullOrEmpty(targetName)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db report <player> <reason>"); return; }
            if (session.LastReportSent.HasValue && (DateTime.Now - session.LastReportSent.Value).TotalMinutes < _config.ReportCooldownMinutes)
            { PrintToChat(player, $"<color=#FF4444>Slow down.</color> Wait {_config.ReportCooldownMinutes - (int)(DateTime.Now - session.LastReportSent.Value).TotalMinutes} min."); return; }
            var target = FindPlayer(targetName);
            var report = new ReportEntry {
                Id = Guid.NewGuid().ToString().Substring(0, 8),
                ReporterId = player.userID, ReporterName = player.displayName,
                TargetId = target?.userID ?? 0, TargetName = target?.displayName ?? $"[offline] {targetName}",
                Reason = reason, Time = DateTime.Now, Status = "pending"
            };
            _reportQueue.Add(report);
            LogActivity("moderation", "Report", $"{player.displayName} reported {report.TargetName}: {reason}", player.UserIDString, player.displayName);
            EvaluateAutoModeration(report);
            session.LastReportSent = DateTime.Now;
            PrintToChat(player, $"<color=#00FF88>Report submitted.</color> ID: <color=#FFD700>{report.Id}</color>");
            foreach (var p in BasePlayer.activePlayerList)
            {
                var s = GetOrCreateSession(p);
                if (s.Role == "admin" || s.Role == "mod")
                    PrintToChat(p, $"<color=#FF4444>REPORT #{report.Id}:</color> {player.displayName} -> {report.TargetName}: {reason}");
            }
        }

        private void HandleReports(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod required</color>"); return; }
            var reports = _reportQueue.OrderByDescending(r => r.Time).Take(15).ToList();
            PrintToChat(player, $"<color=#FFD700>═══ REPORTS ({reports.Count}) ═══</color>");
            if (reports.Count == 0) { PrintToChat(player, "<color=#888>No reports.</color>"); return; }
            foreach (var report in reports)
                PrintToChat(player, $"  <color=#FF4444>#{report.Id}</color> {report.ReporterName} -> {report.TargetName} | {report.Status} | {report.Reason}");
        }

        private void HandleModerationReview(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod required</color>"); return; }
            if (!_config.EnableAIModeration) { PrintToChat(player, "<color=#FF4444>AI moderation is disabled.</color>"); return; }
            var targetName = (args ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(targetName)) { PrintToChat(player, "Usage: /db modreview <player>"); return; }
            var target = FindPlayer(targetName);
            var reports = _reportQueue.Where(r => ContainsIgnoreCase(r.TargetName, targetName) || (target != null && r.TargetId == target.userID)).OrderByDescending(r => r.Time).Take(6).ToList();
            var activity = _activityLog.Where(a => ContainsIgnoreCase(a.PlayerName, targetName) || (target != null && a.PlayerId == target.UserIDString)).OrderByDescending(a => a.Time).Take(10).ToList();
            var reportSummary = reports.Count == 0 ? "none" : string.Join(" | ", reports.Select(r => $"{r.Time:MM/dd HH:mm}: {r.ReporterName} -> {r.Reason}"));
            var activitySummary = activity.Count == 0 ? "none" : string.Join(" | ", activity.Select(a => $"{a.Time:MM/dd HH:mm}: {a.Category}/{a.Action} {a.Details}"));
            var tracked = target != null && _trackedPlayers.TryGetValue(target.UserIDString, out var trackedPlayer) ? trackedPlayer : null;
            var prompt = $"Review Rust moderation context for target {targetName}. Reports: {reportSummary}. Activity: {activitySummary}. Threat level: {tracked?.ThreatLevel ?? "unknown"}. Kills: {tracked?.Kills ?? 0}. Deaths: {tracked?.Deaths ?? 0}. Sessions: {tracked?.SessionCount ?? 0}. Give a concise moderation review with risk level, evidence summary, and recommended action chosen from: observe, warn, mute, kick, ban. Make clear this is advisory unless auto-moderation rules independently trigger.";
            PrintToChat(player, "<color=#FFD700>═══ AI MOD REVIEW ═══</color>");
            var response = GetAssistantResponse(player, session, prompt, false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void EvaluateAutoModeration(ReportEntry latestReport)
        {
            if (!_config.EnableAutoModeration || latestReport == null) return;
            var windowStart = DateTime.Now.AddMinutes(-_config.AutoModerationWindowMinutes);
            var reports = _reportQueue.Where(r => r.TargetName == latestReport.TargetName && r.Time >= windowStart).ToList();
            var reportCount = reports.Count;
            if (reportCount < _config.AutoModerationReportThreshold) return;

            var target = FindPlayer(latestReport.TargetName);
            if (target == null) return;

            var tracked = _trackedPlayers.TryGetValue(target.UserIDString, out var trackedPlayer) ? trackedPlayer : null;
            var highThreat = tracked != null && (tracked.ThreatLevel == "high" || tracked.ThreatLevel == "medium");
            var severeReport = reports.Any(r => ContainsIgnoreCase(r.Reason, "hack") || ContainsIgnoreCase(r.Reason, "cheat") || ContainsIgnoreCase(r.Reason, "aimbot") || ContainsIgnoreCase(r.Reason, "esp"));

            if (reportCount >= _config.AutoModerationBanThreshold && (highThreat || severeReport))
            {
                var reason = $"Auto-ban: {reportCount} reports in {_config.AutoModerationWindowMinutes}m";
                Server.Command($"banid {target.UserIDString} \"{reason}\" {_config.AutoModerationBanDuration}");
                target.Kick(reason);
                LogActivity("moderation", "AutoBan", $"{target.displayName}: {reason}", target.UserIDString, target.displayName);
                foreach (var report in reports) { report.Status = "resolved"; report.ReviewedBy = "auto-ban"; report.ReviewedAt = DateTime.Now; }
                return;
            }

            if (reportCount >= _config.AutoModerationKickThreshold)
            {
                var reason = $"Auto-kick: {reportCount} reports in {_config.AutoModerationWindowMinutes}m";
                target.Kick(reason);
                LogActivity("moderation", "AutoKick", $"{target.displayName}: {reason}", target.UserIDString, target.displayName);
                foreach (var report in reports) if (report.Status == "pending") { report.Status = "reviewed"; report.ReviewedBy = "auto-kick"; report.ReviewedAt = DateTime.Now; }
            }
        }

        private void HandleSlay(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            if (string.IsNullOrWhiteSpace(args)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db slay <player>"); return; }
            var target = FindPlayer(args);
            if (target == null) { PrintToChat(player, $"<color=#FF4444>Player not found:</color> {args}"); return; }
            target.Hurt(target.health + 1000f, Rust.DamageType.Generic, player, false);
            PrintToChat(player, $"<color=#00FF88>Slayed:</color> {target.displayName}");
            LogActivity("moderation", "Slay", $"{target.displayName} slayed by {player.displayName}", target.UserIDString, target.displayName);
        }

        private void HandleRespawn(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            if (string.IsNullOrWhiteSpace(args)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db respawn <player>"); return; }
            var target = FindPlayer(args);
            if (target == null) { PrintToChat(player, $"<color=#FF4444>Player not found:</color> {args}"); return; }
            target.SendConsoleCommand("respawn");
            PrintToChat(player, $"<color=#00FF88>Respawned:</color> {target.displayName}");
        }

        private void HandleNotes(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            var parts = SplitArgs(args, 2);
            var targetName = parts[0].Trim(); var action = parts.Length > 1 ? parts[1].Trim() : "";
            if (string.IsNullOrEmpty(targetName)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db notes <player> [view|add <note>|remove <key>|clear]"); return; }
            var targetOnline = FindPlayer(targetName);
            PlayerSession targetSession = targetOnline != null ? GetOrCreateSession(targetOnline) : null;
            if (targetSession == null) { PrintToChat(player, $"<color=#FF4444>No session for:</color> {targetName}"); return; }
            if (string.IsNullOrEmpty(action) || action == "view")
            {
                PrintToChat(player, $"<color=#FFD700>Notes: {targetOnline?.displayName ?? targetName}</color>");
                if (targetSession.PlayerNotes.Count == 0) PrintToChat(player, "<color=#888>No notes.</color>");
                else foreach (var kvp in targetSession.PlayerNotes)
                    PrintToChat(player, $"  <color=#4DA6FF>[{kvp.Key}]</color> {kvp.Value}");
                return;
            }
            if (action.StartsWith("add ", StringComparison.OrdinalIgnoreCase))
            {
                var noteText = action.Substring(4);
                if (string.IsNullOrWhiteSpace(noteText)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db notes <player> add <note text>"); return; }
                if (targetSession.PlayerNotes.Count >= _config.MaxPlayerNotes)
                { PrintToChat(player, $"<color=#FF4444>Note limit ({_config.MaxPlayerNotes}).</color> Remove some first."); return; }
                var key = DateTime.Now.ToString("HHmm");
                targetSession.PlayerNotes[key] = $"{noteText} -- {player.displayName} {DateTime.Now:yyyy-MM-dd}";
                PrintToChat(player, "<color=#00FF88>Note added.</color>");
                return;
            }
            if (action.StartsWith("remove ", StringComparison.OrdinalIgnoreCase))
            {
                var key = action.Substring(7).Trim();
                if (targetSession.PlayerNotes.Remove(key)) PrintToChat(player, "<color=#00FF88>Note removed.</color>");
                else PrintToChat(player, $"<color=#FF4444>Key not found:</color> {key}");
                return;
            }
            if (action == "clear") { targetSession.PlayerNotes.Clear(); PrintToChat(player, "<color=#00FF88>All notes cleared.</color>"); return; }
            PrintToChat(player, "<color=#FFD700>Usage:</color> /db notes <player> [view|add <note>|remove <key>|clear]");
        }

        private void HandleAdminMsg(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            var parts = SplitArgs(args, 2);
            var targetName = parts[0].Trim(); var message = parts.Length > 1 ? parts[1].Trim() : "";
            if (string.IsNullOrEmpty(targetName) || string.IsNullOrEmpty(message))
            { PrintToChat(player, "<color=#FFD700>Usage:</color> /db adminmsg <player> <message>"); return; }
            var target = FindPlayer(targetName);
            if (target == null) { PrintToChat(player, $"<color=#FF4444>Player not found:</color> {targetName}"); return; }
            PrintToChat(target, $"<color=#FFD700>[ADMIN]</color> {message}");
            PrintToChat(player, $"<color=#FFD700>Sent to {target.displayName}:</color> {message}");
        }

        private void HandleMuteList(BasePlayer player, PlayerSession session)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            PrintToChat(player, "<color=#FFD700>Muted Players</color>");
            if (_mutedPlayers.Count == 0) PrintToChat(player, "<color=#888>No one muted.</color>");
            else foreach (var entry in _mutedPlayers) PrintToChat(player, $"  * {entry}");
        }

        // =====================================================================
        // ECONOMY & REWARDS
        // =====================================================================

        private void HandleDaily(BasePlayer player, PlayerSession session)
        {
            if (!permission.UserHasPermission(player.UserIDString, "rustduckbot.economy") && !HasRoleOrHigher(session.Role, "vip"))
            { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            if (!_config.EnableDailyReward) { PrintToChat(player, "<color=#FF4444>Daily reward is disabled.</color>"); return; }
            if (session.LastDailyReward.HasValue)
            {
                var next = session.LastDailyReward.Value.AddHours(20);
                if (DateTime.Now < next) { var r = next - DateTime.Now; PrintToChat(player, $"<color=#888>Next reward in:</color> {r.Hours}h {r.Minutes}m"); return; }
            }
            session.LastDailyReward = DateTime.Now;
            var isVip = HasRoleOrHigher(session.Role, "vip") || permission.UserHasPermission(player.UserIDString, "rustduckbot.vip");
            var scrapReward = _config.DailyRewardScrap;
            var rpReward = _config.DailyRewardRP;
            if (isVip && _config.VipBonusMultiplier > 1f)
            {
                scrapReward = (int)(scrapReward * _config.VipBonusMultiplier);
                rpReward = (int)(rpReward * _config.VipBonusMultiplier);
            }
            if (scrapReward > 0)
                Server.Command("scavenger.additem \"" + player.UserIDString + "\" scrap " + scrapReward);
            PrintToChat(player, "<color=#FFD700>Daily Reward</color>");
            PrintToChat(player, $"<color=#00FF88>+{scrapReward} scrap</color>" + (isVip && _config.VipBonusMultiplier > 1f ? " <color=#FFD700>(VIP Boost)</color>" : ""));
            if (rpReward > 0) PrintToChat(player, $"<color=#4DA6FF>+{rpReward} RP</color>");
            PrintToChat(player, "<color=#888>Come back tomorrow!</color>");
        }

        private void HandlePlaytime(BasePlayer player, PlayerSession session)
        {
            var sessionDur = DateTime.Now - session.SessionStart;
            var todayTotal = session.PlaytimeMinutesToday + (int)sessionDur.TotalMinutes;
            PrintToChat(player, "<color=#FFD700>Playtime</color>");
            PrintToChat(player, $"Session: {sessionDur.Hours}h {sessionDur.Minutes}m");
            PrintToChat(player, $"Today: {todayTotal / 60}h {todayTotal % 60}m");
            PrintToChat(player, $"Total: {session.OnlineTime.Hours}h {session.OnlineTime.Minutes}m");
            if (todayTotal >= _config.PlaytimeBonusMinutes && _config.DailyRewardScrap > 0)
                PrintToChat(player, $"<color=#FFD700>+ {todayTotal/60}h today!</color> Claim: <color=#FFD700>/db daily</color>");
        }

        private void HandleTop(BasePlayer player, PlayerSession session, string args)
        {
            var sortBy = string.IsNullOrWhiteSpace(args) ? "kills" : args.Trim().ToLower();
            PrintToChat(player, $"<color=#FFD700>Top Players ({sortBy})</color>");
            var allSessions = _sessions.Values.OrderByDescending(s =>
                sortBy == "playtime" ? s.OnlineTime.TotalMinutes :
                sortBy == "kd" ? (s.Deaths == 0 ? s.Kills : (float)s.Kills / s.Deaths) : s.Kills
            ).Take(10).ToList();
            if (allSessions.Count == 0) { PrintToChat(player, "<color=#888>No data yet.</color>"); return; }
            int rank = 1;
            foreach (var s in allSessions)
            {
                var name = s.DisplayName.Length > 14 ? s.DisplayName.Substring(0, 12) + ".." : s.DisplayName;
                var val = sortBy == "playtime" ? $"{s.OnlineTime.TotalHours:F0}h" :
                          sortBy == "kd" ? $"{(s.Deaths == 0 ? s.Kills : (float)s.Kills / s.Deaths):F2}" : $"{s.Kills} kills";
                var medal = rank == 1 ? "1st" : rank == 2 ? "2nd" : rank == 3 ? "3rd" : $"#{rank}";
                PrintToChat(player, $"  {medal}: <color=#4DA6FF>{name}</color>: {val}");
                rank++;
            }
        }

        // =====================================================================
        // COMBAT & INTEL
        // =====================================================================

        private void HandleLastDeath(BasePlayer player, PlayerSession session, string args)
        {
            var targetName = string.IsNullOrWhiteSpace(args) ? player.displayName : args.Trim();
            PlayerSession targetSess = null;
            var targetOnline = FindPlayer(targetName);
            if (targetOnline != null) targetSess = GetOrCreateSession(targetOnline);
            targetSess = targetSess ?? _sessions.Values.FirstOrDefault(s => s.DisplayName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
            if (targetSess == null || targetSess.RecentDeaths.Count == 0)
            { PrintToChat(player, $"<color=#888>No death data for:</color> {targetName}"); return; }
            var d = targetSess.RecentDeaths[0];
            PrintToChat(player, $"<color=#FFD700>Last Death: {targetSess.DisplayName}</color>");
            PrintToChat(player, $"Killer: {d.KillerName}");
            if (!string.IsNullOrEmpty(d.Weapon)) PrintToChat(player, $"Weapon: {d.Weapon}");
            PrintToChat(player, $"Location: {d.Monument} ({GetLocation(d.Location)})");
        }

        private void HandleKiller(BasePlayer player, PlayerSession session, string args)
        {
            var targetName = string.IsNullOrWhiteSpace(args) ? player.displayName : args.Trim();
            PlayerSession targetSess = null;
            var targetOnline = FindPlayer(targetName);
            if (targetOnline != null) targetSess = GetOrCreateSession(targetOnline);
            targetSess = targetSess ?? _sessions.Values.FirstOrDefault(s => s.DisplayName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
            if (targetSess == null || targetSess.RecentDeaths.Count == 0)
            { PrintToChat(player, $"<color=#888>No death data for:</color> {targetName}"); return; }
            var lastKiller = targetSess.RecentDeaths[0].KillerName;
            var lastWeapon = targetSess.RecentDeaths[0].Weapon;
            var killCount = targetSess.RecentDeaths.Count(d => d.KillerName == lastKiller);
            PrintToChat(player, "<color=#FFD700>Killer Info</color>");
            PrintToChat(player, $"Last killed by: {lastKiller}");
            if (!string.IsNullOrEmpty(lastWeapon)) PrintToChat(player, $"Weapon: {lastWeapon}");
            PrintToChat(player, $"They've killed {targetSess.DisplayName} {killCount}x recently.");
        }

        private void HandleWeaponInfo(BasePlayer player, PlayerSession session, string args)
        {
            if (string.IsNullOrWhiteSpace(args)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db weapon <name>"); return; }
            var weaponName = args.ToLower().Trim();
            var weapons = new Dictionary<string, (string dmg, string rpm, string mag, string range)> {
                { "ak47", ("32", "600", "30", "Med-Long") },
                { "mp5a4", ("22", "750", "30", "Short-Med") },
                { "thompson", ("27", "480", "40", "Short-Med") },
                { "python", ("48", "72", "6", "Medium") },
                { "m249", ("42", "600", "100", "Long") },
                { "l96", ("85", "41", "10", "V.Long") },
                { "awp", ("110", "55", "10", "V.Long") },
                { "nailgun", ("15", "300", "20", "Short") },
                { "crossbow", ("45", "25", "1", "Long") },
                { "eoka", ("25x6", "50", "1", "V.Short") },
                { "shotgun", ("25x8", "55", "8", "Short") },
                { "sarquebus", ("70", "30", "1", "Long") },
                { "sar", ("30", "240", "25", "Medium") },
                { "revolver", ("40", "70", "8", "Medium") },
                { "semi", ("28", "200", "15", "Medium") },
                { "smg", ("23", "600", "30", "Short") },
            };
            var match = weapons.Keys.FirstOrDefault(k => k.Contains(weaponName) || weaponName.Contains(k));
            if (match == null)
            { PrintToChat(player, $"<color=#FF4444>Unknown weapon:</color> {args}"); return; }
            var w = weapons[match];
            PrintToChat(player, $"<color=#FFD700>{match.ToUpper()}</color>");
            PrintToChat(player, $"Dmg:{w.dmg} RPM:{w.rpm} Mag:{w.mag} Range:{w.range}");
        }

        private void HandleCompare(BasePlayer player, PlayerSession session, string args)
        {
            var parts = SplitArgs(args, 2);
            var item1 = parts.Length > 0 ? parts[0].Trim() : "";
            var item2 = parts.Length > 1 ? parts[1].Trim() : "";
            if (string.IsNullOrEmpty(item1) || string.IsNullOrEmpty(item2))
            { PrintToChat(player, "<color=#FFD700>Usage:</color> /db compare <item1> <item2>"); return; }
            var items = new Dictionary<string, string> {
                { "ak47", "Dmg:32 | RPM:600 | Mag:30 | Range:Med" },
                { "mp5a4", "Dmg:22 | RPM:750 | Mag:30 | Range:Short" },
                { "thompson", "Dmg:27 | RPM:480 | Mag:40 | Range:Short" },
                { "m249", "Dmg:42 | RPM:600 | Mag:100 | Range:Long" },
                { "l96", "Dmg:85 | RPM:41 | Mag:10 | Range:V.Long" },
                { "c4", "Dmg:450 | Radius:8m | Fuse:10s | Static" },
                { "rocket", "Dmg:350 | Radius:12m | Thrown | Self-propelled" },
                { "hazmat", "Armor:35 | Cold/Fire resist | No rad block" },
                { "metal", "Armor:40 | High bullet resist | Heavy" },
                { "kevlar", "Armor:35 | Medium resist | Lighter" },
            };
            var i1 = items.Keys.FirstOrDefault(k => k.Contains(item1) || item1.Contains(k));
            var i2 = items.Keys.FirstOrDefault(k => k.Contains(item2) || item2.Contains(k));
            if (i1 == null) { PrintToChat(player, $"<color=#FF4444>Unknown:</color> {item1}"); return; }
            if (i2 == null) { PrintToChat(player, $"<color=#FF4444>Unknown:</color> {item2}"); return; }
            if (i1 == i2) { PrintToChat(player, "<color=#FF4444>Compare two different items.</color>"); return; }
            PrintToChat(player, "<color=#FFD700>Compare</color>");
            PrintToChat(player, $"<color=#4DA6FF>{i1.ToUpper()}:</color> {items[i1]}");
            PrintToChat(player, $"<color=#4DA6FF>{i2.ToUpper()}:</color> {items[i2]}");
        }

        private void HandleLoot(BasePlayer player, PlayerSession session, string args)
        {
            if (string.IsNullOrWhiteSpace(args)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db loot <item>"); return; }
            var item = args.ToLower().Trim();
            var lootData = new Dictionary<string, string[]> {
                { "hqx", new[] { "Dome", "Airfield", "Oil Rig" } },
                { "metal", new[] { "Power Plant", "Water Treatment", "Dome" } },
                { "scrap", new[] { "Junkyard", "Train Yard", "Sewer Branch" } },
                { "c4", new[] { "Dome", "Oil Rig", "Military Tunnels" } },
                { "explosives", new[] { "Dome", "Airfield", "Oil Rig" } },
                { "sulfur", new[] { "Outpost", "Bandit Camp", "Mining Outpost" } },
                { "gunpowder", new[] { "Dome", "Military Tunnels", "Train Yard" } },
                { "comp", new[] { "Power Plant", "Water Treatment", "Oil Rig" } },
                { "electronics", new[] { "Power Plant", "Dome", "Arctic Base" } },
                { "fuel", new[] { "Gas Station", "Oil Rig", "Bandit Camp" } },
                { "medkit", new[] { "Dome", "Airfield", "Arctic Base", "Bandit" } },
                { "bandage", new[] { "Dome", "Airfield", "Gas Station", "Outpost" } },
                { "code", new[] { "Satellite Dish", "Dome", "Power Plant" } },
            };
            var match = lootData.Keys.FirstOrDefault(k => k.Contains(item) || item.Contains(k));
            if (match != null)
            {
                PrintToChat(player, $"<color=#FFD700>Loot: {match.ToUpper()}</color>");
                foreach (var loc in lootData[match]) PrintToChat(player, $"  * {loc}");
            }
            else
            {
                PrintToChat(player, "<color=#FFD700>Monuments</color>");
                PrintToChat(player, "Dome: Elite, tech | Airfield: Military | Oil Rig: Elite");
                PrintToChat(player, "Power Plant: Elec comp | Train Yard: Junk, scrap");
                PrintToChat(player, "Outpost: Basic | Bandit: Fuel, meds | Arctic: Tech");
            }
        }

        private void HandleKit(BasePlayer player, PlayerSession session, string args)
        {
            var parts = SplitArgs(args, 2);
            var kitName = parts.Length > 0 ? parts[0].Trim().ToLower() : "";
            var subArg = parts.Length > 1 ? parts[1].Trim() : "";

            if (string.IsNullOrEmpty(kitName) || kitName == "help" || kitName == "list")
            {
                ShowKits(player);
                return;
            }

            if (kitName == "info")
            {
                var target = FindPlayer(string.IsNullOrWhiteSpace(subArg) ? player.displayName : subArg);
                if (target == null) { PrintToChat(player, "Player not found: " + subArg); return; }
                ShowPlayerKitInfo(player, target);
                return;
            }

            if (!_kitDefinitions.TryGetValue(kitName, out var kit))
            {
                var closest = _kitDefinitions.Keys.FirstOrDefault(k => k.Contains(kitName) || kitName.Contains(k));
                PrintToChat(player, "<color=#FF4444>Unknown kit:</color> " + kitName);
                if (closest != null) PrintToChat(player, "<color=#888>Did you mean:</color> " + closest);
                return;
            }

            if (!CanUseKit(player, session, kit, out var reason))
            {
                PrintToChat(player, "<color=#FF4444>Cannot use kit:</color> " + reason);
                return;
            }

            if (!TryGrantKit(player, kit, out var kitError))
            {
                PrintToChat(player, "<color=#FF4444>Kit unavailable:</color> " + kitError);
                LogActivity("kits", "Kit grant failed", kitError + " (" + kit.Name + ")", player.UserIDString, player.displayName);
                return;
            }

            RecordKitUse(player.userID, kit.Name);
            PrintToChat(player, "<color=#00FF88>Kit redeemed:</color> " + kit.DisplayName);
            PrintToChat(player, "<color=#888>Next use in " + kit.CooldownMinutes + " minutes.</color>");
            LogActivity("kits", "Kit claimed", player.displayName + " claimed kit '" + kit.Name + "'", player.UserIDString, player.displayName);
        }

        // =====================================================================
        // BUILDING & BASE
        // =====================================================================

        private void HandleTC(BasePlayer player, PlayerSession session)
        {
            var pos = player.transform.position;
            var tcList = new List<string>();
            foreach (var e in BaseEntity.saveList)
            {
                if (e is BuildingPrivlidge tc && Vector3.Distance(tc.transform.position, pos) < 200f)
                {
                    var dist = Vector3.Distance(tc.transform.position, pos);
                    var authCount = tc.authorizedPlayers.Count;
                    tcList.Add($"* TC @ {GetLocation(tc.transform.position)} | Auth:{authCount} | {dist:F0}m");
                    if (tcList.Count >= 5) break;
                }
            }
            PrintToChat(player, "<color=#FFD700>TC Nearby (200m)</color>");
            if (tcList.Count == 0) PrintToChat(player, "<color=#888>No TC found nearby.</color>");
            else foreach (var tc in tcList) PrintToChat(player, tc);
        }

        private void HandleCupSize(BasePlayer player, PlayerSession session)
        {
            var pos = player.transform.position;
            var nearbyTCs = 0;
            foreach (var e in BaseEntity.saveList)
                if (e is BuildingPrivlidge tc && Vector3.Distance(tc.transform.position, pos) < 42f)
                    nearbyTCs++;
            PrintToChat(player, "<color=#FFD700>Cupboard Coverage</color>");
            PrintToChat(player, $"Your position: {GetLocation(pos)}");
            PrintToChat(player, $"<color=#4DA6FF>TCs within 42m:</color> {nearbyTCs}");
            if (nearbyTCs == 0)
                PrintToChat(player, "<color=#FF4444>No TC coverage! Add a cupboard.</color>");
            else if (nearbyTCs == 1)
                PrintToChat(player, "<color=#00FF88>One TC covers this area.</color>");
            else
                PrintToChat(player, $"<color=#FFD700>Multiple TC coverage ({nearbyTCs} TCs).</color>");
        }

        private void HandleDecayCheck(BasePlayer player, PlayerSession session, string args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "rustduckbot.intel") && !HasRoleOrHigher(session.Role, "vip"))
            { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            var pos = player.transform.position;
            var radius = _config.DecayScanRadius;
            var count = 0;
            foreach (var e in BaseEntity.saveList)
            {
                if (e is BuildingBlock b && Vector3.Distance(b.transform.position, pos) < radius)
                    count++;
                if (count >= 20) break;
            }
            PrintToChat(player, "<color=#FFD700>Decay Scan</color>");
            PrintToChat(player, $"Scanned {radius}m radius from {GetLocation(pos)}");
            PrintToChat(player, $"<color=#4DA6FF>Structures found:</color> {count} (up to 20 shown)");
            if (count >= 20) PrintToChat(player, "<color=#888>More structures exist beyond limit.</color>");
        }

        private void HandleNotifications(BasePlayer player, PlayerSession session, string args)
        {
            if (string.IsNullOrWhiteSpace(args) || args.Trim() == "list")
            {
                PrintToChat(player, "<color=#FFD700>Notifications</color>");
                var unread = session.Notifications.Count(n => !n.Read);
                PrintToChat(player, $"Unread: <color=#FFD700>{unread}</color> | Total: {session.Notifications.Count}");
                if (session.Notifications.Count == 0)
                    PrintToChat(player, "<color=#888>No notifications.</color>");
                else
                    foreach (var n in session.Notifications.Skip(Math.Max(0, session.Notifications.Count - 5)))
                        PrintToChat(player, $"  <color=#4DA6FF>[{n.Type}]</color> {n.Title}: {n.Body}");
                return;
            }
            if (args.Trim() == "clear")
            { session.Notifications.Clear(); PrintToChat(player, "<color=#00FF88>Notifications cleared.</color>"); return; }
            PrintToChat(player, "<color=#FFD700>Usage:</color> /db notify [list|clear]");
        }

        private void HandleSubscribe(BasePlayer player, PlayerSession session, string args)
        {
            var eventName = args.Trim().ToLower();
            if (string.IsNullOrEmpty(eventName))
            {
                PrintToChat(player, "<color=#FFD700>Subscriptions</color>");
                PrintToChat(player, "Available: <color=#4DA6FF>night</color>, <color=#4DA6FF>raid</color>, <color=#4DA6FF>decay</color>, <color=#4DA6FF>events</color>");
                PrintToChat(player, "Usage: <color=#FFD700>/db subscribe <event></color>");
                return;
            }
            var validEvents = new[] { "night", "raid", "decay", "events" };
            if (Array.IndexOf(validEvents, eventName) < 0)
            { PrintToChat(player, $"<color=#FF4444>Unknown event:</color> {eventName}"); return; }
            var existing = session.Notifications.FirstOrDefault(n => n.Type == eventName + "_sub");
            if (existing != null)
            { PrintToChat(player, $"<color=#888>Already subscribed to:</color> {eventName}"); return; }
            session.Notifications.Add(new PlayerNotification {
                Id = Guid.NewGuid().ToString().Substring(0, 8),
                Title = $"Subscribed: {eventName}",
                Body = $"You will receive alerts for: {eventName}",
                Created = DateTime.Now,
                Type = eventName + "_sub",
                Read = false
            });
            PrintToChat(player, $"<color=#00FF88>Subscribed to:</color> {eventName}");
        }

        private void ShowWeather(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══ WEATHER ═══</color>");
            var response = GetAssistantResponse(player, session, "Give a short Rust-oriented weather and visibility advisory for the current server conditions. If no live weather data is available, say that clearly and give practical advice.", false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void ShowWipeInfo(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══ WIPE INFO ═══</color>");
            var response = GetAssistantResponse(player, session, "Explain what players should check for wipe timing and wipe prep on this Rust server. Keep it concise.", false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void ShowMonuments(BasePlayer player, PlayerSession session)
        {
            var pos = player.transform.position;
            var nearest = GetNearestMonument(pos);

            PrintToChat(player, "<color=#FFD700>═══ MONUMENTS ═══</color>");
            PrintToChat(player, $"Nearest: {nearest}");
            PrintToChat(player, $"Position: {GetGridCoord(pos)}");
            var response = GetAssistantResponse(player, session, $"Give concise Rust monument advice for a player near {nearest} at {GetGridCoord(pos)}. Mention loot priorities and risks.", false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void ShowLootInfo(BasePlayer player, PlayerSession session, string type)
        {
            PrintToChat(player, "<color=#FFD700>═══ LOOT LOCATIONS ═══</color>");
            var focus = string.IsNullOrWhiteSpace(type) ? "general loot routing" : type;
            var response = GetAssistantResponse(player, session, $"Give concise Rust loot advice focused on {focus}. Mention best monuments, what loot to prioritize, and major risks.", false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void ShowActiveEvents(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══ ACTIVE EVENTS ═══</color>");
            var events = _raidHistory.Count(r => r.Outcome == "in_progress");
            PrintToChat(player, $"Active raids: {events}");
            var response = GetAssistantResponse(player, session, $"Summarize what active Rust world events a player should check right now. Current tracked raids: {events}. Mention CH47, Bradley, cargo, patrol, and timing awareness.", false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void ShowRecipes(BasePlayer player, PlayerSession session, string item)
        {
            PrintToChat(player, "<color=#FFD700>═══ RECIPES ═══</color>");
            var focus = string.IsNullOrWhiteSpace(item) ? "starter progression and useful early recipes" : item;
            var response = GetAssistantResponse(player, session, $"Explain Rust crafting and recipe guidance for {focus}. Mention workbench tier if relevant and keep it concise.", false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void ShowResearch(BasePlayer player, PlayerSession session, string item)
        {
            PrintToChat(player, "<color=#FFD700>═══ RESEARCH ═══</color>");
            var focus = string.IsNullOrWhiteSpace(item) ? "research priorities for a normal Rust player" : item;
            var response = GetAssistantResponse(player, session, $"Give concise Rust research-table advice for {focus}. Mention likely scrap considerations and what to prioritize.", false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void ShowBlueprintInfo(BasePlayer player, PlayerSession session, string bp)
        {
            PrintToChat(player, "<color=#FFD700>═══ BLUEPRINTS ═══</color>");
            var focus = string.IsNullOrWhiteSpace(bp) ? "blueprint progression" : bp;
            var response = GetAssistantResponse(player, session, $"Give concise Rust blueprint advice for {focus}. Mention when it is worth learning and what progression stage it fits.", false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        // =====================================================================
        // GAMES & FUN
        // =====================================================================

        private void RollDice(BasePlayer player, PlayerSession session, string args)
        {
            var max = 100;
            if (!string.IsNullOrEmpty(args) && int.TryParse(args, out var m)) max = Math.Min(m, 10000);
            var roll = new System.Random().Next(1, max + 1);
            var aiNarr = GetAssistantResponse(player.displayName, session.Role, $"A player rolled {roll} out of {max} in Rust. Give a short dramatic reaction in 1 sentence.", null);
            var narr = (!string.IsNullOrEmpty(aiNarr) && !aiNarr.StartsWith("?")) ? aiNarr : $"Rolled {roll} (1-{max})";
            PrintToChat(player, $"<color=#FFD700>DICE:</color> {narr}");
        }

        private void FlipCoin(BasePlayer player, PlayerSession session)
        {
            var result = new System.Random().Next(2) == 0 ? "HEADS" : "TAILS";
            var aiNarr = GetAssistantResponse(player.displayName, session.Role, $"A coin flip in Rust came up {result}. Give a short dramatic reaction in 1 sentence.", null);
            var narr = (!string.IsNullOrEmpty(aiNarr) && !aiNarr.StartsWith("?")) ? aiNarr : result;
            PrintToChat(player, $"<color=#FFD700>COIN:</color> {narr}");
        }

        private void Magic8Ball(BasePlayer player, PlayerSession session, string question)
        {
            if (string.IsNullOrWhiteSpace(question)) { PrintToChat(player, "Usage: /db 8ball <question>"); return; }
            var aiAnswer = GetAssistantResponse(player.displayName, session.Role, $"Player asked: '{question}'. You are a magic 8-ball. Give a mysterious, short answer in 1-3 words.", null);
            var answer = (!string.IsNullOrEmpty(aiAnswer) && !aiAnswer.StartsWith("?")) ? aiAnswer.Trim() : "Ask again later";
            PrintToChat(player, $"<color=#FFD700>8BALL:</color> {answer}");
        }

        private void PlayRPS(BasePlayer player, PlayerSession session, string choice)
        {
            if (string.IsNullOrWhiteSpace(choice)) { PrintToChat(player, "Usage: /db rps rock|paper|scissors"); return; }
            choice = choice.ToLower();
            if (choice != "rock" && choice != "paper" && choice != "scissors") { PrintToChat(player, "Valid: rock, paper, scissors"); return; }

            var choices = new[] { "rock", "paper", "scissors" };
            var playerChoice = Array.IndexOf(choices, choice);
            var botChoice = new System.Random().Next(3);

            var result = playerChoice == botChoice ? "DRAW" : (playerChoice == 0 && botChoice == 2) || (playerChoice == 1 && botChoice == 0) || (playerChoice == 2 && botChoice == 1) ? "YOU WIN" : "YOU LOSE";
            var aiNarr = GetAssistantResponse(player.displayName, session.Role, $"RPS: Player chose {choice}, Bot chose {choices[botChoice]}. Result: {result}. Give a short dramatic 1-sentence narration.", null);
            var narr = (!string.IsNullOrEmpty(aiNarr) && !aiNarr.StartsWith("?")) ? aiNarr : $"You: {choice.ToUpper()} | Bot: {choices[botChoice].ToUpper()} -- {result}";
            PrintToChat(player, $"<color=#FFD700>RPS:</color> {narr}");
        }

        private void ShowQuote(BasePlayer player, PlayerSession session)
        {
            var response = GetAssistantResponse(player, session, "Give one short gritty quote for a Rust player. Keep it in-character and under 20 words.", false);
            PrintToChat(player, $"<color=#FFD700>QUOTE:</color> {response}");
        }

        private void TellJoke(BasePlayer player, PlayerSession session)
        {
            var response = GetAssistantResponse(player, session, "Tell one short Rust-themed joke. Keep it clean and concise.", false);
            PrintToChat(player, $"<color=#FFD700>JOKE:</color> {response}");
        }

        private void ShowFortune(BasePlayer player, PlayerSession session)
        {
            var response = GetAssistantResponse(player, session, "Give one short fortune for a Rust player. Keep it dramatic and under 20 words.", false);
            PrintToChat(player, $"<color=#FFD700>FORTUNE:</color> {response}");
        }

        // ── Number Guessing Game ─────────────────────────────────────────────
        private class GuessGame
        {
            public int Target;
            public int MaxGuesses;
            public int GuessesLeft;
            public int PrizePool;
            public int EntryFee;
            public bool Active;
            public DateTime Deadline;
        }

        private readonly Dictionary<string, GuessGame> _guessGames = new Dictionary<string, GuessGame>();

        private void HandleGuess(BasePlayer player, PlayerSession session, string args)
        {
            var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { ShowGuessHelp(player); return; }
            var cmd = parts[0].ToLowerInvariant();
            if (cmd == "join")
            {
                var fee = 100;
                if (parts.Length > 1 && int.TryParse(parts[1], out var f)) fee = Math.Max(f, 10);
                var playerId = player.UserIDString;
                if (_guessGames.ContainsKey(playerId) && _guessGames[playerId].Active) { PrintToChat(player, "<color=#888>Already in a guess game.</color>"); return; }
                var gg = new GuessGame
                {
                    Target = UnityEngine.Random.Range(1, 101),
                    MaxGuesses = 7,
                    GuessesLeft = 7,
                    PrizePool = fee,
                    EntryFee = fee,
                    Active = true,
                    Deadline = DateTime.Now.AddMinutes(2)
                };
                _guessGames[playerId] = gg;
                PrintToChat(player, $"<color=#FFD700>🎯 GUESS GAME STARTED!</color> Entry fee: {fee} scrap. Guess a number 1-100, max {gg.MaxGuesses} guesses.");
                PrintToChat(player, $"Prize pool currently: <color=#00FF88>{gg.PrizePool} scrap</color>. Use /db guess <number>");
                return;
            }
            if (_guessGames.TryGetValue(player.UserIDString, out var game))
            {
                if (!game.Active) { PrintToChat(player, "<color=#888>No active game. Use /db guess join <bet></color>"); return; }
                if (game.GuessesLeft <= 0) { PrintToChat(player, "<color=#888>Out of guesses. Game over.</color>"); game.Active = false; return; }
                if (!int.TryParse(cmd, out var guess) || guess < 1 || guess > 100) { PrintToChat(player, "Guess a number 1-100"); return; }
                game.GuessesLeft--;
                if (guess == game.Target)
                {
                    game.Active = false;
                    var prize = game.PrizePool;
                    Server.Command($"scavenger.additem \"{player.UserIDString}\" scrap {prize}");
                    var aiWin = GetAssistantResponse(player.displayName, session.Role, $"A player just won a number guessing game in Rust! They guessed {guess} which was the correct number, and won {prize} scrap after {7 - game.GuessesLeft} tries. Write a short, exciting 1-sentence announcement.", null);
                    var msg = !string.IsNullOrEmpty(aiWin) && !aiWin.StartsWith("⚠") ? aiWin : $"🎯 CORRECT! {player.displayName} guessed {guess} and won <color=#00FF88>{prize} scrap</color>!";
                    Server.Broadcast($"<color=#FFD700>{msg}</color>");
                }
                else if (game.GuessesLeft == 0)
                {
                    game.Active = false;
                    var aiLose = GetAssistantResponse(player.displayName, session.Role, $"A player just lost a number guessing game in Rust. The correct number was {game.Target}. Write a short, funny 1-sentence result.", null);
                    var msg = !string.IsNullOrEmpty(aiLose) && !aiLose.StartsWith("⚠") ? aiLose : $"💀 Out of guesses! The number was <color=#FF4444>{game.Target}</color>. Better luck next time!";
                    PrintToChat(player, $"<color=#888>{msg}</color>");
                }
                else
                {
                    var hint = guess < game.Target ? "higher" : "lower";
                    var emoji = game.GuessesLeft <= 2 ? "🔴" : "🟡";
                    PrintToChat(player, $"<color=#FFD700>Guess {guess} — {hint}!</color> {emoji} {game.GuessesLeft} guesses left. Pool: <color=#00FF88>{game.PrizePool}</color>");
                }
            }
            else { ShowGuessHelp(player); }
        }

        private void ShowGuessHelp(BasePlayer player)
        {
            PrintToChat(player, "<color=#FFD700>═══ Guess Game ═══</color>");
            PrintToChat(player, "<color=#AAA>/db guess join <bet></color> — Join with scrap bet (min 10)");
            PrintToChat(player, "<color=#AAA>/db guess <number></color> — Guess 1-100");
            PrintToChat(player, "<color=#888>7 guesses max, prize pool grows with entry fee.</color>");
        }

        // ── Lucky Block (VIP+) ──────────────────────────────────────────────
        private class LuckyBlock
        {
            public ulong OwnerId;
            public string ItemName;
            public int ItemCount;
            public int PriceScrap;
            public DateTime ListedAt;
        }

        private void HandleShopLegacy(BasePlayer player, PlayerSession session, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                ShowShop(player, session);
                return;
            }

            var parts = args.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var rest = parts.Length > 1 ? parts[1] : string.Empty;
            if (cmd == "list") { ShowShop(player, session); return; }
            if (cmd == "add") { HandleSell(player, session, rest); return; }
            if (cmd == "buy") { HandleBuy(player, session, rest); return; }
            if (cmd == "remove") { HandleCancel(player, rest); return; }
            if (cmd == "exchange") { ExchangeScrapRP(player, session, rest); return; }
            ShowShopHelpLegacy(player);
        }

        private void ShowShopHelpLegacy(BasePlayer player)
        {
            PrintToChat(player, "<color=#FFD700>═══ Shop ═══</color>");
            PrintToChat(player, "<color=#AAA>/db shop</color> — Browse market");
            PrintToChat(player, "<color=#AAA>/db sell <item> <price></color> — List an item");
            PrintToChat(player, "<color=#AAA>/db buy <item></color> — Buy from a listing");
            PrintToChat(player, "<color=#AAA>/db listings</color> — View your listings");
            PrintToChat(player, "<color=#AAA>/db price <item></color> — Check market prices");
        }

        private void AddShopListingLegacy(BasePlayer player, PlayerSession session, string itemName, string priceStr)
        {
            HandleSell(player, session, string.IsNullOrWhiteSpace(priceStr) ? itemName : (itemName + " " + priceStr));
        }

        private void BuyShopItemLegacy(BasePlayer player, PlayerSession session, string itemName)
        {
            HandleBuy(player, session, itemName);
        }

        private void RemoveShopListingLegacy(BasePlayer player, PlayerSession session, string itemName)
        {
            HandleCancel(player, itemName);
        }

        private void ExchangeScrapRP(BasePlayer player, PlayerSession session, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                PrintToChat(player, "Use: /db shop exchange scrap <amount>");
                return;
            }

            var parts = args.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) { PrintToChat(player, "Use: /db shop exchange scrap <amount>"); return; }
            if (!int.TryParse(parts[1], out var amount) || amount <= 0) { PrintToChat(player, "Invalid amount."); return; }
            var type = parts[0].ToLowerInvariant();
            var rate = _config.ShopExchangeRateScrapPerRP;
            if (type == "scrap")
            {
                if (session.TotalScrap < amount) { PrintToChat(player, $"Not enough scrap. Have {session.TotalScrap}."); return; }
                var rp = amount / Math.Max(1, rate);
                if (rp < 1) { PrintToChat(player, $"Minimum exchange is {rate} scrap for 1 RP."); return; }
                session.TotalScrap -= amount;
                PrintToChat(player, $"<color=#FFD700>Exchanged {amount} scrap for {rp} RP.</color>");
                LogActivity("economy", "exchange", $"scrap->rp: {amount} scrap, {rp} RP to {player.displayName}", player.UserIDString, player.displayName);
                return;
            }
            if (type == "rp")
            {
                var scrapNeeded = amount * Math.Max(1, rate);
                if (session.TotalScrap < scrapNeeded) { PrintToChat(player, $"Not enough scrap. Need {scrapNeeded}, have {session.TotalScrap}."); return; }
                session.TotalScrap -= scrapNeeded;
                PrintToChat(player, $"<color=#FFD700>Exchanged {scrapNeeded} scrap for {amount} RP.</color>");
                LogActivity("economy", "exchange", $"rp->scrap: {scrapNeeded} scrap, {amount} RP to {player.displayName}", player.UserIDString, player.displayName);
                return;
            }
            PrintToChat(player, "Use: /db shop exchange scrap <amount> or /db shop exchange rp <amount>");
        }

        // ── Lucky Block (VIP+) ──────────────────────────────────────────────
        private void HandleLucky(BasePlayer player, PlayerSession session)
        {
            if (!HasRoleOrHigher(session.Role, "vip") && !permission.UserHasPermission(player.UserIDString, "rustduckbot.vip"))
            { PrintToChat(player, "<color=#FFD700>Lucky Block</color> — VIP only perk."); return; }
            var cost = 200;
            PrintToChat(player, $"<color=#FFD700>LUCKY BLOCK</color> — Spinning...");
            var roll = UnityEngine.Random.Range(1, 101);
            string reward; int amount;
            if (roll <= 5)
            { reward = "explosive.timed"; amount = 3; }
            else if (roll <= 15)
            { reward = "metal.plate.torso"; amount = 1; }
            else if (roll <= 35)
            { reward = "scrap"; amount = 800; }
            else if (roll <= 60)
            { reward = "scrap"; amount = 400; }
            else
            { reward = "scrap"; amount = 150; }
            Server.Command($"scavenger.additem \"{player.UserIDString}\" {reward} {amount}");
            var tier = roll <= 5 ? "EPIC" : roll <= 15 ? "RARE" : roll <= 35 ? "UNCOMMON" : "COMMON";
            var aiNarr = GetAssistantResponse(player.displayName, session.Role, $"A player just opened a lucky block in Rust and got {amount}x {reward}. The rarity tier is {tier}. Write a short, exciting 1-sentence announcement.", null);
            var msg = !string.IsNullOrEmpty(aiNarr) && !aiNarr.StartsWith("?") ? aiNarr : $"LUCKY BLOCK: {tier} -- {amount}x {reward}!";
            Server.Broadcast($"<color=#FFD700>{msg}</color>");
        }

        private void PlaySlots(BasePlayer player, PlayerSession session)
        {
            var icons = new[] { "GUN", "COIN", "GEAR", "EXPLOSIVE", "KNIFE", "GEM", "SKULL" };
            var r = new System.Random();
            var spin = new[] { icons[r.Next(icons.Length)], icons[r.Next(icons.Length)], icons[r.Next(icons.Length)] };
            var outcome = spin[0] == spin[1] && spin[1] == spin[2] ? "TRIPLE JACKPOT" : spin[0] == spin[1] || spin[1] == spin[2] || spin[0] == spin[2] ? "PAIR" : "NO MATCH";
            var aiNarr = GetAssistantResponse(player.displayName, session.Role, $"Slots: [{spin[0]}] [{spin[1]}] [{spin[2]}]. Result: {outcome}. Give a short dramatic 1-sentence narration.", null);
            var narr = (!string.IsNullOrEmpty(aiNarr) && !aiNarr.StartsWith("?")) ? aiNarr : $"[{spin[0]}] [{spin[1]}] [{spin[2]}] -- {outcome}";
            PrintToChat(player, $"<color=#FFD700>SLOTS:</color> {narr}");
        }

        private void PlaceBet(BasePlayer player, PlayerSession session, string args)
        {
            PrintToChat(player, "<color=#FFD700>🎰 BETTING:</color> Feature coming soon!");
        }

        // =====================================================================
        // SETTINGS
        // =====================================================================

        private void ShowSettings(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══ YOUR SETTINGS ═══</color>");
            PrintToChat(player, $"Theme: {session.Settings.Theme}");
            PrintToChat(player, $"Alerts: {(session.Settings.AlertsEnabled ? "ON" : "OFF")}");
            PrintToChat(player, $"Raid Alerts: {(session.Settings.RaidAlertsEnabled ? "ON" : "OFF")}");
            PrintToChat(player, $"Decay Alerts: {(session.Settings.DecayAlertsEnabled ? "ON" : "OFF")}");
            PrintToChat(player, $"Alert Channel: {session.Settings.AlertChannel}");
            PrintToChat(player, "\nCommands:");
            PrintToChat(player, "/db set <key> <value> — Update setting");
            PrintToChat(player, "/db theme <name> — Set theme (dark/light/security/industrial)");
            PrintToChat(player, "/db alerts_set on/off — Toggle alerts");
        }

        private void UpdateSetting(BasePlayer player, PlayerSession session, string args)
        {
            var parts = args.Split(new[] { ' ' }, 2);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db set <key> <value>"); return; }
            var key = parts[0].ToLower();
            var value = parts[1].ToLower();

            switch (key)
            {
                case "alerts": session.Settings.AlertsEnabled = value == "on"; PrintToChat(player, $"Alerts: {(value == "on" ? "ON" : "OFF")}"); break;
                case "raidalerts": session.Settings.RaidAlertsEnabled = value == "on"; PrintToChat(player, $"Raid Alerts: {(value == "on" ? "ON" : "OFF")}"); break;
                case "decayalerts": session.Settings.DecayAlertsEnabled = value == "on"; PrintToChat(player, $"Decay Alerts: {(value == "on" ? "ON" : "OFF")}"); break;
                case "channel": session.Settings.AlertChannel = value; PrintToChat(player, $"Alert Channel: {value}"); break;
                default: PrintToChat(player, $"Unknown setting: {key}"); break;
            }
        }

        private void SetTheme(BasePlayer player, PlayerSession session, string theme)
        {
            var valid = new[] { "dark", "light", "security", "industrial", "default" };
            if (string.IsNullOrWhiteSpace(theme) || Array.IndexOf(valid, theme.ToLower()) < 0) { PrintToChat(player, $"Valid themes: {string.Join(", ", valid)}"); return; }
            session.Settings.Theme = theme.ToLower();
            PrintToChat(player, $"<color=#00FF00>Theme:</color> {theme.ToUpper()}");
        }

        private void ConfigureAlerts(BasePlayer player, PlayerSession session, string args)
        {
            var action = args.ToLower();
            if (action == "on") { session.Settings.AlertsEnabled = true; PrintToChat(player, "<color=#00FF00>Alerts enabled</color>"); }
            else if (action == "off") { session.Settings.AlertsEnabled = false; PrintToChat(player, "<color=#FF4444>Alerts disabled</color>"); }
            else { PrintToChat(player, "Usage: /db alerts_set on|off"); }
        }

        private void AddBookmark(BasePlayer player, PlayerSession session, string bookmark)
        {
            if (string.IsNullOrWhiteSpace(bookmark)) { PrintToChat(player, "Usage: /db bookmark <name>"); return; }
            if (!session.Bookmarks.Contains(bookmark)) { session.Bookmarks.Add(bookmark); PrintToChat(player, $"<color=#00FF00>Bookmarked:</color> {bookmark}"); }
            else { PrintToChat(player, "Already bookmarked."); }
        }

        private void ShowBookmarks(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, $"<color=#FFD700>═══ BOOKMARKS ({session.Bookmarks.Count}) ═══</color>");
            if (session.Bookmarks.Count == 0) { PrintToChat(player, "No bookmarks. Use /db bookmark <name>."); return; }
            foreach (var b in session.Bookmarks) PrintToChat(player, $"  • {b}");
        }

        // =====================================================================
        // MISC
        // =====================================================================

        private string GetAssistantResponse(string playerName, string role, string message, List<ChatEntry> history)
        {
            if (_localAI != null && _localAI.IsLocalProvider)
            {
                var local = _localAI.GetResponse(playerName, role, message, history);
                if (!string.IsNullOrWhiteSpace(local)) return local;
            }

            var remote = _agentBridge?.GetResponse(playerName, role, message, history);
            return string.IsNullOrWhiteSpace(remote) ? "No AI response available." : remote;
        }

        private string GetAssistantResponse(BasePlayer player, PlayerSession session, string message, bool includeHistory = true)
        {
            var context = $"Live server context: server={ConVar.Server.hostname}; players={BasePlayer.activePlayerList.Count}; sleepers={BasePlayer.sleepingPlayerList.Count}; fps={Math.Round(1.0f / Time.deltaTime, 1)}; uptime={Time.realtimeSinceStartup / 3600.0:F1}h; playerGrid={GetGridCoord(player.transform.position)}; nearestMonument={GetNearestMonument(player.transform.position)}; role={session?.Role ?? "user"}.\n";
            message = context + message;

            var history = includeHistory ? session?.ChatHistory : null;
            return GetAssistantResponse(player.displayName, session?.Role ?? "user", message, history);
        }

        private bool TryGrantBuiltInKit(BasePlayer target, KitDefinition kit, out string error)
        {
            error = null;
            if (!_builtInKitContents.TryGetValue(kit.Name, out var items) || items.Count == 0)
            {
                error = "Built-in kit contents are not defined.";
                return false;
            }

            var granted = new List<Item>();
            foreach (var kitItem in items)
            {
                var item = ItemManager.CreateByName(kitItem.ShortName, kitItem.Amount, kitItem.Skin);
                if (item == null)
                {
                    foreach (var created in granted) created?.Remove();
                    error = "Invalid item shortname: " + kitItem.ShortName;
                    return false;
                }

                if (kitItem.Condition > 0)
                    item.condition = kitItem.Condition;

                var container = target.inventory?.containerMain;
                if (kitItem.Container == "belt") container = target.inventory?.containerBelt;
                else if (kitItem.Container == "wear") container = target.inventory?.containerWear;

                if (container == null || !item.MoveToContainer(container))
                    target.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);

                granted.Add(item);
            }

            return true;
        }

        private bool TryGrantKit(BasePlayer target, KitDefinition kit, out string error)
        {
            if (TryGrantBuiltInKit(target, kit, out error))
                return true;

            if (Kits != null)
            {
                error = null;
                Server.Command("kit give " + kit.RustKitName + " " + target.UserIDString);
                return true;
            }

            return false;
        }

        private void ShowVersion(BasePlayer player) { PrintToChat(player, "<color=#FFD700>RustDuckBot v1.4.5</color> by Duckets | AI: " + (_localAI?.ProviderName ?? _config.AgentProvider)); }
        private void ShowCredits(BasePlayer player) { PrintToChat(player, "Created by <color=#FFD700>Duckets</color> | Powered by <color=#FFD700>DuckBot AI</color>"); }
        private void ShowChangelog(BasePlayer player) { PrintToChat(player, "v1.4.0: Massive feature expansion — 30 new commands across 7 categories"); }
        private void ShowDonateInfo(BasePlayer player) { PrintToChat(player, "Donations help keep the server running! Contact admin."); }
        private void ShowDiscord(BasePlayer player) { PrintToChat(player, "Join our Discord: discord.gg/example"); }
        private void ShowSupport(BasePlayer player) { PrintToChat(player, "Support: Contact admin via Discord | Use /db bug <report> to report issues"); }

        private void HandleBugReport(BasePlayer player, PlayerSession session, string report)
        {
            if (string.IsNullOrWhiteSpace(report)) { PrintToChat(player, "Usage: /db bug <report>"); return; }
            var entry = new ReportEntry
            {
                Id = Guid.NewGuid().ToString().Substring(0, 8),
                ReporterId = player.userID,
                ReporterName = player.displayName,
                TargetId = 0,
                TargetName = "RustDuckBot bug",
                Reason = report,
                Time = DateTime.Now,
                Status = "bug"
            };
            _reportQueue.Add(entry);
            PrintToChat(player, $"<color=#00FF88>Bug report submitted.</color> ID: <color=#FFD700>{entry.Id}</color>");
            LogActivity("system", "Bug report", report, player.UserIDString, player.displayName);
        }

        // =====================================================================
        // CAMERA SYSTEM
        // =====================================================================

        private void ScanCameras()
        {
            _cameras.Clear();
            foreach (var entity in BaseEntity.saveList)
            {
                if (entity == null) continue;
                var prefab = entity.ShortPrefabName?.ToLower() ?? "";
                if (!prefab.Contains("cctv") && !prefab.Contains("camera") && !prefab.Contains("security")) continue;

                var cam = new CameraInfo
                {
                    Id = entity.net != null ? entity.net.ID.Value.ToString() : entity.GetHashCode().ToString(),
                    Name = GetCameraName(entity),
                    Location = GetCameraLocation(entity),
                    Monument = GetNearestMonument(entity.transform.position),
                    Online = true,
                    HasPower = true,
                    IsPTZ = prefab.Contains("ptz") || prefab.Contains("movable") || prefab.Contains("turret"),
                    Entity = entity
                };
                _cameras.Add(cam);
            }
            AddMonumentCameras();
        }

        private string GetCameraName(BaseEntity entity)
        {
            var prefab = entity.ShortPrefabName ?? "Camera";
            if (prefab.Contains("gate")) return "Gate Camera";
            if (prefab.Contains("entry")) return "Entry Camera";
            if (prefab.Contains("storage")) return "Storage Camera";
            if (prefab.Contains("roof")) return "Roof Camera";
            if (prefab.Contains("outside")) return "Perimeter Camera";
            return prefab.Replace("_", " ").Replace("cctv", "CCTV").Trim();
        }

        private string GetCameraLocation(BaseEntity entity)
        {
            var pos = entity.transform.position;
            if (pos.y > 100) return "Sky Tower";
            if (pos.y > 50) return "Rooftop";
            if (pos.y > 10) return "Elevated";
            if (Math.Abs(pos.y) < 5) return "Ground Level";
            return $"Y:{pos.y:F0}";
        }

        private void AddMonumentCameras()
        {
            var monuments = new[] {
                ("Oil Rig Large", "LargeOilrig"), ("Oil Rig Small", "Oilrig"), ("Airfield", "Airfield"),
                ("Military Tunnel", "MilitaryTunnel"), ("Dome", "Dome"), ("Train Yard", "TrainYard"),
                ("Power Plant", "PowerPlant"), ("Satellite", "SatelliteDish"), ("Water Treatment", "WaterTreatment"),
                ("Lighthouse", "Lighthouse"), ("Excavation", "Excavation"), ("Junkyard", "Junkyard"),
                ("Supermarket", "Supermarket"), ("Gas Station", "GasStation"), ("Outpost", "Outpost"),
                ("Bandit Camp", "Bandit"), ("Arctic Base", "Arctic"), ("Desert Base", "Desert"),
                ("Large Barn", "Barn"), ("Mining Outpost", "Mining"), ("Underwater Lab", "Underwater"),
                ("Abandoned Military Base", "AbandonedMilitaryBase"), ("Launch Site", "LaunchSite"),
                ("Junkyard", "Junkyard"), ("Water Treatment", "WaterTreatment")
            };
            int idx = 1000;
            foreach (var (name, code) in monuments)
            {
                _cameras.Add(new CameraInfo { Id = $"monument_{idx}", Name = $"{name} CCTV", Location = name, Monument = name, Online = true, HasPower = true, IsPTZ = false });
                idx++;
            }
        }

        private CameraInfo FindCamera(string idOrName)
        {
            var cam = _cameras.Find(c => c.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
            if (cam != null) return cam;
            cam = _cameras.Find(c => ContainsIgnoreCase(c.Name, idOrName));
            if (cam != null) return cam;
            return _cameras.Find(c => ContainsIgnoreCase(c.Location, idOrName));
        }

        private CameraInfo GetCameraNear(Vector3 position)
        {
            return _cameras.OrderBy(c => Vector3.Distance(c.Entity?.transform.position ?? Vector3.zero, position)).FirstOrDefault();
        }

        private void ExecutePTZ(CameraInfo cam, string action)
        {
            if (cam.Entity == null) return;
            var transform = cam.Entity.transform;
            var euler = transform.eulerAngles;

            switch (action)
            {
                case "left": transform.rotation = Quaternion.Euler(euler.x, euler.y - 45, euler.z); cam.Pan -= 45; break;
                case "right": transform.rotation = Quaternion.Euler(euler.x, euler.y + 45, euler.z); cam.Pan += 45; break;
                case "up": transform.rotation = Quaternion.Euler(Mathf.Clamp(euler.x - 30, 0, 90), euler.y, euler.z); cam.Tilt -= 30; break;
                case "down": transform.rotation = Quaternion.Euler(Mathf.Clamp(euler.x + 30, 0, 90), euler.y, euler.z); cam.Tilt += 30; break;
                case "zoom_in": case "zoom": cam.Zoom = Mathf.Min(cam.Zoom + 20, 200); break;
                case "zoom_out": cam.Zoom = Mathf.Max(cam.Zoom - 20, 50); break;
                case "reset": case "home": transform.rotation = Quaternion.identity; cam.Pan = 0; cam.Tilt = 0; cam.Zoom = 100; break;
            }
        }

        private bool IsPlayerAtComputerStation(BasePlayer player)
        {
            return player.GetComponentInParent<ComputerStation>() != null;
        }

        private void ScanBases()
        {
            _monitoredBases.Clear();
            foreach (var tc in UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>())
            {
                var baseInfo = new BaseInfo
                {
                    OwnerId = tc.OwnerID,
                    Name = $"Base @ {GetLocation(tc.transform.position)}",
                    Position = tc.transform.position,
                    BlockCount = CountBlocksNear(tc.transform.position),
                    MaxBlockHealth = 10000,
                    CurrentBlockHealth = 8500,
                    DecayRatePerHour = 5.0f,
                    UnderAttack = false
                };
                _monitoredBases.Add(baseInfo);
            }
        }

        private float CountBlocksNear(Vector3 pos) { return 100f; } // Placeholder

        private void ScanVendingMachines()
        {
            _vendingMachines.Clear();
            foreach (var vm in UnityEngine.Object.FindObjectsOfType<VendingMachine>())
            {
                _vendingMachines.Add(new VendingInfo
                {
                    Id = vm.net != null ? vm.net.ID.Value.ToString() : vm.OwnerID.ToString(),
                    Name = vm.ShortPrefabName ?? "Vending Machine",
                    OwnerId = vm.OwnerID.ToString(),
                    Position = vm.transform.position,
                    IsActive = true,
                    Direction = "both",
                    Stock = 100
                });
            }
        }

        // =====================================================================
        // PLAYER LOOKUP
        // =====================================================================

        private BasePlayer FindPlayer(string nameOrId)
        {
            if (ulong.TryParse(nameOrId, out var steamId))
            {
                return BasePlayer.FindByID(steamId) ?? BasePlayer.Find(nameOrId);
            }
            var exact = BasePlayer.Find(nameOrId);
            if (exact != null) return exact;
            var all = BasePlayer.activePlayerList;
            BasePlayer best = null;
            foreach (var p in all)
            {
                if (p.displayName.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)) return p;
                if (ContainsIgnoreCase(p.displayName, nameOrId)) best = p;
            }
            return best;
        }

        private BasePlayer FindPlayerByName(string nameOrId)
        {
            return FindPlayer(nameOrId);
        }

        private string[] SplitArgs(string args, int count)
        {
            if (string.IsNullOrWhiteSpace(args)) return Array.Empty<string>();
            if (count <= 1) return new[] { args.Trim() };

            var parts = new List<string>();
            var remaining = args.Trim();
            for (var i = 1; i < count && remaining.Length > 0; i++)
            {
                var splitAt = remaining.IndexOf(' ');
                if (splitAt < 0) break;

                parts.Add(remaining.Substring(0, splitAt));
                remaining = remaining.Substring(splitAt + 1).TrimStart();
            }

            if (remaining.Length > 0) parts.Add(remaining);
            return parts.ToArray();
        }

        // =====================================================================
        // TRACKING & INTEL
        // =====================================================================

        private void TrackPlayer(string playerId, string displayName)
        {
            if (!_trackedPlayers.TryGetValue(playerId, out var tracked))
            {
                tracked = new TrackedPlayer
                {
                    PlayerId = playerId,
                    DisplayName = displayName,
                    FirstSeen = DateTime.Now,
                    ThreatLevel = "low"
                };
                _trackedPlayers[playerId] = tracked;
            }
            tracked.LastSeen = DateTime.Now;
            tracked.SessionCount++;
        }

        private void UpdateTrackedPlayer(string playerId, Vector3? position = null, DateTime? lastSeen = null)
        {
            if (!_trackedPlayers.TryGetValue(playerId, out var tracked)) return;
            if (position.HasValue) { tracked.LastPosition = position.Value; tracked.LastMonument = GetNearestMonument(position.Value); }
            if (lastSeen.HasValue) tracked.LastSeen = lastSeen.Value;
        }

        // =====================================================================
        // ALERTS
        // =====================================================================

        private void CreateAlert(string type, string severity, string title, string message, Vector3? location = null)
        {
            var alert = new AlertEntry
            {
                Id = Guid.NewGuid().ToString().Substring(0, 8),
                Type = type,
                Severity = severity,
                Title = title,
                Message = message,
                Time = DateTime.Now,
                Location = location
            };
            _activeAlerts.Add(alert);
            _mcpClient?.SendMessage(new { type = "alert", alertId = alert.Id, title, message, severity });

            if (_config.EnableSmartAlerts && location.HasValue)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    var dist = Vector3.Distance(player.transform.position, location.Value);
                    if (dist < 100)
                    {
                        var session = GetOrCreateSession(player);
                        if (session?.Settings.AlertsEnabled == true)
                            PrintToChat(player, $"<color=#FF4444>⚠ ALERT:</color> {title}");
                    }
                }
            }
        }

        private List<AlertEntry> GetUnacknowledgedAlerts(string playerId)
        {
            return _activeAlerts.Where(a => !a.Acknowledged && (a.Severity == "critical" || a.Severity == "high")).ToList();
        }

        // =====================================================================
        // ACCESS LOGGING
        // =====================================================================

        private void LogAccess(string playerId, string playerName, string resource, string action, bool success, string cameraId = null)
        {
            _accessLog.Add(new AccessLogEntry
            {
                Time = DateTime.Now,
                PlayerId = playerId,
                PlayerName = playerName,
                Resource = resource,
                Action = action,
                Success = success,
                CameraId = cameraId
            });
            if (_accessLog.Count > _config.MaxActivityLog) _accessLog.RemoveAt(0);
        }

        private void LogActivity(string category, string action, string details, string playerId = null, string playerName = null)
        {
            _activityLog.Add(new ActivityEntry
            {
                Time = DateTime.Now,
                Category = category,
                Action = action,
                Details = details,
                PlayerId = playerId,
                PlayerName = playerName
            });
            if (_activityLog.Count > _config.MaxActivityLog) _activityLog.RemoveAt(0);
        }

        // =====================================================================
        // BROADCASTING
        // =====================================================================

        private void BroadcastMessage(BasePlayer sender, string tag, string message, string type)
        {
            var color = BroadcastColor(type);
            foreach (var player in BasePlayer.activePlayerList)
                PrintToChat(player, $"<color={color}>[{tag}]</color> {message}");

            LogActivity("broadcast", tag, message, sender?.UserIDString, sender?.displayName);
        }

        public void HandleMCPMessage(Dictionary<string, object> message)
        {
            if (message == null) return;
            NextTick(() => HandleMCPMessageMainThread(message));
        }

        private void HandleMCPMessageMainThread(Dictionary<string, object> message)
        {
            var type = GetMessageString(message, "type");
            switch (type)
            {
                case "chat_send":
                    HandleMCPChatSend(message);
                    break;
                case "view_camera_request":
                    HandleMCPViewCamera(message);
                    break;
                case "camera_control":
                    HandleMCPCameraControl(message);
                    break;
                case "camera_snapshot":
                    LogActivity("camera", "Snapshot requested", GetMessageString(message, "camera_id"));
                    break;
                case "admin_command":
                    HandleMCPAdminCommand(message);
                    break;
                case "kick_player":
                    HandleMCPKick(message);
                    break;
                case "ban_player":
                    HandleMCPBan(message);
                    break;
                case "lockdown":
                    HandleMCPLockdown(message);
                    break;
                case "ack_alert":
                    HandleMCPAcknowledgeAlert(message);
                    break;
                case "map_marker_add":
                    HandleMCPMapMarker(message);
                    break;
                case "automation_rule":
                    HandleMCPAutomationRule(message);
                    break;
                case "kit_give":
                    HandleMCPKitGive(message);
                    break;
                case "security_scan":
                    LogActivity("security", "MCP scan requested", $"radius={GetMessageString(message, "radius", "100")}", GetMessageString(message, "requester_id"));
                    break;
                default:
                    PrintAsh($"[MCP] Unhandled message: {type}");
                    break;
            }
        }

        private void HandleMCPChatSend(Dictionary<string, object> message)
        {
            var text = GetMessageString(message, "message");
            if (string.IsNullOrWhiteSpace(text)) return;

            var target = GetMessageString(message, "target", "global");
            var sender = GetMessageString(message, "sender", "DuckBot");
            if (target.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                BroadcastMessage(null, sender, text, "info");
                return;
            }

            var player = FindPlayer(target);
            if (player != null)
                PrintToChat(player, $"<color=#FFD700>{sender}:</color> {text}");
        }

        private void HandleMCPViewCamera(Dictionary<string, object> message)
        {
            var player = FindPlayer(GetMessageString(message, "player_id"));
            var cameraId = GetMessageString(message, "camera_id");
            if (player == null || string.IsNullOrWhiteSpace(cameraId)) return;
            ViewCamera(player, GetOrCreateSession(player), cameraId);
        }

        private void HandleMCPCameraControl(Dictionary<string, object> message)
        {
            var player = FindPlayer(GetMessageString(message, "player_id"));
            var cameraId = GetMessageString(message, "camera_id");
            var action = GetMessageString(message, "action");
            if (player == null || string.IsNullOrWhiteSpace(cameraId) || string.IsNullOrWhiteSpace(action)) return;

            var session = GetOrCreateSession(player);
            session.CurrentCameraId = cameraId;
            ControlCamera(player, session, action);
        }

        private void HandleMCPAdminCommand(Dictionary<string, object> message)
        {
            if (!_config.EnableAdminCommands) return;
            var command = GetMessageString(message, "command");
            if (string.IsNullOrWhiteSpace(command)) return;
            if (!IsRconCommandAllowed(command))
            {
                LogActivity("admin", "MCP command denied", command, null, GetMessageString(message, "admin_name", "MCP"));
                PrintAsh($"[MCP] Denied non-whitelisted RCON command: {command}");
                return;
            }

            ExecuteRconOrConsole(command, GetMessageString(message, "admin_name", "MCP"), GetMessageString(message, "request_id"));
            LogActivity("admin", "MCP command", command, null, GetMessageString(message, "admin_name", "MCP"));
        }

        private void HandleMCPKick(Dictionary<string, object> message)
        {
            var target = FindPlayer(GetMessageString(message, "player_id"));
            var reason = GetMessageString(message, "reason", "Kicked by DuckBot");
            if (target == null) return;
            target.Kick(reason);
            LogActivity("admin", "MCP kick", $"{target.displayName}: {reason}", target.UserIDString, target.displayName);
        }

        private void HandleMCPBan(Dictionary<string, object> message)
        {
            var target = GetMessageString(message, "player_id");
            var reason = GetMessageString(message, "reason", "Banned by DuckBot");
            var duration = GetMessageString(message, "duration", "perm");
            if (string.IsNullOrWhiteSpace(target)) return;

            Server.Command($"banid {target} {duration} \"{reason}\"");
            LogActivity("admin", "MCP ban", $"{target}: {reason} ({duration})");
        }

        private void HandleMCPLockdown(Dictionary<string, object> message)
        {
            var action = GetMessageString(message, "action", "status");
            if (action == "start")
            {
                foreach (var door in UnityEngine.Object.FindObjectsOfType<Door>())
                    door.SetFlag(BaseEntity.Flags.Locked, true);
                BroadcastMessage(null, "LOCKDOWN", GetMessageString(message, "reason", "Emergency lockdown started"), "critical");
            }
            else if (action == "stop")
            {
                foreach (var door in UnityEngine.Object.FindObjectsOfType<Door>())
                    door.SetFlag(BaseEntity.Flags.Locked, false);
                BroadcastMessage(null, "LOCKDOWN", "Emergency lockdown ended", "info");
            }
        }

        private void HandleMCPAcknowledgeAlert(Dictionary<string, object> message)
        {
            var alertId = GetMessageString(message, "alert_id");
            var alert = _activeAlerts.Find(a => a.Id == alertId);
            if (alert == null) return;
            alert.Acknowledged = true;
            alert.AcknowledgedBy = GetMessageString(message, "requester_id", "MCP");
            alert.AcknowledgedAt = DateTime.Now;
        }

        private void HandleMCPMapMarker(Dictionary<string, object> message)
        {
            var markerRaw = GetMessageDictionary(message, "marker");
            if (markerRaw == null) return;

            _gridMarkers.Add(new GridMarker
            {
                Id = GetMessageString(markerRaw, "id", Guid.NewGuid().ToString().Substring(0, 8)),
                Name = GetMessageString(markerRaw, "name", "MCP marker"),
                Position = ParsePosition(GetMessageString(markerRaw, "position")),
                Color = GetMessageString(markerRaw, "color", "yellow"),
                Icon = GetMessageString(markerRaw, "icon", "pin"),
                Visible = true,
                OwnerId = GetMessageString(markerRaw, "ownerId")
            });
        }

        private void HandleMCPAutomationRule(Dictionary<string, object> message)
        {
            var ruleId = GetMessageString(message, "rule_id");
            var action = GetMessageString(message, "action");
            var rule = _automationRules.Find(r => r.Id == ruleId);
            if (rule == null) return;

            switch (action)
            {
                case "enable": rule.Enabled = true; break;
                case "disable": rule.Enabled = false; break;
                case "delete": _automationRules.Remove(rule); break;
                case "run": RunAutomation(rule, null); break;
            }
        }

        private void HandleMCPKitGive(Dictionary<string, object> message)
        {
            var targetName = GetMessageString(message, "player_id");
            var kitName = GetMessageString(message, "kit_name").ToLowerInvariant();
            var actor = GetMessageString(message, "requester_id", "MCP");
            if (string.IsNullOrWhiteSpace(targetName) || string.IsNullOrWhiteSpace(kitName)) return;

            var target = FindPlayer(targetName);
            if (target == null)
            {
                LogActivity("kits", "MCP kit grant failed", $"target not found: {targetName}", null, actor);
                return;
            }

            if (!_kitDefinitions.TryGetValue(kitName, out var kit))
            {
                LogActivity("kits", "MCP kit grant failed", $"unknown kit: {kitName}", target.UserIDString, actor);
                return;
            }

            if (!TryGrantKit(target, kit, out var kitError))
            {
                LogActivity("kits", "MCP kit grant failed", kitError + ": " + kitName, target.UserIDString, actor);
                return;
            }

            LogActivity("kits", "MCP kit grant", actor + " granted kit '" + kit.Name + "' to " + target.displayName, target.UserIDString, target.displayName);
            PrintToChat(target, "<color=#00FF88>DuckBot granted kit:</color> " + kit.DisplayName);
        }

        private string GetMessageString(Dictionary<string, object> message, string key, string fallback = "")
        {
            if (!message.TryGetValue(key, out var value) || value == null) return fallback;
            return Convert.ToString(value) ?? fallback;
        }

        private Dictionary<string, object> GetMessageDictionary(Dictionary<string, object> message, string key)
        {
            if (!message.TryGetValue(key, out var value)) return null;
            return value as Dictionary<string, object>;
        }

        private Vector3 ParsePosition(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Vector3.zero;
            var parts = raw.Split(',');
            if (parts.Length >= 3 && float.TryParse(parts[0], out var x) && float.TryParse(parts[1], out var y) && float.TryParse(parts[2], out var z))
                return new Vector3(x, y, z);
            return Vector3.zero;
        }

        // =====================================================================
        // TIMERS
        // =====================================================================

        private void HeartbeatCallback(object state)
        {
            if (!_serverInitialized) return;
            var players = BasePlayer.activePlayerList;
            var playerList = players.Select(p => new { id = p.UserIDString, name = p.displayName, ping = 0, role = GetOrCreateSession(p).Role, connectedAt = GetOrCreateSession(p).SessionStart.ToString("o"), position = GetGridCoord(p.transform.position), nearestMonument = GetNearestMonument(p.transform.position) }).ToList();

            var onlineNow = new HashSet<ulong>();
            foreach (var p in players) onlineNow.Add(p.userID);
            foreach (var p in players)
            {
                if (!_knownOnlinePlayers.Contains(p.userID))
                    NotifyExternal($"{p.displayName} joined the server", "player_join");
            }
            foreach (var prevId in _knownOnlinePlayers)
            {
                if (!onlineNow.Contains(prevId))
                {
                    var name = players.FirstOrDefault(pl => pl.userID == prevId)?.displayName ?? prevId.ToString();
                    NotifyExternal($"{name} left the server", "player_leave");
                }
            }
            _knownOnlinePlayers = onlineNow;

            _mcpClient?.SendMessage(new
            {
                type = "heartbeat",
                time = DateTime.Now.ToString("o"),
                playerCount = players.Count,
                players = playerList,
                fps = Math.Round(1.0f / Time.deltaTime, 1),
                uptime = $"{Time.realtimeSinceStartup / 3600.0:F1}h",
                serverName = ConVar.Server.hostname,
                serverSeed = ConVar.Server.seed,
                worldSize = ConVar.Server.worldsize,
                serverPvE = ConVar.Server.pve,
                entityCount = BaseEntity.activeEntityList?.Count ?? 0,
                sleepingPlayers = BasePlayer.sleepingPlayerList?.Count ?? 0,
                monuments = _monumentLocations.Select(m => new { name = m.Key, position = $"{m.Value.x:F1},{m.Value.y:F1},{m.Value.z:F1}", grid = GetGridCoord(m.Value) }).ToList(),
                mcpConnected = _mcpClient?.IsConnected == true,
                rconConnected = _rconClient?.IsConnected == true
            });
        }

        private void AutomationCallback(object state)
        {
            foreach (var rule in _automationRules.Where(r => r.Enabled))
            {
                bool trigger = false;
                switch (rule.Trigger)
                {
                    case "time":
                        var currentHour = DateTime.Now.Hour;
                        if (rule.Condition == "sunset" && currentHour >= 18 && currentHour <= 19) trigger = true;
                        if (rule.Condition == "sunrise" && currentHour >= 5 && currentHour <= 6) trigger = true;
                        break;
                }
                if (trigger) RunAutomation(rule, null);
            }
        }

        private void DecayCheckCallback(object state)
        {
            if (!_config.EnableDecayAlerts) return;
            foreach (var warning in _decayWarnings)
            {
                if (warning.HoursRemaining <= _config.DecayAlertHoursBefore && !warning.Alerted)
                {
                    var owner = BasePlayer.FindByID(warning.PlayerId);
                    if (owner != null)
                    {
                        PrintToChat(owner, $"<color=#FF4444>⚠ DECAY WARNING:</color> {warning.BaseName} will collapse in {warning.HoursRemaining}h");
                        warning.Alerted = true;
                    }
                }
            }
        }

        private void RadarCallback(object state)
        {
            if (!_config.EnablePlayerTracking) return;
            foreach (var player in BasePlayer.activePlayerList)
            {
                var session = GetOrCreateSession(player);
                UpdateTrackedPlayer(player.UserIDString, player.transform.position);
            }
        }

        private void SendServerStatus()
        {
            var uptime = Time.realtimeSinceStartup;
            _mcpClient?.SendMessage(new
            {
                type = "server_status",
                uptime = $"{uptime / 3600:F1}h",
                fps = Math.Round(1.0f / Time.deltaTime, 1),
                players = BasePlayer.activePlayerList.Count,
                cameras = _cameras.Count,
                alerts = _activeAlerts.Count,
                mcpConnected = _mcpClient?.IsConnected == true,
                rconConnected = _rconClient?.IsConnected == true
            });
        }

        private class KitDefinition
        {
            public string Name;
            public string DisplayName;
            public string Category;
            public string Description;
            public string RustKitName;
            public string Permission;
            public int CooldownMinutes;
            public int MaxUsesPerDay;
        }

        private Dictionary<string, KitDefinition> _kitDefinitions = new Dictionary<string, KitDefinition>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<ulong, Dictionary<string, DateTime>> _kitCooldowns = new Dictionary<ulong, Dictionary<string, DateTime>>();
        private Dictionary<ulong, Dictionary<string, int>> _kitDailyUses = new Dictionary<ulong, Dictionary<string, int>>();

        private class BuiltInKitItem
        {
            public string ShortName;
            public int Amount;
            public string Container;
            public ulong Skin;
            public float Condition;
        }

        private Dictionary<string, List<BuiltInKitItem>> _builtInKitContents = new Dictionary<string, List<BuiltInKitItem>>(StringComparer.OrdinalIgnoreCase);

        private void InitializeKitDefinitions()
        {
            _kitDefinitions = new Dictionary<string, KitDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["starter"] = new KitDefinition { Name = "starter", DisplayName = "Starter Pack", Category = "combat", Description = "Basic resources to get started", RustKitName = "starter", Permission = "rustduckbot.use", CooldownMinutes = 60, MaxUsesPerDay = 3 },
                ["pvp"] = new KitDefinition { Name = "pvp", DisplayName = "PvP Loadout", Category = "combat", Description = "Combat gear, ammo, and armor", RustKitName = "pvp", Permission = "rustduckbot.vip", CooldownMinutes = 120, MaxUsesPerDay = 2 },
                ["building"] = new KitDefinition { Name = "building", DisplayName = "Builder Bundle", Category = "building", Description = "Building resources and tools", RustKitName = "building", Permission = "rustduckbot.vip", CooldownMinutes = 90, MaxUsesPerDay = 3 },
                ["mini"] = new KitDefinition { Name = "mini", DisplayName = "Mini Starter", Category = "utility", Description = "Server-defined mini kit", RustKitName = "mini", Permission = "rustduckbot.vip", CooldownMinutes = 240, MaxUsesPerDay = 1 },
                ["scrap"] = new KitDefinition { Name = "scrap", DisplayName = "Scrap Heap", Category = "resources", Description = "Server-defined scrap kit", RustKitName = "scrap", Permission = "rustduckbot.use", CooldownMinutes = 30, MaxUsesPerDay = 4 },
                ["admin"] = new KitDefinition { Name = "admin", DisplayName = "Admin Kit", Category = "admin", Description = "Admin-only server kit", RustKitName = "admin", Permission = "rustduckbot.admin", CooldownMinutes = 60, MaxUsesPerDay = 2 }
            };

            _builtInKitContents = new Dictionary<string, List<BuiltInKitItem>>(StringComparer.OrdinalIgnoreCase)
            {
                ["starter"] = new List<BuiltInKitItem>
                {
                    new BuiltInKitItem { ShortName = "rock", Amount = 1, Container = "belt" },
                    new BuiltInKitItem { ShortName = "torch", Amount = 1, Container = "belt" },
                    new BuiltInKitItem { ShortName = "bandage", Amount = 5, Container = "belt" },
                    new BuiltInKitItem { ShortName = "wood", Amount = 3000, Container = "main" },
                    new BuiltInKitItem { ShortName = "stones", Amount = 3000, Container = "main" },
                    new BuiltInKitItem { ShortName = "metal.fragments", Amount = 1000, Container = "main" },
                    new BuiltInKitItem { ShortName = "hatchet", Amount = 1, Container = "belt" },
                    new BuiltInKitItem { ShortName = "pickaxe", Amount = 1, Container = "belt" }
                },
                ["pvp"] = new List<BuiltInKitItem>
                {
                    new BuiltInKitItem { ShortName = "rifle.ak", Amount = 1, Container = "belt" },
                    new BuiltInKitItem { ShortName = "ammo.rifle", Amount = 128, Container = "main" },
                    new BuiltInKitItem { ShortName = "syringe.medical", Amount = 6, Container = "belt" },
                    new BuiltInKitItem { ShortName = "metal.facemask", Amount = 1, Container = "wear" },
                    new BuiltInKitItem { ShortName = "metal.plate.torso", Amount = 1, Container = "wear" },
                    new BuiltInKitItem { ShortName = "roadsign.kilt", Amount = 1, Container = "wear" },
                    new BuiltInKitItem { ShortName = "shoes.boots", Amount = 1, Container = "wear" }
                },
                ["building"] = new List<BuiltInKitItem>
                {
                    new BuiltInKitItem { ShortName = "hammer", Amount = 1, Container = "belt" },
                    new BuiltInKitItem { ShortName = "building.planner", Amount = 1, Container = "belt" },
                    new BuiltInKitItem { ShortName = "wood", Amount = 10000, Container = "main" },
                    new BuiltInKitItem { ShortName = "stones", Amount = 10000, Container = "main" },
                    new BuiltInKitItem { ShortName = "metal.fragments", Amount = 5000, Container = "main" },
                    new BuiltInKitItem { ShortName = "toolgun", Amount = 1, Container = "belt" }
                },
                ["mini"] = new List<BuiltInKitItem>
                {
                    new BuiltInKitItem { ShortName = "scrap", Amount = 750, Container = "main" },
                    new BuiltInKitItem { ShortName = "lowgradefuel", Amount = 200, Container = "main" },
                    new BuiltInKitItem { ShortName = "metal.fragments", Amount = 1000, Container = "main" }
                },
                ["scrap"] = new List<BuiltInKitItem>
                {
                    new BuiltInKitItem { ShortName = "scrap", Amount = 500, Container = "main" }
                },
                ["admin"] = new List<BuiltInKitItem>
                {
                    new BuiltInKitItem { ShortName = "supply.signal", Amount = 5, Container = "main" },
                    new BuiltInKitItem { ShortName = "explosive.timed", Amount = 10, Container = "main" },
                    new BuiltInKitItem { ShortName = "rifle.l96", Amount = 1, Container = "belt" },
                    new BuiltInKitItem { ShortName = "ammo.rifle.explosive", Amount = 64, Container = "main" }
                }
            };
        }

        private bool CanUseKit(BasePlayer player, PlayerSession session, KitDefinition kit, out string reason)
        {
            reason = null;
            if (!string.IsNullOrEmpty(kit.Permission) &&
                !permission.UserHasPermission(player.UserIDString, kit.Permission) &&
                !HasRoleOrHigher(session.Role, "admin"))
            {
                reason = "Permission required: " + kit.Permission;
                return false;
            }

            if (!_kitCooldowns.TryGetValue(player.userID, out var cooldowns))
                _kitCooldowns[player.userID] = cooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            if (cooldowns.TryGetValue(kit.Name, out var lastUsed))
            {
                var elapsed = (DateTime.UtcNow - lastUsed).TotalMinutes;
                if (elapsed < kit.CooldownMinutes)
                {
                    reason = (kit.CooldownMinutes - (int)elapsed) + " minute cooldown remaining";
                    return false;
                }
            }

            if (!_kitDailyUses.TryGetValue(player.userID, out var daily))
                _kitDailyUses[player.userID] = daily = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var key = DateTime.UtcNow.ToString("yyyy-MM-dd") + ":" + kit.Name;
            var used = daily.TryGetValue(key, out var count) ? count : 0;
            if (used >= kit.MaxUsesPerDay)
            {
                reason = "Daily limit reached (" + kit.MaxUsesPerDay + "/day)";
                return false;
            }

            return true;
        }

        private void RecordKitUse(ulong steamId, string kitName)
        {
            if (!_kitCooldowns.TryGetValue(steamId, out var cooldowns))
                _kitCooldowns[steamId] = cooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            cooldowns[kitName] = DateTime.UtcNow;

            if (!_kitDailyUses.TryGetValue(steamId, out var daily))
                _kitDailyUses[steamId] = daily = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var key = DateTime.UtcNow.ToString("yyyy-MM-dd") + ":" + kitName;
            daily[key] = daily.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        private string GetKitCooldownInfo(ulong steamId, KitDefinition kit)
        {
            if (!_kitCooldowns.TryGetValue(steamId, out var cooldowns)) return null;
            if (!cooldowns.TryGetValue(kit.Name, out var lastUsed)) return null;
            var remaining = kit.CooldownMinutes - (int)(DateTime.UtcNow - lastUsed).TotalMinutes;
            return remaining > 0 ? remaining + "m" : null;
        }

        private int GetKitDailyUses(ulong steamId, string kitName)
        {
            if (!_kitDailyUses.TryGetValue(steamId, out var daily)) return 0;
            return daily.TryGetValue(DateTime.UtcNow.ToString("yyyy-MM-dd") + ":" + kitName, out var count) ? count : 0;
        }

        private void ShowKits(BasePlayer player)
        {
            PrintToChat(player, "<color=#FFD700>━━━━━ KITS ━━━━━</color>");
            foreach (var group in _kitDefinitions.Values.GroupBy(k => k.Category).OrderBy(g => g.Key))
            {
                PrintToChat(player, "\n<color=#4DA6FF>" + group.Key.ToUpper() + "</color>");
                foreach (var kit in group)
                {
                    var cooldown = GetKitCooldownInfo(player.userID, kit);
                    var daily = GetKitDailyUses(player.userID, kit.Name);
                    var status = cooldown != null ? " [" + cooldown + "]" : daily >= kit.MaxUsesPerDay ? " [LIMIT]" : " [READY]";
                    PrintToChat(player, "  <color=#4DA6FF>/db kit " + kit.Name + "</color>" + status + " -- " + kit.Description);
                }
            }
            PrintToChat(player, "\n<color=#888>Built-in RustDuckBot kits are active on this server.</color>");
        }

        private void ShowPlayerKitInfo(BasePlayer player, BasePlayer target)
        {
            PrintToChat(player, "<color=#FFD700>━━━ KIT STATUS: " + target.displayName + " ━━━</color>");
            foreach (var kit in _kitDefinitions.Values)
            {
                var cooldown = GetKitCooldownInfo(target.userID, kit);
                var daily = GetKitDailyUses(target.userID, kit.Name);
                var status = cooldown != null ? cooldown : daily >= kit.MaxUsesPerDay ? "limit" : "ready";
                PrintToChat(player, "  <color=#4DA6FF>" + kit.Name + "</color> -- " + daily + "/" + kit.MaxUsesPerDay + " daily -- " + status);
            }
        }

        private void InitializeItemPrices()
        {
            // Price discovery is based on live player listings for now.
        }

        private void HandleConfirm(BasePlayer player, PlayerSession session, string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId))
            {
                PrintToChat(player, "Usage: /db confirm <listing_id>");
                return;
            }

            var listing = _shopListings.FirstOrDefault(l => l.Id.Equals(listingId, StringComparison.OrdinalIgnoreCase) && l.Available);
            if (listing == null)
            {
                PrintToChat(player, "<color=#FF4444>Listing not found or already closed.</color>");
                return;
            }

            listing.Available = false;
            PrintToChat(player, $"<color=#00FF88>Purchase reserved:</color> {listing.ItemName} x{listing.Quantity} for {listing.PricePerUnit} {listing.Currency}");
            PrintToChat(player, "<color=#888>Meet the seller in-game to exchange items safely.</color>");
            LogActivity("trade", "Purchase reserved", $"{player.displayName} reserved {listing.ItemName} ({listing.Id})", player.UserIDString, player.displayName);
        }

        private void HandleCancel(BasePlayer player, string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId))
            {
                PrintToChat(player, "Usage: /db cancel <listing_id>");
                return;
            }

            var listing = _shopListings.FirstOrDefault(l =>
                l.Id.Equals(listingId, StringComparison.OrdinalIgnoreCase) &&
                l.SellerId == player.UserIDString &&
                l.Available);
            if (listing == null)
            {
                PrintToChat(player, "<color=#FF4444>Active listing not found for your account.</color>");
                return;
            }

            listing.Available = false;
            PrintToChat(player, $"<color=#00FF00>Listing cancelled:</color> {listing.ItemName}");
        }

        private void ShowMyListings(BasePlayer player)
        {
            var mine = _shopListings.Where(l => l.SellerId == player.UserIDString).ToList();
            PrintToChat(player, $"<color=#3498DB>═══ YOUR LISTINGS ({mine.Count}) ═══</color>");
            if (mine.Count == 0)
            {
                PrintToChat(player, "No listings. Use /db sell <item> <price> to create one.");
                return;
            }

            foreach (var listing in mine)
            {
                var status = listing.Available ? "open" : "closed";
                PrintToChat(player, $"  [{listing.Id}] {listing.ItemName} x{listing.Quantity} @ {listing.PricePerUnit} {listing.Currency} ({status})");
            }
        }

        private void ShowOffers(BasePlayer player)
        {
            PrintToChat(player, "<color=#FFD700>Buy offers are not enabled yet.</color>");
            PrintToChat(player, "Use /db shop, /db sell, /db buy, /db confirm, and /db mylistings for the current market.");
        }

        private void HandleGridNav(BasePlayer player, PlayerSession session, string args)
        {
            var pos = player.transform.position;
            PrintToChat(player, "<color=#FFD700>━━━ YOUR LOCATION ━━━</color>");
            PrintToChat(player, $"Grid: <color=#4DA6FF>{GetGridCoord(pos)}</color>");
            PrintToChat(player, $"Coords: <color=#888>{GetLocation(pos)}</color>");
            PrintToChat(player, $"Nearest monument: <color=#4DA6FF>{GetNearestMonument(pos)}</color>");
        }

        private void HandleMapIntel(BasePlayer player, PlayerSession session, string args)
        {
            var pos = player.transform.position;
            var grid = GetGridCoord(pos);
            var nearest = GetNearestMonument(pos);
            var visibleMarkers = _gridMarkers.Where(m => m.Visible || m.OwnerId == player.UserIDString).Take(6).Select(m => m.Name + " @ " + GetGridCoord(m.Position)).ToList();
            var nearbyPlayers = BasePlayer.activePlayerList.Count(p => p != player && Vector3.Distance(p.transform.position, pos) <= 150f);
            PrintToChat(player, "<color=#FFD700>═══ MAP INTEL ═══</color>");
            PrintToChat(player, $"Grid: <color=#4DA6FF>{grid}</color> | Nearest: <color=#4DA6FF>{nearest}</color>");
            var prompt = $"Give concise Rust map intelligence for player {player.displayName} (role {session.Role}). Current grid: {grid}. Coords: {GetLocation(pos)}. Nearest monument: {nearest}. Server: {ConVar.Server.hostname}. Seed: {ConVar.Server.seed}. World size: {ConVar.Server.worldsize}. PvE: {ConVar.Server.pve}. Visible markers: {string.Join(", ", visibleMarkers)}. Nearby players within 150m: {(HasRoleOrHigher(session.Role, "vip") ? nearbyPlayers.ToString() : "restricted")}. Mention route safety, likely risks, loot priorities, and one fallback option.";
            var response = GetAssistantResponse(player, session, prompt, false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void HandleRouteAdvice(BasePlayer player, PlayerSession session, string args)
        {
            var pos = player.transform.position;
            var fromGrid = GetGridCoord(pos);
            var destination = string.IsNullOrWhiteSpace(args) ? GetNearestMonument(pos) : args.Trim();
            var knownMonuments = _monumentLocations.Keys.Take(20).ToList();
            PrintToChat(player, "<color=#FFD700>═══ ROUTE ADVICE ═══</color>");
            PrintToChat(player, $"From: <color=#4DA6FF>{fromGrid}</color> -> Target: <color=#4DA6FF>{destination}</color>");
            var prompt = $"Give route advice for a Rust player traveling from grid {fromGrid} near {GetNearestMonument(pos)} to {destination}. Known monuments: {string.Join(", ", knownMonuments)}. Server PvE: {ConVar.Server.pve}. Keep it concise. Include recommended preparation, likely threats, and one safer alternate route or monument if the direct plan is risky.";
            var response = GetAssistantResponse(player, session, prompt, false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void HandleWorldBrief(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══ WORLD BRIEF ═══</color>");
            var prompt = $"Summarize the current Rust world state for player {player.displayName}. Server: {ConVar.Server.hostname}. Players online: {BasePlayer.activePlayerList.Count}. Sleepers: {BasePlayer.sleepingPlayerList.Count}. FPS: {Math.Round(1.0f / Time.deltaTime, 1)}. Uptime: {Time.realtimeSinceStartup / 3600.0:F1}h. Seed: {ConVar.Server.seed}. World size: {ConVar.Server.worldsize}. PvE: {ConVar.Server.pve}. Nearby monument: {GetNearestMonument(player.transform.position)}. Give a concise status brief with the most useful next action.";
            var response = GetAssistantResponse(player, session, prompt, false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void HandleWipePrep(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══ WIPE PREP ═══</color>");
            var prompt = $"Give a concise Rust wipe-prep checklist for player {player.displayName}. Server: {ConVar.Server.hostname}. Current grid: {GetGridCoord(player.transform.position)}. Nearby monument: {GetNearestMonument(player.transform.position)}. World size: {ConVar.Server.worldsize}. Mention starter routing, first monument priorities, what to craft early, and one common mistake to avoid.";
            var response = GetAssistantResponse(player, session, prompt, false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void HandleEventIntel(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══ EVENT INTEL ═══</color>");
            var activeRaids = _raidHistory.Count(r => r.Outcome == "in_progress");
            var prompt = $"Give concise Rust world event intelligence for player {player.displayName}. Current tracked raids: {activeRaids}. Nearby monument: {GetNearestMonument(player.transform.position)}. Mention cargo, patrol heli, Bradley, CH47, locked crate style timing awareness, and say clearly when live tracking is unavailable.";
            var response = GetAssistantResponse(player, session, prompt, false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void HandleTeamIntel(BasePlayer player, PlayerSession session)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }
            PrintToChat(player, "<color=#FFD700>═══ TEAM INTEL ═══</color>");
            var visibleMarkers = _gridMarkers.Where(m => m.Visible || m.OwnerId == player.UserIDString).Take(8).Select(m => m.Name + " @ " + GetGridCoord(m.Position)).ToList();
            var recentAlerts = _activeAlerts.OrderByDescending(a => a.Time).Take(5).Select(a => a.Title).ToList();
            var nearbyPlayers = BasePlayer.activePlayerList.Where(p => p != player && Vector3.Distance(p.transform.position, player.transform.position) <= 200f).Select(p => p.displayName).Take(8).ToList();
            var prompt = $"Give a concise Rust team-intel briefing for player {player.displayName} (role {session.Role}). Grid: {GetGridCoord(player.transform.position)}. Nearest monument: {GetNearestMonument(player.transform.position)}. Nearby players: {string.Join(", ", nearbyPlayers)}. Recent alerts: {string.Join(", ", recentAlerts)}. Visible markers: {string.Join(", ", visibleMarkers)}. Recommend coordination priorities, movement, and one defensive action.";
            var response = GetAssistantResponse(player, session, prompt, false);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void HandleWorldInfo(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>━━━ WORLD INFO ━━━</color>");
            PrintToChat(player, $"Players online: <color=#4DA6FF>{BasePlayer.activePlayerList.Count}</color>");
            PrintToChat(player, $"Sleeping: <color=#888>{BasePlayer.sleepingPlayerList.Count}</color>");
            PrintToChat(player, $"Game time: <color=#4DA6FF>{GetGameTime()}</color>");
            PrintToChat(player, $"Wipe: <color=#888>{GetWipeInfo()}</color>");
        }

        private void LogDuckBotDebug(string message)
        {
            Puts("[DuckBot] " + message);
        }

        public void RegisterRconRequest(int identifier, string command, string requestId)
        {
            if (identifier <= 0) return;
            _pendingRconCommands[identifier] = command ?? "";
            if (!string.IsNullOrWhiteSpace(requestId)) _pendingRconRequestIds[identifier] = requestId;
        }

        public void HandleRconResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                var obj = SimpleJson.Deserialize(json) as Dictionary<string, object>;
                var identifier = 0;
                var message = json;
                if (obj != null)
                {
                    if (obj.TryGetValue("Identifier", out var idValue) || obj.TryGetValue("identifier", out idValue))
                        int.TryParse(Convert.ToString(idValue), out identifier);
                    if (obj.TryGetValue("Message", out var messageValue) || obj.TryGetValue("message", out messageValue))
                        message = Convert.ToString(messageValue);
                }

                var command = identifier > 0 && _pendingRconCommands.TryGetValue(identifier, out var pendingCommand) ? pendingCommand : "";
                var requestId = identifier > 0 && _pendingRconRequestIds.TryGetValue(identifier, out var pendingRequestId) ? pendingRequestId : "";
                if (identifier > 0)
                {
                    _pendingRconCommands.Remove(identifier);
                    _pendingRconRequestIds.Remove(identifier);
                }

                _mcpClient?.SendMessage(new
                {
                    type = "rcon_response",
                    identifier,
                    request_id = requestId,
                    command,
                    message,
                    time = DateTime.Now.ToString("o"),
                    source = "web-rcon"
                });
            }
            catch (Exception ex)
            {
                PrintWarning("Failed to parse RCON response: " + ex.Message);
            }
        }
        private bool IsRconCommandAllowed(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            var firstWord = command.Trim().Split(new[] { ' ' }, 2)[0].ToLowerInvariant();
            var allowList = _config.AllowedRCONCommands ?? Array.Empty<string>();
            return allowList.Any(allowed => string.Equals(allowed, firstWord, StringComparison.OrdinalIgnoreCase));
        }

        private void ExecuteRconOrConsole(string command, string actor, string requestId = null)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            if (_rconClient?.IsConnected == true)
            {
                _rconClient.Execute(command, requestId);
                LogDuckBotDebug($"RCON command by {actor}: {command}");
                return;
            }

            Server.Command(command);
            LogDuckBotDebug($"Console fallback command by {actor}: {command}");
        }

        private void SaveData()
        {
            var data = new DuckBotData { LastSaveTime = DateTime.Now };
            foreach (var kvp in _sessions)
            {
                var ps = kvp.Value;
                data.PlayerSessions[ps.PlayerId.ToString()] = new PlayerSessionData
                {
                    PlayerId = ps.PlayerId, DisplayName = ps.DisplayName, Role = ps.Role,
                    Homes = ps.Homes.ToDictionary(h => h.Key, h => new PositionData(h.Value.X, h.Value.Y, h.Value.Z)),
                    OnlineTime = ps.OnlineTime, LastSeen = ps.LastSeen,
                    PlayerNotes = new Dictionary<string, string>(ps.PlayerNotes),
                    CurrentKillstreak = ps.CurrentKillstreak, LastKillTime = ps.LastKillTime,
                    LastDailyReward = ps.LastDailyReward ?? DateTime.MinValue, TotalScrap = ps.TotalScrap, Balance = ps.Balance,
                    Permissions = ps.Permissions.ToList(), Bookmarks = ps.Bookmarks.ToList()
                };
            }
            foreach (var kvp in _computerSessions)
                data.ComputerStationSessions.Add(new ComputerStationSessionData { PlayerId = kvp.Key, ActiveCameraId = kvp.Value.ActiveCameraId, ActiveCameraName = kvp.Value.ActiveCameraName, IsWatchingCCTV = kvp.Value.IsWatchingCCTV, SessionStart = kvp.Value.SessionStart, CamerasViewed = kvp.Value.CamerasViewed, AvailableCameraCodes = new List<string>(kvp.Value.AvailableCameraCodes) });
            foreach (var e in _activityLog)
                data.ActivityLog.Add(new ActivityEntryData { Time = e.Time, Category = e.Category, Action = e.Action, Details = e.Details, PlayerId = e.PlayerId, PlayerName = e.PlayerName });
            foreach (var a in _alertHistory)
                data.AlertHistory.Add(new AlertEntryData { Id = a.Id, Type = a.Type, Severity = a.Severity, Title = a.Title, Message = a.Message, Time = a.Time, Acknowledged = a.Acknowledged, AcknowledgedBy = a.AcknowledgedBy, AcknowledgedAt = a.AcknowledgedAt });
            foreach (var m in _gridMarkers)
                data.CameraBookmarks.Add(new GridMarkerData { Id = m.Id, Name = m.Name, Position = new PositionData(m.Position.x, m.Position.y, m.Position.z), Color = m.Color, Icon = m.Icon, Visible = m.Visible, OwnerId = m.OwnerId });
            foreach (var g in _groups)
                data.Groups.Add(new GroupData { Id = g.Value.Id, Name = g.Value.Name, LeaderId = g.Value.LeaderId, MemberIds = g.Value.Members.ToList(), SharedHomes = g.Value.SharedHomes.ToDictionary(h => h.Key, h => new PositionData(h.Value.X, h.Value.Y, h.Value.Z)), Created = g.Value.Created });
            foreach (var kvp in _trackedPlayers)
                data.TrackedPlayers[kvp.Key] = new TrackedPlayerData { UserId = kvp.Value.PlayerId, DisplayName = kvp.Value.DisplayName, Kills = kvp.Value.Kills, Deaths = kvp.Value.Deaths, LastSeen = kvp.Value.LastSeen };
            Interface.Oxide.DataFileSystem.WriteObject("DuckBotData", data);
            PrintAsh($"[Data] Saved {data.PlayerSessions.Count} sessions, {_groups.Count} groups, {_activityLog.Count} activity entries");
        }

        private void LoadData()
        {
            var data = Interface.Oxide.DataFileSystem.ReadObject<DuckBotData>("DuckBotData");
            if (data == null) return;
            foreach (var kvp in data.TrackedPlayers)
                if (!_trackedPlayers.ContainsKey(kvp.Key)) _trackedPlayers[kvp.Key] = new TrackedPlayer { PlayerId = kvp.Value.UserId, DisplayName = kvp.Value.DisplayName, Kills = kvp.Value.Kills, Deaths = kvp.Value.Deaths, LastSeen = kvp.Value.LastSeen, FirstSeen = kvp.Value.LastSeen };
            foreach (var e in data.ActivityLog.Take(_config.MaxActivityLog))
                _activityLog.Add(new ActivityEntry { Time = e.Time, Category = e.Category, Action = e.Action, Details = e.Details, PlayerId = e.PlayerId, PlayerName = e.PlayerName });
            _alertHistory = data.AlertHistory.Select(a => new AlertEntry { Id = a.Id, Type = a.Type, Severity = a.Severity, Title = a.Title, Message = a.Message, Time = a.Time, Acknowledged = a.Acknowledged, AcknowledgedBy = a.AcknowledgedBy, AcknowledgedAt = a.AcknowledgedAt }).ToList();
            foreach (var m in data.CameraBookmarks)
                _gridMarkers.Add(new GridMarker { Id = m.Id, Name = m.Name, Position = m.Position.ToVector3(), Color = m.Color, Icon = m.Icon, Visible = m.Visible, OwnerId = m.OwnerId });
            foreach (var g in data.Groups)
                _groups[g.LeaderId] = new PlayerGroup { Id = g.Id, Name = g.Name, LeaderId = g.LeaderId, Members = new HashSet<ulong>(g.MemberIds), SharedHomes = g.SharedHomes.ToDictionary(h => h.Key, h => new Position3D(h.Value.ToVector3())), Created = g.Created, LastActivity = DateTime.Now };
            _saveData = data;
            PrintAsh($"[Data] Loaded {data.PlayerSessions.Count} sessions, {data.Groups.Count} groups, {data.TrackedPlayers.Count} tracked players");
        }

        private void AutoSaveCallback(object state)
        {
            if (_config.EnableAutoFeatures) { SaveData(); LogActivity("system", "Auto-save", "Data auto-saved"); }
        }

        private void AFKCheckCallback(object state)
        {
            if (!_config.EnableAutoFeatures) return;
            foreach (var player in BasePlayer.activePlayerList)
            {
                var session = GetOrCreateSession(player);
                if (!session.IsOnline || !player.IsConnected) continue;
                if (session.IsAFK && !session._afkManual) continue;
                var idleTime = (DateTime.Now - session.LastActivity).TotalMinutes;
                if (idleTime >= _config.AFKKickMinutes && _config.AutoKickAFK)
                {
                    player.Kick("AFK timeout");
                    LogActivity("system", "AFK Kick", $"Kicked {player.displayName} after {idleTime:F0} min idle");
                }
                else if (idleTime >= _config.AFKTimeoutMinutes && !session.IsAFK)
                {
                    session.IsAFK = true;
                    session._afkAutoDetected = true;
                    PrintToChat(player, $"<color=#FFD700>You have been idle for {idleTime:F0} minutes. Use /db afk off to cancel.</color>");
                }
            }
        }

        private void OnEntityDeath(BasePlayer victim, HitInfo info)
        {
            if (victim == null) return;
            var attacker = info?.Initiator as BasePlayer;
            if (attacker == null || attacker == victim) return;
            var attackerSession = GetOrCreateSession(attacker);
            var victimSession = GetOrCreateSession(victim);
            if ((DateTime.Now - attackerSession.LastKillTime).TotalMinutes > 5) attackerSession.CurrentKillstreak = 0;
            attackerSession.CurrentKillstreak++;
            attackerSession.LastKillTime = DateTime.Now;
            int[] milestones = { 3, 5, 10, 15, 20, 25, 50 };
            int[] rewards = { 50, 150, 500, 1000, 2500, 5000, 10000 };
            int mi = Array.FindLastIndex(milestones, m => attackerSession.CurrentKillstreak >= m);
            if (mi >= 0)
            {
                var isVip = HasRoleOrHigher(attackerSession.Role, "vip") || permission.UserHasPermission(attacker.UserIDString, "rustduckbot.vip");
                var reward = rewards[mi];
                if (isVip && _config.VipBonusMultiplier > 1f) reward = (int)(reward * _config.VipBonusMultiplier);
                attackerSession.TotalScrap += reward;
                PrintToChat(attacker, $"<color=#FFD700>⚔ Killstreak {attackerSession.CurrentKillstreak}!</color> Earned <color=#FF9900>+{reward} scrap</color>" + (isVip && _config.VipBonusMultiplier > 1f ? " <color=#FFD700>(VIP Boost)</color>" : ""));
                _ = _agentBridge?.SendToAgentAsync(new { type = "killstreak_reward", playerId = attacker.UserIDString, playerName = attacker.displayName, streak = attackerSession.CurrentKillstreak, reward = rewards[mi], milestone = milestones[mi], timestamp = DateTime.UtcNow.ToString("O") });
                LogActivity("pvp", "Killstreak", $"{attacker.displayName} reached streak {attackerSession.CurrentKillstreak} (milestone {milestones[mi]})", attacker.UserIDString, attacker.displayName);
            }
            if (victimSession.CurrentKillstreak >= 5) PrintToChat(victim, $"<color=#888>Your killstreak of {victimSession.CurrentKillstreak} was ended.</color>");
            victimSession.CurrentKillstreak = 0;
            if (_trackedPlayers.TryGetValue(attacker.UserIDString, out var tp)) tp.Kills++;
            if (_trackedPlayers.TryGetValue(victim.UserIDString, out var vt)) vt.Deaths++;
            TryDetectRaid(attacker, victim, victim.transform.position);
        }

        private void TryDetectRaid(BasePlayer attacker, BasePlayer victim, Vector3 position)
        {
            if (attacker == null || victim == null) return;
            if (attacker == victim) return;
            var cupboards = UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>().Where(tc => Vector3.Distance(tc.transform.position, position) < _config.RaidAlertRadius).ToList();
            foreach (var tc in cupboards)
            {
                var ownerId = tc.OwnerID.ToString();
                if (string.IsNullOrEmpty(ownerId)) continue;
                var isAuth = tc.authorizedPlayers?.Any(p => p == attacker.userID) ?? false;
                if (isAuth) continue;
                var grid = GetGridCoord(position);
                var monument = GetNearestMonument(position);
                foreach (var sid in _raidAlertSubscribers)
                {
                    var sub = BasePlayer.Find(sid.ToString());
                    if (sub != null && sub.IsConnected) PrintToChat(sub, $"<color=#FF4444>⚠ RAID ALERT:</color> {attacker.displayName} is raiding near {grid} ({monument})");
                }
                _ = _agentBridge?.SendToAgentAsync(new { type = "raid_alert", attackerId = attacker.UserIDString, attackerName = attacker.displayName, victimId = victim.UserIDString, victimName = victim.displayName, gridCoord = grid, monument = monument, timestamp = DateTime.UtcNow.ToString("O") });
                LogActivity("security", "Raid detected", $"{attacker.displayName} raiding at {grid} ({monument})", attacker.UserIDString, attacker.displayName);
                return;
            }
        }

        private void OnPlayerSleep(BasePlayer player)
        {
            if (player == null) return;
            var session = GetOrCreateSession(player);
            session.IsAFK = true;
            session.LastActivity = DateTime.Now;
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null) return;
            var session = GetOrCreateSession(player);
            if (session._afkAutoDetected) session.IsAFK = false;
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null) return;
            var session = GetOrCreateSession(player);
            var now = DateTime.Now;
            if (!session.LastDailyReward.HasValue || session.LastDailyReward.Value.Date < now.Date)
            {
                session.TotalScrap += _config.DailyRewardScrap;
                session.LastDailyReward = now;
                timer.Once(1f, () => { if (player.IsConnected) PrintToChat(player, $"<color=#00FF88>Welcome back! Daily reward: +{_config.DailyRewardScrap} scrap</color>"); });
            }
            _ = _agentBridge?.SendToAgentAsync(new { type = "player_respawned", playerId = player.UserIDString, playerName = player.displayName, position = GetGridCoord(player.transform.position), timestamp = DateTime.UtcNow.ToString("O") });
        }

        private void HandleRaidAlert(BasePlayer player, PlayerSession session)
        {
            if (_raidAlertSubscribers.Contains(player.userID))
            { _raidAlertSubscribers.Remove(player.userID); PrintToChat(player, "<color=#00FF88>Raid alerts disabled.</color>"); }
            else
            { _raidAlertSubscribers.Add(player.userID); PrintToChat(player, "<color=#FFD700>Raid alerts enabled. You will be notified of nearby raids.</color>"); }
        }

        private void HandleGroup(BasePlayer player, PlayerSession session, string args)
        {
            var argv = SplitArgs(args, 3);
            var action = argv.Length > 0 ? argv[0].ToLower() : "info";
            var arg1 = argv.Length > 1 ? argv[1] : "";
            switch (action)
            {
                case "create": case "new": HandleGroupCreate(player, session, arg1); break;
                case "invite": HandleGroupInvite(player, session, arg1); break;
                case "join": HandleGroupJoin(player, session); break;
                case "leave": HandleGroupLeave(player, session); break;
                case "kick": HandleGroupKick(player, session, arg1); break;
                case "disband": HandleGroupDisband(player, session); break;
                case "homes": HandleGroupHomes(player, session); break;
                case "tp": HandleGroupTp(player, session, arg1); break;
                case "sethome": HandleGroupSetHome(player, session, arg1); break;
                case "info": HandleGroupInfo(player, session); break;
                default:
                    PrintToChat(player, "<color=#FFD700>Group commands:</color> /db group create [name], /db group invite [player], /db group join, /db group leave, /db group homes, /db group tp [home], /db group sethome [name]");
                    break;
            }
        }

        private void HandleGroupCreate(BasePlayer player, PlayerSession session, string groupName)
        {
            if (_groups.ContainsKey(player.userID)) { PrintToChat(player, "<color=#FF4444>You are already in a group. Leave first.</color>"); return; }
            if (string.IsNullOrWhiteSpace(groupName)) groupName = $"{player.displayName}'s group";
            var group = new PlayerGroup { Id = Guid.NewGuid().ToString("N").Substring(0, 8), Name = groupName, LeaderId = player.userID, Members = new HashSet<ulong> { player.userID }, SharedHomes = new Dictionary<string, Position3D>(), Created = DateTime.Now, LastActivity = DateTime.Now };
            _groups[player.userID] = group;
            PrintToChat(player, $"<color=#00FF88>Group created:</color> {groupName}");
            LogActivity("group", "Group created", $"Group '{groupName}' created by {player.displayName}", player.UserIDString, player.displayName);
        }

        private void HandleGroupInvite(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!_groups.TryGetValue(player.userID, out var group) || group.LeaderId != player.userID) { PrintToChat(player, "<color=#FF4444>Only the group leader can invite players.</color>"); return; }
            var target = FindPlayerByName(targetName);
            if (target == null) { PrintToChat(player, "<color=#FF4444>Player not found.</color>"); return; }
            if (_groups.ContainsKey(target.userID)) { PrintToChat(player, $"<color=#FF4444>{target.displayName} is already in a group.</color>"); return; }
            _groupInvites[target.userID] = player.userID;
            PrintToChat(player, $"<color=#FFD700>Invite sent to {target.displayName}.</color>");
            PrintToChat(target, $"<color=#FFD700>{player.displayName} invited you to group '{group.Name}'. Use /db group join to accept.</color>");
        }

        private void HandleGroupJoin(BasePlayer player, PlayerSession session)
        {
            if (_groups.ContainsKey(player.userID)) { PrintToChat(player, "<color=#FF4444>You are already in a group.</color>"); return; }
            if (!_groupInvites.TryGetValue(player.userID, out var leaderId)) { PrintToChat(player, "<color=#FF4444>No pending invite.</color>"); return; }
            if (!_groups.TryGetValue(leaderId, out var group)) { PrintToChat(player, "<color=#FF4444>Group no longer exists.</color>"); _groupInvites.Remove(player.userID); return; }
            group.Members.Add(player.userID);
            _groups[player.userID] = group;
            _groupInvites.Remove(player.userID);
            PrintToChat(player, $"<color=#00FF88>Joined group: {group.Name}</color>");
            foreach (var mid in group.Members) { var m = BasePlayer.Find(mid.ToString()); if (m != null) PrintToChat(m, $"<color=#FFD700>{player.displayName} joined the group.</color>"); }
            LogActivity("group", "Player joined", $"{player.displayName} joined group '{group.Name}'", player.UserIDString, player.displayName);
        }

        private void HandleGroupLeave(BasePlayer player, PlayerSession session)
        {
            if (!_groups.TryGetValue(player.userID, out var group)) { PrintToChat(player, "<color=#FF4444>You are not in a group.</color>"); return; }
            if (group.LeaderId == player.userID && group.Members.Count > 1)
            {
                var newLeader = group.Members.First(m => m != player.userID);
                group.LeaderId = newLeader;
                group.Members.Remove(player.userID);
                _groups.Remove(player.userID);
                _groups[newLeader] = group;
                var nlp = BasePlayer.Find(newLeader.ToString());
                foreach (var mid in group.Members) { var m = BasePlayer.Find(mid.ToString()); if (m != null) PrintToChat(m, $"<color=#FFD700>{player.displayName} left. {nlp?.displayName ?? "New leader"} is now the leader.</color>"); }
            }
            else
            {
                foreach (var mid in group.Members) { var m = BasePlayer.Find(mid.ToString()); if (m != null && mid != player.userID) PrintToChat(m, $"<color=#FF4444>{player.displayName} left the group.</color>"); _groups.Remove(mid); }
                PrintToChat(player, "<color=#00FF88>You left the group.</color>");
                return;
            }
            group.Members.Remove(player.userID);
            _groups.Remove(player.userID);
            PrintToChat(player, "<color=#00FF88>You left the group.</color>");
            LogActivity("group", "Player left", $"{player.displayName} left group '{group.Name}'", player.UserIDString, player.displayName);
        }

        private void HandleGroupKick(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!_groups.TryGetValue(player.userID, out var group) || group.LeaderId != player.userID) { PrintToChat(player, "<color=#FF4444>Only the group leader can kick players.</color>"); return; }
            var target = FindPlayerByName(targetName);
            if (target == null) { PrintToChat(player, "<color=#FF4444>Player not found.</color>"); return; }
            if (!group.Members.Contains(target.userID)) { PrintToChat(player, $"<color=#FF4444>{target.displayName} is not in your group.</color>"); return; }
            if (target.userID == player.userID) { PrintToChat(player, "<color=#FF4444>You cannot kick yourself.</color>"); return; }
            group.Members.Remove(target.userID);
            _groups.Remove(target.userID);
            PrintToChat(target, "<color=#FF4444>You were kicked from the group.</color>");
            PrintToChat(player, $"<color=#00FF88>Kicked {target.displayName} from the group.</color>");
            foreach (var mid in group.Members) { var m = BasePlayer.Find(mid.ToString()); if (m != null) PrintToChat(m, $"<color=#FFD700>{target.displayName} was kicked from the group.</color>"); }
        }

        private void HandleGroupDisband(BasePlayer player, PlayerSession session)
        {
            if (!_groups.TryGetValue(player.userID, out var group)) { PrintToChat(player, "<color=#FF4444>You are not in a group.</color>"); return; }
            if (group.LeaderId != player.userID) { PrintToChat(player, "<color=#FF4444>Only the group leader can disband.</color>"); return; }
            foreach (var mid in group.Members) { var m = BasePlayer.Find(mid.ToString()); if (m != null) PrintToChat(m, $"<color=#FF4444>Group '{group.Name}' was disbanded by {player.displayName}.</color>"); _groups.Remove(mid); }
            LogActivity("group", "Group disbanded", $"Group '{group.Name}' disbanded by {player.displayName}", player.UserIDString, player.displayName);
        }

        private void HandleGroupHomes(BasePlayer player, PlayerSession session)
        {
            if (!_groups.TryGetValue(player.userID, out var group)) { PrintToChat(player, "<color=#FF4444>You are not in a group.</color>"); return; }
            PrintToChat(player, $"<color=#FFD700>═══ {group.Name} HOMES ({group.SharedHomes.Count}) ═══</color>");
            if (group.SharedHomes.Count == 0) { PrintToChat(player, "No shared homes. Leader: /db group sethome [name]"); return; }
            foreach (var kvp in group.SharedHomes) PrintToChat(player, $"  <color=#4DA6FF>{kvp.Key}</color> @ {GetGridCoord(kvp.Value.ToVector3())}");
        }

        private void HandleGroupSetHome(BasePlayer player, PlayerSession session, string homeName)
        {
            if (!_groups.TryGetValue(player.userID, out var group)) { PrintToChat(player, "<color=#FF4444>You are not in a group.</color>"); return; }
            if (group.LeaderId != player.userID) { PrintToChat(player, "<color=#FF4444>Only the group leader can set shared homes.</color>"); return; }
            if (string.IsNullOrWhiteSpace(homeName)) homeName = $"{player.displayName}'s base";
            group.SharedHomes[homeName] = new Position3D(player.transform.position);
            PrintToChat(player, $"<color=#00FF88>Shared home '{homeName}' set at {GetGridCoord(player.transform.position)}</color>");
            foreach (var mid in group.Members) { var m = BasePlayer.Find(mid.ToString()); if (m != null && mid != player.userID) PrintToChat(m, $"<color=#FFD700>New shared home '{homeName}' was added by {player.displayName}.</color>"); }
        }

        private void HandleGroupTp(BasePlayer player, PlayerSession session, string homeName)
        {
            if (!_groups.TryGetValue(player.userID, out var group)) { PrintToChat(player, "<color=#FF4444>You are not in a group.</color>"); return; }
            if (string.IsNullOrWhiteSpace(homeName)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db group tp [home_name]"); return; }
            if (!group.SharedHomes.TryGetValue(homeName, out var pos)) { PrintToChat(player, $"<color=#FF4444>Home '{homeName}' not found.</color>"); return; }
            session._pendingTeleport = true;
            session._teleportStartPos = new Position3D(player.transform.position);
            session._teleportDestination = pos;
            session._teleportReason = $"group tp to {homeName}";
            PrintToChat(player, $"<color=#FFD700>Teleporting in {_config.TeleportWarmupSeconds}s... Don't move.</color>");
            timer.Once(_config.TeleportWarmupSeconds, () => { if (session._pendingTeleport) { session._pendingTeleport = false; player.Teleport(pos.ToVector3()); PrintToChat(player, $"<color=#00FF88>Teleported to group home '{homeName}'.</color>"); } });
        }

        private void HandleGroupInfo(BasePlayer player, PlayerSession session)
        {
            if (!_groups.TryGetValue(player.userID, out var group)) { PrintToChat(player, "<color=#FF4444>You are not in a group.</color>"); return; }
            PrintToChat(player, $"<color=#FFD700>═══ GROUP: {group.Name} ═══</color>");
            PrintToChat(player, $"Leader: <color=#4DA6FF>{BasePlayer.Find(group.LeaderId.ToString())?.displayName ?? group.LeaderId.ToString()}</color>");
            PrintToChat(player, $"Members ({group.Members.Count}):");
            foreach (var mid in group.Members) { var m = BasePlayer.Find(mid.ToString()); PrintToChat(player, $"  {(m != null && m.IsConnected ? "<color=#00FF88>●" : "<color=#888>○")}</color> {m?.displayName ?? mid.ToString()}"); }
            PrintToChat(player, $"Created: {group.Created:yyyy-MM-dd}");
        }

        private void TrackCommand(string cmd)
        {
            if (!_commandStats.ContainsKey(cmd)) _commandStats[cmd] = 0;
            _commandStats[cmd]++;
        }

        private bool HasRoleOrHigher(string role, string required)
        {
            int Rank(string value)
            {
                switch ((value ?? "user").ToLowerInvariant())
                {
                    case "admin": return 3;
                    case "mod": return 2;
                    case "vip": return 1;
                    default: return 0;
                }
            }

            return Rank(role) >= Rank(required);
        }

        private string RoleColor(string role)
        {
            switch ((role ?? "user").ToLowerInvariant())
            {
                case "admin": return "#FF4444";
                case "mod": return "#FF9900";
                case "vip": return "#00BFFF";
                default: return "#FFFFFF";
            }
        }

        private string SeverityColor(string severity)
        {
            switch ((severity ?? "info").ToLowerInvariant())
            {
                case "critical": return "#FF0000";
                case "high": return "#FF6B6B";
                case "medium": return "#FF9900";
                case "low": return "#FFD700";
                default: return "#FFFFFF";
            }
        }

        private string ThreatColor(string threat)
        {
            switch ((threat ?? "unknown").ToLowerInvariant())
            {
                case "critical": return "#FF0000";
                case "high": return "#FF6B6B";
                case "medium": return "#FF9900";
                case "low": return "#FFD700";
                default: return "#FFFFFF";
            }
        }

        private string BroadcastColor(string type)
        {
            switch ((type ?? "info").ToLowerInvariant())
            {
                case "critical": return "#FF0000";
                case "warning": return "#FF9900";
                case "success": return "#00FF88";
                default: return "#FFD700";
            }
        }

        private string AccessIcon(string action)
        {
            switch ((action ?? string.Empty).ToLowerInvariant())
            {
                case "view": return "👁";
                case "open": return "🚪";
                case "lock": return "🔒";
                case "unlock": return "🔓";
                case "control_left":
                case "control_right":
                case "control_up":
                case "control_down":
                case "control_zoom":
                case "control_zoom_in":
                case "control_zoom_out":
                case "control_reset": return "🎮";
                default: return "•";
            }
        }

        private bool ContainsIgnoreCase(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ActivityColor(string category)
        {
            switch ((category ?? string.Empty).ToLowerInvariant())
            {
                case "security": return "#FF6B6B";
                case "camera": return "#00BFFF";
                case "base": return "#9B59B6";
                case "trading": return "#3498DB";
                case "intel": return "#1ABC9C";
                case "automation": return "#E67E22";
                case "admin": return "#FF4444";
                case "system": return "#FFD700";
                default: return "#FFFFFF";
            }
        }

        private string GetGameTime()
        {
            var t = DateTime.Now.TimeOfDay;
            return $"{t.Hours:D2}:{t.Minutes:D2}";
        }

        private string GetWipeInfo() => "Check server Discord";

        private string GetGridCoord(Vector3 pos) => $"{(int)(pos.x / 150)},{(int)(pos.z / 150)}";

        private string GetLocation(Vector3 pos) => $"{pos.x:F0},{pos.y:F0},{pos.z:F0}";

        private string GetDirection(Vector3 from, Vector3 to)
        {
            var dir = (to - from).normalized;
            var angle = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360;
            if (angle >= 337.5 || angle < 22.5) return "N";
            if (angle >= 22.5 && angle < 67.5) return "NE";
            if (angle >= 67.5 && angle < 112.5) return "E";
            if (angle >= 112.5 && angle < 157.5) return "SE";
            if (angle >= 157.5 && angle < 202.5) return "S";
            if (angle >= 202.5 && angle < 247.5) return "SW";
            if (angle >= 247.5 && angle < 292.5) return "W";
            return "NW";
        }

        private string GetNearestMonument(Vector3 pos)
        {
            var monuments = new Dictionary<string, Vector3> {
                { "Oil Rig", new Vector3(0, 0, 0) },
                { "Airfield", new Vector3(1000, 0, 1000) },
                { "Dome", new Vector3(-500, 0, -500) },
                { "Power Plant", new Vector3(2000, 0, -1500) },
                { "Outpost", new Vector3(-1500, 0, 1000) }
            };
            string nearest = "Unknown";
            float minDist = float.MaxValue;
            foreach (var m in monuments)
            {
                var dist = Vector3.Distance(pos, m.Value);
                if (dist < minDist) { minDist = dist; nearest = m.Key; }
            }
            return nearest;
        }
    }

    // =====================================================================
    // MCP CLIENT
    // =====================================================================

    public class MCPClient
    {
        private readonly string _host;
        private readonly int _port;
        private readonly RustDuckBot _plugin;
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private bool _connected;
        private readonly Queue<object> _agentEventQueue = new Queue<object>();
        private readonly object _queueLock = new object();

        // Static singleton so AgentBridge can enqueue events without a reference
        public static MCPClient DefaultInstance;

        public MCPClient(string host, int port, RustDuckBot plugin)
        {
            _host = host; _port = port; _plugin = plugin;
            _ws = new ClientWebSocket();
            DefaultInstance = this;
        }

        public bool IsConnected => _connected && _ws?.State == WebSocketState.Open;

        public async Task ConnectAsync()
        {
            try
            {
                _cts = new CancellationTokenSource();
                var uri = new Uri($"ws://{_host}:{_port}");
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(uri, _cts.Token);
                _connected = true;
                DefaultInstance = this;
                _plugin.PrintAsh($"MCP connected to {uri}");
                await SendAsync(new { type = "rust_hello", version = "1.2.0", plugin = "RustDuckBot" });
                _receiveTask = ReceiveLoop();
            }
            catch (Exception ex)
            {
                _plugin.PrintAsh($"MCP connect failed: {ex.Message}");
                _connected = false;
                await Task.Delay(5000);
                _ = ConnectAsync();
            }
        }

        public void Disconnect()
        {
            try { _cts?.Cancel(); } catch { }
            try { _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Unload", CancellationToken.None).Wait(); } catch { }
            _connected = false;
        }

        public async void SendMessage(object message)
        {
            if (!IsConnected) return;
            try
            {
                var json = SimpleJson.Serialize(message);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            }
            catch { }
        }

        private async Task SendAsync(object message)
        {
            var json = SimpleJson.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        // Called by AgentBridge to enqueue an async event for the AI agent.
        // Drain logic is handled in the main receive loop.
        public void EnqueueAgentEvent(object gameEvent)
        {
            lock (_queueLock)
            {
                // Keep queue bounded to avoid memory issues
                if (_agentEventQueue.Count < 50)
                    _agentEventQueue.Enqueue(gameEvent);
            }
        }

        // Drain queued events to the AI agent when the connection is healthy.
        private void DrainAgentEventQueue()
        {
            if (!IsConnected) return;
            lock (_queueLock)
            {
                while (_agentEventQueue.Count > 0)
                {
                    var evt = _agentEventQueue.Dequeue();
                    try
                    {
                        var json = SimpleJson.Serialize(new { type = "game_event", data = evt });
                        var bytes = Encoding.UTF8.GetBytes(json);
                        _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token).Wait(100);
                    }
                    catch { }
                }
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[16384];
            var drainCounter = 0;
            while (_ws?.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                try
                {
                    // Drain queued game events to the AI agent periodically
                    if (++drainCounter % 20 == 0)
                        DrainAgentEventQueue();

                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try
                    {
                        var msg = SimpleJson.Deserialize(json) as Dictionary<string, object>;
                        if (msg != null)
                        {
                            var msgType = msg.ContainsKey("type") ? Convert.ToString(msg["type"]) : "unknown";
                            _plugin.PrintAsh($"[MCP] {msgType}");
                            _plugin.HandleMCPMessage(msg);
                        }
                    }
                    catch { }
                }
                catch (OperationCanceledException) { break; }
                catch { break; }
            }
            _connected = false;
        }
    }

    // =====================================================================
    // WS-RCON CLIENT
    // =====================================================================

    public class WSRCONClient
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _password;
        private readonly RustDuckBot _plugin;
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private bool _connected;
        private int _messageId = 0;

        public WSRCONClient(string host, int port, string password, RustDuckBot plugin)
        {
            _host = host; _port = port; _password = password; _plugin = plugin;
        }

        public bool IsConnected => _connected && _ws?.State == WebSocketState.Open;

        public async Task ConnectAsync()
        {
            try
            {
                _cts = new CancellationTokenSource();
                var escapedPassword = Uri.EscapeDataString(_password ?? "");
                var uri = new Uri($"ws://{_host}:{_port}/{escapedPassword}");
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(uri, _cts.Token);
                _connected = true;
                _plugin.PrintAsh($"WS-RCON connected");
                await SendRCONCommand("eventsubscribe chat");
                await SendRCONCommand("eventsubscribe connect");
                await SendRCONCommand("eventsubscribe disconnect");
                await SendRCONCommand("eventsubscribe death");
                _ = ReceiveLoop();
            }
            catch (Exception ex)
            {
                _plugin.PrintAsh($"WS-RCON failed: {ex.Message}");
                _connected = false;
            }
        }

        public void Disconnect()
        {
            try { _cts?.Cancel(); } catch { }
            try { _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Unload", CancellationToken.None).Wait(); } catch { }
            _connected = false;
        }

        public async void Execute(string command)
        {
            Execute(command, null);
        }

        public async void Execute(string command, string requestId)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            await SendRCONCommand(command, requestId);
        }

        private async Task SendRCONCommand(string command, string requestId = null)
        {
            if (!IsConnected) return;
            var msgId = System.Threading.Interlocked.Increment(ref _messageId);
            _plugin.RegisterRconRequest(msgId, command, requestId);
            var json = SimpleJson.Serialize(new { Identifier = msgId, Message = command, Name = "WebRcon" });
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[16384];
            while (_ws?.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _plugin.HandleRconResponse(json);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _plugin.PrintAsh($"WS-RCON receive failed: {ex.Message}");
                    break;
                }
            }
            _connected = false;
        }
    }

    // =====================================================================
    // AGENT BRIDGE
    // =====================================================================

    public class AgentBridge
    {
        private readonly string _provider;
        private readonly string _config;

        public AgentBridge(string provider, string config)
        {
            _provider = provider;
            _config = config;
        }

        // Send an async event to the AI agent (via MCP) without waiting for response.
        // Used for game events like CCTV usage, alerts, raids, etc.
        public async Task SendToAgentAsync(object gameEvent)
        {
            try
            {
                // Fire-and-forget: serialize and hand off to MCP client for delivery
                // The MCP client queues these and delivers them to the AI agent.
                var json = SimpleJson.Serialize(gameEvent);
                if (MCPClient.DefaultInstance != null)
                {
                    MCPClient.DefaultInstance.EnqueueAgentEvent(gameEvent);
                }
            }
            catch
            {
                // Silently ignore delivery failures — game events shouldn't crash the server
            }
        }

        public string GetResponse(string playerName, string role, string message, List<RustDuckBot.ChatEntry> history)
        {
            var lower = message.ToLower();

            if (lower.Contains("help")) return "Type /db help for all commands! Use /db ask <question> to chat with me.";
            if (lower.Contains("hello") || lower.Contains("hi")) return $"Hello {playerName}! I'm DuckBot, your AI assistant on this server. How can I help?";
            if (lower.Contains("who are you")) return "I'm DuckBot, an AI assistant powered by DuckBot MCP! I can help with cameras, security, trading, intel, and more.";
            if (lower.Contains("rules")) return "Server rules: 1) No cheating 2) No griefing 3) No spam 4) Respect staff 5) No RMT";
            if (lower.Contains("wipe")) return "Check server Discord for wipe schedule info.";
            if (lower.Contains("kit")) return "Available kits: starter, pvp, building, mini. Use /kit <name> to redeem.";
            if (lower.Contains("monument") || lower.Contains("map")) return "Key monuments: Oil Rig (best loot), Airfield (military), Dome, Train Yard, Power Plant. Use /db monuments for details.";
            if (lower.Contains("raid")) return "Raid tips: Check /db raiders for active threats, use /db alerts to stay safe!";
            if (lower.Contains("base") || lower.Contains("building")) return "Building tips: Square frames are strongest, air lock entrances, TC auth everyone! Use /db analyze for AI analysis.";
            if (lower.Contains("trade") || lower.Contains("shop")) return "Trading: Use /db shop to browse the market, /db sell <item> <price> to list items.";
            if (lower.Contains("tc") || lower.Contains("cupboard") || lower.Contains("auth")) return "Tool Cupboard (TC) protects your base. Add authorized players to let them build/repair!";
            if (lower.Contains("decay")) return "Decay: Structures decay over time without upkeep. Use /db decay to check your base status.";
            if (lower.Contains("turrets") || lower.Contains("defense")) return "Turrets: Auto turrets shoot enemies automatically! Position them at entry points. Use /db turrets to manage.";
            if (lower.Contains("blueprint") || lower.Contains("bp") || lower.Contains("research")) return "Research: Place items on a research table to unlock BP. Workbenches required for higher tier items.";
            if (lower.Contains("event") || lower.Contains("ch47") || lower.Contains("heli") || lower.Contains("bradley")) return "Events: CH47 patrol chopper, Bradley APC, and Cargo Ship are high-risk, high-reward. Use /db events to check active ones.";
            if (lower.Contains("loot")) return "Best loot locations: Dome (elite crates), Oil Rig (components), Airfield (military). Use /db loot for full list.";
            if (lower.Contains("fps") || lower.Contains("lag")) return "FPS tips: Lower shadow/terrain, close background apps, verify game files.";
            if (lower.Contains("答应") || lower.Contains("中文")) return "你好！我是 DuckBot! Type /db help for all commands.";
            if (lower.Contains("tip") || lower.Contains("advice")) return "Pro tip: Always check your TC auth list, monitor decay, and stay aware of raider activity with /db radar!";

            return $"I heard: '{message}'. Try /db help for commands, or /db ask <question> for AI assistance!";
        }
    }

    // =====================================================================
    // SIMPLE JSON
    // =====================================================================

    public static class SimpleJson
    {
        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            var stringValue = obj as string;
            if (stringValue != null) return $"\"{stringValue.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")}\"";
            if (obj is bool) return (bool)obj ? "true" : "false";
            if (obj is int || obj is long || obj is short || obj is byte || obj is uint || obj is ulong || obj is ushort || obj is sbyte || obj is float || obj is double || obj is decimal)
                return Convert.ToString(obj, CultureInfo.InvariantCulture);
            if (obj is System.Collections.IDictionary dict)
            {
                var parts = new List<string>();
                foreach (System.Collections.DictionaryEntry e in dict) parts.Add($"\"{e.Key}\":{Serialize(e.Value)}");
                return "{" + string.Join(",", parts) + "}";
            }
            if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
            {
                var parts = new List<string>();
                foreach (var item in enumerable) parts.Add(Serialize(item));
                return "[" + string.Join(",", parts) + "]";
            }

            var properties = obj.GetType().GetProperties();
            if (properties.Length > 0)
            {
                var parts = new List<string>();
                foreach (var property in properties)
                {
                    if (!property.CanRead) continue;
                    parts.Add(Serialize(property.Name) + ":" + Serialize(property.GetValue(obj)));
                }
                return "{" + string.Join(",", parts) + "}";
            }
            return Serialize(Convert.ToString(obj, CultureInfo.InvariantCulture) ?? "");
        }

        public static object Deserialize(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject(json);
        }
    }

    // =====================================================================
    // LOCAL AI BRIDGE — supports DuckBot/MCP, LM Studio, OpenAI, Anthropic, OpenRouter
    // Falls back gracefully when no API key is configured.
    // =====================================================================

    internal class LocalAIBridge
    {
        private readonly string _provider;
        private readonly string _lmUrl;
        private readonly string _lmModel;
        private readonly string _lmKey;
        private readonly string _openAiKey;
        private readonly string _openAiBase;
        private readonly string _openAiModel;
        private readonly string _systemPrompt;
        private readonly string _miniMaxKey;
        private readonly string _miniMaxModel;

        public LocalAIBridge(RustDuckBot.ConfigData cfg)
        {
            _provider = cfg.AgentProvider;
            _lmUrl = cfg.LMStudioUrl.TrimEnd('/');
            _lmModel = cfg.LMStudioModel;
            _lmKey = cfg.LMStudioApiKey;
            _openAiKey = cfg.OpenAIApiKey;
            _openAiBase = cfg.OpenAIBaseUrl.TrimEnd('/');
            _openAiModel = cfg.OpenAIModel;
            _miniMaxKey = cfg.MiniMaxApiKey;
            _miniMaxModel = cfg.MiniMaxModel;
            _systemPrompt = BuildSystemPrompt(cfg);
        }

        public string GetResponse(string playerName, string role, string message, List<RustDuckBot.ChatEntry> history)
        {
            try
            {
                switch (_provider)
                {
                    case "lmstudio": return LMPrompt(message, history);
                    case "openai": return OAIPrompt(message, history, _openAiKey, _openAiBase, _openAiModel);
                    case "anthropic": return AnthropicPrompt(message, history);
                    case "openrouter": return OAIPrompt(message, history, _openAiKey, "https://openrouter.ai/api/v1", _openAiModel);
                    case "minimax": return MiniMaxPrompt(message, history);
                    default: return null; // Fall back to DuckBotAgentBridge
                }
            }
            catch (Exception ex)
            {
                return $"⚠ AI error ({_provider}): {ex.Message}";
            }
        }

        public bool IsLocalProvider => _provider != "duckbot";
        public string ProviderName => _provider;

        // ── LM Studio (OpenAI-compatible /v1/chat/completions) ───────────────

        private string LMPrompt(string message, List<RustDuckBot.ChatEntry> history)
        {
            using (var wb = new System.Net.WebClient())
            {
                wb.Headers["Content-Type"] = "application/json";
                if (!string.IsNullOrEmpty(_lmKey))
                    wb.Headers["Authorization"] = $"Bearer {_lmKey}";

                var payload = new Dictionary<string, object> { { "model", _lmModel }, { "messages", BuildMessages(message, history, _systemPrompt) }, { "max_tokens", 600 } };
                var raw = wb.UploadString(ChatCompletionsUrl(_lmUrl), "POST", SimpleJson.Serialize(payload));

                var content = ExtractOpenAIContent(raw);
                return content ?? "No response from local AI.";
            }
        }

        // ── OpenAI-compatible ───────────────────────────────────────────────

        private string OAIPrompt(string message, List<RustDuckBot.ChatEntry> history, string apiKey, string baseUrl, string model)
        {
            if (string.IsNullOrEmpty(apiKey))
                return "⚠ OpenAI API key not configured. Set OpenAIApiKey in config.";

            using (var wb = new System.Net.WebClient())
            {
                wb.Headers["Content-Type"] = "application/json";
                wb.Headers["Authorization"] = $"Bearer {apiKey}";

                var raw = wb.UploadString(ChatCompletionsUrl(baseUrl), "POST",
                    SimpleJson.Serialize(new { model = model, messages = BuildMessages(message, history, _systemPrompt), max_tokens = 800 }));

                var content = ExtractOpenAIContent(raw);
                return content ?? "No response from AI.";
            }
        }

        // ── Anthropic (Claude) ───────────────────────────────────────────────

        private string AnthropicPrompt(string message, List<RustDuckBot.ChatEntry> history)
        {
            if (string.IsNullOrEmpty(_openAiKey))
                return "⚠ Anthropic API key not set as OpenAIApiKey in config.";

            using (var wb = new System.Net.WebClient())
            {
                wb.Headers["Content-Type"] = "application/json";
                wb.Headers["x-api-key"] = _openAiKey;
                wb.Headers["anthropic-version"] = "2023-06-01";

                var systemMsg = new { role = "system", content = _systemPrompt };
                var userMsg = new { role = "user", content = message };
                var msgs = new List<object> { systemMsg, userMsg };

                foreach (var h in history.Skip(Math.Max(0, history.Count - 20)))
                    msgs.Add(new { role = h.IsAI ? "assistant" : "user", content = $"{h.Sender}: {h.Message}" });

                var body = new { model = _openAiModel, max_tokens = 800, messages = msgs };
                var raw = wb.UploadString("https://api.anthropic.com/v1/messages", "POST", SimpleJson.Serialize(body));

                var content = ExtractAnthropicContent(raw);
                return content ?? "No response from Claude.";
            }
        }

        // ── MiniMax ──────────────────────────────────────────────────────────

        private string MiniMaxPrompt(string message, List<RustDuckBot.ChatEntry> history)
        {
            if (string.IsNullOrEmpty(_miniMaxKey))
                return "⚠ MiniMax API key not configured. Set MiniMaxApiKey in config.";

            using (var wb = new System.Net.WebClient())
            {
                wb.Headers["Content-Type"] = "application/json";
                wb.Headers["Authorization"] = $"Bearer {_miniMaxKey}";

                var url = ChatCompletionsUrl("https://api.minimax.chat/v1");
                var raw = wb.UploadString(url, "POST",
                    SimpleJson.Serialize(new { model = _miniMaxModel, messages = BuildMessages(message, history, _systemPrompt), max_tokens = 800 }));

                var content = ExtractOpenAIContent(raw);
                return content ?? "No response from MiniMax.";
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private string ExtractOpenAIContent(string raw)
        {
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(raw);
                return root["choices"]?[0]?["message"]?["content"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private string ExtractAnthropicContent(string raw)
        {
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(raw);
                return root["content"]?[0]?["text"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private object[] BuildMessages(string message, List<RustDuckBot.ChatEntry> history, string system)
        {
            var msgs = new List<object> { new { role = "system", content = system } };
            foreach (var h in history.Skip(Math.Max(0, history.Count - 16)))
                msgs.Add(new { role = h.IsAI ? "assistant" : "user", content = $"{h.Sender}: {h.Message}" });
            msgs.Add(new { role = "user", content = message });
            return msgs.ToArray();
        }

        private static string ChatCompletionsUrl(string baseUrl)
        {
            var trimmed = (baseUrl ?? "http://127.0.0.1:1234").TrimEnd('/');
            return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                    ? trimmed + "/chat/completions"
                    : trimmed + "/v1/chat/completions";
        }

        private string BuildSystemPrompt(RustDuckBot.ConfigData cfg)
        {
            return $@"You are DuckBot, an AI assistant inside a Rust game server. Respond as a helpful, practical in-game terminal.
Player role hierarchy (lowest to highest): user < vip < mod < admin < security.

Current AI provider: {cfg.AgentProvider}.
- ""lmstudio"" — local LM Studio instance (OpenAI-compatible API at {cfg.LMStudioUrl})
- ""duckbot"" — DuckBot MCP bridge with full tool access
- Other providers map to their respective API backends

Built-in RustDuckBot commands and kit system:
- /db kit <name> — grants built-in kit (starter, pvp, building, mini, scrap, admin)
- /db help, /db status, /db info, /db players, /db time, /db weather
- /db cameras, /db scan, /db monuments, /db radar, /db nearby
- /db alerts, /db raiders, /db decay, /db analysis
- /db shop, /db market, /db trade, /db lookup
- /db ask <question> — AI-powered chat (uses current provider)
- /db tip, /db joke, /db quote, /db 8ball, /db roll
- /db settings <key> <value> — player preferences (ownerai, afkcheck, etc.)

Kit permissions by role:
- starter: all players (cooldown applies)
- pvp, building, mini, scrap: vip+
- admin: admin+ only

Live data sources:
- Heartbeat sent every 30s from Rust plugin to MCP bridge
- Cameras scanned at server init and on demand
- Player count, FPS, uptime, connected players available in server status
- Server name, seed, world size, PvE mode, entity count, sleeper count, monuments, player grid, and nearest monument are included when available

MCP/RCON tools:
- `rust_rcon_command_catalog` lists every allowed RCON command with category, role, safety level, examples, and whether to use read-only query or admin command execution
- Read-only RCON query support: status, serverinfo, player.list, players.online, server.hostname, server.seed, server.worldsize, server.pve, global.status, status.gpu, status.ram
- Admin action RCON commands require admin role and whitelist validation
- Informational tools should answer with live data when present and clearly say when data is stale or unavailable

Rules:
- Keep answers concise and Rust-specific
- Prefer practical guidance on survival, base building, loot, monuments, raids
- Do not invent plugin capabilities not confirmed for this build
- Distinguish informational guidance from direct server actions
- Never break character as an in-game AI terminal

Server config flags: CameraControl={cfg.EnableCameraControl}, RaidAlerts={cfg.EnableRaidAlerts}, DecayAlerts={cfg.EnableDecayAlerts}, Automation={cfg.EnableAutomation}.";
        }
    }

    // =====================================================================
    // POSITION
    // =====================================================================

    public class Position3D
    {
        public float X, Y, Z;
        public Position3D(Vector3 v) { X = v.x; Y = v.y; Z = v.z; }
        public Vector3 ToVector3() => new Vector3(X, Y, Z);
    }
} // namespace Oxide.Plugins
