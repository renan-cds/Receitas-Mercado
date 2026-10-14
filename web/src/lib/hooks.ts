import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { get } from './api'
import { FALLBACK_UNITS } from './format'
import type { Categoria, GrupoEquivalencia, Unidade } from './types'

export function useDebounced<T>(value: T, delay = 300) {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delay)
    return () => clearTimeout(t)
  }, [value, delay])
  return debounced
}

export function useOnline() {
  const [online, setOnline] = useState(navigator.onLine)
  useEffect(() => {
    const on = () => setOnline(true)
    const off = () => setOnline(false)
    window.addEventListener('online', on)
    window.addEventListener('offline', off)
    return () => { window.removeEventListener('online', on); window.removeEventListener('offline', off) }
  }, [])
  return online
}

export function useUnits() {
  const q = useQuery({ queryKey: ['unidades'], queryFn: () => get<Unidade[]>('/unidades'), staleTime: Infinity })
  return q.data ?? FALLBACK_UNITS
}

export function useCategories(pantryId: string | undefined) {
  return useQuery({ queryKey: ['categorias', pantryId], queryFn: () => get<Categoria[]>('/categorias'), enabled: !!pantryId })
}

export function useGroups(pantryId: string | undefined) {
  return useQuery({ queryKey: ['grupos', pantryId], queryFn: () => get<GrupoEquivalencia[]>('/grupos-equivalencia'), enabled: !!pantryId })
}

export function usePageTitle(title: string) {
  useEffect(() => { document.title = `${title} · Receitas Mercado` }, [title])
}
