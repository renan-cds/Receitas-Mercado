import { useMemo, useState, type FormEvent } from 'react'
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api, fieldErrors, fileUrl, get, newKey, post, put } from '../lib/api'
import { useSession } from '../lib/session'
import { useUnits } from '../lib/hooks'
import type { Candidato, Confianca, IngredienteItem, Page, Rascunho, ReceitaDetalhe } from '../lib/types'
import { Badge, Button, Card, ErrorNote, Select, TextArea, TextInput, useToast } from './ui'

interface FormLine {
  key: string; nome: string; textoOriginal: string; quantidade: string; unidade: string; opcional: boolean
  ingredienteId: string; confianca: Confianca; candidatos: Candidato[]
}
export interface EditorInitial {
  titulo: string; descricao: string; porcoes: string; tempo: string; dificuldade: string; tags: string; observacoes: string
  imagemUrl: string; imagemArquivoId: string | null; autor: string; urlOrigem: string; origem: string
  status: 'rascunho' | 'completa'; etapas: string[]; linhas: FormLine[]; avisos: string[]
}

let counter = 0
const lineKey = () => `l${++counter}`

export const emptyLine = (): FormLine => ({
  key: lineKey(), nome: '', textoOriginal: '', quantidade: '', unidade: 'G', opcional: false, ingredienteId: '', confianca: 'confirmada', candidatos: [],
})

export function fromDetail(r: ReceitaDetalhe): EditorInitial {
  return {
    titulo: r.titulo, descricao: r.descricao ?? '', porcoes: String(r.porcoes), tempo: r.tempoPreparoMin ? String(r.tempoPreparoMin) : '',
    dificuldade: r.dificuldade ?? '', tags: r.tags.join(', '), observacoes: r.observacoes ?? '', imagemUrl: r.imagemUrl ?? '',
    imagemArquivoId: r.imagemArquivoId, autor: r.autorAtribuicao ?? '', urlOrigem: r.urlOrigem ?? '', origem: r.origem, status: r.status,
    etapas: r.etapas.length ? r.etapas.map((e) => e.instrucao) : [''],
    linhas: r.ingredientes.map((l) => ({
      key: lineKey(), nome: l.nome, textoOriginal: l.textoOriginal ?? '', quantidade: l.quantidade !== null ? String(l.quantidade) : '',
      unidade: l.unidade ?? 'G', opcional: l.opcional, ingredienteId: l.ingredienteId ?? '', confianca: l.confianca, candidatos: [],
    })),
    avisos: [],
  }
}

export function fromDraft(d: Rascunho): EditorInitial {
  return {
    titulo: d.titulo, descricao: d.descricao ?? '', porcoes: String(d.porcoes), tempo: d.tempoPreparoMin ? String(d.tempoPreparoMin) : '',
    dificuldade: '', tags: d.tags.join(', '), observacoes: '', imagemUrl: d.imagemUrl ?? '', imagemArquivoId: null,
    autor: d.autorAtribuicao ?? '', urlOrigem: d.urlOrigem ?? '', origem: d.origem, status: d.status,
    etapas: d.etapas.length ? d.etapas : [''],
    linhas: d.ingredientes.map((l) => ({
      key: lineKey(), nome: l.nome, textoOriginal: l.textoOriginal ?? '', quantidade: l.quantidade !== null ? String(l.quantidade) : '',
      unidade: l.unidade ?? 'G', opcional: l.opcional, ingredienteId: l.ingredienteId ?? '', confianca: l.ingredienteId ? l.confianca : 'incerta',
      candidatos: l.candidatos,
    })),
    avisos: d.avisos,
  }
}

export const blankInitial = (): EditorInitial => ({
  titulo: '', descricao: '', porcoes: '4', tempo: '', dificuldade: '', tags: '', observacoes: '', imagemUrl: '', imagemArquivoId: null,
  autor: '', urlOrigem: '', origem: 'manual', status: 'completa', etapas: [''], linhas: [emptyLine()], avisos: [],
})

export function RecipeEditor({ initial, recipeId, onSaved }: { initial: EditorInitial; recipeId?: string; onSaved: (id: string) => void }) {
  const { pantry } = useSession()
  const units = useUnits()
  const client = useQueryClient()
  const { notify } = useToast()
  const [form, setForm] = useState(initial)
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState<unknown>(null)
  const [key] = useState(newKey)

  const ingredients = useInfiniteQuery({
    queryKey: ['ingredientes', pantry?.id, 'todos'],
    initialPageParam: '' as string,
    queryFn: ({ pageParam }) => get<Page<IngredienteItem>>(`/ingredientes?limite=200${pageParam ? `&cursor=${pageParam}` : ''}`),
    getNextPageParam: (l) => l.proximoCursor ?? undefined,
  })
  if (ingredients.hasNextPage && !ingredients.isFetchingNextPage) void ingredients.fetchNextPage()
  const options = useMemo(() => ingredients.data?.pages.flatMap((p) => p.itens) ?? [], [ingredients.data])

  const set = <K extends keyof EditorInitial>(k: K, v: EditorInitial[K]) => setForm((f) => ({ ...f, [k]: v }))
  const setLine = (i: number, patch: Partial<FormLine>) => setForm((f) => ({ ...f, linhas: f.linhas.map((l, idx) => (idx === i ? { ...l, ...patch } : l)) }))
  const setStep = (i: number, v: string) => setForm((f) => ({ ...f, etapas: f.etapas.map((s, idx) => (idx === i ? v : s)) }))
  const moveStep = (i: number, d: -1 | 1) => setForm((f) => {
    const steps = [...f.etapas]
    const j = i + d
    if (j < 0 || j >= steps.length) return f
    ;[steps[i], steps[j]] = [steps[j], steps[i]]
    return { ...f, etapas: steps }
  })

  const save = useMutation({
    mutationFn: async (status: 'rascunho' | 'completa') => {
      const body = {
        titulo: form.titulo, descricao: form.descricao || null, porcoes: Number(form.porcoes.replace(',', '.')) || 4,
        tempoPreparoMin: form.tempo ? Number(form.tempo) : null, dificuldade: form.dificuldade || null, status,
        origem: form.origem, urlOrigem: form.urlOrigem || null, autorAtribuicao: form.autor || null,
        imagemArquivoId: form.imagemArquivoId, imagemUrl: form.imagemUrl || null, observacoes: form.observacoes || null,
        tags: form.tags.split(',').map((t) => t.trim()).filter(Boolean).slice(0, 10),
        etapas: form.etapas.map((s) => s.trim()).filter(Boolean),
        ingredientes: form.linhas.filter((l) => l.nome.trim()).map((l) => {
          const qty = l.quantidade.trim() === '' ? null : Number(l.quantidade.replace(',', '.'))
          return {
            nome: l.nome.trim(), textoOriginal: l.textoOriginal || null, ingredienteId: l.ingredienteId || null,
            quantidade: qty !== null && Number.isFinite(qty) ? qty : null, unidade: qty !== null ? l.unidade : null,
            opcional: l.opcional, confianca: l.ingredienteId ? l.confianca : null,
          }
        }),
      }
      return recipeId ? put<{ id: string }>(`/receitas/${recipeId}`, body) : post<{ id: string }>('/receitas', body, key)
    },
    onSuccess: async (r) => { await client.invalidateQueries(); notify('Receita salva.'); onSaved(r.id) },
  })
  const errors = fieldErrors(save.error)
  const submit = (status: 'rascunho' | 'completa') => (e: FormEvent) => { e.preventDefault(); save.mutate(status) }

  async function upload(file: File) {
    setUploading(true)
    setUploadError(null)
    try {
      const data = new FormData()
      data.append('arquivo', file)
      const r = await api<{ id: string }>('/arquivos', { method: 'POST', form: data })
      setForm((f) => ({ ...f, imagemArquivoId: r.id, imagemUrl: '' }))
    } catch (e) {
      setUploadError(e)
    } finally {
      setUploading(false)
    }
  }

  const preview = fileUrl(pantry?.id, form.imagemArquivoId) ?? (form.imagemUrl || null)

  return (
    <form onSubmit={submit('completa')} className="space-y-4" noValidate>
      {form.avisos.length > 0 && (
        <div role="note" className="rounded-lg border border-amber-400 bg-amber-50 p-3 text-sm text-amber-950">
          <ul className="list-disc space-y-1 pl-5">{form.avisos.map((a) => <li key={a}>{a}</li>)}</ul>
        </div>
      )}
      <Card className="space-y-3">
        <h2 className="text-lg font-bold">Dados gerais</h2>
        <TextInput label="Título" required value={form.titulo} onChange={(e) => set('titulo', e.target.value)} error={errors.titulo} />
        <TextArea label="Descrição" value={form.descricao} onChange={(e) => set('descricao', e.target.value)} />
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <TextInput label="Porções" inputMode="decimal" required value={form.porcoes} onChange={(e) => set('porcoes', e.target.value)} error={errors.porcoes} />
          <TextInput label="Tempo (min)" inputMode="numeric" value={form.tempo} onChange={(e) => set('tempo', e.target.value)} error={errors.tempoPreparoMin} />
          <Select label="Dificuldade" value={form.dificuldade} onChange={(e) => set('dificuldade', e.target.value)}>
            <option value="">—</option><option value="facil">Fácil</option><option value="medio">Médio</option><option value="dificil">Difícil</option>
          </Select>
          <TextInput label="Tags" value={form.tags} onChange={(e) => set('tags', e.target.value)} hint="Separe por vírgula." />
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <TextInput label="Autoria / fonte" value={form.autor} onChange={(e) => set('autor', e.target.value)} />
          <TextInput label="Endereço da receita original" type="url" value={form.urlOrigem} onChange={(e) => set('urlOrigem', e.target.value)} error={errors.urlOrigem} />
        </div>
        <div className="space-y-2">
          <p className="text-sm font-semibold">Imagem</p>
          {preview && <img src={preview} alt="Pré-visualização da imagem da receita" referrerPolicy="no-referrer" className="max-h-40 rounded-lg border border-slate-300" />}
          <div className="flex flex-wrap items-end gap-3">
            <label className="block text-sm font-medium">
              Enviar arquivo (JPEG, PNG ou WebP até 2 MB)
              <input type="file" accept="image/jpeg,image/png,image/webp" disabled={uploading} className="mt-1 block min-h-11 text-sm"
                onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
            </label>
            {preview && <Button variant="secondary" onClick={() => setForm((f) => ({ ...f, imagemArquivoId: null, imagemUrl: '' }))}>Remover imagem</Button>}
          </div>
          {!form.imagemArquivoId && <TextInput label="…ou endereço de uma imagem (https)" type="url" value={form.imagemUrl} onChange={(e) => set('imagemUrl', e.target.value)} error={errors.imagemUrl} />}
          <ErrorNote error={uploadError} />
        </div>
      </Card>

      <Card>
        <h2 className="mb-1 text-lg font-bold">Ingredientes</h2>
        <p className="mb-3 text-sm text-muted">Vincule cada item a um ingrediente do estoque para calcular o que dá para preparar. Vínculos sugeridos só valem depois de confirmados.</p>
        <ul className="space-y-4">
          {form.linhas.map((l, i) => {
            const needsConfirm = l.ingredienteId && l.confianca !== 'confirmada'
            return (
              <li key={l.key} className="rounded-lg border border-slate-300 p-3">
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-[1fr_6rem_9rem]">
                  <TextInput label="Ingrediente" className="col-span-2 sm:col-span-1" value={l.nome} onChange={(e) => setLine(i, { nome: e.target.value })} />
                  <TextInput label="Qtd." inputMode="decimal" value={l.quantidade} onChange={(e) => setLine(i, { quantidade: e.target.value })} hint="Vazio = a gosto" />
                  <Select label="Unidade" value={l.unidade} onChange={(e) => setLine(i, { unidade: e.target.value })}>
                    {units.map((u) => <option key={u.codigo} value={u.codigo}>{u.nome}</option>)}
                  </Select>
                </div>
                <div className="mt-3 grid gap-3 sm:grid-cols-[1fr_auto]">
                  <Select
                    label="Vínculo com o estoque" value={l.ingredienteId}
                    onChange={(e) => setLine(i, { ingredienteId: e.target.value, confianca: e.target.value ? 'confirmada' : 'incerta' })}
                  >
                    <option value="">Sem vínculo</option>
                    {l.candidatos.length > 0 && <optgroup label="Sugestões">{l.candidatos.map((c) => <option key={c.id} value={c.id}>{c.nome}</option>)}</optgroup>}
                    <optgroup label="Todos os ingredientes">
                      {options.map((o) => <option key={o.id} value={o.id}>{o.nome}</option>)}
                    </optgroup>
                  </Select>
                  <div className="flex items-end gap-2">
                    <label className="flex min-h-11 items-center gap-2 text-sm"><input type="checkbox" className="size-5" checked={l.opcional} onChange={(e) => setLine(i, { opcional: e.target.checked })} />Opcional</label>
                    <Button variant="ghost" aria-label={`Remover ingrediente ${l.nome || i + 1}`} onClick={() => set('linhas', form.linhas.filter((_, idx) => idx !== i))}>Remover</Button>
                  </div>
                </div>
                {needsConfirm && (
                  <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
                    <Badge tone="warn" icon="!">Vínculo sugerido, não confirmado</Badge>
                    <Button variant="secondary" onClick={() => setLine(i, { confianca: 'confirmada' })}>Confirmar vínculo</Button>
                  </div>
                )}
                {l.textoOriginal && <p className="mt-2 text-xs text-muted">Texto original: “{l.textoOriginal}”</p>}
              </li>
            )
          })}
        </ul>
        <Button variant="secondary" className="mt-3" onClick={() => set('linhas', [...form.linhas, emptyLine()])}>+ Adicionar ingrediente</Button>
        {errors.ingredientes && <p role="alert" className="mt-2 text-sm text-red-800">{errors.ingredientes}</p>}
      </Card>

      <Card>
        <h2 className="mb-3 text-lg font-bold">Modo de preparo</h2>
        <ol className="space-y-3">
          {form.etapas.map((s, i) => (
            <li key={i} className="rounded-lg border border-slate-300 p-3">
              <TextArea label={`Etapa ${i + 1}`} value={s} onChange={(e) => setStep(i, e.target.value)} />
              <div className="mt-2 flex gap-2">
                <Button variant="ghost" disabled={i === 0} onClick={() => moveStep(i, -1)} aria-label={`Mover etapa ${i + 1} para cima`}>↑ Subir</Button>
                <Button variant="ghost" disabled={i === form.etapas.length - 1} onClick={() => moveStep(i, 1)} aria-label={`Mover etapa ${i + 1} para baixo`}>↓ Descer</Button>
                <Button variant="ghost" onClick={() => set('etapas', form.etapas.filter((_, idx) => idx !== i))}>Remover</Button>
              </div>
            </li>
          ))}
        </ol>
        <Button variant="secondary" className="mt-3" onClick={() => set('etapas', [...form.etapas, ''])}>+ Adicionar etapa</Button>
      </Card>

      <Card><TextArea label="Observações" value={form.observacoes} onChange={(e) => set('observacoes', e.target.value)} /></Card>

      <ErrorNote error={Object.keys(errors).length ? null : save.error} />
      {Object.keys(errors).length > 0 && <p role="alert" className="text-sm font-medium text-red-800">Corrija os campos destacados e tente novamente.</p>}
      <div className="sticky bottom-20 flex flex-wrap justify-end gap-2 rounded-xl border border-slate-300 bg-white/95 p-3 md:bottom-2">
        <Button variant="secondary" busy={save.isPending} onClick={() => save.mutate('rascunho')}>Salvar como rascunho</Button>
        <Button type="submit" busy={save.isPending}>Salvar receita</Button>
      </div>
    </form>
  )
}
