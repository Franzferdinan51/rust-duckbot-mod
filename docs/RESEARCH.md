# Rust Game MCP Implementation Research

**Compiled:** 2026-05-13 | **Task:** Deep dive on existing Rust game MCP implementations

---

## 2026-05-13 Live GitHub Search Update

Recent GitHub/web scan found useful adjacent projects, but no drop-in Rust-game computer-station/CCTV DuckBot MCP:

- `jumoooo/steam-game-server-mcp` is a TypeScript MCP for Steam and game-server query/RCON/log operations. It is useful as a pattern for server inventory, RCON admin actions, and tests, but it is not a Rust in-game computer/CCTV mod.
- `mjmorales/rcon-mcp-server` is a generic RCON MCP with session management and command execution. It is useful for admin-command safety patterns, but it does not know Rust roles, cameras, or Oxide plugin state.
- `Facepunch/webrcon` documents Rust WebSocket RCON requirements (`+rcon.web 1`, port, password). This remains relevant for admin integrations.
- `OxideMod/Oxide.Rust` is the official Oxide Rust extension source and should remain the source of truth for plugin runtime behavior.

Conclusion: RustDuckBot should keep its custom Oxide plugin plus MCP bridge. Existing MCPs can inform RCON/query/admin patterns, but they do not replace the in-game computer terminal, role model, camera state, or DuckBot skill.

---

## 1. montukxd/mcp_rust_game_dev (Node.js)

**URL:** https://github.com/montukxd/mcp_rust_game_dev
**Language:** Node.js (TypeScript/JS, ESM bundle)
**Stars:** N/A (marketplace plugin)
**Transport:** stdio (Cursor, Cline, Continue.dev, Codex, Gemini) + HTTP (`--http` mode on port 3100)

### Architecture

Full-stack Node.js MCP server that bridges an LLM to a live Rust server via uMod (Oxide) framework. Ships as a pre-built `dist/index.mjs` — no build step required.

```
mcp_rust_game_dev/
├── mcp-server/dist/index.mjs     # The MCP server bundle
├── configs/                      # Pre-made MCP configs for various clients
├── .cursor/
│   ├── mcp.json                  # Cursor MCP config (edit this)
│   ├── rules/                    # Cursor auto-rules for .cs files
│   └── skills/rust-oxide-plugin-dev/SKILL.md  # LLM skill
└── skills/                      # Same skill (non-Cursor clients)
```

### RCON Integration Pattern

Connects to the Rust server via **WebSocket RCON** (Facepunch protocol). Requires server launch args:
```
+rcon.web 1 +rcon.port 28016 +rcon.password "pass"
```

Environment configuration:
```json
{
  "RUST_RCON_HOST": "127.0.0.1",
  "RUST_RCON_PORT": "28016",
  "RUST_RCON_PASSWORD": "your_rcon_password",
  "RUST_SERVER_PATH": "C:/RustServer/Server",
  "RUST_DEPLOY_MODE": "local"
}
```

### Deploy Modes

| Mode | When to use | Required variables |
|------|-------------|-------------------|
| local | Same machine | RUST_SERVER_PATH |
| ftp | Remote via FTP | RUST_FTP_HOST, RUST_FTP_USER, RUST_FTP_PASSWORD |
| sftp | Remote via SSH | RUST_SFTP_HOST, RUST_SFTP_USER, RUST_SFTP_KEY or _PASSWORD |

FTP/SFTP modes require `cd mcp-server && npm install` for the optional dependencies.

### Tool Categories (26 tools total)

**Plugin Lifecycle:**
- `rust_plugin_push` — Deploy → compile → verify (main tool, up to 5 auto-fix iterations)
- `rust_plugin_load` / `unload` / `reload` — Manual plugin control
- `rust_list_plugins` — List loaded plugins

**Server Control:**
- `rust_server_command` — Execute any RCON command
- `rust_server_status` — Server info (map, players, version)
- `rust_server_fps` — FPS and health metrics
- `rust_read_logs` — Read Oxide logs
- `rust_read_console_log` — Read server console log with error filtering

**Config & Permissions:**
- `rust_read_config` / `rust_write_config` — Plugin config (auto-reloads after write)
- `rust_read_data` — Plugin data files
- `rust_grant_permission` / `rust_revoke_permission` — Permission management
- `rust_show_permissions` — List all permissions

**Documentation:**
- `rust_docs_search_hook` — Find hooks by keyword (700+ local index)
- `rust_docs_get_hook` — Hook signature, example, source from docs.oxidemod.com
- `rust_docs_search_api` — Search 25+ developer guides and API reference
- `rust_docs_get_examples` — Code examples for patterns (CUI, timers, database, etc.)
- `rust_docs_browse` — List all docs with links to docs.oxidemod.com and umod.org

**Analysis & Generation:**
- `rust_plugin_performance` — Hook execution time profiling
- `rust_check_runtime_errors` — Parse runtime exceptions with fix hints
- `rust_analyze_plugin` — Static code analysis

**Utilities:**
- `rust_watch_directory` / `rust_unwatch_directory` — Auto-deploy on file save
- `rust_generate_tests` — Generate test plugin
- `rust_generate_docs` — Generate plugin documentation

### Plugin Development Workflow

1. **Research** → `rust_docs_search_hook` / `rust_docs_get_hook`
2. **Code** → LLM writes the `.cs` plugin
3. **Deploy** → `rust_plugin_push` (copy → compile → check errors)
4. **Fix errors** → auto-fix + re-push (up to 5 iterations)
5. **Test** → `rust_check_runtime_errors` (when user tests in-game)
6. **Performance** → `rust_plugin_performance` (for heavy hooks)
7. **Finalize** → `rust_generate_docs` / `rust_generate_tests`

All steps handled by LLM automatically. Developer only describes what the plugin should do.

### Server File Conventions

- Console log must be named `output.txt` or `output_log.txt` in RUST_SERVER_PATH
- Startup script must be named `start.bat`, `run.bat`, `RustDedicated.bat`, `start.cmd`, `run.cmd`, or `start.sh`

### Skill Structure

SKILL.md contains LLM-facing instructions with:
- Oxide hook signatures and patterns
- uMod plugin development best practices
- RCON command reference
- Code generation templates

---

## 2. Vaiz/rust-mcp-server (Rust)

**URL:** https://github.com/Vaiz/rust-mcp-server
**Language:** Rust
**Transport:** stdio
**Purpose:** Bridge between LLM (GitHub Copilot) and local Rust development environment

### Architecture

Cargo-subcommand-centric MCP server. 33 tools focused on Rust toolchain (cargo, rustc, rustup). No game-specific functionality.

### Tool Categories

**Core Cargo Commands:**
- `cargo-build`, `cargo-check`, `cargo-test`, `cargo-doc`, `cargo-fmt`, `cargo-clippy`
- `cargo-clean`, `cargo-new`, `cargo-generate_lockfile`, `cargo-package`
- `cargo-list` — List installed cargo commands

**Dependency Management:**
- `cargo-add` (20+ parameters: branch, features, git, registry, version, etc.)
- `cargo-remove`, `cargo-update`, `cargo-metadata`
- `cargo-search` — Search crates.io
- `cargo-info` — Display package info

**Code Quality & Security:**
- `cargo-clippy` — Lint code
- `cargo-deny-check` — Security advisories, license compliance, banned crates
- `cargo-deny-init` / `cargo-deny-list` / `cargo-deny-install`
- `cargo-expand` — Macro expansion for debugging
- `rustc-explain` — Explain compiler error codes

**Testing & Validation:**
- `cargo-insta-update-snapshots` — Snapshot testing with insta
- `cargo-machete` — Find unused dependencies
- `cargo-hack` — Feature testing, powerset testing, version compatibility

**Rust Toolchain:**
- `rustup-show` — Show active/installed toolchains
- `rustup-toolchain-add` — Install/update toolchains
- `rustup-update` — Update rustup

### CLI Arguments

```
--log-level          error|warn|info|debug|trace  (default: info)
--log-file           File path for logging
--disable-tool      Disable specific tool (repeatable)
--workspace         Path to Rust project (default: cwd)
--registry          Default cargo registry
--generate-docs     Output markdown docs and exit
--disable-recommendations  Suppress experimental recommendations
```

### Tool Schema Pattern

Tools have rich JSON Schema input definitions. Example: `cargo-add` has 20+ optional parameters, most with type string or boolean. This is useful for seeing how to define complex tool schemas.

```typescript
// Example pattern from cargo-add schema
{
  "name": "cargo-add",
  "description": "Adds a dependency to a Rust project using cargo add.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "branch": { "type": "string" },
      "features": { "type": "array", "items": { "type": "string" } },
      "git": { "type": "string" },
      "version": { "type": "string" },
      "manifest_path": { "type": "string" },
      // ... 15+ more parameters
    }
  }
}
```

### VS Code / GitHub Copilot Integration

```json
// .vscode/mcp.json
{
  "servers": {
    "rust-mcp-server": {
      "type": "stdio",
      "command": "C:/path/to/rust-mcp-server.exe",
      "args": ["--log-file", "log/folder/rust-mcp-server.log"]
    }
  }
}
```

---

## 3. modelcontextprotocol/rust-sdk (Official Rust SDK)

**URL:** https://github.com/modelcontextprotocol/rust-sdk
**Crates:** `rmcp` (core) + `rmcp-macros` (procedural macros)
**Current version:** 0.16.0
**Runtime:** tokio async

### Available Crates

| Crate | Purpose |
|-------|---------|
| `rmcp` | Core protocol — message types, transport, handler traits |
| `rmcp-macros` | Procedural macros for #[tool], #[prompt], etc. |

### Transport Options

- **stdio:** `(stdin(), stdout())` — most common for local servers
- **TokioChildProcess:** For spawning child processes (e.g., `npx @modelcontextprotocol/server-everything`)

### Core Pattern — Tools-Only Server

```rust
use rmcp::{handler::server::wrapper::Parameters, schemars, tool, tool_router, ServiceExt, transport::stdio};

#[derive(Debug, serde::Deserialize, schemars::JsonSchema)]
struct AddParams { a: i32, b: i32 }

#[derive(Clone)]
struct Calculator;

#[tool_router(server_handler)]
impl Calculator {
    #[tool(description = "Add two numbers")]
    fn add(&self, Parameters(AddParams { a, b }): Parameters<AddParams>) -> String {
        (a + b).to_string()
    }
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let service = Calculator.serve(stdio()).await?;
    service.waiting().await?;
    Ok(())
}
```

### Explicit Handler Pattern (for multiple capabilities)

```rust
use rmcp::{tool, tool_router, tool_handler, ServerHandler, ServiceExt};

#[tool_router]
impl Calculator {
    #[tool(description = "Add two numbers")]
    fn add(&self, Parameters(AddParams { a, b }): Parameters<AddParams>) -> String { ... }
}

#[tool_handler(name = "calculator", version = "1.0.0", instructions = "A simple calculator")]
impl ServerHandler for Calculator {}
```

### ServerCapabilities Builder

```rust
ServerInfo {
    capabilities: ServerCapabilities::builder()
        .enable_tools()
        .enable_resources()
        .enable_prompts()
        .enable_logging()
        .build(),
    ..Default::default()
}
```

### Available Macros

| Macro | Purpose |
|-------|---------|
| `#[tool]` | Mark function as MCP tool handler |
| `#[tool_router]` | Generate tool router from impl block |
| `#[tool_handler]` | Generate `call_tool` and `list_tools` |
| `#[prompt]` | Mark function as MCP prompt handler |
| `#[prompt_router]` | Generate prompt router |
| `#[prompt_handler]` | Generate `get_prompt` and `list_prompts` |
| `#[task_handler]` | Wire up task lifecycle on OperationProcessor |

### Key Trait: ServerHandler

```rust
pub trait ServerHandler: Send + Sync {
    fn get_info(&self) -> ServerInfo;
    async fn list_tools(&self, ...) -> Result<ListToolsResult, McpError>;
    async fn call_tool(&self, ...) -> Result<CallToolResult, McpError>;
    // + optional: list_resources, read_resource, list_prompts, get_prompt, etc.
}
```

### Resource Pattern

```rust
async fn list_resources(&self, ...) -> Result<ListResourcesResult, McpError> {
    Ok(ListResourcesResult {
        resources: vec![
            RawResource::new("file:///config.json", "config").no_annotation(),
        ],
        ..
    })
}

async fn read_resource(&self, request: ReadResourceRequestParams, ...) -> Result<ReadResourceResult, McpError> {
    match request.uri.as_str() {
        "file:///config.json" => Ok(ReadResourceResult {
            contents: vec![ResourceContents::text(r#"{"key":"value"}"#, &request.uri)],
        }),
        _ => Err(McpError::resource_not_found("resource_not_found", ...)),
    }
}
```

### Prompts Pattern

```rust
#[derive(Debug, Serialize, Deserialize, JsonSchema)]
pub struct CodeReviewArgs {
    pub language: String,
    pub focus_areas: Option<Vec<String>>,
}

#[prompt_router]
impl MyServer {
    #[prompt(name = "code_review", description = "Review code in a given language")]
    async fn code_review(&self, Parameters(args): Parameters<CodeReviewArgs>) -> Result<GetPromptResult, McpError> {
        Ok(GetPromptResult {
            description: Some(format!("Code review for {}", args.language)),
            messages: vec![PromptMessage::new_text(PromptMessageRole::User, format!(...))],
        })
    }
}
```

### Sampling (Server requesting LLM from client)

```rust
let response = context.peer.create_message(CreateMessageRequestParams {
    messages: vec![SamplingMessage::user_text("Explain this error")],
    model_preferences: Some(ModelPreferences { ... }),
    temperature: Some(0.7),
    max_tokens: 150,
    ..
}).await?;
```

### Dependencies

```toml
rmcp = { version = "0.16.0", features = ["server"] }
# Third-party:
tokio = { version = "...", features = ["full"] }
serde = { version = "...", features = ["derive"] }
schemars = "..."  # For JsonSchema derive
```

---

## 4. postrv/narsil-mcp (Rust — Code Intelligence)

**URL:** https://github.com/postrv/narsil-mcp
**Language:** Rust
**Purpose:** 90-tool code intelligence MCP server with tree-sitter parsing
**Transport:** stdio

### Feature Flags

| Feature | Description | Binary Size |
|---------|-------------|-------------|
| `native` (default) | Full MCP server, all tools | ~30MB |
| `graph` | + RDF knowledge graph, SPARQL, CCG tools | ~35MB |
| `frontend` | + Embedded visualization web UI | ~31MB |
| `neural` | + TF-IDF vector search, API embeddings | ~32MB |
| `neural-onnx` | + Local ONNX model inference | ~50MB |
| `wasm` | Browser build | ~3MB |

### 90 Tools Covering

- Symbol extraction (functions, structs, enums, traits, impls)
- Call graph analysis
- Taint analysis / security vulnerability scanning
- SBOM generation, dependency auditing, license compliance
- Control flow graphs, data flow analysis, dead code detection
- Neural semantic search (Voyage AI / OpenAI embeddings)
- 32 languages: Rust, Python, JavaScript, TypeScript, Go, C, C++, Java, C#, Bash, Ruby, Kotlin, PHP, Swift, Verilog, Scala, Lua, Haskell, Elixir, Clojure, Dart, Julia, R, Perl, Zig, Erlang, Elm, Fortran, PowerShell, Nix, Groovy

---

## 5. RustMon (Reference — Not MCP)

**URL:** https://github.com/alexander171294/RustMon
**Language:** Angular/Node.js
**Purpose:** Rust game admin panel (NOT an MCP server — reference for what Rust server admin tooling looks like)

### Features
- Multiple servers login record
- Chat, Players, Console on single screen
- Plugin enable/disable/reload/update checker
- Permissions groups export/import
- Reboot with time warning
- Auto-kick high ping, auto-respond commands, skip queue
- Discord login and bot (server info, chat bridging, group assignment)
- Shared player blacklist between clients

**Note:** This is a traditional admin panel, not an MCP server. Useful as a reference for what admin operations players expect in Rust server tooling.

---

## 6. Oxide/CCTV Plugins (Reference)

### linuxgurugamer/CCTV (Oxide/uMod Plugin)
**URL:** https://github.com/linuxgurugamer/CCTV

Features:
- Monitor functions (switch on/off)
- Detect cameras
- Remote control of camera movement
- Cycle through attached cameras
- Best with First Person Eva and KAS

**Note:** This is an Oxide plugin for in-game CCTV camera control, not an MCP server.

---

## 7. Facepunch/webrcon (Reference — Protocol)

**URL:** https://github.com/Facepunch/webrcon
**Language:** Various
**Purpose:** Official Facepunch WebSocket RCON protocol implementation

This is the underlying protocol that Rust servers use for RCON communication. Any Rust game MCP server would need to implement this protocol to communicate with the game server.

**Protocol details:**
- WebSocket-based RCON
- Server must launch with `+rcon.web 1`
- Default port: 28016
- Authentication via password

---

## Key Findings for DuckBot Rust MCP Implementation

### Transport Selection
- **stdio** is the dominant transport for MCP servers (all analyzed servers use it)
- HTTP/SSE is used as secondary option (montukxd's `--http` mode)

### Tool Schema Patterns
- Use schemars derive macro for JSON Schema generation (Rust SDK standard)
- Rich parameter definitions (see Vaiz/rust-mcp-server `cargo-add` with 20+ params)
- Descriptions should be detailed enough for LLM to use tool correctly

### Server Handler Patterns
- `ServerInfo` / `ServerCapabilities` builder pattern is standard
- `#[tool_router]` + `#[tool_handler]` macro combo for clean separation
- `Parameters<T>` wrapper for deserializing tool input

### Game-Specific Patterns (from montukxd)
- RCON connection via WebSocket (Facepunch webrcon protocol)
- Deploy pipeline: push → compile → verify → auto-fix (iteration loop)
- File watcher for auto-deploy on save
- Hot reload via RCON `o.reload <plugin>`
- Log parsing for error detection
- Config file read/write with auto-reload
- Permission management via RCON

### What's NOT Available
- **No existing Rust-game-specific MCP server** for admin/monitoring tasks
- **No CCTV-focused MCP server** for Rust game
- **No DuckBot-like implementation** in the Rust game space

This represents an opportunity gap — a DuckBot Rust MCP server could fill a need for AI-driven Rust server administration, plugin development assistance, and game monitoring.

---

## Related Links

- Official MCP Spec: https://modelcontextprotocol.io/specification/2025-11-25
- Official Rust SDK docs.rs: https://docs.rs/rmcp/latest/rmcp
- Oxide/uMod documentation: https://docs.oxidemod.com
- uMod plugin catalog: https://umod.org
- Facepunch webrcon: https://github.com/Facepunch/webrcon
