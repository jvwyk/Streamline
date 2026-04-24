using System.Reflection;

var version = Assembly.GetExecutingAssembly().GetName().Version;
Console.WriteLine($"Streamline v{version}");
