import { Bell, CalendarClock, Clock, Moon, ShieldCheck, Sun } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'
import { api, ApiError, type LogEntry, type NotificationSettings, type SettingsDto } from '../api'
import { Badge, Button, Card, cx, ErrorText, Segmented, Skeleton, Toggle } from '../components/ui'
import { fmtDateTime, fmtLimitMinutes, fmtWhen } from '../format'
import { useLive, useTheme, useToast } from '../live'

export default function SettingsPage() {
  const { revision, bump, today } = useLive()
  const toast = useToast()
  const { theme, setTheme } = useTheme()
  const [settings, setSettings] = useState<SettingsDto | null>(null)
  const [log, setLog] = useState<LogEntry[]>([])

  useEffect(() => {
    api.settings().then(setSettings).catch((e: ApiError) => toast.show(e.message, 'error'))
    api.log(40).then(setLog).catch(() => {})
  }, [revision])

  if (!settings) return <Skeleton className="h-96" />

  const saveNotifications = async (notifications: NotificationSettings) => {
    setSettings({ ...settings, notifications })
    try {
      const result = await api.saveSettings({ notifications })
      setSettings(result.settings)
    } catch (e) {
      toast.show((e as Error).message, 'error')
    }
  }

  return (
    <div className="space-y-6">
      <header>
        <p className="text-sm text-zinc-500 dark:text-zinc-400">Учёт, уведомления и оформление</p>
        <h1 className="font-display text-2xl font-semibold">Настройки</h1>
      </header>

      <AccountingCard
        settings={settings}
        onSaved={(s, message, immediate) => {
          setSettings(s)
          toast.show(message, immediate ? 'success' : 'info')
          bump()
        }}
      />

      <Card title="Уведомления" subtitle="Предупреждения перед окончанием лимита">
        <div className="space-y-5">
          <Row icon={<Bell className="size-4" />} title="Предупреждать заранее" text="Уведомление Windows, когда лимит подходит к концу">
            <Toggle
              checked={settings.notifications.warningsEnabled}
              onChange={(v) => saveNotifications({ ...settings.notifications, warningsEnabled: v })}
              label="Предупреждать заранее"
            />
          </Row>
          <div className={cx('pl-11', !settings.notifications.warningsEnabled && 'pointer-events-none opacity-40')}>
            <p className="mb-2 text-sm text-zinc-600 dark:text-zinc-300">За сколько минут до конца</p>
            <div className="flex flex-wrap gap-2">
              {[...settings.allowedWarnMinutes].sort((a, b) => b - a).map((m) => {
                const on = settings.notifications.warnMinutes.includes(m)
                return (
                  <button
                    key={m}
                    type="button"
                    onClick={() =>
                      saveNotifications({
                        ...settings.notifications,
                        warnMinutes: on ? settings.notifications.warnMinutes.filter((x) => x !== m) : [...settings.notifications.warnMinutes, m],
                      })
                    }
                    className={cx(
                      'rounded-full border px-3 py-1 text-sm transition-colors',
                      on
                        ? 'border-violet-500 bg-violet-50 font-medium text-violet-700 dark:bg-violet-500/15 dark:text-violet-300'
                        : 'border-zinc-200 text-zinc-600 hover:border-zinc-300 dark:border-zinc-700 dark:text-zinc-400',
                    )}
                  >
                    {m} мин
                  </button>
                )
              })}
            </div>
            <p className="mt-3 text-xs text-zinc-500 dark:text-zinc-400">Сообщение «Лимит на сегодня исчерпан» показывается всегда.</p>
          </div>
        </div>
      </Card>

      <Card title="Оформление">
        <Row icon={theme === 'dark' ? <Moon className="size-4" /> : <Sun className="size-4" />} title="Тема" text="Тёмная по умолчанию">
          <Segmented
            value={theme}
            onChange={setTheme}
            options={[
              { value: 'dark', label: <><Moon className="size-3.5" /> Тёмная</> },
              { value: 'light', label: <><Sun className="size-3.5" /> Светлая</> },
            ]}
          />
        </Row>
      </Card>

      <Card title="Состояние" subtitle="Диагностика службы и агента">
        <dl className="grid gap-x-8 gap-y-3 text-sm sm:grid-cols-2">
          <Info label="Служба" value={settings.service.mode === 'service' ? `служба Windows, версия ${settings.service.version}` : `консольный режим (разработка), версия ${settings.service.version}`} />
          <Info
            label="Агент в трее"
            value={
              today?.agent.status === 'ok' ? (
                <Badge tone="green">на связи</Badge>
              ) : today?.agent.status === 'late' ? (
                <Badge tone="amber">задерживается</Badge>
              ) : (
                <Badge tone="red">не отвечает</Badge>
              )
            }
          />
          <Info
            label="Блокировка запусков"
            value={settings.service.processStartEvents ? <Badge tone="green">мгновенно (WMI)</Badge> : <Badge tone="amber">проверка раз в 5 с</Badge>}
          />
          <Info label="Данные" value={<span className="font-mono text-xs break-all">{settings.service.dataDirectory}</span>} />
        </dl>
        <div className="mt-5 flex items-start gap-2.5 rounded-xl bg-zinc-50 px-3.5 py-3 text-sm text-zinc-600 dark:bg-zinc-800/50 dark:text-zinc-300">
          <ShieldCheck className="mt-0.5 size-4 shrink-0" />
          <p>Системные программы, Проводник и всё из папки Windows никогда не блокируются — ошибка в настройках не может сломать систему.</p>
        </div>
      </Card>

      <Card title="Журнал" subtitle="Последние события службы">
        {log.length === 0 ? (
          <p className="text-sm text-zinc-500">Пока пусто</p>
        ) : (
          <ul className="divide-y divide-zinc-100 text-sm dark:divide-zinc-800">
            {log.map((e) => (
              <li key={e.id} className="flex items-baseline gap-4 py-2">
                <span className="w-28 shrink-0 text-xs text-zinc-500 tabular dark:text-zinc-400">{fmtDateTime(new Date(e.timestamp))}</span>
                <span className="w-20 shrink-0">
                  <Badge tone={kindTone[e.kind] ?? 'neutral'}>{kindLabel[e.kind] ?? e.kind}</Badge>
                </span>
                <span className="text-zinc-700 dark:text-zinc-300">{e.message}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}

const kindLabel: Record<string, string> = {
  service: 'служба',
  agent: 'агент',
  blocked: 'блокировка',
  limit: 'лимит',
  day: 'день',
}

const kindTone: Record<string, 'neutral' | 'violet' | 'amber' | 'red' | 'green'> = {
  blocked: 'red',
  limit: 'amber',
  agent: 'violet',
}

function AccountingCard({ settings, onSaved }: { settings: SettingsDto; onSaved: (s: SettingsDto, message: string, immediate: boolean) => void }) {
  const base = settings.tomorrow ?? settings.today
  const [reset, setReset] = useState(base.dayResetTime)
  const [idle, setIdle] = useState(base.idleThresholdMinutes)
  const [errors, setErrors] = useState<string[]>([])
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    setReset(base.dayResetTime)
    setIdle(base.idleThresholdMinutes)
  }, [base.dayResetTime, base.idleThresholdMinutes])

  const changed = reset !== base.dayResetTime || idle !== base.idleThresholdMinutes

  const save = async () => {
    setSaving(true)
    setErrors([])
    try {
      const result = await api.saveSettings({ accounting: { dayResetTime: reset, idleThresholdMinutes: idle } })
      onSaved(result.settings, result.accounting?.message ?? 'Сохранено', result.accounting?.timing === 'immediate')
    } catch (e) {
      setErrors((e as ApiError).errors ?? [(e as Error).message])
    } finally {
      setSaving(false)
    }
  }

  return (
    <Card title="Учёт времени" subtitle="Изменения, которые ослабляют учёт, вступают в силу со следующего дня">
      <div className="space-y-5">
        <Row icon={<CalendarClock className="size-4" />} title="Начало нового дня" text="Засиживание после полуночи считается тем же днём. Изменение — всегда с завтра.">
          <input
            type="time"
            value={reset}
            onChange={(e) => setReset(e.target.value)}
            className="h-9 rounded-lg border border-zinc-200 bg-white px-3 text-sm tabular outline-none focus:border-violet-500 dark:border-zinc-700 dark:bg-zinc-800"
          />
        </Row>
        <Row icon={<Clock className="size-4" />} title="Порог бездействия" text="Без ввода дольше этого время не идёт (кроме полноэкранных окон: видео, игры). Увеличение — сразу, уменьшение — с завтра.">
          <div className="flex items-center gap-2">
            <input
              type="number"
              min={1}
              max={60}
              value={idle}
              onChange={(e) => setIdle(Math.round(Number(e.target.value)))}
              className="h-9 w-20 rounded-lg border border-zinc-200 bg-white px-3 text-sm tabular outline-none focus:border-violet-500 dark:border-zinc-700 dark:bg-zinc-800"
            />
            <span className="text-sm text-zinc-500">мин</span>
          </div>
        </Row>

        {settings.tomorrow && (
          <p className="flex items-center gap-2 rounded-lg bg-violet-50 px-3 py-2 text-sm text-violet-900 dark:bg-violet-500/10 dark:text-violet-200">
            <CalendarClock className="size-4" />
            Сейчас действует: начало дня в {settings.today.dayResetTime}, порог {fmtLimitMinutes(settings.today.idleThresholdMinutes)}. Новые значения — {fmtWhen(settings.tomorrowStartsAt)}.
          </p>
        )}

        <ErrorText errors={errors} />
        <div className="flex justify-end">
          <Button variant="primary" onClick={save} disabled={!changed || saving}>
            {saving ? 'Сохраняю…' : 'Сохранить'}
          </Button>
        </div>
      </div>
    </Card>
  )
}

function Row({ icon, title, text, children }: { icon: ReactNode; title: string; text?: string; children: ReactNode }) {
  return (
    <div className="flex items-center gap-4">
      <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-300">{icon}</span>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium">{title}</p>
        {text && <p className="text-xs text-zinc-500 dark:text-zinc-400">{text}</p>}
      </div>
      {children}
    </div>
  )
}

function Info({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-zinc-500 dark:text-zinc-400">{label}</dt>
      <dd className="mt-0.5">{value}</dd>
    </div>
  )
}
