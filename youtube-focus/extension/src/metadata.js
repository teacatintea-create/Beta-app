/*
 * FocusTube — получение метаданных ролика (категория, теги, описание).
 *
 * Два источника:
 *  1) YouTube Data API v3 — если пользователь указал свой API-ключ. Запросы
 *     собираются пачками до 50 роликов (1 единица квоты на пачку).
 *  2) Страница самого ролика (/watch?v=ID) — без ключа. Из неё берём
 *     ytInitialPlayerResponse. Используется только для открытого ролика,
 *     чтобы не скачивать десятки страниц ради ленты.
 */
(function (root) {
  'use strict';
  const FT = (root.FT = root.FT || {});

  const CACHE_LIMIT = 800;
  const cache = new Map(); // videoId -> info
  const inflight = new Map(); // videoId -> Promise

  function remember(id, info) {
    if (!id || !info) return;
    cache.delete(id);
    cache.set(id, info);
    if (cache.size > CACHE_LIMIT) cache.delete(cache.keys().next().value);
  }

  // ---------- разбор HTML страницы ролика ----------

  // Декодирует JS-строку вида '\x7b\x22a\x22...' (так YouTube иногда
  // встраивает JSON на мобильной версии).
  function decodeJsString(src, start) {
    const quote = src[start];
    let out = '';
    for (let i = start + 1; i < src.length; i++) {
      const ch = src[i];
      if (ch === quote) return out;
      if (ch !== '\\') {
        out += ch;
        continue;
      }
      const n = src[++i];
      if (n === 'x') {
        out += String.fromCharCode(parseInt(src.substr(i + 1, 2), 16));
        i += 2;
      } else if (n === 'u') {
        out += String.fromCharCode(parseInt(src.substr(i + 1, 4), 16));
        i += 4;
      } else if (n === 'n') out += '\n';
      else if (n === 't') out += '\t';
      else if (n === 'r') out += '\r';
      else out += n;
    }
    return null;
  }

  // Вырезает JSON-объект, начинающийся с позиции start (учитывая строки).
  function sliceJsonObject(src, start) {
    let depth = 0;
    let inStr = false;
    for (let i = start; i < src.length; i++) {
      const ch = src[i];
      if (inStr) {
        if (ch === '\\') i++;
        else if (ch === '"') inStr = false;
        continue;
      }
      if (ch === '"') inStr = true;
      else if (ch === '{') depth++;
      else if (ch === '}') {
        depth--;
        if (depth === 0) return src.slice(start, i + 1);
      }
    }
    return null;
  }

  function extractAssigned(html, name) {
    const re = new RegExp(name + '"?\\]?\\s*=\\s*', 'g');
    let m;
    while ((m = re.exec(html))) {
      const i = m.index + m[0].length;
      const ch = html[i];
      try {
        if (ch === '{') {
          const json = sliceJsonObject(html, i);
          if (json) return JSON.parse(json);
        } else if (ch === "'" || ch === '"') {
          const str = decodeJsString(html, i);
          if (str) return JSON.parse(str);
        }
      } catch (e) {
        /* пробуем следующее вхождение */
      }
    }
    return null;
  }

  function handleFromUrl(url) {
    const m = /\/@([^/?#]+)/.exec(url || '');
    return m ? '@' + decodeURIComponent(m[1]) : '';
  }

  FT.parseWatchHtml = function (html) {
    const pr = extractAssigned(html, 'ytInitialPlayerResponse');
    if (pr && pr.videoDetails) {
      const vd = pr.videoDetails;
      const mf = (pr.microformat && pr.microformat.playerMicroformatRenderer) || {};
      return {
        title: vd.title || '',
        channel: vd.author || mf.ownerChannelName || '',
        channelId: vd.channelId || mf.externalChannelId || '',
        channelHandle: handleFromUrl(mf.ownerProfileUrl),
        tags: Array.isArray(vd.keywords) ? vd.keywords : [],
        description: vd.shortDescription || '',
        category: mf.category || '',
        categoryId: FT.categoryIdByName(mf.category),
        isShort: false,
        isLive: !!vd.isLiveContent,
        source: 'page',
      };
    }
    // Запасной вариант — регулярные выражения.
    const grab = (re) => {
      const m = re.exec(html);
      if (!m) return '';
      try {
        return JSON.parse('"' + m[1] + '"');
      } catch (e) {
        return m[1];
      }
    };
    const category = grab(/"category":"((?:[^"\\]|\\.)*)"/);
    const title = grab(/"videoDetails":\{[^}]*?"title":"((?:[^"\\]|\\.)*)"/);
    if (!category && !title) return null;
    return {
      title,
      channel: grab(/"author":"((?:[^"\\]|\\.)*)"/),
      channelId: grab(/"channelId":"([\w-]+)"/),
      channelHandle: handleFromUrl(grab(/"ownerProfileUrl":"((?:[^"\\]|\\.)*)"/)),
      tags: [],
      description: grab(/"shortDescription":"((?:[^"\\]|\\.)*)"/),
      category,
      categoryId: FT.categoryIdByName(category),
      source: 'page-regex',
    };
  };

  async function fetchFromPage(videoId) {
    const url = location.origin + '/watch?v=' + encodeURIComponent(videoId);
    const resp = await fetch(url, { credentials: 'include' });
    if (!resp.ok) throw new Error('HTTP ' + resp.status);
    const html = await resp.text();
    return FT.parseWatchHtml(html);
  }

  // ---------- YouTube Data API ----------

  const api = {
    queue: new Map(), // id -> [resolve]
    timer: null,
    disabledUntil: 0,
    lastError: '',
  };

  FT.apiState = api;

  function apiAvailable(settings) {
    return !!(settings && settings.apiKey) && Date.now() >= api.disabledUntil && FT.platform;
  }

  function fromApiItem(item) {
    const sn = item.snippet || {};
    return {
      title: sn.title || '',
      channel: sn.channelTitle || '',
      channelId: sn.channelId || '',
      tags: Array.isArray(sn.tags) ? sn.tags : [],
      description: sn.description || '',
      categoryId: sn.categoryId ? Number(sn.categoryId) : null,
      source: 'api',
    };
  }

  async function flushApi(settings) {
    api.timer = null;
    const batch = Array.from(api.queue.keys()).slice(0, 50);
    const waiters = batch.map((id) => [id, api.queue.get(id)]);
    batch.forEach((id) => api.queue.delete(id));
    if (api.queue.size) api.timer = setTimeout(() => flushApi(settings), 50);

    const url =
      'https://www.googleapis.com/youtube/v3/videos?part=snippet' +
      '&fields=items(id,snippet(title,channelId,channelTitle,tags,description,categoryId))' +
      '&id=' + batch.map(encodeURIComponent).join(',') +
      '&key=' + encodeURIComponent(settings.apiKey);
    let byId = {};
    try {
      const data = await FT.platform.apiFetch(url);
      if (data && data.error) {
        throw new Error((data.error && data.error.message) || 'API error');
      }
      for (const item of (data && data.items) || []) {
        const info = fromApiItem(item);
        byId[item.id] = info;
        remember(item.id, info);
      }
      api.lastError = '';
    } catch (e) {
      api.lastError = String((e && e.message) || e);
      // Неверный ключ или кончилась квота — отдыхаем 10 минут.
      api.disabledUntil = Date.now() + 10 * 60 * 1000;
      byId = {};
    }
    for (const [id, resolvers] of waiters) {
      for (const r of resolvers) r(byId[id] || null);
    }
  }

  function lookupViaApi(videoId, settings) {
    return new Promise((resolve) => {
      const list = api.queue.get(videoId) || [];
      list.push(resolve);
      api.queue.set(videoId, list);
      if (!api.timer) api.timer = setTimeout(() => flushApi(settings), 150);
    });
  }

  // ---------- публичные функции ----------

  FT.meta = {
    cached(videoId) {
      return cache.get(videoId) || null;
    },
    canUseApi(settings) {
      return apiAvailable(settings);
    },
    /** Для ленты: только API (страницы роликов не качаем). */
    lookupForTile(videoId, settings) {
      if (cache.has(videoId)) return Promise.resolve(cache.get(videoId));
      if (!apiAvailable(settings)) return Promise.resolve(null);
      return lookupViaApi(videoId, settings);
    },
    /** Для открытого ролика: API, а если не вышло — страница ролика. */
    lookupForWatch(videoId, settings) {
      const hit = cache.get(videoId);
      if (hit && (hit.categoryId || hit.source === 'page')) return Promise.resolve(hit);
      if (inflight.has(videoId)) return inflight.get(videoId);
      const p = (async () => {
        let info = null;
        if (apiAvailable(settings)) info = await lookupViaApi(videoId, settings);
        if (!info) {
          try {
            info = await fetchFromPage(videoId);
          } catch (e) {
            info = null;
          }
        }
        if (info) remember(videoId, info);
        return info;
      })();
      inflight.set(videoId, p);
      p.finally(() => inflight.delete(videoId));
      return p;
    },
  };
})(typeof globalThis !== 'undefined' ? globalThis : this);
