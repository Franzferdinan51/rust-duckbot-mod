# RustDuckBot

> **v1.3.3** — AI-powered in-game terminal for Rust. Runs with or without an AI agent.

RustDuckBot turns Rust's computer station into a full interactive AI terminal. When a player sits at a Computer Station item in-game, they get a CUI overlay panel — and every player gets 13 teleport commands, 163+ total commands, security alerts, CCTV controls, a built-in AI chat screen, and more. Powered by DuckBot, LM Studio, OpenAI, Claude, or OpenRouter — your choice.

The AI agent is **optional**. Configure LM Studio and the plugin responds locally with no external services needed.

---

## What's New in v1.3.x

- **🖥 Real Computer Station integration** — hooks into `OnComputerStationUse`, `OnCCTVCameraUsed`, `OnPlayerInput` to detect when a player physically sits at a Computer Station in-game, not just types chat commands
- **💬 In-terminal CUI chat screen** — full scrollable chat panel with timestamps, sender colors, input field, and quick-prompt buttons, rendered inside the game
- **🤖 5 AI backends** — DuckBot MCP, LM Studio (local), OpenAI, Anthropic/Claude, OpenRouter
- **🛰 Full teleport system** — 13 teleport commands including tpr/tpa request flow, sethome (5 per player), town, bandit, back, random TP, and coordinates
- **🛡 Null-safe MCP** — plugin no longer crashes when MCP is disconnected; all `_mcpClient` calls are null-guarded

---

## What It Does

### 🖥 Computer Station Integration
- CUI overlay appears when a player opens a Computer Station in-game
- Detects `OnComputerStationUse`, `OnCCTVCameraUsed`, `OnPlayerInput` game hooks
- Camera name auto-resolution via monument proximity (17 named monuments)
- Real-time CCTV cycle detection (JUMP = next, DUCK = prev)
- AI agent notified via MCP of: station open, camera viewed, CCTV cycle, monument camera events

### 💬 In-Terminal Chat
- Full CUI chat panel — scrollable history with timestamps and sender colors
- Native Rust input field — type and press SEND or Enter
- Quick-prompt buttons: "Who is online?", "Any raiders nearby?", "Show alerts", "Base status"
- Auto-refreshes after every AI response
- `/db chat` command opens the panel

### 🤖 Flexible AI — 5 Backends

| Provider | Config needed | Notes |
|---|---|---|
| `duckbot` | MCP URL | Original — DuckBot, OpenClaw, Codex, any MCP agent |
| `lmstudio` | `LMStudioUrl` + `LMStudioModel` | Local LLM, no API key needed |
| `openai` | `OpenAIApiKey` + `OpenAIBaseUrl` | Any OpenAI-compatible provider |
| `anthropic` | `OpenAIApiKey` as `x-api-key` | Claude via Anthropic API |
| `openrouter` | `OpenAIApiKey` | 100+ models, free tier available |

### 🚀 Full Teleport System (13 commands)
- **`tpr <player>`** — request to teleport TO a player (60s request timeout, auto-expires)
- **`tpa <player>`** — ask a player to teleport TO YOU
- **`tpc` / `accept`** — accept an incoming teleport request
- **`tpd` / `deny`** — deny an incoming request
- **`home [name]`** — teleport to a saved home (lists all if no name given)
- **`sethome [name]`** — save current position (default "main", max 5 per player)
- **`removehome <name>`** — delete a saved home
- **`town`** — instant teleport to Outpost (configurable coords)
- **`bandit`** — instant teleport to Bandit Camp (configurable coords)
- **`back`** — return to position before your last teleport (works after any TP command)
- **`rtele`** — random teleport to a safe random spot on the map
- **`pos` / `coords`** — show X/Y/Z, grid reference (e.g. "E-7"), nearest monument
- Warmup countdown (configurable) — moving during countdown cancels the teleport

### 🔐 Security & Base Management
- Role-based access: `user` → `vip` → `mod` → `admin`
- Alerts: raid (explosion detection), decay, breach, turret kills, access logs
- Base management: doors, lights, turrets, auth list, TC authorization
- Automation rules: time-based, raid-triggered, player-join triggers
- Decay monitoring with configurable warning hours

### 📊 Intel & Tracking
- Player tracking: online status, K/D, session time, first seen
- Raid history: location, outcome, attackers, defenders, loot collected
- Map markers: danger zones, patrol routes, base locations
- Grid map with coordinates
- Leaderboard and player stats

### 📡 MCP Bridge (optional)
- Plugin connects to MCP server over WebSocket (`ws://host:port`)
- MCP server: stdio for Claude Desktop, WebSocket for web agents
- Game events push to AI agent in real-time: alerts, raids, camera usage, player joins/leaves, AI chat
- When using local AI (`lmstudio`/`openai`/etc.), MCP is not required

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

### 2. Configure AI (edit `oxide/config/RustDuckBot.json`)

**LM Studio (local, no API key):**
```json
{
  "AgentProvider": "lmstudio",
  "LMStudioUrl": "http://localhost:1234",
  "LMStudioModel": "qwen3.5-9b"
}
```

**DuckBot MCP agent:**
```json
{
  "AgentProvider": "duckbot",
  "AgentConfig": "http://localhost:18797"
}
```

**OpenRouter (free tier):**
```json
{
  "AgentProvider": "openrouter",
  "OpenAIApiKey": "sk-or-...",
  "OpenAIBaseUrl": "https://openrouter.ai/api/v1",
  "OpenAIModel": "google/gemini-2.0-flash-exp:free"
}
```

### 3. Start MCP bridge (only needed for `duckbot` provider)
```bash
cd mcp && npm install && npm start
```

### 4. Use in-game
```
/db help           all commands
/db chat           open chat panel (also opens automatically at computer station)
/db cameras        list CCTV cameras
/db security       alert dashboard
/db ask <msg>      chat with AI
/db home           list your homes
/db sethome main   save a home
/db town            teleport to Outpost
/db bandit          teleport to Bandit Camp
/db pos             show your coordinates
/db radar           nearby players
/db lockdown        admin: lock down server
```

---

## All Commands (`/db <command>`)

| Category | Commands |
|---|---|
| **Help** | `help`, `terminal`, `info`, `whoami`, `credits`, `changelog`, `version` |
| **CCTV** | `cameras`, `view`, `control`, `ptz`, `recordings` |
| **Security** | `security`, `alerts`, `ack`, `access`, `scan`, `threat`, `lockdown`, `sos` |
| **Base** | `base`, `doors`, `door`, `lights`, `light`, `turrets`, `turret`, `decay`, `upkeep`, `auth`, `authorize` |
| **Chat** | `ask <msg>`, `chat` (opens CUI panel), `history` |
| **Teleport** | `tpr <player>`, `tpa <player>`, `tpc`/`accept`, `tpd`/`deny`, `home [name]`, `sethome [name]`, `removehome <name>`, `town`, `bandit`, `back`, `rtele`, `pos`/`coords` |
| **Intel** | `players`, `player <name>`, `track <name>`, `history`, `leaderboard`, `stats`, `radar`, `loot`, `map`, `markers`, `marker`, `near`, `deaths`, `kills`, `kd` |
| **Trading** | `shop`, `sell <item> <price>`, `buy <item>`, `price <item>`, `vending`, `listings`, `market` |
| **Automation** | `automation`, `auto <rule>` |
| **Fun** | `8ball`, `flip`, `roll`, `rps`, `joke`, `fortune`, `bet`, `quote`, `events`, `recipes`, `blueprint`, `research`, `news` |
| **Admin** | `admin`, `kick`, `ban`, `unban`, `mute`, `freeze`, `heal`, `give`, `teleport`/`tp`, `spawn`, `tpa`, `wipe`, `mark`, `bookmarks`, `bookmark`, `removealt` |
| **Utility** | `time`, `weather`, `wipe`, `monuments`/`monu`, `events`, `recipes`, `kits`, `info`, `server` |

---

## Permissions

```
rustduckbot.use          # Default — basic access
rustduckbot.vip          # Camera PTZ, security scans, alerts, teleport (tpr/tpa/home/town/bandit/etc.)
rustduckbot.mod          # Player lookup, kick, activity review, tpa (accept others)
rustduckbot.admin        # Ban, lockdown, spawn, give, automation changes, all admin commands
rustduckbot.security     # Security system access
rustduckbot.automation   # Automation rule management
rustduckbot.trading      # Trading and shop access
rustduckbot.intel        # Intel and tracking access
rustduckbot.teleport     # Teleport commands (tpr, tpa, home, town, bandit, etc.)

oxide.grant user <steam_id> rustduckbot.vip
oxide.grant user <steam_id> rustduckbot.mod
oxide.grant user <steam_id> rustduckbot.admin
```

---

## Configuration Reference

Full config is written to `oxide/config/RustDuckBot.json` on first load. Key fields:

### AI Settings
| Field | Default | Description |
|---|---|---|
| `AgentProvider` | `duckbot` | `duckbot` \| `lmstudio` \| `openai` \| `anthropic` \| `openrouter` |
| `AgentConfig` | `http://localhost:18797` | URL for DuckBot MCP agent |
| `LMStudioUrl` | `http://localhost:1234` | LM Studio HTTP URL |
| `LMStudioModel` | `local-model` | Model name (must match loaded model) |
| `OpenAIApiKey` | _(empty)_ | API key for OpenAI / Anthropic / OpenRouter |
| `OpenAIBaseUrl` | `https://api.openai.com/v1` | OpenAI-compatible base URL |
| `OpenAIModel` | `gpt-4o-mini` | Model name for OpenAI-compatible APIs |

### Teleport Settings
| Field | Default | Description |
|---|---|---|
| `MaxHomesPerPlayer` | `5` | Maximum homes per player |
| `TeleportRequestSeconds` | `60` | Seconds before a tpr/tpa request expires |
| `TeleportCooldownSeconds` | `120` | Seconds between teleport commands |
| `TeleportWarmupSeconds` | `10` | Countdown before tp (moving cancels) |
| `AllowTownTeleport` | `true` | Allow `/db town` |
| `AllowBanditTeleport` | `true` | Allow `/db bandit` |
| `TownCooldownMinutes` | `30` | Minutes between `/db town` uses |
| `BanditCooldownMinutes` | `60` | Minutes between `/db bandit` uses |
| `OutpostX/Y/Z` | `-94.5 / 3.0 / -55.4` | Outpost coordinates (customize for your map) |
| `BanditX/Y/Z` | `-222.6 / 2.0 / 6.7` | Bandit Camp coordinates (customize for your map) |

### General Settings
| Field | Default | Description |
|---|---|---|
| `EnableCameraControl` | `true` | CCTV PTZ and control features |
| `EnableAdminCommands` | `true` | Admin-only commands |
| `EnableRaidAlerts` | `true` | Explosion → raid alert |
| `EnableDecayAlerts` | `true` | Structure decay warnings |
| `EnableSmartAlerts` | `true` | AI-prioritized alert system |
| `MaxChatHistory` | `100` | AI chat history per player |
| `RaidAlertRadius` | `100` | Meters for nearby raid alerts |
| `DecayAlertHoursBefore` | `24` | Hours before decay to warn |

---

## File Structure
```
src/DuckBotMod.cs              # Oxide/uMod Rust plugin (C#, ~4,800 lines)
mcp/server/src/index.ts        # MCP server + WebSocket bridge (TypeScript)
mcp/test/index.test.mjs        # MCP behavior tests
skills/rust-duckbot/SKILL.md   # Agent-facing skill docs
docs/
  SETUP.md                     # Detailed setup guide
  API.md                       # API reference
  AGENT_SWAPPING.md            # How to swap between different AI agents
  OXIDE_PATTERNS.md            # Rust/Oxide development patterns + CCTV research
  RESEARCH.md                  # Research notes
```

---

## Environment Variables (MCP bridge)

| Variable | Default | Purpose |
|---|---|---|
| `RUST_DUCKBOT_BRIDGE_HOST` | `127.0.0.1` | WebSocket bridge host |
| `RUST_DUCKBOT_BRIDGE_PORT` | `3851` | WebSocket bridge port used by the plugin |
| `MCP_STDIO` | `1` | Enable stdio MCP transport (Claude Desktop) |
| `RUST_DUCKBOT_ADMIN_TOKEN` | _(none)_ | Extra secret for dangerous admin tools |
| `RUST_DUCKBOT_ALLOWED_COMMANDS` | _(safe list)_ | Comma-separated whitelist for `rust_admin_command` |
| `RUST_DUCKBOT_SEED_DEMO` | `1` | Set `0` to disable demo camera/player seed data |

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

## Safety Notes

- Keep the MCP bridge bound to **localhost** unless you know why it must be exposed
- Use `RUST_DUCKBOT_ADMIN_TOKEN` and a narrow `RUST_DUCKBOT_ALLOWED_COMMANDS` list on public servers
- Admin commands are role-checked at the plugin level
- Teleport warmup is designed to prevent abuse — players who move get cancelled, but determined admins can bypass with `mod` role
- Customise `OutpostX/Y/Z` and `BanditX/Y/Z` in config for custom maps