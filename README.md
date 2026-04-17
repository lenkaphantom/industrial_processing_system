# Industrial Processing System

A thread-safe, async, priority-based job processing system implemented in C# (.NET Framework 4.7.2), built as a college assignment for the Software Supervisory Control Systems course at FTN SIIT.

## Overview

The system simulates an industrial job processing pipeline following the **producer-consumer pattern** — asynchronous, priority-driven, and event-based. Multiple producer threads submit jobs into a bounded priority queue; a fixed pool of worker tasks continuously dequeues and processes them in priority order.

## Architecture

```
Producer Threads (n, from config)
        │
        │  Submit(Job)
        ▼
┌─────────────────────────────────────┐
│           ProcessingSystem          │
│                                     │
│  ┌─────────────────────────────┐    │
│  │  Thread-Safe Priority Queue │    │
│  │  (bounded by MaxQueueSize)  │    │
│  └────────────┬────────────────┘    │
│               │                     │
│    Idempotency Check (by Job ID)    │
│               │                     │
│        Worker Tasks (n)             │
│         ┌────┴─────┐                │
│      Prime Job    IO Job            │
│    (parallel)  (simulated delay)    │
└─────────────────────────────────────┘
        │
        ▼
  TaskCompletionSource (JobHandle)
        │
   ┌────┴────┐
   │         │
JobCompleted  JobFailed
   │         │
   └────┬────┘
        ▼
   Async log file
```

## Features

- **Thread-safe priority queue** — jobs with lower `Priority` values are processed first (1 = highest priority)
- **Bounded queue** — new jobs are rejected when `MaxQueueSize` is reached
- **Idempotency** — a job with the same `Guid` ID is never executed more than once
- **Two job types:**
  - `Prime` — counts prime numbers up to a given limit, computed in parallel (1–8 threads)
  - `IO` — simulates an I/O read via `Thread.Sleep`, returns a random number between 0 and 100
- **Retry logic** — a job that exceeds 2 seconds is considered failed; it is retried up to 2 additional times before being aborted
- **Event-driven logging** — `JobCompleted` and `JobFailed` events asynchronously write to a log file in the format `[DateTime] [Status] JobId, Result`
- **Periodic reports** — every minute a LINQ-generated XML report is written; the last 10 reports are kept in a rotating ring buffer (`report_0.xml` … `report_9.xml`)
- **Time-independent testing** — no `Thread.Sleep` for waiting on results; uses `TaskCompletionSource`, `SemaphoreSlim`, and `Task.WhenAny`

## Project Structure

```
IndustrialProcessingSystem/
├── Models/
│   ├── Job.cs
│   ├── JobHandle.cs
│   ├── JobType.cs (enum)
│   └── JobResult.cs             # internal stats record
├── Processing/
│   ├── ProcessingSystem.cs      # core: queue, workers, submit, retry
│   ├── JobExecutor.cs           # Prime and IO execution logic
│   ├── JobEventArgs.cs
│   ├── ReportGenerator.cs       # LINQ + XML report writing
│   └── SimplePriorityQueue.cs 
├── Config/
│   └── SystemConfig.cs          # XML deserialization of SystemConfig.xml
├── Logging/
│   └── JobLogger.cs             # async, thread-safe file logger
├── Tests/
│   └── ProcessingSystemTests.cs
├── SystemConfig.xml
└── Program.cs
```

## Configuration

The system is initialised from `SystemConfig.xml`:

```xml
<SystemConfig>
  <WorkerCount>5</WorkerCount>
  <MaxQueueSize>100</MaxQueueSize>
  <Jobs>
    <Job Type="Prime" Payload="numbers:10_000,threads:3" Priority="1"/>
    <Job Type="IO"    Payload="delay:1_000"              Priority="3"/>
  </Jobs>
</SystemConfig>
```

| Field | Description |
|---|---|
| `WorkerCount` | Number of worker tasks and producer threads spawned at startup |
| `MaxQueueSize` | Maximum number of jobs allowed in the queue at any time |
| `Jobs` | Initial jobs loaded into the queue before producers start |

## Payload Format

| Job Type | Payload Format | Example |
|---|---|---|
| `Prime` | `numbers:<limit>,threads:<n>` | `numbers:10_000,threads:3` |
| `IO` | `delay:<ms>` | `delay:1_000` |
