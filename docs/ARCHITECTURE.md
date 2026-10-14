# Архитектура Wellbeing

## Компоненты

| Проект | Что внутри |
|---|---|
| `Wellbeing.Core` (`net10.0`) | Вся логика без WinAPI и I/O. Интерфейсы для внешнего мира — в `Abstractions/`. |
| `Wellbeing.Win32` (`net10.0-windows`) | Реализации этих интерфейсов: процессы, сессии WTS, `CreateProcessAsUser`, WMI, иконки, ACL. Логики здесь нет. |
| `Wellbeing.Service` | Generic Host + `UseWindowsService` + Kestrel на `127.0.0.1`. Фоновые сервисы: `TrackingWorker` (тик раз в секунду), `ProcessGuard` (блокировка), `AgentSupervisor` (перезапуск агента). |
| `Wellbeing.Agent` | WinForms: трей, heartbeat, уведомления (balloon/toast), окно с WebView2. |
| `web/` | React + Vite + TS + Tailwind + Recharts. Собирается в `src/Wellbeing.Service/wwwroot`, раздаётся службой. |

## Учёт времени

`TrackingWorker` раз в секунду вызывает `Tracker.Tick()`. Решение о том, кому засчитать секунды, принимает `AccountingEngine` (Core):

| Последний heartbeat агента | Кому засчитываются секунды |
|---|---|
| ≤ 3 с назад | exe активного окна, если сессия не заблокирована и (бездействие < порога **или** окно полноэкранное) |
| 3–10 с назад | последнему известному активному окну |
| > 10 с назад | всем запущенным программам с лимитом в активной консольной сессии (если она не заблокирована) |

- Время меряется монотонными часами.
- Тики неровные, поэтому миллисекунды копятся, а засчитываются целые секунды.
- Разрыв между тиками больше 5 с (сон, гибернация) не засчитывается.
- Секунды копятся в `UsageLedger` в памяти и раз в 10 с сбрасываются в `usage_minute` (upsert с суммированием).

**Логический день** (`DayClock`): день D заканчивается в `(D+1) + время сброса`, действующее в день D. Поэтому изменённое сегодня время сброса влияет только на следующую границу. День никогда не откатывается назад. После простоя он перескакивает сразу на нужную дату.

## Политика и правило «ослабление — с завтра»

Политика — это JSON: лимиты (с матчерами) плюс настройки учёта. Она хранится ревизиями в `policy_revision`. На день D действует ревизия с наибольшим `seq` среди тех, у которых `effective_day ≤ D`. Ревизии создаются только «на сегодня» или «на завтра», поэтому политика на завтра всегда равна последней ревизии.

Правка всегда делается от последней ревизии, то есть с учётом уже запланированного (`PolicyEditor.Plan`):

1. Если новая политика **не мягче** сегодняшней (`PolicyMath.IsAtLeastAsStrict`), она записывается ревизией на сегодня.
2. Иначе пишутся две ревизии:
   - на сегодня — `PolicyMath.Strictest(сегодня, новая)`: минимум минут, пересечение разрешённого времени, объединение программ, все лимиты из обеих, максимальный порог бездействия, текущее время сброса;
   - на завтра — новая политика целиком.

Поэтому любая смешанная правка разделяется сама: ужесточающая часть вступает в силу сразу, ослабляющая — завтра. `PolicyEditor.TimingFor` сообщает UI, что произошло с конкретным лимитом: `immediate` / `deferred` / `partial`.

## Блокировка

Лимит блокирует программы, когда минуты исчерпаны (`Exhausted`) или сейчас не разрешённое время (`OutsideWindow`, `TimeWindow` — интервал на круге суток, может переходить через полночь; `null` — весь день). `AvailableSeconds` — меньшее из остатка минут и времени до конца окна, от него считаются предупреждения 10/5/1.

`Tracker` после каждого тика пересчитывает `LimitEvaluator.BlockedExes` (exe → название исчерпанного лимита). При изменении этого набора `ProcessGuard` сразу проходит по процессам. Дальше работают два механизма:

- WMI `Win32_ProcessStartTrace` — событие на каждый новый процесс. Имя в событии может быть урезано, поэтому оно уточняется по PID.
- Страховочный проход раз в 5 с.

Процесс завершается через `TerminateProcess`, только если `ProtectionRules.MayTerminate` разрешает. Нельзя трогать:
- процессы сессии 0;
- процессы, путь которых не удалось узнать;
- всё из `%SystemRoot%`, из папки установки и из папки WebView2;
- критичные имена.

Уведомление «Лимит на сегодня исчерпан» отправляется не чаще раза в 20 с для одной программы. Если агент жив, оно уходит в ответе на heartbeat. Если агента нет, служба показывает его через `WTSSendMessage`.

## Агент и его перезапуск

`AgentSupervisor` раз в 2 с смотрит на активную консольную сессию. Решение принимает `AgentWatchdog` (Core):

- первые 10 с после появления сессии ничего не делает — даёт время агенту из автозагрузки;
- если heartbeat нет дольше 10 с: при наличии процесса агента завершает его и запускает заново, иначе просто запускает (`WTSQueryUserToken` + `CreateProcessAsUser`);
- если запуск не дал heartbeat, следующая попытка откладывается: 10 → 20 → 40 → 80 → 120 с.

В одной сессии работает один агент (мьютекс `Local\Wellbeing.Agent`). Повторный запуск с `--show` (ярлык в «Пуске») просит уже работающий экземпляр открыть окно.

## Безопасность API

- Kestrel слушает только `127.0.0.1`. Заголовок `Host` должен быть `127.0.0.1:<порт>` или `localhost:<порт>` — это защита от DNS rebinding. Чужой `Origin` даёт 403.
- Всё под `/api`, кроме `/api/health`, требует токен: в `Authorization: Bearer …` или в cookie `wb_token`.
- Токен создаёт служба при первом запуске и кладёт в `C:\ProgramData\Wellbeing\agent.json`. Читать файл могут только SYSTEM, администраторы и интерактивно вошедшие пользователи.
- Агент передаёт токен в WebView2 как HttpOnly-cookie с SameSite=Strict, так что JavaScript страницы его не видит.

## Схема БД (SQLite, WAL)

```sql
CREATE TABLE kv_state (key TEXT PRIMARY KEY, value TEXT NOT NULL);         -- current_day и т. п.

CREATE TABLE apps (                                                       -- известные программы
    exe TEXT PRIMARY KEY, display_name TEXT NOT NULL, path TEXT, icon_png BLOB,
    first_seen_utc INTEGER NOT NULL, last_seen_utc INTEGER NOT NULL);

CREATE TABLE usage_minute (                                               -- поминутные агрегаты
    day TEXT NOT NULL,            -- логический день 'YYYY-MM-DD'
    minute_utc INTEGER NOT NULL,  -- unix-время начала минуты
    exe TEXT NOT NULL,
    seconds INTEGER NOT NULL,
    PRIMARY KEY (day, minute_utc, exe));

CREATE TABLE policy_revision (                                            -- история политики
    seq INTEGER PRIMARY KEY, created_utc INTEGER NOT NULL,
    effective_day TEXT NOT NULL, policy_json TEXT NOT NULL, summary TEXT);

CREATE TABLE warning_sent (day TEXT, limit_id TEXT, threshold_min INTEGER,  -- предупреждения 10/5/1
    PRIMARY KEY (day, limit_id, threshold_min));

CREATE TABLE settings (key TEXT PRIMARY KEY, value_json TEXT NOT NULL);   -- уведомления
CREATE TABLE event_log (id INTEGER PRIMARY KEY AUTOINCREMENT, ts_utc INTEGER, kind TEXT, message TEXT);
```

Пример `policy_json`:

```json
{
  "limits": [
    { "id": "…", "name": "Игры",
      "matchers": [ { "type": "exe", "exe": "cs2.exe" }, { "type": "exe", "exe": "dota2.exe" } ],
      "weekdayMinutes": 60, "weekendMinutes": 120,
      "weekdayWindow": { "from": "18:00:00", "to": "22:00:00" }, "weekendWindow": null }
  ],
  "accounting": { "dayResetTime": "04:00:00", "idleThresholdSeconds": 120 }
}
```

## API

| | Путь | Назначение |
|---|---|---|
| GET | `/api/health` | служба жива (без токена) |
| POST | `/api/agent/heartbeat` | агент → служба; в ответе — уведомления и текст для трея |
| GET | `/api/today` | сводка дня: общее время, лимиты с остатком, программы, текущая активность, статус агента |
| GET | `/api/usage/timeline?day=` | поминутный таймлайн дня |
| GET | `/api/usage/daily?from=&to=` | по дням с разбивкой по программам (не больше 400 дней) |
| GET | `/api/apps` | известные программы: недавние первыми, можно ли ограничить |
| GET | `/api/apps/{exe}/icon` | иконка PNG |
| POST | `/api/apps/register` | `{ path }` — добавить .exe, выбранный в диалоге |
| GET | `/api/limits` | лимиты: что действует сегодня и что будет завтра |
| POST | `/api/limits[?dryRun=true]` | создать (`dryRun` — только узнать, когда вступит в силу) |
| PUT | `/api/limits/{id}[?dryRun=true]` | изменить |
| DELETE | `/api/limits/{id}` | удалить (с завтра) |
| POST | `/api/limits/{id}/cancel-pending` | отменить запланированное изменение |
| GET / PUT | `/api/settings` | настройки учёта и уведомлений |
| GET | `/api/log?take=` | журнал событий |
| GET | `/api/events` | SSE: `today` (раз в 2 с и при изменениях), `app-blocked` |

## Как добавить правила по заголовку окна (например, «YouTube» в Chrome)

Заготовка уже есть:
- в политике лимит хранит список `matchers` с полем `type`;
- `Matcher.TitleType` зарезервирован;
- заголовок активного окна приходит в heartbeat (`ActivitySample.WindowTitle`).

Чтобы включить правила, нужно:

1. Добавить в `Tracker` отдельный счётчик секунд по лимитам с title-матчерами (их время нельзя вывести из поминутных агрегатов по exe) и хранить его, например, в таблице `usage_rule_minute`.
2. Учитывать эти секунды в `LimitEvaluator`.
3. Блокировать не процесс, а вкладку: закрывать её нельзя, но можно сворачивать окно или показывать оверлей через агента. Это требует отдельного решения.
4. Снять запрет на `title` в `PolicyValidator` и добавить поле в редактор лимита.
