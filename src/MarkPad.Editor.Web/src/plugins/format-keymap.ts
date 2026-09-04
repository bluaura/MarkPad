import { editorViewCtx } from '@milkdown/kit/core'
import type { Ctx } from '@milkdown/kit/ctx'
import { keymap } from '@milkdown/kit/prose/keymap'
import type { Command } from '@milkdown/kit/prose/state'
import { $prose } from '@milkdown/kit/utils'
import * as cmd from '../commands'
import type { CommandTarget } from '../commands'

/**
 * Editing shortcuts (PRD Appendix A) that stay inside the editor (ADR-06). Registered before Milkdown's
 * own keymaps so ours win on overlapping keys. Ctrl+K / Ctrl+Shift+I need native UI → host-keymap.ts.
 */
export function targetFromCtx(ctx: Ctx): CommandTarget {
  return {
    action: (fn) => fn(ctx),
    view: () => ctx.get(editorViewCtx),
  }
}

export const formatKeymap = $prose((ctx) => {
  const run = (fn: (t: CommandTarget) => void): Command => {
    return () => {
      fn(targetFromCtx(ctx))
      return true
    }
  }
  const bindings: Record<string, Command> = {
    'Mod-1': run((t) => cmd.setHeading(t, 1)),
    'Mod-2': run((t) => cmd.setHeading(t, 2)),
    'Mod-3': run((t) => cmd.setHeading(t, 3)),
    'Mod-4': run((t) => cmd.setHeading(t, 4)),
    'Mod-5': run((t) => cmd.setHeading(t, 5)),
    'Mod-6': run((t) => cmd.setHeading(t, 6)),
    'Mod-Shift-0': run((t) => cmd.setHeading(t, 0)),
    'Mod-b': run((t) => cmd.toggleMark(t, 'bold')),
    'Mod-i': run((t) => cmd.toggleMark(t, 'italic')),
    'Mod-Shift-x': run((t) => cmd.toggleMark(t, 'strike')),
    'Mod-e': run((t) => cmd.toggleMark(t, 'code')),
    'Mod-Shift-h': run((t) => cmd.toggleMark(t, 'highlight')),
    'Mod-Shift-8': run((t) => cmd.setList(t, 'bullet')),
    'Mod-Shift-7': run((t) => cmd.setList(t, 'ordered')),
    'Mod-Shift-9': run((t) => cmd.setList(t, 'task')),
    'Mod-Shift-q': run((t) => cmd.toggleBlockquote(t)),
    'Mod-Shift-c': run((t) => cmd.insertCodeBlock(t)),
    'Mod-Shift--': run((t) => cmd.insertHr(t)),
    'Mod-Shift-_': run((t) => cmd.insertHr(t)),
    'Mod-t': run((t) => cmd.insertTable(t, 3, 3)),
    'Mod-Shift-m': run((t) => cmd.insertMath(t, false)),
    'Mod-\\': run((t) => cmd.clearFormat(t)),
  }
  return keymap(bindings)
})
