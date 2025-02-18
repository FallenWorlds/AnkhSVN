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
using Ankh.VS;
using Ankh.Scc;
using SharpSvn;
using Ankh.UI.SccManagement;
using System.Windows.Forms;
using Ankh.UI;
using Ankh.Selection;

namespace Ankh.Commands
{
	[SvnCommand(AnkhCommand.ProjectBranch)]
	[SvnCommand(AnkhCommand.SolutionBranch)]
	class BranchSolutionCommand : CommandBase
	{
		public override void OnUpdate(CommandUpdateEventArgs e)
		{
			if (!e.State.SolutionExists || e.State.SolutionBuilding || e.State.Debugging || e.State.SolutionOpening)
			{
				e.Enabled = false;
				return;
			}
			SvnItem item = GetRoot(e);

			if(item == null || !item.IsVersioned || item.IsDeleteScheduled || item.Status.LocalNodeStatus == SvnStatus.Added || item.Uri == null)
				e.Enabled = false;
		}

		private static SvnItem GetRoot(BaseCommandEventArgs e)
		{
			SvnItem item = null;
			switch (e.Command)
			{
				case AnkhCommand.SolutionBranch:
					IAnkhSolutionSettings ss = e.GetService<IAnkhSolutionSettings>();
					if (ss == null)
						return null;

					string root = ss.ProjectRoot;

					if (string.IsNullOrEmpty(root))
						return null;

					item = e.GetService<ISvnStatusCache>()[root];
					break;
				case AnkhCommand.ProjectBranch:
					SccProject p = EnumTools.GetSingle(e.Selection.GetSelectedProjects(false));
					if(p == null)
						break;

					ISccProjectInfo info = e.GetService<IProjectFileMapper>().GetProjectInfo(p);

					if (info == null || info.ProjectDirectory == null)
						break;

					item = e.GetService<ISvnStatusCache>()[info.ProjectDirectory];
					break;
			}

			return item;
		}

		public override void OnExecute(CommandEventArgs e)
		{
			SvnItem root = GetRoot(e);

			if (root == null)
				return;

			using (CreateBranchDialog dlg = new CreateBranchDialog())
			{
				if (e.Command == AnkhCommand.ProjectBranch)
					dlg.Text = Resources.BranchProject;

				dlg.SrcFolder = root.FullPath;
				dlg.SrcUri = root.Uri;
				dlg.EditSource = false;

				dlg.Revision = root.Status.Revision;

				RepositoryLayoutInfo info;
				if (RepositoryUrlUtils.TryGuessLayout(e.Context, root.Uri, out info))
					dlg.NewDirectoryName = new Uri(info.BranchesRoot, ".");

				while (true)
				{
					if (DialogResult.OK != dlg.ShowDialog(e.Context))
						return;

					string msg = dlg.LogMessage;

					bool retry = false;
					bool ok = false;
					ProgressRunnerResult rr =
						e.GetService<IProgressRunner>().RunModal(Resources.CreatingBranch,
						delegate(object sender, ProgressWorkerArgs ee)
						{
							SvnInfoArgs ia = new SvnInfoArgs();
							ia.ThrowOnError = false;

							if (ee.Client.Info(dlg.NewDirectoryName, ia, null))
							{
								DialogResult dr = DialogResult.Cancel;

								ee.Synchronizer.Invoke((AnkhAction)
									delegate
									{
										AnkhMessageBox mb = new AnkhMessageBox(ee.Context);
										dr = mb.Show(string.Format("The Branch/Tag at Url '{0}' already exists.", dlg.NewDirectoryName),
											"Path Exists", MessageBoxButtons.RetryCancel);
									}, null);

								if (dr == DialogResult.Retry)
								{
									// show dialog again to let user modify the branch URL
									retry = true;
								}
							}
							else
							{
								SvnCopyArgs ca = new SvnCopyArgs();
								ca.CreateParents = true;
								ca.LogMessage = msg;

								ok = dlg.CopyFromUri ?
									ee.Client.RemoteCopy(new SvnUriTarget(dlg.SrcUri, dlg.SelectedRevision), dlg.NewDirectoryName, ca) :
									ee.Client.RemoteCopy(dlg.SrcFolder, dlg.NewDirectoryName, ca);
							}
						});

					if (rr.Succeeded && ok && dlg.SwitchToBranch)
					{
						e.GetService<IAnkhCommandService>().PostExecCommand(AnkhCommand.SolutionSwitchDialog, dlg.NewDirectoryName);
					}

					if (!retry)
						break;
				}
			}
		}
	}
}
