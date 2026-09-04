/**
 * `==highlight==` extension (PRD Appendix A/C: default OFF, settings.markdown.extHighlight turns it on).
 * Parse: a remark transformer splits `==…==` spans out of text nodes into `highlight` mdast nodes.
 * Serialize: a remark-stringify handler writes them back as `==…==` (registered in crepe-factory).
 * When the extension is off nothing is registered and `==` stays ordinary text (same as GitHub / VS Code).
 */
import type { MilkdownPlugin } from '@milkdown/kit/ctx'
import { markRule } from '@milkdown/kit/prose'
import { toggleMark } from '@milkdown/kit/prose/commands'
import { $command, $inputRule, $markSchema, $remark } from '@milkdown/kit/utils'
import type { Parent, PhrasingContent, Root, Text } from 'mdast'
import type { Plugin as UnifiedPlugin } from 'unified'
import { visit } from 'unist-util-visit'

export interface HighlightNode extends Parent {
  type: 'highlight'
  children: PhrasingContent[]
}

const HIGHLIGHT_RE = /==([^=\n]+?)==/g

/** Split text nodes on `==…==` (mdast transformer; no micromark syntax extension needed). */
export function splitHighlights(tree: Root): void {
  visit(tree, 'text', (node: Text, index, parent) => {
    if (!parent || typeof index !== 'number') return // code / inlineCode are literals, never text parents
    const value = node.value
    if (!value.includes('==')) return
    const out: (Text | HighlightNode)[] = []
    let last = 0
    HIGHLIGHT_RE.lastIndex = 0
    let m: RegExpExecArray | null
    while ((m = HIGHLIGHT_RE.exec(value)) !== null) {
      if (m.index > last) out.push({ type: 'text', value: value.slice(last, m.index) })
      out.push({ type: 'highlight', children: [{ type: 'text', value: m[1]! }] })
      last = m.index + m[0].length
    }
    if (out.length === 0) return
    if (last < value.length) out.push({ type: 'text', value: value.slice(last) })
    ;(parent.children as unknown[]).splice(index, 1, ...out)
    return index + out.length
  })
}

const remarkHighlight: UnifiedPlugin<[], Root> = () => (tree) => splitHighlights(tree)

export const remarkHighlightPlugin = $remark('remark-highlight', () => remarkHighlight)

export const highlightSchema = $markSchema('highlight', () => ({
  inclusive: false,
  parseDOM: [{ tag: 'mark' }],
  toDOM: () => ['mark', { class: 'mp-highlight' }, 0],
  parseMarkdown: {
    match: (node) => node.type === 'highlight',
    runner: (state, node, markType) => {
      state.openMark(markType)
      state.next((node as HighlightNode).children as never)
      state.closeMark(markType)
    },
  },
  toMarkdown: {
    match: (mark) => mark.type.name === 'highlight',
    runner: (state, mark) => {
      state.withMark(mark, 'highlight')
    },
  },
}))

export const toggleHighlightCommand = $command('ToggleHighlight', (ctx) => () => toggleMark(highlightSchema.type(ctx)))

export const highlightInputRule = $inputRule((ctx) => markRule(/==([^=]+)==$/, highlightSchema.type(ctx)))

/** remark-stringify handler for the `highlight` mdast node (added to remarkStringifyOptionsCtx). */
export function highlightStringifyHandler(
  node: HighlightNode,
  _parent: unknown,
  state: { containerPhrasing: (node: HighlightNode, info: unknown) => string },
  info: unknown,
): string {
  return `==${state.containerPhrasing(node, info)}==`
}

export const highlightPlugin: MilkdownPlugin[] = [...remarkHighlightPlugin, ...highlightSchema, toggleHighlightCommand, highlightInputRule]
