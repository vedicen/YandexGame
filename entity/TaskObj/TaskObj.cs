using Godot;
using System;

public partial class TaskObj : Entity
{
	public enum TaskStates
	{
		Idle,
		Required,
		InProgress,
	}
	public TaskStates TaskState = TaskStates.Idle;

	public override void _Ready()
	{
	}


	public void ProgressCancel(){
		if(TaskState == TaskStates.InProgress){
			TaskState = TaskStates.Required;
		}
	}
}
