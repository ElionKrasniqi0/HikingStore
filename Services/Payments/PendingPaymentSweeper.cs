namespace HikingStore.Services.Payments
{
    /// <summary>Periodically releases card orders whose customer never completed payment, returning their stock.</summary>
    public class PendingPaymentSweeper : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PendingPaymentSweeper> _logger;

        public PendingPaymentSweeper(IServiceScopeFactory scopeFactory, ILogger<PendingPaymentSweeper> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var payments = scope.ServiceProvider.GetRequiredService<PaymentService>();
                    await payments.ReleaseExpiredOrdersAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Releasing expired card orders failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
