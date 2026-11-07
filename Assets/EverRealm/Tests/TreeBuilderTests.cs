using System.Collections.Generic;
using EverRealm.World;
using NUnit.Framework;
using UnityEngine;

namespace EverRealm.Tests
{
    public class TreeBuilderTests
    {
        const int Seed = 12345; // The shipped WorldSettings seed.

        static (WorldData world, WorldSettings settings, List<Settlement> settlements, int trees) Build(int seed)
        {
            var settings = ScriptableObject.CreateInstance<WorldSettings>();
            settings.seed = seed;
            var terrain = new TerrainGenerator(settings);
            WorldData world = terrain.Generate();
            List<Settlement> settlements = SettlementBuilder.Build(world, settings);
            RoadBuilder.Build(world, settings, settlements, out _);
            return (world, settings, settlements, TreeBuilder.Plant(world, settings, terrain, settlements));
        }

        [Test]
        public void TreesGrowAndKeepOffRoadsAndHamlets()
        {
            (WorldData world, WorldSettings settings, List<Settlement> settlements, int trees) = Build(Seed);
            Assert.Greater(trees, 500);

            int size = world.SizeInBlocks;
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                int y = world.GetChunkTop(x / world.ChunkSize, z / world.ChunkSize) - 1;
                while (y >= 0 && world.GetBlock(x, y, z) is BlockType.Air or BlockType.Leaves) y--;
                if (y < 0 || world.GetBlock(x, y, z) != BlockType.Log) continue;

                while (world.GetBlock(x, y, z) == BlockType.Log) y--;
                Assert.AreNotEqual(BlockType.Path, world.GetBlock(x, y, z), "a trunk stands on a road");
                var trunk = new Vector2((x + 0.5f) * world.BlockSize, (z + 0.5f) * world.BlockSize);
                foreach (Settlement settlement in settlements)
                    Assert.Greater(Vector2.Distance(trunk, new Vector2(settlement.Square.x, settlement.Square.z)), settlement.Radius, $"a tree stands in {settlement.Name}");
            }
        }

        [Test]
        public void SameSeed_GrowsTheSameTrees()
        {
            Assert.AreEqual(Build(Seed).trees, Build(Seed).trees);
        }
    }
}
