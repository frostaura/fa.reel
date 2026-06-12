namespace FrostAura.Reel.Api;

/// <summary>
/// Dev convenience (foresight pattern): loads KEY=VALUE pairs from a .env at the repo root
/// into process env vars before configuration builds. Never overrides already-set variables,
/// so it is a no-op inside containers where compose injects the real environment.
/// </summary>
public static class DotEnv
{
    public static void Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".env")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(Path.Combine(dir.FullName, ".env")))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
