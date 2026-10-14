import { useLayoutEffect, useMemo, useRef, useState, type MouseEvent } from 'react'
import type { TimelineDto } from '../api'
import { fmtDuration, fmtTime } from '../format'
import { useAppColor } from '../live'
import { Dot } from './ui'

interface Run {
  start: number // unix-секунды начала первой минуты
  end: number // unix-секунды конца последней минуты
  exe: string
  seconds: number
}

/** Склеивает поминутные записи в отрезки: в каждой минуте берётся программа, которая была активна дольше всех. */
function buildRuns(minutes: TimelineDto['minutes']): Run[] {
  const dominant = new Map<number, { exe: string; s: number }>()
  for (const m of minutes) {
    const current = dominant.get(m.t)
    if (!current || m.s > current.s) dominant.set(m.t, { exe: m.exe, s: m.s })
  }

  const runs: Run[] = []
  for (const t of [...dominant.keys()].sort((a, b) => a - b)) {
    const { exe, s } = dominant.get(t)!
    const last = runs.at(-1)
    if (last && last.exe === exe && t - last.end <= 60) {
      last.end = t + 60
      last.seconds += s
    } else {
      runs.push({ start: t, end: t + 60, exe, seconds: s })
    }
  }
  return runs
}

const BAR_TOP = 6
const BAR_HEIGHT = 40
const HEIGHT = BAR_TOP + BAR_HEIGHT + 26

export function Timeline({ data, names }: { data: TimelineDto; names: Record<string, string> }) {
  const colorOf = useAppColor()
  const containerRef = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(800)
  const [hover, setHover] = useState<{ run: Run; x: number } | null>(null)

  useLayoutEffect(() => {
    const element = containerRef.current
    if (!element) return
    const observer = new ResizeObserver(([entry]) => setWidth(Math.max(320, entry.contentRect.width)))
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const start = new Date(data.dayStart).getTime() / 1000
  const end = new Date(data.dayEnd).getTime() / 1000
  const span = Math.max(1, end - start)
  const x = (t: number) => ((t - start) / span) * width

  const runs = useMemo(() => buildRuns(data.minutes), [data.minutes])

  const ticks = useMemo(() => {
    const result: number[] = []
    for (let t = start; t <= end; t += 3 * 3600) result.push(t)
    return result
  }, [start, end])

  const now = Date.now() / 1000
  const showNow = now > start && now < end

  const legend = useMemo(() => {
    const totals = new Map<string, number>()
    for (const r of runs) totals.set(r.exe, (totals.get(r.exe) ?? 0) + r.seconds)
    return [...totals.entries()].sort((a, b) => b[1] - a[1]).slice(0, 8)
  }, [runs])

  const onMove = (event: MouseEvent<SVGSVGElement>) => {
    const rect = event.currentTarget.getBoundingClientRect()
    const px = event.clientX - rect.left
    const t = start + (px / width) * span
    // Цель наведения шире самой полоски: ищем ближайший отрезок в пределах 6 px.
    const tolerance = (6 / width) * span
    const run = runs.find((r) => t >= r.start - tolerance && t <= r.end + tolerance)
    setHover(run ? { run, x: px } : null)
  }

  return (
    <div ref={containerRef} className="relative">
      <svg width={width} height={HEIGHT} onMouseMove={onMove} onMouseLeave={() => setHover(null)} className="block select-none" role="img" aria-label="Таймлайн дня">
        <rect x={0} y={BAR_TOP} width={width} height={BAR_HEIGHT} rx={8} fill="var(--hover)" />
        {ticks.map((t) => (
          <g key={t}>
            <line x1={x(t)} x2={x(t)} y1={BAR_TOP} y2={BAR_TOP + BAR_HEIGHT} stroke="var(--grid)" strokeWidth={1} />
            <text
              x={Math.min(Math.max(x(t), 16), width - 16)}
              y={HEIGHT - 6}
              textAnchor="middle"
              fontSize={11}
              fill="var(--muted)"
              className="tabular"
            >
              {fmtTime(new Date(t * 1000))}
            </text>
          </g>
        ))}
        {runs.map((r) => {
          const left = x(r.start)
          const w = Math.max(1.5, x(r.end) - left)
          const active = hover?.run === r
          return (
            <rect
              key={r.start}
              x={left}
              y={BAR_TOP + (active ? 0 : 3)}
              width={w}
              height={BAR_HEIGHT - (active ? 0 : 6)}
              rx={Math.min(3, w / 2)}
              fill={colorOf(r.exe)}
              opacity={hover && !active ? 0.55 : 1}
            />
          )
        })}
        {showNow && (
          <g>
            <line x1={x(now)} x2={x(now)} y1={BAR_TOP - 4} y2={BAR_TOP + BAR_HEIGHT + 4} stroke="var(--ink)" strokeWidth={1.5} />
            <circle cx={x(now)} cy={BAR_TOP - 4} r={3} fill="var(--ink)" />
          </g>
        )}
      </svg>

      {hover && (
        <div
          className="pointer-events-none absolute top-0 z-10 -translate-x-1/2 -translate-y-[calc(100%+6px)] rounded-lg border border-zinc-200 bg-white px-3 py-2 text-xs shadow-lg dark:border-zinc-700 dark:bg-zinc-800"
          style={{ left: Math.min(Math.max(hover.x, 90), width - 90) }}
        >
          <p className="flex items-center gap-1.5 font-medium text-zinc-900 dark:text-zinc-100">
            <Dot color={colorOf(hover.run.exe)} />
            {names[hover.run.exe] ?? hover.run.exe}
          </p>
          <p className="mt-0.5 text-zinc-500 tabular dark:text-zinc-400">
            {fmtTime(new Date(hover.run.start * 1000))}–{fmtTime(new Date(hover.run.end * 1000))} · активно {fmtDuration(hover.run.seconds)}
          </p>
        </div>
      )}

      {legend.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-zinc-600 dark:text-zinc-400">
          {legend.map(([exe]) => (
            <span key={exe} className="inline-flex items-center gap-1.5">
              <Dot color={colorOf(exe)} />
              {names[exe] ?? exe}
            </span>
          ))}
        </div>
      )}
    </div>
  )
}
