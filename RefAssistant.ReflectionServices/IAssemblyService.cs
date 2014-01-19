﻿using System.Collections.Generic;
using Lardite.RefAssistant.ReflectionServices.Data;

namespace Lardite.RefAssistant.ReflectionServices
{
    public interface IAssemblyService
    {
        AssemblyInfo GetAssembly(string fileName);

        AssemblyInfo GetAssembly(AssemblyId assemblyId);

        IEnumerable<AssemblyId> GetManifestAssemblies(AssemblyId assemblyId);

        IEnumerable<TypeId> GetDefinedTypes(AssemblyId assemblyId);

        IEnumerable<TypeId> GetReferencedTypes(AssemblyId assemblyId);
    }
}
