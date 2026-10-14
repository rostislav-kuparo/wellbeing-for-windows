import { CalendarClock, Check, CircleCheck, Clock, FolderOpen, Info, Pencil, Plus, Search, Timer, Trash, Undo2 } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  api,
  ApiError,
  type AppDto,
  type ChangeResult,
  type LimitConfig,
  type LimitInput,
  type LimitItem,
  type LimitProgress,
  type TimeWindowDto,
} from '../api'
import { hasNativeBridge, pickExe } from '../bridge'
import { limitTone } from '../components/limitTone'
import { AppIcon, Badge, Button, Card, cx, EmptyState, ErrorText, IconButton, Meter, Modal, Skeleton, Toggle } from '../components/ui'
import { fmtDuration, fmtLimitMinutes, fmtWhen, fmtWindow, plural, UNLIMITED_MINUTES } from '../format'
import { useLive, useToast } from '../live'
import { AppStack } from './TodayPage'

export default function LimitsPage() {
  const { today, revision, bump } = useLive()
  const toast = useToast()
  const [limits, setLimits] = useState<LimitItem[] | null>(null)
  const [tomorrowStartsAt, setTomorrowStartsAt] = useState<string>('')
  const [apps, setApps] = useState<AppDto[]>([])
  const [editing, setEditing] = useState<LimitItem | 'new' | null>(null)
  const [deleting, setDeleting] = useState<LimitItem | null>(null)

  const showToast = toast.show
  const load = useCallback(() => {
    api
      .limits()
      .then((l) => {
        setLimits(l.limits)
        setTomorrowStartsAt(l.tomorrowStartsAt)
      })
      .catch((e: ApiError) => showToast(e.message, 'error'))
    api.apps().then(setApps).catch(() => {})
  }, [showToast])

  useEffect(load, [load, revision])

  const names = useMemo(() => Object.fromEntries(apps.map((a) => [a.exe, a.displayName])), [apps])
  const live = useMemo(() => new Map((today?.limits ?? []).map((l) => [l.id, l])), [today?.limits])

  const onChanged = (result: ChangeResult) => {
    toast.show(result.message, result.timing === 'immediate' ? 'success' : 'info')
    bump()
  }

  const cancelPending = async (item: LimitItem) => {
    try {
      await api.cancelPending(item.id)
      toast.show('Запланированное изменение отменено', 'success')
      bump()
    } catch (e) {
      toast.show((e as Error).message, 'error')
    }
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="text-sm text-zinc-500 dark:text-zinc-400">Сколько минут в день можно проводить в программах</p>
          <h1 className="font-display text-2xl font-semibold">Лимиты</h1>
        </div>
        <Button variant="primary" onClick={() => setEditing('new')}>
          <Plus className="size-4" /> Добавить лимит
        </Button>
      </header>

      <div className="flex items-start gap-3 rounded-xl border border-violet-200 bg-violet-50/70 px-4 py-3 text-sm text-violet-950 dark:border-violet-500/20 dark:bg-violet-500/[0.07] dark:text-violet-100">
        <Info className="mt-0.5 size-4 shrink-0 text-violet-600 dark:text-violet-300" />
        <p>
          Новые лимиты, уменьшение и сужение разрешённого времени — <b>сразу</b>. Увеличение, расширение времени, удаление и
          исключение программы из группы — <b>со следующего дня</b>
          {tomorrowStartsAt && <> ({fmtWhen(tomorrowStartsAt)})</>}. Так в порыве не получится «дать себе ещё полчасика».
        </p>
      </div>

      {limits === null ? (
        <div className="space-y-3">
          <Skeleton className="h-28" />
          <Skeleton className="h-28" />
        </div>
      ) : limits.length === 0 ? (
        <Card>
          <EmptyState
            icon={<Timer className="size-5" />}
            title="Лимитов пока нет"
            text="Добавьте программу из недавно использованных или выберите .exe. Можно объединить несколько программ в группу с общим лимитом — например, «Игры»."
            action={
              <Button variant="primary" onClick={() => setEditing('new')}>
                <Plus className="size-4" /> Добавить лимит
              </Button>
            }
          />
        </Card>
      ) : (
        <div className="space-y-3">
          {limits.map((item) => (
            <LimitRow
              key={item.id}
              item={item}
              names={names}
              live={live.get(item.id)}
              tomorrowStartsAt={tomorrowStartsAt}
              onEdit={() => setEditing(item)}
              onDelete={() => setDeleting(item)}
              onCancelPending={() => cancelPending(item)}
            />
          ))}
        </div>
      )}

      {editing && (
        <LimitEditor
          item={editing === 'new' ? null : editing}
          apps={apps}
          onAppsChanged={setApps}
          onClose={() => setEditing(null)}
          onSaved={(r) => {
            setEditing(null)
            onChanged(r)
          }}
        />
      )}

      {deleting && (
        <DeleteDialog
          item={deleting}
          tomorrowStartsAt={tomorrowStartsAt}
          onClose={() => setDeleting(null)}
          onDeleted={(r) => {
            setDeleting(null)
            onChanged(r)
          }}
        />
      )}
    </div>
  )
}

const sameWindow = (a: TimeWindowDto | null, b: TimeWindowDto | null) => a?.from === b?.from && a?.to === b?.to

function describePart(minutes: number, window: TimeWindowDto | null): string {
  const text = minutes >= UNLIMITED_MINUTES ? 'без лимита минут' : fmtLimitMinutes(minutes)
  return window ? `${text}, ${fmtWindow(window)}` : text
}

/** «1 ч в день», «1 ч, 18:00–22:00», «будни: 1 ч, 18:00–22:00 · выходные: 3 ч». */
function describe(config: LimitConfig): string {
  const weekday = describePart(config.weekdayMinutes, config.weekdayWindow)
  const separate =
    config.weekendMinutes !== null &&
    (config.weekendMinutes !== config.weekdayMinutes || !sameWindow(config.weekdayWindow, config.weekendWindow))
  if (!separate) return config.weekdayWindow || config.weekdayMinutes >= UNLIMITED_MINUTES ? weekday : `${weekday} в день`
  return `будни: ${weekday} · выходные: ${describePart(config.weekendMinutes!, config.weekendWindow)}`
}

function LimitRow({ item, names, live, tomorrowStartsAt, onEdit, onDelete, onCancelPending }: {
  item: LimitItem
  names: Record<string, string>
  live: LimitProgress | undefined
  tomorrowStartsAt: string
  onEdit: () => void
  onDelete: () => void
  onCancelPending: () => void
}) {
  const config = item.today ?? item.tomorrow!
  // Живые значения из SSE, если есть; иначе — снимок при загрузке страницы.
  const usedSeconds = live?.usedSeconds ?? item.usedSeconds
  const blockReason = live?.blockReason ?? item.blockReason
  const available = live?.availableSeconds ?? item.availableSeconds
  const windowToday = live ? live.window : item.windowToday
  const unlimited = item.allowedSecondsToday >= UNLIMITED_MINUTES * 60
  const exhausted = item.today !== null && blockReason === 'exhausted'
  const outside = item.today !== null && blockReason === 'outsideWindow'
  const tone = outside ? 'warning' : limitTone(unlimited ? 0 : item.allowedSecondsToday, available, exhausted)
  const appNames = config.exes.map((e) => names[e] ?? e)

  return (
    <Card className="!p-0">
      <div className="flex flex-wrap items-center gap-4 p-5">
        <AppStack exes={config.exes} names={names} size={36} />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <p className="truncate font-medium">{config.name}</p>
            {exhausted && <Badge tone="red">исчерпан</Badge>}
            {outside && <Badge tone="amber">сейчас недоступно</Badge>}
          </div>
          <p className="truncate text-sm text-zinc-500 dark:text-zinc-400" title={appNames.join(', ')}>
            {config.exes.length > 1 ? `${config.exes.length} ${plural(config.exes.length, ['программа', 'программы', 'программ'])}: ` : ''}
            {appNames.join(', ')}
          </p>
        </div>

        <div className="w-64">
          <p className="mb-1.5 text-sm font-medium">{item.today ? describe(item.today) : '—'}</p>
          {item.today && (
            <>
              {!unlimited && (
                <Meter value={item.allowedSecondsToday > 0 ? usedSeconds / item.allowedSecondsToday : 1} tone={tone} className="h-1.5" />
              )}
              <p className="mt-1 text-xs text-zinc-500 dark:text-zinc-400">
                сегодня {fmtDuration(usedSeconds)}
                {!unlimited && <> из {fmtLimitMinutes(item.allowedSecondsToday / 60)}</>}
                {outside && windowToday && <> · откроется в {windowToday.from}</>}
                {!outside && !exhausted && windowToday && <> · доступно до {windowToday.to}</>}
              </p>
            </>
          )}
        </div>

        <div className="flex items-center gap-1">
          <IconButton label="Изменить" onClick={onEdit}>
            <Pencil className="size-4" />
          </IconButton>
          {!item.pendingDelete && (
            <IconButton label="Удалить" onClick={onDelete} className="hover:!text-red-600 dark:hover:!text-red-400">
              <Trash className="size-4" />
            </IconButton>
          )}
        </div>
      </div>

      {item.hasPendingChange && (
        <div className="flex flex-wrap items-center gap-3 border-t border-zinc-100 bg-zinc-50/80 px-5 py-2.5 text-sm dark:border-zinc-800 dark:bg-zinc-950/40">
          <CalendarClock className="size-4 shrink-0 text-violet-600 dark:text-violet-400" />
          <p className="flex-1 text-zinc-700 dark:text-zinc-300">
            {item.pendingDelete ? (
              <>Лимит будет удалён {fmtWhen(tomorrowStartsAt)}</>
            ) : (
              <>
                С {fmtWhen(tomorrowStartsAt)}: <b>{describe(item.tomorrow!)}</b>
                {item.tomorrow!.name !== item.today?.name && <> · «{item.tomorrow!.name}»</>}
                {item.tomorrow!.exes.length !== item.today?.exes.length && (
                  <> · {item.tomorrow!.exes.length} {plural(item.tomorrow!.exes.length, ['программа', 'программы', 'программ'])}</>
                )}
              </>
            )}
          </p>
          <Button variant="ghost" className="h-8" onClick={onCancelPending}>
            <Undo2 className="size-4" /> Отменить
          </Button>
        </div>
      )}
    </Card>
  )
}

const PRESETS = [15, 30, 45, 60, 90, 120, 180]

function Chip({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cx(
        'rounded-full border px-2.5 py-0.5 text-xs transition-colors',
        active
          ? 'border-violet-500 bg-violet-50 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300'
          : 'border-zinc-200 text-zinc-600 hover:border-zinc-300 dark:border-zinc-700 dark:text-zinc-400 dark:hover:border-zinc-600',
      )}
    >
      {children}
    </button>
  )
}

/** Минуты в день; 1440 — «без лимита минут» (тогда ограничивает только разрешённое время). */
function MinutesField({ label, value, onChange }: { label: string; value: number; onChange: (v: number) => void }) {
  const unlimited = value >= UNLIMITED_MINUTES
  return (
    <div>
      <label className="mb-1.5 block text-sm font-medium">{label}</label>
      {unlimited ? (
        <p className="flex h-9 items-center text-sm text-zinc-600 dark:text-zinc-300">Без лимита минут — ограничивает только разрешённое время</p>
      ) : (
        <div className="flex items-center gap-3">
          <div className="relative w-32">
            <input
              type="number"
              min={0}
              max={1439}
              step={5}
              value={Number.isNaN(value) ? '' : value}
              onChange={(e) => onChange(e.target.value === '' ? NaN : Math.round(Number(e.target.value)))}
              className="h-9 w-full rounded-lg border border-zinc-200 bg-white pr-12 pl-3 text-sm tabular outline-none focus:border-violet-500 focus:ring-2 focus:ring-violet-500/20 dark:border-zinc-700 dark:bg-zinc-800"
            />
            <span className="pointer-events-none absolute top-1/2 right-3 -translate-y-1/2 text-sm text-zinc-400">мин</span>
          </div>
          <span className="text-sm text-zinc-500 dark:text-zinc-400">{Number.isNaN(value) ? '' : `= ${fmtLimitMinutes(value)}`}</span>
        </div>
      )}
      <div className="mt-2 flex flex-wrap gap-1.5">
        {PRESETS.map((p) => (
          <Chip key={p} active={p === value} onClick={() => onChange(p)}>
            {fmtLimitMinutes(p)}
          </Chip>
        ))}
        <Chip active={unlimited} onClick={() => onChange(UNLIMITED_MINUTES)}>
          без лимита
        </Chip>
      </div>
    </div>
  )
}

const timeInputClass =
  'h-9 rounded-lg border border-zinc-200 bg-white px-2.5 text-sm tabular outline-none focus:border-violet-500 focus:ring-2 focus:ring-violet-500/20 dark:border-zinc-700 dark:bg-zinc-800'

/** Разрешённое время суток: вне его программа закрывается. null — весь день. */
function WindowField({ value, onChange }: { value: TimeWindowDto | null; onChange: (w: TimeWindowDto | null) => void }) {
  const [last, setLast] = useState<TimeWindowDto>(value ?? { from: '18:00', to: '22:00' })
  const set = (w: TimeWindowDto) => {
    setLast(w)
    onChange(w)
  }

  return (
    <div className="rounded-xl bg-zinc-50 px-4 py-3 dark:bg-zinc-800/50">
      <div className="flex items-center justify-between gap-4">
        <div>
          <p className="text-sm font-medium">Только в определённое время</p>
          <p className="text-xs text-zinc-500 dark:text-zinc-400">Вне этого времени программа закрывается. Можно через полночь: 20:00–01:00.</p>
        </div>
        <Toggle checked={value !== null} onChange={(on) => onChange(on ? last : null)} label="Только в определённое время" />
      </div>
      {value && (
        <div className="mt-3 flex flex-wrap items-center gap-2 text-sm">
          <span className="text-zinc-500 dark:text-zinc-400">с</span>
          <input type="time" value={value.from} onChange={(e) => e.target.value && set({ ...value, from: e.target.value })} className={timeInputClass} />
          <span className="text-zinc-500 dark:text-zinc-400">до</span>
          <input type="time" value={value.to} onChange={(e) => e.target.value && set({ ...value, to: e.target.value })} className={timeInputClass} />
          {value.from === value.to && <span className="text-xs text-red-600 dark:text-red-400">начало и конец должны различаться</span>}
        </div>
      )}
    </div>
  )
}
function LimitEditor({ item, apps, onAppsChanged, onClose, onSaved }: {
  item: LimitItem | null
  apps: AppDto[]
  onAppsChanged: (apps: AppDto[]) => void
  onClose: () => void
  onSaved: (result: ChangeResult) => void
}) {
  const base = item ? (item.tomorrow ?? item.today)! : null
  const [selected, setSelected] = useState<string[]>(base?.exes ?? [])
  const [name, setName] = useState(base?.name ?? '')
  const [weekday, setWeekday] = useState(base?.weekdayMinutes ?? 60)
  const [weekdayWindow, setWeekdayWindow] = useState<TimeWindowDto | null>(base?.weekdayWindow ?? null)
  const [weekendOn, setWeekendOn] = useState(
    base?.weekendMinutes != null && (base.weekendMinutes !== base.weekdayMinutes || !sameWindow(base.weekdayWindow, base.weekendWindow)),
  )
  const [weekend, setWeekend] = useState(base?.weekendMinutes ?? base?.weekdayMinutes ?? 120)
  const [weekendWindow, setWeekendWindow] = useState<TimeWindowDto | null>(
    base?.weekendMinutes != null ? base.weekendWindow : (base?.weekdayWindow ?? null),
  )
  const [search, setSearch] = useState('')
  const [manualPath, setManualPath] = useState('')
  const [preview, setPreview] = useState<ChangeResult | null>(null)
  const [errors, setErrors] = useState<string[]>([])
  const [saving, setSaving] = useState(false)

  const byExe = useMemo(() => new Map(apps.map((a) => [a.exe, a])), [apps])
  const autoName = selected.length === 1 ? (byExe.get(selected[0])?.displayName ?? selected[0]) : selected.length > 1 ? 'Группа' : ''

  const input: LimitInput = useMemo(
    () => ({
      name: name.trim() || null,
      exes: selected,
      weekdayMinutes: weekday,
      weekendMinutes: weekendOn ? weekend : null,
      weekdayWindow,
      weekendWindow: weekendOn ? weekendWindow : null,
    }),
    [name, selected, weekday, weekendOn, weekend, weekdayWindow, weekendWindow],
  )

  // «Без лимита минут» без окна времени ничего не ограничивает — такой лимит не сохраняем.
  const pointless =
    (weekday >= UNLIMITED_MINUTES && !weekdayWindow) || (weekendOn && weekend >= UNLIMITED_MINUTES && !weekendWindow)
  const windowsOk = (w: TimeWindowDto | null) => !w || w.from !== w.to
  const valid =
    selected.length > 0 &&
    !Number.isNaN(weekday) &&
    (!weekendOn || !Number.isNaN(weekend)) &&
    !pointless &&
    windowsOk(weekdayWindow) &&
    (!weekendOn || windowsOk(weekendWindow))

  // Предпросмотр: служба сама считает, применится ли изменение сразу или с завтра.
  useEffect(() => {
    if (!valid) {
      setPreview(null)
      return
    }
    let cancelled = false
    const timer = window.setTimeout(() => {
      const request = item ? api.updateLimit(item.id, input, true) : api.createLimit(input, true)
      request
        .then((r) => {
          if (cancelled) return
          setPreview(r)
          setErrors([])
        })
        .catch((e: ApiError) => {
          if (cancelled) return
          setPreview(null)
          setErrors(e.errors ?? [e.message])
        })
    }, 350)
    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [input, valid, item])

  const toggle = (exe: string) => setSelected((s) => (s.includes(exe) ? s.filter((e) => e !== exe) : [...s, exe]))

  const register = async (path: string) => {
    try {
      const app = await api.registerApp(path)
      onAppsChanged([app, ...apps.filter((a) => a.exe !== app.exe)])
      if (!app.canLimit) setErrors([`${app.displayName}: ${app.cannotLimitReason}`])
      else if (!selected.includes(app.exe)) setSelected([...selected, app.exe])
      setManualPath('')
    } catch (e) {
      setErrors((e as ApiError).errors ?? [(e as Error).message])
    }
  }

  const browse = async () => {
    const path = await pickExe()
    if (path) await register(path)
  }

  const save = async () => {
    setSaving(true)
    try {
      const result = item ? await api.updateLimit(item.id, input) : await api.createLimit(input)
      onSaved(result)
    } catch (e) {
      setErrors((e as ApiError).errors ?? [(e as Error).message])
    } finally {
      setSaving(false)
    }
  }

  const query = search.trim().toLowerCase()
  const list = apps
    .filter((a) => !query || a.displayName.toLowerCase().includes(query) || a.exe.includes(query))
    .sort((a, b) => Number(selected.includes(b.exe)) - Number(selected.includes(a.exe)))
  const unknownSelected = selected.filter((exe) => !byExe.has(exe))

  return (
    <Modal
      open
      wide
      onClose={onClose}
      title={item ? 'Изменить лимит' : 'Новый лимит'}
      footer={
        <>
          <PreviewNote preview={preview} />
          <Button onClick={onClose}>Отмена</Button>
          <Button variant="primary" onClick={save} disabled={!valid || saving || preview?.timing === 'none' || errors.length > 0}>
            {saving ? 'Сохраняю…' : 'Сохранить'}
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        <div>
          <div className="mb-2 flex items-center justify-between gap-3">
            <label className="text-sm font-medium">
              Программы {selected.length > 0 && <span className="text-zinc-500">· выбрано {selected.length}</span>}
            </label>
            {hasNativeBridge && (
              <Button className="h-8" onClick={browse}>
                <FolderOpen className="size-4" /> Выбрать .exe…
              </Button>
            )}
          </div>
          <div className="relative mb-2">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-zinc-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Поиск среди недавно использованных"
              className="h-9 w-full rounded-lg border border-zinc-200 bg-white pr-3 pl-9 text-sm outline-none focus:border-violet-500 focus:ring-2 focus:ring-violet-500/20 dark:border-zinc-700 dark:bg-zinc-800"
            />
          </div>
          <div className="max-h-64 overflow-y-auto rounded-xl border border-zinc-200 dark:border-zinc-800">
            {list.length === 0 && unknownSelected.length === 0 && (
              <p className="px-4 py-6 text-center text-sm text-zinc-500">
                {apps.length === 0 ? 'Программы появятся здесь, как только вы начнёте ими пользоваться.' : 'Ничего не найдено'}
              </p>
            )}
            {unknownSelected.map((exe) => (
              <AppOption key={exe} exe={exe} title={exe} subtitle="ещё не запускалась" checked onToggle={() => toggle(exe)} />
            ))}
            {list.map((app) => (
              <AppOption
                key={app.exe}
                exe={app.exe}
                title={app.displayName}
                subtitle={app.canLimit ? `${app.exe}${app.secondsLast7Days > 0 ? ` · ${fmtDuration(app.secondsLast7Days)} за неделю` : ''}` : app.cannotLimitReason ?? ''}
                checked={selected.includes(app.exe)}
                disabled={!app.canLimit}
                onToggle={() => toggle(app.exe)}
              />
            ))}
          </div>
          {!hasNativeBridge && (
            <div className="mt-2 flex gap-2">
              <input
                value={manualPath}
                onChange={(e) => setManualPath(e.target.value)}
                placeholder="C:\Games\game.exe — полный путь к программе"
                className="h-9 flex-1 rounded-lg border border-zinc-200 bg-white px-3 text-sm outline-none focus:border-violet-500 dark:border-zinc-700 dark:bg-zinc-800"
              />
              <Button onClick={() => register(manualPath)} disabled={!manualPath.trim()}>
                Добавить
              </Button>
            </div>
          )}
        </div>

        <div>
          <label className="mb-1.5 block text-sm font-medium">Название</label>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={autoName || 'Например, «Игры»'}
            maxLength={80}
            className="h-9 w-full rounded-lg border border-zinc-200 bg-white px-3 text-sm outline-none focus:border-violet-500 focus:ring-2 focus:ring-violet-500/20 dark:border-zinc-700 dark:bg-zinc-800"
          />
          {selected.length > 1 && <p className="mt-1 text-xs text-zinc-500">Несколько программ — это группа: время всех программ складывается в один общий лимит.</p>}
        </div>

        <section className="space-y-3">
          {weekendOn && <h3 className="text-xs font-semibold tracking-wide text-zinc-500 uppercase dark:text-zinc-400">Будни</h3>}
          <MinutesField label={weekendOn ? 'Лимит в будни' : 'Лимит в день'} value={weekday} onChange={setWeekday} />
          <WindowField value={weekdayWindow} onChange={setWeekdayWindow} />
        </section>

        <div className="flex items-center justify-between gap-4 rounded-xl border border-zinc-200 px-4 py-3 dark:border-zinc-800">
          <div>
            <p className="text-sm font-medium">Отдельные настройки на выходные</p>
            <p className="text-xs text-zinc-500 dark:text-zinc-400">Суббота и воскресенье: свои минуты и своё разрешённое время</p>
          </div>
          <Toggle checked={weekendOn} onChange={setWeekendOn} label="Отдельные настройки на выходные" />
        </div>
        {weekendOn && (
          <section className="space-y-3">
            <h3 className="text-xs font-semibold tracking-wide text-zinc-500 uppercase dark:text-zinc-400">Выходные</h3>
            <MinutesField label="Лимит в выходные" value={weekend} onChange={setWeekend} />
            <WindowField value={weekendWindow} onChange={setWeekendWindow} />
          </section>
        )}

        {pointless && (
          <p className="text-sm text-amber-700 dark:text-amber-300">«Без лимита минут» имеет смысл только вместе с разрешённым временем — иначе лимит ничего не ограничивает.</p>
        )}
        <ErrorText errors={errors} />
      </div>
    </Modal>
  )
}

function AppOption({ exe, title, subtitle, checked, disabled, onToggle }: {
  exe: string
  title: string
  subtitle: string
  checked: boolean
  disabled?: boolean
  onToggle: () => void
}) {
  return (
    <button
      type="button"
      onClick={onToggle}
      disabled={disabled}
      className={cx(
        'flex w-full items-center gap-3 border-b border-zinc-100 px-3 py-2 text-left last:border-b-0 dark:border-zinc-800',
        disabled ? 'cursor-not-allowed opacity-50' : 'hover:bg-zinc-50 dark:hover:bg-zinc-800/60',
        checked && 'bg-violet-50/70 dark:bg-violet-500/[0.08]',
      )}
    >
      <span
        className={cx(
          'flex size-5 shrink-0 items-center justify-center rounded-md border',
          checked ? 'border-violet-600 bg-violet-600 text-white dark:border-violet-500 dark:bg-violet-500' : 'border-zinc-300 dark:border-zinc-600',
        )}
      >
        {checked && <Check className="size-3.5" strokeWidth={3} />}
      </span>
      <AppIcon exe={exe} name={title} size={26} />
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-medium">{title}</span>
        <span className="block truncate text-xs text-zinc-500 dark:text-zinc-400">{subtitle}</span>
      </span>
    </button>
  )
}

function PreviewNote({ preview }: { preview: ChangeResult | null }) {
  if (!preview) return <span className="mr-auto" />
  const map = {
    immediate: { icon: <CircleCheck className="size-4 text-emerald-600 dark:text-emerald-400" />, text: 'Применится сразу' },
    deferred: { icon: <Clock className="size-4 text-violet-600 dark:text-violet-400" />, text: `Вступит в силу ${fmtWhen(preview.tomorrowStartsAt)}` },
    partial: { icon: <Clock className="size-4 text-violet-600 dark:text-violet-400" />, text: `Ограничение — сразу, послабление — ${fmtWhen(preview.tomorrowStartsAt)}` },
    none: { icon: <Info className="size-4 text-zinc-400" />, text: 'Без изменений' },
  }[preview.timing]
  return (
    <span className="mr-auto flex items-center gap-1.5 text-sm text-zinc-600 dark:text-zinc-300">
      {map.icon}
      {map.text}
    </span>
  )
}

function DeleteDialog({ item, tomorrowStartsAt, onClose, onDeleted }: {
  item: LimitItem
  tomorrowStartsAt: string
  onClose: () => void
  onDeleted: (r: ChangeResult) => void
}) {
  const [busy, setBusy] = useState(false)
  const [errors, setErrors] = useState<string[]>([])
  const name = (item.today ?? item.tomorrow)!.name

  const confirm = async () => {
    setBusy(true)
    try {
      onDeleted(await api.deleteLimit(item.id))
    } catch (e) {
      setErrors((e as ApiError).errors ?? [(e as Error).message])
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={`Удалить лимит «${name}»?`}
      footer={
        <>
          <Button onClick={onClose}>Отмена</Button>
          <Button variant="danger" onClick={confirm} disabled={busy}>
            Удалить с завтра
          </Button>
        </>
      }
    >
      <p className="text-sm text-zinc-600 dark:text-zinc-300">
        Удаление вступит в силу {fmtWhen(tomorrowStartsAt)}. До этого лимит продолжит действовать. Передумаете — отмените изменение в списке лимитов.
      </p>
      <div className="mt-3">
        <ErrorText errors={errors} />
      </div>
    </Modal>
  )
}
