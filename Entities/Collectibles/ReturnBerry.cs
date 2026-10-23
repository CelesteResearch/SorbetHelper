using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Celeste.Core.Maps;
using Celeste.Core.Reflection.Runtime;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Gameplay.Collectibles.Strawberries;
using Celeste.Scene.Tracking;
using Celeste.Simulation.Gameplay.Player.Collision.Components;
using Player = Celeste.Simulation.RoomPlayerSession;
using Level = Celeste.Game.Execution.Levels.OriginalLevelSceneRoot;

namespace Celeste.Mods.SorbetHelper.Entities.Collectibles
{
    [CustomEntity("SorbetHelper/ReturnBerry")]
    [RegisterStrawberry(true, false)]
    [RuntimeTypeAlias("Celeste.Mod.SorbetHelper.Entities.ReturnBerry")]
    public class ReturnBerry : OriginalStrawberryState
    {
        private static readonly FieldInfo GhostBerryField =
            typeof(OriginalStrawberryState).GetField("isGhostBerry", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(OriginalStrawberryState).FullName, "isGhostBerry");

        private readonly Vector2[] nodes;
        private readonly float bubbleDelay;
        private readonly bool bubbleParticles;

        private readonly IOriginalStrawberryProcess engine;

        public ReturnBerry(ISourceEngineContext engine, EntityData data, Vector2 offset, MapEntityId id) : base(data,
            offset, id, (IOriginalStrawberryProcess)engine)
        {
            this.engine = (IOriginalStrawberryProcess)engine;
            nodes = data.Nodes == null ? null : Array.ConvertAll(data.Nodes, node => node + offset);
            bubbleDelay = data.Float("delay", 0.3f);
            bubbleParticles = data.Bool("bubbleParticles", false);

            Remove(Get<PlayerColliderComponentState>());
            Add(new PlayerColliderComponentState(OnPlayer));

            // recreate the strawberry seed list but accounting for the first 2 nodes being for the bubble
            (Seeds ??= new List<OriginalStrawberrySeedState>()).Clear();
            if (data.Nodes is { Length: > 2 })
            {
                bool isGhostBerry = (bool)GhostBerryField.GetValue(this);
                for (int i = 0; i < data.Nodes.Length - 2; i++)
                    Seeds.Add(new OriginalStrawberrySeedState(this, offset + data.Nodes[i + 2], i, isGhostBerry,
                        this.engine));
            }
        }

        public override void Update()
        {
            base.Update();

            if (bubbleParticles
                && !Follower.HasLeader && !Collected && !WaitingOnSeeds && Visible &&
                !CollideCheck("Celeste.FakeWall") && !CollideCheck("Celeste.Solid")
                && ((ISourceScene)Scene).SourceScene.Clock.OnInterval(0.75f, ID.ID / 9f, engine))
            {
                ((Level)Scene).Particles.Emit(engine.ParticleTypes.Player.P_CassetteFly, 2,
                    Center + new Vector2(0f, 1f), new Vector2(4f, 5f));
            }
        }

        private new void OnPlayer(Player player)
        {
            // not an override method but still need to call "base" because the original playercollider got removed
            base.OnPlayer(player);

            if (nodes is { Length: >= 2 })
            {
                Add(new SimulationCoroutine(engine, NodeRoutine(player)));
                Collidable = false;
            }
        }

        private IEnumerator NodeRoutine(Player player)
        {
            if (bubbleDelay > 0f)
                yield return bubbleDelay;

            // if maddy is still alive put her in a bubble
            if (!player.Dead && player.Machine.State != 21)
            {
                ((Celeste.Core.Audio.ISourceSpatialAudioEnvironment)engine).SpatialAudio.Play(
                    "event:/game/general/cassette_bubblereturn",
                    ((Level)Scene).Camera.Position + new Vector2(((Level)Scene).Camera.Viewport.Width / 2f,
                        ((Level)Scene).Camera.Viewport.Height / 2f));
                player.StartCassetteFly(nodes[1], nodes[0]);
            }
        }
    }
}
