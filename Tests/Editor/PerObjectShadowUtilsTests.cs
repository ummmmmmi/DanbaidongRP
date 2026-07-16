using NUnit.Framework;

namespace UnityEngine.Rendering.Universal.Tests
{
    /// <summary>
    /// 验证 PerObject Shadow Atlas 的 Tile 分辨率边界行为。
    /// </summary>
    class PerObjectShadowUtilsTests
    {
        /// <summary>
        /// 有效 Atlas 应返回可容纳全部 Tile 的最大分辨率。
        /// </summary>
        [TestCase(2048, 2048, 1, 2048)]
        [TestCase(2048, 1024, 2, 1024)]
        [TestCase(4096, 2048, 8, 1024)]
        [TestCase(2048, 2048, 16, 512)]
        [TestCase(4096, 2048, 32, 512)]
        public void GetPerObjectTileResolutionInAtlasReturnsLargestFittingResolution(
            int atlasWidth, int atlasHeight, int tileCount, int expectedResolution)
        {
            int resolution = PerObjectShadowUtils.GetPerObjectTileResolutionInAtlas(
                atlasWidth, atlasHeight, tileCount);

            Assert.AreEqual(expectedResolution, resolution);
        }

        /// <summary>
        /// 无效输入或无法容纳的 Tile 数量应安全返回零。
        /// </summary>
        [TestCase(0, 2048, 1)]
        [TestCase(2048, 0, 1)]
        [TestCase(2048, 2048, 0)]
        [TestCase(1, 1, 2)]
        [TestCase(-1, 2048, 1)]
        public void GetPerObjectTileResolutionInAtlasReturnsZeroForInvalidInput(
            int atlasWidth, int atlasHeight, int tileCount)
        {
            int resolution = PerObjectShadowUtils.GetPerObjectTileResolutionInAtlas(
                atlasWidth, atlasHeight, tileCount);

            Assert.AreEqual(0, resolution);
        }
    }
}
