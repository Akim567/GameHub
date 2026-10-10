# GameHub

Учебный REST API на ASP.NET Core 10, использующий Redis как единственное хранилище. Redis работает в отказоустойчивой конфигурации: один master, две replica и три Sentinel.

## Запуск

Требования: Docker Desktop с Docker Compose.

```bash
docker compose up -d --build
docker compose ps
```

API доступен по адресу `http://localhost:8080`. OpenAPI-схема: `http://localhost:8080/openapi/v1.json`.

Остановка без удаления данных:

```bash
docker compose down
```

## Архитектура

- `gamehub` — ASP.NET Core REST API, подключающийся через Sentinel;
- `redis-master` — исходный writable-узел на порту `6379`;
- `redis-replica-1`, `redis-replica-2` — read-only реплики на портах `6380`, `6381`;
- `sentinel-1..3` — Sentinel на портах `26379..26381`, имя master — `mymaster`, quorum — `2`;
- `NotificationConsumer` — фоновый consumer группы `notifications-group`.

Приложение использует `StackExchange.Redis`. Sentinel сообщает клиенту адрес текущего master и позволяет клиенту переключиться после failover.

## Структуры Redis

| Назначение | Ключ | Тип | TTL |
|---|---|---|---|
| Профиль | `player:{id}` | Hash | без TTL |
| Кеш профиля | `cache:player:{id}` | String/JSON | 60 секунд |
| Счётчик входов | `logins:{id}` | String | 24 часа |
| Лидерборд | `tournament:main` | Sorted Set | без TTL |
| Достижения | `achievements:{id}` | Set | без TTL |
| Уведомления | `notifications` | Stream | 7 дней |

## API

| Метод | Путь | Назначение |
|---|---|---|
| `POST` | `/api/players/{id}` | Создание или обновление профиля |
| `GET` | `/api/players/{id}` | Получение профиля через cache-aside |
| `PATCH` | `/api/players/{id}/level` | Изменение уровня и отправка уведомления |
| `POST` | `/api/players/{id}/login` | Атомарное увеличение счётчика входов через Lua |
| `POST` | `/api/leaderboard/score` | Добавление очков |
| `GET` | `/api/leaderboard/top?limit=10` | Верхняя часть рейтинга |
| `GET` | `/api/leaderboard/rank/{playerId}` | Место игрока |
| `POST` | `/api/players/{id}/achievements` | Добавление достижения |
| `GET` | `/api/players/{id}/achievements/{name}` | Проверка достижения |
| `GET` | `/api/players/{id1}/achievements/common/{id2}` | Общие достижения |
| `POST` | `/api/players/batch` | Создание профилей через pipeline |

Готовые запросы находятся в `GameHub/GameHub.http`.

## Проверка Redis

Репликация:

```bash
docker compose exec redis-master redis-cli INFO replication
```

Текущий master по мнению Sentinel:

```bash
docker compose exec sentinel-1 redis-cli -p 26379 SENTINEL get-master-addr-by-name mymaster
```

Кеш профиля:

```bash
docker compose exec redis-master redis-cli TTL cache:player:player-1
```

Счётчик входов:

```bash
docker compose exec redis-master redis-cli GET logins:player-1
docker compose exec redis-master redis-cli TTL logins:player-1
```

Stream и consumer group:

```bash
docker compose exec redis-master redis-cli XLEN notifications
docker compose exec redis-master redis-cli XINFO GROUPS notifications
```

## Проверка failover

```bash
docker compose stop redis-master
sleep 15
docker compose exec sentinel-1 redis-cli -p 26379 SENTINEL get-master-addr-by-name mymaster
docker compose start redis-master
```

После ожидания Sentinel должен вернуть `redis-replica-1` или `redis-replica-2`.
