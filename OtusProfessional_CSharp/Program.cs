// See https://aka.ms/new-console-template for more information

using OtusProfessional_CSharp;

string data = "SET  user:1";
ReadOnlySpan<char> span = data.AsSpan();
var parseData = CommandParser.Parse(span);
Console.WriteLine(parseData.Command.ToString() + "\n" + parseData.Key.ToString() + "\n" + parseData.Value.ToString());