# Высокопроизводительный In-Memory кэш для игровых realtime-серверов

Низколатентный TCP-сервис: in-memory key-value хранилище с собственным текстовым протоколом, потокобезопасным ядром и кодогенерируемой бинарной сериализацией. Спроектирован как side-car к игровым realtime-серверам, которым нужна латентность ниже похода в БД.

Прикладные сценарии:

- горячий слой перед БД профилей — матчмейкер берёт `GET player_42` напрямую отсюда;
- состояние активных сессий — HP, позиция, инвентарь, текущее оружие;
- live-ops лидерборды и быстрые счётчики;
- общий KV для координации микросервисов одной партии.

---

## Архитектура

```
┌──────────┐  TCP  ┌─────────────────────────────────────────────────┐
│  Клиент  │ ────► │  TcpServer                                      │
└──────────┘       │  ├─ AcceptLoop  (SemaphoreSlim, MAX_USERS=4)    │
                   │  └─ ProcessClientAsync                          │
                   │     ├─ ArrayPool<byte>.Shared — буфер на сессию │
                   │     ├─ Фрейминг по \r\n (накопительный буфер)   │
                   │     └─ HandleCommandAsync                       │
                   │        ├─ CommandParser (ReadOnlySpan<char>)    │
                   │        ├─ OpenTelemetry: Activity + метрики     │
                   │        └─ Dispatch: SET / GET / DELETE          │
                   │                       │                         │
                   │                       ▼                         │
                   │                  SimpleStore                    │
                   │                  ├─ Dictionary<string, byte[]>  │
                   │                  ├─ ReaderWriterLockSlim        │
                   │                  ├─ Interlocked-счётчики        │
                   │                  └─ UserProfile.SerializeTo…    │
                   │                     (Source Generator)          │
                   └─────────────────────────────────────────────────┘
```

Поток одного запроса:

1. `AcceptConnectionsAsync` берёт билет в `SemaphoreSlim`, принимает сокет, форкается в `ProcessClientAsync`.
2. `ProcessClientAsync` арендует один буфер из `ArrayPool<byte>.Shared` на всё время жизни клиента и крутит цикл `ReceiveAsync` → поиск `\r\n` → `HandleCommandAsync`. Неполный фрейм (без `\r\n` в конце) переезжает в начало буфера до следующего пакета.
3. `HandleCommandAsync` оборачивает обработку в `Activity`, парсит команду через `CommandParser` и диспатчит в `ProcessGetCommandAsync` / `ProcessSetCommandAsync` / `ProcessDeleteCommandAsync`.
4. Ядро `SimpleStore` под `ReaderWriterLockSlim` отдаёт/пишет байты; на запись — `UserProfile.SerializeToBinary()`, на чтение — обратный `DeserializeFromBinary`.
5. Метрики `tcp.commands.processed` и `tcp.command.duration` инкрементятся в `finally`.

### Слои

| Слой | Файлы | Ответственность |
| --- | --- | --- |
| Сеть | `TCPServer/TcpServer.cs`, `TCPServer/Program.cs` | Приём TCP-подключений, накопительный буфер, фрейминг по `\r\n`, backpressure. |
| Парсер | `OtusProfessional_CSharp/CommandParser.cs` | Разбор команды на `Command`/`Key`/`Value` без копирования внутри строки. |
| Конвейер | `TcpServer.HandleCommandAsync` | Диспетчеризация команд, замер длительности, телеметрия, формирование ответа. |
| Хранилище | `OtusProfessional_CSharp/SimpleStore.cs` | `Dictionary<string, byte[]>` под `ReaderWriterLockSlim`, счётчики на `Interlocked`. |
| Сериализатор | `Generator/BinarySerializeGenerator.cs`, `Common/UserProfile.cs`, `Common/Weapon.cs` | Кодогенерируемые `SerializeToBinary` / `DeserializeFromBinary`. |
| Телеметрия | `TCPServer/Telemetry.cs` | `ActivitySource` + `Meter`, OpenTelemetry-экспортёры. |

---

## Протокол

Регистрозависимый текстовый протокол. **Каждая команда оканчивается `\r\n`.** Поля разделены пробелами.

| Команда | Формат | Ответ |
| --- | --- | --- |
| `SET` | `SET <key> <json>\r\n` | `OK\r\n` |
| `GET` | `GET <key>\r\n` | `<json>` или `Key not found \r\n` |
| `DELETE` | `DELETE <key>\r\n` | `OK\r\n` |

Ошибки:

| Ответ | Когда |
| --- | --- |
| `-ERR Unknown command\r\n` | Неизвестная команда или пустой payload `SET` |
| `-ERR Invalid JSON\r\n` | `SET` с невалидным JSON |
| `-ERR Corrupted data\r\n` | Битые байты в хранилище (расхождение версии формата) |

### Примеры

```
SET player_42 {"Id":42,"Username":"kramp","CreatedAt":"2026-05-22T10:00:00Z","Weapon":{"Name":"Excalibur","Damage":50}}\r\n
GET player_42\r\n
DELETE player_42\r\n
```

### Парсер

```csharp
public static ParseCommand Parse(ReadOnlySpan<char> span) { ... }

public ref struct ParseCommand
{
    public ReadOnlySpan<char> Command;
    public ReadOnlySpan<char> Key;
    public ReadOnlySpan<char> Value;
}
```

`ref struct` + `ReadOnlySpan<char>` — slicing внутри одной строки без копирования и без heap-аллокаций.

---

## Хранилище

`SimpleStore` хранит значения уже в бинарном виде:

```csharp
public UserProfile? Get(string key)
{
    _lock.EnterReadLock();
    try
    {
        Interlocked.Increment(ref _getCount);
        if (!_store.TryGetValue(key, out var bytes)) return null;
        return UserProfile.DeserializeFromBinary(bytes);
    }
    finally { _lock.ExitReadLock(); }
}
```

- `ReaderWriterLockSlim` — много читателей, один писатель.
- `Interlocked.Increment` для счётчиков Set/Get/Delete — атомарные O(1) без `lock`. Доступны через `GetStatistics()`.
- Значения — массивы байт, сгенерированные `UserProfile.SerializeToBinary()`.

---

## Сериализация

`Generator/BinarySerializeGenerator.cs` — Roslyn Source Generator. Для классов, помеченных `[GenerateBinarySerializer]`, во время компиляции добавляются методы:

```csharp
public byte[] SerializeToBinary();
public static UserProfile DeserializeFromBinary(byte[] data);
```

Поддерживаются `int`, `string`, `DateTime`, `decimal` и вложенные объекты с тем же атрибутом (`UserProfile` → `Weapon`). Сгенерированный код выгружается на диск (`Common/Generated/...`) благодаря `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` — удобно отлаживать.

### JSON vs Binary

Микробенчмарк `LoadTests/SerializationBenchmark.cs` сравнивает `System.Text.Json` и сгенерированный бинарь на одном `UserProfile { Id, Username, CreatedAt, Weapon { Name, Damage } }` (Apple M1, .NET 10.0.6):

| Method              |      Mean | Ratio | Allocated | Alloc Ratio |
|---------------------|----------:|------:|----------:|------------:|
| JsonSerialization   | 110.67 ns |  1.00 |     112 B |        1.00 |
| **BinarySerialization** | **15.00 ns** | **0.14** |  **64 B** | **0.57** |

Кодогенерация даёт ~**7× по скорости** и ~**2× по аллокациям**: нет рефлексии (раскладка зашита), нет имён полей в потоке, ни одной строковой аллокации на примитивах и `DateTime`.

Полный отчёт — `LoadTests/BenchmarkDotNet.Artifacts/results/LoadTests.SerializationBenchmark-report-github.md`.

---

## Телеметрия

`TCPServer/Telemetry.cs`:

```csharp
public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);
public static readonly Counter<long>     CommandsProcessed = Meter.CreateCounter<long>("tcp.commands.processed");
public static readonly Histogram<double> CommandDuration   = Meter.CreateHistogram<double>("tcp.command.duration", "ms");
```

Каждая команда оборачивается в `Activity` с тегами `command.name`, `command.key`, `command.payload_bytes`, `net.peer.address`, `command.status`, `command.duration_ms`. Counter считает обработанные команды, Histogram — их длительность.

Экспортёр в `TCPServer/Program.cs` — `AddConsoleExporter()`. Замена на OTLP — одна строка, без изменений в сервере.

---

## Запуск

Требуется **.NET 10 SDK**.

| Что | Команда |
| --- | --- |
| Сервер | `dotnet run --project TCPServer -c Release` (слушает `127.0.0.1:8080`) |
| Демо-клиент | `dotnet run --project TcpClient` |
| Тесты | `dotnet test OtusProfessional_CSharp.Test` |
| NBomber | `dotnet run --project LoadTests -c Release` → `1` |
| BenchmarkDotNet | `dotnet run --project LoadTests -c Release` → `2` |

### Нагрузочный тест

`LoadTests/Program.cs` — сценарий `TCP Store Load Test`:

- 100 виртуальных пользователей в секунду в течение 30 секунд, прогрев — 10 секунд;
- каждый юзер: `Connect → SET (random UserProfile) → GET (тот же ключ)`;
- сервер должен быть запущен заранее.

Артефакты NBomber пишутся в `LoadTests/bin/Release/.../reports/`.

---

## Структура решения

```
Common/                          # UserProfile, Weapon, GenerateBinarySerializerAttribute
Generator/                       # Roslyn Source Generator: бинарный сериализатор
OtusProfessional_CSharp/         # SimpleStore + CommandParser (ядро + парсер)
OtusProfessional_CSharp.Test/    # xUnit: парсер, хранилище, сериализатор
TCPServer/                       # TcpServer + Telemetry + Program (entry point)
TcpClient/                       # Тонкий демо-клиент
LoadTests/                       # NBomber-сценарий + BenchmarkDotNet
```

---

## Применённые техники

| Где | Что | Зачем |
| --- | --- | --- |
| `SimpleStore` | `ReaderWriterLockSlim` | Множественные параллельные `GET` без взаимной блокировки; писатели — эксклюзив. |
| `SimpleStore.GetStatistics` | `Interlocked.Increment` | Атомарные O(1) счётчики без `lock` под высоким contention. |
| `TcpServer.ProcessClientAsync` | `ArrayPool<byte>.Shared` | Один буфер на жизнь TCP-сессии — нет per-receive аллокаций. |
| `CommandParser` | `ReadOnlySpan<char>` + `ref struct` | Парсинг команды без копирования. |
| `BinarySerializeGenerator` | Source Generator + `[GenerateBinarySerializer]` | Сериализация без рефлексии; код виден на диске. |
| `Telemetry` | `ActivitySource` + `Meter` (OpenTelemetry) | Трассы и метрики каждой команды; экспортёр сменяемый. |
| `TcpServer.AcceptConnectionsAsync` | `SemaphoreSlim(MAX_USERS_COUNT)` | Backpressure — ограничение одновременных клиентов. |
| `TcpServer.ProcessClientAsync` | Накопительный буфер + `\r\n` | Корректный фрейминг поверх TCP-потока. |

---
