# RustDuckBot

> **v1.4.6** — AI-powered in-game terminal for Rust. Runs with or without an AI agent.

**⚠️ Note:** Due to Oxide.Compiler not exposing Rust game types (CCTVRCamera, ComputerStation, DamageType, etc.), all game hooks are disabled. The plugin loads and `/db` commands work, but real-time events (raids, door opens, player damage, etc.) won't trigger automatically. Use `/db help` for all commands.

RustDuckBot turns Rust's Computer Station into an interactive AI terminal. Every player gets 13 teleport commands, full moderation tools, AI chat (`/db ask`), and 176+ total commands via `/db`. Powered by DuckBot MCP, LM Studio, OpenAI, Claude, or OpenRouter.

**WindowsGSM is the primary supported host path.**

---

## What's New in v1.4.x

- **⚠️ Hooks disabled** — all Oxide game hooks disabled because `CCTVRCamera`, `ComputerStation`, `DamageType`, `Timer` constructor, and other Rust types are not available in Oxide.Compiler. The plugin still works for commands but won't auto-trigger on game events.
- **🖥 Computer Station UI** — CUI overlay for players at Computer Stations
- **🤖 5 AI backends** — DuckBot MCP, LM Studio (local), OpenAI, Anthropic/Claude, OpenRouter
- **🛰 Full teleport system** — 13 teleport commands including tpr/tpa request flow, sethome (5 per player), town, bandit, back, random TP, and coordinates
- **🛡 Moderation tools** — report system with queue, slay, respawn, player notes, admin whisper, mute list
- **💰 Economy & rewards** — daily scrap/RP reward, playtime tracker, top leaderboard (kills/playtime/KD)
- **⚔️ Combat intel** — death history, killer lookup, weapon stats (16 weapons), item comparison, loot finder
- **🏠 Building helpers** — TC scanner (200m), cupboard coverage checker, decay scan
- **🔔 Notification system** — night alerts, event subscriptions, notification list/clear
- **🔐 AI RCON access** — admin-gated MCP/RCON commands with an allowlist
- **🎁 AI kit tools** — agents can list kits and admin-gate kit grants through MCP
- **🎲 AI fun + helper tools** — dice rolls, 8-ball answers, and contextual Rust tips via MCP bridge

---

## WindowsGSM First Setup

This is the path to use with [WindowsGSM.RustOxideWithRustEdit](https://github.com/Joe90384/WindowsGSM.RustOxideWithRustEdit).

1. Install/start the RustOxideWithRustEdit server in WindowsGSM.
2. Use WindowsGSM's file browser or open the server files folder.
3. Copy both plugin files:
   ```text
   src\DuckBotMod.cs          -> serverfiles\oxide\plugins\RustDuckBot.cs
   src\DuckBotCommandShim.cs  -> serverfiles\oxide\plugins\RustDuckBotCommandShim.cs
   ```
   The shim is intentionally tiny. If the large RustDuckBot plugin fails to compile, `/db` should still answer and tell you to check the Oxide compiler logs instead of showing `Unknown command: db`.
4. Start the server and watch the Oxide console/logs for `RustDuckBot v1.4.5 loaded`.
5. Edit the generated config:
   ```text
   serverfiles\oxide\config\RustDuckBot.json
   ```
6. Reload both plugins:
   ```text
   oxide.reload RustDuckBot
   oxide.reload RustDuckBotCommandShim
   ```

The main plugin must still be named exactly:
```text
serverfiles\oxide\plugins\RustDuckBot.cs
```

If `/db help` returns `unknown command: db`, the plugin did not register with Oxide. `/db help` does not call LM Studio or any AI backend, so treat that as a plugin load/compile issue first. Check:

- `serverfiles\oxide\logs\RustDuckBot*.txt`
- WindowsGSM server console output
- `serverfiles\server.log`
- Whether the file is named exactly `RustDuckBot.cs`
- Whether Oxide/uMod compiled it without C# errors

v1.4.5 registers `/db` before optional AI/MCP/RCON startup, includes `[ChatCommand("db")]` and `[ChatCommand("duckbot")]` wrappers, adds a slash-command fallback hook, and ships `RustDuckBotCommandShim.cs` as a separate emergency command responder. The main plugin now uses the fully-qualified Rust plugin base class to avoid `RustPlugin` resolution failures, and the shim dynamically looks for `RustDuckBot`, `DuckBotMod`, and `RustDuckBotMod` before showing the fallback warning. If an optional startup step fails, `/db help` should still answer with a recovery-mode warning that includes the startup error from the Oxide console.

For LM Studio testing on the same Windows host:

```json
{
  "AgentProvider": "lmstudio",
  "LMStudioUrl": "http://127.0.0.1:1234",
  "LMStudioModel": "your-loaded-model",
  "LMStudioApiKey": ""
}
```

If LM Studio has API-key mode enabled, set `LMStudioApiKey`. Otherwise leave it blank. RustDuckBot normalizes `LMStudioUrl` to `/v1/chat/completions`, so both `http://127.0.0.1:1234` and `http://127.0.0.1:1234/v1` work.

---

## What It Does

### 🖥 Computer Station Integration (UI only — hooks disabled)
- CUI overlay appears when a player opens a Computer Station in-game
- Real-time CCTV detection is disabled (CCTVRCamera type not in Oxide compiler)
- Use `/db cameras` to list cameras manually
- Use `/db chat` to open the AI chat panel

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
| `lmstudio` | `LMStudioUrl` + `LMStudioModel` | Local LLM; API key optional if LM Studio requires one |
| `openai` | `OpenAIApiKey` + `OpenAIBaseUrl` | Any OpenAI-compatible provider |
| `anthropic` | `OpenAIApiKey` as `x-api-key` | Claude via Anthropic API |
| `openrouter` | `OpenAIApiKey` | 100+ models, free tier available |

### 🧠 MCP Agent Powers

Any interchangeable MCP-capable agent can use the tool surface. Regular player features and admin features are separated by role checks, with optional `RUST_DUCKBOT_ADMIN_TOKEN` for extra safety.

| Tool Area | Examples |
|---|---|
| Context | `rust_computer_context`, `rust_agent_status`, `rust_server_status` |
| Players | `rust_list_players`, `rust_find_player`, `rust_get_player_info` |
| Cameras | `rust_list_cameras`, `rust_view_camera`, `rust_control_camera`, `rust_get_camera_snapshot` |
| Security | `rust_list_alerts`, `rust_ack_alert`, `rust_security_scan`, `rust_lockdown` |
| Kits | `rust_list_kits`, `rust_give_kit` |
| Fun/Guidance | `rust_roll_dice`, `rust_8ball`, `rust_player_tip` |
| Economy | `rust_market_listings` |
| Map/Base | `rust_list_map_markers`, `rust_add_map_marker`, `rust_base_status` |
| Admin/RCON | `rust_admin_command`, `rust_rcon_command`, `rust_kick_player`, `rust_ban_player` |

---

## All Commands (`/db <command>`)

| Category | Commands |
|---|---|
| **Help** | `help`, `terminal`, `info`, `whoami`, `credits`, `changelog`, `version`, `h` |
| **CCTV** | `cameras`, `view`, `control`, `ptz`, `recordings` |
| **Security** | `security`, `alerts`, `ack`, `access`, `scan`, `threat`, `lockdown`, `sos` |
| **Base** | `base`, `doors`, `door`, `lights`, `light`, `turrets`, `turret`, `decay`, `upkeep`, `auth`, `authorize` |
| **Chat** | `ask <msg>`, `chat` (opens CUI panel), `history` |
| **Teleport** | `tpr <player>`, `tpa <player>`, `tpc`/`accept`, `tpd`/`deny`, `home [name]`, `sethome [name]`, `removehome [name]`, `town`, `bandit`, `back`, `rtele`, `pos`/`coords` |
| **Messaging** | `msg <player> <message>`, `ignore <player>`, `unignore <player>`, `afk`, `team <msg>`, `broadcast`/`bc` |
| **Moderation** | `report <player> <reason>`, `slay <player>`, `respawn <player>`, `notes <player> [view/add/remove/clear]`, `adminmsg <player> <msg>`, `mutelist`, `kick`, `ban`, `unban`, `freeze`, `heal`, `give` |
| **Intel** | `players`, `player <name>`, `track <name>`, `history`, `leaderboard`, `stats`, `radar`, `loot`, `map`, `markers`, `marker`, `near`, `deaths`, `kills`, `kd`, `death [player]`, `killer [player]`, `weapon <name>`, `compare <item1> <item2>` |
| **Trading** | `shop`, `sell <item> <price>`, `buy <item>`, `price <item>`, `vending`, `listings`, `market` |
| **Economy** | `daily` (claim reward), `playtime` (session/today/total), `top [kills|playtime|kd]` (leaderboard) |
| **Kits** | `kits` (list), `kit <name>` (redeem: starter/pvp/building/mini) |
| **Building** | `tc` (tool cupboard nearby), `cupsize` (cupboard coverage), `decaycheck` (structures within radius) |
| **Notifications** | `night` (toggle night alert), `notify`/`notifications` (list/clear), `subscribe <event>` (night/raid/decay/events) |
| **Utility** | `time`, `weather`, `wipe`, `monuments`/`monu`, `events`, `server`, `uptime` |
| **Automation** | `automation`, `auto <rule>` |
| **Fun** | `8ball`, `flip`, `roll`, `rps`, `joke`, `fortune`, `bet`, `quote`, `events`, `recipes`, `blueprint`, `research`, `news` |
| **Admin** | `admin <rcon_command>`, `teleport`/`tp`, `spawn`, `wipe`, `mark`, `bookmarks`, `bookmark`, `removealt` |

---

## Quick Start

### 1. Copy the plugins
```bash
cp src/DuckBotMod.cs /path/to/rust/server/oxide/plugins/RustDuckBot.cs
cp src/DuckBotCommandShim.cs /path/to/rust/server/oxide/plugins/RustDuckBotCommandShim.cs
```
Reload from server console: `oxide.reload RustDuckBot` and `oxide.reload RustDuckBotCommandShim`

**WindowsGSM:** open the server's file browser from WindowsGSM and copy both files:
```text
src\DuckBotMod.cs          -> serverfiles\oxide\plugins\RustDuckBot.cs
src\DuckBotCommandShim.cs  -> serverfiles\oxide\plugins\RustDuckBotCommandShim.cs
```
The WindowsGSM RustOxideWithRustEdit plugin starts `RustDedicated.exe` with `+rcon.web 1` and writes `server.cfg` in the server files directory. DuckBot config appears after first load at:
```text
serverfiles\oxide\config\RustDuckBot.json
```

### 2. Configure AI (edit `oxide/config/RustDuckBot.json`)

**LM Studio on the same Windows host:**
```json
{ "AgentProvider": "lmstudio", "LMStudioUrl": "http://127.0.0.1:1234", "LMStudioModel": "qwen3.5-9b", "LMStudioApiKey": "" }
```
RustDuckBot accepts either `http://127.0.0.1:1234` or `http://127.0.0.1:1234/v1`; it normalizes the request to `/v1/chat/completions`. Set `LMStudioApiKey` only if LM Studio's server is configured to require one.

**DuckBot MCP agent:**
```json
{ "AgentProvider": "duckbot", "AgentConfig": "http://localhost:18797" }
```

**OpenRouter (free tier):**
```json
{ "AgentProvider": "openrouter", "OpenAIApiKey": "sk-or-...", "OpenAIBaseUrl": "https://openrouter.ai/api/v1", "OpenAIModel": "google/gemini-2.0-flash-exp:free" }
```

### 3. Start MCP bridge (only needed for `duckbot` provider)
```bash
cd mcp && npm install && npm start
```

### 4. Enable RCON for AI admin tools

For WindowsGSM, set the same RCON password in `server.cfg` and `oxide/config/RustDuckBot.json`. The WindowsGSM plugin enables WebRCON (`+rcon.web 1`) for you.

```json
{
  "EnableWebSocketRCON": true,
  "RCONPort": 28016,
  "RCONPassword": "same-password-as-server.cfg",
  "AllowedRCONCommands": ["status", "serverinfo", "say", "global.say", "kick", "ban", "banid", "unban", "teleport", "teleport2me"]
}
```

For MCP/agent use, mirror the command allowlist:
```bash
set RUST_DUCKBOT_ADMIN_TOKEN=change-me
set RUST_DUCKBOT_ALLOWED_COMMANDS=status,serverinfo,say,global.say,kick,ban,banid,unban,teleport,teleport2me
```
On macOS/Linux use `export` instead of `set`. The agent tool is `rust_rcon_command` and still requires an admin player role plus the admin token when configured.

The agent can also use `rust_list_kits` and `rust_give_kit`; kit grants are admin-gated and arrive in the plugin as a `kit_give` bridge message.

Player-safe MCP tools include `rust_roll_dice`, `rust_8ball`, and `rust_player_tip`. They run locally in the MCP server and can optionally announce results through the existing `chat_send` bridge, so they work for minigames, giveaways, quick base advice, or new-player help without needing RCON.

### 5. Use in-game
```
/db help              all commands
/db chat              open chat panel (also auto-opens at computer station)
/db home             list your homes
/db sethome main      save a home
/db town / bandit     quick TP to Outpost / Bandit Camp
/db daily             claim daily scrap reward
/db top               server leaderboard
/db weapon ak47       weapon stats
/db tc                find nearby tool cupboards
```

---

## Permissions

```
rustduckbot.use          # Default — basic access
rustduckbot.vip          # Camera PTZ, alerts, teleport (tpr/tpa/home/town/bandit/etc.)
rustduckbot.mod          # Player lookup, kick, report review, notes, activity review
rustduckbot.admin        # Ban, slay, lockdown, spawn, give, automation changes
rustduckbot.security     # Security system access
rustduckbot.automation   # Automation rule management
rustduckbot.trading      # Trading and shop access
rustduckbot.intel        # Intel and tracking access
rustduckbot.teleport     # Teleport commands (tpr, tpa, home, town, bandit, etc.)
rustduckbot.moderation   # Report, slay, respawn, notes, adminmsg, mutelist
rustduckbot.afk          # AFK mode, night alert, event subscriptions
rustduckbot.economy      # Daily reward, playtime, leaderboard

oxide.grant user <steam_id> rustduckbot.vip
oxide.grant user <steam_id> rustduckbot.mod
oxide.grant user <steam_id> rustduckbot.admin
```

---

## Configuration Reference

Full config is written to `oxide/config/RustDuckBot.json` on first load.

### AI Settings
| Field | Default | Description |
|---|---|---|
| `AgentProvider` | `duckbot` | `duckbot` \| `lmstudio` \| `openai` \| `anthropic` \| `openrouter` |
| `AgentConfig` | `http://localhost:18797` | URL for DuckBot MCP agent |
| `LMStudioUrl` | `http://127.0.0.1:1234` | LM Studio HTTP URL; `/v1` optional |
| `LMStudioModel` | `local-model` | Model name |
| `LMStudioApiKey` | _(empty)_ | Optional Bearer token for LM Studio |
| `OpenAIApiKey` | _(empty)_ | API key for OpenAI / Anthropic / OpenRouter |
| `OpenAIBaseUrl` | `https://api.openai.com/v1` | OpenAI-compatible base URL |
| `OpenAIModel` | `gpt-4o-mini` | Model name |

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
| `OutpostX/Y/Z` | `-94.5 / 3.0 / -55.4` | Outpost coordinates |
| `BanditX/Y/Z` | `-222.6 / 2.0 / 6.7` | Bandit Camp coordinates |

### Moderation & AFK
| Field | Default | Description |
|---|---|---|
| `MaxPlayerNotes` | `20` | Max notes per player (mod+) |
| `EnableReportSystem` | `true` | Enable `/db report` command |
| `ReportCooldownMinutes` | `5` | Minutes between reports |
| `AFKTimeoutMinutes` | `10` | Minutes before AFK flag |
| `AFKKickMinutes` | `30` | Minutes before auto-kick |
| `AutoKickAFK` | `true` | Enable AFK auto-kick |

### Economy & Rewards
| Field | Default | Description |
|---|---|---|
| `EnableDailyReward` | `true` | Enable `/db daily` |
| `DailyRewardScrap` | `100` | Scrap per daily reward |
| `DailyRewardRP` | `20` | RP per daily reward |
| `PlaytimeBonusMinutes` | `60` | Minutes played to unlock daily |

### Notifications & Building
| Field | Default | Description |
|---|---|---|
| `MaxNotificationsPerPlayer` | `50` | Max stored notifications |
| `EnableNightAlert` | `true` | Enable night alert toggle |
| `DecayScanRadius` | `200` | Meters for `/db decaycheck` |
| `MaxPrivateMessageLength` | `500` | Max chars per PM |

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
| `EnableWebSocketRCON` | `true` | Allow plugin to connect to Rust WebRCON |
| `RCONPort` | `28016` | Rust WebRCON port |
| `RCONPassword` | _(empty)_ | Must match `+rcon.password` |
| `AllowedRCONCommands` | safe list | First-word allowlist for AI/MCP RCON commands |

---

## Version History

| Version | Commit | What |
|---|---|---|
| `318dde6` | — | Original 3,082-line build, 136 commands |
| `250c19d` | **v1.3.0** | Computer Station hooks + CUI overlay + MCP events |
| `fcdec32` | **v1.3.1** | CUI chat panel + LM Studio / OpenAI / Claude / OpenRouter |
| `daef122` | **v1.3.2** | 10 null-safe MCP calls, merged CanClientMove, version sync |
| `5d3a6b4` | **v1.3.3** | 13 teleport commands + warmup + home system + back + coords |
| `d9fb378` | **v1.4.0** | 30 new commands: moderation, economy, combat intel, building, notifications (176+ total commands) |
| `7fd7696` | **v1.4.1** | WindowsGSM `/db` load-path fixes, AI kits, RCON guardrails, dice/8-ball/tips MCP tools |
| `current` | **v1.4.5** | Fully-qualified Rust plugin base class, shim dynamic lookup for RustDuckBot/DuckBotMod names, emergency `/db` shim plugin |

---

## File Structure
```
src/DuckBotMod.cs              # Oxide/uMod Rust plugin (C#, 6,000+ lines, 176+ commands)
src/DuckBotCommandShim.cs      # Tiny emergency /db + /duckbot shim if the main plugin fails to load
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

## Environment Variables (MCP bridge)

| Variable | Default | Purpose |
|---|---|---|
| `RUST_DUCKBOT_BRIDGE_HOST` | `127.0.0.1` | WebSocket bridge host |
| `RUST_DUCKBOT_BRIDGE_PORT` | `3851` | WebSocket bridge port |
| `MCP_STDIO` | `1` | Enable stdio MCP transport |
| `RUST_DUCKBOT_ADMIN_TOKEN` | _(none)_ | Extra secret for admin tools |
| `RUST_DUCKBOT_ALLOWED_COMMANDS` | _(safe list)_ | Comma-separated whitelist for admin/RCON commands |

---

## Safety Notes

- Keep the MCP bridge bound to **localhost** unless you know why it must be exposed
- Use `RUST_DUCKBOT_ADMIN_TOKEN` and a narrow allowed commands list on public servers
- AI RCON commands are role-checked by MCP, token-checked when configured, and allowlist-checked again inside the plugin
- AI kit grants are admin-gated. Normal players should use `/db kit <name>` for their own cooldown/permission-limited kits
- In-game `/db admin <command>` is for trusted RustDuckBot admins only
- Teleport warmup prevents abuse — moving cancels, but mods/admins bypass it
- Customise `OutpostX/Y/Z` and `BanditX/Y/Z` in config for custom maps
- Report queue is in-memory — resets on plugin reload (persistent storage can be added via data files)

---

## Docs
- [Setup Guide](docs/SETUP.md)
- [API Reference](docs/API.md)
- [Agent Swapping Guide](docs/AGENT_SWAPPING.md)
- [Oxide Patterns](docs/OXIDE_PATTERNS.md)
- [Research Notes](docs/RESEARCH.md)
