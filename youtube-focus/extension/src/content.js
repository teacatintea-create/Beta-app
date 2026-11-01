/*
 * FocusTube — основной скрипт страницы YouTube.
 * Работает одинаково на www.youtube.com (ПК) и m.youtube.com (телефон).
 * Платформенные функции (хранилище, сеть) берутся из FT.platform —
 * его задаёт platform-extension.js или platform-android.js.
 */
(function (root) {
  'use strict';
  const FT = (root.FT = root.FT || {});
  if (root.__focusTubeLoaded) return;
  root.__focusTubeLoaded = true;

  // Карточки роликов на ПК и в мобильной версии.
  const TILE_SELECTOR = [
    'ytd-rich-item-renderer',
    'ytd-video-renderer',
    'ytd-compact-video-renderer',
    'ytd-grid-video-renderer',
    'ytd-reel-item-renderer',
    'ytd-radio-renderer',
    'ytd-compact-radio-renderer',
    'ytd-playlist-renderer',
    'ytd-compact-playlist-renderer',
    'yt-lockup-view-model',
    'ytm-shorts-lockup-view-model',
    'ytm-shorts-lockup-view-model-v2',
    'ytm-rich-item-renderer',
    'ytm-video-with-context-renderer',
    'ytm-compact-video-renderer',
    'ytm-reel-item-renderer',
    'ytm-radio-renderer',
    'ytm-compact-radio-renderer',
    'ytm-playlist-renderer',
    'ytm-compact-playlist-renderer',
    'ytm-video-card-renderer',
  ].join(',');

  // Внутри собственных плейлистов пользователя ничего не фильтруем.
  const SKIP_CONTAINERS =
    'ytd-playlist-video-list-renderer, ytd-playlist-panel-renderer, ytm-playlist-panel-renderer, ytm-playlist-video-list-renderer';

  const TITLE_SELECTORS = [
    '#video-title',
    '.yt-lockup-metadata-view-model__title',
    '.yt-lockup-metadata-view-model-wiz__title',
    '.media-item-headline',
    '.compact-media-item-headline',
    'h3',
    '[class*="headline"]',
  ];

  const CHANNEL_SELECTORS = [
    'ytd-channel-name #text',
    'ytd-channel-name a',
    '#channel-name #text',
    '.yt-content-metadata-view-model__metadata-text',
    '.yt-content-metadata-view-model-wiz__metadata-text',
    '.YtmBadgeAndBylineRendererItemByline',
    '.ytm-badge-and-byline-item-byline',
    '.compact-media-item-byline',
    '.media-item-byline',
    '.subhead',
  ];

  const state = {
    settings: FT.mergeSettings({}),
    active: false,
    watchId: null,
    watchResult: null,
    hold: false, // держать видео на паузе
    pausedByUs: false,
    pending: { watchSeconds: 0, hidden: 0, blocked: 0, allowed: 0 },
    stats: FT.emptyStats(),
    countedHidden: new Set(),
    lastPath: '',
    homeMissingSince: 0,
  };

  // ---------- утилиты ----------

  const text = (el) => (el ? (el.getAttribute('title') || el.textContent || '').trim() : '');

  function videoIdFromHref(href) {
    if (!href) return { id: null, isShort: false, list: null };
    const short = /\/shorts\/([\w-]{11})/.exec(href);
    if (short) return { id: short[1], isShort: true, list: null };
    const v = /[?&]v=([\w-]{11})/.exec(href);
    const list = /[?&]list=([\w-]+)/.exec(href);
    return { id: v ? v[1] : null, isShort: false, list: list ? list[1] : null };
  }

  function pageChannelHandle() {
    const m = /^\/(@[^/]+)/.exec(location.pathname);
    if (m) return decodeURIComponent(m[1]);
    const c = /^\/channel\/([\w-]+)/.exec(location.pathname);
    return c ? c[1] : '';
  }

  function cleanChannel(raw) {
    return String(raw || '')
      .split(/\s[•·]\s|\n/)[0]
      .replace(/[✓✔]/g, '')
      .trim();
  }

  function extractTile(el) {
    const tag = el.tagName.toLowerCase();
    let link = el.querySelector('a[href*="/watch?v="], a[href*="/shorts/"]');
    if (!link) link = el.querySelector('a[href*="list="]');
    const href = link ? link.getAttribute('href') : '';
    const parsed = videoIdFromHref(href);

    let title = '';
    for (const sel of TITLE_SELECTORS) {
      title = text(el.querySelector(sel));
      if (title) break;
    }
    if (!title && link) title = (link.getAttribute('title') || link.getAttribute('aria-label') || '').trim();

    let channel = '';
    for (const sel of CHANNEL_SELECTORS) {
      channel = cleanChannel(text(el.querySelector(sel)));
      if (channel) break;
    }
    const chLink = el.querySelector('a[href^="/@"], a[href*="youtube.com/@"], a[href^="/channel/"]');
    let handle = '';
    if (chLink) {
      const h = /\/(@[^/?#]+)/.exec(chLink.getAttribute('href'));
      handle = h ? decodeURIComponent(h[1]) : '';
      if (!channel) channel = text(chLink);
    }
    if (!channel && !handle) handle = pageChannelHandle();

    const isMix =
      /radio/.test(tag) ||
      /[?&]start_radio=1/.test(href) ||
      (parsed.list && /^RD/.test(parsed.list));
    const isPlaylist = /playlist/.test(tag) || (!parsed.id && !!parsed.list);
    const isShort = parsed.isShort || /shorts|reel/.test(tag);
    let key = parsed.id || (parsed.list ? 'list:' + parsed.list : '');
    if (!key && title) key = 'title:' + title;

    return {
      key,
      videoId: isPlaylist ? null : parsed.id,
      title,
      channel,
      channelHandle: handle,
      isShort,
      isMix,
    };
  }

  // ---------- статистика ----------

  function bump(field, n) {
    state.pending[field] += n || 1;
  }

  async function flushStats() {
    const delta = state.pending;
    if (!delta.watchSeconds && !delta.hidden && !delta.blocked && !delta.allowed) return;
    state.pending = { watchSeconds: 0, hidden: 0, blocked: 0, allowed: 0 };
    try {
      const s = await FT.platform.addStats(delta);
      if (s) state.stats = FT.normalizeStats(s);
    } catch (e) {
      /* расширение перезагружено — не страшно */
    }
  }

  function watchedSecondsToday() {
    return FT.normalizeStats(state.stats).watchSeconds + state.pending.watchSeconds;
  }

  function limitReached() {
    const lim = state.settings.dailyLimitMin;
    return lim > 0 && watchedSecondsToday() >= lim * 60;
  }

  // ---------- оформление ----------

  function applyRootClasses() {
    const html = document.documentElement;
    const s = state.settings;
    const on = state.active;
    html.classList.toggle('ft-active', on);
    html.classList.toggle('ft-hide-shorts', on && s.hideShorts);
    html.classList.toggle('ft-hide-comments', on && s.hideComments);
    html.classList.toggle('ft-hide-endscreen', on && s.hideEndScreen);
    html.classList.toggle('ft-hide-home', on && s.hideHomeFeed);
    html.classList.toggle('ft-on-home', isHome());
  }

  function isHome() {
    return location.pathname === '/' || location.pathname === '';
  }

  // ---------- фильтр карточек ----------

  function decideTile(el) {
    if (el.closest(SKIP_CONTAINERS)) return;
    const info = extractTile(el);
    if (!info.key || (!info.title && !info.isShort && !info.isMix)) return; // ещё не прогрузилась
    const sig = info.key + '|' + info.title + '|' + state.settings.__version;
    if (el.dataset.ftSig === sig && !el.hasAttribute('data-ft-pending')) return;
    el.dataset.ftSig = sig;

    const meta = info.videoId ? FT.meta.cached(info.videoId) : null;
    const full = Object.assign({}, info, meta ? {
      categoryId: meta.categoryId,
      category: meta.category,
      tags: meta.tags,
      description: meta.description,
      channelId: meta.channelId,
      channel: info.channel || meta.channel,
    } : {});
    const res = FT.classify(full, state.settings);

    if (res.allowed) {
      showTile(el);
      return;
    }
    // Не хватает баллов, но категория из API может помочь — спросим.
    const canFlip = !res.decisive && res.score >= res.threshold - 7;
    if (!meta && info.videoId && canFlip && FT.meta.canUseApi(state.settings)) {
      el.setAttribute('data-ft-pending', '');
      el.removeAttribute('data-ft-hidden');
      FT.meta.lookupForTile(info.videoId, state.settings).then((m) => {
        el.removeAttribute('data-ft-pending');
        if (!m) {
          hideTile(el, info.key);
          return;
        }
        el.dataset.ftSig = '';
        decideTile(el);
      });
      return;
    }
    hideTile(el, info.key);
  }

  function hideTile(el, key) {
    el.removeAttribute('data-ft-pending');
    if (!el.hasAttribute('data-ft-hidden')) el.setAttribute('data-ft-hidden', '');
    if (key && !state.countedHidden.has(key)) {
      state.countedHidden.add(key);
      bump('hidden');
    }
  }

  function showTile(el) {
    el.removeAttribute('data-ft-hidden');
    el.removeAttribute('data-ft-pending');
  }

  function scanTiles() {
    const all = document.querySelectorAll(TILE_SELECTOR);
    for (const el of all) {
      // Берём только внешнюю карточку (внутри rich-item лежит lockup и т.п.).
      const parent = el.parentElement && el.parentElement.closest(TILE_SELECTOR);
      if (parent) continue;
      try {
        decideTile(el);
      } catch (e) {
        /* одна странная карточка не должна ломать остальные */
      }
    }
  }

  function resetTiles() {
    for (const el of document.querySelectorAll('[data-ft-sig],[data-ft-hidden],[data-ft-pending]')) {
      el.removeAttribute('data-ft-hidden');
      el.removeAttribute('data-ft-pending');
      delete el.dataset.ftSig;
    }
  }

  // ---------- блокировка видео ----------

  document.addEventListener(
    'play',
    (e) => {
      if (state.hold && e.target && e.target.tagName === 'VIDEO') {
        e.target.pause();
        state.pausedByUs = true;
      }
    },
    true
  );

  function mainVideo() {
    return (
      document.querySelector('#movie_player video, .html5-video-player video, #player video, video.html5-main-video') ||
      document.querySelector('video')
    );
  }

  function holdPlayback(on) {
    state.hold = on;
    const v = mainVideo();
    if (!v) return;
    if (on && !v.paused) {
      v.pause();
      state.pausedByUs = true;
    } else if (!on && state.pausedByUs) {
      state.pausedByUs = false;
      const p = v.play();
      if (p && p.catch) p.catch(() => {});
    }
  }

  function domWatchInfo() {
    const title =
      text(document.querySelector('ytd-watch-metadata h1, h1.ytd-watch-metadata, .slim-video-information-title, .slim-video-metadata-title')) ||
      document.title.replace(/\s*-\s*YouTube\s*$/, '');
    const channel = cleanChannel(
      text(document.querySelector('ytd-watch-metadata ytd-channel-name a, #owner ytd-channel-name a, .slim-owner-channel-name, ytm-slim-owner-renderer .yt-core-attributed-string'))
    );
    return { title, channel };
  }

  async function guardWatch(videoId) {
    if (!state.active) return;
    if (limitReached()) {
      holdPlayback(true);
      showOverlay('limit');
      return;
    }
    if (!state.settings.blockWatchPage) {
      hideOverlay();
      holdPlayback(false);
      return;
    }
    holdPlayback(true);
    showOverlay('checking');

    let info = await FT.meta.lookupForWatch(videoId, state.settings);
    if (state.watchId !== videoId) return; // ушли на другой ролик
    if (!info || !info.title) {
      await new Promise((r) => setTimeout(r, 1200));
      if (state.watchId !== videoId) return;
      info = Object.assign({}, info || {}, domWatchInfo());
    }
    const res = FT.classify(info, state.settings);
    state.watchResult = { info, res, videoId };
    if (res.allowed) {
      bump('allowed');
      hideOverlay();
      holdPlayback(false);
    } else {
      bump('blocked');
      showOverlay('blocked');
    }
  }

  // ---------- оверлей (Shadow DOM, чтобы стили YouTube не мешали) ----------

  let overlayHost = null;

  const OVERLAY_CSS = `
    :host { all: initial; }
    .wrap { position: fixed; inset: 0; z-index: 2147483000; display: flex;
      align-items: center; justify-content: center; padding: 16px;
      background: rgba(10, 14, 20, .94); color: #eef2f6;
      font: 15px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
    .wrap.checking { background: rgba(10, 14, 20, .55); }
    .card { max-width: 560px; width: 100%; background: #161c25; border: 1px solid #2a3442;
      border-radius: 16px; padding: 24px; box-shadow: 0 20px 60px rgba(0,0,0,.5); }
    .checking .card { max-width: 320px; text-align: center; }
    h2 { margin: 0 0 8px; font-size: 22px; line-height: 1.25; }
    .emoji { font-size: 40px; line-height: 1; margin-bottom: 12px; }
    .muted { color: #9aa7b6; }
    .video { margin: 12px 0; padding: 10px 12px; background: #0f141b; border-radius: 10px; }
    .video b { display: block; }
    ul { margin: 8px 0 0; padding-left: 18px; color: #c4ceda; font-size: 14px; }
    li.plus { color: #8bd3a0; } li.minus { color: #f19a9a; }
    .btns { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 18px; }
    button { font: inherit; border: 0; border-radius: 10px; padding: 10px 14px; cursor: pointer;
      background: #253041; color: #eef2f6; }
    button.primary { background: #3b82f6; color: white; }
    button:disabled { opacity: .5; cursor: default; }
    .small { font-size: 13px; }
  `;

  function escapeHtml(s) {
    return String(s || '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  function showOverlay(kind) {
    if (!overlayHost || !overlayHost.isConnected) {
      overlayHost = document.createElement('focustube-overlay');
      overlayHost.attachShadow({ mode: 'open' });
      (document.body || document.documentElement).appendChild(overlayHost);
    }
    const sr = overlayHost.shadowRoot;
    let html = '';
    if (kind === 'checking') {
      html = `<div class="wrap checking"><div class="card"><div class="emoji">🔎</div>
        <div>FocusTube проверяет ролик…</div></div></div>`;
    } else if (kind === 'limit') {
      html = `<div class="wrap"><div class="card"><div class="emoji">⏰</div>
        <h2>Лимит на сегодня исчерпан</h2>
        <p class="muted">Сегодня ты уже провёл на YouTube ${FT.formatMinutes(watchedSecondsToday())}
        при лимите ${state.settings.dailyLimitMin} мин. Самое время заняться чем-то в реальной жизни 🙂</p>
        <div class="btns"><button class="primary" data-act="home">На главную</button>
        <button data-act="back">← Назад</button></div></div></div>`;
    } else {
      const wr = state.watchResult || { info: {}, res: { reasons: [], score: 0, threshold: 0 } };
      const reasons = wr.res.reasons
        .slice(0, 6)
        .map((r) => `<li class="${r.delta > 0 ? 'plus' : 'minus'}">${escapeHtml(r.text)} (${r.delta > 0 ? '+' : ''}${r.delta})</li>`)
        .join('');
      const ch = wr.info.channel || wr.info.channelHandle || '';
      const canOnce = state.settings.mode !== 'strict';
      html = `<div class="wrap"><div class="card"><div class="emoji">🎯</div>
        <h2>Этот ролик не похож на полезный</h2>
        <div class="video"><b>${escapeHtml(wr.info.title || 'Без названия')}</b>
          <span class="muted">${escapeHtml(ch)}</span></div>
        <div class="muted small">Баллы: ${wr.res.score} из нужных ${wr.res.threshold}.
          ${reasons ? 'Почему:' : 'Не нашлось ни одной приметы образовательного или творческого контента.'}</div>
        ${reasons ? `<ul>${reasons}</ul>` : ''}
        <div class="btns">
          <button class="primary" data-act="search">🔎 Найти что-то полезное</button>
          <button data-act="back">← Назад</button>
          ${ch ? `<button data-act="allow-channel">✅ Канал полезный — разрешить</button>` : ''}
          ${canOnce ? `<button data-act="once" class="small">Посмотреть один раз</button>` : ''}
        </div></div></div>`;
    }
    sr.innerHTML = `<style>${OVERLAY_CSS}</style>${html}`;
    sr.querySelectorAll('button[data-act]').forEach((b) => b.addEventListener('click', onOverlayAction));
  }

  function hideOverlay() {
    if (overlayHost) overlayHost.remove();
    overlayHost = null;
  }

  function goBack() {
    if (history.length > 1) history.back();
    else location.href = location.origin + '/';
  }

  function startCountdown(btn, seconds, label, done) {
    if (seconds <= 0) return done();
    btn.disabled = true;
    let left = seconds;
    btn.textContent = label + ' (' + left + ')';
    const t = setInterval(() => {
      left--;
      if (!btn.isConnected) return clearInterval(t);
      if (left <= 0) {
        clearInterval(t);
        btn.disabled = false;
        btn.textContent = 'Подтвердить: ' + label.toLowerCase();
        btn.onclick = (e) => {
          e.stopImmediatePropagation();
          done();
        };
        return;
      }
      btn.textContent = label + ' (' + left + ')';
    }, 1000);
  }

  async function onOverlayAction(e) {
    const act = e.currentTarget.dataset.act;
    const wr = state.watchResult;
    if (act === 'back') goBack();
    else if (act === 'home' || act === 'search') location.href = location.origin + '/';
    else if (act === 'allow-channel' && wr) {
      const name = wr.info.channelHandle || wr.info.channel;
      startCountdown(e.currentTarget, Math.min(state.settings.lockDelaySec, 10), 'Разрешить канал', async () => {
        const list = state.settings.whitelistChannels.slice();
        if (name && !list.includes(name)) list.push(name);
        await FT.platform.saveSettings({ whitelistChannels: list });
      });
    } else if (act === 'once') {
      startCountdown(e.currentTarget, state.settings.lockDelaySec, 'Посмотреть один раз', () => {
        hideOverlay();
        holdPlayback(false);
      });
    }
  }

  // ---------- главная страница без ленты ----------

  let homeHost = null;

  const HOME_CSS = `
    :host { all: initial; display: block; }
    .panel { box-sizing: border-box; max-width: 720px; margin: 32px auto; padding: 24px 16px;
      color: var(--ft-fg, #0f0f0f); font: 15px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; text-align: center; }
    .panel.floating { position: fixed; left: 0; right: 0; top: 64px; z-index: 1000; margin: 0 auto;
      background: var(--ft-bg, #fff); border-radius: 16px; }
    h1 { font-size: 28px; line-height: 1.2; margin: 0 0 6px; }
    p { margin: 0 0 18px; color: var(--ft-muted, #606060); }
    form { display: flex; gap: 8px; max-width: 560px; margin: 0 auto 16px; }
    input { flex: 1; min-width: 0; font: inherit; padding: 12px 16px; border-radius: 999px;
      border: 1px solid var(--ft-border, #ccc); background: var(--ft-input, #fff); color: inherit; }
    button { font: inherit; padding: 12px 18px; border: 0; border-radius: 999px; cursor: pointer;
      background: #3b82f6; color: #fff; }
    .chips, .links { display: flex; flex-wrap: wrap; gap: 8px; justify-content: center; margin: 0 0 16px; }
    .chip, .link { padding: 8px 14px; border-radius: 999px; text-decoration: none; color: inherit;
      background: var(--ft-chip, #f2f2f2); font-size: 14px; }
    .stats { font-size: 13px; color: var(--ft-muted, #606060); }
  `;

  function renderHomePanel(floating) {
    if (!homeHost) {
      homeHost = document.createElement('focustube-home');
      homeHost.attachShadow({ mode: 'open' });
    }
    const dark = document.documentElement.hasAttribute('dark') ||
      (root.matchMedia && root.matchMedia('(prefers-color-scheme: dark)').matches && !document.querySelector('ytd-app'));
    homeHost.style.cssText = dark
      ? '--ft-fg:#f1f1f1;--ft-muted:#aaa;--ft-border:#303030;--ft-input:#121212;--ft-chip:#272727;--ft-bg:#0f0f0f'
      : '';
    const s = state.settings;
    const chips = s.interests
      .map((i) => `<a class="chip" href="/results?search_query=${encodeURIComponent(i)}">${escapeHtml(i)}</a>`)
      .join('');
    const limit = s.dailyLimitMin ? ` из ${s.dailyLimitMin} мин` : '';
    const st = FT.normalizeStats(state.stats);
    homeHost.shadowRoot.innerHTML = `<style>${HOME_CSS}</style>
      <div class="panel ${floating ? 'floating' : ''}">
        <h1>Что хочешь узнать сегодня?</h1>
        <p>Лента рекомендаций скрыта FocusTube. Ищи то, ради чего пришёл.</p>
        <form><input name="q" placeholder="Например: как работает трансформатор" autocomplete="off">
          <button type="submit">Найти</button></form>
        ${chips ? `<div class="chips">${chips}</div>` : ''}
        <div class="links">
          <a class="link" href="/feed/subscriptions">Подписки</a>
          <a class="link" href="/playlist?list=WL">Смотреть позже</a>
          <a class="link" href="/feed/playlists">Плейлисты</a>
          <a class="link" href="/feed/history">История</a>
          ${FT.platform.openSettings ? '<a class="link" href="#" data-ft-settings>⚙ Настройки FocusTube</a>' : ''}
        </div>
        <div class="stats">Сегодня на YouTube: ${FT.formatMinutes(watchedSecondsToday())}${limit} ·
          скрыто роликов: ${st.hidden + state.pending.hidden} · заблокировано: ${st.blocked + state.pending.blocked}</div>
      </div>`;
    homeHost.shadowRoot.querySelector('form').addEventListener('submit', (e) => {
      e.preventDefault();
      const q = e.target.q.value.trim();
      if (q) location.href = location.origin + '/results?search_query=' + encodeURIComponent(q);
    });
    const settingsLink = homeHost.shadowRoot.querySelector('[data-ft-settings]');
    if (settingsLink) {
      settingsLink.addEventListener('click', (e) => {
        e.preventDefault();
        FT.platform.openSettings();
      });
    }
  }

  function homeAnchor() {
    return (
      document.querySelector('ytd-browse[page-subtype="home"]:not([hidden]) ytd-rich-grid-renderer') ||
      document.querySelector('ytm-browse ytm-rich-grid-renderer') ||
      document.querySelector('ytm-browse ytm-section-list-renderer')
    );
  }

  function updateHome() {
    const want = state.active && state.settings.hideHomeFeed && isHome();
    if (!want) {
      if (homeHost) homeHost.remove();
      state.homeMissingSince = 0;
      return;
    }
    const anchor = homeAnchor();
    if (anchor && anchor.parentElement) {
      if (!homeHost || !homeHost.isConnected || homeHost.nextElementSibling !== anchor) {
        renderHomePanel(false);
        anchor.parentElement.insertBefore(homeHost, anchor);
      }
      state.homeMissingSince = 0;
      return;
    }
    // Не нашли, куда встроиться (YouTube поменял вёрстку) — показываем поверх.
    if (!state.homeMissingSince) state.homeMissingSince = Date.now();
    if (Date.now() - state.homeMissingSince > 2500 && document.body && (!homeHost || !homeHost.isConnected)) {
      renderHomePanel(true);
      document.body.appendChild(homeHost);
    }
  }

  // ---------- навигация ----------

  function onLocation() {
    const url = new URL(location.href);
    const path = url.pathname;
    applyRootClasses();

    if (!state.active) {
      hideOverlay();
      holdPlayback(false);
      state.watchId = null;
      return;
    }

    const short = /^\/shorts\/([\w-]{11})/.exec(path);
    if (short && state.settings.hideShorts) {
      // Shorts открываем как обычный ролик — там он пройдёт проверку.
      location.replace(location.origin + '/watch?v=' + short[1]);
      return;
    }

    const id = path === '/watch' ? url.searchParams.get('v') : null;
    if (id) {
      if (id !== state.watchId) {
        state.watchId = id;
        state.watchResult = null;
        guardWatch(id);
      }
    } else {
      if (state.watchId) {
        state.watchId = null;
        hideOverlay();
        holdPlayback(false);
      }
    }
    state.lastPath = path + url.search;
  }

  // ---------- учёт времени ----------

  function tickTime(seconds) {
    if (document.visibilityState !== 'visible') return;
    if (location.pathname !== '/watch' && !location.pathname.startsWith('/shorts')) return;
    const v = mainVideo();
    if (!v || v.paused || v.ended || v.readyState < 2) return;
    bump('watchSeconds', seconds);
    if (state.active && limitReached() && state.watchId) {
      holdPlayback(true);
      showOverlay('limit');
    }
  }

  // ---------- запуск ----------

  function setSettings(raw) {
    const s = FT.mergeSettings(raw);
    s.__version = (state.settings.__version || 0) + 1;
    state.settings = s;
    const wasActive = state.active;
    state.active = FT.isActive(s);
    resetTiles();
    state.watchId = null; // перепроверить текущий ролик с новыми настройками
    if (wasActive && !state.active) hideOverlay();
    onLocation();
    if (homeHost && homeHost.isConnected) renderHomePanel(homeHost.shadowRoot.querySelector('.floating'));
    scheduleScan();
  }

  let scanTimer = null;
  function scheduleScan() {
    if (scanTimer) return;
    scanTimer = setTimeout(() => {
      scanTimer = null;
      if (state.active) scanTiles();
      updateHome();
    }, 120);
  }

  FT.start = async function () {
    if (FT._started) return;
    FT._started = true;
    // В Android скрипт может запуститься раньше, чем появится <html>.
    while (!document.documentElement) await new Promise((r) => setTimeout(r, 10));
    try {
      state.stats = FT.normalizeStats(await FT.platform.getStats());
    } catch (e) {
      /* ignore */
    }
    setSettings(await FT.platform.getSettings());
    FT.platform.onSettingsChanged((s) => setSettings(s));

    const observer = new MutationObserver(scheduleScan);
    const attach = () => observer.observe(document.documentElement, { childList: true, subtree: true });
    attach();

    root.addEventListener('yt-navigate-finish', onLocation);
    root.addEventListener('popstate', onLocation);

    let lastHref = location.href;
    setInterval(() => {
      if (location.href !== lastHref) {
        lastHref = location.href;
        onLocation();
      }
      // Пауза закончилась или наступила.
      const nowActive = FT.isActive(state.settings);
      if (nowActive !== state.active) setSettings(state.settings);
    }, 500);

    setInterval(() => {
      // YouTube переиспользует карточки — периодически перепроверяем.
      if (state.active) scanTiles();
      updateHome();
    }, 1500);

    setInterval(() => tickTime(5), 5000);
    setInterval(flushStats, 15000);
    root.addEventListener('pagehide', flushStats);
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'hidden') flushStats();
    });
  };

  // Для тестов.
  FT._content = { extractTile, state, scanTiles, onLocation, setSettings };

  if (FT.platform && !FT.platform.manualStart) FT.start();
})(typeof globalThis !== 'undefined' ? globalThis : this);
