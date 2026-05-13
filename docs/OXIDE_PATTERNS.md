# Rust Oxide/uMod C# Plugin Development Patterns

**DuckBot Mod Research — May 2026**

---

## Overview

This document covers plugin development patterns for Rust using the Oxide/uMod framework, with focus on:
- CCTV / Computer Station camera system APIs
- IPC between C# plugins and external processes (WebSocket, RCON, named pipes)
- Camera entity APIs in Rust
- Player entity APIs (chat, permissions, kicking)
- How existing plugins communicate with external tools

---

## 1. Camera System in Rust

### How CCTV Cameras Work in Vanilla Rust

Rust added a full CCTV system in the March 2020 CCTV Update. The key entities are:

- **`ComputerStation`** — A placeable entity that acts as a terminal. Players right-click it to enter camera-viewing mode.
- **`CCTVCamera`** — The actual camera entity (world model at `assets/prefabs/resource/cctv camera/cctv_camera.worldmodel.prefab`)

### Vanilla Usage (No Plugin Required)

1. Place a **Computer Station** in your base
2. Acquire a **camera tool** (`tool.camera` prefab) — found in loot or spawned
3. Use `/sc add` or right-click while holding the camera to place it on walls
4. Right-click the Computer Station to enter surveillance mode
5. Cycle cameras with **JUMP** or **DUCK**
6. Enter camera codes (like `oilrig` or `largeoilrig`) to view monument cameras

### Key Vanilla Prefabs

| Prefab Path | Purpose |
|---|---|
| `assets/prefabs/resource/cctv camera/cctv_camera.worldmodel.prefab` | The CCTV camera world model |
| `assets/prefabs/tools/camera/camera.item.prefab` | The camera tool item |
| `assets/prefabs/tools/camera/tool_camera.prefab` | Camera deployable |
| `assets/content/properties/lootspawn/generated/items/res/cctv.camera.asset` | Loot spawn asset |

### Existing Plugins — SecurityCameras (k1lly0u / CHAOS)

The [SecurityCameras plugin](https://chaoscode.io/resources/securitycameras.90/) by k1lly0u is a well-known community plugin that demonstrates camera system integration.

**Features:**
- Places player-deployable cameras around a base
- Cameras link to the Computer Station via building ownership (tool cupboard)
- Public cameras can be placed anywhere and registered to any terminal by ID
- Players cycle cameras with JUMP/DUCK
- Camera overlay UI with configurable image URL
- Permission-based limits: `securitycameras.use` (default 4), `securitycameras.pro` (10)
- Chat commands: `/sc`, `/sc add`, `/sc add public <terminalID>`, `/sc name`, `/sc remove`

**Architecture highlights:**
- Cameras are placed using the vanilla camera item in the player's hands
- Camera registration is tied to the base's Tool Cupboard authorization
- Camera names stored in plugin data, displayed in RustNET console overlay

---

## 2. Computer Station (Camera Station) Access

The Computer Station is a `BaseEntity` subclass in Rust's Unity/C# codebase. In Oxide plugins, you interact with it through the standard entity hooks.

### Finding Computer Stations

```csharp
// Find all ComputerStation entities in the world
List<BaseEntity> stations = new List<BaseEntity>();
foreach (var entity in BaseEntity.allEntityList)
{
    if (entity.ShortPrefabName == "computerstation" || entity.PrefabName.Contains("computer"))
    {
        stations.Add(entity);
    }
}
```

### Checking if a Player is Using a Computer Station

Players enter "camera view mode" when using a Computer Station. You can detect this via:

```csharp
// In any hook, check if player is currently viewing through a camera
bool IsViewingCamera(BasePlayer player)
{
    // Check if player is in an observe mode or CCTV view
    // CameraViewerConsole is the Unity component that handles CCTV viewing
    return player.GetComponent<CameraViewerConsole>() != null;
}
```

### Sending Chat to a Player Using a Camera Station

When a player is using a Computer Station, they are in an "observer" state. Chat still works normally.

```csharp
// Send a chat message to a player
player.ChatMessage("Your message here");

// Or via console command
player.SendConsoleCommand("chat.add", new object[] { 0, "Message" });
```

---

## 3. IPC Between C# Plugin and External Process

This is a core question for DuckBot: how does a Rust plugin communicate with an external process (like a Python Discord bot)?

### Option A: WebSocket RCON (Recommended)

Rust has built-in WebSocket RCON support. This is the most common approach for external integrations.

**Enabling WebSocket RCON in server.cfg:**
```
rcon.web 1
rcon.port 28016
rcon.password "your-password"
```

**How it works:**
- Rust's built-in RCON uses WebSocket protocol
- Connect via `ws://server:28016` with the password as the first message (or use the standard RCON protocol over WebSocket)
- Send console commands and receive responses
- Subscribe to game events (chat, connections, etc.)

**Rust RCON libraries by language:**
- **Java**: [MrGraversen/rust-rcon](https://github.com/MrGraversen/rust-rcon) — Async, fault-tolerant, translates game events into POJO events
- **Python**: `pip install rust-rcon` (various async libraries on PyPI)
- **Node.js**: `npm install rust-rcon` or `npm install @skinwalker/rust-rcon`

**Example — Issuing commands via RCON:**
```csharp
// In a plugin, you can call RCON.Broadcast or RCON.ServerCommand:
RCON.ServerCommand("say Hello from DuckBot!");
```

**Example — Receiving events in external process:**

Most RCON libraries work as clients connecting TO the Rust server (not listening on the server). They subscribe to events like `OnPlayerChat`, `OnPlayerConnect`, etc.

```java
// MrGraversen rust-rcon example (Java)
RustRconClient client = RustRconClient.connect("localhost", 28016, "password");
client.onPlayerChat().subscribe(event -> {
    System.out.println(event.getSteamId() + ": " + event.getMessage());
});
client.onPlayerConnected().subscribe(event -> {
    System.out.println("Player connected: " + event.getSteamId());
});
```

**Plugin → External Process:**
If your plugin needs to PUSH data to an external process (not just respond to RCON queries), you have two approaches:

1. **Plugin opens a WebSocket client** — Your plugin acts as a WebSocket client connecting to your external server
2. **Plugin opens a TCP/named pipe** — Your plugin listens for connections from your external process

### Option B: Oxide WebSocket Extension

[mattwilshire/Oxide.Ext.WebSocket](https://github.com/mattwilshire/Oxide.Ext.WebSocket) is an Oxide extension that adds WebSocket server capability directly to Oxide.

```csharp
// This is an EXTENSION (native C++ module), not a pure plugin
// It allows plugins to:
// - Create WebSocket servers
// - Receive messages and call hooked plugin methods
// - Push messages to connected clients
```

This approach lets your plugin act as a WebSocket SERVER that external processes connect to. This is cleaner for the plugin → external direction.

### Option C: Plugin WebRequest (Outbound HTTP)

Oxide plugins can make outbound HTTP requests via `webrequest.EnsureHead` or similar. This lets your plugin call webhooks or HTTP APIs on your external service.

```csharp
// Simple HTTP POST from plugin to external service
webrequest.EnsureHead("http://your-server:port/webhook", (code, response) => {
    if (code != 0)
        PrintWarning($"Webhook failed: {code}");
});
```

### Option D: Named Pipes (Windows)

For Windows servers, named pipes provide low-latency IPC between processes on the same machine.

```csharp
// In your plugin (C#)
using System.IO;
using System.IO.Pipes;

void SendToPipe(string message)
{
    using (var client = new NamedPipeClientStream(".", "DuckBotPipe", PipeDirection.Out))
    {
        client.Connect(1000);
        using (var writer = new StreamWriter(client))
        {
            writer.WriteLine(message);
        }
    }
}
```

This requires a companion process (Python, etc.) running on the same machine as the Rust server, listening on the named pipe.

---

## 4. Camera Entity APIs

### Spawning a CCTV Camera

```csharp
// Via ItemManager (spawns the item in world)
Item camera = ItemManager.CreateByName("cctv.camera", 1, 0uL);
camera.CreateWorldObject(player.eyes.position, default(Quaternion));
```

### Camera-Related Prefabs

```
assets/prefabs/resource/cctv camera/cctv_camera.worldmodel.prefab
assets/prefabs/tools/camera/camera.item.prefab
```

### Getting Camera Position/Rotation

```csharp
// All entities in Rust are BaseEntity / BaseCombatEntity
// You can get their world position and rotation:
Vector3 position = cameraEntity.transform.position;
Quaternion rotation = cameraEntity.transform.rotation;

// For camera specifically, the forward direction is the view direction
Vector3 lookDir = cameraEntity.transform.forward;
```

### CCTV Camera Entity (from Oxide perspective)

In Oxide's abstraction, CCTV cameras are regular `BaseEntity` objects. You can find them via `BaseEntity.allEntityList` and filter by prefab name.

```csharp
// Find all CCTV camera entities
var allCameras = BaseEntity.allEntityList
    .Where(e => e.ShortPrefabName.Contains("cctv") || e.PrefabName.Contains("camera"))
    .ToList();
```

---

## 5. Player Entity APIs

### BasePlayer — Core Reference

`BasePlayer` is the main player entity class in Rust. Key properties and methods:

```csharp
// Connection
player.userID        // SteamID as ulong
player.displayName  // Player's in-game name
player.IPlayer       // Oxide's platform-agnostic IPlayer interface
player.IsConnected() // bool
player.IsAdmin       // bool (server admin)
player.IsSleeping()  // bool
player.IsDead()      // bool

// Position
player.transform.position  // Vector3
player.eyes.position       // Vector3 (eye level)
player.eyes.rotation       // Quaternion (view direction)

// Chat & Commands
player.ChatMessage(string message)
player.SendConsoleCommand(string command, params object[] args)

// Inventory
player.inventory.GiveItem(Item item)
player.inventory.FindItemByUID(ulong uid)
player.inventory.containerMain   // Main inventory
player.inventory.containerWear   // Wear slot
player.inventory.containerBelt   // Belt/hotbar

// Building Authorization
player.CanBuild()           // bool - can place building blocks
player.IsBuildingAuthed()   // bool - has tool cupboard auth
player.OwnerID()            // ulong - SteamID of TC owner

// Movement State
player.IsFlying()          // bool
player.IsSwimming()        // bool
player.IsOnGround()        // bool
```

### Chat Commands

```csharp
using Oxide.Core.Plugins;

[ChatCommand("duckbot")]
void CmdDuckBot(BasePlayer player, string command, string[] args)
{
    if (player == null) return;
    player.ChatMessage("Hello from DuckBot!");
}
```

### Console Commands

```csharp
[ConsoleCommand("duckbot.status")]
void CmdDuckBotStatus(ConsoleSystem.Arg arg)
{
    BasePlayer player = arg.Player();
    string output = "DuckBot is running!";
    if (player != null)
        player.ConsoleMessage(output);
    else
        PrintToConsole(arg, output);
}
```

### Permissions

```csharp
// Check permission
bool canUse = permission.UserHasPermission(player.UserIDString, "duckbot.use");

// Grant/revoke via code (requires the Permissions plugin or Oxide's built-in)
permission.GrantPermission(player.UserIDString, "duckbot.admin");
```

### Kicking/Banning

```csharp
// Kick player
player.Kick("Kicked by DuckBot");

// Ban (Oxide)
permission.BanPlayer(player, "Reason", 3600); // 3600 seconds

// Unban
permission.UnbanPlayer(player);
```

### Sending Rich UI (CUI)

Oxide uses Rust's native CUI system for in-game UI:

```csharp
using Oxide.Game.Rust.Cui;

class MyUI
{
    static CuiElementContainer CreateOverlay()
    {
        var container = new CuiElementContainer();
        container.Add(new CuiElement
        {
            Name = "duckbot.panel",
            Parent = "Overlay",
            Components =
            {
                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0.3 0.3" },
                new CuiImageComponent { Color = "0.1 0.1 0.1 0.9" }
            }
        });
        container.Add(new CuiElement
        {
            Name = "duckbot.label",
            Parent = "duckbot.panel",
            Components =
            {
                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                new CuiTextComponent { Text = "DuckBot Panel", FontSize = 14, Align = TextAnchor.MiddleCenter }
            }
        });
        return container;
    }

    static void ShowUI(BasePlayer player)
    {
        CuiHelper.DestroyUi(player, "duckbot.panel");
        CuiHelper.AddUi(player, CreateOverlay());
    }

    static void HideUI(BasePlayer player)
    {
        CuiHelper.DestroyUi(player, "duckbot.panel");
    }
}
```

---

## 6. How Existing Plugins Communicate with External Tools

### Pattern 1: RCON as the Bridge

Most external Rust tools (stats trackers, admin dashboards, Discord bots) use RCON as the communication channel:

1. External process connects as an RCON CLIENT to the Rust server
2. RCON broadcasts events (chat, connect, disconnect) to the client
3. The client can send console commands back to the server

This is the standard, well-supported approach. The main limitation is **bidirectional**: RCON is request-response. The server doesn't push to the client unless the client is subscribed to event streams (which Rust RCON does support via its WebSocket protocol).

### Pattern 2: Plugin HTTP Webhook

Simpler plugins often just `webrequest.EnsureHead` to a web endpoint:

```csharp
webrequest.EnsureHead($"https://api.duckbot.local/event?type=chat&player={player.displayName}&msg={message}");
```

The external service runs a simple HTTP server and receives pings from the plugin.

### Pattern 3: Plugin WebSocket Client

More sophisticated plugins include a WebSocket client that maintains a persistent connection to an external service:

```csharp
// Pseudocode for plugin WebSocket client
class DuckBotBridge
{
    WebSocket ws;
    async void Connect(string url)
    {
        ws = new WebSocket(url);
        ws.OnMessage += HandleMessage;
        await ws.ConnectAsync();
    }

    void SendEvent(string eventType, object data)
    {
        ws.Send(Json.Serialize(new { type = eventType, data = data }));
    }
}
```

### Pattern 4: Oxide WebSocket Extension

The [Oxide.WebSocket extension](https://github.com/mattwilshire/Oxide.Ext.WebSocket) allows the plugin to BE a WebSocket server. External clients (your Python bot) connect as WebSocket clients. This is the cleanest pattern for real-time bidirectional communication.

---

## 7. Plugin Structure Reference

### Minimal Plugin Template

```csharp
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("DuckBot", "DuckTeam", "0.1.0")]
    [Description("DuckBot - Discord integration for Rust")]
    class DuckBot : RustPlugin
    {
        [PluginReference] Plugin DiscordBridge;

        protected override void LoadConfig()
        {
            base.LoadConfig();
            // Load config here
        }

        protected override void LoadDefaultConfig()
        {
            // Set defaults here
        }

        void Init()
        {
            // Called when plugin loads
        }

        void OnServerInitialized()
        {
            // Called after server fully starts — good time to connect to external services
        }

        [ChatCommand("duckbot")]
        void CmdDuckBot(BasePlayer player, string command, string[] args)
        {
            player.ChatMessage("DuckBot ready!");
        }

        object OnPlayerChat(BasePlayer player, string message)
        {
            // Return non-null to block the message
            // Send to Discord here
            return null; // allow normal chat
        }

        void OnPlayerConnected(BasePlayer player)
        {
            // Player joined
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            // Player left
        }

        void Unload()
        {
            // Plugin unloading — close connections, save data
        }
    }
}
```

### csproj Reference

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>DuckBot</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Oxide.Rust" Version="*" />
    <Reference Include="Oxide.Rust" />
    <Reference Include="Oxide.Core" />
    <Reference Include="UnityEngine.CoreModule" />
  </ItemGroup>
</Project>
```

Download Oxide assemblies from the [releases page](https://github.com/OxideMod/Oxide.Rust/releases) and reference them locally.

---

## 8. Key Resources

| Resource | URL |
|---|---|
| uMod Rust Documentation | https://umod.org/documentation/games/rust |
| Oxide Mod Downloads | https://github.com/OxideMod/Oxide.Rust/releases |
| Plugin Repository (Calytic) | https://github.com/Calytic/oxideplugins |
| Plugin Repository (john-clark) | https://github.com/john-clark/rust-oxide-umod |
| Rust RCON (Java) | https://github.com/MrGraversen/rust-rcon |
| Oxide WebSocket Extension | https://github.com/mattwilshire/Oxide.Ext.WebSocket |
| Plugin Dev Guide | https://github.com/kwamaking/rust-plugin-development |
| Rust CCTV Update Announcement | https://rust.facepunch.com/news/cctv-update |

---

## 9. DuckBot Integration Recommendation

For DuckBot (Python Discord bot ↔ Rust server), recommended architecture:

1. **Enable Rust WebSocket RCON** (`rcon.web 1` in server.cfg)
2. **Python side**: Use `rust-rcon` Python library to connect as an RCON client
3. **Events flow**: Rust → RCON WebSocket → Python → Discord (automatic via event subscriptions)
4. **Commands flow**: Discord → Python → RCON → `RCON.ServerCommand()` in a companion plugin (or direct from Python)

The Python `rust-rcon` async library connects to the Rust server's RCON WebSocket port, subscribes to events, and can issue console commands back. No custom Rust plugin strictly needed for basic Discord integration — Python can directly send commands like `say`, `kick`, etc.

For deeper integration (plugin-controlled events, custom data), a thin Oxide plugin that opens a WebSocket client to your Python service would provide push-based communication.