﻿//
// Copyright © 2011 Lardite.
//
// Author: Belikov Sergey (sbelikov@lardite.com),
//         Chistov Victor (vchistov@lardite.com)
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EnvDTE;
using VSLangProj80;

using Mono.Cecil;

using Lardite.RefAssistant.ObjectModel;

namespace Lardite.RefAssistant.VsProxy.Projects
{
    /// <summary>
    /// Base features to wrap visual studio project.
    /// </summary>
    internal class BaseProjectWrapper
    {
        #region Constants

        protected const string OutputPath = "OutputPath";
        protected const string LocalPath = "LocalPath";
        protected const string OutputFileName = "OutputFileName";

        #endregion // Constants

        #region Fields

        private Project _vsProject;
        private readonly PublicKeyTokenConverter _tokenConverter;

        #endregion // Fields

        #region .ctor

        /// <summary>
        /// Initialize a new instance of the <see cref="BaseProjectWrapper"/> class.
        /// </summary>
        /// <param name="vsProject">The Visual Studio project.</param>
        public BaseProjectWrapper(Project vsProject)
        {
            if (vsProject == null)
            {
                throw Error.ArgumentNull("vsProject");
            }

            _vsProject = vsProject;
            _tokenConverter = new PublicKeyTokenConverter();
        }

        #endregion // .ctor

        #region Properties

        /// <summary>
        /// Get wrapped project.
        /// </summary>
        protected Project Project
        {
            get { return _vsProject; }
        }

        /// <summary>
        /// Get project kind.
        /// </summary>
        public Guid Kind
        {
            get 
            {
                Guid kind;
                Guid.TryParse(Project.Kind, out kind);
                return kind;
            }
        }
        
        /// <summary>
        /// Check wherether project is building currently.
        /// </summary>
        public bool IsBuildInProgress
        {
            get { return DTEHelper.IsBuildInProgress(Project.DTE); }
        }

        /// <summary>
        /// Has this project assembly.
        /// </summary>
        public bool HasAssembly
        {
            get
            {
                try
                {
                    return AssemblyDefinition != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Get AssemblyDefinition of project.
        /// </summary>
        private AssemblyDefinition AssemblyDefinition
        {
            get
            {
                return AssemblyDefinition
                    .ReadAssembly(GetOutputAssemblyPath(), new ReaderParameters(ReadingMode.Deferred));
            }
        }

        #endregion // Properties

        #region Virtual methods

        /// <summary>
        /// Get output assembly path.
        /// </summary>
        /// <returns>Returns full path.</returns>
        public virtual string GetOutputAssemblyPath()
        {
            string outputPath = Project.ConfigurationManager.ActiveConfiguration.Properties.Item(OutputPath).Value.ToString();
            string buildPath = Project.Properties.Item(LocalPath).Value.ToString();
            string targetName = Project.Properties.Item(OutputFileName).Value.ToString();
            return Path.Combine(buildPath, Path.Combine(outputPath, targetName));
        }

        /// <summary>
        /// Removes unused usings from project classes.
        /// </summary>
        /// <param name="serviceProvider">Service provider.</param>
        public virtual void RemoveUnusedUsings(IServiceProvider serviceProvider)
        {
        }

        /// <summary>
        /// Removes unused references.
        /// </summary>
        /// <param name="unusedProjectReferences">Unused project references.</param>
        /// <returns>Removed references count.</returns>
        public virtual IEnumerable<ProjectReference> RemoveUnusedReferences(IEnumerable<ProjectReference> unusedProjectReferences)
        {
            var projectReferences = ((VSProject2)Project.Object).References.Cast<Reference3>();
            var unusedReferences = from p in projectReferences
                                   join up in unusedProjectReferences on p.Name equals up.Name
                                   select new { Reference = p, ProjectReference = up };

            var unusedReferencesList = unusedReferences.Select(r => r.ProjectReference).ToList();
            foreach (var unusedReference in unusedReferences)
            {
                unusedReference.Reference.Remove();
            }

            return unusedReferencesList;
        }

        /// <summary>
        /// Get list of project references.
        /// </summary>
        /// <returns>Returns project references.</returns>
        public virtual IEnumerable<ProjectReference> GetProjectReferences()
        {
            var references = new List<ProjectReference>();
            var projectReferences = ((VSProject2)Project.Object).References;

            foreach (Reference3 projectReference in projectReferences)
            {
                var reference = CreateProjectReference(projectReference);
                if (reference != null)
                {
                    references.Add(reference);
                }
            }

            return references;
        }

        #endregion // Virtual methods

        #region Public methods

        /// <summary>
        /// Builds the project.
        /// </summary>
        /// <returns>Returns zero (0) if there are no errors.</returns>
        public int Build()
        {
            return DTEHelper.BuildProject(Project);
        }

        /// <summary>
        /// Get output assembly path.
        /// </summary>
        /// <param name="outputPath">The full path.</param>
        /// <returns>Returns true if error occurs, otherwise false.</returns>
        public bool TryGetOutputAssemblyPath(out string outputPath)
        {
            outputPath = string.Empty;
            try
            {
                outputPath = GetOutputAssemblyPath();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get project information.
        /// </summary>
        public ProjectInfo GetProjectInfo()
        {
            return new ProjectInfo
            {
                Name = Project.Name,
                Type = Kind,
                AssemblyPath = GetOutputAssemblyPath(),
                ConfigurationName = Project.ConfigurationManager.ActiveConfiguration.ConfigurationName,
                PlatformName = Project.ConfigurationManager.ActiveConfiguration.PlatformName,
                References = GetProjectReferences()
            };
        }

        #endregion // Public methods

        #region Internal methods

        /// <summary>
        /// Creates project reference.
        /// </summary>
        protected ProjectReference BuildProjectReference(string name, string identity, string location, string version,
            string culture, string publicKeyToken)
        {
            return new ProjectReference()
                {
                    Name = name,
                    Identity = identity,
                    Location = location,
                    Version = Version.Parse(version),
                    Culture = string.Compare(culture, "0", StringComparison.Ordinal) == 0 ? string.Empty : culture,
                    PublicKeyToken = publicKeyToken
                };
        }

        /// <summary>
        /// Create project reference. In some cases the Visual Studio returns incorrect project output path.
        /// </summary>
        /// <param name="projectRef">The project reference.</param>
        /// <returns>Returns project reference assembly.</returns>        
        private ProjectReference CreateProjectReference(Reference3 projectRef)
        {
            if (projectRef.SourceProject == null)
            {
                return BuildProjectReference(projectRef.Name, projectRef.Identity,
                    projectRef.Path, projectRef.Version,
                    projectRef.Culture, projectRef.PublicKeyToken);
            }

            if (!File.Exists(projectRef.Path))
            {
                return null;
            }

            var activeConfiguration = Project.DTE.Solution.SolutionBuild.ActiveConfiguration.Name;

            var assemblyName = Path.GetFileName(projectRef.Path);
            var pattern = string.Format(@"\\(obj|bin)\\(?<Configuration>(Release|Debug))\\{0}", assemblyName);
            var regex = Regex.Match(projectRef.Path, pattern);
            if (regex.Success)
            {
                var projectRefConfiguration = regex.Groups["Configuration"].Value.ToString();
                if (projectRefConfiguration.Equals(activeConfiguration, StringComparison.Ordinal)
                    && File.Exists(projectRef.Path))
                {
                    return BuildProjectReference(projectRef.Name, projectRef.Identity,
                        projectRef.Path, projectRef.Version,
                        projectRef.Culture, projectRef.PublicKeyToken);
                }
            }

            var wrapper = DTEHelper.CreateProjectWrapper(projectRef.SourceProject);
            var assemblyDef = wrapper.AssemblyDefinition;

            return BuildProjectReference(projectRef.Name, projectRef.Identity,
                wrapper.GetOutputAssemblyPath(), assemblyDef.Name.Version.ToString(),
                assemblyDef.Name.Culture, _tokenConverter.ConvertFrom(assemblyDef.Name.PublicKeyToken));
        }

        #endregion // Internal methods
    }
}
