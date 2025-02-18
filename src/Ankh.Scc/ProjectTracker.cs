// Copyright 2008-2009 The AnkhSVN Project
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using SharpSvn;

using Ankh.Commands;
using Ankh.Configuration;
using Ankh.VS;
using System.ComponentModel.Design;
using Ankh.Scc.ProjectMap;

namespace Ankh.Scc
{
    //[CLSCompliant(false)]
    [GlobalService(typeof(ProjectTracker))]
    partial class ProjectTracker : AnkhService, IVsTrackProjectDocumentsEvents2, IVsTrackProjectDocumentsEvents3
    {
        bool _hookedSolution;
        bool _hookedProjects;
        uint _projectCookie;
        uint _documentCookie;
        SccProvider _sccProvider;
        bool _collectHints;
        bool _solutionLoaded;
        readonly HybridCollection<string> _fileHints = new HybridCollection<string>(StringComparer.OrdinalIgnoreCase);
        readonly SortedList<string, string> _fileOrigins;
        readonly HybridCollection<string> _alreadyProcessed = new HybridCollection<string>(StringComparer.OrdinalIgnoreCase);

        public ProjectTracker(IAnkhServiceProvider context)
            : base(context)
        {
            _fileOrigins = new SortedList<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();

            AnkhServiceEvents ev = GetService<AnkhServiceEvents>();

            ev.SccProviderActivated += OnSvnSccProviderActivated;
            ev.SccProviderDeactivated += OnSccProviderDeactivated;

            IAnkhCommandStates states = GetService<IAnkhCommandStates>();

            if (states != null && states.SccProviderActive)
                OnSvnSccProviderActivated(this, EventArgs.Empty);
        }

        private void OnSccProviderDeactivated(object sender, EventArgs e)
        {
            Hook(true, false);
        }

        private void OnSvnSccProviderActivated(object sender, EventArgs e)
        {
            _sccProvider = GetService<SvnSccProvider>();
            Hook(true, true);
            LoadInitial();
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    Hook(false, false);
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        IAnkhSccProviderEvents SccEvents
        {
            [DebuggerStepThrough]
            get { return _sccProvider; }
        }

        SccProvider SccProvider
        {
            [DebuggerStepThrough]
            get { return _sccProvider; }
        }

        SccProjectMap ProjectMap
        {
            get { return _sccProvider.ProjectMap; }
        }

        private void LoadInitial()
        {
            IVsSolution solution = GetService<IVsSolution>(typeof(SVsSolution));

            if (solution == null)
                return;

            string dir, file, user;
            if (!VSErr.Succeeded(solution.GetSolutionInfo(out dir, out file, out user))
                || string.IsNullOrEmpty(file))
            {
                return; // No solution loaded, nothing to load
            }

            Guid none = Guid.Empty;
            IEnumHierarchies hierEnum;
            if (!VSErr.Succeeded(solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, ref none, out hierEnum)))
                return;

            IVsHierarchy[] hiers = new IVsHierarchy[32];
            uint nFetched;
            while (VSErr.Succeeded(hierEnum.Next((uint)hiers.Length, hiers, out nFetched)))
            {
                if (nFetched == 0)
                    break;
                for (int i = 0; i < nFetched; i++)
                {
                    IVsSccProject2 p2 = hiers[i] as IVsSccProject2;

                    if (p2 != null)
                        SccEvents.OnProjectOpened(p2, false);
                }
            }

            _solutionLoaded = true;
            SccEvents.OnSolutionOpened(false);
        }

        public void Hook(bool enableSolution, bool enableProjects)
        {
            if (enableSolution != _hookedSolution)
            {
                IVsSolution solution = GetService<IVsSolution>(typeof(SVsSolution));

                if (enableSolution && solution != null)
                {
                    Marshal.ThrowExceptionForHR(solution.AdviseSolutionEvents(this, out _documentCookie));
                    _hookedSolution = true;
                }
                else if (_hookedSolution)
                {
                    Marshal.ThrowExceptionForHR(solution.UnadviseSolutionEvents(_documentCookie));
                    _hookedSolution = false;
                }
            }

            if (enableProjects != _hookedProjects)
            {
                IVsTrackProjectDocuments2 tracker = GetService<IVsTrackProjectDocuments2>(typeof(SVsTrackProjectDocuments));
                if (tracker != null)
                {
                    //FIXME: really dont want to throw here on these if shutting down
                    //  is just too bad, best effort
                    if (enableProjects)
                    {
                        Marshal.ThrowExceptionForHR(tracker.AdviseTrackProjectDocumentsEvents(this, out _projectCookie));
                        _hookedProjects = true;
                    }
                    else if (_hookedProjects)
                    {
                        Marshal.ThrowExceptionForHR(tracker.UnadviseTrackProjectDocumentsEvents(_projectCookie));
                        _hookedProjects = false;
                    }
                }

                IAnkhConfigurationService cfg = GetService<IAnkhConfigurationService>();

                if (cfg != null && !cfg.Instance.DontHookSolutionExplorerRefresh)
                {
                    IAnkhGlobalCommandHook cmdHook = GetService<IAnkhGlobalCommandHook>();

                    if (cmdHook != null)
                    {
                        CommandID slnRefresh = new CommandID(VSConstants.VSStd2K, (int)VSConstants.VSStd2KCmdID.SLNREFRESH);
                        if (enableProjects)
                            cmdHook.HookCommand(slnRefresh, OnSolutionRefreshCommand);
                        else
                            cmdHook.UnhookCommand(slnRefresh, OnSolutionRefreshCommand);
                    }
                }
            }
        }

        private void OnSolutionRefreshCommand(object sender, EventArgs e)
        {
            SccEvents.OnSolutionRefreshCommand(e);
        }

        ISvnStatusCache _svnCache;
        ISvnStatusCache SvnCache
        {
            get { return _svnCache ?? (_svnCache = GetService<ISvnStatusCache>()); }
        }

        #region IVsTrackProjectDocumentsEvents2 Members

        public int OnAfterSccStatusChanged(int cProjects, int cFiles, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgpszMkDocuments, uint[] rgdwSccStatus)
        {
            return VSErr.S_OK;
        }

        #endregion

        /// <summary>
        /// Accesses a specified set of files and asks all implementers of this method to release any locks that may exist on those files.
        /// </summary>
        /// <param name="grfRequiredAccess">[in] A value from the <see cref="T:Microsoft.VisualStudio.Shell.Interop.__HANDSOFFMODE"></see> enumeration, indicating the type of access requested. This can be used to optimize the locks that actually need to be released.</param>
        /// <param name="cFiles">[in] The number of files in the rgpszMkDocuments array.</param>
        /// <param name="rgpszMkDocuments">[in] If there are any locks on this array of file names, the caller wants them to be released.</param>
        /// <returns>
        /// If the method succeeds, it returns <see cref="F:Microsoft.VisualStudio.VSErr.S_OK"></see>. If it fails, it returns an error code.
        /// </returns>
        public int HandsOffFiles(uint grfRequiredAccess, int cFiles, string[] rgpszMkDocuments)
        {
            if (_collectHints && rgpszMkDocuments != null)
            {
                // Some projects call HandsOffFiles of files they want to add. Use that to collect extra origin information
                foreach (string file in rgpszMkDocuments)
                {
                    if (!SccProvider.IsSafeSccPath(file))
                        continue;

                    string fullFile = SvnTools.GetNormalizedFullPath(file);
                    if (!_fileHints.Contains(fullFile))
                        _fileHints.Add(fullFile);
                }
            }
            return VSErr.S_OK;
        }

        /// <summary>
        /// Called when a project has completed operations on a set of files.
        /// </summary>
        /// <param name="cFiles">[in] Number of file names given in the rgpszMkDocuments array.</param>
        /// <param name="rgpszMkDocuments">[in] An array of file names.</param>
        /// <returns>
        /// If the method succeeds, it returns <see cref="F:Microsoft.VisualStudio.VSErr.S_OK"></see>. If it fails, it returns an error code.
        /// </returns>
        public int HandsOnFiles(int cFiles, string[] rgpszMkDocuments)
        {
            return VSErr.S_OK;
        }

        bool _registeredSccCleanup;
        internal void OnSccCleanup(CommandEventArgs e)
        {
            _registeredSccCleanup = false;
            _collectHints = false;

            _fileHints.Clear();
            _fileOrigins.Clear();
            _alreadyProcessed.Clear();
        }

        void RegisterForSccCleanup()
        {
            if (_registeredSccCleanup)
                return;

            Context.GetService<IAnkhCommandService>().PostTickCommand(ref _registeredSccCleanup, AnkhCommand.SccFinishTasks);
        }

        public IEnumerable<string> GetAllDocumentFiles(string documentName)
        {
            SccProjectFile file;

            if (ProjectMap.TryGetFile(documentName, out file))
                return file.GetAllFiles();
            else if (SvnItem.IsValidPath(documentName))
                return new string[] { documentName };
            else
                return new string[0];
        }
    }
}
