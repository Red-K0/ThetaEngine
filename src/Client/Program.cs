using Theta.Discord;
using Theta.Testing;

Console.CursorVisible = false;

AppDomain app = AppDomain.CurrentDomain;

app.UnhandledException += static async (_, e) =>
{
	Console.Clear();
	Console.WriteLine($"An unhandled exception occurred, with message: {((Exception)e.ExceptionObject).Message}\n");
	if (e.IsTerminating) await Core.Restart();
};


SerializationTest.RunTest();

await Task.Delay(Timeout.Infinite);

