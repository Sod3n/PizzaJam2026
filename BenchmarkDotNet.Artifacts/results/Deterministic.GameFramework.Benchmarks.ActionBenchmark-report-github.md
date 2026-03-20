```

BenchmarkDotNet v0.13.12, macOS 26.3 (25D125) [Darwin 25.3.0]
Apple M2 Pro, 1 CPU, 10 logical and 10 physical cores
.NET SDK 8.0.413
  [Host] : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD
  Dry    : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD

Job=Dry  IterationCount=1  LaunchCount=1  
RunStrategy=ColdStart  UnrollFactor=1  WarmupCount=1  

```
| Method         | Mean     | Error | Allocated |
|--------------- |---------:|------:|----------:|
| Execute_Action | 393.3 μs |    NA |     736 B |
| GetDenseId     | 352.9 μs |    NA |     736 B |
