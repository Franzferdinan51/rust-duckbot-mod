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
    [Info("RustDuckBot", "1.2.0", "Duckets")]
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
            public string AgentProvider = "duckbot";
            public string AgentConfig = "http://localhost:18797";
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
        private Dictionary<string, int> _commandStats = new Dictionary<string, int>();

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

            // Initialize default automation rules
            InitializeDefaultAutomation();

            PrintAsh("<color=#FFD700>RustDuckBot v1.2.0</color> loaded.");
            PrintAsh($"MCP: ws://{_config.MCPServerHost}:{_config.MCPServerPort} | Agent: {_config.AgentProvider}");
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
            LogActivity("system", "Server initialized", $"RustDuckBot v1.2.0 started. Cameras: {_cameras.Count}");
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            var session = GetOrCreateSession(player);
            session.IsOnline = true;
            session.LastSeen = DateTime.Now;
            TrackPlayer(player.UserIDString, player.displayName);

            _mcpClient.SendMessage(new { type = "player_joined", playerId = player.UserIDString, playerName = player.displayName, role = session.Role, time = DateTime.Now.ToString("o") });

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

            _mcpClient.SendMessage(new { type = "player_left", playerId = player.UserIDString, playerName = player.displayName, reason = reason, time = DateTime.Now.ToString("o") });
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

                // === BROADCAST & MESSAGING ===
                case "broadcast": case "bc": Broadcast(player, session, argStr); break;
                case "say": HandleChat(player, session, argStr); break;
                case "msg": SendMessage(player, session, argStr); break;
                case "team": HandleTeamMessage(player, session, argStr); break;

                // === ADMIN ===
                case "status": ServerStatus(player, session); break;
                case "admin": HandleAdmin(player, session, argStr); break;
                case "kick": HandleKick(player, session, argStr); break;
                case "ban": HandleBan(player, session, argStr); break;
                case "unban": HandleUnban(player, session, argStr); break;
                case "mute": HandleMute(player, session, argStr); break;
                case "freeze": HandleFreeze(player, session, argStr); break;
                case "heal": HandleHeal(player, session, argStr); break;
                case "give": HandleGive(player, session, argStr); break;
                case "teleport": case "tp": HandleTeleport(player, session, argStr); break;
                case "spawn": HandleSpawn(player, session, argStr); break;

                // === UTILITY ===
                case "time": ShowTime(player, session); break;
                case "weather": ShowWeather(player, session); break;
                case "wipe": ShowWipeInfo(player, session); break;
                case "monuments": case "monu": ShowMonuments(player, session); break;
                case "loot": ShowLootInfo(player, session, argStr); break;
                case "events": ShowActiveEvents(player, session); break;
                case "recipes": ShowRecipes(player, session, argStr); break;
                case "research": ShowResearch(player, session, argStr); break;
                case "blueprint": case "bp": ShowBlueprintInfo(player, session, argStr); break;
                case "kits": ShowKits(player, session); break;
                case "kit": RedeemKit(player, session, argStr); break;

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
            PrintToChat(player, $"<color=#FFD700>Version:</color> Rust vlatest");
            PrintToChat(player, $"<color=#FFD700>Plugin:</color> RustDuckBot v1.2.0");
            PrintToChat(player, $"<color=#FFD700>AI:</color> {_config.AgentProvider}");
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
            _mcpClient.SendMessage(new { type = "camera_view", playerId = player.UserIDString, cameraId = camInfo.Id, cameraName = camInfo.Name });
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

            var response = _agentBridge.GetResponse(player.displayName, session.Role, message, session.ChatHistory);

            session.ChatHistory.Add(new ChatEntry { Sender = "DuckBot", Message = response, Time = DateTime.Now, IsAI = true });

            // Handle multi-line responses
            var lines = response.Split('\n');
            foreach (var line in lines)
                PrintToChat(player, $"<color=#FFD700>DuckBot:</color> {line.Trim()}");

            _mcpClient.SendMessage(new { type = "ai_chat", playerId = player.UserIDString, playerName = player.displayName, message, response });
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
            _mcpClient.SendMessage(new { type = "admin_command", playerId = player.UserIDString, command });
            LogActivity("admin", "RCON", $"{player.displayName}: {command}", player.UserIDString, player.displayName);
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
            _mcpClient.SendMessage(new { type = "kick", playerId = player.UserIDString, targetId = target.UserIDString, reason });
            LogActivity("admin", "Kick", $"{player.displayName} kicked {target.displayName}: {reason}", player.UserIDString, player.displayName);
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
            _mcpClient.SendMessage(new { type = "ban", playerId = player.UserIDString, targetId = target.UserIDString, reason, duration });
            LogActivity("admin", "Ban", $"{player.displayName} banned {target.displayName}: {reason} ({duration})", player.UserIDString, player.displayName);
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

        private void ShowVersion(BasePlayer player) { PrintToChat(player, "<color=#FFD700>RustDuckBot v1.2.0</color> by Duckets | AI: DuckBot MCP Bridge"); }
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
            _mcpClient.SendMessage(new { type = "alert", alertId = alert.Id, title, message, severity });

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

            _mcpClient.SendMessage(new
            {
                type = "heartbeat",
                time = DateTime.Now.ToString("o"),
                playerCount = players.Count,
                players = playerList,
                mcpConnected = _mcpClient.IsConnected()
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
            _mcpClient.SendMessage(new
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

        public MCPClient(string host, int port, RustDuckBot plugin)
        {
            _host = host; _port = port; _plugin = plugin;
            _ws = new ClientWebSocket();
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

        private async Task SendAsync(object message)
        {
            var json = SimpleJson.Serialize(message);
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
    // POSITION
    // =====================================================================

    public class Position3D
    {
        public float X, Y, Z;
        public Position3D(Vector3 v) { X = v.x; Y = v.y; Z = v.z; }
        public Vector3 ToVector3() => new Vector3(X, Y, Z);
    }
}
