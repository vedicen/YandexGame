
public class AITask
{
    public Entity Target { get; }
    public TaskData Data { get; }

    public AITask(Entity target, TaskData data)
    {
        Target = target;
        Data = data;
    }
}
