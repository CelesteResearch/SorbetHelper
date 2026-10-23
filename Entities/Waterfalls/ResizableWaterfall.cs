using System;
using System.Collections.Generic;
using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Mathematics;
using Celeste.Core.Randomness;
using Celeste.Core.Reflection.Runtime;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Execution.Processes;
using Celeste.Game.Execution.Startup;
using Celeste.Game.Gameplay.Environment.Water;
using Celeste.Graphics.Geometry.TwoD.Camera;
using Celeste.Graphics.Rendering;
using Celeste.Scene.Entities;
using Celeste.Scene.Tracking;
using Celeste.Simulation.Presentation.Particles;
using Celeste.Simulation.Presentation.PostProcessing.Displacement;
using Level = Celeste.Game.Execution.Levels.OriginalLevelSceneRoot;
using Water = Celeste.Game.Gameplay.Environment.Water.OriginalWaterState;
using Solid = Celeste.Simulation.Gameplay.Platforms.Foundation.SourceSolidEntityState;
using Color = Celeste.Core.Display.PackedColor;
using Rectangle = Celeste.Core.Geometry.IntRect;
using SoundSource = Celeste.Simulation.Audio.Sources.SoundSourceComponentState;

namespace Celeste.Mods.SorbetHelper.Entities.Waterfalls
{
// grr evil backwards compat
    [CustomEntity("SorbetHelper/BigWaterfall")]
    [RuntimeTypeAlias("Celeste.Mod.SorbetHelper.Entities.ResizableWaterfall")]
    public class ResizableWaterfall : SceneEntityState, ISourceEngineContextEntity
    {
        private enum SplashParticleDepths
        {
            ParticlesBG,
            Particles,
            ParticlesFG,
            None
        }

        private float width;
        private float height;

        private readonly bool ignoreSolids;
        private readonly bool ignoreWater;
        private readonly float wavePercent;
        private readonly bool rippleWater;

        private readonly Color baseColor;
        private readonly Color surfaceColor;
        private readonly Color fillColor;
        private readonly SplashParticleDepths splashParticleDepth;

        private Water water, topWater;
        private Solid solid;
        private readonly List<float> lines = [];
        private SoundSource loopingSfx;
        private SoundSource enteringSfx;

        private bool visibleOnCamera;

        private readonly ISourceEngineContext engine;
        ISourceEngineContext ISourceEngineContextEntity.EngineContext => engine;

        public ResizableWaterfall(ISourceEngineContext engine, EntityData data, Vector2 offset) : base(
            ((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration, data.Position + offset)
        {
            this.engine = engine;
            Tags = ((IOriginalGameTagsEnvironment)engine).Tags.TransitionUpdate.Value;

            width = data.Width;

            float alpha = data.Float("alpha", 1f);
            baseColor = Celeste.Graphics.Display.Colors.SourceHexColor.HexToColor(data.Attr("color", "87CEFA")) * alpha;
            surfaceColor = baseColor * 0.8f;
            fillColor = baseColor * 0.3f;

            Depth = data.Int("depth", -49900);
            splashParticleDepth = data.Enum("splashParticleDepth", SplashParticleDepths.ParticlesFG);

            ignoreSolids = data.Bool("ignoreSolids", false);
            ignoreWater = data.Bool("ignoreWater", false);
            wavePercent = data.Float("wavePercent", 1f);
            rippleWater = data.Bool("rippleWater", true);

            bool hasLines = data.Bool("lines", true);
            if (!hasLines)
                return;

            int edgeSize = width <= 8f ? 2 : 3;
            lines.Add(edgeSize);
            lines.Add(width - 1f - edgeSize);

            if (width > 16f)
            {
                int lineCount = ((ISourceRandomEnvironment)engine).Random.Next((int)(width / 16f));
                for (int i = 0; i < lineCount; i++)
                    lines.Add(8f + SourceRandomMath.NextFloat(((ISourceRandomEnvironment)engine).Random, width - 16f));
            }
        }

        public override void Awake(object scene)
        {
            base.Awake(scene);
            Level level = ((Level)Scene);

            height = 8f;
            while (Y + height < level.Bounds.Bottom
                   && (ignoreWater || (water = SourceSceneCollision.FirstRect<Water>(
                       ((ISourceScene)Scene).SourceScene.Entities, "Celeste.Water",
                       new Rectangle((int)X, (int)(Y + height), 8, 8))) is null)
                   && (ignoreSolids ||
                       (solid = SourceSceneCollision.FirstRect<Solid>(((ISourceScene)Scene).SourceScene.Entities,
                           "Celeste.Solid", new Rectangle((int)X, (int)(Y + height), 8, 8))) is null ||
                       !solid.BlockWaterfalls))
            {
                height += 8f;
                solid = null;
            }

            if (water is not null)
                height = Math.Max(water.Top - Y, 0f);

            if (!ignoreWater &&
                SourceSceneCollision.FirstRect<Water>(((ISourceScene)Scene).SourceScene.Entities, "Celeste.Water",
                    new Rectangle((int)X, (int)(Y - 1), 8, 1)) is { BottomSurface: not null } waterAbove)
                topWater = waterAbove;

            Add(loopingSfx = ((IOriginalWaterfallSoundEnvironment)engine).SoundSources.Create());
            loopingSfx.Play(width <= 24
                ? "event:/env/local/waterfall_small_main"
                : "event:/env/local/waterfall_big_main");
            loopingSfx.Position.X = width / 2f;
            Add(enteringSfx = ((IOriginalWaterfallSoundEnvironment)engine).SoundSources.Create());
            enteringSfx.Play(
                (water is not null && !SourceSceneCollision.CheckRect(((ISourceScene)Scene).SourceScene.Entities,
                    "Celeste.Solid", new Rectangle((int)X, (int)(Y + height), 8, 16)))
                    ? "event:/env/local/waterfall_small_in_deep"
                    : "event:/env/local/waterfall_small_in_shallow");
            enteringSfx.Position.X = width / 2f;
            enteringSfx.Position.Y = height;

            Add(new DisplacementRenderHookState(RenderDisplacement));
        }

        public override void Update()
        {
            base.Update();

            Level level = ((Level)Scene);

            loopingSfx?.Position.Y =
                ScalarMath.Clamp((level.Camera.Position.Y + level.Camera.Viewport.Height / 2f), Y, height);

            Visible = visibleOnCamera = InView(level.Camera);
            if (!Visible)
                return;

            Water.Surface bottomWaterSurface = water is { Active: true } ? water.TopSurface : null;
            Water.Surface topWaterSurface = topWater is { Active: true } ? topWater.BottomSurface : null;
            if (rippleWater && ((ISourceScene)Scene).SourceScene.Clock.OnInterval(0.3f, engine) &&
                (bottomWaterSurface is not null || topWaterSurface is not null))
            {
                int rippleCount = (int)MathF.Ceiling(width / 40f);
                float rippleDistance = width / rippleCount;
                float rippleStrengthMult = MathF.Pow(0.85f, MathF.Max(width / 40f - 1f, 0f)); // hmm
                for (int i = 0; i < rippleCount; i++)
                {
                    float x = rippleDistance / 2f + rippleDistance * i;
                    bottomWaterSurface?.DoRipple(new Vector2(X + x, water.Top), 0.75f * rippleStrengthMult);
                    topWaterSurface?.DoRipple(new Vector2(X + x, topWater.Bottom), 0.5f * rippleStrengthMult);
                }
            }

            if (splashParticleDepth != SplashParticleDepths.None
                && !level.Transitioning
                && (solid is not null || water is not null || topWater is not null))
            {
                ParticleSystemPresentation particles = splashParticleDepth switch
                {
                    SplashParticleDepths.ParticlesBG => level.ParticlesBG,
                    SplashParticleDepths.Particles => level.Particles,
                    SplashParticleDepths.ParticlesFG => level.ParticlesFG,
                    _ => throw new ArgumentOutOfRangeException()
                };

                if (((ISourceScene)level).SourceScene.Clock.OnInterval(1f / 60f, engine) &&
                    (solid is not null || water is not null))
                    particles.Emit(((IOriginalGameplayProcess)engine).ParticleTypes.Water.P_Splash, 1,
                        new Vector2(X + width / 2f, Y + height + 2f), new Vector2(width / 2f + 4f, 2f), baseColor,
                        -MathF.PI / 2f);

                if (((ISourceScene)level).SourceScene.Clock.OnInterval(0.15f, engine) && topWater is not null)
                    particles.Emit(((IOriginalGameplayProcess)engine).ParticleTypes.Water.P_Splash, 1,
                        new Vector2(X + width / 2f, Y), new Vector2(width / 2f + 4f, 2f), baseColor,
                        SourceRandomMath.Range(((ISourceRandomEnvironment)engine).Random, 0, MathF.PI));
            }
        }

        public void RenderDisplacement()
        {
            if (!visibleOnCamera)
                return;

            Color waveColor = new Color(0.5f, 0.5f, wavePercent, 1f);

            Water.Surface bottomWaterSurface = water?.TopSurface;
            Water.Surface topWaterSurface = topWater?.BottomSurface;
            if (bottomWaterSurface is null && topWaterSurface is null)
            {
                SourcePrimitiveDrawing.Rect(((Celeste.Core.Rendering.ISourceRenderEnvironment)engine).CurrentContext,
                    this, X, Y, width, height, waveColor);
                return;
            }

            float renderY = Y;
            float renderHeight = height;

            if (bottomWaterSurface is not null)
                renderHeight += bottomWaterSurface.Position.Y - water.Top;
            if (topWaterSurface is not null)
            {
                renderY += topWaterSurface.Position.Y - topWater.Bottom;
                renderHeight -= topWaterSurface.Position.Y - topWater.Bottom;
            }

            for (int i = 0; i < width; i++)
            {
                float sliceX = X + i;
                float bottomSurfaceHeight = bottomWaterSurface is not null
                    ? MathF.Round(bottomWaterSurface.GetSurfaceHeight(new Vector2(sliceX, water.Top)))
                    : 0f;
                float topSurfaceHeight = topWaterSurface is not null
                    ? MathF.Round(topWaterSurface.GetSurfaceHeight(new Vector2(sliceX, topWater.Bottom)))
                    : 0f;
                SourcePrimitiveDrawing.Rect(((Celeste.Core.Rendering.ISourceRenderEnvironment)engine).CurrentContext,
                    this, sliceX, renderY + topSurfaceHeight, 1f, renderHeight - bottomSurfaceHeight - topSurfaceHeight,
                    waveColor);
            }
        }

        public override void Render(Celeste.Core.Rendering.ISourceRenderContext context)
        {
            if (!visibleOnCamera)
                return;

            int reduceEdges = width <= 8f ? 1 : 0;
            int edgeSize = 3 - reduceEdges;

            Water.Surface bottomWaterSurface = water?.TopSurface;
            Water.Surface topWaterSurface = topWater?.BottomSurface;
            if (bottomWaterSurface is null && topWaterSurface is null)
            {
                SourcePrimitiveDrawing.Rect(context, this, X + reduceEdges, Y, width - reduceEdges, height, fillColor);
                SourcePrimitiveDrawing.Rect(context, this, X - 1f, Y, edgeSize, height, surfaceColor);
                SourcePrimitiveDrawing.Rect(context, this, X + width - (edgeSize - 1), Y, edgeSize, height,
                    surfaceColor);

                foreach (float line in lines)
                    SourcePrimitiveDrawing.Rect(context, this, X + line, Y, 1f, height, surfaceColor);

                return;
            }

            float renderY = Y;
            float renderHeight = height;

            if (bottomWaterSurface is not null)
                renderHeight += bottomWaterSurface.Position.Y - water.Top;
            if (topWaterSurface is not null)
            {
                renderY += topWaterSurface.Position.Y - topWater.Bottom;
                renderHeight -= topWaterSurface.Position.Y - topWater.Bottom;
            }

            for (int i = reduceEdges; i < width - reduceEdges; i++)
                DrawWaterfallSlice(X + i, fillColor);

            for (int i = 0; i < edgeSize; i++)
            {
                DrawWaterfallSlice(X - 1f + i, surfaceColor);
                DrawWaterfallSlice(X + width - i, surfaceColor);
            }

            foreach (float line in lines)
                DrawWaterfallSlice(X + line, surfaceColor);

            return;

            void DrawWaterfallSlice(float x, Color color)
            {
                float bottomSurfaceHeight = bottomWaterSurface is not null
                    ? MathF.Round(bottomWaterSurface.GetSurfaceHeight(new Vector2(x, water!.Top)))
                    : 0f;
                float topSurfaceHeight = topWaterSurface is not null
                    ? MathF.Round(topWaterSurface.GetSurfaceHeight(new Vector2(x, topWater!.Bottom)))
                    : 0f;
                SourcePrimitiveDrawing.Rect(context, this, x, renderY + topSurfaceHeight, 1f,
                    renderHeight - bottomSurfaceHeight - topSurfaceHeight, color);
            }
        }

        private bool InView(CameraState camera) => X < camera.Right + 24f && X + width > camera.Left - 24f
                                                                          && Y < camera.Bottom + 24f &&
                                                                          Y + height > camera.Top - 24f;
    }
}
