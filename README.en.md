# Yandex Link Router

[Русский](README.md) | **English**

Makes Yandex Music links open in the desktop app instead of the browser on Windows —
without touching the browser itself.

**Windows 10 and 11 only.** The whole approach is built on the Windows shell
(ProgIDs, `StartMenuInternet` clients, the `yandexmusic://` protocol registration);
macOS and Linux handle links differently and are out of scope.

```
link from Telegram / Word / anywhere
        |
   ymrouter.exe            <- becomes the default "browser"
        |-- music.yandex.*/album|track|artist|playlist|users/...  -> yandexmusic://... (app)
        `-- everything else                                        -> your browser
```

## Why

Windows has no per-site handler for `https://`: the "Apps for websites" feature only
works for packaged (Microsoft Store) apps, and the Yandex Music desktop app is an
Electron app that registers only its own `yandexmusic://` protocol. A Store version
is no longer available, so links like `https://music.yandex.ru/album/1/track/2`
always landed in the browser.

This project puts a tiny (24 KB) router between the shell and the browser. It registers
itself as a browser candidate, inspects each URL and either forwards it to the app as
a `yandexmusic://` deeplink or passes it to the browser that was default before.

## Where a link goes

The router only sees links that the **shell** hands over. Everything that happens
*inside* a browser tab never reaches it — that is a Windows boundary, not a bug.

| Where the link comes from | Who handles it |
|---|---|
| Telegram, Discord, mail client, Explorer, desktop shortcut, Start menu | `ymrouter.exe` → app |
| Click on a link inside a web page | that browser — the router never sees it |
| `music.yandex.ru` URL typed or pasted into the address bar | that browser |
| Link inside Chrome / Edge / Opera / other browser | that browser, as usual |
| `yandexmusic://...` link | the app itself, no router involved |

The router matches only exact hosts (`music.yandex.ru`, `.com`, `.by`, `.kz`, `.uz`)
and only content paths (`/album/`, `/track/`, `/artist/`, `/playlist/`, `/playlists/`,
`/users/`). Everything else — including `music.yandex.ru/` itself, search pages and
login pages — goes to the browser. Query parameters (`ref_id`, UTM tags) are dropped
when the deeplink is built.

## Install

1. Download `YandexLinkRouter-Setup.exe` from the [latest release](../../releases/latest).
2. Run it — no admin rights required. SmartScreen may warn about an unsigned binary:
   *More info → Run anyway*.
3. In the Settings window that opens, pick **Yandex Link Router** as your web browser
   (Windows 10: *Apps → Default apps → Web browser*; Windows 11: *Apps → Default apps →
   Yandex Link Router → set http and https*).
4. Click any Yandex Music link.

Requirements: Windows 10/11, .NET Framework 4.x (built in), any installed browser,
and the Yandex Music desktop app. Without the app the router quietly falls back to
the browser.

The install is per user (`HKCU` + `%LOCALAPPDATA%`), so on a shared PC every account
has to run the installer once.

## Links clicked inside the browser

A browser page cannot be intercepted by the shell: a click inside a tab never reaches
Windows, so the router cannot see or redirect those links. They simply open in the
browser — a limitation of the approach, not a setting.

The only way around it is a browser extension that intercepts navigation. This project
does not ship one.

If a link has already opened in the browser, the track has to be found in the app
manually. Links that arrive in a messenger or another app are caught by the router.

## What the installer does

- copies `ymrouter.exe` to `%LOCALAPPDATA%\Programs\YandexLinkRouter`;
- remembers the current default browser and its command line;
- registers `YMRouter.HTTP` / `YMRouter.HTTPS` ProgIDs, a
  `Software\Clients\StartMenuInternet\YMRouter` client and a `RegisteredApplications`
  entry, so the app shows up in *Default apps*;
- adds an entry to *Installed apps* with an uninstaller;
- opens `ms-settings:defaultapps`.

No services, no scheduled tasks, no elevation.

## Changed your default browser?

The router remembers the browser at install time and does not follow later changes.
If you switch the default browser, ordinary links keep going to the old one.

Fix: run the installer again. It picks up the current default browser (or the saved
one, if the router is still the default), updates the settings and opens *Default
apps* again. You do not need to re-select "Yandex Link Router" if it is already the
default.

## Uninstall

1. Settings → Apps → Installed apps → "Yandex Link Router" → Uninstall.
2. In the Settings window that opens, pick your browser again: the router entry is
   gone and Windows will ask what should open links.

Everything is removed: `%LOCALAPPDATA%\Programs\YandexLinkRouter`, the `HKCU` keys
(`YMRouter.HTTP`/`.HTTPS`, `Software\YandexLinkRouter`, `StartMenuInternet\YMRouter`,
the `RegisteredApplications` value) and the log in `%LOCALAPPDATA%\YandexLinkRouter`.

If uninstalling from Settings is not available, run the uninstaller directly:

```
%LOCALAPPDATA%\Programs\YandexLinkRouter\uninstall.exe /uninstall
```

## Build

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Only `csc.exe` from .NET Framework 4 is needed (present on every Windows 10/11).
The router is embedded into the installer as a resource, so the output is a single
24 KB executable. CI builds the same thing on every push.

## Repository layout

```
src/Router.cs          link router itself
src/Setup.cs           per-user installer / uninstaller
build.ps1              build script (router -> installer -> zip)
install-notes.txt      instructions shipped inside the zip
.github/workflows/     CI: build on push, attach binaries on tag
```

## How the deeplink works

The app registers `yandexmusic://` and strips the prefix, treating the rest as a path:
`yandexmusic://album/3891291/track/31995925` opens that track. The router builds
exactly that from `https://music.yandex.ru/album/3891291/track/31995925?...`, so query
parameters are dropped.

## Limits and caveats

- Windows only. This is shell integration (registry ProgIDs, browser clients,
  protocol handler), so it cannot be ported to macOS or Linux as is.
- The browser is remembered at install time. Changed your default browser? Run the
  installer again.
- Windows protects the default-browser choice with a hash, so the last step is always
  manual — a program cannot silently become the default browser. Windows feature
  updates may reset the choice; just pick the router again.
- Every link starts a new app process; the already running instance picks the deeplink
  up via single-instance. The app's own log shows harmless cache errors during that.
- If the Yandex Music app is not installed (or was removed), the router falls back to
  the browser — `yandexmusic://` is checked before forwarding.
- The router only understands `http`/`https`. `mailto:`, `ftp:` and other schemes are
  handled by Windows as usual.
- Both binaries are unsigned, so SmartScreen and some antivirus products may complain
  once.
- Uninstalling leaves the default-browser choice pointing at a removed app; Windows
  will ask you to pick a browser, or pick it manually in *Default apps*.

## License

MIT — see [LICENSE](LICENSE).
