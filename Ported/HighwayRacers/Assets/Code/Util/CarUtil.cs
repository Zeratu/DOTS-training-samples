﻿using System;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace HighwayRacer
{
    public class CarUtil
    {
        public static void SetSpeedForUnblocked(ref TargetSpeed targetSpeed, ref Speed speed, float dt, float unblockedSpeed)
        {
            targetSpeed.Val = unblockedSpeed;

            if (targetSpeed.Val < speed.Val)
            {
                speed.Val -= RoadSys.decelerationRate * dt;
                if (speed.Val < targetSpeed.Val)
                {
                    speed.Val = targetSpeed.Val;
                }
            }
            else if (targetSpeed.Val > speed.Val)
            {
                speed.Val += RoadSys.accelerationRate * dt;
                if (speed.Val > targetSpeed.Val)
                {
                    speed.Val = targetSpeed.Val;
                }
            }
        }

        // binary search to find a car's pos in the bucket
        // (we assume the pos is present!)
        public static int findInBucket(UnsafeList<Car> bucket, float pos, float lane)
        {
            int start = 0;
            int end = bucket.Length - 1;
            int idx = end / 2;
            while (true)
            {
                var candidate = bucket[idx];
                var samePos = candidate.Pos == pos;
                if (samePos && candidate.Lane == lane)
                {
                    return idx;
                }

                if ((pos > candidate.Pos) || (samePos && lane > candidate.Lane)) // look up 
                {
                    //Assert.IsFalse(idx == end, "exhausted search at end");
                    start = idx + 1;
                    idx = (end - start) / 2 + start;
                }
                else // look down
                {
                    //Assert.IsFalse(idx == start, "exhausted search at start");
                    end = idx - 1;
                    idx = (end - start) / 2 + start;
                }
            }
        }

        public static bool CanMerge(int index, ref Car car, int destLane, float segmentLength,
            UnsafeList<Car> bucket, UnsafeList<Car> nextBucket)
        {
            // return false if a car is behind in dest lane within the mergeBehind range
            for (int behindIdx = index - 1; behindIdx >= 0; behindIdx--)
            {
                var other = bucket[behindIdx];

                if ((pos - other.Pos) > RoadSys.mergeLookBehind)
                {
                    break; // all remaining cars too far back to block us from behind
                }

                if (other.Lane == destLane)
                {
                    return false; // blocked
                }
            }

            // return false if a car is ahead in dest lane within the mergeAhead range
            for (int aheadIdx = index + 1; aheadIdx < bucket.Length; aheadIdx++)
            {
                var other = bucket[aheadIdx];

                if ((other.Pos - pos) > RoadSys.mergeLookAhead)
                {
                    return true; // all remaining cars too far ahead to block us
                }

                if (other.Lane == destLane)
                {
                    return false; // blocked
                }
            }

            // same as above, but continue check in second bucket


            for (int aheadIdx = 0; aheadIdx < nextBucket.Length; aheadIdx++)
            {
                var other = nextBucket[aheadIdx];
                var otherPos = (wrapAround) ? other.Pos + trackLength : other.Pos;

                if ((otherPos - pos) > RoadSys.mergeLookAhead)
                {
                    return true; // all remaining cars too far ahead to block us
                }

                if (other.Lane == destLane)
                {
                    return false; // blocked
                }
            }

            return true; // exhausted the second bucket without finding a blocking car ahead
        }


        public static void SetUnblockedSpeed(ref Speed speed, ref TargetSpeed targetSpeed, float dt, float unblockedSpeed)
        {
            var newTargetSpeed = unblockedSpeed;

            if (newTargetSpeed < speed.Val)
            {
                var s = speed.Val - RoadSys.decelerationRate * dt;
                if (s < newTargetSpeed)
                {
                    s = newTargetSpeed;
                }

                speed.Val = s;
            }
            else if (newTargetSpeed > speed.Val)
            {
                var s = speed.Val + RoadSys.accelerationRate * dt;
                if (s > newTargetSpeed)
                {
                    s = newTargetSpeed;
                }

                speed.Val = s;
            }

            targetSpeed.Val = newTargetSpeed;
        }

        // 'index' is the car's index in the bucket; it's valid even when car is not blocked 
        public static void GetClosestPosAndSpeed(ref Car car, out float distance, out float closestSpeed, int index, float segmentLength, UnsafeList<Car> bucket, UnsafeList<Car> nextBucket)
        {
            distance = float.MaxValue;
            closestSpeed = 0.0f;

            // find pos and speed of car ahead in lane within the mergeAhead range
            for (int i = index + 1; i < bucket.Length; i++)
            {
                var other = bucket[i];

                var dist = (other.Pos - trackPos.Val);
                if (dist > RoadSys.mergeLookAhead)
                {
                    return; // all remaining cars too far ahead to block us
                }

                if (other.Lane == lane) // blocked in the same lane
                {
                    distance = dist;
                    closestSpeed = other.Speed;
                    return;
                }
            }

            // continue check in second bucket

            for (int forwardIdx = 0; forwardIdx < bucket.Length; forwardIdx++)
            {
                var other = bucket[forwardIdx];
                var otherPos = (wrapAround) ? other.Pos + trackLength : other.Pos;

                var dist = otherPos - trackPos.Val;
                if (dist > RoadSys.mergeLookAhead)
                {
                    return; // all remaining cars too far ahead to block us
                }

                if (other.Lane == lane)
                {
                    distance = dist;
                    closestSpeed = other.Speed;
                    return; // blocked in the same lane
                }
            }

            // exhausted the second bucket without finding a blocking car ahead
        }
    }
}