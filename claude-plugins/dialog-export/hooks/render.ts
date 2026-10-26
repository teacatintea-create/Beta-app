import type { Dialog, Part } from './dialog.ts'

export type Assets = { katexCss: string; viewerCss: string; scripts: string[] }

const escHtml = (s: string) => s.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c] ?? c)

// Inline <script> text must not close or comment out its element.
const inlineJs = (js: string) => js.replace(/<(\/script|!--|script)/gi, '\\x3C$1')

export function toHtml(d: Dialog, a: Assets): string {
  const json = JSON.stringify(d)
    .replace(/</g, '\\u003c')
    .replaceAll(String.fromCharCode(0x2028), '\\u2028')
    .replaceAll(String.fromCharCode(0x2029), '\\u2029')
  return `<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="generator" content="dialog-export">
<title>${escHtml(d.title)}</title>
<style>${a.katexCss}</style>
<style>${a.viewerCss}</style>
</head>
<body>
<div id="app"><noscript><p style="padding:16px">Для отображения диалога включите JavaScript. Исходные данные — в JSON внутри этого файла.</p></noscript></div>
<script type="application/json" id="dialog-data">${json}</script>
${a.scripts.map(s => `<script>${inlineJs(s)}</script>`).join('\n')}
</body>
</html>
`
}

// ---------- Markdown ----------

const LANG: Record<string, string> = {
  kt: 'kotlin', kts: 'kotlin', py: 'python', js: 'javascript', ts: 'typescript', tsx: 'tsx', jsx: 'jsx', rs: 'rust',
  rb: 'ruby', sh: 'bash', yml: 'yaml', md: 'markdown', htm: 'html', gradle: 'groovy', cs: 'csharp', h: 'c', hpp: 'cpp',
}
function langOf(path: unknown): string {
  const base = String(path ?? '').split(/[\\/]/).pop()?.toLowerCase() ?? ''
  const ext = base.includes('.') ? (base.split('.').pop() ?? '') : ''
  return LANG[ext] ?? ext
}

function fence(code: string, lang = ''): string {
  const longest = Math.max(0, ...(code.match(/`+/g) ?? []).map(r => r.length))
  const f = '`'.repeat(Math.max(3, longest + 1))
  return `${f}${lang}\n${code.replace(/\n$/, '')}\n${f}`
}

/** \( \) and \[ \] become $ and $$, which more Markdown viewers render; code is left alone. */
export function normalizeMath(md: string): string {
  return md
    .split(/(^(?:```|~~~)[^\n]*\n[\s\S]*?^(?:```|~~~)[ \t]*$|`[^`\n]+`)/m)
    .map((seg, i) =>
      i % 2 === 1
        ? seg
        : seg
            .replace(/\\\[([\s\S]+?)\\\]/g, (_, m: string) => `$$${m}$$`)
            .replace(/\\\(([\s\S]+?)\\\)/g, (_, m: string) => `$${m.trim()}$`),
    )
    .join('')
}

const details = (summary: string, body: string) => `<details>\n<summary>${escHtml(summary)}</summary>\n\n${body}\n\n</details>`

function stripLineNumbers(text: string): string | undefined {
  const lines = text.split('\n')
  const numbered = lines.filter(l => /^\s*\d+[\t→]/.test(l)).length
  if (numbered < Math.max(1, lines.length * 0.6)) return undefined
  return lines.map(l => l.replace(/^\s*\d+[\t→]/, '')).join('\n')
}

function toolMd(p: Extract<Part, { t: 'tool' }>): string {
  const inp = p.input
  const sum = String(inp.file_path ?? inp.path ?? inp.command ?? inp.pattern ?? inp.url ?? inp.query ?? inp.description ?? '').split('\n')[0]?.slice(0, 120) ?? ''
  const body: string[] = []
  if (p.name === 'Write' && typeof inp.content === 'string') body.push(fence(inp.content, langOf(inp.file_path)))
  else if (p.name === 'Edit' && 'old_string' in inp) {
    const diff = [...String(inp.old_string).split('\n').map(l => `- ${l}`), ...String(inp.new_string).split('\n').map(l => `+ ${l}`)]
    body.push(fence(diff.join('\n'), 'diff'))
  } else if (p.name === 'Bash' && typeof inp.command === 'string') body.push(fence(inp.command, 'bash'))
  else if (p.name === 'TodoWrite' && Array.isArray(inp.todos))
    body.push((inp.todos as { content?: string; status?: string }[]).map(t => `- [${t.status === 'completed' ? 'x' : ' '}] ${t.content ?? ''}`).join('\n'))
  else if (p.name !== 'Read' && Object.keys(inp).length) body.push(fence(JSON.stringify(inp, null, 2), 'json'))
  const r = p.result
  if (r?.text) {
    const plain = p.name === 'Read' && !r.isError ? stripLineNumbers(r.text) : undefined
    body.push(`**${r.isError ? 'Ошибка' : 'Результат'}:**`, plain !== undefined ? fence(plain, langOf(inp.file_path)) : fence(r.text))
  }
  for (const im of r?.images ?? []) body.push(`![${escHtml(String(inp.file_path ?? 'image'))}](data:${im.mime};base64,${im.data})`)
  return details(`🔧 ${p.name}${sum ? `: ${sum}` : ''}${r?.isError ? ' — ошибка' : ''}`, body.join('\n\n') || '_без вывода_')
}

function partMd(p: Part): string {
  switch (p.t) {
    case 'text': return normalizeMath(p.text)
    case 'image': return `![${p.name ?? 'изображение'}](${p.url ?? `data:${p.mime};base64,${p.data}`})`
    case 'file': {
      if (p.text !== undefined) return details(`📄 ${p.name}`, fence(p.text, p.lang ?? langOf(p.name)))
      if (p.mime.startsWith('image/') && p.data) return `![${p.name}](data:${p.mime};base64,${p.data})`
      return `📎 [${p.name}](data:${p.mime};base64,${p.data ?? ''}) (${p.mime})`
    }
    case 'tool': return toolMd(p)
    case 'thinking': return details('💭 Размышления', p.text)
    case 'command': return `\`/${p.name}${p.args ? ` ${p.args}` : ''}\`${p.output ? `\n\n${fence(p.output)}` : ''}`
    case 'note': return `> _${p.text}_`
  }
}

const WHO = { user: '👤 Вы', assistant: '🤖 Claude', system: '⚙️ Система' } as const

export function toMarkdown(d: Dialog): string {
  const meta = [
    d.startedAt && `- **Начат:** ${d.startedAt}`,
    `- **Экспорт:** ${d.exportedAt}`,
    d.models.length > 0 && `- **Модель:** ${d.models.join(', ')}`,
    `- **Сообщений:** ${d.messages.length}`,
    d.sessionId && `- **Сессия:** \`${d.sessionId}\``,
  ].filter(Boolean)
  const out = [`# ${d.title}`, meta.join('\n')]
  if (d.note) out.push(`> ${d.note}`)
  for (const m of d.messages) {
    out.push('---', `### ${WHO[m.role]}${m.time ? ` · ${m.time.replace('T', ' ').replace(/\.\d+Z$/, ' UTC')}` : ''}`)
    out.push(...m.parts.map(partMd))
  }
  return `${out.join('\n\n')}\n`
}
