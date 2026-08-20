using System;
using System.IO;

namespace SquadDash;

internal sealed class SystemEnvironmentProvider : IEnvironmentProvider
{
    public string? GetEnvironmentVariable(string name, EnvironmentVariableTarget target)
    {
        return Environment.GetEnvironmentVariable(name, target);
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }
}
