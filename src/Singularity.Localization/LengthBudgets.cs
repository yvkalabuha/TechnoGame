namespace Singularity.Localization;

/// <summary>
/// Length budgets of UI_DESIGN_SYSTEM §13.2 (ADR-0018 §3.2 L6), in graphemes. The role of a `ui-*` string is the
/// last key segment. When §13.2 changes, this table changes in the same commit; a test compares them.
/// </summary>
public static class LengthBudgets
{
    /// <summary>Row of §13.2 (first column, prefix), role suffix of the key, budget. Suffix null: no character budget.</summary>
    public static readonly (string DocRow, string? Role, int Budget)[] Rows =
    [
        ("Заголовок полноэкранного листа", "sheet_title", 40),
        ("Заголовок вклейки", "inset_title", 28),       // inset of 30% of the screen
        ("Заголовок вклейки", "inset_title_wide", 40),  // inset of 40-50%
        ("Кнопка", "btn", 24),
        ("Кнопка с ценой", "btn_cost", 18),
        ("Вкладка", "tab", 16),
        ("Графа штампа", "stamp", 20),
        ("Место сигнального поля", "signal", 12),
        ("Ярлык надпечатки", "tag", 24),
        ("Подпись шкалы и размера", "scale", 14),
        ("Пункт меню", "menu", 40),
        ("Ячейка таблицы", null, 0),
        ("Тело подсказки", null, 0),
    ];

    /// <summary>Budget of `.short` of a data entity (signal field place, §13.2).</summary>
    public const int DataShort = 12;

    /// <summary>State words (§13.1 item 8): RU and EN limits, no +30%.</summary>
    public const int StateWordRu = 16, StateWordEn = 18;

    public static int? OfRole(string role)
    {
        foreach ((_, string? r, int budget) in Rows)
        {
            if (r == role)
                return budget;
        }
        return null;
    }

    /// <summary>Rule of §13.2: the longest version plus 30% must fit.</summary>
    public static bool Fits(int longest, int budget) => (longest * 13 + 9) / 10 <= budget;
}
