// 
// PythonParser.cs
//  
// Author:
//       Carlos Alberto Cortez <calberto.cortez@gmail.com>
// 
// Copyright (c) 2012 Carlos Alberto Cortez
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

using MonoDevelop.IronPython.Parser.Dom;
using MonoDevelop.IronPython.Resolver;

using IronPython;
using IronPython.Hosting;
using IronPython.Compiler;
using IronPython.Compiler.Ast;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting.Runtime;
using ICSharpCode.NRefactory.Documentation;

// Workaround for a *quite* annoying mcs bug that happens
// trying to use types under IronPython.Compiler (IronPython.dll),
// complaining we are missing a reference (????), but compiles fine under VS
using IronPythonParserEngine = IronPython.Compiler.Parser;
using IronPythonAst = IronPython.Compiler.Ast.PythonAst;

namespace MonoDevelop.IronPython.Parser
{
	public class PythonParser
	{
		ScriptEngine pythonEngine;
		CompilerOptions compilerOptions;
		PythonOptions langOptions;
		
		CustomPythonWalker walker;
		
		public PythonParser ()
		{
			pythonEngine = Python.CreateEngine ();
			
			var langContext = HostingHelpers.GetLanguageContext (pythonEngine);
			compilerOptions = langContext.GetCompilerOptions ();
			langOptions = (PythonOptions) langContext.Options;
			
			walker = new CustomPythonWalker ();
		}
		
		public PythonResolver CreateResolver (ProjectDom dom, object editor, string fileName)
		{
			return new PythonResolver (dom, fileName);
		}
		
		public PythonExpressionFinder CreateExpressionFinder (ProjectDom dom)
		{
			return new PythonExpressionFinder (dom);
		}
		
		public DocumentationComment Parse (ProjectDom dom, string fileName, string content)
		{
			var document = new DocumentationComment (fileName);
			var compilationUnit = new PythonCompilationUnit (fileName);
			document.CompilationUnit = compilationUnit;
			
			if (String.IsNullOrEmpty (content))
				return document;
			
			var scriptSource = pythonEngine.CreateScriptSourceFromString (content, SourceCodeKind.File);
			var context = new CompilerContext (HostingHelpers.GetSourceUnit (scriptSource),
				compilerOptions, ErrorSink.Default);
			var parser = IronPythonParserEngine.CreateParser (context, langOptions);
			
			IronPythonAst ast = null;
			try {
				ast = parser.ParseFile (false);
			} catch (SyntaxErrorException exc) {
				// We could likely improve the error message
				document.Errors.Add (new Error (exc.Line, exc.Column, exc.Message));
				return document;
			}
			
			walker.Reset ();
			ast.Walk (walker);
			
			compilationUnit.Module = walker.Module;
			compilationUnit.Build ();
			
			return document;
		}
	}
}

