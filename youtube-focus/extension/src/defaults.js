/*
 * FocusTube — общие настройки по умолчанию и справочники.
 * Файл используется и в расширении для браузера, и в Android-приложении
 * (подключается в WebView), и в тестах под Node.js.
 */
(function (root) {
  'use strict';
  const FT = (root.FT = root.FT || {});

  // Категории YouTube (videoCategoryId). Названия бывают на английском и на
  // русском — в зависимости от языка интерфейса, поэтому храним оба варианта.
  FT.CATEGORIES = {
    1: { en: 'Film & Animation', ru: 'Фильмы и анимация', group: 'creative' },
    2: { en: 'Autos & Vehicles', ru: 'Транспорт', group: 'neutral' },
    10: { en: 'Music', ru: 'Музыка', group: 'creative' },
    15: { en: 'Pets & Animals', ru: 'Животные', group: 'neutral' },
    17: { en: 'Sports', ru: 'Спорт', group: 'neutral' },
    19: { en: 'Travel & Events', ru: 'Путешествия', group: 'neutral' },
    20: { en: 'Gaming', ru: 'Видеоигры', group: 'distracting' },
    22: { en: 'People & Blogs', ru: 'Люди и блоги', group: 'distracting' },
    23: { en: 'Comedy', ru: 'Юмор', group: 'distracting' },
    24: { en: 'Entertainment', ru: 'Развлечения', group: 'distracting' },
    25: { en: 'News & Politics', ru: 'Новости и политика', group: 'neutral' },
    26: { en: 'Howto & Style', ru: 'Хобби и стиль', group: 'creative' },
    27: { en: 'Education', ru: 'Образование', group: 'education' },
    28: { en: 'Science & Technology', ru: 'Наука и техника', group: 'education' },
    29: { en: 'Nonprofits & Activism', ru: 'Некоммерческие организации', group: 'neutral' },
  };

  FT.categoryIdByName = function (name) {
    if (!name) return null;
    const n = String(name).trim().toLowerCase();
    for (const id of Object.keys(FT.CATEGORIES)) {
      const c = FT.CATEGORIES[id];
      if (c.en.toLowerCase() === n || c.ru.toLowerCase() === n) return Number(id);
    }
    // Частые альтернативные написания.
    const aliases = {
      'howto & style': 26,
      'how-to & style': 26,
      'наука и технологии': 28,
      'хобби и образ жизни': 26,
      'фильмы и мультфильмы': 1,
      'игры': 20,
      'комедии': 23,
      'новости': 25,
      'образование и обучение': 27,
    };
    return aliases[n] || null;
  };

  FT.DEFAULTS = {
    enabled: true,
    // strict — только явно образовательное; balanced — нужна хотя бы одна
    // «полезная» примета; soft — скрывается только явное развлекалово.
    mode: 'balanced',
    allowCreative: true,
    // Категории, которые считаются полезными (творчество включается/выключается
    // отдельным флагом allowCreative).
    allowedCategories: [27, 28],
    creativeCategories: [1, 10, 26],
    hideShorts: true,
    hideHomeFeed: true,
    hideComments: false,
    hideEndScreen: true,
    hideMixes: true,
    blockWatchPage: true,
    dailyLimitMin: 0,
    apiKey: '',
    whitelistChannels: [],
    blacklistChannels: [],
    extraAllowKeywords: [],
    extraBlockKeywords: [],
    interests: ['физика', 'программирование', 'история', 'рисование'],
    lockDelaySec: 20,
    pausedUntil: 0,
  };

  FT.MODE_THRESHOLDS = { strict: 4, balanced: 2, soft: -1 };

  FT.MODE_LABELS = {
    strict: 'Строгий — только явно образовательное',
    balanced: 'Сбалансированный — нужна хотя бы одна полезная примета',
    soft: 'Мягкий — скрывать только явное развлекалово',
  };

  FT.mergeSettings = function (stored) {
    const s = Object.assign({}, FT.DEFAULTS, stored || {});
    for (const key of [
      'allowedCategories',
      'creativeCategories',
      'whitelistChannels',
      'blacklistChannels',
      'extraAllowKeywords',
      'extraBlockKeywords',
      'interests',
    ]) {
      if (!Array.isArray(s[key])) s[key] = FT.DEFAULTS[key].slice();
    }
    if (!(s.mode in FT.MODE_THRESHOLDS)) s.mode = FT.DEFAULTS.mode;
    s.dailyLimitMin = Math.max(0, Number(s.dailyLimitMin) || 0);
    s.lockDelaySec = Math.max(0, Number(s.lockDelaySec) || 0);
    s.pausedUntil = Number(s.pausedUntil) || 0;
    return s;
  };

  FT.isActive = function (s, now) {
    return !!s.enabled && (now || Date.now()) >= (s.pausedUntil || 0);
  };

  FT.todayKey = function (d) {
    d = d || new Date();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return d.getFullYear() + '-' + m + '-' + day;
  };

  FT.emptyStats = function () {
    return { date: FT.todayKey(), watchSeconds: 0, hidden: 0, blocked: 0, allowed: 0 };
  };

  FT.normalizeStats = function (stats) {
    if (!stats || stats.date !== FT.todayKey()) return FT.emptyStats();
    return Object.assign(FT.emptyStats(), stats);
  };

  FT.formatMinutes = function (seconds) {
    const m = Math.floor((seconds || 0) / 60);
    if (m < 60) return m + ' мин';
    return Math.floor(m / 60) + ' ч ' + (m % 60) + ' мин';
  };
})(typeof globalThis !== 'undefined' ? globalThis : this);
