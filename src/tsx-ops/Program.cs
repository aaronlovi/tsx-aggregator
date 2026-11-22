using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tsx_ops.Commands;

namespace tsx_ops;

/// <summary>
/// tsx-ops: operations CLI for tsx-aggregator. Safe to run alongside production: read-only commands use a
/// read-only transaction and nothing here runs migrations. See the "TSX Ops CLI" vault note.
/// </summary>
public static class Program {
    internal static readonly ICommand[] Commands = [
        new SnapshotCommand(),
        new DiffCommand(),
        new ServiceCommand(),
        new EventsCommand(),
        new RequeueCommand(),
    ];

    public static async Task<int> Main(string[] args) {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h") {
            PrintUsage(args.Length == 0 ? Console.Error : Console.Out);
            return args.Length == 0 ? ExitCodes.Usage : ExitCodes.Ok;
        }

        ICommand? command = Commands.FirstOrDefault(c => c.Name == args[0]);
        if (command is null) {
            await Console.Error.WriteLineAsync($"Unknown command '{args[0]}'.");
            PrintUsage(Console.Error);
            return ExitCodes.Usage;
        }

        string[] commandArgs = args[1..];
        if (commandArgs.Any(a => a is "--help" or "-h")) {
            await Console.Out.WriteLineAsync(command.Usage);
            return ExitCodes.Ok;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            cts.Cancel();
        };

        using OpsContext context = OpsContext.Create();
        try {
            return await command.RunAsync(commandArgs, context, cts.Token);
        } catch (UsageException ex) {
            await Console.Error.WriteLineAsync(ex.Message);
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync(command.Usage);
            return ExitCodes.Usage;
        } catch (OperationCanceledException) {
            await Console.Error.WriteLineAsync("Cancelled.");
            return ExitCodes.Error;
        } catch (Exception ex) {
            await Console.Error.WriteLineAsync($"{command.Name} failed: {ex}");
            return ExitCodes.Error;
        }
    }

    private static void PrintUsage(TextWriter writer) {
        writer.WriteLine("Usage: tsx-ops <command> [options]");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        int width = Commands.Max(c => c.Name.Length);
        foreach (ICommand command in Commands)
            writer.WriteLine($"  {command.Name.PadRight(width)}  {command.Description}");
        writer.WriteLine();
        writer.WriteLine("Run 'tsx-ops <command> --help' for a command's options.");
    }
}
