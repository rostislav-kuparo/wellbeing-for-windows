import type { MeterTone } from './ui'

/** Состояние лимита: исчерпан, скоро кончится (≤ 10 мин или ≤ 15%), всё в порядке. */
export function limitTone(allowedSeconds: number, remainingSeconds: number, exhausted: boolean): MeterTone {
  if (exhausted) return 'critical'
  if (remainingSeconds <= 600 || remainingSeconds <= allowedSeconds * 0.15) return 'warning'
  return 'accent'
}
