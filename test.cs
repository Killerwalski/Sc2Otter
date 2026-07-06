using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ""Sc2Otter"", ""user_settings.json"");
        var json = File.ReadAllText(path);
        
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var jsonDoc = JsonDocument.Parse(json);
        Console.WriteLine($""Raw MySc2Name from JSON: {jsonDoc.RootElement.GetProperty("MySc2Name").GetString()}"");

        var myName = jsonDoc.RootElement.GetProperty("MySc2Name").GetString();
        var myNames = (myName ?? """").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        Console.WriteLine($""Count: {myNames.Count}"");
        foreach(var n in myNames) Console.WriteLine($""Name: '{n}'"");
        
        var pName = ""APECRAFT"";
        Console.WriteLine($""Contains APECRAFT?: {myNames.Contains(pName)}"");
    }
}
