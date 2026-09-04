import { Plugin, PluginKey } from '@milkdown/kit/prose/state'
import { $prose } from '@milkdown/kit/utils'
import { bridge } from '../bridge'

/**
 * PRD F-VIEW-04: Ctrl+click on a link asks the host to open it (browser / new tab / shell);
 * a plain click just places the cursor. The host decides based on the href (see EditorHost link.open).
 */
export const linkClickPlugin = $prose(() => {
  return new Plugin({
    key: new PluginKey('markpad-link-click'),
    props: {
      handleClick(view, pos, event) {
        if (!event.ctrlKey && !event.metaKey) return false
        const $pos = view.state.doc.resolve(pos)
        const linkType = view.state.schema.marks['link']
        if (!linkType) return false
        const mark = linkType.isInSet($pos.marks()) ?? linkType.isInSet($pos.nodeAfter?.marks ?? [])
        if (!mark) return false
        const href = String(mark.attrs['href'] ?? '')
        if (!href) return false
        event.preventDefault()
        void bridge.call('link.open', { href }).catch((err) => bridge.log('warn', `link.open failed: ${String(err)}`))
        return true
      },
    },
  })
})
