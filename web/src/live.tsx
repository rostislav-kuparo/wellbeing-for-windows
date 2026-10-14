import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { api, type TodayDto } from './api'
import { notifyTheme } from './bridge'
import { addDays, dayKey, parseDay } from './format'

// ── Живые данные дня (SSE /api/events) ───────────────────────────────────────

interface LiveState {
  today: TodayDto | null
  connected: boolean
  /** Увеличивается при каждом изменении лимитов/настроек — страницы перезагружают свои данные. */
  revision: number
  bump: () => void
}

const LiveContext = createContext<LiveState>({ today: null, connected: false, revision: 0, bump: () => {} })

export function LiveProvider({ children }: { children: ReactNode }) {
  const [today, setToday] = useState<TodayDto | null>(null)
  const [connected, setConnected] = useState(false)
  const [revision, setRevision] = useState(0)
  const toast = useToast()
  const toastRef = useRef(toast)
  toastRef.current = toast

  useEffect(() => {
    let source: EventSource | null = null
    const connect = () => {
      source = new EventSource('/api/events')
      source.addEventListener('today', (event) => {
        setToday(JSON.parse((event as MessageEvent).data))
        setConnected(true)
      })
      source.addEventListener('app-blocked', (event) => {
        const data = JSON.parse((event as MessageEvent).data) as { displayName: string; limit: string }
        toastRef.current.show(`${data.displayName} закрыта: лимит «${data.limit}» исчерпан`, 'warning')
      })
      source.onerror = () => setConnected(false)
    }
    connect()
    api.today().then(setToday).catch(() => {})

    // EventSource сам переподключается при обрыве, но не после 401/500 — тогда пересоздаём вручную.
    const watchdog = window.setInterval(() => {
      if (source?.readyState === EventSource.CLOSED) {
        source.close()
        connect()
      }
    }, 5000)

    return () => {
      window.clearInterval(watchdog)
      source?.close()
    }
  }, [])

  const bump = useCallback(() => setRevision((r) => r + 1), [])
  const value = useMemo(() => ({ today, connected, revision, bump }), [today, connected, revision, bump])
  return <LiveContext.Provider value={value}>{children}</LiveContext.Provider>
}

export const useLive = () => useContext(LiveContext)

// ── Цвета программ ───────────────────────────────────────────────────────────
// Цвет закреплён за программой (по её месту в топе за 30 дней на момент открытия окна) и не меняется
// при фильтрации. Первые 7 программ получают свой цвет, остальные — нейтральный «Другие».

const SLOTS = 7
const ColorContext = createContext<{ colorOf: (exe: string) => string; order: string[] }>({ colorOf: () => 'var(--series-other)', order: [] })

export function ColorProvider({ children }: { children: ReactNode }) {
  const { today } = useLive()
  const [order, setOrder] = useState<string[]>([])
  const day = today?.day

  useEffect(() => {
    if (!day || order.length > 0) return
    const to = parseDay(day)
    api
      .daily(dayKey(addDays(to, -29)), day)
      .then((daily) => {
        const totals = new Map<string, number>()
        for (const d of daily.days) for (const [exe, s] of Object.entries(d.apps)) totals.set(exe, (totals.get(exe) ?? 0) + s)
        setOrder([...totals.entries()].sort((a, b) => b[1] - a[1]).slice(0, SLOTS).map(([exe]) => exe))
      })
      .catch(() => {})
  }, [day, order.length])

  const colorOf = useCallback(
    (exe: string) => {
      const index = order.indexOf(exe)
      return index >= 0 ? `var(--series-${index + 1})` : 'var(--series-other)'
    },
    [order],
  )
  const value = useMemo(() => ({ colorOf, order }), [colorOf, order])
  return <ColorContext.Provider value={value}>{children}</ColorContext.Provider>
}

export const useAppColor = () => useContext(ColorContext).colorOf

/** Программы, у которых есть свой цвет, в порядке слотов палитры. */
export const useAppOrder = () => useContext(ColorContext).order

// ── Тема ─────────────────────────────────────────────────────────────────────

export type Theme = 'dark' | 'light'

const ThemeContext = createContext<{ theme: Theme; setTheme: (t: Theme) => void }>({ theme: 'dark', setTheme: () => {} })

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(() =>
    document.documentElement.classList.contains('dark') ? 'dark' : 'light',
  )

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark')
    notifyTheme(theme)
    try {
      localStorage.setItem('wb-theme', theme)
    } catch {
      // хранилище недоступно — тема просто не запомнится
    }
  }, [theme])

  const value = useMemo(() => ({ theme, setTheme: setThemeState }), [theme])
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

export const useTheme = () => useContext(ThemeContext)

// ── Всплывающие сообщения ────────────────────────────────────────────────────

export type ToastKind = 'info' | 'success' | 'warning' | 'error'
interface Toast {
  id: number
  text: string
  kind: ToastKind
}

const ToastContext = createContext<{ toasts: Toast[]; show: (text: string, kind?: ToastKind) => void; dismiss: (id: number) => void }>({
  toasts: [],
  show: () => {},
  dismiss: () => {},
})

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const nextId = useRef(1)

  const dismiss = useCallback((id: number) => setToasts((list) => list.filter((t) => t.id !== id)), [])
  const show = useCallback(
    (text: string, kind: ToastKind = 'info') => {
      const id = nextId.current++
      setToasts((list) => [...list.slice(-3), { id, text, kind }])
      window.setTimeout(() => dismiss(id), 6000)
    },
    [dismiss],
  )

  const value = useMemo(() => ({ toasts, show, dismiss }), [toasts, show, dismiss])
  return <ToastContext.Provider value={value}>{children}</ToastContext.Provider>
}

export const useToast = () => useContext(ToastContext)
