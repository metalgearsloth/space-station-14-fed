using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static Robust.UnitTesting.RobustIntegrationTest;

namespace Content.MapRenderer.Painters
{
    public sealed class TilePainter : IDisposable
    {
        public const int TileImageSize = EyeManager.PixelsPerMeter;

        private readonly ITileDefinitionManager _sTileDefinitionManager;
        private readonly SharedMapSystem _sMapSystem;
        private readonly IResourceManager _resManager;
        private readonly Dictionary<int, Dictionary<string, List<Image[]>>> _tileImageCache = new();

        public TilePainter(ClientIntegrationInstance client, ServerIntegrationInstance server)
        {
            _sTileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
            _resManager = client.ResolveDependency<IResourceManager>();
            var esm = server.ResolveDependency<IEntitySystemManager>();
            _sMapSystem = esm.GetEntitySystem<SharedMapSystem>();
        }

        public void Run(Image gridCanvas, EntityUid gridUid, MapGridComponent grid, Vector2 customOffset = default)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var bounds = grid.LocalAABB;
            var xOffset = -bounds.Left;
            var yOffset = -bounds.Bottom;
            var tileSize = grid.TileSize * TileImageSize;

            var images = GetTileImages(tileSize);
            var i = 0;

            _sMapSystem.GetAllTiles(gridUid, grid).AsParallel().ForAll(tile =>
            {
                var path = _sTileDefinitionManager[tile.Tile.TypeId].Sprite.ToString();

                if (string.IsNullOrWhiteSpace(path))
                    return;

                var x = (int) (tile.X + xOffset + customOffset.X);
                var y = (int) (tile.Y + yOffset + customOffset.Y);
                var image = images[path][tile.Tile.Variant][tile.Tile.RotationMirroring % 8];

                gridCanvas.Mutate(o => o.DrawImage(image, new Point(x * tileSize, y * tileSize), 1));

                Interlocked.Increment(ref i);
            });

            Console.WriteLine($"{nameof(TilePainter)} painted {i} tiles on grid {gridUid} in {(int) stopwatch.Elapsed.TotalMilliseconds} ms");
        }

        public void Dispose()
        {
            foreach (var cache in _tileImageCache.Values)
            {
                foreach (var images in cache.Values)
                {
                    foreach (var variants in images)
                    {
                        foreach (var image in variants)
                        {
                            image.Dispose();
                        }
                    }
                }
            }
        }

        private Dictionary<string, List<Image[]>> GetTileImages(int tileSize)
        {
            if (_tileImageCache.TryGetValue(tileSize, out var cached))
                return cached;

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var images = new Dictionary<string, List<Image[]>>();

            foreach (var definition in _sTileDefinitionManager)
            {
                var path = definition.Sprite.ToString();

                if (string.IsNullOrWhiteSpace(path))
                    continue;

                images[path] = new List<Image[]>(definition.Variants);

                using var stream = _resManager.ContentFileRead(path);
                using Image tileSheet = Image.Load<Rgba32>(stream);

                if (tileSheet.Width != tileSize * definition.Variants || tileSheet.Height != tileSize)
                {
                    throw new NotSupportedException($"Unable to use tiles with a dimension other than {tileSize}x{tileSize}.");
                }

                for (var i = 0; i < definition.Variants; i++)
                {
                    var index = i;
                    using var tileImage = tileSheet.Clone(o => o.Crop(new Rectangle(tileSize * index, 0, tileSize, tileSize)).Flip(FlipMode.Vertical));
                    images[path].Add(CreateTileVariants(tileImage));
                }
            }

            Console.WriteLine($"Indexed all tile images in {(int) stopwatch.Elapsed.TotalMilliseconds} ms");

            _tileImageCache.Add(tileSize, images);
            return images;
        }

        private static Image[] CreateTileVariants(Image source)
        {
            var variants = new Image[8];
            variants[0] = source.CloneAs<Rgba32>();
            variants[1] = source.Clone(o => o.Rotate(90f));
            variants[2] = source.Clone(o => o.Rotate(180f));
            variants[3] = source.Clone(o => o.Rotate(270f));
            variants[4] = source.Clone(o => o.Flip(FlipMode.Horizontal));
            variants[5] = source.Clone(o => o.Rotate(90f).Flip(FlipMode.Horizontal));
            variants[6] = source.Clone(o => o.Rotate(180f).Flip(FlipMode.Horizontal));
            variants[7] = source.Clone(o => o.Rotate(270f).Flip(FlipMode.Horizontal));

            return variants;
        }
    }
}
