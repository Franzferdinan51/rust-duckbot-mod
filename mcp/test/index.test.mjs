import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  ALL_TOOLS,
  DEFAULT_CONFIG,
  createState,
  handleRustMessage,
  handleToolCall,
} from '../dist/index.js';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

test('exports a broad RustDuckBot MCP tool surface', () => {
  const toolNames = new Set(ALL_TOOLS.map((tool) => tool.name));

  assert.equal(toolNames.has('rust_list_cameras'), true);
  assert.equal(toolNames.has('rust_control_camera'), true);
  assert.equal(toolNames.has('rust_list_alerts'), true);
  assert.equal(toolNames.has('rust_set_automation_rule'), true);
  assert.equal(toolNames.has('rust_admin_command'), true);
  assert.equal(toolNames.has('rust_rcon_command'), true);
  assert.equal(toolNames.has('rust_list_kits'), true);
  assert.equal(toolNames.has('rust_give_kit'), true);
  assert.equal(ALL_TOOLS.length >= 24, true);
});

test('updates live state from Rust plugin heartbeat and camera messages', async () => {
  const state = createState(false);

  handleRustMessage({
    type: 'heartbeat',
    playerCount: 1,
    players: [{ id: 'steam1', name: 'DuckAdmin', role: 'admin', ping: 42, connectedAt: 'now' }],
  }, state);
  handleRustMessage({
    type: 'camera_update',
    cameras: [{ id: 'gate', name: 'Gate Cam', location: 'Front', online: true, hasPower: true, isPTZ: true }],
  }, state);

  const players = JSON.parse((await handleToolCall('rust_list_players', {}, state)).content[0].text);
  const cameras = JSON.parse((await handleToolCall('rust_list_cameras', {}, state)).content[0].text);

  assert.equal(players.count, 1);
  assert.equal(players.players[0].role, 'admin');
  assert.equal(cameras.count, 1);
  assert.equal(cameras.cameras[0].id, 'gate');
});

test('denies admin tools to regular users', async () => {
  const state = createState(false);
  handleRustMessage({
    type: 'player_list',
    players: [{ id: 'steam-user', name: 'RegularPlayer', role: 'user' }],
  }, state);

  const result = await handleToolCall('rust_admin_command', {
    requester_id: 'steam-user',
    command: 'status',
  }, state, DEFAULT_CONFIG);

  assert.equal(result.isError, true);
  assert.match(result.content[0].text, /Permission denied/);
});

test('routes RCON tool calls through the same admin gate and whitelist', async () => {
  const state = createState(false);
  handleRustMessage({
    type: 'player_list',
    players: [{ id: 'steam-admin', name: 'DuckAdmin', role: 'admin' }],
  }, state);

  const result = JSON.parse((await handleToolCall('rust_rcon_command', {
    requester_id: 'steam-admin',
    command: 'status',
  }, state, DEFAULT_CONFIG)).content[0].text);

  assert.equal(result.status, 'queued_no_rust_client');
  assert.equal(state.outboundMessages[0].type, 'admin_command');
  assert.equal(state.outboundMessages[0].command, 'status');
});

test('lists kits and lets admins queue kit grants for the Rust plugin', async () => {
  const state = createState(false);
  handleRustMessage({
    type: 'player_list',
    players: [{ id: 'steam-admin', name: 'DuckAdmin', role: 'admin' }, { id: 'steam-vip', name: 'VipPlayer', role: 'vip' }],
  }, state);

  const kits = JSON.parse((await handleToolCall('rust_list_kits', {}, state, DEFAULT_CONFIG)).content[0].text);
  assert.equal(kits.kits.some((kit) => kit.name === 'starter'), true);
  assert.equal(kits.kits.some((kit) => kit.name === 'admin'), true);

  const grant = JSON.parse((await handleToolCall('rust_give_kit', {
    requester_id: 'steam-admin',
    player_id: 'steam-vip',
    kit_name: 'starter',
  }, state, DEFAULT_CONFIG)).content[0].text);

  assert.equal(grant.status, 'queued_no_rust_client');
  assert.equal(state.outboundMessages.at(-1).type, 'kit_give');
  assert.equal(state.outboundMessages.at(-1).kit_name, 'starter');
  assert.equal(state.outboundMessages.at(-1).player_id, 'steam-vip');
});

test('guards the C# /db command path against previous silent-load regressions', () => {
  const source = readFileSync(resolve(repoRoot, 'src/DuckBotMod.cs'), 'utf8');
  assert.match(source, /cmd\.AddChatCommand\("db", this, nameof\(CmdDuckBot\)\)/);
  assert.doesNotMatch(source, /using\s+\w+\s*=\s*(Rust|Oxide\.Core)\./);
  assert.doesNotMatch(source, /\/tmp\/duckbot_debug/);
  assert.equal((source.match(/case "coords"/g) ?? []).length, 1);
  assert.equal((source.match(/case "time": ShowTime\(player, session\); break;/g) ?? []).length, 1);
  assert.match(source, /case "kit_give":\s*HandleMCPKitGive\(message\);/s);
});

test('queues plugin actions when the Rust websocket bridge is not connected', async () => {
  const state = createState(false);
  handleRustMessage({
    type: 'player_list',
    players: [{ id: 'steam-vip', name: 'VipPlayer', role: 'vip' }],
  }, state);
  handleRustMessage({
    type: 'camera_update',
    cameras: [{ id: 'gate', name: 'Gate Cam', location: 'Front', online: true, hasPower: true, isPTZ: true }],
  }, state);

  const result = JSON.parse((await handleToolCall('rust_control_camera', {
    requester_id: 'steam-vip',
    player_id: 'steam-vip',
    camera_id: 'gate',
    action: 'left',
  }, state)).content[0].text);

  assert.equal(result.status, 'queued_no_rust_client');
  assert.equal(state.outboundMessages.length, 1);
  assert.equal(state.outboundMessages[0].type, 'camera_control');
});
