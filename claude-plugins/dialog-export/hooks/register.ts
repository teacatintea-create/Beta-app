import type { EngineInterface, Register } from 'claude-code'

import { firstPrompt, fromApi, fromTranscript } from './dialog.ts'
import type { Dialog, ReadOptions } from './dialog.ts'
import { toHtml, toMarkdown } from './render.ts'
import type { Assets } from './render.ts'

const COMMAND = 'export-dialog'
const FORMATS = ['html', 'md', 'json', 'pdf'] as const
type Format = (typeof FORMATS)[number]
const MAX_READ = 4 * 1024 * 1024 - 1024

type Options = ReadOptions & {
  formats: Format[]
  out?: string
  session?: string
  all: boolean
  list: boolean
  /** true: into the Obsidian vault (an error when none is found); false: never; undefined: when one is found. */
  obsidian?: boolean
  vault?: string
  folder?: string
}

const HELP = `/${COMMAND} [html|md|json|pdf|all] [--out <папка или файл>] [--no-tools] [--thinking]
/${COMMAND} --list                 — сессии этого проекта
/${COMMAND} --session <id> [...]   — экспорт другой сессии (можно начало id)
/${COMMAND} --all [...]            — экспорт всех сессий проекта
/${COMMAND} --vault <путь> [--folder <папка>] — запомнить хранилище Obsidian
/${COMMAND} obsidian | --here      — только в Obsidian / только в ./claude-exports/
Если хранилище Obsidian найдено, Markdown-заметка сразу пишется в него
(папка Claude); иначе — html в ./claude-exports/`

function parseArgs(args: string): Options | { error: string } {
  const words = args.match(/"[^"]*"|'[^']*'|\S+/g)?.map(w => w.replace(/^(["'])(.*)\1$/, '$2')) ?? []
  const o: Options = { formats: [], tools: true, thinking: false, all: false, list: false, skipCommand: COMMAND }
  for (let i = 0; i < words.length; i++) {
    const w = words[i] ?? ''
    const lw = w.toLowerCase()
    if (lw === 'all' || lw === 'все') o.formats = [...FORMATS]
    else if (lw === 'markdown') o.formats.push('md')
    else if ((FORMATS as readonly string[]).includes(lw)) o.formats.push(lw as Format)
    else if (w === '--no-tools') o.tools = false
    else if (w === '--thinking') o.thinking = true
    else if (w === '--all') o.all = true
    else if (w === '--list') o.list = true
    else if (w === '--session' || w === '-s') o.session = words[++i]
    else if (w === '--out' || w === '-o') o.out = words[++i]
    else if (lw === 'obsidian' || lw === 'обсидиан' || w === '--obsidian') o.obsidian = true
    else if (w === '--here' || w === '--no-obsidian') o.obsidian = false
    else if (w === '--vault') o.vault = words[++i]
    else if (w === '--folder') o.folder = words[++i]
    else if (w === '--help' || w === '-h' || w === 'help') return { error: HELP }
    else if (!w.startsWith('-') && /[\\/.]/.test(w)) o.out = w
    else return { error: `Не понял «${w}».\n\n${HELP}` }
  }
  if (o.out && o.obsidian === undefined) o.obsidian = false
  return o
}

// ---------- paths ----------

const sepOf = (p: string) => (p.includes('\\') && !p.includes('/') ? '\\' : '/')
const join = (a: string, b: string) => (/[\\/]$/.test(a) ? a + b : a + sepOf(a) + b)
const isAbs = (p: string) => /^([\\/]|[A-Za-z]:[\\/])/.test(p)

async function configDir($: EngineInterface): Promise<string | undefined> {
  const custom = await $.env.get('CLAUDE_CONFIG_DIR')
  if (custom) return custom
  const home = (await $.env.get('HOME')) ?? (await $.env.get('USERPROFILE'))
  return home ? join(home, '.claude') : undefined
}

/** The folder of this project's transcripts, and the current session's file in it. */
async function locate($: EngineInterface): Promise<{ dir?: string; file?: string; id: string }> {
  const id = await $.session.id()
  const config = await configDir($)
  if (!config) return { id }
  const projects = join(config, 'projects')
  const root = await $.session.root()
  const guess = join(projects, root.replace(/[^a-zA-Z0-9]/g, '-'))
  if (await $.fs.exists(join(guess, `${id}.jsonl`))) return { dir: guess, file: join(guess, `${id}.jsonl`), id }
  const dirs = await $.fs.list(projects).catch(() => [])
  for (const d of dirs) {
    if (d.kind !== 'dir') continue
    const file = join(join(projects, d.name), `${id}.jsonl`)
    if (await $.fs.exists(file)) return { dir: join(projects, d.name), file, id }
  }
  return { dir: (await $.fs.exists(guess)) ? guess : undefined, id }
}

// ---------- Obsidian ----------

type Vault = { path: string; folder: string; from: 'аргумент' | 'настройки' | 'Obsidian' }

const str = (v: unknown) => (typeof v === 'string' && v.trim() ? v.trim() : undefined)

async function home($: EngineInterface) {
  return (await $.env.get('HOME')) ?? (await $.env.get('USERPROFILE'))
}

async function expand($: EngineInterface, p: string) {
  const h = await home($)
  return h ? p.replace(/^~(?=[\\/]|$)/, h) : p
}

async function readJson($: EngineInterface, path: string): Promise<Record<string, unknown> | undefined> {
  try {
    const v: unknown = JSON.parse(await $.fs.read(path))
    return v && typeof v === 'object' && !Array.isArray(v) ? (v as Record<string, unknown>) : undefined
  } catch {
    return undefined
  }
}

const isDir = ($: EngineInterface, p: string) => $.fs.stat(p).then(s => s.kind === 'dir', () => false)

/** Shared with the obsidian-export plugin, so one setting serves both. */
async function sharedConfig($: EngineInterface) {
  const config = await configDir($)
  return config ? join(join(config, 'obsidian-export'), 'config.json') : undefined
}

/** Vaults the Obsidian app knows, the open one first, then the most recently used. */
async function appVaults($: EngineInterface): Promise<string[]> {
  const h = await home($)
  const appData = await $.env.get('APPDATA')
  const xdg = (await $.env.get('XDG_CONFIG_HOME')) ?? (h ? join(h, '.config') : undefined)
  const files = [
    appData && join(join(appData, 'obsidian'), 'obsidian.json'),
    h && join(h, 'Library/Application Support/obsidian/obsidian.json'),
    xdg && join(xdg, 'obsidian/obsidian.json'),
    h && join(h, '.var/app/md.obsidian.Obsidian/config/obsidian/obsidian.json'),
    h && join(h, 'snap/obsidian/current/.config/obsidian/obsidian.json'),
  ].filter((f): f is string => Boolean(f))
  const found: { path: string; open: boolean; ts: number }[] = []
  for (const f of files) {
    const vaults = (await readJson($, f))?.vaults
    if (!vaults || typeof vaults !== 'object') continue
    for (const v of Object.values(vaults as Record<string, { path?: unknown; open?: unknown; ts?: unknown }>)) {
      const path = str(v?.path)
      if (path) found.push({ path, open: v.open === true, ts: typeof v.ts === 'number' ? v.ts : 0 })
    }
  }
  return found.sort((a, b) => Number(b.open) - Number(a.open) || b.ts - a.ts).map(v => v.path)
}

/** The vault to export into, or why there is none. `--vault` is remembered in the shared settings. */
async function findVault($: EngineInterface, o: Options): Promise<Vault | { error: string }> {
  const file = await sharedConfig($)
  const shared = (file && (await readJson($, file))) || {}
  const folder = (o.folder ?? str(await $.env.get('OBSIDIAN_EXPORT_FOLDER')) ?? str(shared.folder) ?? 'Claude').replace(/^[\\/]+|[\\/]+$/g, '')
  if (o.vault) {
    const path = await expand($, o.vault)
    if (!(await isDir($, path))) return { error: `Папки хранилища нет: ${path}` }
    if (file) await $.fs.write(file, `${JSON.stringify({ ...shared, vault_path: path, folder }, null, 2)}\n`)
    return { path, folder, from: 'аргумент' }
  }
  const set = str(await $.env.get('OBSIDIAN_VAULT_PATH')) ?? str(shared.vault_path)
  if (set) {
    const path = await expand($, set)
    return (await isDir($, path)) ? { path, folder, from: 'настройки' } : { error: `Папки хранилища из настроек нет: ${path}` }
  }
  for (const path of await appVaults($)) if (await isDir($, path)) return { path, folder, from: 'Obsidian' }
  return { error: `Хранилище Obsidian не найдено. Укажите его: /${COMMAND} --vault <путь к хранилищу>` }
}

const pad = (n: number) => String(n).padStart(2, '0')
const fileSafe = (s: string) => s.replace(/[\\/:*?"<>|#^[\]\x00-\x1f]+/g, ' ').replace(/\s+/g, ' ').trim().replace(/[.… ]+$/, '').slice(0, 90).trim()

/** The note's path without extension: the one this session was exported to before, else "<date> <title>". */
async function vaultStem($: EngineInterface, v: Vault, d: Dialog): Promise<string> {
  const key = d.sessionId ? `vault-note:${d.sessionId}` : undefined
  const saved = key ? await $.store.get(key) : undefined
  // Kept where it was, even if moved within the vault; a note in another vault is not reused.
  if (typeof saved === 'string' && saved.startsWith(v.path) && (await $.fs.exists(`${saved}.md`))) return saved
  const t = new Date(d.startedAt ?? d.exportedAt)
  const dir = v.folder ? join(v.path, v.folder) : v.path
  const name = `${t.getFullYear()}-${pad(t.getMonth() + 1)}-${pad(t.getDate())} ${fileSafe(d.title) || 'Диалог'}`
  let stem = join(dir, name)
  if (await $.fs.exists(`${stem}.md`)) {
    const mine = d.sessionId && (await $.fs.read(`${stem}.md`).then(t => t.includes(`session_id: ${JSON.stringify(d.sessionId)}`), () => false))
    if (!mine) stem = join(dir, `${name} (${d.sessionId?.slice(0, 8) ?? Date.now()})`)
  }
  if (key) await $.store.set(key, stem)
  return stem
}

function obsidianUri(v: Vault, file: string): string {
  const rel = file.slice(v.path.length).replace(/^[\\/]+/, '').replace(/\\/g, '/').replace(/\.md$/, '')
  const vaultName = v.path.split(/[\\/]/).filter(Boolean).pop() ?? ''
  return `obsidian://open?vault=${encodeURIComponent(vaultName)}&file=${encodeURIComponent(rel)}`
}

const B64 = new Uint8Array(128)
for (let i = 0; i < 64; i++) B64['ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'.charCodeAt(i)] = i

function fromBase64(s: string): Uint8Array {
  const clean = s.replace(/[^A-Za-z0-9+/]/g, '')
  const out = new Uint8Array(Math.floor((clean.length * 3) / 4))
  let bits = 0
  let acc = 0
  let at = 0
  for (let i = 0; i < clean.length; i++) {
    acc = (acc << 6) | (B64[clean.charCodeAt(i)] ?? 0)
    bits += 6
    if (bits >= 8) {
      bits -= 8
      out[at++] = (acc >> bits) & 0xff
    }
  }
  return out.subarray(0, at)
}

/** Reads a text file of any size: past the 4 MiB a read carries, in pieces `split` cuts. */
async function readBig($: EngineInterface, path: string): Promise<string> {
  const { size } = await $.fs.stat(path)
  if (size <= MAX_READ) return $.fs.read(path)
  const tmp = (await $.process.run(['mktemp', '-d'])).stdout.trim()
  if (!tmp) throw new Error('mktemp failed')
  try {
    const split = await $.process.run(['split', '-b', '3000000', '-a', '4', path, join(tmp, 'p')], { timeoutMs: 120_000 })
    if (split.exitCode !== 0) throw new Error(split.stderr || 'split failed')
    const pieces = (await $.fs.list(tmp)).map(e => e.name).sort()
    const chunks = await Promise.all(pieces.map(async n => fromBase64((await $.fs.read(join(tmp, n), { as: 'bytes' })).base64)))
    const all = new Uint8Array(chunks.reduce((n, c) => n + c.length, 0))
    let at = 0
    for (const c of chunks) { all.set(c, at); at += c.length }
    return new TextDecoder().decode(all)
  } finally {
    await $.process.run(['rm', '-rf', tmp]).catch(() => undefined)
  }
}

// ---------- reading a dialog ----------

async function currentDialog($: EngineInterface, o: Options, file?: string): Promise<Dialog> {
  const exportedAt = new Date().toISOString()
  if (file) {
    try {
      const d = fromTranscript(await readBig($, file), o)
      if (d.messages.length > 0) return { ...d, exportedAt }
    } catch {
      // fall through to the context the engine holds
    }
  }
  const api = await $.session.messages({ as: 'api' })
  const d = fromApi(api, o)
  return {
    ...d,
    exportedAt,
    title: firstPrompt(d.messages) ?? 'Диалог с Claude',
    sessionId: await $.session.id(),
    cwd: await $.session.cwd(),
    models: [await $.session.model()],
    note: 'Файл истории не прочитан — экспортирован текущий контекст: если диалог сжимался, ранняя часть заменена сводкой.',
  }
}

async function sessionFiles($: EngineInterface, dir: string) {
  const entries = (await $.fs.list(dir)).filter(e => e.kind === 'file' && e.name.endsWith('.jsonl'))
  return entries.sort((a, b) => b.mtimeMs - a.mtimeMs)
}

// ---------- writing ----------

let assetsCache: Assets | undefined
async function assets($: EngineInterface): Promise<Assets> {
  if (assetsCache) return assetsCache
  const read = (name: string) => $.fs.read(`${$.plugin.root}/assets/${name}`)
  const [katexCss, viewerCss, ...scripts] = await Promise.all(
    ['katex.css', 'viewer.css', 'katex.js', 'mhchem.js', 'marked.js', 'highlight.js', 'viewer.js'].map(read),
  )
  assetsCache = { katexCss: katexCss ?? '', viewerCss: viewerCss ?? '', scripts }
  return assetsCache
}

function baseName(d: Dialog): string {
  const t = new Date(d.startedAt ?? d.exportedAt)
  const date = `${t.getFullYear()}-${pad(t.getMonth() + 1)}-${pad(t.getDate())}_${pad(t.getHours())}-${pad(t.getMinutes())}`
  const slug = d.title.replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-+|-+$/g, '').slice(0, 48).replace(/-+$/, '') || 'dialog'
  return `${date}_${slug}${d.sessionId ? `_${d.sessionId.slice(0, 8)}` : ''}`
}

const BROWSERS = [
  'google-chrome', 'google-chrome-stable', 'chromium', 'chromium-browser', 'microsoft-edge', 'msedge', 'brave-browser',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/Applications/Chromium.app/Contents/MacOS/Chromium',
  '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  '/opt/pw-browsers/chromium-1194/chrome-linux/chrome',
]

async function findBrowser($: EngineInterface): Promise<string | undefined> {
  const saved = await $.store.get('browser')
  for (const b of typeof saved === 'string' ? [saved, ...BROWSERS] : BROWSERS) {
    const ok = await $.process.run([b, '--version'], { timeoutMs: 10_000 }).then(r => r.exitCode === 0, () => false)
    if (ok) {
      await $.store.set('browser', b)
      return b
    }
  }
  return undefined
}

async function printPdf($: EngineInterface, html: string, pdf: string): Promise<string | undefined> {
  const browser = await findBrowser($)
  if (!browser) return 'нет Chrome/Chromium/Edge — откройте HTML и нажмите «Печать / PDF»'
  const url = `file://${html.startsWith('/') ? '' : '/'}${encodeURI(html.replace(/\\/g, '/'))}?print=1`
  const r = await $.process.run(
    [browser, '--headless', '--disable-gpu', '--no-sandbox', '--no-pdf-header-footer', '--virtual-time-budget=20000', `--print-to-pdf=${pdf}`, url],
    { timeoutMs: 180_000 },
  ).catch((e: Error) => ({ exitCode: 1, stderr: e.message }))
  if (r.exitCode !== 0 || !(await $.fs.exists(pdf))) return `браузер не напечатал PDF: ${r.stderr.split('\n')[0] ?? ''}`
  return undefined
}

async function writeDialog($: EngineInterface, d: Dialog, o: Options, target: { dir: string; file?: string; obsidian?: boolean }) {
  const written: string[] = []
  const problems: string[] = []
  const stem = target.file ? target.file.replace(/\.(html?|md|json|pdf)$/i, '') : join(target.dir, baseName(d))
  let htmlPath: string | undefined
  const html = async () => {
    if (htmlPath) return htmlPath
    htmlPath = `${stem}.html`
    await $.fs.write(htmlPath, toHtml(d, await assets($)))
    return htmlPath
  }
  for (const f of o.formats) {
    if (f === 'html') written.push(await html())
    else if (f === 'md') {
      await $.fs.write(`${stem}.md`, toMarkdown(d, { obsidian: target.obsidian }))
      written.push(`${stem}.md`)
    } else if (f === 'json') {
      await $.fs.write(`${stem}.json`, JSON.stringify(d, null, 2))
      written.push(`${stem}.json`)
    } else if (f === 'pdf') {
      const src = await html()
      const why = await printPdf($, src, `${stem}.pdf`)
      if (why) problems.push(`PDF: ${why}`)
      else written.push(`${stem}.pdf`)
      if (!o.formats.includes('html') && !why) written.push(`${src} (для PDF)`)
    }
  }
  return { written, problems }
}

function stats(d: Dialog): string {
  let images = 0
  let files = 0
  let tools = 0
  for (const m of d.messages)
    for (const p of m.parts) {
      if (p.t === 'image') images++
      else if (p.t === 'file') files++
      else if (p.t === 'tool') { tools++; images += p.result?.images.length ?? 0 }
    }
  return `${d.messages.length} сообщ., ${images} изобр., ${files} файл., ${tools} вызов. инструментов`
}

async function outTarget($: EngineInterface, o: Options, many: boolean) {
  const cwd = await $.session.cwd()
  const out = o.out ? (isAbs(o.out) ? o.out : join(cwd, o.out)) : join(cwd, 'claude-exports')
  const isFile = !many && /\.(html?|md|json|pdf)$/i.test(out)
  return isFile ? { dir: out.replace(/[\\/][^\\/]*$/, ''), file: out } : { dir: out }
}

async function run($: EngineInterface, args: string): Promise<string> {
  const parsed = parseArgs(args)
  if ('error' in parsed) return parsed.error
  const o = parsed
  const where = await locate($)

  let vault: Vault | undefined
  let vaultNote = ''
  if (o.obsidian !== false) {
    const found = await findVault($, o)
    if ('error' in found) {
      if (o.obsidian || o.vault) return found.error
      vaultNote = `ℹ Obsidian: ${found.error.replace(/\.$/, '')} — сохраняю в папку проекта.`
    } else vault = found
  }
  if (o.formats.length === 0) o.formats = vault ? ['md'] : ['html']

  if (o.list) {
    if (!where.dir) return 'Не нашёл папку с историей сессий этого проекта.'
    const files = (await sessionFiles($, where.dir)).slice(0, 30)
    const rows = await Promise.all(
      files.map(async f => {
        const id = f.name.replace(/\.jsonl$/, '')
        const title = await readBig($, join(where.dir ?? '', f.name)).then(t => fromTranscript(t, o).title, () => '?')
        const date = new Date(f.mtimeMs).toLocaleString('ru-RU')
        return `${id === where.id ? '▶' : ' '} ${id.slice(0, 8)}  ${date}  ${title}`
      }),
    )
    return `Сессии проекта (новые сверху):\n${rows.join('\n')}\n\nЭкспорт: /${COMMAND} --session <id>`
  }

  const jobs: { file?: string }[] = []
  if (o.all || o.session) {
    if (!where.dir) return 'Не нашёл папку с историей сессий этого проекта.'
    const files = await sessionFiles($, where.dir)
    const picked = o.all ? files : files.filter(f => f.name.startsWith(o.session ?? ''))
    if (picked.length === 0) return `Нет сессии «${o.session}». Список: /${COMMAND} --list`
    if (!o.all && picked.length > 1) return `Под «${o.session}» подходит несколько сессий — уточните id.`
    jobs.push(...picked.map(f => ({ file: join(where.dir ?? '', f.name) })))
  } else jobs.push({ file: where.file })

  const target = vault ? undefined : await outTarget($, o, jobs.length > 1)
  const lines: string[] = []
  for (const job of jobs) {
    const isCurrent = job.file === where.file
    let d: Dialog
    try {
      d = isCurrent
        ? await currentDialog($, o, job.file)
        : { ...fromTranscript(await readBig($, job.file ?? ''), o), exportedAt: new Date().toISOString() }
    } catch (err) {
      lines.push(`✗ ${job.file}: ${(err as Error).message}`)
      continue
    }
    if (d.messages.length === 0) continue
    const stem = vault ? await vaultStem($, vault, d) : undefined
    const to = vault && stem ? { dir: vault.path, file: `${stem}.md`, obsidian: true } : target ?? { dir: '.' }
    const { written, problems } = await writeDialog($, d, o, to)
    lines.push(`✓ «${d.title}» — ${stats(d)}`, ...written.map(w => `   ${w}`), ...problems.map(p => `   ⚠ ${p}`))
    if (vault && stem && written.some(w => w.endsWith('.md'))) lines.push(`   Открыть: ${obsidianUri(vault, `${stem}.md`)}`)
  }
  const head = vault ? `Экспорт в Obsidian (${vault.path}, хранилище: ${vault.from}):` : 'Экспорт диалога:'
  const body = lines.length ? `${head}\n${lines.join('\n')}` : 'Нечего экспортировать: в диалоге пока нет сообщений.'
  return vaultNote ? `${body}\n${vaultNote}` : body
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    await $.command.register({
      name: 'export-dialog',
      description: 'Экспорт диалога в Obsidian или в HTML / Markdown / JSON / PDF с формулами, картинками и файлами',
      argumentHint: '[html|md|json|pdf|all] [obsidian|--here] [--vault путь] [--out путь] [--no-tools] [--thinking] [--list|--session id|--all]',
    })
    await $.tool.register({
      name: 'export_dialog',
      description:
        'Exports this conversation to files the user can open: a self-contained HTML page (KaTeX formulas, images, attached files, code, tool calls; works offline), Markdown, JSON or PDF. When an Obsidian vault is configured or found, the export is written straight into it as a Markdown note (default format md) and an obsidian:// link is returned. Use when the user asks to save, export or download the chat/dialog, or to put it into Obsidian. Returns the written paths.',
      inputSchema: {
        type: 'object',
        properties: {
          formats: { type: 'array', items: { type: 'string', enum: [...FORMATS] }, description: 'Default ["html"].' },
          out: { type: 'string', description: 'Folder or file path. Given, the export goes there instead of the Obsidian vault.' },
          obsidian: {
            type: 'boolean',
            description:
              'true: export into the Obsidian vault (fails when none is found); false: into ./claude-exports/. Omitted: into the vault when one is configured or found, else ./claude-exports/.',
          },
          vault: { type: 'string', description: 'Obsidian vault path to use and remember for later exports.' },
          folder: { type: 'string', description: 'Folder inside the vault (default "Claude"), remembered with vault.' },
          tools: { type: 'boolean', description: 'Include tool calls and their results (default true).' },
          thinking: { type: 'boolean', description: 'Include thinking blocks (default false).' },
        },
      },
    })
    return next(e)
  })

  on('command.run', { command: 'export-dialog' }, async ($, e) => ({ text: await run($, e.args) })).catch(($, e) => ({
    text: `Экспорт не удался. Подробности: claude --debug.`,
  }))

  on('tool.call', { tool: 'mcp__dialog-export__export_dialog' }, async ($, e) => {
    const input = e as unknown as {
      formats?: string[]; out?: string; tools?: boolean; thinking?: boolean; obsidian?: boolean; vault?: string; folder?: string
    }
    const args = [
      ...(input.formats ?? []),
      ...(input.out ? ['--out', `"${input.out}"`] : []),
      ...(input.obsidian === true ? ['--obsidian'] : input.obsidian === false ? ['--here'] : []),
      ...(input.vault ? ['--vault', `"${input.vault}"`] : []),
      ...(input.folder ? ['--folder', `"${input.folder}"`] : []),
      ...(input.tools === false ? ['--no-tools'] : []),
      ...(input.thinking ? ['--thinking'] : []),
    ].join(' ')
    return { result: await run($, args) }
  }).catch(() => ({ deny: 'dialog-export: экспорт не удался.' }))
}
