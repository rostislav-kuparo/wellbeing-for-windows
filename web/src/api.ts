// Типы повторяют DTO службы (src/Wellbeing.Service/Api/Dtos.cs).

/** Разрешённое время суток, «18:00»–«22:00». Может переходить через полночь. */
export interface TimeWindowDto {
  from: string
  to: string
}

export type BlockReason = 'none' | 'exhausted' | 'outsideWindow'

export type ActivityState = 'counting' | 'idle' | 'locked' | 'noWindow' | 'noUser' | 'sleep' | 'waiting'
export type AttributionMode = 'agent' | 'grace' | 'fallback' | 'none'

export interface LimitProgress {
  id: string
  name: string
  exes: string[]
  allowedSeconds: number
  usedSeconds: number
  remainingSeconds: number
  exhausted: boolean
  hasPendingChange: boolean
  blockReason: BlockReason
  /** Сколько можно прямо сейчас: меньшее из остатка минут и времени до конца окна. */
  availableSeconds: number
  window: TimeWindowDto | null
  windowSecondsLeft: number | null
}

export interface AppUsage {
  exe: string
  displayName: string
  seconds: number
  hasIcon: boolean
}

export interface TodayDto {
  day: string
  dayStart: string
  dayEnd: string
  totalSeconds: number
  limits: LimitProgress[]
  apps: AppUsage[]
  current: { exe: string | null; displayName: string | null; state: ActivityState; mode: AttributionMode }
  agent: { status: 'ok' | 'late' | 'missing'; lastSeenSecondsAgo: number | null }
}

export interface TimelineDto {
  day: string
  dayStart: string
  dayEnd: string
  minutes: { t: number; exe: string; s: number }[]
}

export interface DailyDto {
  days: { day: string; totalSeconds: number; apps: Record<string, number> }[]
  names: Record<string, string>
}

export interface AppDto {
  exe: string
  displayName: string
  path: string | null
  lastSeen: string
  secondsLast7Days: number
  hasIcon: boolean
  canLimit: boolean
  cannotLimitReason: string | null
}

export interface LimitConfig {
  name: string
  exes: string[]
  weekdayMinutes: number
  weekendMinutes: number | null
  weekdayWindow: TimeWindowDto | null
  weekendWindow: TimeWindowDto | null
}

export interface LimitItem {
  id: string
  today: LimitConfig | null
  tomorrow: LimitConfig | null
  hasPendingChange: boolean
  pendingDelete: boolean
  allowedSecondsToday: number
  usedSeconds: number
  remainingSeconds: number
  exhausted: boolean
  blockReason: BlockReason
  availableSeconds: number
  windowToday: TimeWindowDto | null
}

export interface LimitsDto {
  day: string
  tomorrowStartsAt: string
  limits: LimitItem[]
}

export interface LimitInput {
  name?: string | null
  exes: string[]
  weekdayMinutes: number
  weekendMinutes: number | null
  weekdayWindow: TimeWindowDto | null
  weekendWindow: TimeWindowDto | null
}

export type ChangeTiming = 'none' | 'immediate' | 'deferred' | 'partial'

export interface ChangeResult {
  timing: ChangeTiming
  tomorrowStartsAt: string
  message: string
  id: string | null
}

export interface Accounting {
  dayResetTime: string
  idleThresholdMinutes: number
}

export interface NotificationSettings {
  warningsEnabled: boolean
  warnMinutes: number[]
}

export interface SettingsDto {
  today: Accounting
  tomorrow: Accounting | null
  notifications: NotificationSettings
  allowedWarnMinutes: number[]
  tomorrowStartsAt: string
  service: { version: string; mode: 'service' | 'console'; dataDirectory: string; processStartEvents: boolean }
}

export interface LogEntry {
  id: number
  timestamp: string
  kind: string
  message: string
}

export class ApiError extends Error {
  readonly status: number
  readonly errors: string[]

  constructor(status: number, errors: string[]) {
    super(errors.join('\n'))
    this.status = status
    this.errors = errors
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  let response: Response
  try {
    response = await fetch(path, {
      method,
      credentials: 'same-origin',
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError(0, ['Служба Wellbeing недоступна'])
  }

  if (!response.ok) {
    let errors = [`Ошибка ${response.status}`]
    if (response.status === 401) errors = ['Нет доступа к службе. Откройте Wellbeing через значок в трее.']
    else {
      try {
        const json = await response.json()
        if (Array.isArray(json?.errors)) errors = json.errors
      } catch {
        // тело не JSON — оставляем код ошибки
      }
    }
    throw new ApiError(response.status, errors)
  }

  return (await response.json()) as T
}

const q = (params: Record<string, string | boolean | undefined>) => {
  const entries = Object.entries(params).filter(([, v]) => v !== undefined && v !== false) as [string, string | boolean][]
  return entries.length ? '?' + new URLSearchParams(entries.map(([k, v]) => [k, String(v)])).toString() : ''
}

export const api = {
  today: () => request<TodayDto>('GET', '/api/today'),
  timeline: (day?: string) => request<TimelineDto>('GET', '/api/usage/timeline' + q({ day })),
  daily: (from: string, to: string) => request<DailyDto>('GET', '/api/usage/daily' + q({ from, to })),
  apps: () => request<AppDto[]>('GET', '/api/apps'),
  registerApp: (path: string) => request<AppDto>('POST', '/api/apps/register', { path }),
  limits: () => request<LimitsDto>('GET', '/api/limits'),
  createLimit: (input: LimitInput, dryRun = false) => request<ChangeResult>('POST', '/api/limits' + q({ dryRun }), input),
  updateLimit: (id: string, input: LimitInput, dryRun = false) =>
    request<ChangeResult>('PUT', `/api/limits/${id}` + q({ dryRun }), input),
  deleteLimit: (id: string, dryRun = false) => request<ChangeResult>('DELETE', `/api/limits/${id}` + q({ dryRun })),
  cancelPending: (id: string) => request<ChangeResult>('POST', `/api/limits/${id}/cancel-pending`),
  settings: () => request<SettingsDto>('GET', '/api/settings'),
  saveSettings: (input: { accounting?: Accounting; notifications?: NotificationSettings }) =>
    request<{ accounting: ChangeResult | null; settings: SettingsDto }>('PUT', '/api/settings', input),
  log: (take = 50) => request<LogEntry[]>('GET', '/api/log' + q({ take: String(take) })),
}

export const iconUrl = (exe: string) => `/api/apps/${encodeURIComponent(exe)}/icon`
