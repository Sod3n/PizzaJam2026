```

BenchmarkDotNet v0.13.12, macOS 26.3 (25D125) [Darwin 25.3.0]
Apple M2 Pro, 1 CPU, 10 logical and 10 physical cores
.NET SDK 8.0.413
  [Host] : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD
  Dry    : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD

Job=Dry  IterationCount=1  LaunchCount=1  
RunStrategy=ColdStart  UnrollFactor=1  WarmupCount=1  

```
| Method                    | Mean       | Error | Allocated   |
|-------------------------- |-----------:|------:|------------:|
| Serialize_10k             |   284.7 μs |    NA |  3587.32 KB |
| Deserialize_10k_FullSync  | 1,894.6 μs |    NA |  1540.18 KB |
| Deserialize_10k_Rollback  |   956.4 μs |    NA |   897.38 KB |
| Serialize_100k            | 2,676.2 μs |    NA | 28675.32 KB |
| Deserialize_100k_FullSync | 2,629.1 μs |    NA | 12292.18 KB |
| Deserialize_100k_Rollback | 2,207.8 μs |    NA |  7169.38 KB |
