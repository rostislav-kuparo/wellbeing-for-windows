using Wellbeing.Core.Limits;

namespace Wellbeing.Core.Text;

/// <summary>Тексты о блокировке для уведомлений и журнала.</summary>
public static class BlockText
{
    public static string Title(LimitStatus status) =>
        status.BlockReason == BlockReason.Exhausted ? "Лимит на сегодня исчерпан" : "Сейчас недоступно";

    /// <summary>«лимит «Игры» на сегодня исчерпан» / ««Игры» доступны с 18:00 до 22:00».</summary>
    public static string Reason(LimitStatus status)
    {
        var name = status.Rule.Name;
        return status.BlockReason switch
        {
            BlockReason.Exhausted => $"лимит «{name}» на сегодня исчерпан",
            BlockReason.OutsideWindow when status.Window is { IsEmpty: false } w => $"«{name}»: разрешено с {w.From:HH\\:mm} до {w.To:HH\\:mm}",
            BlockReason.OutsideWindow => $"«{name}»: сегодня недоступно",
            _ => string.Empty,
        };
    }

    /// <summary>Строка журнала, когда лимит начинает блокировать программы.</summary>
    public static string Journal(LimitStatus status) => status.BlockReason switch
    {
        BlockReason.Exhausted => $"Лимит «{status.Rule.Name}» исчерпан — программы заблокированы до конца дня",
        BlockReason.OutsideWindow when status.Window is { IsEmpty: false } w => $"«{status.Rule.Name}»: вне разрешённого времени ({w}) — программы заблокированы",
        BlockReason.OutsideWindow => $"«{status.Rule.Name}»: сегодня недоступно — программы заблокированы",
        _ => string.Empty,
    };
}
