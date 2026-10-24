import { beforeEach, describe, expect, it, vi } from 'vitest'

const store: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (k: string) => store[k] ?? null,
  setItem: (k: string, v: string) => { store[k] = v },
  removeItem: (k: string) => { delete store[k] },
})

const { rememberPantry, getActivePantry, setActivePantry, isRetryable, ApiError } = await import('./api')

describe('rememberPantry', () => {
  beforeEach(() => {
    for (const key of Object.keys(store)) delete store[key]
    setActivePantry(null)
  })

  it('sets the pantry header before screens fetch', () => {
    rememberPantry({
      id: 'u1', email: 'a@b.c', nome: 'Ana', despensaAtivaId: 'd2',
      despensas: [
        { id: 'd1', nome: 'Casa', papel: 'leitor' },
        { id: 'd2', nome: 'Sítio', papel: 'admin' },
      ],
    })
    expect(getActivePantry()).toBe('d2')
    expect(store['rm.despensa']).toBe('d2')
  })

  it('keeps the pantry already chosen in localStorage', () => {
    store['rm.despensa'] = 'd1'
    rememberPantry({
      id: 'u1', email: 'a@b.c', nome: 'Ana', despensaAtivaId: 'd2',
      despensas: [
        { id: 'd1', nome: 'Casa', papel: 'leitor' },
        { id: 'd2', nome: 'Sítio', papel: 'admin' },
      ],
    })
    expect(getActivePantry()).toBe('d1')
  })
})

describe('isRetryable', () => {
  it('retries connection and missing-pantry errors, not 401', () => {
    expect(isRetryable(new ApiError(0, 'SEM_CONEXAO', 'x'))).toBe(true)
    expect(isRetryable(new ApiError(400, 'DESPENSA_OBRIGATORIA', 'x'))).toBe(true)
    expect(isRetryable(new ApiError(503, 'ERRO', 'x'))).toBe(true)
    expect(isRetryable(new ApiError(401, 'NAO_AUTENTICADO', 'x'))).toBe(false)
    expect(isRetryable(new ApiError(404, 'NAO_ENCONTRADO', 'x'))).toBe(false)
  })
})
