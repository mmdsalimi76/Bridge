# PRD Implementation Checklist
## Agentless Desktop VPS Management & Deployment Workstation

*Professional Software Engineer Analysis*

---

## Project Overview
**Product**: Agentless Desktop VPS Management & Deployment Workstation  
**Framework**: C# .NET 8 + Avalonia UI 11 (Fluent theme)  
**Architecture**: MVVM with CommunityToolkit.Mvvm  
**SSH/SCP**: Renci.SshNet library  
**Target**: Single-file .exe distribution, zero VPS footprint

---

## 📋 PHASE 1: Architecture Foundation & Core SSH Engine
_Goal: Establish C# Avalonia project, MVVM structure, and resilient SSH/SCP communication layer_

### Deliverables

| # | Task | Description | Status |
|---|------|-------------|--------|
| P1.1 | Solution & Project Setup | Create `VpsManager.sln`, `VpsManager.Client/`, `VpsManager.Core/` projects | ☐ |
| P1.2 | MVVM Scaffold | Implement `CommunityToolkit.Mvvm` source generators, set up ViewModelBase | ☐ |
| P1.3 | Avalonia UI Scaffold | Create main window with Fluent Dark theme navigation sidebar | ☐ |
| P1.4 | Program.cs Setup | Configure Avalonia platform builder, startup object | ☐ |
| P1.5 | Windows Credential Manager Integration | Implement `WindowsCredentialStore.cs` using DPAPI/CredentialManager | ☐ |
| P1.6 | Secure Password/Key Storage | Support encrypted private keys (.pem, .ppk), passphrase handling | ☐ |

### SSH Service Requirements

| # | Task | Description | Status |
|---|------|-------------|--------|
| P1.7 | `SshService` Core Implementation | Wrap `Renci.SshNet` with async command execution | ☐ |
| P1.8 | Connection Pooling | Maintain persistent, auto-reconnecting SSH channels | ☐ |
| P1.9 | Heartbeat Keep-Alive | Active ping to avoid timeouts during long deployments | ☐ |
| P1.10 | Streaming Output Handling | Real-time stdout/stderr piping via events/observables | ☐ |
| P1.11 | Password Authentication | Standard SSH password auth support | ☐ |
| P1.12 | Key-Based Authentication | Support OpenSSH, .pem, .ppk encrypted private keys | ☐ |
| P1.13 | Host Key Verification | MitM attack prevention, enforced host key checking | ☐ |

### Definition of Done (Phase 1)
- [ ] Avalonia UI launches with dark theme sidebar
- [ ] SSH connections establish successfully with auto-reconnect
- [ ] Credentials stored securely via Windows Credential Manager
- [ ] Streaming output works in UI terminal
- [ ] All authentication methods (password + key) functional

---

## 📋 PHASE 2: Docker Orchestration & Database Safety
_Goal: Implement remote container control, log streaming, and database backup verification_

### Docker Compose Requirements

| # | Task | Description | Status |
|---|------|-------------|--------|
| P2.1 | DockerService Core | Implement `IDockerService` interface | ☐ |
| P2.2 | Visual Compose Manager | GUI representation of `docker-compose.yml` services | ☐ |
| P2.3 | Up/Down/Build/Restart | Remote container lifecycle commands | ☐ |
| P2.4 | Live Log Streamer | `AvaloniaEdit` terminal with ANSI color code rendering | ☐ |
| P2.5 | Autoscroll Control | Log viewer autoscroll behavior | ☐ |
| P2.6 | Quick Health Status | Container CPU, memory, uptime, exit-code reporting | ☐ |

### Database Service Requirements

| # | Task | Description | Status |
|---|------|-------------|--------|
| P2.7 | `DatabaseService` Core | Implement `IDatabaseService` interface | ☐ |
| P2.8 | Pre-Deploy Safety Dump | Automated `pg_dump` or `mysqldump` inside DB containers | ☐ |
| P2.9 | MD5 Verification Pipeline | Remote dump MD5 → SCP → Local MD5 → Compare & confirm | ☐ |
| P2.10 | One-Click Restore/Migration | UI interface for schema migrations & rollback | ☐ |

### Definition of Done (Phase 2)
- [ ] Docker Compose dashboard with service status grid
- [ ] Live log streaming with colorized ANSI output
- [ ] Database backup with MD5 verification
- [ ] One-click restore functionality
- [ ] Health status reporting (CPU, memory, uptime)

---

## 📋 PHASE 3: Selective Patching & Sideloading
_Goal: Implement the key features for restricted network deployments_

### Selective Patching Engine Requirements

| # | Task | Description | Status |
|---|------|-------------|--------|
| P3.1 | Local File Change Indexer | Scan directory trees against ignore filters (`.git`, `node_modules`, `*.env`) | ☐ |
| P3.2 | Interactive Patch Selector UI | TreeView/DataGrid with checkable nodes for file selection | ☐ |
| P3.3 | Atomic ZIP Transfer | Package selected files into in-memory ZIP buffer | ☐ |
| P3.4 | SCP Upload | Upload ZIP via SCP to target staging directory | ☐ |
| P3.5 | Remote Extraction | Extract remote ZIP and swap target files atomically | ☐ |
| P3.6 | Container Reload | Trigger `docker compose restart <service>` post-transfer | ☐ |

### Sideloading Engine Requirements

| # | Task | Description | Status |
|---|------|-------------|--------|
| P3.7 | Local Image Exporter | Run `docker save` or export local tarballs | ☐ |
| P3.8 | Streaming Transfer with Progress Bar | Stream large tar archives via SCP with byte-progress | ☐ |
| P3.9 | Remote Ingestion | Trigger remote `docker load -i <image.tar>` | ☐ |
| P3.10 | Temporary Cleanup | Auto-cleanup remote tar archives after load | ☐ |

### Definition of Done (Phase 3)
- [ ] Directory diffing with customizable ignore patterns
- [ ] Interactive file tree with checkable node selection
- [ ] Atomic ZIP transfer over SCP (no partial states)
- [ ] Docker image sideloading pipeline with progress bar
- [ ] Remote container reload after patch/sideload

---

## 📋 PHASE 4: Master Pipeline, UX Polish & Release
_Goal: End-to-end automated deployment pipeline, error recovery, and release packaging_

### Master Pipeline Requirements

| # | Task | Description | Status |
|---|------|-------------|--------|
| P4.1 | 1-Click Production Deployment | Step-by-step progress stepper UI | ☐ |
| P4.2 | Pre-Deploy Safety Gates | DB backup + health check before migration | ☐ |
| P4.3 | Automatic Rollback | Abort on remote command failure, prompt for rollback | ☐ |
| P4.4 | Error Recovery | Full pipeline error handling & recovery | ☐ |

### UX Polish Requirements

| # | Task | Description | Status |
|---|------|-------------|--------|
| P4.5 | UI Polish & Dark Theme | Refinements, animations, visual consistency | ☐ |
| P4.6 | Unit Testing | Comprehensive unit tests for all services | ☐ |
| P4.7 | Integration Testing | End-to-end test scenarios | ☐ |

### Release Packaging Requirements

| # | Task | Description | Status |
|---|------|-------------|--------|
| P4.8 | Single-File .EXE Build | `dotnet publish -c Release -r win-x86 -p:PublishSingleFile=true` | ☐ |
| P4.9 | Zero-Dependency Distribution | No .NET runtime required on target machine | ☐ |
| P4.10 | Release Notes & Documentation | User guide, API docs, deployment guide | ☐ |

### Definition of Done (Phase 4)
- [ ] Master 1-click deployment pipeline functional
- [ ] Automatic pre-deploy safety gates working
- [ ] Unit tests >80% coverage for core services
- [ ] Integration tests passing
- [ ] Single-file .exe produced and tested
- [ ] Documentation complete

---

## 🔧 TECHNICAL PREREQUISITES

### Development Environment

| # | Requirement | Status |
|---|-------------|--------|
| T.1 | .NET 8 SDK installed | ☐ |
| T.2 | Visual Studio 2022 or VS Code with C# extension | ☐ |
| T.3 | Avalonia.UI 11.X package | ☐ |
| T.4 | CommunityToolkit.Mvvm package | ☐ |
| T.5 | Renci.SshNet package | ☐ |
| T.6 | AvaloniaEdit package | ☐ |
| T.7 | AdysTech.CredentialManager package (or alternative) | ☐ |

### NuGet Packages Checklist

| Package | Version | Purpose | Status |
|---------|---------|---------|--------|
| `Avalonia.Desktop` | 11.x | UI Framework | ☐ |
| `Avalonia.Themes.Fluent` | 11.x | Fluent Dark Theme | ☐ |
| `CommunityToolkit.Mvvm` | 8.x | MVVM Source Generators | ☐ |
| `Renci.SshNet` | 2.0+ | SSH/SCP Library | ☐ |
| `AvaloniaEdit` | 2.0+ | Terminal/Log Viewer | ☐ |
| `AdysTech.CredentialManager` | 2.0+ | Windows Credential Store | ☐ |
| `System.Text.Json` | Built-in | JSON Serialization | ☐ |

---

## 📁 PROJECT STRUCTURE CHECKLIST

### Solution Structure (per PRD line 30-88)

```
VpsManager.sln
├── src/
│   ├── VpsManager.Client/       # UI Layer
│   ├── VpsManager.Core/         # Business Logic
│   │   ├── Services/            # Core Services
│   │   ├── Security/            # Credential Management
│   │   ├── Models/              # Data Models
│   │   └── Helpers/             # Utilities
│   └── VpsManager.Desktop/      # Single-File Executable
│
└── tests/
    ├── VpsManager.Core.Tests/
    └── VpsManager.Client.Tests/
```

### Client Project Structure

| Directory | Purpose | Status |
|-----------|---------|--------|
| `Views/` | Avalonia XAML Views | ☐ |
| `ViewModels/` | MVVM ViewModels | ☐ |
| `Assets/` | Icons, fonts, branding | ☐ |
| `Styles/` | Custom XAML styles | ☐ |
| `Converters/` | ValueConverters | ☐ |

### Core Project Structure

| Directory | Purpose | Status |
|-----------|---------|--------|
| `Services/` | SshService, PatchService, etc. | ☐ |
| `Security/` | CredentialStore implementations | ☐ |
| `Models/` | ConnectionProfile, ContainerStatus, etc. | ☐ |
| `Helpers/` | HashHelper, CompressionHelper, AnsiColorParser | ☐ |

---

## ✅ SUCCESS CRITERIA

### Technical Metrics (from PRD Section 4)

| Pillar | Metric | Target |
|--------|--------|--------|
| **Speed** | App cold start | $< 1.5\text{s}$ |
| **Speed** | UI frame rate during transfers | 60 fps |
| **Performance** | MD5 hashing on large dumps (>5GB) | Chunked stream processing |
| **Reliability** | Pre-deploy DB dumps | Mandatory automated trigger |
| **Reliability** | MD5 checksum validation | Post-transfer confirmation |
| **Security** | Password/key storage | Never plain-text, DPAPI/Credential Manager |
| **Security** | Host key verification | Enforced, MitM prevention |

### Phase Completion Gates

- **Phase 1 Complete**: SSH connections functional, UI launched, credentials secure
- **Phase 2 Complete**: Docker Compose operational, logs streaming, DB backup verified
- **Phase 3 Complete**: Patching pipeline functional, sideloading operational
- **Phase 4 Complete**: 1-click deployment, single-file .exe, full documentation

---

## ⚠️ NOTES FOR PROFESSIONAL IMPLEMENTATION

### Key Design Decisions

1. **Agentless Architecture**: No daemons on VPS - all actions over SSH/SCP only
2. **Air-Gapped Support**: `docker save` → SCP → `docker load` pipeline for restricted networks
3. **Atomic Operations**: ZIP-based transfer with MD5 verification at each stage
4. **Local-First Security**: All credentials stored in Windows Credential Manager/DPAPI
5. **MVVM Pattern**: Full separation of concerns with CommunityToolkit.Mvvm source generators

### Risk Mitigation

- **Network Timeouts**: 30s SSH handshake timeout, configurable SCP timeouts
- **Failure Recovery**: Automatic rollback on remote command failure
- **Memory Management**: Chunked crypto streams for large DB dumps (>5GB)
- **Error Handling**: Abort subsequent steps on failure, prompt for rollback

### Testing Strategy

- **Unit Tests**: Service layer logic, hash computation, credential management
- **Integration Tests**: SSH connection scenarios (may require test VPS)
- **UI Tests**: ViewModel validation, data binding correctness
- **Performance Tests**: Cold start timing, streaming throughput, memory usage

---

*Checklist generated from PRD_AND_ROADMAP.md analysis*
*Total items: 55+ technical tasks across 4 phases*