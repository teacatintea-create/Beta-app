/*
 * FocusTube — платформенный слой для расширения браузера (Chrome, Edge,
 * Яндекс Браузер, Opera, Firefox). Настройки — chrome.storage.sync,
 * статистика — chrome.storage.local (через фоновый скрипт, чтобы вкладки не
 * затирали данные друг друга).
 */
(function (root) {
  'use strict';
  const FT = (root.FT = root.FT || {});
  // chrome.* с колбэками работает и в Chrome, и в Firefox.
  const api = root.chrome;

  function send(msg) {
    return new Promise((resolve, reject) => {
      try {
        api.runtime.sendMessage(msg, (resp) => {
          const err = api.runtime.lastError;
          if (err) reject(new Error(err.message));
          else resolve(resp);
        });
      } catch (e) {
        reject(e);
      }
    });
  }

  FT.platform = {
    name: 'extension',
    getSettings() {
      return new Promise((resolve) => {
        api.storage.sync.get(null, (items) => resolve(FT.mergeSettings(items || {})));
      });
    },
    saveSettings(partial) {
      return new Promise((resolve) => api.storage.sync.set(partial, () => resolve()));
    },
    onSettingsChanged(cb) {
      api.storage.onChanged.addListener((changes, area) => {
        if (area !== 'sync') return;
        FT.platform.getSettings().then(cb);
      });
    },
    getStats() {
      return new Promise((resolve) => {
        api.storage.local.get('stats', (items) => resolve(FT.normalizeStats(items && items.stats)));
      });
    },
    addStats(delta) {
      return send({ type: 'ft:addStats', delta });
    },
    async apiFetch(url) {
      const resp = await send({ type: 'ft:apiFetch', url });
      if (!resp || resp.error) throw new Error((resp && resp.error) || 'Нет ответа');
      return resp.data;
    },
  };
})(typeof globalThis !== 'undefined' ? globalThis : this);
