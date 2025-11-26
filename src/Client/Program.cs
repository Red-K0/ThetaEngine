using Theta.Discord.Backend;
using Theta.Testing;

Console.CursorVisible = false;

AppDomain app = AppDomain.CurrentDomain;

app.UnhandledException += static async (_, e) =>
{
	Console.Clear();
	Core.Log($"An unhandled exception occurred, with message: {((Exception)e.ExceptionObject).Message}", NetCord.Logging.LogLevel.Critical);
	if (e.IsTerminating) await Core.Restart();
};


SerializationTest.RunTest();

await Task.Delay(Timeout.Infinite);

