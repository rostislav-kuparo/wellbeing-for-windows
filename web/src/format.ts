/** «2 ч 05 мин», «23 мин», «< 1 мин». */
export function fmtDuration(seconds: number): string {
  if (seconds <= 0) return '0 мин'
  if (seconds < 60) return '< 1 мин'
  const totalMinutes = Math.floor(seconds / 60)
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  if (hours === 0) return `${minutes} мин`
  return minutes === 0 ? `${hours} ч` : `${hours} ч ${String(minutes).padStart(2, '0')} мин`
}

/** Остаток с округлением вверх до минуты: 61 с → «2 мин». */
export function fmtRemaining(seconds: number): string {
  if (seconds <= 0) return '0 мин'
  return fmtDuration(Math.ceil(seconds / 60) * 60)
}

/** 1440 минут — «без лимита минут»: ограничено только разрешённым временем. */
export const UNLIMITED_MINUTES = 1440

/** «18:00–22:00» */
export const fmtWindow = (w: { from: string; to: string }) => `${w.from}–${w.to}`

/** Длина окна в секундах с учётом перехода через полночь. */
export function windowLengthSeconds(w: { from: string; to: string }): number {
  const toSec = (t: string) => {
    const [h, m] = t.split(':').map(Number)
    return h * 3600 + m * 60
  }
  return (((toSec(w.to) - toSec(w.from)) % 86400) + 86400) % 86400
}

/** Минуты лимита: 90 → «1 ч 30 мин». */
export function fmtLimitMinutes(minutes: number): string {
  if (minutes === 0) return '0 мин'
  return fmtDuration(minutes * 60)
}

/** Короткая подпись для осей: «2 ч», «30 мин», «1,5 ч». */
export function fmtAxis(seconds: number): string {
  if (seconds === 0) return '0'
  if (seconds < 3600) return `${Math.round(seconds / 60)} мин`
  const hours = seconds / 3600
  return `${Number.isInteger(hours) ? hours : hours.toFixed(1).replace('.', ',')} ч`
}

export function plural(n: number, forms: [string, string, string]): string {
  const mod10 = n % 10
  const mod100 = n % 100
  if (mod10 === 1 && mod100 !== 11) return forms[0]
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return forms[1]
  return forms[2]
}

/** 'YYYY-MM-DD' → локальная дата (полночь). */
export function parseDay(day: string): Date {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(y, m - 1, d)
}

export function dayKey(date: Date): string {
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${m}-${d}`
}

export function addDays(date: Date, days: number): Date {
  const result = new Date(date)
  result.setDate(result.getDate() + days)
  return result
}

/** Понедельник недели, в которую входит дата. */
export function startOfWeek(date: Date): Date {
  const result = new Date(date.getFullYear(), date.getMonth(), date.getDate())
  const shift = (result.getDay() + 6) % 7
  return addDays(result, -shift)
}

export function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1)
}

export function daysBetween(from: Date, to: Date): number {
  return Math.round((to.getTime() - from.getTime()) / 86_400_000)
}

export const weekdayShort = ['Вс', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб']

const timeFormat = new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit' })
const dateFormat = new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'long' })
const dateWeekdayFormat = new Intl.DateTimeFormat('ru-RU', { weekday: 'long', day: 'numeric', month: 'long' })
const monthFormat = new Intl.DateTimeFormat('ru-RU', { month: 'long', year: 'numeric' })
const dateTimeFormat = new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

export const fmtTime = (date: Date) => timeFormat.format(date)
export const fmtDate = (date: Date) => dateFormat.format(date)
export const fmtDateWeekday = (date: Date) => dateWeekdayFormat.format(date)
export const fmtDateTime = (date: Date) => dateTimeFormat.format(date)
export const fmtMonth = (date: Date) => {
  const text = monthFormat.format(date).replace(' г.', '')
  return text.charAt(0).toUpperCase() + text.slice(1)
}

/** «завтра в 04:00» / «сегодня в 04:00» (если сейчас глубокая ночь). */
export function fmtWhen(iso: string): string {
  const at = new Date(iso)
  const now = new Date()
  const word = at.toDateString() === now.toDateString() ? 'сегодня' : 'завтра'
  return `${word} в ${fmtTime(at)}`
}
