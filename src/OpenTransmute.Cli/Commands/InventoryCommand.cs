using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTransmute.Data;
using OpenTransmute.Models;

namespace OpenTransmute.Cli.Commands;

internal static class InventoryCommand
{
    internal static Command Build(IServiceProvider sp)
    {
        var cmd = new Command("inventory", "Browse inventory items extracted from decomposed projects");

        var searchOpt   = new Option<string?>("--search", "-s") { Description = "Filter by name or summary (contains, case-insensitive)" };
        var categoryOpt = new Option<InventoryCategory?>("--category", "-c") { Description = "Filter by category" };
        var projectOpt  = new Option<string?>("--project", "-p") { Description = "Filter by project name" };
        var verboseOpt  = new Option<bool>("--verbose", "-v") { Description = "Show raw markdown block for each item" };

        cmd.Options.Add(searchOpt);
        cmd.Options.Add(categoryOpt);
        cmd.Options.Add(projectOpt);
        cmd.Options.Add(verboseOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var search   = parseResult.GetValue(searchOpt);
            var category = parseResult.GetValue(categoryOpt);
            var project  = parseResult.GetValue(projectOpt);
            var verbose  = parseResult.GetValue(verboseOpt);

            var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var items = await db.InventoryItems.Include(i => i.Project).ToListAsync(ct);

            if (category.HasValue)
                items = items.Where(i => i.Category == category.Value).ToList();
            if (!string.IsNullOrWhiteSpace(project))
                items = items.Where(i => i.Project.Name.Equals(project, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!string.IsNullOrWhiteSpace(search))
                items = items.Where(i =>
                    i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    i.Summary.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!items.Any())
            {
                Console.WriteLine("No items found.");
                return 0;
            }

            string sep = new string('-', 110);
            Console.WriteLine($"{"ID",-36}  {"Category",-24}  {"Project",-18}  Name");
            Console.WriteLine(sep);

            foreach (var item in items)
            {
                Console.WriteLine($"{item.Id,-36}  {item.Category,-24}  {item.Project.Name,-18}  {item.Name}");

                if (!string.IsNullOrWhiteSpace(item.Summary))
                    Console.WriteLine($"{"",36}  {"",24}  {"",18}  {item.Summary}");

                if (verbose && !string.IsNullOrWhiteSpace(item.RawMarkdown))
                {
                    Console.WriteLine();
                    Console.WriteLine(item.RawMarkdown);
                    Console.WriteLine(sep);
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{items.Count} item(s).");
            return 0;
        });

        return cmd;
    }
}
