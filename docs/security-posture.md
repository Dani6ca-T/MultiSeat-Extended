# Security posture

What MultiSeat changes about a host, what it deliberately does not protect against, and how to undo
the parts you would rather not keep.

Everything here is a statement of fact about the shipped code, not a recommendation. Where a
setting is a genuine weakening, it says so plainly — a security note that only lists reassurances
is not much use.

---

## Machine-wide changes made by the install scripts

Both `prerequisites\install-prerequisites.ps1` and `scripts\install-service.ps1` apply these. They
are **machine-wide**, they affect users and connections that have nothing to do with MultiSeat, and
**they survive uninstalling the service** — nothing removes them for you.

| Setting | Where | Effect beyond MultiSeat |
|---|---|---|
| `fDenyTSConnections = 0` + "Remote Desktop" firewall group enabled | `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server` | Remote Desktop is on, and port 3389 is reachable from the network |
| `UserAuthentication = 0` (**NLA off**) | `…\WinStations\RDP-Tcp` | Every RDP client, not just loopback, reaches the logon stage before authenticating |
| `SecurityLayer = 2` | `…\WinStations\RDP-Tcp` | TLS for the listener. Not a weakening — this one is an improvement |
| `AuthenticationLevel = 0` | `HKLM\SOFTWARE\Policies\…\Terminal Services` | mstsc stops warning about server certificate mismatches, for **every user on the host and every server they connect to** |
| `AllowUnsignedFiles = 1` | same policy key | mstsc stops warning about unsigned `.rdp` files, machine-wide |
| `127.0.0.2` cert trust + device redirection | `HKCU\…\Terminal Server Client\…` | Scoped to loopback only — no wider effect |

### Why NLA has to go

The seat logon is an mstsc connection to `127.0.0.2` with a stored credential, driven by a service
with no one present to answer a prompt. NLA makes that prompt. The setting lives on the `RDP-Tcp`
listener, which serves the network as well as loopback, and Windows offers no per-target form of
it — so there is no way to keep NLA for real clients and drop it only for the loopback connection.

**The mitigation is to keep 3389 off the network**, which is a decision about the host rather than
about MultiSeat, so the scripts do not make it for you. MultiSeat needs no inbound firewall rule at
all: loopback traffic is not filtered. Disabling the group is safe:

```powershell
Disable-NetFirewallRule -DisplayGroup 'Remote Desktop'
```

### Undoing the rest

```powershell
# Restore NLA (breaks seat provisioning while set)
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp' `
  -Name UserAuthentication -Value 1
Restart-Service TermService -Force

# Restore mstsc's certificate and unsigned-file warnings
$p = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services'
Remove-ItemProperty $p -Name AuthenticationLevel  -ErrorAction SilentlyContinue
Remove-ItemProperty $p -Name AllowUnsignedFiles   -ErrorAction SilentlyContinue

# Turn Remote Desktop off entirely (seats stop working)
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' `
  -Name fDenyTSConnections -Value 1
```

Restoring NLA or disabling Remote Desktop stops seats from being created. The two mstsc warning
settings can be restored without breaking anything, at the cost of a dialog appearing during
provisioning that nobody is there to dismiss — which usually shows up as a seat that times out.

---

## What protects the API

- **Plaintext HTTP.** There is no HTTPS and no setting that claims otherwise. An option named
  `RequireHttps` used to exist, defaulted to true, and was never read by anything; it was removed
  rather than implemented.
- **API key**, sent as `X-MultiSeat-Key` or `?key=` (the query form exists because a browser cannot
  set headers on a WebSocket handshake). Bound beyond loopback, that key crosses the network in
  clear — the service logs a warning at startup saying so.
- `MultiSeat:ApiBindLoopbackOnly` binds to loopback only. Worth turning on for any host whose
  dashboard is only ever opened locally, including over a remote-desktop tool.
- **`/ws` is authenticated too.** It was not, and `/ws/seats` broadcasts whole `SeatInfo` objects —
  account names, session ids, ports, PIDs, audio device ids.
- **CORS defaults to loopback origins.** It used to default to `AllowAnyOrigin` whenever
  `CorsOrigins` was empty, which is the shipped configuration.
- **One request bypasses the key**: `GET /api/system/auth`, so the dashboard can show the auth
  toggle before it holds a key. `POST` on that path — the call that turns authentication off — is
  gated, and a test holds that distinction in place.

## What protects the secrets on disk

- `accounts.json` (seat passwords) and `api-key.txt` carry an explicit DACL of SYSTEM +
  Administrators with inheritance disabled. They previously inherited `ProgramData`'s ACL, which
  grants `BUILTIN\Users` read.
- Seat passwords are DPAPI-protected at **CurrentUser** scope, which for this service is SYSTEM.
  The previous `LocalMachine` scope could be decrypted by any process on the machine regardless of
  which user it ran as.
- Seat accounts are **standard users** (`Users` + `Remote Desktop Users`), not administrators. They
  were administrators, on the incorrect belief that SudoVDA IPC required it.
  `MultiSeat:GrantSeatAdministrator` turns that back on for a setup that genuinely needs it, and
  hands the seat the ability to reach SYSTEM and therefore the credential store.
- The RDP credential used during provisioning is written through the credential API, not by passing
  the password on `cmdkey`'s command line.

## What is *not* protected

Stated so nobody has to discover it:

- **An administrator on this host can read everything.** Admins can become SYSTEM, and SYSTEM can
  decrypt the credential store. The file ACLs and DPAPI scope stop non-admin local users, backups,
  and copies taken off the machine — not a determined local administrator.
- **The API has no user model.** One key, all-or-nothing; anyone holding it can provision, tear
  down, and read every seat's details.
- **Seat account passwords are recoverable by design.** The service has to log seats in
  unattended, so it must be able to retrieve them in plaintext. They are protected in storage, not
  hashed.
- **Turning authentication off is a supported action.** `POST /api/system/auth` disables it, and
  the service warns loudly at startup when it is off.
