// Запуск: node --test youtube-focus/extension/test
'use strict';
const test = require('node:test');
const assert = require('node:assert');
const path = require('node:path');

require(path.join(__dirname, '../src/defaults.js'));
require(path.join(__dirname, '../src/classifier.js'));
require(path.join(__dirname, '../src/metadata.js'));
const FT = globalThis.FT;

const allowed = (info, s) => FT.classify(info, s || {}).allowed;

test('образовательные названия проходят в сбалансированном режиме', () => {
  for (const title of [
    'Как работает трансформатор — простое объяснение',
    'Лекция 5. Теория вероятностей',
    'Python с нуля: урок 1',
    'Why is the sky blue? Physics explained',
    'Linear Algebra Lecture 3 — MIT',
    'Документальный фильм о Байкале',
    'Сопромат для студентов: изгиб балки',
  ]) {
    assert.ok(allowed({ title, channel: 'Какой-то канал' }), title);
  }
});

test('развлекательные названия скрываются', () => {
  for (const title of [
    'ПРАНК НАД ДРУГОМ 😂😂😂',
    'Реакция на новый клип',
    '24 часа в торговом центре челлендж',
    'Brawl Stars прохождение #12',
    'Funny cat fails compilation',
    'МЕМЫ ДНЯ',
    'Мой влог: распаковка покупок',
  ]) {
    assert.ok(!allowed({ title, channel: 'Канал' }), title);
  }
});

test('ролик без примет скрыт в сбалансированном и показан в мягком режиме', () => {
  const info = { title: 'Вечер у озера', channel: 'Иван' };
  assert.strictEqual(allowed(info, { mode: 'balanced' }), false);
  assert.strictEqual(allowed(info, { mode: 'soft' }), true);
});

test('творчество зависит от флага allowCreative', () => {
  const info = { title: 'Рисую портрет акварелью', channel: 'Art' };
  assert.strictEqual(allowed(info, { allowCreative: true }), true);
  assert.strictEqual(allowed(info, { allowCreative: false }), false);
});

test('белый и чёрный списки каналов главнее всего', () => {
  assert.ok(allowed({ title: 'ПРАНК', channel: 'Мой Канал' }, { whitelistChannels: ['мой канал'] }));
  assert.ok(allowed({ title: 'x', channelHandle: '@MyChannel' }, { whitelistChannels: ['@mychannel'] }));
  assert.ok(!allowed({ title: 'Лекция по физике', channel: 'Спам' }, { blacklistChannels: ['Спам'] }));
});

test('известные научно-популярные каналы проходят даже в строгом режиме', () => {
  assert.ok(allowed({ title: 'The Most Misunderstood Concept', channel: 'Veritasium' }, { mode: 'strict' }));
});

test('категория YouTube влияет на решение', () => {
  assert.ok(allowed({ title: 'Новое видео', channel: 'X', categoryId: 27 }));
  assert.ok(allowed({ title: 'Новое видео', channel: 'X', category: 'Наука и техника' }));
  assert.ok(!allowed({ title: 'Новое видео', channel: 'X', categoryId: 24 }, { mode: 'soft' }));
});

test('Shorts и миксы скрываются по настройке', () => {
  assert.ok(!allowed({ title: 'Лекция', isShort: true }));
  assert.ok(allowed({ title: 'Лекция по физике', isShort: true }, { hideShorts: false }));
  assert.ok(!allowed({ title: 'Микс', isMix: true }));
});

test('пользовательские слова и интересы', () => {
  assert.ok(allowed({ title: 'Локомотив ЭП20 изнутри' }, { extraAllowKeywords: ['локомотив'] }));
  assert.ok(!allowed({ title: 'Лекция про сериалы' }, { extraBlockKeywords: ['сериал'] }));
  assert.ok(allowed({ title: 'Железные дороги Сибири' }, { interests: ['железные дороги'], mode: 'balanced' }));
});

test('слова не срабатывают внутри других слов', () => {
  // «шок» не должен ловить «шоколад», «мем» — «мемориал».
  const r = FT.classify({ title: 'Как делают шоколад' }, {});
  assert.ok(!r.reasons.some((x) => x.text.includes('развлекательные')), JSON.stringify(r.reasons));
  const r2 = FT.classify({ title: 'Мемориал Победы — история' }, {});
  assert.ok(!r2.reasons.some((x) => x.text.includes('развлекательные')), JSON.stringify(r2.reasons));
});

test('разбор ytInitialPlayerResponse со страницы ролика', () => {
  const pr = {
    videoDetails: {
      title: 'Как устроен атом',
      author: 'Наука',
      channelId: 'UC123',
      keywords: ['физика', 'атом'],
      shortDescription: 'Лекция',
    },
    microformat: { playerMicroformatRenderer: { category: 'Education', ownerProfileUrl: 'http://www.youtube.com/@nauka' } },
  };
  const html = '<script>var ytInitialPlayerResponse = ' + JSON.stringify(pr) + ';var meta = 1;</script>';
  const info = FT.parseWatchHtml(html);
  assert.strictEqual(info.categoryId, 27);
  assert.strictEqual(info.channelHandle, '@nauka');
  assert.deepStrictEqual(info.tags, ['физика', 'атом']);

  // Мобильная версия иногда кладёт JSON строкой с \x-экранированием.
  const escaped = JSON.stringify(pr).replace(/[{}"]/g, (c) => '\\x' + c.charCodeAt(0).toString(16));
  const info2 = FT.parseWatchHtml("<script>var ytInitialPlayerResponse = '" + escaped + "';</script>");
  assert.strictEqual(info2.title, 'Как устроен атом');
  assert.strictEqual(info2.categoryId, 27);
});
