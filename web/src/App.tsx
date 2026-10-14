import { ChartColumn, CircleCheck, Gauge, Info, Moon, Settings, Sun, Timer, TriangleAlert, X } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'
import { cx } from './components/ui'
import { fmtDuration } from './format'
import { useLive, useTheme, useToast } from './live'
import HistoryPage from './pages/HistoryPage'
import LimitsPage from './pages/LimitsPage'
import SettingsPage from './pages/SettingsPage'
import TodayPage from './pages/TodayPage'

type Route = 'today' | 'history' | 'limits' | 'settings'

const routes: { id: Route; label: string; icon: ReactNode }[] = [
  { id: 'today', label: 'Сегодня', icon: <Gauge className="size-[18px]" /> },
  { id: 'history', label: 'История', icon: <ChartColumn className="size-[18px]" /> },
  { id: 'limits', label: 'Лимиты', icon: <Timer className="size-[18px]" /> },
  { id: 'settings', label: 'Настройки', icon: <Settings className="size-[18px]" /> },
]

function readRoute(): Route {
  const hash = window.location.hash.replace('#/', '').replace('#', '')
  return routes.some((r) => r.id === hash) ? (hash as Route) : 'today'
}

export default function App() {
  const [route, setRoute] = useState<Route>(readRoute)

  useEffect(() => {
    const onHash = () => setRoute(readRoute())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])

  return (
    <div className="flex h-full">
      <Sidebar route={route} />
      <main className="min-w-0 flex-1 overflow-y-auto">
        <ConnectionBanner />
        <div className="mx-auto max-w-6xl px-8 py-8">
          {route === 'today' && <TodayPage />}
          {route === 'history' && <HistoryPage />}
          {route === 'limits' && <LimitsPage />}
          {route === 'settings' && <SettingsPage />}
        </div>
      </main>
      <Toasts />
    </div>
  )
}

function Sidebar({ route }: { route: Route }) {
  const { today } = useLive()
  const { theme, setTheme } = useTheme()

  return (
    <aside className="flex w-60 shrink-0 flex-col border-r border-zinc-200 bg-white/60 px-3 py-5 dark:border-zinc-800/80 dark:bg-zinc-900/40">
      <div className="mb-7 flex items-center gap-2.5 px-2.5">
        <img src="/favicon.svg" alt="" className="size-8" />
        <div>
          <p className="font-display text-[15px] leading-tight font-semibold">Wellbeing</p>
          <p className="text-xs text-zinc-500 dark:text-zinc-400">экранное время</p>
        </div>
      </div>

      <nav className="flex flex-col gap-0.5">
        {routes.map((r) => (
          <a
            key={r.id}
            href={`#/${r.id}`}
            className={cx(
              'flex items-center gap-3 rounded-lg px-2.5 py-2 text-sm font-medium transition-colors',
              route === r.id
                ? 'bg-violet-50 text-violet-700 dark:bg-violet-500/12 dark:text-violet-300'
                : 'text-zinc-600 hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/70 dark:hover:text-zinc-100',
            )}
          >
            {r.icon}
            {r.label}
          </a>
        ))}
      </nav>

      <div className="mt-auto space-y-3 px-1">
        {today && (
          <div className="rounded-xl bg-zinc-100/80 px-3 py-2.5 dark:bg-zinc-800/60">
            <p className="text-xs text-zinc-500 dark:text-zinc-400">Сегодня</p>
            <p className="font-display text-lg font-semibold">{fmtDuration(today.totalSeconds)}</p>
          </div>
        )}
        <button
          type="button"
          onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
          className="flex w-full items-center gap-3 rounded-lg px-2.5 py-2 text-sm text-zinc-600 transition-colors hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/70 dark:hover:text-zinc-100"
        >
          {theme === 'dark' ? <Sun className="size-[18px]" /> : <Moon className="size-[18px]" />}
          {theme === 'dark' ? 'Светлая тема' : 'Тёмная тема'}
        </button>
      </div>
    </aside>
  )
}

function ConnectionBanner() {
  const { connected, today } = useLive()
  const [visible, setVisible] = useState(false)

  // Не пугаем мгновенно: показываем, только если связи нет дольше 4 секунд.
  useEffect(() => {
    if (connected) {
      setVisible(false)
      return
    }
    const timer = window.setTimeout(() => setVisible(true), 4000)
    return () => window.clearTimeout(timer)
  }, [connected])

  if (!visible) return null
  return (
    <div className="flex items-center gap-2 border-b border-amber-200 bg-amber-50 px-8 py-2.5 text-sm text-amber-900 dark:border-amber-500/20 dark:bg-amber-500/10 dark:text-amber-200">
      <TriangleAlert className="size-4 shrink-0" />
      {today ? 'Нет связи со службой Wellbeing — данные могут быть устаревшими. Переподключаюсь…' : 'Служба Wellbeing недоступна. Проверьте, что она установлена и запущена.'}
    </div>
  )
}

function Toasts() {
  const { toasts, dismiss } = useToast()
  return (
    <div className="pointer-events-none fixed right-5 bottom-5 z-[60] flex w-96 flex-col gap-2">
      {toasts.map((t) => (
        <div
          key={t.id}
          className="pointer-events-auto flex items-start gap-3 rounded-xl border border-zinc-200 bg-white px-4 py-3 text-sm shadow-lg dark:border-zinc-700 dark:bg-zinc-800"
        >
          <span className="mt-0.5">
            {t.kind === 'success' && <CircleCheck className="size-4 text-emerald-600 dark:text-emerald-400" />}
            {t.kind === 'info' && <Info className="size-4 text-violet-600 dark:text-violet-400" />}
            {(t.kind === 'warning' || t.kind === 'error') && <TriangleAlert className={cx('size-4', t.kind === 'error' ? 'text-red-600 dark:text-red-400' : 'text-amber-600 dark:text-amber-400')} />}
          </span>
          <p className="flex-1 text-zinc-800 dark:text-zinc-100">{t.text}</p>
          <button type="button" onClick={() => dismiss(t.id)} className="text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200" aria-label="Закрыть">
            <X className="size-4" />
          </button>
        </div>
      ))}
    </div>
  )
}
