import type { EvtMsg, Msg, ReqMsg, ResErr, ResOk } from './bridge-types'

/** Error raised by a handler; `code` travels to the host as `e.code`. */
export class BridgeError extends Error {
  constructor(
    public readonly code: string,
    msg: string,
  ) {
    super(msg)
    this.name = 'BridgeError'
  }
}

export type Handler = (params: unknown) => unknown | Promise<unknown>

interface Transport {
  send(msg: Msg): void
  onMessage(cb: (msg: Msg) => void): void
}

interface WebViewLike {
  postMessage(msg: unknown): void
  addEventListener(type: 'message', cb: (e: { data: unknown }) => void): void
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewLike }
    /** Dev-only hook: the browser console can push host→web messages with `__markpadHost.send({...})`. */
    __markpadHost?: { send(msg: Msg): void; log: Msg[] }
  }
}

function createTransport(): Transport {
  const wv = window.chrome?.webview
  if (wv) {
    return {
      send: (msg) => wv.postMessage(msg),
      onMessage: (cb) => wv.addEventListener('message', (e) => cb(e.data as Msg)),
    }
  }
  // Browser dev mode (npm run dev): echo to console and keep an inbox for manual testing.
  const log: Msg[] = []
  let handler: ((msg: Msg) => void) | null = null
  window.__markpadHost = {
    log,
    send: (msg) => handler?.(msg),
  }
  return {
    send: (msg) => {
      log.push(msg)
      if (msg.t !== 'evt' || (msg.m !== 'changed' && msg.m !== 'selection')) {
        console.debug('[bridge → host]', msg)
      }
      // Auto-answer a few host requests so image paste works in the browser.
      if (msg.t === 'req' && handler) {
        const reply = devAutoReply(msg)
        if (reply) setTimeout(() => handler?.(reply), 0)
      }
    },
    onMessage: (cb) => {
      handler = cb
    },
  }
}

function devAutoReply(req: ReqMsg): ResOk | ResErr | null {
  const p = req.p as Record<string, unknown> | undefined
  switch (req.m) {
    case 'asset.save':
      return { t: 'res', id: req.id, ok: true, r: { relPath: `data:${p?.mime};base64,${p?.bytesBase64}` } }
    case 'image.resolve':
      return { t: 'res', id: req.id, ok: true, r: { url: p?.src } }
    case 'link.open':
      window.open(String(p?.href), '_blank', 'noopener')
      return { t: 'res', id: req.id, ok: true, r: { ok: true } }
    default:
      return { t: 'res', id: req.id, ok: false, e: { code: 'NOT_IMPLEMENTED', msg: `dev transport: ${req.m}` } }
  }
}

const DEFAULT_TIMEOUT_MS = 10_000

/**
 * JSON-RPC style bridge (ARCHITECTURE §4.2). Request ids are independent per direction.
 * host→web requests are dispatched to registered handlers; web→host requests await a response.
 */
export class Bridge {
  private readonly handlers = new Map<string, Handler>()
  private readonly pending = new Map<
    number,
    { resolve: (v: unknown) => void; reject: (e: Error) => void; timer: number }
  >()
  private nextId = 1
  private readonly transport: Transport

  constructor() {
    this.transport = createTransport()
    this.transport.onMessage((msg) => void this.dispatch(msg))
  }

  register(method: string, handler: Handler): void {
    if (this.handlers.has(method)) throw new Error(`bridge: duplicate handler for ${method}`)
    this.handlers.set(method, handler)
  }

  emit(method: string, payload?: unknown): void {
    const msg: EvtMsg = { t: 'evt', m: method, p: payload }
    this.transport.send(msg)
  }

  log(level: 'debug' | 'info' | 'warn' | 'error', msg: string): void {
    this.emit('log', { level, msg })
  }

  call<T = unknown>(method: string, payload?: unknown, timeoutMs = DEFAULT_TIMEOUT_MS): Promise<T> {
    const id = this.nextId++
    const msg: ReqMsg = { t: 'req', id, m: method, p: payload }
    return new Promise<T>((resolve, reject) => {
      const timer = window.setTimeout(() => {
        this.pending.delete(id)
        reject(new BridgeError('TIMEOUT', `host did not answer ${method} within ${timeoutMs}ms`))
      }, timeoutMs)
      this.pending.set(id, { resolve: resolve as (v: unknown) => void, reject, timer })
      this.transport.send(msg)
    })
  }

  private async dispatch(msg: Msg): Promise<void> {
    if (!msg || typeof msg !== 'object' || !('t' in msg)) return
    switch (msg.t) {
      case 'req':
        await this.handleRequest(msg)
        return
      case 'res':
        this.handleResponse(msg)
        return
      case 'evt':
        console.debug('[bridge ← host evt]', msg.m, msg.p)
        return
    }
  }

  private async handleRequest(req: ReqMsg): Promise<void> {
    const handler = this.handlers.get(req.m)
    if (!handler) {
      this.transport.send({ t: 'res', id: req.id, ok: false, e: { code: 'UNKNOWN_METHOD', msg: req.m } })
      return
    }
    try {
      const r = await handler(req.p)
      this.transport.send({ t: 'res', id: req.id, ok: true, r: r ?? null })
    } catch (err) {
      const code = err instanceof BridgeError ? err.code : 'INTERNAL'
      const text = err instanceof Error ? err.message : String(err)
      console.error(`[bridge] ${req.m} failed:`, err)
      this.transport.send({ t: 'res', id: req.id, ok: false, e: { code, msg: text } })
    }
  }

  private handleResponse(res: ResOk | ResErr): void {
    const entry = this.pending.get(res.id)
    if (!entry) return
    this.pending.delete(res.id)
    window.clearTimeout(entry.timer)
    if (res.ok) entry.resolve(res.r)
    else entry.reject(new BridgeError(res.e.code, res.e.msg))
  }
}

export const bridge = new Bridge()
