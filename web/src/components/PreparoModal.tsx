import { useEffect, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError, get, newKey, post } from '../lib/api'
import type { PreparoPreview } from '../lib/types'
import { Badge, Button, ErrorNote, Modal, Spinner, useToast } from './ui'
import { formatDate, formatQuantity, lineStatusInfo } from '../lib/format'

export function PreparoModal({ recipeId, servings, includeOptionals, onClose }: { recipeId: string; servings: number; includeOptionals: boolean; onClose: () => void }) {
  const client = useQueryClient()
  const { notify } = useToast()
  const [preview, setPreview] = useState<PreparoPreview | null>(null)
  const [useAvailable, setUseAvailable] = useState(false)
  const [key] = useState(newKey)
  const started = useRef(false)

  const start = useMutation({
    mutationFn: () => post<PreparoPreview>('/preparos', { receitaId: recipeId, porcoes: servings, incluirOpcionais: includeOptionals }),
    onSuccess: setPreview,
  })
  useEffect(() => {
    if (started.current) return
    started.current = true
    start.mutate()
  }, [start])

  const confirm = useMutation({
    mutationFn: () => post(`/preparos/${preview!.preparoId}/confirmar`, { consumirDisponivel: useAvailable }, key),
    onSuccess: async () => { await client.invalidateQueries(); notify('Preparo confirmado e estoque atualizado.'); onClose() },
    onError: async (error) => {
      if (error instanceof ApiError && error.code === 'ESTOQUE_INSUFICIENTE' && preview) {
        setPreview(await get<PreparoPreview>(`/preparos/${preview.preparoId}`))
      }
    },
  })
  const cancel = async () => {
    if (preview && !confirm.isSuccess) await post(`/preparos/${preview.preparoId}/cancelar`).catch(() => undefined)
    onClose()
  }

  const shortages = confirm.error instanceof ApiError && confirm.error.code === 'ESTOQUE_INSUFICIENTE'
    ? ((confirm.error.details as { faltantes?: { texto?: string; nome?: string; faltante?: number; necessario?: number; disponivel?: number; unidade: string }[] })?.faltantes ?? [])
    : []

  return (
    <Modal open onClose={cancel} title="Preparar receita" wide>
      {(start.isPending || !preview) && !start.error && <Spinner label="Conferindo o estoque…" />}
      <ErrorNote error={start.error} />
      {preview && (
        <>
          <p className="mb-3">Você vai preparar <strong>{preview.receitaTitulo}</strong> para <strong>{preview.porcoes}</strong> porções. Revise o que será baixado do estoque.</p>
          <ul className="divide-y divide-slate-200 rounded-lg border border-slate-300">
            {preview.itens.map((i) => {
              const info = lineStatusInfo[i.status]
              return (
                <li key={i.ordem} className="p-3 text-sm">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-semibold">{i.texto}{i.opcional && <span className="ml-1 font-normal text-muted">(opcional)</span>}</span>
                    <span className="flex items-center gap-2">
                      {i.quantidade !== null && <span>{formatQuantity(i.quantidade, i.unidade)}</span>}
                      <Badge tone={info.tone}>{info.label}</Badge>
                    </span>
                  </div>
                  {i.naoDescontavel && <p className="mt-1 text-amber-900">{i.motivoNaoDescontavel}</p>}
                  {i.faltante > 0 && <p className="mt-1 text-red-800">Faltam {formatQuantity(i.faltante, i.unidade)} em estoque.</p>}
                  {i.baixas.map((b) => (
                    <div key={b.ingredienteId} className="mt-1 text-muted">
                      Baixar {formatQuantity(b.quantidade, b.unidade)} de <strong>{b.nome}</strong>{b.substituto && ' (equivalente)'}{b.aproximado && ' — conversão aproximada'}
                      {b.lotes.length > 0 && <> · lotes: {b.lotes.map((l) => `${formatQuantity(l.quantidade, b.unidade)} (${formatDate(l.validade)})`).join(', ')}</>}
                    </div>
                  ))}
                </li>
              )
            })}
          </ul>
          {(preview.temFaltantes || shortages.length > 0) && (
            <div role="alert" className="mt-3 rounded-lg border border-red-300 bg-red-50 p-3 text-sm text-red-900">
              <p className="font-semibold">Não há estoque suficiente para todos os ingredientes.</p>
              {shortages.map((s, idx) => <p key={idx}>{s.texto ?? s.nome}: faltam {formatQuantity(s.faltante ?? null, s.unidade)}</p>)}
              <label className="mt-2 flex min-h-11 items-center gap-2 font-medium">
                <input type="checkbox" className="size-5" checked={useAvailable} onChange={(e) => setUseAvailable(e.target.checked)} />
                Usar apenas o que há disponível (as faltas não são baixadas)
              </label>
            </div>
          )}
          {preview.temNaoDescontaveis && <p className="mt-3 text-sm text-amber-900">Itens com vínculo incerto, sem quantidade ou com unidade incompatível não serão baixados.</p>}
          {preview.expirado && <p role="alert" className="mt-3 font-medium text-red-800">Esta conferência expirou. Feche e inicie o preparo novamente.</p>}
          {confirm.error && shortages.length === 0 && <div className="mt-3"><ErrorNote error={confirm.error} /></div>}
          <div className="mt-5 flex justify-end gap-2">
            <Button variant="secondary" onClick={cancel}>Cancelar</Button>
            <Button
              busy={confirm.isPending}
              disabled={preview.expirado || (preview.temFaltantes && !useAvailable)}
              onClick={() => confirm.mutate()}
            >
              Confirmar preparo
            </Button>
          </div>
        </>
      )}
    </Modal>
  )
}
