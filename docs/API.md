# RustDuckBot API Reference

RustDuckBot has two integration surfaces:

- The Oxide plugin exposes in-game `/db` and `/duckbot` commands.
- The MCP server exposes tools over stdio and accepts live Rust plugin events over WebSocket.

## WebSocket Bridge

Default bridge:

```text
ws://127.0.0.1:3851
```

The plugin sends events like:

```json
{ "type": "rust_hello", "version": "1.2.0", "plugin": "RustDuckBot" }
{ "type": "heartbeat", "playerCount": 2, "players": [{ "id": "7656...", "name": "Duckets", "role": "admin" }] }
{ "type": "camera_update", "cameras": [{ "id": "cam_gate_front", "name": "Main Gate", "online": true, "hasPower": true, "isPTZ": true }] }
{ "type": "player_chat", "playerId": "7656...", "playerName": "Duckets", "role": "admin", "message": "hello" }
{ "type": "alert", "alertId": "abc123", "severity": "high", "title": "Explosion", "message": "Explosion near base" }
```

The MCP server sends actions back to the plugin:

```json
{ "type": "view_camera_request", "camera_id": "cam_gate_front", "player_id": "7656..." }
{ "type": "camera_control", "camera_id": "cam_gate_front", "action": "left", "player_id": "7656..." }
{ "type": "chat_send", "target": "global", "sender": "DuckBot", "message": "Hello" }
{ "type": "admin_command", "command": "status", "admin_name": "Duckets" }
```

## Role Checks

Dangerous tools use `requester_id`, `requester_role`, or the known player state from the bridge.

| Role | MCP Access |
| --- | --- |
| `user` | Chat, camera view, player lookup, server status |
| `vip` | Camera control, alert ack, security scan, markers |
| `mod` | Activity logs, kick |
| `admin` | Admin command, ban, lockdown, automation mutation |

If `RUST_DUCKBOT_ADMIN_TOKEN` is configured, admin-level MCP calls must include `admin_token`.

## MCP Tools

### Context

`rust_computer_context`

```json
{ "player_id": "76561198000000001" }
```

Returns role, available capability groups, bridge state, camera count, player count, and active alerts.

### Cameras

`rust_list_cameras`

```json
{ "player_id": "76561198000000001" }
```

`rust_view_camera`

```json
{ "camera_id": "cam_gate_front", "player_id": "76561198000000001" }
```

`rust_control_camera` requires `vip+`.

```json
{
  "camera_id": "cam_gate_front",
  "action": "left",
  "player_id": "76561198000000001",
  "requester_role": "vip"
}
```

Actions: `left`, `right`, `up`, `down`, `zoom`, `zoom_in`, `zoom_out`, `reset`, `home`.

`rust_get_camera_snapshot`

```json
{ "camera_id": "cam_gate_front", "player_id": "76561198000000001" }
```

### Players

`rust_list_players`

```json
{ "role_filter": "all" }
```

`rust_get_player_info`

```json
{ "player_id": "76561198000000001" }
```

`rust_find_player`

```json
{ "pattern": "duck" }
```

### Chat

`rust_chat_send`

```json
{ "message": "Server restart in 5 minutes", "target": "global", "sender": "DuckBot" }
```

`rust_chat_history`

```json
{ "player_id": "76561198000000001", "limit": 20 }
```

### Server And Security

`rust_server_status`

```json
{}
```

`rust_list_alerts`

```json
{ "include_acknowledged": false, "severity": "high" }
```

`rust_ack_alert` requires `vip+`.

```json
{ "alert_id": "abc123", "requester_id": "76561198000000001" }
```

`rust_security_scan` requires `vip+`.

```json
{ "requester_id": "76561198000000001", "radius": 100 }
```

`rust_list_activity` requires `mod+` when no `player_id` filter is supplied.

```json
{ "category": "admin", "limit": 50, "requester_role": "mod" }
```

### In-Game Computer Features

`rust_list_map_markers`

```json
{ "player_id": "76561198000000001" }
```

`rust_add_map_marker` requires `vip+`.

```json
{
  "name": "raid base",
  "position": "E12",
  "color": "red",
  "icon": "danger",
  "requester_role": "vip"
}
```

`rust_list_automation_rules`

```json
{}
```

`rust_set_automation_rule` requires `admin`.

```json
{
  "rule_id": "auto_raid_alert",
  "action": "disable",
  "requester_role": "admin",
  "admin_token": "change-me"
}
```

`rust_base_status`

```json
{ "player_id": "76561198000000001" }
```

`rust_market_listings`

```json
{ "query": "scrap", "include_unavailable": false }
```

`rust_list_kits`

```json
{ "category": "combat" }
```

`rust_give_kit` requires `admin`.

```json
{
  "player_id": "TargetName",
  "kit_name": "starter",
  "requester_role": "admin",
  "admin_token": "change-me"
}
```

### Fun And Guidance

`rust_roll_dice`

```json
{ "sides": 6, "count": 2, "player_id": "TargetName", "announce": true }
```

`rust_8ball`

```json
{ "question": "Should we raid tonight?", "player_id": "TargetName", "announce": true }
```

`rust_player_tip`

```json
{ "category": "base", "player_id": "TargetName", "announce": true }
```

These tools are player-safe by default. When `announce` is true, the MCP server sends the result through the existing `chat_send` bridge.

### Admin

`rust_admin_command` requires `admin` and uses the command whitelist.

```json
{
  "command": "status",
  "requester_role": "admin",
  "player_name": "Duckets",
  "admin_token": "change-me"
}
```

`rust_rcon_command` requires `admin`, uses the same MCP whitelist, and sends a WebRCON-backed command through the Rust plugin.

```json
{
  "command": "status",
  "requester_role": "admin",
  "player_name": "Duckets",
  "admin_token": "change-me"
}
```

`rust_kick_player` requires `mod+`.

```json
{ "player_id": "TargetName", "reason": "Rule violation", "requester_role": "mod" }
```

`rust_ban_player` requires `admin`.

```json
{ "player_id": "TargetName", "reason": "Cheating", "duration": "7d", "requester_role": "admin" }
```

`rust_lockdown` requires `admin`.

```json
{ "action": "start", "reason": "Raid defense", "requester_role": "admin" }
```

### Compatibility Aliases

The MCP server also accepts these older names:

- `rust_get_cameras`
- `rust_get_online_players`
- `rust_get_server_status`
- `rust_get_recent_chat`
- `rust_send_chat`
- `rust_execute_command`

## In-Game Commands

Main commands:

```text
/db help
/db terminal
/db cameras
/db view <camera>
/db control <left|right|up|down|zoom|reset>
/db ask <message>
/db alerts
/db security
/db players
/db status
/db admin <command>
```

The plugin includes more utility commands for base management, trading, intel, automation, markers, settings, and small chat games. Use `/db help` in game for the live command list.
