```

BenchmarkDotNet v0.13.12, macOS 26.3 (25D125) [Darwin 25.3.0]
Apple M2 Pro, 1 CPU, 10 logical and 10 physical cores
.NET SDK 8.0.413
  [Host]     : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD


```
| Method           | EntityCount | Mean      | Error     | StdDev    | Gen0   | Allocated |
|----------------- |------------ |----------:|----------:|----------:|-------:|----------:|
| **Tick_NoChanges**   | **100**         |  **6.411 ns** | **0.1305 ns** | **0.1089 ns** |      **-** |         **-** |
| Tick_WithChanges | 100         | 60.692 ns | 1.2244 ns | 2.0458 ns | 0.0191 |     160 B |
| **Tick_NoChanges**   | **1000**        |  **6.254 ns** | **0.0647 ns** | **0.0505 ns** |      **-** |         **-** |
| Tick_WithChanges | 1000        | 58.634 ns | 1.2016 ns | 1.2340 ns | 0.0191 |     160 B |
