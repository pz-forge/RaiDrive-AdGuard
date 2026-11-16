# RaiDrive AdGuard

Kill the RaiDrive Mount startup ad popup.

RaiDrive Mount drops a borderless ad panel into the bottom-right corner of the
screen about 15 seconds after launch, every boot. This is a ~20 KB watcher that
closes that window before it ever paints. It does not touch RaiDrive's files,
does not break its code signature, and survives app updates.

```
before:  [ ads.raidrive.com 336x280 panel ]
after:   nothing
```

English | [简体中文](README.zh-CN.md)

## The popup

Measured on RaiDrive Mount 2026.6.25:

| | |
|---|---|
| Owner process | `RaiDrive.Mount.exe` (its own WPF window, not a separate promo exe) |
| Window class | `HwndWrapper[RaiDrive.Mount;;<guid>]` |
| Window title | empty |
| Content host | embedded WebView2 (Edge) |
| Ad slot | `336x280` |
| Page title | `STANDARD 2026.6.25 \| RaiDrive` |
| Source | `ads.raidrive.com` — `/alt/250x250` `/alt/336x280` `/alt/728x90` `/standard/u/widget_202309` |
| Managed code | `RaiDrive.Center.ViewModels.AdvertisingViewModel` (`VisibleAdvertisement`, `DoNotice`, `NoticeKey`) |
| Appears | ~15 s after launch |

The assembly ships with ReleaseManagedObf, and user settings live in an encrypted
binary blob under `HKCU\Software\OpenBoxLab\RaiDrive Mount\Users\{guid}` — there
is no plaintext switch to flip.

### Why the usual tricks fail

Blocking `ads.raidrive.com` in `hosts` does not work. The request fails, but
RaiDrive still creates the WebView2 window — you get an empty box instead of an
ad. It does not remove the popup, it just makes it uglier.

## How this works

A resident watcher. When a window is about to become visible it hides it first,
then checks whether it was the ad shell and closes it. Closing the window does
not disturb RaiDrive — the app stays up and mounts stay connected.

### Match conditions

All five must hold, so ordinary RaiDrive windows are never touched:

1. Top-level window (no parent)
2. Class name starts with `HwndWrapper[RaiDrive`
3. **Window title is empty** — the real UI (`RaiDrive Mount`, `Hidden Window`) always has one
4. Owner process is `RaiDrive.Mount`
5. A `Chrome_*` child exists (the WebView2 host)

### Two lines of defence

- **WinEvent hook** — listens on `EVENT_OBJECT_CREATE` / `EVENT_OBJECT_SHOW`.
  On a match it calls `SW_HIDE` immediately, then resolves asynchronously: if the
  WebView2 host appears it sends `WM_CLOSE`; if not, it restores with `SW_SHOW`.
- **20 ms sweep** — a highest-priority thread polling top-level windows, covering
  the gap when the hook fires late.

Both paths hide before they close, so even a late hook produces no visible flash.

## Install

Requires Windows 10/11 x64. Nothing to build — copy the binary and register it.

```powershell
$dst = "$env:LOCALAPPDATA\RaiDriveAdGuard"
New-Item -ItemType Directory -Force -Path $dst | Out-Null
Copy-Item .\RaiDriveAdGuard.exe $dst

Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
  -Name 'RaiDriveAdGuard' -Value "$dst\RaiDriveAdGuard.exe" -Type String

Start-Process "$dst\RaiDriveAdGuard.exe"
```

Or run `install.ps1` from an elevated-free normal PowerShell — it does the above.

## Verify

Restart RaiDrive and watch the bottom-right corner:

```powershell
Get-Process RaiDrive.Mount | Stop-Process -Force
Start-Process 'C:\Program Files\OpenBoxLab\RaiDrive Mount\RaiDrive.Mount.exe' -ArgumentList '/background'
```

The window should never appear. `guard.log` next to the exe records every hit:

```
2026-11-16 17:49:52.382 === start pid=10528 ===
2026-11-16 17:49:52.421 hooks create=80151785 show=60294571
2026-11-16 17:50:08.240 closed hwnd=2098474 159x27 via sweep
```

Measured over three cold starts of watcher + RaiDrive together, sampling every
200 ms for 60 s each:

| Round | Visible ad frames | App alive |
|---|---|---|
| 1 | 0 | yes |
| 2 | 0 | yes |
| 3 | 0 | yes |

Mapped drive and `RaiDriveMountService` unaffected.

## Usage

```powershell
# close an ad that is already on screen
& "$env:LOCALAPPDATA\RaiDriveAdGuard\RaiDriveAdGuard.exe" --scan
```

## Uninstall

```powershell
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'RaiDriveAdGuard'
Get-Process RaiDriveAdGuard -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item "$env:LOCALAPPDATA\RaiDriveAdGuard" -Recurse -Force
```

RaiDrive is untouched and returns to its normal ad-serving behaviour.

## Build

No SDK required — the .NET Framework compiler that ships with Windows is enough.
Stop any running instance first, the exe is locked while it runs.

```
csc.exe -nologo -target:winexe -optimize+ -platform:x64 -out:RaiDriveAdGuard.exe AdGuard.cs
```

`csc.exe` lives in `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`. It only
supports C# 5, so the source avoids newer syntax.

> Keep `AdGuard.cs` pure ASCII. On Chinese-locale systems the framework compilers
> parse sources as ANSI and fail with a bogus "unterminated string" error on any
> non-ASCII byte.

## Limitations

- Start the watcher before or alongside RaiDrive. Simultaneous autostart measured
  clean, but the earlier it is up the better.
- If a RaiDrive major version changes the window shape (new class name, no
  WebView2) the match fails. Check `guard.log`, then adjust `IsShellCandidate`
  and `HasWebView`.
- Handles this one popup shape only. Other promo windows would need their own rule.

## License

MIT — see [LICENSE](LICENSE).

Not affiliated with OpenBoxLab. Window matching, no code from RaiDrive is
included or redistributed.
