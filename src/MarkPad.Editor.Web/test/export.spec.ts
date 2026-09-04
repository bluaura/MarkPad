import { describe, expect, it } from 'vitest'
import { renderHtml } from '../src/export/render-html'

describe('renderHtml (F-EXP-01)', () => {
  it('renders GFM with highlighted code, math and task lists', async () => {
    const md = [
      '# Title',
      '',
      'Text with **bold** and $E=mc^2$.',
      '',
      '- [x] done',
      '',
      '```js',
      'const x = 1 // c',
      '```',
      '',
      '| a | b |',
      '|---|---|',
      '| 1 | 2 |',
      '',
    ].join('\n')
    const r = await renderHtml(md, { inlineImages: false, theme: 'light' })
    expect(r.html).toContain('<h1>Title</h1>')
    expect(r.html).toContain('<strong>bold</strong>')
    expect(r.html).toContain('class="katex"')
    expect(r.hasMath).toBe(true)
    expect(r.html).toContain('tok-keyword')
    expect(r.html).toContain('<table>')
    expect(r.html).toContain('type="checkbox"')
    expect(r.css).toContain('.tok-keyword')
  })

  it('inlines local images through the reader and leaves remote ones alone', async () => {
    const md = '![a](assets/a.png) ![b](https://x/y.png)'
    const r = await renderHtml(md, {
      inlineImages: true,
      theme: 'dark',
      readImage: async (src) => (src === 'assets/a.png' ? 'data:image/png;base64,AAAA' : null),
    })
    expect(r.html).toContain('src="data:image/png;base64,AAAA"')
    expect(r.html).toContain('src="https://x/y.png"')
  })

  it('renders ==highlight== as <mark> only when enabled', async () => {
    const on = await renderHtml('a ==b== c', { inlineImages: false, theme: 'light', highlight: true })
    expect(on.html).toContain('<mark>b</mark>')
    const off = await renderHtml('a ==b== c', { inlineImages: false, theme: 'light' })
    expect(off.html).toContain('a ==b== c')
  })

  it('keeps inline HTML', async () => {
    const r = await renderHtml('<details><summary>x</summary>\n\nbody\n\n</details>', { inlineImages: false, theme: 'light' })
    expect(r.html).toContain('<details>')
    expect(r.html).toContain('<summary>x</summary>')
  })
})
