import { Crepe } from '@milkdown/crepe'
import type { Ctx } from '@milkdown/kit/ctx'
import { editorViewCtx, remarkStringifyOptionsCtx } from '@milkdown/kit/core'
import type { Node as ProseNode } from '@milkdown/kit/prose/model'
import type { EditorView } from '@milkdown/kit/prose/view'
import { bridge } from './bridge'
import type { AssetSaveResult, ChangedEvent, EditorSettings } from './bridge-types'
import { resolveImageUrl, setAllowRemoteImages } from './plugins/image-resolver'
import { computeSelectionContext, computeStats, currentLine } from './selection'

export interface MarkPadEditor {
  readonly crepe: Crepe
  action<T>(fn: (ctx: Ctx) => T): T
  view(): EditorView
  getMarkdown(): string
  isDirty(): boolean
  setReadonly(value: boolean): void
  focus(): void
  destroy(): Promise<void>
}

const CHANGED_DEBOUNCE_MS = 150
const SELECTION_DEBOUNCE_MS = 50

async function fileToBase64(file: File): Promise<string> {
  const buf = new Uint8Array(await file.arrayBuffer())
  let bin = ''
  const chunk = 0x8000
  for (let i = 0; i < buf.length; i += chunk) {
    bin += String.fromCharCode(...buf.subarray(i, i + chunk))
  }
  return btoa(bin)
}

async function uploadToHost(file: File): Promise<string> {
  const bytesBase64 = await fileToBase64(file)
  const r = await bridge.call<AssetSaveResult>('asset.save', {
    bytesBase64,
    mime: file.type || 'application/octet-stream',
    suggestedName: file.name || undefined,
  })
  return r.relPath
}

/**
 * Create a Crepe editor for one document (ARCHITECTURE §3.3 "부트스트랩").
 * A fresh instance per load keeps history and serializer options clean; each tab owns a WebView anyway.
 */
export async function createEditor(root: HTMLElement, markdown: string, settings: EditorSettings): Promise<MarkPadEditor> {
  root.replaceChildren()
  setAllowRemoteImages(settings.allowRemoteImages)

  const crepe = new Crepe({
    root,
    defaultValue: markdown,
    features: {
      [Crepe.Feature.Toolbar]: false, // native XAML toolbar (ADR-02)
      [Crepe.Feature.TopBar]: false,
      [Crepe.Feature.BlockEdit]: false, // revisit in P2 (T-61)
      [Crepe.Feature.AI]: false,
      [Crepe.Feature.Placeholder]: true,
      [Crepe.Feature.Latex]: settings.markdown.extMath,
    },
    featureConfigs: {
      [Crepe.Feature.ImageBlock]: {
        onUpload: uploadToHost,
        inlineOnUpload: uploadToHost,
        blockOnUpload: uploadToHost,
        proxyDomURL: resolveImageUrl,
      },
      [Crepe.Feature.Placeholder]: {
        text: '내용을 입력하세요…',
        mode: 'doc',
      },
    },
  })

  // Serialization style for NEW/changed blocks (PRD F-SET-04). Untouched blocks keep the original (§5).
  crepe.editor.config((ctx) => {
    ctx.update(remarkStringifyOptionsCtx, (prev) => ({
      ...prev,
      bullet: settings.markdown.bullet,
      emphasis: settings.markdown.emphasis,
      strong: '*' as const,
      listItemIndent: settings.markdown.listIndent >= 4 ? ('tab' as const) : ('one' as const),
      fences: true,
      rule: '-' as const,
      ruleRepetition: 3,
    }))
  })

  let loadedDoc: ProseNode | null = null
  let changedTimer = 0
  let selectionTimer = 0
  let lastChanged: ChangedEvent | null = null

  const emitChanged = (ctx: Ctx): void => {
    const state = ctx.get(editorViewCtx).state
    const dirty = loadedDoc ? !state.doc.eq(loadedDoc) : false
    const { words, chars } = computeStats(state)
    const evt: ChangedEvent = { dirty, words, chars, line: currentLine(state) }
    if (
      lastChanged &&
      lastChanged.dirty === evt.dirty &&
      lastChanged.words === evt.words &&
      lastChanged.chars === evt.chars &&
      lastChanged.line === evt.line
    ) {
      return
    }
    lastChanged = evt
    bridge.emit('changed', evt)
  }

  const scheduleChanged = (ctx: Ctx): void => {
    window.clearTimeout(changedTimer)
    changedTimer = window.setTimeout(() => emitChanged(ctx), CHANGED_DEBOUNCE_MS)
  }

  const scheduleSelection = (ctx: Ctx): void => {
    window.clearTimeout(selectionTimer)
    selectionTimer = window.setTimeout(() => {
      const state = ctx.get(editorViewCtx).state
      bridge.emit('selection', computeSelectionContext(state))
    }, SELECTION_DEBOUNCE_MS)
  }

  crepe.on((listener) => {
    listener.mounted((ctx) => {
      loadedDoc = ctx.get(editorViewCtx).state.doc
      emitChanged(ctx)
      scheduleSelection(ctx)
    })
    // `updated` fires per transaction without serializing (see ARCHITECTURE Appendix A.4).
    listener.updated((ctx) => {
      scheduleChanged(ctx)
      scheduleSelection(ctx)
    })
    listener.selectionUpdated((ctx) => scheduleSelection(ctx))
  })

  await crepe.create()

  return {
    crepe,
    action: (fn) => crepe.editor.action(fn),
    view: () => crepe.editor.ctx.get(editorViewCtx),
    getMarkdown: () => crepe.getMarkdown(),
    isDirty: () => {
      const doc = crepe.editor.ctx.get(editorViewCtx).state.doc
      return loadedDoc ? !doc.eq(loadedDoc) : false
    },
    setReadonly: (value) => {
      crepe.setReadonly(value)
    },
    focus: () => {
      crepe.editor.ctx.get(editorViewCtx).focus()
    },
    destroy: async () => {
      window.clearTimeout(changedTimer)
      window.clearTimeout(selectionTimer)
      await crepe.destroy()
    },
  }
}
