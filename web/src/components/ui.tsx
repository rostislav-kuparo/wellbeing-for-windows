import { X } from 'lucide-react'
import { useEffect, useState, type ButtonHTMLAttributes, type ReactNode } from 'react'
import { iconUrl } from '../api'

export function cx(...classes: (string | false | null | undefined)[]) {
  return classes.filter(Boolean).join(' ')
}

export function Card({ children, className, title, action, subtitle }: {
  children: ReactNode
  className?: string
  title?: ReactNode
  subtitle?: ReactNode
  action?: ReactNode
}) {
  return (
    <section
      className={cx(
        'rounded-2xl border border-zinc-200/80 bg-white p-5 shadow-sm shadow-zinc-900/[0.03] dark:border-zinc-800 dark:bg-zinc-900 dark:shadow-none',
        className,
      )}
    >
      {(title || action) && (
        <header className="mb-4 flex items-start justify-between gap-4">
          <div>
            {title && <h2 className="text-[15px] font-semibold text-zinc-900 dark:text-zinc-100">{title}</h2>}
            {subtitle && <p className="mt-0.5 text-sm text-zinc-500 dark:text-zinc-400">{subtitle}</p>}
          </div>
          {action}
        </header>
      )}
      {children}
    </section>
  )
}

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'

export function Button({ variant = 'secondary', className, children, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant }) {
  return (
    <button
      type="button"
      {...props}
      className={cx(
        'inline-flex h-9 items-center justify-center gap-2 rounded-lg px-3.5 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-500',
        variant === 'primary' && 'bg-violet-600 text-white hover:bg-violet-500 dark:bg-violet-500 dark:hover:bg-violet-400',
        variant === 'secondary' &&
          'border border-zinc-200 bg-white text-zinc-800 hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-100 dark:hover:bg-zinc-700',
        variant === 'ghost' && 'text-zinc-600 hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-zinc-100',
        variant === 'danger' && 'bg-red-600 text-white hover:bg-red-500',
        className,
      )}
    >
      {children}
    </button>
  )
}

export function IconButton({ label, children, className, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { label: string }) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      {...props}
      className={cx(
        'inline-flex size-8 items-center justify-center rounded-lg text-zinc-500 transition-colors hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-zinc-100',
        'focus-visible:outline-2 focus-visible:outline-violet-500',
        className,
      )}
    >
      {children}
    </button>
  )
}

export type MeterTone = 'accent' | 'warning' | 'critical'

/** Полоса прогресса лимита: заливка несёт состояние (обычно → скоро кончится → исчерпан). */
export function Meter({ value, tone, className }: { value: number; tone: MeterTone; className?: string }) {
  const percent = Math.max(0, Math.min(1, value)) * 100
  return (
    <div className={cx('h-2 overflow-hidden rounded-full', trackClass[tone], className)}>
      <div
        className="h-full rounded-full transition-[width] duration-700 ease-out"
        style={{ width: `${percent}%`, background: fillColor[tone] }}
      />
    </div>
  )
}

const trackClass: Record<MeterTone, string> = {
  accent: 'bg-violet-100 dark:bg-violet-500/15',
  warning: 'bg-amber-100 dark:bg-amber-500/15',
  critical: 'bg-red-100 dark:bg-red-500/15',
}

const fillColor: Record<MeterTone, string> = {
  accent: 'var(--accent)',
  warning: 'var(--warning)',
  critical: 'var(--critical)',
}

export function Badge({ children, tone = 'neutral', className }: { children: ReactNode; tone?: 'neutral' | 'violet' | 'amber' | 'red' | 'green'; className?: string }) {
  return (
    <span
      className={cx(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap',
        tone === 'neutral' && 'bg-zinc-100 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300',
        tone === 'violet' && 'bg-violet-100 text-violet-800 dark:bg-violet-500/15 dark:text-violet-300',
        tone === 'amber' && 'bg-amber-100 text-amber-900 dark:bg-amber-500/15 dark:text-amber-300',
        tone === 'red' && 'bg-red-100 text-red-800 dark:bg-red-500/15 dark:text-red-300',
        tone === 'green' && 'bg-emerald-100 text-emerald-800 dark:bg-emerald-500/15 dark:text-emerald-300',
        className,
      )}
    >
      {children}
    </span>
  )
}

export function Toggle({ checked, onChange, label, disabled }: { checked: boolean; onChange: (v: boolean) => void; label: string; disabled?: boolean }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cx(
        'relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors disabled:opacity-50',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-500',
        checked ? 'bg-violet-600 dark:bg-violet-500' : 'bg-zinc-300 dark:bg-zinc-700',
      )}
    >
      <span className={cx('inline-block size-5 rounded-full bg-white shadow transition-transform', checked ? 'translate-x-[22px]' : 'translate-x-0.5')} />
    </button>
  )
}

export function Segmented<T extends string>({ value, options, onChange }: { value: T; options: { value: T; label: ReactNode }[]; onChange: (v: T) => void }) {
  return (
    <div className="inline-flex rounded-lg bg-zinc-100 p-1 dark:bg-zinc-800">
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          onClick={() => onChange(o.value)}
          className={cx(
            'inline-flex items-center gap-1.5 rounded-md px-3 py-1 text-sm font-medium transition-colors',
            o.value === value
              ? 'bg-white text-zinc-900 shadow-sm dark:bg-zinc-700 dark:text-zinc-100'
              : 'text-zinc-500 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100',
          )}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}

export function Modal({ open, onClose, title, children, footer, wide }: {
  open: boolean
  onClose: () => void
  title: ReactNode
  children: ReactNode
  footer?: ReactNode
  wide?: boolean
}) {
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-zinc-950/50 backdrop-blur-[2px]" onClick={onClose} />
      <div
        role="dialog"
        aria-modal="true"
        className={cx(
          'relative flex max-h-[90vh] w-full flex-col rounded-2xl border border-zinc-200 bg-white shadow-2xl dark:border-zinc-800 dark:bg-zinc-900',
          wide ? 'max-w-2xl' : 'max-w-md',
        )}
      >
        <header className="flex items-center justify-between gap-4 border-b border-zinc-100 px-5 py-4 dark:border-zinc-800">
          <h2 className="text-base font-semibold">{title}</h2>
          <IconButton label="Закрыть" onClick={onClose}>
            <X className="size-4" />
          </IconButton>
        </header>
        <div className="overflow-y-auto px-5 py-4">{children}</div>
        {footer && <footer className="flex items-center justify-end gap-2 border-t border-zinc-100 px-5 py-3 dark:border-zinc-800">{footer}</footer>}
      </div>
    </div>
  )
}

/** Иконка программы из службы; если её нет — кружок с первой буквой. */
export function AppIcon({ exe, name, size = 28, className }: { exe: string; name?: string; size?: number; className?: string }) {
  const [failed, setFailed] = useState(false)
  const letter = (name || exe).replace(/\.exe$/i, '').charAt(0).toUpperCase()
  if (failed)
    return (
      <span
        className={cx('inline-flex shrink-0 items-center justify-center rounded-lg bg-zinc-200 font-semibold text-zinc-600 dark:bg-zinc-700 dark:text-zinc-200', className)}
        style={{ width: size, height: size, fontSize: size * 0.45 }}
        aria-hidden
      >
        {letter}
      </span>
    )
  return (
    <img
      src={iconUrl(exe)}
      alt=""
      width={size}
      height={size}
      onError={() => setFailed(true)}
      className={cx('shrink-0 object-contain', className)}
      style={{ width: size, height: size }}
      draggable={false}
    />
  )
}

export function Dot({ color, className }: { color: string; className?: string }) {
  return <span className={cx('inline-block size-2.5 shrink-0 rounded-full', className)} style={{ background: color }} aria-hidden />
}

export function EmptyState({ icon, title, text, action }: { icon: ReactNode; title: string; text?: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 py-10 text-center">
      <div className="mb-1 flex size-11 items-center justify-center rounded-xl bg-zinc-100 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400">{icon}</div>
      <p className="font-medium">{title}</p>
      {text && <p className="max-w-sm text-sm text-zinc-500 dark:text-zinc-400">{text}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  )
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={cx('animate-pulse rounded-lg bg-zinc-100 dark:bg-zinc-800', className)} />
}

export function ErrorText({ errors }: { errors: string[] }) {
  if (errors.length === 0) return null
  return (
    <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-300">
      {errors.map((e) => (
        <p key={e}>{e}</p>
      ))}
    </div>
  )
}
