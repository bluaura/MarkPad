import { bridge } from '../bridge'
import type { AssetReadResult, ExportPreparePrintParams, ExportRenderHtmlParams, ExportRenderHtmlResult } from '../bridge-types'
import { renderHtml } from '../export/render-html'
import { joinFrontMatter } from '../frontmatter'
import { requireSession } from '../session'
import { applyTheme, currentTheme } from '../theme'

let printSnapshot: { mode: 'light' | 'dark'; readonly: boolean } | null = null

export function registerExportHandlers(): void {
  bridge.register('export.renderHtml', async (raw): Promise<ExportRenderHtmlResult> => {
    const p = (raw ?? {}) as Partial<ExportRenderHtmlParams>
    const s = requireSession()
    const markdown = joinFrontMatter('', s.editor.getMarkdown())
    const result = await renderHtml(markdown, {
      inlineImages: !!p.inlineImages,
      theme: p.theme === 'dark' ? 'dark' : 'light',
      highlight: s.settings.markdown.extHighlight,
      readImage: async (src) => {
        try {
          const r = await bridge.call<AssetReadResult>('asset.readBase64', { src })
          return r?.dataUrl ?? null
        } catch {
          return null
        }
      },
    })
    return result
  })

  // PDF pipeline (ARCHITECTURE §3.2 PdfExportService): light theme, readonly, print CSS, then restore.
  bridge.register('export.preparePrint', (raw) => {
    const p = (raw ?? {}) as Partial<ExportPreparePrintParams>
    const s = requireSession()
    printSnapshot = { mode: currentTheme().mode, readonly: s.editor.crepe.readonly }
    applyTheme({ mode: p.theme === 'dark' ? 'dark' : 'light' })
    s.editor.setReadonly(true)
    document.documentElement.classList.add('mp-print')
    return null
  })

  bridge.register('export.restore', () => {
    document.documentElement.classList.remove('mp-print')
    const s = requireSession()
    if (printSnapshot) {
      applyTheme({ mode: printSnapshot.mode })
      s.editor.setReadonly(printSnapshot.readonly)
      printSnapshot = null
    }
    return null
  })
}
