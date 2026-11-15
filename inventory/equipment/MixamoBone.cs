public enum MixamoBone
{
    Hips,
    Spine,
    Spine1,
    Spine2,
    Neck,
    Head,
    HeadTopEnd,
    LeftShoulder,
    LeftArm,
    LeftForeArm,
    LeftHand,
    RightShoulder,
    RightArm,
    RightForeArm,
    RightHand
}

public static class MixamoBoneNames
{
    public static string ToMixamoName(this MixamoBone bone) => bone switch
    {
        MixamoBone.Hips => "mixamorig:Hips",
        MixamoBone.Spine => "mixamorig:Spine",
        MixamoBone.Spine1 => "mixamorig:Spine1",
        MixamoBone.Spine2 => "mixamorig:Spine2",
        MixamoBone.Neck => "mixamorig:Neck",
        MixamoBone.Head => "mixamorig:Head",
        MixamoBone.HeadTopEnd => "mixamorig:HeadTop_End",
        MixamoBone.LeftShoulder => "mixamorig:LeftShoulder",
        MixamoBone.LeftArm => "mixamorig:LeftArm",
        MixamoBone.LeftForeArm => "mixamorig:LeftForeArm",
        MixamoBone.LeftHand => "mixamorig:LeftHand",
        MixamoBone.RightShoulder => "mixamorig:RightShoulder",
        MixamoBone.RightArm => "mixamorig:RightArm",
        MixamoBone.RightForeArm => "mixamorig:RightForeArm",
        MixamoBone.RightHand => "mixamorig:RightHand",
        _ => string.Empty
    };
}
