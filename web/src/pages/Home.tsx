import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { get } from '../lib/api'
import { useSession } from '../lib/session'
import type { Page, ReceitaResumo, Resumo } from '../lib/types'
import { Badge, Card, Empty, ErrorNote, PageTitle, Spinner } from '../components/ui'
import { RecipeCard } from '../components/RecipeCard'
import { formatDate, formatQuantity } from '../lib/format'
import { usePageTitle } from '../lib/hooks'

export function HomePage() {
  usePageTitle('Início')
  const { pantry, me } = useSession()
  const summary = useQuery({ queryKey: ['resumo', pantry?.id], queryFn: () => get<Resumo>('/estoque/resumo'), enabled: !!pantry })
  const ready = useQuery({
    queryKey: ['receitas', pantry?.id, 'pode-home'],
    queryFn: () => get<Page<ReceitaResumo>>('/receitas?compat=pode&ordem=compat&limite=6'),
    enabled: !!pantry,
  })

  if (!pantry) return <Empty title="Você ainda não participa de nenhuma despensa">Crie uma em “Mais” ou aceite um convite.</Empty>
  const s = summary.data

  return (
    <>
      <PageTitle>Olá, {me.nome.split(' ')[0]}</PageTitle>
      <ErrorNote error={summary.error} onRetry={() => summary.refetch()} />
      {summary.isLoading && <Spinner />}
      {s && (
        <section aria-label="Resumo do estoque" className="mb-6 grid grid-cols-2 gap-3 md:grid-cols-4">
          <Stat to="/estoque" label="Ingredientes em estoque" value={s.comEstoque} />
          <Stat to="/estoque?situacao=vencendo" label={`Vencendo em ${s.alertaValidadeDias} dias`} value={s.lotesVencendo} tone={s.lotesVencendo ? 'warn' : undefined} />
          <Stat to="/estoque?situacao=vencido" label="Lotes vencidos" value={s.lotesVencidos} tone={s.lotesVencidos ? 'bad' : undefined} />
          <Stat to="/estoque?situacao=baixo" label="Abaixo do mínimo" value={s.abaixoDoMinimo} tone={s.abaixoDoMinimo ? 'warn' : undefined} />
        </section>
      )}

      {s && s.proximosVencimentos.length > 0 && (
        <Card className="mb-6">
          <h2 className="mb-2 text-lg font-bold">Próximos vencimentos</h2>
          <ul className="divide-y divide-slate-200">
            {s.proximosVencimentos.slice(0, 6).map((l) => (
              <li key={l.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                <Link className="font-semibold text-brand-700 underline" to={`/estoque/${l.ingredienteId}`}>{l.nome}</Link>
                <span className="text-sm text-muted">{formatQuantity(l.quantidade, l.unidade)}</span>
                <Badge tone={l.situacaoValidade === 'vencido' ? 'bad' : 'warn'} icon={l.situacaoValidade === 'vencido' ? '✕' : '!'}>
                  {l.situacaoValidade === 'vencido' ? 'Vencido em ' : 'Vence em '}{formatDate(l.dataValidade)}
                </Badge>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <section aria-labelledby="pode">
        <div className="mb-3 flex items-center justify-between">
          <h2 id="pode" className="text-lg font-bold">O que dá para preparar agora</h2>
          <Link to="/receitas?compat=pode" className="text-sm font-semibold text-brand-700 underline">Ver todas</Link>
        </div>
        {ready.isLoading && <Spinner />}
        <ErrorNote error={ready.error} onRetry={() => ready.refetch()} />
        {ready.data && ready.data.itens.length === 0 && (
          <Empty title="Nenhuma receita está 100% disponível no momento">
            Cadastre receitas, registre suas compras no <Link className="underline" to="/estoque">estoque</Link> ou veja as que estão quase lá em <Link className="underline" to="/receitas?ordem=compat">Receitas</Link>.
          </Empty>
        )}
        <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {ready.data?.itens.map((r) => <li key={r.id}><RecipeCard recipe={r} pantryId={pantry.id} /></li>)}
        </ul>
        <p className="mt-3 text-xs text-muted">A compatibilidade considera apenas ingredientes com vínculo confirmado ao estoque. Itens incertos aparecem como “verificar”.</p>
      </section>
    </>
  )
}

function Stat({ to, label, value, tone }: { to: string; label: string; value: number; tone?: 'warn' | 'bad' }) {
  const color = tone === 'bad' ? 'border-red-400 bg-red-50' : tone === 'warn' ? 'border-amber-400 bg-amber-50' : 'border-slate-300 bg-white'
  return (
    <Link to={to} className={`block rounded-xl border p-4 ${color}`}>
      <span className="block text-3xl font-bold">{value}</span>
      <span className="text-sm text-muted">{label}</span>
    </Link>
  )
}
