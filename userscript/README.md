# Userscript (optional, Firefox)

Complements the router: the router catches links handed over by Windows, this script
catches clicks on Yandex Music links **inside** a browser page.

Needs Firefox with [Tampermonkey](https://addons.mozilla.org/firefox/addon/tampermonkey/)
or Violentmonkey.

## Install

1. Open Tampermonkey → *Create a new script* (or drag `yandex-music-app.user.js` into
   the browser window).
2. Confirm installation.
3. Click a Yandex Music link on any page — the app opens.
4. On a track/album page opened without a click, a yellow **«Открыть в Яндекс Музыке»**
   button appears in the bottom-right corner.

## Why the button

Browsers block script-initiated navigation to external protocols unless it happens
during a user gesture. A click is a gesture, a page load is not, hence the button.

## Limits

- Firefox only (any Chromium browser needs its own extension).
- Disabled in private windows by default.
- Doesn't affect links already handled by the router (Telegram, shortcuts, etc.).
