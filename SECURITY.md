# Security Policy

Agents Hub handles sensitive credentials, developer tool execution, process orchestration, and local network communication. We take the security of your tools, API keys, and system integrity seriously.

---

## Supported Versions

Security patches and vulnerability updates are provided for the current active release line:

| Version | Supported | Status |
| :--- | :--- | :--- |
| **1.0.x** | Yes | Current active release line. |
| **< 1.0.0** | No | Legacy preview releases. Please upgrade to the latest release. |

---

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

If you discover a security vulnerability or potential exposure in Agents Hub:

1. **GitHub Security Advisory**: Submit a private report via [GitHub Private Vulnerability Reporting](https://github.com/ZerroDevs/AgentsHub/security/advisories/new).
2. **Direct Email**: If Private Vulnerability Reporting is unavailable, send an email to the project maintainers via the contact details on the [ZerroDevs GitHub Profile](https://github.com/ZerroDevs).

### What to Include in Your Report

To help us investigate and resolve the issue quickly, please provide:
- A clear description of the vulnerability and its potential impact.
- Step-by-step instructions or a minimal proof of concept to reproduce the issue.
- The version of Agents Hub and Windows OS version (e.g., Windows 11 23H2 x64).
- Any affected components (e.g., ConPTY terminal, LAN proxy, WebView2 IPC bridge, credential store).
- Redact all personal tokens, API keys, and sensitive paths from your logs or screenshots.

### Response Timeline

- **Acknowledgment**: Within 48 hours of receipt.
- **Assessment & Triage**: We will confirm the issue, evaluate severity, and keep you updated on progress.
- **Remediation**: A patched release will be published, followed by a public advisory crediting your discovery (unless you prefer to remain anonymous).

---

## Security Architecture & Threat Model

Agents Hub incorporates strict security boundaries across its subsystems:

### 1. Secret & Credential Storage
- **No Secrets in Code**: API keys, tokens, and credentials are never stored in the source repository or binary assets.
- **Windows Credential Manager**: Antigravity profile credentials and sensitive tokens are persisted directly in the OS Windows Credential Manager under unique target names.
- **Local Application Data**: Custom provider API keys and model configurations are saved strictly to `%AppData%\AgentsHub\ai_config.json` with user-level access permissions.
- **Pre-Commit Secret Scanner**: Integrated pattern matching scans uncommitted and staged files before Git commits for OpenAI keys, Anthropic tokens, AWS credentials, GitHub PATs, private keys, SSH keys, and shell injection hazards.

### 2. IPC & WebView2 Bridge Boundary
- **Message Validation**: Communications between the WebView2 interface and the C# host (`DashboardWebBridge.cs`) pass through strict JSON schema parsing and whitelist validation.
- **Explicit RPC Actions**: Only predefined actions can be invoked; arbitrary code injection through the bridge is blocked.
- **External Links**: Links pointing to external URLs are stripped of WebView navigation permissions and opened exclusively in the default system browser via the shell launcher.

### 3. Process Execution & Shell Isolation
- **Antigravity VirtualHome**: Each profile's environment variables (`USERPROFILE`, `HOME`, `APPDATA`, `LOCALAPPDATA`) are redirected to an isolated sandbox directory to prevent cross-account profile contamination.
- **Tree-Aware Process Termination**: When stopping an agent or engine, process trees are traversed and killed via native job objects and PID trees to prevent background orphan processes.
- **Elevated Shell Guards**: Launching Administrator shells or modifying Windows Firewall rules prompts standard Windows User Account Control (UAC) elevation dialogs.

### 4. Network Exposure & Localhost Binding
- **Strict Localhost Default**: All listening ports (`9119`, `9229`, `9230`, `9339`+) bind exclusively to `127.0.0.1`.
- **Opt-In LAN Broadcast**: The LAN reverse proxy (`9191`) binds to `0.0.0.0` only when explicitly toggled on by the user. Turning off LAN broadcast immediately closes the public listener and resets links to `127.0.0.1`.
- **Transparent Reverse Proxy**: The LAN gateway enforces path checks and serves a static offline warning page if the backend engine is unreachable.
