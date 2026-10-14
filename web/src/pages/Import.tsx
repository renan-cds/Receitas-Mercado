import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { get, post } from '../lib/api'
import { useSession } from '../lib/session'
import { usePageTitle } from '../lib/hooks'
import type { Rascunho } from '../lib/types'
import { Badge, Button, Card, ErrorNote, PageTitle, TextArea, TextInput } from '../components/ui'
import { RecipeEditor, fromDraft } from '../components/RecipeEditor'

interface SearchResult { fonte: { nome: string; url: string }; aviso: string; itens: Rascunho[] }

export function ImportPage() {
  usePageTitle('Importar receita')
  const { pantry, canEdit, isAdmin } = useSession()
  const navigate = useNavigate()
  const [draft, setDraft] = useState<Rascunho | null>(null)
  const [url, setUrl] = useState('')
  const [q, setQ] = useState('')
  const [ia, setIa] = useState('')
  const [results, setResults] = useState<SearchResult | null>(null)
  const status = useQuery({ queryKey: ['ia-status', pantry?.id], queryFn: () => get<{ disponivel: boolean }>('/ia/status'), enabled: !!pantry })

  const importUrl = useMutation({ mutationFn: () => post<Rascunho>('/receitas/importar-url', { url }), onSuccess: setDraft })
  const search = useMutation({ mutationFn: () => get<SearchResult>(`/receitas/busca-externa?q=${encodeURIComponent(q)}`), onSuccess: setResults })
  const generate = useMutation({
    mutationFn: () => post<Rascunho>('/ia/gerar-receita', { ingredientes: ia.split(/[,\n]/).map((s) => s.trim()).filter(Boolean), porcoes: 4 }),
    onSuccess: setDraft,
  })
  const suggest = useMutation({
    mutationFn: () => post<{ itens: Rascunho[] }>('/ia/sugerir-receitas', { quantidade: 3 }),
    onSuccess: (r) => setResults({ fonte: { nome: 'Sugestões de IA', url: '' }, aviso: 'Receitas geradas por IA com base no seu estoque. Revise antes de salvar.', itens: r.itens }),
  })

  if (!canEdit) return <ErrorNote error={new Error('Seu papel nesta despensa não permite importar receitas.')} />

  if (draft) {
    return (
      <>
        <PageTitle actions={<Button variant="secondary" onClick={() => setDraft(null)}>Voltar</Button>}>Revisar antes de salvar</PageTitle>
        <p className="mb-4 text-sm text-muted">Nada foi salvo ainda. Confira os dados, confirme os vínculos com o estoque e salve.</p>
        <RecipeEditor key={draft.titulo + draft.urlOrigem} initial={fromDraft(draft)} onSaved={(id) => navigate(`/receitas/${id}`)} />
      </>
    )
  }

  return (
    <>
      <PageTitle>Importar ou buscar receitas</PageTitle>
      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <h2 className="mb-2 text-lg font-bold">Importar por link</h2>
          <p className="mb-3 text-sm text-muted">Cole o endereço de uma página de receita. Lemos apenas os dados estruturados (título, ingredientes e etapas) e guardamos o link e a autoria originais.</p>
          <form onSubmit={(e) => { e.preventDefault(); importUrl.mutate() }} className="space-y-3">
            <TextInput label="Endereço (https://…)" type="url" required value={url} onChange={(e) => setUrl(e.target.value)} />
            <ErrorNote error={importUrl.error} />
            <Button type="submit" busy={importUrl.isPending}>Importar</Button>
          </form>
        </Card>
        <Card>
          <h2 className="mb-2 text-lg font-bold">Buscar no TheMealDB</h2>
          <p className="mb-3 text-sm text-muted">Fonte gratuita com receitas em inglês. Tente termos como “chicken”, “pasta” ou “cake”.</p>
          <form onSubmit={(e) => { e.preventDefault(); search.mutate() }} className="space-y-3">
            <TextInput label="Termo de busca" required minLength={2} value={q} onChange={(e) => setQ(e.target.value)} />
            <ErrorNote error={search.error} />
            <Button type="submit" busy={search.isPending}>Buscar</Button>
          </form>
        </Card>
        <Card className="lg:col-span-2">
          <h2 className="mb-2 text-lg font-bold">Com inteligência artificial (opcional)</h2>
          {status.data?.disponivel ? (
            <div className="grid gap-4 md:grid-cols-2">
              <form onSubmit={(e) => { e.preventDefault(); generate.mutate() }} className="space-y-3">
                <TextArea label="Ingredientes que você quer usar" required value={ia} onChange={(e) => setIa(e.target.value)} hint="Separe por vírgula ou linha." />
                <ErrorNote error={generate.error} />
                <Button type="submit" busy={generate.isPending}>Gerar receita</Button>
              </form>
              <div className="space-y-3">
                <p className="text-sm text-muted">Ou peça sugestões com o que está no estoque, priorizando o que vence antes.</p>
                <ErrorNote error={suggest.error} />
                <Button variant="secondary" busy={suggest.isPending} onClick={() => suggest.mutate()}>Sugerir com meu estoque</Button>
              </div>
            </div>
          ) : (
            <p className="text-sm text-muted">A IA não está configurada. O aplicativo funciona normalmente sem ela.{isAdmin && ' Configure em “Mais”.'}</p>
          )}
          <p className="mt-3 text-xs text-muted">Receitas geradas por IA são rascunhos: quantidades, tempos e etapas podem conter erros.</p>
        </Card>
      </div>

      {results && (
        <section className="mt-6" aria-labelledby="resultados">
          <h2 id="resultados" className="text-lg font-bold">Resultados — {results.fonte.nome}</h2>
          <p className="mb-3 text-sm text-muted">{results.aviso}</p>
          {results.itens.length === 0 && <p>Nenhum resultado encontrado.</p>}
          <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {results.itens.map((r, i) => (
              <li key={i} className="flex flex-col gap-2 rounded-xl border border-slate-300 bg-white p-3">
                {r.imagemUrl && <img src={r.imagemUrl} alt="" referrerPolicy="no-referrer" loading="lazy" className="h-32 w-full rounded object-cover" />}
                <h3 className="font-bold">{r.titulo}</h3>
                <p className="text-xs text-muted">{r.ingredientes.length} ingredientes · {r.etapas.length} etapas</p>
                <Badge tone="info">{r.origem === 'ia' ? 'IA' : 'TheMealDB'}</Badge>
                <Button className="mt-auto" onClick={() => setDraft(r)}>Revisar e salvar</Button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  )
}

