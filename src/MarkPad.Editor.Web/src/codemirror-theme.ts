import { HighlightStyle, syntaxHighlighting } from '@codemirror/language'
import type { Extension } from '@codemirror/state'
import { EditorView, lineNumbers } from '@codemirror/view'
import { tags as t } from '@lezer/highlight'

/**
 * CodeMirror theme driven by CSS custom properties (see styles/editor.css), so code blocks follow the
 * app's light/dark mode without re-creating editors (PRD F-VIEW-08, T-33). Replaces Crepe's oneDark.
 */
const chrome = EditorView.theme({
  '&': { backgroundColor: 'var(--mp-code-bg)', color: 'var(--mp-code-fg)' },
  '.cm-content': { fontFamily: 'var(--mp-font-mono)', fontSize: '0.92em', caretColor: 'var(--mp-accent)' },
  '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--mp-accent)' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection': { backgroundColor: 'var(--mp-selected)' },
  '.cm-activeLine': { backgroundColor: 'transparent' },
  '.cm-gutters': { backgroundColor: 'transparent', color: 'var(--mp-fg-muted)', border: 'none', fontFamily: 'var(--mp-font-mono)' },
  '.cm-activeLineGutter': { backgroundColor: 'transparent' },
  '.cm-lineNumbers .cm-gutterElement': { minWidth: '2.5em', paddingRight: '0.8em' },
})

const highlight = HighlightStyle.define([
  { tag: [t.keyword, t.modifier, t.operatorKeyword, t.controlKeyword, t.definitionKeyword], color: 'var(--mp-syn-keyword)' },
  { tag: [t.string, t.special(t.string), t.character], color: 'var(--mp-syn-string)' },
  { tag: [t.number, t.integer, t.float, t.bool, t.null, t.atom], color: 'var(--mp-syn-number)' },
  { tag: [t.comment, t.lineComment, t.blockComment, t.docComment], color: 'var(--mp-syn-comment)', fontStyle: 'italic' },
  { tag: [t.function(t.variableName), t.function(t.propertyName), t.labelName], color: 'var(--mp-syn-function)' },
  { tag: [t.typeName, t.className, t.namespace, t.macroName], color: 'var(--mp-syn-type)' },
  { tag: [t.variableName, t.propertyName, t.attributeName], color: 'var(--mp-syn-variable)' },
  { tag: [t.tagName, t.angleBracket], color: 'var(--mp-syn-tag)' },
  { tag: [t.operator, t.punctuation, t.bracket, t.separator], color: 'var(--mp-syn-operator)' },
  { tag: [t.regexp, t.escape, t.url, t.link], color: 'var(--mp-syn-regexp)' },
  { tag: [t.meta, t.processingInstruction, t.annotation], color: 'var(--mp-syn-meta)' },
  { tag: t.heading, fontWeight: 'bold', color: 'var(--mp-syn-keyword)' },
  { tag: t.emphasis, fontStyle: 'italic' },
  { tag: t.strong, fontWeight: 'bold' },
  { tag: t.invalid, color: 'var(--mp-syn-invalid)' },
])

export const markPadCodeTheme: Extension = [chrome, syntaxHighlighting(highlight)]

/** Line numbers are always present; visibility is toggled with the `mp-hide-gutters` class (context bar). */
export const markPadCodeExtensions: Extension[] = [lineNumbers()]
