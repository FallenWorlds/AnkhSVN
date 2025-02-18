﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

using Ankh.UI;
using Ankh.VS;
using Ankh.ExtensionPoints.UI;

namespace Ankh.WpfPackage.Services
{
	[GlobalService(typeof(IWinFormsThemingService), MinVersion = VSInstance.VS2012)]
	sealed partial class ThemingService : AnkhService, IWinFormsThemingService
	{
		public ThemingService(IAnkhServiceProvider context)
			: base(context)
		{

		}

		protected override void OnInitialize()
		{
			base.OnInitialize();
		}


		public Color GetThemedColorValue(ref Guid colorCategory, string colorName, bool foreground)
		{
			Type vsUIShell5 = typeof(IVsUIShell5);

			if (vsUIShell5 == null)
				throw new InvalidOperationException();

			IVsUIShell5 vs5 = GetService<IVsUIShell5>(typeof(SVsUIShell));
			MethodInfo method = vsUIShell5.GetMethod("GetThemedColor");

			uint clr = vs5.GetThemedColor(ref colorCategory, colorName, foreground ? (uint)1 : 0);
			// TODO: Use bitshifting

			byte[] colorComponents = BitConverter.GetBytes(clr);
			return System.Drawing.Color.FromArgb(colorComponents[3], colorComponents[0], colorComponents[1], colorComponents[2]);
		}

		bool VSThemeWindow(IntPtr hwnd, bool forDialog)
		{
			var ui6 = GetService<IVsUIShell6>(typeof(SVsUIShell));


			if (ui6 != null)
			{
				bool r = ui6.ThemeWindow(hwnd);
				if (r && forDialog)
					ui6.SetFixedThemeColors(hwnd);
				return r;
			}
			else
				return false;
		}

		delegate IVsUIObject GetIconForFile(string filename, __VSUIDATAFORMAT desiredFormat);
		GetIconForFile _giff;

		delegate IVsUIObject GetIconForFileEx(string filename, __VSUIDATAFORMAT desiredFormat, out uint iconSource);
		GetIconForFileEx _giffEx;

		public bool TryGetIcon(string path, out IntPtr hIcon)
		{
			hIcon = IntPtr.Zero;

			if (_giff == null)
			{
#pragma warning disable 0618    // needed for 2010
				var imgs = GetService<IVsImageService>(typeof(SVsImageService));

				if (imgs != null)
				{
					_giff = imgs.GetIconForFile;
					_giffEx = imgs.GetIconForFileEx;
				}
#pragma warning restore 0618
			}
			try
			{
				IVsUIObject uiOb;
				uint src = 0;

				if (_giffEx != null)
					uiOb = _giffEx(path, __VSUIDATAFORMAT.VSDF_WIN32, out src);
				else
					uiOb = _giff(path, __VSUIDATAFORMAT.VSDF_WIN32);

				if (src == 2 || uiOb == null)
					return false; // Just use the os directly. (Allows caching)

				object data;
				if (!VSErr.Succeeded(uiOb.get_Data(out data)))
					return false;

				IVsUIWin32Icon vsIcon = data as IVsUIWin32Icon;

				if (vsIcon == null)
					return false;

				if (!VSErr.Succeeded(vsIcon.GetHICON(out var iconHandle)))
					return false;

				hIcon = (IntPtr)iconHandle;

				return (hIcon != IntPtr.Zero);
			}
			catch { }

			return false;
		}

		public void ThemeRecursive(System.Windows.Forms.Control control, bool forDialog)
		{
			bool recurse = true;
			bool autoTheme = true;
			ISupportsVSTheming themeControl = control as ISupportsVSTheming;
			if (themeControl != null)
			{
				CancelEventArgs ca = new CancelEventArgs(false);
				if (themeControl != null)
					themeControl.OnThemeChange(this, ca);

				if (ca.Cancel)
					recurse = autoTheme = false; // No recurse!
			}

			IThemedControl themed = control as IThemedControl;
			if (themed != null)
			{
				ApplyThemeEventArgs atea = new ApplyThemeEventArgs(this, forDialog);

				themed.OnApplyTheme(atea);

				recurse = !atea.NoRecurse;
				autoTheme = !atea.DontTheme;
				forDialog = atea.ForDialog;
			}

			if (autoTheme && control.IsHandleCreated)
			{
				VSThemeWindow(control, forDialog);
			}

			if (recurse)
				foreach (Control c in control.Controls)
				{
					ThemeRecursive(c, forDialog);
				}
		}

		bool MaybeTheme<T>(Action<T> how, Control control, bool forDialog) where T : class
		{
			T value = control as T;
			if (value != null)
			{
				how(value);
				return true;
			}
			return false;
		}

		bool MaybeTheme<T>(Action<T, bool> how, Control control, bool forDialog) where T : class
		{
			T value = control as T;
			if (value != null)
			{
				how(value, forDialog);
				return true;
			}
			return false;
		}

		IAnkhVSColor _colorSvc;
		public IAnkhVSColor ColorSvc
		{
			get { return _colorSvc ?? (_colorSvc = GetService<IAnkhVSColor>()); }
		}

		IUIService _uiService;
		public IUIService UIService
		{
			get { return _uiService ?? (_uiService = GetService<IUIService>()); }
		}

		Font _dialogFont;
		public Font DialogFont
		{
			get { return _dialogFont ?? (_dialogFont = (Font)UIService.Styles["DialogFont"]); }
		}

		void ThemeOne(Label label)
		{
			if (label.Font != DialogFont)
				label.Font = DialogFont;

			if (label.BackColor != label.Parent.BackColor)
				label.BackColor = label.Parent.BackColor;

			if (label.ForeColor != label.Parent.ForeColor)
				label.ForeColor = label.Parent.ForeColor;

			LinkLabel ll = label as LinkLabel;
			if (ll != null)
			{
				Color clrLink;

				if (VSColors.TryGetColor((__VSSYSCOLOREX)__VSSYSCOLOREX3.VSCOLOR_STARTPAGE_TEXT_CONTROL_LINK_SELECTED, out clrLink))
					ll.LinkColor = clrLink;
			}

			if (label.BorderStyle == BorderStyle.Fixed3D)
				label.BorderStyle = BorderStyle.FixedSingle;
		}

		void ThemeOne(TextBox textBox)
		{
			if (textBox.Font != DialogFont)
				textBox.Font = DialogFont;

			Color backColor;
			if (!textBox.ReadOnly
				|| !ColorSvc.TryGetColor((__VSSYSCOLOREX)__VSSYSCOLOREX3.VSCOLOR_COMBOBOX_BACKGROUND, out backColor))
			{
				backColor = textBox.Parent.BackColor;
			}

			if (textBox.BackColor != backColor)
				textBox.BackColor = backColor;

			if (textBox.ForeColor != textBox.Parent.ForeColor)
				textBox.ForeColor = textBox.Parent.ForeColor;

			if (textBox.BorderStyle == BorderStyle.Fixed3D)
				textBox.BorderStyle = BorderStyle.FixedSingle;
		}

		void ThemeOne(ListView listView, bool forDialog)
		{
			VSThemeWindow(listView.Handle, forDialog);

			if (listView.Font != DialogFont)
				listView.Font = DialogFont;

			Color oldBack = listView.BackColor;
			Color oldFore = listView.ForeColor;
			Color newBack = listView.Parent.BackColor;
			Color newFore = listView.Parent.ForeColor;
			bool updateBack= false, updateFore = false;

			if (oldBack != newBack)
			{
				listView.BackColor = newBack;
				updateBack = true;
			}

			if (oldFore != newFore)
			{
				listView.ForeColor = newFore;
				updateFore = true;
			}

			// In some cases we can iterate over third party components here,
			// so make sure we don't fail because we try to iterate a virtual
			// listview
			if ((updateBack || updateFore) && !listView.VirtualMode)
			{
				foreach(ListViewItem lvi in listView.Items)
				{
					if (updateFore && lvi.ForeColor == oldFore)
						lvi.ForeColor = newFore;

					if (updateBack && lvi.BackColor == oldBack)
						lvi.BackColor = newBack;
				}
			}


			if (listView.BorderStyle == BorderStyle.Fixed3D)
				listView.BorderStyle = BorderStyle.FixedSingle;

			IntPtr header = NativeMethods.SendMessage(listView.Handle, NativeMethods.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);

			if (header != IntPtr.Zero)
			{
				VSThemeWindow(header, forDialog);

				// TODO: Force colors?
			}
		}

		void ThemeOne(TreeView treeView, bool forDialog)
		{
			VSThemeWindow(treeView.Handle, forDialog);

			if (treeView.Font != DialogFont)
				treeView.Font = DialogFont;

			if (treeView.BackColor != treeView.Parent.BackColor)
				treeView.BackColor = treeView.Parent.BackColor;

			if (treeView.ForeColor != treeView.Parent.ForeColor)
				treeView.ForeColor = treeView.Parent.ForeColor;

			if (treeView.BorderStyle == BorderStyle.Fixed3D)
				treeView.BorderStyle = BorderStyle.FixedSingle;
		}

		void ThemeOne(UserControl userControl)
		{
			if (userControl.Parent != null && userControl.Font != userControl.Parent.Font)
				userControl.Font = userControl.Parent.Font;

			Color color;
			if (ColorSvc.TryGetColor(__VSSYSCOLOREX.VSCOLOR_TOOLWINDOW_BACKGROUND, out color))
			{
				if (userControl.BackColor != color)
					userControl.BackColor = color;
			}

			if (ColorSvc.TryGetColor(__VSSYSCOLOREX.VSCOLOR_TOOLWINDOW_TEXT, out color))
			{
				if (userControl.ForeColor != color)
					userControl.ForeColor = color;
			}

			if (userControl.BorderStyle == BorderStyle.Fixed3D)
				userControl.BorderStyle = BorderStyle.FixedSingle;
		}

		private void ThemeOne(ContainerControl container)
		{
			if (container.Parent != null)
			{
			}
			else
				ThemeForm(container);
		}

		private void ThemeForm(ContainerControl form)
		{
			if (form.Parent != null && form.Font != form.Parent.Font)
				form.Font = form.Parent.Font;

			Color color;
			if (ColorSvc.TryGetColor(__VSSYSCOLOREX.VSCOLOR_TOOLWINDOW_BACKGROUND, out color))
			{
				if (form.BackColor != color)
					form.BackColor = color;
			}

			if (ColorSvc.TryGetColor(__VSSYSCOLOREX.VSCOLOR_TOOLWINDOW_TEXT, out color))
			{
				if (form.ForeColor != color)
					form.ForeColor = color;
			}
		}

		private void ThemeOne(ScrollableControl one)
		{

		}


		void ThemeOne(Panel panel)
		{
			if (panel.Parent != null && panel.Font != panel.Parent.Font)
				panel.Font = panel.Parent.Font;

			Color color;
			if (ColorSvc.TryGetColor(__VSSYSCOLOREX.VSCOLOR_TOOLWINDOW_BACKGROUND, out color))
			{
				if (panel.BackColor != color)
					panel.BackColor = color;
			}

			if (ColorSvc.TryGetColor(__VSSYSCOLOREX.VSCOLOR_TOOLWINDOW_TEXT, out color))
			{
				if (panel.ForeColor != color)
					panel.ForeColor = color;
			}

			if (panel.BorderStyle == BorderStyle.Fixed3D)
				panel.BorderStyle = BorderStyle.FixedSingle;
		}

		void ThemeOne(ToolStrip toolBar)
		{
			if (toolBar.Font != toolBar.Parent.Font)
				toolBar.Font = toolBar.Parent.Font;

			ToolStripRenderer renderer = UIService.Styles["VsRenderer"] as ToolStripRenderer;

			if (renderer != null)
				toolBar.Renderer = renderer;
		}

		private void ThemeOne(Button button)
		{
			if (button.Parent != null && button.Font != button.Parent.Font)
				button.Font = button.Parent.Font;

			if (button.BackColor != SystemColors.ButtonFace)
				button.BackColor = SystemColors.ButtonFace;

			if (button.ForeColor != SystemColors.WindowText)
				button.ForeColor = SystemColors.WindowText;
		}

		const __VSSYSCOLOREX VSCOLOR_BRANDEDUI_TITLE = (__VSSYSCOLOREX)__VSSYSCOLOREX2.VSCOLOR_BRANDEDUI_TITLE;
		const __VSSYSCOLOREX VSCOLOR_BRANDEDUI_BORDER = (__VSSYSCOLOREX)__VSSYSCOLOREX2.VSCOLOR_BRANDEDUI_BORDER;
		const __VSSYSCOLOREX VSCOLOR_BRANDEDUI_TEXT = (__VSSYSCOLOREX)__VSSYSCOLOREX2.VSCOLOR_BRANDEDUI_TEXT;
		const __VSSYSCOLOREX VSCOLOR_BRANDEDUI_BACKGROUND = (__VSSYSCOLOREX)__VSSYSCOLOREX2.VSCOLOR_BRANDEDUI_BACKGROUND;
		const __VSSYSCOLOREX VSCOLOR_BRANDEDUI_FILL = (__VSSYSCOLOREX)__VSSYSCOLOREX2.VSCOLOR_BRANDEDUI_FILL;
		const __VSSYSCOLOREX VSCOLOR_GRAYTEXT = (__VSSYSCOLOREX)__VSSYSCOLOREX3.VSCOLOR_GRAYTEXT;
		const __VSSYSCOLOREX VSCOLOR_COMMANDBAR_TOOLBAR_SEPARATOR = (__VSSYSCOLOREX)__VSSYSCOLOREX3.VSCOLOR_COMMANDBAR_TOOLBAR_SEPARATOR;
		const __VSSYSCOLOREX VSCOLOR_THREEDFACE = (__VSSYSCOLOREX)__VSSYSCOLOREX3.VSCOLOR_THREEDFACE;

		IAnkhVSColor _vsColors;
		IAnkhVSColor VSColors
		{
			get { return _vsColors ?? (_vsColors = GetService<IAnkhVSColor>()); }
		}

		void ThemeOne(PropertyGrid grid)
		{
			Color clrTitle, clrBorder, clrText, clrBackground, clrFill, clrGrayText;

			if (!VSColors.TryGetColor(VSCOLOR_BRANDEDUI_TITLE, out clrTitle))
				clrTitle = SystemColors.WindowText;
			if (!VSColors.TryGetColor(VSCOLOR_BRANDEDUI_BORDER, out clrBorder))
				clrBorder = SystemColors.WindowFrame;
			if (!VSColors.TryGetColor(__VSSYSCOLOREX.VSCOLOR_TOOLWINDOW_TEXT, out clrText))
				clrText = SystemColors.WindowText;
			if (!VSColors.TryGetColor(VSCOLOR_BRANDEDUI_BACKGROUND, out clrBackground))
				clrBackground = SystemColors.InactiveBorder;
			if (!VSColors.TryGetColor(__VSSYSCOLOREX.VSCOLOR_TOOLWINDOW_BACKGROUND, out clrFill))
				clrFill = SystemColors.Control;
			if (!VSColors.TryGetColor(VSCOLOR_GRAYTEXT, out clrGrayText))
				clrGrayText = SystemColors.WindowText;

			grid.BackColor = clrFill;

			grid.HelpBackColor = clrFill;
			grid.ViewBackColor = clrFill;

			grid.ViewForeColor = clrText;
			grid.HelpForeColor = clrText;
			grid.LineColor = clrBackground;
			grid.CategoryForeColor = clrTitle;

			if (VSVersion.VS2012OrLater)
			{
				// New in 4.5 properties. Properly added for VS2012.
				SetProperty(grid, "HelpBorderColor", clrFill);
				SetProperty(grid, "ViewBorderColor", clrFill);
				SetProperty(grid, "DisabledItemForeColor", clrGrayText);
				SetProperty(grid, "CategorySplitterColor", clrBackground);

				// The OS glyphs don't work in the dark theme. VS uses the same trick. (Unavailable in 4.0)
				SetProperty(grid, "CanShowVisualStyleGlyphs", false);
			}
		}

		void ThemeOne(ComboBox combo)
		{
			if (combo.Font != DialogFont)
				combo.Font = DialogFont;

			if (combo.BackColor != combo.Parent.BackColor)
				combo.BackColor = combo.Parent.BackColor;

			if (combo.ForeColor != combo.Parent.ForeColor)
				combo.ForeColor = combo.Parent.ForeColor;
		}

		void ThemeOne(SplitContainer panel)
		{
			IHasSplitterColor ex = panel as IHasSplitterColor;
			if (ex != null)
				ThemeOne(ex);

			if (panel.Parent != null && panel.Font != panel.Parent.Font)
				panel.Font = panel.Parent.Font;

			Color color;
			if (ColorSvc.TryGetColor(__VSSYSCOLOREX.VSCOLOR_TOOLWINDOW_BACKGROUND, out color))
			{
				if (panel.BackColor != color)
				{
					panel.BackColor = color;
					panel.Panel1.BackColor = color;
					panel.Panel2.BackColor = color;
				}
			}

			if (ColorSvc.TryGetColor(__VSSYSCOLOREX.VSCOLOR_TOOLWINDOW_TEXT, out color))
			{
				if (panel.ForeColor != color)
				{
					panel.ForeColor = color;
					panel.Panel1.ForeColor = color;
					panel.Panel2.ForeColor = color;
				}
			}

			if (panel.BorderStyle == BorderStyle.Fixed3D)
				panel.BorderStyle = BorderStyle.FixedSingle;
		}

		void ThemeOne(IHasSplitterColor splitter)
		{
			Color clrSplitter;

			if (!VSColors.TryGetColor(VSCOLOR_THREEDFACE, out clrSplitter))
				clrSplitter = SystemColors.InactiveBorder;

			splitter.SplitterColor = clrSplitter;
		}

		private void SetProperty(PropertyGrid grid, string propertyName, object value)
		{
			PropertyInfo pi = typeof(PropertyGrid).GetProperty(propertyName);

			Debug.Assert(pi != null, "Grid Property exists");
			if (pi != null)
				pi.SetValue(grid, value, null);
		}

		static class NativeMethods
		{
			public const Int32 LVM_GETHEADER = 0x1000 + 31; // LVM_FIRST + 31

			[DllImport("user32.dll")]
			public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
		}

		void VSThemeWindow(Control control, bool forDialog)
		{
			bool ok =
				MaybeTheme<ToolStrip>(ThemeOne, control, forDialog)
				|| MaybeTheme<Label>(ThemeOne, control, forDialog)
				|| MaybeTheme<TextBox>(ThemeOne, control, forDialog)
				|| MaybeTheme<ListView>(ThemeOne, control, forDialog)
				|| MaybeTheme<TreeView>(ThemeOne, control, forDialog)
				|| MaybeTheme<Panel>(ThemeOne, control, forDialog)
				|| MaybeTheme<UserControl>(ThemeOne, control, forDialog)
				|| MaybeTheme<PropertyGrid>(ThemeOne, control, forDialog)
				|| MaybeTheme<ComboBox>(ThemeOne, control, forDialog)
				|| MaybeTheme<SplitContainer>(ThemeOne, control, forDialog)
				|| MaybeTheme<IHasSplitterColor>(ThemeOne, control, forDialog)
				|| MaybeTheme<Button>(ThemeOne, control, forDialog)
				|| MaybeTheme<ContainerControl>(ThemeOne, control, forDialog)
				|| MaybeTheme<ScrollableControl>(ThemeOne, control, forDialog);
		}
	}
}
