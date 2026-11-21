using System.Threading;
using System.Threading.Tasks;

namespace tsx_ops.Commands;

/// <summary>A tsx-ops subcommand. See the "TSX Ops CLI" vault note for the conventions commands follow.</summary>
internal interface ICommand {
    string Name { get; }

    /// <summary>One line, shown in the command list.</summary>
    string Description { get; }

    /// <summary>Full usage text, shown for --help and on bad arguments.</summary>
    string Usage { get; }

    /// <returns>An exit code from <see cref="ExitCodes"/>.</returns>
    /// <exception cref="UsageException">Bad arguments.</exception>
    Task<int> RunAsync(string[] args, OpsContext context, CancellationToken ct);
}
