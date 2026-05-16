import { createServer } from 'node:http';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  GetPromptRequestSchema,
  ListPromptsRequestSchema,
  ListResourcesRequestSchema,
  ListToolsRequestSchema,
  ReadResourceRequestSchema,
} from '@modelcontextprotocol/sdk/types.js';
import { WebSocket, WebSocketServer } from 'ws';

const __dirname = dirname(fileURLToPath(import.meta.url));
const VERSION = '1.4.5';

type Role = 'user' | 'vip' | 'mod' | 'admin';
type LogLevel = 'debug' | 'info' | 'warn' | 'error';
type JsonObject = Record<string, unknown>;

interface ServerConfig {
  stdioEnabled: boolean;
  bridgeEnabled: boolean;
  bridgeHost: string;
  bridgePort: number;
  logLevel: LogLevel;
  seedDemoData: boolean;
  maxHistory: number;
  adminToken?: string;
  allowedAdminCommands: string[];
}

interface CameraState {
  id: string;
  name: string;
  location: string;
  monument?: string;
  online: boolean;
  hasPower: boolean;
  isPTZ: boolean;
  viewCount?: number;
  lastActivity?: string;
}

interface PlayerState {
  id: string;
  name: string;
  role: Role;
  ping?: number;
  connectedAt?: string;
  currentCamera?: string;
  online?: boolean;
  position?: string;
  monument?: string;
}

interface ServerStatus {
  uptime: string;
  fps: number;
  players: number;
  sleeping?: number;
  cameras: number;
  alerts: number;
  memoryMB?: number;
  mcpConnected: boolean;
  rconConnected?: boolean;
  lastUpdated: string;
  serverName?: string;
  serverSeed?: number;
  worldSize?: number;
  serverPvE?: boolean;
  entityCount?: number;
  sleepingPlayers?: number;
  monuments?: Array<{ name: string; position: string; grid?: string }>;
}

interface ChatMessage {
  playerId?: string;
  playerName?: string;
  sender: string;
  message: string;
  time: string;
  role?: Role;
  target?: string;
  isAI?: boolean;
}

interface AlertState {
  id: string;
  type: string;
  severity: 'low' | 'medium' | 'high' | 'critical' | string;
  title: string;
  message: string;
  time: string;
  acknowledged?: boolean;
  acknowledgedBy?: string;
  location?: string;
}

interface ActivityState {
  time: string;
  category: string;
  action: string;
  details: string;
  playerId?: string;
  playerName?: string;
}

interface MapMarker {
  id: string;
  name: string;
  position: string;
  color?: string;
  icon?: string;
  ownerId?: string;
  visible?: boolean;
}

interface AutomationRule {
  id: string;
  name: string;
  trigger: string;
  condition: string;
  action: string;
  enabled: boolean;
  priority?: number;
  lastTriggered?: string;
}

interface BaseState {
  ownerId?: string;
  name: string;
  position: string;
  blockCount?: number;
  healthPercent?: number;
  decayRatePerHour?: number;
  upkeepCost?: number;
  underAttack?: boolean;
  doors?: number;
  lights?: number;
  turrets?: number;
}

interface MarketListing {
  id: string;
  sellerId?: string;
  sellerName?: string;
  itemName: string;
  quantity: number;
  pricePerUnit: number;
  currency: string;
  available: boolean;
  listedAt?: string;
}

interface KitDefinition {
  name: string;
  displayName: string;
  category: string;
  description: string;
  permission: string;
  cooldownMinutes: number;
  maxUsesPerDay: number;
}

interface RconResponseState {
  requestId?: string;
  identifier?: number;
  command?: string;
  message: string;
  raw?: unknown;
  time: string;
  source?: string;
}

export interface DuckBotState {
  cameras: Map<string, CameraState>;
  players: Map<string, PlayerState>;
  chatHistory: ChatMessage[];
  alerts: Map<string, AlertState>;
  activity: ActivityState[];
  markers: Map<string, MapMarker>;
  automationRules: Map<string, AutomationRule>;
  bases: BaseState[];
  marketListings: MarketListing[];
  rconResponses: RconResponseState[];
  server: ServerStatus;
  rustClients: Set<WebSocket>;
  outboundMessages: JsonObject[];
  bridgeStartedAt: string;
}

const ROLE_RANK: Record<Role, number> = {
  user: 0,
  vip: 1,
  mod: 2,
  admin: 3,
};

const DEFAULT_KITS: KitDefinition[] = [
  { name: 'starter', displayName: 'Starter Pack', category: 'combat', description: 'Basic resources to get started.', permission: 'rustduckbot.use', cooldownMinutes: 60, maxUsesPerDay: 3 },
  { name: 'pvp', displayName: 'PvP Loadout', category: 'combat', description: 'Combat gear, ammo, and armor.', permission: 'rustduckbot.vip', cooldownMinutes: 120, maxUsesPerDay: 2 },
  { name: 'building', displayName: 'Builder Bundle', category: 'building', description: 'Building resources and tools.', permission: 'rustduckbot.vip', cooldownMinutes: 90, maxUsesPerDay: 3 },
  { name: 'mini', displayName: 'Mini Starter', category: 'utility', description: 'Server-defined mini kit.', permission: 'rustduckbot.vip', cooldownMinutes: 240, maxUsesPerDay: 1 },
  { name: 'scrap', displayName: 'Scrap Heap', category: 'resources', description: 'Server-defined scrap kit.', permission: 'rustduckbot.use', cooldownMinutes: 30, maxUsesPerDay: 4 },
  { name: 'admin', displayName: 'Admin Kit', category: 'admin', description: 'Admin-only server kit.', permission: 'rustduckbot.admin', cooldownMinutes: 60, maxUsesPerDay: 2 },
];

const EIGHT_BALL_RESPONSES = [
  'Signs point to yes.',
  'Bring extra meds first.',
  'Not unless the counters are asleep.',
  'The loot room says maybe.',
  'Ask again after you check upkeep.',
  'Yes, but depot before you get bold.',
  'The island is not convinced.',
  'Absolutely, if you have bags down.',
];

const PLAYER_TIPS: Record<string, string[]> = {
  starter: [
    'Place bags before roaming so a bad fight does not reset your night.',
    'Stone tools are loud but fast; upgrade your path once you have metal fragments.',
    'Split loot between a main box and a hidden stash until your base has real doors.',
  ],
  base: [
    'Add an airlock before expanding; one extra door can save the whole base.',
    'Check tool cupboard upkeep before logging off, especially after upgrading walls.',
    'Honeycomb the side with your tool cupboard first if resources are tight.',
  ],
  combat: [
    'Take a flank route after firing; players chase the last sound they heard.',
    'Carry one wall or barricade when roaming with gear you care about.',
    'Reload before looting. The best loot box on Rust is often the next player.',
  ],
  farming: [
    'Depot when your inventory is half valuable. Greed is a very expensive backpack.',
    'Use safe zones to recycle early components before roaming deeper.',
    'Mark rich nodes or barrels for teammates with DuckBot map markers.',
  ],
  cctv: [
    'Use cameras before opening outer doors during raid hours.',
    'Name cameras by location so the AI can switch feeds quickly under pressure.',
    'Keep one camera on your approach route and one on the tool cupboard path.',
  ],
  admin: [
    'Use RCON through the whitelist for routine fixes, then audit recent activity.',
    'Grant kits through DuckBot MCP so the plugin logs who requested it.',
    'Check player reports and recent chat before using punitive commands.',
  ],
};

export const DEFAULT_CONFIG: ServerConfig = {
  stdioEnabled: process.env['MCP_STDIO'] !== '0',
  bridgeEnabled: process.env['RUST_DUCKBOT_BRIDGE'] !== '0',
  bridgeHost: process.env['RUST_DUCKBOT_BRIDGE_HOST'] ?? process.env['MCP_WS_HOST'] ?? '127.0.0.1',
  bridgePort: Number(process.env['RUST_DUCKBOT_BRIDGE_PORT'] ?? process.env['MCP_WS_PORT'] ?? 3851),
  logLevel: (process.env['MCP_LOG_LEVEL'] as LogLevel | undefined) ?? 'info',
  seedDemoData: process.env['RUST_DUCKBOT_SEED_DEMO'] !== '0',
  maxHistory: Number(process.env['RUST_DUCKBOT_MAX_HISTORY'] ?? 200),
  adminToken: process.env['RUST_DUCKBOT_ADMIN_TOKEN'],
  allowedAdminCommands: (process.env['RUST_DUCKBOT_ALLOWED_COMMANDS'] ?? 'status,serverinfo,kick,ban,unban,say,global.say,inventory.give,teleport,teleport2me,weather,time')
    .split(',')
    .map((value) => value.trim().toLowerCase())
    .filter(Boolean),
};

function loadConfig(): ServerConfig {
  const configPath = process.env['MCP_CONFIG_PATH'] ?? join(__dirname, '../config.json');
  if (!existsSync(configPath)) return DEFAULT_CONFIG;

  try {
    const parsed = JSON.parse(readFileSync(configPath, 'utf8')) as Partial<ServerConfig>;
    return { ...DEFAULT_CONFIG, ...parsed };
  } catch (error) {
    log('warn', `Could not load config ${configPath}: ${String(error)}`);
    return DEFAULT_CONFIG;
  }
}

function log(level: LogLevel, ...args: unknown[]): void {
  const levels: Record<LogLevel, number> = { debug: 0, info: 1, warn: 2, error: 3 };
  const configured = (process.env['MCP_LOG_LEVEL'] as LogLevel | undefined) ?? DEFAULT_CONFIG.logLevel;
  if (levels[level] < levels[configured]) return;
  console.error(`[${new Date().toISOString()}] [${level.toUpperCase()}]`, ...args);
}

function textResult(text: string, isError = false) {
  return { content: [{ type: 'text' as const, text }], isError };
}

function jsonResult(value: unknown, isError = false) {
  return textResult(JSON.stringify(value, null, 2), isError);
}

function requiredString(args: JsonObject, name: string): string | undefined {
  const value = args[name];
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined;
}

function optionalString(args: JsonObject, name: string, fallback = ''): string {
  return requiredString(args, name) ?? fallback;
}

function optionalNumber(args: JsonObject, name: string, fallback: number): number {
  const value = args[name];
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function boundedInteger(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, Math.floor(value)));
}

function randomItem<T>(items: T[]): T {
  return items[Math.floor(Math.random() * items.length)] ?? items[0];
}

function normalizeRole(value: unknown): Role {
  if (typeof value !== 'string') return 'user';
  const role = value.toLowerCase();
  return role === 'admin' || role === 'mod' || role === 'vip' || role === 'user' ? role : 'user';
}

function hasRole(actual: Role, required: Role): boolean {
  return ROLE_RANK[actual] >= ROLE_RANK[required];
}

function findPlayer(state: DuckBotState, playerIdOrName?: string): PlayerState | undefined {
  if (!playerIdOrName) return undefined;
  const lower = playerIdOrName.toLowerCase();
  return state.players.get(playerIdOrName)
    ?? Array.from(state.players.values()).find((player) => player.name.toLowerCase() === lower)
    ?? Array.from(state.players.values()).find((player) => player.name.toLowerCase().includes(lower));
}

function requesterRole(state: DuckBotState, args: JsonObject): Role {
  const explicit = args['requester_role'] ?? args['player_role'] ?? args['role'];
  if (explicit) return normalizeRole(explicit);

  const requesterId = requiredString(args, 'requester_id') ?? requiredString(args, 'player_id');
  const requesterName = requiredString(args, 'requester_name') ?? requiredString(args, 'admin_name') ?? requiredString(args, 'player_name');
  return findPlayer(state, requesterId)?.role ?? findPlayer(state, requesterName)?.role ?? 'user';
}

function requireRole(state: DuckBotState, args: JsonObject, minimum: Role) {
  const role = requesterRole(state, args);
  if (!hasRole(role, minimum)) {
    return textResult(`Permission denied: ${minimum}+ required, requester has ${role}.`, true);
  }
  return undefined;
}

function requireAdminToken(config: ServerConfig, args: JsonObject) {
  if (!config.adminToken) return undefined;
  const token = requiredString(args, 'admin_token');
  if (token !== config.adminToken) {
    return textResult('Permission denied: admin_token is required for this server.', true);
  }
  return undefined;
}

function commandAllowed(config: ServerConfig, command: string): boolean {
  const firstWord = command.trim().split(/\s+/)[0]?.toLowerCase() ?? '';
  return config.allowedAdminCommands.includes(firstWord);
}

const RCON_COMMAND_CATALOG = [
  { command: 'status', category: 'read', role: 'admin', safety: 'read-only', description: 'Detailed server status including hostname, players, FPS, uptime, and connected player rows.', example: 'status' },
  { command: 'serverinfo', category: 'read', role: 'admin', safety: 'read-only', description: 'Server metadata as reported by Rust, commonly including map/seed/world settings and player counts.', example: 'serverinfo' },
  { command: 'player.list', category: 'read', role: 'admin', safety: 'read-only', description: 'List known/connected players when supported by the server build/plugins.', example: 'player.list' },
  { command: 'players.online', category: 'read', role: 'admin', safety: 'read-only', description: 'List online players when supported by the server build/plugins.', example: 'players.online' },
  { command: 'server.hostname', category: 'read', role: 'admin', safety: 'read-only', description: 'Show or query the configured server hostname.', example: 'server.hostname' },
  { command: 'server.seed', category: 'read', role: 'admin', safety: 'read-only', description: 'Show the current map seed.', example: 'server.seed' },
  { command: 'server.worldsize', category: 'read', role: 'admin', safety: 'read-only', description: 'Show the current map world size.', example: 'server.worldsize' },
  { command: 'server.pve', category: 'read', role: 'admin', safety: 'read-only', description: 'Show whether PvE mode is enabled.', example: 'server.pve' },
  { command: 'global.status', category: 'read', role: 'admin', safety: 'read-only', description: 'Alternate/global status command where supported.', example: 'global.status' },
  { command: 'kick', category: 'moderation', role: 'admin', safety: 'action', description: 'Kick a player from the server. Requires target and optional reason.', example: 'kick "PlayerName" "reason"' },
  { command: 'ban', category: 'moderation', role: 'admin', safety: 'destructive-action', description: 'Ban a player by name/ID depending on Rust command behavior.', example: 'ban "PlayerName" "reason"' },
  { command: 'banid', category: 'moderation', role: 'admin', safety: 'destructive-action', description: 'Ban a SteamID with a reason/duration when supported.', example: 'banid 7656119... "reason"' },
  { command: 'unban', category: 'moderation', role: 'admin', safety: 'action', description: 'Remove a ban for a player/SteamID.', example: 'unban 7656119...' },
  { command: 'say', category: 'communication', role: 'admin', safety: 'action', description: 'Send a server chat message.', example: 'say "Server restart in 5 minutes"' },
  { command: 'global.say', category: 'communication', role: 'admin', safety: 'action', description: 'Send a global server chat message.', example: 'global.say "Welcome to the server"' },
  { command: 'inventory.give', category: 'inventory', role: 'admin', safety: 'action', description: 'Give item(s) through Rust inventory command syntax.', example: 'inventory.give wood 1000' },
  { command: 'teleport', category: 'movement', role: 'admin', safety: 'action', description: 'Teleport a player using Rust teleport syntax.', example: 'teleport PlayerName 0 0 0' },
  { command: 'teleport2me', category: 'movement', role: 'admin', safety: 'action', description: 'Teleport target player to the admin.', example: 'teleport2me PlayerName' },
  { command: 'weather', category: 'world', role: 'admin', safety: 'action', description: 'Query or change weather depending on supplied arguments.', example: 'weather' },
  { command: 'time', category: 'world', role: 'admin', safety: 'action', description: 'Query or change in-game time depending on supplied arguments.', example: 'time' },
  { command: 'save', category: 'maintenance', role: 'admin', safety: 'action', description: 'Force a world/server save.', example: 'save' },
  { command: 'gc.collect', category: 'maintenance', role: 'admin', safety: 'maintenance-action', description: 'Trigger garbage collection on the server.', example: 'gc.collect' },
  { command: 'status.gpu', category: 'diagnostics', role: 'admin', safety: 'read-only', description: 'GPU diagnostic status where supported.', example: 'status.gpu' },
  { command: 'status.ram', category: 'diagnostics', role: 'admin', safety: 'read-only', description: 'RAM diagnostic status where supported.', example: 'status.ram' },
];

function rconCatalogForConfig(config: ServerConfig): JsonObject[] {
  return RCON_COMMAND_CATALOG
    .filter((entry) => config.allowedAdminCommands.includes(entry.command))
    .map((entry) => ({
      ...entry,
      enabled: true,
      queryTool: READ_ONLY_RCON_COMMANDS.has(entry.command) ? 'rust_rcon_query' : 'rust_rcon_command',
    }));
}
const READ_ONLY_RCON_COMMANDS = new Set(RCON_COMMAND_CATALOG.filter((entry) => entry.safety === 'read-only').map((entry) => entry.command));

function readOnlyRconAllowed(command: string): boolean {
  const firstWord = command.trim().split(/\s+/)[0]?.toLowerCase() ?? '';
  return READ_ONLY_RCON_COMMANDS.has(firstWord);
}

function sanitizeRconQuery(command: string): string | undefined {
  const trimmed = command.trim();
  if (!trimmed || trimmed.length > 180) return undefined;
  if (/[;|&`$<>]/.test(trimmed)) return undefined;
  return readOnlyRconAllowed(trimmed) ? trimmed : undefined;
}

function latestRconResponse(state: DuckBotState, command?: string): RconResponseState | undefined {
  if (!command) return state.rconResponses[state.rconResponses.length - 1];
  const firstWord = command.trim().split(/\s+/)[0]?.toLowerCase() ?? '';
  return [...state.rconResponses].reverse().find((response) => response.command?.toLowerCase().startsWith(firstWord));
}

function parseRconMessage(message: string): JsonObject {
  const lines = message.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  const statusPlayers = lines
    .filter((line) => /^\d+\s+/.test(line) || /steamid/i.test(line))
    .slice(0, 200);
  return {
    lines,
    summary: lines.slice(0, 20).join('\n'),
    playerRows: statusPlayers,
  };
}

function nowIso(): string {
  return new Date().toISOString();
}

function pushLimited<T>(items: T[], item: T, max: number): void {
  items.push(item);
  while (items.length > max) items.shift();
}

export function createState(): DuckBotState {
  const state: DuckBotState = {
    cameras: new Map(),
    players: new Map(),
    chatHistory: [],
    alerts: new Map(),
    activity: [],
    markers: new Map(),
    automationRules: new Map(),
    bases: [],
    marketListings: [],
    rconResponses: [],
    server: {
      uptime: '0h',
      fps: 0,
      players: 0,
      cameras: 0,
      alerts: 0,
      mcpConnected: false,
      lastUpdated: nowIso(),
    },
    rustClients: new Set(),
    outboundMessages: [],
    bridgeStartedAt: nowIso(),
  };
  return state;
}

const schema = {
  string: (description: string, _example?: string) => ({ type: 'string', description }),
  number: (description: string) => ({ type: 'number', description }),
  boolean: (description: string) => ({ type: 'boolean', description }),
  integer: (description: string) => ({ type: 'integer', description }),
  role: { type: 'string', enum: ['user', 'vip', 'mod', 'admin'], description: 'Requester role when the caller already knows it.' },
};

function optionalInteger(obj: any, key: string, fallback: number): number {
  const v = obj?.[key];
  return typeof v === 'number' && Number.isInteger(v) ? v : fallback;
}

export const ALL_TOOLS = [
  {
    name: 'rust_computer_context',
    description: 'Get the in-game DuckBot computer context for a player: role, active camera, alerts, and available feature groups.',
    inputSchema: {
      type: 'object',
      properties: { player_id: schema.string('Player Steam ID.'), player_name: schema.string('Player display name.') },
    },
  },
  {
    name: 'rust_list_cameras',
    description: 'List available CCTV cameras with location, power, online state, PTZ support, and view counts.',
    inputSchema: { type: 'object', properties: { player_id: schema.string('Optional player Steam ID for access-aware filtering.') } },
  },
  {
    name: 'rust_view_camera',
    description: 'Ask the Rust plugin to switch a player computer station to a camera feed.',
    inputSchema: {
      type: 'object',
      properties: {
        camera_id: schema.string('Camera identifier or known alias.'),
        player_id: schema.string('Player Steam ID requesting the view.'),
        requester_role: schema.role,
      },
      required: ['camera_id', 'player_id'],
    },
  },
  {
    name: 'rust_control_camera',
    description: 'Control a PTZ camera. Requires vip or higher.',
    inputSchema: {
      type: 'object',
      properties: {
        camera_id: schema.string('Camera identifier.'),
        action: { type: 'string', enum: ['left', 'right', 'up', 'down', 'zoom', 'zoom_in', 'zoom_out', 'reset', 'home'], description: 'PTZ action.' },
        player_id: schema.string('Requester Steam ID.'),
        requester_role: schema.role,
      },
      required: ['camera_id', 'action', 'player_id'],
    },
  },
  {
    name: 'rust_get_camera_snapshot',
    description: 'Request a best-effort snapshot/thumbnail from a camera through the Rust plugin.',
    inputSchema: {
      type: 'object',
      properties: { camera_id: schema.string('Camera identifier.'), player_id: schema.string('Requester Steam ID.'), requester_role: schema.role },
      required: ['camera_id'],
    },
  },
  {
    name: 'rust_list_players',
    description: 'List online players known to DuckBot with roles, ping, and session info.',
    inputSchema: { type: 'object', properties: { role_filter: { type: 'string', enum: ['all', 'user', 'vip', 'mod', 'admin'] } } },
  },
  {
    name: 'rust_get_player_info',
    description: 'Get known player details and recent chat history.',
    inputSchema: {
      type: 'object',
      properties: { player_id: schema.string('Steam ID.'), player_name: schema.string('Display name.') },
    },
  },
  {
    name: 'rust_find_player',
    description: 'Search players by partial display name or Steam ID.',
    inputSchema: { type: 'object', properties: { pattern: schema.string('Partial name or ID.') }, required: ['pattern'] },
  },
  {
    name: 'rust_server_status',
    description: 'Get Rust server health: uptime, FPS, players, cameras, alerts, memory, and bridge status.',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'rust_map_overview',
    description: 'Get structured map/world overview: server name, seed, world size, PvE mode, monuments, markers, players, sleepers, entities, alerts, and uptime.',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'rust_route_advice',
    description: 'Return structured route context between a player/grid origin and a target monument or grid for AI planning.',
    inputSchema: { type: 'object', properties: { player_id: schema.string('Optional player origin.'), from_grid: schema.string('Optional explicit origin grid.'), to: schema.string('Target monument or grid.'), requester_role: schema.role }, required: ['to'] },
  },
  {
    name: 'rust_monument_advice_context',
    description: 'Return structured monument context including grid, position, and nearby monuments for AI briefing.',
    inputSchema: { type: 'object', properties: { monument: schema.string('Monument name.'), from_grid: schema.string('Optional player grid for context.') }, required: ['monument'] },
  },
  {
    name: 'rust_map_marker_catalog',
    description: 'Return DuckBot map markers grouped by owner/public visibility/type.',
    inputSchema: { type: 'object', properties: { player_id: schema.string('Optional owner filter.') } },
  },
  {
    name: 'rust_chat_moderation_context',
    description: 'Return recent chat and activity context for AI-powered moderation review, focused on harassment/spam/scam language and suspicious exploit coordination clues.',
    inputSchema: { type: 'object', properties: { player_id: schema.string('Optional player filter.'), limit: schema.number('Default 20, max 100.'), requester_role: schema.role } },
  },
  {
    name: 'rust_rcon_command_catalog',
    description: 'List every preconfigured RCON command DuckBot exposes to LM Studio, with category, role, safety level, examples, and whether to call rust_rcon_query or rust_rcon_command.',
    inputSchema: { type: 'object', properties: { category: schema.string('Optional category filter: read, moderation, communication, inventory, movement, world, maintenance, diagnostics.'), safety: schema.string('Optional safety filter: read-only, action, destructive-action, maintenance-action.') } },
  },
  {
    name: 'rust_rcon_query',
    description: 'Run a safe read-only RCON query and return the latest parsed output when available. Allowed: status, serverinfo, player.list, players.online, server.hostname, server.seed, server.worldsize, server.pve.',
    inputSchema: {
      type: 'object',
      properties: {
        command: schema.string('Read-only RCON command to run.'),
        requester_id: schema.string('Requester Steam ID.'),
        requester_role: schema.role,
      },
      required: ['command'],
    },
  },
  {
    name: 'rust_rcon_history',
    description: 'Read recent RCON responses captured from the Rust WebRCON connection. Requires admin.',
    inputSchema: {
      type: 'object',
      properties: { limit: schema.number('Default 10, max 50.'), requester_role: schema.role },
    },
  },
  {
    name: 'rust_get_server_info',
    description: 'Get enriched live server info: name, seed/map fields when available, FPS, players, sleepers, entities, cameras, alerts, RCON/MCP state.',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'rust_get_player_positions',
    description: 'List online player positions/grid/nearest monument known from DuckBot heartbeat. Requires mod or higher unless requester filters self.',
    inputSchema: { type: 'object', properties: { player_id: schema.string('Optional Steam ID/self filter.'), requester_id: schema.string('Requester Steam ID.'), requester_role: schema.role } },
  },
  {
    name: 'rust_get_monument_info',
    description: 'List known monument names, coordinates, and grid references from the RustDuckBot monument table.',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'rust_bridge_status',
    description: 'Inspect MCP bridge health, client count, queue depth, last heartbeat, and latest RCON response time.',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'rust_admin_event_create',
    description: 'Start a server-wide random event (coinflip, jackpot, scavenger, dropparty). Requires mod+ role. AI narrates the result.',
    inputSchema: {
      type: 'object',
      properties: {
        event_type: { type: 'string', enum: ['coinflip', 'jackpot', 'scavenger', 'dropparty'], description: 'Event type to start.' },
        args: schema.string('Event-specific argument: coinflip=scrap pot, jackpot=scrap amount, scavenger=duration in seconds, dropparty=item name.'),
        requester_id: schema.string('Admin/Mod Steam ID.'),
        requester_role: schema.role,
      },
      required: ['event_type', 'requester_id', 'requester_role'],
    },
  },
  {
    name: 'rust_admin_event_list',
    description: 'List active admin/mod events, participants, remaining time, and prize pools.',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'rust_admin_event_cancel',
    description: 'Cancel an active server event by type. Requires mod+ role.',
    inputSchema: {
      type: 'object',
      properties: {
        event_type: { type: 'string', description: 'Event type to cancel (coinflip, jackpot, scavenger, dropparty).' },
        requester_id: schema.string('Admin/Mod Steam ID.'),
        requester_role: schema.role,
      },
      required: ['event_type', 'requester_id', 'requester_role'],
    },
  },
  {
    name: 'rust_economy_status',
    description: 'Get economy overview: daily reward status, active events, VIP bonus multiplier, and recent loot game results.',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'rust_vip_bonus_info',
    description: 'Get current VIP bonus multiplier and what rewards it applies to (daily, killstreak).',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'rust_lucky_block_prizes',
    description: 'List the current lucky block prize tiers, drop rates, and reward items. Shows what VIP players can win.',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'rust_guess_game_status',
    description: 'Check if a player currently has an active number guessing game running, including guesses left and current prize pool.',
    inputSchema: {
      type: 'object',
      properties: { player_id: schema.string('Steam ID of player to check.') },
      required: ['player_id'],
    },
  },
  {
    name: 'rust_get_player_stats',
    description: 'Get player statistics: kills, deaths, K/D ratio, playtime, total scrap, daily claims, and activity level.',
    inputSchema: {
      type: 'object',
      properties: { player_id: schema.string('Steam ID of player.') },
      required: ['player_id'],
    },
  },
  {
    name: 'rust_leaderboard',
    description: 'Get server leaderboard by category: kills, K/D, scrap earned, events won, or activity score.',
    inputSchema: {
      type: 'object',
      properties: {
        category: schema.string('kills | kd | scrap | events | activity'),
        limit: schema.number('Max entries to return (default 10).'),
      },
    },
  },
  {
    name: 'rust_shop_listings',
    description: 'Get current player-to-player shop listings, prices, and seller info.',
    inputSchema: {
      type: 'object',
      properties: { filter: schema.string('Optional item name filter.') },
    },
  },
  {
    name: 'rust_chat_send',
    description: 'Send a chat message to a player or global chat through the Rust plugin.',
    inputSchema: {
      type: 'object',
      properties: {
        message: schema.string('Message body.'),
        target: schema.string('Player name/ID, or global.'),
        sender: schema.string('Sender display name. Defaults to DuckBot.'),
      },
      required: ['message'],
    },
  },
  {
    name: 'rust_chat_history',
    description: 'Read recent chat history, optionally filtered by player.',
    inputSchema: {
      type: 'object',
      properties: { player_id: schema.string('Optional Steam ID filter.'), limit: schema.number('Maximum messages, default 20, max 100.') },
    },
  },
  {
    name: 'rust_list_alerts',
    description: 'List smart alerts from the DuckBot computer security system.',
    inputSchema: {
      type: 'object',
      properties: { include_acknowledged: schema.boolean('Include acknowledged alerts.'), severity: schema.string('Optional severity filter.') },
    },
  },
  {
    name: 'rust_ack_alert',
    description: 'Acknowledge an alert. Requires vip or higher.',
    inputSchema: {
      type: 'object',
      properties: { alert_id: schema.string('Alert ID.'), requester_id: schema.string('Requester Steam ID.'), requester_role: schema.role },
      required: ['alert_id'],
    },
  },
  {
    name: 'rust_security_scan',
    description: 'Request/return a security scan summary for nearby players, alerts, cameras, and watched bases. Requires vip or higher.',
    inputSchema: {
      type: 'object',
      properties: { requester_id: schema.string('Requester Steam ID.'), radius: schema.number('Scan radius in meters.'), requester_role: schema.role },
    },
  },
  {
    name: 'rust_list_activity',
    description: 'List recent DuckBot audit/activity entries. Requires mod or higher for all-player logs.',
    inputSchema: {
      type: 'object',
      properties: { category: schema.string('Optional category.'), player_id: schema.string('Optional player filter.'), limit: schema.number('Default 25, max 100.'), requester_role: schema.role },
    },
  },
  {
    name: 'rust_list_map_markers',
    description: 'List DuckBot grid/map markers available to a player.',
    inputSchema: { type: 'object', properties: { player_id: schema.string('Optional owner/player filter.') } },
  },
  {
    name: 'rust_add_map_marker',
    description: 'Create a DuckBot map marker. Requires vip or higher.',
    inputSchema: {
      type: 'object',
      properties: {
        name: schema.string('Marker name.'),
        position: schema.string('Grid or x,y,z position.'),
        color: schema.string('Marker color.'),
        icon: schema.string('Marker icon/type.'),
        requester_id: schema.string('Requester Steam ID.'),
        requester_role: schema.role,
      },
      required: ['name', 'position'],
    },
  },
  {
    name: 'rust_list_automation_rules',
    description: 'List DuckBot automation rules.',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'rust_set_automation_rule',
    description: 'Enable, disable, run, or delete an automation rule. Requires admin.',
    inputSchema: {
      type: 'object',
      properties: {
        rule_id: schema.string('Automation rule ID.'),
        action: { type: 'string', enum: ['enable', 'disable', 'run', 'delete'], description: 'Action to perform.' },
        requester_id: schema.string('Requester Steam ID.'),
        requester_role: schema.role,
        admin_token: schema.string('Optional server admin token when configured.'),
      },
      required: ['rule_id', 'action'],
    },
  },
  {
    name: 'rust_base_status',
    description: 'List monitored bases, decay, defenses, and attack state for a player.',
    inputSchema: { type: 'object', properties: { player_id: schema.string('Optional owner Steam ID.'), requester_role: schema.role } },
  },
  {
    name: 'rust_market_listings',
    description: 'List DuckBot trading/vending listings.',
    inputSchema: {
      type: 'object',
      properties: { query: schema.string('Optional item search.'), include_unavailable: schema.boolean('Include unavailable listings.') },
    },
  },
  {
    name: 'rust_list_kits',
    description: 'List DuckBot-aware server kits, permissions, cooldowns, and daily limits.',
    inputSchema: {
      type: 'object',
      properties: { category: schema.string('Optional kit category filter.') },
    },
  },
  {
    name: 'rust_give_kit',
    description: 'Grant a configured server kit to a player through the Rust plugin. Requires admin and optional admin_token.',
    inputSchema: {
      type: 'object',
      properties: {
        player_id: schema.string('Target Steam ID or display name.'),
        kit_name: schema.string('Configured kit name, such as starter, pvp, building, mini, scrap, or admin.'),
        requester_id: schema.string('Admin Steam ID.'),
        requester_role: schema.role,
        admin_token: schema.string('Optional server admin token when configured.'),
      },
      required: ['player_id', 'kit_name'],
    },
  },
  {
    name: 'rust_roll_dice',
    description: 'Roll safe in-game dice for giveaways, minigames, disputes, or player fun. Can announce to a player or global chat.',
    inputSchema: {
      type: 'object',
      properties: {
        sides: schema.number('Sides per die. Default 100, min 2, max 10000.'),
        count: schema.number('Number of dice. Default 1, max 20.'),
        player_id: schema.string('Optional target player Steam ID or name.'),
        announce: schema.boolean('When true, send the result to Rust chat.'),
      },
    },
  },
  {
    name: 'rust_8ball',
    description: 'Answer a lighthearted Rust question with an 8-ball style response. Can announce to a player or global chat.',
    inputSchema: {
      type: 'object',
      properties: {
        question: schema.string('Player question.'),
        player_id: schema.string('Optional target player Steam ID or name.'),
        announce: schema.boolean('When true, send the answer to Rust chat.'),
      },
      required: ['question'],
    },
  },
  {
    name: 'rust_player_tip',
    description: 'Give a useful Rust tip by category, optionally sending it in-game to a player.',
    inputSchema: {
      type: 'object',
      properties: {
        category: { type: 'string', enum: ['starter', 'base', 'combat', 'farming', 'cctv', 'admin'], description: 'Tip category.' },
        player_id: schema.string('Optional target player Steam ID or name.'),
        announce: schema.boolean('When true, send the tip to Rust chat.'),
      },
    },
  },
  {
    name: 'rust_admin_command',
    description: 'Execute a whitelisted Rust server console/RCON command through the plugin. Requires admin and optional admin_token.',
    inputSchema: {
      type: 'object',
      properties: {
        command: schema.string('Raw Rust console command. The first word must be in the whitelist.'),
        requester_id: schema.string('Admin Steam ID.'),
        player_name: schema.string('Admin display name for audit.'),
        requester_role: schema.role,
        admin_token: schema.string('Optional server admin token when configured.'),
      },
      required: ['command'],
    },
  },
  {
    name: 'rust_rcon_command',
    description: 'Execute a whitelisted Rust WebRCON command through the RustDuckBot plugin. Requires admin and optional admin_token.',
    inputSchema: {
      type: 'object',
      properties: {
        command: schema.string('Raw Rust RCON command. The first word must be in the whitelist.'),
        requester_id: schema.string('Admin Steam ID.'),
        player_name: schema.string('Admin display name for audit.'),
        requester_role: schema.role,
        admin_token: schema.string('Optional server admin token when configured.'),
      },
      required: ['command'],
    },
  },
  {
    name: 'rust_kick_player',
    description: 'Kick a player. Requires mod or higher.',
    inputSchema: {
      type: 'object',
      properties: {
        player_id: schema.string('Target Steam ID or display name.'),
        reason: schema.string('Reason shown/logged.'),
        requester_id: schema.string('Requester Steam ID.'),
        requester_role: schema.role,
      },
      required: ['player_id'],
    },
  },
  {
    name: 'rust_ban_player',
    description: 'Ban a player. Requires admin.',
    inputSchema: {
      type: 'object',
      properties: {
        player_id: schema.string('Target Steam ID or display name.'),
        reason: schema.string('Ban reason.'),
        duration: schema.string('Duration such as 1d, 7d, 30d, perm.'),
        requester_id: schema.string('Requester Steam ID.'),
        requester_role: schema.role,
        admin_token: schema.string('Optional server admin token when configured.'),
      },
      required: ['player_id', 'reason'],
    },
  },
  {
    name: 'rust_lockdown',
    description: 'Start, stop, or query emergency base/server lockdown. Requires admin.',
    inputSchema: {
      type: 'object',
      properties: {
        action: { type: 'string', enum: ['start', 'stop', 'status'], description: 'Lockdown action.' },
        reason: schema.string('Reason for audit.'),
        requester_id: schema.string('Requester Steam ID.'),
        requester_role: schema.role,
        admin_token: schema.string('Optional server admin token when configured.'),
      },
      required: ['action'],
    },
  },
  {
    name: 'rust_agent_status',
    description: 'Show DuckBot MCP bridge status and agent interchangeability details.',
    inputSchema: { type: 'object', properties: {} },
  },
];

function resolveCamera(state: DuckBotState, value: string): CameraState | undefined {
  const lower = value.toLowerCase();
  return state.cameras.get(value)
    ?? Array.from(state.cameras.values()).find((camera) => camera.id.toLowerCase() === lower)
    ?? Array.from(state.cameras.values()).find((camera) => camera.name.toLowerCase().includes(lower) || camera.location.toLowerCase().includes(lower));
}

function sendToRust(state: DuckBotState, message: JsonObject): boolean {
  const withMeta = { ...message, mcp_time: nowIso() };
  state.outboundMessages.push(withMeta);

  let sent = false;
  for (const client of state.rustClients) {
    if (client.readyState !== WebSocket.OPEN) continue;
    client.send(JSON.stringify(withMeta));
    sent = true;
  }
  return sent;
}

function recordActivity(state: DuckBotState, category: string, action: string, details: string, playerId?: string, playerName?: string, maxHistory = DEFAULT_CONFIG.maxHistory): void {
  pushLimited(state.activity, { time: nowIso(), category, action, details, playerId, playerName }, maxHistory);
}

export async function handleToolCall(
  name: string,
  args: JsonObject = {},
  state: DuckBotState = defaultState,
  config: ServerConfig = DEFAULT_CONFIG,
) {
  switch (name) {
    case 'rust_computer_context': {
      const player = findPlayer(state, requiredString(args, 'player_id') ?? requiredString(args, 'player_name'));
      const role = player?.role ?? requesterRole(state, args);
      return jsonResult({
        player: player ?? null,
        role,
        bridgeConnected: state.rustClients.size > 0,
        capabilities: {
          user: ['chat', 'view_cameras', 'server_status', 'market', 'kit_list', 'dice', '8ball', 'tips'],
          vip: ['ptz_camera_control', 'security_scan', 'alerts', 'markers', 'base_status'],
          mod: ['activity_review', 'player_lookup', 'kick'],
          admin: ['admin_commands', 'ban', 'lockdown', 'automation', 'kit_grants'],
        },
        activeAlerts: Array.from(state.alerts.values()).filter((alert) => !alert.acknowledged).length,
        cameras: state.cameras.size,
        players: state.players.size,
      });
    }

    case 'rust_list_cameras':
    case 'rust_get_cameras':
      return jsonResult({ cameras: Array.from(state.cameras.values()), count: state.cameras.size });

    case 'rust_view_camera': {
      const cameraId = requiredString(args, 'camera_id');
      const playerId = requiredString(args, 'player_id');
      if (!cameraId || !playerId) return textResult('camera_id and player_id are required.', true);
      const camera = resolveCamera(state, cameraId);
      if (!camera) return textResult(`Camera not found: ${cameraId}`, true);
      if (!camera.online || !camera.hasPower) return textResult(`Camera unavailable: ${camera.name}`, true);
      camera.viewCount = (camera.viewCount ?? 0) + 1;
      camera.lastActivity = nowIso();
      const sent = sendToRust(state, { type: 'view_camera_request', camera_id: camera.id, player_id: playerId });
      recordActivity(state, 'camera', 'view', `${playerId} requested ${camera.name}`, playerId, undefined, config.maxHistory);
      return jsonResult({ status: sent ? 'sent_to_rust' : 'queued_no_rust_client', camera, player_id: playerId });
    }

    case 'rust_control_camera': {
      const denied = requireRole(state, args, 'vip');
      if (denied) return denied;
      const cameraId = requiredString(args, 'camera_id');
      const action = requiredString(args, 'action');
      const playerId = requiredString(args, 'player_id');
      if (!cameraId || !action || !playerId) return textResult('camera_id, action, and player_id are required.', true);
      const camera = resolveCamera(state, cameraId);
      if (!camera) return textResult(`Camera not found: ${cameraId}`, true);
      if (!camera.isPTZ) return textResult(`${camera.name} does not support PTZ control.`, true);
      const sent = sendToRust(state, { type: 'camera_control', camera_id: camera.id, action, player_id: playerId });
      recordActivity(state, 'camera', 'control', `${playerId} ${action} ${camera.name}`, playerId, undefined, config.maxHistory);
      return jsonResult({ status: sent ? 'sent_to_rust' : 'queued_no_rust_client', camera_id: camera.id, action });
    }

    case 'rust_get_camera_snapshot': {
      const cameraId = requiredString(args, 'camera_id');
      if (!cameraId) return textResult('camera_id is required.', true);
      const camera = resolveCamera(state, cameraId);
      if (!camera) return textResult(`Camera not found: ${cameraId}`, true);
      const sent = sendToRust(state, { type: 'camera_snapshot', camera_id: camera.id, player_id: optionalString(args, 'player_id') });
      return jsonResult({ status: sent ? 'snapshot_requested' : 'queued_no_rust_client', camera });
    }

    case 'rust_list_players':
    case 'rust_get_online_players': {
      const roleFilter = optionalString(args, 'role_filter', 'all');
      const players = Array.from(state.players.values()).filter((player) => roleFilter === 'all' || player.role === roleFilter);
      return jsonResult({ players, count: players.length });
    }

    case 'rust_get_player_info': {
      const player = findPlayer(state, requiredString(args, 'player_id') ?? requiredString(args, 'player_name'));
      if (!player) return textResult('Player not found.', true);
      const history = state.chatHistory.filter((message) => message.playerId === player.id || message.playerName === player.name).slice(-20);
      return jsonResult({ player, recentChat: history });
    }

    case 'rust_find_player': {
      const pattern = requiredString(args, 'pattern');
      if (!pattern) return textResult('pattern is required.', true);
      const lower = pattern.toLowerCase();
      const players = Array.from(state.players.values()).filter((player) => player.id.includes(pattern) || player.name.toLowerCase().includes(lower));
      return jsonResult({ players, count: players.length });
    }

    case 'rust_server_status':
    case 'rust_get_server_status':
      return jsonResult({ ...state.server, bridgeClients: state.rustClients.size, queuedMessages: state.outboundMessages.length });

    case 'rust_map_overview':
      return jsonResult({
        server: state.server,
        markerCount: state.markers.size,
        publicMarkerCount: Array.from(state.markers.values()).filter((marker) => marker.visible).length,
        playerCount: state.players.size,
        monuments: state.server.monuments ?? [],
        monumentCount: state.server.monuments?.length ?? 0,
      });

    case 'rust_route_advice': {
      const playerId = optionalString(args, 'player_id');
      const fromGrid = optionalString(args, 'from_grid');
      const to = requiredString(args, 'to');
      if (!to) return textResult('to is required.', true);
      const player = playerId ? state.players.get(playerId) : undefined;
      const origin = fromGrid || player?.position || 'unknown';
      const nearest = player?.monument;
      const matchingMonuments = (state.server.monuments ?? []).filter((monument) => monument.name.toLowerCase().includes(to.toLowerCase()) || (monument.grid ?? '').toLowerCase().includes(to.toLowerCase()));
      return jsonResult({ origin, target: to, nearestMonument: nearest, matchingMonuments, player: player ?? null, guidance: 'Use this structured context with LM Studio to produce route/safety advice.' });
    }

    case 'rust_monument_advice_context': {
      const monumentName = requiredString(args, 'monument');
      if (!monumentName) return textResult('monument is required.', true);
      const monuments = state.server.monuments ?? [];
      const monument = monuments.find((item) => item.name.toLowerCase().includes(monumentName.toLowerCase()));
      const nearby = monuments.filter((item) => item !== monument).slice(0, 6);
      return jsonResult({ monument: monument ?? null, fromGrid: optionalString(args, 'from_grid'), nearbyMonuments: nearby, guidance: 'Use this with LM Studio to explain loot, risk, travel, and progression relevance.' });
    }

    case 'rust_map_marker_catalog': {
      const playerId = optionalString(args, 'player_id');
      const markers = Array.from(state.markers.values()).filter((marker) => !playerId || marker.ownerId === playerId);
      return jsonResult({
        markers,
        count: markers.length,
        publicMarkers: markers.filter((marker) => marker.visible),
        ownedMarkers: playerId ? markers.filter((marker) => marker.ownerId === playerId) : [],
        icons: Array.from(new Set(markers.map((marker) => marker.icon))),
      });
    }

    case 'rust_chat_moderation_context': {
      const denied = requireRole(state, args, 'mod');
      if (denied) return denied;
      const playerId = optionalString(args, 'player_id');
      const limit = Math.min(optionalNumber(args, 'limit', 20), 100);
      const chat = state.chatHistory.filter((entry) => !playerId || entry.target === playerId || entry.sender === playerId).slice(-limit);
      const activity = state.activity.filter((entry) => !playerId || entry.playerId === playerId).slice(-limit);
      const reports = state.activity.filter((entry) => entry.category === 'moderation' && entry.action === 'Report' && (!playerId || entry.details.toLowerCase().includes(playerId.toLowerCase()))).slice(-limit);
      return jsonResult({ chat, activity, reports, guidance: 'Use for AI moderation of spam, harassment, scams, suspicious coordination, and player reports. Do not treat this as proof of cheating by itself.' });
    }

    case 'rust_rcon_command_catalog': {
      const category = optionalString(args, 'category');
      const safety = optionalString(args, 'safety');
      const catalog = rconCatalogForConfig(config).filter((entry) => (!category || entry['category'] === category) && (!safety || entry['safety'] === safety));
      return jsonResult({ commands: catalog, count: catalog.length, readOnlyCommands: catalog.filter((entry) => entry['safety'] === 'read-only').map((entry) => entry['command']), actionCommands: catalog.filter((entry) => entry['safety'] !== 'read-only').map((entry) => entry['command']), guidance: 'Use rust_rcon_query for read-only commands. Use rust_rcon_command only for explicit admin actions after confirming target/action.' });
    }

    case 'rust_rcon_query': {
      const denied = requireRole(state, args, 'admin');
      if (denied) return denied;
      const command = sanitizeRconQuery(requiredString(args, 'command') ?? '');
      if (!command) return textResult('Only safe read-only RCON queries are allowed for rust_rcon_query.', true);
      const requestId = `rcon_${Date.now()}_${Math.random().toString(16).slice(2)}`;
      const sent = sendToRust(state, { type: 'admin_command', command, admin_name: optionalString(args, 'requester_id', 'mcp-query'), request_id: requestId });
      const latest = latestRconResponse(state, command);
      recordActivity(state, 'admin', 'rcon_query', command, optionalString(args, 'requester_id'), undefined, config.maxHistory);
      return jsonResult({ status: sent ? 'sent_to_rust' : 'queued_no_rust_client', requestId, command, latestResponse: latest ? { ...latest, parsed: parseRconMessage(latest.message) } : null, note: 'RCON responses are asynchronous; call rust_rcon_history if this response predates the query.' });
    }

    case 'rust_rcon_history': {
      const denied = requireRole(state, args, 'admin');
      if (denied) return denied;
      const limit = Math.min(optionalNumber(args, 'limit', 10), 50);
      return jsonResult({ responses: state.rconResponses.slice(-limit).map((response) => ({ ...response, parsed: parseRconMessage(response.message) })), count: Math.min(state.rconResponses.length, limit) });
    }

    case 'rust_get_server_info':
      return jsonResult({ ...state.server, bridgeClients: state.rustClients.size, queuedMessages: state.outboundMessages.length, latestRcon: latestRconResponse(state) ?? null });

    case 'rust_get_player_positions': {
      const requesterId = optionalString(args, 'requester_id');
      const playerId = optionalString(args, 'player_id');
      if (!playerId || playerId !== requesterId) {
        const denied = requireRole(state, args, 'mod');
        if (denied) return denied;
      }
      const players = Array.from(state.players.values())
        .filter((player) => !playerId || player.id === playerId)
        .map((player) => ({ id: player.id, name: player.name, role: player.role, online: player.online, position: player.position, ping: player.ping, connectedAt: player.connectedAt }));
      return jsonResult({ players, count: players.length });
    }

    case 'rust_get_monument_info':
      return jsonResult({ monuments: state.server.monuments ?? [], count: state.server.monuments?.length ?? 0 });

    case 'rust_bridge_status':
      return jsonResult({ bridgeClients: state.rustClients.size, queuedMessages: state.outboundMessages.length, bridgeStartedAt: state.bridgeStartedAt, lastHeartbeat: state.server.lastUpdated, mcpConnected: state.server.mcpConnected, rconConnected: state.server.rconConnected, latestRcon: latestRconResponse(state) ?? null });

    case 'rust_admin_event_create': {
      const denied = requireRole(state, args, 'mod');
      if (denied) return denied;
      const eventType = requiredString(args, 'event_type') ?? 'unknown';
      const reqId = requiredString(args, 'requester_id') ?? 'mcp';
      const reqRole = optionalString(args, 'requester_role', 'mod');
      recordActivity(state, 'event', eventType, `Started by ${reqId}`, reqId, undefined, config.maxHistory);
      const sent = sendToRust(state, { type: 'mcp_event_create', event_type: eventType, args: optionalString(args, 'args', ''), requester_id: reqId, requester_role: reqRole });
      return jsonResult({ status: sent ? 'sent' : 'queued', event_type: eventType, args: optionalString(args, 'args', ''), started_by: reqId });
    }

    case 'rust_admin_event_list': {
      const events = state.activity
        .filter(e => e.category === 'event')
        .slice(-10)
        .map(e => ({ action: e.action, details: e.details, playerId: e.playerId }));
      return jsonResult({ active_events: events, count: events.length, note: 'Event state is maintained by the Rust plugin; this shows recent event history from activity log.' });
    }

    case 'rust_admin_event_cancel': {
      const denied = requireRole(state, args, 'mod');
      if (denied) return denied;
      const eventType = requiredString(args, 'event_type') ?? 'unknown';
      const reqId = requiredString(args, 'requester_id') ?? 'mcp';
      recordActivity(state, 'event', 'cancelled', eventType, reqId, undefined, config.maxHistory);
      const sent = sendToRust(state, { type: 'mcp_event_cancel', event_type: eventType, requester_id: reqId });
      return jsonResult({ status: sent ? 'sent' : 'queued', event_type: eventType, cancelled_by: reqId });
    }

    case 'rust_economy_status': {
      const events = state.activity.filter(e => e.category === 'event').slice(-5).map(e => ({ type: e.action, details: e.details }));
      const vipActivity = state.activity.filter(e => e.action.includes('VIP') || e.details.includes('VIP')).slice(-5);
      return jsonResult({ active_events: events, vip_activity: vipActivity, note: 'Economy state (daily timers, prize pools, active guess games) is maintained by the Rust plugin. This shows recent event and VIP reward history.' });
    }

    case 'rust_vip_bonus_info': {
      return jsonResult({ vip_bonus_multiplier: 1.5, applies_to: ['daily_reward_scrap', 'daily_reward_rp', 'killstreak_scrap'], note: 'VIP multiplier is configured in the Rust plugin config. Check RustDuckBot.json for the current VipBonusMultiplier value.' });
    }

    case 'rust_lucky_block_prizes': {
      return jsonResult({ tiers: [{ rarity: 'EPIC (5%)', items: ['3x explosive.timed'] }, { rarity: 'RARE (10%)', items: ['1x metal.plate.torso'] }, { rarity: 'UNCOMMON (20%)', items: ['800x scrap'] }, { rarity: 'COMMON (65%)', items: ['400x scrap', '150x scrap'] }], cost: 200, requirement: 'VIP or rustduckbot.vip permission' });
    }

    case 'rust_get_player_stats': {
      const playerId = requiredString(args, 'player_id');
      const playerActivity = state.activity.filter(e => e.playerId === playerId);
      const kills = playerActivity.filter(e => e.action === 'kill').length;
      const deaths = playerActivity.filter(e => e.action === 'death').length;
      const scrapTotal = playerActivity.filter(e => e.action === 'scrap').reduce((sum, e) => sum + (parseInt(e.details.split('+').filter(Boolean)[0]?.replace(/\D/g, '') ?? '0')), 0);
      const dailyClaims = playerActivity.filter(e => e.action === 'daily').length;
      const eventsWon = playerActivity.filter(e => e.action === 'won').length;
      const kd = deaths > 0 ? (kills / deaths).toFixed(2) : kills > 0 ? kills.toFixed(2) : '0.00';
      const activeScore = playerActivity.length;
      return jsonResult({ player_id: playerId, kills, deaths, kd, scrap_total: scrapTotal, daily_claims: dailyClaims, events_won: eventsWon, activity_score: activeScore, note: 'Detailed stats (kills/deaths/scrap per session) are tracked by the Rust plugin. This summary is from the MCP activity log.' });
    }

    case 'rust_leaderboard': {
      const category = optionalString(args, 'category', 'kills');
      const limit = Math.min(optionalInteger(args, 'limit', 10), 50);
      const playerScores: Record<string, number> = {};
      for (const entry of state.activity) {
        if (!entry.playerId) continue;
        if (category === 'kills') { if (entry.action === 'kill') playerScores[entry.playerId] = (playerScores[entry.playerId] ?? 0) + 1; }
        else if (category === 'events') { if (entry.action === 'won') playerScores[entry.playerId] = (playerScores[entry.playerId] ?? 0) + 1; }
        else if (category === 'activity') { playerScores[entry.playerId] = (playerScores[entry.playerId] ?? 0) + 1; }
      }
      const sorted = Object.entries(playerScores).sort((a, b) => b[1] - a[1]).slice(0, limit);
      return jsonResult({ category, entries: sorted.map(([pid, score], i) => ({ rank: i + 1, player_id: pid, score })), total_players: sorted.length });
    }

    case 'rust_shop_listings': {
      const filter = optionalString(args, 'filter', '');
      // Shop listings are maintained by the Rust plugin; reflect recent shop activity from the log
      const shopActivity = state.activity.filter(e => e.category === 'economy' && (e.action === 'shop_add' || e.action === 'shop_buy'));
      const listings = shopActivity.slice(-20).map(e => ({ action: e.action, details: e.details, playerId: e.playerId, time: e.time }));
      return jsonResult({ listings, count: listings.length, active_count: 0, note: 'Live shop listings (item name, price, seller) are maintained by the Rust plugin. Use /db shop list in-game to see them. This shows recent shop activity.' });
    }

    case 'rust_guess_game_status': {
      const playerId = requiredString(args, 'player_id');
      const recentActivity = state.activity.filter(e => e.playerId === playerId && e.category === 'game' && e.action === 'guess').slice(-1);
      return jsonResult({ player_id: playerId, active_game: recentActivity.length > 0 ? { note: 'Active game state is maintained by the Rust plugin. Check /db guess in-game for current state.' } : null, guidance: 'Direct player to use /db guess in-game to check their current game state, guesses remaining, and prize pool.' });
    }

    case 'rust_chat_send':
    case 'rust_send_chat': {
      const message = requiredString(args, 'message');
      if (!message) return textResult('message is required.', true);
      const target = optionalString(args, 'target', 'global');
      const sender = optionalString(args, 'sender', 'DuckBot');
      const sent = sendToRust(state, { type: 'chat_send', message, target, sender });
      pushLimited(state.chatHistory, { sender, message, target, time: nowIso(), isAI: true }, config.maxHistory);
      return jsonResult({ status: sent ? 'sent_to_rust' : 'queued_no_rust_client', target, sender, message });
    }

    case 'rust_chat_history':
    case 'rust_get_recent_chat': {
      const playerId = requiredString(args, 'player_id');
      const limit = Math.min(optionalNumber(args, 'limit', 20), 100);
      const messages = playerId ? state.chatHistory.filter((message) => message.playerId === playerId).slice(-limit) : state.chatHistory.slice(-limit);
      return jsonResult({ messages, count: messages.length });
    }

    case 'rust_list_alerts': {
      const includeAcknowledged = Boolean(args['include_acknowledged']);
      const severity = requiredString(args, 'severity');
      const alerts = Array.from(state.alerts.values()).filter((alert) => {
        if (!includeAcknowledged && alert.acknowledged) return false;
        if (severity && alert.severity !== severity) return false;
        return true;
      });
      return jsonResult({ alerts, count: alerts.length });
    }

    case 'rust_ack_alert': {
      const denied = requireRole(state, args, 'vip');
      if (denied) return denied;
      const alertId = requiredString(args, 'alert_id');
      if (!alertId) return textResult('alert_id is required.', true);
      const alert = state.alerts.get(alertId);
      if (!alert) return textResult(`Alert not found: ${alertId}`, true);
      alert.acknowledged = true;
      alert.acknowledgedBy = optionalString(args, 'requester_id', 'mcp');
      sendToRust(state, { type: 'ack_alert', alert_id: alertId, requester_id: alert.acknowledgedBy });
      return jsonResult({ acknowledged: alert });
    }

    case 'rust_security_scan': {
      const denied = requireRole(state, args, 'vip');
      if (denied) return denied;
      const radius = optionalNumber(args, 'radius', 100);
      const sent = sendToRust(state, { type: 'security_scan', requester_id: optionalString(args, 'requester_id'), radius });
      return jsonResult({
        status: sent ? 'sent_to_rust' : 'local_summary_only',
        radius,
        onlinePlayers: state.players.size,
        activeAlerts: Array.from(state.alerts.values()).filter((alert) => !alert.acknowledged),
        onlineCameras: Array.from(state.cameras.values()).filter((camera) => camera.online && camera.hasPower).length,
      });
    }

    case 'rust_list_activity': {
      const playerId = requiredString(args, 'player_id');
      if (!playerId) {
        const denied = requireRole(state, args, 'mod');
        if (denied) return denied;
      }
      const category = requiredString(args, 'category');
      const limit = Math.min(optionalNumber(args, 'limit', 25), 100);
      const entries = state.activity
        .filter((entry) => !category || entry.category === category)
        .filter((entry) => !playerId || entry.playerId === playerId)
        .slice(-limit);
      return jsonResult({ entries, count: entries.length });
    }

    case 'rust_list_map_markers': {
      const playerId = requiredString(args, 'player_id');
      const markers = Array.from(state.markers.values()).filter((marker) => marker.visible || !playerId || marker.ownerId === playerId);
      return jsonResult({ markers, count: markers.length });
    }

    case 'rust_add_map_marker': {
      const denied = requireRole(state, args, 'vip');
      if (denied) return denied;
      const nameArg = requiredString(args, 'name');
      const position = requiredString(args, 'position');
      if (!nameArg || !position) return textResult('name and position are required.', true);
      const marker: MapMarker = {
        id: `marker_${Date.now()}`,
        name: nameArg,
        position,
        color: optionalString(args, 'color', 'yellow'),
        icon: optionalString(args, 'icon', 'pin'),
        ownerId: optionalString(args, 'requester_id'),
        visible: true,
      };
      state.markers.set(marker.id, marker);
      const sent = sendToRust(state, { type: 'map_marker_add', marker });
      return jsonResult({ status: sent ? 'sent_to_rust' : 'stored_locally', marker });
    }

    case 'rust_list_automation_rules':
      return jsonResult({ rules: Array.from(state.automationRules.values()), count: state.automationRules.size });

    case 'rust_set_automation_rule': {
      const denied = requireRole(state, args, 'admin') ?? requireAdminToken(config, args);
      if (denied) return denied;
      const ruleId = requiredString(args, 'rule_id');
      const action = requiredString(args, 'action');
      if (!ruleId || !action) return textResult('rule_id and action are required.', true);
      const rule = state.automationRules.get(ruleId);
      if (!rule) return textResult(`Rule not found: ${ruleId}`, true);
      if (action === 'enable') rule.enabled = true;
      if (action === 'disable') rule.enabled = false;
      if (action === 'delete') state.automationRules.delete(ruleId);
      if (action === 'run') rule.lastTriggered = nowIso();
      const sent = sendToRust(state, { type: 'automation_rule', rule_id: ruleId, action });
      return jsonResult({ status: sent ? 'sent_to_rust' : 'stored_locally', rule, deleted: action === 'delete' });
    }

    case 'rust_base_status': {
      const playerId = requiredString(args, 'player_id');
      const bases = playerId ? state.bases.filter((base) => base.ownerId === playerId) : state.bases;
      return jsonResult({ bases, count: bases.length });
    }

    case 'rust_market_listings': {
      const query = requiredString(args, 'query')?.toLowerCase();
      const includeUnavailable = Boolean(args['include_unavailable']);
      const listings = state.marketListings
        .filter((listing) => includeUnavailable || listing.available)
        .filter((listing) => !query || listing.itemName.toLowerCase().includes(query));
      return jsonResult({ listings, count: listings.length });
    }

    case 'rust_list_kits': {
      const category = requiredString(args, 'category')?.toLowerCase();
      const kits = DEFAULT_KITS.filter((kit) => !category || kit.category.toLowerCase() === category);
      return jsonResult({ kits, count: kits.length });
    }

    case 'rust_give_kit': {
      const denied = requireRole(state, args, 'admin') ?? requireAdminToken(config, args);
      if (denied) return denied;
      const playerId = requiredString(args, 'player_id');
      const kitName = requiredString(args, 'kit_name')?.toLowerCase();
      if (!playerId || !kitName) return textResult('player_id and kit_name are required.', true);
      const kit = DEFAULT_KITS.find((item) => item.name === kitName);
      if (!kit) return textResult(`Unknown kit: ${kitName}`, true);
      const sent = sendToRust(state, { type: 'kit_give', player_id: playerId, kit_name: kit.name, requester_id: optionalString(args, 'requester_id') });
      recordActivity(state, 'kits', 'grant', `${optionalString(args, 'requester_id', 'mcp-admin')} granted ${kit.name} to ${playerId}`, optionalString(args, 'requester_id'), undefined, config.maxHistory);
      return jsonResult({ status: sent ? 'sent_to_rust' : 'queued_no_rust_client', player_id: playerId, kit });
    }

    case 'rust_roll_dice': {
      const sides = boundedInteger(optionalNumber(args, 'sides', 100), 2, 10000);
      const count = boundedInteger(optionalNumber(args, 'count', 1), 1, 20);
      const rolls = Array.from({ length: count }, () => Math.floor(Math.random() * sides) + 1);
      const total = rolls.reduce((sum, value) => sum + value, 0);
      const expression = `${count}d${sides}`;
      const playerId = optionalString(args, 'player_id');
      const message = `DuckBot rolled ${expression}: ${rolls.join(', ')}${count > 1 ? ` = ${total}` : ''}`;
      let status = 'local_only';
      if (Boolean(args['announce'])) {
        const sent = sendToRust(state, { type: 'chat_send', message, target: playerId || 'global', sender: 'DuckBot' });
        status = sent ? 'sent_to_rust' : 'queued_no_rust_client';
        pushLimited(state.chatHistory, { sender: 'DuckBot', message, target: playerId || 'global', time: nowIso(), isAI: true }, config.maxHistory);
      }
      return jsonResult({ status, expression, sides, count, rolls, total, message });
    }

    case 'rust_8ball': {
      const question = requiredString(args, 'question');
      if (!question) return textResult('question is required.', true);
      const answer = randomItem(EIGHT_BALL_RESPONSES);
      const playerId = optionalString(args, 'player_id');
      const message = `DuckBot 8-ball: ${answer}`;
      let status = 'local_only';
      if (Boolean(args['announce'])) {
        const sent = sendToRust(state, { type: 'chat_send', message, target: playerId || 'global', sender: 'DuckBot' });
        status = sent ? 'sent_to_rust' : 'queued_no_rust_client';
        pushLimited(state.chatHistory, { sender: 'DuckBot', message, target: playerId || 'global', time: nowIso(), isAI: true }, config.maxHistory);
      }
      return jsonResult({ status, question, answer, message });
    }

    case 'rust_player_tip': {
      const requestedCategory = requiredString(args, 'category')?.toLowerCase() ?? 'starter';
      const tips = PLAYER_TIPS[requestedCategory] ?? PLAYER_TIPS['starter'];
      const category = PLAYER_TIPS[requestedCategory] ? requestedCategory : 'starter';
      const tip = randomItem(tips);
      const playerId = optionalString(args, 'player_id');
      const message = `DuckBot tip: ${tip}`;
      let status = 'local_only';
      if (Boolean(args['announce'])) {
        const sent = sendToRust(state, { type: 'chat_send', message, target: playerId || 'global', sender: 'DuckBot' });
        status = sent ? 'sent_to_rust' : 'queued_no_rust_client';
        pushLimited(state.chatHistory, { sender: 'DuckBot', message, target: playerId || 'global', time: nowIso(), isAI: true }, config.maxHistory);
      }
      return jsonResult({ status, category, tip, message });
    }

    case 'rust_admin_command':
    case 'rust_rcon_command':
    case 'rust_execute_command': {
      const denied = requireRole(state, args, 'admin') ?? requireAdminToken(config, args);
      if (denied) return denied;
      const command = requiredString(args, 'command');
      if (!command) return textResult('command is required.', true);
      if (!commandAllowed(config, command)) return textResult(`Command is not whitelisted: ${command.split(/\s+/)[0] ?? command}`, true);
      const adminName = optionalString(args, 'player_name', optionalString(args, 'requester_id', 'mcp-admin'));
      const sent = sendToRust(state, { type: 'admin_command', command, admin_name: adminName });
      recordActivity(state, 'admin', 'command', `${adminName}: ${command}`, optionalString(args, 'requester_id'), adminName, config.maxHistory);
      return jsonResult({ status: sent ? 'sent_to_rust' : 'queued_no_rust_client', command, admin_name: adminName });
    }

    case 'rust_kick_player': {
      const denied = requireRole(state, args, 'mod');
      if (denied) return denied;
      const target = requiredString(args, 'player_id');
      if (!target) return textResult('player_id is required.', true);
      const reason = optionalString(args, 'reason', 'Kicked by staff');
      const sent = sendToRust(state, { type: 'kick_player', player_id: target, reason, requester_id: optionalString(args, 'requester_id') });
      return jsonResult({ status: sent ? 'sent_to_rust' : 'queued_no_rust_client', player_id: target, reason });
    }

    case 'rust_ban_player': {
      const denied = requireRole(state, args, 'admin') ?? requireAdminToken(config, args);
      if (denied) return denied;
      const target = requiredString(args, 'player_id');
      const reason = requiredString(args, 'reason');
      if (!target || !reason) return textResult('player_id and reason are required.', true);
      const duration = optionalString(args, 'duration', 'perm');
      const sent = sendToRust(state, { type: 'ban_player', player_id: target, reason, duration, requester_id: optionalString(args, 'requester_id') });
      return jsonResult({ status: sent ? 'sent_to_rust' : 'queued_no_rust_client', player_id: target, reason, duration });
    }

    case 'rust_lockdown': {
      const denied = requireRole(state, args, 'admin') ?? requireAdminToken(config, args);
      if (denied) return denied;
      const action = requiredString(args, 'action');
      if (!action) return textResult('action is required.', true);
      const sent = sendToRust(state, { type: 'lockdown', action, reason: optionalString(args, 'reason'), requester_id: optionalString(args, 'requester_id') });
      return jsonResult({ status: sent ? 'sent_to_rust' : 'queued_no_rust_client', action });
    }

    case 'rust_agent_status':
      return jsonResult({
        name: 'rust-duckbot-mcp',
        version: VERSION,
        bridge: {
          clients: state.rustClients.size,
          startedAt: state.bridgeStartedAt,
          queuedMessages: state.outboundMessages.length,
        },
        interchangeableAgents: ['DuckBot/OpenClaw', 'Codex', 'Claude Desktop', 'Cursor', 'any MCP client over stdio'],
        transports: ['stdio MCP', 'websocket bridge'],
      });

    default:
      return textResult(`Unknown tool: ${name}`, true);
  }
}

function valueAsString(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback;
}

function valueAsNumber(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function normalizeCamera(raw: JsonObject): CameraState {
  const id = valueAsString(raw['id'] ?? raw['Id'] ?? raw['camera_id'] ?? raw['cameraId'], `camera_${Date.now()}`);
  return {
    id,
    name: valueAsString(raw['name'] ?? raw['Name'], id),
    location: valueAsString(raw['location'] ?? raw['Location'], 'Unknown'),
    monument: valueAsString(raw['monument'] ?? raw['Monument'], undefined as unknown as string),
    online: raw['online'] !== false && raw['Online'] !== false,
    hasPower: raw['hasPower'] !== false && raw['HasPower'] !== false,
    isPTZ: Boolean(raw['isPTZ'] ?? raw['IsPTZ'] ?? raw['ptz']),
    viewCount: valueAsNumber(raw['viewCount'] ?? raw['ViewCount'], 0),
    lastActivity: valueAsString(raw['lastActivity'] ?? raw['LastActivity'], undefined as unknown as string),
  };
}

function normalizePlayer(raw: JsonObject): PlayerState {
  const id = valueAsString(raw['id'] ?? raw['playerId'] ?? raw['player_id'] ?? raw['UserIDString'], `player_${Date.now()}`);
  return {
    id,
    name: valueAsString(raw['name'] ?? raw['playerName'] ?? raw['player_name'] ?? raw['displayName'], id),
    role: normalizeRole(raw['role']),
    ping: valueAsNumber(raw['ping'], 0),
    connectedAt: valueAsString(raw['connectedAt'] ?? raw['connected_at'], nowIso()),
    currentCamera: valueAsString(raw['currentCamera'] ?? raw['current_camera'], undefined as unknown as string),
    online: raw['online'] !== false,
    position: valueAsString(raw['position'], undefined as unknown as string),
    monument: valueAsString(raw['nearestMonument'] ?? raw['monument'], undefined as unknown as string),
  };
}

export function handleRustMessage(raw: JsonObject, state: DuckBotState = defaultState, config: ServerConfig = DEFAULT_CONFIG): void {
  const type = valueAsString(raw['type']);
  switch (type) {
    case 'rust_hello':
    case 'mcp_hello':
    case 'heartbeat': {
      state.server.mcpConnected = true;
      state.server.lastUpdated = nowIso();
      // Store live server metrics from heartbeat
      state.server.fps = valueAsNumber((raw as JsonObject)['fps'], 0);
      state.server.uptime = valueAsString((raw as JsonObject)['uptime'], '0h');
      const players = raw['players'];
      if (Array.isArray(players)) {
        state.players.clear();
        for (const player of players) {
          if (typeof player === 'object' && player) {
            const normalized = normalizePlayer(player as JsonObject);
            state.players.set(normalized.id, normalized);
          }
        }
      }
      state.server.players = valueAsNumber(raw['playerCount'] ?? raw['playersOnline'], state.players.size);
      state.server.rconConnected = Boolean(raw['rconConnected'] ?? state.server.rconConnected);
      state.server.serverName = valueAsString(raw['serverName'], state.server.serverName);
      state.server.serverSeed = valueAsNumber(raw['serverSeed'], state.server.serverSeed ?? 0);
      state.server.worldSize = valueAsNumber(raw['worldSize'], state.server.worldSize ?? 0);
      state.server.serverPvE = Boolean(raw['serverPvE'] ?? state.server.serverPvE);
      state.server.entityCount = valueAsNumber(raw['entityCount'], state.server.entityCount ?? 0);
      state.server.sleepingPlayers = valueAsNumber(raw['sleepingPlayers'] ?? raw['sleeping'], state.server.sleepingPlayers ?? 0);
      state.server.sleeping = state.server.sleepingPlayers;
      const monuments = raw['monuments'];
      if (Array.isArray(monuments)) state.server.monuments = monuments.map((item) => {
        const monument = item as JsonObject;
        return { name: valueAsString(monument['name']), position: valueAsString(monument['position']), grid: valueAsString(monument['grid']) };
      });
      break;
    }

    case 'player_list': {
      const players = raw['players'];
      if (Array.isArray(players)) {
        state.players.clear();
        for (const player of players) {
          if (typeof player === 'object' && player) {
            const normalized = normalizePlayer(player as JsonObject);
            state.players.set(normalized.id, normalized);
          }
        }
      }
      state.server.players = state.players.size;
      state.server.lastUpdated = nowIso();
      break;
    }

    case 'player_joined': {
      const player = normalizePlayer(raw);
      player.online = true;
      state.players.set(player.id, player);
      recordActivity(state, 'system', 'player_joined', `${player.name} joined`, player.id, player.name, config.maxHistory);
      break;
    }

    case 'player_left': {
      const id = valueAsString(raw['playerId'] ?? raw['player_id']);
      const player = findPlayer(state, id);
      if (player) player.online = false;
      recordActivity(state, 'system', 'player_left', `${valueAsString(raw['playerName'] ?? raw['player_name'] ?? raw['name'], id)} left`, id, undefined, config.maxHistory);
      break;
    }

    case 'player_chat':
    case 'ai_chat': {
      const playerId = valueAsString(raw['playerId'] ?? raw['player_id']);
      const playerName = valueAsString(raw['playerName'] ?? raw['player_name'], playerId);
      pushLimited(state.chatHistory, {
        playerId,
        playerName,
        sender: type === 'ai_chat' ? 'DuckBot' : playerName,
        role: normalizeRole(raw['role']),
        message: valueAsString(raw['message']),
        time: valueAsString(raw['time'], nowIso()),
        isAI: type === 'ai_chat',
      }, config.maxHistory);
      break;
    }

    case 'camera_update': {
      const cameras = raw['cameras'];
      if (Array.isArray(cameras)) {
        state.cameras.clear();
        for (const camera of cameras) {
          if (typeof camera === 'object' && camera) {
            const normalized = normalizeCamera(camera as JsonObject);
            state.cameras.set(normalized.id, normalized);
          }
        }
      }
      state.server.cameras = state.cameras.size;
      state.server.lastUpdated = nowIso();
      break;
    }

    case 'camera_view':
    case 'camera_control': {
      const cameraId = valueAsString(raw['cameraId'] ?? raw['camera_id']);
      const camera = resolveCamera(state, cameraId);
      if (camera) camera.lastActivity = nowIso();
      recordActivity(state, 'camera', type, `${valueAsString(raw['playerName'] ?? raw['player_name'])} ${type} ${cameraId}`, valueAsString(raw['playerId'] ?? raw['player_id']), valueAsString(raw['playerName'] ?? raw['player_name']), config.maxHistory);
      break;
    }

    case 'alert': {
      const id = valueAsString(raw['alertId'] ?? raw['alert_id'] ?? raw['id'], `alert_${Date.now()}`);
      state.alerts.set(id, {
        id,
        type: valueAsString(raw['alertType'] ?? raw['alert_type'] ?? raw['category'], 'system'),
        severity: valueAsString(raw['severity'], 'medium'),
        title: valueAsString(raw['title'], 'Alert'),
        message: valueAsString(raw['message']),
        time: valueAsString(raw['time'], nowIso()),
        acknowledged: false,
        location: valueAsString(raw['location'], undefined as unknown as string),
      });
      state.server.alerts = state.alerts.size;
      break;
    }

    case 'activity': {
      recordActivity(
        state,
        valueAsString(raw['category'], 'system'),
        valueAsString(raw['action'], 'event'),
        valueAsString(raw['details'], ''),
        valueAsString(raw['playerId'] ?? raw['player_id']),
        valueAsString(raw['playerName'] ?? raw['player_name']),
        config.maxHistory,
      );
      break;
    }

    case 'server_status': {
      state.server = {
        ...state.server,
        uptime: valueAsString(raw['uptime'], state.server.uptime),
        fps: valueAsNumber(raw['fps'], state.server.fps),
        players: valueAsNumber(raw['players'] ?? raw['playerCount'], state.players.size),
        sleeping: valueAsNumber(raw['sleeping'], state.server.sleeping ?? 0),
        cameras: valueAsNumber(raw['cameras'], state.cameras.size),
        alerts: valueAsNumber(raw['alerts'], state.alerts.size),
        memoryMB: valueAsNumber(raw['memoryMB'] ?? raw['memory'], state.server.memoryMB ?? 0),
        mcpConnected: true,
        rconConnected: Boolean(raw['rconConnected'] ?? state.server.rconConnected),
        lastUpdated: nowIso(),
      };
      break;
    }

    case 'rcon_response': {
      const message = valueAsString(raw['message'] ?? raw['response'] ?? raw['body']);
      pushLimited(state.rconResponses, {
        requestId: valueAsString(raw['request_id'] ?? raw['requestId']),
        identifier: valueAsNumber(raw['identifier'] ?? raw['id'], 0),
        command: valueAsString(raw['command']),
        message,
        raw,
        source: valueAsString(raw['source'], 'rust-rcon'),
        time: valueAsString(raw['time'], nowIso()),
      }, config.maxHistory);
      state.server.rconConnected = true;
      state.server.lastUpdated = nowIso();
      break;
    }

    case 'automation_update': {
      const rules = raw['rules'];
      if (Array.isArray(rules)) {
        state.automationRules.clear();
        for (const rule of rules) {
          if (typeof rule === 'object' && rule) {
            const item = rule as JsonObject;
            const id = valueAsString(item['id'] ?? item['Id'], `rule_${Date.now()}`);
            state.automationRules.set(id, {
              id,
              name: valueAsString(item['name'] ?? item['Name'], id),
              trigger: valueAsString(item['trigger'] ?? item['Trigger']),
              condition: valueAsString(item['condition'] ?? item['Condition']),
              action: valueAsString(item['action'] ?? item['Action']),
              enabled: item['enabled'] !== false && item['Enabled'] !== false,
              priority: valueAsNumber(item['priority'] ?? item['Priority'], 0),
              lastTriggered: valueAsString(item['lastTriggered'] ?? item['LastTriggered'], undefined as unknown as string),
            });
          }
        }
      }
      break;
    }

    default:
      log('debug', `Unhandled Rust message type: ${type || '(missing)'}`);
  }
}

export function createMcpServer(state: DuckBotState, config: ServerConfig): Server {
  const server = new Server(
    { name: 'rust-duckbot-mcp', version: VERSION },
    { capabilities: { tools: {}, resources: {}, prompts: {} } },
  );

  server.setRequestHandler(ListToolsRequestSchema, async () => ({ tools: ALL_TOOLS }));
  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args = {} } = request.params;
    return handleToolCall(name, args as JsonObject, state, config);
  });

  server.setRequestHandler(ListResourcesRequestSchema, async () => ({
    resources: [
      { uri: 'rustduckbot://map/overview', name: 'Map/world overview', mimeType: 'application/json' },
      { uri: 'rustduckbot://map/markers', name: 'Map marker catalog', mimeType: 'application/json' },
      { uri: 'rustduckbot://chat/moderation', name: 'Chat moderation context', mimeType: 'application/json' },

      { uri: 'rustduckbot://cameras', name: 'Known cameras', mimeType: 'application/json' },
      { uri: 'rustduckbot://players', name: 'Known players', mimeType: 'application/json' },
      { uri: 'rustduckbot://rcon/catalog', name: 'RCON command catalog', mimeType: 'application/json' },
      { uri: 'rustduckbot://rcon/history', name: 'Recent RCON responses', mimeType: 'application/json' },
      { uri: 'rustduckbot://activity', name: 'Activity/audit log', mimeType: 'application/json' },
      { uri: 'rustduckbot://automation', name: 'Automation rules', mimeType: 'application/json' },
      { uri: 'rustduckbot://monuments', name: 'Known monuments', mimeType: 'application/json' },
      { uri: 'rustduckbot://events', name: 'Recent server events and loot games', mimeType: 'application/json' },
      { uri: 'rustduckbot://economy', name: 'Economy overview, VIP bonuses, event history', mimeType: 'application/json' },
      { uri: 'rustduckbot://leaderboard', name: 'Player leaderboards by kills, events, activity', mimeType: 'application/json' },
      { uri: 'rustduckbot://server/status', name: 'Live server status snapshot', mimeType: 'application/json' },
      { uri: 'rustduckbot://alerts', name: 'Active server alerts', mimeType: 'application/json' },
    ],
  }));

  server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
    const uri = request.params.uri;
    if (uri === 'rustduckbot://map/overview') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify({ server: state.server, markerCount: state.markers.size, markers: Array.from(state.markers.values()), players: Array.from(state.players.values()) }, null, 2) }] };
    if (uri === 'rustduckbot://map/markers') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify(Array.from(state.markers.values()), null, 2) }] };
    if (uri === 'rustduckbot://chat/moderation') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify({ chat: state.chatHistory.slice(-50), activity: state.activity.slice(-50) }, null, 2) }] };
    if (uri === 'rustduckbot://server/status') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify(state.server, null, 2) }] };
    if (uri === 'rustduckbot://cameras') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify(Array.from(state.cameras.values()), null, 2) }] };
    if (uri === 'rustduckbot://players') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify(Array.from(state.players.values()), null, 2) }] };
    if (uri === 'rustduckbot://alerts') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify(Array.from(state.alerts.values()), null, 2) }] };
    if (uri === 'rustduckbot://rcon/catalog') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify(rconCatalogForConfig(config), null, 2) }] };
    if (uri === 'rustduckbot://rcon/history') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify(state.rconResponses, null, 2) }] };
    if (uri === 'rustduckbot://activity') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify(state.activity, null, 2) }] };
    if (uri === 'rustduckbot://automation') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify(Array.from(state.automationRules.values()), null, 2) }] };
    if (uri === 'rustduckbot://monuments') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify(state.server.monuments ?? [], null, 2) }] };
    if (uri === 'rustduckbot://events') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify({ recent: state.activity.filter(e => e.category === 'event').slice(-20), active_count: state.activity.filter(e => e.category === 'event').length }, null, 2) }] };
    if (uri === 'rustduckbot://economy') return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify({ vip_bonus_multiplier: 1.5, vip_applies_to: ['daily_reward', 'killstreak'], recent_events: state.activity.filter(e => e.category === 'event').slice(-10), recent_games: state.activity.filter(e => e.category === 'game').slice(-10) }, null, 2) }] };
    if (uri === 'rustduckbot://leaderboard') {
      const playerScores: Record<string, number> = {};
      for (const entry of state.activity) { if (!entry.playerId) continue; playerScores[entry.playerId] = (playerScores[entry.playerId] ?? 0) + 1; }
      const sorted = Object.entries(playerScores).sort((a, b) => b[1] - a[1]).slice(0, 20).map(([pid, score], i) => ({ rank: i + 1, player_id: pid, activity_score: score }));
      return { contents: [{ uri, mimeType: 'application/json', text: JSON.stringify({ category: 'activity', entries: sorted }, null, 2) }] };
    }
    return { contents: [{ uri, mimeType: 'text/plain', text: `Unknown resource: ${uri}` }] };
  });

  server.setRequestHandler(ListPromptsRequestSchema, async () => ({
    prompts: [
      {
        name: 'rust_duckbot_player_reply',
        description: 'Generate a concise in-game DuckBot reply based on player role and context.',
        arguments: [
          { name: 'player_name', description: 'Player display name', required: true },
          { name: 'player_role', description: 'user, vip, mod, or admin', required: true },
          { name: 'message', description: 'Player message', required: true },
        ],
      },
      {
        name: 'rust_map_briefing',
        description: 'Prepare a map/world briefing for a player using DuckBot map tools.',
        arguments: [
          { name: 'player_name', description: 'Player display name', required: true },
          { name: 'player_role', description: 'user, vip, mod, or admin', required: true },
          { name: 'goal', description: 'Player goal such as loot, route, safety, monuments, or wipe prep', required: false },
        ],
      },
      {
        name: 'rust_route_planner',
        description: 'Plan a route between a player origin and target grid/monument using structured map context.',
        arguments: [
          { name: 'origin', description: 'Starting grid or player location', required: true },
          { name: 'target', description: 'Destination grid or monument', required: true },
          { name: 'player_role', description: 'user, vip, mod, or admin', required: false },
        ],
      },
      {
        name: 'rust_monument_briefing',
        description: 'Explain a Rust monument with progression, loot, and travel context.',
        arguments: [
          { name: 'monument', description: 'Monument name', required: true },
          { name: 'player_role', description: 'user, vip, mod, or admin', required: false },
        ],
      },
      {
        name: 'rust_admin_world_review',
        description: 'Review live world/server context for admins before taking actions.',
        arguments: [
          { name: 'admin_name', description: 'Admin display name', required: true },
          { name: 'focus', description: 'Focus area such as players, map, alerts, routes, moderation', required: false },
        ],
      },
    ],
  }));

  server.setRequestHandler(GetPromptRequestSchema, async (request) => {
    const args = request.params.arguments ?? {};
    if (request.params.name === 'rust_duckbot_player_reply') {
      return {
        description: 'RustDuckBot player reply',
        messages: [{
          role: 'user',
          content: {
            type: 'text',
            text: `You are DuckBot inside a Rust computer station. Reply concisely for chat.\nPlayer: ${args['player_name']}\nRole: ${args['player_role']}\nMessage: ${args['message']}\nUse tools only within the player's role.`,
          },
        }],
      };
    }

    if (request.params.name === 'rust_map_briefing') {
      return {
        description: 'Rust map/world briefing',
        messages: [{
          role: 'user',
          content: {
            type: 'text',
            text: `Prepare a concise Rust map/world briefing for ${args['player_name']} (role ${args['player_role']}). Goal: ${args['goal'] || 'general survival and routing'}. Use rust_map_overview, rust_get_monument_info, rust_map_marker_catalog, and rust_get_player_positions when role permits. Focus on grid position context, monuments, route safety, loot priorities, and one fallback plan.` ,
          },
        }],
      };
    }

    if (request.params.name === 'rust_route_planner') {
      return {
        description: 'Rust route planner',
        messages: [{
          role: 'user',
          content: {
            type: 'text',
            text: `Plan a Rust route from ${args['origin']} to ${args['target']} for role ${args['player_role'] || 'user'}. Use rust_route_advice, rust_map_overview, rust_get_monument_info, and rust_map_marker_catalog. Explain route safety, prep, threats, and a safer alternate option if needed.` ,
          },
        }],
      };
    }

    if (request.params.name === 'rust_monument_briefing') {
      return {
        description: 'Rust monument briefing',
        messages: [{
          role: 'user',
          content: {
            type: 'text',
            text: `Explain the Rust monument ${args['monument']} for role ${args['player_role'] || 'user'}. Use rust_monument_advice_context and rust_map_overview. Cover why players go there, likely loot/progression value, travel risk, and what to bring.` ,
          },
        }],
      };
    }

    if (request.params.name === 'rust_admin_world_review') {
      return {
        description: 'Rust admin world review',
        messages: [{
          role: 'user',
          content: {
            type: 'text',
            text: `Review the live Rust world for admin ${args['admin_name']}. Focus: ${args['focus'] || 'map, players, alerts, and routing'}. Use rust_map_overview, rust_get_player_positions, rust_list_activity, rust_chat_moderation_context, rust_bridge_status, and rust_rcon_command_catalog before suggesting actions.` ,
          },
        }],
      };
    }

    if (request.params.name === 'rust_duckbot_admin_review') {
      return {
        description: 'RustDuckBot admin review',
        messages: [{
          role: 'user',
          content: {
            type: 'text',
            text: `Review this Rust admin action for safety and auditability.\nAdmin: ${args['admin_name']}\nAction: ${args['action']}\nTarget: ${args['target'] ?? 'n/a'}\nRespond with APPROVE or DENY and a short reason.`,
          },
        }],
      };
    }

    throw new Error(`Unknown prompt: ${request.params.name}`);
  });

  return server;
}

async function handleWsRpc(rpc: JsonObject, socket: WebSocket, state: DuckBotState, config: ServerConfig): Promise<boolean> {
  const method = valueAsString(rpc['method']);
  const id = rpc['id'];
  if (!method) return false;

  if (method === 'initialize') {
    socket.send(JSON.stringify({
      jsonrpc: '2.0',
      id,
      result: {
        protocolVersion: '2025-06-18',
        capabilities: { tools: { listChanged: true }, resources: {}, prompts: {} },
        serverInfo: { name: 'rust-duckbot-mcp', version: VERSION },
      },
    }));
    return true;
  }

  if (method === 'tools/list') {
    socket.send(JSON.stringify({ jsonrpc: '2.0', id, result: { tools: ALL_TOOLS } }));
    return true;
  }

  if (method === 'tools/call') {
    const params = (typeof rpc['params'] === 'object' && rpc['params'] ? rpc['params'] : {}) as JsonObject;
    const toolName = valueAsString(params['name']);
    const toolArgs = (typeof params['arguments'] === 'object' && params['arguments'] ? params['arguments'] : {}) as JsonObject;
    const result = await handleToolCall(toolName, toolArgs, state, config);
    socket.send(JSON.stringify({ jsonrpc: '2.0', id, result }));
    return true;
  }

  return false;
}

export function startBridgeServer(state: DuckBotState, config: ServerConfig): { close: () => void } | undefined {
  if (!config.bridgeEnabled) return undefined;

  const httpServer = createServer();
  const wss = new WebSocketServer({ server: httpServer });

  wss.on('connection', (socket, request) => {
    log('info', `Bridge client connected from ${request.socket.remoteAddress ?? 'unknown'}`);
    socket.send(JSON.stringify({ type: 'mcp_hello', name: 'rust-duckbot-mcp', version: VERSION, tools: ALL_TOOLS.length }));

    socket.on('message', async (data) => {
      const raw = data.toString();
      try {
        const parsed = JSON.parse(raw) as JsonObject;
        const handledAsRpc = await handleWsRpc(parsed, socket, state, config);
        if (!handledAsRpc) {
          state.rustClients.add(socket);
          handleRustMessage(parsed, state, config);
        }
      } catch (error) {
        log('warn', `Bridge message parse failed: ${String(error)}`);
      }
    });

    socket.on('close', () => {
      state.rustClients.delete(socket);
      state.server.mcpConnected = state.rustClients.size > 0;
      log('info', 'Bridge client disconnected');
    });
  });

  httpServer.listen(config.bridgePort, config.bridgeHost, () => {
    log('info', `RustDuckBot bridge listening on ws://${config.bridgeHost}:${config.bridgePort}`);
  });

  return {
    close: () => {
      wss.close();
      httpServer.close();
    },
  };
}

const defaultState = createState();

export async function main(): Promise<void> {
  const config = loadConfig();
  const state = createState();

  if (config.bridgeEnabled) {
    startBridgeServer(state, config);
  } else {
    log('info', 'RustDuckBot bridge disabled (RUST_DUCKBOT_BRIDGE=0)');
  }

  if (config.stdioEnabled) {
    const server = createMcpServer(state, config);
    await server.connect(new StdioServerTransport());
    log('info', 'RustDuckBot MCP stdio transport connected');
  } else {
    log('info', 'MCP stdio transport disabled');
  }
}

const isMain = process.argv[1] ? import.meta.url === pathToFileURL(process.argv[1]).href : false;
if (isMain) {
  main().catch((error) => {
    log('error', 'Fatal MCP server error:', error);
    process.exit(1);
  });
}
