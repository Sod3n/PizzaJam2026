```

BenchmarkDotNet v0.13.12, macOS 26.3 (25D125) [Darwin 25.3.0]
Apple M2 Pro, 1 CPU, 10 logical and 10 physical cores
.NET SDK 8.0.413
  [Host]     : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD


```
| Method    | EntityCount | Mean       | Error     | StdDev    | Allocated |
|---------- |------------ |-----------:|----------:|----------:|----------:|
| **FullCycle** | **100**         |   **8.126 μs** | **0.1473 μs** | **0.1150 μs** |         **-** |
| **FullCycle** | **1000**        | **441.876 μs** | **8.6609 μs** | **7.2322 μs** |         **-** |
