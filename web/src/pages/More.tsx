import { useState, type ReactNode } from 'react'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { del, get, patch, post, put, setActivePantry } from '../lib/api'
import { useSession } from '../lib/session'
import { useCategories, useGroups, usePageTitle } from '../lib/hooks'
import { formatDateTime, roleLabels } from '../lib/format'
import type { Convite, IaConfig, Membro, Page, Papel } from '../lib/types'
import { Badge, Button, Card, ConfirmDialog, Empty, ErrorNote, PageTitle, Select, Spinner, TextInput, useToast } from '../components/ui'

type Tab = 'conta' | 'despensa' | 'membros' | 'organizacao' | 'historico' | 'ia'

export function MorePage() {
  usePageTitle('Mais')
  const { isAdmin } = useSession()
  const [tab, setTab] = useState<Tab>('conta')
  const tabs: [Tab, string][] = [
    ['conta', 'Minha conta'], ['despensa', 'Despensa'], ['membros', 'Membros'], ['organizacao', 'Categorias e grupos'], ['historico', 'Histórico de preparos'],
    ...(isAdmin ? [['ia', 'Inteligência artificial'] as [Tab, string]] : []),
  ]
  return (
    <>
      <PageTitle>Mais opções</PageTitle>
      <div role="group" aria-label="Seções" className="mb-5 flex flex-wrap gap-2">
        {tabs.map(([key, label]) => (
          <button
            key={key}
            type="button"
            aria-pressed={tab === key}
            onClick={() => setTab(key)}
            className={`min-h-11 rounded-full border px-4 text-sm font-semibold ${tab === key ? 'border-brand-700 bg-brand-700 text-white' : 'border-slate-400 bg-white text-ink hover:bg-slate-50'}`}
          >
            {label}
          </button>
        ))}
      </div>
      {tab === 'conta' && <AccountSection />}
      {tab === 'despensa' && <PantrySection />}
      {tab === 'membros' && <MembersSection />}
      {tab === 'organizacao' && <OrganizationSection />}
      {tab === 'historico' && <HistorySection />}
      {tab === 'ia' && isAdmin && <AiSection />}
    </>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card className="mb-5 max-w-2xl">
      <h2 className="mb-3 text-lg font-bold">{title}</h2>
      {children}
    </Card>
  )
}

function AccountSection() {
  const { me, refresh, logout } = useSession()
  const { notify } = useToast()
  const [nome, setNome] = useState(me.nome)
  const [atual, setAtual] = useState('')
  const [nova, setNova] = useState('')
  const [senhaExclusao, setSenhaExclusao] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)

  const profile = useMutation({
    mutationFn: () => put('/auth/me', { nome }),
    onSuccess: async () => { await refresh(); notify('Perfil atualizado.') },
  })
  const password = useMutation({
    mutationFn: () => put('/auth/senha', { atual, nova }),
    onSuccess: () => { setAtual(''); setNova(''); notify('Senha alterada.') },
  })
  const remove = useMutation({
    mutationFn: () => del('/auth/me', { senha: senhaExclusao }),
    onSuccess: () => { setActivePantry(null); window.location.assign('/entrar') },
  })

  return (
    <>
      <Section title="Perfil">
        <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); profile.mutate() }}>
          <TextInput label="E-mail" value={me.email} readOnly />
          <TextInput label="Nome" required maxLength={80} value={nome} onChange={(e) => setNome(e.target.value)} />
          <ErrorNote error={profile.error} />
          <div><Button type="submit" busy={profile.isPending} disabled={!nome.trim() || nome === me.nome}>Salvar perfil</Button></div>
        </form>
      </Section>
      <Section title="Alterar senha">
        <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); password.mutate() }}>
          <TextInput label="Senha atual" type="password" autoComplete="current-password" required value={atual} onChange={(e) => setAtual(e.target.value)} />
          <TextInput label="Nova senha" type="password" autoComplete="new-password" required minLength={10} hint="Mínimo de 10 caracteres." value={nova} onChange={(e) => setNova(e.target.value)} />
          <ErrorNote error={password.error} />
          <div><Button type="submit" busy={password.isPending} disabled={!atual || nova.length < 10}>Alterar senha</Button></div>
        </form>
      </Section>
      <Section title="Sessão e dados">
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => void logout()}>Sair</Button>
          <Button variant="danger" onClick={() => setConfirmDelete(true)}>Excluir minha conta</Button>
        </div>
        <p className="mt-3 text-sm text-muted">Despensas em que você é a única pessoa serão excluídas junto com a conta. Em despensas compartilhadas, você apenas deixa de ser membro.</p>
      </Section>
      <ConfirmDialog
        open={confirmDelete}
        danger
        busy={remove.isPending}
        title="Excluir conta?"
        confirmLabel="Excluir definitivamente"
        message={
          <div className="grid gap-3">
            <p>Esta ação é permanente. Confirme com sua senha.</p>
            <TextInput label="Senha" type="password" autoComplete="current-password" value={senhaExclusao} onChange={(e) => setSenhaExclusao(e.target.value)} />
            <ErrorNote error={remove.error} />
          </div>
        }
        onConfirm={() => remove.mutate()}
        onClose={() => setConfirmDelete(false)}
      />
    </>
  )
}

function PantrySection() {
  const { pantry, me, isAdmin, switchPantry, refresh } = useSession()
  const client = useQueryClient()
  const { notify } = useToast()
  const settings = useQuery({
    queryKey: ['despensa', pantry?.id],
    queryFn: () => get<{ id: string; nome: string; fuso: string; alertaValidadeDias: number }>(`/despensas/${pantry!.id}`),
    enabled: !!pantry,
  })
  const [nome, setNome] = useState<string | null>(null)
  const [fuso, setFuso] = useState<string | null>(null)
  const [dias, setDias] = useState<string | null>(null)
  const [novaDespensa, setNovaDespensa] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [confirmLeave, setConfirmLeave] = useState(false)
  const [confirmName, setConfirmName] = useState('')

  const current = settings.data
  const save = useMutation({
    mutationFn: () => patch(`/despensas/${pantry!.id}`, {
      nome: nome ?? current!.nome, fuso: fuso ?? current!.fuso, alertaValidadeDias: Number(dias ?? current!.alertaValidadeDias),
    }),
    onSuccess: async () => {
      setNome(null); setFuso(null); setDias(null)
      await client.invalidateQueries({ queryKey: ['despensa'] })
      await client.invalidateQueries({ queryKey: ['resumo'] })
      await refresh()
      notify('Configurações salvas.')
    },
  })
  const create = useMutation({
    mutationFn: () => post<{ id: string }>('/despensas', { nome: novaDespensa }),
    onSuccess: async (r) => { await refresh(); switchPantry(r.id); setNovaDespensa(''); notify('Despensa criada.') },
  })
  const leave = useMutation({
    mutationFn: () => post(`/despensas/${pantry!.id}/sair`),
    onSuccess: async () => { setActivePantry(null); await client.clear(); window.location.assign('/') },
  })
  const remove = useMutation({
    mutationFn: () => del(`/despensas/${pantry!.id}`, { confirmacaoNome: confirmName }),
    onSuccess: async () => { setActivePantry(null); client.clear(); window.location.assign('/') },
  })

  if (!pantry) return <Empty title="Nenhuma despensa selecionada" />
  return (
    <>
      <Section title={`Despensa: ${pantry.nome}`}>
        <p className="mb-3 text-sm text-muted">Seu papel: <strong>{roleLabels[pantry.papel]}</strong>.</p>
        {settings.isLoading && <Spinner />}
        {current && (
          <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
            <TextInput label="Nome" required maxLength={80} disabled={!isAdmin} value={nome ?? current.nome} onChange={(e) => setNome(e.target.value)} />
            <TextInput label="Fuso horário" disabled={!isAdmin} hint="Usado para decidir o que está vencido ou vencendo. Ex.: America/Sao_Paulo." value={fuso ?? current.fuso} onChange={(e) => setFuso(e.target.value)} />
            <TextInput label="Avisar validade com antecedência de (dias)" type="number" min={1} max={90} disabled={!isAdmin} value={dias ?? String(current.alertaValidadeDias)} onChange={(e) => setDias(e.target.value)} />
            <ErrorNote error={save.error} />
            {isAdmin && <div><Button type="submit" busy={save.isPending}>Salvar</Button></div>}
          </form>
        )}
        <div className="mt-4 flex flex-wrap gap-2 border-t border-slate-200 pt-4">
          <Button variant="secondary" onClick={() => setConfirmLeave(true)}>Sair desta despensa</Button>
          {isAdmin && <Button variant="danger" onClick={() => setConfirmDelete(true)}>Excluir despensa</Button>}
        </div>
        <div className="mt-3"><ErrorNote error={leave.error} /></div>
      </Section>
      <Section title="Criar outra despensa">
        <form className="flex flex-wrap items-end gap-3" onSubmit={(e) => { e.preventDefault(); create.mutate() }}>
          <TextInput className="min-w-56 flex-1" label="Nome da nova despensa" maxLength={80} value={novaDespensa} onChange={(e) => setNovaDespensa(e.target.value)} />
          <Button type="submit" busy={create.isPending} disabled={!novaDespensa.trim() || me.despensas.length >= 5}>Criar</Button>
        </form>
        <div className="mt-3"><ErrorNote error={create.error} /></div>
      </Section>
      <ConfirmDialog
        open={confirmLeave}
        busy={leave.isPending}
        title="Sair da despensa?"
        confirmLabel="Sair"
        message={<p>Você perde o acesso a esta despensa. Seus catálogos pessoais e favoritas nela serão removidos. Se for o último administrador, será preciso promover outra pessoa antes.</p>}
        onConfirm={() => leave.mutate()}
        onClose={() => setConfirmLeave(false)}
      />
      <ConfirmDialog
        open={confirmDelete}
        danger
        busy={remove.isPending}
        title="Excluir despensa?"
        confirmLabel="Excluir definitivamente"
        message={
          <div className="grid gap-3">
            <p>Todo o estoque, receitas, listas e membros desta despensa serão apagados para sempre. Digite <strong>{pantry.nome}</strong> para confirmar.</p>
            <TextInput label="Nome da despensa" value={confirmName} onChange={(e) => setConfirmName(e.target.value)} />
            <ErrorNote error={remove.error} />
          </div>
        }
        onConfirm={() => remove.mutate()}
        onClose={() => setConfirmDelete(false)}
      />
    </>
  )
}

function MembersSection() {
  const { pantry, isAdmin, me } = useSession()
  const client = useQueryClient()
  const { notify } = useToast()
  const [papel, setPapel] = useState<Papel>('editor')
  const [link, setLink] = useState<string | null>(null)
  const members = useQuery({ queryKey: ['membros', pantry?.id], queryFn: () => get<Membro[]>(`/despensas/${pantry!.id}/membros`), enabled: !!pantry })
  const invites = useQuery({ queryKey: ['convites', pantry?.id], queryFn: () => get<Convite[]>(`/despensas/${pantry!.id}/convites`), enabled: !!pantry && isAdmin })

  const createInvite = useMutation({
    mutationFn: () => post<{ caminho: string }>(`/despensas/${pantry!.id}/convites`, { papel, validadeDias: 7 }),
    onSuccess: async (r) => { setLink(`${window.location.origin}${r.caminho}`); await client.invalidateQueries({ queryKey: ['convites'] }) },
  })
  const revoke = useMutation({
    mutationFn: (id: string) => del(`/despensas/${pantry!.id}/convites/${id}`),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: ['convites'] }); notify('Convite revogado.') },
  })
  const changeRole = useMutation({
    mutationFn: (v: { id: string; papel: Papel }) => patch(`/despensas/${pantry!.id}/membros/${v.id}`, { papel: v.papel }),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: ['membros'] }); notify('Papel atualizado.') },
  })
  const removeMember = useMutation({
    mutationFn: (id: string) => del(`/despensas/${pantry!.id}/membros/${id}`),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: ['membros'] }); notify('Membro removido.') },
  })
  const copy = async () => {
    if (!link) return
    try { await navigator.clipboard.writeText(link); notify('Link copiado.') } catch { notify('Não foi possível copiar; selecione o link manualmente.', 'bad') }
  }

  return (
    <>
      <Section title="Membros">
        <ErrorNote error={members.error ?? changeRole.error ?? removeMember.error} />
        {members.isLoading && <Spinner />}
        <ul className="divide-y divide-slate-200">
          {members.data?.map((m) => (
            <li key={m.usuarioId} className="flex flex-wrap items-center justify-between gap-3 py-2">
              <span>
                <span className="block font-semibold">{m.nome}{m.usuarioId === me.id && <span className="text-muted"> (você)</span>}</span>
                {m.email && <span className="text-xs text-muted">{m.email}</span>}
              </span>
              {isAdmin ? (
                <span className="flex items-center gap-2">
                  <label className="sr-only" htmlFor={`papel-${m.usuarioId}`}>Papel de {m.nome}</label>
                  <select
                    id={`papel-${m.usuarioId}`}
                    value={m.papel}
                    onChange={(e) => changeRole.mutate({ id: m.usuarioId, papel: e.target.value as Papel })}
                    className="min-h-11 rounded-lg border border-slate-500 bg-white px-2"
                  >
                    {(['admin', 'editor', 'leitor'] as Papel[]).map((p) => <option key={p} value={p}>{roleLabels[p]}</option>)}
                  </select>
                  {m.usuarioId !== me.id && (
                    <Button variant="ghost" aria-label={`Remover ${m.nome}`} onClick={() => removeMember.mutate(m.usuarioId)}>Remover</Button>
                  )}
                </span>
              ) : <Badge tone="muted">{roleLabels[m.papel]}</Badge>}
            </li>
          ))}
        </ul>
      </Section>
      {isAdmin && (
        <Section title="Convidar pessoas">
          <p className="mb-3 text-sm text-muted">Gere um link de uso único, válido por 7 dias, e envie à pessoa. O convite só concede papel de editor ou leitor.</p>
          <div className="flex flex-wrap items-end gap-3">
            <Select label="Papel" value={papel} onChange={(e) => setPapel(e.target.value as Papel)}>
              <option value="editor">Editor</option>
              <option value="leitor">Leitor</option>
            </Select>
            <Button busy={createInvite.isPending} onClick={() => createInvite.mutate()}>Gerar link</Button>
          </div>
          <div className="mt-3"><ErrorNote error={createInvite.error ?? revoke.error} /></div>
          {link && (
            <div className="mt-3 rounded-lg border border-brand-600 bg-brand-50 p-3">
              <p className="mb-2 text-sm font-semibold">Link do convite (mostrado apenas agora):</p>
              <input readOnly aria-label="Link do convite" value={link} onFocus={(e) => e.currentTarget.select()} className="mb-2 block min-h-11 w-full rounded-lg border border-slate-500 bg-white px-3 text-sm" />
              <Button variant="secondary" onClick={() => void copy()}>Copiar link</Button>
            </div>
          )}
          {invites.data && invites.data.length > 0 && (
            <div className="mt-4">
              <h3 className="mb-1 text-sm font-bold">Convites pendentes</h3>
              <ul className="divide-y divide-slate-200">
                {invites.data.map((i) => (
                  <li key={i.id} className="flex items-center justify-between gap-3 py-2 text-sm">
                    <span>{roleLabels[i.papel]} · expira em {formatDateTime(i.expiraEm)}</span>
                    <Button variant="ghost" onClick={() => revoke.mutate(i.id)}>Revogar</Button>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </Section>
      )}
    </>
  )
}

function NameList({ title, hint, items, canEdit, onCreate, onRename, onDelete, error, extra }: {
  title: string; hint: string; items: { id: string; nome: string; extra?: string }[]; canEdit: boolean
  onCreate: (nome: string) => Promise<unknown>; onRename: (id: string, nome: string) => Promise<unknown>; onDelete: (id: string) => Promise<unknown>
  error: unknown; extra?: ReactNode
}) {
  const [novo, setNovo] = useState('')
  const [editing, setEditing] = useState<string | null>(null)
  const [draft, setDraft] = useState('')
  const [busy, setBusy] = useState(false)
  const run = async (fn: () => Promise<unknown>) => { setBusy(true); try { await fn() } catch { /* o erro é exibido via query de origem */ } finally { setBusy(false) } }
  return (
    <Section title={title}>
      <p className="mb-3 text-sm text-muted">{hint}</p>
      <ErrorNote error={error} />
      {extra}
      <ul className="divide-y divide-slate-200">
        {items.map((i) => (
          <li key={i.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
            {editing === i.id ? (
              <form className="flex flex-1 flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); void run(async () => { await onRename(i.id, draft); setEditing(null) }) }}>
                <TextInput className="min-w-48 flex-1" label="Novo nome" maxLength={80} value={draft} onChange={(e) => setDraft(e.target.value)} />
                <Button type="submit" busy={busy} disabled={!draft.trim()}>Salvar</Button>
                <Button variant="secondary" onClick={() => setEditing(null)}>Cancelar</Button>
              </form>
            ) : (
              <>
                <span><span className="font-semibold">{i.nome}</span>{i.extra && <span className="block text-xs text-muted">{i.extra}</span>}</span>
                {canEdit && (
                  <span className="flex gap-1">
                    <Button variant="ghost" aria-label={`Renomear ${i.nome}`} onClick={() => { setEditing(i.id); setDraft(i.nome) }}>Renomear</Button>
                    <Button variant="ghost" aria-label={`Excluir ${i.nome}`} onClick={() => void run(() => onDelete(i.id))}>Excluir</Button>
                  </span>
                )}
              </>
            )}
          </li>
        ))}
      </ul>
      {canEdit && (
        <form className="mt-3 flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); void run(async () => { await onCreate(novo); setNovo('') }) }}>
          <TextInput className="min-w-48 flex-1" label="Adicionar" maxLength={80} value={novo} onChange={(e) => setNovo(e.target.value)} />
          <Button type="submit" busy={busy} disabled={!novo.trim()}>Adicionar</Button>
        </form>
      )}
    </Section>
  )
}

function OrganizationSection() {
  const { pantry, canEdit } = useSession()
  const client = useQueryClient()
  const { notify } = useToast()
  const categories = useCategories(pantry?.id)
  const groups = useGroups(pantry?.id)
  const [catError, setCatError] = useState<unknown>(null)
  const [groupError, setGroupError] = useState<unknown>(null)

  const wrap = (setError: (e: unknown) => void, invalidate: string, message: string, fn: () => Promise<unknown>) => async () => {
    try {
      setError(null)
      await fn()
      await client.invalidateQueries({ queryKey: [invalidate] })
      await client.invalidateQueries({ queryKey: ['ingredientes'] })
      notify(message)
    } catch (e) {
      setError(e)
      throw e
    }
  }

  return (
    <>
      <NameList
        title="Categorias"
        hint="Organizam o estoque (ex.: Grãos, Laticínios). Uma categoria só pode ser excluída quando não tem ingredientes."
        items={categories.data?.map((c) => ({ id: c.id, nome: c.nome, extra: `${c.ingredientes} ingredientes` })) ?? []}
        canEdit={canEdit}
        error={catError ?? categories.error}
        onCreate={(nome) => wrap(setCatError, 'categorias', 'Categoria criada.', () => post('/categorias', { nome }))()}
        onRename={(id, nome) => wrap(setCatError, 'categorias', 'Categoria renomeada.', () => patch(`/categorias/${id}`, { nome }))()}
        onDelete={(id) => wrap(setCatError, 'categorias', 'Categoria excluída.', () => del(`/categorias/${id}`))()}
      />
      <NameList
        title="Grupos de equivalência"
        hint="Ingredientes de um mesmo grupo podem substituir uns aos outros na análise de receitas (ex.: açúcar refinado e açúcar cristal). Vincule ingredientes ao grupo na tela do ingrediente."
        items={groups.data?.map((g) => ({ id: g.id, nome: g.nome, extra: g.ingredientes.length ? g.ingredientes.map((i) => i.nome).join(', ') : 'Sem ingredientes' })) ?? []}
        canEdit={canEdit}
        error={groupError ?? groups.error}
        onCreate={(nome) => wrap(setGroupError, 'grupos', 'Grupo criado.', () => post('/grupos-equivalencia', { nome }))()}
        onRename={(id, nome) => wrap(setGroupError, 'grupos', 'Grupo renomeado.', () => patch(`/grupos-equivalencia/${id}`, { nome }))()}
        onDelete={(id) => wrap(setGroupError, 'grupos', 'Grupo excluído.', () => del(`/grupos-equivalencia/${id}`))()}
      />
    </>
  )
}

interface PreparoHistorico { preparoId: string; receitaId: string | null; receitaTitulo: string; porcoes: number; confirmadoEm: string; consumirDisponivel: boolean; usuarioNome: string | null }

function HistorySection() {
  const { pantry } = useSession()
  const query = useInfiniteQuery({
    queryKey: ['preparos', pantry?.id],
    initialPageParam: '',
    queryFn: ({ pageParam }) => get<Page<PreparoHistorico>>(`/preparos?limite=20${pageParam ? `&cursor=${pageParam}` : ''}`),
    getNextPageParam: (last) => last.proximoCursor ?? undefined,
    enabled: !!pantry,
  })
  const items = query.data?.pages.flatMap((p) => p.itens) ?? []
  return (
    <Card className="max-w-2xl">
      <h2 className="mb-3 text-lg font-bold">Histórico de preparos</h2>
      <ErrorNote error={query.error} onRetry={() => query.refetch()} />
      {query.isLoading && <Spinner />}
      {query.data && items.length === 0 && <Empty title="Nenhum preparo confirmado ainda">Quando você preparar uma receita, a baixa no estoque aparece aqui.</Empty>}
      <ul className="divide-y divide-slate-200">
        {items.map((p) => (
          <li key={p.preparoId} className="py-2">
            <span className="block font-semibold">{p.receitaTitulo}</span>
            <span className="text-sm text-muted">
              {p.porcoes} porções · {formatDateTime(p.confirmadoEm)}{p.usuarioNome ? ` · ${p.usuarioNome}` : ''}
              {p.consumirDisponivel ? ' · consumiu apenas o disponível' : ''}
            </span>
          </li>
        ))}
      </ul>
      {query.hasNextPage && <div className="mt-3 text-center"><Button variant="secondary" busy={query.isFetchingNextPage} onClick={() => query.fetchNextPage()}>Carregar mais</Button></div>}
    </Card>
  )
}

function AiSection() {
  const { pantry } = useSession()
  const client = useQueryClient()
  const { notify } = useToast()
  const config = useQuery({ queryKey: ['ia-config', pantry?.id], queryFn: () => get<IaConfig>('/ia/config'), enabled: !!pantry })
  const [baseUrl, setBaseUrl] = useState<string | null>(null)
  const [modelo, setModelo] = useState<string | null>(null)
  const [apiKey, setApiKey] = useState('')
  const [ativa, setAtiva] = useState<boolean | null>(null)
  const [limite, setLimite] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<string | null>(null)
  const c = config.data

  const save = useMutation({
    mutationFn: (removeKey: boolean) => put<IaConfig>('/ia/config', {
      baseUrl: baseUrl ?? c?.baseUrl ?? '', modelo: modelo ?? c?.modelo ?? '', apiKey: apiKey || null, removerChave: removeKey,
      ativa: ativa ?? c?.ativa ?? true, limiteHora: Number(limite ?? c?.limiteHora ?? 20),
    }),
    onSuccess: async () => {
      setApiKey(''); setBaseUrl(null); setModelo(null); setAtiva(null); setLimite(null)
      await client.invalidateQueries({ queryKey: ['ia-config'] })
      await client.invalidateQueries({ queryKey: ['ia-status'] })
      notify('Configuração de IA salva.')
    },
  })
  const test = useMutation({
    mutationFn: () => post<{ ok: boolean; resposta: string }>('/ia/testar'),
    onSuccess: (r) => setTestResult(`Conexão funcionando. Resposta: ${r.resposta}`),
    onError: () => setTestResult(null),
  })
  const remove = useMutation({
    mutationFn: () => del('/ia/config'),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: ['ia-config'] }); await client.invalidateQueries({ queryKey: ['ia-status'] }); notify('Configuração de IA removida.') },
  })

  if (config.isLoading) return <Spinner />
  return (
    <Section title="Inteligência artificial (opcional)">
      <p className="mb-3 text-sm text-muted">
        O app funciona sem IA. Para usar sugestões e adaptações, informe um provedor compatível com a API da OpenAI (use sua própria chave — o uso é cobrado pelo provedor, não pelo app).
        Modelos locais (ex.: Ollama) também funcionam. A chave é guardada criptografada e nunca é exibida de volta. Dados enviados: nomes, quantidades e validades do estoque e das receitas envolvidas.
      </p>
      <ErrorNote error={config.error} />
      <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); save.mutate(false) }}>
        <TextInput label="URL base da API" required placeholder="https://api.openai.com/v1" value={baseUrl ?? c?.baseUrl ?? ''} onChange={(e) => setBaseUrl(e.target.value)} />
        <TextInput label="Modelo" required placeholder="gpt-4o-mini" value={modelo ?? c?.modelo ?? ''} onChange={(e) => setModelo(e.target.value)} />
        <TextInput
          label="Chave da API"
          type="password"
          autoComplete="off"
          placeholder={c?.temChave ? '•••••••• (chave guardada — deixe em branco para manter)' : ''}
          value={apiKey}
          onChange={(e) => setApiKey(e.target.value)}
        />
        <TextInput label="Limite de chamadas por hora" type="number" min={1} max={60} value={limite ?? String(c?.limiteHora ?? 20)} onChange={(e) => setLimite(e.target.value)} />
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="size-5" checked={ativa ?? c?.ativa ?? true} onChange={(e) => setAtiva(e.target.checked)} />
          Recursos de IA ativos nesta despensa
        </label>
        <ErrorNote error={save.error ?? test.error ?? remove.error} />
        {testResult && <p role="status" className="text-sm font-medium text-emerald-800">{testResult}</p>}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" busy={save.isPending} disabled={!(baseUrl ?? c?.baseUrl) || !(modelo ?? c?.modelo)}>Salvar</Button>
          {c?.configurada && <Button variant="secondary" busy={test.isPending} onClick={() => { setTestResult(null); test.mutate() }}>Testar conexão</Button>}
          {c?.temChave && <Button variant="secondary" onClick={() => save.mutate(true)}>Remover chave</Button>}
          {c?.configurada && <Button variant="danger" busy={remove.isPending} onClick={() => remove.mutate()}>Remover configuração</Button>}
        </div>
      </form>
    </Section>
  )
}
