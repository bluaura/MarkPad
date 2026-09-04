import '@milkdown/crepe/theme/common/style.css'
import './styles/editor.css'

import { bridge } from './bridge'
import type { EditorSettings } from './bridge-types'
import { registerDocHandlers } from './handlers/doc'
import { registerFindHandlers } from './handlers/find'
import { registerFormatHandlers } from './handlers/format'
import { registerInsertHandlers } from './handlers/insert'
import { registerViewHandlers } from './handlers/view'
import { installFileDrop } from './file-drop'
import { installHostKeymap } from './host-keymap'
import { defaultTheme, applyTheme } from './theme'

export const EDITOR_VERSION = __EDITOR_VERSION__

registerDocHandlers()
registerFormatHandlers()
registerInsertHandlers()
registerViewHandlers()
registerFindHandlers()
installHostKeymap()
installFileDrop()
applyTheme(defaultTheme)

window.addEventListener('error', (e) => bridge.log('error', `${e.message} @${e.filename}:${e.lineno}`))
window.addEventListener('unhandledrejection', (e) => bridge.log('error', `unhandled: ${String(e.reason)}`))

bridge.emit('ready', { version: EDITOR_VERSION })

// Browser dev mode: no host → load a sample document so `npm run dev` is useful on its own.
if (!window.chrome?.webview) {
  const settings: EditorSettings = {
    theme: { ...defaultTheme, mode: matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light' },
    markdown: {
      emphasis: '*',
      bullet: '-',
      listIndent: 2,
      extHighlight: false,
      extMath: true,
      extMermaid: true,
      frontMatter: true,
    },
    allowRemoteImages: true,
  }
  const sample = [
    '---',
    'title: MarkPad dev sample',
    'tags: [dev, sample]',
    '---',
    '',
    '# MarkPad 개발 모드',
    '',
    '브라우저에서 직접 실행 중입니다. 호스트 없이 편집기 동작만 확인합니다.',
    '',
    '- [ ] 체크리스트 항목',
    '- [x] 완료 항목',
    '',
    '| 열 A | 열 B |',
    '|---|---|',
    '| 1 | 2 |',
    '',
    '```ts',
    'const answer: number = 42',
    '```',
    '',
    '> 인용문과 **굵게**, *기울임*, ~~취소선~~, `코드`.',
    '',
  ].join('\n')
  window.__markpadHost?.send({ t: 'req', id: 1, m: 'doc.load', p: { text: sample, settings } })
  const bar = document.createElement('div')
  bar.className = 'markpad-devbar'
  bar.textContent = `dev mode · editor ${EDITOR_VERSION} · __markpadHost.send({t:'req',id:2,m:'format.toggle',p:{mark:'bold'}})`
  document.body.appendChild(bar)
}
