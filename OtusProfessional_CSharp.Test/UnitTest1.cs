using System.Text;

namespace OtusProfessional_CSharp.Test;

public class UnitTest1
{
    [Fact]
    public void Parse_CommandWithThreeArguments_ReturnsCorrectParts()
    {
        string data = "SET user:1 data";
        ReadOnlySpan<char> span = data.AsSpan();
        var parseData = CommandParser.Parse(span);
        Assert.Equal("SET", parseData.Command.ToString());
        Assert.Equal("user:1", parseData.Key.ToString());
        Assert.Equal("data", parseData.Value.ToString());
    }
    
    [Fact]
    public void Parse_CommandWithTwoArguments_ReturnsCorrectParts()
    {
        string data = "SET user:1";
        ReadOnlySpan<char> span = data.AsSpan();
        var parseData = CommandParser.Parse(span);
        Assert.Equal("SET", parseData.Command.ToString());
        Assert.Equal("user:1", parseData.Key.ToString());
    }
    
    [Fact]
    public void Parse_CommandWithoutKey_ReturnsEmptyParts()
    {
        string data = "SET";
        ReadOnlySpan<char> span = data.AsSpan();
        var parseData = CommandParser.Parse(span);
        Assert.Equal("", parseData.Command.ToString());
        Assert.Equal("", parseData.Key.ToString());
        Assert.Equal("", parseData.Value.ToString());
    }
    
    [Fact]
    public void Parse_CommandWithManySpaces_ReturnsCorrectParts()
    {
        string data = "  SET   user:1   data";
        ReadOnlySpan<char> span = data.AsSpan();
        var parseData = CommandParser.Parse(span);
        Assert.Equal("SET", parseData.Command.ToString());
        Assert.Equal("user:1", parseData.Key.ToString());
        Assert.Equal("  data", parseData.Value.ToString());
    }
    
    [Fact]
    public async Task ConcurrentSetAndGet_ShouldMaintainDataIntegrityAndAccurateStatistics()
    {
        using var store = new SimpleStore();
        
        int numSetTasks = 50;
        int numGetTasks = 50;
        int operationsPerTask = 1000;

        var tasks = new List<Task>();
        
        for (int i = 0; i < numSetTasks; i++)
        {
            int taskId = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < operationsPerTask; j++)
                {
                    string key = $"key_{taskId}_{j}";
                    byte[] value = Encoding.UTF8.GetBytes($"value_{taskId}_{j}");
                    store.Set(key, value);
                }
            }));
        }

        for (int i = 0; i < numGetTasks; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < operationsPerTask; j++)
                {
                    string key = $"key_{j % numSetTasks}_{j}";
                    var _ = store.Get(key); 
                }
            }));
        }

        await Task.WhenAll(tasks);
        
        var (setCount, getCount, deleteCount) = store.GetStatistics();
        
        long expectedSetCount = numSetTasks * operationsPerTask;
        long expectedGetCount = numGetTasks * operationsPerTask;

        Assert.Equal(expectedSetCount, setCount);
        Assert.Equal(expectedGetCount, getCount);
        Assert.Equal(0, deleteCount);
        
        for (int i = 0; i < numSetTasks; i++)
        {
            for (int j = 0; j < operationsPerTask; j++)
            {
                string key = $"key_{i}_{j}";
                string expectedValueString = $"value_{i}_{j}";
                
                var resultBytes = store.Get(key);
                
                Assert.NotNull(resultBytes);
                
                string actualValueString = Encoding.UTF8.GetString(resultBytes);
                Assert.Equal(expectedValueString, actualValueString);
            }
        }

        var finalStats = store.GetStatistics();
        Assert.Equal(expectedGetCount + expectedSetCount, finalStats.Item2); 
    }
}