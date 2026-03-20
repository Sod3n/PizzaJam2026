```

BenchmarkDotNet v0.13.12, macOS 26.3 (25D125) [Darwin 25.3.0]
Apple M2 Pro, 1 CPU, 10 logical and 10 physical cores
.NET SDK 8.0.413
  [Host]     : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD


```
| Method                    | EntityCount | Mean         | Error      | StdDev     | Gen0   | Allocated |
|-------------------------- |------------ |-------------:|-----------:|-----------:|-------:|----------:|
| **Filter_Standard_Iteration** | **1000**        |  **2,966.69 ns** |  **11.931 ns** |   **9.315 ns** | **0.0038** |      **48 B** |
| Reactive_Tick_With_Filter | 1000        |     33.47 ns |   0.681 ns |   1.841 ns | 0.0048 |      40 B |
| **Filter_Standard_Iteration** | **10000**       | **40,797.68 ns** | **551.468 ns** | **488.862 ns** |      **-** |      **48 B** |
| Reactive_Tick_With_Filter | 10000       |     34.82 ns |   0.716 ns |   0.853 ns | 0.0048 |      40 B |
