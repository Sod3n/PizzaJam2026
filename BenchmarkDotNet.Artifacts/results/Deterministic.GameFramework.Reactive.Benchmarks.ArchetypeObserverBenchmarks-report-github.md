```

BenchmarkDotNet v0.13.12, macOS 26.3 (25D125) [Darwin 25.3.0]
Apple M2 Pro, 1 CPU, 10 logical and 10 physical cores
.NET SDK 8.0.413
  [Host]     : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD


```
| Method                           | EntityCount | Mean        | Error     | StdDev    | Median      | Gen0   | Allocated |
|--------------------------------- |------------ |------------:|----------:|----------:|------------:|-------:|----------:|
| **ObserveArchetype_Warmup_FullScan** | **100**         |   **746.63 ns** | **10.600 ns** |  **9.397 ns** |   **742.90 ns** | **0.0381** |     **328 B** |
| Process_Add_Single_Entity        | 100         |    41.79 ns |  0.856 ns |  1.608 ns |    41.08 ns | 0.0095 |      80 B |
| **ObserveArchetype_Warmup_FullScan** | **1000**        | **5,804.43 ns** | **98.046 ns** | **91.712 ns** | **5,775.27 ns** | **0.0534** |     **480 B** |
| Process_Add_Single_Entity        | 1000        |    40.49 ns |  0.499 ns |  0.389 ns |    40.45 ns | 0.0095 |      80 B |
