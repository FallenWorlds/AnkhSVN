// Copyright 2006-2009 The AnkhSVN Project
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
using System.ComponentModel.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using Ankh.Commands;
using Ankh.Configuration;
using Ankh.Scc;
using Ankh.UI.WorkingCopyExplorer.Nodes;
using Ankh.VS;

namespace Ankh.UI.WorkingCopyExplorer
{
	public partial class WorkingCopyExplorerControl : AnkhToolWindowControl
	{
		IAnkhConfigurationService _configurationService;
		protected virtual IAnkhConfigurationService ConfigurationService
		{
			get { return _configurationService ?? (_configurationService = Context.GetService<IAnkhConfigurationService>()); }
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="WorkingCopyExplorerControl"/> class.
		/// </summary>
		public WorkingCopyExplorerControl()
		{
			this.InitializeComponent();
		}

		protected override void OnContextChanged(EventArgs e)
		{
			base.OnContextChanged(e);
			folderTree.Context = Context;
			fileList.Context = Context;
		}

		protected override void OnThemeChanged(EventArgs e)
		{
 #if VS_11_ENV
			// Remove the chrome
			foldersStrip.Visible = foldersStrip.Enabled = false;
			folderTree.BorderStyle = BorderStyle.None;
			fileList.BorderStyle = BorderStyle.None;
 #endif // VS_11_ENV

			base.OnThemeChanged(e);
		}

		/// <summary>
		/// Called when the frame is created
		/// </summary>
		/// <param name="e"></param>
		protected override void OnFrameCreated(EventArgs e)
		{
			base.OnFrameCreated(e);

			ToolWindowHost.CommandContext = AnkhId.SccExplorerContextGuid;
			ToolWindowHost.KeyboardContext = AnkhId.SccExplorerContextGuid;

			folderTree.Context = Context;
			fileList.Context = Context;

			VSCommandHandler.Install(Context, this, AnkhCommand.ExplorerUp, OnUp, OnUpdateUp);
			VSCommandHandler.Install(Context, this, AnkhCommand.Refresh, OnRefresh, OnUpdateRefresh);
			VSCommandHandler.Install(Context, this, new CommandID(VSConstants.GUID_VSStandardCommandSet97, (int)VSConstants.VSStd97CmdID.Open), OnOpen, OnUpdateOpen);
			VSCommandHandler.Install(Context, this, new CommandID(VSConstants.GUID_VSStandardCommandSet97, (int)VSConstants.VSStd97CmdID.Delete), OnDelete, OnUpdateDelete);
			VSCommandHandler.Install(Context, this, new CommandID(VSConstants.GUID_VSStandardCommandSet97, (int)VSConstants.VSStd97CmdID.Rename), OnRename, OnUpdateRename);

			AnkhServiceEvents environment = Context.GetService<AnkhServiceEvents>();

			// The Workingcopy explorer is a singleton toolwindow (Will never be destroyed unless VS closes)
			environment.SolutionClosed += OnSolutionClosed;
			environment.SolutionOpened += OnSolutionOpened;

			IUIService ui = Context.GetService<IUIService>();

			if (ui != null)
			{
				ToolStripRenderer renderer = ui.Styles["VsToolWindowRenderer"] as ToolStripRenderer;

				if (renderer != null)
					foldersStrip.Renderer = renderer;
			}
		}

		protected override void OnLoad(EventArgs e)
		{
			IServiceContainer container = Context.GetService<IServiceContainer>();

			if (container != null)
			{
				if (null == container.GetService(typeof(WorkingCopyExplorerControl)))
					container.AddService(typeof(WorkingCopyExplorerControl), this);
			}

			base.OnLoad(e);

			fileList.ColumnWidthChanged += new ColumnWidthChangedEventHandler(fileList_ColumnWidthChanged);
			IDictionary<string, int> widths = ConfigurationService.GetColumnWidths(GetType());
			fileList.SetColumnWidths(widths);
		}

		protected void fileList_ColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e)
		{
			IDictionary<string, int> widths = fileList.GetColumnWidths();
			ConfigurationService.SaveColumnsWidths(GetType(), widths);
		}

		void OnSolutionOpened(object sender, EventArgs e)
		{
			AddRoots(false);
			AddRoots(true);
			RefreshRoots();
		}

		void OnSolutionClosed(object sender, EventArgs e)
		{
			AddRoots(false);
			AddRoots(true);
			RefreshRoots();
		}

		CommandMapper _mapper;
		CommandMapper CommandMapper
		{
			get
			{
				if (_mapper == null && Context != null)
					_mapper = Context.GetService<CommandMapper>();

				return _mapper;
			}
		}

		void OnUpdateDelete(object sender, CommandUpdateEventArgs e)
		{
			CommandMapper mapper = CommandMapper;

			if (mapper != null)
				mapper.PerformUpdate(AnkhCommand.ItemDelete, e);
			else
				e.Enabled = false;
		}

		void OnDelete(object sender, CommandEventArgs e)
		{
			e.GetService<IAnkhCommandService>().PostExecCommand(AnkhCommand.ItemDelete);
		}

		void OnUpdateRename(object sender, CommandUpdateEventArgs e)
		{
			CommandMapper mapper = CommandMapper;

			if (mapper != null)
				mapper.PerformUpdate(AnkhCommand.ItemRename, e);
			else
				e.Enabled = false;
		}

		void OnRename(object sender, CommandEventArgs e)
		{
			e.GetService<IAnkhCommandService>().PostExecCommand(AnkhCommand.ItemRename);
		}

		bool _rootsPresent;
		void AddRoots(bool add)
		{
			if (add == _rootsPresent)
				return;

			if (!_rootsPresent)
			{
				IAnkhSolutionSettings slnSettings = Context.GetService<IAnkhSolutionSettings>();
				if (!string.IsNullOrEmpty(slnSettings.SolutionFilename))
				{
					SvnItem slnItem = FileStatusCache[slnSettings.SolutionFilename];
					folderTree.AddRoot(new WCSolutionNode(Context, slnItem));
				}
				folderTree.AddRoot(new WCMyComputerNode(Context));

				_rootsPresent = true;
			}
			else
			{
				folderTree.ClearRoots();
				_rootsPresent = false;
			}
		}

		ISvnStatusCache FileStatusCache
		{
			get { return Context.GetService<ISvnStatusCache>(); }
		}

		protected override void OnFrameShow(Ankh.Scc.UI.FrameEventArgs e)
		{
			base.OnFrameShow(e);
			switch (e.Show)
			{
				case __FRAMESHOW.FRAMESHOW_WinShown:
					AddRoots(true);
					break;
			}
		}

		protected override void OnFrameClose(EventArgs e)
		{
			base.OnFrameClose(e);

			AddRoots(false);
		}

		private void RefreshRoots()
		{
			AddRoots(false);
			AddRoots(true);
		}

		internal void AddRoot(WCTreeNode root)
		{
			this.folderTree.AddRoot(root);
		}

		internal void RefreshItem(WCTreeNode item)
		{
			item.Refresh();
		}

		private void folderTree_AfterSelect(object sender, TreeViewEventArgs e)
		{
			WCTreeNode item = this.folderTree.SelectedItem;
			if (item == null)
				return;

			this.fileList.SetDirectory(item);
		}

		ISvnStatusCache _cache;
		protected internal ISvnStatusCache StatusCache
		{
			get { return _cache ?? (_cache = Context.GetService<ISvnStatusCache>()); }
		}

		public void AddRoot(string path)
		{
			if (string.IsNullOrEmpty(path))
				throw new ArgumentNullException("path");

			SvnItem item = StatusCache[path];

			if (item == null)
				return;

			SvnWorkingCopy wc = item.WorkingCopy;

			string root;
			if (wc != null)
				root = wc.FullPath;
			else
				root = item.FullPath;



			AddRoot(CreateRoot(root));

			folderTree.SelectSubNode(item);
		}

		public void BrowsePath(IAnkhServiceProvider context, string path)
		{
			if (context == null)
				throw new ArgumentNullException("context");
			else if (string.IsNullOrEmpty(path))
				throw new ArgumentNullException("path");

			folderTree.BrowsePath(path);

			if (context.GetService<ISvnStatusCache>()[path].IsFile)
			{
				fileList.SelectPath(path);
			}
		}

		private WCTreeNode CreateRoot(string directory)
		{
			StatusCache.MarkDirtyRecursive(directory);
			SvnItem item = StatusCache[directory];

			return new WCDirectoryNode(Context, null, item);
		}

		internal void OpenItem(IAnkhServiceProvider context, string p)
		{
			Ankh.Commands.IAnkhCommandService cmd = context.GetService<Ankh.Commands.IAnkhCommandService>();

			if (cmd != null)
				cmd.ExecCommand(AnkhCommand.ItemOpenVisualStudio, true);
		}

		void OnUpdateRefresh(object sender, CommandUpdateEventArgs e)
		{
		}

		void OnRefresh(object sender, CommandEventArgs e)
		{
			e.GetService<IAnkhCommandService>().DirectlyExecCommand(AnkhCommand.Refresh);

			RefreshSelection();
		}

		void OnUpdateUp(object sender, CommandUpdateEventArgs e)
		{
			FileSystemTreeNode tn = folderTree.SelectedNode as FileSystemTreeNode;

			if (tn == null || !(tn.Parent is FileSystemTreeNode))
				e.Enabled = false;
		}

		void OnUp(object sender, CommandEventArgs e)
		{
			FileSystemTreeNode t = folderTree.SelectedNode as FileSystemTreeNode;
			if (t != null && t.Parent != null)
			{
				folderTree.SelectedNode = t.Parent;
			}
		}

		void OnUpdateOpen(object sender, CommandUpdateEventArgs e)
		{
			SvnItem item = EnumTools.GetSingle(e.Selection.GetSelectedSvnItems(false));

			if (item == null)
				e.Enabled = false;
		}

		void OnOpen(object sender, CommandEventArgs e)
		{
			SvnItem item = EnumTools.GetSingle(e.Selection.GetSelectedSvnItems(false));

			if (item.IsDirectory)
			{
				folderTree.SelectSubNode(item);
				return;
			}

			AutoOpenCommand(e, item);
		}

		private static void AutoOpenCommand(CommandEventArgs e, SvnItem item)
		{
			IProjectFileMapper pfm = e.GetService<IProjectFileMapper>();
			IAnkhCommandService svc = e.GetService<IAnkhCommandService>();
			IAnkhSolutionSettings solutionSettings = e.GetService<IAnkhSolutionSettings>();

			if (pfm == null || svc == null || solutionSettings == null)
				return;

			// We can assume we have a file

			if (pfm.IsProjectFileOrSolution(item.FullPath))
			{
				// Ok, the user selected the current solution file or an open project
				// Let's jump to it in the solution explorer

				svc.ExecCommand(AnkhCommand.ItemSelectInSolutionExplorer);
				return;
			}

			if (item.InSolution)
			{
				// The file is part of the solution, we can assume VS knows how to open it
				svc.ExecCommand(AnkhCommand.ItemOpenVisualStudio);
				return;
			}

			string filename = item.Name;
			string ext = item.Extension;

			if (string.IsNullOrEmpty(ext))
			{
				// No extension -> Open as text
				svc.PostExecCommand(AnkhCommand.ItemOpenTextEditor);
				return;
			}

			foreach (string projectExt in solutionSettings.AllProjectExtensionsFilter.Split(';'))
			{
				if (projectExt.TrimStart('*').Trim().Equals(ext, StringComparison.OrdinalIgnoreCase))
				{
					// We found a project or solution: Ask VS to open it

					e.GetService<IAnkhSolutionSettings>().OpenProjectFile(item.FullPath);
					return;
				}
			}

			bool odd = false;
			foreach (string block in solutionSettings.OpenFileFilter.Split('|'))
			{
				odd = !odd;
				if (odd)
					continue;

				foreach (string itemExt in block.Split(';'))
				{
					if (itemExt.TrimStart('*').Trim().Equals(ext, StringComparison.OrdinalIgnoreCase))
					{
						VsShellUtilities.OpenDocument(e.Context, item.FullPath);
						return;
					}
				}
			}

			// Ultimate fallback: Just open the file as windows would
			svc.PostExecCommand(AnkhCommand.ItemOpenWindows);
		}

		public void RefreshSelection()
		{
			FileSystemTreeNode tn = (FileSystemTreeNode)folderTree.SelectedNode;
			tn.WCNode.Refresh(true);
			folderTree.FillNode(tn);

			WCTreeNode item = this.folderTree.SelectedItem;
			if (item == null)
				return;

			this.fileList.SetDirectory(item);
		}

		public string SelectedDirectory
		{
			get
			{
				WCDirectoryNode item = this.folderTree.SelectedItem as WCDirectoryNode;
				if (item == null)
					return null;

				return item.SvnItem.FullPath;
			}
		}
	}
}
