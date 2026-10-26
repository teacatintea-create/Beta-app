/* Renders the exported dialog held in #dialog-data. Libraries (katex, marked,
   hljs) are inlined before this script; each is optional and degrades to text. */
(function () {
  'use strict'

  var data = JSON.parse(document.getElementById('dialog-data').textContent)
  var params = new URLSearchParams(location.search)
  var isPrint = params.has('print')
  var store = [] // raw code / file payloads the buttons copy or download

  function esc(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    })
  }

  // ---------- formulas ----------

  function tex(src, display) {
    if (!window.katex) return '<code class="tex">' + esc(display ? '$$' + src + '$$' : '$' + src + '$') + '</code>'
    try {
      return katex.renderToString(src, { displayMode: display, throwOnError: false, strict: 'ignore', output: 'htmlAndMathml' })
    } catch (e) {
      return '<code class="tex" title="' + esc(e.message) + '">' + esc(src) + '</code>'
    }
  }

  var mathBlock = {
    name: 'mathBlock',
    level: 'block',
    start: function (s) {
      var m = /(^|\n)[ \t]*(\$\$|\\\[|\\begin\{)/.exec(s)
      return m ? m.index + m[1].length : undefined
    },
    tokenizer: function (s) {
      var m =
        /^[ \t]*\$\$([\s\S]+?)\$\$[ \t]*(?:\n|$)/.exec(s) ||
        /^[ \t]*\\\[([\s\S]+?)\\\][ \t]*(?:\n|$)/.exec(s) ||
        /^[ \t]*(\\begin\{([a-zA-Z]+\*?)\}[\s\S]+?\\end\{\2\})[ \t]*(?:\n|$)/.exec(s)
      if (m) return { type: 'mathBlock', raw: m[0], text: m[1].trim() }
    },
    renderer: function (t) {
      return '<div class="math-block">' + tex(t.text, true) + '</div>\n'
    },
  }

  var mathInline = {
    name: 'mathInline',
    level: 'inline',
    start: function (s) {
      var i = s.search(/\$|\\\(|\\\[/)
      return i < 0 ? undefined : i
    },
    tokenizer: function (s) {
      var m
      if ((m = /^\$\$([\s\S]+?)\$\$/.exec(s))) return { type: 'mathInline', raw: m[0], text: m[1].trim(), display: true }
      if ((m = /^\\\[([\s\S]+?)\\\]/.exec(s))) return { type: 'mathInline', raw: m[0], text: m[1].trim(), display: true }
      if ((m = /^\\\(([\s\S]+?)\\\)/.exec(s))) return { type: 'mathInline', raw: m[0], text: m[1].trim(), display: false }
      if ((m = /^\$(?![\s$])((?:\\[\s\S]|[^\\$\n])+?)(?<![\s\\])\$(?![\d$])/.exec(s)))
        return { type: 'mathInline', raw: m[0], text: m[1], display: false }
    },
    renderer: function (t) {
      return t.display ? '<span class="math-block" style="display:block">' + tex(t.text, true) + '</span>' : tex(t.text, false)
    },
  }

  // ---------- code ----------

  var EXT = {
    kt: 'kotlin', kts: 'kotlin', py: 'python', js: 'javascript', mjs: 'javascript', cjs: 'javascript', jsx: 'javascript',
    ts: 'typescript', tsx: 'typescript', mts: 'typescript', rs: 'rust', rb: 'ruby', sh: 'bash', zsh: 'bash', bash: 'bash',
    yml: 'yaml', yaml: 'yaml', md: 'markdown', html: 'xml', htm: 'xml', xml: 'xml', svg: 'xml', gradle: 'groovy',
    h: 'c', hpp: 'cpp', cc: 'cpp', cs: 'csharp', m: 'objectivec', toml: 'ini', ini: 'ini', cfg: 'ini', txt: 'plaintext',
    json: 'json', jsonl: 'json', css: 'css', scss: 'scss', sql: 'sql', go: 'go', java: 'java', swift: 'swift', php: 'php',
    lua: 'lua', r: 'r', pl: 'perl', diff: 'diff', patch: 'diff', tex: 'latex', c: 'c', cpp: 'cpp', mk: 'makefile',
  }
  function langOf(path) {
    var base = String(path || '').split(/[\\/]/).pop().toLowerCase()
    if (base === 'makefile') return 'makefile'
    if (base === 'dockerfile') return 'dockerfile'
    var ext = base.indexOf('.') < 0 ? '' : base.split('.').pop()
    return EXT[ext] || ext
  }
  function highlight(code, lang) {
    if (window.hljs && lang && hljs.getLanguage(lang)) {
      try { return hljs.highlight(code, { language: lang, ignoreIllegals: true }).value } catch (e) {}
    }
    return esc(code)
  }
  function codeBlock(code, lang, name) {
    var i = store.push({ text: code, name: name }) - 1
    var label = name || lang || 'text'
    return (
      '<div class="code"><div class="code-head"><span class="name" title="' + esc(label) + '">' + esc(label) + '</span>' +
      '<button data-copy="' + i + '">Копировать</button>' +
      (name ? '<button data-dl="' + i + '">Скачать</button>' : '') +
      '</div><pre><code class="hljs">' + highlight(code, lang) + '</code></pre></div>'
    )
  }

  // ---------- markdown ----------

  function makeMd(breaks) {
    if (!window.marked) return null
    var md = new marked.Marked({ gfm: true, breaks: breaks })
    md.use({
      extensions: [mathBlock, mathInline],
      renderer: {
        html: function (html) { return esc(typeof html === 'string' ? html : html.text) },
        code: function (code, info) {
          if (typeof code === 'object') { info = code.lang; code = code.text }
          var lang = String(info || '').trim().split(/\s+/)[0].toLowerCase()
          return codeBlock(code.replace(/\n$/, ''), lang)
        },
        link: function (href, title, text) {
          if (typeof href === 'object') { title = href.title; text = this.parser.parseInline(href.tokens); href = href.href }
          var safe = /^(https?:|mailto:|#|\/|\.)/i.test(href || '') ? href : '#'
          return '<a href="' + esc(safe) + '"' + (title ? ' title="' + esc(title) + '"' : '') + ' target="_blank" rel="noopener">' + text + '</a>'
        },
      },
    })
    return md
  }
  var mdAssistant = makeMd(false)
  var mdUser = makeMd(true)

  function markdown(text, isUser) {
    var md = isUser ? mdUser : mdAssistant
    if (!md) return '<div class="md"><p style="white-space:pre-wrap">' + esc(text) + '</p></div>'
    try { return '<div class="md">' + md.parse(text) + '</div>' }
    catch (e) { return '<div class="md"><p style="white-space:pre-wrap">' + esc(text) + '</p></div>' }
  }

  // ---------- parts ----------

  function size(n) {
    if (n < 1024) return n + ' Б'
    if (n < 1048576) return (n / 1024).toFixed(1) + ' КБ'
    return (n / 1048576).toFixed(1) + ' МБ'
  }
  function b64size(b64) { return Math.floor((b64.length * 3) / 4) - (/==$/.test(b64) ? 2 : /=$/.test(b64) ? 1 : 0) }
  function image(mime, b64, name) {
    return '<figure class="img"><img loading="lazy" alt="' + esc(name || 'изображение') + '" src="data:' + esc(mime) + ';base64,' + b64 + '">' +
      (name ? '<figcaption>' + esc(name) + '</figcaption>' : '') + '</figure>'
  }
  function icon(mime, name) {
    if (/pdf/.test(mime)) return '📕'
    if (/^image\//.test(mime)) return '🖼️'
    if (/^text\/|json|xml|javascript/.test(mime) || langOf(name)) return '📄'
    return '📦'
  }

  function file(p) {
    var name = p.name || 'файл'
    var mime = p.mime || 'application/octet-stream'
    var bytes = p.data ? b64size(p.data) : new Blob([p.text || '']).size
    var i = store.push({ text: p.text, data: p.data, mime: mime, name: name }) - 1
    var head =
      '<div class="file-head"><span class="icon">' + icon(mime, name) + '</span><div class="info"><div class="fname" title="' + esc(name) + '">' + esc(name) +
      '</div><div class="fmeta">' + esc(mime) + ' · ' + size(bytes) + (p.text != null ? ' · ' + p.text.split('\n').length + ' строк' : '') + '</div></div>' +
      '<button data-dl="' + i + '">Скачать</button></div>'
    var body = ''
    if (p.text != null) {
      var lang = p.lang || langOf(name)
      body = lang === 'markdown' ? '<div class="file-md">' + markdown(p.text, false) + '</div>' : codeBlock(p.text, lang, name)
    } else if (/^image\//.test(mime) && p.data) body = '<div style="padding:8px 12px">' + image(mime, p.data) + '</div>'
    else if (/pdf/.test(mime) && p.data) body = '<iframe data-pdf="' + i + '" title="' + esc(name) + '"></iframe>'
    return '<div class="file">' + head + body + '</div>'
  }

  function stripLineNumbers(text) {
    var lines = text.split('\n')
    var numbered = lines.filter(function (l) { return /^\s*\d+[\t→]/.test(l) }).length
    if (numbered < Math.max(1, lines.length * 0.6)) return null
    return lines.map(function (l) { return l.replace(/^\s*\d+[\t→]/, '') }).join('\n')
  }

  function diff(oldS, newS) {
    var out = []
    String(oldS || '').split('\n').forEach(function (l) { out.push('- ' + l) })
    String(newS || '').split('\n').forEach(function (l) { out.push('+ ' + l) })
    return out.join('\n')
  }

  function str(v) { return typeof v === 'string' ? v : v == null ? '' : JSON.stringify(v, null, 2) }

  function toolSummary(name, inp) {
    var s = inp.file_path || inp.notebook_path || inp.path || inp.command || inp.pattern || inp.url || inp.query || inp.description || inp.skill || inp.prompt || ''
    return str(s).split('\n')[0].slice(0, 160)
  }

  function toolBody(p) {
    var inp = p.input && typeof p.input === 'object' ? p.input : {}
    var n = p.name
    var html = ''
    if ((n === 'Write' || n === 'create_file') && typeof inp.content === 'string') html += codeBlock(inp.content, langOf(inp.file_path), inp.file_path || 'файл')
    else if (n === 'Edit' && 'old_string' in inp) html += codeBlock(diff(inp.old_string, inp.new_string), 'diff', null)
    else if (n === 'MultiEdit' && Array.isArray(inp.edits)) html += inp.edits.map(function (e) { return codeBlock(diff(e.old_string, e.new_string), 'diff', null) }).join('')
    else if (n === 'Bash' && inp.command) html += codeBlock(inp.command, 'bash', null)
    else if (n === 'TodoWrite' && Array.isArray(inp.todos)) {
      html += '<ul class="todos">' + inp.todos.map(function (t) {
        var mark = t.status === 'completed' ? '☑' : t.status === 'in_progress' ? '◐' : '☐'
        return '<li class="' + esc(t.status) + '">' + mark + ' ' + esc(t.content || t.activeForm || '') + '</li>'
      }).join('') + '</ul>'
    } else if (n !== 'Read' && Object.keys(inp).length) html += codeBlock(JSON.stringify(inp, null, 2), 'json', null)

    var r = p.result
    if (r) {
      if (r.text) {
        var plain = n === 'Read' ? stripLineNumbers(r.text) : null
        html += '<div class="label">' + (r.isError ? 'Ошибка' : 'Результат') + '</div>'
        html += plain != null && !r.isError ? codeBlock(plain, langOf(inp.file_path), inp.file_path) : '<pre class="out' + (r.isError ? ' err' : '') + '">' + esc(r.text) + '</pre>'
      }
      ;(r.images || []).forEach(function (im) { html += image(im.mime, im.data, inp.file_path) })
    }
    return html || '<div class="label">Без вывода</div>'
  }

  function tool(p) {
    var r = p.result
    var state = !r ? '…' : r.isError ? 'ошибка' : '✓'
    return '<details class="tool' + (r && r.isError ? ' err' : '') + '"><summary><span class="tname">' + esc(p.name) + '</span>' +
      '<span class="tsum">' + esc(toolSummary(p.name, p.input || {})) + '</span><span class="tstate">' + state + '</span></summary>' +
      '<div class="tool-body">' + toolBody(p) + '</div></details>'
  }

  function part(p, isUser) {
    switch (p.t) {
      case 'text': return markdown(p.text, isUser)
      case 'image': return p.url ? '<figure class="img"><img loading="lazy" alt="" src="' + esc(p.url) + '"></figure>' : image(p.mime, p.data, p.name)
      case 'file': return file(p)
      case 'tool': return tool(p)
      case 'thinking': return '<details class="thinking"><summary>Размышления</summary><div class="thinking-body">' + markdown(p.text, false) + '</div></details>'
      case 'command':
        return '<div class="cmd"><code>/' + esc(p.name) + (p.args ? ' ' + esc(p.args) : '') + '</code></div>' + (p.output ? '<pre class="out">' + esc(p.output) + '</pre>' : '')
      case 'note': return '<div class="note">' + esc(p.text) + '</div>'
    }
    return ''
  }

  // ---------- page ----------

  var WHO = { user: 'Вы', assistant: 'Claude', system: 'Система' }
  function time(iso) {
    if (!iso) return ''
    var d = new Date(iso)
    return isNaN(d) ? '' : d.toLocaleString('ru-RU', { dateStyle: 'medium', timeStyle: 'short' })
  }

  document.title = data.title
  var root = document.getElementById('app')
  var toc = []
  var htmlParts = data.messages.map(function (m, i) {
    var id = 'm' + (i + 1)
    if (m.role === 'user') {
      var first = m.parts.filter(function (p) { return p.t === 'text' || p.t === 'command' })[0]
      var label = first ? (first.t === 'text' ? first.text : '/' + first.name + ' ' + (first.args || '')) : m.parts.length ? '[вложение]' : ''
      toc.push('<a href="#' + id + '" title="' + esc(label.slice(0, 300)) + '">' + esc(label.replace(/\s+/g, ' ').slice(0, 80)) + '</a>')
    }
    return '<article class="msg ' + m.role + '" id="' + id + '"><header><span class="who">' + WHO[m.role] + '</span>' +
      (m.time ? '<time datetime="' + esc(m.time) + '">' + esc(time(m.time)) + '</time>' : '') + '</header><div class="body">' +
      m.parts.map(function (p) { return part(p, m.role === 'user') }).join('') + '</div></article>'
  })

  var meta = [
    data.exportedAt && 'Экспорт: ' + time(data.exportedAt),
    data.startedAt && 'Начат: ' + time(data.startedAt),
    data.models && data.models.length && 'Модель: ' + data.models.join(', '),
    'Сообщений: ' + data.messages.length,
    data.cwd && 'Папка: ' + data.cwd,
    data.sessionId && 'Сессия: ' + data.sessionId,
  ].filter(Boolean)

  root.innerHTML =
    '<div class="topbar"><h1 title="' + esc(data.title) + '">' + esc(data.title) + '</h1><div class="actions">' +
    '<button data-act="expand">Развернуть всё</button><button data-act="collapse">Свернуть всё</button>' +
    '<button data-act="theme">Тема</button><button data-act="print">Печать / PDF</button></div></div>' +
    '<div class="layout">' + (toc.length > 1 ? '<nav class="toc"><div class="toc-title">Содержание</div>' + toc.join('') + '</nav>' : '') +
    '<main><div class="meta">' + meta.map(function (s) { return '<span>' + esc(s) + '</span>' }).join('') + '</div>' +
    (data.note ? '<div class="note" style="margin-bottom:24px">' + esc(data.note) + '</div>' : '') +
    htmlParts.join('') + '</main></div>'

  // ---------- behaviour ----------

  function bytesOf(b64) {
    var bin = atob(b64)
    var a = new Uint8Array(bin.length)
    for (var i = 0; i < bin.length; i++) a[i] = bin.charCodeAt(i)
    return a
  }
  function blobOf(item) {
    return item.data != null ? new Blob([bytesOf(item.data)], { type: item.mime || 'application/octet-stream' }) : new Blob([item.text || ''], { type: 'text/plain;charset=utf-8' })
  }

  document.querySelectorAll('iframe[data-pdf]').forEach(function (f) {
    try { f.src = URL.createObjectURL(blobOf(store[+f.getAttribute('data-pdf')])) } catch (e) {}
  })

  function setAll(open) { document.querySelectorAll('details').forEach(function (d) { d.open = open }) }

  document.addEventListener('click', function (ev) {
    var t = ev.target
    if (!(t instanceof Element)) return
    var b = t.closest('button')
    if (b) {
      if (b.hasAttribute('data-copy')) {
        var text = store[+b.getAttribute('data-copy')].text
        var done = function () { b.textContent = 'Скопировано'; setTimeout(function () { b.textContent = 'Копировать' }, 1200) }
        if (navigator.clipboard) navigator.clipboard.writeText(text).then(done, function () {})
        return
      }
      if (b.hasAttribute('data-dl')) {
        var item = store[+b.getAttribute('data-dl')]
        var a = document.createElement('a')
        a.href = URL.createObjectURL(blobOf(item))
        a.download = String(item.name || 'file').split(/[\\/]/).pop()
        document.body.appendChild(a); a.click(); a.remove()
        return
      }
      var act = b.getAttribute('data-act')
      if (act === 'expand') setAll(true)
      else if (act === 'collapse') setAll(false)
      else if (act === 'print') window.print()
      else if (act === 'theme') {
        var cur = document.documentElement.getAttribute('data-theme') || (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
        var next = cur === 'dark' ? 'light' : 'dark'
        document.documentElement.setAttribute('data-theme', next)
        try { localStorage.setItem('dialog-export-theme', next) } catch (e) {}
      }
      return
    }
    if (t.tagName === 'IMG' && t.closest('figure.img')) {
      var box = document.createElement('div')
      box.className = 'lightbox'
      box.innerHTML = '<img src="' + t.getAttribute('src') + '" alt="">'
      box.addEventListener('click', function () { box.remove() })
      document.body.appendChild(box)
    }
  })
  document.addEventListener('keydown', function (ev) {
    if (ev.key === 'Escape') document.querySelectorAll('.lightbox').forEach(function (b) { b.remove() })
  })

  try {
    var saved = localStorage.getItem('dialog-export-theme')
    if (saved && !isPrint) document.documentElement.setAttribute('data-theme', saved)
  } catch (e) {}

  var themeBeforePrint = null
  var openBeforePrint = []
  window.addEventListener('beforeprint', function () {
    themeBeforePrint = document.documentElement.getAttribute('data-theme')
    document.documentElement.setAttribute('data-theme', 'light')
    openBeforePrint = Array.prototype.map.call(document.querySelectorAll('details'), function (d) { return d.open })
    setAll(true)
  })
  window.addEventListener('afterprint', function () {
    if (themeBeforePrint) document.documentElement.setAttribute('data-theme', themeBeforePrint)
    else document.documentElement.removeAttribute('data-theme')
    document.querySelectorAll('details').forEach(function (d, i) { d.open = !!openBeforePrint[i] })
  })
  if (isPrint) {
    document.documentElement.setAttribute('data-theme', 'light')
    setAll(true)
  }
})()
