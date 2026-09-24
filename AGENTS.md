# AGENTS.md

## What this repo is

Single-project .NET 10 ASP.NET Core service (`GenOnlineService`) replacing the legacy GameSpy backend for Command & Conquer: Generals and Zero Hour (GeneralsOnline / NGMP). Provides REST endpoints, WebSockets, player stats, matchmaking, custom lobbies, and TURN relay coordination backed by MariaDB/MySQL.

## Build and Run

```bash
# Build Debug
dotnet build GenOnlineService/GenOnlineService.csproj

# Build Release (Must be clean — Release enforces TreatWarningsAsErrors=True)
dotnet build GenOnlineService/GenOnlineService.csproj -c Release

# Run locally
# CRITICAL: Current working directory MUST be GenOnlineService/ so relative paths to data/ resolve correctly:
cd GenOnlineService && dotnet run
```

### Build & Verification Rules
- **No Test Suite**: `dotnet test` in CI runs against the web project and passes vacuously. Verification is clean compilation in both Debug and Release configurations.
- **Compiler Warnings**: Although `<WarningLevel>0</WarningLevel>` is configured, `<TreatWarningsAsErrors>True</TreatWarningsAsErrors>` is enforced on Release builds. Any warning generated in Release configuration will break CI.
- **Solution Platforms**: Explicitly restricted to `x64` and `ARM64` (no AnyCPU target in the solution).
- **CWD Sensitivity**: `data/*.json`, `Exceptions/`, `crcfiles/`, and `runserver.sh` are resolved relative to the process working directory (`Path.Combine("data", ...)`). Running `dotnet run` from the repo root breaks file-backed endpoints.

## Startup Hard Requirements (Fails Boot If Unmet)

1. **Database Schema**: Must be imported manually using `GenOnlineService/Database_Structure/structure.sql`. **No Entity Framework migrations exist or are expected.** Schema alterations require updating `structure.sql` alongside the corresponding models in `Database/`.
2. **Reachable MariaDB/MySQL**: `ServerVersion.AutoDetect`, `DailyStatsManager.LoadFromDB`, and `TokenRevocationManager.Initialize` execute before `app.Run()`.
3. **Database Configuration**: All `Database:*` keys in `appsettings.json` must be populated (`host`, `port`, `dbname`, `dbuser`, `dbpassword`).
4. **JWT Signing Key**: `JwtSettings:Key` must be at least 32 bytes and must not contain `TODO` (`Program.ValidateSigningKey`).
5. **Sentry Configuration**: `Sentry:enabled` and `Sentry:dsn` keys must exist (dsn may be an empty string).

## Architecture & Subsystems

```
GenOnlineService/
├── Program.cs                     # Main entry: Kestrel, JWT auth, WebSockets route, background timers
├── Constants.cs                   # Session state dictionaries, WebSocket protocol enums & DTOs (~2800 lines)
├── LobbyManager.cs                # Singleton: Custom lobbies, game lifecycle, roster management
├── MatchmakingManager.cs          # Static class: Ranked 1v1 / QuickMatch queues, automated match creation
├── ExternalLeaderboardsClient.cs  # Cloudflare Calls TURN & STUN credential acquisition
├── Controllers/                   # REST & WebSocket endpoint handlers
│   ├── CheckLogin/                # Polled by game client during web login
│   ├── LoginCode/                 # Internal API called by generalsx-login portal
│   ├── LoginWithToken/            # 30-day persistent JWT session login
│   ├── Matchmaking/               # Queue join/leave/poll REST handlers
│   ├── MatchUpdate/               # Post-game telemetry, CRC validation, ELO computation
│   ├── Monitoring/                # Health checks, uptime, active user stats
│   ├── VersionCheck/              # Version compatibility and auto-update metadata
│   └── WebSocket/                 # Real-time game lobby & presence channel (/ws)
├── Database/                      # AppDbContext and partial domain classes (Database.*.cs)
└── data/                          # Runtime data files (motd.txt, rooms.json, patchdata.json)
```

### Key Architectural Patterns
- **REST Route Convention**: `env/{environment}/contract/{contract_version}/[controller]`. The URL segment derives from the controller class name.
  - *Known exceptions*: `API_MatchHistoryController` maps to `.../MatchHistory`; `VersionCheck` also maintains legacy `/cloud/env:prod/VersionCheck`; `Monitoring` contains subroutes (`ActiveUsers`, `BasicStats`, `Database`, `Uptime`).
- **Raw JSON Request Bodies**: Request bodies are read manually as raw JSON using `StreamReader(HttpContext.Request.Body)` rather than MVC model binding. Responses use `APIResult` wrappers.
- **Authentication**:
  - In-game player sessions: JWT Bearer tokens with 30-day lifetimes.
  - Internal server-to-server endpoints (e.g. `LoginCode` from web login): `X-Api-Key` header validated against `API:keys` and `API:webserver_key` (compared uppercase).
- **State Management**: Active sessions and presence live in thread-safe static `ConcurrentDictionary` collections inside `Constants.cs`.
- **Database Context**: `AppDbContext` is registered via `AddPooledDbContextFactory` with default `NoTracking`. Static helpers use `ServiceLocator.Services.CreateScope()`.

## Operational Diagnostics & Testing

For live server operations, refer to the `generals-online-ops` skill.

### Critical Log Filtering Rule
> [!IMPORTANT]
> The game client polls `GET /env/{env}/contract/1/CheckLogin` every 1 second while waiting for OAuth login. When monitoring logs in production or staging, **always filter out `CheckLogin` and `Monitoring`** to prevent terminal flooding:
> ```bash
> docker compose logs -f --tail=100 gameserver | grep -vE 'Monitoring|CheckLogin'
> ```

### Diagnostic Curl Commands
```bash
# Test public VersionCheck (expects HTTP 200 with JSON payload)
curl -i -X POST https://online.generalsx.org/env/live/contract/1/VersionCheck

# Test authenticated MOTD (expects HTTP 401 Unauthorized without JWT)
curl -i https://online.generalsx.org/env/live/contract/1/MOTD

# Test WebSocket handshake
curl -i -N -H "Connection: Upgrade" -H "Upgrade: websocket" -H "Host: online.generalsx.org" \
  -H "Origin: https://online.generalsx.org" -H "Sec-WebSocket-Key: SGVsbG8sIHdvcmxkIQ==" \
  -H "Sec-WebSocket-Version: 13" https://online.generalsx.org/ws
```

### MariaDB 11.4 SQL Gotcha
MariaDB 11.4 in production does **not** support `LIMIT` clauses inside `IN (...)` subqueries (`ERROR 1235 (42000)`). Avoid subqueries with `LIMIT`; use direct queries with `ORDER BY ... DESC LIMIT N`.

### Diagnosing Matchmaking HTTP 500
`MatchmakingController.Put()` (`PUT /env/{env}/contract/1/matchmaking`) returns HTTP 500 if client JSON payload is missing mandatory fields (`playlist`, `maps`, `exe_crc`, `ini_crc`, `anticheat_id`) or if the player does not have an active WebSocket session in `Constants.cs`.

## Conventions

- **Indentation**: Tabs across all C# and project files.
- **License Header**: Source files carry the AGPL-3.0 header block — maintain it on all new or modified files.
- **Commit Messages**: Conventional Commits with scopes:
  - `fix(lobby): prevent joins after deletion`
  - `feat(stats): include online player count in global stats`
  - `fix(matchdata): use reported outcomes for tie-break`
