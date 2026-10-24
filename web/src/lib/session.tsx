import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, get, isRetryable, post, rememberPantry, setActivePantry } from './api'
import type { DespensaResumo, Me, Papel } from './types'

interface Session {
  me: Me
  pantry: DespensaResumo | null
  role: Papel
  canEdit: boolean
  isAdmin: boolean
  switchPantry: (id: string) => void
  logout: () => Promise<void>
  refresh: () => Promise<void>
}

const Ctx = createContext<Session | null>(null)
export const useSession = () => {
  const value = useContext(Ctx)
  if (!value) throw new Error('useSession fora do provedor')
  return value
}

export function SessionProvider({ children, onAnonymous }: {
  children: (me: Me | null, loading: boolean, error?: unknown, retry?: () => void) => ReactNode
  onAnonymous?: () => void
}) {
  const client = useQueryClient()
  const query = useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      const me = await get<Me>('/auth/me')
      rememberPantry(me)
      return me
    },
    retry: (count, error) => !(error instanceof ApiError && error.status === 401) && isRetryable(error) && count < 4,
    retryDelay: (i) => Math.min(500 * 2 ** i, 8000),
    staleTime: 60_000,
  })
  const [chosen, setChosen] = useState<string | null>(localStorage.getItem('rm.despensa'))
  const me = query.data ?? null

  const pantry = useMemo(() => {
    if (!me) return null
    return me.despensas.find((d) => d.id === chosen) ?? me.despensas.find((d) => d.id === me.despensaAtivaId) ?? me.despensas[0] ?? null
  }, [me, chosen])

  useEffect(() => { if (pantry) setActivePantry(pantry.id) }, [pantry?.id])
  useEffect(() => {
    if (query.isError && !me && errorStatus(query.error) === 401) onAnonymous?.()
  }, [query.isError, me, query.error, onAnonymous])

  useEffect(() => {
    const wake = () => {
      if (document.visibilityState === 'visible') void client.refetchQueries({ type: 'active' })
    }
    document.addEventListener('visibilitychange', wake)
    window.addEventListener('online', wake)
    return () => {
      document.removeEventListener('visibilitychange', wake)
      window.removeEventListener('online', wake)
    }
  }, [client])

  const switchPantry = useCallback((id: string) => {
    setActivePantry(id)
    setChosen(id)
    client.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' })
  }, [client])

  const logout = useCallback(async () => {
    await post('/auth/sair').catch(() => undefined)
    setActivePantry(null)
    client.clear()
    window.location.assign('/entrar')
  }, [client])

  const refresh = useCallback(async () => { await client.invalidateQueries({ queryKey: ['me'] }) }, [client])

  const value = useMemo<Session | null>(() => {
    if (!me) return null
    const role = pantry?.papel ?? 'leitor'
    return { me, pantry, role, canEdit: role !== 'leitor', isAdmin: role === 'admin', switchPantry, logout, refresh }
  }, [me, pantry, switchPantry, logout, refresh])

  if (!value) return <>{children(null, query.isPending, query.error, () => { void query.refetch() })}</>
  return <Ctx.Provider value={value}>{children(me, false)}</Ctx.Provider>
}

function errorStatus(error: unknown) {
  return error instanceof ApiError ? error.status : 0
}
