# Развёртывание «Пульса»

На [публичном адресе Layero](https://pulse-club.layero.app/) работает прокси из `deploy/layero`. Он передаёт HTTPS-запросы и WebSocket в приложение Blazor Interactive Server. ASP.NET Core, SQL Server Express и файлы данных работают на отдельном сервере.

## Приложение

Сборка для Linux x64:

```powershell
dotnet publish src/FitnessCenter.Web/FitnessCenter.Web.csproj -c Release -r linux-x64 --self-contained true -o .artifacts/publish/linux-x64
```

Для запуска в режиме Production серверу нужны строка подключения `ConnectionStrings__DefaultConnection` и настройки прослушивания HTTP для внутреннего прокси. Профиль опубликованного дизайна включается через `Design__Variant=Palette`. Приложение применяет миграции при запуске, поэтому учётной записи базы нужны права на их выполнение.

Для первого запуска учебной базы явно задайте `Demo__Seed=true`, `Demo__AdminPassword` и `Demo__ManagerPassword`. После заполнения базы отключите `Demo__Seed` и удалите установочные пароли из окружения приложения. На действующем сервере приложение и SQL слушают локальные порты, а входящий HTTPS принимает отдельная служба Nginx.

## Layero

Публикуйте каталог `deploy/layero` как приложение Node.js по настройкам из `layero.json`. Для доступа к серверу проект использует три переменные окружения:

| Переменная | Значение |
| --- | --- |
| `PULSE_ORIGIN` | HTTPS-адрес сервера без пути |
| `PULSE_ORIGIN_CA` | Публичная часть сертификата в формате PEM для доверия к серверу |
| `PULSE_EDGE_KEY` | Отдельный ключ, который проверяет серверный прокси |

На сервере копия ключа хранится в `/etc/pulse/.env` с правами доступа `0600`. В Layero значение задаётся в настройках окружения проекта. При смене ключа обновите серверную проверку и переменную Layero, затем разверните прокси заново.

После публикации проверьте главную, `/services`, `/booking` и `/Account/Login`. Проверьте загрузку CSS, изображений и подключение Blazor через WebSocket.
