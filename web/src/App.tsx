import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { SessionProvider } from './lib/session'
import { Layout } from './components/Layout'
import { ErrorNote, Spinner } from './components/ui'
import { AuthPage } from './pages/Auth'
import { InvitePage } from './pages/Invite'
import { HomePage } from './pages/Home'
import { StockPage } from './pages/Stock'
import { IngredientPage } from './pages/Ingredient'
import { RecipesPage } from './pages/Recipes'
import { RecipePage } from './pages/Recipe'
import { RecipeFormPage } from './pages/RecipeForm'
import { ImportPage } from './pages/Import'
import { CatalogsPage, CatalogPage } from './pages/Catalogs'
import { ShoppingPage, ShoppingListPage } from './pages/Shopping'
import { MorePage } from './pages/More'

export function App() {
  const location = useLocation()
  const publicPath = ['/entrar', '/criar-conta'].includes(location.pathname)
  const target = `/entrar?next=${encodeURIComponent(location.pathname + location.search)}`

  return (
    <SessionProvider>
      {(me, loading, error, retry) => {
        if (publicPath) {
          if (me) return <Navigate to="/" replace />
          return (
            <Routes>
              <Route path="/entrar" element={<AuthPage mode="entrar" />} />
              <Route path="/criar-conta" element={<AuthPage mode="registrar" />} />
            </Routes>
          )
        }
        if (loading) return <Spinner label="Carregando…" />
        if (!me) {
          if (error) {
            return (
              <div className="mx-auto max-w-md p-6">
                <ErrorNote error={error} onRetry={retry} />
              </div>
            )
          }
          return <Navigate to={target} replace />
        }
        return (
          <Routes>
            <Route element={<Layout />}>
              <Route index element={<HomePage />} />
              <Route path="estoque" element={<StockPage />} />
              <Route path="estoque/:id" element={<IngredientPage />} />
              <Route path="receitas" element={<RecipesPage />} />
              <Route path="receitas/nova" element={<RecipeFormPage />} />
              <Route path="receitas/importar" element={<ImportPage />} />
              <Route path="receitas/:id" element={<RecipePage />} />
              <Route path="receitas/:id/editar" element={<RecipeFormPage />} />
              <Route path="catalogos" element={<CatalogsPage />} />
              <Route path="catalogos/:id" element={<CatalogPage />} />
              <Route path="compras" element={<ShoppingPage />} />
              <Route path="compras/:id" element={<ShoppingListPage />} />
              <Route path="mais" element={<MorePage />} />
              <Route path="convite/:token" element={<InvitePage />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Route>
          </Routes>
        )
      }}
    </SessionProvider>
  )
}
