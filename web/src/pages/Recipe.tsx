import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { del, fileUrl, get, post, put } from '../lib/api'
import { useSession } from '../lib/session'
import { usePageTitle } from '../lib/hooks'
import type { Catalogo, Compat, ReceitaDetalhe } from '../lib/types'
import { Badge, Button, Card, ConfirmDialog, ErrorNote, Modal, PageTitle, Select, Spinner, TextInput, useToast } from '../components/ui'
import { PreparoModal } from '../components/PreparoModal'
import { estadoInfo, formatQuantity, lineStatusInfo, originLabels } from '../lib/format'

export function RecipePage() {
  const { id = '' } = useParams()
  const { pantry, canEdit } = useSession()
  const navigate = useNavigate()
  const client = useQueryClient()
  const { notify } = useToast()
  const [servings, setServings] = useState<number | null>(null)
  const [optionals, setOptionals] = useState(true)
  const [cooking, setCooking] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [cataloging, setCataloging] = useState(false)
  const [shopping, setShopping] = useState(false)

  const recipe = useQuery({ queryKey: ['receita', pantry?.id, id], queryFn: () => get<ReceitaDetalhe>(`/receitas/${id}`) })
  usePageTitle(recipe.data?.titulo ?? 'Receita')
  const desired = servings ?? recipe.data?.porcoes ?? 1
  const compat = useQuery({
    queryKey: ['compat', pantry?.id, id, desired, optionals],
    queryFn: () => get<Compat>(`/receitas/${id}/compatibilidade?porcoes=${desired}&incluirOpcionais=${optionals}`),
    enabled: !!recipe.data && recipe.data.ingredientes.length > 0,
    placeholderData: (previous) => previous,
  })

  const favorite = useMutation({
    mutationFn: (on: boolean) => (on ? put(`/receitas/${id}/favorito`) : del(`/receitas/${id}/favorito`)),
    onSuccess: () => client.invalidateQueries({ queryKey: ['receita'] }).then(() => client.invalidateQueries({ queryKey: ['receitas'] })),
  })
  const duplicate = useMutation({
    mutationFn: () => post<{ id: string }>(`/receitas/${id}/duplicar`),
    onSuccess: async (r) => { await client.invalidateQueries({ queryKey: ['receitas'] }); notify('Receita duplicada.'); navigate(`/receitas/${r.id}/editar`) },
  })
  const remove = useMutation({
    mutationFn: () => del(`/receitas/${id}`),
    onSuccess: async () => { await client.invalidateQueries(); navigate('/receitas', { replace: true }) },
  })

  if (recipe.isLoading) return <Spinner />
  if (recipe.error) return <ErrorNote error={recipe.error} onRetry={() => recipe.refetch()} />
  const r = recipe.data!
  const image = fileUrl(pantry?.id, r.imagemArquivoId) ?? r.imagemUrl
  const factor = desired / r.porcoes
  const lineStatus = new Map((compat.data?.linhas ?? []).map((l) => [l.linhaId, l]))
  const estado = compat.data ? estadoInfo[compat.data.estado] : r.status === 'rascunho' ? estadoInfo.rascunho : null

  return (
    <>
      <PageTitle
        actions={
          <>
            <Button variant="secondary" aria-pressed={r.favorito} busy={favorite.isPending} onClick={() => favorite.mutate(!r.favorito)}>
              {r.favorito ? '★ Favorita' : '☆ Favoritar'}
            </Button>
            <Button variant="secondary" onClick={() => setCataloging(true)}>Catálogos</Button>
            <Button variant="secondary" onClick={() => setShopping(true)}>Lista de compras</Button>
            {canEdit && <Link to={`/receitas/${r.id}/editar`} className="inline-flex min-h-11 items-center rounded-lg border border-slate-400 bg-white px-4 text-sm font-semibold">Editar</Link>}
          </>
        }
      >
        {r.titulo}
      </PageTitle>
      <ErrorNote error={favorite.error ?? duplicate.error} />

      <div className="grid gap-4 lg:grid-cols-[2fr_3fr]">
        <div className="space-y-4">
          {image && <img src={image} alt="" referrerPolicy="no-referrer" className="max-h-72 w-full rounded-xl border border-slate-300 object-cover" />}
          <Card>
            <div className="flex flex-wrap items-center gap-2">
              {estado && <Badge tone={estado.tone} icon={estado.icon}>{estado.label}</Badge>}
              {r.status === 'rascunho' && <Badge tone="muted">Rascunho</Badge>}
              <Badge tone="info">{originLabels[r.origem] ?? r.origem}</Badge>
              {r.tempoPreparoMin && <Badge tone="muted">{r.tempoPreparoMin} min</Badge>}
              {r.dificuldade && <Badge tone="muted">{r.dificuldade}</Badge>}
            </div>
            {r.descricao && <p className="mt-3">{r.descricao}</p>}
            {r.tags.length > 0 && <p className="mt-2 text-sm text-muted">{r.tags.map((t) => `#${t}`).join(' ')}</p>}
            {(r.autorAtribuicao || r.urlOrigem) && (
              <p className="mt-3 text-sm text-muted">
                Fonte: {r.autorAtribuicao ?? 'não informada'}
                {r.urlOrigem && <> · <a className="font-semibold text-brand-700 underline" href={r.urlOrigem} target="_blank" rel="noopener noreferrer nofollow">ver original</a></>}
              </p>
            )}
            {r.origem === 'ia' && <p className="mt-3 rounded bg-amber-50 p-2 text-sm text-amber-950">Receita gerada por IA: confira quantidades, tempos e etapas.</p>}
            {r.observacoes && <p className="mt-3 text-sm"><strong>Observações:</strong> {r.observacoes}</p>}
          </Card>
          {canEdit && (
            <Card>
              <h2 className="mb-2 text-lg font-bold">Preparar</h2>
              <div className="flex flex-wrap items-end gap-3">
                <TextInput
                  label="Porções" type="number" min={1} max={500} step={1} className="w-28" value={String(desired)}
                  onChange={(e) => { const n = Number(e.target.value); setServings(n > 0 ? n : null) }}
                />
                <label className="flex min-h-11 items-center gap-2 text-sm">
                  <input type="checkbox" className="size-5" checked={optionals} onChange={(e) => setOptionals(e.target.checked)} />
                  Incluir opcionais
                </label>
              </div>
              <Button className="mt-3 w-full" disabled={r.status === 'rascunho'} onClick={() => setCooking(true)}>Preparar esta receita</Button>
              {r.status === 'rascunho' && <p className="mt-2 text-xs text-muted">Complete a receita (ingredientes e etapas) para poder prepará-la.</p>}
            </Card>
          )}
          {canEdit && (
            <div className="flex gap-2">
              <Button variant="secondary" busy={duplicate.isPending} onClick={() => duplicate.mutate()}>Duplicar</Button>
              <Button variant="ghost" onClick={() => setDeleting(true)}>Excluir</Button>
            </div>
          )}
        </div>

        <div className="space-y-4">
          <Card>
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h2 className="text-lg font-bold">Ingredientes <span className="text-sm font-normal text-muted">({desired} porções)</span></h2>
              {compat.data && r.status === 'completa' && (
                <span className="text-sm font-semibold">{compat.data.percentual.toLocaleString('pt-BR', { maximumFractionDigits: 0 })}% dos obrigatórios</span>
              )}
            </div>
            <ul className="divide-y divide-slate-200">
              {r.ingredientes.filter((l) => optionals || !l.opcional).map((l) => {
                const status = l.id ? lineStatus.get(l.id) : undefined
                const info = status ? lineStatusInfo[status.status] : null
                return (
                  <li key={l.id ?? l.nome} className="py-2">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span>
                        <strong>{l.quantidade !== null ? formatQuantity(l.quantidade * factor, l.unidade) : ''}</strong> {l.nome}
                        {l.opcional && <span className="text-muted"> (opcional)</span>}
                      </span>
                      {info && <Badge tone={info.tone}>{info.label}</Badge>}
                    </div>
                    {status && status.status === 'insufficient' && <p className="text-sm text-red-800">Faltam {formatQuantity(status.faltante, l.unidade)}.</p>}
                    {status?.usaEquivalente && <p className="text-xs text-muted">Usa ingrediente equivalente do estoque.</p>}
                    {l.confianca !== 'confirmada' && <p className="text-xs text-amber-900">Vínculo com o estoque {l.confianca === 'inferida' ? 'sugerido, não confirmado' : 'não definido'}. <Link className="underline" to={`/receitas/${r.id}/editar`}>Revisar</Link></p>}
                  </li>
                )
              })}
            </ul>
            {compat.data && <p className="mt-3 text-xs text-muted">{compat.data.aviso}</p>}
            {compat.isError && <ErrorNote error={compat.error} />}
          </Card>
          <Card>
            <h2 className="mb-2 text-lg font-bold">Modo de preparo</h2>
            {r.etapas.length === 0 ? <p className="text-muted">Esta receita ainda não tem etapas.</p> : (
              <ol className="list-decimal space-y-3 pl-5">{r.etapas.map((e) => <li key={e.ordem}>{e.instrucao}</li>)}</ol>
            )}
          </Card>
        </div>
      </div>

      {cooking && <PreparoModal recipeId={r.id} servings={desired} includeOptionals={optionals} onClose={() => setCooking(false)} />}
      <ConfirmDialog
        open={deleting} onClose={() => setDeleting(false)} danger busy={remove.isPending} confirmLabel="Excluir receita"
        title="Excluir receita?" message={<><p>O histórico de preparos continua guardado, mas a receita some de catálogos e favoritos.</p><ErrorNote error={remove.error} /></>}
        onConfirm={() => remove.mutate()}
      />
      {cataloging && <CatalogPicker recipeId={r.id} onClose={() => setCataloging(false)} />}
      {shopping && <ShoppingFromRecipe recipe={r} servings={desired} optionals={optionals} onClose={() => setShopping(false)} />}
    </>
  )
}

function CatalogPicker({ recipeId, onClose }: { recipeId: string; onClose: () => void }) {
  const { pantry } = useSession()
  const client = useQueryClient()
  const { notify } = useToast()
  const catalogs = useQuery({ queryKey: ['catalogos', pantry?.id], queryFn: () => get<Catalogo[]>('/catalogos') })
  const [catalogId, setCatalogId] = useState('')
  const add = useMutation({
    mutationFn: () => put(`/catalogos/${catalogId || catalogs.data?.[0]?.id}/receitas/${recipeId}`),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: ['catalogos'] }); notify('Receita adicionada ao catálogo.'); onClose() },
  })
  return (
    <Modal open onClose={onClose} title="Adicionar a um catálogo">
      {catalogs.isLoading && <Spinner />}
      {catalogs.data?.length === 0 && <p>Você ainda não tem catálogos. <Link to="/catalogos" className="font-semibold text-brand-700 underline">Criar um catálogo</Link>.</p>}
      {catalogs.data && catalogs.data.length > 0 && (
        <form onSubmit={(e) => { e.preventDefault(); add.mutate() }} className="space-y-3">
          <Select label="Catálogo" value={catalogId || catalogs.data[0].id} onChange={(e) => setCatalogId(e.target.value)}>
            {catalogs.data.map((c) => <option key={c.id} value={c.id}>{c.nome} {c.visibilidade === 'pessoal' ? '(pessoal)' : '(da despensa)'}</option>)}
          </Select>
          <ErrorNote error={add.error} />
          <div className="flex justify-end gap-2"><Button variant="secondary" onClick={onClose}>Cancelar</Button><Button type="submit" busy={add.isPending}>Adicionar</Button></div>
        </form>
      )}
    </Modal>
  )
}

function ShoppingFromRecipe({ recipe, servings, optionals, onClose }: { recipe: ReceitaDetalhe; servings: number; optionals: boolean; onClose: () => void }) {
  const navigate = useNavigate()
  const client = useQueryClient()
  const { canEdit } = useSession()
  const [nome, setNome] = useState(`Compras: ${recipe.titulo}`.slice(0, 120))
  const create = useMutation({
    mutationFn: () => post<{ lista: { id: string }; ignorados: string[] }>('/listas/de-receitas', { nome, receitas: [{ receitaId: recipe.id, porcoes: servings }], incluirOpcionais: optionals }),
    onSuccess: async (r) => { await client.invalidateQueries({ queryKey: ['listas'] }); navigate(`/compras/${r.lista.id}`) },
  })
  return (
    <Modal open onClose={onClose} title="Gerar lista de compras">
      {!canEdit ? <p>Somente editores e administradores criam listas de compras.</p> : (
        <form onSubmit={(e) => { e.preventDefault(); create.mutate() }} className="space-y-3">
          <p className="text-sm text-muted">Vamos listar o que falta para {servings} porções, descontando o que já está em estoque.</p>
          <TextInput label="Nome da lista" required value={nome} onChange={(e) => setNome(e.target.value)} />
          <ErrorNote error={create.error} />
          <div className="flex justify-end gap-2"><Button variant="secondary" onClick={onClose}>Cancelar</Button><Button type="submit" busy={create.isPending}>Criar lista</Button></div>
        </form>
      )}
    </Modal>
  )
}
