using System.Security.Cryptography;
using Mono.Cecil;

namespace Polyfill.Core
{
    /// <summary>
    /// A note Polyfill leaves inside every assembly it writes, saying what it was built from.
    /// </summary>
    /// <remarks>
    /// The question that has to be answerable at the top of every launch is "is this file MelonLoader's
    /// output or ours, and if ours, from which original". Everything else - which backup to read, whether a
    /// kept copy is stale, whether a game update happened - follows from it.
    ///
    /// Putting the answer INSIDE the file is what makes it survive. A stamp beside it can be deleted, a
    /// version string can be missing, a folder can be copied to another machine, a player can swap a file by
    /// hand. The note travels with the thing it describes, and reading it is one Cecil open of a module the
    /// caller was going to open anyway.
    ///
    /// It costs a type with four string constants and no methods: no new assembly reference (string and
    /// object come from the corlib reference every assembly already has), nothing to construct, nothing that
    /// runs. The name carries angle brackets so that no C# compiler can produce it and nothing can bind to
    /// it by accident.
    /// </remarks>
    internal static class Provenance
    {
        internal const string MarkerType = "<Polyfill>Provenance";

        internal sealed class Mark
        {
            /// <summary>SHA-256 of the untouched assembly this was written from.</summary>
            internal string Source;
            /// <summary>The Polyfill version that wrote it.</summary>
            internal string By;
            /// <summary>What the interop assemblies were generated from at the time. See GeneratorIdentity.</summary>
            internal string Generator;
            internal string At;
        }

        /// <summary>Write the note. Called once per assembly, just before the image goes to disk.</summary>
        internal static void Add(ModuleDefinition module, Mark mark)
        {
            if (module.GetType(MarkerType) != null) return;   // rebuilt from a marked source; should not happen

            // Sealed + abstract is a static class: nothing to instantiate, so nothing needs a constructor.
            var type = new TypeDefinition("", MarkerType,
                TypeAttributes.NotPublic | TypeAttributes.Class | TypeAttributes.Sealed
                | TypeAttributes.Abstract | TypeAttributes.BeforeFieldInit,
                module.TypeSystem.Object);

            Constant(module, type, "Source", mark.Source);
            Constant(module, type, "By", mark.By);
            Constant(module, type, "Generator", mark.Generator);
            Constant(module, type, "At", mark.At);

            module.Types.Add(type);
        }

        /// <summary>The note in this module, or null when the file is MelonLoader's own.</summary>
        internal static Mark Read(ModuleDefinition module)
        {
            var type = module?.GetType(MarkerType);
            if (type == null) return null;

            var mark = new Mark();
            foreach (var field in type.Fields)
            {
                string value = field.Constant as string;
                switch (field.Name)
                {
                    case "Source": mark.Source = value; break;
                    case "By": mark.By = value; break;
                    case "Generator": mark.Generator = value; break;
                    case "At": mark.At = value; break;
                }
            }
            return mark;
        }

        /// <summary>The note in the file at this path, or null - including when the file cannot be read at
        /// all, because "we cannot tell" and "it is not ours" lead to the same careful branch.</summary>
        internal static Mark ReadFrom(string path)
        {
            try
            {
                using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true });
                return Read(module);
            }
            catch { return null; }
        }

        /// <summary>Identity of an untouched assembly. Roughly 40 ms for the 13 MB Assembly-CSharp, and only
        /// asked for on the branches that cannot answer without it.</summary>
        internal static string Sha256(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                return Convert.ToHexString(sha.ComputeHash(stream));
            }
            catch { return null; }
        }

        private static void Constant(ModuleDefinition module, TypeDefinition type, string name, string value)
            => type.Fields.Add(new FieldDefinition(name,
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal
                | FieldAttributes.HasDefault,
                module.TypeSystem.String)
            { Constant = value ?? "" });
    }
}
