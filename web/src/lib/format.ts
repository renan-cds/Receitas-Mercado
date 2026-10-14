import type { Estado, LineStatus, Unidade } from './types'

const number = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 })
export const formatNumber = (n: number) => number.format(n)

export const FALLBACK_UNITS: Unidade[] = [
  { codigo: 'G', nome: 'grama', abreviacao: 'g', dimensao: 'massa', aproximada: false },
  { codigo: 'KG', nome: 'quilograma', abreviacao: 'kg', dimensao: 'massa', aproximada: false },
  { codigo: 'ML', nome: 'mililitro', abreviacao: 'ml', dimensao: 'volume', aproximada: false },
  { codigo: 'L', nome: 'litro', abreviacao: 'L', dimensao: 'volume', aproximada: false },
  { codigo: 'UN', nome: 'unidade', abreviacao: 'un', dimensao: 'unidade', aproximada: false },
]

const abbreviations: Record<string, string> = {
  G: 'g', KG: 'kg', ML: 'ml', L: 'L', UN: 'un', XICARA: 'xíc', COLHER_SOPA: 'col. sopa', COLHER_CHA: 'col. chá',
}
export const unitLabel = (code: string | null | undefined) => (code ? abbreviations[code] ?? code.toLowerCase() : '')

export function formatQuantity(quantity: number | null | undefined, unit: string | null | undefined) {
  if (quantity === null || quantity === undefined) return 'a gosto'
  return `${formatNumber(quantity)} ${unitLabel(unit)}`.trim()
}

export function formatDate(iso: string | null | undefined) {
  if (!iso) return 'sem validade'
  const [y, m, d] = iso.slice(0, 10).split('-')
  return `${d}/${m}/${y}`
}

export function formatDateTime(iso: string) {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
}

export const estadoInfo: Record<Estado, { label: string; tone: 'ok' | 'warn' | 'bad' | 'muted'; icon: string }> = {
  pode_preparar: { label: 'Pode preparar', tone: 'ok', icon: '✓' },
  faltam_ingredientes: { label: 'Faltam ingredientes', tone: 'bad', icon: '✕' },
  verificar_ingredientes: { label: 'Verificar ingredientes', tone: 'warn', icon: '!' },
  rascunho: { label: 'Rascunho', tone: 'muted', icon: '✎' },
}

export const lineStatusInfo: Record<LineStatus, { label: string; tone: 'ok' | 'warn' | 'bad' | 'muted' }> = {
  ok: { label: 'Em estoque', tone: 'ok' },
  approximate: { label: 'Estoque (conversão aproximada)', tone: 'warn' },
  insufficient: { label: 'Falta', tone: 'bad' },
  uncertain: { label: 'Vínculo não confirmado', tone: 'warn' },
  no_quantity: { label: 'Sem quantidade', tone: 'muted' },
  incompatible_unit: { label: 'Unidade incompatível', tone: 'warn' },
}

export const movementLabels: Record<string, string> = {
  entrada: 'Entrada', consumo: 'Consumo', perda: 'Perda', correcao: 'Correção', baixa_preparo: 'Preparo', entrada_compra: 'Compra',
}

export const roleLabels = { admin: 'Administrador', editor: 'Editor', leitor: 'Leitor' } as const

export const originLabels: Record<string, string> = { manual: 'Manual', url: 'Importada por URL', busca: 'TheMealDB', ia: 'Sugestão de IA' }
