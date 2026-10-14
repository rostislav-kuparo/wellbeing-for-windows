import { ArrowDownRight, ArrowUpRight, ChartColumn, ChevronLeft, ChevronRight, Minus } from 'lucide-react'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api, type DailyDto } from '../api'
import { AppIcon, Card, cx, Dot, EmptyState, IconButton, Segmented, Skeleton } from '../components/ui'
import {
  addDays,
  dayKey,
  daysBetween,
  fmtAxis,
  fmtDate,
  fmtDateWeekday,
  fmtDuration,
  fmtMonth,
  parseDay,
  startOfWeek,
  weekdayShort,
} from '../format'
import { useAppColor, useAppOrder, useLive } from '../live'

type Mode = 'week' | 'month'
const OTHER = '__other'

interface Period {
  from: Date
  to: Date
  prevFrom: Date
  prevTo: Date
  /** Сколько дней периода уже прошло (для среднего в день). */
  elapsedDays: number
  title: string
}

function makePeriod(mode: Mode, offset: number, today: Date): Period {
  let from: Date, to: Date, prevFrom: Date, prevTo: Date, title: string
  if (mode === 'week') {
    from = addDays(startOfWeek(today), offset * 7)
    to = addDays(from, 6)
    prevFrom = addDays(from, -7)
    prevTo = addDays(from, -1)
    title = offset === 0 ? 'Эта неделя' : offset === -1 ? 'Прошлая неделя' : `${fmtDate(from)} — ${fmtDate(to)}`
  } else {
    from = new Date(today.getFullYear(), today.getMonth() + offset, 1)
    to = new Date(from.getFullYear(), from.getMonth() + 1, 0)
    prevFrom = new Date(from.getFullYear(), from.getMonth() - 1, 1)
    prevTo = addDays(from, -1)
    title = fmtMonth(from)
  }
  const elapsedDays = today < from ? 0 : today > to ? daysBetween(from, to) + 1 : daysBetween(from, today) + 1
  return { from, to, prevFrom, prevTo, elapsedDays, title }
}

/** Шаг делений оси Y — «круглые» значения времени, не больше ~5 делений. */
function niceTicks(maxSeconds: number): number[] {
  const steps = [900, 1800, 3600, 7200, 10800, 14400, 21600]
  const step = steps.find((s) => maxSeconds / s <= 5) ?? 28800
  const ticks: number[] = []
  for (let t = 0; t <= maxSeconds + step * 0.25 || ticks.length < 2; t += step) ticks.push(t)
  return ticks
}

export default function HistoryPage() {
  const { today } = useLive()
  const [mode, setMode] = useState<Mode>('week')
  const [offset, setOffset] = useState(0)
  const [data, setData] = useState<DailyDto | null>(null)
  const [error, setError] = useState<string | null>(null)

  const todayDate = useMemo(() => (today ? parseDay(today.day) : null), [today?.day])
  const period = useMemo(() => (todayDate ? makePeriod(mode, offset, todayDate) : null), [mode, offset, todayDate])

  useEffect(() => {
    if (!period) return
    let cancelled = false
    setData(null)
    api
      .daily(dayKey(period.prevFrom), dayKey(period.to))
      .then((d) => !cancelled && setData(d))
      .catch((e) => !cancelled && setError(e.message))
    return () => {
      cancelled = true
    }
  }, [period])

  if (!period || !todayDate) return <Skeleton className="h-96" />

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="text-sm text-zinc-500 dark:text-zinc-400">История</p>
          <h1 className="font-display text-2xl font-semibold">{period.title}</h1>
        </div>
        <div className="flex items-center gap-3">
          <Segmented
            value={mode}
            onChange={(m) => {
              setMode(m)
              setOffset(0)
            }}
            options={[
              { value: 'week', label: 'Неделя' },
              { value: 'month', label: 'Месяц' },
            ]}
          />
          <div className="flex items-center">
            <IconButton label="Назад" onClick={() => setOffset(offset - 1)}>
              <ChevronLeft className="size-4" />
            </IconButton>
            <IconButton label="Вперёд" onClick={() => setOffset(offset + 1)} disabled={offset >= 0} className="disabled:opacity-30">
              <ChevronRight className="size-4" />
            </IconButton>
          </div>
        </div>
      </header>

      {error && <p className="text-sm text-red-600">{error}</p>}
      {data ? <HistoryContent data={data} mode={mode} period={period} todayKey={dayKey(todayDate)} /> : <Skeleton className="h-96" />}
    </div>
  )
}

function HistoryContent({ data, mode, period, todayKey }: { data: DailyDto; mode: Mode; period: Period; todayKey: string }) {
  const colorOf = useAppColor()
  const order = useAppOrder()

  const { current, previous } = useMemo(() => {
    const fromKey = dayKey(period.from)
    return {
      current: data.days.filter((d) => d.day >= fromKey),
      previous: data.days.filter((d) => d.day < fromKey),
    }
  }, [data, period])

  const total = current.reduce((s, d) => s + d.totalSeconds, 0)
  const prevTotal = previous.reduce((s, d) => s + d.totalSeconds, 0)
  const avg = period.elapsedDays > 0 ? total / period.elapsedDays : 0
  const prevAvg = previous.length > 0 ? prevTotal / previous.length : 0
  const name = (exe: string) => (exe === OTHER ? 'Другие' : (data.names[exe] ?? exe))

  // Серии: программы со своим цветом (в порядке палитры) + «Другие».
  const series = useMemo(() => {
    const present = new Set(current.flatMap((d) => Object.keys(d.apps)))
    const colored = order.filter((exe) => present.has(exe))
    const hasOther = [...present].some((exe) => !colored.includes(exe))
    return hasOther ? [...colored, OTHER] : colored
  }, [current, order])

  const rows = useMemo(
    () =>
      current.map((d) => {
        const date = parseDay(d.day)
        const row: Record<string, number | string> = {
          key: d.day,
          label: mode === 'week' ? `${weekdayShort[date.getDay()]} ${date.getDate()}` : String(date.getDate()),
          total: d.totalSeconds,
        }
        let other = 0
        for (const [exe, seconds] of Object.entries(d.apps)) {
          if (series.includes(exe)) row[exe] = seconds
          else other += seconds
        }
        if (other > 0) row[OTHER] = other
        row.__top = [...series].reverse().find((s) => Number(row[s] ?? 0) > 0) ?? ''
        return row
      }),
    [current, series, mode],
  )

  const maxDay = Math.max(0, ...current.map((d) => d.totalSeconds))
  const ticks = niceTicks(maxDay)
  const periodWord = mode === 'week' ? 'неделе' : 'месяцу'

  if (total === 0 && prevTotal === 0)
    return (
      <Card>
        <EmptyState icon={<ChartColumn className="size-5" />} title="Нет данных за этот период" text="Как только вы начнёте пользоваться компьютером, здесь появятся графики." />
      </Card>
    )

  return (
    <div className="space-y-6">
      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile label={mode === 'week' ? 'Всего за неделю' : 'Всего за месяц'} value={fmtDuration(total)} />
        <StatTile label="В среднем в день" value={fmtDuration(avg)} />
        <StatTile label={mode === 'week' ? 'К прошлой неделе' : 'К прошлому месяцу'} value={<Delta current={avg} previous={prevAvg} perDay />} />
      </div>

      <Card title="Экранное время по дням" subtitle={`Разбивка по программам · ${fmtDate(period.from)} — ${fmtDate(period.to)}`}>
        <Legend items={series.map((s) => ({ key: s, label: name(s), color: s === OTHER ? 'var(--series-other)' : colorOf(s) }))} />
        <div className="h-72">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={rows} margin={{ top: 8, right: 4, bottom: 0, left: 4 }} barCategoryGap={mode === 'week' ? '30%' : '18%'}>
              <CartesianGrid vertical={false} stroke="var(--grid)" />
              <XAxis
                dataKey="label"
                tickLine={false}
                axisLine={{ stroke: 'var(--axis)' }}
                tick={{ fill: 'var(--muted)', fontSize: 12 }}
                interval={mode === 'month' ? 1 : 0}
              />
              <YAxis
                ticks={ticks}
                domain={[0, ticks.at(-1) ?? 'auto']}
                tickFormatter={fmtAxis}
                tickLine={false}
                axisLine={false}
                width={56}
                tick={{ fill: 'var(--muted)', fontSize: 12 }}
              />
              <Tooltip
                cursor={{ fill: 'var(--hover)' }}
                content={(props) => <DayTooltip {...(props as unknown as TooltipLike)} name={name} colorOf={(s) => (s === OTHER ? 'var(--series-other)' : colorOf(s))} />}
              />
              {series.map((s) => (
                <Bar
                  key={s}
                  dataKey={s}
                  stackId="day"
                  fill={s === OTHER ? 'var(--series-other)' : colorOf(s)}
                  maxBarSize={24}
                  isAnimationActive={false}
                  shape={(props: unknown) => <Segment {...(props as SegmentProps)} isTop={(props as SegmentProps).payload?.__top === s} />}
                />
              ))}
            </BarChart>
          </ResponsiveContainer>
        </div>
        {mode === 'week' && current.some((d) => d.day === todayKey) && <p className="mt-2 text-xs text-zinc-500 dark:text-zinc-400">Сегодняшний день ещё не закончился.</p>}
      </Card>

      {mode === 'week' && <WeekComparison current={current} previous={previous} />}

      <Card title="По программам" subtitle={`Сравнение с прошлым периодом — по ${periodWord} целиком`}>
        <Breakdown current={current} previous={previous} name={name} colorOf={colorOf} total={total} />
      </Card>
    </div>
  )
}

function StatTile({ label, value }: { label: string; value: ReactNode }) {
  return (
    <Card className="py-4">
      <p className="text-sm text-zinc-500 dark:text-zinc-400">{label}</p>
      <div className="font-display mt-1 text-2xl font-semibold">{value}</div>
    </Card>
  )
}

/** Изменение экранного времени. Больше — хуже, поэтому рост красный, снижение зелёное (+ стрелка и подпись). */
function Delta({ current, previous, perDay }: { current: number; previous: number; perDay?: boolean }) {
  if (previous <= 0) return <span className="text-base font-normal text-zinc-500 dark:text-zinc-400">нет данных</span>
  const diff = current - previous
  const percent = Math.round((diff / previous) * 100)
  if (Math.abs(diff) < 60)
    return (
      <span className="inline-flex items-center gap-1.5 text-zinc-600 dark:text-zinc-300">
        <Minus className="size-5" /> без изменений
      </span>
    )
  const up = diff > 0
  return (
    <span className={cx('inline-flex items-center gap-1.5', up ? 'text-red-600 dark:text-red-400' : 'text-[var(--good-text)]')}>
      {up ? <ArrowUpRight className="size-5" /> : <ArrowDownRight className="size-5" />}
      {up ? '+' : '−'}
      {fmtDuration(Math.abs(diff))}
      <span className="text-sm font-normal text-zinc-500 dark:text-zinc-400">
        {perDay ? 'в день · ' : ''}
        {up ? '+' : '−'}
        {Math.abs(percent)}%
      </span>
    </span>
  )
}

function Legend({ items }: { items: { key: string; label: string; color: string }[] }) {
  return (
    <div className="mb-4 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-zinc-600 dark:text-zinc-400">
      {items.map((i) => (
        <span key={i.key} className="inline-flex items-center gap-1.5">
          <Dot color={i.color} />
          {i.label}
        </span>
      ))}
    </div>
  )
}

interface SegmentProps {
  x?: number
  y?: number
  width?: number
  height?: number
  fill?: string
  payload?: Record<string, unknown>
  isTop?: boolean
}

/** Сегмент стопки: 2 px зазора сверху (цвета поверхности) и скругление 4 px только у верхнего сегмента. */
function Segment({ x = 0, y = 0, width = 0, height = 0, fill, isTop }: SegmentProps) {
  if (height <= 0 || width <= 0) return null
  const gap = height > 3 ? 2 : 0
  const top = y + gap
  const h = height - gap
  const r = isTop ? Math.min(4, h, width / 2) : 0
  const d = `M${x},${top + h} L${x},${top + r} Q${x},${top} ${x + r},${top} L${x + width - r},${top} Q${x + width},${top} ${x + width},${top + r} L${x + width},${top + h} Z`
  return <path d={d} fill={fill} />
}

interface TooltipLike {
  active?: boolean
  payload?: { dataKey?: string | number; value?: number; payload?: Record<string, unknown> }[]
}

function DayTooltip({ active, payload, name, colorOf }: TooltipLike & { name: (exe: string) => string; colorOf: (exe: string) => string }) {
  if (!active || !payload?.length) return null
  const row = payload[0].payload ?? {}
  const items = payload.filter((p) => Number(p.value) > 0).sort((a, b) => Number(b.value) - Number(a.value))
  return (
    <div className="min-w-52 rounded-lg border border-zinc-200 bg-white px-3 py-2.5 text-xs shadow-lg dark:border-zinc-700 dark:bg-zinc-800">
      <p className="mb-1.5 font-medium text-zinc-900 capitalize dark:text-zinc-100">{fmtDateWeekday(parseDay(String(row.key)))}</p>
      {items.length === 0 && <p className="text-zinc-500">нет активности</p>}
      <ul className="space-y-1">
        {items.map((p) => (
          <li key={String(p.dataKey)} className="flex items-center justify-between gap-4">
            <span className="flex items-center gap-1.5 text-zinc-600 dark:text-zinc-300">
              <Dot color={colorOf(String(p.dataKey))} className="size-2" />
              {name(String(p.dataKey))}
            </span>
            <span className="text-zinc-900 tabular dark:text-zinc-100">{fmtDuration(Number(p.value))}</span>
          </li>
        ))}
      </ul>
      {items.length > 1 && (
        <p className="mt-1.5 flex justify-between border-t border-zinc-100 pt-1.5 font-medium text-zinc-900 dark:border-zinc-700 dark:text-zinc-100">
          <span>Всего</span>
          <span className="tabular">{fmtDuration(Number(row.total))}</span>
        </p>
      )}
    </div>
  )
}

/** Эта неделя против прошлой по дням недели. */
function WeekComparison({ current, previous }: { current: DailyDto['days']; previous: DailyDto['days'] }) {
  const rows = current.map((d, i) => {
    const date = parseDay(d.day)
    return { label: weekdayShort[date.getDay()], key: d.day, current: d.totalSeconds, previous: previous[i]?.totalSeconds ?? 0 }
  })
  const ticks = niceTicks(Math.max(0, ...rows.map((r) => Math.max(r.current, r.previous))))

  return (
    <Card title="Сравнение с прошлой неделей" subtitle="Экранное время по дням недели">
      <Legend
        items={[
          { key: 'current', label: 'Эта неделя', color: 'var(--accent)' },
          { key: 'previous', label: 'Прошлая неделя', color: 'var(--compare)' },
        ]}
      />
      <div className="h-56">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={rows} margin={{ top: 8, right: 4, bottom: 0, left: 4 }} barGap={2} barCategoryGap="28%">
            <CartesianGrid vertical={false} stroke="var(--grid)" />
            <XAxis dataKey="label" tickLine={false} axisLine={{ stroke: 'var(--axis)' }} tick={{ fill: 'var(--muted)', fontSize: 12 }} />
            <YAxis ticks={ticks} domain={[0, ticks.at(-1) ?? 'auto']} tickFormatter={fmtAxis} tickLine={false} axisLine={false} width={56} tick={{ fill: 'var(--muted)', fontSize: 12 }} />
            <Tooltip
              cursor={{ fill: 'var(--hover)' }}
              content={(props) => {
                const p = props as unknown as TooltipLike
                if (!p.active || !p.payload?.length) return null
                const row = p.payload[0].payload as { key: string; current: number; previous: number }
                return (
                  <div className="rounded-lg border border-zinc-200 bg-white px-3 py-2.5 text-xs shadow-lg dark:border-zinc-700 dark:bg-zinc-800">
                    <p className="mb-1.5 font-medium capitalize">{fmtDateWeekday(parseDay(row.key))}</p>
                    <p className="flex items-center justify-between gap-6">
                      <span className="flex items-center gap-1.5 text-zinc-600 dark:text-zinc-300">
                        <Dot color="var(--accent)" className="size-2" /> Эта неделя
                      </span>
                      <span className="tabular">{fmtDuration(row.current)}</span>
                    </p>
                    <p className="flex items-center justify-between gap-6">
                      <span className="flex items-center gap-1.5 text-zinc-600 dark:text-zinc-300">
                        <Dot color="var(--compare)" className="size-2" /> Прошлая
                      </span>
                      <span className="tabular">{fmtDuration(row.previous)}</span>
                    </p>
                  </div>
                )
              }}
            />
            <Bar dataKey="previous" fill="var(--compare)" maxBarSize={20} radius={[4, 4, 0, 0]} isAnimationActive={false} />
            <Bar dataKey="current" fill="var(--accent)" maxBarSize={20} radius={[4, 4, 0, 0]} isAnimationActive={false} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </Card>
  )
}

function Breakdown({ current, previous, name, colorOf, total }: {
  current: DailyDto['days']
  previous: DailyDto['days']
  name: (exe: string) => string
  colorOf: (exe: string) => string
  total: number
}) {
  const [expanded, setExpanded] = useState(false)
  const sum = (days: DailyDto['days']) => {
    const totals = new Map<string, number>()
    for (const d of days) for (const [exe, s] of Object.entries(d.apps)) totals.set(exe, (totals.get(exe) ?? 0) + s)
    return totals
  }
  const now = sum(current)
  const before = sum(previous)
  const apps = [...now.entries()].sort((a, b) => b[1] - a[1])
  const shown = expanded ? apps : apps.slice(0, 10)

  if (apps.length === 0) return <p className="py-6 text-center text-sm text-zinc-500">За этот период активности нет</p>

  return (
    <div>
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-xs text-zinc-500 dark:text-zinc-400">
            <th className="pb-2 font-medium">Программа</th>
            <th className="w-1/3 pb-2 font-medium">Доля</th>
            <th className="pb-2 text-right font-medium">Время</th>
            <th className="pb-2 text-right font-medium">К прошлому периоду</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-zinc-100 dark:divide-zinc-800">
          {shown.map(([exe, seconds]) => {
            const prev = before.get(exe) ?? 0
            const diff = seconds - prev
            return (
              <tr key={exe}>
                <td className="py-2.5">
                  <span className="flex items-center gap-2.5">
                    <AppIcon exe={exe} name={name(exe)} size={22} />
                    <span className="truncate font-medium">{name(exe)}</span>
                  </span>
                </td>
                <td className="py-2.5 pr-4">
                  <span className="flex items-center gap-2">
                    <span className="h-1.5 flex-1 rounded-full bg-zinc-100 dark:bg-zinc-800">
                      <span className="block h-full rounded-full" style={{ width: `${Math.max(1, (seconds / total) * 100)}%`, background: colorOf(exe) }} />
                    </span>
                    <span className="w-9 text-right text-xs text-zinc-500 tabular dark:text-zinc-400">{Math.round((seconds / total) * 100)}%</span>
                  </span>
                </td>
                <td className="py-2.5 text-right tabular">{fmtDuration(seconds)}</td>
                <td className={cx('py-2.5 text-right text-xs tabular', prev === 0 ? 'text-zinc-500 dark:text-zinc-400' : diff > 60 ? 'text-red-600 dark:text-red-400' : diff < -60 ? 'text-[var(--good-text)]' : 'text-zinc-500 dark:text-zinc-400')}>
                  {prev === 0 ? 'новая' : Math.abs(diff) < 60 ? '≈' : `${diff > 0 ? '+' : '−'}${fmtDuration(Math.abs(diff))}`}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
      {apps.length > 10 && (
        <button type="button" onClick={() => setExpanded(!expanded)} className="mt-3 text-sm font-medium text-violet-600 hover:text-violet-500 dark:text-violet-400">
          {expanded ? 'Свернуть' : `Показать все (${apps.length})`}
        </button>
      )}
    </div>
  )
}

