﻿using Unity.Entities;
using Unity.Mathematics;

public enum DistanceFieldModel
{
    SpherePlane,
    Metaballs,
    SpinMixer,
    SphereField,
    FigureEight,
    PerlinNoise,
}

public struct DistanceField : IComponentData
{
    public DistanceFieldModel Value;
    public float SwitchCooldown;
    public Random rng;
    public int Preview;
}

public class DistanceFieldAuthoring : UnityEngine.MonoBehaviour, IConvertGameObjectToEntity
{
    public DistanceFieldModel model = DistanceFieldModel.FigureEight;
    public bool preview = false;
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var distanceField = new DistanceField { Value = model, SwitchCooldown = 0f, rng = new Random(1), Preview = preview ? 1 : 0 };
        distanceField.rng.InitState(0x3731275Bu);
        dstManager.AddComponentData(entity, distanceField);
    }
}