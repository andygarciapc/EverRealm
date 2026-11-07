using EverRealm.World;
using NUnit.Framework;
using UnityEngine;

namespace EverRealm.Tests
{
    public class SettlementBuilderTests
    {
        const int Seed = 12345; // The shipped WorldSettings seed.

        static WorldSettings CreateSettings(int seed, float blockSize)
        {
            var settings = ScriptableObject.CreateInstance<WorldSettings>();
            settings.seed = seed;
            settings.blockSize = blockSize;
            return settings;
        }

        static (WorldData world, Settlement settlement) Build(int seed, float blockSize)
        {
            WorldSettings settings = CreateSettings(seed, blockSize);
            WorldData world = new TerrainGenerator(settings).Generate();
            return (world, SettlementBuilder.Build(world, settings)[0]);
        }

        static BlockType BlockAt(WorldData world, Vector3 position, float heightAboveFloor) =>
            world.GetBlock(Mathf.FloorToInt(position.x / world.BlockSize),
                           Mathf.FloorToInt((position.y + heightAboveFloor) / world.BlockSize),
                           Mathf.FloorToInt(position.z / world.BlockSize));

        [Test]
        public void SameSeed_ProducesSameHamlet()
        {
            Settlement first = Build(Seed, 0.7f).settlement;
            Settlement second = Build(Seed, 0.7f).settlement;
            Assert.IsNotNull(first);
            Assert.AreEqual(first.Square, second.Square);
            Assert.AreEqual(first.Houses.Count, second.Houses.Count);
            for (int i = 0; i < first.Houses.Count; i++)
                Assert.AreEqual(first.Houses[i].Door, second.Houses[i].Door);
        }

        [Test]
        public void HutsAreWalkable()
        {
            (WorldData world, Settlement settlement) = Build(Seed, 0.7f);
            Assert.IsNotNull(settlement, "no hamlet site found");
            Assert.GreaterOrEqual(settlement.Houses.Count, 3);

            foreach (House house in settlement.Houses)
            {
                Assert.IsFalse(house.Contains(house.Door), "door point should be outside");
                Assert.IsTrue(house.Contains(house.Inside), "inside point should be inside");
                Assert.IsTrue(house.Contains(house.Bed), "bed should be inside");
                Assert.AreNotEqual(BlockType.Air, BlockAt(world, house.Door, -0.1f), "ground under the door point");
                Assert.AreEqual(BlockType.Planks, BlockAt(world, house.Inside, -0.1f), "floor inside");

                // The tallest planned human (2.1m, controller reaching ~2.3m) fits through without catching the lintel.
                foreach (Vector3 point in new[] { house.Door, Vector3.Lerp(house.Door, house.Inside, 0.5f), house.Inside, house.Bed })
                for (float h = 0.1f; h <= 2.35f; h += 0.25f)
                    Assert.AreEqual(BlockType.Air, BlockAt(world, point, h), $"blocked at {point} + {h}m");
            }
        }

        [Test]
        public void DoorwaysAreFourBlocksTall()
        {
            (WorldData world, Settlement settlement) = Build(Seed, 0.7f);
            foreach (House house in settlement.Houses)
            {
                // Halfway between the door and inside points is in the wall's plane.
                Vector3 doorway = Vector3.Lerp(house.Door, house.Inside, 0.5f);
                for (int block = 0; block < 4; block++)
                    Assert.AreEqual(BlockType.Air, BlockAt(world, doorway, (block + 0.5f) * 0.7f), $"doorway block {block}");
                Assert.AreEqual(BlockType.Wood, BlockAt(world, doorway, 4.5f * 0.7f), "lintel");
            }
        }

        [Test]
        public void SquareReachesEveryDoorAndWorkSpot()
        {
            Settlement settlement = Build(Seed, 0.7f).settlement;
            foreach (House house in settlement.Houses)
                Assert.IsTrue(settlement.IsPathClear(settlement.Square, house.Door, 0.45f));
            foreach (Vector3 spot in settlement.WorkSpots)
                Assert.IsTrue(settlement.IsPathClear(settlement.Square, spot, 0.45f));
        }

        [Test]
        public void PathThroughAHouseIsNotClear()
        {
            Settlement settlement = Build(Seed, 0.7f).settlement;
            Rect footprint = settlement.Houses[0].Footprint;
            var west = new Vector3(footprint.xMin - 2f, 0f, footprint.center.y);
            var east = new Vector3(footprint.xMax + 2f, 0f, footprint.center.y);
            Assert.IsFalse(settlement.IsPathClear(west, east, 0.45f));
        }

        [Test]
        public void FromBehindAHouse_ADetourCornerLeadsRoundIt()
        {
            Settlement settlement = Build(Seed, 0.7f).settlement;
            House house = settlement.Houses[0];
            // Directly behind the house, on the far side from its door.
            Vector3 throughCentre = new Vector3(house.Footprint.center.x, house.Door.y, house.Footprint.center.y) - house.Door;
            Vector3 behind = house.Door + throughCentre * 2.6f;
            Assert.IsFalse(settlement.IsPathClear(behind, house.Door, 0.45f));

            // Following detours corner to corner reaches a point that sees the door within a few hops.
            Vector3 position = behind;
            for (int hop = 0; hop < 3 && !settlement.IsPathClear(position, house.Door, 0.45f); hop++)
            {
                Assert.IsTrue(settlement.TryDetour(position, house.Door, 0.45f, out Vector3 corner), $"no detour on hop {hop}");
                Assert.IsTrue(settlement.IsPathClear(position, corner, 0.45f));
                position = corner;
            }
            Assert.IsTrue(settlement.IsPathClear(position, house.Door, 0.45f), "never got round the house");
        }
    }
}
