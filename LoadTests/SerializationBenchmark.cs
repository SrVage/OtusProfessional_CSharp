using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Common;

namespace LoadTests;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3)] 
public class SerializationBenchmark
{
    private UserProfile _testData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _testData = new UserProfile
        {
            Id = 12345,
            Username = "test_user_benchmark",
            CreatedAt = DateTime.UtcNow,
            Weapon = new Weapon()
            {
                Damage = 100,
                Name = "Excalibur"
            }
        };

        var jsonSize = JsonSerializer.SerializeToUtf8Bytes(_testData).Length;
        var binarySize = _testData.SerializeToBinary().Length;
        
        Console.WriteLine("-------------------------------");
        Console.WriteLine($"Размер JSON: {jsonSize} байт");
        Console.WriteLine($"Размер Binary: {binarySize} байт\n");
    }
    
    [Benchmark(Baseline = true)]
    public byte[] JsonSerialization()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testData);
    }
    
    [Benchmark]
    public byte[] BinarySerialization()
    {
        return _testData.SerializeToBinary();
    }
}