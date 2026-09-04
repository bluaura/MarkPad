import type { EditorSettings } from './bridge-types'
import type { MarkPadEditor } from './editor'
import { BridgeError } from './bridge'

/** Per-page document session: the live editor plus the pieces kept outside it (front matter). */
export interface Session {
  editor: MarkPadEditor
  settings: EditorSettings
  path: string | null
  frontMatterRaw: string
}

let session: Session | null = null

export function getSession(): Session | null {
  return session
}

export function requireSession(): Session {
  if (!session) throw new BridgeError('NOT_READY', 'no document loaded')
  return session
}

export function setSession(next: Session | null): void {
  session = next
}
