import { bridge } from './bridge'

/**
 * App-level shortcuts (PRD Appendix B) are captured at the document level — before
 * ProseMirror or CodeMirror see them — and delegated to the host as `shortcut` events
 * (ARCHITECTURE ADR-06). Editing shortcuts (Ctrl+B etc.) stay in the editor keymaps.
 */
const APP_SHORTCUTS = new Set([
  'ctrl+n',
  'ctrl+o',
  'ctrl+s',
  'ctrl+shift+s',
  'ctrl+w',
  'ctrl+tab',
  'ctrl+shift+tab',
  'ctrl+f',
  'ctrl+h',
  'ctrl+p',
  'ctrl+,',
  'ctrl+shift+e',
  'ctrl+shift+o',
  'ctrl+shift+t',
  'ctrl+shift+b',
  'ctrl+=',
  'ctrl+-',
  'ctrl+0',
  'f5',
  ...Array.from({ length: 9 }, (_, i) => `ctrl+alt+${i + 1}`),
])

export function shortcutKey(e: KeyboardEvent): string | null {
  const parts: string[] = []
  if (e.ctrlKey) parts.push('ctrl')
  if (e.altKey) parts.push('alt')
  if (e.shiftKey) parts.push('shift')
  let key = e.key.toLowerCase()
  if (key === '+') key = '='
  if (key === 'add') key = '='
  if (key === 'subtract') key = '-'
  if (key === 'control' || key === 'shift' || key === 'alt' || key === 'meta') return null
  // Use the physical digit for Ctrl+Alt+n so IME/AltGr layouts do not interfere.
  if (e.ctrlKey && e.altKey && /^Digit[1-9]$/.test(e.code)) key = e.code.slice(5)
  parts.push(key)
  return parts.join('+')
}

export function installHostKeymap(target: Document = document): () => void {
  const onKeyDown = (e: KeyboardEvent): void => {
    if (e.isComposing) return
    const key = shortcutKey(e)
    if (!key || !APP_SHORTCUTS.has(key)) return
    e.preventDefault()
    e.stopPropagation()
    bridge.emit('shortcut', { key })
  }
  target.addEventListener('keydown', onKeyDown, { capture: true })
  return () => target.removeEventListener('keydown', onKeyDown, { capture: true })
}
