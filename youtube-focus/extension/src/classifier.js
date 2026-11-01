/*
 * FocusTube — классификатор роликов.
 *
 * Решает, показывать ли ролик, по набору примет:
 *  - белый/чёрный список каналов (пользовательский) и встроенный список
 *    научно-популярных каналов;
 *  - категория YouTube (если удалось её узнать: из страницы ролика или через
 *    YouTube Data API);
 *  - ключевые слова в названии, тегах и описании (русский и английский);
 *  - признаки кликбейта.
 *
 * Каждая примета добавляет или отнимает баллы. Ролик показывается, если сумма
 * не ниже порога выбранного режима (FT.MODE_THRESHOLDS).
 */
(function (root) {
  'use strict';
  const FT = (root.FT = root.FT || {});

  // Синтаксис правил: строка — начало слова (основа). «$» в конце — слово
  // целиком. Регистр и «ё/е» не важны.
  const EDU_RULES = [
    // русский
    ['лекци', 3], ['урок', 2], ['курс', 2], ['обучени', 2], ['учебн', 2],
    ['туториал', 3], ['объясн', 2], ['разбор', 2], ['наук', 2], ['научн', 3],
    ['научпоп', 3], ['физик', 3], ['математ', 3], ['алгебр', 3], ['геометри', 3],
    ['матанализ', 3], ['интеграл', 2], ['производн', 2], ['теорем', 3],
    ['уравнени', 2], ['формул', 1], ['хими', 2], ['биолог', 3], ['генетик', 3],
    ['эволюци', 2], ['анатоми', 2], ['медицин', 2], ['нейробиолог', 3],
    ['астроном', 3], ['астрофиз', 3], ['космос', 2], ['вселенн', 2], ['планет', 1],
    ['квант', 2], ['термодинам', 3], ['механик', 2], ['сопромат', 3],
    ['электротехник', 3], ['электроник', 2], ['схемотехник', 3], ['инженер', 2],
    ['истори', 1], ['историческ', 2], ['археолог', 3], ['антрополог', 3],
    ['географ', 2], ['экономик', 2], ['философ', 2], ['психолог', 1],
    ['социолог', 2], ['лингвист', 3], ['грамматик', 2], ['английский язык', 2],
    ['программирован', 3], ['программист', 2], ['алгоритм', 3],
    ['структуры данных', 3], ['нейросет', 2], ['машинное обучение', 3],
    ['искусственный интеллект', 2], ['базы данных', 2], ['операционн', 2],
    ['компьютерн', 1], ['документальн', 3], ['исследовани', 2], ['эксперимент', 2],
    ['учен', 2], ['профессор', 2], ['академик', 2], ['университет', 2],
    ['семинар', 2], ['вебинар', 2], ['конспект', 2], ['егэ$', 2], ['огэ$', 2],
    ['экзамен', 1], ['подготовка к', 1], ['для начинающих', 2], ['с нуля', 2],
    ['пошагов', 1], ['как работает', 2], ['как устроен', 2], ['что такое', 2],
    ['почему', 1], ['зачем', 1], ['как сделать', 1], ['своими словами', 1],
    // английский
    ['lecture', 3], ['lesson', 2], ['course', 2], ['tutorial', 3], ['explained', 3],
    ['explainer', 2], ['explain', 1], ['how to', 2], ['how does', 2], ['how do', 1],
    ['why do', 1], ['why is', 1], ['why are', 1], ['what is', 1], ['what if', 1],
    ['science', 2], ['scientific', 2], ['physics', 3], ['math$', 3], ['maths$', 3],
    ['mathemat', 3], ['calculus', 3], ['algebra', 3], ['geometry', 3], ['theorem', 3],
    ['proof$', 1], ['chemistry', 3], ['chemical', 1], ['biology', 3], ['genetic', 2],
    ['evolution', 2], ['anatomy', 2], ['medicine', 2], ['neuroscience', 3],
    ['astronomy', 3], ['astrophysics', 3], ['universe', 1], ['quantum', 2],
    ['engineering', 3], ['engineer', 2], ['electronics', 2], ['history', 2],
    ['historical', 2], ['archaeolog', 3], ['geography', 2], ['economics', 2],
    ['philosophy', 2], ['linguistic', 3], ['programming', 3], ['coding', 2],
    ['python', 2], ['javascript', 2], ['typescript', 2], ['kotlin', 2], ['rust$', 1],
    ['c\\+\\+', 2], ['algorithm', 3], ['data structure', 3], ['machine learning', 3],
    ['neural network', 3], ['deep learning', 3], ['computer science', 3],
    ['coding challenge', 3], ['documentary', 3], ['research', 2], ['experiment', 2],
    ['scientist', 2], ['professor', 2], ['university', 2], ['crash course', 3],
    ['introduction to', 2], ['intro to', 2], ['for beginners', 2], ['from scratch', 2],
    ['step by step', 1], ['deep dive', 2], ['masterclass', 2], ['ted-?ed', 3],
    ['ted talk', 2], ['tedx', 2],
  ];

  const CREATIVE_RULES = [
    ['рисован', 2], ['рисую', 2], ['рисуем', 2], ['рисунок', 2], ['скетч', 2],
    ['иллюстрац', 2], ['живопис', 2], ['акварел', 2], ['масло$', 1], ['графика', 1],
    ['цифровой арт', 2], ['3d модел', 2], ['моделирован', 2], ['blender', 2],
    ['анимаци', 2], ['мультфильм', 1], ['короткометраж', 2], ['музык', 1],
    ['теория музыки', 3], ['гармони', 1], ['кавер', 1], ['гитар', 2], ['фортепиан', 2],
    ['пианино', 2], ['барабан', 1], ['вокал', 2], ['композиц', 2], ['сочиняю', 2],
    ['аранжиров', 2], ['лепк', 2], ['скульпт', 2], ['гончар', 2], ['керамик', 2],
    ['вязан', 2], ['вышив', 2], ['шитье', 2], ['своими руками', 2], ['мастер-класс', 2],
    ['мастер класс', 2], ['рукодели', 2], ['столяр', 2], ['фотограф', 2],
    ['каллиграф', 2], ['леттеринг', 2], ['оригами', 2], ['дизайн', 1], ['архитектур', 2],
    ['писательств', 2], ['поэзи', 2], ['стихотворен', 2], ['рецепт', 1],
    ['speedpaint', 2], ['speed paint', 2], ['drawing', 2], ['sketch', 2],
    ['painting', 2], ['watercolor', 2], ['illustration', 2], ['digital art', 2],
    ['concept art', 2], ['3d model', 2], ['sculpt', 2], ['animation', 2],
    ['short film', 2], ['music theory', 3], ['guitar', 2], ['piano', 2], ['cover$', 1],
    ['songwriting', 2], ['composing', 2], ['pottery', 2], ['ceramic', 2],
    ['knitting', 2], ['sewing', 2], ['woodworking', 2], ['diy$', 2], ['handmade', 2],
    ['craft', 1], ['photography', 2], ['calligraphy', 2], ['origami', 2],
    ['architecture', 2], ['creative writing', 2], ['poetry', 2], ['recipe', 1],
  ];

  const DISTRACTING_RULES = [
    ['пранк', 4], ['prank', 4], ['челлендж', 3], ['challenge', 2], ['реакция на', 3],
    ['реагирую', 3], ['react to', 3], ['reacts to', 3], ['reacting to', 3],
    ['reaction$', 2], ['мем$', 3], ['мемы', 3], ['мемов', 3], ['мемас', 3],
    ['meme', 3], ['прикол', 3], ['приколы', 3], ['смешн', 2], ['ржак', 3], ['угар', 3],
    ['funny', 2], ['fail$', 2], ['fails$', 3], ['фейл', 2], ['тикток', 3], ['tiktok', 3],
    ['#shorts', 2], ['стрим', 2], ['stream$', 1], ['livestream', 1], ['летсплей', 3],
    ["let'?s play", 3], ['прохождени', 3], ['walkthrough', 2], ['gameplay', 3],
    ['геймплей', 3], ['fortnite', 3], ['фортнайт', 3], ['roblox', 3], ['роблокс', 3],
    ['brawl stars', 3], ['бравл', 3], ['standoff', 2], ['pubg', 3], ['cs2$', 2],
    ['cs:go', 2], ['dota', 2], ['дота', 2], ['майнкрафт', 1], ['minecraft', 1], ['gta', 2],
    ['шок$', 2], ['шокир', 2], ['shocking', 2], ['сплетн', 3], ['драма$', 2],
    ['drama$', 2], ['скандал', 2], ['хайп', 2], ['hype$', 1], ['мукбанг', 3],
    ['mukbang', 3], ['asmr', 2], ['асмр', 2], ['влог', 2], ['vlog', 2], ['распаковк', 2],
    ['unboxing', 2], ['haul$', 2], ['розыгрыш', 2], ['giveaway', 2], ['24 часа', 3],
    ['24 hours', 3], ['последний кто', 3], ['last to', 3], ['казино', 4], ['casino', 4],
    ['ставки', 3], ['betting', 3], ['нарезк', 2], ['compilation', 2], ['подборк', 1],
    ['лучшие моменты', 2], ['best moments', 2], ['топ-?\\d+', 1], ['top \\d+', 1],
    ['тренд', 1], ['trend', 1], ['не поверишь', 3], ['you won\'?t believe', 3],
    ['gone wrong', 3], ['пошло не так', 2], ['сериал', 1], ['трейлер', 1], ['trailer', 1],
  ];

  // Встроенный список заведомо полезных каналов (название или @handle).
  FT.BUILTIN_EDU_CHANNELS = [
    'kurzgesagt', 'kurzgesagt – in a nutshell', 'veritasium', '3blue1brown',
    'vsauce', 'smartereveryday', 'mark rober', 'numberphile', 'computerphile',
    'ted-ed', 'ted', 'tedx talks', 'khan academy', 'crashcourse',
    'mit opencourseware', 'stanford', 'harvard university', 'minutephysics',
    'minuteearth', 'scishow', 'sabine hossenfelder', 'pbs space time',
    'real engineering', 'practical engineering', 'primer', 'sebastian lague',
    'two minute papers', 'fireship', 'freecodecamp.org', 'freecodecamp',
    'the coding train', 'cs50', 'tom scott', 'wendover productions',
    'cgp grey', 'oversimplified', 'nilered', 'steve mould', 'stand-up maths',
    'mathologer', 'blender guru', 'proko', 'branch education', 'the action lab',
    'physics girl', 'scott manley', 'everyday astronaut', 'ben eater',
    'постнаука', 'arzamas', 'thoisoi', 'научпок', 'sci one', 'scione',
    'хауди хо', 'itproger', 'гоша дударь', 'selfedu', 'сергей немчинский',
    'борис трушин', 'антропогенез.ру', 'редакция.наука', 'редакция. наука',
    'физтех', 'лекторий фпми', 'мфти', 'простая наука',
  ];

  function normalize(text) {
    return String(text || '')
      .toLowerCase()
      .replace(/ё/g, 'е')
      .replace(/[ \s]+/g, ' ')
      .trim();
  }
  FT.normalizeText = normalize;

  function normalizeChannel(name) {
    return normalize(name).replace(/^@/, '');
  }
  FT.normalizeChannel = normalizeChannel;

  function escapeRegex(s) {
    return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }

  // Правило уже является регулярным выражением (в списках выше), а
  // пользовательские слова экранируются.
  function compileRule(pattern, weight, isUserText) {
    let src = normalize(pattern);
    let wholeWord = false;
    if (src.endsWith('$')) {
      wholeWord = true;
      src = src.slice(0, -1);
    }
    if (!src) return null;
    const body = isUserText ? escapeRegex(src) : src;
    const re = new RegExp(
      '(?:^|[^\\p{L}\\p{N}_])(' + body + ')' + (wholeWord ? '(?![\\p{L}\\p{N}])' : ''),
      'iu'
    );
    return { re, w: weight, label: src };
  }

  function compileList(list) {
    return list.map(([p, w]) => compileRule(p, w, false)).filter(Boolean);
  }

  const COMPILED = {
    edu: compileList(EDU_RULES),
    creative: compileList(CREATIVE_RULES),
    distracting: compileList(DISTRACTING_RULES),
  };

  const userRuleCache = new Map();
  function compileUserList(words, weight) {
    const key = weight + '|' + words.join('\u0001');
    if (userRuleCache.has(key)) return userRuleCache.get(key);
    const rules = words.map((w) => compileRule(String(w), weight, true)).filter(Boolean);
    if (userRuleCache.size > 50) userRuleCache.clear();
    userRuleCache.set(key, rules);
    return rules;
  }

  function matchRules(text, rules) {
    const hits = [];
    if (!text) return hits;
    for (const r of rules) {
      const m = r.re.exec(text);
      if (m) hits.push({ word: m[1], w: r.w });
    }
    return hits;
  }

  function sumCapped(hits, cap) {
    let s = 0;
    for (const h of hits) s += h.w;
    return Math.min(s, cap);
  }

  function countEmoji(text) {
    const m = String(text || '').match(/\p{Extended_Pictographic}/gu);
    return m ? m.length : 0;
  }

  function capsRatio(text) {
    const letters = String(text || '').match(/\p{L}/gu) || [];
    if (letters.length < 10) return 0;
    const upper = letters.filter((ch) => ch !== ch.toLowerCase()).length;
    return upper / letters.length;
  }

  function channelSet(list) {
    return new Set((list || []).map(normalizeChannel).filter(Boolean));
  }

  const builtinSet = channelSet(FT.BUILTIN_EDU_CHANNELS);

  function channelVariants(info) {
    return [info.channel, info.channelHandle, info.channelId]
      .map(normalizeChannel)
      .filter(Boolean);
  }

  FT.isChannelListed = function (info, list) {
    const set = channelSet(list);
    return channelVariants(info).some((v) => set.has(v));
  };

  /**
   * info: { title, channel, channelHandle, channelId, categoryId, category,
   *         tags: [], description, isShort, isMix }
   * Возвращает { allowed, score, threshold, reasons: [{text, delta}], decisive }
   */
  FT.classify = function (info, settingsIn) {
    const s = FT.mergeSettings(settingsIn);
    const reasons = [];
    const add = (delta, text) => {
      if (delta) reasons.push({ delta, text });
      return delta;
    };
    const threshold = FT.MODE_THRESHOLDS[s.mode];
    const variants = channelVariants(info);

    const finish = (allowed, score, decisive) => ({
      allowed,
      score,
      threshold,
      reasons,
      decisive: !!decisive,
    });

    if (variants.length && variants.some((v) => channelSet(s.whitelistChannels).has(v))) {
      add(100, 'канал в вашем белом списке');
      return finish(true, 100, true);
    }
    if (variants.length && variants.some((v) => channelSet(s.blacklistChannels).has(v))) {
      add(-100, 'канал в вашем чёрном списке');
      return finish(false, -100, true);
    }
    if (info.isShort && s.hideShorts) {
      add(-100, 'Shorts скрыты в настройках');
      return finish(false, -100, true);
    }
    if (info.isMix && s.hideMixes) {
      add(-100, 'миксы и автоплейлисты скрыты');
      return finish(false, -100, true);
    }

    let score = 0;
    if (variants.some((v) => builtinSet.has(v))) {
      score += add(5, 'известный образовательный канал');
    }

    let categoryId = info.categoryId != null ? Number(info.categoryId) : null;
    if (!categoryId && info.category) categoryId = FT.categoryIdByName(info.category);
    if (categoryId) {
      const cat = FT.CATEGORIES[categoryId];
      const catName = cat ? cat.ru : 'категория ' + categoryId;
      if (s.allowedCategories.includes(categoryId)) {
        score += add(4, 'категория «' + catName + '»');
      } else if (s.allowCreative && s.creativeCategories.includes(categoryId)) {
        score += add(3, 'творческая категория «' + catName + '»');
      } else if (cat && cat.group === 'distracting') {
        score += add(-2, 'развлекательная категория «' + catName + '»');
      }
    }

    const title = normalize(info.title);
    const extra = normalize(
      [Array.isArray(info.tags) ? info.tags.join(' , ') : '', info.description || ''].join(' \n ')
    );

    const describe = (hits) => hits.map((h) => '«' + h.word + '»').join(', ');

    const eduHits = matchRules(title, COMPILED.edu);
    if (eduHits.length) {
      score += add(sumCapped(eduHits, 6), 'полезные слова в названии: ' + describe(eduHits));
    }
    if (s.allowCreative) {
      const crHits = matchRules(title, COMPILED.creative);
      if (crHits.length) {
        score += add(sumCapped(crHits, 4), 'творчество в названии: ' + describe(crHits));
      }
    }
    const interestHits = matchRules(title, compileUserList(s.interests, 2));
    if (interestHits.length) {
      score += add(sumCapped(interestHits, 4), 'ваши интересы: ' + describe(interestHits));
    }
    const userAllow = matchRules(title, compileUserList(s.extraAllowKeywords, 3));
    if (userAllow.length) {
      score += add(sumCapped(userAllow, 6), 'ваши разрешённые слова: ' + describe(userAllow));
    }

    const badHits = matchRules(title, COMPILED.distracting);
    if (badHits.length) {
      score += add(-sumCapped(badHits, 12), 'развлекательные слова: ' + describe(badHits));
    }
    const userBlock = matchRules(title, compileUserList(s.extraBlockKeywords, 5));
    if (userBlock.length) {
      score += add(-sumCapped(userBlock, 20), 'ваши запрещённые слова: ' + describe(userBlock));
    }

    if (extra) {
      const eduExtra = matchRules(extra, COMPILED.edu);
      if (eduExtra.length) {
        score += add(Math.min(eduExtra.length, 3), 'полезные теги/описание: ' + describe(eduExtra.slice(0, 4)));
      }
      const badExtra = matchRules(extra, COMPILED.distracting);
      if (badExtra.length) {
        score += add(-Math.min(badExtra.length, 3), 'развлекательные теги/описание: ' + describe(badExtra.slice(0, 4)));
      }
    }

    const rawTitle = String(info.title || '');
    if (countEmoji(rawTitle) >= 3) score += add(-1, 'много эмодзи в названии');
    if (/[!?]{3,}/.test(rawTitle) || capsRatio(rawTitle) > 0.6) {
      score += add(-1, 'кликбейтное оформление названия');
    }

    return finish(score >= threshold, score, false);
  };

  // Для тестов и отладки.
  FT._rules = { EDU_RULES, CREATIVE_RULES, DISTRACTING_RULES };
})(typeof globalThis !== 'undefined' ? globalThis : this);
