import { useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, del, fieldErrors, get, newKey, patch, post } from '../lib/api'
import { useSession } from '../lib/session'
import { useUnits, usePageTitle } from '../lib/hooks'
import type { IngredienteDetalhe, Lote, Movimentacao, Page } from '../lib/types'
import { Badge, Button, Card, ConfirmDialog, ErrorNote, Modal, PageTitle, Select, Spinner, TextArea, TextInput, useToast } from '../components/ui'
import { IngredientFormModal } from '../components/IngredientForm'
import { formatDate, formatDateTime, formatNumber, formatQuantity, movementLabels, unitLabel } from '../lib/format'

type Kind = 'entrada' | 'saida' | 'perda' | 'correcao'
const titles: Record<Kind, string> = { entrada: 'Registrar entrada', saida: 'Registrar saída', perda: 'Registrar perda', correcao: 'Corrigir estoque' }
const paths: Record<Kind, string> = { entrada: '/estoque/entradas', saida: '/estoque/saidas', perda: '/estoque/perdas', correcao: '/estoque/correcoes' }

export function IngredientPage() {
  const { id = '' } = useParams()
  const { canEdit, pantry } = useSession()
  const navigate = useNavigate()
  const client = useQueryClient()
  const { notify } = useToast()
  const [kind, setKind] = useState<Kind | null>(null)
  const [editing, setEditing] = useState(false)
  const [lotEdit, setLotEdit] = useState<Lote | null>(null)
  const [discard, setDiscard] = useState<Lote | null>(null)
  const [removing, setRemoving] = useState(false)

  const query = useQuery({ queryKey: ['ingrediente', pantry?.id, id], queryFn: () => get<IngredienteDetalhe>(`/ingredientes/${id}`) })
  usePageTitle(query.data?.nome ?? 'Ingrediente')
  const history = useInfiniteQuery({
    queryKey: ['movimentacoes', pantry?.id, id],
    initialPageParam: '' as string,
    queryFn: ({ pageParam }) => get<Page<Movimentacao>>(`/estoque/movimentacoes?ingredienteId=${id}&limite=15${pageParam ? `&cursor=${encodeURIComponent(pageParam)}` : ''}`),
    getNextPageParam: (l) => l.proximoCursor ?? undefined,
  })

  const refresh = () => client.invalidateQueries()
  const archive = useMutation({
    mutationFn: (archive: boolean) => post(`/ingredientes/${id}/${archive ? 'arquivar' : 'reativar'}`),
    onSuccess: async () => { await refresh(); notify('Situação atualizada.') },
  })
  const discardLot = useMutation({
    mutationFn: (lot: Lote) => post(`/lotes/${lot.id}/descartar`, { motivo: 'Lote descartado' }, newKey()),
    onSuccess: async () => { setDiscard(null); await refresh(); notify('Lote descartado e registrado como perda.') },
  })
  const remove = useMutation({
    mutationFn: () => del(`/ingredientes/${id}`),
    onSuccess: async () => { await refresh(); navigate('/estoque', { replace: true }) },
  })

  if (query.isLoading) return <Spinner />
  if (query.error) return <ErrorNote error={query.error} onRetry={() => query.refetch()} />
  const ing = query.data!
  const low = ing.quantidadeMinima > 0 && ing.quantidadeTotal < ing.quantidadeMinima

  return (
    <>
      <PageTitle
        actions={canEdit && (
          <>
            <Button onClick={() => setKind('entrada')} disabled={ing.arquivado}>+ Entrada</Button>
            <Button variant="secondary" onClick={() => setKind('saida')}>Saída</Button>
            <Button variant="secondary" onClick={() => setKind('perda')}>Perda</Button>
            <Button variant="secondary" onClick={() => setKind('correcao')}>Corrigir</Button>
          </>
        )}
      >
        {ing.nome}
      </PageTitle>
      <Card className="mb-4">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p className="text-sm text-muted">Em estoque</p>
            <p className="text-4xl font-bold">{formatQuantity(ing.quantidadeTotal, ing.unidadeReferencia)}</p>
            <div className="mt-2 flex flex-wrap gap-2">
              <Badge tone="muted">{ing.categoriaNome}</Badge>
              {ing.arquivado && <Badge tone="muted">Arquivado</Badge>}
              {low && <Badge tone="warn" icon="!">Abaixo do mínimo ({formatNumber(ing.quantidadeMinima)} {unitLabel(ing.unidadeReferencia)})</Badge>}
              {ing.localArmazenamento && <Badge tone="info">{ing.localArmazenamento}</Badge>}
            </div>
            {ing.observacoes && <p className="mt-2 text-sm text-muted">{ing.observacoes}</p>}
          </div>
          {canEdit && (
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => setEditing(true)}>Editar</Button>
              <Button variant="secondary" busy={archive.isPending} onClick={() => archive.mutate(!ing.arquivado)}>{ing.arquivado ? 'Reativar' : 'Arquivar'}</Button>
              <Button variant="ghost" onClick={() => setRemoving(true)}>Excluir</Button>
            </div>
          )}
        </div>
        <ErrorNote error={archive.error} />
      </Card>

      <Card className="mb-4">
        <h2 className="mb-2 text-lg font-bold">Lotes</h2>
        {ing.lotes.length === 0 ? <p className="text-muted">Sem lotes em estoque.</p> : (
          <ul className="divide-y divide-slate-200">
            {ing.lotes.map((l) => (
              <li key={l.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                <span>
                  <strong>{formatQuantity(l.quantidade, ing.unidadeReferencia)}</strong>
                  <span className="ml-2 text-sm text-muted">entrada em {formatDate(l.dataEntrada)}</span>
                </span>
                <span className="flex flex-wrap items-center gap-2">
                  {l.situacaoValidade === 'vencido' && <Badge tone="bad" icon="✕">Vencido</Badge>}
                  {l.situacaoValidade === 'vencendo' && <Badge tone="warn" icon="!">Vencendo</Badge>}
                  <span className="text-sm">{l.dataValidade ? `Validade ${formatDate(l.dataValidade)}` : 'Sem validade'}</span>
                  {canEdit && (
                    <>
                      <Button variant="ghost" onClick={() => setLotEdit(l)}>Editar</Button>
                      <Button variant="ghost" onClick={() => setDiscard(l)}>Descartar</Button>
                    </>
                  )}
                </span>
              </li>
            ))}
          </ul>
        )}
        <p className="mt-2 text-xs text-muted">As baixas seguem a regra “primeiro que vence, primeiro que sai”.</p>
      </Card>

      <Card>
        <h2 className="mb-2 text-lg font-bold">Histórico</h2>
        {history.isLoading && <Spinner />}
        <ul className="divide-y divide-slate-200">
          {history.data?.pages.flatMap((p) => p.itens).map((m) => (
            <li key={m.id} className="flex flex-wrap items-center justify-between gap-2 py-2 text-sm">
              <span>
                <Badge tone={m.sentido > 0 ? 'ok' : 'muted'}>{movementLabels[m.tipo] ?? m.tipo}</Badge>
                <span className="ml-2">{m.motivo}</span>
              </span>
              <span className="text-muted">
                <strong className={m.sentido > 0 ? 'text-emerald-800' : 'text-ink'}>{m.sentido > 0 ? '+' : '−'}{formatQuantity(m.quantidade, m.unidade)}</strong>
                {' · '}{m.usuarioNome ?? 'Conta removida'} · {formatDateTime(m.criadoEm)}
              </span>
            </li>
          ))}
        </ul>
        {history.hasNextPage && <Button variant="secondary" className="mt-3" busy={history.isFetchingNextPage} onClick={() => history.fetchNextPage()}>Mostrar mais</Button>}
      </Card>

      {kind && <MovementModal kind={kind} ing={ing} onClose={() => setKind(null)} />}
      {editing && <IngredientFormModal open initial={ing} onClose={() => setEditing(false)} />}
      {lotEdit && <LotModal lot={lotEdit} onClose={() => setLotEdit(null)} />}
      <ConfirmDialog
        open={!!discard} onClose={() => setDiscard(null)} danger busy={discardLot.isPending} confirmLabel="Descartar lote"
        title="Descartar lote?"
        message={<><p>O saldo restante deste lote ({discard ? formatQuantity(discard.quantidade, ing.unidadeReferencia) : ''}) será registrado como perda.</p><ErrorNote error={discardLot.error} /></>}
        onConfirm={() => discard && discardLot.mutate(discard)}
      />
      <ConfirmDialog
        open={removing} onClose={() => setRemoving(false)} danger busy={remove.isPending} confirmLabel="Excluir"
        title="Excluir ingrediente?"
        message={<><p>Só é possível excluir ingredientes sem histórico e sem uso em receitas ou listas. Caso contrário, arquive.</p>
          {remove.error instanceof ApiError && <p role="alert" className="mt-2 font-medium text-red-800">{remove.error.message}</p>}</>}
        onConfirm={() => remove.mutate()}
      />
    </>
  )
}

function MovementModal({ kind, ing, onClose }: { kind: Kind; ing: IngredienteDetalhe; onClose: () => void }) {
  const units = useUnits()
  const client = useQueryClient()
  const { notify } = useToast()
  const [quantidade, setQuantidade] = useState('')
  const [unidade, setUnidade] = useState(ing.unidadeReferencia)
  const [validade, setValidade] = useState('')
  const [motivo, setMotivo] = useState('')
  const [direcao, setDirecao] = useState<'mais' | 'menos'>('menos')
  const [loteId, setLoteId] = useState('')
  const [key] = useState(newKey)

  const sameDimension = units.filter((u) => {
    const ref = units.find((x) => x.codigo === ing.unidadeReferencia)
    return ref && u.dimensao === ref.dimensao || (ref?.dimensao === 'volume' && u.dimensao === 'culinaria')
  })

  const run = useMutation({
    mutationFn: () => {
      const q = Number(quantidade.replace(',', '.'))
      const body: Record<string, unknown> = { ingredienteId: ing.id, quantidade: q, unidade }
      if (kind === 'entrada') { body.dataValidade = validade || null; body.motivo = motivo || undefined }
      if (kind === 'saida' || kind === 'perda') { body.motivo = motivo || (kind === 'perda' ? 'Perda' : 'Consumo'); body.loteId = loteId || null }
      if (kind === 'correcao') { body.delta = direcao === 'mais' ? q : -q; body.motivo = motivo; delete body.quantidade; body.loteId = loteId || null }
      return post(paths[kind], body, key)
    },
    onSuccess: async () => { await client.invalidateQueries(); notify('Estoque atualizado.'); onClose() },
  })
  const errors = fieldErrors(run.error)
  const submit = (e: FormEvent) => { e.preventDefault(); run.mutate() }

  return (
    <Modal open onClose={onClose} title={`${titles[kind]} — ${ing.nome}`}>
      <form onSubmit={submit} className="space-y-3" noValidate>
        {kind === 'correcao' && (
          <Select label="Tipo de correção" value={direcao} onChange={(e) => setDirecao(e.target.value as 'mais' | 'menos')}>
            <option value="menos">Tinha menos do que o sistema mostra (diminuir)</option>
            <option value="mais">Tinha mais do que o sistema mostra (aumentar)</option>
          </Select>
        )}
        <div className="grid grid-cols-2 gap-3">
          <TextInput label="Quantidade" inputMode="decimal" required autoFocus value={quantidade} onChange={(e) => setQuantidade(e.target.value)} error={errors.quantidade ?? errors.delta} />
          <Select label="Unidade" value={unidade} onChange={(e) => setUnidade(e.target.value)}>
            {sameDimension.map((u) => <option key={u.codigo} value={u.codigo}>{u.nome}{u.aproximada ? ' (aprox.)' : ''}</option>)}
          </Select>
        </div>
        {kind === 'entrada' && <TextInput label="Data de validade (opcional)" type="date" value={validade} onChange={(e) => setValidade(e.target.value)} />}
        {(kind === 'saida' || kind === 'perda' || (kind === 'correcao' && direcao === 'menos')) && ing.lotes.length > 0 && (
          <Select label="Lote (opcional)" value={loteId} onChange={(e) => setLoteId(e.target.value)} hint="Sem escolher, usamos o que vence primeiro.">
            <option value="">Automático (vence primeiro)</option>
            {ing.lotes.map((l) => <option key={l.id} value={l.id}>{formatQuantity(l.quantidade, ing.unidadeReferencia)} · {formatDate(l.dataValidade)}</option>)}
          </Select>
        )}
        {kind !== 'entrada' && (
          <TextArea label={kind === 'correcao' ? 'Motivo da correção' : 'Motivo'} required={kind !== 'perda' || false} value={motivo} onChange={(e) => setMotivo(e.target.value)} error={errors.motivo} />
        )}
        <ErrorNote error={Object.keys(errors).length ? null : run.error} />
        <div className="flex justify-end gap-2 pt-2">
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" busy={run.isPending}>Confirmar</Button>
        </div>
      </form>
    </Modal>
  )
}

function LotModal({ lot, onClose }: { lot: Lote; onClose: () => void }) {
  const client = useQueryClient()
  const { notify } = useToast()
  const [validade, setValidade] = useState(lot.dataValidade ?? '')
  const [obs, setObs] = useState(lot.observacao ?? '')
  const save = useMutation({
    mutationFn: () => patch(`/lotes/${lot.id}`, { dataValidade: validade || null, observacao: obs || null }),
    onSuccess: async () => { await client.invalidateQueries(); notify('Lote atualizado.'); onClose() },
  })
  return (
    <Modal open onClose={onClose} title="Editar lote">
      <form onSubmit={(e) => { e.preventDefault(); save.mutate() }} className="space-y-3">
        <TextInput label="Data de validade" type="date" value={validade} onChange={(e) => setValidade(e.target.value)} />
        <TextArea label="Observação" value={obs} onChange={(e) => setObs(e.target.value)} />
        <ErrorNote error={save.error} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" busy={save.isPending}>Salvar</Button>
        </div>
      </form>
    </Modal>
  )
}
