// PythonEditorIndentation.cs
//
// Copyright (c) 2008 Christian Hergert <chris@dronelabs.com>
//               2012 Carlos Alberto Cortez <calberto.cortez@gmail.com>
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
using System.Collections.Generic;
using System.IO;

using MonoDevelop.Core;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Gui.Content;
using MonoDevelop.Projects;

namespace MonoDevelop.IronPython.Formatting
{
	public class PythonEditorIndentation : TextEditorExtension
	{
		const string StdPythonIndentation = "    "; // 4 spaces
		
		public override bool KeyPress (Gdk.Key key, char keyChar, Gdk.ModifierType modifier)
		{
			if (Editor.IsSomethingSelected)
				return base.KeyPress (key, keyChar, modifier);
			
			if (key == Gdk.Key.Return || key == Gdk.Key.KP_Enter) {
				string line = GetCurrentLine ();
				string trimmed = line.Trim ();
				bool indent = false;
				bool removeIndent = false;
				
				if ((modifier & Gdk.ModifierType.ControlMask) != 0)
				{
					Editor.Insert (Editor.Caret.Offset, "\n");

					string endTrim = line.TrimEnd ();
					if (!String.IsNullOrEmpty (endTrim))
					{
						int i = 0;
						while (Char.IsWhiteSpace (endTrim[i]))
							i++;

						if (i > 4)
							i -= 4;

						for (int j = 0; j < i; j++)
							Editor.Insert (Editor.Caret.Offset, " ");
						
						return false;
					}
					else if (line.Length > 4)
					{
						// get the last line, remove 4 chars from it if we can
						Editor.Insert (Editor.Caret.Offset, line.Substring (0, line.Length - 4));
						return false;
					}
				}
				
				if (trimmed.EndsWith (":"))
				{
					// Only indent if we are at the very end.
					if (Editor.Caret.Column - 1 >= line.Length)
						indent = true;
				}
				else if (trimmed.StartsWith ("if ") ||
				         trimmed.StartsWith ("def "))
				{
					int openCount = line.Split ('(').Length;
					int closeCount = line.Split (')').Length;

					if (openCount > closeCount)
						indent = true;
				} else if (trimmed.StartsWith ("return") ||
						trimmed.Equals ("pass"))
					removeIndent = true;

				if (indent)
				{
					bool retval = base.KeyPress (key, keyChar, modifier);
					InsertIndentation ();
					return retval;
				} else if (removeIndent) {
					bool retval = base.KeyPress (key, keyChar, modifier);
					RemoveIndentation ();
					return retval;
				}
			}
			
			if (key == Gdk.Key.Tab) {
				string line = GetCurrentLine ();
				
				if (line.Trim ().Length == 0) {
					InsertIndentation ();
					return false;
				}
			}
			
			if (key == Gdk.Key.BackSpace) {
				if (RemoveIndentation ())
					return false;
			}
			
			return base.KeyPress (key, keyChar, modifier);
		}
		
		string GetCurrentLine ()
		{
			return Editor.GetLineText (Editor.Caret.Line);
		}
		
		void InsertIndentation ()
		{
			Editor.Insert (Editor.Caret.Offset, StdPythonIndentation);
			Editor.Caret.Offset += StdPythonIndentation.Length;
			Editor.Document.CommitLineUpdate (Editor.Caret.Line);
		}
		
		bool RemoveIndentation ()
		{
			int indentLength = StdPythonIndentation.Length;
			int column = Editor.Caret.Column - 1; // column is 1-based
			
			if (column < indentLength)
				return false;
			
			string line = GetCurrentLine ();
			if (String.Compare (line, line.Length - indentLength, 
					StdPythonIndentation, 0, indentLength) != 0)
				return false;
			
			int prevOffset = Editor.Caret.Offset;
			Editor.Caret.Offset -= indentLength;
			Editor.Remove (prevOffset - indentLength, indentLength);
			Editor.Document.CommitLineUpdate (Editor.Caret.Line);
			
			return true;
		}
	}
}
