import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { fieldErrors, post, rememberPantry } from '../lib/api'
import type { Me } from '../lib/types'
import { Button, Card, ErrorNote, TextInput } from '../components/ui'
import { usePageTitle } from '../lib/hooks'

export function AuthPage({ mode }: { mode: 'entrar' | 'registrar' }) {
  usePageTitle(mode === 'entrar' ? 'Entrar' : 'Criar conta')
  const navigate = useNavigate()
  const client = useQueryClient()
  const [params] = useSearchParams()
  const next = params.get('next') && params.get('next')!.startsWith('/') ? params.get('next')! : '/'
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [nome, setNome] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const errors = fieldErrors(error)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const me = await post<Me>(mode === 'entrar' ? '/auth/entrar' : '/auth/registrar', mode === 'entrar' ? { email, senha } : { email, senha, nome })
      rememberPantry(me)
      client.setQueryData(['me'], me)
      navigate(next, { replace: true })
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="mx-auto flex min-h-dvh max-w-md flex-col justify-center px-4 py-8">
      <h1 className="mb-1 text-3xl font-bold text-brand-700">Receitas Mercado</h1>
      <p className="mb-6 text-muted">Saiba o que você tem em casa e o que dá para cozinhar.</p>
      <Card>
        <h2 className="mb-4 text-xl font-bold">{mode === 'entrar' ? 'Entrar' : 'Criar conta'}</h2>
        <form onSubmit={submit} className="space-y-4" noValidate>
          {mode === 'registrar' && (
            <TextInput label="Seu nome" autoComplete="name" required value={nome} onChange={(e) => setNome(e.target.value)} error={errors.nome} />
          )}
          <TextInput label="E-mail" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} error={errors.email} />
          <TextInput
            label="Senha" type="password" required value={senha} onChange={(e) => setSenha(e.target.value)}
            autoComplete={mode === 'entrar' ? 'current-password' : 'new-password'}
            hint={mode === 'registrar' ? 'Pelo menos 10 caracteres.' : undefined} error={errors.senha}
          />
          {!Object.keys(errors).length && <ErrorNote error={error} />}
          <Button type="submit" busy={busy} className="w-full">{mode === 'entrar' ? 'Entrar' : 'Criar conta'}</Button>
        </form>
      </Card>
      <p className="mt-4 text-center text-sm">
        {mode === 'entrar' ? (
          <>Ainda não tem conta? <Link className="font-semibold text-brand-700 underline" to={`/criar-conta${params.get('next') ? `?next=${encodeURIComponent(next)}` : ''}`}>Criar conta</Link></>
        ) : (
          <>Já tem conta? <Link className="font-semibold text-brand-700 underline" to={`/entrar${params.get('next') ? `?next=${encodeURIComponent(next)}` : ''}`}>Entrar</Link></>
        )}
      </p>
    </main>
  )
}
