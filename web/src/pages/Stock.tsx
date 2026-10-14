import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useInfiniteQuery } from '@tanstack/react-query'
import { get } from '../lib/api'
import { useSession } from '../lib/session'
import { useCategories, useDebounced, usePageTitle } from '../lib/hooks'
import type { IngredienteItem, Page } from '../lib/types'
import { Badge, Button, Empty, ErrorNote, PageTitle, Select, Spinner, TextInput } from '../components/ui'
import { IngredientFormModal } from '../components/IngredientForm'
import { formatDate, formatQuantity, unitLabel } from '../lib/format'

const situacoes = [
  ['', 'Todos'], ['em_estoque', 'Em estoque'], ['zerado', 'Sem estoque'], ['baixo', 'Abaixo do mínimo'], ['vencendo', 'Vencendo'], ['vencido', 'Vencidos'],
] as const

export function StockPage() {
  usePageTitle('Estoque')
  const { pantry, canEdit } = useSession()
  const [params, setParams] = useSearchParams()
  const [search, setSearch] = useState('')
  const debounced = useDebounced(search)
  const situacao = params.get('situacao') ?? ''
  const categoria = params.get('categoria') ?? ''
  const archived = params.get('arquivados') === '1'
  const [creating, setCreating] = useState(false)
  const categories = useCategories(pantry?.id)

  const query = useInfiniteQuery({
    queryKey: ['ingredientes', pantry?.id, debounced, situacao, categoria, archived],
    initialPageParam: '' as string,
    queryFn: ({ pageParam }) => {
      const q = new URLSearchParams({ limite: '40' })
      if (debounced) q.set('busca', debounced)
      if (situacao) q.set('situacao', situacao)
      if (categoria) q.set('categoriaId', categoria)
      if (archived) q.set('arquivados', 'true')
      if (pageParam) q.set('cursor', pageParam)
      return get<Page<IngredienteItem>>(`/ingredientes?${q}`)
    },
    getNextPageParam: (last) => last.proximoCursor ?? undefined,
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
      <PageTitle actions={canEdit && <Button onClick={() => setCreating(true)}>+ Novo ingrediente</Button>}>Estoque</PageTitle>
      <form role="search" onSubmit={(e) => e.preventDefault()} className="mb-4 grid gap-3 sm:grid-cols-3">
        <TextInput label="Buscar ingrediente" type="search" value={search} onChange={(e) => setSearch(e.target.value)} />
        <Select label="Situação" value={situacao} onChange={(e) => setParam('situacao', e.target.value)}>
          {situacoes.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
        </Select>
        <Select label="Categoria" value={categoria} onChange={(e) => setParam('categoria', e.target.value)}>
          <option value="">Todas</option>
          {categories.data?.map((c) => <option key={c.id} value={c.id}>{c.nome}</option>)}
        </Select>
      </form>
      <label className="mb-4 flex min-h-11 items-center gap-2 text-sm">
        <input type="checkbox" className="size-5" checked={archived} onChange={(e) => setParam('arquivados', e.target.checked ? '1' : '')} />
        Mostrar arquivados
      </label>

      <ErrorNote error={query.error} onRetry={() => query.refetch()} />
      {query.isLoading && <Spinner />}
      {!query.isLoading && items.length === 0 && !query.error && (
        <Empty title="Nenhum ingrediente encontrado">
          {canEdit ? 'Cadastre seu primeiro ingrediente para começar a controlar o estoque.' : 'Peça a um editor para cadastrar ingredientes.'}
        </Empty>
      )}
      <ul className="divide-y divide-slate-200 overflow-hidden rounded-xl border border-slate-300 bg-white">
        {items.map((i) => (
          <li key={i.id}>
            <Link to={`/estoque/${i.id}`} className="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-3 hover:bg-brand-50">
              <span className="min-w-40 flex-1">
                <span className="block font-semibold">{i.nome}</span>
                <span className="text-xs text-muted">{i.categoriaNome}{i.localArmazenamento ? ` · ${i.localArmazenamento}` : ''}</span>
              </span>
              <span className="text-right">
                <span className="block text-lg font-bold">{formatQuantity(i.quantidadeTotal, i.unidadeReferencia)}</span>
                {i.quantidadeMinima > 0 && <span className="text-xs text-muted">mín. {i.quantidadeMinima} {unitLabel(i.unidadeReferencia)}</span>}
              </span>
              <span className="flex min-w-36 flex-wrap justify-end gap-1">
                {i.arquivado && <Badge tone="muted">Arquivado</Badge>}
                {i.quantidadeTotal <= 0 && <Badge tone="muted">Sem estoque</Badge>}
                {i.abaixoDoMinimo && i.quantidadeTotal > 0 && <Badge tone="warn" icon="!">Abaixo do mínimo</Badge>}
                {i.situacaoValidade === 'vencido' && <Badge tone="bad" icon="✕">Vencido {formatDate(i.proximaValidade)}</Badge>}
                {i.situacaoValidade === 'vencendo' && <Badge tone="warn" icon="!">Vence {formatDate(i.proximaValidade)}</Badge>}
              </span>
            </Link>
          </li>
        ))}
      </ul>
      {query.hasNextPage && (
        <div className="mt-4 text-center">
          <Button variant="secondary" busy={query.isFetchingNextPage} onClick={() => query.fetchNextPage()}>Carregar mais</Button>
        </div>
      )}
      {creating && <IngredientFormModal open onClose={() => setCreating(false)} />}
    </>
  )
}
