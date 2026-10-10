# Redis connection

## Objective

Run Redis in a highly available topology and connect GameHub through Redis Sentinel instead of a fixed Redis node.

## Topology

- one writable Redis master: `redis-master:6379`;
- two read-only replicas: `redis-replica-1:6379`, `redis-replica-2:6379`;
- three Sentinels on port `26379` with quorum `2`;
- Sentinel service name: `mymaster`;
- the application and all Redis services share the `gamehub-network` Docker network.

Host ports are `6379` for the initial master, `6380` and `6381` for replicas, and `26379` through `26381` for Sentinels.

## Application contract

Configuration is read from the `Redis` section:

```json
{
  "Redis": {
    "ServiceName": "mymaster",
    "Sentinels": [
      "sentinel-1:26379",
      "sentinel-2:26379",
      "sentinel-3:26379"
    ]
  }
}
```

`StackExchange.Redis` uses `ServiceName` to discover the current writable node through Sentinel and to follow a later master switch. Application startup fails if it cannot establish and verify this connection after the configured retry attempts.

## Acceptance checks

1. The solution builds without errors.
2. Both replicas report the `slave`/`replica` role and the master reports two connected replicas.
3. Each Sentinel resolves `mymaster` to the current master.
4. GameHub logs a successful Redis ping during startup.
