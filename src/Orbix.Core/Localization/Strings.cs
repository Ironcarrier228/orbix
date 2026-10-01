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

        // ---- settings window ----
        A("Settings.Title", "Orbix — параметры", "Orbix — Settings");
        A("Settings.General", "Общие", "General");
        A("Settings.Orb", "Орб", "Orb");
        A("Settings.Menu", "Меню", "Menu");
        A("Settings.Items", "Элементы меню", "Menu items");
        A("Settings.Appearance", "Оформление", "Appearance");
        A("Settings.About", "О программе", "About");
        A("Settings.Language", "Язык интерфейса", "Interface language");
        A("Settings.CardSystem", "Система", "System");
        A("Settings.CardSize", "Размер и прозрачность", "Size & opacity");
        A("Settings.CardGeometry", "Геометрия", "Geometry");
        A("Settings.CardThemeAccent", "Тема и цвет акцента", "Theme & accent colour");
        A("Settings.CardEditor", "Свойства элемента", "Item properties");
        A("Settings.Autostart", "Запускать вместе с Windows", "Start with Windows");
        A("Settings.HotkeysGroup", "Горячие клавиши", "Hotkeys");
        A("Settings.HotkeyEnabled", "Глобальные горячие клавиши", "Global hotkeys");
        A("Settings.HotkeyOpen", "Открыть меню", "Open the menu");
        A("Settings.HotkeyOrb", "Показать / скрыть орб", "Show / hide the orb");
        A("Settings.HotkeyPlaceholder", "нажмите сочетание…", "press a combination…");
        A("Settings.HotkeyState.Registered", "активно", "active");
        A("Settings.HotkeyState.Conflict", "занято другим приложением", "used by another application");
        A("Settings.HotkeyState.Invalid", "нужны модификаторы и клавиша", "needs modifiers plus a key");
        A("Settings.HotkeyState.Suspended", "приостановлено (полный экран)", "suspended (full screen)");
        A("Settings.HotkeyState.Disabled", "выключено", "off");
        A("Settings.ConfigGroup", "Конфигурация", "Configuration");
        A("Settings.Export", "Экспорт…", "Export…");
        A("Settings.Import", "Импорт…", "Import…");
        A("Settings.OpenFolder", "Открыть папку", "Open folder");
        A("Settings.DataDir", "Папка данных", "Data folder");
        A("Settings.ImportOk", "Конфигурация импортирована", "Configuration imported");
        A("Settings.ImportFail", "Не удалось импортировать: {0}", "Import failed: {0}");
        A("Settings.OrbSize", "Размер орба", "Orb size");
        A("Settings.OrbOpacity", "Прозрачность в покое", "Resting opacity");
        A("Settings.Breathing", "Анимация «дыхание»", "Breathing animation");
        A("Settings.VisibilityGroup", "Видимость", "Visibility");
        A("Settings.Visibility", "Показывать орб", "Show the orb");
        A("Settings.Visibility.Always", "всегда", "Always");
        A("Settings.Visibility.DesktopOnly", "только поверх рабочего стола", "Only over the desktop");
        A("Settings.Visibility.AutoHide", "скрывать в полноэкранных приложениях", "Hide in full-screen apps");
        A("Settings.Monitor", "Монитор", "Monitor");
        A("Settings.Monitor.Primary", "основной", "Primary");
        A("Settings.Monitor.Cursor", "под курсором", "Under the cursor");
        A("Settings.AllowMove", "Разрешить перемещение орба (режим правки)", "Allow moving the orb (edit mode)");
        A("Settings.OrbStyleGroup", "Вид орба", "Orb look");
        A("Settings.OrbColor", "Цвет сферы (hex)", "Sphere colour (hex)");
        A("Settings.OrbImage", "Картинка вместо сферы", "Image instead of the sphere");
        A("Settings.Browse", "Обзор…", "Browse…");
        A("Settings.Clear", "Сброс", "Clear");
        A("Settings.Radius", "Радиус кольца значков", "Icon ring radius");
        A("Settings.ItemSize", "Размер значков", "Icon size");
        A("Settings.HoverDelay", "Задержка открытия при наведении", "Hover-open delay");
        A("Settings.SubmenuOpen", "Открывать группы", "Open groups");
        A("Settings.SubmenuOpen.Hover", "наведением", "On hover");
        A("Settings.SubmenuOpen.Click", "щелчком", "On click");
        A("Settings.MenuBehaviourGroup", "Поведение меню", "Menu behaviour");
        A("Settings.Wheel", "Листать значки колесом", "Cycle icons with the wheel");
        A("Settings.Keyboard", "Управление с клавиатуры (стрелки, цифры)", "Keyboard control (arrows, digits)");
        A("Settings.Tooltips", "Подсказки с названиями", "Name tooltips");
        A("Settings.Rings", "Показывать кольца орбит", "Show orbit guide rings");
        A("Settings.Confirm", "Подтверждать опасные действия вторым щелчком", "Confirm dangerous actions with a second click");
        A("Settings.Animations", "Анимации появления", "Appearance animations");
        A("Settings.Blur", "Размытие фона (акрил)", "Blur the background (acrylic)");
        A("Settings.BlurOpacity", "Непрозрачность фона", "Background opacity");
        A("Settings.ItemsHint", "Перетаскивание и порядок — в меню (Ctrl+E). Выберите элемент, чтобы изменить его.", "Reorder by dragging in the menu (Ctrl+E). Select an item to edit it.");
        A("Settings.GroupHint", "Группа открывается отдельной орбитой; её содержимое редактируется в меню (Ctrl+E).", "A group opens as its own orbit; edit its content in the menu (Ctrl+E).");
        A("Settings.AddApp", "Приложение…", "App…");
        A("Settings.AddGroup", "Группа", "Group");
        A("Settings.AddLink", "Ссылка…", "Link…");
        A("Settings.AddCommand", "Команда…", "Command…");
        A("Settings.AddSystem", "Системное", "System");
        A("Settings.Up", "Выше", "Up");
        A("Settings.Down", "Ниже", "Down");
        A("Settings.Delete", "Удалить", "Delete");
        A("Settings.Name", "Название", "Name");
        A("Settings.Kind", "Тип", "Kind");
        A("Settings.Kind.App", "Приложение или файл", "Application or file");
        A("Settings.Kind.Group", "Группа", "Group");
        A("Settings.Kind.Url", "Ссылка", "Link");
        A("Settings.Kind.Command", "Команда", "Command");
        A("Settings.Kind.System", "Системное действие", "System action");
        A("Settings.Target", "Объект", "Target");
        A("Settings.Arguments", "Аргументы", "Arguments");
        A("Settings.RunAsAdmin", "Запускать от администратора", "Run as administrator");
        A("Settings.Icon", "Значок", "Icon");
        A("Settings.ChangeIcon", "Сменить…", "Change…");
        A("Settings.ResetIcon", "Сброс", "Reset");
        A("Settings.Theme", "Тема", "Theme");
        A("Settings.Theme.Dark", "тёмная", "Dark");
        A("Settings.Theme.Light", "светлая", "Light");
        A("Settings.Theme.System", "как в системе", "Follow system");
        A("Settings.SystemAccent", "Брать цвет акцента из системы", "Take the accent colour from the system");
        A("Settings.Accent", "Цвет акцента", "Accent colour");
        A("Settings.AccentCustom", "Свой цвет (hex)", "Custom colour (hex)");
        A("Settings.Version", "Версия", "Version");
        A("Settings.AboutText", "Радиальное меню быстрого запуска. Свободное программное обеспечение (GPLv3).", "A radial quick-launch menu. Free software (GPLv3).");
        A("Settings.Repo", "Репозиторий проекта:", "Project repository:");
        A("Settings.License", "Лицензия: GNU GPL v3. Значок и оформление — часть проекта.", "License: GNU GPL v3. The icon and the look are part of the project.");
        A("Menu.NewItem", "Новый элемент", "New item");

        // ---- launch errors ----
        A("Launch.NotFound", "Не найдено: {0}", "Not found: {0}");
        A("Launch.Failed", "Не удалось запустить: {0}", "Could not start: {0}");
        A("Launch.NoTarget", "Для элемента не указан файл или адрес", "The item has no file or address");
        A("Launch.AccessDenied", "Нет доступа: {0}", "Access denied: {0}");

        return t;
    }
}
