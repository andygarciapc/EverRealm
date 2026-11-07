using System.Collections.Generic;
using EverRealm.World;
using NUnit.Framework;
using UnityEngine;

namespace EverRealm.Tests
{
    public class LandformTests
    {
        const int Seed = 12345; // The shipped WorldSettings seed.

        sealed class Island
        {
            public WorldData World;
            public RoadNetwork Roads;
            public List<Settlement> Settlements;
            public WorldSettings Settings;
            public int[] GroundBeforeOverhangs;
            public int Formations, Arches, Overhangs;
        }

        static readonly Dictionary<int, Island> Islands = new Dictionary<int, Island>();

        static Island Build(int seed)
        {
            if (Islands.TryGetValue(seed, out Island island)) return island;
            var settings = ScriptableObject.CreateInstance<WorldSettings>();
            settings.seed = seed;
            var terrain = new TerrainGenerator(settings);
            WorldData world = terrain.Generate();
            List<Settlement> settlements = SettlementBuilder.Build(world, settings);
            RoadNetwork roads = RoadBuilder.Build(world, settings, settlements, out _);
            int[] tops = SettlementBuilder.TopBlocks(world);
            int formations = FormationBuilder.Build(world, settings, terrain, tops, settlements);
            List<CliffRim> rims = Cliffs.FindRims(world, settings, tops);
            int arches = ArchBuilder.Build(world, settings, terrain, tops, rims, settlements);
            var ground = (int[])tops.Clone();
            int overhangs = OverhangBuilder.Build(world, settings, terrain, tops, rims, settlements);
            return Islands[seed] = new Island { World = world, Settings = settings, GroundBeforeOverhangs = ground, Roads = roads, Settlements = settlements, Formations = formations, Arches = arches, Overhangs = overhangs };
        }

        [TestCase(Seed), TestCase(777), TestCase(42)]
        public void IslandsHaveFormationsAndOverhangsAndFewArches(int seed)
        {
            Island island = Build(seed);
            Assert.Greater(island.Formations, 8);
            Assert.Greater(island.Overhangs, 0);
            Assert.Less(island.Overhangs, 12000, "overhangs should be rare");
            Assert.GreaterOrEqual(island.Arches, 1);
            Assert.LessOrEqual(island.Arches, 5);
        }

        [TestCase(Seed), TestCase(777), TestCase(42)]
        public void RoadsKeepTheirHeadroom(int seed)
        {
            Island island = Build(seed);
            WorldData world = island.World;
            int headroom = Mathf.CeilToInt(2.5f / world.BlockSize);
            foreach (IReadOnlyList<Vector3> road in island.Roads.Roads)
            foreach (Vector3 point in road)
            {
                int x = Mathf.FloorToInt(point.x / world.BlockSize), z = Mathf.FloorToInt(point.z / world.BlockSize);
                int y = Mathf.RoundToInt(point.y / world.BlockSize);
                while (world.GetBlock(x, y, z) == BlockType.Path) y++; // Roads that meet can differ by a block.
                for (int i = 0; i < headroom; i++)
                    Assert.AreEqual(BlockType.Air, world.GetBlock(x, y + i, z), $"rock over the road at {point}");
            }
        }

        [TestCase(Seed), TestCase(777), TestCase(42)]
        public void OverhangsAreThinLipsNotWalls(int seed)
        {
            Island island = Build(seed);
            WorldData world = island.World;
            int size = world.SizeInBlocks, thickest = Mathf.CeilToInt(island.Settings.overhangReach / world.BlockSize * 1.1f) + 3;
            int[] ground = island.GroundBeforeOverhangs, tops = SettlementBuilder.TopBlocks(world);
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                int index = x + z * size;
                if (world.GetBlock(x, ground[index] + 1, z) != BlockType.Stone && world.GetBlock(x, ground[index] + 1, z) != BlockType.Air) continue;
                int y = tops[index], rock = 0;
                if (y <= ground[index] || world.GetBlock(x, y, z) is BlockType.Leaves or BlockType.Log) continue;
                while (y > ground[index] && world.GetBlock(x, y, z) != BlockType.Air) { rock++; y--; }
                Assert.LessOrEqual(rock, thickest, $"a wall of rock at column {x},{z}");
            }
        }

        [Test]
        public void HamletsStayClear()
        {
            Island island = Build(Seed);
            WorldData world = island.World;
            foreach (Settlement settlement in island.Settlements)
            {
                int x = Mathf.FloorToInt(settlement.Square.x / world.BlockSize), z = Mathf.FloorToInt(settlement.Square.z / world.BlockSize);
                int y = Mathf.RoundToInt(settlement.Square.y / world.BlockSize);
                for (int i = 0; i < 20; i++)
                    Assert.AreEqual(BlockType.Air, world.GetBlock(x, y + i, z), $"rock over {settlement.Name}");
            }
        }

        [Test]
        public void SomeRockHangsOverOpenAir()
        {
            WorldData world = Build(Seed).World;
            int overhanging = 0, size = world.SizeInBlocks, headroom = Mathf.CeilToInt(2.5f / world.BlockSize);
            for (int z = 0; z < size; z += 2)
            for (int x = 0; x < size; x += 2)
            {
                int y = world.GetChunkTop(x / world.ChunkSize, z / world.ChunkSize) - 1;
                while (y >= 0 && world.GetBlock(x, y, z) == BlockType.Air) y--;
                while (y >= 0 && world.GetBlock(x, y, z) != BlockType.Air) y--;
                int air = 0;
                while (y >= 0 && world.GetBlock(x, y, z) == BlockType.Air) { air++; y--; }
                if (y >= 0 && air >= headroom) overhanging++;
            }
            Assert.Greater(overhanging, 200);
        }
    }
}
