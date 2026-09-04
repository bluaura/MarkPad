import type { EditorTheme } from './bridge-types'

export const defaultTheme: EditorTheme = {
  mode: 'light',
  fontFamily: "'Segoe UI Variable Text', 'Segoe UI', 'Malgun Gothic', sans-serif",
  fontSize: 15,
  lineHeight: 1.7,
  maxWidth: 800,
  zoom: 1,
}

let current: EditorTheme = { ...defaultTheme }

export function currentTheme(): EditorTheme {
  return current
}

/** Apply theme values as CSS custom properties on <html> (ARCHITECTURE §3.3 "테마"). */
export function applyTheme(theme: Partial<EditorTheme>): EditorTheme {
  current = { ...current, ...theme }
  const root = document.documentElement
  root.dataset['theme'] = current.mode
  root.style.setProperty('--mp-font-family', current.fontFamily)
  root.style.setProperty('--mp-font-size', `${current.fontSize}px`)
  root.style.setProperty('--mp-line-height', String(current.lineHeight))
  root.style.setProperty('--mp-max-width', `${current.maxWidth}px`)
  root.style.setProperty('--mp-zoom', String(current.zoom))
  if (current.accent) root.style.setProperty('--mp-accent', current.accent)
  return current
}
