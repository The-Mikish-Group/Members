using Members.Data;
using Members.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Members.Data
{
    public static class ColorVarSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext context, string cssFilePath)
        {
            if (context.ColorVars.Any())
            {
                return; // DB has been seeded
            }

            var cssContent = await System.IO.File.ReadAllTextAsync(cssFilePath);
            var regex = new Regex(@"--(?<name>[\w-]+)\s*,\s*(?<value>#[0-9a-fA-F]{3,6})\)");
            var matches = regex.Matches(cssContent);

            foreach (Match match in matches)
            {
                var name = match.Groups["name"].Value;
                var value = match.Groups["value"].Value;

                if (!context.ColorVars.Any(c => c.Name == name))
                {
                    context.ColorVars.Add(new ColorVar { Name = name, Value = value });
                }
            }

            await context.SaveChangesAsync();
        }
    }
}
