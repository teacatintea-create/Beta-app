'use strict';

const $ = (id) => document.getElementById(id);
const CHECKS = ['allowCreative', 'blockWatchPage', 'hideHomeFeed', 'hideShorts', 'hideMixes', 'hideEndScreen', 'hideComments'];
const LISTS = ['interests', 'whitelistChannels', 'blacklistChannels', 'extraAllowKeywords', 'extraBlockKeywords'];
const API_ORIGIN = 'https://www.googleapis.com/youtube/v3/*';

let settings = FT.mergeSettings({});

function renderCategories() {
  const box = $('categories');
  box.innerHTML = '';
  for (const id of Object.keys(FT.CATEGORIES).map(Number)) {
    const c = FT.CATEGORIES[id];
    const creative = settings.creativeCategories.includes(id);
    const label = document.createElement('label');
    label.className = 'check';
    label.innerHTML = `<input type="checkbox" data-cat="${id}"> <span>${c.ru}${creative ? ' <span class="tag">(творчество)</span>' : ''}</span>`;
    const input = label.querySelector('input');
    input.checked = settings.allowedCategories.includes(id) || (creative && settings.allowCreative);
    input.disabled = creative;
    if (creative) label.title = 'Включается флажком «Творчество тоже считается полезным»';
    box.appendChild(label);
  }
}

function render() {
  $('mode').value = settings.mode;
  for (const k of CHECKS) $(k).checked = !!settings[k];
  for (const k of LISTS) $(k).value = settings[k].join('\n');
  $('dailyLimitMin').value = settings.dailyLimitMin;
  $('lockDelaySec').value = settings.lockDelaySec;
  $('apiKey').value = settings.apiKey;
  renderCategories();
  runTest();
}

function collect() {
  const out = { mode: $('mode').value };
  for (const k of CHECKS) out[k] = $(k).checked;
  for (const k of LISTS) {
    out[k] = $(k).value.split('\n').map((s) => s.trim()).filter(Boolean);
  }
  out.dailyLimitMin = Math.max(0, parseInt($('dailyLimitMin').value, 10) || 0);
  out.lockDelaySec = Math.max(0, parseInt($('lockDelaySec').value, 10) || 0);
  out.apiKey = $('apiKey').value.trim();
  out.allowedCategories = Array.from(document.querySelectorAll('[data-cat]'))
    .filter((i) => i.checked && !i.disabled)
    .map((i) => Number(i.dataset.cat));
  return out;
}

function requestApiPermission() {
  // В Firefox доступ к сайтам из host_permissions нужно разрешить явно.
  return new Promise((resolve) => {
    if (!chrome.permissions || !chrome.permissions.request) return resolve(true);
    chrome.permissions.contains({ origins: [API_ORIGIN] }, (has) => {
      if (has) return resolve(true);
      chrome.permissions.request({ origins: [API_ORIGIN] }, (granted) => resolve(!!granted));
    });
  });
}

async function save() {
  const next = collect();
  if (next.apiKey) await requestApiPermission();
  chrome.storage.sync.set(next, () => {
    settings = FT.mergeSettings(Object.assign({}, settings, next));
    $('saved').textContent = 'Сохранено ✓';
    setTimeout(() => ($('saved').textContent = ''), 2000);
    renderCategories();
    runTest();
  });
}

function runTest() {
  const title = $('testTitle').value.trim();
  const channel = $('testChannel').value.trim();
  const box = $('testResult');
  if (!title && !channel) {
    box.innerHTML = '';
    return;
  }
  const res = FT.classify({ title, channel }, settings);
  const items = res.reasons
    .map((r) => `<li class="${r.delta > 0 ? 'ok' : 'bad'}">${escapeHtml(r.text)} (${r.delta > 0 ? '+' : ''}${r.delta})</li>`)
    .join('');
  box.innerHTML = `<div class="verdict ${res.allowed ? 'ok' : 'bad'}">${res.allowed ? 'Покажется ✓' : 'Будет скрыт ✕'}
    — ${res.score} балл(ов), порог режима ${res.threshold}</div>
    ${items ? `<ul>${items}</ul>` : '<p class="muted small">Ни одной приметы не найдено.</p>'}`;
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

$('save').addEventListener('click', save);
$('reset').addEventListener('click', () => {
  if (!confirm('Вернуть все настройки к стандартным? Списки каналов и слов очистятся.')) return;
  const d = FT.mergeSettings({});
  chrome.storage.sync.set(d, () => {
    settings = d;
    render();
  });
});
$('allowCreative').addEventListener('change', () => {
  settings.allowCreative = $('allowCreative').checked;
  renderCategories();
});
$('testTitle').addEventListener('input', runTest);
$('testChannel').addEventListener('input', runTest);

chrome.storage.sync.get(null, (items) => {
  settings = FT.mergeSettings(items);
  render();
});
