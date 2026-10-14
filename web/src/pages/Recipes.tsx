import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { get } from '../lib/api'
import { useSession } from '../lib/session'
import { useDebounced, usePageTitle } from '../lib/hooks'
import type { Catalogo, Page, ReceitaResumo } from '../lib/types'
import { Button, Empty, ErrorNote, PageTitle, Select, Spinner, TextInput } from '../components/ui'
import { RecipeCard } from '../components/RecipeCard'

export function RecipesPage() {
  usePageTitle('Receitas')
  const { pantry, canEdit } = useSession()
  const [params, setParams] = useSearchParams()
  const [search, setSearch] = useState(params.get('busca') ?? '')
  const busca = useDebounced(search)
  const compat = params.get('compat') ?? ''
  const ordem = params.get('ordem') ?? ''
  const catalogo = params.get('catalogo') ?? ''
  const favoritas = params.get('favoritas') === '1'
  const status = params.get('status') ?? ''

  const catalogs = useQuery({ queryKey: ['catalogos', pantry?.id], queryFn: () => get<Catalogo[]>('/catalogos'), enabled: !!pantry })
  const query = useInfiniteQuery({
    queryKey: ['receitas', pantry?.id, busca, compat, ordem, catalogo, favoritas, status],
    initialPageParam: '' as string,
    queryFn: ({ pageParam }) => {
      const q = new URLSearchParams({ limite: '24' })
      if (busca) q.set('busca', busca)
      if (compat) q.set('compat', compat)
      if (ordem) q.set('ordem', ordem)
      if (catalogo) q.set('catalogoId', catalogo)
      if (favoritas) q.set('favoritas', 'true')
      if (status) q.set('status', status)
      if (pageParam) q.set('cursor', pageParam)
      return get<Page<ReceitaResumo>>(`/receitas?${q}`)
    },
    getNextPageParam: (l) => l.proximoCursor ?? undefined,
    enabled: !!pantry,
  })
  const items = query.data?.pages.flatMap((p) => p.itens) ?? []

  const setParam = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    setParams(next, { replace: true })
  }

  return (
    <>
      <PageTitle
        actions={
          <>
            <Link to="/catalogos" className="inline-flex min-h-11 items-center rounded-lg border border-slate-400 bg-white px-4 text-sm font-semibold">Catálogos</Link>
            {canEdit && <Link to="/receitas/importar" className="inline-flex min-h-11 items-center rounded-lg border border-slate-400 bg-white px-4 text-sm font-semibold">Importar / buscar</Link>}
            {canEdit && <Link to="/receitas/nova" className="inline-flex min-h-11 items-center rounded-lg bg-brand-600 px-4 text-sm font-semibold text-white">+ Nova receita</Link>}
          </>
        }
      >
        Receitas
      </PageTitle>
      <form role="search" onSubmit={(e) => e.preventDefault()} className="mb-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <TextInput label="Buscar receita" type="search" value={search} onChange={(e) => setSearch(e.target.value)} />
        <Select label="Posso preparar?" value={compat} onChange={(e) => setParam('compat', e.target.value)}>
          <option value="">Todas</option>
          <option value="pode">Pode preparar</option>
          <option value="faltam">Faltam ingredientes</option>
          <option value="verificar">Verificar ingredientes</option>
          <option value="rascunho">Rascunhos</option>
        </Select>
        <Select label="Catálogo" value={catalogo} onChange={(e) => setParam('catalogo', e.target.value)}>
          <option value="">Todos</option>
          {catalogs.data?.map((c) => <option key={c.id} value={c.id}>{c.nome}{c.visibilidade === 'pessoal' ? ' (pessoal)' : ''}</option>)}
        </Select>
        <Select label="Ordenar por" value={ordem} onChange={(e) => setParam('ordem', e.target.value)}>
          <option value="">Mais recentes</option>
          <option value="compat">Mais prontas primeiro</option>
          <option value="titulo">Título (A–Z)</option>
        </Select>
      </form>
      <div className="mb-4 flex flex-wrap items-center gap-4">
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="size-5" checked={favoritas} onChange={(e) => setParam('favoritas', e.target.checked ? '1' : '')} />
          Somente favoritas
        </label>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="size-5" checked={status === 'rascunho'} onChange={(e) => setParam('status', e.target.checked ? 'rascunho' : '')} />
          Somente rascunhos
        </label>
      </div>

      <ErrorNote error={query.error} onRetry={() => query.refetch()} />
      {query.isLoading && <Spinner />}
      {!query.isLoading && items.length === 0 && !query.error && (
        <Empty title="Nenhuma receita encontrada">
          {canEdit ? <>Crie uma receita, <Link className="underline" to="/receitas/importar">importe por link ou busque</Link>.</> : 'Peça a um editor para cadastrar receitas.'}
        </Empty>
      )}
      <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {items.map((r) => <li key={r.id}><RecipeCard recipe={r} pantryId={pantry!.id} /></li>)}
      </ul>
      {query.hasNextPage && (
        <div className="mt-4 text-center"><Button variant="secondary" busy={query.isFetchingNextPage} onClick={() => query.fetchNextPage()}>Carregar mais</Button></div>
      )}
    </>
  )
}
