# RustDuckBot

RustDuckBot turns Rust's computer station idea into an agent-aware in-game terminal. Players can chat with DuckBot, inspect cameras, review alerts, use base/security/trading/intel helpers, and let admins run controlled server actions through an MCP bridge.

The agent is interchangeable: DuckBot/OpenClaw, Codex, Claude Desktop, Cursor, or any MCP client can use the same tools.

## What Is Included

```
src/DuckBotMod.cs              # Oxide/uMod Rust plugin
mcp/server/src/index.ts        # MCP server plus WebSocket bridge
mcp/test/index.test.mjs        # MCP behavior tests
skills/rust-duckbot/SKILL.md   # Agent-facing skill
docs/                         # Setup, API, agent swapping, research notes
```

## Features

- Computer-station style `/db` and `/duckbot` terminal commands.
- Player roles: `user`, `vip`, `mod`, `admin`.
- Camera tools: list, view, PTZ control, snapshot requests.
- Player and server tools: online players, lookup, status, chat history.
- Security tools: alerts, acknowledgements, scans, activity log.
- Extra in-game systems: base status, map markers, automation rules, market listings.
- Admin tools with role checks, command whitelist, and optional admin token.
- MCP stdio transport for local agents plus WebSocket bridge for the Rust plugin.

## Quick Start

### 1. Install the Oxide plugin

Copy the plugin to your Rust server:

```bash
cp src/DuckBotMod.cs /path/to/rust/server/oxide/plugins/RustDuckBot.cs
```

Reload it from the Rust server console:

```text
o.reload RustDuckBot
```

### 2. Start the MCP bridge

```bash
cd mcp
npm install
npm test
npm start
```

By default the MCP server:

- exposes stdio MCP to your agent
- listens for the Rust plugin on `ws://127.0.0.1:3851`
- seeds demo camera/player data until the live plugin sends state

### 3. Configure an MCP client

Example MCP config:

```json
{
  "mcpServers": {
    "rust-duckbot": {
      "command": "node",
      "args": ["/full/path/to/rust-duckbot-mod/mcp/dist/index.js"],
      "env": {
        "RUST_DUCKBOT_BRIDGE_PORT": "3851"
      }
    }
  }
}
```

### 4. Use it in game

```text
/db help
/db cameras
/db view cam_gate_front
/db ask can you check the base?
/db alerts
/db status
```

## Role Model

| Role | Examples |
| --- | --- |
| `user` | Chat, view cameras, server context, market listings |
| `vip` | Camera PTZ, security scans, alerts, markers, base status |
| `mod` | Player lookup, activity review, kick |
| `admin` | Admin commands, ban, lockdown, automation changes |

Grant roles through Oxide permissions:

```text
oxide.grant user <steam_id> rustduckbot.vip
oxide.grant user <steam_id> rustduckbot.mod
oxide.grant user <steam_id> rustduckbot.admin
```

## Important Environment Variables

| Variable | Default | Purpose |
| --- | --- | --- |
| `RUST_DUCKBOT_BRIDGE_HOST` | `127.0.0.1` | WebSocket bridge host |
| `RUST_DUCKBOT_BRIDGE_PORT` | `3851` | WebSocket bridge port used by the plugin |
| `MCP_STDIO` | `1` | Set `0` to disable stdio MCP |
| `RUST_DUCKBOT_ADMIN_TOKEN` | unset | Optional extra secret for dangerous admin tools |
| `RUST_DUCKBOT_ALLOWED_COMMANDS` | safe starter list | Comma-separated whitelist for `rust_admin_command` |
| `RUST_DUCKBOT_SEED_DEMO` | `1` | Set `0` to disable demo state |

## Verification

```bash
cd mcp
npm test
```

The test suite builds TypeScript and checks the MCP tool surface, live Rust message ingestion, role gating, and queued plugin actions.

## Docs

- [Setup Guide](docs/SETUP.md)
- [API Reference](docs/API.md)
- [Agent Swapping](docs/AGENT_SWAPPING.md)
- [Research Notes](docs/RESEARCH.md)

## Safety Notes

RustDuckBot is powerful. Keep the bridge bound to localhost unless you know exactly why it must be exposed. Use `RUST_DUCKBOT_ADMIN_TOKEN` and a narrow `RUST_DUCKBOT_ALLOWED_COMMANDS` list for public or shared servers.
