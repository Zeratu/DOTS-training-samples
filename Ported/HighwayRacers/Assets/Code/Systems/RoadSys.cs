﻿using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace HighwayRacer
{
    public class RoadSys : SystemBase
    {
        public static NativeArray<RoadSegment> roadSegments;
        public static NativeArray<float> thresholds;
        public static NativeArray<float> segmentLengths;
        public static CarBuckets CarBuckets;

        public const int nLanes = 4;
        public static int nSegments = 8;
        public const float minDist = 4.0f;

        public const float mergeLookAhead = 14.0f;
        public const float mergeLookBehind = 5.5f;

        public const float decelerationRate = 3.0f; // m/s to lose per second
        public const float accelerationRate = 8.0f; // m/s to lose per second

        public const float carSpawnDist = 8.0f;
        public static int numCars = 129;

        public static float minLength = 400.0f; // 4 * curved length + 4 * min straight length
        public static float maxLength = 200999;

        public const float laneWidth = 1.88f;
        public static float roadLength = minLength;

        public static float straightLength;

        private List<GameObject> straightRoadExtraPieces = new List<GameObject>(); // pieces of straight road that should be destroyed when we recreate the road

        protected override void OnCreate()
        {
            RequireSingletonForUpdate<RoadInit>();
            var ent = EntityManager.CreateEntity(typeof(RoadInit));
            EntityManager.SetComponentData(ent,
                new RoadInit()
                {
                    Length = roadLength,
                    NumCars = numCars
                }
            );
            base.OnCreate();
        }

        protected override void OnDestroy()
        {
            roadSegments.Dispose();
            thresholds.Dispose();
            segmentLengths.Dispose();
            CarBuckets.Dispose();
            base.OnDestroy();
        }

        public static int NumCarsFitInStraightLane()
        {
            return (int) math.ceil(straightLength / minDist);
        }
        
        public static int NumCarsFitInStraightSegment(float straightLength)
        {
            return (int) math.ceil((nLanes * straightLength) / minDist);
        }

        public static int GetMaxCars(float length)
        {
            var n = (int) Mathf.Floor((nLanes * length) / carSpawnDist);
            return n - (8 * nLanes);   // account for wrap around + for rounding error   
        }

        protected override void OnUpdate()
        {
            var roadInit = GetSingleton<RoadInit>();
            EntityManager.DestroyEntity(GetSingletonEntity<RoadInit>());

            numCars = roadInit.NumCars;
            roadLength = roadInit.Length;
            
            World.DefaultGameObjectInjectionWorld.GetExistingSystem<CameraSys>().ResetCamera();
            CarSpawnSys.respawnCars = true;

            var transform = GameObject.Find("Road").transform;

            var rotFromCardinal = new Dictionary<Cardinal, quaternion>();
            rotFromCardinal[Cardinal.UP] = quaternion.EulerXYZ(0, 0, 0);
            rotFromCardinal[Cardinal.DOWN] = quaternion.EulerXYZ(0, math.radians(180), 0);
            rotFromCardinal[Cardinal.LEFT] = quaternion.EulerXYZ(0, math.radians(-90), 0);
            rotFromCardinal[Cardinal.RIGHT] = quaternion.EulerXYZ(0, math.radians(90), 0);

            var vecFromCardinal = new Dictionary<Cardinal, float3>();
            vecFromCardinal[Cardinal.UP] = Vector3.forward;
            vecFromCardinal[Cardinal.DOWN] = Vector3.back;
            vecFromCardinal[Cardinal.LEFT] = Vector3.left;
            vecFromCardinal[Cardinal.RIGHT] = Vector3.right;

            var rightRotFromCardinal = new Dictionary<Cardinal, quaternion>();
            rightRotFromCardinal[Cardinal.UP] = quaternion.EulerXYZ(0, math.radians(90), 0);
            rightRotFromCardinal[Cardinal.DOWN] = quaternion.EulerXYZ(0, math.radians(270), 0);
            rightRotFromCardinal[Cardinal.LEFT] = quaternion.EulerXYZ(0, 0, 0);
            rightRotFromCardinal[Cardinal.RIGHT] = quaternion.EulerXYZ(0, math.radians(180), 0);

            var leftVecFromCardinal = new Dictionary<Cardinal, float3>();
            leftVecFromCardinal[Cardinal.UP] = Vector3.left;
            leftVecFromCardinal[Cardinal.DOWN] = Vector3.right;
            leftVecFromCardinal[Cardinal.LEFT] = Vector3.back;
            leftVecFromCardinal[Cardinal.RIGHT] = Vector3.forward;

            const float baseStraightLength = 12.0f;
            const float curvedLength = 48.69f; // calculated from radius that is midpoint between first and last lane
            
            int segmentsPerStraightaway = (int) (roadLength / 4000) + 1;  // todo: play with this number to experiment with segment size
            nSegments = 4 + (4 * segmentsPerStraightaway);

            straightLength = (roadLength - curvedLength * 4) / (nSegments - 4);
            float straightScale = straightLength / baseStraightLength;

            if (roadSegments.IsCreated)
            {
                roadSegments.Dispose();
            }

            if (thresholds.IsCreated)
            {
                thresholds.Dispose();
            }

            roadSegments = new NativeArray<RoadSegment>(nSegments, Allocator.Persistent);
            thresholds = new NativeArray<float>(nSegments, Allocator.Persistent);
            segmentLengths = new NativeArray<float>(nSegments, Allocator.Persistent);
            
            CarBuckets.Dispose();
            CarBuckets = new CarBuckets(nSegments, NumCarsFitInStraightSegment(straightLength) * 2);
            
            var segmentGOs = new GameObject[nSegments];

            // cleanup extra straight pieces
            foreach (var go in straightRoadExtraPieces)
            {
                GameObject.Destroy(go);
            }

            straightRoadExtraPieces.Clear();

            int newIdx = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);

                var segmentInfo = child.gameObject.GetComponent<SegmentAuth>();
                var cardinal = segmentInfo.direction;

                if (segmentInfo.radius > 0) // curved
                {
                    segmentGOs[newIdx] = child.gameObject;

                    roadSegments[newIdx] = new RoadSegment()
                    {
                        Position = new float3(),
                        EndPosition = new float3(),
                        Direction = cardinal,
                        DirectionVec = vecFromCardinal[cardinal],
                        DirectionRot = rotFromCardinal[cardinal],
                        DirectionRotEnd = rightRotFromCardinal[cardinal],
                        DirectionLaneOffset = leftVecFromCardinal[cardinal] * laneWidth,
                        Length = curvedLength,
                        Radius = segmentInfo.radius,
                    };

                    newIdx++;
                }
                else // straight
                {
                    child.localScale = new Vector3(1, 1, straightScale);

                    for (int j = 0; j < segmentsPerStraightaway; j++)
                    {
                        if (j == 0)
                        {
                            segmentGOs[newIdx] = child.gameObject;
                        }
                        else
                        {
                            var newTrackPiece = GameObject.Instantiate(child.gameObject);
                            segmentGOs[newIdx] = newTrackPiece;
                            straightRoadExtraPieces.Add(newTrackPiece);
                        }

                        roadSegments[newIdx] = new RoadSegment()
                        {
                            Position = new float3(),
                            EndPosition = new float3(),
                            Direction = cardinal,
                            DirectionVec = vecFromCardinal[cardinal],
                            DirectionRot = rotFromCardinal[cardinal],
                            DirectionRotEnd = rightRotFromCardinal[cardinal],
                            DirectionLaneOffset = leftVecFromCardinal[cardinal] * laneWidth,
                            Length = straightLength,
                            Radius = 0,
                        };

                        newIdx++;
                    }
                }
            }

            var pos = new float3();
            float lengthSum = 0;

            // put the track pieces in place: end point of segment is start point of next
            for (int i = 0; i < roadSegments.Length; i++)
            {
                var segment = roadSegments[i];
                float length = 0;
                float3 endPos = new float3();

                if (segment.Radius > 0) // curved
                {
                    endPos = pos + vecFromCardinal[segment.Direction] * segment.Radius + leftVecFromCardinal[segment.Direction] * -segment.Radius;
                    length = curvedLength;
                    lengthSum += curvedLength;
                }
                else // straight
                {
                    endPos = pos + vecFromCardinal[segment.Direction] * straightLength;
                    length = straightLength;
                    lengthSum += straightLength;
                }

                segmentGOs[i].transform.position = pos;

                segment.Position = pos;
                segment.EndPosition = endPos;
                segment.Length = length;
                segment.Threshold = lengthSum;
                thresholds[i] = lengthSum;
                segmentLengths[i] = length;

                roadSegments[i] = segment;

                pos = endPos;
            }
        }
    }

// todo: use float2's instead
    public struct RoadSegment
    {
        public float3 Position;
        public float3 EndPosition; // relative from Position 
        public Cardinal Direction;
        public float3 DirectionVec;
        public quaternion DirectionRot;
        public quaternion DirectionRotEnd; // only for curved segments; 90 degrees right of DirectionRot
        public float3 DirectionLaneOffset;
        public float Length;
        public float Radius; // 0 for straight segments 
        public float Threshold; // end track pos i.e. cummulative length of this segment and all prior
    }

    public enum Cardinal
    {
        UP,
        DOWN,
        LEFT,
        RIGHT,
    }
}