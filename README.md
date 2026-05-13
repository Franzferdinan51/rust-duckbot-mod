# RustDuckBot

> **v1.3.1** — AI-powered in-game terminal for Rust. Runs with or without an AI agent.

RustDuckBot turns Rust's computer station into a real interactive AI terminal. When a player sits at a computer station, they get a full CUI overlay panel with CCTV controls, security alerts, and a built-in AI chat screen — powered by your choice of DuckBot, LM Studio, OpenAI, Claude, or OpenRouter.

The AI agent is **optional**. If you just want local AI responses, configure LM Studio (or any OpenAI-compatible provider) and the plugin responds directly — no MCP server, no external agent needed.

---

## What It Does

### 🖥 Computer Station Integration
- **CUI Terminal Overlay** appears when player opens a Computer Station in-game
- **Real game hooks**: `OnComputerStationUse`, `OnCCTVCameraUsed`, `OnPlayerInput` — actually detects when player sits at the in-game item, not just chat commands
- Camera name auto-resolution via monument proximity
- AI notified via MCP of: station open, camera viewed, CCTV cycle, monument camera

### 💬 In-Terminal Chat Screen
- Full CUI chat panel — scrollable message history with timestamps and sender colors
- Native Rust input field — type and press SEND
- Quick-prompt buttons: "Who is online?", "Any raiders nearby?", "Show alerts", "Base status"
- Auto-refreshes on new AI response
- `/db chat` command opens the panel

### 🤖 Flexible AI — 5 Backend Options

| Provider | Command | What you need |
|---|---|---|
| `duckbot` | DuckBot MCP / OpenClaw agent | Any DuckBot-compatible agent on `AgentConfig` URL |
| `lmstudio` | Local LM Studio | `LMStudioUrl` (default `localhost:1234`) — no API key needed |
| `openai` | OpenAI API | `OpenAIApiKey` + `OpenAIBaseUrl` |
| `anthropic` | Claude (Anthropic) | `OpenAIApiKey` (used as `x-api-key`) |
| `openrouter` | 100+ models via OpenRouter | `OpenAIApiKey` — free tier works |

### 🔐 Full Security & Base System
- Role-based access: `user` → `vip` → `mod` → `admin`
- Alerts: raid detection, decay warnings, breach alerts, turret kills
- Base management: doors, lights, turrets, auth, decay monitoring
- Automation rules: time triggers, raid auto-alerts, welcome messages
- Intel: player tracking, kill stats, raid history, map markers, grid map

### 📡 MCP Bridge
- Plugin connects to MCP server over WebSocket
- MCP server exposes tools via stdio (Claude Desktop) and WebSocket (web agents)
- Game events (alerts, raids, camera usage) push to AI agent in real-time

---

## Quick Start

### 1. Copy the plugin
```bash
cp src/DuckBotMod.cs /path/to/rust/server/oxide/plugins/RustDuckBot.cs
```
Reload from server console:
```
oxide.reload RustDuckBot
```

### 2. Configure (oxide/config/RustDuckBot.json)
```json
{
  "AgentProvider": "lmstudio",
  "LMStudioUrl": "http://localhost:1234",
  "LMStudioModel": "qwen3.5-9b",
  "AgentConfig": "http://localhost:18797"
}
```
For full DuckBot agent support, also start the MCP bridge:
```bash
cd mcp && npm install && npm start
```

### 3. Use in-game
```
/db help         — all commands
/db chat         — open chat panel (or just sit at a computer station)
/db cameras      — list CCTV cameras
/db security     — alert dashboard
/db ask <msg>    — chat with AI
/db radar        — nearby players
/db lockdown     — admin: lock down server
```

---

## File Structure
```
src/DuckBotMod.cs              # Oxide/uMod Rust plugin (C#, ~4,400 lines)
mcp/server/src/index.ts        # MCP server + WebSocket bridge (TypeScript)
mcp/test/index.test.mjs        # MCP behavior tests
skills/rust-duckbot/SKILL.md   # Agent-facing skill docs
docs/
  SETUP.md                     # Detailed setup guide
  API.md                       # API reference
  AGENT_SWAPPING.md            # How to swap between different AI agents
  OXIDE_PATTERNS.md            # Rust/Oxide development patterns
  RESEARCH.md                  # Research notes
```

---

## All Commands (`/db <command>`)

| Category | Commands |
|---|---|
| **Help** | `help`, `terminal`, `info`, `whoami`, `credits`, `changelog` |
| **CCTV** | `cameras`, `view`, `control`, `ptz`, `recordings` |
| **Security** | `security`, `alerts`, `ack`, `access`, `scan`, `threat`, `lockdown`, `sos` |
| **Base** | `base`, `doors`, `door`, `lights`, `light`, `turrets`, `turret`, `decay`, `upkeep`, `auth`, `authorize` |
| **Chat** | `ask <msg>`, `chat` (opens CUI panel), `history` |
| **Intel** | `players`, `player <name>`, `track <name>`, `history`, `leaderboard`, `stats`, `radar`, `loot`, `map`, `markers`, `marker` |
| **Trading** | `shop`, `sell <item> <price>`, `buy <item>`, `price <item>`, `vending`, `listings` |
| **Automation** | `automation`, `auto <rule>` |
| **Fun** | `8ball`, `flip`, `roll`, `rps`, `joke`, `fortune`, `bet`, `quote`, `events`, `recipes` |
| **Admin** | `admin`, `kick`, `ban`, `unban`, `tpa`, `home`, `kits`, `wipe`, `mark`, `bookmarks`, `bookmark` |

---

## Permissions
```text
rustduckbot.use          # Default — basic access
rustduckbot.vip          # Camera PTZ, security scans, alerts
rustduckbot.mod          # Player lookup, kick, activity review
rustduckbot.admin        # Ban, lockdown, automation changes, admin commands
rustduckbot.security     # Security system access
rustduckbot.automation   # Automation rule management
rustduckbot.trading      # Trading and shop access
rustduckbot.intel        # Intel and tracking access

oxide.grant user <steam_id> rustduckbot.vip
oxide.grant user <steam_id> rustduckbot.mod
oxide.grant user <steam_id> rustduckbot.admin
```

---

## Environment Variables

| Variable | Default | Purpose |
|---|---|---|
| `RUST_DUCKBOT_BRIDGE_HOST` | `127.0.0.1` | WebSocket bridge host |
| `RUST_DUCKBOT_BRIDGE_PORT` | `3851` | WebSocket bridge port |
| `MCP_STDIO` | `1` | Enable stdio MCP transport |
| `RUST_DUCKBOT_ADMIN_TOKEN` | _(none)_ | Extra secret for dangerous admin tools |
| `RUST_DUCKBOT_ALLOWED_COMMANDS` | _(safe list)_ | Comma-separated whitelist for `rust_admin_command` |

---

## MCP Tool Surface (for agent integrations)

When running with a full AI agent via MCP, these tools are available:

| Tool | Description |
|---|---|
| `rust_chat_send` | Send a chat message to a player |
| `rust_view_camera` | Get current camera feed info |
| `rust_camera_control` | Control PTZ on a camera |
| `rust_player_list` | List online players |
| `rust_admin_command` | Run a whitelisted RCON/console command |
| `rust_kick_player` | Kick a player |
| `rust_ban_player` | Ban a player |
| `rust_lockdown` | Enable/disable server lockdown |
| `rust_alert_acknowledge` | Acknowledge an alert |
| `rust_map_marker` | Place a marker on the in-game map |
| `rust_automation_rule` | Create/get/delete automation rules |

---

## Docs
- [Setup Guide](docs/SETUP.md)
- [API Reference](docs/API.md)
- [Agent Swapping Guide](docs/AGENT_SWAPPING.md)
- [Oxide Patterns](docs/OXIDE_PATTERNS.md)
- [Research Notes](docs/RESEARCH.md)

---

## Safety

- Keep the MCP bridge bound to **localhost** unless you know why it must be exposed
- Use `RUST_DUCKBOT_ADMIN_TOKEN` and a narrow `RUST_DUCKBOT_ALLOWED_COMMANDS` list on public servers
- Admin commands are role-checked at the plugin level, not just by chat parsing