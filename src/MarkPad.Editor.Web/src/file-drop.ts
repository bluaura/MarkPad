import { bridge } from './bridge'

const TEXT_EXT = /\.(md|markdown|txt)$/i

/**
 * Files dropped onto the editor page. WebView2 gives the page file *contents* but never paths, so
 * markdown/text files are forwarded to the host as `files.dropped` (opened as untitled copies);
 * images and everything else fall through to Crepe's own drop handling (image upload → asset.save).
 */
export function installFileDrop(target: Document = document): () => void {
  const onDrop = (e: DragEvent): void => {
    const files = Array.from(e.dataTransfer?.files ?? []).filter((f) => TEXT_EXT.test(f.name))
    if (files.length === 0) return
    e.preventDefault()
    e.stopPropagation()
    void Promise.all(files.map(async (f) => ({ name: f.name, text: await f.text() }))).then((payload) => {
      bridge.emit('files.dropped', { files: payload })
    })
  }
  const onDragOver = (e: DragEvent): void => {
    const types = Array.from(e.dataTransfer?.items ?? [])
    if (types.some((i) => i.kind === 'file')) e.preventDefault()
  }
  target.addEventListener('drop', onDrop, { capture: true })
  target.addEventListener('dragover', onDragOver, { capture: true })
  return () => {
    target.removeEventListener('drop', onDrop, { capture: true })
    target.removeEventListener('dragover', onDragOver, { capture: true })
  }
}
