using System;
using System.Reflection;

var assembly = Assembly.LoadFile(@"C:\Code\Antigravity\Sc2Otter\src\Sc2Otter.LocalOtter\bin\Release\net10.0\win-x64\sc2otter.dll");
foreach (var name in assembly.GetManifestResourceNames())
{
    Console.WriteLine(name);
}
