// The export's own model of a conversation, and its reading from a Claude Code
// transcript (.jsonl) or from the engine's Messages API view of the session.

export type ToolResult = { text: string; images: { mime: string; data: string }[]; isError: boolean }

export type Part =
  | { t: 'text'; text: string }
  | { t: 'image'; mime: string; data?: string; url?: string; name?: string }
  | { t: 'file'; name: string; mime: string; data?: string; text?: string; lang?: string }
  | { t: 'tool'; id: string; name: string; input: Record<string, unknown>; result?: ToolResult }
  | { t: 'thinking'; text: string }
  | { t: 'command'; name: string; args: string; output?: string }
  | { t: 'note'; text: string }

export type Role = 'user' | 'assistant' | 'system'
export type Msg = { role: Role; time?: string; parts: Part[] }

export type Dialog = {
  title: string
  sessionId?: string
  cwd?: string
  startedAt?: string
  exportedAt: string
  models: string[]
  source: 'transcript' | 'context'
  note?: string
  messages: Msg[]
}

export type ReadOptions = { tools: boolean; thinking: boolean; skipCommand?: string }

type Block = { type: string; [field: string]: unknown }
type Row = Record<string, any>

const REMINDER = /<system-reminder>[\s\S]*?<\/system-reminder>/g
const CAVEAT = /<local-command-caveat>[\s\S]*?<\/local-command-caveat>/g
const ANSI = /\x1b\[[0-9;?]*[ -/]*[@-~]/g

const tag = (text: string, name: string) => new RegExp(`<${name}>([\\s\\S]*?)</${name}>`).exec(text)?.[1]

class Builder {
  messages: Msg[] = []
  tools = new Map<string, Extract<Part, { t: 'tool' }>>()
  models = new Set<string>()
  lastCommand: Extract<Part, { t: 'command' }> | undefined
  lastBash: Extract<Part, { t: 'tool' }> | undefined
  skipping = false
  opts: ReadOptions

  constructor(opts: ReadOptions) {
    this.opts = opts
  }

  push(role: Role, parts: Part[], time?: string) {
    if (parts.length === 0) return
    const last = this.messages.at(-1)
    if (last && last.role === role) last.parts.push(...parts)
    else this.messages.push({ role, time, parts })
  }

  userText(raw: string): Part[] {
    const name = tag(raw, 'command-name')
    if (name !== undefined) {
      const part: Part = { t: 'command', name: name.trim().replace(/^\//, ''), args: (tag(raw, 'command-args') ?? '').trim() }
      this.skipping = part.name === this.opts.skipCommand
      this.lastCommand = part
      return this.skipping ? [] : [part]
    }
    const stdout = tag(raw, 'local-command-stdout') ?? tag(raw, 'local-command-stderr')
    if (stdout !== undefined) {
      if (this.lastCommand && !this.skipping) this.lastCommand.output = stdout.replace(ANSI, '').trim()
      return []
    }
    const bash = tag(raw, 'bash-input')
    if (bash !== undefined) {
      const part: Part = { t: 'tool', id: `bash-${this.tools.size}`, name: 'Bash', input: { command: bash } }
      this.lastBash = part
      return this.opts.tools ? [part] : []
    }
    const out = tag(raw, 'bash-stdout')
    if (out !== undefined) {
      const err = (tag(raw, 'bash-stderr') ?? '').replace(ANSI, '')
      if (this.lastBash) this.lastBash.result = { text: [out.replace(ANSI, ''), err].filter(Boolean).join('\n'), images: [], isError: err.length > 0 }
      return []
    }
    this.skipping = false
    const text = raw.replace(REMINDER, '').replace(CAVEAT, '').trim()
    return text ? [{ t: 'text', text }] : []
  }

  result(block: Block) {
    const tool = this.tools.get(String(block.tool_use_id))
    if (!tool) return
    const r: ToolResult = { text: '', images: [], isError: block.is_error === true }
    const content = block.content
    if (typeof content === 'string') r.text = content
    else if (Array.isArray(content)) {
      const texts: string[] = []
      for (const c of content as Block[]) {
        if (c.type === 'text') texts.push(String(c.text))
        else if (c.type === 'image') {
          const src = c.source as Row | undefined
          if (src?.type === 'base64') r.images.push({ mime: String(src.media_type), data: String(src.data) })
        } else if (Array.isArray(c.content) || typeof c.content === 'string') texts.push(JSON.stringify(c.content, null, 2))
      }
      r.text = texts.join('\n')
    } else if (content != null) r.text = JSON.stringify(content, null, 2)
    r.text = r.text.replace(REMINDER, '').replace(ANSI, '').trim()
    tool.result = r
  }

  blocks(role: 'user' | 'assistant', content: unknown): Part[] {
    if (typeof content === 'string') return role === 'user' ? this.userText(content) : content.trim() ? [{ t: 'text', text: content }] : []
    if (!Array.isArray(content)) return []
    const parts: Part[] = []
    for (const b of content as Block[]) {
      switch (b.type) {
        case 'text': {
          const text = String(b.text ?? '')
          parts.push(...(role === 'user' ? this.userText(text) : text.trim() ? [{ t: 'text' as const, text }] : []))
          break
        }
        case 'image': {
          const src = b.source as Row | undefined
          if (src?.type === 'base64') parts.push({ t: 'image', mime: String(src.media_type), data: String(src.data) })
          else if (src?.type === 'url') parts.push({ t: 'image', mime: 'image/*', url: String(src.url) })
          break
        }
        case 'document': {
          const src = (b.source ?? {}) as Row
          const name = String(b.title ?? (src.media_type === 'application/pdf' ? 'документ.pdf' : 'документ.txt'))
          if (src.type === 'base64') parts.push({ t: 'file', name, mime: String(src.media_type), data: String(src.data) })
          else if (src.type === 'text') parts.push({ t: 'file', name, mime: String(src.media_type ?? 'text/plain'), text: String(src.data) })
          else if (src.type === 'content' && Array.isArray(src.content))
            parts.push({ t: 'file', name, mime: 'text/plain', text: (src.content as Block[]).map(c => String(c.text ?? '')).join('\n') })
          else if (src.type === 'url') parts.push({ t: 'text', text: `📎 [${name}](${String(src.url)})` })
          break
        }
        case 'thinking':
          if (this.opts.thinking && String(b.thinking ?? '').trim()) parts.push({ t: 'thinking', text: String(b.thinking) })
          break
        case 'tool_use':
        case 'server_tool_use':
        case 'mcp_tool_use': {
          if (!this.opts.tools) break
          const part: Part = { t: 'tool', id: String(b.id), name: String(b.name), input: (b.input ?? {}) as Record<string, unknown> }
          this.tools.set(part.id, part)
          parts.push(part)
          break
        }
        default:
          if (b.type.endsWith('tool_result')) this.result(b)
      }
    }
    return parts
  }

  attachment(a: Row | undefined, time?: string) {
    if (!a || typeof a !== 'object') return
    if (a.type === 'queued_command' && typeof a.prompt === 'string') {
      this.push('user', this.userText(a.prompt), time)
      return
    }
    if (a.type === 'directory' && typeof a.content === 'string') {
      this.push('user', [{ t: 'file', name: `${String(a.path ?? 'папка')}/`, mime: 'inode/directory', text: a.content }], time)
      return
    }
    if (a.type !== 'file') return
    const c = (a.content ?? {}) as Row
    const f = (c.file ?? {}) as Row
    const name = String(a.filename ?? f.filePath ?? 'файл')
    if (c.type === 'text') this.push('user', [{ t: 'file', name, mime: 'text/plain', text: String(f.content ?? '') }], time)
    else if (c.type === 'image' && f.base64) this.push('user', [{ t: 'image', mime: String(f.type ?? 'image/png'), data: String(f.base64), name }], time)
    else if (c.type === 'pdf' && f.base64) this.push('user', [{ t: 'file', name, mime: 'application/pdf', data: String(f.base64) }], time)
    else if (c.type === 'notebook' && Array.isArray(f.cells))
      this.push('user', [{ t: 'file', name, mime: 'application/x-ipynb+json', text: JSON.stringify(f.cells, null, 2), lang: 'json' }], time)
  }
}

export function firstPrompt(messages: readonly Msg[]): string | undefined {
  for (const m of messages) {
    if (m.role !== 'user') continue
    for (const p of m.parts) {
      if (p.t !== 'text') continue
      const plain = p.text
        .replace(/```[\s\S]*?```/g, ' ')
        .replace(/\$\$[\s\S]*?\$\$|\\\[[\s\S]*?\\\]|\\\([\s\S]*?\\\)/g, m => m.replace(/^(\$\$|\\[[(])|(\$\$|\\[\])])$/g, ''))
        .replace(/[#*_`>$]/g, '')
        .replace(/\s+/g, ' ')
        .trim()
      if (plain) return plain.length > 80 ? `${plain.slice(0, 80).replace(/\s+\S*$/, '')}…` : plain
    }
  }
  return undefined
}

/** Reads a Claude Code transcript: one JSON row per line, the whole history kept. */
export function fromTranscript(jsonl: string, opts: ReadOptions): Omit<Dialog, 'exportedAt'> {
  const b = new Builder(opts)
  let title: string | undefined
  let customTitle: string | undefined
  let cwd: string | undefined
  let sessionId: string | undefined
  let startedAt: string | undefined
  for (const line of jsonl.split('\n')) {
    if (!line.trim()) continue
    let r: Row
    try { r = JSON.parse(line) } catch { continue }
    if (r.type === 'summary' && typeof r.summary === 'string') title ??= r.summary
    if (r.type === 'custom-title' && typeof r.customTitle === 'string') customTitle = r.customTitle
    if (r.isSidechain) continue
    const time = typeof r.timestamp === 'string' ? r.timestamp : undefined
    if (r.type === 'system' && r.subtype === 'compact_boundary') {
      b.push('system', [{ t: 'note', text: 'Здесь контекст был сжат; выше — полная история.' }], time)
      continue
    }
    if (r.type === 'attachment') {
      b.attachment(r.attachment, time)
      continue
    }
    if (r.type !== 'user' && r.type !== 'assistant') continue
    if (r.isMeta || r.isCompactSummary || r.isVisibleInTranscriptOnly) {
      // still pair tool results that ride on such rows
      if (Array.isArray(r.message?.content)) b.blocks('user', (r.message.content as Block[]).filter(x => x.type === 'tool_result'))
      continue
    }
    cwd ??= r.cwd
    sessionId ??= r.sessionId
    startedAt ??= time
    if (r.type === 'assistant' && typeof r.message?.model === 'string' && !r.message.model.startsWith('<')) b.models.add(r.message.model)
    b.push(r.type, b.blocks(r.type, r.message?.content), time)
  }
  return {
    title: customTitle ?? title ?? firstPrompt(b.messages) ?? 'Диалог с Claude',
    sessionId, cwd, startedAt,
    models: [...b.models],
    source: 'transcript',
    messages: b.messages,
  }
}

/** Reads the engine's Messages API view: what the next request is built from. */
export function fromApi(messages: readonly { role: 'user' | 'assistant'; content: unknown }[], opts: ReadOptions): Pick<Dialog, 'messages' | 'source' | 'models'> {
  const b = new Builder(opts)
  for (const m of messages) b.push(m.role, b.blocks(m.role, m.content))
  return { messages: b.messages, source: 'context', models: [] }
}
