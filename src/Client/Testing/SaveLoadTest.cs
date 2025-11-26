#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

using System.Collections.Concurrent;
using Theta.Engine;
using static Theta.Engine.Tracking;

namespace Theta.Testing;

internal static class SerializationTest
{
	public static void RunTest()
	{
		Console.WriteLine("Creating entities...");

		List<Entity> entities = [];

		for (int i = 0; i < 1024; i++) entities.Add(new());

		Console.WriteLine("Saving state...");

		Serialization.SaveState("E:\\state.dat");

		Console.WriteLine("Loading state...");

		Serialization.LoadState("E:\\state.dat");

		Console.WriteLine("Test complete.");
	}
}

[EntityTypeId(1)]
internal class Player : Entity
{
	public string Name; public int Level;

	public Player() { }

	public Player(Serialization.EntityRecord record) : base(record) { }

	protected override object[] SaveData() => [Name, Level];

	protected override void LoadData(object[] data) { data.Assign(ref Name, 0); data.Assign(ref Level, 1); }
}

[EntityTypeId(2)]
internal class Monster : Entity
{
	public int HP;
	public string Type;

	public Monster() { }

	public Monster(Serialization.EntityRecord record) : base(record) { }

	protected override object[] SaveData() => [HP, Type];

	protected override void LoadData(object[] data) { data.Assign(ref HP, 0); data.Assign(ref Type, 1); }
}
