using BenchmarkDotNet.Running;
using Common;
using LoadTests;
using NBomber.CSharp;

Console.WriteLine("=== Load Testing & Benchmarking Tool ===\n");
Console.WriteLine("1. Run NBomber Load Tests");
Console.WriteLine("2. Run BenchmarkDotNet Serialization Benchmark");
Console.WriteLine("\nSelect option (1 or 2):");

var choice = Console.ReadLine();

if (choice == "2")
{
    Console.WriteLine("\n=== Running Serialization Benchmark ===\n");
    BenchmarkRunner.Run<SerializationBenchmark>();
}
else
{
    Console.WriteLine("\n=== Running NBomber Load Tests ===\n");
    
    var random = new Random();

    var scenario = Scenario.Create("TCP Store Load Test", async context =>
        {
            string randomKey = $"key_{random.Next(1000, 9999)}";
            using (var client = new LoadTests.TcpClient())
            {
                await Step.Run("Connect operation", context, async () =>
                {
                    try
                    {
                        await client.ConnectAsync();
                        return Response.Ok();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        return Response.Fail();
                    }
                });
                await Step.Run("SET operation", context, async () =>
                {
                    try
                    {
                        string randomValue = $"value_{Guid.NewGuid().ToString().Substring(0, 8)}";
                        var profile = new UserProfile()
                        {
                            Id = random.Next(0, int.MaxValue),
                            Username = randomValue,
                            CreatedAt = DateTime.UtcNow
                        };

                        await client.SetAsync(randomKey, profile);

                        return Response.Ok();
                    }
                    catch (Exception e)
                    {
                        return Response.Fail();
                    }
                });
                await Step.Run("GET operation", context, async () =>
                {
                    try
                    {
                        await client.GetAsync(randomKey);

                        return Response.Ok();
                    }
                    catch
                    {
                        return Response.Fail();
                    }
                });
            }

            return Response.Ok();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(10))
        .WithLoadSimulations(
            Simulation.Inject(100, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30))
        );

    NBomberRunner
        .RegisterScenarios(scenario)
        .Run();

    Console.WriteLine("\n=== Test completed ===");
}

