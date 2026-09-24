# AGENTS.md - Справка для инженеров и агентов проекта PasteImageAsFile

## 1. Назначение и стек
- **Назначение**: Утилита Windows для моментальной вставки скопированных изображений как файлов через Ctrl+V, продвинутого буфера обмена в стиле Windows 11 и плавающей полки файлов SuperHub.
- **Стек**: C# (.NET Framework 4.0 / 4.5, C# 5 синтаксис), WinForms, Win32 API / PInvoke (DWM, Shell, User32).
- **Сборка**: Без внешних nuget пакетов, встроенным компилятором `csc.exe`.

## 2. Ключевые команды
- **Сборка**: `cmd.exe /c build.bat` (компилирует `src\*.cs` в `dist\PasteImageAsFile.exe`).
- **Запуск демона**: `dist\PasteImageAsFile.exe --daemon`
- **Открыть настройки**: `dist\PasteImageAsFile.exe`
- **Вызов буфера обмена**: `dist\PasteImageAsFile.exe --history`
- **Установка в систему**: `dist\PasteImageAsFile.exe --install`

## 3. Архитектура модулей
- `src/Program.cs`: Точка входа, IPC, трей-меню, поиск активных пользовательских окон и маршрутизация команд.
- `src/ClipboardWatcher.cs`: Отслеживание изменений буфера обмена (`AddClipboardFormatListener`), дополнение формата `FileDropList`, FileSystemWatcher для рабочего стола.
- `src/ClipboardHistoryManager.cs`: Хранилище истории буфера обмена и полки SuperHub (сохранение в `history.json`).
- `src/ClipboardFlyoutForm.cs`: Пользовательский интерфейс буфера обмена в стиле Fluent Windows 11 (вкладки Все, Снимки, Текст, Файлы, SuperHub, поиск, кастомный темный скроллбар, режимы Вставить [Ctrl+V] и Копировать).
- `src/SuperHubDockForm.cs`: Плавающая полка SuperHub (авто-выдвижение у края экрана при Drag-and-Drop, режимы Copy vs Move с Preferred DropEffect, выгрузка всех файлов сразу кнопкой `📦`).
- `src/ThemeHelper.cs`: Определение системной темы Windows (`AppsUseLightTheme`), события `ThemeChanged`, палитра цветов для Dark и Light режимов.
- `src/Config.cs`: Управление конфигурацией в реестре (`HKCU\Software\PasteImageAsFile`).
- `src/DesktopHelper.cs`: Позиционирование файлов на рабочем столе под курсором через IFolderView / IShellFolder.
- `src/FileIconHelper.cs`: Извлечение системных иконок для файлов через `SHGetFileInfo`.
- `src/MainForm.cs`: Окно управления и настроек (вкладки Основные, Буфер обмена, SuperHub).
- `src/ShellIntegration.cs`: Интеграция с Проводником и автозапуском реестра.

## 4. Важные ограничения и соглашения
- **Синтаксис C# 5**: Не использовать expression-bodied properties (`=>`), строковую интерполяцию (`$""`), операторы `?.` - компилятор .NET 4.0 их не поддерживает.
- **Безопасные пути**: Для любых путей файлов обязательно использовать `SafeGetFileName`, `SafeFileExists`, `SafeDirectoryExists` во избежание `ArgumentException` на непечатных символах заголовков окон.
- **Целостность файлов**: При Drag-and-Drop из SuperHub по умолчанию включен режим Copy (`DragDropEffects.Copy` и `Preferred DropEffect = 1`), чтобы проводник не удалял оригиналы файлов.
