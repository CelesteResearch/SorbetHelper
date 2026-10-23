using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Runtime;

namespace Celeste.Mods.SorbetHelper.Modules
{
    public sealed class SorbetHelperModule(ISourceEngineContext engine) : SourceEverestModule(engine)
    {
        // These three original entities have no lifecycle hooks. Everest owns their
        // actual factory, tracker and strawberry registration leases.
        public override void Load()
        {
        }

        public override void Unload()
        {
        }
    }
}
