using System;
using EverRealm.World;
using NUnit.Framework;
using UnityEngine;

namespace EverRealm.Tests
{
    public class TerrainGeneratorTests
    {
        static WorldSettings CreateSettings(int seed, float blockSize = 1f)
        {
            var settings = ScriptableObject.CreateInstance<WorldSettings>();
            settings.seed = seed;
            settings.blockSize = blockSize;
            settings.islandSize = 128f;
            return settings;
        }

        static bool SameBlocks(WorldData a, WorldData b)
        {
            for (int cz = 0; cz < a.ChunksPerSide; cz++)
            for (int cx = 0; cx < a.ChunksPerSide; cx++)
                if (!a.GetChunk(cx, cz).AsSpan().SequenceEqual(b.GetChunk(cx, cz)))
                    return false;
            return true;
        }

        [Test]
        public void SameSeed_ProducesIdenticalBlocks()
        {
            WorldData first = new TerrainGenerator(CreateSettings(42)).Generate();
            WorldData second = new TerrainGenerator(CreateSettings(42)).Generate();
            Assert.IsTrue(SameBlocks(first, second));
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentBlocks()
        {
            WorldData first = new TerrainGenerator(CreateSettings(42)).Generate();
            WorldData second = new TerrainGenerator(CreateSettings(43)).Generate();
            Assert.IsFalse(SameBlocks(first, second));
        }

        [Test]
        public void HeightIsIndependentOfBlockSize()
        {
            var coarse = new TerrainGenerator(CreateSettings(7, 1f));
            var fine = new TerrainGenerator(CreateSettings(7, 0.5f));
            Assert.AreEqual(coarse.HeightAt(40.3f, 77.9f), fine.HeightAt(40.3f, 77.9f));
        }

        [Test]
        public void IslandEdgeIsBelowSeaLevel()
        {
            WorldSettings settings = CreateSettings(42);
            var generator = new TerrainGenerator(settings);
            Assert.Less(generator.HeightAt(0.5f, 0.5f), settings.seaLevel);
            Assert.Less(generator.HeightAt(settings.islandSize - 0.5f, settings.islandSize * 0.5f), settings.seaLevel);
        }
    }
}
