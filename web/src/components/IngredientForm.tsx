import { useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { fieldErrors, post, put } from '../lib/api'
import { useCategories, useGroups, useUnits } from '../lib/hooks'
import { useSession } from '../lib/session'
import type { IngredienteDetalhe } from '../lib/types'
import { Button, ErrorNote, Modal, Select, TextArea, TextInput, useToast } from './ui'

export function IngredientFormModal({ open, onClose, initial, onSaved }: {
  open: boolean; onClose: () => void; initial?: IngredienteDetalhe; onSaved?: (id: string) => void
}) {
  const { pantry } = useSession()
  const categories = useCategories(pantry?.id)
  const groups = useGroups(pantry?.id)
  const units = useUnits()
  const client = useQueryClient()
  const { notify } = useToast()
  const [nome, setNome] = useState(initial?.nome ?? '')
  const [categoriaId, setCategoriaId] = useState(initial?.categoriaId ?? '')
  const [unidade, setUnidade] = useState(initial?.unidadeReferencia ?? 'G')
  const [grupo, setGrupo] = useState(initial?.grupoEquivalenciaId ?? '')
  const [minimo, setMinimo] = useState(String(initial?.quantidadeMinima ?? 0))
  const [local, setLocal] = useState(initial?.localArmazenamento ?? '')
  const [obs, setObs] = useState(initial?.observacoes ?? '')
  const [newCategory, setNewCategory] = useState('')

  const save = useMutation({
    mutationFn: async () => {
      let category = categoriaId
      if (category === '__nova') {
        const created = await post<{ id: string }>('/categorias', { nome: newCategory })
        category = created.id
      }
      const body = {
        nome, categoriaId: category || categories.data?.[0]?.id, unidadeReferencia: unidade,
        grupoEquivalenciaId: grupo || null, quantidadeMinima: Number(minimo.replace(',', '.')) || 0,
        localArmazenamento: local || null, observacoes: obs || null,
      }
      return initial ? put<{ id: string }>(`/ingredientes/${initial.id}`, body) : post<{ id: string }>('/ingredientes', body)
    },
    onSuccess: async (r) => {
      await client.invalidateQueries()
      notify(initial ? 'Ingrediente atualizado.' : 'Ingrediente criado.')
      onSaved?.(r.id)
      onClose()
    },
  })
  const errors = fieldErrors(save.error)
  const submit = (e: FormEvent) => { e.preventDefault(); save.mutate() }
  const lockUnit = !!initial && initial.quantidadeTotal > 0

  return (
    <Modal open={open} onClose={onClose} title={initial ? 'Editar ingrediente' : 'Novo ingrediente'}>
      <form onSubmit={submit} className="space-y-3" noValidate>
        <TextInput label="Nome" required value={nome} onChange={(e) => setNome(e.target.value)} error={errors.nome} />
        <Select label="Categoria" value={categoriaId || categories.data?.[0]?.id || ''} onChange={(e) => setCategoriaId(e.target.value)}>
          {categories.data?.map((c) => <option key={c.id} value={c.id}>{c.nome}</option>)}
          <option value="__nova">+ Nova categoria…</option>
        </Select>
        {categoriaId === '__nova' && <TextInput label="Nome da nova categoria" required value={newCategory} onChange={(e) => setNewCategory(e.target.value)} />}
        <div className="grid grid-cols-2 gap-3">
          <Select label="Unidade de referência" value={unidade} onChange={(e) => setUnidade(e.target.value)} disabled={lockUnit}
            hint={lockUnit ? 'Só muda com o estoque zerado.' : 'O estoque é guardado nesta unidade.'}>
            {units.filter((u) => !u.aproximada).map((u) => <option key={u.codigo} value={u.codigo}>{u.nome}</option>)}
          </Select>
          <TextInput label="Quantidade mínima" inputMode="decimal" value={minimo} onChange={(e) => setMinimo(e.target.value)} error={errors.quantidadeMinima} hint="Avisa quando ficar abaixo." />
        </div>
        <Select label="Grupo de equivalência (opcional)" value={grupo} onChange={(e) => setGrupo(e.target.value)} hint="Ingredientes do mesmo grupo podem substituir um ao outro nas receitas.">
          <option value="">Nenhum</option>
          {groups.data?.map((g) => <option key={g.id} value={g.id}>{g.nome}</option>)}
        </Select>
        <TextInput label="Local de armazenamento (opcional)" value={local} onChange={(e) => setLocal(e.target.value)} />
        <TextArea label="Observações (opcional)" value={obs} onChange={(e) => setObs(e.target.value)} />
        <ErrorNote error={Object.keys(errors).length ? null : save.error} />
        <div className="flex justify-end gap-2 pt-2">
          <Button variant="secondary" onClick={onClose}>Cancelar</Button>
          <Button type="submit" busy={save.isPending}>Salvar</Button>
        </div>
      </form>
    </Modal>
  )
}

export async function archiveIngredient(id: string, archive: boolean) {
  return post(`/ingredientes/${id}/${archive ? 'arquivar' : 'reativar'}`)
}

