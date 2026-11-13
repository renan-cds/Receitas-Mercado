import { useMemo, useRef, useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { del, get, newKey, patch, post } from '../lib/api'
import { useSession } from '../lib/session'
import { usePageTitle, useUnits } from '../lib/hooks'
import { formatDateTime, formatNumber, unitLabel } from '../lib/format'
import type { IngredienteItem, ListaItem, ListaResumo, Lista, Page, ReceitaResumo } from '../lib/types'
import { Badge, Button, Card, ConfirmDialog, Empty, ErrorNote, Modal, PageTitle, Select, Spinner, TextInput, useToast } from '../components/ui'

const statusLabel = { aberta: 'Aberta', comprada: 'Comprada', arquivada: 'Arquivada' } as const
const statusTone = { aberta: 'info', comprada: 'ok', arquivada: 'muted' } as const

export function ShoppingPage() {
  usePageTitle('Compras')
  const { pantry, canEdit } = useSession()
  const [creating, setCreating] = useState<'manual' | 'receitas' | null>(null)
  const [arquivadas, setArquivadas] = useState(false)
  const query = useQuery({ queryKey: ['listas', pantry?.id], queryFn: () => get<ListaResumo[]>('/listas'), enabled: !!pantry })
  const lists = (query.data ?? []).filter((l) => arquivadas || l.status !== 'arquivada')

  return (
    <>
      <PageTitle actions={canEdit && <>
        <Button variant="secondary" onClick={() => setCreating('receitas')}>Lista a partir de receitas</Button>
        <Button onClick={() => setCreating('manual')}>+ Nova lista</Button>
      </>}>Listas de compras</PageTitle>
      <label className="mb-4 flex min-h-11 items-center gap-2 text-sm">
        <input type="checkbox" className="size-5" checked={arquivadas} onChange={(e) => setArquivadas(e.target.checked)} />
        Mostrar arquivadas
      </label>
      <ErrorNote error={query.error} onRetry={() => query.refetch()} />
      {query.isLoading && <Spinner />}
      {query.data && lists.length === 0 && (
        <Empty title="Nenhuma lista de compras">Crie uma lista manual ou gere uma a partir das receitas que você quer fazer — o que já existe no estoque é descontado.</Empty>
      )}
      <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {lists.map((l) => (
          <li key={l.id}>
            <Link to={`/compras/${l.id}`} className="block h-full rounded-xl border border-slate-300 bg-white p-4 shadow-sm hover:border-brand-600">
              <span className="flex items-start justify-between gap-2">
                <span className="font-bold">{l.nome}</span>
                <Badge tone={statusTone[l.status]}>{statusLabel[l.status]}</Badge>
              </span>
              <span className="mt-2 block text-sm text-muted">{l.comprados} de {l.itens} itens comprados</span>
              <span className="block text-xs text-muted">Criada em {formatDateTime(l.criadoEm)}</span>
            </Link>
          </li>
        ))}
      </ul>
      {creating === 'manual' && <ManualListModal onClose={() => setCreating(null)} />}
      {creating === 'receitas' && <FromRecipesModal onClose={() => setCreating(null)} />}
    </>
  )
}

function ManualListModal({ onClose }: { onClose: () => void }) {
  const [nome, setNome] = useState('')
  const client = useQueryClient()
  const navigate = useNavigate()
  const create = useMutation({
    mutationFn: () => post<{ id: string }>('/listas', { nome }),
    onSuccess: async (l) => { await client.invalidateQueries({ queryKey: ['listas'] }); onClose(); navigate(`/compras/${l.id}`) },
  })
  return (
    <Modal open onClose={onClose} title="Nova lista">
      <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); create.mutate() }}>
        <TextInput label="Nome da lista" required maxLength={120} value={nome} onChange={(e) => setNome(e.target.value)} />
        <ErrorNote error={create.error} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" busy={create.isPending} disabled={!nome.trim()}>Criar</Button>
        </div>
      </form>
    </Modal>
  )
}

function FromRecipesModal({ onClose }: { onClose: () => void }) {
  const { pantry } = useSession()
  const client = useQueryClient()
  const navigate = useNavigate()
  const [nome, setNome] = useState('Compras da semana')
  const [busca, setBusca] = useState('')
  const [chosen, setChosen] = useState<Record<string, { titulo: string; porcoes: number }>>({})
  const [opcionais, setOpcionais] = useState(false)
  const recipes = useQuery({
    queryKey: ['receitas-busca', pantry?.id, busca],
    queryFn: () => get<Page<ReceitaResumo>>(`/receitas?limite=30&status=completa${busca ? `&busca=${encodeURIComponent(busca)}` : ''}`),
  })
  const create = useMutation({
    mutationFn: () => post<{ lista: Lista; ignorados: string[] }>('/listas/de-receitas', {
      nome,
      incluirOpcionais: opcionais,
      receitas: Object.entries(chosen).map(([receitaId, v]) => ({ receitaId, porcoes: v.porcoes })),
    }),
    onSuccess: async (r) => { await client.invalidateQueries({ queryKey: ['listas'] }); onClose(); navigate(`/compras/${r.lista.id}`, { state: { ignorados: r.ignorados } }) },
  })
  const count = Object.keys(chosen).length

  return (
    <Modal open onClose={onClose} title="Lista a partir de receitas" wide>
      <div className="grid gap-3">
        <TextInput label="Nome da lista" required maxLength={120} value={nome} onChange={(e) => setNome(e.target.value)} />
        <TextInput label="Buscar receita" type="search" value={busca} onChange={(e) => setBusca(e.target.value)} />
        <ul className="max-h-72 divide-y divide-slate-200 overflow-y-auto rounded-lg border border-slate-300">
          {recipes.isLoading && <li><Spinner /></li>}
          {recipes.data?.itens.map((r) => {
            const selected = chosen[r.id]
            return (
              <li key={r.id} className="flex flex-wrap items-center gap-3 px-3 py-2">
                <label className="flex min-h-11 flex-1 items-center gap-2">
                  <input
                    type="checkbox"
                    className="size-5"
                    checked={!!selected}
                    onChange={(e) => setChosen((c) => {
                      const next = { ...c }
                      if (e.target.checked) next[r.id] = { titulo: r.titulo, porcoes: r.porcoes }
                      else delete next[r.id]
                      return next
                    })}
                  />
                  <span className="font-medium">{r.titulo}</span>
                </label>
                {selected && (
                  <label className="flex items-center gap-2 text-sm">
                    Porções
                    <input
                      type="number"
                      min={0.5}
                      step={0.5}
                      value={selected.porcoes}
                      onChange={(e) => setChosen((c) => ({ ...c, [r.id]: { ...selected, porcoes: Number(e.target.value) } }))}
                      className="min-h-11 w-24 rounded-lg border border-slate-500 px-2"
                    />
                  </label>
                )}
              </li>
            )
          })}
        </ul>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="size-5" checked={opcionais} onChange={(e) => setOpcionais(e.target.checked)} />
          Incluir ingredientes opcionais
        </label>
        <ErrorNote error={create.error} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button busy={create.isPending} disabled={count === 0 || !nome.trim() || Object.values(chosen).some((c) => !(c.porcoes > 0))} onClick={() => create.mutate()}>
            Gerar lista ({count})
          </Button>
        </div>
      </div>
    </Modal>
  )
}

export function ShoppingListPage() {
  const { id = '' } = useParams()
  const { pantry, canEdit } = useSession()
  const client = useQueryClient()
  const navigate = useNavigate()
  const ignorados = ((useLocation().state as { ignorados?: string[] } | null)?.ignorados) ?? []
  const { notify } = useToast()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<ListaItem | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [launching, setLaunching] = useState(false)
  const query = useQuery({ queryKey: ['lista', pantry?.id, id], queryFn: () => get<Lista>(`/listas/${id}`), enabled: !!pantry })
  const list = query.data
  usePageTitle(list?.nome ?? 'Lista de compras')

  const refresh = async () => {
    await client.invalidateQueries({ queryKey: ['lista'] })
    await client.invalidateQueries({ queryKey: ['listas'] })
  }
  const toggle = useMutation({
    mutationFn: (item: ListaItem) => patch<Lista>(`/listas/${id}/itens/${item.id}`, {
      texto: item.texto, quantidadeComprar: item.quantidadeComprar, unidade: item.unidade, comprado: !item.comprado,
    }),
    onSuccess: (data) => { client.setQueryData(['lista', pantry?.id, id], data); void client.invalidateQueries({ queryKey: ['listas'] }) },
  })
  const removeItem = useMutation({
    mutationFn: (itemId: string) => del(`/listas/${id}/itens/${itemId}`),
    onSuccess: refresh,
  })
  const setStatus = useMutation({
    mutationFn: (status: Lista['status']) => patch<Lista>(`/listas/${id}`, { nome: list!.nome, status }),
    onSuccess: async () => { await refresh(); notify('Lista atualizada.') },
  })
  const remove = useMutation({
    mutationFn: () => del(`/listas/${id}`),
    onSuccess: async () => { await refresh(); navigate('/compras', { replace: true }) },
  })

  const pendingEntries = useMemo(() => list?.itens.filter((i) => i.comprado && !i.entradaRegistrada && i.ingredienteId) ?? [], [list])

  if (query.isLoading) return <Spinner />
  if (query.error || !list) return <><PageTitle>Lista de compras</PageTitle><ErrorNote error={query.error} onRetry={() => query.refetch()} /></>

  const open = list.itens.filter((i) => !i.comprado)
  const done = list.itens.filter((i) => i.comprado)
  const readOnly = !canEdit

  const row = (i: ListaItem) => (
    <li key={i.id} className="flex flex-wrap items-center gap-3 px-3 py-2">
      <label className="flex min-h-11 min-w-48 flex-1 items-center gap-3">
        <input
          type="checkbox"
          className="size-6"
          checked={i.comprado}
          disabled={readOnly || i.entradaRegistrada || toggle.isPending}
          onChange={() => toggle.mutate(i)}
        />
        <span>
          <span className={`block font-semibold ${i.comprado ? 'text-muted line-through' : ''}`}>{i.texto}</span>
          <span className="text-xs text-muted">
            {i.ingredienteNome && i.ingredienteNome !== i.texto ? `${i.ingredienteNome} · ` : ''}
            {i.quantidadeDescontadaEstoque > 0 ? `${formatNumber(i.quantidadeDescontadaEstoque)} ${unitLabel(i.unidade)} já em estoque · ` : ''}
            {i.ingredienteId ? 'vinculado ao estoque' : 'sem vínculo com o estoque'}
          </span>
        </span>
      </label>
      <span className="font-bold">{formatNumber(i.quantidadeComprar)} {unitLabel(i.unidade)}</span>
      {i.entradaRegistrada && <Badge tone="ok" icon="✓">No estoque</Badge>}
      {!readOnly && !i.entradaRegistrada && (
        <span className="flex gap-1">
          <Button variant="ghost" aria-label={`Editar ${i.texto}`} onClick={() => setEditing(i)}>Editar</Button>
          <Button variant="ghost" aria-label={`Remover ${i.texto}`} onClick={() => removeItem.mutate(i.id)}>✕</Button>
        </span>
      )}
    </li>
  )

  return (
    <>
      <p className="mb-2 text-sm"><Link to="/compras" className="text-brand-700 underline">← Listas de compras</Link></p>
      <PageTitle actions={canEdit && <>
        <Button variant="secondary" onClick={() => setAdding(true)}>+ Item</Button>
        {list.status === 'arquivada'
          ? <Button variant="secondary" onClick={() => setStatus.mutate('aberta')}>Reabrir</Button>
          : <Button variant="secondary" onClick={() => setStatus.mutate('arquivada')}>Arquivar</Button>}
        <Button variant="danger" onClick={() => setConfirmDelete(true)}>Excluir</Button>
      </>}>{list.nome}</PageTitle>
      <div className="mb-4"><Badge tone={statusTone[list.status]}>{statusLabel[list.status]}</Badge></div>
      <ErrorNote error={toggle.error ?? removeItem.error ?? setStatus.error} />
      {ignorados.length > 0 && (
        <div role="note" className="mb-4 rounded-lg border border-amber-400 bg-amber-50 p-3 text-sm text-amber-950">
          <p className="font-semibold">Alguns ingredientes não entraram na lista:</p>
          <ul className="list-disc pl-5">{ignorados.map((t) => <li key={t}>{t}</li>)}</ul>
        </div>
      )}

      {list.itens.length === 0 && <Empty title="Lista vazia">Adicione itens manualmente.</Empty>}
      {open.length > 0 && (
        <section aria-labelledby="a-comprar" className="mb-5">
          <h2 id="a-comprar" className="mb-2 text-lg font-bold">A comprar ({open.length})</h2>
          <ul className="divide-y divide-slate-200 rounded-xl border border-slate-300 bg-white">{open.map(row)}</ul>
        </section>
      )}
      {done.length > 0 && (
        <section aria-labelledby="comprados" className="mb-5">
          <h2 id="comprados" className="mb-2 text-lg font-bold">Comprados ({done.length})</h2>
          <ul className="divide-y divide-slate-200 rounded-xl border border-slate-300 bg-white">{done.map(row)}</ul>
        </section>
      )}

      {canEdit && pendingEntries.length > 0 && (
        <Card className="mb-5">
          <p className="mb-3 text-sm">{pendingEntries.length} {pendingEntries.length === 1 ? 'item comprado ainda não entrou' : 'itens comprados ainda não entraram'} no estoque.</p>
          <Button onClick={() => setLaunching(true)}>Lançar compras no estoque</Button>
        </Card>
      )}

      {adding && <ItemModal listId={id} onClose={() => setAdding(false)} onSaved={refresh} />}
      {editing && <ItemModal listId={id} item={editing} onClose={() => setEditing(null)} onSaved={refresh} />}
      {launching && <LaunchModal listId={id} items={pendingEntries} onClose={() => setLaunching(false)} onDone={refresh} />}
      <ConfirmDialog
        open={confirmDelete}
        danger
        busy={remove.isPending}
        title="Excluir lista?"
        message={<p>A lista “{list.nome}” e seus itens serão excluídos. O estoque não é alterado.</p>}
        confirmLabel="Excluir"
        onConfirm={() => remove.mutate()}
        onClose={() => setConfirmDelete(false)}
      />
    </>
  )
}

function ItemModal({ listId, item, onClose, onSaved }: { listId: string; item?: ListaItem; onClose: () => void; onSaved: () => Promise<void> }) {
  const { pantry } = useSession()
  const units = useUnits()
  const [texto, setTexto] = useState(item?.texto ?? '')
  const [quantidade, setQuantidade] = useState(item ? String(item.quantidadeComprar) : '1')
  const [unidade, setUnidade] = useState(item?.unidade ?? 'UN')
  const [ingredienteId, setIngredienteId] = useState(item?.ingredienteId ?? '')
  const ingredients = useQuery({
    queryKey: ['ingredientes-todos', pantry?.id],
    queryFn: () => get<Page<IngredienteItem>>('/ingredientes?limite=100'),
    enabled: !item,
  })
  const save = useMutation({
    mutationFn: () => item
      ? patch(`/listas/${listId}/itens/${item.id}`, { texto, quantidadeComprar: Number(quantidade), unidade, comprado: item.comprado })
      : post(`/listas/${listId}/itens`, { texto, quantidade: Number(quantidade), unidade, ingredienteId: ingredienteId || null }),
    onSuccess: async () => { await onSaved(); onClose() },
  })
  const pickIngredient = (value: string) => {
    setIngredienteId(value)
    const found = ingredients.data?.itens.find((i) => i.id === value)
    if (found) {
      if (!texto.trim()) setTexto(found.nome)
      setUnidade(found.unidadeReferencia)
    }
  }
  const valid = texto.trim() && Number(quantidade) >= 0 && quantidade !== ''

  return (
    <Modal open onClose={onClose} title={item ? 'Editar item' : 'Adicionar item'}>
      <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        {!item && (
          <Select label="Ingrediente do estoque (opcional)" value={ingredienteId} onChange={(e) => pickIngredient(e.target.value)}
            hint="Vincule para que a compra possa entrar no estoque automaticamente.">
            <option value="">Sem vínculo</option>
            {ingredients.data?.itens.map((i) => <option key={i.id} value={i.id}>{i.nome}</option>)}
          </Select>
        )}
        <TextInput label="Item" required maxLength={200} value={texto} onChange={(e) => setTexto(e.target.value)} />
        <div className="grid grid-cols-2 gap-3">
          <TextInput label="Quantidade" required type="number" inputMode="decimal" min={0} step="any" value={quantidade} onChange={(e) => setQuantidade(e.target.value)} />
          <Select label="Unidade" value={unidade} onChange={(e) => setUnidade(e.target.value)}>
            {units.map((u) => <option key={u.codigo} value={u.codigo}>{u.nome}</option>)}
          </Select>
        </div>
        <ErrorNote error={save.error} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" busy={save.isPending} disabled={!valid}>Salvar</Button>
        </div>
      </form>
    </Modal>
  )
}

interface LaunchResult { lancados: { itemId: string; texto: string }[]; ignorados: { itemId: string; texto: string; motivo: string }[] }
const skipReasons: Record<string, string> = {
  ja_lancado: 'já lançado', sem_ingrediente: 'sem vínculo com o estoque', quantidade_zero: 'quantidade zero',
}

function LaunchModal({ listId, items, onClose, onDone }: { listId: string; items: ListaItem[]; onClose: () => void; onDone: () => Promise<void> }) {
  const { notify } = useToast()
  const key = useRef(newKey())
  const [validades, setValidades] = useState<Record<string, string>>({})
  const [result, setResult] = useState<LaunchResult | null>(null)
  const launch = useMutation({
    mutationFn: () => post<LaunchResult>(`/listas/${listId}/lancar-entradas`, {
      itens: items.map((i) => ({ itemId: i.id, dataValidade: validades[i.id] || null })),
    }, key.current),
    onSuccess: async (r) => {
      setResult(r)
      await onDone()
      await Promise.resolve()
      notify(`${r.lancados.length} ${r.lancados.length === 1 ? 'item entrou' : 'itens entraram'} no estoque.`)
    },
  })

  return (
    <Modal open onClose={onClose} title="Lançar compras no estoque" wide>
      {result ? (
        <div className="grid gap-3">
          <p>{result.lancados.length} {result.lancados.length === 1 ? 'item foi lançado' : 'itens foram lançados'} no estoque.</p>
          {result.ignorados.length > 0 && (
            <ul className="list-disc pl-5 text-sm">
              {result.ignorados.map((i) => <li key={i.itemId}>{i.texto}: {skipReasons[i.motivo] ?? i.motivo}</li>)}
            </ul>
          )}
          <div className="flex justify-end"><Button onClick={onClose}>Fechar</Button></div>
        </div>
      ) : (
        <div className="grid gap-3">
          <p className="text-sm text-muted">Informe a validade de cada item, se houver. Cada lançamento cria um lote no estoque.</p>
          <ul className="divide-y divide-slate-200">
            {items.map((i) => (
              <li key={i.id} className="flex flex-wrap items-center justify-between gap-3 py-2">
                <span className="font-medium">{i.texto} <span className="text-muted">({formatNumber(i.quantidadeComprar)} {unitLabel(i.unidade)})</span></span>
                <label className="flex items-center gap-2 text-sm">
                  Validade
                  <input
                    type="date"
                    value={validades[i.id] ?? ''}
                    onChange={(e) => setValidades((v) => ({ ...v, [i.id]: e.target.value }))}
                    className="min-h-11 rounded-lg border border-slate-500 px-2"
                  />
                </label>
              </li>
            ))}
          </ul>
          <ErrorNote error={launch.error} />
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>Cancelar</Button>
            <Button busy={launch.isPending} onClick={() => launch.mutate()}>Lançar {items.length} {items.length === 1 ? 'item' : 'itens'}</Button>
          </div>
        </div>
      )}
    </Modal>
  )
}
