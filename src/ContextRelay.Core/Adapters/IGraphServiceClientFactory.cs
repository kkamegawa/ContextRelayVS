using ContextRelay.Core.Auth;
using Microsoft.Graph;

namespace ContextRelay.Core.Adapters;

public interface IGraphServiceClientFactory
{
    GraphServiceClient Create(ContextRelayAuthSettings settings, ContextRelayFeatureOptions featureOptions);
}
