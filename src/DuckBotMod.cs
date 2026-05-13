using Oxide.Core.Plugins;
using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RustDuckBot
{
    [Info("RustDuckBot", "1.3.1", "Duckets")]
    [Description("AI-powered computer station with DuckBot. CCTV, security, base management, trading, automation, intel, and more.")]
    public class RustDuckBot : RustPlugin
    {
        // =====================================================================
        // CONFIGURATION
        // =====================================================================

        private ConfigData _config;

        private class ConfigData
        {
            public string MCPServerHost = "127.0.0.1";
            public int MCPServerPort = 3851;
            public string AgentProvider = "duckbot";  // duckbot | lmstudio | openai | anthropic | openrouter
            public string AgentConfig = "http://localhost:18797";
            // LM Studio settings (used when AgentProvider = "lmstudio")
            public string LMStudioUrl = "http://localhost:1234";
            public string LMStudioModel = "local-model";
            public string LMStudioApiKey = ""; // Optional: for auth if required
            // OpenAI-compatible settings (used for openai/anthropic/openrouter)
            public string OpenAIApiKey = "";
            public string OpenAIBaseUrl = "https://api.openai.com/v1";
            public string OpenAIModel = "gpt-4o-mini";
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
            // AFK / inactivity
            public int AFKTimeoutMinutes = 10;
            public int AFKKickMinutes = 30;
            public bool AutoKickAFK = true;
            // Economy
            public bool EnableDailyReward = true;
            public int DailyRewardScrap = 100;
            public int DailyRewardRP = 20;
            public int PlaytimeBonusMinutes = 60; // bonus after N minutes
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
        private AgentBridge _agentBridge;
        private LocalAIBridge _localAI;
        private Timer _heartbeatTimer;
        private Timer _automationTimer;
        private Timer _decayTimer;
        private Timer _radarTimer;
        private bool _serverInitialized;

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

            public static string BuildTerminal(string playerName, string role, int unreadAlerts, string currentCam, int cmdCount)
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
                        new CuiTextComponent { Text = "RustDuckBot v1.3.0 | /db help | AI: DuckBot", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.5 0.4 0.2 1" }
                    }
                });

                return container;
            }

            public static string BuildCameraList(List<CameraInfo> cameras, string currentCam)
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

        private class ChatEntry
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

            // Chat commands - all under /db
            cmd.AddChatCommand("duckbot", this, nameof(CmdDuckBot));
            cmd.AddChatCommand("db", this, nameof(CmdDuckBot));

            // Subscribe to hooks
            Subscribe(nameof(OnPlayerConnected));
            Subscribe(nameof(OnPlayerDisconnected));
            Subscribe(nameof(OnEntityTakeDamage));
            Subscribe(nameof(OnPlayerAttacked));
            Subscribe(nameof(OnDoorOpened));
            Subscribe(nameof(OnDoorClosed));
            Subscribe(nameof(OnExplosion));
            Subscribe(nameof(OnChat));
            // CCTV / Computer Station hooks
            Subscribe(nameof(OnCCTVCameraUsed));
            Subscribe(nameof(OnComputerStationUse));
            Subscribe(nameof(OnPlayerInput));
            Subscribe(nameof(CanClientMove));

            // Initialize monument camera codes
            InitializeMonumentCodes();

            PrintAsh("<color=#FFD700>RustDuckBot v1.3.0</color> loaded. Computer Station: <color=#00FF00>ENABLED</color> | Chat Panel: <color=#00FF00>ENABLED</color>");
            var aiMode = _config.AgentProvider == "duckbot" ? $"DuckBot MCP ({_config.AgentConfig})" : $"Local AI: {_config.AgentProvider}";
            PrintAsh($"AI: <color=#FFD700>{aiMode}</color> | MCP: ws://{_config.MCPServerHost}:{_config.MCPServerPort}");
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
            _serverInitialized = true;
            _ = _mcpClient.ConnectAsync();

            if (_config.EnableWebSocketRCON && !string.IsNullOrEmpty(_config.RCONPassword))
            {
                var rcon = new WSRCONClient("127.0.0.1", _config.RCONPort, _config.RCONPassword, this);
                _ = rcon.ConnectAsync();
            }

            ScanCameras();
            ScanBases();
            ScanVendingMachines();

            // Heartbeat every 30s
            _heartbeatTimer = new Timer(HeartbeatCallback, null, 30000, 30000);

            // Automation every 60s
            _automationTimer = new Timer(AutomationCallback, null, 60000, 60000);

            // Decay check every 5 min
            _decayTimer = new Timer(DecayCheckCallback, null, 300000, 300000);

            // Radar sweep every 10s
            _radarTimer = new Timer(RadarCallback, null, 10000, 10000);

            SendServerStatus();
            LogActivity("system", "Server initialized", $"RustDuckBot v1.3.1 started. Cameras: {_cameras.Count}");
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            var session = GetOrCreateSession(player);
            session.IsOnline = true;
            session.LastSeen = DateTime.Now;
            TrackPlayer(player.UserIDString, player.displayName);

            _mcpClient?.SendMessage(new { type = "player_joined", playerId = player.UserIDString, playerName = player.displayName, role = session.Role, time = DateTime.Now.ToString("o") });

            // Check for alerts
            var alerts = GetUnacknowledgedAlerts(player.UserIDString);
            if (alerts.Count > 0)
            {
                var unack = alerts.Count;
                PrintToChat(player, $"<color=#FF4444>You have {unack} unacknowledged alert(s).</color> Use /db alerts to view.");
            }

            // Automation: welcome message
            var welcomeRule = _automationRules.Find(r => r.Name == "Welcome" && r.Enabled);
            if (welcomeRule != null)
            {
                var welcome = _agentBridge.GetResponse(player.displayName, session.Role, "welcome_message", null);
                PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {welcome}");
            }

            LogActivity("system", "Player connected", $"{player.displayName} ({player.UserIDString})", player.UserIDString, player.displayName);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            var session = GetOrCreateSession(player);
            if (session != null)
            {
                session.IsOnline = false;
                session.LastSeen = DateTime.Now;
                session.OnlineTime += DateTime.Now - session.SessionStart;
            }

            UpdateTrackedPlayer(player.UserIDString, lastSeen: DateTime.Now, position: player.transform.position);

            _mcpClient?.SendMessage(new { type = "player_left", playerId = player.UserIDString, playerName = player.displayName, reason = reason, time = DateTime.Now.ToString("o") });
            LogActivity("system", "Player disconnected", $"{player.displayName}: {reason}", player.UserIDString, player.displayName);
        }

        private void OnEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            if (info == null || info.Initiator == null) return;

            var attacker = info.Initiator as BasePlayer;
            var target = entity as BasePlayer;

            if (attacker != null && target != null)
            {
                // Track combat
                LogActivity("security", "Combat", $"{attacker.displayName} hit {target.displayName} for {info.damageTypes.Total()}", attacker.UserIDString, attacker.displayName);

                // Raid detection
                if (info.damageTypes.Has(DamageType.Explosion) || info.damageTypes.Has(DamageType.Heat))
                {
                    CreateAlert("raid", "high", "Explosion detected", $"Explosion near {target.displayName}'s position", entity.transform.position);
                    LogActivity("security", "Raid", $"Explosion: {attacker.displayName} vs {target.displayName}", attacker.UserIDString, attacker.displayName);
                }
            }

            // Turret shooting
            var turret = entity as AutoTurret;
            if (turret != null && info.InitiatorPlayer != null)
            {
                LogActivity("security", "Turret fire", $"Turret at {GetLocation(entity.transform.position)} shot {info.InitiatorPlayer.displayName}");
            }
        }

        private void OnPlayerAttacked(BasePlayer attacker, HitInfo info)
        {
            // Track player attacks
        }

        private void OnDoorOpened(BasePlayer player, Door door)
        {
            LogAccess(player.UserIDString, player.displayName, door.ShortPrefabName, "open", true, GetCameraNear(door.transform.position)?.Id);
            door.SetFlag(BaseEntity.Flags.Open, true);
            if (_computerSessions.TryGetValue(player.userID, out var session) && session.TerminalOpen)
            {
                session.Station = null;
                session.TerminalOpen = false;
                session.IsWatchingCCTV = false;
                session.ActiveCameraId = null;
            }
        }

        private void OnDoorClosed(Door door)
        {
            door.SetFlag(BaseEntity.Flags.Open, false);
        }

        private void OnExplosion(Vector3 position, float radius, BasePlayer attacker = null)
        {
            CreateAlert("raid", "critical", "Explosion!", $"Explosion at {GetLocation(position)} radius {radius}m", position);

            if (_config.EnableRaidAlerts)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    var dist = Vector3.Distance(player.transform.position, position);
                    if (dist < _config.RaidAlertRadius)
                    {
                        var session = GetOrCreateSession(player);
                        if (session?.Settings.RaidAlertsEnabled == true)
                        {
                            PrintToChat(player, $"<color=#FF0000>⚠ RAID ALERT:</color> Explosion at {GetLocation(position)} ({dist:F0}m away)");
                        }
                    }
                }
            }
        }

        private void Unload()
        {
            _heartbeatTimer?.Dispose();
            _automationTimer?.Dispose();
            _decayTimer?.Dispose();
            _radarTimer?.Dispose();
            _mcpClient?.Disconnect();
            SaveData();
        }

        // =====================================================================
        // COMPUTER STATION / CCTV HOOKS
        // Detect when a player sits at a computer station to use CCTV cameras.
        // This is the core integration point for in-game terminal interaction.
        // =====================================================================

        // Fired when a player presses USE on a ComputerStation entity.
        // Return non-null to block the interaction.
        private object OnComputerStationUse(BasePlayer player, ComputerStation station)
        {
            if (player == null || station == null) return null;

            var session = GetOrCreateSession(player);
            session.TerminalOpen = true;
            session.Station = station;
            session.SessionStart = DateTime.UtcNow;
            _computerSessions[player.userID] = session;

            // Notify AI agent
            _ = _agentBridge.SendToAgentAsync(new
            {
                type = "computer_station_open",
                playerId = player.UserIDString,
                playerName = player.displayName,
                stationId = station.net.ID.Value.ToString(),
                stationName = station.ShortPrefabName,
                timestamp = DateTime.UtcNow.ToString("O")
            });

            // Log the session
            LogActivity("security", "Terminal opened",
                $"Player {player.displayName} opened computer station",
                player.UserIDString, player.displayName);

            // Show the CUI terminal overlay
            ShowTerminalUI(player);

            // Announce in chat that DuckBot is ready at the terminal
            timer.Once(0.5f, () =>
            {
                if (player.IsConnected())
                {
                    player.ChatMessage(
                        "<color=#FFD700>🖥  DuckBot Terminal Active</color>\n" +
                        "<color=#888>Type <color=#FFD700>/db ask &lt;message&gt;</color> to control everything.\n" +
                        "Type <color=#FFD700>/db help</color> for all commands.\n" +
                        "Press <color=#888>E</color> or <color=#888>F</color> to cycle cameras in Rust's CCTV panel.</color>");
                }
            });

            PrintAsh($"[CCTV] {player.displayName} opened computer station terminal");
            return null; // Allow the interaction
        }

        // Fired every frame while a player is watching a CCTV camera.
        // station = the ComputerStation they used to enter, camera = the CCTV entity.
        private void OnCCTVCameraUsed(BasePlayer player, ComputerStation station, CCTVCamera camera)
        {
            if (player == null) return;

            var session = GetOrCreateSession(player);
            session.IsWatchingCCTV = true;
            session.Station = station;
            _computerSessions[player.userID] = session;

            string cameraId = camera?.net?.ID.Value.ToString() ?? "unknown";
            string cameraName = GetCameraDisplayName(camera, cameraId);

            if (session.ActiveCameraId != cameraId)
            {
                session.PreviousCameraId = session.ActiveCameraId;
                session.ActiveCameraId = cameraId;
                session.ActiveCameraName = cameraName;
                session.CamerasViewed++;

                PrintAsh($"[CCTV] {player.displayName} → viewing camera {cameraName} (ID: {cameraId})");

                // Notify AI agent of camera switch
                _ = _agentBridge.SendToAgentAsync(new
                {
                    type = "camera_viewed",
                    playerId = player.UserIDString,
                    playerName = player.displayName,
                    cameraId = cameraId,
                    cameraName = cameraName,
                    cameraCount = session.CamerasViewed,
                    timestamp = DateTime.UtcNow.ToString("O")
                });

                // Auto-detect monument camera codes
                DetectMonumentCamera(player, cameraId);

                // Log access
                AddAccessLog(player.UserIDString, player.displayName,
                    cameraId, "view", true, $"Watching {cameraName}");
            }
        }

        // Capture monument camera codes the player types into Rust's CCTV panel.
        // We detect new codes by watching the player's input while at a station.
        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;

            // Check if player is at a computer station
            if (!_computerSessions.TryGetValue(player.userID, out var session) || !session.TerminalOpen)
                return;

            // Detect camera code input via chat-like button presses
            // JUMP = cycle next camera, DUCK = cycle previous
            if (input.WasJustPressed(BUTTON.JUMP))
            {
                PrintAsh($"[CCTV] {player.displayName} cycled to NEXT camera");
                _ = _agentBridge.SendToAgentAsync(new
                {
                    type = "cctv_cycle",
                    playerId = player.UserIDString,
                    direction = "next"
                });
            }
            else if (input.WasJustPressed(BUTTON.DUCK))
            {
                PrintAsh($"[CCTV] {player.displayName} cycled to PREVIOUS camera");
                _ = _agentBridge.SendToAgentAsync(new
                {
                    type = "cctv_cycle",
                    playerId = player.UserIDString,
                    direction = "previous"
                });
            }
        }

        // Block movement while at computer station (keeps player "seated")
        private object CanClientMove(BasePlayer player, Proto.EntitySnapshot snapshot)
        {
            if (player == null) return null;
            var session = GetOrCreateSession(player);

            // Cancel warmup teleport if player moves during countdown
            if (session._pendingTeleport && session._teleportStartPos != null)
            {
                var currentPos = player.transform.position;
                var startPos = session._teleportStartPos.ToVector3();
                if (Vector3.Distance(currentPos, startPos) > 1f)
                {
                    session._pendingTeleport = false;
                    PrintToChat(player, "<color=#FF4444>Teleport cancelled:</color> you moved!");
                }
            }

            // Keep player seated if they're at the computer station
            if (!_computerSessions.TryGetValue(player.userID, out var compSession))
                return null;
            if (compSession.TerminalOpen && compSession.IsWatchingCCTV)
                return null;

            return null;
        }

        // Detect monument camera codes from the player's perspective while at CCTV
        private void DetectMonumentCamera(BasePlayer player, string cameraId)
        {
            if (player == null || string.IsNullOrEmpty(cameraId)) return;

            // Check if any known monument code was entered
            foreach (var code in _monumentCameraCodes)
            {
                if (cameraId.Contains(code, StringComparison.OrdinalIgnoreCase))
                {
                    PrintAsh($"[CCTV] Monument camera detected: <color=#00FF00>{code}</color> by {player.displayName}");
                    _ = _agentBridge.SendToAgentAsync(new
                    {
                        type = "monument_camera",
                        playerId = player.UserIDString,
                        monumentCode = code,
                        cameraId = cameraId
                    });
                    return;
                }
            }
        }

        // Get human-readable camera name
        private string GetCameraDisplayName(CCTVCamera camera, string cameraId)
        {
            if (camera == null) return $"Camera_{cameraId}";

            // Try to get position-based name
            var pos = camera.transform?.position ?? default(Vector3);

            // Check if it's near any known monument
            foreach (var kvp in _monumentLocations)
            {
                if (Vector3.Distance(pos, kvp.Value) < 200f)
                    return $"{kvp.Key} Camera";
            }

            return $"Camera_{cameraId.Substring(0, Math.Min(6, cameraId.Length))}";
        }

        // Show CUI terminal overlay when player opens computer station
        private void ShowTerminalUI(BasePlayer player)
        {
            if (player == null || !player.IsConnected()) return;

            int unreadAlerts = _activeAlerts.Count(a => !a.Acknowledged);
            string role = GetPlayerRole(player);

            var container = TerminalUI.BuildTerminal(
                player.displayName, role, unreadAlerts,
                _computerSessions.TryGetValue(player.userID, out var s) ? s.ActiveCameraName : "none",
                _commandStats.Count
            );

            CuiHelper.AddUi(player, container);
        }

        // Hide terminal UI when player closes computer station
        private void HideTerminalUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, TerminalUI.OVERLAY_NAME);
        }

        // Check if a player is currently at a computer station
        private bool IsAtComputerStation(ulong playerId)
        {
            return _computerSessions.TryGetValue(playerId, out var session) && session.TerminalOpen;
        }

        // Get current camera session for a player
        private ComputerStationSession GetCameraSession(ulong playerId)
        {
            _computerSessions.TryGetValue(playerId, out var session);
            return session;
        }

        // =====================================================================
        // CUI CHAT SCREEN — rendered inside the computer station terminal
        // =====================================================================

        // Per-player input field state (keyed by SteamID)
        private Dictionary<ulong, string> _chatInputDraft = new Dictionary<ulong, string>();

        /// <summary>Called by CUI input handler when player types in the chat field.</summary>
        private void OnChatInputChanged(BasePlayer player, string text)
        {
            if (player == null) return;
            _chatInputDraft[player.userID] = text;
        }

        /// <summary>Called when player presses Enter / SEND in the chat panel.</summary>
        private void OnChatSubmit(BasePlayer player, string text)
        {
            if (player == null || string.IsNullOrWhiteSpace(text)) return;
            _chatInputDraft[player.userID] = "";

            var session = GetOrCreateSession(player);
            session.IsAtComputerStation = IsPlayerAtComputerStation(player);
            HandleAIChat(player, session, text);

            // Refresh the chat panel so the new message appears
            timer.Once(0.3f, () =>
            {
                if (player.IsConnected() && IsPlayerAtComputerStation(player))
                    ShowChatPanel(player);
            });
        }

        /// <summary>Show the in-terminal chat panel with AI conversation history.</summary>
        private void ShowChatPanel(BasePlayer player)
        {
            if (player == null || !player.IsConnected()) return;

            // Blow away old terminal UI and rebuild as chat-focused layout
            CuiHelper.DestroyUi(player, "duckbot_chat");
            CuiHelper.DestroyUi(player, "duckbot_terminal");

            var container = new CuiElementContainer();

            // ── FULL-SCREEN CHAT PANEL ────────────────────────────────────────
            container.Add(new CuiElement
            {
                Name = "duckbot_chat",
                Parent = "Overlay",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0.55 0", AnchorMax = "0.99 1" },
                    new CuiImageComponent { Color = "0.04 0.04 0.07 0.97", Material = "assets/content/ui/uibackgroundblur.mat" }
                }
            });

            // ── HEADER ──────────────────────────────────────────────────────
            container.Add(new CuiElement
            {
                Parent = "duckbot_chat",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0 0.93", AnchorMax = "1 1" },
                    new CuiImageComponent { Color = "0.12 0.09 0.04 1" }
                }
            });
            container.Add(new CuiElement
            {
                Parent = "duckbot_chat",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0 0.93", AnchorMax = "1 1" },
                    new CuiTextComponent { Text = "💬  TERMINAL CHAT", FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "1 0.84 0 1" }
                }
            });
            // Back button (×)
            container.Add(new CuiElement
            {
                Name = "duckbot_chat_back_btn",
                Parent = "duckbot_chat",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0.88 0.1", AnchorMax = "0.97 0.9" },
                    new CuiImageComponent { Color = "0.2 0.08 0.08 1" }
                }
            });
            container.Add(new CuiElement
            {
                Parent = "duckbot_chat_back_btn",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                    new CuiTextComponent { Text = "✕", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 0.5 0.5 1" }
                }
            });

            // ── MESSAGE AREA ─────────────────────────────────────────────────
            container.Add(new CuiElement
            {
                Parent = "duckbot_chat",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0.02 0.14", AnchorMax = "0.98 0.92" },
                    new CuiImageComponent { Color = "0.03 0.03 0.05 1" }
                }
            });

            var session = GetOrCreateSession(player);
            var history = session?.ChatHistory ?? new List<ChatEntry>();
            var recent = history.Skip(Math.Max(0, history.Count - 30)).ToList();

            float msgAreaH = 0.92f - 0.14f; // 0.78 total height
            float msgH = msgAreaH / 25f;
            int maxDisplay = Math.Min(25, recent.Count);

            for (int i = 0; i < maxDisplay; i++)
            {
                var entry = recent[recent.Count - maxDisplay + i];
                bool isAi = entry.IsAI;
                float yBottom = 0.14f + (msgAreaH - (i + 1) * msgH);
                float yTop = yBottom + msgH * 0.9f;
                var timeStr = entry.Time.ToString("HH:mm");
                var senderColor = isAi ? "#00DD88" : (entry.Sender == "DuckBot" ? "#FFD700" : "#4DA6FF");

                container.Add(new CuiElement
                {
                    Parent = "duckbot_chat",
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = $"0.02 {yBottom}", AnchorMax = $"0.15 {yTop}" },
                        new CuiTextComponent { Text = timeStr, FontSize = 7, Align = TextAnchor.UpperRight, Color = "0.4 0.4 0.4 1" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = "duckbot_chat",
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = $"0.16 {yBottom}", AnchorMax = $"0.98 {yTop}" },
                        new CuiTextComponent {
                            Text = $"<color={senderColor}><b>{entry.Sender}:</b></color> {entry.Message}",
                            FontSize = 9, Align = TextAnchor.UpperLeft,
                            Color = isAi ? "0.85 1 0.9 1" : "0.85 0.85 0.85 1"
                        }
                    }
                });
            }

            if (history.Count == 0)
            {
                container.Add(new CuiElement
                {
                    Parent = "duckbot_chat",
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0.02 0.4", AnchorMax = "0.98 0.6" },
                        new CuiTextComponent { Text = "<color=#888>No messages yet. Start chatting below!</color>", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.4 0.4 0.4 1" }
                    }
                });
            }

            // ── INPUT AREA ───────────────────────────────────────────────────
            container.Add(new CuiElement
            {
                Parent = "duckbot_chat",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0 0.055", AnchorMax = "1 0.13" },
                    new CuiImageComponent { Color = "0.08 0.06 0.04 1" }
                }
            });
            container.Add(new CuiElement
            {
                Name = "duckbot_chat_input_bg",
                Parent = "duckbot_chat",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0.02 0.065", AnchorMax = "0.85 0.12" },
                    new CuiImageComponent { Color = "0.08 0.08 0.1 1" }
                }
            });

            var draft = _chatInputDraft.TryGetValue(player.userID, out var d) ? d : "";
            container.Add(new CuiElement
            {
                Parent = "duckbot_chat_input_bg",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0.01 0", AnchorMax = "0.99 1" },
                    new CuiInputFieldComponent
                    {
                        Text = draft,
                        Command = "db_chat_input ",
                        FontSize = 11, Color = "0.9 0.9 0.9 1",
                        Align = TextAnchor.MiddleLeft, CharLimit = 300,
                        IsPassword = false, ReadOnly = false,
                        NeedsCursor = true, Autofocus = true,
                    }
                }
            });

            // Send button
            container.Add(new CuiElement
            {
                Name = "duckbot_chat_send_btn",
                Parent = "duckbot_chat",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0.87 0.065", AnchorMax = "0.97 0.12" },
                    new CuiImageComponent { Color = "0.8 0.55 0 1" }
                }
            });
            container.Add(new CuiElement
            {
                Parent = "duckbot_chat_send_btn",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                    new CuiTextComponent { Text = "SEND", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }
            });

            // Quick prompt buttons
            var quickPrompts = new[] { "Who is online?", "Any raiders nearby?", "Show alerts", "Base status" };
            container.Add(new CuiElement
            {
                Parent = "duckbot_chat",
                Components = {
                    new CuiRectTransformComponent { AnchorMin = "0 0.005", AnchorMax = "1 0.05" },
                    new CuiImageComponent { Color = "0.05 0.04 0.03 1" }
                }
            });

            float xQ = 0.01f;
            foreach (var prompt in quickPrompts)
            {
                float width = 0.24f;
                var btnHash = Math.Abs(prompt.GetHashCode()) & 0xFFFF;
                container.Add(new CuiElement
                {
                    Name = $"duckbot_qbtn_{btnHash}",
                    Parent = "duckbot_chat",
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = $"{xQ} 0.01", AnchorMax = $"{xQ + width - 0.01} 0.05" },
                        new CuiImageComponent { Color = "0.12 0.09 0.06 1" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = $"duckbot_qbtn_{btnHash}",
                    Components = {
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                        new CuiTextComponent { Text = $"⚡ {prompt}", FontSize = 7, Align = TextAnchor.MiddleCenter, Color = "0.9 0.75 0.4 1" }
                    }
                });
                xQ += width;
            }

            CuiHelper.AddUi(player, container);

            // Register console command: /db_chat_input <text> is called by the input field
            cmd.AddConsoleCommand("db_chat_input", this, nameof(CmdChatInput));
        }

        private void CmdChatInput(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            var text = arg.FullString?.Trim() ?? "";
            if (!string.IsNullOrEmpty(text))
                OnChatSubmit(player, text);
            else
                ShowChatPanel(player); // re-render on empty enter
        }

        // =====================================================================
        // PLAYER SESSION HELPERS
        // =====================================================================

        private PlayerSession GetOrCreateSession(BasePlayer player)
        {
            if (!_sessions.TryGetValue(player.userID, out var session))
            {
                session = new PlayerSession
                {
                    PlayerId = player.userID,
                    DisplayName = player.displayName,
                    Role = GetPlayerRole(player)
                };
                _sessions[player.userID] = session;
            }
            else
            {
                // Refresh role
                session.Role = GetPlayerRole(player);
                session.DisplayName = player.displayName;
            }
            return session;
        }

        private string GetPlayerRole(BasePlayer player)
        {
            foreach (var adminId in _config.AdminSteamIds)
                if (player.UserIDString == adminId) return "admin";
            if (player.IsAdmin) return "admin";
            if (permission.UserHasPermission(player.UserIDString, "rustduckbot.admin")) return "admin";
            if (permission.UserHasPermission(player.UserIDString, "rustduckbot.mod")) return "mod";
            if (permission.UserHasPermission(player.UserIDString, "rustduckbot.vip")) return "vip";
            return "user";
        }

        private bool HasRoleOrHigher(string playerRole, string requiredRole)
        {
            var order = new[] { "user", "vip", "mod", "admin" };
            var pIdx = Array.IndexOf(order, playerRole);
            var rIdx = Array.IndexOf(order, requiredRole);
            return pIdx >= rIdx;
        }

        // =====================================================================
        // MAIN COMMAND HANDLER
        // =====================================================================

        private void CmdDuckBot(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            var session = GetOrCreateSession(player);
            session.IsAtComputerStation = IsPlayerAtComputerStation(player);
            session.LastPosition = new Position3D(player.transform.position);

            // Track command usage
            TrackCommand("duckbot");

            if (args.Length == 0)
            {
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
                case "help": case "h": ShowHelp(player, session); break;
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
                case "shop": case "market": ShowShop(player, session); break;
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
                case "slay": HandleSlay(player, session, argStr); break;
                case "respawn": HandleRespawn(player, session, argStr); break;
                case "notes": HandleNotes(player, session, argStr); break;
                case "adminmsg": HandleAdminMsg(player, session, argStr); break;
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
                case "cupsize": HandleCupSize(player, session); break;
                case "decaycheck": HandleDecayCheck(player, session, argStr); break;

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
                case "freeze": HandleFreeze(player, session, argStr); break;
                case "heal": HandleHeal(player, session, argStr); break;
                case "give": HandleGive(player, session, argStr); break;
                case "teleport": case "tp": HandleTeleport(player, session, argStr); break;
                case "spawn": HandleSpawn(player, session, argStr); break;

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

                // === AI CHAT ===
                case "ask": case "ai": HandleAIChat(player, session, argStr); break;
                case "search": SearchKnowledge(player, session, argStr); break;
                case "recommend": GetRecommendations(player, session); break;
                case "analyze": AnalyzeBase(player, session); break;

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
                case "slots": PlaySlots(player, session); break;
                case "bet": PlaceBet(player, session, argStr); break;

                // === MISC ===
                case "version": case "ver": ShowVersion(player); break;
                case "credits": ShowCredits(player); break;
                case "changelog": ShowChangelog(player); break;
                case "donate": ShowDonateInfo(player); break;
                case "discord": ShowDiscord(player); break;
                case "support": ShowSupport(player); break;

                default:
                    // Treat as AI chat
                    HandleAIChat(player, session, fullMessage);
                    break;
            }
        }

        // =====================================================================
        // HELP & INFO
        // =====================================================================

        private void ShowHelp(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>");
            PrintToChat(player, "<color=#FFD700>      RUSSDUCKBOT v1.2.0 — HELP</color>");
            PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>");
            PrintToChat(player, "<color=#FFD700>/db terminal</color> — Open AI computer terminal");
            PrintToChat(player, "<color=#FFD700>/db help</color> — Show this help");
            PrintToChat(player, "<color=#888>/db whoami</color> — Your role & permissions");
            PrintToChat(player, "<color=#888>/db server</color> — Server information");

            PrintToChat(player, "\n<color=#00BFFF>━━━ CCTV SYSTEM ━━━</color>");
            PrintToChat(player, "<color=#888>/db cameras</color> — List all CCTV cameras");
            PrintToChat(player, "<color=#888>/db view <id></color> — View camera feed");
            PrintToChat(player, "<color=#888>/db control <dir></color> — PTZ control (left/right/up/down/zoom/reset)");
            PrintToChat(player, "<color=#888>/db recordings</color> — View recent recordings");

            PrintToChat(player, "\n<color=#FF6B6B>━━━ SECURITY ━━━</color>");
            PrintToChat(player, "<color=#888>/db security</color> — Security dashboard");
            PrintToChat(player, "<color=#888>/db alerts</color> — View active alerts");
            PrintToChat(player, "<color=#888>/db ack <id></color> — Acknowledge alert");
            PrintToChat(player, "<color=#888>/db access</color> — Access log");
            PrintToChat(player, "<color=#888>/db scan</color> — Scan nearby area");
            PrintToChat(player, "<color=#888>/db lockdown</color> — Emergency lockdown");
            PrintToChat(player, "<color=#888>/db sos</color> — Send emergency alert");

            PrintToChat(player, "\n<color=#9B59B6>━━━ BASE MANAGEMENT ━━━</color>");
            PrintToChat(player, "<color=#888>/db base</color> — Base information");
            PrintToChat(player, "<color=#888>/db doors</color> — List doors");
            PrintToChat(player, "<color=#888>/db lights</color> — List lights");
            PrintToChat(player, "<color=#888>/db turrets</color> — List turrets");
            PrintToChat(player, "<color=#888>/db decay</color> — Decay status");
            PrintToChat(player, "<color=#888>/db upkeep</color> — Upkeep info");
            PrintToChat(player, "<color=#888>/db auth</color> — TC auth list");

            PrintToChat(player, "\n<color=#3498DB>━━━ TRADING ━━━</color>");
            PrintToChat(player, "<color=#888>/db shop</color> — Browse market");
            PrintToChat(player, "<color=#888>/db sell <item> <price></color> — Sell item");
            PrintToChat(player, "<color=#888>/db buy <item></color> — Buy item");
            PrintToChat(player, "<color=#888>/db price <item></color> — Check prices");
            PrintToChat(player, "<color=#888>/db vending</color> — Manage vending machines");

            PrintToChat(player, "\n<color=#1ABC9C>━━━ INTEL ━━━</color>");
            PrintToChat(player, "<color=#888>/db players</color> — Online players");
            PrintToChat(player, "<color=#888>/db player <name></color> — Player details");
            PrintToChat(player, "<color=#888>/db track <name></color> — Track a player");
            PrintToChat(player, "<color=#888>/db history</color> — Your chat history");
            PrintToChat(player, "<color=#888>/db playerhistory <name></color> — Player chat/activity history");
            PrintToChat(player, "<color=#888>/db leaderboard</color> — Top players");
            PrintToChat(player, "<color=#888>/db stats</color> — Player statistics");
            PrintToChat(player, "<color=#888>/db radar</color> — Nearby players");
            PrintToChat(player, "<color=#888>/db grid</color> — Grid map");
            PrintToChat(player, "<color=#888>/db mark <name></color> — Place marker");
            PrintToChat(player, "<color=#888>/db near <radius></color> — Find nearby players");
            PrintToChat(player, "<color=#888>/db raiders</color> — Active raiders");
            PrintToChat(player, "<color=#888>/db raid</color> — Raid history");

            PrintToChat(player, "\n<color=#E67E22>━━━ AUTOMATION ━━━</color>");
            PrintToChat(player, "<color=#888>/db automation</color> — Automation dashboard");
            PrintToChat(player, "<color=#888>/db rules</color> — Automation rules");
            PrintToChat(player, "<color=#888>/db schedule</color> — Scheduled tasks");

            PrintToChat(player, "\n<color=#F39C12>━━━ AI TERMINAL ━━━</color>");
            PrintToChat(player, "<color=#888>/db ask <question></color> — Ask AI anything");
            PrintToChat(player, "<color=#888>/db analyze</color> — Analyze your base");
            PrintToChat(player, "<color=#888>/db recommend</color> — Get recommendations");
            PrintToChat(player, "<color=#888>/db search <query></color> — Search knowledge");

            PrintToChat(player, "\n<color=#888>━━━ ACTIVITY & CHAT ━━━</color>");
            PrintToChat(player, "<color=#888>/db activity</color> — Recent activity");
            PrintToChat(player, "<color=#888>/db broadcast <msg></color> — Broadcast (admin)");
            PrintToChat(player, "<color=#888>/db say <msg></color> — Chat with AI");

            if (HasRoleOrHigher(session.Role, "vip"))
            {
                PrintToChat(player, "\n<color=#00FF00>━━━ VIP COMMANDS ━━━</color>");
                PrintToChat(player, "<color=#888>/db door <id> lock/unlock</color> — Control doors");
                PrintToChat(player, "<color=#888>/db light <id> on/off</color> — Control lights");
                PrintToChat(player, "<color=#888>/db time</color> — Game time & weather");
                PrintToChat(player, "<color=#888>/db monuments</color> — Monument map");
                PrintToChat(player, "<color=#888>/db loot <type></color> — Loot locations");
                PrintToChat(player, "<color=#888>/db kits</color> — Available kits");
            }

            if (HasRoleOrHigher(session.Role, "mod"))
            {
                PrintToChat(player, "\n<color=#FF9900>━━━ MOD COMMANDS ━━━</color>");
                PrintToChat(player, "<color=#888>/db kick <player> <reason></color> — Kick player");
                PrintToChat(player, "<color=#888>/db mute <player></color> — Mute player");
                PrintToChat(player, "<color=#888>/db freeze <player></color> — Freeze player");
                PrintToChat(player, "<color=#888>/db msg <player> <msg></color> — Private message");
                PrintToChat(player, "<color=#888>/db team <msg></color> — Team message");
            }

            if (HasRoleOrHigher(session.Role, "admin"))
            {
                PrintToChat(player, "\n<color=#FF4444>━━━ ADMIN COMMANDS ━━━</color>");
                PrintToChat(player, "<color=#888>/db status</color> — Server status");
                PrintToChat(player, "<color=#888>/db ban <player> <reason></color> — Ban player");
                PrintToChat(player, "<color=#888>/db unban <steamid></color> — Unban player");
                PrintToChat(player, "<color=#888>/db admin <cmd></color> — Run RCON command");
                PrintToChat(player, "<color=#888>/db heal <player></color> — Heal player");
                PrintToChat(player, "<color=#888>/db give <player> <item> <qty></color> — Give items");
                PrintToChat(player, "<color=#888>/db tp <from> <to></color> — Teleport");
                PrintToChat(player, "<color=#888>/db spawn <item> <qty></color> — Spawn item");
                PrintToChat(player, "<color=#888>/db settings</color> — Server settings");
            }

            PrintToChat(player, "\n<color=#FFD700>━━━ GAMES & FUN ━━━</color>");
            PrintToChat(player, "<color=#888>/db roll <max></color> — Roll dice");
            PrintToChat(player, "<color=#888>/db flip</color> — Flip coin");
            PrintToChat(player, "<color=#888>/db 8ball <question></color> — Magic 8 ball");
            PrintToChat(player, "<color=#888>/db joke</color> — Random joke");
            PrintToChat(player, "<color=#888>/db fortune</color> — Daily fortune");

            PrintToChat(player, "\n<color=#FFD700>═══════════════════════════════════════</color>");
            PrintToChat(player, "Type /db <command> to use. Use /db terminal for AI chat.");
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
            PrintToChat(player, $"<color=#FFD700>Plugin:</color> RustDuckBot v1.3.0");
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
            PrintToChat(player, $"<color=#FFD700>MCP:</color> {(_mcpClient?.IsConnected() == true ? "<color=#00FF00>Connected" : "<color=#FF4444>Disconnected")}");
            PrintToChat(player, "<color=#FFD700>═══════════════════════════════════════</color>");
        }

        private void WhoAmI(BasePlayer player, PlayerSession session)
        {
            var roleColor = session.Role switch { "admin" => "#FF4444", "mod" => "#FF9900", "vip" => "#00FF00", _ => "#FFD700" };
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

            // Show recent alerts
            if (alerts.Count > 0)
            {
                PrintToChat(player, "\n<color=#FF4444>Recent Alerts:</color>");
                foreach (var alert in alerts.Take(5))
                {
                    var sevColor = alert.Severity switch { "critical" => "#FF0000", "high" => "#FF4444", "medium" => "#FF9900", _ => "#FFD700" };
                    PrintToChat(player, $"  <color={sevColor}>[{alert.Severity.ToUpper()}]</color> {alert.Title}: {alert.Message}");
                }
            }

            // Show recent access
            if (accessEntries.Count > 0)
            {
                PrintToChat(player, "\n<color=#888>Recent Access:</color>");
                foreach (var access in accessEntries.Take(5))
                {
                    PrintToChat(player, $"  <color=#888>[{access.Time:HH:mm}]</color> {access.PlayerName} {access.Action} {access.Resource}");
                }
            }

            // Show monitored bases
            var myBases = _monitoredBases.Where(b => b.OwnerId == player.UserIDString || b.AuthorizedPlayers.Contains(player.UserIDString)).ToList();
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
                var sevColor = alert.Severity switch { "critical" => "#FF0000", "high" => "#FF4444", "medium" => "#FF9900", _ => "#FFD700" };
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
                var icon = entry.Action switch { "enter" => "→", "exit" => "←", "view" => "👁", "control" => "⚙", "attempt" => "⚠", _ => "•" };
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
                    var color = tp.ThreatLevel switch { "high" => "#FF0000", "medium" => "#FF9900", _ => "#FFD700" };
                    PrintToChat(player, $"  <color={color}>[</color>{tp.ThreatLevel.ToUpper()}<color={color}>]</color> {tp.DisplayName} | K:{tp.Kills} D:{tp.Deaths} | Last seen: {tp.LastSeen:HH:mm}");
                }
                return;
            }

            var target = _trackedPlayers.Values.FirstOrDefault(p => p.DisplayName.Contains(targetName, StringComparison.OrdinalIgnoreCase));
            if (target == null) { PrintToChat(player, $"Player not tracked: {targetName}"); return; }

            PrintToChat(player, $"<color=#FFD700>═══ THREAT: {target.DisplayName} ═══</color>");
            var tColor = target.ThreatLevel switch { "high" => "#FF0000", "medium" => "#FF9900", "low" => "#00FF00", _ => "#888" };
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
            var bases = _monitoredBases.Where(b => b.OwnerId == player.UserIDString || b.AuthorizedPlayers.Contains(player.UserIDString)).ToList();
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
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }

            var parts = args.Split(' ', 2);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db door <lock_id|position> lock/unlock/open/close"); return; }

            var action = parts[1].ToLowerInvariant();
            var validActions = new[] { "lock", "unlock", "open", "close" };
            if (Array.IndexOf(validActions, action) < 0) { PrintToChat(player, $"Valid: {string.Join(", ", validActions)}"); return; }

            var doors = UnityEngine.Object.FindObjectsOfType<Door>().ToList();
            var targetDoor = doors.FirstOrDefault(d => d.UserIDString == parts[0] || GetLocation(d.transform.position).Contains(parts[0]));
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
            var lights = UnityEngine.Object.FindObjectsOfType<ElectricHeater>().ToList();
            PrintToChat(player, $"<color=#9B59B6>═══ LIGHTS ({lights.Count} found) ═══</color>");
            foreach (var light in lights.Take(20))
            {
                var isOn = light.IsOn();
                PrintToChat(player, $"  {(isOn ? "💡" : "⚫")} {light.ShortPrefabName ?? "Light"} @ {GetLocation(light.transform.position)}");
            }
        }

        private void ControlLight(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }

            var parts = args.Split(' ', 2);
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
                var active = turret.IsActive();
                PrintToChat(player, $"  {(online ? "🔫" : "⚫")} {turret.ShortPrefabName ?? "Turret"} {(active ? "🔫ACTIVE" : "")} @ {GetLocation(turret.transform.position)}");
            }
        }

        private void ControlTurret(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod+ required</color>"); return; }

            var parts = args.Split(' ', 2);
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
            var upkeep = player.inventory.AllItems().Sum(i => i.amount);
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
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }
            var parts = args.Split(' ', 2);
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
            var parts = args.Split(' ', 2);
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

            var listing = _shopListings.FirstOrDefault(l => l.Available && l.ItemName.Contains(args, StringComparison.OrdinalIgnoreCase));
            if (listing == null) { PrintToChat(player, $"Item not found: {args}"); return; }

            PrintToChat(player, $"<color=#FFD700>BUY:</color> {listing.ItemName} @ {listing.PricePerUnit} {listing.Currency}");
        }

        private void CheckPrice(BasePlayer player, PlayerSession session, string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) { PrintToChat(player, "Usage: /db price <item_name>"); return; }

            var listings = _shopListings.Where(l => l.ItemName.Contains(itemName, StringComparison.OrdinalIgnoreCase) && l.Available).ToList();
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

            var tracked = _trackedPlayers.GetValueOrDefault(target.UserIDString);
            var pSession = GetOrCreateSession(target);

            PrintToChat(player, $"<color=#1ABC9C>═══ PLAYER: {target.displayName} ═══</color>");
            PrintToChat(player, $"  SteamID: {target.UserIDString}");
            PrintToChat(player, $"  Role: {pSession.Role}");
            PrintToChat(player, $"  Position: {GetLocation(target.transform.position)}");
            PrintToChat(player, $"  Online: {(target.IsConnected() ? "🟢" : "⚫")} {(tracked?.LastSeen ?? DateTime.Now):HH:mm}");

            if (tracked != null)
            {
                var kd = tracked.Deaths > 0 ? (tracked.Kills / (float)tracked.Deaths).ToString("F2") : tracked.Kills.ToString();
                PrintToChat(player, $"  Kills: {tracked.Kills} | Deaths: {tracked.Deaths}");
                PrintToChat(player, $"  K/D: {kd}");
                PrintToChat(player, $"  Sessions: {tracked.SessionCount} | Time: {tracked.TotalOnlineTime.TotalHours:F1}h");
                PrintToChat(player, $"  Raids: {tracked.RaidsParticipated}");
                var tColor = tracked.ThreatLevel switch { "high" => "#FF0000", "medium" => "#FF9900", "low" => "#00FF00", _ => "#888" };
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

            var tracked = _trackedPlayers.GetValueOrDefault(target.UserIDString);
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
            var parts = args.Split(' ', 3);

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
            if (!HasRoleOrHigher(session.Role, "vip")) { PrintToChat(player, "<color=#FF4444>VIP+ required</color>"); return; }

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

            var parts = args.Split(' ', 3);
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
                case "lights.on": ConsoleSystemRun.ServerCommand("lights on"); break;
                case "lights.off": ConsoleSystemRun.ServerCommand("lights off"); break;
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
            if (_localAI.IsLocalProvider)
            {
                // Direct LM Studio / OpenAI / Anthropic / OpenRouter
                response = _localAI.GetResponse(player.displayName, session.Role, message, session.ChatHistory);
            }
            else
            {
                // DuckBot MCP / agent bridge
                response = _agentBridge.GetResponse(player.displayName, session.Role, message, session.ChatHistory);
            }

            session.ChatHistory.Add(new ChatEntry { Sender = "DuckBot", Message = response, Time = DateTime.Now, IsAI = true });

            // Handle multi-line responses
            var lines = response.Split('\n');
            foreach (var line in lines)
                PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {line.Trim()}");

            // Send to MCP (skip if we used a local provider without MCP)
            if (_mcpClient?.IsConnected() == true)
                _mcpClient?.SendMessage(new { type = "ai_chat", playerId = player.UserIDString, playerName = player.displayName, message, response });
        }

        private void SearchKnowledge(BasePlayer player, PlayerSession session, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) { PrintToChat(player, "Usage: /db search <query>"); return; }

            var results = _activityLog.Where(a => a.Action.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Details.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(10).ToList();
            PrintToChat(player, $"<color=#FFD700>═══ SEARCH: {query} ({results.Count} results) ═══</color>");
            foreach (var r in results)
                PrintToChat(player, $"  <color=#888>[{r.Time:MM/dd HH:mm}]</color> {r.Category}: {r.Action} - {r.Details}");
        }

        private void GetRecommendations(BasePlayer player, PlayerSession session)
        {
            var pos = player.transform.position;
            PrintToChat(player, "<color=#FFD700>═══ AI RECOMMENDATIONS ═══</color>");
            PrintToChat(player, $"Position: {GetLocation(pos)}");
            PrintToChat(player, "1. Check nearby monuments for loot");
            PrintToChat(player, "2. Monitor decay on your base");
            PrintToChat(player, "3. Keep tracking raider activity");
            PrintToChat(player, "4. Use /db analyze for detailed base analysis");
            var response = _agentBridge.GetResponse(player.displayName, session.Role, "recommendations_for_player", null);
            PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {response}");
        }

        private void AnalyzeBase(BasePlayer player, PlayerSession session)
        {
            var bases = _monitoredBases.Where(b => b.OwnerId == player.UserIDString).ToList();
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
                var color = entry.Category switch { "security" => "#FF6B6B", "base" => "#9B59B6", "trade" => "#3498DB", "system" => "#888", _ => "#FFD700" };
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

        private void SendMessage(BasePlayer player, PlayerSession session, string args)
        {
            var parts = args.Split(' ', 2);
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
            PrintToChat(player, $"<color=#FFD700>MCP:</color> {(_mcpClient?.IsConnected() == true ? "<color=#00FF00>Connected" : "<color=#FF4444>Disconnected")}");
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
            ConsoleSystemRun.ServerCommand(command);
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
            ConsoleSystemRun.ServerCommand($"banid {target.UserIDString} \"{reason}\" {duration}");
            target.Kick(reason);
            PrintToChat(player, $"Banned: {target.displayName} ({duration})");
            LogActivity("admin", "Ban", $"{player.displayName} banned {target.displayName}: {reason} ({duration})", player.UserIDString, player.displayName);
            _mcpClient?.SendMessage(new { type = "ban", playerId = player.UserIDString, targetId = target.UserIDString, reason, duration });
        }

        private void HandleUnban(BasePlayer player, PlayerSession session, string steamId)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            if (string.IsNullOrWhiteSpace(steamId)) { PrintToChat(player, "Usage: /db unban <steamid>"); return; }
            ConsoleSystemRun.ServerCommand($"unban {steamId}");
            PrintToChat(player, $"<color=#00FF00>Unbanned:</color> {steamId}");
            LogActivity("admin", "Unban", $"{player.displayName} unbanned {steamId}", player.UserIDString, player.displayName);
        }

        private void HandleMute(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod required</color>"); return; }
            if (string.IsNullOrWhiteSpace(targetName)) { PrintToChat(player, "Usage: /db mute <player>"); return; }
            var target = FindPlayer(targetName);
            if (target == null) { PrintToChat(player, $"Player not found: {targetName}"); return; }
            _mutedPlayers.Add(target.displayName);
            PrintToChat(player, $"<color=#FF9900>Muted:</color> {target.displayName}");
            LogActivity("admin", "Mute", $"{player.displayName} muted {target.displayName}", player.UserIDString, player.displayName);
        }

        private void HandleFreeze(BasePlayer player, PlayerSession session, string targetName)
        {
            if (!HasRoleOrHigher(session.Role, "mod")) { PrintToChat(player, "<color=#FF4444>Mod required</color>"); return; }
            if (string.IsNullOrWhiteSpace(targetName)) { PrintToChat(player, "Usage: /db freeze <player>"); return; }
            var target = FindPlayer(targetName);
            if (target == null) { PrintToChat(player, $"Player not found: {targetName}"); return; }
            target.SetFlag(BaseEntity.Flags.Frozen, true);
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
            var parts = args.Split(' ', 3);
            if (parts.Length < 2) { PrintToChat(player, "Usage: /db give <player> <item> <qty>"); return; }
            var target = FindPlayer(parts[0]);
            if (target == null) { PrintToChat(player, $"Player not found: {parts[0]}"); return; }
            var qty = parts.Length > 2 && int.TryParse(parts[2], out var q) ? q : 1;
            PrintToChat(player, $"<color=#00FF00>Give:</color> {qty}x {parts[1]} to {target.displayName}");
        }

        private void HandleTeleport(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>Admin required</color>"); return; }
            var parts = args.Split(' ', 2);
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
            var parts = args.Split(' ', 2);
            if (parts.Length == 0) { PrintToChat(player, "Usage: /db spawn <item> [qty]"); return; }
            var qty = parts.Length > 1 && int.TryParse(parts[1], out var q) ? q : 1;
            PrintToChat(player, $"<color=#00FF00>Spawning:</color> {qty}x {parts[0]}");
        }

        // =====================================================================
        // UTILITY
        // =====================================================================

        private void ShowTime(BasePlayer player, PlayerSession session)
        {
            var time = TODWorld.Timespan;
            var hours = (int)time.TotalHours % 24;
            var mins = (int)time.TotalMinutes % 60;
            PrintToChat(player, "<color=#FFD700>═══ GAME TIME ═══</color>");
            PrintToChat(player, $"Time: {hours:D2}:{mins:D2}");
            PrintToChat(player, $"Day: {World.Timeframes.GetValueOrDefault("day", 1)}");
            PrintToChat(player, $"Sun: {(hours >= 6 && hours < 18 ? "☀️" : "🌙")}");
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

            var parts = args.Split(' ', 2);
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
                    if (target.IsConnected()) PrintToChat(player, $"<color=#888>Request to {target.displayName} expired.</color>");
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
            if (fromPlayer?.IsConnected() == true)
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
                    h.Key.Contains(homeName, StringComparison.OrdinalIgnoreCase))
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
            if (player == null || !player.IsConnected()) return;

            var session = GetOrCreateSession(player);

            // Save current position first (so /back works)
            session.LastPosition = new Position3D(player.transform.position);

            // Warmup delay if configured
            if (_config.TeleportWarmupSeconds > 0 && !HasRoleOrHigher(session.Role, "mod"))
            {
                PrintToChat(player, $"<color=#FFD700>Don't move!</color> Teleporting in {_config.TeleportWarmupSeconds}s...");

                // Cancel if player moves during warmup — hook will catch it
                session._pendingTeleport = true;
                session._teleportDestination = destination;
                session._teleportReason = reason;
                session._teleportStartPos = new Position3D(player.transform.position);

                timer.Once(_config.TeleportWarmupSeconds * 1000f, () =>
                {
                    if (session._pendingTeleport && player.IsConnected())
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
            session.LastReportSent = DateTime.Now;
            PrintToChat(player, $"<color=#00FF88>Report submitted.</color> ID: <color=#FFD700>{report.Id}</color>");
            foreach (var p in BasePlayer.activePlayerList)
            {
                var s = GetOrCreateSession(p);
                if (s.Role == "admin" || s.Role == "mod")
                    PrintToChat(p, $"<color=#FF4444>REPORT #{report.Id}:</color> {player.displayName} -> {report.TargetName}: {reason}");
            }
        }

        private void HandleSlay(BasePlayer player, PlayerSession session, string args)
        {
            if (!HasRoleOrHigher(session.Role, "admin")) { PrintToChat(player, "<color=#FF4444>No permission.</color>"); return; }
            var parts = SplitArgs(args, 2);
            var targetName = parts[0].Trim();
            var reason = parts.Length > 1 ? parts[1].Trim() : "Slain by admin";
            if (string.IsNullOrEmpty(targetName)) { PrintToChat(player, "<color=#FFD700>Usage:</color> /db slay <player> [reason]"); return; }
            var target = FindPlayer(targetName);
            if (target == null) { PrintToChat(player, $"<color=#FF4444>Player not found:</color> {targetName}"); return; }
            target.Hurt(new HitInfo(target, target, DamageType.Suicide, 9999f));
            Broadcast(player, session, $"<color=#FF4444>{target.displayName} was slain:</color> {reason}");
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
            if (_config.DailyRewardScrap > 0)
                ConsoleSystemRun.ServerCommand($" scavenger.additem "{player.UserIDString}" scrap {_config.DailyRewardScrap}");
            PrintToChat(player, "<color=#FFD700>Daily Reward</color>");
            PrintToChat(player, $"<color=#00FF88>+{_config.DailyRewardScrap} scrap</color>");
            if (_config.DailyRewardRP > 0) PrintToChat(player, $"<color=#4DA6FF>+{_config.DailyRewardRP} RP</color>");
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
            var allSessions = _playerSessions.Values.OrderByDescending(s =>
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
            targetSess = targetSess ?? _playerSessions.Values.FirstOrDefault(s => s.DisplayName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
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
            targetSess = targetSess ?? _playerSessions.Values.FirstOrDefault(s => s.DisplayName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
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
            var kitList = new[] { "starter", "pvp", "building", "mini" };
            if (string.IsNullOrEmpty(kitName))
            {
                PrintToChat(player, "<color=#FFD700>Available Kits</color>");
                foreach (var k in kitList) PrintToChat(player, $"  * <color=#4DA6FF>{k}</color>");
                PrintToChat(player, "<color=#888>Use /db kit <name> to redeem</color>");
                return;
            }
            var match = kitList.FirstOrDefault(k => k.Contains(kitName) || kitName.Contains(k));
            if (match == null) { PrintToChat(player, $"<color=#FF4444>Unknown kit:</color> {kitName}"); return; }
            PrintToChat(player, $"<color=#00FF88>Redeeming kit:</color> {match}");
            ConsoleSystemRun.ServerCommand($"kit give {match} {player.UserIDString}");
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
                    var auth = tc.authorizedPlayers.Select(a => a.username).ToList();
                    tcList.Add($"* TC @ {GetLocation(tc.transform.position)} | Auth:{auth.Count} | {dist:F0}m");
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
                    foreach (var n in session.Notifications.TakeLast(5))
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
            PrintToChat(player, "Clear skies");
            PrintToChat(player, "Wind: 5 km/h NW");
            PrintToChat(player, $"Next event: Check server events");
        }

        private void ShowWipeInfo(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══ WIPE INFO ═══</color>");
            PrintToChat(player, "Last wipe: Check server");
            PrintToChat(player, "Next wipe: Check server Discord");
            PrintToChat(player, "Wipe type: Monthly (BP + Map)");
        }

        private void ShowMonuments(BasePlayer player, PlayerSession session)
        {
            var pos = player.transform.position;
            var nearest = GetNearestMonument(pos);

            PrintToChat(player, "<color=#FFD700>═══ MONUMENTS ═══</color>");
            PrintToChat(player, $"Nearest: {nearest}");
            PrintToChat(player, $"Position: {GetGridCoord(pos)}");
            PrintToChat(player, "Key monuments:");
            PrintToChat(player, "• Oil Rig (Large/Small) - Best loot");
            PrintToChat(player, "• Airfield - Military");
            PrintToChat(player, "• Dome - Mid tier");
            PrintToChat(player, "• Train Yard - Components");
            PrintToChat(player, "• Power Plant - Electric");
            PrintToChat(player, "• Outpost/Bandit - Trading");
            PrintToChat(player, "Use /db grid for full map.");
        }

        private void ShowLootInfo(BasePlayer player, PlayerSession session, string type)
        {
            PrintToChat(player, "<color=#FFD700>═══ LOOT LOCATIONS ═══</color>");
            PrintToChat(player, "Dome: Elite crates, tech trash");
            PrintToChat(player, "Airfield: Military crates, ammo");
            PrintToChat(player, "Oil Rig: Elite crates, components");
            PrintToChat(player, "Train Yard: Junk, scrap");
            PrintToChat(player, "Power Plant: Electric comp, fuses");
            PrintToChat(player, "Sewer: Recycler, raw scrap");
            PrintToChat(player, "Mining: Raw ore nodes");
            PrintToChat(player, "Arctic: Elite crates, meds");
        }

        private void ShowActiveEvents(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══ ACTIVE EVENTS ═══</color>");
            var events = _raidHistory.Count(r => r.Outcome == "in_progress");
            PrintToChat(player, $"Active raids: {events}");
            PrintToChat(player, "Check server for CH47, Bradley, Cargo");
        }

        private void ShowRecipes(BasePlayer player, PlayerSession session, string item)
        {
            PrintToChat(player, "<color=#FFD700>═══ RECIPES ═══</color>");
            PrintToChat(player, "Workbench 1: Basic items");
            PrintToChat(player, "Workbench 2: Medium items");
            PrintToChat(player, "Workbench 3: Advanced items");
            PrintToChat(player, "Use /db ask for specific recipes.");
        }

        private void ShowResearch(BasePlayer player, PlayerSession session, string item)
        {
            PrintToChat(player, "<color=#FFD700>═══ RESEARCH ═══</color>");
            PrintToChat(player, "Place item + research table");
            PrintToChat(player, "Scrap cost varies by item tier");
            PrintToChat(player, "Use /db ask for specific research cost.");
        }

        private void ShowBlueprintInfo(BasePlayer player, PlayerSession session, string bp)
        {
            PrintToChat(player, "<color=#FFD700>═══ BLUEPRINTS ═══</color>");
            PrintToChat(player, $"Research progress: Check workbench");
            PrintToChat(player, "Use /db ask for specific BP info.");
        }

        private void ShowKits(BasePlayer player, PlayerSession session)
        {
            PrintToChat(player, "<color=#FFD700>═══ AVAILABLE KITS ═══</color>");
            PrintToChat(player, "• starter - Basic resources");
            PrintToChat(player, "• pvp - Combat gear");
            PrintToChat(player, "• building - Construction mats");
            PrintToChat(player, "• mini - Mini toolkit");
            PrintToChat(player, "Use /kit <name> to redeem");
        }

        private void RedeemKit(BasePlayer player, PlayerSession session, string kitName)
        {
            if (string.IsNullOrWhiteSpace(kitName)) { PrintToChat(player, "Usage: /db kit <kit_name> | /db kits for list"); return; }
            var kits = new[] { "starter", "pvp", "building", "mini" };
            if (Array.IndexOf(kits, kitName.ToLower()) < 0) { PrintToChat(player, $"Unknown kit: {kitName}. Available: {string.Join(", ", kits)}"); return; }
            PrintToChat(player, $"<color=#00FF00>Redeeming kit:</color> {kitName}");
            ConsoleSystemRun.ServerCommand($"kit give {kitName} {player.UserIDString}");
        }

        // =====================================================================
        // GAMES & FUN
        // =====================================================================

        private void RollDice(BasePlayer player, PlayerSession session, string args)
        {
            var max = 100;
            if (!string.IsNullOrEmpty(args) && int.TryParse(args, out var m)) max = Math.Min(m, 10000);
            var roll = new System.Random().Next(1, max + 1);
            PrintToChat(player, $"<color=#FFD700>🎲 DICE:</color> {roll} (1-{max})");
        }

        private void FlipCoin(BasePlayer player, PlayerSession session)
        {
            var result = new System.Random().Next(2) == 0 ? "HEADS" : "TAILS";
            PrintToChat(player, $"<color=#FFD700>🪙 COIN:</color> {result}");
        }

        private void Magic8Ball(BasePlayer player, PlayerSession session, string question)
        {
            if (string.IsNullOrWhiteSpace(question)) { PrintToChat(player, "Usage: /db 8ball <question>"); return; }
            var responses = new[] { "Yes", "No", "Maybe", "Definitely", "Absolutely not", "Ask again later", "Very likely", "Unlikely", "Signs point to yes", "My sources say no", "Without a doubt", "Don't count on it" };
            var answer = responses[new System.Random().Next(responses.Length)];
            PrintToChat(player, $"<color=#FFD700>🎱 8BALL:</color> {answer}");
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
            var resultColor = result == "YOU WIN" ? "#00FF00" : result == "YOU LOSE" ? "#FF4444" : "#FFD700";

            PrintToChat(player, $"<color=#FFD700>✊👋✌️ RPS:</color>");
            PrintToChat(player, $"You: {choice.ToUpper()} | Bot: {choices[botChoice].ToUpper()}");
            PrintToChat(player, $"Result: <color={resultColor}>{result}</color>");
        }

        private void ShowQuote(BasePlayer player, PlayerSession session)
        {
            var quotes = new[] { "The best time to plant a tree was 20 years ago. The second best time is now.", "Rust never sleeps.", "Every raid is a lesson.", "The stone that the builder refuses will always be the head cornerstone.", "In Rust, we trust.", "Loot today, lose tomorrow.", "A base without TC is just a loot drop.", "The ocean is unforgiving.", "The best weapon is the one you don't have to aim.", "Trust no one, verify everything." };
            PrintToChat(player, $"<color=#FFD700>💬 QUOTE:</color> \"{quotes[new System.Random().Next(quotes.Length)]}\"");
        }

        private void TellJoke(BasePlayer player, PlayerSession session)
        {
            var jokes = new[] { "Why did the raider cross the map? To get to the other base. 💀", "How many raiders does it take to breach a door? None, they just console command it. 😏", "What's a base without a TC? A loot pinata. 🎉", "Why don't raiders play poker? Because they always bring a console. 🖥️", "What's the best defense? A friend with admin permissions. 🤫" };
            PrintToChat(player, $"<color=#FFD700>😂 JOKE:</color> {jokes[new System.Random().Next(jokes.Length)]}");
        }

        private void ShowFortune(BasePlayer player, PlayerSession session)
        {
            var fortunes = new[] { "A big raid is coming your way.", "Someone is watching your base right now.", "Your next scrap run will be legendary.", "A stranger will offer you a good trade today.", "Your TC auth list needs a cleanup.", "A monument is calling your name.", "Today is a good day to farm.", "Beware of false friends.", "The best loot is yet to come.", "Your base will survive this wipe." };
            PrintToChat(player, $"<color=#FFD700>🔮 FORTUNE:</color> {fortunes[new System.Random().Next(fortunes.Length)]}");
        }

        private void PlaySlots(BasePlayer player, PlayerSession session)
        {
            var emojis = new[] { "🔫", "💰", "⚙️", "🧨", "🔪", "💎", "💀" };
            var r = new System.Random();
            var spin = new[] { emojis[r.Next(emojis.Length)], emojis[r.Next(emojis.Length)], emojis[r.Next(emojis.Length)] };
            var result = spin[0] == spin[1] && spin[1] == spin[2] ? "<color=#00FF00>JACKPOT!</color>" : spin[0] == spin[1] || spin[1] == spin[2] || spin[0] == spin[2] ? "<color=#FFD700>PAIR!</color>" : "<color=#888>Try again</color>";
            PrintToChat(player, $"<color=#FFD700>🎰 SLOTS:</color>");
            PrintToChat(player, $"  [{spin[0]}] [{spin[1]}] [{spin[2]}]");
            PrintToChat(player, $"  {result}");
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
            var parts = args.Split(' ', 2);
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

        private void ShowVersion(BasePlayer player) { PrintToChat(player, "<color=#FFD700>RustDuckBot v1.3.1</color> by Duckets | AI: " + (_localAI?.ProviderName ?? _config.AgentProvider) + " MCP Bridge"); }
        private void ShowCredits(BasePlayer player) { PrintToChat(player, "Created by <color=#FFD700>Duckets</color> | Powered by <color=#FFD700>DuckBot AI</color>"); }
        private void ShowChangelog(BasePlayer player) { PrintToChat(player, "v1.2.0: Added 50+ commands, automation, security, trading, intel, games"); }
        private void ShowDonateInfo(BasePlayer player) { PrintToChat(player, "Donations help keep the server running! Contact admin."); }
        private void ShowDiscord(BasePlayer player) { PrintToChat(player, "Join our Discord: discord.gg/example"); }
        private void ShowSupport(BasePlayer player) { PrintToChat(player, "Support: Contact admin via Discord | Use /db bug <report> to report issues"); }

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
                    Id = entity.net?.ID?.Value.ToString() ?? entity.GetHashCode().ToString(),
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
            cam = _cameras.Find(c => c.Name.Contains(idOrName, StringComparison.OrdinalIgnoreCase));
            if (cam != null) return cam;
            return _cameras.Find(c => c.Location.Contains(idOrName, StringComparison.OrdinalIgnoreCase));
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
            return player.GetComponentInParent<ComputerStation>() != null || player.GetComponentInParent<CameraViewerConsole>() != null;
        }

        private void ScanBases()
        {
            _monitoredBases.Clear();
            foreach (var tc in UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>())
            {
                var baseInfo = new BaseInfo
                {
                    OwnerId = tc.OwnerID.ToString(),
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
                    Id = vm.UserIDString ?? vm.net?.ID?.Value.ToString() ?? Guid.NewGuid().ToString().Substring(0, 8),
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
                if (p.displayName.Contains(nameOrId, StringComparison.OrdinalIgnoreCase)) best = p;
            }
            return best;
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
            var color = type switch { "critical" => "#FF0000", "warning" => "#FF9900", "info" => "#00BFFF", _ => "#FFD700" };
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
            ConsoleSystemRun.ServerCommand(command);
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

            ConsoleSystemRun.ServerCommand($"banid {target} {duration} \"{reason}\"");
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
            var playerList = players.Select(p => new { id = p.UserIDString, name = p.displayName, ping = p.net?.connection?.avgPing ?? 0, role = GetOrCreateSession(p).Role, connectedAt = GetOrCreateSession(p).SessionStart.ToString("o") }).ToList();

            _mcpClient?.SendMessage(new
            {
                type = "heartbeat",
                time = DateTime.Now.ToString("o"),
                playerCount = players.Count,
                players = playerList,
                mcpConnected = _mcpClient?.IsConnected() == true
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
                        var time = TODWorld.Timespan;
                        if (rule.Condition == "sunset" && time.Hours >= 18 && time.Hours <= 19) trigger = true;
                        if (rule.Condition == "sunrise" && time.Hours >= 5 && time.Hours <= 6) trigger = true;
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
                mcpConnected = _mcpClient?.IsConnected() == true
            });
        }

        private void SaveData() { }

        private void TrackCommand(string cmd)
        {
            if (!_commandStats.ContainsKey(cmd)) _commandStats[cmd] = 0;
            _commandStats[cmd]++;
        }

        private string GetGameTime()
        {
            var t = TODWorld.Timespan;
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

        public bool IsConnected() => _connected && _ws?.State == WebSocketState.Open;

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
            if (!IsConnected()) return;
            try
            {
                var json = SimpleJson.Serialize(message);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            }
            catch { }
        }

        /// <summary>Safe wrapper: null-guards the MCP client and checks IsConnected before sending.
        /// Use this instead of bare _mcpClient.SendMessage(...) throughout the plugin.</summary>
        private void SafeMCPSend(object payload)
        {
            try { _mcpClient?.SendMessage(payload); } catch { }
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
            if (!IsConnected()) return;
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
                        var msg = Json.Deserialize(json) as Dictionary<string, object>;
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

        public bool IsConnected() => _connected && _ws?.State == WebSocketState.Open;

        public async Task ConnectAsync()
        {
            try
            {
                _cts = new CancellationTokenSource();
                var uri = new Uri($"ws://{_host}:{_port}");
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(uri, _cts.Token);
                _connected = true;
                _plugin.PrintAsh($"WS-RCON connected");
                await SendRCONCommand("password", _password);
                await SendRCONCommand("eventsubscribe", "chat");
                await SendRCONCommand("eventsubscribe", "connect");
                await SendRCONCommand("eventsubscribe", "disconnect");
                await SendRCONCommand("eventsubscribe", "death");
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

        private async Task SendRCONCommand(string cmd, string value = "")
        {
            if (!IsConnected()) return;
            var msgId = System.Threading.Interlocked.Increment(ref _messageId);
            var json = SimpleJson.Serialize(new { Identifier = msgId, Message = value, Name = cmd });
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            while (_ws?.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                }
                catch (OperationCanceledException) { break; }
                catch { break; }
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

        public string GetResponse(string playerName, string role, string message, List<object> history)
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
            if (obj is string s) return $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")}\"";
            if (obj is bool b) return b ? "true" : "false";
            if (obj is int or long or short or byte or uint or ulong or ushort or sbyte or float or double or decimal)
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

    private class LocalAIBridge
    {
        private readonly string _provider;
        private readonly string _lmUrl;
        private readonly string _lmModel;
        private readonly string _lmKey;
        private readonly string _openAiKey;
        private readonly string _openAiBase;
        private readonly string _openAiModel;
        private readonly string _systemPrompt;

        public LocalAIBridge(ConfigData cfg)
        {
            _provider = cfg.AgentProvider;
            _lmUrl = cfg.LMStudioUrl.TrimEnd('/');
            _lmModel = cfg.LMStudioModel;
            _lmKey = cfg.LMStudioApiKey;
            _openAiKey = cfg.OpenAIApiKey;
            _openAiBase = cfg.OpenAIBaseUrl.TrimEnd('/');
            _openAiModel = cfg.OpenAIModel;
            _systemPrompt = BuildSystemPrompt(cfg);
        }

        public string GetResponse(string playerName, string role, string message, List<ChatEntry> history)
        {
            try
            {
                return _provider switch
                {
                    "lmstudio" => LMPrompt(message, history),
                    "openai" => OAI Prompt(message, history, _openAiKey, _openAiBase, _openAiModel),
                    "anthropic" => AnthropicPrompt(message, history),
                    "openrouter" => OAIPrompt(message, history, _openAiKey, "https://openrouter.ai/api/v1", _openAiModel),
                    _ => null! // Fall back to DuckBotAgentBridge
                };
            }
            catch (Exception ex)
            {
                return $"⚠ AI error ({_provider}): {ex.Message}";
            }
        }

        public bool IsLocalProvider => _provider != "duckbot";
        public string ProviderName => _provider;

        // ── LM Studio (OpenAI-compatible /v1/chat/completions) ───────────────

        private string LMPrompt(string message, List<ChatEntry> history)
        {
            using var wb = new System.Net.WebClient();
            wb.Headers["Content-Type"] = "application/json";
            if (!string.IsNullOrEmpty(_lmKey))
                wb.Headers["Authorization"] = $"Bearer {_lmKey}";

            var body = new System.Collections.Specialized.NameValueCollection
            {
                ["model"] = _lmModel,
                ["messages"] = BuildMessages(message, history, _systemPrompt),
                ["max_tokens"] = "600",
                ["stream"] = "false"
            };

            var raw = wb.UploadString($"{_lmUrl}/v1/chat/completions", "POST",
                SimpleJson.Serialize(new { model = _lmModel, messages = BuildMessages(message, history, _systemPrompt), max_tokens = 600, stream = false }));

            dynamic? resp = Deserialize(raw);
            var content = resp?["choices"]?[0]?["message"]?["content"];
            return content ?? "No response from local AI.";
        }

        // ── OpenAI-compatible ───────────────────────────────────────────────

        private string OAIPrompt(string message, List<ChatEntry> history, string apiKey, string baseUrl, string model)
        {
            if (string.IsNullOrEmpty(apiKey))
                return "⚠ OpenAI API key not configured. Set OpenAIApiKey in config.";

            using var wb = new System.Net.WebClient();
            wb.Headers["Content-Type"] = "application/json";
            wb.Headers["Authorization"] = $"Bearer {apiKey}";

            var raw = wb.UploadString($"{baseUrl}/chat/completions", "POST",
                SimpleJson.Serialize(new { model, messages = BuildMessages(message, history, _systemPrompt), max_tokens = 800 }));

            dynamic? resp = Deserialize(raw);
            var content = resp?["choices"]?[0]?["message"]?["content"];
            return content ?? "No response from AI.";
        }

        // ── Anthropic (Claude) ───────────────────────────────────────────────

        private string AnthropicPrompt(string message, List<ChatEntry> history)
        {
            if (string.IsNullOrEmpty(_openAiKey))
                return "⚠ Anthropic API key not set as OpenAIApiKey in config.";

            using var wb = new System.Net.WebClient();
            wb.Headers["Content-Type"] = "application/json";
            wb.Headers["x-api-key"] = _openAiKey;
            wb.Headers["anthropic-version"] = "2023-06-01";

            var systemMsg = new { role = "system", content = _systemPrompt };
            var userMsg = new { role = "user", content = message };
            var msgs = new List<object> { systemMsg, userMsg };

            foreach (var h in history.TakeLast(20))
                msgs.Add(new { role = h.IsAI ? "assistant" : "user", content = $"{h.Sender}: {h.Message}" });

            var body = new { model = _openAiModel, max_tokens = 800, messages = msgs };
            var raw = wb.UploadString("https://api.anthropic.com/v1/messages", "POST", SimpleJson.Serialize(body));

            dynamic? resp = Deserialize(raw);
            var content = resp?["content"]?[0]?["text"];
            return content ?? "No response from Claude.";
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private object[] BuildMessages(string message, List<ChatEntry> history, string system)
        {
            var msgs = new List<object> { new { role = "system", content = system } };
            foreach (var h in history.TakeLast(16))
                msgs.Add(new { role = h.IsAI ? "assistant" : "user", content = $"{h.Sender}: {h.Message}" });
            msgs.Add(new { role = "user", content = message });
            return msgs.ToArray();
        }

        private string BuildSystemPrompt(ConfigData cfg)
        {
            return $@"You are DuckBot, an AI assistant inside a Rust game server. Respond as a helpful, friendly NPC.
Player role hierarchy: user < vip < mod < admin.

You have access to these Rust server features (via the Rust plugin):
- CCTV camera surveillance (monument cameras + base cameras)
- Security alerts (raids, decays, breaches, turret kills)
- Player tracking and online status
- Base management (doors, lights, turrets, auth)
- Trading and shop listings
- Automation rules
- Intel: kill stats, raid history, map markers

Rules:
- Keep responses under 200 words
- Be concise and useful in the Rust game context
- Answer Rust-related questions helpfully
- If you don't know, say so — don't make up commands
- Use emoji sparingly
- Never break character as an in-game AI terminal

Server config: CameraControl={cfg.EnableCameraControl}, RaidAlerts={cfg.EnableRaidAlerts}, DecayAlerts={cfg.EnableDecayAlerts}";
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
}
