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


        // ---- Tray ----
        A("Tray.Open", "Открыть меню", "Open menu");
        A("Tray.Close", "Закрыть меню", "Close menu");
        A("Tray.ShowOrb", "Показать орб", "Show orb");
        A("Tray.HideOrb", "Скрыть орб", "Hide orb");
        A("Tray.Edit", "Редактировать меню", "Edit menu");
        A("Tray.Profile", "Профиль", "Profile");
        A("Tray.Settings", "Параметры…", "Settings…");
        A("Tray.Autostart", "Запускать вместе с Windows", "Start with Windows");
        A("Tray.Exit", "Выход", "Exit");
        A("Tray.WelcomeTitle", "Orbix запущен", "Orbix is running");
        A("Tray.WelcomeText", "Нажмите {0}, чтобы открыть радиальное меню. Значок остаётся в области уведомлений.", "Press {0} to open the radial menu. The icon stays in the notification area.");
        A("Tray.Conflict", "Сочетание {0} уже занято другим приложением. Выберите другое в параметрах.", "The shortcut {0} is already used by another application. Choose a different one in the settings.");

        // ---- Menu (overlay) ----
        A("Menu.Close", "Закрыть", "Close");
        A("Menu.Back", "Назад", "Back");
        A("Menu.Add", "Добавить элемент", "Add an item");
        A("Menu.Delete", "Удалить «{0}»", "Delete \"{0}\"");
        A("Menu.Deleted", "Удалено: {0}. Ctrl+Z — вернуть", "Deleted: {0}. Ctrl+Z to undo");
        A("Menu.GroupCount", "Группа · элементов: {0}", "Group · {0} items");
        A("Menu.ConfirmAgain", "Нажмите ещё раз, чтобы подтвердить: {0}", "Click again to confirm: {0}");
        A("Menu.MovedInto", "«{0}» перемещён в «{1}»", "\"{0}\" moved into \"{1}\"");
        A("Menu.SearchNone", "ничего не найдено", "nothing found");
        A("Menu.EditBanner", "Редактирование: перетаскивайте значки, ПКМ — действия, Esc — выход", "Editing: drag icons, right click for actions, Esc to leave");
        A("Menu.OrbCentered", "Орб возвращён в центр экрана", "The orb is back in the screen centre");
        A("Menu.DropNothing", "Это нельзя добавить в меню", "This cannot be added to the menu");
        A("Menu.Added", "Добавлено: {0}", "Added: {0}");
        A("Menu.AddedTo", "Добавлено: {0} → «{1}»", "Added: {0} → \"{1}\"");
        A("Menu.NewGroup", "Новая группа", "New group");
        A("Menu.NewLink", "Новая ссылка", "New link");
        A("Menu.NewCommand", "Новая команда", "New command");

        // ---- context menu of the edit mode ----
        A("Ctx.Rename", "Переименовать", "Rename");
        A("Ctx.ChangeIcon", "Сменить значок…", "Change icon…");
        A("Ctx.ResetIcon", "Сбросить значок", "Reset icon");
        A("Ctx.EditInSettings", "Свойства…", "Properties…");
        A("Ctx.Delete", "Удалить", "Delete");
        A("Ctx.AddFile", "Добавить приложение или файл…", "Add an application or file…");
        A("Ctx.AddFolder", "Добавить папку…", "Add a folder…");
        A("Ctx.AddGroup", "Добавить группу", "Add a group");
        A("Ctx.AddLink", "Добавить ссылку…", "Add a link…");
        A("Ctx.AddCommand", "Добавить команду…", "Add a command…");
        A("Ctx.AddSystem", "Системное действие", "System action");
        A("Action.Hibernate", "Гибернация", "Hibernate");
        A("Action.OrbixSettings", "Параметры Orbix", "Orbix settings");

        // ---- dialogs ----
        A("Dialog.ChooseIcon", "Выберите значок", "Choose an icon");
        A("Dialog.ImageFilter", "Изображения и значки|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe;*.dll|Все файлы|*.*", "Images and icons|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe;*.dll|All files|*.*");
        A("Dialog.ChooseFile", "Выберите приложение или файл", "Choose an application or file");
        A("Dialog.AppFilter", "Приложения и ярлыки|*.exe;*.lnk;*.bat;*.cmd;*.url|Все файлы|*.*", "Applications and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.url|All files|*.*");
        A("Dialog.ChooseFolder", "Выберите папку", "Choose a folder");

        // ---- launch errors ----
        A("Launch.NotFound", "Не найдено: {0}", "Not found: {0}");
        A("Launch.Failed", "Не удалось запустить: {0}", "Could not start: {0}");
        A("Launch.NoTarget", "Для элемента не указан файл или адрес", "The item has no file or address");
        A("Launch.AccessDenied", "Нет доступа: {0}", "Access denied: {0}");

        return t;
    }
}
