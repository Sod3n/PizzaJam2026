```

BenchmarkDotNet v0.13.12, macOS 26.3 (25D125) [Darwin 25.3.0]
Apple M2 Pro, 1 CPU, 10 logical and 10 physical cores
.NET SDK 8.0.413
  [Host] : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD
  Dry    : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD

Job=Dry  IterationCount=1  LaunchCount=1  
RunStrategy=ColdStart  UnrollFactor=1  WarmupCount=1  

```
| Method                   | Count | Mean     | Error | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------- |------ |---------:|------:|------:|--------:|----------:|------------:|
| **Dispatcher_Many_Entities** | **100**   |       **NA** |    **NA** |     **?** |       **?** |        **NA** |           **?** |
| ECS_System_Many_Entities | 100   | 822.4 μs |    NA |     ? |       ? |     736 B |           ? |
|                          |       |          |       |       |         |           |             |
| **Dispatcher_Many_Entities** | **1000**  |       **NA** |    **NA** |     **?** |       **?** |        **NA** |           **?** |
| ECS_System_Many_Entities | 1000  | 918.2 μs |    NA |     ? |       ? |     736 B |           ? |

Benchmarks with issues:
  ActionEcsBenchmark.Dispatcher_Many_Entities: Dry(IterationCount=1, LaunchCount=1, RunStrategy=ColdStart, UnrollFactor=1, WarmupCount=1) [Count=100]
  ActionEcsBenchmark.Dispatcher_Many_Entities: Dry(IterationCount=1, LaunchCount=1, RunStrategy=ColdStart, UnrollFactor=1, WarmupCount=1) [Count=1000]
