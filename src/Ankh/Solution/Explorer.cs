// $Id$
using System;
using EnvDTE;
using Utils;

using NSvn.Core;
using NSvn.Common;
using Utils.Win32;
using System.Collections;
using System.Drawing;
using System.Diagnostics;
using System.IO;

using C = Utils.Win32.Constants;
using DteConstants = EnvDTE.Constants;
using System.Windows.Forms;


namespace Ankh.Solution
{

    /// <summary>
    /// Represents the Solution Explorer window in the VS.NET IDE
    /// </summary>
    public class Explorer : ISolutionExplorer
    {
        public Explorer( _DTE dte, IContext context )
        {
            this.dte = dte;
            this.context = context;
            this.projectItems = new Hashtable( new ItemHashCodeProvider(), 
                new ItemComparer() );
            this.projects = new Hashtable( new ProjectHashCodeProvider(), 
                new ProjectComparer() );
            
            // get the uihierarchy root
            this.uiHierarchy = (UIHierarchy)this.dte.Windows.Item( 
                DteConstants.vsWindowKindSolutionExplorer ).Object; 
            this.solutionNode = null;            
        }

        /// <summary>
        ///  To be called when a solution is loaded.
        /// </summary>
        public void Load()
        {           
            this.Unload();
            this.context.ProjectFileWatcher.AddFile( this.DTE.Solution.FullName );
            this.SetUpTreeview();
            this.SyncAll();

            Trace.WriteLine( String.Format( "Cache hit rate: {0}%", 
                this.context.StatusCache.CacheHitSuccess ), "Ankh" );
        }

        /// <summary>
        ///  Resets the state of the object. To be called when a solution is unloaded.
        /// </summary>
        public void Unload()
        {
            Debug.WriteLine( "Unloading existing solution information", "Ankh" );
            this.projectItems.Clear();
            this.projects.Clear();
            if ( this.treeview != null )
                this.treeview.ClearStatusImages();
            if ( this.originalImageList != IntPtr.Zero )
            {
                this.treeview.StatusImageList = originalImageList;
                originalImageList = IntPtr.Zero;
            }
            this.context.ProjectFileWatcher.Clear();
            this.solutionNode = null;
        }    

        /// <summary>
        /// Refreshes the parents of the selected items.
        /// </summary>
        public void RefreshSelectionParents()
        {
            foreach( UIHierarchyItem item in (Array)this.uiHierarchy.SelectedItems )
            {
                TreeNode node = this.GetNode( item );
                if ( node != null )
                {
                    if ( node == this.solutionNode )
                        node.Refresh();
                    else
                        node.Parent.Refresh();
                }
            }
        }

        /// <summary>
        /// Refreshes all subnodes of a specific project.
        /// </summary>
        /// <param name="project"></param>
        public void Refresh( Project project )
        {
            TreeNode node = this.GetNode( project );
            if ( node != null )
                node.Refresh();
        }

        /// <summary>
        /// Refreshes the current selection.
        /// </summary>
        public void RefreshSelection()
        {
            foreach( UIHierarchyItem item in (Array)this.uiHierarchy.SelectedItems )
            {
                TreeNode node = this.GetNode( item );
                // special care for the solution
                if ( node == solutionNode )
                    this.SyncAll();
                else if ( node != null )
                    node.Refresh();
            }
        }


        /// <summary>
        /// Updates the status of the given item.
        /// </summary>
        /// <param name="item"></param>
        public void Refresh( ProjectItem item )
        {
            TreeNode node = (TreeNode)this.projectItems[item];
            if ( node != null )
                node.Refresh();
        }       

        public void SyncAll()
        {
            // build the whole tree anew
            Debug.WriteLine( "Synchronizing with treeview", "Ankh" );

            this.projectItems.Clear();
            this.projects.Clear();
            
            // generate a status cache
            this.GenerateStatusCache( this.dte.Solution.FullName );
            
            // store the original image list
            this.originalImageList = this.treeview.StatusImageList;
            
            // and assign the status image list to the tree
            this.treeview.StatusImageList = statusImageList.Handle;

            // avoid lots of flickering while we walk the tree
            this.treeview.LockWindowUpdate( true );
            try
            {

                // we assume there is a single root node
                this.solutionNode = TreeNode.CreateSolutionNode( 
                    this.uiHierarchy.UIHierarchyItems.Item(1), this );
            }
            finally
            {
                // done
                this.treeview.LockWindowUpdate( false );
            }
 
            Debug.WriteLine( "Created solution node", "Ankh" );
        }

        /// <summary>
        /// Returns the SvnItem resources associated with the selected items
        /// in the solution explorer.
        /// </summary>
        /// <param name="getChildItems">Whether children of the items in 
        /// question should be included.</param>        /// 
        /// <returns>A list of SvnItem instances.</returns>
        public IList GetSelectionResources( bool getChildItems )
        {
            return this.GetSelectionResources( getChildItems, null );
        }

        /// <summary>	 	
        /// Visits all the selected nodes.	 	
        /// </summary>	 	
        /// <param name="visitor"></param>	 	
        public void VisitSelectedNodes( INodeVisitor visitor )	 	
        {	 	
            //foreach( SelectedItem item in items )	 	
            object o = this.uiHierarchy.SelectedItems;	 	
            foreach( UIHierarchyItem item in (Array)this.uiHierarchy.SelectedItems )	 	
            {	 	
                TreeNode node = this.GetNode( item );	 	
                if ( node != null )	 	
                    node.Accept( visitor );	 	
            }	 	
        }

        


        /// <summary>
        /// Returns the SvnItem resources associated with the selected items
        /// in the solution explorer.
        /// </summary>
        /// <param name="getChildItems">Whether children of the items in 
        /// question should be included.</param>
        /// <param name="filter">A callback used to filter the items
        /// that are added.</param>
        /// <returns>A list of SvnItem instances.</returns>
        public IList GetSelectionResources( bool getChildItems, 
            ResourceFilterCallback filter )
        {
            ArrayList list = new ArrayList();

            object o = this.uiHierarchy.SelectedItems;	 	
            foreach( UIHierarchyItem item in (Array)this.uiHierarchy.SelectedItems )	 	
            {	 	
                TreeNode node = this.GetNode( item );	 	
                if ( node != null )	 	
                    node.GetResources( list, getChildItems, filter );	 	
            }

            return list;
        }

        /// <summary>
        /// Returns all  the SvnItem resources from root
        /// </summary>
        /// <param name="filter">A callback used to filter the items
        /// that are added.</param>
        /// <returns>A list of SvnItem instances.</returns>
        public IList GetAllResources( ResourceFilterCallback filter )
        {
            ArrayList list = new ArrayList();

            TreeNode node = solutionNode; 	
            if ( node != null )	 	
                node.GetResources( list, true, filter );	 	

            return list;
        }

        /// <summary>
        /// Retrieves the resources associated with a project item.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="recursive"></param>
        /// <returns></returns>
        public IList GetItemResources( ProjectItem item, bool recursive )
        {
            ArrayList list = new ArrayList();

            TreeNode node = this.GetNode(item);
            if ( node != null )
                node.GetResources( list, recursive, null );

            return list;
        }

        public ProjectItem GetSelectedProjectItem()
        {
            Array array = (Array)this.uiHierarchy.SelectedItems;
            UIHierarchyItem uiItem = (UIHierarchyItem)array.GetValue(0);
            return uiItem.Object as ProjectItem;
        }

       

        internal TreeView TreeView
        {
            [System.Diagnostics.DebuggerStepThrough]
            get{ return this.treeview; }
        }

        internal IContext Context
        {
            get{ return this.context; }
        }

        internal _DTE DTE
        {
            [System.Diagnostics.DebuggerStepThrough]
            get{ return this.dte; }
        }

        /// <summary>
        /// Retrieves the window handle to the solution explorer treeview and uses it
        /// to replace it's imagelist with our own.
        /// </summary>
        internal void SetUpTreeview()
        {
            // dragons be here - modify with care
            Debug.WriteLine( "Setting up treeview", "Ankh" );
            Window solutionExplorerWindow = this.dte.Windows.Item(
                EnvDTE.Constants.vsWindowKindSolutionExplorer);

            // we need to make sure its not hidden and that it is dockable
            bool linkable = solutionExplorerWindow.Linkable;
            bool hidden = solutionExplorerWindow.AutoHides;
            bool isFloating = solutionExplorerWindow.IsFloating;

            // these two operations need to be done in an exact order, 
            // depending on whether it is initially hidden
            if ( hidden )
            {
                solutionExplorerWindow.AutoHides = false;
                solutionExplorerWindow.IsFloating = false;
                solutionExplorerWindow.Linkable = true;
            }
            else
            {
                solutionExplorerWindow.IsFloating = false;
                solutionExplorerWindow.Linkable = true;
                solutionExplorerWindow.AutoHides = false;
            }
            
            // find the solution explorer window
            // Get the caption of the solution explorer            
            string slnExplorerCaption = solutionExplorerWindow.Caption;
            Debug.WriteLine( "Caption of solution explorer window is " + slnExplorerCaption, 
                "Ankh" );
            //            string vsnetCaption = this.dte.MainWindow.C
            IntPtr vsnet = (IntPtr)this.dte.MainWindow.HWnd;//Win32.FindWindow( VSNETWINDOW, null );

            // first try finding it as a child of the main VS.NET window
            IntPtr slnExplorer = Win32.FindWindowEx( vsnet, IntPtr.Zero, GENERICPANE, 
                slnExplorerCaption );

            // not there? Try looking for a floating palette. These are toplevel windows for 
            // some reason
            if ( slnExplorer == IntPtr.Zero )
            {
                Debug.WriteLine( "Solution explorer not a child of VS.NET window. " + 
                    "Searching floating windows", "Ankh" );
                // we need to search for the caption of any of the potentially linked windows
                IntPtr floatingPalette = IntPtr.Zero;
                foreach( Window win in solutionExplorerWindow.LinkedWindowFrame.LinkedWindows )
                {
                    floatingPalette = Win32.FindWindow( VBFLOATINGPALETTE, 
                        win.Caption );
                    if ( floatingPalette != IntPtr.Zero )
                        break;
                }
                
                // the solution explorer should be a direct child of the palette
                slnExplorer = Win32.FindWindowEx( floatingPalette, IntPtr.Zero, GENERICPANE,
                    slnExplorerCaption );
            }

            IntPtr uiHierarchy = Win32.FindWindowEx( slnExplorer, IntPtr.Zero, 
                UIHIERARCHY, null );
            IntPtr treeHwnd = Win32.FindWindowEx( uiHierarchy, IntPtr.Zero, TREEVIEW, 
                null );         
 
            if ( treeHwnd == IntPtr.Zero )
                throw new ApplicationException( 
                    "Could not attach to solution explorer treeview" );

            this.treeview = new TreeView( treeHwnd );

            // reset back to the original hiding-state and dockable state            
            solutionExplorerWindow.Linkable = linkable;
            solutionExplorerWindow.IsFloating = isFloating;
            if ( solutionExplorerWindow.Linkable )
                solutionExplorerWindow.AutoHides = hidden;

            // load the status images image strip
            Debug.WriteLine( "Loading status images", "Ankh" );
            Bitmap statusImages = (Bitmap)Image.FromStream( 
                this.GetType().Assembly.GetManifestResourceStream( STATUS_IMAGES ) );

            statusImages.MakeTransparent( statusImages.GetPixel(0,0) );

            this.statusImageList = new ImageList();
            this.statusImageList.ImageSize = new Size(7, 16);
            this.statusImageList.Images.AddStrip( statusImages );    
        
        }

        

        /// <summary>
        /// Adds a new resource to the tree.
        /// </summary>
        internal void AddResource( ProjectItem key, TreeNode node )
        {
            this.projectItems[key] = node;
        }

        internal void AddResource( Project key, TreeNode node, string projectFile )
        {
            this.projects[key] = node;
            this.context.ProjectFileWatcher.AddFile( projectFile );
        }

        internal void SetSolution( TreeNode node )
        {
            // we assume theres only one of these
            this.solutionNode = node;
        }

        private void GenerateStatusCache( string solutionPath )
        {
            DebugTimer t = DebugTimer.Start();
            string solutionDir = Path.GetDirectoryName( solutionPath );
           
            Debug.WriteLine( "Getting status cache", "Ankh" );
            
            this.context.StatusCache.Status( solutionDir );

            t.End( "Got status cache", "Ankh" );
        }

        

        

        private TreeNode GetNode(UIHierarchyItem item)
        {
            if ( item.Object == null )
                return null;

            if ( item.Object is ProjectItem && this.projectItems.Contains( item.Object ) )
                return ((TreeNode)this.projectItems[item.Object]);
            else if ( item.Object is Project && this.projects.Contains(item.Object) )
                return ((TreeNode)this.projects[item.Object]); 
            else if ( item == this.uiHierarchy.UIHierarchyItems.Item(1) )
                return this.solutionNode;
            else
                return null;
        }
        
        private TreeNode GetNode( ProjectItem item )
        {
            if ( item == null )
                return null;

            if (this.projectItems.Contains( item ) )
                return ((TreeNode)this.projectItems[item]);
            else
                return null;
        }

        private TreeNode GetNode( Project project )
        {
            if ( project == null )
                return null;

            if ( this.projects.Contains( project ) )
                return ((TreeNode)this.projects[project]);
            else
                return null;
        }

        


        #region class ItemHashCodeProvider
        private class ItemHashCodeProvider : IHashCodeProvider
        {        
            public int GetHashCode(object obj)
            {
                string filename;
                try
                {
                    try
                    {
                        filename = ((ProjectItem)obj).get_FileNames(1);
                    }
                    catch( ArgumentException )
                    {
                        // hack for those project types which use a 0-based index
                        // (GRRRR)
                        filename = ((ProjectItem)obj).get_FileNames(0);
                    }
                    string str = ((ProjectItem)obj).ContainingProject.FullName + "|" + filename;
                    return str.GetHashCode();
                }
                catch( Exception )
                {
                    return obj.GetHashCode();
                }
            }
        }
        #endregion

        #region class ItemComparer
        private class ItemComparer : IComparer
        {        
            public int Compare(object x, object y)
            {
                try
                {
                    string str_a = ((ProjectItem)x).ContainingProject.FullName + "|" + GetFileName(x);
                    string str_b = ((ProjectItem)y).ContainingProject.FullName + "|" + GetFileName(y);
                    return str_a.CompareTo(str_b);
                }
                catch( Exception )
                {
                    return -1;
                }
            }

            private static string GetFileName( object obj )
            {
                string filename = null;
                try
                {
                    filename = ((ProjectItem)obj).get_FileNames(1);
                }
                catch( ArgumentException )
                {
                    // hack for those project types which use a 0-based index
                    // (GRRRR)
                    filename = ((ProjectItem)obj).get_FileNames(0);
                }

                return filename;
            }
        }
        #endregion
        #region class ProjectHashCodeProvider
        private class ProjectHashCodeProvider : IHashCodeProvider
        {        
            public int GetHashCode(object obj)
            {
                try
                {
                    return ((Project)obj).FullName.GetHashCode();
                }
                catch( Exception )
                {
                    return obj.GetHashCode();
                }
            }
        }
        #endregion

        #region class ProjectComparer
        private class ProjectComparer : IComparer
        {        
            public int Compare(object x, object y)
            {
                try
                {
                    return ((Project)x).FullName.CompareTo(
                        ((Project)y).FullName );
                }
                catch( Exception )
                {
                    return -1;
                }
            }
        }
        #endregion


        private _DTE dte;
        private UIHierarchy uiHierarchy;
        private const string VSNETWINDOW = "wndclass_desked_gsk";
        private const string GENERICPANE = "GenericPane";
        private const string UIHIERARCHY = "VsUIHierarchyBaseWin";
        private const string TREEVIEW = "SysTreeView32";
        private const string VBFLOATINGPALETTE = "VBFloatingPalette";
        private IDictionary projectItems;
        private IDictionary projects;
        private TreeNode solutionNode;
        private ImageList statusImageList;
        private IContext context;
        private TreeView treeview;
        private IntPtr originalImageList = IntPtr.Zero;

        private const string STATUS_IMAGES = "Ankh.status_icons.bmp";
    }
}
