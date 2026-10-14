import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useEffect, useRef } from 'react'
import { useSession } from '../lib/session'
import { useOnline } from '../lib/hooks'

const links = [
  { to: '/', label: 'Início', icon: '🏠', end: true },
  { to: '/estoque', label: 'Estoque', icon: '🥫' },
  { to: '/receitas', label: 'Receitas', icon: '🍲' },
  { to: '/compras', label: 'Compras', icon: '🛒' },
  { to: '/mais', label: 'Mais', icon: '⚙️' },
]

export function Layout() {
  const { me, pantry, switchPantry } = useSession()
  const online = useOnline()
  const location = useLocation()
  const main = useRef<HTMLElement>(null)

  useEffect(() => { main.current?.focus({ preventScroll: true }); window.scrollTo(0, 0) }, [location.pathname])

  const navClass = ({ isActive }: { isActive: boolean }) =>
    `flex min-h-12 flex-col items-center justify-center gap-0.5 rounded-lg px-3 py-1 text-xs font-semibold md:flex-row md:gap-2 md:text-sm ${isActive ? 'bg-brand-100 text-brand-800' : 'text-muted hover:bg-slate-100'}`

  return (
    <div className="min-h-dvh pb-20 md:pb-0">
      <a href="#conteudo" className="sr-only-focusable fixed left-2 top-2 z-50 rounded bg-white px-3 py-2 font-semibold shadow">Pular para o conteúdo</a>
      {!online && (
        <div role="status" className="bg-amber-200 px-4 py-2 text-center text-sm font-semibold text-amber-950">
          Você está offline. Os dados exibidos podem estar desatualizados e as alterações não serão enviadas até a conexão voltar.
        </div>
      )}
      <header className="sticky top-0 z-30 border-b border-slate-300 bg-white/95 backdrop-blur">
        <div className="mx-auto flex max-w-6xl items-center gap-3 px-4 py-2">
          <span className="text-lg font-bold text-brand-700">Receitas Mercado</span>
          {me.despensas.length > 1 ? (
            <label className="ml-auto flex items-center gap-2 text-sm">
              <span className="sr-only md:not-sr-only">Despensa</span>
              <select
                value={pantry?.id ?? ''}
                onChange={(e) => switchPantry(e.target.value)}
                className="min-h-11 rounded-lg border border-slate-500 bg-white px-2 py-1"
              >
                {me.despensas.map((d) => <option key={d.id} value={d.id}>{d.nome}</option>)}
              </select>
            </label>
          ) : (
            <span className="ml-auto truncate text-sm text-muted">{pantry?.nome}</span>
          )}
          <nav aria-label="Principal" className="hidden gap-1 md:flex">
            {links.map((l) => <NavLink key={l.to} to={l.to} end={l.end} className={navClass}>{l.label}</NavLink>)}
          </nav>
        </div>
      </header>
      <main id="conteudo" ref={main} tabIndex={-1} className="mx-auto max-w-6xl px-4 py-5 outline-none">
        <Outlet />
      </main>
      <nav aria-label="Principal" className="fixed inset-x-0 bottom-0 z-40 grid grid-cols-5 border-t border-slate-300 bg-white px-1 py-1 md:hidden">
        {links.map((l) => (
          <NavLink key={l.to} to={l.to} end={l.end} className={navClass}>
            <span aria-hidden="true" className="text-lg">{l.icon}</span>
            {l.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
