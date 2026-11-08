using Godot;

[GlobalClass]
public partial class TaskData : Resource
{
    public enum TaskStates
    {
        None,
        Required,
        InProgress,
        Completed
    }

    public enum TaskTypes
    {
        None,
        Kill,
        Loot,
        Repair,
        Collect
    }

    public enum Races
    {
        None,
        Human,
        Orc,
        Elf,
        Undead
    }

    public enum Skills
    {
        Construction,
        MeleeCombat,
        RangedCombat,
        Lockpicking,
        Gathering
    }

    [Export] public TaskStates State = TaskStates.Required;
    [Export] public TaskTypes Type = TaskTypes.None;
    [Export] public Races RequiredRace = Races.None;
    [Export] public Godot.Collections.Array<Skills> RequiredSkills = new();
}