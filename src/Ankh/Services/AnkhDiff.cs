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
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using SharpSvn;

using Ankh.Scc;
using Ankh.Scc.UI;
using Ankh.UI;
using Ankh.VS;
using Ankh.Configuration;
using Ankh.Commands;

namespace Ankh.Services
{
	[GlobalService(typeof(IAnkhDiffHandler))]
	partial class AnkhDiff : AnkhService, IAnkhDiffHandler
	{
		public AnkhDiff(IAnkhServiceProvider context)
			: base(context)
		{
		}

		ISvnStatusCache _statusCache;
		ISvnStatusCache Cache
		{
			get { return _statusCache ?? (_statusCache = GetService<ISvnStatusCache>()); }
		}

		/// <summary>
		/// Gets path to the diff executable while taking care of config file settings.
		/// </summary>
		/// <param name="context"></param>
		/// <returns>The exe path.</returns>
		protected string GetDiffPath(DiffMode mode, String filename)
		{
			IAnkhConfigurationService cs = GetService<IAnkhConfigurationService>();

			switch (mode)
			{
				case DiffMode.PreferInternal:
					return null;
				default:
					return cs.Instance.GetDiffExePath(filename);
			}
		}

		public bool RunDiff(AnkhDiffArgs args)
		{
			if (args == null)
				throw new ArgumentNullException("args");
			else if (!args.Validate())
				throw new ArgumentException("Arguments not filled correctly", "args");

			SetFloat(args);

			string diffApp = this.GetDiffPath(args.Mode, args.MineFile);

			if (string.IsNullOrEmpty(diffApp))
			{
				IAnkhInternalDiff internalDiff = GetService<IAnkhInternalDiff>();

				if (internalDiff == null || !internalDiff.HasDiff)
					throw new InvalidOperationException("Internal diff not available");

				return internalDiff.RunDiff(args);
			}

			string program;
			string arguments;
			if (!Substitute(diffApp, args, DiffToolMode.Diff, out program, out arguments)
				|| !File.Exists(program))
			{
				new AnkhMessageBox(Context).Show(string.Format("Can't find diff program '{0}'", program ?? diffApp));
				return false;
			}

			Process p = new Process();
			p.StartInfo = new ProcessStartInfo(program, arguments);

			string mergedFile = args.MineFile;

			DiffToolMonitor monitor = null;
			if (!string.IsNullOrEmpty(mergedFile))
			{
				monitor = new DiffToolMonitor(Context, mergedFile, false, null);

				p.EnableRaisingEvents = true;
				monitor.Register(p);
			}

			bool started = false;
			try
			{
				return started = p.Start();
			}
			finally
			{
				if (!started)
				{
					if (monitor != null)
						monitor.Dispose();
				}
			}
		}

		private void SetFloat(AnkhDiffToolArgs args)
		{
			IAnkhConfigurationService cs = GetService<IAnkhConfigurationService>();

			if (cs != null)
				args.ShowDiffAsDocument = !cs.Instance.FloatDiffEditors;
		}

		/// <summary>
		/// Gets path to the diff executable while taking care of config file settings.
		/// </summary>
		/// <param name="context"></param>
		/// <returns>The exe path.</returns>
		protected string GetMergePath(DiffMode mode, string filename)
		{
			IAnkhConfigurationService cs = GetService<IAnkhConfigurationService>();

			switch (mode)
			{
				case DiffMode.PreferInternal:
					return null;
				default:
					return cs.Instance.GetMergeExePath(filename);
			}
		}

		public bool RunMerge(AnkhMergeArgs args)
		{
			if (args == null)
				throw new ArgumentNullException("args");
			else if (!args.Validate())
				throw new ArgumentException("Arguments not filled correctly", "args");

			SetFloat(args);

			string mergeApp = this.GetMergePath(args.Mode, args.MineFile);

			if (string.IsNullOrEmpty(mergeApp))
			{
				IAnkhInternalDiff internalDiff = GetService<IAnkhInternalDiff>();

				if (internalDiff != null && internalDiff.HasMerge)
				{
					return internalDiff.RunMerge(args);
				}
			}

			if (string.IsNullOrEmpty(mergeApp))
			{
				new AnkhMessageBox(Context).Show("Please specify a merge tool in Tools->Options->SourceControl->Subversion", "AnkhSVN - No visual merge tool is available");

				return false;
			}

			string program;
			string arguments;
			if (!Substitute(mergeApp, args, DiffToolMode.Merge, out program, out arguments)
				|| !File.Exists(program))
			{
				new AnkhMessageBox(Context).Show(string.Format("Can't find merge program '{0}'", program ?? mergeApp));
				return false;
			}

			Process p = new Process();
			p.StartInfo = new ProcessStartInfo(program, arguments);

			string mergedFile = args.MergedFile;

			DiffToolMonitor monitor = null;
			if (!string.IsNullOrEmpty(mergedFile))
			{
				monitor = new DiffToolMonitor(Context, mergedFile, false, args.GetMergedExitCodes());

				p.EnableRaisingEvents = true;
				monitor.Register(p);
			}

			bool started = false;
			try
			{
				return started = p.Start();
			}
			finally
			{
				if (!started)
				{
					if (monitor != null)
						monitor.Dispose();
				}
			}
		}

		protected string GetPatchPath(DiffMode mode)
		{
			IAnkhConfigurationService cs = GetService<IAnkhConfigurationService>();

			switch (mode)
			{
				case DiffMode.PreferInternal:
					return null;
				default:
					return cs.Instance.PatchExePath;
			}
		}

		public bool RunPatch(AnkhPatchArgs args)
		{
			if (args == null)
				throw new ArgumentNullException("args");
			else if (!args.Validate())
				throw new ArgumentException("Arguments not filled correctly", "args");

			SetFloat(args);

			string diffApp = GetPatchPath(args.Mode);

			if (string.IsNullOrEmpty(diffApp))
			{
				new AnkhMessageBox(Context).Show("Please specify a merge tool in Tools->Options->SourceControl->Subversion", "AnkhSVN - No visual merge tool is available");

				return false;
			}

			string program;
			string arguments;
			if (!Substitute(diffApp, args, DiffToolMode.Patch, out program, out arguments))
			{
				new AnkhMessageBox(Context).Show(string.Format("Can't find patch program '{0}'", program));
				return false;
			}

			Process p = new Process();
			p.StartInfo = new ProcessStartInfo(program, arguments);

			string applyTo = args.ApplyTo;

			DiffToolMonitor monitor = null;
			if (applyTo != null)
			{
				monitor = new DiffToolMonitor(Context, applyTo, true, args.GetMergedExitCodes());

				p.EnableRaisingEvents = true;
				monitor.Register(p);
			}

			bool started = false;
			try
			{
				return started = p.Start();
			}
			finally
			{
				if (!started)
				{
					if (monitor != null)
						monitor.Dispose();
				}
			}
		}

		public SvnUriTarget GetCopyOrigin(SvnItem item)
		{
			if (item == null)
				throw new ArgumentNullException("item");

			// TODO: Maybe handle cases where the parent was copied instead of the child?

			SvnUriTarget copiedFrom = null;
			using (SvnClient client = GetService<ISvnClientPool>().GetNoUIClient())
			{
				SvnInfoArgs ia = new SvnInfoArgs();
				ia.ThrowOnError = false;
				ia.Depth = SvnDepth.Empty;

				client.Info(item.FullPath, ia,
					delegate(object sender, SvnInfoEventArgs ee)
					{
						if (ee.CopyFromUri != null)
						{
							copiedFrom = new SvnUriTarget(ee.CopyFromUri, ee.CopyFromRevision);
						}
					});
			}
			return copiedFrom;
		}

		sealed class DiffToolMonitor : AnkhService, IVsFileChangeEvents
		{
			uint _cookie;
			readonly string _toMonitor;
			readonly bool _monitorDir;
			IAnkhOpenDocumentTracker _odt;
			int[] _resolvedExitCodes;

			public DiffToolMonitor(IAnkhServiceProvider context, string monitor, bool monitorDir, int[] resolvedExitCodes)
				: base(context)
			{
				if (string.IsNullOrEmpty(monitor))
					throw new ArgumentNullException("monitor");
				else if (!SvnItem.IsValidPath(monitor))
					throw new ArgumentOutOfRangeException("monitor");

				_monitorDir = monitorDir;
				_toMonitor = monitor;

				IVsFileChangeEx fx = GetService<IVsFileChangeEx>(typeof(SVsFileChangeEx));

				_cookie = 0;
				if (fx == null)
				{ }
				else if (!_monitorDir)
				{
					if (!VSErr.Succeeded(fx.AdviseFileChange(monitor,
							(uint)(_VSFILECHANGEFLAGS.VSFILECHG_Time | _VSFILECHANGEFLAGS.VSFILECHG_Size
							| _VSFILECHANGEFLAGS.VSFILECHG_Add | _VSFILECHANGEFLAGS.VSFILECHG_Del
							| _VSFILECHANGEFLAGS.VSFILECHG_Attr),
							this,
							out _cookie)))
					{
						_cookie = 0;
					}
				}
				else
				{
					if (!VSErr.Succeeded(fx.AdviseDirChange(monitor, 1, this, out _cookie)))
					{
						_cookie = 0;
					}
				}

				IAnkhOpenDocumentTracker odt = GetService<IAnkhOpenDocumentTracker>();

				if (odt != null)
				{
					if (odt.IgnoreChanges(_toMonitor, true))
						_odt = odt;
				}

				if (resolvedExitCodes != null)
					_resolvedExitCodes = (int[])resolvedExitCodes.Clone();
			}

			public void Dispose()
			{
				if (_cookie != 0)
				{
					uint ck = _cookie;
					_cookie = 0;

					IVsFileChangeEx fx = GetService<IVsFileChangeEx>(typeof(SVsFileChangeEx));

					if (fx != null)
					{
						if (!_monitorDir)
							fx.UnadviseFileChange(ck);
						else
							fx.UnadviseDirChange(ck);
					}
				}

				if (_odt != null)
				{
					_odt.IgnoreChanges(_toMonitor, false);
					_odt = null;
				}
			}

			private void MarkResolved()
			{
				ISvnClientPool pool = GetService<ISvnClientPool>();
				if (pool == null)
					return;

				using(SvnClient client = pool.GetClient())
				{
					client.Resolved(_toMonitor);
				}
			}


			public void Register(Process p)
			{
				p.Exited += new EventHandler(OnExited);
			}

			void OnExited(object sender, EventArgs e)
			{
				Process process = sender as Process;
				IAnkhCommandService cmd = GetService<IAnkhCommandService>();

				if (cmd != null)
					cmd.PostIdleAction(Dispose);
				else
					Dispose();

				if (process != null && _resolvedExitCodes != null)
					foreach(int ec in _resolvedExitCodes)
					{
						if (ec == process.ExitCode)
						{
							cmd.PostIdleAction(MarkResolved);
							break;
						}
					}

				IFileStatusMonitor m = GetService<IFileStatusMonitor>();

				if (m != null)
				{
					m.ScheduleSvnStatus(_toMonitor);
				}
			}

			public int DirectoryChanged(string pszDirectory)
			{
				ISvnStatusCache fsc = GetService<ISvnStatusCache>();

				if (fsc != null)
				{
					fsc.MarkDirtyRecursive(SvnTools.GetNormalizedFullPath(pszDirectory));
				}

				return VSErr.S_OK;
			}

			public int FilesChanged(uint cChanges, string[] rgpszFile, uint[] rggrfChange)
			{
				if (rgpszFile == null)
					return VSErr.E_POINTER;

				foreach (string file in rgpszFile)
				{
					if (string.Equals(file, _toMonitor, StringComparison.OrdinalIgnoreCase))
					{
						IFileStatusMonitor m = GetService<IFileStatusMonitor>();

						if (m != null)
						{
							bool isDirty;
							m.ExternallyChanged(_toMonitor, out isDirty);

							if (isDirty)
								Dispose();
						}

						break;
					}
				}

				return VSErr.S_OK;
			}
		}


		#region Argument Substitution support

		enum DiffToolMode
		{
			None,
			Diff,
			Merge,
			Patch
		}

		string SubstituteEmpty(string from)
		{
			return SubstituteArguments(from, EmptyDiffArgs, DiffToolMode.None);
		}

		private bool Substitute(string reference, AnkhDiffToolArgs args, DiffToolMode toolMode, out string program, out string arguments)
		{
			if (string.IsNullOrEmpty(reference))
				throw new ArgumentNullException("reference");
			else if (args == null)
				throw new ArgumentNullException("args");

			// Ok: We received a string with a program and arguments and windows
			// wants a program and arguments separated. Let's find the program before substituting

			reference = reference.TrimStart();

			program = null;
			arguments = null;

			string app;
			if (!string.IsNullOrEmpty(app = AnkhDiffTool.GetToolNameFromTemplate(reference)))
			{
				// We have a predefined template. Just use it
				AnkhDiffTool tool = GetAppItem(app, toolMode);

				if (tool == null)
					return false;
				else if (!tool.IsAvailable)
					return false;

				program = SubstituteArguments(tool.Program, args, toolMode);
				arguments = SubstituteArguments(tool.Arguments, args, toolMode);

				return !String.IsNullOrEmpty(program) && File.Exists(program);
			}
			else if (!SvnTools.TrySplitCommandLine(reference, SubstituteEmpty, out program, out arguments))
				return false;

			program = SubstituteArguments(program, args, toolMode);
			arguments = SubstituteArguments(arguments, args, toolMode);

			return true;
		}

		static readonly AnkhDiffArgs EmptyDiffArgs = new AnkhDiffArgs();
		Regex _re;

		private string SubstituteArguments(string arguments, AnkhDiffToolArgs diffArgs, DiffToolMode toolMode)
		{
			if (diffArgs == null)
				throw new ArgumentNullException("diffArgs");

			if (_re == null)
			{
				const string ifBody = "\\?(?<tick>['\"])(?<ifbody>([^'\"]|('')|(\"\"))*)\\k<tick>";
				const string elseBody = "(:(?<tick2>['\"])(?<elsebody>([^'\"]|('')|(\"\"))*)\\k<tick2>)?";
				const string isBody = "=(?<tick3>['\"])(?<isbody>([^'\"]|('')|(\"\"))*)\\k<tick3>";

				_re = new Regex(@"(\%(?<pc>[a-zA-Z0-9_]+)(\%|\b))" +
								"|(\\$\\((?<vs>[a-zA-Z0-9_-]+)(\\((?<arg>[a-zA-Z0-9_-]*)\\))?\\))" +
								"|(\\$\\((?<if>[a-zA-Z0-9_-]+)" + ifBody + elseBody + "\\))" +
								"|(\\$\\((?<is>[a-zA-Z0-9_-]+)" + isBody + "\\))");
			}

			return _re.Replace(arguments, new Replacer(this, diffArgs, toolMode).Replace).TrimEnd();
		}

		sealed class Replacer : AnkhService
		{
			readonly AnkhDiff _diff;
			readonly AnkhDiffToolArgs _toolArgs;
			readonly AnkhDiffArgs _diffArgs;
			readonly AnkhMergeArgs _mergeArgs;
			readonly AnkhPatchArgs _patchArgs;
			readonly DiffToolMode _toolMode;

			public Replacer(AnkhDiff context, AnkhDiffToolArgs args, DiffToolMode toolMode)
				: base(context)
			{
				if (context == null)
					throw new ArgumentNullException("context");
				else if (args == null)
					throw new ArgumentNullException("args");

				_diff = context;
				_toolArgs = args;
				_diffArgs = args as AnkhDiffArgs;
				_mergeArgs = args as AnkhMergeArgs;
				_patchArgs = args as AnkhPatchArgs;
				_toolMode = toolMode;
			}

			AnkhDiffArgs DiffArgs
			{
				get { return _diffArgs; }
			}

			AnkhMergeArgs MergeArgs
			{
				get { return _mergeArgs; }
			}

			AnkhPatchArgs PatchArgs
			{
				get { return _patchArgs; }
			}

			public string Replace(Match match)
			{
				string key;
				string value;
				bool vsStyle = true;

				if (match.Groups["pc"].Length > 1)
				{
					vsStyle = false;
					key = match.Groups["pc"].Value;
				}
				else if (match.Groups["vs"].Length > 1)
					key = match.Groups["vs"].Value;
				else if (match.Groups["if"].Length > 1)
				{
					string kk = match.Groups["if"].Value;

					bool isTrue = false;
					if (TryGetValue(kk, true, "", out value))
						isTrue = !string.IsNullOrEmpty(value);

					value = match.Groups[isTrue ? "ifbody" : "elsebody"].Value ?? "";

					value = value.Replace("''", "'").Replace("\"\"", "\"");

					return _diff.SubstituteArguments(value, _diffArgs, _toolMode);
				}
				else if (match.Groups["is"].Length > 1)
				{
					string k = match.Groups["is"].Value;
					value = match.Groups["isbody"].Value;
					value = value.Replace("''", "'").Replace("\"\"", "\"");

					SetValue(k, value);
					return "";
				}
				else
					return match.Value; // Don't replace if not matched

				string arg = match.Groups["arg"].Value ?? "";
				TryGetValue(key, vsStyle, arg, out value);

				return value ?? "";
			}

			private void SetValue(string key, string value)
			{
				switch (key)
				{
					case "ResolveConflictOn":
						List<int> intVals = new List<int>();

						foreach (string s in value.Split(','))
						{
							int i;
							if (int.TryParse(s.Trim(), out i))
							{
								intVals.Add(i);
							}
						}

						if (intVals.Count > 0 && MergeArgs != null)
							MergeArgs.SetMergedExitCodes(intVals.ToArray());
						break;
				}
			}

			bool TryGetValue(string key, bool vsStyle, string arg, out string value)
			{
				if (key == null)
					throw new ArgumentNullException("key");

				key = key.ToUpperInvariant();
				value = null;

				string v;
				switch (key)
				{
					case "BASE":
						if (DiffArgs != null)
							value = DiffArgs.BaseFile;
						else
							return false;
						break;
					case "BNAME":
					case "BASENAME":
						if (DiffArgs != null)
							value = DiffArgs.BaseTitle ?? Path.GetFileName(DiffArgs.BaseFile);
						else
							return false;
						break;
					case "MINE":
						if (DiffArgs != null)
							value = DiffArgs.MineFile;
						else
							return false;
						break;
					case "YNAME":
					case "MINENAME":
						if (DiffArgs != null)
							value = DiffArgs.MineTitle ?? Path.GetFileName(DiffArgs.MineFile);
						else
							return false;
						break;

					case "THEIRS":
						if (MergeArgs != null)
							value = MergeArgs.TheirsFile;
						else
							return false;
						break;
					case "TNAME":
					case "THEIRNAME":
					case "THEIRSNAME":
						if (MergeArgs != null)
							value = MergeArgs.TheirsTitle ?? Path.GetFileName(MergeArgs.TheirsFile);
						else
							return false;
						break;
					case "MERGED":
						if (MergeArgs != null)
							value = MergeArgs.MergedFile;
						else
							return false;
						break;
					case "MERGEDNAME":
					case "MNAME":
						if (MergeArgs != null)
							value = MergeArgs.MergedTitle ?? Path.GetFileName(MergeArgs.MergedFile);
						else
							return false;
						break;

					case "PATCHFILE":
						if (PatchArgs != null)
							value = PatchArgs.PatchFile;
						else
							return false;
						break;
					case "APPLYTODIR":
						if (PatchArgs != null)
							value = PatchArgs.ApplyTo;
						else
							return false;
						break;
					case "APPPATH":
						v = _diff.GetAppPath(arg, _toolMode);
						value = _diff.SubstituteArguments(v ?? "", DiffArgs, _toolMode);
						break;
					case "APPTEMPLATE":
						v = _diff.GetAppTemplate(arg, _toolMode);
						value = _diff.SubstituteArguments(v ?? "", DiffArgs, _toolMode);
						break;
					case "PROGRAMFILES":
						// Return the environment variable if using environment variable style
						value = (vsStyle ? null : Environment.GetEnvironmentVariable(key)) ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
						break;
					case "COMMONPROGRAMFILES":
						// Return the environment variable if using environment variable style
						value = (vsStyle ? null : Environment.GetEnvironmentVariable(key)) ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
						break;
					case "HOSTPROGRAMFILES":
						// Use the WOW64 program files directory if available, otherwise just program files
						value = Environment.GetEnvironmentVariable("PROGRAMW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
						break;
					case "APPDATA":
						value = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
						break;
					case "LOCALAPPDATA":
						value = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
						break;
					case "READONLY":
						if (DiffArgs != null && DiffArgs.ReadOnly)
							value = "1";
						else
							value = "";
						break;
					case "VSHOME":
						IVsSolution sol = GetService<IVsSolution>(typeof(SVsSolution));
						if (sol == null)
							return false;
						object val;
						if (VSErr.Succeeded(sol.GetProperty((int)__VSSPROPID.VSSPROPID_InstallDirectory, out val)))
							value = val as string;
						return true;
					default:
						// Just replace with "" if unknown
						v = Environment.GetEnvironmentVariable(key);
						if (!string.IsNullOrEmpty(v))
							value = v;
						return (value != null);
				}

				return true;
			}
		}

		#endregion

		public string GetTitle(SvnItem target, SvnRevision revision)
		{
			if (target == null)
				throw new ArgumentNullException("target");
			if (revision == null)
				throw new ArgumentNullException("revision");

			return GetTitle(target.Name, revision);
		}

		public string GetTitle(SvnTarget target, SvnRevision revision)
		{
			if (target == null)
				throw new ArgumentNullException("target");
			if (revision == null)
				throw new ArgumentNullException("revision");

			return GetTitle(target.FileName, revision);
		}

		static string GetTitle(string fileName, SvnRevision revision)
		{
			string strRev = revision.RevisionType == SvnRevisionType.Time ?
				revision.Time.ToLocalTime().ToString("g") : revision.ToString();

			return fileName + " - " + strRev;
		}

		static string PathSafeRevision(SvnRevision revision)
		{
			if (revision.RevisionType == SvnRevisionType.Time)
				return revision.Time.ToLocalTime().ToString("yyyyMMdd_hhmmss");
			return revision.ToString();
		}

		string GetName(string filename, SvnRevision rev)
		{
			if (string.IsNullOrEmpty(filename))
				throw new ArgumentNullException("filename");
			else if (rev == null)
				throw new ArgumentNullException("rev");

			return (Path.GetFileNameWithoutExtension(filename) + "." + PathSafeRevision(rev) + Path.GetExtension(filename)).Trim('.');
		}

		string GetTempPath(string filename, SvnRevision rev)
		{
			string name = GetName(filename, rev);
			string file;
			if (_lastDir == null || !Directory.Exists(_lastDir) || File.Exists(file = Path.Combine(_lastDir, name)))
			{
				_lastDir = GetService<IAnkhTempDirManager>().GetTempDir();

				file = Path.Combine(_lastDir, name);
			}

			return file;
		}

		string _lastDir;
		public string GetTempFile(SvnItem target, SharpSvn.SvnRevision revision, bool withProgress)
		{
			if (target == null)
				throw new ArgumentNullException("target");
			else if (revision == null)
				throw new ArgumentNullException("revision");

			string file = GetTempPath(target.Name, revision);

			if (target.NodeKind != SvnNodeKind.File)
				throw new InvalidOperationException("Can't create a tempfile from a directory");

			ProgressRunnerResult r = GetService<IProgressRunner>().RunModal(Resources.RetrievingFileForComparison,
				delegate(object sender, ProgressWorkerArgs aa)
				{
					SvnWriteArgs wa = new SvnWriteArgs();
					wa.Revision = revision;

					using (Stream s = File.Create(file))
						aa.Client.Write(target.FullPath, s, wa);
				});

			if (!r.Succeeded)
				return null; // User canceled

			if (File.Exists(file))
				File.SetAttributes(file, FileAttributes.ReadOnly); // A readonly file does not allow editting from many diff tools

			return file;
		}

		public string GetTempFile(SharpSvn.SvnTarget target, SharpSvn.SvnRevision revision, bool withProgress)
		{
			if (target == null)
				throw new ArgumentNullException("target");
			else if (revision == null)
				throw new ArgumentNullException("revision");

			string file = GetTempPath(target.FileName, revision);
			bool unrelated = false;

			ProgressRunnerResult r = GetService<IProgressRunner>().RunModal(Resources.RetrievingFileForComparison,
				delegate(object sender, ProgressWorkerArgs aa)
				{
					SvnWriteArgs wa = new SvnWriteArgs();
					wa.Revision = revision;
					wa.AddExpectedError(SvnErrorCode.SVN_ERR_CLIENT_UNRELATED_RESOURCES);

					using (Stream s = File.Create(file))
						if (!aa.Client.Write(target, s, wa))
						{
							if (wa.LastException.SvnErrorCode == SvnErrorCode.SVN_ERR_CLIENT_UNRELATED_RESOURCES)
								unrelated = true;
						}
				});

			if (!r.Succeeded || unrelated)
				return null; // User canceled

			if (File.Exists(file))
				File.SetAttributes(file, FileAttributes.ReadOnly); // A readonly file does not allow editting from many diff tools

			return file;
		}

		public string[] GetTempFiles(SvnTarget target, SvnRevision from, SvnRevision to, bool withProgress)
		{
			if (target == null)
				throw new ArgumentNullException("target");
			else if (from == null)
				throw new ArgumentNullException("from");
			else if (to == null)
				throw new ArgumentNullException("to");

			string f1;
			string f2;

			if (from.RevisionType == SvnRevisionType.Number && to.RevisionType == SvnRevisionType.Number && from.Revision + 1 == to.Revision)
			{
				f1 = GetTempPath(target.FileName, from);
				f2 = GetTempPath(target.FileName, to);

				int n = 0;
				ProgressRunnerResult r = Context.GetService<IProgressRunner>().RunModal(Resources.RetrievingMultipleVersionsOfFile,
					delegate(object sender, ProgressWorkerArgs e)
					{
						SvnFileVersionsArgs ea = new SvnFileVersionsArgs();
						ea.Start = from;
						ea.End = to;
						ea.AddExpectedError(SvnErrorCode.SVN_ERR_UNSUPPORTED_FEATURE); // Github

						e.Client.FileVersions(target, ea,
							delegate(object sender2, SvnFileVersionEventArgs e2)
							{
								if (n++ == 0)
									e2.WriteTo(f1);
								else
									e2.WriteTo(f2);
							});
					});

				if (!r.Succeeded)
					return null;

				if (n != 2)
				{
					// Sloooooow workaround for SvnBridge / Codeplex

					f1 = GetTempFile(target, from, withProgress);
					if (f1 == null)
						return null; // Canceled
					f2 = GetTempFile(target, to, withProgress);
				}
			}
			else
			{
				f1 = GetTempFile(target, from, withProgress);
				if (f1 == null)
					return null; // Canceled
				f2 = GetTempFile(target, to, withProgress);
			}

			if (string.IsNullOrEmpty(f1) || string.IsNullOrEmpty(f2))
				return null;

			string[] files = new string[] { f1, f2 };

			foreach (string f in files)
			{
				if (File.Exists(f))
					File.SetAttributes(f, FileAttributes.ReadOnly);
			}

			return files;
		}
	}
}
