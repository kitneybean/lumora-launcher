# lumora-launcher

Лаунчер Minecraft **Lumora** на Avalonia (Windows / Linux / macOS).

- Вход через аккаунт Lumora Auth.
- Сам ставит нужную версию Minecraft, Java и лоадер (NeoForge) для сборки сервера.
- Синхронизирует моды и конфиги сборки, сохраняя файлы игрока (ресурспаки, шейдеры, `options.txt`).
- Добавляет серверы сборки в список серверов игры (`servers.dat`), по желанию — автоподключение.
- Запускает игру с authlib-injector, поэтому скины с Lumora Auth видны в игре.
- Настройки в духе Prism Launcher: Java, память, JVM-аргументы, окно, опциональные моды —
  глобально и отдельно для каждой сборки.

## Структура

| Проект | Назначение |
|---|---|
| `src/Lumora.Launcher.App` | UI (Avalonia, MVVM) |
| `src/Lumora.Launcher.Core` | Логика: авторизация, синхронизация, `servers.dat`, запуск (CmlLib.Core), настройки |
| `tests/Lumora.Launcher.Tests` | Тесты |

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
