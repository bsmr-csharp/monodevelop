// 
// ExceptionCaughtDialog.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using Mono.Debugging.Client;
using MonoDevelop.Core;
using Gtk;
using System.Threading;
using MonoDevelop.Components;
using MonoDevelop.Ide.TextEditing;
using MonoDevelop.Ide;

namespace MonoDevelop.Debugger
{
	public partial class ExceptionCaughtWidget : Gtk.Bin
	{
		Gtk.TreeStore stackStore;
		ExceptionInfo exception;
		bool destroyed;
		
		public ExceptionCaughtWidget (ExceptionInfo exception)
		{
			this.Build ();

			stackStore = new TreeStore (typeof(string), typeof(string), typeof(int), typeof(int));
			treeStack.Model = stackStore;
			var crt = new CellRendererText ();
			crt.WrapWidth = 200;
			crt.WrapMode = Pango.WrapMode.WordChar;
			treeStack.AppendColumn ("", crt, "markup", 0);
			treeStack.ShowExpanders = false;
			
			valueView.AllowExpanding = true;
			valueView.Frame = DebuggingService.CurrentFrame;
			this.exception = exception;
			
			exception.Changed += HandleExceptionChanged;
			treeStack.SizeAllocated += delegate(object o, SizeAllocatedArgs args) {
				if (crt.WrapWidth != args.Allocation.Width) {
					crt.WrapWidth = args.Allocation.Width;
					Fill ();
				}
			};
			
			Fill ();
			treeStack.RowActivated += HandleRowActivated;
		}

		void HandleRowActivated (object o, RowActivatedArgs args)
		{
			Gtk.TreeIter it;
			if (!stackStore.GetIter (out it, args.Path))
				return;
			string file = (string) stackStore.GetValue (it, 1);
			int line = (int) stackStore.GetValue (it, 2);
			if (!string.IsNullOrEmpty (file))
				IdeApp.Workbench.OpenDocument (file, line, 0);
		}

		void HandleExceptionChanged (object sender, EventArgs e)
		{
			Gtk.Application.Invoke (delegate {
				Fill ();
			});
		}
		
		void Fill ()
		{
			if (destroyed)
				return;
			
			stackStore.Clear ();
			valueView.ClearValues ();

			labelType.Markup = GettextCatalog.GetString ("<b>{0}</b> has been thrown", exception.Type);
			labelMessage.Text = string.IsNullOrEmpty (exception.Message) ?
			                    string.Empty : 
			                    exception.Message;
			
			ShowStackTrace (exception, false);
			
			if (!exception.IsEvaluating && exception.Instance != null) {
				valueView.AddValue (exception.Instance);
				valueView.ExpandRow (new TreePath ("0"), false);
			}
			if (exception.StackIsEvaluating) {
				stackStore.AppendValues ("Loading...", "", 0, 0);
			}
		}
		
		void ShowStackTrace (ExceptionInfo exc, bool showExceptionNode)
		{
			TreeIter it = TreeIter.Zero;
			if (showExceptionNode) {
				treeStack.ShowExpanders = true;
				string tn = exc.Type + ": " + exc.Message;
				it = stackStore.AppendValues (tn, null, 0, 0);
			}

			foreach (ExceptionStackFrame frame in exc.StackTrace) {
				string text = GLib.Markup.EscapeText (frame.DisplayText);
				if (!string.IsNullOrEmpty (frame.File)) {
					text += "\n<small>" + GLib.Markup.EscapeText (frame.File);
					if (frame.Line > 0) {
						text += ":" + frame.Line;
						if (frame.Column > 0)
							text += "," + frame.Column;
					}
					text += "</small>";
				}
				if (!it.Equals (TreeIter.Zero))
					stackStore.AppendValues (it, text, frame.File, frame.Line, frame.Column);
				else
					stackStore.AppendValues (text, frame.File, frame.Line, frame.Column);
			}
			
			ExceptionInfo inner = exc.InnerException;
			if (inner != null)
				ShowStackTrace (inner, true);
		}
		
		protected override void OnDestroyed ()
		{
			destroyed = true;
			exception.Changed -= HandleExceptionChanged;
			base.OnDestroyed ();
		}
	}

	class ExceptionCaughtDialog: Gtk.Dialog
	{
		ExceptionCaughtWidget widget;
		ExceptionInfo ex;
		ExceptionCaughtMessage msg;

		public ExceptionCaughtDialog (ExceptionInfo val, ExceptionCaughtMessage msg)
		{
			Title = GettextCatalog.GetString ("Exception Caught");
			ex = val;
			widget = new ExceptionCaughtWidget (val);
			this.msg = msg;

			VBox box = new VBox ();
			box.Spacing = 6;
			box.PackStart (widget, true, true, 0);
			HButtonBox buttonBox = new HButtonBox ();
			buttonBox.BorderWidth = 6;

			var copy = new Gtk.Button (GettextCatalog.GetString ("Copy to Clipboard"));
			buttonBox.PackStart (copy, false, false, 0);
			copy.Clicked += HandleCopyClicked;

			var close = new Gtk.Button (GettextCatalog.GetString ("Close"));
			buttonBox.PackStart (close, false, false, 0);
			close.Clicked += (sender, e) => msg.Close ();

			box.PackStart (buttonBox, false, false, 0);
			VBox.Add (box);

			DefaultWidth = 500;
			DefaultHeight = 350;

			box.ShowAll ();
			ActionArea.Hide ();
		}

		protected override bool OnDeleteEvent (Gdk.Event evnt)
		{
			msg.Close ();
			return true;
		}

		void HandleCopyClicked (object sender, EventArgs e)
		{
			var text = ex.ToString ();
			var clipboard = Clipboard.Get (Gdk.Atom.Intern ("CLIPBOARD", false));
			clipboard.Text = text;
			clipboard = Clipboard.Get (Gdk.Atom.Intern ("PRIMARY", false));
			clipboard.Text = text;
		}
	}

	class ExceptionCaughtMessage
	{
		ExceptionInfo ex;
		FilePath file;
		int line;
		ExceptionCaughtDialog dialog;
		ExceptionCaughtButton button;

		public ExceptionCaughtMessage (ExceptionInfo val, FilePath file, int line, int col)
		{
			ex = val;
			this.file = file;
			this.line = line;
		}

		public void ShowDialog ()
		{
			if (dialog == null) {
				dialog = new ExceptionCaughtDialog (ex, this);
				MessageService.ShowCustomDialog (dialog, IdeApp.Workbench.RootWindow);
			}
		}

		public void ShowButton ()
		{
			if (dialog != null) {
				dialog.Destroy ();
				dialog = null;
			}
			if (button == null) {
				button = new ExceptionCaughtButton (ex, this);
				button.File = file;
				button.Line = line;
				TextEditorService.RegisterExtension (button);
			}
		}

		public void Dispose ()
		{
			if (dialog != null) {
				dialog.Destroy ();
				dialog = null;
			}
			if (button != null) {
				button.Dispose ();
				button = null;
			}
			if (Closed != null)
				Closed (this, EventArgs.Empty);
		}

		public void Close ()
		{
			ShowButton ();
		}

		public event EventHandler Closed;
	}

	class ExceptionCaughtButton: TopLevelWidgetExtension
	{
		ExceptionCaughtMessage dlg;
		ExceptionInfo exception;
		Gtk.Label messageLabel;

		public ExceptionCaughtButton (ExceptionInfo val, ExceptionCaughtMessage dlg)
		{
			this.exception = val;
			this.dlg = dlg;
			OffsetX = 6;
		}

		protected override void OnLineDeleted ()
		{
			dlg.Dispose ();
		}

		public override Widget CreateWidget ()
		{
			var icon = Gdk.Pixbuf.LoadFromResource ("lightning.png");
			var image = new Gtk.Image (icon);
			var button = new EventBox ();

			HBox box = new HBox (false, 6);
			VBox vb = new VBox ();
			vb.PackStart (image, false, false, 0);
			box.PackStart (vb);
			vb = new VBox (false, 6);
			vb.PackStart (new Gtk.Label () {
				Markup = GettextCatalog.GetString ("<b>{0}</b> has been thrown", exception.Type),
				Xalign = 0
			});
			messageLabel = new Gtk.Label () {
				Xalign = 0,
				NoShowAll = true
			};
			vb.PackStart (messageLabel);

			var detailsBtn = new Gtk.Button (GettextCatalog.GetString ("Show Details"));
			HBox hh = new HBox ();
			detailsBtn.Clicked += (o,e) => dlg.ShowDialog ();
			hh.PackStart (detailsBtn, false, false, 0);
			vb.PackStart (hh, false, false, 0);

			box.PackStart (vb);

			exception.Changed += delegate {
				Application.Invoke (delegate {
					LoadData ();
				});
			};
			LoadData ();

			button.VisibleWindow = false;
			button.Add (box);

			PopoverWidget eb = new PopoverWidget ();
			eb.ShowArrow = true;
			eb.EnableAnimation = true;
			eb.PopupPosition = PopupPosition.Left;
			eb.ContentBox.Add (button);
			eb.ShowAll ();
			return eb;
		}

		void LoadData ()
		{
			if (!string.IsNullOrEmpty (exception.Message)) {
				messageLabel.Show ();
				messageLabel.Text = exception.Message;
				if (messageLabel.SizeRequest ().Width > 400) {
					messageLabel.WidthRequest = 400;
					messageLabel.Wrap = true;
				}
			} else {
				messageLabel.Hide ();
			}
		}
	}
}

