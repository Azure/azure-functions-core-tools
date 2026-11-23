using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FunctionNamespace;

public sealed class TimerFunction(ILogger<TimerFunction> logger)
{
    private readonly ILogger<TimerFunction> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    [Function(nameof(TimerFunction))]
    public void Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer)
    {
        _logger.LogInformation("Timer triggered. Next occurrence: {NextOccurrence}", timer.ScheduleStatus?.Next);
    }
}
