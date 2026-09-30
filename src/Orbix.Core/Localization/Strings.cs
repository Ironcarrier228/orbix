namespace Orbix.Core.Localization;

/// <summary>
/// All user-visible strings. Each entry is declared once with both translations (Russian, English).
/// </summary>
internal static class Strings
{
    internal static readonly Dictionary<string, (string Ru, string En)> Table = Build();

    private static Dictionary<string, (string Ru, string En)> Build()
    {
        var t = new Dictionary<string, (string Ru, string En)>(StringComparer.Ordinal);
        void A(string key, string ru, string en) => t.Add(key, (ru, en));

        // ---- Default menu content ----
        A("Default.Profile", "Основной", "Main");
        A("Default.Explorer", "Проводник", "File Explorer");
        A("Default.Notepad", "Блокнот", "Notepad");
        A("Default.Calculator", "Калькулятор", "Calculator");
        A("Default.Terminal", "Командная строка", "Command Prompt");
        A("Default.Paint", "Paint", "Paint");
        A("Default.Settings", "Параметры Windows", "Windows Settings");
        A("Default.TaskManager", "Диспетчер задач", "Task Manager");
        A("Default.System", "Система", "System");
        A("Default.Lock", "Блокировка", "Lock");
        A("Default.Screenshot", "Скриншот", "Screenshot");
        A("Default.ShowDesktop", "Рабочий стол", "Show desktop");
        A("Default.Power", "Питание", "Power");
        A("Default.Sleep", "Сон", "Sleep");
        A("Default.Restart", "Перезагрузка", "Restart");
        A("Default.Shutdown", "Выключение", "Shut down");
        A("Default.SignOut", "Выход из системы", "Sign out");

        return t;
    }
}
