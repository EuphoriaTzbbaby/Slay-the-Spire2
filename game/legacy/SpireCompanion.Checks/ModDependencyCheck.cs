using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class ModDependencyCheck
{
    public static void Run(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var dependencies = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToArray();
        if (dependencies.Contains("SpireCompanion.Core"))
            throw new InvalidOperationException("Mod still depends on SpireCompanion.Core before its initializer can run.");
        var types = metadata.TypeDefinitions.Select(h => metadata.GetString(metadata.GetTypeDefinition(h).Name)).ToHashSet();
        foreach (var required in new[] { "ActionGate", "ChatClient", "ChatConversation", "SolverContract", "GameSnapshot", "EncryptedProfileStore" })
            if (!types.Contains(required)) throw new InvalidOperationException($"Core type not embedded in Mod: {required}");
        Console.WriteLine("PASS Mod contains tested core types and has no external SpireCompanion.Core assembly reference.");
    }
}
