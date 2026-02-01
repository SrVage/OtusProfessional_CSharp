namespace OtusProfessional_CSharp;

public static class CommandParser
{
    public static ParseCommand Parse(ReadOnlySpan<char> span)
    {
        span = span.Trim();
        int index1 = span.IndexOf(' ');
        
        if (index1 == -1) 
            return new ParseCommand();
        
        ReadOnlySpan<char> command = span.Slice(0, index1);
        
        span = span.Slice(index1 + 1).TrimStart();
        int index2 = span.IndexOf(' ');

        if (index2 == -1)
        {
            ReadOnlySpan<char> key = span;
            return new ParseCommand {
                Command = command, 
                Key = key, 
                Value = ReadOnlySpan<char>.Empty
            };
        }

        if (index2 > 0)
        {
            ReadOnlySpan<char> key = span.Slice(0, index2);
            
            ReadOnlySpan<char> value = span.Slice(index2 + 1);
            
            return new ParseCommand {
                Command = command, 
                Key = key, 
                Value = value
            };
        }

        return new ParseCommand();
    }
}

public ref struct ParseCommand
{
    public ReadOnlySpan<char> Command;
    public ReadOnlySpan<char> Key;
    public ReadOnlySpan<char> Value;
}