// ==UserScript==
// @name         Yandex Music: открывать в приложении
// @namespace    local.ym
// @version      2.0
// @description  Ссылки music.yandex.* открываются в десктопном приложении Яндекс Музыки
// @match        *://*/*
// @run-at       document-start
// @grant        none
// ==/UserScript==

// Дополнение к Yandex Link Router: маршрутизатор ловит ссылки, которые отдаёт
// система, а этот скрипт — клики внутри браузера. Нужен Tampermonkey или
// Violentmonkey в Firefox.

(() => {
  'use strict';

  const HOST = /^music\.yandex\.(ru|com|by|kz|uz)$/i;

  function toDeeplink(href) {
    let u;
    try {
      u = new URL(href, location.href);
    } catch (e) {
      return null;
    }
    if (!HOST.test(u.hostname)) return null;
    const path = u.pathname.replace(/^\/+/, '');
    if (!path) return null;
    return 'yandexmusic://' + path;
  }

  // 1. Клик по ссылке на любом сайте — это действие пользователя, браузер разрешает
  document.addEventListener('click', (e) => {
    const el = e.target instanceof Element ? e.target.closest('a[href]') : null;
    if (!el) return;
    const link = toDeeplink(el.href);
    if (!link) return;
    e.preventDefault();
    e.stopPropagation();
    location.href = link;
  }, true);

  // 2. Если страница трека открылась сама (из мессенджера, новой вкладки) —
  //    автопереход браузер заблокирует, поэтому показываем кнопку
  const isContentPage = HOST.test(location.hostname) &&
    /^\/(album|track|artist|playlists|users)\//.test(location.pathname);

  if (!isContentPage) return;

  const deep = 'yandexmusic://' + location.pathname.replace(/^\/+/, '');

  function showBanner() {
    if (document.getElementById('ym-open-app')) return;
    const box = document.createElement('div');
    box.id = 'ym-open-app';
    box.textContent = 'Открыть в Яндекс Музыке';
    box.style.cssText = [
      'position:fixed', 'right:16px', 'bottom:16px', 'z-index:2147483647',
      'background:#ffdb4d', 'color:#000', 'padding:12px 18px', 'border-radius:10px',
      'font:600 14px/1.2 system-ui,-apple-system,Segoe UI,sans-serif',
      'box-shadow:0 6px 20px rgba(0,0,0,.35)', 'cursor:pointer',
      'border:1px solid rgba(0,0,0,.15)', 'user-select:none'
    ].join(';');
    box.addEventListener('click', () => {
      location.href = deep;
    });
    document.documentElement.appendChild(box);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', showBanner);
  } else {
    showBanner();
  }
})();
