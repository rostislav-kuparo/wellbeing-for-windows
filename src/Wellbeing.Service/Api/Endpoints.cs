using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Wellbeing.Core;
using Wellbeing.Core.Abstractions;
using Wellbeing.Core.Contracts;
using Wellbeing.Core.Limits;
using Wellbeing.Core.Policies;
using Wellbeing.Core.Safety;
using Wellbeing.Core.Time;
using Wellbeing.Core.Tracking;
using Wellbeing.Service.Blocking;
using Wellbeing.Service.Data;
using Wellbeing.Service.Tracking;
using Wellbeing.Win32;

namespace Wellbeing.Service.Api;

public static class Endpoints
{
    static readonly string Version =
        typeof(Endpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";

    public static void MapWellbeingApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/health", () => Results.Ok(new { status = "ok", version = Version }));

        api.MapPost("/agent/heartbeat", Heartbeat);
        api.MapGet("/today", (Tracker tracker) => tracker.Today is { } today ? Results.Ok(today) : Results.StatusCode(503));
        api.MapGet("/usage/timeline", Timeline);
        api.MapGet("/usage/daily", Daily);

        api.MapGet("/apps", Apps);
        api.MapGet("/apps/{exe}/icon", Icon);
        api.MapPost("/apps/register", RegisterApp);

        api.MapGet("/limits", Limits);
        api.MapPost("/limits", CreateLimit);
        api.MapPut("/limits/{id:guid}", UpdateLimit);
        api.MapDelete("/limits/{id:guid}", DeleteLimit);
        api.MapPost("/limits/{id:guid}/cancel-pending", CancelPending);

        api.MapGet("/settings", GetSettings);
        api.MapPut("/settings", PutSettings);

        api.MapGet("/log", (StateRepository state, int? take) => state.RecentEvents(Math.Clamp(take ?? 100, 1, 500)));
        api.MapGet("/events", Events);
    }

    // ── агент ────────────────────────────────────────────────────────────────

    static HeartbeatResponse Heartbeat(HeartbeatRequest request, AgentRegistry agents, AppCatalog apps, NotificationHub hub, Tracker tracker)
    {
        var exe = string.IsNullOrWhiteSpace(request.Exe) ? null : ExeName.Normalize(request.Exe);
        var sample = new ActivitySample(exe, request.WindowTitle, Math.Max(0, request.IdleSeconds), request.IsFullscreen, request.IsLocked);
        agents.Record(request.SessionId, request.AgentProcessId, sample);
        if (exe is not null) apps.Touch(exe, request.ExePath);
        return new HeartbeatResponse(hub.Drain(request.SessionId), tracker.TrayText);
    }

    // ── статистика ───────────────────────────────────────────────────────────

    static IResult Timeline(string? day, Tracker tracker, UsageRepository usage, PolicyService policies)
    {
        if (!TryParseDay(day, tracker.CurrentDay, out var date)) return BadRequest("Некорректная дата");
        tracker.Flush();
        var minutes = usage.Timeline(date).Select(r => new TimelineEntryDto(r.MinuteUtc, r.Exe, r.Seconds)).ToList();
        return Results.Ok(new TimelineDto(LogicalDay.Format(date), policies.DayStartsAt(date), policies.DayStartsAt(date.AddDays(1)), minutes));
    }

    static IResult Daily(string? from, string? to, Tracker tracker, UsageRepository usage, AppCatalog apps)
    {
        if (!TryParseDay(to, tracker.CurrentDay, out var toDay)) return BadRequest("Некорректная дата");
        if (!TryParseDay(from, toDay.AddDays(-6), out var fromDay)) return BadRequest("Некорректная дата");
        if (fromDay > toDay || toDay.DayNumber - fromDay.DayNumber > 400) return BadRequest("Некорректный период");

        tracker.Flush();
        var rows = usage.Daily(fromDay, toDay);
        var byDay = rows
            .GroupBy(r => r.Day)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, long>)g.ToDictionary(r => r.Exe, r => r.Seconds));

        var days = new List<DayUsageDto>();
        for (var d = fromDay; d <= toDay; d = d.AddDays(1))
        {
            var key = LogicalDay.Format(d);
            var appsOfDay = byDay.GetValueOrDefault(key) ?? new Dictionary<string, long>();
            days.Add(new DayUsageDto(key, appsOfDay.Values.Sum(), appsOfDay));
        }

        var names = rows.Select(r => r.Exe).Distinct().ToDictionary(e => e, apps.DisplayName);
        return Results.Ok(new DailyDto(days, names));
    }

    // ── программы ────────────────────────────────────────────────────────────

    static IReadOnlyList<AppDto> Apps(Tracker tracker, UsageRepository usage, AppCatalog apps, ProtectionRules protection)
    {
        tracker.Flush();
        var today = tracker.CurrentDay;
        var week = usage.TotalsBetween(today.AddDays(-6), today);
        return apps.All
            .OrderByDescending(a => a.LastSeenUtc)
            .Select(a => ToDto(a, week.GetValueOrDefault(a.Exe), protection))
            .ToList();
    }

    static IResult Icon(string exe, AppCatalog apps, HttpContext context)
    {
        if (!ExeName.IsValid(exe)) return Results.NotFound();
        var png = apps.Icon(ExeName.Normalize(exe));
        if (png is null) return Results.NotFound();
        context.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.File(png, "image/png");
    }

    static IResult RegisterApp(RegisterAppInput input, AppCatalog apps, ProtectionRules protection)
    {
        var path = input.Path?.Trim().Trim('"');
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || !ExeName.IsValid(Path.GetFileName(path)))
            return BadRequest("Укажите полный путь к .exe");
        if (!File.Exists(path)) return BadRequest("Файл не найден");

        var info = apps.Register(path);
        return Results.Ok(ToDto(info, 0, protection));
    }

    static AppDto ToDto(AppInfo app, long weekSeconds, ProtectionRules protection)
    {
        var reason = protection.WhyCannotLimit(app.Exe, app.Path, WindowsPaths.ExistsInSystemDirectories);
        return new AppDto(app.Exe, app.DisplayName, app.Path, DateTimeOffset.FromUnixTimeSeconds(app.LastSeenUtc),
            weekSeconds, app.HasIcon, reason is null, reason);
    }

    // ── лимиты ───────────────────────────────────────────────────────────────

    static LimitsDto Limits(Tracker tracker, PolicyService policies, IClock clock)
    {
        var day = tracker.CurrentDay;
        var today = policies.ForDay(day);
        var now = TimeOnly.FromDateTime(clock.ToLocal(clock.UtcNow));
        var statuses = LimitEvaluator.Evaluate(today, day, tracker.UsageToday(), now).ToDictionary(s => s.Rule.Id);

        var items = PolicyBook.Diff(today, policies.Latest).Select(change =>
        {
            var status = statuses.GetValueOrDefault(change.Id);
            return new LimitItemDto(
                change.Id,
                ToDto(change.Today),
                ToDto(change.Tomorrow),
                change.HasPendingChange,
                change.PendingDelete,
                status?.AllowedSeconds ?? 0,
                status?.UsedSeconds ?? 0,
                status?.RemainingSeconds ?? 0,
                status?.IsExhausted ?? false,
                Dto.Reason(status?.BlockReason ?? BlockReason.None),
                status?.AvailableSeconds ?? 0,
                Dto.Window(status?.Window));
        }).ToList();

        return new LimitsDto(LogicalDay.Format(day), policies.TomorrowStartsAt(day), items);
    }

    static IResult CreateLimit(LimitInput input, bool? dryRun, Tracker tracker, PolicyService policies, AppCatalog apps, ProtectionRules protection)
    {
        if (!TryBuildRule(Guid.NewGuid(), input, apps, protection, out var rule, out var errors)) return Results.BadRequest(errors);
        var outcome = policies.Change(tracker.CurrentDay, p => p with { Limits = [.. p.Limits, rule] },
            $"Новый лимит «{rule.Name}»: {Minutes(rule)}", dryRun == true);
        return Results.Ok(ToDto(outcome, outcome.TimingFor(rule.Id), rule.Id));
    }

    static IResult UpdateLimit(Guid id, LimitInput input, bool? dryRun, Tracker tracker, PolicyService policies, AppCatalog apps, ProtectionRules protection)
    {
        var day = tracker.CurrentDay;
        var existing = policies.Latest.Find(id) ?? policies.ForDay(day).Find(id);
        if (existing is null) return Results.NotFound();
        if (!TryBuildRule(id, input, apps, protection, out var rule, out var errors)) return Results.BadRequest(errors);

        var outcome = policies.Change(day, p => Upsert(p, rule),
            $"Лимит «{rule.Name}»: {Minutes(existing)} → {Minutes(rule)}", dryRun == true);
        return Results.Ok(ToDto(outcome, outcome.TimingFor(id), id));
    }

    static IResult DeleteLimit(Guid id, bool? dryRun, Tracker tracker, PolicyService policies)
    {
        var existing = policies.Latest.Find(id);
        if (existing is null) return Results.NotFound();
        var outcome = policies.Change(tracker.CurrentDay, p => p with { Limits = p.Limits.Where(l => l.Id != id).ToList() },
            $"Удалён лимит «{existing.Name}»", dryRun == true);
        return Results.Ok(ToDto(outcome, outcome.TimingFor(id), id));
    }

    static IResult CancelPending(Guid id, Tracker tracker, PolicyService policies)
    {
        var day = tracker.CurrentDay;
        var today = policies.ForDay(day).Find(id);
        var outcome = policies.Change(day,
            p => today is null ? p with { Limits = p.Limits.Where(l => l.Id != id).ToList() } : Upsert(p, today),
            $"Отменено запланированное изменение «{today?.Name ?? id.ToString()}»", dryRun: false);
        return Results.Ok(ToDto(outcome, outcome.TimingFor(id), id));
    }

    static bool TryBuildRule(Guid id, LimitInput input, AppCatalog apps, ProtectionRules protection, out LimitRule rule, out ErrorsDto errors)
    {
        var exes = (input.Exes ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(ExeName.Normalize)
            .Distinct()
            .ToList();
        var name = string.IsNullOrWhiteSpace(input.Name)
            ? exes.Count == 1 ? apps.DisplayName(exes[0]) : "Группа"
            : input.Name.Trim();

        var weekdayWindow = Dto.ParseWindow(input.WeekdayWindow, out var weekdayError);
        var weekendWindow = Dto.ParseWindow(input.WeekendWindow, out var weekendError);
        rule = LimitRule.Create(id, name, exes, input.WeekdayMinutes, input.WeekendMinutes, weekdayWindow, weekendWindow);

        var problems = PolicyValidator.Validate(rule, protection, exe => apps.Get(exe)?.Path, WindowsPaths.ExistsInSystemDirectories).ToList();
        if ((weekdayError ?? weekendError) is { } windowError) problems.Insert(0, windowError);
        errors = new ErrorsDto(problems);
        return problems.Count == 0;
    }

    static Policy Upsert(Policy policy, LimitRule rule) => policy with
    {
        Limits = policy.Find(rule.Id) is null
            ? [.. policy.Limits, rule]
            : policy.Limits.Select(l => l.Id == rule.Id ? rule : l).ToList(),
    };

    /// <summary>Кратко для журнала изменений: «60 мин, 18:00–22:00 / 120 мин».</summary>
    static string Minutes(LimitRule rule)
    {
        static string Part(int minutes, TimeWindow? window) =>
            (minutes >= LimitRule.MaxMinutes ? "без лимита минут" : $"{minutes} мин") + (window is null ? "" : $", {window}");

        var weekday = Part(rule.WeekdayMinutes, rule.WeekdayWindow);
        return rule.HasSeparateWeekend ? $"{weekday} / {Part(rule.EffectiveWeekendMinutes, rule.EffectiveWeekendWindow)}" : weekday;
    }

    static LimitConfigDto? ToDto(LimitRule? rule) =>
        rule is null
            ? null
            : new LimitConfigDto(rule.Name, rule.Exes.ToList(), rule.WeekdayMinutes, rule.WeekendMinutes,
                Dto.Window(rule.WeekdayWindow), Dto.Window(rule.HasSeparateWeekend ? rule.WeekendWindow : null));

    static ChangeResultDto ToDto(PolicyChangeOutcome outcome, ChangeTiming changeTiming, Guid? id)
    {
        var when = WhenText(outcome.TomorrowStartsAt);
        var (timing, message) = changeTiming switch
        {
            ChangeTiming.Immediate => ("immediate", "Применено сразу"),
            ChangeTiming.Deferred => ("deferred", $"Вступит в силу {when}"),
            ChangeTiming.Partial => ("partial", $"Ограничение применено сразу, послабление — {when}"),
            _ => ("none", "Ничего не изменилось"),
        };
        return new ChangeResultDto(timing, outcome.TomorrowStartsAt, message, id);
    }

    /// <summary>«завтра в 04:00» или «сегодня в 04:00», если сейчас глубокая ночь и новый день начнётся сегодня.</summary>
    static string WhenText(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        var word = local.Date == DateTimeOffset.Now.Date ? "сегодня" : "завтра";
        return $"{word} в {local:HH:mm}";
    }

    // ── настройки ────────────────────────────────────────────────────────────

    static SettingsDto GetSettings(Tracker tracker, PolicyService policies, SettingsService settings, ServiceOptions options, ProcessGuard guard)
    {
        var day = tracker.CurrentDay;
        var today = ToDto(policies.ForDay(day).Accounting);
        var tomorrow = ToDto(policies.Latest.Accounting);
        var notifications = settings.Notifications;
        return new SettingsDto(
            today,
            tomorrow == today ? null : tomorrow,
            new NotificationSettingsDto(notifications.WarningsEnabled, notifications.WarnMinutes),
            NotificationSettings.AllowedMinutes,
            policies.TomorrowStartsAt(day),
            new ServiceInfoDto(Version, options.IsWindowsService ? "service" : "console", options.DataDirectory, guard.StartEventsActive));
    }

    static IResult PutSettings(SettingsInput input, Tracker tracker, PolicyService policies, SettingsService settings, ServiceOptions options, ProcessGuard guard)
    {
        ChangeResultDto? accountingResult = null;
        if (input.Accounting is { } accounting)
        {
            if (!TimeOnly.TryParseExact(accounting.DayResetTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var reset))
                return BadRequest("Время сброса в формате ЧЧ:ММ");

            var proposed = new AccountingSettings(reset, accounting.IdleThresholdMinutes * 60);
            var errors = PolicyValidator.Validate(proposed);
            if (errors.Count > 0) return Results.BadRequest(new ErrorsDto(errors));

            var outcome = policies.Change(tracker.CurrentDay, p => p with { Accounting = proposed },
                $"Настройки учёта: сброс в {accounting.DayResetTime}, бездействие {accounting.IdleThresholdMinutes} мин", dryRun: false);
            accountingResult = ToDto(outcome, outcome.AccountingTiming, null);
        }

        if (input.Notifications is { } notifications)
            settings.Save(new NotificationSettings(notifications.WarningsEnabled, notifications.WarnMinutes ?? []));

        return Results.Ok(new SettingsChangeDto(accountingResult, GetSettings(tracker, policies, settings, options, guard)));
    }

    static AccountingDto ToDto(AccountingSettings settings) =>
        new(settings.DayResetTime.ToString("HH:mm", CultureInfo.InvariantCulture), settings.IdleThresholdSeconds / 60);

    // ── события (SSE) ────────────────────────────────────────────────────────

    static async Task Events(HttpContext context, EventBroadcaster events, Tracker tracker)
    {
        var ct = context.RequestAborted;
        var response = context.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";

        var (id, reader) = events.Subscribe();
        try
        {
            if (tracker.Today is { } today)
                await Write(response, new ServerEvent("today", JsonSerializer.Serialize(today, EventBroadcaster.Json)), ct);
            else
                await Comment(response, "hello", ct);

            Task<bool>? wait = null;
            while (!ct.IsCancellationRequested)
            {
                wait ??= reader.WaitToReadAsync(ct).AsTask();
                if (await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(20), ct)) != wait)
                {
                    await Comment(response, "ping", ct);
                    continue;
                }

                var more = await wait;
                wait = null;
                if (!more) break;
                while (reader.TryRead(out var message))
                    await Write(response, message, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            events.Unsubscribe(id);
        }
    }

    static async Task Write(HttpResponse response, ServerEvent message, CancellationToken ct)
    {
        await response.WriteAsync($"event: {message.Name}\ndata: {message.Json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    static async Task Comment(HttpResponse response, string text, CancellationToken ct)
    {
        await response.WriteAsync($": {text}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    // ── общее ────────────────────────────────────────────────────────────────

    static bool TryParseDay(string? value, DateOnly fallback, out DateOnly day)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            day = fallback;
            return true;
        }

        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    static IResult BadRequest(string message) => Results.BadRequest(new ErrorsDto([message]));
}
