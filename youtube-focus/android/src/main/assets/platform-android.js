/*
 * FocusTube — платформенный слой для Android-приложения.
 * Настройки и статистика хранятся в SharedPreferences, доступ к ним — через
 * JavascriptInterface «FocusTubeAndroid» (см. JsBridge.kt).
 */
(function (root) {
  'use strict';
  const FT = (root.FT = root.FT || {});
  const bridge = root.FocusTubeAndroid;
  if (!bridge) return;

  const listeners = [];
  const parse = (s, fallback) => {
    try {
      return JSON.parse(s);
    } catch (e) {
      return fallback;
    }
  };

  FT.platform = {
    name: 'android',
    async getSettings() {
      return FT.mergeSettings(parse(bridge.getSettings(), {}));
    },
    async saveSettings(partial) {
      bridge.saveSettings(JSON.stringify(partial));
      const s = await FT.platform.getSettings();
      listeners.forEach((cb) => cb(s));
    },
    onSettingsChanged(cb) {
      listeners.push(cb);
    },
    // Вызывается из Kotlin, когда пользователь поменял настройки в приложении.
    _emit(raw) {
      const s = FT.mergeSettings(raw || parse(bridge.getSettings(), {}));
      listeners.forEach((cb) => cb(s));
    },
    async getStats() {
      return FT.normalizeStats(parse(bridge.getStats(), null));
    },
    async addStats(delta) {
      return parse(bridge.addStats(JSON.stringify(delta)), null);
    },
    async apiFetch(url) {
      const resp = await fetch(url);
      const data = await resp.json();
      if (!resp.ok) throw new Error((data && data.error && data.error.message) || 'HTTP ' + resp.status);
      return data;
    },
    openSettings() {
      bridge.openSettings();
    },
  };
})(typeof globalThis !== 'undefined' ? globalThis : this);
