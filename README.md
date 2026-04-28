# swagSMB

GUI frontend for SMBLibrary with some extras and improved security. Windows only.

## Download

<table border="0">
<tbody>
<tr>
<td align="center" valign="top"><a href="https://github.com/fosterbarnes/swagSMB/releases/download/v1.0.0/swagSMBInstaller_v1.0.0_x64.exe"><img src="./.resources/svg/download_x64.svg" width="180" height="auto" alt="x64 installer"/></a></td>
<td align="center" valign="top"><a href="https://github.com/fosterbarnes/swagSMB/releases/download/v1.0.0/swagSMBInstaller_v1.0.0_x86.exe"><img src="./.resources/svg/download_x86.svg" width="180" height="auto" alt="x86 installer"/></a></td>
<td align="center" valign="top"><a href="https://github.com/fosterbarnes/swagSMB/releases/download/v1.0.0/swagSMBInstaller_v1.0.0_arm64.exe"><img src="./.resources/svg/download_arm.svg" width="180" height="auto" alt="ARM64 installer"/></a></td>
</tr>
</tbody>
</table>

## Screenshots

| <h3>Shares</h3> |
|:---:|
| ![Shares](./.resources/scr/1.png) |

| <h3>Settings</h3> |
|:---:|
| ![Shares](./.resources/scr/2.png) |

| <h3>Logs</h3> |
|:---:|
| ![Shares](./.resources/scr/3.png) |

## Security

### What the app enforces
- Encrypted local vault (`%AppData%\swagSMB\config.secure`):
  - PBKDF2-SHA256 key derivation from the master password (600,000 iterations)
  - AES-CBC encryption with random per-file salt and IV
  - HMAC-SHA256 integrity over header + ciphertext (verified before decryption)
- Master password required to open the GUI (and to start the server, unless auto-tray is consented to).
- NTLMv2-only authentication: NTLMv1 and NTLMv1-Extended Session Security are rejected at the auth layer.
- Cryptographically random NTLM server challenge (`RandomNumberGenerator`), not predictable.
- SMB1 is hard-disabled in code; SMB2 and SMB3 only.
- Local share `LocalPath` validation: must be a fully-qualified path on a fixed local disk; UNC paths and reparse points/junctions are rejected.
- Master-password retries throttled in-app (1.5s delay per failure, app exits after 5 consecutive failures).
- Two enabled shares with the same username but different passwords are rejected at startup (prevents auth-conflict bypass).

### What the app does NOT enforce (upstream SMBLibrary 1.5.7 limitations)
- `Require signing`, `Default encryption required`, and `Lock protocol policy` toggles are present in Settings for forward-compatibility but are currently **disabled** in the UI. SMBLibrary 1.5.7's `SMBServer.Start(...)` does not gate connections on signing/encryption negotiation; clients negotiate independently. **Treat the wire as untrusted on hostile networks.**
- If wire-level encryption and integrity are non-negotiable requirements, restrict swagSMB to trusted network segments or protect it with a tunnel (WireGuard, Tailscale, etc.) until native support is available upstream.

### Auto-tray credential storage (opt-in)
- If you enable **Start minimized to tray** and turn **off** *Require master password when starting to tray*, swagSMB stores a DPAPI-protected blob (`tray.key`) under your Windows user. Any program running as your Windows user can decrypt this blob and recover the master password. A consent dialog is shown before this is enabled. Leave the require-password option on for stronger protection.

## Notes
- Default listen port is **5446** (avoids conflicting with Windows SMB on **445**). Change it in Settings if needed and allow the port through the firewall.
- Exported PowerShell mapping scripts contain the share password (Base64-encoded UTF-8 string, **not encrypted**). The export dialog warns you and asks you to delete the script after first run. A "prompt for credentials at runtime" mode is also available.
