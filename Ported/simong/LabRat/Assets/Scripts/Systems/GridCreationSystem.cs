using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;


public class GridCreationSystem : SystemBase
{
    public NativeArray<CellInfo> Cells { get; private set; }

    private EntityCommandBufferSystem m_commandBuffer;
    private EntityQuery m_requestQuery;

    protected override void OnCreate()
    {
        m_commandBuffer = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();
        m_requestQuery = GetEntityQuery(ComponentType.ReadOnly<GenerateGridRequestComponent>());

        RequireForUpdate(m_requestQuery);

        base.OnCreate();
    }

    protected override void OnDestroy()
    {
        if (Cells.IsCreated)
        {
            Cells.Dispose();
        }

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        var constantData = ConstantData.Instance;

        Debug.Log("generate grid");

        if (!Cells.IsCreated && constantData != null)
        {


            Cells = new NativeArray<CellInfo>(constantData.BoardDimensions.x * constantData.BoardDimensions.y, Allocator.Persistent);

            var cellsarray = Cells;

            int width = constantData.BoardDimensions.x;
            int height = constantData.BoardDimensions.y;
            int bottomRight = (width * height) - 1;

            var random = new Unity.Mathematics.Random((uint)(DateTime.Now.Ticks % uint.MaxValue));

            Job.WithCode(() =>
            {
                int bottomLeft = width * (height - 1);

                //initialise travel
                cellsarray[0] = cellsarray[0].SetTravelDirections(GridDirection.NORTH | GridDirection.EAST);

                cellsarray[width - 1] = cellsarray[width - 1].SetTravelDirections(GridDirection.NORTH | GridDirection.WEST);
                cellsarray[bottomLeft] = cellsarray[bottomLeft].SetTravelDirections(GridDirection.SOUTH | GridDirection.EAST);
                cellsarray[bottomRight] = cellsarray[(width * height) - 1].SetTravelDirections(GridDirection.SOUTH | GridDirection.WEST);

                GridDirection fromNorth = GridDirection.SOUTH | GridDirection.EAST | GridDirection.WEST;
                GridDirection fromSouth = GridDirection.NORTH | GridDirection.EAST | GridDirection.WEST;
                GridDirection fromWest = GridDirection.NORTH | GridDirection.SOUTH | GridDirection.EAST;
                GridDirection fromEast = GridDirection.NORTH | GridDirection.SOUTH | GridDirection.WEST;

                for (int i = 1; i < (width - 1); i++)
                {
                    cellsarray[i] = cellsarray[i].SetTravelDirections(fromSouth);
                    cellsarray[bottomLeft + i] = cellsarray[bottomLeft + i].SetTravelDirections(fromNorth);
                }

                for (int i = 1; i < (height - 1); i++)
                {
                    cellsarray[width * i] = cellsarray[width * i].SetTravelDirections(fromWest);
                    cellsarray[(width * (i + 1)) - 1] = cellsarray[(width * (i + 1)) - 1].SetTravelDirections(fromEast);
                }

                //position bases
                int xOffset = (int)(width * 0.333f);
                int yOffset = (int)(height * 0.333f);

                int baseIndex = (yOffset * width) + xOffset;

                cellsarray[baseIndex] = cellsarray[baseIndex].SetBasePlayerId(0);

                baseIndex = (yOffset * 2 * width) + xOffset * 2;

                cellsarray[baseIndex] = cellsarray[baseIndex].SetBasePlayerId(1);

                baseIndex = (yOffset * 2 * width) + xOffset;

                cellsarray[baseIndex] = cellsarray[baseIndex].SetBasePlayerId(2);

                baseIndex = (yOffset * width) + xOffset * 2;

                cellsarray[baseIndex] = cellsarray[baseIndex].SetBasePlayerId(3);

            }).Schedule();

            Job.WithCode(() =>
            {

                //add walls

                int numWalls = (int)(width * height * 0.2f);
                //Debug.Log("create walls: " + numWalls);

                int count = 0;
                while (count < numWalls)
                {
                    int x = random.NextInt(0, width);
                    int y = random.NextInt(0, height);
                    int index = (y * width) + x;

                    var cell = cellsarray[index];

                    GridDirection dir = (GridDirection)(1 << (random.NextInt(0, 3)));

                    if (cell.CanTravel(dir) && cell.CanTravel(GridDirection.ALL))
                    {
                        cellsarray[index] = cell.BlockTravel(dir);
                        count++;

                        switch (dir)
                        {
                            case GridDirection.NORTH:
                                y++;
                                dir = GridDirection.SOUTH;
                                break;
                            case GridDirection.SOUTH:
                                dir = GridDirection.NORTH;
                                y--;
                                break;
                            case GridDirection.EAST:
                                dir = GridDirection.WEST;
                                x++;
                                break;
                            case GridDirection.WEST:
                                dir = GridDirection.EAST;
                                x--;
                                break;
                        }

                        if (x >= 0 && x < width && y >= 0 && y < height)
                        {
                            index = (y * width) + x;
                            cellsarray[index] = cellsarray[index].BlockTravel(dir);
                        }

                        //Debug.Log("adding wall at (" + x + ", " + y + ") dir: " + dir);
                    }
                }

                //add holes
                var numHoles = random.NextInt(0, 4);
                count = 0;

                while (count < numHoles)
                {
                    int x = random.NextInt(1, width - 1);
                    int y = random.NextInt(1, height - 1);
                    int index = (y * width) + x;

                    var cell = cellsarray[index];

                    if (cell.IsEmpty())
                    {
                        cellsarray[index] = cell.SetIsHole();
                        count++;
                    }
                }

            }).Schedule();

            var ecb = m_commandBuffer.CreateCommandBuffer();
            var cellSize = constantData.CellSize;

            var spawnCats = constantData.CatSpawnerCount;
            var spawnMice = constantData.MouseSpawnerCount;

            Entities.ForEach((in PrefabReferenceComponent prefabs) => {

                //create cells and bases
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        var index = (width * y) + x;
                        var cell = cellsarray[index];

                        if (!cell.IsHole())
                        {
                            Entity prefab = ((x + y) % 2) == 1 ? prefabs.CellOddPrefab : prefabs.CellPrefab;
                            var entity = ecb.Instantiate(prefab);

                            if (entity != Entity.Null)
                            {
                                ecb.SetComponent(entity, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(x, y), cellSize) });
                            }
                        }

                        if (cell.IsBase())
                        {
                            int playerId = cell.GetBasePlayerId();
                            Entity basePrefab = Entity.Null;

                            switch (playerId)
                            {
                                case 0:
                                    basePrefab = prefabs.BasePrefab0;
                                    break;

                                case 1:
                                    basePrefab = prefabs.BasePrefab1;
                                    break;

                                case 2:
                                    basePrefab = prefabs.BasePrefab2;
                                    break;

                                case 3:
                                    basePrefab = prefabs.BasePrefab3;
                                    break;
                            }

                            if (basePrefab != Entity.Null)
                            {
                                var entity = ecb.Instantiate(basePrefab);

                                if (entity != Entity.Null)
                                {
                                    ecb.SetComponent(entity, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(x, y), cellSize) });
                                    ecb.AddComponent(entity, new NonUniformScale { Value = new float3(1f) });
                                }
                            }
                        }

                        //Debug.Log("cell (" + x + ", " + y + ")");

                        for (int i = 0; i <= 3; i++)
                        {
                            GridDirection dir = (GridDirection)(1 << i);
                            bool check = true;

                            switch (dir)
                            {
                                case GridDirection.SOUTH:
                                    check = (y == 0);
                                    break;
                                case GridDirection.WEST:
                                    check = (x == 0);
                                    break;
                            }

                            if (check && !cell.CanTravel(dir))
                            {
                                var wall = ecb.Instantiate(prefabs.WallPrefab);

                                //Debug.Log("spawning wall at (" + x + ", " + y + ") dir: " + dir);

                                if (wall != Entity.Null)
                                {
                                    ecb.SetComponent(wall, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(x, y), cellSize) });
                                    ecb.SetComponent(wall, new Rotation2D { Value = Utility.DirectionToAngle(dir) });
                                }
                            }
                        }
                    }
                }

                //create spawners
                var count = 0;
                int offset = (int)(height / (spawnCats + 1));

                while(count < spawnCats)
                {
                    var spawner = ecb.Instantiate(prefabs.CatSpawnerPrefab);
                    if (spawner != Entity.Null)
                    {
                        ecb.SetComponent(spawner, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(width - 1, count * offset), cellSize) });
                        ecb.SetComponent(spawner, new Direction2D { Value = GridDirection.WEST });
                    }

                    if(count < (spawnCats - 1))
                    {
                        spawner = ecb.Instantiate(prefabs.CatSpawnerPrefab);
                        if (spawner != Entity.Null)
                        {
                            ecb.SetComponent(spawner, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(0, height - 1 - (count * offset)), cellSize) });
                            ecb.SetComponent(spawner, new Direction2D { Value = GridDirection.EAST });
                        }
                    }

                    count += 2;
                }

                count = 0;
                offset = (int)(width / (spawnMice + 1));

                while (count < spawnMice)
                {
                    var spawner = ecb.Instantiate(prefabs.MouseSpawnerPrefab);
                    if (spawner != Entity.Null)
                    {
                        ecb.SetComponent(spawner, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(count * offset, 0), cellSize) });
                        ecb.SetComponent(spawner, new Direction2D { Value = GridDirection.NORTH });
                    }

                    if(count < (spawnMice - 1))
                    {
                        spawner = ecb.Instantiate(prefabs.MouseSpawnerPrefab);
                        if (spawner != Entity.Null)
                        {
                            ecb.SetComponent(spawner, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(width - 1 - (count * offset), height - 1), cellSize) });
                            ecb.SetComponent(spawner, new Direction2D { Value = GridDirection.SOUTH });
                        }
                    }

                    count += 2;
                }

            }).Schedule();

            Entities.ForEach((Entity entity, in GenerateGridRequestComponent request) =>
            {
                ecb.DestroyEntity(entity);
            }).Schedule();

            m_commandBuffer.AddJobHandleForProducer(Dependency);

            /*for (int i = 0; i <= bottomRight; i++)
            {
                var directions = i +" can travel: ";
                if (cellsarray[i].CanTravel(GridDirection.NORTH)) directions += "N";
                if (cellsarray[i].CanTravel(GridDirection.EAST)) directions += "E";
                if (cellsarray[i].CanTravel(GridDirection.SOUTH)) directions += "S";
                if (cellsarray[i].CanTravel(GridDirection.WEST)) directions += "W";

                Debug.Log(directions);
                Debug.Log(i + " is hole: " + cellsarray[i].IsHole());
            }

            Debug.Log("cell infos created"); */
        }
    }
}