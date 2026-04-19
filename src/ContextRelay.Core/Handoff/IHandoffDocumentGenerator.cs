using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Handoff;

public interface IHandoffDocumentGenerator
{
    string GeneratePlan(HandoffContext context);

    string GenerateTasks();

    string GenerateTestPlan();

    string GenerateHandoff(HandoffContext context);

    Task<HandoffGenerationResult> GenerateAsync(
        HandoffContext context,
        HandoffGenerationOptions? options = null,
        CancellationToken cancellationToken = default);
}
