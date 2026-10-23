using System.Numerics;
using Celeste.Core.Reflection.Runtime;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Everest.Tracking.Attributes;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Execution.Processes;
using Celeste.Scene.Tracking;
using Celeste.Simulation.Gameplay.Player.Collision.Components;
using Player = Celeste.Simulation.RoomPlayerSession;
using Level = Celeste.Game.Execution.Levels.OriginalLevelSceneRoot;

namespace Celeste.Mods.SorbetHelper.Entities.Hazards
{
    [CustomEntity("SorbetHelper/FlagToggledKillbox")]
    [EverestTrackedAs(typeof(Celeste.Game.Entities.Hazards.Killboxes.OriginalKillboxEntity))]
    [RuntimeTypeAlias("Celeste.Mod.SorbetHelper.Entities.FlagToggledKillbox")]
    public class FlagToggledKillbox : Celeste.Game.Entities.Hazards.Killboxes.OriginalKillboxEntity
    {
        private readonly string flag;
        private readonly bool inverted;
        private readonly bool flagOnly;

        private readonly float playerAboveThreshold;

        private readonly bool updateOnLoad;

        private readonly ISourceEngineContext engine;

        public FlagToggledKillbox(ISourceEngineContext engine, EntityData data, Vector2 offset) : base(engine, data,
            offset)
        {
            this.engine = engine;
            flag = data.Attr("flag", "");
            inverted = data.Bool("inverted", false);
            flagOnly = data.Bool("flagOnly", false);

            playerAboveThreshold = data.Float("playerAboveThreshold", 32f);

            updateOnLoad = data.Bool("updateOnLoad", false);

            if (data.Bool("lenientHitbox", false))
                Get<PlayerColliderComponentState>().OnCollide = LenientOnPlayer;
        }

        public override void Awake(object scene)
        {
            base.Awake(scene);

            if (updateOnLoad)
                Update();
        }

        public override void Update()
        {
            // flag only mode checks
            if (flagOnly)
            {
                if (string.IsNullOrEmpty(flag))
                    Collidable = inverted;
                else
                    Collidable = (((Level)Scene).Session.GetFlag(flag) != inverted);

                return;
            }

            // normal collidability checks
            Player player =
                ((ISourceScene)Scene).SourceScene.Entities.GetTrackedEntity<Player>(typeof(Player).FullName);

            if (!Collidable && player is not null && player.Bottom < Top - playerAboveThreshold)
                Collidable = true;
            else if (player is not null && player.Top > Bottom + 32f)
                Collidable = false;

            // only keep collidable if the flag is set (or null/empty)
            bool canBeCollidable = string.IsNullOrEmpty(flag) || (((Level)Scene).Session.GetFlag(flag) != inverted);
            Collidable = Collidable && canBeCollidable;
        }

        // based on Level.EnforceBounds
        private void LenientOnPlayer(Player player)
        {
            if (player.Top > Top && ((IOriginalGameplayProcess)engine).SaveContext.CurrentSave.Assists.Invincible)
            {
                player.Play("event:/game/general/assist_screenbottom");
                player.Bounce(Top);
            }
            else if (player.Top > Top + 4f)
            {
                player.Die(Vector2.Zero);
            }
        }
    }
}
