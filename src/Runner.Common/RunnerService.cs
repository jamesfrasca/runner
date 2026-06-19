using System;
using System.Threading;
using System.Threading.Tasks;
using GitHub.Runner.Sdk;
using GitHub.Services.Common;
using GitHub.Services.WebApi;
using Sdk.WebApi.WebApi.RawClient;

namespace GitHub.Runner.Common
{

    [AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
    public sealed class ServiceLocatorAttribute : Attribute
    {
        public static readonly string DefaultPropertyName = "Default";

        public Type Default { get; set; }
    }

    public interface IRunnerService
    {
        void Initialize(IHostContext context);
    }

    public abstract class RunnerService
    {
        protected IHostContext HostContext { get; private set; }
        protected Tracing Trace { get; private set; }

        public string TraceName
        {
            get
            {
                return GetType().Name;
            }
        }

        public virtual void Initialize(IHostContext hostContext)
        {
            HostContext = hostContext;
            Trace = HostContext.GetTrace(TraceName);
            Trace.Entering();
        }

        protected async Task<VssConnection> EstablishVssConnection(Uri serverUrl, VssCredentials credentials, TimeSpan timeout)
        {
            Trace.Info($"EstablishVssConnection");
            Trace.Info($"Establish connection with {timeout.TotalSeconds} seconds timeout.");
            var retryHelper = new RetryHelper(Trace, new RetryStrategy
            {
                MaxAttempts = 5,
                GetBackoff = (_, _, _) => TimeSpan.FromMilliseconds(100),
                OnRetry = (context, ex, _) =>
                {
                    Trace.Info($"Catch exception during connect. {context.MaxAttempts - context.AttemptNumber} attempt left.");
                    Trace.Error(ex);
                },
            });

            return await retryHelper.ExecuteAsync(
                operationName: nameof(EstablishVssConnection),
                operation: async () =>
                {
                    var connection = VssUtil.CreateConnection(serverUrl, credentials, timeout: timeout);
                    await connection.ConnectAsync();
                    return connection;
                });
        }

        protected async Task RetryRequest(Func<Task> func,
            CancellationToken cancellationToken,
            int maxAttempts = 5,
            Func<Exception, bool> shouldRetry = null
        )
        {
            async Task<Unit> wrappedFunc()
            {
                await func();
                return Unit.Value;
            }
            await RetryRequest<Unit>(wrappedFunc, cancellationToken, maxAttempts, shouldRetry);
        }

        protected async Task<T> RetryRequest<T>(Func<Task<T>> func,
            CancellationToken cancellationToken,
            int maxAttempts = 5,
            Func<Exception, bool> shouldRetry = null
        )
        {
            var retryHelper = new RetryHelper(Trace, new RetryStrategy
            {
                MaxAttempts = maxAttempts,
                ShouldRetry = shouldRetry,
                GetBackoff = (_, _, _) => BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)),
                OnRetry = (context, ex, backoff) =>
                {
                    Trace.Error("Catch exception during request");
                    Trace.Error(ex);
                    Trace.Warning($"Back off {backoff.TotalSeconds} seconds before next retry. {context.MaxAttempts - context.AttemptNumber} attempt left.");
                },
            });

            return await retryHelper.ExecuteAsync(func, cancellationToken);
        }
    }
}
