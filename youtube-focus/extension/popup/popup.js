'use strict';

const $ = (id) => document.getElementById(id);
let settings = FT.mergeSettings({});
let countdownTimer = null;

function load() {
  chrome.storage.sync.get(null, (items) => {
    settings = FT.mergeSettings(items);
    render();
  });
  chrome.storage.local.get('stats', ({ stats }) => renderStats(FT.normalizeStats(stats)));
}

function save(partial) {
  chrome.storage.sync.set(partial, load);
}

function render() {
  const active = FT.isActive(settings);
  const paused = settings.enabled && !active;
  const status = $('status');
  status.textContent = !settings.enabled ? 'выключен' : paused ? 'на паузе' : 'включён';
  status.className = 'pill ' + (active ? 'on' : 'off');
  $('mode').value = settings.mode;
  $('mode-hint').textContent = FT.MODE_LABELS[settings.mode];
  $('allowCreative').checked = settings.allowCreative;

  const toggle = $('toggle');
  toggle.textContent = settings.enabled ? 'Выключить' : 'Включить';
  toggle.className = settings.enabled ? '' : 'primary';
  const pause = $('pause');
  if (paused) {
    const min = Math.ceil((settings.pausedUntil - Date.now()) / 60000);
    pause.textContent = 'Снять паузу (' + min + ' мин)';
    pause.className = 'primary';
  } else {
    pause.textContent = 'Пауза на 15 минут';
    pause.className = '';
    pause.disabled = !settings.enabled;
  }
}

function renderStats(stats) {
  $('watched').textContent = FT.formatMinutes(stats.watchSeconds);
  $('hidden').textContent = stats.hidden;
  $('blocked').textContent = stats.blocked;
  const lim = settings.dailyLimitMin;
  $('limit').textContent = lim ? 'из ' + lim + ' мин лимита' : 'на YouTube сегодня';
  const pct = lim ? Math.min(100, (stats.watchSeconds / 60 / lim) * 100) : 0;
  $('bar-fill').style.width = pct + '%';
  $('bar-fill').style.background = pct >= 100 ? 'var(--off)' : '';
}

// Ослабление защиты (выключить / пауза / мягче режим) — только после ожидания.
function withDelay(action, label) {
  const sec = settings.lockDelaySec;
  if (!sec) return action();
  if (countdownTimer) return;
  let left = sec;
  const buttons = document.querySelectorAll('button, select, input');
  buttons.forEach((b) => (b.disabled = true));
  const tick = () => {
    $('countdown').textContent = label + ' через ' + left + ' с. Точно надо? Закрой окно, чтобы передумать.';
    if (left-- <= 0) {
      clearInterval(countdownTimer);
      countdownTimer = null;
      $('countdown').textContent = '';
      buttons.forEach((b) => (b.disabled = false));
      action();
    }
  };
  tick();
  countdownTimer = setInterval(tick, 1000);
}

const STRICTNESS = { strict: 2, balanced: 1, soft: 0 };

$('mode').addEventListener('change', (e) => {
  const mode = e.target.value;
  if (STRICTNESS[mode] < STRICTNESS[settings.mode]) {
    $('mode').value = settings.mode;
    withDelay(() => save({ mode }), 'Режим станет мягче');
  } else save({ mode });
});

$('allowCreative').addEventListener('change', (e) => save({ allowCreative: e.target.checked }));

$('toggle').addEventListener('click', () => {
  if (settings.enabled) withDelay(() => save({ enabled: false }), 'Фильтр выключится');
  else save({ enabled: true, pausedUntil: 0 });
});

$('pause').addEventListener('click', () => {
  if (settings.enabled && !FT.isActive(settings)) save({ pausedUntil: 0 });
  else withDelay(() => save({ pausedUntil: Date.now() + 15 * 60 * 1000 }), 'Пауза начнётся');
});

$('options').addEventListener('click', (e) => {
  e.preventDefault();
  chrome.runtime.openOptionsPage();
});

load();
