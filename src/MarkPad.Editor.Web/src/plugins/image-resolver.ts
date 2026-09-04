import { bridge } from '../bridge'
import type { ImageResolveResult } from '../bridge-types'

/** Virtual host the WinUI host maps to the document folder (ARCHITECTURE §3.3 "이미지 처리"). */
export const DOC_HOST = 'https://doc.markpad/'

const REMOTE_RE = /^(https?:)?\/\//i
const DATA_RE = /^(data|blob):/i
const WIN_ABS_RE = /^([a-zA-Z]:[\\/]|\\\\|file:)/

let allowRemote = true
const resolved = new Map<string, string>()

export function setAllowRemoteImages(value: boolean): void {
  allowRemote = value
}

/** 1x1 transparent placeholder used when remote images are blocked. */
const BLOCKED_PLACEHOLDER =
  'data:image/svg+xml;utf8,' +
  encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="240" height="80"><rect width="100%" height="100%" fill="#eee"/><text x="50%" y="50%" font-size="12" text-anchor="middle" dominant-baseline="middle" fill="#888" font-family="sans-serif">remote image blocked</text></svg>',
  )

/**
 * Convert the markdown `src` (kept verbatim in the model) into a URL the WebView can display.
 * Relative paths → doc.markpad virtual host; absolute local paths → host-side drive mapping;
 * remote → as is or placeholder; data: → as is.
 */
export async function resolveImageUrl(src: string): Promise<string> {
  if (!src) return src
  if (DATA_RE.test(src)) return src
  if (REMOTE_RE.test(src)) return allowRemote ? src : BLOCKED_PLACEHOLDER
  if (WIN_ABS_RE.test(src)) {
    const cached = resolved.get(src)
    if (cached) return cached
    try {
      const r = await bridge.call<ImageResolveResult>('image.resolve', { src })
      resolved.set(src, r.url)
      return r.url
    } catch {
      return src
    }
  }
  if (src.startsWith('/')) return DOC_HOST + src.slice(1)
  // Relative path: keep "./" and "../" segments, the virtual host resolves them against the doc folder.
  return DOC_HOST + src.replace(/^\.\//, '')
}
