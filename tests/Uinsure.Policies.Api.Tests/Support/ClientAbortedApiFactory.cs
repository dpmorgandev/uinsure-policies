using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services;

namespace Uinsure.Policies.Api.Tests.Support;

// What the server did with one request, seen from outside the app's whole pipeline (after its exception handling).
public sealed record ServedRequest(int Status, string? ContentType, bool Aborted);

// Production over HTTPS, for a sale the client gives up on. The app's IPolicyRepository is wrapped so a lookup
// of an armed reference waits on the request's own token until the client aborts, as a slow database call does
// when the client goes away; the OperationCanceledException then comes from the real controller → service →
// repository path. A middleware ahead of the app's pipeline records the status and content type the server
// ended with for each request carrying the probe header, and a logger provider captures what is logged.
public sealed class ClientAbortedApiFactory : ProductionApiFactory
{
    public const string ProbeHeader = "X-Test-Abort-Probe";

    private readonly ConcurrentDictionary<string, TaskCompletionSource> _reached = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ServedRequest>> _served = new(StringComparer.Ordinal);

    public ClientAbortedApiFactory() => ClientOptions.BaseAddress = TransportClients.HttpsBaseAddress;

    public CapturingLoggerProvider Logs { get; } = new();

    // Arms the next lookup of the reference only. The task completes once that lookup is waiting, so the test
    // aborts mid-request, never before the request reaches the repository.
    public Task ArmLookup(string reference) =>
        _reached.GetOrAdd(reference, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    // Completes when the request with this probe value has left the app's pipeline.
    public Task<ServedRequest> Served(string probe) => ServedSignal(probe).Task;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(Logs);
            services.AddTransient<IStartupFilter>(_ => new ServedRequestRecorder(this));

            var app = services.Single(descriptor => descriptor.ServiceType == typeof(IPolicyRepository));
            var implementation = app.ImplementationType
                ?? throw new InvalidOperationException("The app's IPolicyRepository is expected to be registered by type.");
            services.Remove(app);
            services.Add(new ServiceDescriptor(
                typeof(IPolicyRepository),
                provider => new StallingPolicyRepository((IPolicyRepository)ActivatorUtilities.CreateInstance(provider, implementation), this),
                app.Lifetime));
        });
    }

    private TaskCompletionSource<ServedRequest> ServedSignal(string probe) =>
        _served.GetOrAdd(probe, _ => new TaskCompletionSource<ServedRequest>(TaskCreationOptions.RunContinuationsAsynchronously));

    private sealed class ServedRequestRecorder(ClientAbortedApiFactory factory) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var probe = context.Request.Headers[ProbeHeader].ToString();
                if (probe.Length == 0)
                {
                    await nextMiddleware(context);
                    return;
                }

                try
                {
                    await nextMiddleware(context);
                }
                finally
                {
                    factory.ServedSignal(probe).TrySetResult(new ServedRequest(
                        context.Response.StatusCode,
                        context.Response.ContentType,
                        context.RequestAborted.IsCancellationRequested));
                }
            });
            next(app);
        };
    }

    private sealed class StallingPolicyRepository(IPolicyRepository real, ClientAbortedApiFactory factory) : IPolicyRepository
    {
        public async Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct)
        {
            if (factory._reached.TryRemove(reference.Value, out var reached))
            {
                reached.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }

            return await real.GetByReference(reference, ct);
        }

        public void Add(Policy policy) => real.Add(policy);

        public Task SaveChanges(CancellationToken ct) => real.SaveChanges(ct);
    }
}
