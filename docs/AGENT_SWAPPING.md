# Agent Swapping Guide

RustDuckBot is intentionally agent-neutral. The Rust plugin owns in-game state and permissions; the MCP server exposes a stable tool surface; any MCP-capable agent can connect to that surface.

## Recommended Model

```text
Rust game server
  -> Oxide plugin: src/DuckBotMod.cs
  -> WebSocket bridge: ws://127.0.0.1:3851
  -> MCP server: mcp/dist/index.js
  -> agent: DuckBot, Codex, Claude Desktop, Cursor, or custom MCP client
```

## DuckBot / OpenClaw

Use the default plugin config:

```json
{
  "AgentProvider": "duckbot",
  "AgentConfig": "http://localhost:18797",
  "MCPServerHost": "127.0.0.1",
  "MCPServerPort": 3851
}
```

Then add the MCP server to your agent's MCP config:

```json
{
  "mcpServers": {
    "rust-duckbot": {
      "command": "node",
      "args": ["/full/path/to/rust-duckbot-mod/mcp/dist/index.js"]
    }
  }
}
```

## Codex

Use a local MCP config entry pointing to the built server:

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

The skill at `skills/rust-duckbot/SKILL.md` tells the agent how to map player intent to role-safe tools.

## Claude Desktop / Cursor / VS Code

The same MCP entry works in clients that support stdio MCP:

```json
{
  "mcpServers": {
    "rust-duckbot": {
      "command": "node",
      "args": ["/full/path/to/rust-duckbot-mod/mcp/dist/index.js"]
    }
  }
}
```

Restart the client after editing the config.

## Custom Agents

Custom agents have two choices:

- Use stdio MCP and call `tools/list` / `tools/call`.
- Connect to the WebSocket bridge and send MCP-like JSON-RPC messages.

Minimal WebSocket `tools/call` example:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "rust_server_status",
    "arguments": {}
  }
}
```

## Role Safety

Agents should always include one of:

- `requester_id`
- `requester_role`
- both, when available

Admin tools can additionally require `admin_token` when the server owner sets `RUST_DUCKBOT_ADMIN_TOKEN`.

## Swapping Rules

- Keep the MCP tool names stable.
- Do not bypass the plugin's role model.
- Keep responses short because Rust chat is cramped.
- Treat `rust_admin_command`, `rust_ban_player`, `rust_lockdown`, and automation mutations as high-risk operations.
- Prefer camera, player, alert, and server-status tools before guessing.
