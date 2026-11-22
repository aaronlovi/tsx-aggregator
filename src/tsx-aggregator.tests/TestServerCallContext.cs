using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace tsx_aggregator.tests;

/// <summary>A minimal gRPC call context for calling a service handler directly: only the cancellation token matters.</summary>
internal sealed class TestServerCallContext(CancellationToken ct) : ServerCallContext {
    public TestServerCallContext() : this(CancellationToken.None) { }

    protected override string MethodCore => "test";
    protected override string HostCore => "localhost";
    protected override string PeerCore => "test";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => [];
    protected override CancellationToken CancellationTokenCore => ct;
    protected override Metadata ResponseTrailersCore => [];
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new(null, []);
    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
