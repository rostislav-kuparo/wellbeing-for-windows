import { CalendarClock, Clock, Hourglass, Lock, Plus, Timer, TriangleAlert } from 'lucide-react'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, type LimitProgress, type TimelineDto, type TodayDto } from '../api'
import { limitTone } from '../components/limitTone'
import { Timeline } from '../components/Timeline'
import { AppIcon, Badge, Button, Card, cx, Dot, EmptyState, Meter, Skeleton } from '../components/ui'
import { fmtDateWeekday, fmtDuration, fmtLimitMinutes, fmtRemaining, fmtTime, fmtWindow, parseDay, plural, UNLIMITED_MINUTES, windowLengthSeconds } from '../format'
import { useAppColor, useLive } from '../live'

export default function TodayPage() {
  const { today, revision } = useLive()
  const [timeline, setTimeline] = useState<TimelineDto | null>(null)

  // Таймлайн тяжелее сводки — обновляем раз в минуту и при смене дня.
  useEffect(() => {
    if (!today?.day) return
    let cancelled = false
    const load = () => api.timeline().then((t) => !cancelled && setTimeline(t)).catch(() => {})
    load()
    const timer = window.setInterval(load, 60_000)
    return () => {
      cancelled = true
      window.clearInterval(timer)
    }
  }, [today?.day, revision])

  const names = useMemo(() => Object.fromEntries((today?.apps ?? []).map((a) => [a.exe, a.displayName])), [today?.apps])

  if (!today) return <TodaySkeleton />

  return (
    <div className="space-y-6">
      <header>
        <p className="text-sm text-zinc-500 capitalize dark:text-zinc-400">{fmtDateWeekday(parseDay(today.day))}</p>
        <h1 className="font-display text-2xl font-semibold">Сегодня</h1>
      </header>

      <Hero today={today} />

      <section>
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-[15px] font-semibold">Лимиты</h2>
          {today.limits.length > 0 && (
            <a href="#/limits" className="text-sm font-medium text-violet-600 hover:text-violet-500 dark:text-violet-400">
              Настроить
            </a>
          )}
        </div>
        {today.limits.length === 0 ? (
          <Card>
            <EmptyState
              icon={<Timer className="size-5" />}
              title="Лимитов пока нет"
              text="Задайте, сколько минут в день можно проводить в отдельных программах или группах программ."
              action={
                <Button variant="primary" onClick={() => (window.location.hash = '#/limits')}>
                  <Plus className="size-4" /> Добавить лимит
                </Button>
              }
            />
          </Card>
        ) : (
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {today.limits.map((limit) => (
              <LimitCard key={limit.id} limit={limit} names={names} dayEnd={today.dayEnd} />
            ))}
          </div>
        )}
      </section>

      <div className="grid gap-6 lg:grid-cols-5">
        <Card title="Топ программ" subtitle="Время, когда окно программы было активным" className="lg:col-span-2">
          <TopApps today={today} />
        </Card>
        <Card title="Таймлайн дня" subtitle="Какая программа когда была активна" className="lg:col-span-3">
          {timeline ? (
            timeline.minutes.length > 0 ? (
              <Timeline data={timeline} names={names} />
            ) : (
              <p className="py-8 text-center text-sm text-zinc-500">Сегодня ещё ничего не записано</p>
            )
          ) : (
            <Skeleton className="h-24" />
          )}
        </Card>
      </div>
    </div>
  )
}

function Hero({ today }: { today: TodayDto }) {
  const { current, agent } = today
  const exhausted = today.limits.filter((l) => l.exhausted).length

  return (
    <Card className="relative overflow-hidden">
      <div className="pointer-events-none absolute -top-24 -right-16 size-72 rounded-full bg-violet-500/10 blur-3xl dark:bg-violet-500/15" />
      <div className="relative flex flex-wrap items-end justify-between gap-6">
        <div>
          <p className="text-sm font-medium text-zinc-500 dark:text-zinc-400">Экранное время сегодня</p>
          <p className="font-display mt-1 text-5xl font-semibold tracking-tight">{fmtDuration(today.totalSeconds)}</p>
          <CurrentActivity today={today} />
        </div>
        <dl className="grid grid-cols-3 gap-x-8 gap-y-1 text-sm">
          <Stat label="Программ" value={String(today.apps.length)} />
          <Stat label="Лимитов" value={exhausted > 0 ? `${today.limits.length} · ${exhausted} исч.` : String(today.limits.length)} />
          <Stat label="Новый день" value={fmtTime(new Date(today.dayEnd))} icon={<CalendarClock className="size-3.5" />} />
        </dl>
      </div>

      {(current.mode === 'fallback' || agent.status === 'missing') && (
        <div className="relative mt-5 flex items-start gap-2.5 rounded-xl bg-amber-50 px-3.5 py-2.5 text-sm text-amber-900 dark:bg-amber-500/10 dark:text-amber-200">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" />
          <p>
            Агент в трее не отвечает. Пока он перезапускается, время идёт <b>всем запущенным программам с лимитом</b>, даже если их окна не активны.
          </p>
        </div>
      )}
    </Card>
  )
}

function Stat({ label, value, icon }: { label: string; value: string; icon?: ReactNode }) {
  return (
    <div>
      <dt className="flex items-center gap-1 text-zinc-500 dark:text-zinc-400">
        {icon}
        {label}
      </dt>
      <dd className="mt-0.5 font-semibold">{value}</dd>
    </div>
  )
}

const stateText: Record<TodayDto['current']['state'], string> = {
  counting: 'идёт учёт',
  idle: 'пауза — нет активности',
  locked: 'экран заблокирован',
  noWindow: 'нет активного окна',
  noUser: 'никто не вошёл в систему',
  sleep: 'компьютер просыпается',
  waiting: 'ожидание агента',
}

function CurrentActivity({ today }: { today: TodayDto }) {
  const { current } = today
  const counting = current.state === 'counting'
  return (
    <div className="mt-3 flex items-center gap-2 text-sm text-zinc-600 dark:text-zinc-300">
      <span className="relative flex size-2.5">
        {counting && <span className="absolute inline-flex size-full animate-ping rounded-full bg-emerald-500 opacity-60" />}
        <span className={cx('relative inline-flex size-2.5 rounded-full', counting ? 'bg-emerald-500' : 'bg-zinc-400 dark:bg-zinc-600')} />
      </span>
      {current.exe && current.mode !== 'fallback' ? (
        <>
          <span>Сейчас:</span>
          <AppIcon exe={current.exe} name={current.displayName ?? undefined} size={18} />
          <span className="font-medium text-zinc-900 dark:text-zinc-100">{current.displayName}</span>
          <span className="text-zinc-500 dark:text-zinc-400">· {stateText[current.state]}</span>
        </>
      ) : (
        <span>{current.mode === 'fallback' && counting ? 'учёт без агента' : stateText[current.state]}</span>
      )}
    </div>
  )
}

function LimitCard({ limit, names, dayEnd }: { limit: LimitProgress; names: Record<string, string>; dayEnd: string }) {
  const unlimited = limit.allowedSeconds >= UNLIMITED_MINUTES * 60
  const outside = limit.blockReason === 'outsideWindow'
  const tone = outside ? 'warning' : limitTone(unlimited ? 0 : limit.allowedSeconds, limit.availableSeconds, limit.exhausted)

  // Без лимита минут полоса показывает, сколько прошло от разрешённого окна.
  let fraction = limit.allowedSeconds > 0 ? limit.usedSeconds / limit.allowedSeconds : 1
  if (unlimited && limit.window) {
    const length = windowLengthSeconds(limit.window)
    fraction = outside ? 0 : length > 0 ? 1 - (limit.windowSecondsLeft ?? 0) / length : 0
  }

  return (
    <Card className="flex flex-col gap-3.5">
      <div className="flex items-center gap-3">
        <AppStack exes={limit.exes} names={names} />
        <div className="min-w-0 flex-1">
          <p className="truncate font-medium">{limit.name}</p>
          <p className="truncate text-xs text-zinc-500 dark:text-zinc-400">
            {limit.exes.length > 1 ? `${limit.exes.length} ${plural(limit.exes.length, ['программа', 'программы', 'программ'])}` : (names[limit.exes[0]] ?? limit.exes[0])}
          </p>
        </div>
        {limit.hasPendingChange && <Badge tone="violet">изменится завтра</Badge>}
      </div>

      <div>
        {limit.exhausted ? (
          <p className="flex items-center gap-2 text-xl font-semibold text-red-600 dark:text-red-400">
            <Lock className="size-5" /> Исчерпан
          </p>
        ) : outside ? (
          <p className="flex items-center gap-2 text-xl font-semibold text-amber-700 dark:text-amber-300">
            <Clock className="size-5" /> Сейчас недоступно
          </p>
        ) : (
          <p className="text-xl font-semibold">
            <span className="text-sm font-normal text-zinc-500 dark:text-zinc-400">осталось </span>
            {fmtRemaining(limit.availableSeconds)}
          </p>
        )}
      </div>

      <Meter value={fraction} tone={tone} />

      <p className="flex items-center justify-between gap-3 text-xs text-zinc-500 dark:text-zinc-400">
        <span>
          {unlimited ? `${fmtDuration(limit.usedSeconds)} сегодня` : `${fmtDuration(limit.usedSeconds)} из ${fmtLimitMinutes(limit.allowedSeconds / 60)}`}
        </span>
        {limit.exhausted ? (
          <span>заблокировано до {fmtTime(new Date(dayEnd))}</span>
        ) : outside ? (
          <span>{limit.window && windowLengthSeconds(limit.window) > 0 ? `разрешено ${fmtWindow(limit.window)}` : 'сегодня закрыто'}</span>
        ) : tone === 'warning' ? (
          <span className="inline-flex items-center gap-1 text-amber-700 dark:text-amber-300">
            <Hourglass className="size-3.5" /> скоро закончится
          </span>
        ) : limit.window ? (
          <span>доступно до {limit.window.to}</span>
        ) : null}
      </p>
    </Card>
  )
}
export function AppStack({ exes, names, size = 32 }: { exes: string[]; names: Record<string, string>; size?: number }) {
  const shown = exes.slice(0, 3)
  return (
    <div className="flex shrink-0 -space-x-2">
      {shown.map((exe) => (
        <span key={exe} className="rounded-lg bg-white p-0.5 ring-2 ring-white dark:bg-zinc-900 dark:ring-zinc-900">
          <AppIcon exe={exe} name={names[exe]} size={size - 4} />
        </span>
      ))}
      {exes.length > 3 && (
        <span
          className="flex items-center justify-center rounded-lg bg-zinc-100 text-xs font-semibold text-zinc-600 ring-2 ring-white dark:bg-zinc-800 dark:text-zinc-300 dark:ring-zinc-900"
          style={{ width: size, height: size }}
        >
          +{exes.length - 3}
        </span>
      )}
    </div>
  )
}

function TopApps({ today }: { today: TodayDto }) {
  const colorOf = useAppColor()
  const [expanded, setExpanded] = useState(false)
  const apps = today.apps
  if (apps.length === 0) return <p className="py-8 text-center text-sm text-zinc-500">Сегодня ещё ничего не записано</p>

  const max = apps[0].seconds
  const shown = expanded ? apps : apps.slice(0, 7)
  return (
    <div>
      <ul className="space-y-3.5">
        {shown.map((app) => (
          <li key={app.exe} className="flex items-center gap-3">
            <AppIcon exe={app.exe} name={app.displayName} size={28} />
            <div className="min-w-0 flex-1">
              <div className="mb-1.5 flex items-baseline justify-between gap-3">
                <span className="flex min-w-0 items-center gap-1.5 text-sm font-medium">
                  <Dot color={colorOf(app.exe)} className="size-2" />
                  <span className="truncate">{app.displayName}</span>
                </span>
                <span className="shrink-0 text-sm whitespace-nowrap text-zinc-600 tabular dark:text-zinc-300">{fmtDuration(app.seconds)}</span>
              </div>
              <div className="h-1.5 rounded-full bg-zinc-100 dark:bg-zinc-800">
                <div className="h-full rounded-full" style={{ width: `${Math.max(2, (app.seconds / max) * 100)}%`, background: colorOf(app.exe) }} />
              </div>
            </div>
          </li>
        ))}
      </ul>
      {apps.length > 7 && (
        <button
          type="button"
          onClick={() => setExpanded(!expanded)}
          className="mt-4 text-sm font-medium text-violet-600 hover:text-violet-500 dark:text-violet-400"
        >
          {expanded ? 'Свернуть' : `Показать все (${apps.length})`}
        </button>
      )}
    </div>
  )
}

function TodaySkeleton() {
  return (
    <div className="space-y-6">
      <Skeleton className="h-10 w-48" />
      <Skeleton className="h-40" />
      <div className="grid gap-4 md:grid-cols-3">
        <Skeleton className="h-36" />
        <Skeleton className="h-36" />
        <Skeleton className="h-36" />
      </div>
    </div>
  )
}
