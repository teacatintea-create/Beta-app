/*
 * FocusTube — фоновый скрипт расширения.
 *  - накапливает дневную статистику (последовательно, без гонок между вкладками);
 *  - делает запросы к YouTube Data API от имени страниц;
 *  - показывает на значке, сколько минут сегодня потрачено.
 */
'use strict';

// chrome.* с колбэками работает и в Chrome, и в Firefox.
const ext = chrome;

if (typeof importScripts === 'function') {
  // Chrome (service worker). В Firefox файл defaults.js подключён через manifest.
  importScripts('defaults.js');
}

let chain = Promise.resolve();

function storageGet(area, key) {
  return new Promise((resolve) => ext.storage[area].get(key, (items) => resolve(items || {})));
}
function storageSet(area, obj) {
  return new Promise((resolve) => ext.storage[area].set(obj, () => resolve()));
}

function addStats(delta) {
  chain = chain.then(async () => {
    const { stats } = await storageGet('local', 'stats');
    const s = FT.normalizeStats(stats);
    for (const k of ['watchSeconds', 'hidden', 'blocked', 'allowed']) {
      s[k] += Number((delta && delta[k]) || 0);
    }
    await storageSet('local', { stats: s });
    updateBadge(s);
    return s;
  });
  return chain;
}

function updateBadge(stats) {
  const action = ext.action || ext.browserAction;
  if (!action) return;
  const min = Math.floor((stats.watchSeconds || 0) / 60);
  action.setBadgeText({ text: min ? String(min) : '' });
  action.setBadgeBackgroundColor({ color: '#3b82f6' });
  action.setTitle({ title: 'FocusTube — сегодня на YouTube: ' + FT.formatMinutes(stats.watchSeconds) });
}

async function apiFetch(url) {
  if (!/^https:\/\/www\.googleapis\.com\/youtube\/v3\//.test(url)) {
    return { error: 'Разрешены только запросы к YouTube Data API' };
  }
  try {
    const resp = await fetch(url);
    const data = await resp.json();
    if (!resp.ok) return { error: (data && data.error && data.error.message) || 'HTTP ' + resp.status };
    return { data };
  } catch (e) {
    return { error: String(e && e.message) };
  }
}

ext.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg || typeof msg.type !== 'string') return false;
  if (msg.type === 'ft:addStats') {
    addStats(msg.delta).then(sendResponse);
    return true;
  }
  if (msg.type === 'ft:apiFetch') {
    apiFetch(msg.url).then(sendResponse);
    return true;
  }
  return false;
});

ext.runtime.onInstalled.addListener(async (details) => {
  if (details.reason === 'install') {
    // Записываем настройки по умолчанию, чтобы их было видно в настройках.
    const current = await storageGet('sync', null);
    await storageSet('sync', FT.mergeSettings(current));
    ext.runtime.openOptionsPage && ext.runtime.openOptionsPage();
  }
  const { stats } = await storageGet('local', 'stats');
  updateBadge(FT.normalizeStats(stats));
});

ext.runtime.onStartup &&
  ext.runtime.onStartup.addListener(async () => {
    const { stats } = await storageGet('local', 'stats');
    updateBadge(FT.normalizeStats(stats));
  });
