using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using OpenTransmute.Orchestration;

namespace OpenTransmute.Cli.Commands;

/// <summary>
/// <c>fix</c> command — applies the find/replace corrections recorded by a prior verify run
/// to a project's decomposition documents. Operates at the project level against the on-disk
/// <c>Output/Verification/&lt;project&gt;/</c> reports and <c>Output/Decomposition/&lt;project&gt;/</c> docs.
/// </summary>
internal static class FixCommand
{
    /// <summary>Builds the <c>fix</c> command, wiring its options and run action.</summary>
    internal static Command Build(IServiceProvider sp)
    {
        var cmd = new Command("fix", "Apply verification fixes to a project's decomposition documents");

        var projectArg = new Argument<string>("project")
        {
            Description = "Project name (must match the decomposition/verification output directory)"
        };
        var dryRunOpt = new Option<bool>("--dry-run")
        {
            Description = "Preview the fixes that would be applied without modifying any files"
        };

        cmd.Arguments.Add(projectArg);
        cmd.Options.Add(dryRunOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            string project = parseResult.GetValue(projectArg)!;
            bool dryRun    = parseResult.GetValue(dryRunOpt);
            string outputRoot = Directory.GetCurrentDirectory();

            FixService fixService = sp.GetRequiredService<FixService>();

            Console.WriteLine($"{(dryRun ? "Previewing" : "Applying")} fixes: {project}");
            Console.WriteLine();

            FixRunResult result = await fixService.ApplyProjectFixesAsync(project, outputRoot, dryRun, ct);

            if (!result.VerificationDirExists)
            {
                Console.Error.WriteLine(
                    $"Error: No verification output found for '{project}'. Run 'otx verify' first.");
                return 1;
            }

            if (result.TotalFixable == 0)
            {
                Console.WriteLine("No pending fixes found. Nothing to do.");
                return 0;
            }

            // Group the console output by document for readability.
            foreach (IGrouping<string, FixOutcome> docGroup in result.Outcomes
                         .GroupBy(o => o.DocumentName)
                         .OrderBy(g => g.Key))
            {
                Console.WriteLine(docGroup.Key);
                foreach (FixOutcome outcome in docGroup)
                {
                    string marker = outcome.Status switch
                    {
                        FixOutcomeStatus.Applied   => "[fixed]   ",
                        FixOutcomeStatus.Previewed => "[would]   ",
                        _                          => "[skipped] "
                    };

                    Console.WriteLine($"  {marker}({outcome.Severity}) #{outcome.ClaimId} {outcome.Claim}");
                    if (outcome.Status == FixOutcomeStatus.Skipped && outcome.SkipReason is not null)
                        Console.WriteLine($"            {outcome.SkipReason}");
                }
                Console.WriteLine();
            }

            string verb = dryRun ? "would be applied" : "applied";
            Console.WriteLine($"{result.AppliedCount}/{result.TotalFixable} fixes {verb}" +
                (result.SkippedCount > 0 ? $" ({result.SkippedCount} skipped)" : string.Empty) +
                (dryRun ? " — dry run, no files changed." : "."));

            return 0;
        });

        return cmd;
    }
}
