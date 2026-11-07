# lumora-launcher

Лаунчер Minecraft **Lumora** на Avalonia (Windows / Linux / macOS).

- Вход через аккаунт Lumora Auth.
- Сам ставит нужную версию Minecraft, Java и лоадер (NeoForge) для сборки сервера.
- Синхронизирует моды и конфиги сборки, сохраняя файлы игрока (ресурспаки, шейдеры, `options.txt`).
- Добавляет серверы сборки в список серверов игры (`servers.dat`), по желанию — автоподключение.
- Запускает игру с authlib-injector, поэтому скины с Lumora Auth видны в игре.
- Настройки в духе Prism Launcher: Java, память, JVM-аргументы, окно, опциональные моды —
  глобально и отдельно для каждой сборки.

![Серверы](docs/screenshots/02-servers.png)

| | |
|---|---|
| ![Вход](docs/screenshots/01-login.png) | ![Установка сборки](docs/screenshots/03-installing.png) |
| ![Опциональные моды](docs/screenshots/04-build-mods.png) | ![Память](docs/screenshots/05-settings-memory.png) |
| ![Скин](docs/screenshots/07-skin.png) | ![Консоль](docs/screenshots/08-console.png) |

## Демо-режим

Пока API Lumora Auth и установка через CmlLib не подключены, лаунчер работает на демо-сервисах
(`src/Lumora.Launcher.App/Services/Demo`): подходит любой логин (пароль `wrong` — ошибка входа),
серверы, статусы, установка и запуск игры имитируются. Настройки, поиск Java, папки и логи — настоящие.

## Структура

| Проект | Назначение |
|---|---|
| `src/Lumora.Launcher.App` | UI (Avalonia, MVVM): экраны, тема, демо-сервисы |
| `src/Lumora.Launcher.Core` | Логика: интерфейсы API и игры, `servers.dat`, поиск Java, настройки |
| `tests/Lumora.Launcher.Tests` | Тесты |
| `tools/Lumora.Launcher.Screenshots` | Рендер экранов в PNG без дисплея (для README и проверки вёрстки) |

Скриншоты обновляются командой:

```bash
dotnet run --project tools/Lumora.Launcher.Screenshots -- docs/screenshots
```

Параметр запуска `--data <папка>` (или переменная `LUMORA_HOME`) задаёт папку с настройками — удобно для тестов.

## Сборка

Лаунчер использует `Lumora.Contracts` из репозитория `lumora-auth`, который должен лежать рядом:

```
Minecraft/
├─ lumora-auth/
└─ lumora-launcher/
```

```bash
dotnet build
dotnet test
dotnet run --project src/Lumora.Launcher.App
```

Другой путь к контрактам: `dotnet build -p:LumoraContractsProject=<путь>/Lumora.Contracts.csproj`.
