import { useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { get, post } from '../lib/api'
import { Button, Card, ErrorNote, PageTitle, Spinner, useToast } from '../components/ui'
import { useSession } from '../lib/session'
import { roleLabels } from '../lib/format'
import type { Papel } from '../lib/types'
import { usePageTitle } from '../lib/hooks'

interface Preview { despensaNome: string; papel: Papel; expiraEm: string; jaMembro: boolean; valido: boolean }

export function InvitePage() {
  usePageTitle('Convite')
  const { token = '' } = useParams()
  const navigate = useNavigate()
  const { refresh, switchPantry } = useSession()
  const { notify } = useToast()
  const preview = useQuery({ queryKey: ['convite', token], queryFn: () => get<Preview>(`/convites/${token}`), retry: false })
  const accept = useMutation({
    mutationFn: () => post<{ despensaId: string }>(`/convites/${token}/aceitar`),
    onSuccess: async (r) => {
      await refresh()
      switchPantry(r.despensaId)
      notify('Você entrou na despensa.')
      navigate('/', { replace: true })
    },
  })

  if (preview.isLoading) return <Spinner />
  if (preview.error) return <><PageTitle>Convite</PageTitle><ErrorNote error={preview.error} /></>
  const p = preview.data!
  return (
    <>
      <PageTitle>Convite para uma despensa</PageTitle>
      <Card className="max-w-lg">
        <p>Você foi convidado para a despensa <strong>{p.despensaNome}</strong> como <strong>{roleLabels[p.papel]}</strong>.</p>
        {p.jaMembro && <p className="mt-2 text-muted">Você já participa desta despensa.</p>}
        {!p.valido && !p.jaMembro && <p className="mt-2 font-medium text-red-800">Este convite expirou, foi usado ou foi revogado. Peça um novo link.</p>}
        <div className="mt-4"><ErrorNote error={accept.error} /></div>
        <div className="mt-4 flex gap-2">
          {p.valido && !p.jaMembro && <Button busy={accept.isPending} onClick={() => accept.mutate()}>Entrar na despensa</Button>}
          <Button variant="secondary" onClick={() => navigate('/')}>Ir para o início</Button>
        </div>
      </Card>
    </>
  )
}
