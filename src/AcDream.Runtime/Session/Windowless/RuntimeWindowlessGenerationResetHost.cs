using AcDream.Runtime;
using AcDream.Runtime.Entities;

namespace AcDream.Runtime.Session.Windowless;

internal sealed class RuntimeWindowlessGenerationResetHost
    : IRuntimeGenerationResetHost
{
    public void RetireEntityProjection(RuntimeEntityRecord entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
    }

    public void DrainEntityProjectionBoundary()
    {
    }

    public void CompleteEntityProjectionRetirement()
    {
    }
}
