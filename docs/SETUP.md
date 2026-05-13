# RustDuckBot Setup Guide

## Requirements

- Rust dedicated server with Oxide/uMod installed
- Node.js 18 or newer for the MCP bridge
- An MCP-capable agent such as DuckBot/OpenClaw, Codex, Claude Desktop, Cursor, or VS Code

## Install The Plugin

Oxide plugins are single `.cs` files. Copy the plugin with the class/plugin name:

```bash
cp src/DuckBotMod.cs /path/to/rust/server/oxide/plugins/RustDuckBot.cs
```

Reload from the Rust server console:

```text
o.reload RustDuckBot
```

The plugin registers:

```text
rustduckbot.use
rustduckbot.vip
rustduckbot.mod
rustduckbot.admin
rustduckbot.security
rustduckbot.automation
rustduckbot.trading
rustduckbot.intel
```

Grant access:

```text
oxide.grant user <steam_id> rustduckbot.use
oxide.grant user <steam_id> rustduckbot.vip
oxide.grant user <steam_id> rustduckbot.admin
```

## Build And Run The MCP Server

```bash
cd mcp
npm install
npm test
npm start
```

Defaults:

- MCP stdio is enabled for agents.
- The Rust plugin bridge listens on `ws://127.0.0.1:3851`.
- Demo state is seeded until the live server sends player/camera updates.

## Plugin Configuration

The C# plugin defaults to the same bridge port:

```json
{
  "MCPServerHost": "127.0.0.1",
  "MCPServerPort": 3851,
  "AgentProvider": "duckbot",
  "AgentConfig": "http://localhost:18797",
  "EnableCameraControl": true,
  "EnableAdminCommands": true,
  "EnableAutomation": true,
  "AdminSteamIds": []
}
```

If your Rust server runs on another machine, run the MCP bridge near the server or tunnel the bridge securely. Do not expose the bridge publicly without authentication and firewall rules.

## MCP Client Configuration

Use the built `dist/index.js` entrypoint:

```json
{
  "mcpServers": {
    "rust-duckbot": {
      "command": "node",
      "args": ["/full/path/to/rust-duckbot-mod/mcp/dist/index.js"],
      "env": {
        "RUST_DUCKBOT_BRIDGE_PORT": "3851",
        "RUST_DUCKBOT_ADMIN_TOKEN": "change-me"
      }
    }
  }
}
```

For local development without a Rust server, keep `RUST_DUCKBOT_SEED_DEMO=1`.

## Test In Game

```text
/db help
/db whoami
/db cameras
/db view cam_gate_front
/db ask hello duckbot
/db alerts
```

Admin smoke test:

```text
/db status
/db admin status
```

## Troubleshooting

### Plugin does not load

- Confirm the file is at `oxide/plugins/RustDuckBot.cs`.
- Check the server console after `o.reload RustDuckBot`.
- Verify Oxide/uMod is installed for the Rust server.

### MCP bridge disconnected

- Confirm `npm start` is running in `mcp`.
- Confirm the plugin config uses host `127.0.0.1` and port `3851` unless you changed the bridge.
- Check firewall or Docker/container networking if the server and MCP process are separated.

### Admin tools denied

- Grant `rustduckbot.admin`, add the Steam ID to `AdminSteamIds`, or use Rust's `authlevel`.
- If `RUST_DUCKBOT_ADMIN_TOKEN` is set, the MCP tool call must include `admin_token`.
- If `rust_admin_command` says a command is not whitelisted, add the first command word to `RUST_DUCKBOT_ALLOWED_COMMANDS`.
