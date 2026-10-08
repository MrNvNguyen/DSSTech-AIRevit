using Dss.RevitMcp;

Console.Error.WriteLine("Dss.RevitMcp stdio sidecar starting.");
await McpStdioServer.RunAsync(Console.In, Console.Out, Console.Error);
