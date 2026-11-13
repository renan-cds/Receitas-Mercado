import type { Me } from './types'

export class ApiError extends Error {
  status: number
  code: string
  details: unknown
  constructor(status: number, code: string, message: string, details?: unknown) {
    super(message)
    this.status = status
    this.code = code
    this.details = details
  }
}

let activePantry: string | null = localStorage.getItem('rm.despensa')

export function setActivePantry(id: string | null) {
  activePantry = id
  if (id) localStorage.setItem('rm.despensa', id)
  else localStorage.removeItem('rm.despensa')
}
export function getActivePantry() { return activePantry }

/** Define o cabeçalho da despensa na hora, antes das telas pedirem dados. */
export function rememberPantry(me: Me | null | undefined) {
  if (!me?.despensas.length) return
  const chosen = localStorage.getItem('rm.despensa')
  const pantry = me.despensas.find((d) => d.id === chosen)
    ?? me.despensas.find((d) => d.id === me.despensaAtivaId)
    ?? me.despensas[0]
  if (pantry) setActivePantry(pantry.id)
}

export function isRetryable(error: unknown) {
  if (!(error instanceof ApiError)) return true
  if (error.status === 0 || error.code === 'SEM_CONEXAO' || error.code === 'DESPENSA_OBRIGATORIA') return true
  if (error.status === 429 || error.status >= 500) return true
  return false
}

function delay(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

export function newKey() {
  return crypto.randomUUID()
}

type Options = { method?: string; body?: unknown; key?: string; pantry?: boolean; signal?: AbortSignal; form?: FormData }

export async function api<T = unknown>(path: string, opts: Options = {}): Promise<T> {
  const headers: Record<string, string> = { 'X-Requested-With': 'rm-web', Accept: 'application/json' }
  if (opts.pantry !== false && activePantry) headers['X-Despensa-Id'] = activePantry
  if (opts.key) headers['Idempotency-Key'] = opts.key
  let body: BodyInit | undefined
  if (opts.form) body = opts.form
  else if (opts.body !== undefined) {
    headers['Content-Type'] = 'application/json'
    body = JSON.stringify(opts.body)
  }
  let response: Response
  const send = () => fetch(`/api${path}`, { method: opts.method ?? 'GET', headers, body, credentials: 'same-origin', signal: opts.signal })
  try {
    try {
      response = await send()
    } catch (error) {
      if ((error as Error).name === 'AbortError') throw error
      // PWA/iOS e host que dorme: o primeiro fetch após reabrir costuma falhar.
      await delay(400)
      response = await send()
    }
  } catch (error) {
    if ((error as Error).name === 'AbortError') throw error
    throw new ApiError(0, 'SEM_CONEXAO', 'Sem conexão com o servidor. Verifique sua internet e tente novamente.')
  }
  const text = await response.text()
  let data: unknown = null
  if (text) {
    try { data = JSON.parse(text) } catch { data = null }
  }
  if (!response.ok) {
    const e = (data ?? {}) as { codigo?: string; mensagem?: string; detalhes?: unknown }
    throw new ApiError(response.status, e.codigo ?? 'ERRO', e.mensagem ?? 'Algo deu errado. Tente novamente.', e.detalhes)
  }
  return data as T
}

export const get = <T,>(path: string) => api<T>(path)
export const post = <T,>(path: string, body: unknown = {}, key?: string) => api<T>(path, { method: 'POST', body, key })
export const put = <T,>(path: string, body: unknown = {}) => api<T>(path, { method: 'PUT', body })
export const patch = <T,>(path: string, body: unknown) => api<T>(path, { method: 'PATCH', body })
export const del = <T,>(path: string, body?: unknown) => api<T>(path, { method: 'DELETE', body })

export function fileUrl(pantryId: string | null | undefined, fileId: string | null | undefined) {
  return pantryId && fileId ? `/api/despensas/${pantryId}/arquivos/${fileId}` : null
}

export function fieldErrors(error: unknown): Record<string, string> {
  if (error instanceof ApiError && error.code === 'VALIDACAO' && error.details && typeof error.details === 'object') {
    return Object.fromEntries(Object.entries(error.details as Record<string, string[]>).map(([k, v]) => [k, v.join(' ')]))
  }
  return {}
}
