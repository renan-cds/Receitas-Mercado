import { useNavigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { get } from '../lib/api'
import { useSession } from '../lib/session'
import { usePageTitle } from '../lib/hooks'
import type { ReceitaDetalhe } from '../lib/types'
import { ErrorNote, PageTitle, Spinner } from '../components/ui'
import { RecipeEditor, blankInitial, fromDetail } from '../components/RecipeEditor'

export function RecipeFormPage() {
  const { id } = useParams()
  const { pantry, canEdit } = useSession()
  const navigate = useNavigate()
  usePageTitle(id ? 'Editar receita' : 'Nova receita')
  const recipe = useQuery({ queryKey: ['receita', pantry?.id, id], queryFn: () => get<ReceitaDetalhe>(`/receitas/${id}`), enabled: !!id })

  if (!canEdit) return <ErrorNote error={new Error('Seu papel nesta despensa não permite editar receitas.')} />
  if (id && recipe.isLoading) return <Spinner />
  if (recipe.error) return <ErrorNote error={recipe.error} />

  return (
    <>
      <PageTitle>{id ? 'Editar receita' : 'Nova receita'}</PageTitle>
      <RecipeEditor key={id ?? 'nova'} initial={recipe.data ? fromDetail(recipe.data) : blankInitial()} recipeId={id} onSaved={(rid) => navigate(`/receitas/${rid}`)} />
    </>
  )
}
