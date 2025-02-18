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
using Ankh.Collections;
using Ankh.UI;

namespace Ankh.Scc
{
	public sealed class BatchStartedEventArgs : EventArgs, IDisposable
	{
		Stack<AnkhAction> _closers = new Stack<AnkhAction>();
		IAnkhThreadedWaitService _svc;
		IAnkhThreadedWaitDialog _dlg;
		int _ctr;
		bool _initDone;
		DateTime _snap;

		public BatchStartedEventArgs(IAnkhThreadedWaitService svc)
		{
			_svc = svc;
			_snap = DateTime.Now;
		}

		public event AnkhAction Disposers
		{
			add
			{
				if (value != null)
					_closers.Push(value);
			}
			remove
			{
				throw new InvalidOperationException();
			}
		}

		public void Tick()
		{
			_ctr++;
			if ((_ctr & 31) != 0)
				return;

			if (!_initDone)
			{
				_initDone = true;
				if (_svc != null)
					_dlg = _svc.Start(Resources.WaitCaption, Resources.WaitMessage);
			}
			else if ((_ctr & 255) == 0 && _dlg != null)
			{
				_dlg.Tick();
			}
		}


		public void Dispose()
		{
			using (_dlg) // Closes dialog if needed
			{
				while (_closers.Count > 0)
				{
					AnkhAction action = _closers.Pop();
					try
					{
						action();
					}
					catch { }
				}
			}
		}

	}
	/// <summary>
	///
	/// </summary>
	/// <remarks>This service is only available in the UI thread</remarks>
	public interface IPendingChangesManager
	{
		/// <summary>
		/// Gets a boolean indicating whether the pending changes manager is active
		/// </summary>
		bool IsActive { get; set; }

		/// <summary>
		/// Gets a the actual list of all current pending changes
		/// </summary>
		PendingChangeCollection PendingChanges { get; }

		/// <summary>
		/// Gets a list of all current pending changes below a specific path
		/// </summary>
		/// <returns></returns>
		IEnumerable<PendingChange> GetAllBelow(string path);

		/// <summary>
		/// Schedules a refresh of all pending change state
		/// </summary>
		/// <param name="clearStateCache"></param>
		void FullRefresh(bool clearStateCache);

		/// <summary>
		/// Schedules a refresh the pending change state for the specified path
		/// </summary>
		void Refresh(string path);
		/// <summary>
		/// Schedules a refresh of the pending change state for the specified paths
		/// </summary>
		/// <param name="paths"></param>
		void Refresh(IEnumerable<string> paths);

		/// <summary>
		/// Raised around 'large' updates
		/// </summary>
		event EventHandler<BatchStartedEventArgs> BatchUpdateStarted;

		/// <summary>
		/// Raised when the pending changes manager is activated or disabled
		/// </summary>
		event EventHandler IsActiveChanged;

		/// <summary>
		/// Clears all state; called on solution close
		/// </summary>
		void Clear();

		/// <summary>
		/// Tries to get a matching file from the specified text
		/// </summary>
		/// <param name="text"></param>
		/// <param name="change"></param>
		/// <returns></returns>
		/// <remarks>Called from the log message editor in an attempt to provide a mouse over</remarks>
		bool TryMatchFile(string text, out PendingChange change);

		/// <summary>
		/// Determines whether the list of pending changes contains the specified path.
		/// </summary>
		/// <param name="path">The path.</param>
		/// <returns>
		/// <c>true</c> if the specified path contains path; otherwise, <c>false</c>.
		/// </returns>
		bool Contains(string path);

		IEnumerable<string> GetSuggestedChangeLists();
	}
}
