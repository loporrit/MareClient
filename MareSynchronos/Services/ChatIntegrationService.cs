using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public sealed class ChatIntegrationService : MediatorSubscriberBase, IDisposable
{
	public bool Activated { get; private set; } = false;

    public ChatIntegrationService(ILogger<ChatIntegrationService> logger, MareMediator mediator) : base(logger, mediator)
    {
    }

	public void Activate()
	{
		if (Activated)
			return;
	}

    public void Dispose()
    {
    }
}