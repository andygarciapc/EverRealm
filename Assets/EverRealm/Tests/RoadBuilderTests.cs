using System.Collections.Generic;
using EverRealm.World;
using NUnit.Framework;
using UnityEngine;

namespace EverRealm.Tests
{
    public class RoadBuilderTests
    {
        const int Seed = 12345; // The shipped WorldSettings seed.

        sealed class Island
        {
            public WorldData World;
            public WorldSettings Settings;
            public List<Settlement> Settlements;
            public RoadNetwork Roads;
            public Vector3? Landing;
        }

        static readonly Dictionary<int, Island> Islands = new Dictionary<int, Island>();

        static Island Build(int seed)
        {
            if (Islands.TryGetValue(seed, out Island island)) return island;
            var settings = ScriptableObject.CreateInstance<WorldSettings>();
            settings.seed = seed;
            WorldData world = new TerrainGenerator(settings).Generate();
            List<Settlement> settlements = SettlementBuilder.Build(world, settings);
            RoadNetwork roads = RoadBuilder.Build(world, settings, settlements, out Vector3? landing);
            return Islands[seed] = new Island { World = world, Settings = settings, Settlements = settlements, Roads = roads, Landing = landing };
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        [TestCase(12345), TestCase(777), TestCase(1), TestCase(42), TestCase(20261106)]
        public void IslandHasThreeHamletsAtWalkingDistance(int seed)
        {
            Island island = Build(seed);
            Assert.AreEqual(3, island.Settlements.Count);
            for (int a = 0; a < 3; a++)
            for (int b = a + 1; b < 3; b++)
                Assert.GreaterOrEqual(Flat(island.Settlements[a].Square, island.Settlements[b].Square), island.Settings.minHamletSpacing);
        }

        [Test]
        public void HamletsHaveDifferentNames()
        {
            var names = new HashSet<string>();
            foreach (Settlement settlement in Build(Seed).Settlements) Assert.IsTrue(names.Add(settlement.Name), settlement.Name);
        }

        [Test]
        public void SameSeed_ProducesSameRoads()
        {
            WorldSettings settings = Build(Seed).Settings;
            WorldData world = new TerrainGenerator(settings).Generate();
            List<Settlement> settlements = SettlementBuilder.Build(world, settings);
            RoadNetwork again = RoadBuilder.Build(world, settings, settlements, out _);
            RoadNetwork first = Build(Seed).Roads;
            Assert.AreEqual(first.Roads.Count, again.Roads.Count);
            for (int road = 0; road < first.Roads.Count; road++)
                CollectionAssert.AreEqual(first.Roads[road], again.Roads[road]);
        }

        [TestCase(12345), TestCase(777), TestCase(1), TestCase(42), TestCase(20261106)]
        public void EveryHamletAndTheLandingConnectByRoad(int seed)
        {
            Island island = Build(seed);
            Assert.IsTrue(island.Landing.HasValue, "no landing found");
            foreach (Settlement from in island.Settlements)
            {
                foreach (Settlement to in island.Settlements)
                {
                    if (from == to) continue;
                    Assert.IsTrue(island.Roads.TryFindRoute(from.Square, to.Square, out List<Vector3> route, out _), $"{from.Name} to {to.Name}");
                    Assert.Less(Flat(route[0], from.Square), from.Radius, $"road should reach {from.Name}");
                    Assert.Less(Flat(route[route.Count - 1], to.Square), to.Radius, $"road should reach {to.Name}");
                }
                Assert.IsTrue(island.Roads.TryFindRoute(island.Landing.Value, from.Square, out List<Vector3> ashore, out _), $"landing to {from.Name}");
                Assert.Less(Flat(ashore[ashore.Count - 1], from.Square), from.Radius, $"road from the landing should reach {from.Name}");
            }
        }

        [TestCase(12345), TestCase(777), TestCase(1), TestCase(42), TestCase(20261106)]
        public void RoadsStayDryAndNeverStepMoreThanOneBlock(int seed)
        {
            Island island = Build(seed);
            WorldData world = island.World;
            int[] tops = SettlementBuilder.TopBlocks(world);
            foreach (IReadOnlyList<Vector3> road in island.Roads.Roads)
            {
                int previous = int.MinValue;
                for (int i = 0; i + 1 < road.Count; i++)
                {
                    int steps = Mathf.CeilToInt(Flat(road[i], road[i + 1]) / (world.BlockSize * 0.5f));
                    for (int s = 0; s < steps; s++)
                    {
                        Vector3 point = Vector3.Lerp(road[i], road[i + 1], s / (float)steps);
                        int bx = Mathf.FloorToInt(point.x / world.BlockSize), bz = Mathf.FloorToInt(point.z / world.BlockSize);
                        int top = tops[bx + bz * world.SizeInBlocks];
                        Assert.Greater((top + 1) * world.BlockSize, island.Settings.seaLevel, $"under water at {point}");
                        if (previous != int.MinValue) Assert.LessOrEqual(Mathf.Abs(top - previous), 1, $"step at {point}");
                        previous = top;
                    }
                }
            }
        }

        [Test]
        public void RoadsLeaveHutsStanding()
        {
            Island island = Build(Seed);
            WorldData world = island.World;
            foreach (Settlement settlement in island.Settlements)
            foreach (House house in settlement.Houses)
            {
                int y = Mathf.RoundToInt(house.Inside.y / world.BlockSize) - 1;
                int bx = Mathf.FloorToInt(house.Inside.x / world.BlockSize), bz = Mathf.FloorToInt(house.Inside.z / world.BlockSize);
                Assert.AreEqual(BlockType.Planks, world.GetBlock(bx, y, bz), "hut floor");
            }
        }

        [Test]
        public void GradeProfile_KeepsFixedSamplesAndLimitsTheSlope()
        {
            var heights = new[] { 20f, 40f, 0f, 35f, 22f };
            var isFixed = new[] { true, false, false, false, true };
            var along = new[] { 0f, 4f, 8f, 12f, 16f };
            float[] graded = RoadBuilder.GradeProfile(heights, isFixed, along, RoadBuilder.MaxGrade, 15f);

            Assert.AreEqual(20f, graded[0]);
            Assert.AreEqual(22f, graded[4]);
            for (int i = 1; i < graded.Length; i++)
            {
                Assert.LessOrEqual(Mathf.Abs(graded[i] - graded[i - 1]), 4f * RoadBuilder.MaxGrade + 0.001f, $"sample {i}");
                Assert.GreaterOrEqual(graded[i], 15f);
            }
        }

        [Test]
        public void RouteFollowsTheRoadFromWhereverYouAre()
        {
            Island island = Build(Seed);
            Settlement from = island.Settlements[0], to = island.Settlements[1];
            Vector3 offRoad = from.Square + new Vector3(3f, 0f, 3f);
            Assert.IsTrue(island.Roads.TryFindRoute(offRoad, to.Square, out List<Vector3> route, out float length));
            Assert.Greater(route.Count, 10);
            Assert.GreaterOrEqual(length, Flat(from.Square, to.Square) * 0.9f);
            Assert.Less(Flat(route[route.Count - 1], to.Square), 4f, "route should end at the square");
        }

        [Test]
        public void NoRoads_NoRoute()
        {
            Assert.IsFalse(new RoadNetwork().TryFindRoute(Vector3.zero, Vector3.one, out _, out _));
        }
    }
}
