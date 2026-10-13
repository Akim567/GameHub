# Сценарий 

## 1. Запуск системы

```bash
docker compose down
```

```bash
docker compose up -d --build
```

```bash
docker compose ps
```

Ожидаем: запущены 7 контейнеров, Redis и Sentinel имеют статус `healthy`.

```bash
docker compose exec redis-master redis-cli INFO replication
```

Ожидаем: `role:master` и `connected_slaves:2`.

```bash
docker compose exec sentinel-1 redis-cli -p 26379 SENTINEL get-master-addr-by-name mymaster
```

Ожидаем: `redis-master`, порт `6379`.

## 2. Профили и кеширование

Создаём профиль игрока `1001`:

```bash
curl -s -X POST http://localhost:8080/api/players/1001 \
  -H "Content-Type: application/json" \
  -d '{"name":"Alex","level":10,"region":"EU"}'
```

Создаём профиль игрока `1002`:

```bash
curl -s -X POST http://localhost:8080/api/players/1002 \
  -H "Content-Type: application/json" \
  -d '{"name":"Sam","level":7,"region":"NA"}'
```

Ожидаем: два профиля созданы в Redis Hash.

Первое чтение профиля `1001`:

```bash
curl -i http://localhost:8080/api/players/1001
```

Повторное чтение профиля `1001`:

```bash
curl -i http://localhost:8080/api/players/1001
```

Ожидаем: первый ответ содержит `X-Cache: MISS`, второй — `X-Cache: HIT`.

```bash
docker compose exec redis-master redis-cli TTL cache:player:1001
```

Ожидаем: TTL от 1 до 60 секунд.

## 3. Счётчик входов через Lua

```bash
curl -s -X POST http://localhost:8080/api/players/1001/login
```

```bash
curl -s -X POST http://localhost:8080/api/players/1001/login
```

```bash
docker compose exec redis-master redis-cli GET logins:1001
```

```bash
docker compose exec redis-master redis-cli TTL logins:1001
```

Ожидаем: счётчик увеличился два раза, TTL около `86400` секунд.

## 4. Лидерборд

Добавляем очки игроку `1001`:

```bash
curl -s -X POST http://localhost:8080/api/leaderboard/score \
  -H "Content-Type: application/json" \
  -d '{"playerId":"1001","score":125.5}'
```

Добавляем очки игроку `1002`:

```bash
curl -s -X POST http://localhost:8080/api/leaderboard/score \
  -H "Content-Type: application/json" \
  -d '{"playerId":"1002","score":80}'
```

Получаем таблицу лидеров:

```bash
curl -s "http://localhost:8080/api/leaderboard/top?limit=10"
```

Получаем место игрока `1001`:

```bash
curl -s http://localhost:8080/api/leaderboard/rank/1001
```

Ожидаем: игрок `1001` выше `1002`, место начинается с 1.

## 5. Достижения

Добавляем достижение игроку `1001`:

```bash
curl -s -X POST http://localhost:8080/api/players/1001/achievements \
  -H "Content-Type: application/json" \
  -d '{"achievementName":"video-first-win"}'
```

Добавляем такое же достижение игроку `1002`:

```bash
curl -s -X POST http://localhost:8080/api/players/1002/achievements \
  -H "Content-Type: application/json" \
  -d '{"achievementName":"video-first-win"}'
```

Проверяем достижение игрока `1001`:

```bash
curl -s http://localhost:8080/api/players/1001/achievements/video-first-win
```

Получаем общие достижения игроков `1001` и `1002`:

```bash
curl -s http://localhost:8080/api/players/1001/achievements/common/1002
```

Ожидаем: достижение найдено, пересечение содержит `video-first-win`.

## 6. Stream и consumer group

```bash
curl -s -X PATCH http://localhost:8080/api/players/1001/level \
  -H "Content-Type: application/json" \
  -d '{"delta":3}'
```

```bash
docker compose exec redis-master redis-cli XLEN notifications
```

```bash
docker compose exec redis-master redis-cli XINFO GROUPS notifications
```

```bash
docker compose exec redis-master redis-cli XPENDING notifications notifications-group
```

```bash
docker compose logs --tail 30 gamehub
```

Ожидаем: Stream не пуст, группа `notifications-group` существует, `pending` равен 0, в логах есть `Notification ... processed`.

## 7. Pipeline

```bash
docker compose exec redis-master redis-cli SCAN 0 MATCH "player:batch-*" COUNT 100
```

Ожидаем: API вернул `created: 10`, Redis содержит 10 профилей `batch-*`.

## 8. Защита от split-brain

```bash
docker compose pause redis-replica-1 redis-replica-2
```

```bash
sleep 12
```

```bash
docker compose exec redis-master redis-cli SET split-brain:test blocked
```

Ожидаем: ошибка `NOREPLICAS` — master без актуальной реплики отклоняет запись.

```bash
docker compose unpause redis-replica-1 redis-replica-2
```

```bash
sleep 3
```

```bash
docker compose exec redis-master redis-cli SET split-brain:test restored
```

Ожидаем: `OK`.

## 9. Самопроверка

Сначала заново заполните кеш, затем сразу запустите проверку:

```bash
curl -s http://localhost:8080/api/players/1001
```

```bash
python3 "/Users/macbook/Downloads/dz1 check.py"
```

Ожидаем: `Пройдено: 13`, `Не пройдено: 0`, `Пропущено: 0`.

## 10. Sentinel failover

Этот пункт выполняется последним, после самопроверки.

```bash
docker compose stop redis-master
```

```bash
sleep 15
```

```bash
docker compose exec sentinel-1 redis-cli -p 26379 SENTINEL get-master-addr-by-name mymaster
```

Ожидаем: новым master стал `redis-replica-1` или `redis-replica-2`.

Проверяем, что приложение продолжает записывать и читать:

```bash
curl -s -X POST http://localhost:8080/api/players/after-failover \
  -H "Content-Type: application/json" \
  -d '{"name":"Survivor","level":1,"region":"EU"}'
```

```bash
curl -s http://localhost:8080/api/players/after-failover
```

Ожидаем: профиль успешно создан и прочитан через новый master.

```bash
docker compose start redis-master
```

```bash
sleep 10
```

```bash
docker compose exec redis-master redis-cli ROLE
```
