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
}