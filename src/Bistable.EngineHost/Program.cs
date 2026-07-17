using Bistable.EngineHost;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;
EngineRpcServer server = new(new Bistable.Engine.DesignElaborationService());
await server.RunAsync(Console.In, Console.Out, CancellationToken.None);
