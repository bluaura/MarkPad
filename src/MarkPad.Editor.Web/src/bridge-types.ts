// Bridge message contracts (ARCHITECTURE.md §4). Keep in sync with MarkPad.App/Bridge/BridgeMessages.cs.

// ---- transport envelope ----
export interface ReqMsg {
  t: 'req'
  id: number
  m: string
  p?: unknown
}
export interface ResOk {
  t: 'res'
  id: number
  ok: true
  r?: unknown
}
export interface ResErr {
  t: 'res'
  id: number
  ok: false
  e: { code: string; msg: string }
}
export interface EvtMsg {
  t: 'evt'
  m: string
  p?: unknown
}
export type Msg = ReqMsg | ResOk | ResErr | EvtMsg

// ---- settings (subset of settings.json that the editor needs) ----
export type ThemeMode = 'light' | 'dark'

export interface EditorTheme {
  mode: ThemeMode
  fontFamily: string
  fontSize: number
  lineHeight: number
  maxWidth: number
  zoom: number
  accent?: string
}

export interface MarkdownStyle {
  emphasis: '*' | '_'
  bullet: '-' | '*' | '+'
  listIndent: number
  extHighlight: boolean
  extMath: boolean
  extMermaid: boolean
  frontMatter: boolean
}

export interface EditorSettings {
  theme: EditorTheme
  markdown: MarkdownStyle
  allowRemoteImages: boolean
}

// ---- host → web ----
export interface DocLoadParams {
  text: string
  path?: string | null
  readonly?: boolean
  settings: EditorSettings
}
export interface DocSerializeParams {
  original: string
}
export interface DocSerializeResult {
  text: string
  changedBlocks: number[]
}
export interface FormatToggleParams {
  mark: 'bold' | 'italic' | 'strike' | 'code' | 'highlight'
}
export interface FormatHeadingParams {
  level: number // 0 = paragraph, 1..6
}
export interface FormatListParams {
  type: 'bullet' | 'ordered' | 'task'
}
export interface InsertCodeBlockParams {
  lang?: string
}
export interface InsertTableParams {
  rows: number
  cols: number
}
export interface InsertLinkParams {
  text?: string
  href: string
}
export interface InsertImageParams {
  src: string
  alt?: string
}
export interface InsertTextParams {
  text: string
}
export interface InsertMathParams {
  display: boolean
}
export interface SetReadonlyParams {
  value: boolean
}
export interface FindParams {
  query: string
  caseSensitive?: boolean
  wholeWord?: boolean
}
export interface FindReplaceParams {
  replacement: string
}
export interface FindResult {
  count: number
  index: number
}
export interface ExportRenderHtmlParams {
  inlineImages: boolean
  theme: ThemeMode
}
export interface ExportRenderHtmlResult {
  html: string
  css: string
  hasMath: boolean
}
export interface ExportPreparePrintParams {
  theme?: ThemeMode
  pageSize?: string
}
export interface AssetReadParams {
  src: string
}
export interface AssetReadResult {
  dataUrl: string | null
}
export interface RewriteAssetPathsParams {
  map: Record<string, string>
}
export interface RewriteAssetPathsResult {
  count: number
}

// ---- web → host events ----
export interface ReadyEvent {
  version: string
}
export interface ChangedEvent {
  dirty: boolean
  words: number
  chars: number
  line: number
}
export interface SelectionContext {
  bold: boolean
  italic: boolean
  strike: boolean
  code: boolean
  highlight: boolean
  heading: number
  list: 'bullet' | 'ordered' | 'task' | null
  blockquote: boolean
  inTable: boolean
  inCode: boolean
  codeLang: string | null
  link: string | null
  /** Non-empty text selection (the link flyout hides its text box when true). */
  hasSelection: boolean
}
export interface OutlineItem {
  level: number
  text: string
  pos: number
}
export interface OutlineEvent {
  items: OutlineItem[]
  active: number
}
export interface CopyRichResult {
  html: string
  text: string
  markdown: string
}
export interface ShortcutEvent {
  key: string // e.g. "ctrl+s", "ctrl+shift+s", "ctrl+alt+3", "f5"
}
export interface LogEvent {
  level: 'debug' | 'info' | 'warn' | 'error'
  msg: string
}

// ---- web → host requests ----
export interface AssetSaveParams {
  bytesBase64: string
  mime: string
  suggestedName?: string
}
export interface AssetSaveResult {
  relPath: string
}
export interface LinkOpenParams {
  href: string
}
export interface ImageResolveParams {
  src: string
}
export interface ImageResolveResult {
  url: string
}
