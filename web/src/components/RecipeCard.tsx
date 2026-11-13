import { Link } from 'react-router-dom'
import type { ReceitaResumo } from '../lib/types'
import { fileUrl } from '../lib/api'
import { estadoInfo } from '../lib/format'
import { Badge } from './ui'

export function RecipeCard({ recipe, pantryId }: { recipe: ReceitaResumo; pantryId: string }) {
  const image = fileUrl(pantryId, recipe.imagemArquivoId) ?? recipe.imagemUrl
  const info = estadoInfo[recipe.compatibilidade.estado]
  return (
    <Link to={`/receitas/${recipe.id}`} className="flex h-full flex-col overflow-hidden rounded-xl border border-slate-300 bg-white shadow-sm hover:border-brand-600">
      {image ? (
        <img src={image} alt="" loading="lazy" referrerPolicy="no-referrer" className="h-36 w-full object-cover" />
      ) : (
        <div aria-hidden="true" className="flex h-36 items-center justify-center bg-brand-50 text-5xl">🍲</div>
      )}
      <div className="flex flex-1 flex-col gap-2 p-3">
        <h3 className="text-base font-bold leading-snug">
          {recipe.favorito && <span aria-label="Favorita" className="mr-1 text-amber-600">★</span>}
          {recipe.titulo}
        </h3>
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={info.tone} icon={info.icon}>{info.label}</Badge>
          {recipe.compatibilidade.estado !== 'rascunho' && (
            <span className="text-xs text-muted">{recipe.compatibilidade.obrigatoriosOk}/{recipe.compatibilidade.obrigatoriosTotal} obrigatórios</span>
          )}
        </div>
        <p className="mt-auto text-xs text-muted">
          {recipe.porcoes} porções{recipe.tempoPreparoMin ? ` · ${recipe.tempoPreparoMin} min` : ''}
        </p>
      </div>
    </Link>
  )
}
