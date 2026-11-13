import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { del, fileUrl, get, patch, post, put } from '../lib/api'
import { useSession } from '../lib/session'
import { usePageTitle } from '../lib/hooks'
import type { Catalogo, CatalogoDetalhe, Page, ReceitaResumo } from '../lib/types'
import { Badge, Button, ConfirmDialog, Empty, ErrorNote, Modal, PageTitle, Select, Spinner, TextArea, TextInput, useToast } from '../components/ui'

type Visibilidade = 'pessoal' | 'despensa'

function CatalogFormModal({ catalog, onClose }: { catalog?: CatalogoDetalhe; onClose: () => void }) {
  const { pantry, isAdmin } = useSession()
  const client = useQueryClient()
  const { notify } = useToast()
  const [nome, setNome] = useState(catalog?.nome ?? '')
  const [descricao, setDescricao] = useState(catalog?.descricao ?? '')
  const [visibilidade, setVisibilidade] = useState<Visibilidade>(catalog?.visibilidade ?? 'pessoal')
  const navigate = useNavigate()

  const save = useMutation({
    mutationFn: () => {
      const body = { nome, descricao: descricao || null, visibilidade }
      return catalog ? patch<Catalogo>(`/catalogos/${catalog.id}`, body) : post<{ id: string }>('/catalogos', body)
    },
    onSuccess: async (result) => {
      await client.invalidateQueries({ queryKey: ['catalogos'] })
      notify(catalog ? 'Catálogo atualizado.' : 'Catálogo criado.')
      onClose()
      if (!catalog && result && 'id' in result) navigate(`/catalogos/${result.id}`)
    },
  })

  return (
    <Modal open onClose={onClose} title={catalog ? 'Editar catálogo' : 'Novo catálogo'}>
      <form
        className="grid gap-3"
        onSubmit={(e) => { e.preventDefault(); save.mutate() }}
      >
        <TextInput label="Nome" required maxLength={80} value={nome} onChange={(e) => setNome(e.target.value)} />
        <TextArea label="Descrição" maxLength={1000} value={descricao} onChange={(e) => setDescricao(e.target.value)} />
        <Select
          label="Visibilidade"
          value={visibilidade}
          disabled={!!catalog}
          hint={!isAdmin && !catalog ? 'Somente administradores criam catálogos compartilhados.' : 'Catálogos pessoais só aparecem para você; compartilhados aparecem para todos da despensa.'}
          onChange={(e) => setVisibilidade(e.target.value as Visibilidade)}
        >
          <option value="pessoal">Pessoal</option>
          {(isAdmin || catalog?.visibilidade === 'despensa') && <option value="despensa">Compartilhado com a despensa</option>}
        </Select>
        <ErrorNote error={save.error} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" busy={save.isPending} disabled={!nome.trim() || !pantry}>Salvar</Button>
        </div>
      </form>
    </Modal>
  )
}

export function CatalogsPage() {
  usePageTitle('Catálogos')
  const { pantry, canEdit } = useSession()
  const [creating, setCreating] = useState(false)
  const query = useQuery({ queryKey: ['catalogos', pantry?.id], queryFn: () => get<Catalogo[]>('/catalogos'), enabled: !!pantry })
  const personal = query.data?.filter((c) => c.visibilidade === 'pessoal') ?? []
  const shared = query.data?.filter((c) => c.visibilidade === 'despensa') ?? []

  const group = (title: string, items: Catalogo[], empty: string) => (
    <section aria-labelledby={`g-${title}`} className="mb-6">
      <h2 id={`g-${title}`} className="mb-2 text-lg font-bold">{title}</h2>
      {items.length === 0 ? <p className="text-sm text-muted">{empty}</p> : (
        <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {items.map((c) => (
            <li key={c.id}>
              <Link to={`/catalogos/${c.id}`} className="block h-full rounded-xl border border-slate-300 bg-white p-4 shadow-sm hover:border-brand-600">
                <span className="block font-bold">{c.nome}</span>
                {c.descricao && <span className="mt-1 block text-sm text-muted">{c.descricao}</span>}
                <span className="mt-2 block text-xs text-muted">{c.quantidadeReceitas} {c.quantidadeReceitas === 1 ? 'receita' : 'receitas'}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  )

  return (
    <>
      <PageTitle actions={<>
        <Link to="/receitas" className="inline-flex min-h-11 items-center rounded-lg border border-slate-400 bg-white px-4 text-sm font-semibold">Todas as receitas</Link>
        {canEdit && <Button onClick={() => setCreating(true)}>+ Novo catálogo</Button>}
      </>}>Catálogos</PageTitle>
      <ErrorNote error={query.error} onRetry={() => query.refetch()} />
      {query.isLoading && <Spinner />}
      {query.data && (
        query.data.length === 0
          ? <Empty title="Nenhum catálogo ainda">Organize suas receitas em coleções, como “Jantares rápidos” ou “Sobremesas”.</Empty>
          : <>
              {group('Meus catálogos', personal, 'Você ainda não criou catálogos pessoais.')}
              {group('Compartilhados da despensa', shared, 'Nenhum catálogo compartilhado.')}
            </>
      )}
      {creating && <CatalogFormModal onClose={() => setCreating(false)} />}
    </>
  )
}

export function CatalogPage() {
  const { id = '' } = useParams()
  const { pantry, canEdit, isAdmin } = useSession()
  const client = useQueryClient()
  const navigate = useNavigate()
  const { notify } = useToast()
  const [editing, setEditing] = useState(false)
  const [adding, setAdding] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const query = useQuery({ queryKey: ['catalogo', pantry?.id, id], queryFn: () => get<CatalogoDetalhe>(`/catalogos/${id}`), enabled: !!pantry })
  const c = query.data
  usePageTitle(c?.nome ?? 'Catálogo')

  const manageable = !!c && canEdit && (c.visibilidade === 'pessoal' ? c.meu : isAdmin)
  const invalidate = async () => {
    await client.invalidateQueries({ queryKey: ['catalogo'] })
    await client.invalidateQueries({ queryKey: ['catalogos'] })
  }
  const remove = useMutation({
    mutationFn: () => del(`/catalogos/${id}`),
    onSuccess: async () => { await invalidate(); notify('Catálogo excluído.'); navigate('/catalogos', { replace: true }) },
  })
  const removeRecipe = useMutation({
    mutationFn: (recipeId: string) => del(`/catalogos/${id}/receitas/${recipeId}`),
    onSuccess: async () => { await invalidate(); notify('Receita removida do catálogo.') },
  })
  const duplicate = useMutation({
    mutationFn: () => post<{ id: string }>(`/catalogos/${id}/duplicar`, { incluirReceitas: true, visibilidade: 'pessoal' }),
    onSuccess: async (r) => { await invalidate(); notify('Catálogo duplicado.'); navigate(`/catalogos/${r.id}`) },
  })

  if (query.isLoading) return <Spinner />
  if (query.error || !c) return <><PageTitle>Catálogo</PageTitle><ErrorNote error={query.error} onRetry={() => query.refetch()} /></>

  return (
    <>
      <p className="mb-2 text-sm"><Link to="/catalogos" className="text-brand-700 underline">← Catálogos</Link></p>
      <PageTitle actions={<>
        {canEdit && <Button variant="secondary" busy={duplicate.isPending} onClick={() => duplicate.mutate()}>Duplicar como pessoal</Button>}
        {manageable && <Button variant="secondary" onClick={() => setEditing(true)}>Editar</Button>}
        {manageable && <Button variant="danger" onClick={() => setConfirmDelete(true)}>Excluir</Button>}
      </>}>{c.nome}</PageTitle>
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <Badge tone={c.visibilidade === 'despensa' ? 'info' : 'muted'}>{c.visibilidade === 'despensa' ? 'Compartilhado' : 'Pessoal'}</Badge>
        {c.descricao && <span className="text-sm text-muted">{c.descricao}</span>}
      </div>
      <ErrorNote error={duplicate.error ?? removeRecipe.error} />
      {manageable && <div className="mb-4"><Button onClick={() => setAdding(true)}>+ Adicionar receitas</Button></div>}
      {c.receitas.length === 0 ? <Empty title="Este catálogo está vazio" /> : (
        <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {c.receitas.map((r) => {
            const image = fileUrl(pantry?.id, r.imagemArquivoId) ?? r.imagemUrl
            return (
              <li key={r.id} className="flex overflow-hidden rounded-xl border border-slate-300 bg-white shadow-sm">
                <Link to={`/receitas/${r.id}`} className="flex flex-1 items-center gap-3 p-3 hover:bg-brand-50">
                  {image
                    ? <img src={image} alt="" loading="lazy" referrerPolicy="no-referrer" className="size-14 rounded-lg object-cover" />
                    : <span aria-hidden="true" className="flex size-14 items-center justify-center rounded-lg bg-brand-50 text-2xl">🍲</span>}
                  <span className="font-semibold">{r.titulo}</span>
                </Link>
                {manageable && (
                  <Button variant="ghost" aria-label={`Remover ${r.titulo} do catálogo`} onClick={() => removeRecipe.mutate(r.id)}>✕</Button>
                )}
              </li>
            )
          })}
        </ul>
      )}
      {editing && <CatalogFormModal catalog={c} onClose={() => setEditing(false)} />}
      {adding && <AddRecipesModal catalogId={c.id} existing={new Set(c.receitas.map((r) => r.id))} onClose={() => setAdding(false)} onChanged={invalidate} />}
      <ConfirmDialog
        open={confirmDelete}
        danger
        busy={remove.isPending}
        title="Excluir catálogo?"
        message={<p>O catálogo “{c.nome}” será excluído. As receitas continuam disponíveis.</p>}
        confirmLabel="Excluir"
        onConfirm={() => remove.mutate()}
        onClose={() => setConfirmDelete(false)}
      />
    </>
  )
}

function AddRecipesModal({ catalogId, existing, onClose, onChanged }: { catalogId: string; existing: Set<string>; onClose: () => void; onChanged: () => Promise<void> }) {
  const [busca, setBusca] = useState('')
  const { pantry } = useSession()
  const { notify } = useToast()
  const recipes = useQuery({
    queryKey: ['receitas-busca', pantry?.id, busca],
    queryFn: () => get<Page<ReceitaResumo>>(`/receitas?limite=30${busca ? `&busca=${encodeURIComponent(busca)}` : ''}`),
  })
  const add = useMutation({
    mutationFn: (recipeId: string) => put(`/catalogos/${catalogId}/receitas/${recipeId}`),
    onSuccess: async () => { await onChanged(); notify('Receita adicionada.') },
  })
  return (
    <Modal open onClose={onClose} title="Adicionar receitas" wide>
      <TextInput label="Buscar receita" type="search" value={busca} onChange={(e) => setBusca(e.target.value)} />
      <div className="mt-3"><ErrorNote error={add.error ?? recipes.error} /></div>
      {recipes.isLoading && <Spinner />}
      <ul className="mt-3 divide-y divide-slate-200">
        {recipes.data?.itens.map((r) => (
          <li key={r.id} className="flex items-center justify-between gap-3 py-2">
            <span className="font-medium">{r.titulo}</span>
            {existing.has(r.id)
              ? <Badge tone="ok" icon="✓">No catálogo</Badge>
              : <Button variant="secondary" busy={add.isPending && add.variables === r.id} onClick={() => add.mutate(r.id)}>Adicionar</Button>}
          </li>
        ))}
      </ul>
      {recipes.data && recipes.data.itens.length === 0 && <p className="text-sm text-muted">Nenhuma receita encontrada.</p>}
    </Modal>
  )
}
