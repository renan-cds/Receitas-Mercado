import { createContext, useCallback, useContext, useEffect, useId, useRef, useState, type ButtonHTMLAttributes, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react'
import { ApiError } from '../lib/api'

type Tone = 'ok' | 'warn' | 'bad' | 'muted' | 'info'
const toneClasses: Record<Tone, string> = {
  ok: 'bg-emerald-100 text-emerald-900 border-emerald-300',
  warn: 'bg-amber-100 text-amber-950 border-amber-400',
  bad: 'bg-red-100 text-red-900 border-red-300',
  muted: 'bg-slate-100 text-slate-800 border-slate-300',
  info: 'bg-sky-100 text-sky-950 border-sky-300',
}

export function Badge({ tone = 'muted', children, icon }: { tone?: Tone; children: ReactNode; icon?: string }) {
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${toneClasses[tone]}`}>
      {icon && <span aria-hidden="true">{icon}</span>}
      {children}
    </span>
  )
}

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost'
const variants: Record<Variant, string> = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700 disabled:bg-slate-400',
  secondary: 'bg-white text-ink border border-slate-400 hover:bg-slate-50 disabled:text-slate-500',
  danger: 'bg-red-700 text-white hover:bg-red-800 disabled:bg-slate-400',
  ghost: 'bg-transparent text-brand-700 hover:bg-brand-50 underline-offset-2',
}

export function Button({ variant = 'primary', busy, className = '', children, ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; busy?: boolean }) {
  return (
    <button
      type="button"
      {...rest}
      disabled={rest.disabled || busy}
      aria-busy={busy || undefined}
      className={`inline-flex min-h-11 items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-semibold transition-colors disabled:cursor-not-allowed ${variants[variant]} ${className}`}
    >
      {busy && <span aria-hidden="true" className="size-4 animate-spin rounded-full border-2 border-current border-t-transparent" />}
      {children}
    </button>
  )
}

const inputClass = 'block min-h-11 w-full rounded-lg border border-slate-500 bg-white px-3 py-2 text-base text-ink placeholder:text-slate-500 disabled:bg-slate-100 aria-[invalid=true]:border-red-700 aria-[invalid=true]:ring-1 aria-[invalid=true]:ring-red-700'

interface FieldProps { label: string; hint?: string; error?: string; required?: boolean; className?: string }

export function Field({ label, hint, error, required, className = '', children }: FieldProps & { children: (props: { id: string; 'aria-describedby'?: string; 'aria-invalid'?: boolean; required?: boolean }) => ReactNode }) {
  const id = useId()
  const describedBy = [hint ? `${id}-hint` : '', error ? `${id}-err` : ''].filter(Boolean).join(' ') || undefined
  return (
    <div className={className}>
      <label htmlFor={id} className="mb-1 block text-sm font-semibold">
        {label}{required && <span aria-hidden="true" className="text-red-700"> *</span>}
      </label>
      {children({ id, 'aria-describedby': describedBy, 'aria-invalid': error ? true : undefined, required })}
      {hint && <p id={`${id}-hint`} className="mt-1 text-xs text-muted">{hint}</p>}
      {error && <p id={`${id}-err`} role="alert" className="mt-1 text-sm font-medium text-red-800">{error}</p>}
    </div>
  )
}

export function TextInput({ label, hint, error, required, className, ...rest }: FieldProps & InputHTMLAttributes<HTMLInputElement>) {
  return (
    <Field label={label} hint={hint} error={error} required={required} className={className}>
      {(p) => <input {...p} {...rest} className={inputClass} />}
    </Field>
  )
}

export function TextArea({ label, hint, error, required, className, ...rest }: FieldProps & TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <Field label={label} hint={hint} error={error} required={required} className={className}>
      {(p) => <textarea {...p} {...rest} className={`${inputClass} min-h-24`} />}
    </Field>
  )
}

export function Select({ label, hint, error, required, className, children, ...rest }: FieldProps & SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <Field label={label} hint={hint} error={error} required={required} className={className}>
      {(p) => <select {...p} {...rest} className={inputClass}>{children}</select>}
    </Field>
  )
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <section className={`rounded-xl border border-slate-300 bg-white p-4 shadow-sm ${className}`}>{children}</section>
}

export function PageTitle({ children, actions }: { children: ReactNode; actions?: ReactNode }) {
  return (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <h1 className="text-2xl font-bold tracking-tight">{children}</h1>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}

export function Spinner({ label = 'Carregando…' }: { label?: string }) {
  return (
    <div role="status" className="flex items-center gap-3 p-6 text-muted">
      <span aria-hidden="true" className="size-5 animate-spin rounded-full border-2 border-brand-600 border-t-transparent" />
      {label}
    </div>
  )
}

export function Empty({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="rounded-xl border border-dashed border-slate-400 bg-white/60 p-8 text-center">
      <p className="text-lg font-semibold">{title}</p>
      {children && <div className="mt-2 text-muted">{children}</div>}
    </div>
  )
}

export function errorMessage(error: unknown) {
  if (error instanceof ApiError) {
    if (error.code === 'VALIDACAO' && error.details && typeof error.details === 'object') {
      const first = Object.values(error.details as Record<string, string[]>)[0]
      if (first?.[0]) return `${error.message} ${first[0]}`
    }
    return error.message
  }
  return error instanceof Error ? error.message : 'Algo deu errado.'
}

export function ErrorNote({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  if (!error) return null
  return (
    <div role="alert" className="rounded-lg border border-red-300 bg-red-50 p-3 text-sm text-red-900">
      <p>{errorMessage(error)}</p>
      {onRetry && <Button variant="secondary" className="mt-2" onClick={onRetry}>Tentar novamente</Button>}
    </div>
  )
}

export function Modal({ open, onClose, title, children, wide }: { open: boolean; onClose: () => void; title: string; children: ReactNode; wide?: boolean }) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])
  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      onClose={onClose}
      onCancel={(e) => { e.preventDefault(); onClose() }}
      className={`m-auto max-h-[92dvh] overflow-y-auto rounded-xl border border-slate-300 bg-white p-0 shadow-xl`}
      style={{ width: `min(calc(100% - 1rem), ${wide ? '48rem' : '32rem'})` }}
    >
      {open && (
        <div className="p-5">
          <div className="mb-4 flex items-start justify-between gap-3">
            <h2 id={titleId} className="text-xl font-bold">{title}</h2>
            <Button variant="ghost" aria-label="Fechar" onClick={onClose} className="-mr-2 -mt-1 min-h-11 min-w-11">✕</Button>
          </div>
          {children}
        </div>
      )}
    </dialog>
  )
}

interface ToastApi { notify: (message: string, tone?: 'ok' | 'bad') => void }
const ToastCtx = createContext<ToastApi>({ notify: () => undefined })
export const useToast = () => useContext(ToastCtx)

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<{ id: number; message: string; tone: 'ok' | 'bad' }[]>([])
  const notify = useCallback((message: string, tone: 'ok' | 'bad' = 'ok') => {
    const id = Date.now() + Math.random()
    setItems((list) => [...list, { id, message, tone }])
    setTimeout(() => setItems((list) => list.filter((i) => i.id !== id)), 6000)
  }, [])
  return (
    <ToastCtx.Provider value={{ notify }}>
      {children}
      <div role="status" aria-live="polite" className="pointer-events-none fixed inset-x-0 bottom-20 z-50 flex flex-col items-center gap-2 px-3 md:bottom-6">
        {items.map((i) => (
          <div key={i.id} className={`pointer-events-auto max-w-md rounded-lg px-4 py-3 text-sm font-medium shadow-lg ${i.tone === 'ok' ? 'bg-brand-800 text-white' : 'bg-red-800 text-white'}`}>
            {i.message}
          </div>
        ))}
      </div>
    </ToastCtx.Provider>
  )
}

export function ConfirmDialog({ open, title, message, confirmLabel = 'Confirmar', danger, busy, onConfirm, onClose }: {
  open: boolean; title: string; message: ReactNode; confirmLabel?: string; danger?: boolean; busy?: boolean; onConfirm: () => void; onClose: () => void
}) {
  return (
    <Modal open={open} onClose={onClose} title={title}>
      <div className="text-sm">{message}</div>
      <div className="mt-5 flex justify-end gap-2">
        <Button variant="secondary" onClick={onClose}>Cancelar</Button>
        <Button variant={danger ? 'danger' : 'primary'} busy={busy} onClick={onConfirm}>{confirmLabel}</Button>
      </div>
    </Modal>
  )
}
